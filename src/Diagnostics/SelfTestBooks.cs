using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// 1.29.3: Project Expansion's Knowledge Sharing Book and Alchemical Books, and "Burn what I mine" on the tablet. The
    /// server test has no player, so what needs one (reading a book, teleporting, breaking a block) is checked through the
    /// pieces it's built from: what a book teaches, what a trip costs, how places are kept, the tablet's filter and hook.
    /// </summary>
    public partial class SelfTest
    {
        void CheckBooks()
        {
            bool on = mod.Config.Expansion;
            Line($"Knowledge Sharing Book, Alchemical Books, Burn what I mine (1.29.3): Expansion {(on ? "on" : "off")}");
            Item kb = sapi.World.GetItem(new AssetLocation("vintagee:knowledgebook"));
            Item B(string tier) => sapi.World.GetItem(new AssetLocation("vintagee:alchemicalbook-" + tier));
            string[] tiers = { "basic", "advanced", "master", "arcane" };

            // What's there: the Knowledge Sharing Book and the Basic book always, the others with the tiers.
            var missing = new List<string>();
            if (!(kb is ItemKnowledgeBook)) missing.Add("knowledge book");
            foreach (string t in tiers) if ((t == "basic" || on) && !(B(t) is ItemAlchemicalBook)) missing.Add(t + " book");
            var extra = on ? new List<string>() : tiers.Skip(1).Where(t => B(t) != null).ToList();
            Check(missing.Count == 0 && extra.Count == 0, $"the Knowledge Sharing Book and the Basic Alchemical Book are there{(on ? ", and the Advanced, Master and Arcane books" : "; the others aren't, with the tiers off")}"
                + (missing.Count + extra.Count == 0 ? "" : ": missing " + string.Join(", ", missing) + " extra " + string.Join(", ", extra)));
            if (missing.Count > 0) return;

            // Recipes: one Knowledge Sharing Book recipe, with violet matter (red with the tiers off).
            List<GridRecipe> grid = sapi.World.GridRecipes ?? new List<GridRecipe>();
            var kbRecipes = grid.Where(r => r.Output?.Code?.ToString() == "vintagee:knowledgebook").ToList();
            string matter = on ? "vintagee:matter-violet" : "vintagee:matter-red";
            bool kbRight = kbRecipes.Count == 1 && kbRecipes[0].ResolvedIngredients.Any(i => i?.Code?.ToString() == matter)
                           && kbRecipes[0].ResolvedIngredients.Any(i => i?.Code?.ToString() == "game:gear-temporal" && i.Quantity == 8);
            Check(kbRight, $"one Knowledge Sharing Book recipe, with {matter} and 8 temporal gears for each nether star ({kbRecipes.Count} recipes)");
            bool Makes(string output, params string[] needs) => grid.Any(r => r.Output?.Code?.ToString() == output && r.ResolvedIngredients != null
                && needs.All(n => r.ResolvedIngredients.Any(i => i?.Code?.ToString() == n)));
            var noRecipe = new List<string>();
            if (!Makes("vintagee:alchemicalbook-basic", "vintagee:philosophersstone", "vintagee:matter-red", "game:gear-temporal", "vintagee:covalencedust-high")) noRecipe.Add("basic");
            if (on)
            {
                if (!Makes("vintagee:alchemicalbook-advanced", "vintagee:alchemicalbook-basic", "vintagee:star-magnum-ein", "vintagee:star-magnum-omega")) noRecipe.Add("advanced");
                if (!Makes("vintagee:alchemicalbook-master", "vintagee:alchemicalbook-advanced", "vintagee:star-colossal-ein", "vintagee:star-colossal-omega")) noRecipe.Add("master");
                if (!Makes("vintagee:alchemicalbook-arcane", "vintagee:alchemicalbook-master", "vintagee:finalstar", "vintagee:finalstarshard")) noRecipe.Add("arcane");
            }
            Check(noRecipe.Count == 0, "Project Expansion's book recipes: the Basic book with the philosopher's stone and a temporal gear for the ender pearl"
                + (on ? ", each better book from the one before and the bigger stars" : "") + (noRecipe.Count == 0 ? "" : ": not " + string.Join(", ", noRecipe)));
            double V(CollectibleObject c) => c == null ? 0 : mod.Emc.GetValue(c);
            var values = tiers.Where(t => t == "basic" || on).Select(t => V(B(t))).ToList();
            Line($"  values: knowledge book {EmcFormat.Format(V(kb))}; " + string.Join(", ", tiers.Zip(values, (t, v) => t + " " + EmcFormat.Format(v))));
            Check(V(kb) > 0 && values.All(v => v > 0) && values.Zip(values.Skip(1), (a, b) => b > a).All(x => x), "the books have values from their recipes, each Alchemical Book more than the one before");

            // Names, the creative tab, and Project Expansion's prices.
            IEnumerable<CollectibleObject> all = new CollectibleObject[] { kb }.Concat(tiers.Select(B).Where(b => b != null));
            var unnamed = all.Where(c => Lang.GetMatchingIfExists("vintagee:item-" + c.Code.Path) == null || c.CreativeInventoryTabs?.Contains("vintagee") != true).Select(c => c.Code.Path).ToList();
            Check(unnamed.Count == 0, "the books have names and are in Vintage-E's creative tab" + (unnamed.Count == 0 ? "" : ": " + string.Join(", ", unnamed)));
            double[] prices = { 1000, 500, 100, 0 };
            var wrong = tiers.Select((t, i) => (t, i)).Where(p => B(p.t) is ItemAlchemicalBook b && (b.EmcPerBlock != prices[p.i] || b.CanBind != (p.t != "basic"))).Select(p => p.t).ToList();
            Check(wrong.Count == 0, "the books charge Project Expansion's 1,000, 500, 100 and 0 EMC a block, and all but the Basic can be bound" + (wrong.Count == 0 ? "" : ": " + string.Join(", ", wrong)));

            // What a trip costs: the distance rounded up, times the rate (and the server's multiplier); free for the Arcane book.
            decimal five = AlchemicalBooks.Cost(1000, 0, 64, 0, 3, 68, 0), half = AlchemicalBooks.Cost(1000, 0, 0, 0, 0.5, 0, 0), free = AlchemicalBooks.Cost(0, 0, 0, 0, 1000, 0, 0);
            double before = mod.Config.AlchemicalBookCostMultiplier;
            mod.Config.AlchemicalBookCostMultiplier = 2;
            double doubled = AlchemicalBooks.PerBlock(mod, null, new ItemStack(B("basic")));
            mod.Config.AlchemicalBookCostMultiplier = before;
            Check(five == 5000 && half == 500 && free == 0 && Math.Abs(doubled - 2000) < 1e-9,
                $"a trip costs the distance times the rate: 5 blocks on the Basic book {EmcFormat.Total(five)}, half a block {EmcFormat.Total(half)}, the Arcane book {free}; AlchemicalBookCostMultiplier ×2 makes it {doubled} a block");

            // How places are kept: as JSON, in the book or with the player, names found whatever their case.
            var places = new AlchemicalPlaces();
            places.Places.Add(new AlchemicalPlace { Name = "Home", X = 512000.5, Y = 120, Z = 512000.5 });
            places.Places.Add(new AlchemicalPlace { Name = "Mine", X = 512100, Y = 40.25, Z = 511900, Dim = 0 });
            places.Back = new AlchemicalPlace { Name = "", X = 1, Y = 2, Z = 3 };
            var back = AlchemicalPlaces.Parse(places.ToJson());
            Check(back.Places.Count == 2 && back.Find("home")?.X == 512000.5 && back.Find("MINE")?.Y == 40.25 && back.Back?.Z == 3 && AlchemicalPlaces.Parse("not json").Places.Count == 0,
                "places are kept as JSON and read back exactly, found by name whatever its case; a damaged list reads as empty");
            Check(!AlchemicalBooks.StormNow(sapi) || mod.Config.AlchemicalBookInStorms == false, $"the storm check answers ({(AlchemicalBooks.StormNow(sapi) ? "a storm is on" : "no storm")}; AlchemicalBookInStorms {mod.Config.AlchemicalBookInStorms})");

            // What a Knowledge Sharing Book teaches: what the reader doesn't know and is still in the game, exact versions up to the limit.
            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item water = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            string granite = PlayerEmc.KeyOf(sapi.World.GetBlock(new AssetLocation("game:rock-granite")));
            string flint = PlayerEmc.KeyOf(sapi.World.GetItem(new AssetLocation("game:flint")));
            string exact = null;
            if (bucket != null && water != null)
            {
                var full = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(water, 1000));
                full.Attributes["contents"] = contents;
                exact = EmcCommands.KnowledgeKey(mod, full);
            }
            string[] written = { granite, flint, "i:nosuchmod:thing", exact };
            List<string> learn = ItemKnowledgeBook.Plan(sapi.World, written, new[] { granite }, 0, 10, out int skipped);
            List<string> capped = ItemKnowledgeBook.Plan(sapi.World, written, Array.Empty<string>(), 5, 5, out int skippedAtLimit);
            Check(PlayerEmc.IsExact(exact) && PlayerEmc.CountsAsExact(sapi.World, exact) && learn.SequenceEqual(new[] { flint, exact }) && skipped == 0
                  && capped.SequenceEqual(new[] { granite, flint }) && skippedAtLimit == 1,
                $"a Knowledge Sharing Book teaches what the reader doesn't know and is still in the game ({learn.Count}: {string.Join(", ", learn.Select(k => k.Length > 40 ? k.Substring(0, 40) + "…" : k))}), and leaves out exact versions past the limit ({skippedAtLimit})");
            // The book carries what's written in it, compressed: read back exactly, even a big list, and small.
            string[] many = sapi.World.Collectibles.Where(c => c?.Code != null).Select(PlayerEmc.KeyOf).Where(k => k != null).Append(exact).ToArray();
            byte[] packed = ItemKnowledgeBook.Pack(many);
            string[] unpacked = ItemKnowledgeBook.Unpack(packed);
            int raw = System.Text.Encoding.UTF8.GetByteCount(string.Join("\n", many));
            int firstDiff = unpacked == null ? -1 : Enumerable.Range(0, Math.Min(unpacked.Length, many.Length)).FirstOrDefault(i => unpacked[i] != many[i], -1);
            string diff = unpacked == null ? "nothing read back" : firstDiff >= 0 ? $"first difference at {firstDiff}: [{many[firstDiff]}] read as [{unpacked[firstDiff]}]"
                : unpacked.Length != many.Length ? $"{unpacked.Length} read back" : "all the same";
            Check(unpacked != null && unpacked.SequenceEqual(many), $"a book carries what's written in it, read back exactly: {many.Length} things ({diff})");
            Check(packed.Length < raw / 3, $"what a book carries is compressed: {packed.Length:#,0} bytes for {raw:#,0}");
            string[] empty = ItemKnowledgeBook.Unpack(ItemKnowledgeBook.Pack(Array.Empty<string>()));
            string[] damaged = ItemKnowledgeBook.Unpack(new byte[] { 1, 2, 3 });
            string[] cut = ItemKnowledgeBook.Unpack(packed.Take(packed.Length / 2).ToArray());
            Check(ItemKnowledgeBook.Unpack(null) == null && damaged == null && empty?.Length == 0 && cut == null,
                $"a damaged book reads as nothing, an empty one as empty (null: {ItemKnowledgeBook.Unpack(null)?.Length.ToString() ?? "nothing"}, damaged: {damaged?.Length.ToString() ?? "nothing"}, empty: {empty?.Length.ToString() ?? "nothing"}, cut in half: {cut?.Length.ToString() ?? "nothing"})");
            Check(!mod.Config.KnowledgeBookConsumed && mod.Emc.KnowledgeBookConsumed == mod.Config.KnowledgeBookConsumed && mod.Emc.MaxExactKnowledge == mod.Config.MaxExactKnowledge,
                $"a Knowledge Sharing Book isn't used up by default (KnowledgeBookConsumed {mod.Config.KnowledgeBookConsumed}), and players' games are told that and the exact-knowledge limit ({mod.Emc.MaxExactKnowledge})");

            // A copy made from EMC is a blank book, and a book's writing doesn't change what it's learned as.
            var written1 = new ItemStack(kb);
            written1.Attributes.SetString(ItemKnowledgeBook.OwnerKey, "someone");
            written1.Attributes.SetString(ItemKnowledgeBook.OwnerNameKey, "Someone");
            written1.Attributes.SetInt(ItemKnowledgeBook.CountKey, 12);
            written1.Attributes.SetBytes(ItemKnowledgeBook.KeysKey, packed);
            var bound = new ItemStack(B("basic"));
            bound.Attributes.SetString(ItemAlchemicalBook.PlacesKey, places.ToJson());
            Check(EmcCommands.KnowledgeKey(mod, written1) == PlayerEmc.KeyOf(kb) && EmcCommands.KnowledgeKey(mod, bound) == PlayerEmc.KeyOf(B("basic"))
                  && Math.Abs(mod.Emc.GetValue(written1) - V(kb)) < 1e-9,
                "a written book and a book with places are learned and valued as the plain book (copies made from EMC are blank)");

            // Burn what I mine: its hook, its switch and its filter.
            Item tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
            var tablet = new ItemStack(tabletItem);
            bool offFirst = !TabletMining.On(tablet);
            tablet.Attributes.SetBool(TabletMining.Key, true);
            Block graniteBlock = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            Item flintItem = sapi.World.GetItem(new AssetLocation("game:flint"));
            bool everything = TabletMining.Wants(tablet, graniteBlock, sapi.World) && TabletMining.Wants(tablet, flintItem, sapi.World);
            (tablet.Attributes as TreeAttribute)?.SetStringArray(TabletMagnet.FilterKey, new[] { granite, "", "", "", "", "", "", "", "" });
            bool only = TabletMining.Wants(tablet, graniteBlock, sapi.World) && !TabletMining.Wants(tablet, flintItem, sapi.World);
            tablet.Attributes.SetBool(TabletMagnet.ExcludeKey, true);
            bool except = !TabletMining.Wants(tablet, graniteBlock, sapi.World) && TabletMining.Wants(tablet, flintItem, sapi.World);
            Check(TabletMining.Patched && offFirst && TabletMining.On(tablet) && everything && only && except,
                $"Burn what I mine: the container-spill hook is in place ({TabletMining.Error ?? "no error"}), it's off on a new tablet, and it follows the magnet's filter (empty: everything; only these; all except these)");

            // The looks.
            ProjectELook.Result orig = mod.TestLooks.TryGetValue(ProjectELook.LookOriginal, out var ro) ? ro : null;
            ProjectELook.Result nl = mod.TestLooks.TryGetValue(ProjectELook.LookNew, out var rn) ? rn : null;
            string Swap(ProjectELook.Result r, string key) => r != null && r.Swapped.TryGetValue(key, out string v) ? v : null;
            var newBook = nl?.Objects?.GetValueOrDefault("vintagee:alchemicalbook-basic") as Item;
            Check(Swap(orig, "vintagee:knowledgebook#all") == "vintagee:projecte/expansion/book-knowledgebook" || Swap(orig, "vintagee:knowledgebook#base") == "vintagee:projecte/expansion/book-knowledgebook"
                  || orig?.Swapped?.Values.Contains("vintagee:projecte/expansion/book-knowledgebook") == true,
                $"Original look: the books wear Project Expansion's sprites");
            Check(newBook?.Shape?.Base?.ToString() == "vintagee:newlook/item/book" && Swap(nl, "vintagee:alchemicalbook-basic#leather") == "game:block/leather/green",
                $"New look: the books are the Tome's book in the game's leathers ({newBook?.Shape?.Base}, {Swap(nl, "vintagee:alchemicalbook-basic#leather") ?? "unchanged"})");
        }
    }
}
