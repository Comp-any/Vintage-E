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

        /// <param name="site">A spot in loaded chunks where blocks may be scanned (and ripe crops harvested, still water frozen).</param>
        public static void Run(VintageEModSystem mod, ICoreServerAPI sapi, BlockPos site, Action<string> line)
        {
            line($"BENCH machine: {Environment.ProcessorCount} cores, calibration {CalibrationMs():0.#} ms (lower is a faster machine)");

            // ---- the mapping, and what a remap costs the game thread
            Try(line, "mapping (whole remap on the game thread)", () =>
            {
                var parts = new List<string>();
                for (int i = 0; i < 2; i++)
                {
                    var sw = Stopwatch.StartNew();
                    EmcMappingReport report = mod.RecalculateEmc("benchmark");
                    sw.Stop();
                    parts.Add($"{report?.Milliseconds} ms mapping, {sw.ElapsedMilliseconds} ms with what follows it ({report?.TimingText()})");
                }
                return string.Join(" | ", parts);
            });
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

            // ---- the table every joining player is sent
            Try(line, "EMC table packet", () =>
            {
                var m = Measure(() => mod.Emc.ToPacket(), 3);
                using var buffer = new MemoryStream();
                ProtoBuf.Serializer.Serialize(buffer, mod.Emc.ToPacket());
                return Show(m) + " to build, " + buffer.Length / 1024 + " KB on the wire";
            });

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
                var sw = Stopwatch.StartNew();
                foreach (GridRecipe r in picked) mod.EmcCrafting.Choose(null, r, known, carried, 1e15);
                double first = sw.Elapsed.TotalMilliseconds;
                var m = Measure(() => { foreach (GridRecipe r in picked) mod.EmcCrafting.Choose(null, r, known, carried, 1e15); }, 1, 3);
                return $"{first:0.#} ms the first time (learned items resolved), then {Show(m)}";
            });
            Try(line, "knowledge on the wire", () =>
            {
                long Bytes(string[] keys)
                {
                    using var ms = new MemoryStream();
                    using var w = new BinaryWriter(ms);
                    new StringArrayAttribute(keys).ToBytes(w);
                    w.Flush();
                    return ms.Length;
                }
                return $"the whole list is {Bytes(known.Take(300).ToArray())} bytes for 300 learned items, {Bytes(known)} bytes for {known.Length}";
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
                return $"a wolf ({hostile}) {Show(Measure(() => mod.Trinkets.IsHostile(wolf), 200000))}; a chicken ({tame}) {Show(Measure(() => mod.Trinkets.IsHostile(hen), 200000))}";
            });
            Try(line, "Harvest scan, radius 5 (a worn band: 605 blocks)", () => Show(Measure(() => mod.Trinkets.Harvest(site, 5, null), 40)));
            Try(line, "Harvest scan, radius 16 (a red pedestal: 5,445 blocks)", () => Show(Measure(() => mod.Trinkets.Harvest(site, 16, null), 8)));
            Try(line, "Freeze scan, radius 3 (a worn Zero Ring)", () => Show(Measure(() => mod.Trinkets.Freeze(site, 3, null), 200)));
            Try(line, "Freeze scan, radius 12 (a red pedestal)", () => Show(Measure(() => mod.Trinkets.Freeze(site, 12, null), 20)));
            Try(line, "Grow, 80 tries in radius 16 (a red pedestal)", () => Show(Measure(() => mod.Trinkets.Grow(site, 16, 80, null, 0), 50)));

            // ---- small things done very often
            Try(line, $"finding the mod system ({sapi.ModLoader.Systems.Count()} mod systems loaded)", () =>
                Show(Measure(() => sapi.ModLoader.GetModSystem<VintageEModSystem>(), 200000)));
            Try(line, "a trinket's Kind", () =>
            {
                var ring = sapi.World.GetItem(new AssetLocation("vintagee:trinket-zeroring")) as ItemEmcTrinket;
                if (ring == null) return "no zero ring";
                return Show(Measure(() => { if (ring.Kind == null) throw new InvalidOperationException(); }, 1000000));
            });
        }
    }
}
