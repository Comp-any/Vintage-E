using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// The Storm Collector (1.29.4, an idea from the ProjectE add-ons): EMC from the weather instead of the sun.
    /// <list type="bullet">
    /// <item>Rain or snow falling on it: up to <c>emcPerSecond</c> at full strength (it must see the sky).</item>
    /// <item>A thunderstorm: times <c>thunderMultiplier</c>.</item>
    /// <item>A temporal storm: <c>temporalPerSecond</c> more, wherever it stands (Vintage Story's own storms).</item>
    /// <item>Lightning that strikes it: <c>lightningEmc</c> at once. It is a lightning rod too (the game's AttractsLightning
    /// behaviour, in its JSON), so it draws the strikes around it.</item>
    /// </list>
    /// It keeps up to <c>capacity</c> and feeds condensers, relays, furnaces and EMC Links next to it, as a collector does;
    /// sneak + right-click with an empty hand or a Klein star takes the EMC. Rates times the server's MachineSpeedMultiplier.
    /// </summary>
    public class BEStormCollector : BlockEntity, IEmcInteractable
    {
        public decimal Stored;
        double emcPerSecond = 8, thunderMultiplier = 3, temporalPerSecond = 40, lightningEmc = 4000;
        decimal capacity = 20000;
        double shownRate;
        long lastStrikeMs = -10000;
        WeatherSystemServer weather;

        public decimal Capacity => capacity;
        public double EmcPerSecond => emcPerSecond;
        public double LightningEmc => lightningEmc;
        public double TemporalPerSecond => temporalPerSecond;
        public double ThunderMultiplier => thunderMultiplier;
        /// <summary>What it made a second when last worked out (players' games get it from the server).</summary>
        public double ShownRate => shownRate;
        /// <summary>Lightning that has struck it since the server started (the doctor and the test).</summary>
        public int Strikes { get; private set; }

        VintageEModSystem Mod => VintageEModSystem.Of(Api);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ReadTier();
            if (api.Side != EnumAppSide.Server) return;
            RegisterGameTickListener(OnServerTick, 1000);
            // After base.Initialize: its own lightning rod behaviour has moved a strike onto it by the time this hears it.
            weather = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (weather != null) weather.OnLightningImpactBegin += OnLightning;
        }

        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            ReadTier();
        }

        void ReadTier()
        {
            JsonObject a = (Block ?? Api?.World.BlockAccessor.GetBlock(Pos))?.Attributes;
            if (a == null) return;
            emcPerSecond = a["emcPerSecond"].AsDouble(8);
            thunderMultiplier = a["thunderMultiplier"].AsDouble(3);
            temporalPerSecond = a["temporalPerSecond"].AsDouble(40);
            lightningEmc = a["lightningEmc"].AsDouble(4000);
            capacity = EmcAmount.From(a["capacity"].AsDouble(20000));
        }

        public override void OnBlockRemoved()
        {
            Unhook();
            base.OnBlockRemoved();
        }

        public override void OnBlockUnloaded()
        {
            Unhook();
            base.OnBlockUnloaded();
        }

        void Unhook()
        {
            if (weather != null) weather.OnLightningImpactBegin -= OnLightning;
            weather = null;
        }

        /// <summary>Whether it sees the sky: nothing above it stops rain.</summary>
        public bool UnderSky => Api != null && Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z) <= Pos.Y;

        /// <summary>Rain or snow on it now, 0 to 1 (nothing under a roof).</summary>
        public double Rain => Api is ICoreServerAPI sapi && UnderSky ? GameMath.Clamp(WeatherControl.Precipitation(sapi, Pos.ToVec3d().Add(0.5, 1, 0.5)), 0, 1) : 0;

        public bool Thunder => Api is ICoreServerAPI sapi && UnderSky && WeatherControl.IsThundering(sapi, Pos.ToVec3d().Add(0.5, 1, 0.5));

        public bool TemporalStorm => AlchemicalBooks.StormNow(Api);

        /// <summary>EMC a second now, from the weather (lightning comes on top, when it strikes).</summary>
        public double RateNow() => Rate(emcPerSecond, Rain, Thunder, thunderMultiplier, TemporalStorm, temporalPerSecond, Mod?.Config?.MachineSpeedMultiplier ?? 1);

        /// <summary>The weather's EMC a second: rain (0 to 1) times the rate, times more in a thunderstorm, plus a temporal storm's.</summary>
        public static double Rate(double perSecond, double rain, bool thunder, double thunderMultiplier, bool temporal, double temporalPerSecond, double speed)
        {
            double rate = perSecond * GameMath.Clamp(rain, 0, 1) * (thunder ? thunderMultiplier : 1);
            if (temporal) rate += temporalPerSecond;
            return rate * Math.Max(0, speed);
        }

        /// <summary>One second's work now (the test runs it without waiting).</summary>
        public void Tick(float dt) => OnServerTick(dt);

        void OnServerTick(float dt)
        {
            double rate = RateNow();
            decimal before = Stored;
            Stored = Math.Min(capacity, EmcAmount.Add(Stored, EmcTransfer.PerTick(rate * dt)));
            Feed();
            if (Stored != before || Math.Abs(rate - shownRate) > 1e-6)
            {
                shownRate = rate;
                MarkDirty(false);
            }
        }

        /// <summary>Gives what it holds to the EMC acceptors next to it (condensers, relays, furnaces, links).</summary>
        void Feed()
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (Stored <= 0) break;
                if (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face)) is IEmcAcceptor acceptor) Stored -= acceptor.AcceptEmc(Stored);
            }
            if (Stored < 0) Stored = 0;
        }

        void OnLightning(ref Vec3d impactPos, ref EnumHandling handling)
        {
            if (Api == null || impactPos == null) return;
            double dx = impactPos.X - (Pos.X + 0.5), dz = impactPos.Z - (Pos.Z + 0.5);
            if (dx * dx + dz * dz > 2.25 || Math.Abs(impactPos.Y - (Pos.Y + 1)) > 3) return; // only a strike on it
            Struck();
        }

        /// <summary>Lightning struck it: its burst of EMC (once per strike, however many times the game reports it).</summary>
        public decimal Struck()
        {
            long now = Api.World.ElapsedMilliseconds;
            if (now - lastStrikeMs < 500) return 0;
            lastStrikeMs = now;
            Strikes++;
            decimal before = Stored;
            Stored = Math.Min(capacity, EmcAmount.Add(Stored, EmcAmount.From(lightningEmc * (Mod?.Config?.MachineSpeedMultiplier ?? 1))));
            MarkDirty(false);
            return Stored - before;
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!byPlayer.Entity.Controls.Sneak) return false;
            ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!(hand?.Empty != false || hand.Itemstack.Collectible is IEmcStorageItem)) return false;
            if (Api.Side == EnumAppSide.Client) return true;
            if (Mod?.Config?.CollectorsChargePlayers == false) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;
            if (Stored < 1) return true;
            Stored -= EmcTransfer.ToPlayerOrStar(byPlayer, decimal.Floor(Stored));
            MarkDirty(false);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 8, 0.4f);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            EmcAmount.Write(tree, "emc", Stored);
            tree.SetDouble("rate", shownRate);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Stored = EmcAmount.Read(tree, "emc");
            shownRate = EmcTransfer.Clean(tree.GetDouble("rate"));
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:collector-stored", EmcFormat.Total(Stored), EmcFormat.Total(capacity)));
            dsc.AppendLine(Lang.Get("vintagee:storm-rate", EmcFormat.Format(shownRate)));
        }
    }
}
