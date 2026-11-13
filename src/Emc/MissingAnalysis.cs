using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// Why things have no value, and the value loops behind it.
    /// <para>A <b>value loop</b> is a ring of unvalued items each made from the next (A needs B, B needs C, C needs A):
    /// none of them can get a value, because every way of making one already needs another. Each loop is reported
    /// once, starting at its first item by code: <c>a -> b -> c -> a</c>.</para>
    /// <para>A <b>settled loop</b> is a ring of recipes that kept making each other cheaper (A → 2 B, B → A). The
    /// solver stops that by keeping each item's first value; those are listed too, for information.</para>
    /// </summary>
    public static class MissingAnalysis
    {
        public const string NoRecipe = "nothing the mod can read makes it (no recipe, drop or process): loot, creative-only, or made a way the mod doesn't know";

        /// <summary>Fills MissingWhy, MissingRecipes, Loops, LoopOf and SettledLoops on the report.</summary>
        public static void Run(EmcMappingReport report, List<Conversion> conversions, ICollection<CollectibleObject> frozen)
        {
            var blacklisted = new HashSet<CollectibleObject>(report.Blacklisted);
            var free = new HashSet<CollectibleObject>(report.Free);
            bool Valued(CollectibleObject c) => c != null && (report.Values.ContainsKey(c) || free.Contains(c));
            bool Unvalued(CollectibleObject c) => c != null && !Valued(c) && !blacklisted.Contains(c);

            var producers = new Dictionary<CollectibleObject, List<Conversion>>();
            foreach (Conversion conv in conversions)
            {
                IEnumerable<CollectibleObject> outs = conv.JointOutputs != null ? conv.JointOutputs.Select(o => o.Key) : new[] { conv.Output };
                foreach (CollectibleObject o in outs)
                {
                    if (o == null) continue;
                    if (!producers.TryGetValue(o, out var list)) producers[o] = list = new List<Conversion>();
                    list.Add(conv);
                }
            }

            // Unvalued item → the unvalued items its recipes are waiting for. Walked out from the missing items only.
            var needs = new Dictionary<CollectibleObject, HashSet<CollectibleObject>>();
            var queue = new Queue<CollectibleObject>(report.Missing);
            var visited = new HashSet<CollectibleObject>(report.Missing);
            while (queue.Count > 0)
            {
                CollectibleObject c = queue.Dequeue();
                var set = new HashSet<CollectibleObject>();
                if (producers.TryGetValue(c, out var list))
                {
                    report.MissingRecipes[c] = list;
                    foreach (Conversion conv in list)
                    {
                        foreach (ConversionInput input in conv.Inputs)
                        {
                            if (input.Choices.Any(Valued)) continue;
                            foreach (CollectibleObject y in input.Choices.Where(Unvalued)) set.Add(y);
                        }
                        // What a recipe hands back must have a value too before its net cost is known.
                        if (conv.Returned != null) foreach (var kv in conv.Returned) if (kv.Key != null && Unvalued(kv.Key) && kv.Key != c && !conv.IsOptionalReturn(kv.Key)) set.Add(kv.Key);
                    }
                }
                needs[c] = set;
                foreach (CollectibleObject y in set) if (visited.Add(y)) queue.Enqueue(y);
            }

            report.Loops = FindLoops(needs);
            for (int i = 0; i < report.Loops.Count; i++)
            {
                foreach (CollectibleObject c in report.Loops[i]) if (!report.LoopOf.ContainsKey(c)) report.LoopOf[c] = i;
            }

            foreach (CollectibleObject c in report.Missing)
            {
                report.MissingWhy[c] = ShortWhy(report, c, Valued, blacklisted);
            }

            // Settled loops: rings of valued recipes (each item made from the next) that pass through an item the solver had
            // to freeze. Ordinary two-way recipes (ingot <-> block) are rings too, but nothing in them needed freezing.
            var frozenSet = new HashSet<CollectibleObject>(frozen ?? Array.Empty<CollectibleObject>());
            var frozenNeeds = new Dictionary<CollectibleObject, HashSet<CollectibleObject>>();
            if (frozenSet.Count > 0)
            {
                var valuedNeeds = new Dictionary<CollectibleObject, HashSet<CollectibleObject>>();
                foreach (var kv in producers)
                {
                    if (!report.Values.ContainsKey(kv.Key)) continue;
                    var set = new HashSet<CollectibleObject>();
                    foreach (Conversion conv in kv.Value)
                    {
                        foreach (ConversionInput input in conv.Inputs)
                        {
                            foreach (CollectibleObject y in input.Choices) if (y != null && report.Values.ContainsKey(y)) set.Add(y);
                        }
                    }
                    valuedNeeds[kv.Key] = set;
                }
                foreach (List<CollectibleObject> component in Components(valuedNeeds))
                {
                    if (!component.Any(frozenSet.Contains)) continue;
                    var members = new HashSet<CollectibleObject>(component);
                    foreach (CollectibleObject c in component) frozenNeeds[c] = new HashSet<CollectibleObject>(valuedNeeds[c].Where(members.Contains));
                }
            }
            report.SettledLoops = FindLoops(frozenNeeds);
        }

        static string ShortWhy(EmcMappingReport report, CollectibleObject c, System.Func<CollectibleObject, bool> valued, HashSet<CollectibleObject> blacklisted)
        {
            if (!report.MissingRecipes.TryGetValue(c, out var list) || list.Count == 0) return NoRecipe;
            if (report.LoopOf.TryGetValue(c, out int loop))
            {
                return "stuck in a value loop: " + LoopText(report.Loops[loop]) + " (each is only made from the next, and nothing outside the loop gives any of them a value)";
            }
            Conversion first = list[0];
            var lacking = Lacking(first, valued).Select(x => x.Code + (blacklisted.Contains(x) ? " (blacklisted)" : "")).Distinct().ToList();
            return list.Count + " way(s) to make it, e.g. " + EmcMapper.Describe(first) + (lacking.Count > 0 ? "; no value for: " + string.Join(", ", lacking) : "");
        }

        /// <summary>For each ingredient slot that nothing valued fills: its first choice.</summary>
        public static IEnumerable<CollectibleObject> Lacking(Conversion conv, System.Func<CollectibleObject, bool> valued)
        {
            foreach (ConversionInput input in conv.Inputs)
            {
                if (input.Choices.Length == 0 || input.Choices.Any(valued)) continue;
                yield return input.Choices[0];
            }
            // The solver waits for what a recipe hands back (a bucket, a byproduct) as for an ingredient.
            if (conv.Returned != null)
                foreach (var kv in conv.Returned)
                    if (kv.Key != null && kv.Key != conv.Output && !valued(kv.Key) && !conv.IsOptionalReturn(kv.Key)) yield return kv.Key;
        }

        public static string LoopText(IList<CollectibleObject> loop)
        {
            if (loop == null || loop.Count == 0) return "";
            return string.Join(" -> ", loop.Select(c => c.Code.ToString())) + " -> " + loop[0].Code;
        }

        /// <summary>
        /// Every ring in the graph (Tarjan's strongly connected components; a component of several items, or an item
        /// that needs itself). Each comes back as one short cycle through it, from its first item by code.
        /// </summary>
        public static List<List<CollectibleObject>> FindLoops(Dictionary<CollectibleObject, HashSet<CollectibleObject>> graph)
        {
            HashSet<CollectibleObject> Out(CollectibleObject c) => graph.TryGetValue(c, out var s) ? s : new HashSet<CollectibleObject>();
            var loops = new List<List<CollectibleObject>>();
            foreach (List<CollectibleObject> component in Components(graph))
            {
                List<CollectibleObject> cycle = ShortestCycle(component, Out);
                if (cycle != null) loops.Add(cycle);
            }
            return loops.OrderBy(l => l[0].Code.ToString(), StringComparer.Ordinal).ToList();
        }

        /// <summary>The graph's rings: strongly connected components of several items, or of one item that needs itself.</summary>
        static List<List<CollectibleObject>> Components(Dictionary<CollectibleObject, HashSet<CollectibleObject>> graph)
        {
            var result = new List<List<CollectibleObject>>();
            var index = new Dictionary<CollectibleObject, int>();
            var low = new Dictionary<CollectibleObject, int>();
            var onStack = new HashSet<CollectibleObject>();
            var stack = new Stack<CollectibleObject>();
            int next = 0;
            HashSet<CollectibleObject> Out(CollectibleObject c) => graph.TryGetValue(c, out var s) ? s : new HashSet<CollectibleObject>();

            // Iterative, so a long chain of recipes can't overflow the stack.
            foreach (CollectibleObject root in graph.Keys.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
            {
                if (index.ContainsKey(root)) continue;
                var work = new Stack<(CollectibleObject node, IEnumerator<CollectibleObject> it)>();
                index[root] = low[root] = next++;
                stack.Push(root); onStack.Add(root);
                work.Push((root, Out(root).GetEnumerator()));
                while (work.Count > 0)
                {
                    var (node, it) = work.Peek();
                    if (it.MoveNext())
                    {
                        CollectibleObject w = it.Current;
                        if (!index.ContainsKey(w))
                        {
                            index[w] = low[w] = next++;
                            stack.Push(w); onStack.Add(w);
                            work.Push((w, Out(w).GetEnumerator()));
                        }
                        else if (onStack.Contains(w)) low[node] = Math.Min(low[node], index[w]);
                        continue;
                    }
                    work.Pop();
                    if (work.Count > 0) { var parent = work.Peek().node; low[parent] = Math.Min(low[parent], low[node]); }
                    if (low[node] != index[node]) continue;
                    var component = new List<CollectibleObject>();
                    CollectibleObject x;
                    do { x = stack.Pop(); onStack.Remove(x); component.Add(x); } while (x != node);
                    if (component.Count > 1 || Out(node).Contains(node)) result.Add(component);
                }
            }
            return result;
        }

        /// <summary>The shortest way round from the component's first item (by code) back to itself.</summary>
        static List<CollectibleObject> ShortestCycle(List<CollectibleObject> component, System.Func<CollectibleObject, HashSet<CollectibleObject>> next)
        {
            var members = new HashSet<CollectibleObject>(component);
            CollectibleObject start = component.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).First();
            if (next(start).Contains(start)) return new List<CollectibleObject> { start };
            var cameFrom = new Dictionary<CollectibleObject, CollectibleObject>();
            var queue = new Queue<CollectibleObject>();
            foreach (CollectibleObject n in next(start).Where(members.Contains).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
            {
                if (cameFrom.ContainsKey(n)) continue;
                cameFrom[n] = start;
                queue.Enqueue(n);
            }
            while (queue.Count > 0)
            {
                CollectibleObject c = queue.Dequeue();
                if (next(c).Contains(start))
                {
                    var path = new List<CollectibleObject>();
                    for (CollectibleObject p = c; p != start; p = cameFrom[p]) path.Add(p);
                    path.Add(start);
                    path.Reverse();
                    return path;
                }
                foreach (CollectibleObject n in next(c).Where(members.Contains).OrderBy(x => x.Code.ToString(), StringComparer.Ordinal))
                {
                    if (n == start || cameFrom.ContainsKey(n)) continue;
                    cameFrom[n] = c;
                    queue.Enqueue(n);
                }
            }
            return null;
        }
    }
}
