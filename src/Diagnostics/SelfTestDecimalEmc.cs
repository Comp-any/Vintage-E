using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// 1.27.0: EMC totals as decimal. A player's balance, a Klein star's contents and the Gem of Eternal Density's
    /// store are exact (<see cref="EmcAmount"/>); saves from before keep working, and so does going back to an older
    /// version. These run without a player: the balance's save format is checked on attribute trees like a player's.
    /// </summary>
    public partial class SelfTest
    {
        void CheckDecimalEmc()
        {
            Line("EMC totals as decimal (1.27.0)");

            // Amounts worked out in double come in safely.
            Check(EmcAmount.From(double.NaN) == 0m && EmcAmount.From(double.PositiveInfinity) == decimal.MaxValue
                && EmcAmount.From(double.NegativeInfinity) == decimal.MinValue && EmcAmount.From(1e30) == decimal.MaxValue,
                "a NaN, an infinity or a number past the decimal range never throws (0, or clamped)");
            Check(EmcAmount.From(102.4) == 102.4m && EmcAmount.From(0.1 + 0.2) == 0.3m && EmcAmount.From(0.0001) == 0.0001m,
                $"a value from the table comes in as written ({EmcAmount.From(102.4)}, {EmcAmount.From(0.1 + 0.2)}, {EmcAmount.From(0.0001)})");
            Check(EmcAmount.Add(decimal.MaxValue, 1m) == decimal.MaxValue && EmcAmount.Add(decimal.MinValue, -1m) == decimal.MinValue
                && EmcAmount.Multiply(decimal.MaxValue, 2m) == decimal.MaxValue,
                "adding past the largest decimal stops there instead of throwing");

            // Dividing a total by a value (how many can be made, how many fit) overflows long before the total does.
            Check(EmcAmount.Divide(decimal.MaxValue, 0.0001m) == decimal.MaxValue && EmcAmount.Divide(5m, 0m) == 0m && EmcAmount.Divide(10m, 4m) == 2.5m,
                "dividing the largest total by the smallest value stops at the largest decimal (and by 0 gives 0) instead of throwing");
            Check(EmcAmount.HowMany(decimal.MaxValue, 0.0001m) == int.MaxValue && EmcAmount.HowMany(10m, 3m) == 3 && EmcAmount.HowMany(10m, 0.0001m) == 100000
                && EmcAmount.HowMany(-5m, 1m) == 0 && EmcAmount.HowMany(5m, 0m) == 0 && EmcAmount.HowMany(0.00009m, 0.0001m) == 0,
                $"how many fit is counted without throwing ({EmcAmount.HowMany(decimal.MaxValue, 0.0001m)} items of 0.0001 for the largest balance)");
            decimal ceiling = decimal.MaxValue;
            for (int i = 0; i < 3; i++) ceiling = EmcAmount.Add(ceiling, EmcAmount.From(10_485_760));
            Check(ceiling == decimal.MaxValue && EmcAmount.Add(ceiling, -1m) == decimal.MaxValue - 1m,
                "a balance at the ceiling stays there when more is added, and spending from it works");

            // The point of it: small amounts on a huge total.
            decimal exact = 1_000_000_000_000_000m;
            double rough = 1e15;
            for (int i = 0; i < 1000; i++) { exact = EmcAmount.Add(exact, EmcAmount.From(0.09)); rough += 0.09; }
            Check(exact == 1_000_000_000_000_090m,
                $"1,000 items worth 0.09 on a balance of 1 quadrillion add up exactly ({EmcFormat.Total(exact)}; a double gives {rough.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})");
            decimal star = 3_518_437_208_883_200_000m + 0.0001m;
            Check(star - 3_518_437_208_883_200_000m == 0.0001m, "a full Gargantuan Omega star (3.5 quintillion) still keeps 0.0001");

            // How it is saved: the exact text, and the double an older version reads.
            const string key = "vintageeTestEmc";
            var tree = new TreeAttribute();
            decimal big = 123_456_789_012_345_678.0123m;
            EmcAmount.Write(tree, key, big);
            Check(EmcAmount.Read(tree, key) == big && tree.GetDouble(key) == (double)big && tree.GetString(key + EmcAmount.Suffix) != null,
                $"a total is saved exactly, with the old double beside it ({tree.GetString(key + EmcAmount.Suffix)}, {tree.GetDouble(key)})");
            var copy = tree.Clone() as TreeAttribute;
            Check(EmcAmount.Read(copy, key) == big, "the saved total survives the tree being copied (as the game does when it saves)");
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                tree.ToBytes(w);
                w.Flush();
                ms.Position = 0;
                var back = new TreeAttribute();
                back.FromBytes(new BinaryReader(ms));
                Check(EmcAmount.Read(back, key) == big, $"the saved total survives being written to bytes and read back ({EmcAmount.Read(back, key)})");
            }

            // Saves from before 1.27.0, and from an older version run in between.
            var legacy = new TreeAttribute();
            legacy.SetDouble(key, 50.5);
            Check(EmcAmount.Read(legacy, key) == 50.5m, "a save from before 1.27.0 (a double only) reads as it was");
            EmcAmount.Write(legacy, key, EmcAmount.Add(EmcAmount.Read(legacy, key), 0.25m));
            Check(EmcAmount.Read(legacy, key) == 50.75m && legacy.HasAttribute(key + EmcAmount.Suffix),
                "it gains the exact copy the first time it changes (no separate step needed)");
            legacy.SetDouble(key, 70); // an older Vintage-E changed only the double
            Check(EmcAmount.Read(legacy, key) == 70m, "after going back to an older version and changing the balance there, its number wins");
            var broken = new TreeAttribute();
            broken.SetDouble(key, 5);
            broken.SetString(key + EmcAmount.Suffix, "not a number");
            var nan = new TreeAttribute();
            nan.SetDouble(key, double.NaN);
            Check(EmcAmount.Read(broken, key) == 5m && EmcAmount.Read(nan, key) == 0m && EmcAmount.Read(null, key) == 0m,
                "a broken exact copy falls back to the double, a NaN reads as 0, no tree reads as 0");
            EmcAmount.Remove(tree, key);
            Check(!EmcAmount.Has(tree, key) && EmcAmount.Read(tree, key) == 0m, "removing a total removes both copies");

            // A player's balance moved over from the synced attributes (before 1.21.1) joins the exact one.
            var watched = new TreeAttribute();
            var own = new TreeAttribute();
            watched.SetDouble(PlayerEmc.EmcKey, 50);
            EmcAmount.Write(own, PlayerEmc.EmcKey, 0.0001m);
            PlayerEmc.MigrateTree(watched, own);
            Check(EmcAmount.Read(own, PlayerEmc.EmcKey) == 50.0001m && !EmcAmount.Has(watched, PlayerEmc.EmcKey),
                $"a balance from before 1.21.1 is added to the exact one ({EmcAmount.Read(own, PlayerEmc.EmcKey)})");

            // What a player's game is sent: the exact number.
            var packet = new PlayerEmcPacket { HasEmc = true, Emc = 1_234_567_890_123_456_789.0123m };
            var result = new TransmuteResultPacket { HasEmc = true, Emc = 7_922_816_251_426_433_759_354_395.0335m };
            PlayerEmcPacket packetBack;
            TransmuteResultPacket resultBack;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, packet);
                ms.Position = 0;
                packetBack = ProtoBuf.Serializer.Deserialize<PlayerEmcPacket>(ms);
            }
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, result);
                ms.Position = 0;
                resultBack = ProtoBuf.Serializer.Deserialize<TransmuteResultPacket>(ms);
            }
            Check(packetBack?.Emc == packet.Emc && resultBack?.Emc == result.Emc,
                $"a player's game is sent the exact balance ({packetBack?.Emc}, {resultBack?.Emc})");

            // Klein stars.
            var starItem = sapi.World.GetItem(new AssetLocation("vintagee:kleinstar-ein"));
            if (starItem == null) Check(false, "the Klein star Ein is there to test with");
            else
            {
                var old = new ItemStack(starItem);
                old.Attributes.SetDouble(StoredEmc.AttributeKey, 1000);
                Check(StoredEmc.Get(old) == 1000m, "a Klein star filled before 1.27.0 keeps what it holds");
                var full = new ItemStack(starItem);
                StoredEmc.Set(full, 3_518_437_208_883_200_000.0001m);
                Check(StoredEmc.Get(full) == 3_518_437_208_883_200_000.0001m, "a star holds a huge amount exactly");
                StoredEmc.Set(full, 0m);
                Check(!EmcAmount.Has(full.Attributes, StoredEmc.AttributeKey), "an emptied star carries no EMC data at all (so it stacks and compares like a new one)");
                var a = new ItemStack(starItem);
                var b = new ItemStack(starItem);
                StoredEmc.Set(a, 10m);
                StoredEmc.Set(b, 20.5m);
                TreeAttribute keptA = StackDataUtil.KeptData(a), keptB = StackDataUtil.KeptData(b);
                Check((keptA == null && keptB == null) || (keptA != null && keptB != null && keptA.Equals(sapi.World, keptB)),
                    "how much a star holds doesn't make it a different thing to learn (the exact copy is ignored like the old one)");
                Check(StoredEmc.Capacity(a) == 50000m, $"a star's capacity reads as a decimal ({StoredEmc.Capacity(a)})");
            }

            // Machines: a collector's, relay's or condenser's store, saved under the same key as before.
            Block relayBlock = sapi.World.GetBlock(new AssetLocation("vintagee:antimatterrelay-mk1"));
            Block collectorBlock = sapi.World.GetBlock(new AssetLocation("vintagee:energycollector-mk1"));
            if (relayBlock == null || collectorBlock == null) Check(false, "the Antimatter Relay MK1 and Energy Collector MK1 are there to test with");
            else
            {
                var oldRelay = new TreeAttribute();
                oldRelay.SetDouble("emc", 1234.5);
                var oldCollector = new TreeAttribute();
                oldCollector.SetDouble("emc", 9999.75);
                oldCollector.SetDouble("rate", 4);
                var relayLoaded = new BEAntimatterRelay { Block = relayBlock };
                var collectorLoaded = new BEEnergyCollector { Block = collectorBlock };
                relayLoaded.FromTreeAttributes(oldRelay, sapi.World);
                collectorLoaded.FromTreeAttributes(oldCollector, sapi.World);
                Check(relayLoaded.Stored == 1234.5m && collectorLoaded.Stored == 9999.75m,
                    $"a relay and a collector saved before 1.27.0 keep their stores ({relayLoaded.Stored}, {collectorLoaded.Stored})");

                var saving = new BEAntimatterRelay { Block = relayBlock, Pos = new BlockPos(0, 0, 0), Stored = 99_999.0001m };
                var saved = new TreeAttribute();
                saving.ToTreeAttributes(saved);
                var reloaded = new BEAntimatterRelay { Block = relayBlock };
                reloaded.FromTreeAttributes(saved, sapi.World);
                Check(reloaded.Stored == 99_999.0001m && saved.GetDouble("emc") == 99_999.0001,
                    $"a relay's store is saved exactly, with the double an older version reads ({reloaded.Stored}, {saved.GetDouble("emc")})");

                // What a tick adds: a rate times the tick's length, as an exact amount.
                decimal sum = 0m;
                for (int i = 0; i < 10; i++) sum = EmcAmount.Add(sum, EmcTransfer.PerTick(0.1));
                Check(sum == 1m, $"ten ticks adding 0.1 EMC come to exactly 1 ({sum})");
            }

            // Shown as before, cut off rather than rounded, and never throwing.
            Check(EmcFormat.Total(1_234_567.899m) == "1,234,567.89" && EmcFormat.Total(0.0129m) == "0.012" && EmcFormat.Total(12.5m) == "12.5",
                $"totals show as before ({EmcFormat.Total(1_234_567.899m)}, {EmcFormat.Total(0.0129m)}, {EmcFormat.Total(12.5m)})");
            Check(EmcFormat.Short(1_234_567) == "1.23 " + Lang.Get("vintagee:number-million") && EmcFormat.Short(4.5e12) == "4.5 " + Lang.Get("vintagee:number-trillion")
                && EmcFormat.ShortTotal(3_518_437_208_883_200_000m) == "3.51 " + Lang.Get("vintagee:number-quintillion"),
                $"the short form goes past trillions ({EmcFormat.Short(1_234_567)}, {EmcFormat.Short(4.5e12)}, {EmcFormat.ShortTotal(3_518_437_208_883_200_000m)})");
            string most = null;
            try { most = EmcFormat.Total(decimal.MaxValue) + " / " + EmcFormat.ShortTotal(decimal.MaxValue); }
            catch (Exception e) { most = "threw " + e.GetType().Name; }
            Check(most != null && !most.StartsWith("threw", StringComparison.Ordinal), $"the largest total shows without throwing ({most})");
        }
    }
}
