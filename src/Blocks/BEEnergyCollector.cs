using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// Turns sunlight into EMC. Output scales with the sky light reaching the block above it.
    /// Stored EMC is pushed into any adjacent condenser every second, or (sneak + right-click with an empty hand)
    /// moved into the player's personal EMC for use at a transmutation table.
    /// Rates come from the block's attributes: <c>emcPerSecond</c>, <c>capacity</c>.
    /// <para>
    /// ProjectE's fuel slot (<see cref="FuelUpgrader"/>): right-click with coal or an alchemical fuel to put it in; the
    /// collector spends its EMC turning it into the next better fuel, up to the fuel set as its lock (sneak + right-click
    /// with that fuel) or the best there is. An empty hand takes the finished fuel out (then what's left to upgrade, then
    /// a Klein star).
    /// </para>
    /// </summary>
    public class BEEnergyCollector : BlockEntity, IEmcInteractable, IStarHolder
    {
        public double Stored;
        public StarHolder Star { get; } = new StarHolder();
        public FuelUpgrader Fuel { get; } = new FuelUpgrader();
        public double Capacity => capacity;
        /// <summary>EMC a second in full sunlight, before the server's machine speed; and what it made on its last tick.</summary>
        public double RatePerSecond => ratePerSecond;
        public double CurrentRate => currentRate;
        double currentRate;
        double ratePerSecond = 4;
        double capacity = 10000;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            JsonObject attrs = Block?.Attributes ?? api.World.BlockAccessor.GetBlock(Pos)?.Attributes;
            ratePerSecond = attrs?["emcPerSecond"].AsDouble(4) ?? 4;
            capacity = attrs?["capacity"].AsDouble(10000) ?? 10000;
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, 1000);
            }
        }

        VintageEModSystem Mod => VintageEModSystem.Of(Api);

        void OnServerTick(float dt)
        {
            VintageEConfig cfg = Mod.Config;
            int sun = Api.World.BlockAccessor.GetLightLevel(Pos.UpCopy(), EnumLightLevelType.TimeOfDaySunLight);
            int maxSun = Math.Max(1, Api.World.SunBrightness);
            double light = Math.Max(GameMath.Clamp(sun / (double)maxSun, 0, 1), cfg.CollectorDarkFactor);
            double rate = ratePerSecond * cfg.MachineSpeedMultiplier * light;

            double before = Stored;
            Stored = Math.Min(capacity, Stored + rate * dt);

            // A Klein star inside is charged first, at the collector's own rate (so it still feeds its neighbours).
            double charged = Star.Charge(Math.Min(Stored, Math.Max(1, ratePerSecond * cfg.MachineSpeedMultiplier) * dt));
            if (charged > 0) { Stored -= charged; before = -1; } // so the sync below runs

            // Then the fuel slot (ProjectE: upgrading fuel comes before feeding neighbours).
            double spent = Fuel.Upgrade(Mod, Api.World, Stored);
            if (spent > 0 || Fuel.Changed) { Stored -= spent; before = -1; Fuel.Changed = false; }

            // Feed neighbouring acceptors (condensers).
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (Stored <= 0) break;
                if (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face)) is IEmcAcceptor acceptor)
                {
                    Stored -= acceptor.AcceptEmc(Stored);
                }
            }
            if (Stored < 0) Stored = 0;

            if (Math.Abs(Stored - before) > 1e-6 || Math.Abs(rate - currentRate) > 1e-6)
            {
                currentRate = rate;
                MarkDirty(false);
            }
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (Fuel.Interact(this, Mod, byPlayer)) return true;
            if (!byPlayer.Entity.Controls.Sneak) return Star.Interact(this, byPlayer);
            bool wantsWithdraw = hand?.Empty != false || hand.Itemstack.Collectible is IEmcStorageItem;
            if (!wantsWithdraw) return false;
            if (Api.Side == EnumAppSide.Client) return true;

            if (!Mod.Config.CollectorsChargePlayers) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;
            if (Stored < 1) return true;

            Stored -= EmcTransfer.ToPlayerOrStar(byPlayer, Math.Floor(Stored));
            MarkDirty(false);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 8, 0.4f);
            return true;
        }

        /// <summary>
        /// The Klein star inside comes out with the block however it goes. An explosion (a Nova Catalyst, a lens
        /// charge, the game's bombs) never runs the block's own OnBlockBroken, only this one, so until 1.21.1 a
        /// collector that was blown up took its star with it.
        /// </summary>
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                Star.Drop(Api.World, Pos);
                Fuel.Drop(Api.World, Pos);
            }
            base.OnBlockBroken(byPlayer);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("emc", Stored);
            tree.SetDouble("rate", currentRate);
            Star.ToTree(tree);
            Fuel.ToTree(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Stored = EmcTransfer.Clean(tree.GetDouble("emc"));
            currentRate = EmcTransfer.Clean(tree.GetDouble("rate"));
            Star.FromTree(tree, worldAccessForResolve);
            Fuel.FromTree(tree, worldAccessForResolve);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:collector-stored", EmcFormat.Format(Stored), EmcFormat.Format(capacity)));
            dsc.AppendLine(Lang.Get("vintagee:collector-rate", EmcFormat.Format(currentRate)));
            Star.Info(dsc);
            Fuel.Info(Api?.World, dsc);
        }
    }

    /// <summary>
    /// An Energy Collector's fuel slot, without a window (like the Klein star slot): fuel waiting to be upgraded, the
    /// finished fuel, and the lock (the fuel to stop at). Each fuel in <see cref="VintageEConfig.CollectorFuels"/> is
    /// worth more than the one before it; the collector pays the difference in EMC from its store to lift fuel straight
    /// to the lock (or the most valuable fuel when no lock is set), up to 64 items a second.
    /// </summary>
    public class FuelUpgrader
    {
        const string InKey = "vintageeFuelIn", OutKey = "vintageeFuelOut", LockKey = "vintageeFuelLock";
        public ItemStack Input, Output;
        /// <summary>Code of the fuel to stop at; null: the best fuel.</summary>
        public string Lock;
        /// <summary>Something moved without EMC being spent (the block needs saving).</summary>
        public bool Changed;

        /// <summary>The fuels in order of EMC value, cheapest first (the server's list, resolved and valued).</summary>
        public static List<CollectibleObject> Chain(VintageEModSystem mod, IWorldAccessor world)
        {
            var list = new List<CollectibleObject>();
            if (mod?.Config?.CollectorFuels == null || mod.Emc == null) return list;
            foreach (string code in mod.Config.CollectorFuels.Distinct())
            {
                if (string.IsNullOrWhiteSpace(code)) continue;
                var loc = new AssetLocation(code.Trim());
                CollectibleObject c = (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
                if (c == null || c.Id == 0 || list.Contains(c) || mod.Emc.GetValue(c) <= 0) continue;
                list.Add(c);
            }
            return list.OrderBy(c => mod.Emc.GetValue(c)).ToList();
        }

        /// <summary>Where fuel of this kind ends up: the lock if it is worth more, else the best fuel; null if it isn't a fuel on the list.</summary>
        public CollectibleObject TargetFor(VintageEModSystem mod, List<CollectibleObject> chain, CollectibleObject c)
        {
            int at = chain.IndexOf(c);
            if (at < 0) return null;
            int lockAt = Lock == null ? -1 : chain.FindIndex(x => x.Code.ToString() == Lock);
            int to = lockAt >= 0 ? lockAt : chain.Count - 1;
            return to > at ? chain[to] : c;
        }

        /// <summary>Upgrades what it can with up to <paramref name="available"/> EMC; returns what it spent.</summary>
        public double Upgrade(VintageEModSystem mod, IWorldAccessor world, double available)
        {
            if (Input == null || mod == null) return 0;
            List<CollectibleObject> chain = Chain(mod, world);
            CollectibleObject target = TargetFor(mod, chain, Input.Collectible);
            if (target == null) return 0; // no longer on the list: it waits to be taken out
            double cost = Math.Max(0, mod.Emc.GetValue(target) - mod.Emc.GetValue(Input.Collectible));
            double spent = 0;
            for (int n = 0; n < 64 && Input != null; n++)
            {
                if (Output != null && (Output.Collectible != target || Output.StackSize >= Output.Collectible.MaxStackSize)) break;
                if (available - spent < cost) break;
                spent += cost;
                Input.StackSize--;
                if (Input.StackSize <= 0) Input = null;
                if (Output == null) Output = new ItemStack(target, 1);
                else Output.StackSize++;
                if (cost <= 0) Changed = true;
            }
            return spent;
        }

        static bool IsFuel(VintageEModSystem mod, IWorldAccessor world, ItemStack stack) => stack != null && Chain(mod, world).Contains(stack.Collectible);

        /// <summary>
        /// Right-click: fuel in hand goes in (sneak: becomes the lock, or clears it if it already is); an empty hand takes
        /// the finished fuel, then the fuel still waiting. Returns true if it handled the click.
        /// </summary>
        public bool Interact(BlockEntity be, VintageEModSystem mod, IPlayer byPlayer)
        {
            ItemSlot hand = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            if (hand == null) return false;
            IWorldAccessor world = be.Api.World;
            bool sneak = byPlayer.Entity.Controls.Sneak;
            bool fuelInHand = IsFuel(mod, world, hand.Itemstack);
            bool takeOut = hand.Empty && !sneak && (Output != null || Input != null);
            bool putIn = fuelInHand && !sneak && (Input == null || Input.Collectible == hand.Itemstack.Collectible && Input.StackSize < Input.Collectible.MaxStackSize);
            bool setLock = fuelInHand && sneak;
            if (!takeOut && !putIn && !setLock) return false;
            if (be.Api.Side == EnumAppSide.Client) return true;
            if (!world.Claims.TryAccess(byPlayer, be.Pos, EnumBlockAccessFlags.Use)) return true;
            if (setLock)
            {
                string code = hand.Itemstack.Collectible.Code.ToString();
                Lock = Lock == code ? null : code;
            }
            else if (putIn)
            {
                int room = Input == null ? hand.Itemstack.StackSize : Input.Collectible.MaxStackSize - Input.StackSize;
                ItemStack moved = hand.TakeOut(Math.Min(room, hand.Itemstack.StackSize));
                if (Input == null) Input = moved; else Input.StackSize += moved.StackSize;
                hand.MarkDirty();
            }
            else
            {
                ItemStack give = Output ?? Input;
                if (Output != null) Output = null; else Input = null;
                if (!byPlayer.InventoryManager.TryGiveItemstack(give, true)) world.SpawnItemEntity(give, be.Pos.ToVec3d().Add(0.5, 1, 0.5));
            }
            world.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), be.Pos.X + 0.5, be.Pos.Y + 0.5, be.Pos.Z + 0.5, null, true, 8, 0.6f);
            be.MarkDirty(true);
            return true;
        }

        public void Drop(IWorldAccessor world, BlockPos pos)
        {
            if (Input != null) world.SpawnItemEntity(Input, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            if (Output != null) world.SpawnItemEntity(Output, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            Input = Output = null;
        }

        public void ToTree(ITreeAttribute tree)
        {
            if (Input != null) tree.SetItemstack(InKey, Input); else tree.RemoveAttribute(InKey);
            if (Output != null) tree.SetItemstack(OutKey, Output); else tree.RemoveAttribute(OutKey);
            if (Lock != null) tree.SetString(LockKey, Lock); else tree.RemoveAttribute(LockKey);
        }

        public void FromTree(ITreeAttribute tree, IWorldAccessor world)
        {
            Input = tree.GetItemstack(InKey);
            if (Input != null && !Input.ResolveBlockOrItem(world)) Input = null;
            Output = tree.GetItemstack(OutKey);
            if (Output != null && !Output.ResolveBlockOrItem(world)) Output = null;
            Lock = tree.GetString(LockKey);
        }

        public void Info(IWorldAccessor world, StringBuilder dsc)
        {
            if (Input != null) dsc.AppendLine(Lang.Get("vintagee:collector-fuel-in", Input.StackSize, Input.GetName()));
            if (Output != null) dsc.AppendLine(Lang.Get("vintagee:collector-fuel-out", Output.StackSize, Output.GetName()));
            if (Lock != null && world != null)
            {
                var loc = new AssetLocation(Lock);
                CollectibleObject c = (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
                dsc.AppendLine(Lang.Get("vintagee:collector-fuel-lock", c == null ? Lock : c.GetHeldItemName(new ItemStack(c))));
            }
        }
    }
}
