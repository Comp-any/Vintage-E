using System;
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
    /// </summary>
    public class BEEnergyCollector : BlockEntity, IEmcInteractable, IStarHolder
    {
        public double Stored;
        public StarHolder Star { get; } = new StarHolder();
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

        VintageEModSystem Mod => Api.ModLoader.GetModSystem<VintageEModSystem>();

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

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("emc", Stored);
            tree.SetDouble("rate", currentRate);
            Star.ToTree(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Stored = tree.GetDouble("emc");
            currentRate = tree.GetDouble("rate");
            Star.FromTree(tree, worldAccessForResolve);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:collector-stored", EmcFormat.Format(Stored), EmcFormat.Format(capacity)));
            dsc.AppendLine(Lang.Get("vintagee:collector-rate", EmcFormat.Format(currentRate)));
            Star.Info(dsc);
        }
    }
}
