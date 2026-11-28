using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// 1.29.0: Project Expansion's tiers (MK4 to MK16 collectors and relays, magenta to fading matter, the Final Star
    /// Shard, the Magnum, Colossal and Gargantuan stars) and the Expansion setting that takes them out. Run with the
    /// setting on (the default) it checks they're all there and follow the curve; run off (the workflow's expansion
    /// input), that none of them are and the rest of the mod is as before.
    /// </summary>
    public partial class SelfTest
    {
        static readonly string[] ExpansionMatters = { "magenta", "pink", "purple", "violet", "blue", "cyan", "green", "lime", "yellow", "orange", "white", "fading" };
        static readonly string[] StarSizes = { "magnum", "colossal", "gargantuan" };
        static readonly string[] StarTiers = { "ein", "zwei", "drei", "vier", "sphere", "omega" };

        /// <summary>Vintage-E's curve: base, x3, x10, x30... (a collector: 4, 12, 40, 120...).</summary>
        static double Curve(int mk, double basis) => basis * Math.Pow(10, (mk - 1) / 2) * ((mk - 1) % 2 == 1 ? 3 : 1);

        void CheckExpansionTiers()
        {
            bool on = mod.Config.Expansion;
            Line($"Project Expansion's tiers (1.29.0): {(on ? "on" : "off")}");
            Check(ExpansionTiers.Error == null && ExpansionTiers.On == on && sapi.World.Config.GetBool(ExpansionTiers.WorldKey, !on) == on,
                $"the Expansion setting reached the start-up switch and the world settings ({ExpansionTiers.On}, {(ExpansionTiers.Error ?? "no error")})");

            // The switch itself, on copies of the files: only the new tiers come out.
            JObject manifest = null;
            try { manifest = JObject.Parse(sapi.Assets.Get(new AssetLocation("vintagee", ExpansionTiers.ManifestFile)).ToText()); }
            catch (Exception e) { Check(false, "the Expansion manifest can be read: " + e.Message); }
            if (manifest != null)
            {
                var files = new Dictionary<string, string>
                {
                    ["blocktypes/energycollector.json"] = "{ \"code\": \"energycollector\", \"variantgroups\": [ { \"code\": \"tier\", \"states\": [ \"mk1\", \"mk2\", \"mk3\", \"mk4\", \"mk16\" ] } ] }",
                    ["itemtypes/matter.json"] = "{ \"code\": \"matter\", \"variantgroups\": [ { \"code\": \"type\", \"states\": [ \"dark\", \"red\", \"magenta\", \"fading\" ] } ] }",
                    ["itemtypes/star.json"] = "{ \"code\": \"star\" }",
                    ["recipes/grid/expansion.json"] = "[ {}, {}, {} ]",
                };
                int removed = ExpansionTiers.Strip(manifest, p => files.TryGetValue(p, out string t) ? t : null, (p, t) => files[p] = t);
                JObject col = JObject.Parse(files["blocktypes/energycollector.json"]), mat = JObject.Parse(files["itemtypes/matter.json"]);
                string colStates = string.Join(",", col["variantgroups"][0]["states"].Values<string>()), matStates = string.Join(",", mat["variantgroups"][0]["states"].Values<string>());
                Check(colStates == "mk1,mk2,mk3" && matStates == "dark,red" && (bool?)JObject.Parse(files["itemtypes/star.json"])["enabled"] == false
                      && files["recipes/grid/expansion.json"] == "[]" && removed == 2 + 2 + 1 + 3,
                    $"switched off, only the new tiers come out: collectors {colStates}, matter {matStates}, stars off, recipes emptied ({removed} entries)");
                Check(ProjectELook.CompanionOf("config/vintagee-new-look.json") == "config/vintagee-new-look-expansion.json", "the looks read the tiers' companion maps");
            }

            Block C(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "energycollector-mk" + mk));
            Block R(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "antimatterrelay-mk" + mk));
            Item M(string m) => sapi.World.GetItem(new AssetLocation("vintagee", "matter-" + m));
            Block MB(string m) => sapi.World.GetBlock(new AssetLocation("vintagee", "matterblock-" + m));
            Item S(string size, string tier) => sapi.World.GetItem(new AssetLocation("vintagee", "star-" + size + "-" + tier));
            Item shard = sapi.World.GetItem(new AssetLocation("vintagee", "finalstarshard"));
            bool baseThere = Enumerable.Range(1, 3).All(mk => C(mk) != null && R(mk) != null) && M("dark") != null && M("red") != null && MB("red") != null
                             && sapi.World.GetItem(new AssetLocation("vintagee", "kleinstar-omega")) != null;
            Check(baseThere, "MK1-3 collectors and relays, dark and red matter and the Klein stars are there either way");

            var expansionOutputs = (sapi.World.GridRecipes ?? new List<GridRecipe>())
                .Where(r => r?.Output?.Code?.Domain == "vintagee" && IsExpansionCode(r.Output.Code.Path)).ToList();

            if (!on)
            {
                var left = new List<string>();
                for (int mk = 4; mk <= 16; mk++) { if (C(mk) != null) left.Add("collector mk" + mk); if (R(mk) != null) left.Add("relay mk" + mk); }
                left.AddRange(ExpansionMatters.Where(m => M(m) != null || MB(m) != null).Select(m => m + " matter"));
                left.AddRange(StarSizes.SelectMany(s => StarTiers.Where(t => S(s, t) != null).Select(t => s + " " + t)));
                if (shard != null) left.Add("final star shard");
                Check(left.Count == 0, "switched off, none of the tiers exist" + (left.Count == 0 ? "" : ": " + string.Join(", ", left.Take(8))));
                Check(expansionOutputs.Count == 0, $"switched off, no recipe makes any of them ({expansionOutputs.Count})");
                Check(ExpansionTiers.Removed > 100, $"switched off, the start-up switch took {ExpansionTiers.Removed} entries out");
                return;
            }

            // On: every tier, on Vintage-E's curve.
            var missing = new List<string>();
            for (int mk = 1; mk <= 16; mk++) { if (C(mk) == null) missing.Add("collector mk" + mk); if (R(mk) == null) missing.Add("relay mk" + mk); }
            missing.AddRange(ExpansionMatters.Where(m => M(m) == null).Select(m => m + " matter"));
            missing.AddRange(ExpansionMatters.Where(m => MB(m) == null).Select(m => m + " matter block"));
            missing.AddRange(StarSizes.SelectMany(s => StarTiers.Where(t => S(s, t) == null).Select(t => s + " star " + t)));
            if (shard == null) missing.Add("final star shard");
            Check(missing.Count == 0, "every tier is there: collectors and relays MK1-16, 12 matters and their blocks, 18 stars, the Final Star Shard" + (missing.Count == 0 ? "" : " (missing " + string.Join(", ", missing.Take(8)) + ")"));
            if (missing.Count > 0) return;

            var offCurve = new List<string>();
            for (int mk = 1; mk <= 16; mk++)
            {
                double rate = C(mk).Attributes["emcPerSecond"].AsDouble(), cap = C(mk).Attributes["capacity"].AsDouble();
                double bonus = R(mk).Attributes["bonusPerSecond"].AsDouble(), transfer = R(mk).Attributes["transferPerSecond"].AsDouble(), rcap = R(mk).Attributes["capacity"].AsDouble();
                if (rate != Curve(mk, 4)) offCurve.Add($"collector mk{mk} {rate}/s");
                if (mk >= 3 && cap != 1500 * rate) offCurve.Add($"collector mk{mk} stores {cap}");
                if (bonus != Curve(mk, 1) || transfer != Curve(mk, 64)) offCurve.Add($"relay mk{mk} {bonus}/{transfer}");
                if (mk >= 3 && rcap != 15625 * transfer) offCurve.Add($"relay mk{mk} stores {rcap}");
            }
            Check(offCurve.Count == 0, $"collectors and relays follow the curve (MK16: {EmcFormat.Format(C(16).Attributes["emcPerSecond"].AsDouble())} EMC/s, relay {EmcFormat.Format(R(16).Attributes["transferPerSecond"].AsDouble())}/s)"
                + (offCurve.Count == 0 ? "" : ": " + string.Join(", ", offCurve.Take(6))));

            var starsWrong = new List<string>();
            double want = 204_800_000;
            foreach (string size in StarSizes)
            {
                foreach (string tier in StarTiers)
                {
                    Item s = S(size, tier);
                    double cap = (s as IEmcStorageItem)?.GetCapacity(new ItemStack(s)) ?? -1;
                    if (!(s is ItemKleinStar) || cap != want) starsWrong.Add($"{size} {tier}: {cap}");
                    want *= 4;
                }
            }
            Check(starsWrong.Count == 0, "the 18 stars hold 204.8 million x 4 each, up to " + EmcFormat.Format(want / 4) + (starsWrong.Count == 0 ? "" : ": " + string.Join(", ", starsWrong.Take(4))));

            // Made from the tier before.
            bool Makes(int mk, string kind, string prev, string matter)
            {
                return (sapi.World.GridRecipes ?? new List<GridRecipe>()).Any(r => r.Output?.Code?.ToString() == $"vintagee:{kind}-mk{mk}"
                    && r.ResolvedIngredients != null && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == prev)
                    && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == matter));
            }
            var noRecipe = new List<string>();
            for (int mk = 4; mk <= 16; mk++)
            {
                string matter = mk == 16 ? "vintagee:finalstarshard" : "vintagee:matter-" + ExpansionMatters[mk - 4];
                if (!Makes(mk, "energycollector", $"vintagee:energycollector-mk{mk - 1}", matter)) noRecipe.Add("collector mk" + mk);
                if (!Makes(mk, "antimatterrelay", $"vintagee:antimatterrelay-mk{mk - 1}", matter)) noRecipe.Add("relay mk" + mk);
            }
            Check(noRecipe.Count == 0, "each collector and relay is made from the one before and its tier's matter (MK16: the Final Star Shard)" + (noRecipe.Count == 0 ? "" : ": not " + string.Join(", ", noRecipe)));

            // Their values follow from the recipes, and climb.
            double V(CollectibleObject c) => c == null ? 0 : mod.Emc.GetValue(c);
            var matterValues = new[] { "red" }.Concat(ExpansionMatters).Select(m => V(M(m))).ToList();
            var collectorValues = Enumerable.Range(1, 16).Select(mk => V(C(mk))).ToList();
            var relayValues = Enumerable.Range(1, 16).Select(mk => V(R(mk))).ToList();
            var starValues = new[] { V(sapi.World.GetItem(new AssetLocation("vintagee", "kleinstar-omega"))) }.Concat(StarSizes.SelectMany(s => StarTiers.Select(t => V(S(s, t))))).ToList();
            bool Climbs(List<double> v) => v.All(x => x > 0) && v.Zip(v.Skip(1), (a, b) => b > a).All(x => x);
            Line("  matter values: " + string.Join(", ", new[] { "red" }.Concat(ExpansionMatters).Zip(matterValues, (m, v) => m + " " + EmcFormat.Format(v))));
            Line($"  final star shard {EmcFormat.Format(V(shard))}; collector MK16 {EmcFormat.Format(collectorValues[15])}; relay MK16 {EmcFormat.Format(relayValues[15])}; gargantuan star omega {EmcFormat.Format(starValues.Last())}");
            Check(Climbs(matterValues) && V(shard) > 0, "every matter has a value from its recipe, each more than the one before, and so has the Final Star Shard");
            Check(Climbs(collectorValues) && Climbs(relayValues), "every collector and relay has a value, each more than the tier before");
            double ratio = starValues[0] > 0 ? starValues[1] / starValues[0] : 0;
            Check(Climbs(starValues) && ratio > 3.9 && ratio < 4.1, $"every star is worth more than the one before, Magnum Ein about four Klein Star Omegas (x{ratio.ToString("0.###", CultureInfo.InvariantCulture)})");

            // Four stars into the next keep what they held.
            GridRecipe magnumRecipe = sapi.World.GridRecipes.FirstOrDefault(r => r.Output?.Code?.ToString() == "vintagee:star-magnum-ein");
            Item klein = sapi.World.GetItem(new AssetLocation("vintagee", "kleinstar-omega"));
            if (magnumRecipe != null && klein != null)
            {
                decimal Craft(decimal each)
                {
                    var ins = Enumerable.Range(0, 4).Select(_ => { var st = new ItemStack(klein); StoredEmc.Set(st, each); return (ItemSlot)new DummySlot(st); }).ToArray();
                    var outSlot = new DummySlot(new ItemStack(S("magnum", "ein")));
                    S("magnum", "ein").OnCreatedByCrafting(ins, outSlot, magnumRecipe);
                    return StoredEmc.Get(outSlot.Itemstack);
                }
                decimal part = Craft(12_800_000.25m), full = Craft(51_200_000m);
                Check(part == 51_200_001m && full == 204_800_000m,
                    $"four Klein Star Omegas make a Magnum Star Ein holding all they held ({EmcFormat.Total(part)} from four quarter-full, {EmcFormat.Total(full)} from four full)");
            }
            else Check(false, "the Magnum Star Ein recipe and the Klein Star Omega are there");

            // Names, the creative tab, the worn stars, and the looks.
            var unnamed = new List<string>();
            foreach (CollectibleObject c in Enumerable.Range(4, 13).SelectMany(mk => new CollectibleObject[] { C(mk), R(mk) })
                         .Concat(ExpansionMatters.SelectMany(m => new CollectibleObject[] { M(m), MB(m) }))
                         .Concat(StarSizes.SelectMany(s => StarTiers.Select(t => (CollectibleObject)S(s, t)))).Append(shard))
            {
                string kind = c is Block ? "block" : "item";
                if (Lang.GetMatchingIfExists("vintagee:" + kind + "-" + c.Code.Path) == null) unnamed.Add(c.Code.Path + " (name)");
                if (c.CreativeInventoryTabs == null || !c.CreativeInventoryTabs.Contains("vintagee")) unnamed.Add(c.Code.Path + " (creative tab)");
            }
            Check(unnamed.Count == 0, "every new block and item has a name and is in Vintage-E's creative tab" + (unnamed.Count == 0 ? "" : ": " + string.Join(", ", unnamed.Take(6))));
            var noWorn = StarSizes.SelectMany(s => StarTiers.Select(t => "star-" + s + "-" + t)).Where(c => !sapi.Assets.Exists(new AssetLocation("vintagee", "shapes/entity/worn/" + c + ".json"))).ToList();
            Check(noWorn.Count == 0, "every star can be worn (its emblem model is there)" + (noWorn.Count == 0 ? "" : ": " + string.Join(", ", noWorn.Take(4))));
            ProjectELook.Result orig = mod.TestLooks.TryGetValue(ProjectELook.LookOriginal, out var ro) ? ro : null;
            string Swap(string key) => orig != null && orig.Swapped.TryGetValue(key, out string v) ? v : null;
            Check(Swap("vintagee:energycollector-mk7#up") == "vintagee:projecte/expansion/collector-violet" && Swap("vintagee:antimatterrelay-mk16#down") == "vintagee:projecte/expansion/relay-final",
                $"Original look: the tiers wear Project Expansion's textures ({Swap("vintagee:energycollector-mk7#up") ?? "unchanged"}, {Swap("vintagee:antimatterrelay-mk16#down") ?? "unchanged"})");
            ProjectELook.Result nl = mod.TestLooks.TryGetValue(ProjectELook.LookNew, out var rn) ? rn : null;
            var newCollector = nl?.Objects?.GetValueOrDefault("vintagee:energycollector-mk12") as Block;
            var newStar = nl?.Objects?.GetValueOrDefault("vintagee:star-colossal-vier") as Item;
            string NewSwap(string key) => nl != null && nl.Swapped.TryGetValue(key, out string v) ? v : null;
            Check(NewSwap("vintagee:energycollector-mk8#glow") == "game:block/stone/gem/sapphire" && NewSwap("vintagee:antimatterrelay-mk16#glow") == "game:block/stone/gem/diamond"
                  && NewSwap("vintagee:energycollector-mk4#glow") == "vintagee:newlook/material/magentamatter",
                $"New look: the tiers glow with the game's gems where it has one of their colour, as MK1-3 do ({NewSwap("vintagee:energycollector-mk8#glow") ?? "unchanged"}, {NewSwap("vintagee:antimatterrelay-mk16#glow") ?? "unchanged"}; magenta: {NewSwap("vintagee:energycollector-mk4#glow") ?? "unchanged"})");
            Check(newCollector?.Shape?.Base?.ToString() == "vintagee:newlook/block/energycollector" && newStar?.Shape?.Base?.ToString() == "vintagee:newlook/item/star",
                $"New look: the new collectors and stars get the same models as MK1-3 and the Klein stars ({newCollector?.Shape?.Base}, {newStar?.Shape?.Base})");
        }

        static bool IsExpansionCode(string path)
        {
            if (path == "finalstarshard" || path.StartsWith("star-", StringComparison.Ordinal)) return true;
            foreach (string prefix in new[] { "energycollector-mk", "antimatterrelay-mk" })
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(path.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mk) && mk >= 4) return true;
            }
            return ExpansionMatters.Any(m => path == "matter-" + m || path == "matterblock-" + m);
        }
    }
}
