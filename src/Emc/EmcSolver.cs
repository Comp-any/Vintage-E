using System;
using System.Collections.Generic;
using System.Linq;

namespace VintageE
{
    /// <summary>
    /// The value-propagation core of the EMC mapper, independent of the game so it can be tested on its own.
    ///
    /// Rules (the same spirit as ProjectE's mapper):
    /// <list type="number">
    /// <item>Fixed values (base values, overrides, item attributes) never change.</item>
    /// <item>Every other item gets the <b>cheapest</b> value any conversion produces for it:
    ///   sum(cheapest valued choice per ingredient × quantity) − returned items, divided by output quantity.
    ///   Taking the minimum means crafting something and burning it can never earn more EMC than its ingredients.</item>
    /// <item>If values are still falling after <see cref="MaxPasses"/> passes, the items involved sit in a
    ///   self-cheapening loop (A→2B, B→A). The graph is solved again with those items <i>frozen</i>: each keeps
    ///   the first value it gets and is never lowered after that, which breaks the loop while still giving
    ///   them a sensible value. Only if that still fails are they dropped.</item>
    /// <item>Optionally, a processing step (ore → ingot, raw → dried) whose single input has no value yet
    ///   gives that input a value worked out backwards from its output.</item>
    /// </list>
    /// </summary>
    public sealed class EmcSolver<T> where T : class
    {
        public sealed class Edge
        {
            public T Output;
            public double OutputQuantity;
            public T[][] Choices;
            public double[] Quantities;
            public T[] Returned;
            public double[] ReturnedQuantities;
            /// <summary>By returned item (or null): true = with no value yet it counts as nothing, instead of the edge waiting.</summary>
            public bool[] ReturnedOptional;
            /// <summary>Processing edges may be used backwards to infer an unvalued input from its output.</summary>
            public bool Reversible;
            /// <summary>Flat EMC added per run on top of the ingredients (time bonuses, a machine's power).</summary>
            public double ExtraCost;
            /// <summary>By slot (or null): never work that slot's value out backwards from this edge (a tool's wear).</summary>
            public bool[] NoInfer;
            /// <summary>Caller data (the conversion this edge came from), reported back through <see cref="Via"/>.</summary>
            public object Tag;
        }

        /// <summary>
        /// One input producing several outputs at once. Its value is shared out: outputs that already have a value
        /// keep it, the remainder is split evenly between the ones that don't. If every output is valued but the
        /// input isn't, the input is worth the sum of its outputs.
        /// </summary>
        public sealed class JointEdge
        {
            public T Input;
            public double InputQuantity = 1;
            public T[] Outputs;
            public double[] OutputQuantities;
            /// <summary>
            /// When set, the edge has these ingredient slots instead of <see cref="Input"/> (each slot: the cheapest valued
            /// choice counts), plus <see cref="ExtraCost"/>, less what's handed back: another mod's machine recipe with
            /// several ingredients and several results, none of them the main one. What it all costs is shared out the same
            /// way. Worked backwards only with <see cref="InferFromCrafting"/>, and only into one unknown slot.
            /// </summary>
            public T[][] Choices;
            public double[] Quantities;
            public bool[] NoInfer;
            public double ExtraCost;
            public T[] Returned;
            public double[] ReturnedQuantities;
            public bool[] ReturnedOptional;
            /// <summary>By output (or null for evenly): its weight when what's left is shared out.</summary>
            public double[] ShareWeights;
            public object Tag;
        }

        public const double MinValue = 1e-6;

        public Dictionary<T, double> Fixed = new Dictionary<T, double>();
        public HashSet<T> Excluded = new HashSet<T>();
        /// <summary>Items with no value of their own that cost nothing as ingredients (a pattern or schematic that isn't used up).</summary>
        public HashSet<T> Free = new HashSet<T>();
        public List<Edge> Edges = new List<Edge>();
        public List<JointEdge> JointEdges = new List<JointEdge>();
        public int MaxPasses = 80;
        /// <summary>Forward/infer rounds; each round of backward inference can enable the next.</summary>
        public int MaxRounds = 400;
        public bool InferBackwards = true;
        /// <summary>
        /// Also infer from crafting: when every ingredient of a recipe but one has a value and the output already has one,
        /// the missing ingredient is worth what's left. The highest such value is used, so it never makes anything else cheaper.
        /// </summary>
        public bool InferFromCrafting = true;

        public Dictionary<T, double> Values { get; private set; }
        public Dictionary<T, double> Inferred { get; } = new Dictionary<T, double>();
        /// <summary>The edge (or joint edge) that gave each item its current value; for inferred items, the edge that was worked backwards.</summary>
        public Dictionary<T, object> Via { get; } = new Dictionary<T, object>();
        /// <summary>Items in self-cheapening loops that were given up on entirely (rare).</summary>
        public HashSet<T> Unstable { get; } = new HashSet<T>();
        /// <summary>The first value each item ever got; loop items that can't be settled fall back to it instead of losing their value.</summary>
        readonly Dictionary<T, double> firstValue = new Dictionary<T, double>();
        /// <summary>The value each frozen item settled at (its cheapest recipe outside the loop), kept across restarts.</summary>
        readonly Dictionary<T, double> frozenValue = new Dictionary<T, double>();
        /// <summary>Items that never settled and were given their first value at the end (normally empty).</summary>
        public HashSet<T> KeptAtFirstValue { get; } = new HashSet<T>();
        /// <summary>Items in self-cheapening loops that keep their first value instead of being lowered.</summary>
        public HashSet<T> Frozen { get; } = new HashSet<T>();
        public int TotalPasses { get; private set; }
        /// <summary>How long the last <see cref="Solve"/> spent relaxing and inferring, and how many rounds it took (for the mapping's time breakdown).</summary>
        public long ForwardMs { get; private set; }
        public long InferMs { get; private set; }
        public int Rounds { get; private set; }

        /// <summary>
        /// What a solve works out about its edges before it starts, none of which depends on the values: which edges
        /// use each item, which make it, each edge's ingredient slots without the excluded items (they can never
        /// have a value) and the free slots (they add nothing), and the one-input processing steps. The mapper solves
        /// the same edges eight times or more in a row, so it hands this from one solver to the next
        /// (<see cref="Shared"/>) instead of having each build it again.
        /// </summary>
        public sealed class Graph
        {
            internal Edge[] Edges;
            internal HashSet<T> Excluded, Free;
            internal int ExcludedCount, FreeCount;
            /// <summary>Every item an edge names, numbered: the solver keeps values in arrays by these numbers while it works.</summary>
            internal Dictionary<T, int> Ids;
            internal T[] Items;
            /// <summary>By item number: the edges that use the item (null: none).</summary>
            internal List<int>[] Dependents;
            internal Dictionary<T, List<int>> ByOutput;
            internal int[] OutputIds;
            internal int[][][] Slots;
            internal double[][] Quantities;
            internal int[][] Returned;
            internal double[][] ReturnedQuantities;
            internal bool[][] ReturnedOptional;
            /// <summary>Edges whose cost can never be worked out (a slot only excluded items could fill).</summary>
            internal bool[] Never;
            internal int[] ReversibleSingles;

            /// <summary>Whether it was built for exactly these edges and lists.</summary>
            internal bool Fits(EmcSolver<T> solver)
            {
                if (Edges.Length != solver.Edges.Count || !ReferenceEquals(Excluded, solver.Excluded) || !ReferenceEquals(Free, solver.Free)) return false;
                if (ExcludedCount != solver.Excluded.Count || FreeCount != solver.Free.Count) return false;
                for (int i = 0; i < Edges.Length; i++) if (!ReferenceEquals(Edges[i], solver.Edges[i])) return false;
                return true;
            }
        }

        /// <summary>
        /// The prepared graph. Set it to an earlier solver's to reuse it: it is only used if it was built for the same
        /// edges, excluded and free lists (the same objects, unchanged in size), and built afresh otherwise.
        /// </summary>
        public Graph Shared;

        Graph graph;
        // Which edges the next relaxing has to look at again. An edge that was worked out and none of whose
        // ingredients has changed value since comes out the same, so it is left alone: every round used to start by
        // working out all of them again (56 rounds of 27,700 recipes in a base game mapping). The values that come
        // out are the same, in the same order: only work that changes nothing is skipped.
        bool[] dirty;
        /// <summary>Edges that last came out under the floor: looked at every time, as they name the loop suspects.</summary>
        bool[] floorHit;
        /// <summary>Edges every ingredient slot of which had a value the last time their cost was worked out (so crafting has nothing to infer from them).</summary>
        bool[] slotsKnown;
        bool allDirty;
        // The same facts as Values, Unstable and Frozen, by item number: working an edge out reads these arrays instead
        // of looking every ingredient up in three or four hash tables (with fifty mods that is most of a mapping's
        // time). Values stays the record: every change goes into both, and the arrays are filled from the tables
        // again whenever those are rebuilt.
        double[] val;
        bool[] has, unstable, frozen;
        /// <summary>By edge: its output is fixed or excluded (never worked out).</summary>
        bool[] pinnedOut;
        /// <summary>By item: how often this run of Forward lowered it; and the pass in which it last changed.</summary>
        int[] loweredCount, changedStamp;
        int stamp;

        public void Solve()
        {
            if (Shared == null || !Shared.Fits(this)) Shared = BuildGraph();
            graph = Shared;
            int edgeCount = Edges.Count, itemCount = graph.Items.Length;
            dirty = new bool[edgeCount];
            floorHit = new bool[edgeCount];
            slotsKnown = new bool[edgeCount];
            pinnedOut = new bool[edgeCount];
            for (int i = 0; i < edgeCount; i++) pinnedOut[i] = Fixed.ContainsKey(Edges[i].Output) || Excluded.Contains(Edges[i].Output);
            val = new double[itemCount];
            has = new bool[itemCount];
            unstable = new bool[itemCount];
            frozen = new bool[itemCount];
            loweredCount = new int[itemCount];
            changedStamp = new int[itemCount];
            stamp = 0;
            allDirty = true;
            Values = new Dictionary<T, double>(Fixed);
            FillArrays();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < MaxRounds; round++)
            {
                Rounds++;
                long before = clock.ElapsedMilliseconds;
                bool settled = Forward();
                ForwardMs += clock.ElapsedMilliseconds - before;
                if (!settled)
                {
                    // Loop detected: freeze (or, second time round, drop) the offenders and start over from trusted values.
                    Values = new Dictionary<T, double>(Fixed);
                    foreach (var kv in Inferred) if (!Unstable.Contains(kv.Key)) Values[kv.Key] = kv.Value;
                    foreach (var kv in frozenValue) if (!Unstable.Contains(kv.Key)) Values[kv.Key] = kv.Value;
                    foreach (T k in Via.Keys.Where(k => !Inferred.ContainsKey(k)).ToList()) Via.Remove(k);
                    // Values were taken away: everything is worked out again, and no slot is known to be filled.
                    FillArrays();
                    allDirty = true;
                    Array.Clear(slotsKnown, 0, slotsKnown.Length);
                    continue;
                }
                before = clock.ElapsedMilliseconds;
                int inferred = InferOnce();
                InferMs += clock.ElapsedMilliseconds - before;
                if (inferred == 0) break;
            }

            // Never drop a value outright: anything that wouldn't settle keeps the first value it got, frozen,
            // and one more pass lets everything made from it pick that up.
            if (Unstable.Count > 0)
            {
                foreach (T item in Unstable)
                {
                    if (!firstValue.TryGetValue(item, out double v)) continue;
                    Values[item] = v;
                    Frozen.Add(item);
                    KeptAtFirstValue.Add(item);
                }
                Unstable.ExceptWith(KeptAtFirstValue);
                FillArrays();
                allDirty = true;
                Forward();
            }
        }

        Graph BuildGraph()
        {
            int n = Edges.Count;
            var g = new Graph
            {
                Edges = Edges.ToArray(), Excluded = Excluded, Free = Free, ExcludedCount = Excluded.Count, FreeCount = Free.Count,
                Ids = new Dictionary<T, int>(), ByOutput = new Dictionary<T, List<int>>(), OutputIds = new int[n],
                Slots = new int[n][][], Quantities = new double[n][], Returned = new int[n][], ReturnedQuantities = new double[n][], ReturnedOptional = new bool[n][], Never = new bool[n]
            };
            var items = new List<T>();
            int IdOf(T item)
            {
                if (!g.Ids.TryGetValue(item, out int id))
                {
                    id = items.Count;
                    g.Ids[item] = id;
                    items.Add(item);
                }
                return id;
            }
            var dependents = new Dictionary<T, List<int>>();
            var reversible = new List<int>();
            var slots = new List<int[]>();
            var quantities = new List<double>();
            var members = new List<int>();
            var back = new List<int>();
            var backQuantities = new List<double>();
            for (int i = 0; i < n; i++)
            {
                Edge e = Edges[i];
                // Every item an edge names, excluded ones too: the order edges are worked through in comes from this.
                foreach (T[] choice in e.Choices)
                {
                    foreach (T c in choice) { AddIndexed(dependents, c, i); IdOf(c); }
                }
                if (e.Returned != null) foreach (T r in e.Returned) { AddIndexed(dependents, r, i); IdOf(r); }
                g.OutputIds[i] = IdOf(e.Output);
                AddIndexed(g.ByOutput, e.Output, i);
                if (e.Reversible && e.Choices.Length == 1 && e.Choices[0].Length == 1 && (e.NoInfer == null || !e.NoInfer[0])) reversible.Add(i);

                slots.Clear();
                quantities.Clear();
                bool never = false;
                for (int slot = 0; slot < e.Choices.Length; slot++)
                {
                    T[] choice = e.Choices[slot];
                    double quantity = e.Quantities[slot];
                    bool free = false;
                    members.Clear();
                    foreach (T c in choice)
                    {
                        if (Free.Contains(c)) { free = true; break; }
                        if (!Excluded.Contains(c)) members.Add(g.Ids[c]);
                    }
                    if (free)
                    {
                        // Costs nothing whatever else could fill it: 0 × the quantity, which adds nothing (unless the
                        // quantity is not a number, and then the cost never is one either).
                        if (double.IsNaN(quantity) || double.IsInfinity(quantity)) never = true;
                        continue;
                    }
                    if (members.Count == 0) never = true; // nothing that can ever have a value fills it
                    slots.Add(members.ToArray());
                    quantities.Add(quantity);
                }
                g.Never[i] = never;
                g.Slots[i] = slots.ToArray();
                g.Quantities[i] = quantities.ToArray();

                if (e.Returned != null)
                {
                    back.Clear();
                    backQuantities.Clear();
                    var backOptional = new List<bool>();
                    for (int j = 0; j < e.Returned.Length; j++)
                    {
                        T r = e.Returned[j];
                        if (Excluded.Contains(r) || Free.Contains(r)) continue;
                        back.Add(g.Ids[r]);
                        backQuantities.Add(e.ReturnedQuantities[j]);
                        backOptional.Add(e.ReturnedOptional != null && e.ReturnedOptional[j]);
                    }
                    if (back.Count > 0)
                    {
                        g.Returned[i] = back.ToArray();
                        g.ReturnedQuantities[i] = backQuantities.ToArray();
                        if (backOptional.Contains(true)) g.ReturnedOptional[i] = backOptional.ToArray();
                    }
                }
            }
            g.ReversibleSingles = reversible.ToArray();
            g.Items = items.ToArray();
            g.Dependents = new List<int>[g.Items.Length];
            foreach (var kv in dependents) g.Dependents[g.Ids[kv.Key]] = kv.Value;
            return g;
        }

        static void AddIndexed(Dictionary<T, List<int>> index, T item, int edge)
        {
            if (!index.TryGetValue(item, out var list)) index[item] = list = new List<int>();
            if (list.Count == 0 || list[list.Count - 1] != edge) list.Add(edge);
        }

        /// <summary>The arrays from the tables: after Values, Unstable or Frozen were rebuilt or changed in bulk.</summary>
        void FillArrays()
        {
            Dictionary<T, int> ids = graph.Ids;
            Array.Clear(has, 0, has.Length);
            foreach (var kv in Values)
            {
                if (!ids.TryGetValue(kv.Key, out int id)) continue;
                val[id] = kv.Value;
                has[id] = true;
            }
            Array.Clear(unstable, 0, unstable.Length);
            foreach (T item in Unstable) if (ids.TryGetValue(item, out int id)) unstable[id] = true;
            Array.Clear(frozen, 0, frozen.Length);
            foreach (T item in Frozen) if (ids.TryGetValue(item, out int id)) frozen[id] = true;
        }

        /// <summary>Gives an item a value, in the table and (if an edge names it) the array.</summary>
        void SetValue(T item, double value)
        {
            Values[item] = value;
            if (graph.Ids.TryGetValue(item, out int id))
            {
                val[id] = value;
                has[id] = true;
            }
        }

        /// <summary>An item's value changed: every edge that uses it has to be worked out again.</summary>
        void MarkUsersDirty(int item)
        {
            List<int> deps = graph.Dependents[item];
            if (deps == null) return;
            for (int k = 0; k < deps.Count; k++) dirty[deps[k]] = true;
        }

        /// <summary>Relaxes until nothing changes. Returns false (and fills <see cref="Unstable"/>) if it never settles.</summary>
        /// <summary>An item lowered more often than this in one run is in a self-cheapening loop (values creep down towards zero).</summary>
        public int LoopLowerLimit = 48;

        bool Forward()
        {
            Array.Clear(loweredCount, 0, loweredCount.Length);
            var suspects = new HashSet<T>();
            int count = Edges.Count;
            var queued = new bool[count];
            List<int> work = null;
            // The items that changed in a pass, in the order they first did (what a set of them handed out before).
            var changed = new List<int>();

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                TotalPasses++;
                changed.Clear();
                stamp++;
                if (pass == 0)
                {
                    // The first pass goes through the edges in order. Those that can't come out differently are
                    // skipped; one whose ingredient changes earlier in this same pass is marked as it happens, and
                    // is worked out when its turn comes, as it always was.
                    bool all = allDirty;
                    allDirty = false;
                    for (int i = 0; i < count; i++)
                    {
                        if (all || dirty[i] || floorHit[i]) Relax(i, suspects, changed);
                    }
                }
                else
                {
                    foreach (int i in work)
                    {
                        queued[i] = false;
                        Relax(i, suspects, changed);
                    }
                }

                if (changed.Count == 0) return true;

                // Values that keep creeping down never "fail to converge" once they hit MinValue, so catch them by count.
                if (suspects.Count > 0)
                {
                    bool anyNew = false;
                    foreach (T item in suspects) anyNew |= Frozen.Add(item);
                    if (anyNew) { SettleFrozen(suspects); return false; }
                    Unstable.UnionWith(suspects);
                    return false;
                }

                work = new List<int>();
                foreach (int item in changed)
                {
                    List<int> deps = graph.Dependents[item];
                    if (deps == null) continue;
                    foreach (int d in deps)
                    {
                        if (!queued[d]) { queued[d] = true; work.Add(d); }
                    }
                }
                if (work.Count == 0) return true;

                if (pass == MaxPasses - 1)
                {
                    var changedItems = new List<T>(changed.Count);
                    foreach (int item in changed) changedItems.Add(graph.Items[item]);
                    bool anyNew = false;
                    foreach (T item in changedItems) anyNew |= Frozen.Add(item);
                    if (!anyNew) Unstable.UnionWith(changedItems); // freezing didn't help: give up on these
                    else SettleFrozen(changedItems);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Works one edge out and, if it makes its output cheaper (or gives it its first value), takes that value.</summary>
        void Relax(int i, HashSet<T> suspects, List<int> changed)
        {
            dirty[i] = false;
            int output = graph.OutputIds[i];
            if (pinnedOut[i] || unstable[output]) return;
            if (frozen[output] && has[output]) return;

            double cost = Cost(i);
            if (double.IsNaN(cost)) return;
            Edge e = graph.Edges[i];
            double per = cost / e.OutputQuantity;
            if (double.IsInfinity(per)) return;
            bool had = has[output];
            double current = val[output];
            if (per < MinValue)
            {
                // A recipe that would push a value below the floor: a loop that makes more than it takes (3 plates ->
                // 4 shingle blocks, 1 block -> 15 plates) ends here, so it counts as a loop and the value is frozen
                // at the first one it got, rather than settling at a millionth.
                floorHit[i] = true;
                if (had && current > MinValue) suspects.Add(e.Output);
                return;
            }
            floorHit[i] = false;
            if (!had || per < current * (1 - 1e-9))
            {
                if (had)
                {
                    int n = ++loweredCount[output];
                    if (n > LoopLowerLimit) suspects.Add(e.Output);
                    // Falling a thousandfold from the first derivation is a loop too, however few steps it took.
                    if (firstValue.TryGetValue(e.Output, out double first) && per * 1000 < first) suspects.Add(e.Output);
                }
                Values[e.Output] = per;
                val[output] = per;
                has[output] = true;
                Via[e.Output] = e;
                if (!firstValue.ContainsKey(e.Output)) firstValue[e.Output] = per;
                if (changedStamp[output] != stamp) { changedStamp[output] = stamp; changed.Add(output); }
                MarkUsersDirty(output);
            }
        }

        /// <summary>
        /// Items just frozen for sitting in a self-cheapening loop get the value of their cheapest recipe that does not
        /// go through another loop item, worked out from the values as they stand; only when every recipe runs
        /// through the loop does the first value they ever got stay. Before this, a frozen item kept whichever
        /// derivation happened first, which could be its dearest recipe (a crucible from a mod's expensive
        /// reversible process rather than from clay).
        /// </summary>
        void SettleFrozen(IEnumerable<T> items)
        {
            var set = new HashSet<T>(items);
            foreach (T item in set)
            {
                double best = double.PositiveInfinity;
                object via = null;
                graph.ByOutput.TryGetValue(item, out List<int> makers);
                for (int m = 0; makers != null && m < makers.Count; m++)
                {
                    int i = makers[m];
                    Edge e = graph.Edges[i];
                    bool throughLoop = false;
                    foreach (T[] choice in e.Choices)
                    {
                        if (choice.Length == 1 && (set.Contains(choice[0]) || Frozen.Contains(choice[0]))) { throughLoop = true; break; }
                    }
                    if (throughLoop) continue;
                    double cost = Cost(i);
                    if (double.IsNaN(cost) || e.OutputQuantity <= 0) continue;
                    double per = cost / e.OutputQuantity;
                    if (per >= MinValue && per < best) { best = per; via = e; }
                }
                if (via != null && !double.IsInfinity(best)) { SetValue(item, best); Via[item] = via; firstValue[item] = best; frozenValue[item] = best; }
                else if (firstValue.TryGetValue(item, out double first)) { SetValue(item, first); frozenValue[item] = first; }
            }
        }

        /// <summary>Cost of one run of the edge, or NaN if some ingredient has no value yet.</summary>
        double Cost(int index)
        {
            if (graph.Never[index]) return double.NaN;
            int[][] slots = graph.Slots[index];
            double[] quantities = graph.Quantities[index];
            double sum = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                double best = double.PositiveInfinity;
                int[] slot = slots[i];
                for (int k = 0; k < slot.Length; k++)
                {
                    int c = slot[k];
                    if (!has[c] || unstable[c]) continue;
                    double v = val[c];
                    if (v < best) best = v;
                }
                if (double.IsPositiveInfinity(best)) return double.NaN;
                sum += best * quantities[i];
            }
            slotsKnown[index] = true;
            sum += graph.Edges[index].ExtraCost;
            int[] returned = graph.Returned[index];
            if (returned != null)
            {
                double[] returnedQuantities = graph.ReturnedQuantities[index];
                bool[] optional = graph.ReturnedOptional[index];
                for (int j = 0; j < returned.Length; j++)
                {
                    // Something handed back that has no value yet: the net cost isn't known, so wait (counting it as
                    // nothing gave a bear's head pelt the whole pelt's value, with the body then worth as much again),
                    // unless the recipe says not to wait for it (then it counts as nothing until it has one).
                    if (!has[returned[j]]) { if (optional != null && optional[j]) continue; return double.NaN; }
                    sum -= val[returned[j]] * returnedQuantities[j];
                }
            }
            return sum > 0 ? sum : double.NaN;
        }

        /// <summary>
        /// Works values out backwards for items that are still unvalued. Returns how many were added.
        /// <list type="bullet">
        /// <item>Joint processing (one input, several outputs): unvalued outputs share what the input is worth beyond the valued outputs;
        ///   an unvalued input is worth the sum of its outputs.</item>
        /// <item>Single-input processing steps (ore → nugget, raw → dried): the input is worth its output. Cheapest proposal wins.</item>
        /// <item>Crafting (when <see cref="InferFromCrafting"/>): a recipe whose output has a value and where exactly one
        ///   ingredient slot has none values that ingredient at whatever is left. Only used for items processing couldn't
        ///   value, and the <i>highest</i> proposal wins, so no recipe that already had a value gets cheaper.</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// A shared-out edge with several ingredient slots: once every slot (and everything handed back) has a value, the
        /// outputs without one share what's left of the cost after the valued ones; with every output valued and exactly
        /// one slot open, that slot is worth what's left (only with <see cref="InferFromCrafting"/>).
        /// </summary>
        /// <summary>How often each output of a several-ingredient shared-out edge was lowered by it again (each is lowered a few times at most).</summary>
        readonly Dictionary<T, int> jointLowered = new Dictionary<T, int>();
        const int MaxJointLowerings = 8;

        void InferJoint(JointEdge j, Action<T, double, object> propose, Action<T, double, object> proposeShare, Func<T, bool> open)
        {
            double known = j.ExtraCost;
            int unknownSlot = -1;
            for (int s = 0; s < j.Choices.Length; s++)
            {
                double best = double.PositiveInfinity;
                bool anyOpen = false;
                foreach (T c in j.Choices[s])
                {
                    if (c == null) continue;
                    if (Free.Contains(c)) { best = 0; break; }
                    if (Excluded.Contains(c) || Unstable.Contains(c)) continue;
                    if (Values.TryGetValue(c, out double v)) { if (v < best) best = v; }
                    else if (open(c)) anyOpen = true;
                }
                double q = j.Quantities[s];
                if (!double.IsPositiveInfinity(best)) known += best * q;
                else if (anyOpen && unknownSlot < 0 && (j.NoInfer == null || !j.NoInfer[s])) unknownSlot = s;
                else return; // a second open slot, a slot nothing can fill, or a tool's wear with no value yet
            }
            double back = 0;
            if (j.Returned != null)
            {
                for (int k = 0; k < j.Returned.Length; k++)
                {
                    T r = j.Returned[k];
                    if (r == null || Excluded.Contains(r) || Free.Contains(r)) continue;
                    if (!Values.TryGetValue(r, out double rv))
                    {
                        if (j.ReturnedOptional != null && j.ReturnedOptional[k]) continue; // counts as nothing until it has one
                        return; // wait until what's handed back has a value
                    }
                    back += rv * j.ReturnedQuantities[k];
                }
            }
            if (unknownSlot < 0)
            {
                // The outputs without a value share what's left, and so do the ones that got theirs from this edge before:
                // if what goes in got cheaper since, their share is lowered (never raised; a few times at most).
                double remaining = known - back;
                var sharers = new List<int>();
                for (int k = 0; k < j.Outputs.Length; k++)
                {
                    T o = j.Outputs[k];
                    if (o == null || Excluded.Contains(o)) continue;
                    bool mine = Values.ContainsKey(o) && !Fixed.ContainsKey(o) && Via.TryGetValue(o, out object via) && ReferenceEquals(via, j);
                    if (mine) sharers.Add(k);
                    else if (Values.TryGetValue(o, out double ov)) remaining -= ov * j.OutputQuantities[k];
                    else if (open(o)) sharers.Add(k);
                }
                if (sharers.Count == 0 || remaining <= MinValue) return;
                // By the document's weights ("share"), or evenly.
                double weights = j.ShareWeights == null ? sharers.Count : sharers.Sum(k => j.ShareWeights[k]);
                if (weights <= 0) return;
                foreach (int k in sharers)
                {
                    T o = j.Outputs[k];
                    double share = remaining * (j.ShareWeights == null ? 1 : j.ShareWeights[k]) / weights;
                    double per = share / j.OutputQuantities[k];
                    if (Values.TryGetValue(o, out double current))
                    {
                        if (per < current * (1 - 1e-9) && (!jointLowered.TryGetValue(o, out int n) || n < MaxJointLowerings)) proposeShare(o, per, j);
                    }
                    else proposeShare(o, per, j);
                }
                return;
            }
            if (!InferBackwards || !InferFromCrafting) return;
            double sum = 0;
            for (int k = 0; k < j.Outputs.Length; k++)
            {
                T o = j.Outputs[k];
                if (o == null || Excluded.Contains(o)) continue;
                if (!Values.TryGetValue(o, out double ov)) return;
                sum += ov * j.OutputQuantities[k];
            }
            double qty = j.Quantities[unknownSlot];
            if (qty <= 0) return;
            double each = (sum + back - known) / qty;
            if (each < MinValue || double.IsNaN(each) || double.IsInfinity(each)) return;
            foreach (T c in j.Choices[unknownSlot])
            {
                if (c != null && open(c)) propose(c, each, j);
            }
        }

        int InferOnce()
        {
            var proposals = new Dictionary<T, double>();
            var proposalVia = new Dictionary<T, object>();
            // Working backwards, an input is worth the most its processing yields: a fish that fillets into 48 of
            // fillet is worth 48 even if cooking it whole gives 24, or filleting makes EMC out of nothing. (The lowest
            // yield used to win, which left every item with two uses open to that.)
            void Propose(T item, double v, object via)
            {
                if (v < MinValue || double.IsInfinity(v) || double.IsNaN(v)) return;
                if (!proposals.TryGetValue(item, out double prev) || v > prev) { proposals[item] = v; proposalVia[item] = via; }
            }
            bool Open(T item) => !Values.ContainsKey(item) && !Excluded.Contains(item) && !Unstable.Contains(item) && !Fixed.ContainsKey(item);
            // What a shared-out edge with several ingredients makes is costed forwards: of two such edges sharing an output,
            // the cheaper share wins (the highest-wins rule above is for working backwards).
            var shares = new Dictionary<T, double>();
            var shareVia = new Dictionary<T, object>();
            void ProposeShare(T item, double v, object via)
            {
                if (v < MinValue || double.IsInfinity(v) || double.IsNaN(v)) return;
                if (!shares.TryGetValue(item, out double prev) || v < prev) { shares[item] = v; shareVia[item] = via; }
            }

            foreach (JointEdge j in JointEdges)
            {
                if (j.Choices != null)
                {
                    InferJoint(j, Propose, ProposeShare, Open);
                    continue;
                }
                if (Excluded.Contains(j.Input) || Unstable.Contains(j.Input)) continue;
                if (Values.TryGetValue(j.Input, out double inputValue))
                {
                    double remaining = inputValue * j.InputQuantity;
                    var open = new List<int>();
                    for (int k = 0; k < j.Outputs.Length; k++)
                    {
                        if (Excluded.Contains(j.Outputs[k])) continue;
                        if (Values.TryGetValue(j.Outputs[k], out double ov)) remaining -= ov * j.OutputQuantities[k];
                        else if (Open(j.Outputs[k])) open.Add(k);
                    }
                    if (open.Count == 0 || remaining <= MinValue) continue;
                    double share = remaining / open.Count;
                    foreach (int k in open) Propose(j.Outputs[k], share / j.OutputQuantities[k], j);
                }
                else if (InferBackwards && Open(j.Input))
                {
                    double sum = 0;
                    bool all = true;
                    for (int k = 0; k < j.Outputs.Length && all; k++)
                    {
                        if (Excluded.Contains(j.Outputs[k])) continue;
                        if (Values.TryGetValue(j.Outputs[k], out double ov)) sum += ov * j.OutputQuantities[k];
                        else all = false;
                    }
                    if (all) Propose(j.Input, sum / j.InputQuantity, j);
                }
            }

            if (!InferBackwards) goto apply;
            foreach (int single in graph.ReversibleSingles)
            {
                Edge e = graph.Edges[single];
                T input = e.Choices[0][0];
                if (Values.ContainsKey(input) || Excluded.Contains(input) || Unstable.Contains(input)) continue;
                if (!Values.TryGetValue(e.Output, out double outValue)) continue;
                // What the process also hands back is part of what the input yields; wait until that has a value too.
                double back = 0;
                bool backKnown = true;
                if (e.Returned != null)
                {
                    for (int r = 0; r < e.Returned.Length && backKnown; r++)
                    {
                        T item = e.Returned[r];
                        if (Excluded.Contains(item) || Free.Contains(item)) continue;
                        if (Values.TryGetValue(item, out double rv)) back += rv * e.ReturnedQuantities[r];
                        else if (e.ReturnedOptional == null || !e.ReturnedOptional[r]) backKnown = false;
                    }
                }
                if (!backKnown) continue;
                Propose(input, (outValue * e.OutputQuantity + back - e.ExtraCost) / e.Quantities[0], e);
            }

            if (InferFromCrafting && proposals.Count == 0)
            {
                // Only once processing has nothing more to say, so the more direct evidence always wins.
                var craftProposals = new Dictionary<T, double>();
                var craftVia = new Dictionary<T, object>();
                for (int index = 0; index < graph.Edges.Length; index++)
                {
                    // Every slot filled: nothing is missing, so there is nothing to infer (most recipes, most rounds).
                    if (slotsKnown[index]) continue;
                    Edge e = graph.Edges[index];
                    if (!Values.TryGetValue(e.Output, out double outValue)) continue;
                    int unknownSlot = -1;
                    double known = 0;
                    bool usable = true;
                    for (int i = 0; i < e.Choices.Length && usable; i++)
                    {
                        double best = double.PositiveInfinity;
                        bool anyOpen = false;
                        foreach (T c in e.Choices[i])
                        {
                            if (Free.Contains(c)) { best = 0; break; }
                            if (Excluded.Contains(c) || Unstable.Contains(c)) continue;
                            if (Values.TryGetValue(c, out double v)) { if (v < best) best = v; }
                            else if (Open(c)) anyOpen = true;
                        }
                        if (!double.IsPositiveInfinity(best)) known += best * e.Quantities[i];
                        else if (anyOpen && unknownSlot < 0 && (e.NoInfer == null || !e.NoInfer[i])) unknownSlot = i;
                        else usable = false; // a second unknown slot, a slot nothing can fill, or a tool's wear
                    }
                    if (!usable || unknownSlot < 0) continue;
                    double total = outValue * e.OutputQuantity - e.ExtraCost - known;
                    if (e.Returned != null)
                    {
                        for (int j = 0; j < e.Returned.Length; j++)
                        {
                            if (Values.TryGetValue(e.Returned[j], out double rv)) total += rv * e.ReturnedQuantities[j];
                        }
                    }
                    double each = total / e.Quantities[unknownSlot];
                    if (each < MinValue || double.IsNaN(each) || double.IsInfinity(each)) continue;
                    foreach (T c in e.Choices[unknownSlot])
                    {
                        if (!Open(c)) continue;
                        if (!craftProposals.TryGetValue(c, out double prev) || each > prev) { craftProposals[c] = each; craftVia[c] = e; }
                    }
                }
                foreach (var kv in craftProposals) { proposals[kv.Key] = kv.Value; proposalVia[kv.Key] = craftVia[kv.Key]; }
            }

            apply:
            // Forward shares: a lower one also replaces a value the same kind of share gave before; with a proposal from
            // working backwards for the same item, the lower counts (the cheapest way to make it wins).
            foreach (var kv in shares)
            {
                double v = proposals.TryGetValue(kv.Key, out double back) ? Math.Min(back, kv.Value) : kv.Value;
                if (Values.ContainsKey(kv.Key)) jointLowered[kv.Key] = jointLowered.TryGetValue(kv.Key, out int n) ? n + 1 : 1;
                proposals[kv.Key] = v;
                proposalVia[kv.Key] = v == kv.Value ? shareVia[kv.Key] : proposalVia[kv.Key];
            }
            foreach (var kv in proposals)
            {
                SetValue(kv.Key, kv.Value);
                Inferred[kv.Key] = kv.Value;
                Via[kv.Key] = proposalVia[kv.Key];
                if (graph.Ids.TryGetValue(kv.Key, out int id)) MarkUsersDirty(id);
                // What makes it is looked at again too (it only ever had no value before, so nothing can change there; this costs next to nothing).
                if (graph.ByOutput.TryGetValue(kv.Key, out List<int> makers)) foreach (int m in makers) dirty[m] = true;
            }
            return proposals.Count;
        }
    }
}
