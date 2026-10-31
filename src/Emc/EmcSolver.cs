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
            /// <summary>Processing edges may be used backwards to infer an unvalued input from its output.</summary>
            public bool Reversible;
            /// <summary>Flat EMC added per run on top of the ingredients (time bonuses).</summary>
            public double ExtraCost;
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
        /// <summary>Items that never settled and were given their first value at the end (normally empty).</summary>
        public HashSet<T> KeptAtFirstValue { get; } = new HashSet<T>();
        /// <summary>Items in self-cheapening loops that keep their first value instead of being lowered.</summary>
        public HashSet<T> Frozen { get; } = new HashSet<T>();
        public int TotalPasses { get; private set; }

        Dictionary<T, List<int>> dependentsByItem;

        public void Solve()
        {
            BuildDependencyIndex();
            Values = new Dictionary<T, double>(Fixed);

            for (int round = 0; round < MaxRounds; round++)
            {
                if (!Forward())
                {
                    // Loop detected: freeze (or, second time round, drop) the offenders and start over from trusted values.
                    Values = new Dictionary<T, double>(Fixed);
                    foreach (var kv in Inferred) if (!Unstable.Contains(kv.Key)) Values[kv.Key] = kv.Value;
                    foreach (T k in Via.Keys.Where(k => !Inferred.ContainsKey(k)).ToList()) Via.Remove(k);
                    continue;
                }
                if (InferOnce() == 0) break;
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
                Forward();
            }
        }

        void BuildDependencyIndex()
        {
            dependentsByItem = new Dictionary<T, List<int>>();
            for (int i = 0; i < Edges.Count; i++)
            {
                Edge e = Edges[i];
                foreach (T[] choice in e.Choices)
                {
                    foreach (T c in choice) AddDependent(c, i);
                }
                if (e.Returned != null) foreach (T r in e.Returned) AddDependent(r, i);
            }
        }

        void AddDependent(T item, int edge)
        {
            if (!dependentsByItem.TryGetValue(item, out var list)) dependentsByItem[item] = list = new List<int>();
            if (list.Count == 0 || list[list.Count - 1] != edge) list.Add(edge);
        }

        /// <summary>Relaxes until nothing changes. Returns false (and fills <see cref="Unstable"/>) if it never settles.</summary>
        /// <summary>An item lowered more often than this in one run is in a self-cheapening loop (values creep down towards zero).</summary>
        public int LoopLowerLimit = 48;

        bool Forward()
        {
            var lowered = new Dictionary<T, int>();
            var suspects = new HashSet<T>();
            var queued = new bool[Edges.Count];
            var work = new List<int>(Edges.Count);
            for (int i = 0; i < Edges.Count; i++) { work.Add(i); queued[i] = true; }

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                TotalPasses++;
                var changed = new HashSet<T>();
                foreach (int i in work)
                {
                    queued[i] = false;
                    Edge e = Edges[i];
                    if (Fixed.ContainsKey(e.Output) || Excluded.Contains(e.Output) || Unstable.Contains(e.Output)) continue;
                    if (Frozen.Contains(e.Output) && Values.ContainsKey(e.Output)) continue;

                    double cost = Cost(e);
                    if (double.IsNaN(cost)) continue;
                    double per = cost / e.OutputQuantity;
                    if (per < MinValue || double.IsInfinity(per)) continue;

                    bool had = Values.TryGetValue(e.Output, out double current);
                    if (!had || per < current * (1 - 1e-9))
                    {
                        if (had)
                        {
                            lowered.TryGetValue(e.Output, out int n);
                            lowered[e.Output] = ++n;
                            if (n > LoopLowerLimit) suspects.Add(e.Output);
                        }
                        Values[e.Output] = per;
                        Via[e.Output] = e;
                        if (!firstValue.ContainsKey(e.Output)) firstValue[e.Output] = per;
                        changed.Add(e.Output);
                    }
                }

                if (changed.Count == 0) return true;

                // Values that keep creeping down never "fail to converge" once they hit MinValue, so catch them by count.
                if (suspects.Count > 0)
                {
                    bool anyNew = false;
                    foreach (T item in suspects) anyNew |= Frozen.Add(item);
                    if (anyNew) return false;
                    Unstable.UnionWith(suspects);
                    return false;
                }

                work = new List<int>();
                foreach (T item in changed)
                {
                    if (!dependentsByItem.TryGetValue(item, out var deps)) continue;
                    foreach (int d in deps)
                    {
                        if (!queued[d]) { queued[d] = true; work.Add(d); }
                    }
                }
                if (work.Count == 0) return true;

                if (pass == MaxPasses - 1)
                {
                    bool anyNew = false;
                    foreach (T item in changed) anyNew |= Frozen.Add(item);
                    if (!anyNew) Unstable.UnionWith(changed); // freezing didn't help: give up on these
                    return false;
                }
            }
            return true;
        }

        /// <summary>Cost of one run of the edge, or NaN if some ingredient has no value yet.</summary>
        double Cost(Edge e)
        {
            double sum = 0;
            for (int i = 0; i < e.Choices.Length; i++)
            {
                double best = double.PositiveInfinity;
                foreach (T c in e.Choices[i])
                {
                    if (Free.Contains(c)) { best = 0; break; }
                    if (Excluded.Contains(c) || Unstable.Contains(c)) continue;
                    if (Values.TryGetValue(c, out double v) && v < best) best = v;
                }
                if (double.IsPositiveInfinity(best)) return double.NaN;
                sum += best * e.Quantities[i];
            }
            sum += e.ExtraCost;
            if (e.Returned != null)
            {
                for (int j = 0; j < e.Returned.Length; j++)
                {
                    if (Values.TryGetValue(e.Returned[j], out double rv)) sum -= rv * e.ReturnedQuantities[j];
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
        int InferOnce()
        {
            var proposals = new Dictionary<T, double>();
            var proposalVia = new Dictionary<T, object>();
            void Propose(T item, double v, object via)
            {
                if (v < MinValue || double.IsInfinity(v) || double.IsNaN(v)) return;
                if (!proposals.TryGetValue(item, out double prev) || v < prev) { proposals[item] = v; proposalVia[item] = via; }
            }
            bool Open(T item) => !Values.ContainsKey(item) && !Excluded.Contains(item) && !Unstable.Contains(item) && !Fixed.ContainsKey(item);

            foreach (JointEdge j in JointEdges)
            {
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
            foreach (Edge e in Edges)
            {
                if (!e.Reversible || e.Choices.Length != 1 || e.Choices[0].Length != 1) continue;
                T input = e.Choices[0][0];
                if (Values.ContainsKey(input) || Excluded.Contains(input) || Unstable.Contains(input)) continue;
                if (!Values.TryGetValue(e.Output, out double outValue)) continue;
                Propose(input, (outValue * e.OutputQuantity - e.ExtraCost) / e.Quantities[0], e);
            }

            if (InferFromCrafting && proposals.Count == 0)
            {
                // Only once processing has nothing more to say, so the more direct evidence always wins.
                var craftProposals = new Dictionary<T, double>();
                var craftVia = new Dictionary<T, object>();
                foreach (Edge e in Edges)
                {
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
                        else if (anyOpen && unknownSlot < 0) unknownSlot = i;
                        else usable = false; // a second unknown slot, or a slot nothing can fill
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
            foreach (var kv in proposals)
            {
                Values[kv.Key] = kv.Value;
                Inferred[kv.Key] = kv.Value;
                Via[kv.Key] = proposalVia[kv.Key];
            }
            return proposals.Count;
        }
    }
}
