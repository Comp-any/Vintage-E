using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>1.29.4: Project Expansion's fuel tiers, the Storm Collector, and Burn what I mine around the area actions.</summary>
    public partial class SelfTest
    {
        static readonly string[] FuelTiers = { "magenta", "pink", "purple", "violet", "blue", "cyan", "green", "lime", "yellow", "orange", "white" };

        void CheckFuelsAndStorm()
        {
            bool on = mod.Config.Expansion;
            Line($"Fuel tiers, Storm Collector, Burn what I mine for area actions (1.29.4): Expansion {(on ? "on" : "off")}");
            Item F(string type) => sapi.World.GetItem(new AssetLocation("vintagee:alchemicalfuel-" + type));
            Block FB(string type) => sapi.World.GetBlock(new AssetLocation("vintagee:alchemicalfuelblock-" + type));
            double V(CollectibleObject c) => c == null ? 0 : mod.Emc.GetValue(c);
            List<GridRecipe> grid = sapi.World.GridRecipes ?? new List<GridRecipe>();

            // The fuel tiers: there with Expansion on, each 4 of the one before, burning 4 times as long.
            var types = FuelTiers.Select(t => t + "fuel").ToList();
            var there = types.Where(t => F(t) != null && FB(t) != null).ToList();
            bool baseThere = F("aeternalisfuel") != null && FB("aeternalisfuel") != null;
            Check(baseThere && (on ? there.Count == types.Count : there.Count == 0 && types.All(t => F(t) == null && FB(t) == null)),
                on ? $"magenta to white fuel and their blocks are there ({there.Count} of {types.Count})" : $"switched off, none of the fuel tiers are there ({there.Count})");
            List<CollectibleObject> chain = FuelUpgrader.Chain(mod, sapi.World);
            string last = chain.LastOrDefault()?.Code?.ToString();
            Check(last == (on ? "vintagee:alchemicalfuel-whitefuel" : "vintagee:alchemicalfuel-aeternalisfuel"),
                $"a collector's fuel slot upgrades through {chain.Count} fuels, up to {last}");
            if (on && there.Count == types.Count)
            {
                var wrong = new List<string>();
                string prev = "aeternalisfuel";
                foreach (string t in types)
                {
                    bool made = grid.Any(r => r.Output?.Code?.ToString() == "vintagee:alchemicalfuel-" + t && r.ResolvedIngredients != null
                        && r.ResolvedIngredients.Count(i => i?.Code?.ToString() == "vintagee:alchemicalfuel-" + prev) == 4
                        && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == "vintagee:philosophersstone"));
                    double ratio = V(F(prev)) > 0 ? V(F(t)) / V(F(prev)) : 0;
                    float burn = F(t).CombustibleProps?.BurnDuration ?? 0, before = F(prev).CombustibleProps?.BurnDuration ?? 0;
                    double forge = F(t).Attributes?["inForge"]["durationMul"].AsDouble() ?? 0, forgeBefore = F(prev).Attributes?["inForge"]["durationMul"].AsDouble() ?? 0;
                    if (!made) wrong.Add(t + " recipe");
                    if (ratio < 3.9 || ratio > 4.1) wrong.Add($"{t} value ×{ratio:0.##}");
                    if (Math.Abs(burn - 4 * before) > 1 || F(t).CombustibleProps?.BurnTemperature != 1600) wrong.Add($"{t} burns {burn} s");
                    if (Math.Abs(forge - 4 * forgeBefore) > 1e-9) wrong.Add($"{t} forge ×{forge}");
                    if (Math.Abs((FB(t).CombustibleProps?.BurnDuration ?? 0) - 9 * burn) > 1) wrong.Add(t + " block");
                    if (Lang.GetMatchingIfExists("vintagee:item-alchemicalfuel-" + t) == null || F(t).CreativeInventoryTabs?.Contains("vintagee") != true) wrong.Add(t + " name or tab");
                    prev = t;
                }
                Line($"  values: aeternalis {EmcFormat.Format(V(F("aeternalisfuel")))}, magenta {EmcFormat.Format(V(F("magentafuel")))}, white {EmcFormat.Format(V(F("whitefuel")))}");
                Check(wrong.Count == 0, "each fuel is 4 of the one before with the philosopher's stone, worth 4 times as much, and burns 4 times as long (in a forge too), its block 9 times"
                    + (wrong.Count == 0 ? "" : ": " + string.Join(", ", wrong.Take(6))));
            }
            // A settings file from before 1.29.4: its CollectorFuels gets the new fuels once; the file then says so.
            string oldFile = "{ \"CollectorFuels\": [ \"game:charcoal\", \"game:coke\", \"vintagee:alchemicalfuel-alchemicalcoal\", \"vintagee:alchemicalfuel-mobiusfuel\", \"vintagee:alchemicalfuel-aeternalisfuel\" ] }";
            var old = JsonConvert.DeserializeObject<VintageEConfig>(oldFile);
            old.Normalize();
            string saved = JsonConvert.SerializeObject(old);
            bool once = old.CollectorFuels.Count == old.CollectorFuels.Distinct().Count();
            Check(VintageEConfig.ExpansionFuels.All(old.CollectorFuels.Contains) && once && saved.Contains("\"CollectorFuelsVersion\":1"),
                $"a server's saved collector fuels get the new fuels, each fuel once ({old.CollectorFuels.Count} fuels; the file remembers it)");
            var reloaded = JsonConvert.DeserializeObject<VintageEConfig>(saved);
            reloaded.Normalize();
            Check(reloaded.CollectorFuels.Count == old.CollectorFuels.Count, $"saved and loaded again, the fuel list doesn't grow ({old.CollectorFuels.Count} then {reloaded.CollectorFuels.Count})");

            // The Storm Collector.
            Block S(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "stormcollector-mk" + mk));
            var missing = Enumerable.Range(1, 3).Where(mk => S(mk) == null).ToList();
            Check(missing.Count == 0, "Storm Collectors MK1-3 are there" + (missing.Count == 0 ? "" : ": missing " + string.Join(", ", missing)));
            if (missing.Count > 0) return;
            bool recipes = grid.Any(r => r.Output?.Code?.ToString() == "vintagee:stormcollector-mk1" && r.ResolvedIngredients != null
                                         && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == "game:lightningrod")
                                         && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == "vintagee:energycollector-mk1"))
                           && grid.Any(r => r.Output?.Code?.ToString() == "vintagee:stormcollector-mk2" && r.ResolvedIngredients?.Any(i => i?.Code?.ToString() == "vintagee:matter-dark") == true)
                           && grid.Any(r => r.Output?.Code?.ToString() == "vintagee:stormcollector-mk3" && r.ResolvedIngredients?.Any(i => i?.Code?.ToString() == "vintagee:matter-red") == true);
            var sv = Enumerable.Range(1, 3).Select(mk => V(S(mk))).ToList();
            bool named = Enumerable.Range(1, 3).All(mk => Lang.GetMatchingIfExists("vintagee:block-" + S(mk).Code.Path) != null && S(mk).CreativeInventoryTabs?.Contains("vintagee") == true);
            Check(recipes && sv.All(v => v > 0) && sv[1] > sv[0] && sv[2] > sv[1] && named,
                $"Storm Collectors are made from a collector and the game's lightning rod, then dark and red matter; their values climb ({string.Join(", ", sv.Select(EmcFormat.Format))}); named, in the tab");
            double r1 = BEStormCollector.Rate(8, 0.5, false, 3, false, 40, 1), r2 = BEStormCollector.Rate(8, 1, true, 3, false, 40, 1),
                   r3 = BEStormCollector.Rate(8, 0, false, 3, true, 40, 1), r4 = BEStormCollector.Rate(8, 1, true, 3, true, 40, 2), r5 = BEStormCollector.Rate(8, 3, false, 3, false, 40, 1);
            Check(r1 == 4 && r2 == 24 && r3 == 40 && r4 == 128 && r5 == 8,
                $"its rate: half rain {r1}, a thunderstorm {r2}, a temporal storm under a roof {r3}, all at double machine speed {r4}, rain past 1 counts as 1 ({r5})");

            if (ownedSite == null) { Check(false, "a place to put the Storm Collector was set"); return; }
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos sp = ownedSite.AddCopy(2, 0, 4), relayPos = ownedSite.AddCopy(3, 0, 4);
            ba.SetBlock(S(1).Id, sp);
            if (!(ba.GetBlockEntity(sp) is BEStormCollector storm)) { Check(false, $"a Storm Collector has its block entity ({ba.GetBlockEntity(sp)?.GetType().Name ?? "none"})"); return; }
            bool rod = storm.GetBehavior<BEBehaviorAttractsLightning>() != null;
            Line($"  the Storm Collector sees the sky: {storm.UnderSky}; rain {storm.Rain:0.##}, thunderstorm {storm.Thunder}, temporal storm {storm.TemporalStorm}, {EmcFormat.Format(storm.RateNow())} EMC a second now");
            bool struck = WeatherControl.Strike(sapi, sp.ToVec3d().Add(0.5, 1, 0.5));
            decimal fromStrike = storm.Stored;
            decimal again = storm.Struck(); // the same strike reported twice counts once
            decimal want = EmcAmount.From(storm.LightningEmc * mod.Config.MachineSpeedMultiplier);
            Check(rod && struck && storm.Strikes == 1 && fromStrike == want && again == 0,
                $"it's a lightning rod (the game's behaviour: {rod}), and lightning striking it gives {EmcFormat.Total(fromStrike)} EMC once ({storm.Strikes} strike, {EmcFormat.Total(again)} more for the same strike)");
            Block relayBlock = sapi.World.GetBlock(new AssetLocation("vintagee:antimatterrelay-mk1"));
            ba.SetBlock(relayBlock.Id, relayPos);
            var relay = ba.GetBlockEntity(relayPos) as BEAntimatterRelay;
            storm.Tick(1);
            Check(relay != null && relay.Stored > 0 && storm.Stored < fromStrike, $"it feeds a relay next to it ({EmcFormat.Total(relay?.Stored ?? 0)} given, {EmcFormat.Total(storm.Stored)} left)");
            decimal kept = storm.Stored;
            Block next = ItemMatterUpgrader.NextBlock(sapi.World, S(1));
            ba.ExchangeBlock(S(2).Id, sp);
            Check(next == S(2) && storm.EmcPerSecond == S(2).Attributes["emcPerSecond"].AsDouble() && storm.Capacity == EmcAmount.From(S(2).Attributes["capacity"].AsDouble()) && storm.Stored == kept,
                $"the Matter Upgrader takes it to MK2 where it stands, keeping its EMC, at MK2's numbers ({storm.EmcPerSecond}/s, {EmcFormat.Total(storm.Capacity)})");
            ba.SetBlock(0, relayPos);
            ba.SetBlock(0, sp);

            // Burn what I mine around the area actions: with nobody to burn for, an action just runs.
            Check(TabletMining.During<int>(null, () => 5) == 5, "the area actions run as before when nobody is burning what they mine");

            // The New look.
            ProjectELook.Result nl = mod.TestLooks.TryGetValue(ProjectELook.LookNew, out var rn) ? rn : null;
            var newStorm = nl?.Objects?.GetValueOrDefault("vintagee:stormcollector-mk1") as Block;
            Check(newStorm?.Shape?.Base?.ToString() == "vintagee:newlook/block/stormcollector", $"New look: the Storm Collector is the collector's model with a lightning rod ({newStorm?.Shape?.Base})");
        }
    }
}
