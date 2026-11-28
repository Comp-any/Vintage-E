using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// A Power Flower (1.29.2, Project Expansion's): makes EMC every second, with no sunlight needed, as much as 18
    /// collectors and 30 relay bonuses of its tier (the block's <c>emcPerSecond</c>), and pays it straight into its
    /// owner's personal EMC. While the owner isn't in the game it banks what it makes and pays it all when they're back;
    /// broken, the bank goes with the block (<see cref="BlockOwnedMachine"/>). A Compact Sun under it multiplies its
    /// output (<see cref="VintageEConfig.CompactSunBonus"/>), and the server's <see cref="VintageEConfig.PowerFlowerMultiplier"/>.
    /// </summary>
    public class BEPowerFlower : BlockEntity, IOwnedMachine
    {
        public string OwnerUid { get; private set; }
        public string OwnerName { get; private set; }
        /// <summary>EMC made while its owner was away, waiting for them.</summary>
        public decimal Stored;
        double basePerSecond;

        public decimal Held { get => Stored; set => Stored = value < 0 ? 0 : value; }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ReadTier();
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 1000);
        }

        /// <summary>The Matter Upgrader swaps the block in place: read the new tier's numbers.</summary>
        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            ReadTier();
        }

        void ReadTier() => basePerSecond = (Block ?? Api?.World.BlockAccessor.GetBlock(Pos))?.Attributes?["emcPerSecond"].AsDouble(0) ?? 0;

        public double BasePerSecond => basePerSecond;
        // Players' games have the default settings, so they show what the server last sent (as the collector does).
        double shownRate;
        int shownSun = 1;
        bool OnServer => Api?.Side == EnumAppSide.Server;
        public int SunBonus => !OnServer ? shownSun : BlockCompactSun.Below(Api?.World, Pos) ? BlockCompactSun.Bonus(Api) : 1;

        /// <summary>EMC a second with the server's multiplier and a Compact Sun under it.</summary>
        public double PerSecond
        {
            get
            {
                if (!OnServer) return shownRate;
                double mult = Math.Max(0, VintageEModSystem.Of(Api)?.Config?.PowerFlowerMultiplier ?? 1);
                return basePerSecond * mult * SunBonus;
            }
        }

        public void SetOwner(string uid, string name)
        {
            OwnerUid = uid;
            OwnerName = name;
            MarkDirty(true);
        }

        void OnServerTick(float dt) => Produce(dt);

        /// <summary>One tick's work: makes <paramref name="seconds"/> worth and pays the owner if they're in the game. Returns what it paid.</summary>
        public decimal Produce(float seconds)
        {
            decimal made = EmcTransfer.PerTick(PerSecond * seconds);
            decimal before = Stored;
            Stored = EmcAmount.Add(Stored, made);
            IServerPlayer owner = OwnedMachine.OnlineOwner(Api, OwnerUid);
            decimal paid = 0;
            if (owner != null && Stored > 0)
            {
                paid = Stored;
                PlayerEmc.AddEmcExact(owner, Stored);
                Stored = 0;
            }
            // Sent to players only when what's waiting or the rate changes (not every second while it pays).
            double rate = PerSecond;
            if (Stored != before || Math.Abs(rate - shownRate) > 1e-9)
            {
                shownRate = rate;
                shownSun = SunBonus;
                MarkDirty(false);
            }
            return paid;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (OwnerUid != null) tree.SetString("ownerUid", OwnerUid);
            if (OwnerName != null) tree.SetString("ownerName", OwnerName);
            EmcAmount.Write(tree, "emc", Stored);
            tree.SetDouble("rate", OnServer ? PerSecond : shownRate);
            tree.SetInt("sun", SunBonus);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            OwnerUid = tree.GetString("ownerUid");
            OwnerName = tree.GetString("ownerName");
            Stored = EmcAmount.Read(tree, "emc");
            shownRate = tree.GetDouble("rate");
            shownSun = Math.Max(1, tree.GetInt("sun", 1));
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(OwnerName == null ? Lang.Get("vintagee:owned-noowner")
                : Lang.Get(forPlayer?.PlayerUID == OwnerUid ? "vintagee:owned-owner-you" : "vintagee:owned-owner", OwnerName));
            int sun = SunBonus;
            dsc.AppendLine(Lang.Get(sun > 1 ? "vintagee:flower-rate-sun" : "vintagee:flower-rate", EmcFormat.Format(PerSecond), sun));
            if (Stored > 0) dsc.AppendLine(Lang.Get("vintagee:flower-waiting", EmcFormat.Total(Stored)));
        }
    }
}
