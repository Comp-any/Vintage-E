using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Timings of the mod's own hot paths, run by the self-test (SelfTestOnStart) and written into its report as lines
    /// starting "BENCH". They are how a change that is meant to make something faster is checked: the same line from
    /// the run before and the run after, on the same kind of machine. Each figure is the best of several rounds, after
    /// a warm-up; "B/call" is what one call allocates, which doesn't vary from run to run the way time does.
    /// </summary>
    public static class Benchmarks
    {
        /// <summary>Nanoseconds and bytes allocated per call: the best of <paramref name="rounds"/> rounds, after one warm-up call.</summary>
        public static (double ns, double bytes) Measure(Action action, int iterations, int rounds = 5)
        {
            action();
            double bestNs = double.MaxValue, bestBytes = double.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) action();
                sw.Stop();
                double bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
                bestNs = Math.Min(bestNs, sw.Elapsed.TotalMilliseconds * 1e6 / iterations);
                bestBytes = Math.Min(bestBytes, bytes);
            }
            return (bestNs, bestBytes);
        }

        public static string Time(double ns) =>
            ns >= 1e6 ? (ns / 1e6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ms"
            : ns >= 1e3 ? (ns / 1e3).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " us"
            : ns.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ns";

        public static string Show((double ns, double bytes) m) => Time(m.ns) + ", " + m.bytes.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " B/call";

        /// <summary>
        /// A fixed amount of plain arithmetic, timed: says how fast this machine is, so two runs' timings can be put
        /// side by side (the build machines differ from one run to the next).
        /// </summary>
        public static double CalibrationMs()
        {
            double best = double.MaxValue;
            for (int round = 0; round < 3; round++)
            {
                var sw = Stopwatch.StartNew();
                double sum = 0;
                for (int i = 1; i <= 20_000_000; i++) sum += Math.Sqrt(i) / (i + 1.0);
                sw.Stop();
                if (sum > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        /// <summary>Runs one benchmark; a failure is a line in the report, never a failed self-test.</summary>
        static void Try(Action<string> line, string name, Func<string> run)
        {
            try { line("BENCH " + name + ": " + run()); }
            catch (Exception e) { line("BENCH " + name + ": could not run (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        /// <summary>"before -> after (n times faster)" for one thing measured both ways in the same run.</summary>
        static string Pair((double ns, double bytes) before, (double ns, double bytes) after)
        {
            string times = after.ns > 0 && before.ns / after.ns >= 1.05
                ? (before.ns / after.ns).ToString(before.ns / after.ns >= 10 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture) + " times faster"
                : "no faster";
            return $"before {Show(before)} -> after {Show(after)} ({times})";
        }

        static long WireBytes<T>(T packet)
        {
            using var buffer = new MemoryStream();
            ProtoBuf.Serializer.Serialize(buffer, packet);
            return buffer.Length;
        }

        // The area scans as they were before 1.21.1 (a new position for every block, the questions asked of the block
        // itself every time), kept here so both ways are timed on the same machine in the same run.
        static int OldHarvestScan(IBlockAccessor ba, BlockPos center, int radius)
        {
            int n = 0;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        if (TrinketEffects.IsRipeCrop(ba.GetBlock(pos))) n++;
                    }
            return n;
        }

        static int OldFreezeScan(ICoreServerAPI sapi, BlockPos center, int radius)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block ice = sapi.World.GetBlock(new AssetLocation("game:lakeice"));
            if (ice == null || ice.Id == 0) return 0;
            int n = 0;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    for (int dy = -2; dy <= 0; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (fluid?.Code == null || !fluid.Code.Path.StartsWith("water-still", StringComparison.Ordinal)) continue;
                        if (ba.GetBlock(pos, BlockLayersAccess.Solid)?.Id > 0) continue;
                        n++;
                    }
                }
            return n;
        }

        static readonly Random oldRand = new Random(7);

        static int OldGrowScan(TrinketEffects fx, IBlockAccessor ba, BlockPos center, int radius, int attempts)
        {
            int n = 0;
            for (int i = 0; i < attempts; i++)
            {
                BlockPos pos = center.AddCopy(oldRand.Next(-radius, radius + 1), oldRand.Next(-2, 3), oldRand.Next(-radius, radius + 1));
                if (fx.NextCropStage(ba.GetBlock(pos)) != null) n++;
            }
            return n;
        }

        /// <param name="site">A spot in loaded chunks where blocks may be scanned (and ripe crops harvested, still water frozen).</param>
        public static void Run(VintageEModSystem mod, ICoreServerAPI sapi, BlockPos site, Action<string> line)
        {
            line($"BENCH machine: {Environment.ProcessorCount} cores, calibration {CalibrationMs():0.#} ms (lower is a faster machine)");
            IBlockAccessor ba = sapi.World.BlockAccessor;

            // ---- the mapping, and what a remap costs the game thread
            Try(line, "mapping (a whole remap)", () =>
            {
                var parts = new List<string>();
                for (int i = 0; i < 2; i++)
                {
                    var sw = Stopwatch.StartNew();
                    EmcMappingReport report = mod.RecalculateEmc("benchmark");
                    sw.Stop();
                    // What is left on the game thread when a player starts the remap: putting the result into play.
                    long applying = 0;
                    if (report != null)
                    {
                        foreach (var t in report.Timings)
                        {
                            if (t.Key == "contents and materials table" || (t.Key.StartsWith("(after)", StringComparison.Ordinal) && !t.Key.Contains("DumpValuesOnStart"))) applying += t.Value;
                        }
                    }
                    parts.Add($"{report?.Milliseconds} ms mapping, {sw.ElapsedMilliseconds} ms with what follows it; of that the game thread does {applying} ms when a player starts it ({report?.TimingText()})");
                }
                return string.Join(" | ", parts);
            });
            Try(line, "settings copy for a background remap", () => Show(Measure(() => mod.ConfigSnapshot(), 20)));
            Try(line, "cache fingerprint", () =>
            {
                double best = double.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    EmcCache.Fingerprint(sapi, mod, sapi.Logger);
                    sw.Stop();
                    best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                }
                return best.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ms";
            });
            Try(line, "an ingredient's optional member (asked for every ingredient of every recipe, in the mapping and the fingerprint)", () =>
            {
                object ing = sapi.World.GridRecipes.FirstOrDefault(r => r?.ResolvedIngredients != null && r.ResolvedIngredients.Any(i => i != null))?.ResolvedIngredients.First(i => i != null);
                if (ing == null) return "no ingredient";
                Type t = ing.GetType();
                return Pair(Measure(() => { object v = t.GetField("ConsumeQuantity")?.GetValue(ing) ?? t.GetProperty("ConsumeQuantity")?.GetValue(ing); }, 200000),
                    Measure(() => ConversionCollector.OptionalMember(ing, "ConsumeQuantity"), 200000));
            });

            // ---- the table every joining player is sent
            Try(line, "EMC table packet", () =>
                Pair(Measure(() => mod.Emc.BuildPacket(), 3), Measure(() => mod.Emc.ToPacket(), 1000)) + ", " + WireBytes(mod.Emc.ToPacket()) / 1024 + " KB on the wire");

            // ---- burning and planning
            Item stick = sapi.World.GetItem(new AssetLocation("game:stick"));
            Try(line, "PlanBurn (a stack of sticks)", () =>
            {
                var sticks = new ItemStack(stick, 8);
                return Show(Measure(() => mod.Transmutation.PlanBurn(null, sticks, out _, out _), 20000));
            });

            // Everything a creative player's Tome of Knowledge teaches: the worst case for planning against knowledge.
            string[] known = mod.Emc.All.Keys.Where(c => c?.Code != null && CollectibleIndex.IsListed(c) && mod.Emc.CanTransmute(c)).Select(PlayerEmc.KeyOf).ToArray();
            Try(line, $"Emc Craft planning (25 recipes against {known.Length} learned items)", () =>
            {
                List<GridRecipe> all = sapi.World.GridRecipes.Where(r => r != null && r.Enabled && r.ResolvedIngredients != null && r.Output?.ResolvedItemStack != null).ToList();
                int step = Math.Max(1, all.Count / 25);
                List<GridRecipe> picked = all.Where((r, i) => i % step == 0).Take(25).ToList();
                var carried = new List<ItemSlot>();
                void Plan() { foreach (GridRecipe r in picked) mod.EmcCrafting.Choose(null, r, known, carried, 1e15); }
                Plan(); // the learned items are resolved once per knowledge list, either way
                try
                {
                    mod.EmcCrafting.RememberMatches = false;
                    var before = Measure(Plan, 1, 3);
                    mod.EmcCrafting.RememberMatches = true;
                    var sw = Stopwatch.StartNew();
                    Plan();
                    double first = sw.Elapsed.TotalMilliseconds;
                    return Pair(before, Measure(Plan, 1, 3)) + $"; the first time a recipe's ingredients are met: {first:0.#} ms";
                }
                finally { mod.EmcCrafting.RememberMatches = true; }
            });
            Try(line, "handbook ingredient key (per ingredient on screen, per frame)", () =>
            {
                CraftingRecipeIngredient ing = sapi.World.GridRecipes.FirstOrDefault(r => r?.ResolvedIngredients != null && r.ResolvedIngredients.Any(i => i != null && !i.IsTool))?.ResolvedIngredients.First(i => i != null && !i.IsTool);
                if (ing == null) return "no ingredient";
                return Pair(Measure(() => CollectibleBehaviorEmcTooltip.IngredientKey(ing), 20000), Measure(() => CollectibleBehaviorEmcTooltip.IngredientKeyCached(ing), 200000));
            });
            Try(line, "magnet / gem filter, one item checked", () =>
            {
                CollectibleObject granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite")), andesite = sapi.World.GetBlock(new AssetLocation("game:rock-andesite"));
                Item tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
                if (granite == null || andesite == null || tabletItem == null) return "no tablet or rock";
                var tablet = new ItemStack(tabletItem);
                ((TreeAttribute)tablet.Attributes).SetStringArray(TabletMagnet.FilterKey, new[] { PlayerEmc.KeyOf(granite), PlayerEmc.KeyOf(stick), "", "", "", "", "", "", "" });
                bool same = TabletMagnet.Passes(tablet, granite) == TabletMagnet.Passes(tablet, granite, sapi.World) && TabletMagnet.Passes(tablet, andesite) == TabletMagnet.Passes(tablet, andesite, sapi.World);
                return Pair(Measure(() => TabletMagnet.Passes(tablet, andesite), 200000), Measure(() => TabletMagnet.Passes(tablet, andesite, sapi.World), 200000)) + (same ? "" : " BUT THE ANSWERS DIFFER");
            });

            // ---- what goes over the network about a player's EMC and knowledge
            Try(line, "player EMC and knowledge on the wire", () =>
            {
                long Whole(string[] keys)
                {
                    using var ms = new MemoryStream();
                    using var w = new BinaryWriter(ms);
                    new StringArrayAttribute(keys).ToBytes(w);
                    w.Flush();
                    return ms.Length;
                }
                string[] three = known.Take(300).ToArray();
                long oneKey = WireBytes(new PlayerEmcPacket { Learned = new[] { three.Length > 0 ? three[three.Length - 1] : "i:game:stick" } });
                long balance = WireBytes(new PlayerEmcPacket { HasEmc = true, Emc = 123456.78 });
                return $"learning one item, knowing 300: before {Whole(three)} bytes to every player in view -> after {oneKey} bytes to the learner alone; knowing {known.Length}: before {Whole(known)} bytes -> after {oneKey}; "
                    + $"a change of balance: before an attribute update to every player in view on every change -> after {balance} bytes to the owner, at most once a tick";
            });

            // ---- ticks
            Try(line, "IsHostile", () =>
            {
                Entity Make(string start)
                {
                    EntityProperties type = sapi.World.EntityTypes.FirstOrDefault(t => t?.Code != null && t.Code.Domain == "game" && t.Code.Path.StartsWith(start, StringComparison.Ordinal));
                    if (type == null) return null;
                    Entity e = sapi.World.ClassRegistry.CreateEntity(type);
                    e.Code = type.Code;
                    return e;
                }
                Entity wolf = Make("wolf-"), hen = Make("chicken-");
                if (wolf == null || hen == null) return "no wolf or chicken entity type";
                bool hostile = mod.Trinkets.IsHostile(wolf), tame = mod.Trinkets.IsHostile(hen);
                return $"a wolf ({hostile}): " + Pair(Measure(() => mod.Trinkets.MatchesHostileList(wolf.Code, out _), 200000), Measure(() => mod.Trinkets.IsHostile(wolf), 200000))
                    + $"; a chicken ({tame}): " + Pair(Measure(() => mod.Trinkets.MatchesHostileList(hen.Code, out _), 200000), Measure(() => mod.Trinkets.IsHostile(hen), 200000));
            });
            Try(line, "interdiction torch, one tick (two searches of the area before, one after)", () =>
            {
                Vec3d center = site.ToVec3d().Add(0.5, 0.5, 0.5);
                int around = sapi.World.GetEntitiesAround(center, 40, 40, null).Length;
                return Pair(Measure(() => { mod.Trinkets.Repel(center, 8); mod.Trinkets.RepelProjectiles(center, 8, null); }, 2000), Measure(() => mod.Trinkets.RepelAll(center, 8), 2000)) + $", with {around} creatures and items within 40 blocks";
            });
            Try(line, "Harvest scan, radius 5 (a worn band: 605 blocks)", () => Pair(Measure(() => OldHarvestScan(ba, site, 5), 40), Measure(() => mod.Trinkets.Harvest(site, 5, null), 40)));
            Try(line, "Harvest scan, radius 16 (a red pedestal: 5,445 blocks)", () => Pair(Measure(() => OldHarvestScan(ba, site, 16), 8), Measure(() => mod.Trinkets.Harvest(site, 16, null), 8)));
            Try(line, "Freeze scan, radius 3 (a worn Zero Ring)", () => Pair(Measure(() => OldFreezeScan(sapi, site, 3), 200), Measure(() => mod.Trinkets.Freeze(site, 3, null), 200)));
            Try(line, "Freeze scan, radius 12 (a red pedestal)", () => Pair(Measure(() => OldFreezeScan(sapi, site, 12), 20), Measure(() => mod.Trinkets.Freeze(site, 12, null), 20)));
            Try(line, "Grow, 80 tries in radius 16 (a red pedestal)", () => Pair(Measure(() => OldGrowScan(mod.Trinkets, ba, site, 16, 80), 50), Measure(() => mod.Trinkets.Grow(site, 16, 80, null, 0), 50)));
            Try(line, "pedestal range and power (read several times on every pedestal tick)", () =>
            {
                Block pedestal = sapi.World.Blocks.FirstOrDefault(b => b?.Code != null && b.Code.Domain == "vintagee" && b.Attributes != null && b.Attributes["pedestalRange"].Exists);
                if (pedestal == null) return "no pedestal with a range";
                float kept = pedestal.Attributes["pedestalRange"].AsFloat(1);
                return Pair(Measure(() => { float r = pedestal.Attributes?["pedestalRange"].AsFloat(1) ?? 1; }, 200000), Measure(() => { float r = kept; }, 200000));
            });
            Try(line, "an active trinket's effects (per trinket, per player, twice a second)", () =>
            {
                var ring = sapi.World.GetItem(new AssetLocation("vintagee:trinket-ringofarcana")) as ItemEmcTrinket;
                if (ring == null) return "no Ring of Arcana";
                var stack = new ItemStack(ring);
                var buffer = new string[4];
                return Pair(Measure(() => { foreach (string effect in TrinketEffects.EffectsOf(ring, stack)) { } }, 200000), Measure(() => TrinketEffects.EffectsInto(ring, stack, buffer), 200000));
            });
            Try(line, "a condenser's copy of its target and its price (per condenser, twice a second)", () =>
            {
                var target = new ItemStack(stick, 1);
                return Show(Measure(() => mod.Emc.GetValue(BEEnergyCondenser.CopyOf(mod, target)), 200000)) + " before -> after: kept until the target or the table changes";
            });
            Try(line, "matter furnace: finding the renderer's fields (per furnace on screen, four times a second)", () =>
            {
                Type t = typeof(Vintagestory.GameContent.BlockEntityFirepit);
                return Show(Measure(() => { var a = t.GetField("ContentStack"); var b = t.GetField("contentStackRenderer"); var c = t.GetField("Transform"); }, 200000)) + " before -> after: found once";
            });

            // ---- small things done very often
            Try(line, $"finding the mod system ({sapi.ModLoader.Systems.Count()} mod systems loaded)", () =>
                Pair(Measure(() => sapi.ModLoader.GetModSystem<VintageEModSystem>(), 200000), Measure(() => VintageEModSystem.Of(sapi), 200000)));
            Try(line, "a trinket's Kind", () =>
            {
                var ring = sapi.World.GetItem(new AssetLocation("vintagee:trinket-zeroring")) as ItemEmcTrinket;
                if (ring == null) return "no zero ring";
                return Pair(Measure(() => { string k = ring.Variant?["type"] ?? ""; }, 1000000), Measure(() => { if (ring.Kind == null) throw new InvalidOperationException(); }, 1000000));
            });
        }
    }
}
