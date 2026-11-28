using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// 1.29.2: Project Expansion's Power Flowers, EMC Links, Compact Sun, Matter Upgrader and Final Star. The server test
    /// has no player, so what needs an owner in the game (paying and spending their EMC) is checked as far as the owner
    /// being away: what a flower keeps, what a link takes in and holds.
    /// </summary>
    public partial class SelfTest
    {
        /// <summary>Where these checks place their machines (set beside the parity checks' pads).</summary>
        BlockPos ownedSite;

        void CheckFlowersAndLinks()
        {
            bool on = mod.Config.Expansion;
            Line($"Power Flowers, EMC Links, Compact Sun, Matter Upgrader (1.29.2): Expansion {(on ? "on" : "off")}");
            Block F(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "powerflower-mk" + mk));
            Block L(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "emclink-mk" + mk));
            Block C(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "energycollector-mk" + mk));
            Block R(int mk) => sapi.World.GetBlock(new AssetLocation("vintagee", "antimatterrelay-mk" + mk));
            Block sunBlock = sapi.World.GetBlock(new AssetLocation("vintagee", "compactsun"));
            Item star = sapi.World.GetItem(new AssetLocation("vintagee", "finalstar"));
            Item upgrader = sapi.World.GetItem(new AssetLocation("vintagee", "matterupgrader"));
            int top = on ? 16 : 3;

            // What's there: MK1-3 and the upgrader always, the rest with Expansion on.
            var missing = new List<string>();
            for (int mk = 1; mk <= top; mk++) { if (F(mk) == null) missing.Add("flower mk" + mk); if (L(mk) == null) missing.Add("link mk" + mk); }
            if (!(upgrader is ItemMatterUpgrader)) missing.Add("matter upgrader");
            if (on && !(sunBlock is BlockCompactSun)) missing.Add("compact sun");
            if (on && star == null) missing.Add("final star");
            Check(missing.Count == 0, $"Power Flowers and EMC Links MK1-{top}, the Matter Upgrader{(on ? ", the Compact Sun and the Final Star" : "")} are there"
                + (missing.Count == 0 ? "" : ": missing " + string.Join(", ", missing.Take(8))));
            if (!on)
            {
                var left = Enumerable.Range(4, 13).SelectMany(mk => new[] { F(mk) == null ? null : "flower mk" + mk, L(mk) == null ? null : "link mk" + mk })
                    .Where(s => s != null).ToList();
                if (sunBlock != null) left.Add("compact sun");
                if (star != null) left.Add("final star");
                Check(left.Count == 0, "switched off, flowers and links MK4-16, the Compact Sun and the Final Star don't exist" + (left.Count == 0 ? "" : ": " + string.Join(", ", left.Take(6))));
                Check(ItemMatterUpgrader.NextBlock(sapi.World, C(3)) == null && ItemMatterUpgrader.NextBlock(sapi.World, F(2)) == F(3),
                    "switched off, the Matter Upgrader stops at MK3 and still upgrades MK1-3");
            }
            if (missing.Count > 0) return;

            // Their numbers: a flower is 18 collectors and 30 relay bonuses of its tier; a link Project Expansion's limits.
            var wrong = new List<string>();
            for (int mk = 1; mk <= top; mk++)
            {
                double want = 18 * C(mk).Attributes["emcPerSecond"].AsDouble() + 30 * R(mk).Attributes["bonusPerSecond"].AsDouble();
                double got = F(mk).Attributes["emcPerSecond"].AsDouble();
                if (Math.Abs(got - want) > 1e-6) wrong.Add($"flower mk{mk} {got} (want {want})");
                var a = L(mk).Attributes;
                if (mk == 16)
                {
                    if (!a["unlimited"].AsBool()) wrong.Add("link mk16 has limits");
                }
                else if (a["emcPerSecond"].AsDouble() != Math.Pow(16, mk) || a["itemsPerSecond"].AsDouble() != Math.Pow(2, mk - 1)
                         || a["litresPerSecond"].AsDouble() != 10 * Math.Pow(2, mk - 1) || a["inputSlots"].AsInt() != 3 * mk)
                    wrong.Add($"link mk{mk} {a["emcPerSecond"].AsDouble()}/{a["itemsPerSecond"].AsDouble()}/{a["litresPerSecond"].AsDouble()}/{a["inputSlots"].AsInt()}");
            }
            Check(wrong.Count == 0, $"flowers make 18 collectors and 30 relay bonuses of their tier (MK{top}: {EmcFormat.Format(F(top).Attributes["emcPerSecond"].AsDouble())} EMC/s), links have 16^MK EMC, 2^(MK-1) items, 3 slots a tier"
                + (wrong.Count == 0 ? "" : ": " + string.Join(", ", wrong.Take(5))));

            // Recipes and values.
            List<GridRecipe> grid = sapi.World.GridRecipes ?? new List<GridRecipe>();
            bool Makes(string output, params string[] needs) => grid.Any(r => r.Output?.Code?.ToString() == output && r.ResolvedIngredients != null
                && needs.All(n => r.ResolvedIngredients.Any(i => i?.Code?.ToString() == n)));
            var noRecipe = new List<string>();
            if (!Makes("vintagee:emclink-mk1", "vintagee:transmutationtablet", "vintagee:energycondenser-mk1", "vintagee:covalencedust-high")) noRecipe.Add("link mk1");
            for (int mk = 2; mk <= top; mk++)
            {
                string matter = mk == 2 ? "vintagee:matter-dark" : mk == 3 ? "vintagee:matter-red" : mk == 16 ? "vintagee:finalstarshard" : null;
                if (!Makes($"vintagee:emclink-mk{mk}", $"vintagee:emclink-mk{mk - 1}") || matter != null && !Makes($"vintagee:emclink-mk{mk}", matter)) noRecipe.Add("link mk" + mk);
            }
            for (int mk = 1; mk <= top; mk++)
            {
                if (!Makes($"vintagee:powerflower-mk{mk}", $"vintagee:emclink-mk{mk}", $"vintagee:antimatterrelay-mk{mk}", $"vintagee:energycollector-mk{mk}")) noRecipe.Add("flower mk" + mk);
            }
            if (!Makes("vintagee:matterupgrader", "vintagee:emclink-mk1", "vintagee:powerflower-mk1", "vintagee:matter-dark")) noRecipe.Add("upgrader");
            if (on && !Makes("vintagee:finalstar", "vintagee:powerflower-mk16")) noRecipe.Add("final star");
            if (on && !Makes("vintagee:compactsun", "vintagee:finalstar", "vintagee:finalstarshard", "vintagee:matter-yellow")) noRecipe.Add("compact sun");
            Check(noRecipe.Count == 0, "Project Expansion's recipes: links from the one before and the tier's matter, flowers from the tier's link, relays and 9 collectors, the upgrader"
                + (on ? ", the Final Star from 8 Final flowers, the Compact Sun from Final Stars and shards" : "") + (noRecipe.Count == 0 ? "" : ": not " + string.Join(", ", noRecipe)));
            GridRecipe nine = grid.FirstOrDefault(r => r.Output?.Code?.ToString() == "vintagee:powerflower-mk1");
            int collectorsIn = nine?.ResolvedIngredients?.Where(i => i?.Code?.ToString() == "vintagee:energycollector-mk1").Sum(i => i.Quantity) ?? 0;
            Check(collectorsIn == 18, $"a flower takes two \"compressed collectors\" of 9 collectors each ({collectorsIn})");

            double V(CollectibleObject c) => c == null ? 0 : mod.Emc.GetValue(c);
            var flowers = Enumerable.Range(1, top).Select(mk => V(F(mk))).ToList();
            var links = Enumerable.Range(1, top).Select(mk => V(L(mk))).ToList();
            bool Climbs(List<double> v) => v.All(x => x > 0) && v.Zip(v.Skip(1), (a, b) => b > a).All(x => x);
            Line($"  values: flower MK1 {EmcFormat.Format(flowers[0])}, MK{top} {EmcFormat.Format(flowers.Last())}; link MK1 {EmcFormat.Format(links[0])}, MK{top} {EmcFormat.Format(links.Last())}; upgrader {EmcFormat.Format(V(upgrader))}"
                 + (on ? $"; final star {EmcFormat.Format(V(star))}; compact sun {EmcFormat.Format(V(sunBlock))}" : ""));
            Check(Climbs(flowers) && Climbs(links) && V(upgrader) > 0 && (!on || V(star) > flowers.Last() * 8 && V(sunBlock) > V(star)),
                "every flower and link has a value from its recipe, each more than the tier before" + (on ? "; the Final Star more than its 8 flowers, the Compact Sun more than a Final Star" : ""));

            // Names and the creative tab.
            var unnamed = new List<string>();
            IEnumerable<CollectibleObject> all = Enumerable.Range(1, top).SelectMany(mk => new CollectibleObject[] { F(mk), L(mk) }).Append(upgrader);
            if (on) all = all.Append(sunBlock).Append(star);
            foreach (CollectibleObject c in all)
            {
                string kind = c is Block ? "block" : "item";
                if (Lang.GetMatchingIfExists("vintagee:" + kind + "-" + c.Code.Path) == null) unnamed.Add(c.Code.Path + " (name)");
                if (c.CreativeInventoryTabs == null || !c.CreativeInventoryTabs.Contains("vintagee")) unnamed.Add(c.Code.Path + " (creative tab)");
            }
            Check(unnamed.Count == 0, "they all have names and are in Vintage-E's creative tab" + (unnamed.Count == 0 ? "" : ": " + string.Join(", ", unnamed.Take(6))));

            if (ownedSite == null) { Check(false, "a place to put the machines was set"); return; }
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos p = ownedSite.Copy();
            BlockPos flowerPos = p.AddCopy(0, 1, 0), linkPos = p.AddCopy(3, 0, 0), collectorPos = p.AddCopy(6, 0, 0);

            // A flower with no one in the game keeps what it makes; a Compact Sun under it makes it ten times as much.
            if (on) ba.SetBlock(sunBlock.Id, p);
            ba.SetBlock(F(2).Id, flowerPos);
            var flower = ba.GetBlockEntity(flowerPos) as BEPowerFlower;
            Check(flower != null, $"a Power Flower has its block entity ({ba.GetBlockEntity(flowerPos)?.GetType().Name ?? "none"})");
            if (flower != null)
            {
                flower.SetOwner("vintagee-selftest-owner", "Self-test");
                decimal paid = flower.Produce(1f);
                double each = F(2).Attributes["emcPerSecond"].AsDouble() * (on ? BlockCompactSun.Bonus(sapi) : 1);
                Check(paid == 0 && flower.Stored == EmcTransfer.PerTick(each) && flower.SunBonus == (on ? BlockCompactSun.Bonus(sapi) : 1),
                    $"with its owner away a flower keeps what it makes: {EmcFormat.Total(flower.Stored)} after a second{(on ? $" (×{flower.SunBonus} from the Compact Sun under it)" : "")}");
                double before = mod.Config.PowerFlowerMultiplier;
                mod.Config.PowerFlowerMultiplier = 2;
                double doubled = flower.PerSecond;
                mod.Config.PowerFlowerMultiplier = before;
                Check(Math.Abs(doubled - 2 * each) < 1e-6, $"PowerFlowerMultiplier multiplies it ({EmcFormat.Format(doubled)} at ×2)");

                // Broken, what it kept goes with it, and counts in its value.
                ItemStack[] drops = F(2).GetDrops(sapi.World, flowerPos, null);
                ItemStack drop = drops?.FirstOrDefault(d => d?.Collectible == F(2));
                decimal held = EmcAmount.Read(drop?.Attributes, OwnedMachine.HeldKey);
                Check(drop != null && held == EmcTransfer.PerTick(each) && flower.Stored == 0 && Math.Abs(mod.Emc.GetValue(drop) - (V(F(2)) + (double)held)) < 1e-6,
                    $"broken, a flower's dropped block carries the {EmcFormat.Total(held)} EMC it kept, and is worth that much more");
                flower.Stored = 123.5m;

                // The Matter Upgrader: next tier in place, everything kept, the new tier's rate.
                Block next = ItemMatterUpgrader.NextBlock(sapi.World, F(2));
                decimal cost = ItemMatterUpgrader.Cost(mod, F(2), F(3));
                ba.ExchangeBlock(F(3).Id, flowerPos);
                var after = ba.GetBlockEntity(flowerPos) as BEPowerFlower;
                Check(next == F(3) && cost == EmcAmount.From(V(F(3))) - EmcAmount.From(V(F(2))) && cost > 0 && ReferenceEquals(after, flower)
                      && after.Stored == 123.5m && after.OwnerUid == "vintagee-selftest-owner" && after.BasePerSecond == F(3).Attributes["emcPerSecond"].AsDouble(),
                    $"the Matter Upgrader turns a MK2 flower into MK3 where it stands for the difference in value ({EmcFormat.Total(cost)}), keeping its owner and what it kept, at MK3's rate ({after?.BasePerSecond})");
            }
            Check(ItemMatterUpgrader.NextBlock(sapi.World, F(top)) == null && ItemMatterUpgrader.NextBlock(sapi.World, sapi.World.GetBlock(new AssetLocation("game:rock-granite"))) == null
                  && ItemMatterUpgrader.NextBlock(sapi.World, C(3)) == (on ? C(4) : null) && ItemMatterUpgrader.NextBlock(sapi.World, L(1)) == L(2) && ItemMatterUpgrader.NextBlock(sapi.World, R(2)) == R(3),
                $"the Matter Upgrader knows the next tier of collectors, relays, flowers and links, and stops at MK{top}");

            // A collector: the upgrade reads the new capacity and rate; a Compact Sun on top is full sun ×10.
            ba.SetBlock(C(1).Id, collectorPos);
            if (ba.GetBlockEntity(collectorPos) is BEEnergyCollector col)
            {
                col.Stored = 500;
                ba.ExchangeBlock(C(2).Id, collectorPos);
                Check(col.Stored == 500 && col.Capacity == (decimal)C(2).Attributes["capacity"].AsDouble() && col.RatePerSecond == C(2).Attributes["emcPerSecond"].AsDouble(),
                    $"an upgraded collector keeps its EMC and takes MK2's capacity and rate ({EmcFormat.Total(col.Capacity)}, {col.RatePerSecond}/s)");
                if (on)
                {
                    ba.SetBlock(sunBlock.Id, collectorPos.UpCopy());
                    double rate = col.RateNow(), want = col.RatePerSecond * mod.Config.MachineSpeedMultiplier * BlockCompactSun.Bonus(sapi);
                    Check(Math.Abs(rate - want) < 1e-6, $"a Compact Sun on a collector is full sun ×{BlockCompactSun.Bonus(sapi)} ({EmcFormat.Format(rate)} EMC/s)");
                    ba.SetBlock(0, collectorPos.UpCopy());
                }
            }
            else Check(false, "an Energy Collector has its block entity");

            // An EMC Link: takes in EMC up to its limit a second only once it has an owner, burnable items into its tier's slots.
            ba.SetBlock(L(1).Id, linkPos);
            if (ba.GetBlockEntity(linkPos) is BEEmcLink link)
            {
                decimal none = link.AcceptEmc(1000);
                link.SetOwner("vintagee-selftest-owner", "Self-test");
                decimal first = link.AcceptEmc(1000), second = link.AcceptEmc(1000);
                Check(none == 0 && first == 16 && second == 0 && link.Buffer == 16, $"an EMC Link MK1 takes no EMC without an owner, then 16 EMC a second ({none}, {first}, {second})");
                ItemStack rock = new ItemStack(sapi.World.GetBlock(new AssetLocation("game:rock-granite")), 10);
                var from = new DummySlot(rock);
                ItemSlot into = link.Inventory.GetAutoPushIntoSlot(BlockFacing.UP, from);
                int slot = into == null ? -1 : Enumerable.Range(0, link.Inventory.Count).First(i => link.Inventory[i] == into);
                bool outputShut = !link.Inventory[0].CanHold(from) && !link.Inventory[4].CanHold(from) && link.Inventory[3].CanHold(from);
                Check(slot == 1 && outputShut && link.Inventory.GetAutoPullFromSlot(BlockFacing.DOWN) == null,
                    $"hoppers put burnable items into its first slot (slot {slot}), only into its tier's 3 slots, never into the output, and nothing comes out until it makes something");
                ba.ExchangeBlock(L(2).Id, linkPos);
                Check(link.InputSlots == 6 && link.Inventory[4].CanHold(from) && link.Buffer == 16 && link.OwnerUid == "vintagee-selftest-owner",
                    $"upgraded to MK2 it keeps its owner and EMC and has 6 slots ({link.InputSlots})");
                if (on)
                {
                    ba.ExchangeBlock(L(16).Id, linkPos);
                    decimal lots = link.AcceptEmc(1_000_000_000_000m);
                    Check(link.Unlimited && lots == 1_000_000_000_000m, $"the Final link has no limits (took {EmcFormat.Total(lots)} at once)");
                }
                Item tablet = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
                link.SetTarget(EmcCommands.KnowledgeKey(mod, new ItemStack(tablet)));
                ItemStack made = TransmutationService.Prototype(mod, sapi.World, link.TargetKey);
                Check(made?.Collectible == tablet, $"a link set to an item makes that item ({made?.Collectible?.Code})");
                decimal holding = link.Buffer;
                ItemStack[] drops = L(on ? 16 : 2).GetDrops(sapi.World, linkPos, null);
                decimal carried = EmcAmount.Read(drops?.FirstOrDefault(d => d?.Collectible == L(on ? 16 : 2))?.Attributes, OwnedMachine.HeldKey);
                Check(holding > 0 && carried == holding && link.Buffer == 0, $"broken, a link's dropped block carries the {EmcFormat.Total(carried)} EMC it was holding for its owner");
            }
            else Check(false, "an EMC Link has its block entity");

            // The looks.
            ProjectELook.Result orig = mod.TestLooks.TryGetValue(ProjectELook.LookOriginal, out var ro) ? ro : null;
            string Swap(ProjectELook.Result r, string key) => r != null && r.Swapped.TryGetValue(key, out string v) ? v : null;
            Check(Swap(orig, "vintagee:powerflower-mk2#collector") == "vintagee:projecte/expansion/collector-dark" && Swap(orig, "vintagee:emclink-mk1#up") == "vintagee:projecte/expansion/emclink-basic",
                $"Original look: flowers and links wear Project Expansion's textures ({Swap(orig, "vintagee:powerflower-mk2#collector") ?? "unchanged"}, {Swap(orig, "vintagee:emclink-mk1#up") ?? "unchanged"})");
            ProjectELook.Result nl = mod.TestLooks.TryGetValue(ProjectELook.LookNew, out var rn) ? rn : null;
            var newFlower = nl?.Objects?.GetValueOrDefault("vintagee:powerflower-mk3") as Block;
            var newLink = nl?.Objects?.GetValueOrDefault("vintagee:emclink-mk2") as Block;
            Check(newFlower?.Shape?.Base?.ToString() == "vintagee:newlook/block/powerflower" && newLink?.Shape?.Base?.ToString() == "vintagee:newlook/block/energycondenser"
                  && Swap(nl, "vintagee:powerflower-mk3#glow") == "game:block/stone/gem/ruby" && (!on || Swap(nl, "vintagee:compactsun#core") == "game:block/stone/gem/citrine"),
                $"New look: flowers keep Project Expansion's model in the game's metals and gems, links the condenser's, the Compact Sun glows citrine ({newFlower?.Shape?.Base}, {newLink?.Shape?.Base}, {Swap(nl, "vintagee:powerflower-mk3#glow") ?? "unchanged"}, {Swap(nl, "vintagee:compactsun#core") ?? "unchanged"})");
        }
    }
}
