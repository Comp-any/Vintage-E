using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Anti-Matter Relay MK1–3: an EMC battery and booster between collectors and the machines they power.
    /// Accepts EMC from collectors (or other relays), adds a small bonus while it holds EMC, and pushes it on to
    /// adjacent condensers, furnaces and emptier relays. Sneak + right-click with an empty hand to take the EMC
    /// yourself, or with a Klein star to charge it. Attributes: <c>capacity</c>, <c>bonusPerSecond</c>, <c>transferPerSecond</c>.
    /// <para>
    /// ProjectE's burn slots: right-click with anything that has an EMC value (not a Klein star) to put it in; the relay
    /// burns it into its store, up to 20 items a second, while there's room. An empty hand takes back what's left.
    /// </para>
    /// </summary>
    public class BEAntimatterRelay : BlockEntity, IEmcAcceptor, IEmcInteractable, IStarHolder
    {
        public double Stored;
        public StarHolder Star { get; } = new StarHolder();
        /// <summary>What waits in the burn slot.</summary>
        public ItemStack Burn;
        public const int BurnPerSecond = 20;
        const string BurnKey = "vintageeBurn";
        double capacity = 100000, bonusPerSecond = 1, transferPerSecond = 64;
        public double Capacity => capacity;
        public double TransferPerSecond => transferPerSecond;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            capacity = Block?.Attributes?["capacity"].AsDouble(100000) ?? 100000;
            bonusPerSecond = Block?.Attributes?["bonusPerSecond"].AsDouble(1) ?? 1;
            transferPerSecond = Block?.Attributes?["transferPerSecond"].AsDouble(64) ?? 64;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 1000);
        }

        double Fill => capacity <= 0 ? 1 : Stored / capacity;

        public double AcceptEmc(double amount)
        {
            if (!EmcTransfer.Usable(amount)) return 0;
            double take = Math.Min(amount, Math.Max(0, capacity - Stored));
            Stored += take;
            if (take > 0) MarkDirty(false);
            return take;
        }

        void OnServerTick(float dt)
        {
            double before = Stored;
            double speed = VintageEModSystem.Of(Api)?.Config.MachineSpeedMultiplier ?? 1;
            if (Stored > 0) Stored = Math.Min(capacity, Stored + bonusPerSecond * speed * dt);
            if (BurnSome(BurnPerSecond) > 0) before = -1;
            double charged = Star.Charge(Math.Min(Stored, transferPerSecond * speed * dt)); // a Klein star inside is charged first, at the relay's rate
            if (charged > 0) { Stored -= charged; before = -1; } // so the sync below runs

            double budget = Math.Min(Stored, transferPerSecond * speed * dt);
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (budget <= 0) break;
                BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face));
                if (!(be is IEmcAcceptor acceptor) || be is BEEnergyCollector) continue;
                if (be is BEAntimatterRelay other && other.Fill >= Fill) continue; // flow downhill only
                double moved = acceptor.AcceptEmc(budget);
                budget -= moved;
                Stored -= moved;
            }
            if (Math.Abs(Stored - before) > 1e-6) MarkDirty(false);
        }

        /// <summary>Burns up to <paramref name="max"/> items from the burn slot into the store, while they fit. Returns how many.</summary>
        public int BurnSome(int max)
        {
            if (Burn == null) return 0;
            var mod = VintageEModSystem.Of(Api);
            double each = mod?.Emc == null || mod.Emc.IsNoTransmute(Burn.Collectible) ? 0 : mod.Emc.GetValue(Burn);
            if (!EmcTransfer.Usable(each)) return 0;
            int n = 0;
            while (Burn != null && n < max && Stored + each <= capacity)
            {
                Stored += each;
                Burn.StackSize--;
                if (Burn.StackSize <= 0) Burn = null;
                n++;
            }
            return n;
        }

        /// <summary>The burn slot's right-click: an item with an EMC value goes in, an empty hand takes it back. True if handled.</summary>
        bool BurnInteract(IPlayer byPlayer)
        {
            ItemSlot hand = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            if (hand == null || byPlayer.Entity.Controls.Sneak) return false;
            ItemStack held = hand.Itemstack;
            var mod = VintageEModSystem.Of(Api);
            bool burnable = held != null && !(held.Collectible is IEmcStorageItem) && !(held.Collectible is ItemEmcTrinket) && mod?.Emc != null
                && !mod.Emc.IsNoTransmute(held.Collectible) && mod.Emc.GetValue(held) > 0 && !StackContents.HasNestedStacks(held);
            bool putIn = burnable && (Burn == null || Burn.Equals(Api.World, held, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes) && Burn.StackSize < Burn.Collectible.MaxStackSize);
            bool takeOut = hand.Empty && Burn != null;
            if (!putIn && !takeOut) return false;
            if (Api.Side == EnumAppSide.Client) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;
            if (putIn)
            {
                int room = Burn == null ? held.StackSize : Burn.Collectible.MaxStackSize - Burn.StackSize;
                ItemStack moved = hand.TakeOut(Math.Min(room, held.StackSize));
                if (Burn == null) Burn = moved; else Burn.StackSize += moved.StackSize;
                hand.MarkDirty();
            }
            else
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(Burn, true)) Api.World.SpawnItemEntity(Burn, Pos.ToVec3d().Add(0.5, 1, 0.5));
                Burn = null;
            }
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 8, 0.6f);
            MarkDirty(true);
            return true;
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (BurnInteract(byPlayer)) return true; // the burn slot first: what's in it comes out before a Klein star
            if (!byPlayer.Entity.Controls.Sneak) return Star.Interact(this, byPlayer);
            if (Api.Side == EnumAppSide.Client) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use) || Stored < 1) return true;
            Stored -= EmcTransfer.ToPlayerOrStar(byPlayer, Math.Floor(Stored));
            MarkDirty(false);
            return true;
        }

        /// <summary>The Klein star inside comes out however the relay goes, an explosion included (see BEEnergyCollector.OnBlockBroken).</summary>
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                Star.Drop(Api.World, Pos);
                if (Burn != null) Api.World.SpawnItemEntity(Burn, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                Burn = null;
            }
            base.OnBlockBroken(byPlayer);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("emc", Stored);
            Star.ToTree(tree);
            if (Burn != null) tree.SetItemstack(BurnKey, Burn); else tree.RemoveAttribute(BurnKey);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Stored = EmcTransfer.Clean(tree.GetDouble("emc"));
            Star.FromTree(tree, worldAccessForResolve);
            Burn = tree.GetItemstack(BurnKey);
            if (Burn != null && !Burn.ResolveBlockOrItem(worldAccessForResolve)) Burn = null;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:collector-stored", EmcFormat.Format(Stored), EmcFormat.Format(capacity)));
            dsc.AppendLine(Lang.Get("vintagee:relay-bonus", EmcFormat.Format(bonusPerSecond), EmcFormat.Format(transferPerSecond)));
            Star.Info(dsc);
            if (Burn != null) dsc.AppendLine(Lang.Get("vintagee:relay-burn", Burn.StackSize, Burn.GetName()));
        }
    }

    /// <summary>Moving EMC out of machines: into a Klein star in the player's hand if there is one, otherwise their personal EMC.</summary>
    public static class EmcTransfer
    {
        /// <summary>An amount of EMC a machine can take: a real number above zero (not NaN, not infinite).</summary>
        public static bool Usable(double amount) => amount > 0 && !double.IsInfinity(amount);

        /// <summary>
        /// Stored EMC as read back from a save: NaN, an infinity or a negative number becomes 0. A NaN in a setting
        /// a collector multiplies by ends up in its store, from there in every relay, condenser and furnace it
        /// feeds, and is saved with them; this lets such machines work again once the setting is put right.
        /// </summary>
        public static double Clean(double stored) => double.IsNaN(stored) || double.IsInfinity(stored) || stored < 0 ? 0 : stored;

        /// <summary>Returns how much was moved.</summary>
        public static double ToPlayerOrStar(IPlayer player, double amount)
        {
            if (amount <= 0) return 0;
            ItemSlot held = player.InventoryManager?.ActiveHotbarSlot;
            if (held?.Itemstack?.Collectible is IEmcStorageItem star)
            {
                double stored = StoredEmc.Get(held.Itemstack);
                double move = Math.Min(amount, star.GetCapacity(held.Itemstack) - stored);
                if (move <= 0) return 0;
                StoredEmc.Set(held.Itemstack, stored + move);
                held.MarkDirty();
                return move;
            }
            if (held?.Empty == false) return 0;
            PlayerEmc.AddEmc(player, amount);
            return amount;
        }
    }

    /// <summary>Interdiction Torch: keeps hostile creatures away from it (radius from the <c>radius</c> attribute).</summary>
    public class BEInterdictionTorch : BlockEntity
    {
        float radius = 8;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            radius = Block?.Attributes?["radius"].AsFloat(8) ?? 8;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(_ =>
            {
                try
                {
                    TrinketEffects fx = VintageEModSystem.Of(Api)?.Trinkets;
                    Vec3d center = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
                    fx?.RepelAll(center, radius); // hostile creatures pushed away; arrows and the like turned too (players' own fly on)
                }
                catch (Exception e) { Api.Logger.Warning("Vintage-E interdiction torch at {0}: {1}", Pos, e.Message); }
            }, 250);
        }
    }

    /// <summary>
    /// Nova Catalyst / Nova Cataclysm: lit like the game's bombs, by holding right-click on it with a torch or a
    /// firestarter; a few seconds later it explodes, dropping what it breaks. Attributes: <c>radius</c>, <c>fuseSeconds</c>.
    /// </summary>
    public class BENovaExplosive : BlockEntity, Vintagestory.GameContent.IIgnitable
    {
        bool lit;
        double fuseLeft;
        string litBy;
        long listener;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server && lit) StartTicking();
        }

        void StartTicking()
        {
            if (listener == 0) listener = RegisterGameTickListener(OnTick, 100);
        }

        public bool IsLit => lit;

        // The game's torches and firestarters call these while you hold right-click on the block (as with its bombs).
        public Vintagestory.GameContent.EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
        {
            if (lit) return Vintagestory.GameContent.EnumIgniteState.NotIgnitablePreventDefault;
            return secondsIgniting > 0.75f ? Vintagestory.GameContent.EnumIgniteState.IgniteNow : Vintagestory.GameContent.EnumIgniteState.Ignitable;
        }

        public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
        {
            if (secondsIgniting < 0.7f) return;
            handling = EnumHandling.PreventDefault;
            if (Api.Side != EnumAppSide.Server) return;
            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
            if (byPlayer != null) Light(byPlayer);
        }

        public Vintagestory.GameContent.EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
            => Vintagestory.GameContent.EnumIgniteState.NotIgnitable;

        void Light(IPlayer byPlayer)
        {
            if (lit) return;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.BuildOrBreak)) return;
            lit = true;
            litBy = byPlayer.PlayerUID;
            fuseLeft = Block?.Attributes?["fuseSeconds"].AsDouble(4) ?? 4;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/fuse"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 24, 1f);
            StartTicking();
            MarkDirty(false);
        }

        void OnTick(float dt)
        {
            if (!lit) return;
            fuseLeft -= dt;
            Api.World.SpawnParticles(4, ColorUtil.ToRgba(255, 255, 160, 40), Pos.ToVec3d().Add(0.3, 1, 0.3), Pos.ToVec3d().Add(0.7, 1.2, 0.7),
                new Vec3f(-0.3f, 0.5f, -0.3f), new Vec3f(0.3f, 1.2f, 0.3f), 0.6f, -0.1f, 0.4f);
            if (fuseLeft > 0) return;
            lit = false;
            float radius = Block?.Attributes?["radius"].AsFloat(6) ?? 6;
            BlockPos at = Pos.Copy();
            Api.World.BlockAccessor.SetBlock(0, at);
            ((IServerWorldAccessor)Api.World).CreateExplosion(at, EnumBlastType.RockBlast, radius, radius + 2, 1f, litBy);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("lit", lit);
            tree.SetDouble("fuse", fuseLeft);
            if (litBy != null) tree.SetString("litBy", litBy);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            lit = tree.GetBool("lit");
            fuseLeft = tree.GetDouble("fuse");
            litBy = tree.GetString("litBy");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            if (lit) dsc.AppendLine(Lang.Get("vintagee:nova-lit", Math.Max(0, Math.Ceiling(fuseLeft))));
        }
    }

    /// <summary>
    /// Dark Matter Pedestal: right-click with a ring, stone, amulet, talisman or watch to set it on the pedestal (empty
    /// hand takes it back). It then works on the area around the pedestal for free, like ProjectE's: the Harvest Goddess
    /// Band grows and harvests crops (8 blocks), the Watch grows crops and slows hostile creatures (12), the Ignition
    /// Ring burns hostile creatures (8), the Zero Ring freezes water and puts out burning creatures (6), the Black Hole
    /// Band and Void Ring pull in loose items (10) and put them into containers next to the pedestal, Swiftwolf's
    /// Rending Gale calls lightning down on every creature but players and pets (10, every 3.5 seconds, as ProjectE),
    /// the Soul/Body/Life Stones heal and feed players (6), the Repair Talisman mends their gear (6), the Evertide
    /// Amulet brings rain and the Volcanite Amulet clear skies. Archangel's Smite fires three homing arrows into the
    /// air every 2 seconds while a hostile creature is near (10).
    /// </summary>
    public class BEPedestal : BlockEntity, IEmcInteractable
    {
        ItemStack held;
        float acc, swrgAcc, smiteAcc;

        /// <summary>ProjectE's pedestal cooldowns: Swiftwolf's lightning every 3.5 seconds, the smite's arrows every 2.</summary>
        public const float SwrgSeconds = 3.5f, SmiteSeconds = 2f;

        /// <summary>What a pedestal takes: a ring, stone, amulet, talisman or watch, or an Archangel's Smite.</summary>
        public static bool IsPedestalItem(ItemStack stack) => stack?.Collectible is ItemEmcTrinket || (stack?.Collectible as ItemEmcGadget)?.Kind == "archangelssmite";

        public ItemStack Held => held;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (held != null && !held.ResolveBlockOrItem(api.World)) held = null;
            if (api.Side == EnumAppSide.Server)
            {
                // A held item that didn't survive a mod change leaves the block in its "active" look; put it right.
                if (held == null) api.Event.RegisterCallback(_ => { try { UpdateBlockState(); } catch { } }, 50);
                RegisterGameTickListener(OnServerTick, 500);
                if ((held?.Collectible as ItemEmcTrinket)?.Kind is string k && (k == "evertideamulet" || k == "volcaniteamulet"))
                    RegisterDelayedCallback(_ => ApplyWeather(true), 3000);
            }
            else
            {
                renderer = new PedestalItemRenderer((ICoreClientAPI)api, Pos, (float)(Block?.SelectionBoxes?.FirstOrDefault()?.Y2 ?? 0.75) + 0.3f);
                ((ICoreClientAPI)api).Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "vintagee-pedestal");
                renderer.Stack = held;
            }
        }

        PedestalItemRenderer renderer;

        /// <summary>The Evertide Amulet on a pedestal brings rain, the Volcanite Amulet stops it; taken off (or gone), the weather is the game's again.</summary>
        void ApplyWeather(bool placed)
        {
            if (!(Api is ICoreServerAPI)) return;
            TrinketEffects fx = VintageEModSystem.Of(Api)?.Trinkets;
            fx?.SetPedestalWeather(Pos, placed ? (held?.Collectible as ItemEmcTrinket)?.Kind : null);
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (Api.Side == EnumAppSide.Client) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;
            if (held == null && IsPedestalItem(hand?.Itemstack))
            {
                held = hand.TakeOut(1);
                hand.MarkDirty();
                ApplyWeather(true);
                PESounds.Play(Api.World, PESounds.Charge, null, Pos, 12); // ProjectE: a pedestal switching on charges up
            }
            else if (held != null && (hand == null || hand.Empty))
            {
                ApplyWeather(false);
                PESounds.Play(Api.World, PESounds.Uncharge, null, Pos, 12);
                if (!byPlayer.InventoryManager.TryGiveItemstack(held, true)) Api.World.SpawnItemEntity(held, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
                held = null;
            }
            else return true;
            UpdateBlockState();
            MarkDirty(true);
            return true;
        }

        void UpdateBlockState()
        {
            Block current = Api.World.BlockAccessor.GetBlock(Pos);
            if (current?.Code == null) return;
            Block want = Api.World.GetBlock(current.CodeWithVariant("state", held == null ? "empty" : "active"));
            if (want != null && want.Id != 0 && want.Id != current.Id) Api.World.BlockAccessor.ExchangeBlock(want.Id, Pos);
        }

        void OnServerTick(float dt)
        {
            try { PedestalTick(dt); }
            catch (Exception e) { Api.Logger.Warning("Vintage-E pedestal at {0}: {1}", Pos, e.Message); }
        }

        void PedestalTick(float dt)
        {
            TrinketEffects fx = VintageEModSystem.Of(Api)?.Trinkets;
            if (fx == null) return;
            Vec3d center = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
            if ((held?.Collectible as ItemEmcGadget)?.Kind == "archangelssmite")
            {
                smiteAcc += dt;
                if (smiteAcc >= SmiteSeconds)
                {
                    smiteAcc = 0;
                    SmiteVolley(fx, center);
                }
                return;
            }
            if (!(held?.Collectible is ItemEmcTrinket t)) return;
            acc += dt;
            bool everySecond = acc >= 1;
            if (everySecond) acc = 0;
            swrgAcc += dt;
            foreach (string effect in TrinketEffects.EffectsOf(t, held))
            {
                PedestalEffect(fx, effect, center, everySecond);
            }
        }

        /// <summary>How much further the pedestal reaches than a Dark Matter Pedestal (block attribute "pedestalRange"; red matter: 2).</summary>
        public float RangeMultiplier { get { ReadMultipliers(); return rangeMultiplier; } }
        /// <summary>How much stronger its effects are (block attribute "pedestalPower"; red matter: 2).</summary>
        public float PowerMultiplier { get { ReadMultipliers(); return powerMultiplier; } }

        // Read from the block's attributes once per block (it changes between the empty and active looks): they were
        // parsed from JSON several times on every tick of every pedestal.
        Block multipliersOf;
        float rangeMultiplier = 1, powerMultiplier = 1;

        void ReadMultipliers()
        {
            Block block = Block;
            if (block != null && ReferenceEquals(block, multipliersOf)) return;
            multipliersOf = block;
            rangeMultiplier = block?.Attributes?["pedestalRange"].AsFloat(1) ?? 1;
            powerMultiplier = block?.Attributes?["pedestalPower"].AsFloat(1) ?? 1;
        }

        int R(float r) => (int)Math.Round(r * RangeMultiplier);

        void PedestalEffect(TrinketEffects fx, string effect, Vec3d center, bool everySecond)
        {
            float range = RangeMultiplier, power = PowerMultiplier;
            switch (effect)
            {
                case "harvestgoddessband":
                    if (everySecond)
                    {
                        // A bigger area gets more growth attempts, so crops in it grow as often per block, then more with power.
                        fx.Grow(Pos, R(8), (int)Math.Round(10 * range * range * power), null, 0);
                        fx.Harvest(Pos, R(8), null);
                    }
                    break;
                case "watchofflowingtime":
                    if (everySecond) fx.Grow(Pos, R(12), (int)Math.Round(24 * range * range * power), null, 0);
                    fx.Slow(center, 12 * range);
                    break;
                case "ignitionring": if (everySecond) fx.Burn(center, 8 * range, null, null, 0, 3 * power); break;
                case "zeroring":
                    // Once a second (it was twice): a red pedestal looks at 1,300 blocks of water each time.
                    if (everySecond) fx.Freeze(Pos.UpCopy(), R(6), null);
                    fx.Extinguish(center, 6 * range);
                    break;
                case "blackholeband":
                    fx.Attract(center.AddCopy(0, 0.6, 0), 10 * range, outsideClaimsOnly: true);
                    Collect(center);
                    break;
                case "voidpull":
                    fx.Attract(center.AddCopy(0, 0.6, 0), TrinketEffects.VoidPullRadius * range, outsideClaimsOnly: true);
                    Collect(center);
                    break;
                case "swiftwolfsrendinggale":
                    if (swrgAcc >= SwrgSeconds)
                    {
                        swrgAcc = 0;
                        StrikeAround(fx, center);
                    }
                    break;
                case "soulstone":
                case "bodystone":
                case "lifestone":
                case "repairtalisman":
                    if (everySecond) HelpPlayers(effect, center);
                    break;
            }
        }

        /// <summary>
        /// ProjectE's Swiftwolf pedestal: lightning on every creature in reach that isn't a player or a pet (at most 8 a
        /// time). Next to a player, or without the game's weather system, 4 damage each (× the pedestal's power) instead.
        /// </summary>
        public int StrikeAround(TrinketEffects fx, Vec3d center)
        {
            float range = RangeMultiplier, power = PowerMultiplier;
            int n = 0;
            foreach (Entity e in Api.World.GetEntitiesAround(center, 10 * range, 6 * range, x => x is EntityAgent && !(x is EntityPlayer) && x.Alive && !TrinketEffects.IsPet(x)))
            {
                if (n++ >= 8) break;
                // The game's lightning hurts everything within 8 blocks of where it lands: with a player that close, the
                // creature is shocked directly instead, so standing by your own pedestal is safe.
                bool playerNear = Api.World.GetPlayersAround(e.Pos.XYZ, 9, 9).Any(p => p?.Entity != null && p.Entity.Alive);
                if (playerNear || !WeatherControl.Strike((ICoreServerAPI)Api, e.Pos.XYZ))
                {
                    e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Block, Type = EnumDamageType.Electricity }, 4f * power);
                }
            }
            return n;
        }

        /// <summary>ProjectE's smite pedestal: with a hostile creature in reach, three homing arrows go up from 2 blocks above it.</summary>
        public int SmiteVolley(TrinketEffects fx, Vec3d center)
        {
            float range = RangeMultiplier, power = PowerMultiplier;
            if (Api.World.GetNearestEntity(center, 10 * range, 6 * range, fx.IsHostile) == null) return 0;
            int fired = 0;
            for (int i = 0; i < 3; i++)
            {
                var dir = new Vec3d((Api.World.Rand.NextDouble() - 0.5) * 0.2, 1, (Api.World.Rand.NextDouble() - 0.5) * 0.2);
                if (EntityVintageEProjectile.ShootFrom(Api.World, center.AddCopy(0, 2, 0), dir, "arrow", 50, shot => shot.Damage = ItemEmcGadget.ArrowDamage * power) != null) fired++;
            }
            if (fired > 0) Api.World.PlaySoundAt(new AssetLocation("game:sounds/bow-release"), center.X, center.Y + 2, center.Z, null, true, 24, 1f);
            return fired;
        }

        /// <summary>Puts items that reached the pedestal into containers next to it (below first, then the sides and above).</summary>
        void Collect(Vec3d center)
        {
            var containers = new System.Collections.Generic.List<IBlockEntityContainer>();
            foreach (BlockFacing face in new[] { BlockFacing.DOWN, BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.UP })
            {
                if (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face)) is IBlockEntityContainer c && c.Inventory != null) containers.Add(c);
            }
            if (containers.Count == 0) return;
            foreach (Entity e in Api.World.GetEntitiesAround(center.AddCopy(0, 0.6, 0), 1.5f, 1.5f, x => x is EntityItem && x.Alive))
            {
                var ei = (EntityItem)e;
                if (ei.Itemstack == null) continue;
                var source = new DummySlot(ei.Itemstack);
                foreach (IBlockEntityContainer container in containers)
                {
                    // Only where the container lets automation push (a hopper would): never a condenser's target slot
                    // or a furnace's fuel slot, which taking every slot in turn used to fill with whatever fell nearby.
                    if (!(container.Inventory is InventoryBase inv)) continue;
                    for (int guard = 0; guard < 64 && !source.Empty; guard++)
                    {
                        ItemSlot slot = null;
                        try { slot = inv.GetAutoPushIntoSlot(BlockFacing.UP, source); } catch { }
                        if (slot == null) break;
                        int moved = source.TryPutInto(Api.World, slot, source.StackSize);
                        if (moved <= 0) break;
                    }
                    if (source.Empty) break;
                }
                if (source.Empty) e.Die(EnumDespawnReason.PickedUp);
                else ei.Itemstack = source.Itemstack;
            }
        }

        void HelpPlayers(string effect, Vec3d center)
        {
            float range = RangeMultiplier, power = PowerMultiplier;
            foreach (IPlayer p in Api.World.GetPlayersAround(center, 6 * range, 4 * range))
            {
                if (!(p.Entity is EntityPlayer ep) || !ep.Alive) continue;
                if (effect == "soulstone" || effect == "lifestone")
                {
                    ITreeAttribute health = ep.WatchedAttributes.GetTreeAttribute("health");
                    if (health != null && health.GetFloat("currenthealth") < health.GetFloat("maxhealth"))
                    {
                        ep.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, 0.5f * power);
                        PESounds.Play(Api.World, PESounds.Heal, null, ep, 8);
                    }
                }
                if (effect == "bodystone" || effect == "lifestone")
                {
                    ITreeAttribute hunger = ep.WatchedAttributes.GetTreeAttribute("hunger");
                    if (hunger != null)
                    {
                        float cur = hunger.GetFloat("currentsaturation"), max = hunger.GetFloat("maxsaturation");
                        if (max > 0 && cur < max) TrinketEffects.GiveSaturation(ep, Math.Min(max - cur, 25 * power), ItemEmcTrinket.FeedsNutrition(held));
                    }
                }
                if (effect == "repairtalisman")
                {
                    VintageEConfig config = VintageEModSystem.Of(Api)?.Config;
                    for (int n = 0; n < Math.Max(1, (int)Math.Round(power)); n++)
                    {
                        foreach (ItemSlot slot in EmcWallet.CarriedSlots(p)) TrinketEffects.RepairOnce(slot, config);
                    }
                }
            }
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (held != null && Api.Side == EnumAppSide.Server)
            {
                ApplyWeather(false);
                Api.World.SpawnItemEntity(held, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            held = null;
            base.OnBlockBroken(byPlayer);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            ApplyWeather(false);
            DisposeRenderer();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            ApplyWeather(false);
            DisposeRenderer();
        }

        void DisposeRenderer()
        {
            if (renderer == null) return;
            (Api as ICoreClientAPI)?.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            renderer.Dispose();
            renderer = null;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (held != null) tree.SetItemstack("held", held);
            else tree.RemoveAttribute("held");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            held = tree.GetItemstack("held");
            // An item from a removed mod can't be resolved: drop it rather than crash on every look or click.
            if (held != null && !held.ResolveBlockOrItem(worldAccessForResolve)) held = null;
            if (renderer != null) renderer.Stack = held;
        }

        public override void OnStoreCollectibleMappings(System.Collections.Generic.Dictionary<int, AssetLocation> blockIdMapping, System.Collections.Generic.Dictionary<int, AssetLocation> itemIdMapping)
        {
            held?.Collectible?.OnStoreCollectibleMappings(Api.World, new DummySlot(held), blockIdMapping, itemIdMapping);
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, System.Collections.Generic.Dictionary<int, AssetLocation> oldBlockIdMapping, System.Collections.Generic.Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            if (held != null && !held.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve)) held = null;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(held == null ? Lang.Get("vintagee:pedestal-empty") : Lang.Get("vintagee:pedestal-holding", held.GetName()));
        }
    }
}
