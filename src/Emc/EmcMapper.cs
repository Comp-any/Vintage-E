using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    public class EmcMappingReport
    {
        public Dictionary<CollectibleObject, double> Values;
        public int BaseCount;
        public int DerivedCount;
        public int InferredCount;
        public int ConversionCount;
        public int SourceCount;
        public int UnstableRemoved;
        /// <summary>Items whose value never settled and that kept the first value they got.</summary>
        public int KeptAtFirstValue;
        public int LoopFrozen;
        public int UnmatchedRules;
        public int SkippedRecipes;
        /// <summary>The skipped recipes with their reasons (first 300), for /emc diagnose.</summary>
        public List<string> Skipped = new List<string>();
        public int SiblingCount;
        public int RelativeCount;
        public int FallbackCount;
        /// <summary>Items valued from trader prices, and the measured EMC per unit of trader currency.</summary>
        public int TraderCount;
        /// <summary>Guessed values replaced by a recipe that makes the item for less (checked once every value is known).</summary>
        public int CheaperRecipeCount;
        /// <summary>Items valued by "defaultValues" rules (nothing else could value them).</summary>
        public int DefaultCount;
        /// <summary>Items valued as fuel (burn heat × time) or food (satiety) because nothing else could value them.</summary>
        public int FuelCount, FoodCount;
        /// <summary>Other mods' items valued like the base game's variant of the same family; items guessed across other variant groups.</summary>
        public int OtherModCount, GuessCount;
        /// <summary>For each block/item still without a value: whether anything makes it, and which ingredient has no value.</summary>
        public Dictionary<CollectibleObject, string> MissingWhy = new Dictionary<CollectibleObject, string>();
        /// <summary>Items valued by each compatibility file (asset location → count), i.e. by its last-resort rules; only files that valued something.</summary>
        public SortedDictionary<string, int> CompatUsed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Every way the mod knows of making each unvalued item (missing ones and the unvalued things they need). Not cached.</summary>
        public Dictionary<CollectibleObject, List<Conversion>> MissingRecipes = new Dictionary<CollectibleObject, List<Conversion>>();
        /// <summary>Value loops: rings of unvalued items each only made from the next (see <see cref="MissingAnalysis"/>).</summary>
        public List<List<CollectibleObject>> Loops = new List<List<CollectibleObject>>();
        /// <summary>Which of <see cref="Loops"/> each item is in.</summary>
        public Dictionary<CollectibleObject, int> LoopOf = new Dictionary<CollectibleObject, int>();
        /// <summary>Rings of recipes that kept making each other cheaper; their items kept their first value.</summary>
        public List<List<CollectibleObject>> SettledLoops = new List<List<CollectibleObject>>();
        /// <summary>Material-specific values (a gold round shield...) from recipes that put data on what they make.</summary>
        public Dictionary<CollectibleObject, List<VariantValue>> Variants = new Dictionary<CollectibleObject, List<VariantValue>>();
        public double TraderRate;
        public long Milliseconds;
        /// <summary>Where the mapping's time went, in order: stage → milliseconds (empty when the values came from the cache).</summary>
        public List<KeyValuePair<string, long>> Timings = new List<KeyValuePair<string, long>>();
        /// <summary>Adds to a stage's time (stages that run more than once add up).</summary>
        public void AddTiming(string stage, long ms)
        {
            for (int i = 0; i < Timings.Count; i++)
            {
                if (Timings[i].Key != stage) continue;
                Timings[i] = new KeyValuePair<string, long>(stage, Timings[i].Value + ms);
                return;
            }
            Timings.Add(new KeyValuePair<string, long>(stage, ms));
        }
        /// <summary>"collecting recipes 840 ms, solving 900 ms, ..." for the log and the doctor.</summary>
        public string TimingText() => string.Join(", ", Timings.Select(t => t.Key + " " + t.Value + " ms"));
        public SortedDictionary<string, int> Sources;
        /// <summary>Blocks/items that appear in the creative inventory (i.e. are real, obtainable content), excluding the blacklist.</summary>
        public int CreativeTotal;
        /// <summary>Of those, the ones that still have no value.</summary>
        public List<CollectibleObject> Missing = new List<CollectibleObject>();
        /// <summary>Everything the blacklist keeps from having a value (for the /emc blacklist window).</summary>
        public List<CollectibleObject> Blacklisted = new List<CollectibleObject>();
        /// <summary>Free ingredients: on the blacklist, but cost nothing in recipes.</summary>
        public List<CollectibleObject> Free = new List<CollectibleObject>();
        /// <summary>The gray list: kept out of <see cref="Missing"/> and never given a guessed value, but valued by recipes.</summary>
        public List<CollectibleObject> Gray = new List<CollectibleObject>();
        /// <summary>Hand-written recipes that couldn't be used, with why (empty when the values came from the cache).</summary>
        public List<string> CustomRecipeProblems = new List<string>();
        /// <summary>True when the values were loaded from the on-disk cache instead of being mapped.</summary>
        public bool FromCache;
        /// <summary>When this mapping (or cache load) finished, for /emc diagnose.</summary>
        public DateTime MappedAt = DateTime.Now;
        public string CachedAt;
        /// <summary>Why each item has its value ("base value", "grid: 1 game:log-... -> 12", ...). Empty when loaded from cache.</summary>
        public Dictionary<CollectibleObject, string> Origins = new Dictionary<CollectibleObject, string>();
        /// <summary>The recipe or process each recipe-derived value came from (forwards, worked backwards or shared out). Empty from the cache.</summary>
        public Dictionary<CollectibleObject, Conversion> Via = new Dictionary<CollectibleObject, Conversion>();
        /// <summary>Values worked out backwards from what an item is used for (rather than how it's made).</summary>
        public HashSet<CollectibleObject> Inferred = new HashSet<CollectibleObject>();
        /// <summary>Items that kept their first value because their recipes kept making each other cheaper.</summary>
        public HashSet<CollectibleObject> Frozen = new HashSet<CollectibleObject>();
        /// <summary>Every recipe and process the mapper read (for /emc dump's "other ways to make it"). Empty from the cache.</summary>
        public List<Conversion> Conversions = new List<Conversion>();

        Dictionary<CollectibleObject, List<Conversion>> byOutput;

        /// <summary>Every way of making <paramref name="c"/> the mapper knows of (recipes, processes, shared-out processes).</summary>
        public List<Conversion> WaysToMake(CollectibleObject c)
        {
            if (byOutput == null)
            {
                var map = new Dictionary<CollectibleObject, List<Conversion>>();
                void Add(CollectibleObject o, Conversion conv)
                {
                    if (o == null) return;
                    if (!map.TryGetValue(o, out var list)) map[o] = list = new List<Conversion>();
                    if (!list.Contains(conv)) list.Add(conv);
                }
                foreach (Conversion conv in Conversions ?? new List<Conversion>())
                {
                    if (conv.JointOutputs != null) foreach (var o in conv.JointOutputs) Add(o.Key, conv);
                    else Add(conv.Output, conv);
                }
                byOutput = map;
            }
            return c != null && byOutput.TryGetValue(c, out var found) ? found : new List<Conversion>();
        }
    }

    /// <summary>
    /// Works out an EMC value for every block and item from scratch at server start:
    /// seeds a small set of base values, collects every recipe and processing step the loaded
    /// mods define, then propagates values through that graph with <see cref="EmcSolver{T}"/>.
    /// Nothing is hard-coded per item, so modded content is valued automatically as long as
    /// it is ultimately made from something with a value.
    /// </summary>
    public class EmcMapper
    {
        readonly ICoreServerAPI sapi;
        readonly VintageEConfig config;
        readonly EmcRuleSet rules;
        readonly ILogger logger;
        /// <summary>Free ingredients (see <see cref="EmcRuleSet.FreeIngredients"/>), set by Seed.</summary>
        HashSet<CollectibleObject> free = new HashSet<CollectibleObject>();
        /// <summary>The gray list (see <see cref="EmcMappingReport.Gray"/>), set by Seed.</summary>
        HashSet<CollectibleObject> gray = new HashSet<CollectibleObject>();
        /// <summary>Which value file's rule valued each item (defaultValues and relativeValues), for the compatibility counts.</summary>
        readonly Dictionary<CollectibleObject, string> ruleSourceOf = new Dictionary<CollectibleObject, string>();
        /// <summary>When set, only conversions it accepts are used (/emc diagnose clean maps without other mods' recipes).</summary>
        public System.Func<Conversion, bool> ConversionFilter;
        /// <summary>Material-specific values from traders' typed stacks, merged into the report's variants after the recipe ones.</summary>
        readonly Dictionary<CollectibleObject, List<VariantValue>> traderVariants = new Dictionary<CollectibleObject, List<VariantValue>>();

        public EmcMapper(ICoreServerAPI sapi, VintageEConfig config, EmcRuleSet rules, ILogger logger)
        {
            this.sapi = sapi;
            this.config = config;
            this.rules = rules;
            this.logger = logger;
        }

        // The mapping's time breakdown: solving is counted on its own, whichever stage asked for it.
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        long lapStart, solveMs, solveMsAtLap, forwardMs, inferMs;
        int solves, solveRounds;

        /// <summary>Books the time since the last lap, less what the solver took of it, to <paramref name="stage"/>.</summary>
        void Lap(EmcMappingReport report, string stage)
        {
            long now = clock.ElapsedMilliseconds;
            report.AddTiming(stage, Math.Max(0, now - lapStart - (solveMs - solveMsAtLap)));
            lapStart = now;
            solveMsAtLap = solveMs;
        }

        public EmcMappingReport Run()
        {
            var report = new EmcMappingReport();
            clock.Restart();
            lapStart = solveMs = solveMsAtLap = forwardMs = inferMs = 0;
            solves = solveRounds = 0;
            var index = new CollectibleIndex(sapi.World.Collectibles);

            Seed(index, report, out Dictionary<CollectibleObject, double> fixedValues, out HashSet<CollectibleObject> excluded);
            Lap(report, "base values and lists");

            // 3. Every way of making things.
            var collector = new ConversionCollector(sapi, index, logger, config)
            {
                Constructions = rules.Constructions,
                CustomRecipes = rules.Recipes.Concat(config.CustomRecipes ?? new List<string>()).ToList()
            };
            collector.CollectAll();
            if (ConversionFilter != null) collector.Conversions.RemoveAll(c => !ConversionFilter(c));
            report.SkippedRecipes = collector.SkippedRecipes;
            report.Skipped = collector.Skipped;
            report.CustomRecipeProblems = collector.CustomRecipeProblems;
            Lap(report, "reading recipes");

            // 4. Propagate.
            var edges = new List<EmcSolver<CollectibleObject>.Edge>();
            var joints = new List<EmcSolver<CollectibleObject>.JointEdge>();
            foreach (Conversion conv in collector.Conversions)
            {
                if (conv.JointOutputs != null)
                {
                    joints.Add(new EmcSolver<CollectibleObject>.JointEdge
                    {
                        Input = conv.Inputs[0].Choices[0],
                        InputQuantity = conv.Inputs[0].Quantity,
                        Outputs = conv.JointOutputs.Select(o => o.Key).ToArray(),
                        OutputQuantities = conv.JointOutputs.Select(o => o.Value).ToArray(),
                        Tag = conv
                    });
                    continue;
                }
                if (excluded.Contains(conv.Output)) continue;
                var edge = new EmcSolver<CollectibleObject>.Edge
                {
                    Output = conv.Output,
                    OutputQuantity = conv.OutputQuantity,
                    Choices = conv.Inputs.Select(i => i.Choices).ToArray(),
                    Quantities = conv.Inputs.Select(i => i.Quantity).ToArray(),
                    Reversible = conv.Kind == ConversionKind.Processing,
                    ExtraCost = conv.ExtraCost,
                    Tag = conv
                };
                if (conv.Returned != null)
                {
                    edge.Returned = conv.Returned.Select(r => r.Key).ToArray();
                    edge.ReturnedQuantities = conv.Returned.Select(r => r.Value).ToArray();
                }
                edges.Add(edge);
            }
            EmcSolver<CollectibleObject> solver = Solve(fixedValues, excluded, edges, joints);
            Lap(report, "building the graph");

            // 5. Last resort for whatever is still unvalued: borrow from its closest variant sibling
            //    (ore-poor-galena-chalk ← ore-poor-galena-granite, bread-spelt-charred ← bread-spelt-perfect).
            //    The highest sibling value is used, so a guessed value can never be cheaper to create than to burn.
            //    Then, only if the server owner asked for it, a flat fallback value for anything still left.
            var extra = new Dictionary<CollectibleObject, double>();
            var extraOrigin = new Dictionary<CollectibleObject, string>();
            var currency = new HashSet<CollectibleObject>();
            // The gray list is valued by recipes only: every guessing step below leaves it alone.
            var noGuess = new HashSet<CollectibleObject>(excluded);
            noGuess.UnionWith(gray);
            // Data-driven ratios first (metal bit = 1/20 ingot...), then variant siblings for whatever is left.
            foreach (var kv in RelativeValues(index, solver.Values, noGuess, extraOrigin)) extra[kv.Key] = kv.Value;
            report.RelativeCount = extra.Count;
            if (config.UseTraderPrices)
            {
                try
                {
                    var traders = new TraderPrices(sapi, logger);
                    traders.Collect();
                    double rate = traders.MeasureRate(solver.Values, out int samples);
                    report.TraderRate = rate;
                    logger.Notification("EMC: {0} traders price {1} different items; {2} of them already have a value, giving {3:0.##} EMC per unit of trader currency (median).",
                        traders.TraderCount, traders.PricePerItem.Count, samples, rate);
                    if (rate > 0)
                    {
                        // The currency itself (rusty gears) is worth what it buys, so selling things for gears and
                        // burning the gears can never come out ahead.
                        foreach (string pattern in rules.TraderCurrency)
                        {
                            foreach (CollectibleObject c in index.Match(pattern))
                            {
                                if (noGuess.Contains(c) || PinnedByConfig(index, c)) continue;
                                // A mod's recipe that makes the currency for less is the real price (craft, then burn).
                                if (solver.Values.TryGetValue(c, out double made) && made < rate) continue;
                                extra[c] = rate;
                                currency.Add(c);
                                extraOrigin[c] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "trader currency: {0:0.##} EMC per gear, measured on {1} traded items", rate, samples);
                            }
                        }
                        foreach (var kv in traders.PricePerItem)
                        {
                            CollectibleObject c = kv.Key;
                            if (solver.Values.ContainsKey(c) || noGuess.Contains(c) || extra.ContainsKey(c)) continue;
                            extra[c] = kv.Value * rate;
                            extraOrigin[c] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "trader price: {0:0.##} x {1:0.##} EMC per gear", kv.Value, rate);
                            report.TraderCount++;
                        }
                        // Typed stacks: the price belongs to that version alone (a trader's painting isn't every clutter block).
                        foreach (var (c, data, each) in traders.TypedPrices)
                        {
                            if (noGuess.Contains(c)) continue;
                            if (!traderVariants.TryGetValue(c, out var list)) traderVariants[c] = list = new List<VariantValue>();
                            list.Add(new VariantValue { Attributes = data, Value = each * rate, Origin = string.Format(System.Globalization.CultureInfo.InvariantCulture, "trader price: {0:0.##} x {1:0.##} EMC per gear", each, rate) });
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Warning("EMC: could not read trader prices: {0}", e.Message);
                }
            }
            // Every stage below only fills what is still unvalued, and the graph is solved again after it, so anything
            // made from the newly valued things gets its real recipe value before a rougher stage guesses at it.
            int extraBefore = 0;
            EmcSolver<CollectibleObject> Stage(EmcSolver<CollectibleObject> previous, bool force = false)
            {
                if (!force && extra.Count == extraBefore) return previous;
                extraBefore = extra.Count;
                var seeded = new Dictionary<CollectibleObject, double>(fixedValues);
                foreach (var kv in extra) seeded[kv.Key] = kv.Value;
                EmcSolver<CollectibleObject> next = Solve(seeded, excluded, edges, joints);
                // Backward inference depends on what's known when it runs, so a later solve can occasionally miss
                // something an earlier one valued. Keep those earlier values.
                foreach (var kv in previous.Values)
                {
                    if (next.Values.ContainsKey(kv.Key) || excluded.Contains(kv.Key) || next.Unstable.Contains(kv.Key)) continue;
                    next.Values[kv.Key] = kv.Value;
                    if (previous.Via.TryGetValue(kv.Key, out object via)) next.Via[kv.Key] = via;
                    if (previous.Inferred.ContainsKey(kv.Key)) next.Inferred[kv.Key] = kv.Value;
                }
                return next;
            }
            Lap(report, "ratios and trader prices");
            solver = Stage(solver); // ratios and trader prices

            bool Skip(CollectibleObject c) => noGuess.Contains(c) || extra.ContainsKey(c);
            Dictionary<CollectibleObject, double> Known()
            {
                var k = new Dictionary<CollectibleObject, double>(solver.Values);
                foreach (var kv in extra) k[kv.Key] = kv.Value;
                return k;
            }

            // Ratio rules again, now that trader prices may have valued what they're relative to (cheese slices of a
            // traded cheese, say).
            int relativeAgain = 0;
            foreach (var kv in RelativeValues(index, Known(), noGuess, extraOrigin))
            {
                if (extra.ContainsKey(kv.Key) || solver.Values.ContainsKey(kv.Key)) continue;
                extra[kv.Key] = kv.Value;
                relativeAgain++;
            }
            report.RelativeCount += relativeAgain;
            solver = Stage(solver);
            void Take(Dictionary<CollectibleObject, double> found, ref int counter)
            {
                foreach (var kv in found)
                {
                    if (extra.ContainsKey(kv.Key) || solver.Values.ContainsKey(kv.Key)) continue;
                    extra[kv.Key] = kv.Value;
                    counter++;
                }
            }

            // From the most to the least reliable: variants that only look or sit differently (and other mods' variants of
            // base-game families), the value files' family defaults, the middle value of other variants, and finally an
            // estimate from what a thing does (fuel, food) for whatever nothing else could value.
            var guesser = new VariantGuesser(rules);
            int guessed = 0;
            if (config.InferFromVariantSiblings)
            {
                Take(guesser.Guess(index, Known(), Skip, extraOrigin, otherGroups: false), ref guessed);
                solver = Stage(solver);
            }

            // Family-level guesses from the value files, for things no recipe, trader or variant could value.
            var defaults = new Dictionary<CollectibleObject, (double value, string pattern)>();
            foreach (EmcValueRule rule in rules.Defaults)
            {
                if (config.DisableBuiltInBaseValues && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                foreach (CollectibleObject c in index.Match(rule.Pattern))
                {
                    if (solver.Values.ContainsKey(c) || noGuess.Contains(c) || extra.ContainsKey(c)) continue;
                    if (rule.Value > 0)
                    {
                        double itemsPerLitre = rule.PerLitre ? EmcRegistry.ItemsPerLitre(c) : 0;
                        defaults[c] = (itemsPerLitre > 0 ? rule.Value / itemsPerLitre : rule.Value, rule.Pattern);
                        ruleSourceOf[c] = rule.Source;
                    }
                    else { defaults.Remove(c); ruleSourceOf.Remove(c); }
                }
            }
            foreach (var kv in defaults)
            {
                extra[kv.Key] = kv.Value.value;
                extraOrigin[kv.Key] = "default value (" + kv.Value.pattern + ")";
                report.DefaultCount++;
            }
            solver = Stage(solver);

            // Ratio rules once more, now that variants and defaultValues may have valued what they're relative to (a
            // live fish of a raw fish whose processing gives another mod's fish eggs, valued only by a default).
            int relativeLate = 0;
            foreach (var kv in RelativeValues(index, Known(), noGuess, extraOrigin))
            {
                if (extra.ContainsKey(kv.Key) || solver.Values.ContainsKey(kv.Key)) continue;
                extra[kv.Key] = kv.Value;
                relativeLate++;
            }
            report.RelativeCount += relativeLate;
            solver = Stage(solver);

            if (config.InferFromVariantSiblings && rules.CopyAcrossOtherGroups)
            {
                Take(guesser.Guess(index, Known(), Skip, extraOrigin, otherGroups: true), ref guessed);
                solver = Stage(solver);
            }
            if (config.EstimateFuelAndFood)
            {
                Take(UseEstimates.Fuel(index, Known(), rules.FuelReference, Skip, extraOrigin), ref report.FuelCount);
                Take(UseEstimates.Food(index, Known(), Skip, extraOrigin), ref report.FoodCount);
                solver = Stage(solver);
            }
            report.SiblingCount = guesser.SameFamilyCount;
            report.OtherModCount = guesser.OtherModCount;
            report.GuessCount = guesser.OtherGroupCount;

            if (config.FallbackValue > 0)
            {
                foreach (CollectibleObject c in index.All)
                {
                    if (IsListed(c) && !noGuess.Contains(c) && !solver.Values.ContainsKey(c) && !extra.ContainsKey(c))
                    {
                        extra[c] = config.FallbackValue;
                        extraOrigin[c] = "FallbackValue from the server config";
                        report.FallbackCount++;
                    }
                }
            }
            solver = Stage(solver);

            Lap(report, "guesses (variants, defaults, fuel and food)");

            // The cheapest real way to get an item is its value. A guessed value (a trader price, a default, a copy
            // from a variant, an estimate) is seeded as fixed, so a recipe whose ingredients only got values in a later
            // stage could never lower it: blasting powder stayed at its trader price (53.66) although saltpeter,
            // sulfur and coal make six for 25.8 each. Check now, with every value known, and solve once more.
            // Each guess is raised at most once: where what it yields is itself made from it (a fruit, its pitted
            // version, its juice), raising it again every round would run away.
            var raised = new HashSet<CollectibleObject>();
            for (int round = 0; round < 8; round++)
            {
                // The other way round: a guessed value for something that is processed (one input) into more than
                // it is worth. A guessed input can't be worth less than what it yields, or processing then burning
                // makes EMC (a compat-file fruit at 16 pressed into 24 of juice). Raise it to the yield.
                var dearer = new Dictionary<CollectibleObject, (double yield, Conversion conv)>();
                foreach (Conversion conv in collector.Conversions)
                {
                    // One input, one choice, nothing handed back: processing, or a grid recipe that just converts
                    // (a strong bone into a bone).
                    if (conv.Inputs.Count != 1 || conv.Inputs[0].Choices.Length != 1 || conv.Returned != null) continue;
                    CollectibleObject i = conv.Inputs[0].Choices[0];
                    if (i == null || !extra.ContainsKey(i) || currency.Contains(i) || PinnedByConfig(index, i) || raised.Contains(i)) continue;
                    double? y = YieldPer(conv, solver.Values);
                    if (y == null || y.Value <= extra[i] * 1.001) continue;
                    if (!dearer.TryGetValue(i, out var best) || y.Value > best.yield) dearer[i] = (y.Value, conv);
                }
                var cheaper = new Dictionary<CollectibleObject, (double per, Conversion conv)>();
                foreach (Conversion conv in collector.Conversions)
                {
                    CollectibleObject o = conv.Output;
                    // A guess that is also raised to its yield keeps the raise: the burn exploit is the one to close.
                    if (o == null || conv.JointOutputs != null || !extra.ContainsKey(o) || currency.Contains(o) || PinnedByConfig(index, o) || dearer.ContainsKey(o) || raised.Contains(o)) continue;
                    double? per = ViableCostPer(conv, solver.Values);
                    if (per == null || per.Value <= 0 || per.Value >= extra[o] * 0.999) continue;
                    // A recipe whose ingredients were themselves priced from this item (o -> 2P, P + stick -> o) would
                    // lower it every round towards nothing; only recipes that rest on other things count.
                    if (RestsOn(conv, o, solver)) continue;
                    if (!cheaper.TryGetValue(o, out var best) || per.Value < best.per) cheaper[o] = (per.Value, conv);
                }
                if (cheaper.Count == 0 && dearer.Count == 0) break;
                foreach (var kv in cheaper)
                {
                    extraOrigin.TryGetValue(kv.Key, out string was);
                    double oldValue = extra[kv.Key];
                    extra[kv.Key] = kv.Value.per;
                    extraOrigin[kv.Key] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "recipe ({0}), cheaper than its {1} of {2:0.##}", kv.Value.conv.Source, was ?? "guessed value", oldValue);
                    report.CheaperRecipeCount++;
                }
                foreach (var kv in dearer)
                {
                    raised.Add(kv.Key);
                    extraOrigin.TryGetValue(kv.Key, out string was);
                    double oldValue = extra[kv.Key];
                    extra[kv.Key] = kv.Value.yield;
                    extraOrigin[kv.Key] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "what it yields ({0}), more than its {1} of {2:0.##}", kv.Value.conv.Source, was ?? "guessed value", oldValue);
                    report.CheaperRecipeCount++;
                }
                solver = Stage(solver, force: true);
            }

            Lap(report, "guesses against recipes");

            report.Values = new Dictionary<CollectibleObject, double>();
            foreach (var kv in solver.Values)
            {
                double tidy = Tidy(kv.Value);
                if (tidy > 0 && !double.IsNaN(tidy) && !double.IsInfinity(tidy) && !excluded.Contains(kv.Key) && !solver.Unstable.Contains(kv.Key)) report.Values[kv.Key] = tidy;
            }
            report.BaseCount = fixedValues.Count;
            report.InferredCount = solver.Inferred.Keys.Count(k => report.Values.ContainsKey(k));
            report.DerivedCount = report.Values.Count - report.BaseCount - report.InferredCount - report.SiblingCount - report.FallbackCount - report.RelativeCount - report.TraderCount - report.DefaultCount
                - report.FuelCount - report.FoodCount - report.OtherModCount - report.GuessCount;
            report.Variants = VariantValues(collector.Conversions, report.Values, excluded);
            foreach (var kv in traderVariants)
            {
                if (!report.Variants.TryGetValue(kv.Key, out var list)) report.Variants[kv.Key] = list = new List<VariantValue>();
                foreach (VariantValue v in kv.Value)
                {
                    if (!list.Any(e => StackDataUtil.IsSubset(sapi.World, e.Attributes, v.Attributes) && StackDataUtil.IsSubset(sapi.World, v.Attributes, e.Attributes))) list.Add(v);
                }
            }
            report.ConversionCount = solver.Edges.Count + solver.JointEdges.Count;
            report.Conversions = collector.Conversions;
            Lap(report, "material-specific values");
            FillCoverage(index, report, excluded);
            MissingAnalysis.Run(report, collector.Conversions, solver.Frozen);
            Lap(report, "coverage and missing items");
            report.Sources = collector.SourceCounts;
            report.SourceCount = collector.SourceCounts.Count(s => s.Value > 0);
            var compatSources = new HashSet<string>(rules.Compat.Select(f => f.Source), StringComparer.Ordinal);
            foreach (var kv in ruleSourceOf)
            {
                if (!compatSources.Contains(kv.Value) || !report.Values.ContainsKey(kv.Key)) continue;
                report.CompatUsed[kv.Value] = report.CompatUsed.TryGetValue(kv.Value, out int n) ? n + 1 : 1;
            }
            report.UnstableRemoved = solver.Unstable.Count;
            report.KeptAtFirstValue = solver.KeptAtFirstValue.Count;
            report.LoopFrozen = solver.Frozen.Count;
            foreach (CollectibleObject c in report.Values.Keys)
            {
                string origin;
                if (extraOrigin.TryGetValue(c, out string how)) origin = how;
                else if (fixedValues.ContainsKey(c)) origin = "base value";
                else if (solver.Via.TryGetValue(c, out object via))
                {
                    object tag = via is EmcSolver<CollectibleObject>.Edge ed ? ed.Tag : via is EmcSolver<CollectibleObject>.JointEdge je ? je.Tag : via;
                    origin = (solver.Inferred.ContainsKey(c) ? "worked backwards from " : "") + Describe(tag as Conversion);
                    if (solver.Frozen.Contains(c)) origin += " (recipe loop: kept its first value)";
                    if (tag is Conversion used) report.Via[c] = used;
                    if (solver.Inferred.ContainsKey(c)) report.Inferred.Add(c);
                    if (solver.Frozen.Contains(c)) report.Frozen.Add(c);
                }
                else origin = "?";
                report.Origins[c] = origin;
            }

            foreach (var src in collector.SourceCounts)
            {
                logger.VerboseDebug("EMC source {0}: {1} conversions", src.Key, src.Value);
            }
            if (config.LogAllValues)
            {
                foreach (var kv in report.Values.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    logger.Notification("EMC {0} {1} = {2}", kv.Key.ItemClass == EnumItemClass.Block ? "block" : "item", kv.Key.Code, EmcFormat.Format(kv.Value));
                }
            }
            Lap(report, "origins");
            report.AddTiming($"solving ({solves} solves, {solveRounds} rounds; relaxing {forwardMs} ms, inferring {inferMs} ms)", solveMs);
            return report;
        }

        /// <summary>Blacklist and base values: asset files (ours, then other mods') → item attributes → server config.</summary>
        void Seed(CollectibleIndex index, EmcMappingReport report, out Dictionary<CollectibleObject, double> fixedValues, out HashSet<CollectibleObject> excluded)
        {
            // 1. Things that must never have a value (creative/debug blocks etc.).
            excluded = new HashSet<CollectibleObject>();
            foreach (string pattern in rules.Blacklist)
            {
                excluded.UnionWith(index.Match(pattern));
            }
            foreach (string pattern in rules.Allow)
            {
                excluded.ExceptWith(index.Match(pattern));
            }
            // The server's own lists last: its blacklist (single items or whole mods), then its exceptions.
            foreach (string pattern in config.Blacklist)
            {
                excluded.UnionWith(index.Match(pattern));
            }
            foreach (string pattern in config.Allow)
            {
                excluded.ExceptWith(index.Match(pattern));
            }
            // Free ingredients: no value of their own (so blacklisted), but recipes using them only cost the rest.
            free = new HashSet<CollectibleObject>();
            foreach (string pattern in rules.FreeIngredients.Concat(config.FreeIngredients)) free.UnionWith(index.Match(pattern));
            excluded.UnionWith(free);
            report.Free = free.Where(c => c?.Code != null).ToList();

            // The gray list: no guessed values, not "missing"; recipes still value it. Blacklisted things stay blacklisted.
            gray = new HashSet<CollectibleObject>();
            foreach (string pattern in rules.GrayList.Concat(config.GrayList ?? new List<string>())) gray.UnionWith(index.Match(pattern));
            gray.ExceptWith(excluded);

            // 2. Seed values. Later sources win: asset files (ours, then other mods') → item attributes → server config.
            fixedValues = new Dictionary<CollectibleObject, double>();
            foreach (EmcValueRule rule in rules.Values)
            {
                if (config.DisableBuiltInBaseValues && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                if (!Apply(index, rule.Pattern, rule.Value, fixedValues, excluded, overrideBlacklist: false, perLitre: rule.PerLitre))
                {
                    report.UnmatchedRules++;
                    logger.VerboseDebug("EMC rule '{0}' from {1} matched nothing", rule.Pattern, rule.Source);
                }
            }
            foreach (CollectibleObject c in index.All)
            {
                // Any mod can give its items a value with  "attributes": { "emcValue": 128 }  (0 = no EMC).
                JsonObject attr = c.Attributes?["emcValue"];
                if (attr != null && attr.Exists)
                {
                    double v = attr.AsDouble(-1);
                    if (v > 0 && !double.IsNaN(v) && !double.IsInfinity(v)) fixedValues[c] = v;
                    else if (v == 0) { excluded.Add(c); fixedValues.Remove(c); }
                }
            }
            foreach (var kv in config.BaseValues) Apply(index, kv.Key, kv.Value, fixedValues, excluded, overrideBlacklist: true);
            foreach (var kv in config.FixedValues) Apply(index, kv.Key, kv.Value, fixedValues, excluded, overrideBlacklist: true);
            foreach (CollectibleObject c in excluded) fixedValues.Remove(c);

        }

        /// <summary>
        /// Builds a report around values that came from the cache: re-applies the (cheap) blacklist/seeding
        /// so coverage numbers are exact, without collecting recipes or solving anything.
        /// </summary>
        public EmcMappingReport FromValues(Dictionary<CollectibleObject, double> values)
        {
            var report = new EmcMappingReport { FromCache = true, Values = values };
            var index = new CollectibleIndex(sapi.World.Collectibles);
            Seed(index, report, out _, out HashSet<CollectibleObject> excluded);
            foreach (CollectibleObject c in excluded) values.Remove(c);
            FillCoverage(index, report, excluded);
            return report;
        }

        void FillCoverage(CollectibleIndex index, EmcMappingReport report, HashSet<CollectibleObject> excluded)
        {
            report.Blacklisted = excluded.Where(c => c?.Code != null).ToList();
            report.Gray = gray.Where(c => c?.Code != null).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList();
            foreach (CollectibleObject c in index.All)
            {
                if (!IsListed(c) || excluded.Contains(c)) continue;
                bool valued = report.Values.ContainsKey(c);
                if (!valued && gray.Contains(c)) continue; // waiting on the gray list: not counted until something makes it
                report.CreativeTotal++;
                if (!valued) report.Missing.Add(c);
            }
        }

        static bool IsListed(CollectibleObject c) => CollectibleIndex.IsListed(c);

        /// <summary>
        /// What one output of this recipe costs with the given values, or null when a slot has no valued choice (the
        /// recipe can't be run yet), when the output is among its own inputs, or when something returned is unvalued.
        /// </summary>
        double? ViableCostPer(Conversion conv, IReadOnlyDictionary<CollectibleObject, double> values)
        {
            if (conv.OutputQuantity <= 0) return null;
            double cost = conv.ExtraCost;
            foreach (ConversionInput input in conv.Inputs)
            {
                double best = double.MaxValue;
                foreach (CollectibleObject c in input.Choices)
                {
                    if (c == null) continue;
                    if (c == conv.Output) return null;
                    double v = free.Contains(c) ? 0 : values.TryGetValue(c, out double x) ? x : double.MaxValue;
                    if (v < best) best = v;
                }
                if (best == double.MaxValue) return null;
                cost += best * input.Quantity;
            }
            if (conv.Returned != null)
                foreach (var r in conv.Returned)
                {
                    if (!values.TryGetValue(r.Key, out double rv)) return null;
                    cost -= rv * r.Value;
                }
            return cost / conv.OutputQuantity;
        }

        /// <summary>True when any ingredient of this recipe got its value (through the solver's Via chain) from <paramref name="target"/>.</summary>
        static bool RestsOn(Conversion conv, CollectibleObject target, EmcSolver<CollectibleObject> solver)
        {
            var seen = new HashSet<CollectibleObject>();
            var stack = new Stack<(CollectibleObject item, int depth)>();
            foreach (ConversionInput input in conv.Inputs) foreach (CollectibleObject c in input.Choices) if (c != null) stack.Push((c, 0));
            while (stack.Count > 0)
            {
                var (item, depth) = stack.Pop();
                if (item == target) return true;
                if (depth >= 10 || !seen.Add(item) || !solver.Via.TryGetValue(item, out object via)) continue;
                if (via is EmcSolver<CollectibleObject>.Edge e)
                {
                    foreach (CollectibleObject[] choice in e.Choices) foreach (CollectibleObject c in choice) if (c != null) stack.Push((c, depth + 1));
                }
                else if (via is EmcSolver<CollectibleObject>.JointEdge j && j.Input != null) stack.Push((j.Input, depth + 1));
            }
            return false;
        }

        /// <summary>What one unit of this single-input process's input yields in EMC (every output valued), or null.</summary>
        static double? YieldPer(Conversion conv, IReadOnlyDictionary<CollectibleObject, double> values)
        {
            double inQty = conv.Inputs[0].Quantity;
            if (inQty <= 0) return null;
            double sum = 0;
            if (conv.JointOutputs != null)
            {
                foreach (var o in conv.JointOutputs)
                {
                    if (o.Key == conv.Inputs[0].Choices[0]) return null;
                    if (!values.TryGetValue(o.Key, out double v)) return null;
                    sum += v * o.Value;
                }
            }
            else
            {
                if (conv.Output == null || conv.Output == conv.Inputs[0].Choices[0] || !values.TryGetValue(conv.Output, out double v)) return null;
                sum = v * conv.OutputQuantity;
            }
            return (sum - conv.ExtraCost) / inQty;
        }

        EmcSolver<CollectibleObject> Solve(Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, List<EmcSolver<CollectibleObject>.Edge> edges, List<EmcSolver<CollectibleObject>.JointEdge> joints)
        {
            var solver = new EmcSolver<CollectibleObject>
            {
                Fixed = fixedValues,
                Excluded = excluded,
                Free = free,
                MaxPasses = config.MaxMappingPasses,
                InferBackwards = config.InferValuesFromProcessing,
                InferFromCrafting = config.InferValuesFromProcessing && config.InferValuesFromCrafting
            };
            solver.Edges.AddRange(edges);
            solver.JointEdges.AddRange(joints);
            long before = clock.ElapsedMilliseconds;
            solver.Solve();
            solveMs += clock.ElapsedMilliseconds - before;
            solves++;
            solveRounds += solver.Rounds;
            forwardMs += solver.ForwardMs;
            inferMs += solver.InferMs;
            return solver;
        }

        /// <summary>
        /// relativeValues rules: an unvalued item matching Code gets Ratio × the value of the item named by Of,
        /// with the '*' part of Code carried over ("game:metalbit-*" of "game:ingot-*" → metalbit-copper from ingot-copper).
        /// </summary>
        /// <summary>True if the server owner set this item's value by hand (ModConfig BaseValues/FixedValues).</summary>
        bool PinnedByConfig(CollectibleIndex index, CollectibleObject c)
        {
            foreach (string key in config.FixedValues.Keys.Concat(config.BaseValues.Keys))
            {
                if (index.Match(key).Contains(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// Material-specific values: every recipe that puts data on what it makes (a round shield's metal and wood, a
        /// chest's wood) gives that exact version a value - its own ingredients, not the cheapest recipe's.
        /// </summary>
        static Dictionary<CollectibleObject, List<VariantValue>> VariantValues(List<Conversion> conversions, Dictionary<CollectibleObject, double> values, HashSet<CollectibleObject> excluded)
        {
            var best = new Dictionary<CollectibleObject, Dictionary<string, VariantValue>>();
            foreach (Conversion conv in conversions)
            {
                if (conv.OutputData == null || conv.JointOutputs != null || conv.Output == null || excluded.Contains(conv.Output) || conv.OutputQuantity <= 0) continue;
                double cost = conv.ExtraCost;
                bool ok = true;
                foreach (ConversionInput input in conv.Inputs)
                {
                    double cheapest = double.MaxValue;
                    foreach (CollectibleObject choice in input.Choices)
                    {
                        if (values.TryGetValue(choice, out double v) && v > 0 && v < cheapest) cheapest = v;
                    }
                    if (cheapest == double.MaxValue) { ok = false; break; }
                    cost += cheapest * input.Quantity;
                }
                if (!ok) continue;
                if (conv.Returned != null)
                {
                    foreach (var r in conv.Returned)
                    {
                        if (values.TryGetValue(r.Key, out double v)) cost -= v * r.Value;
                    }
                }
                double each = Tidy(cost / conv.OutputQuantity);
                if (each <= 0) continue;
                string key = StackDataUtil.Encode(conv.OutputData);
                if (!best.TryGetValue(conv.Output, out var byData)) best[conv.Output] = byData = new Dictionary<string, VariantValue>();
                if (!byData.TryGetValue(key, out VariantValue prev) || each < prev.Value)
                {
                    byData[key] = new VariantValue { Attributes = conv.OutputData, Value = each, Origin = Describe(conv) };
                }
            }
            var result = new Dictionary<CollectibleObject, List<VariantValue>>();
            foreach (var kv in best)
            {
                // Only worth keeping when the versions differ (otherwise the plain value says it all).
                if (kv.Value.Count < 2 && !(values.TryGetValue(kv.Key, out double plain) && kv.Value.Values.Any(v => Math.Abs(v.Value - plain) > 1e-6))) continue;
                result[kv.Key] = kv.Value.Values.ToList();
            }
            return result;
        }

        /// <summary>Why each missing block/item has no value: nothing makes it, or what it's made from has no value.</summary>
        /// <summary>"grid: 1 game:log-placed-oak-ud (+11 more) -> 12" — a short, human-readable description of a conversion.</summary>
        internal static string Describe(Conversion conv)
        {
            if (conv == null) return "?";
            static string Num(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            static string Choice(CollectibleObject[] choices) => choices.Length == 0 ? "?" : choices[0].Code + (choices.Length > 1 ? $" (or {choices.Length - 1} more)" : "");
            string inputs = string.Join(" + ", conv.Inputs.Select(i => Num(i.Quantity) + " " + Choice(i.Choices)));
            string outputs = conv.JointOutputs != null
                ? string.Join(" + ", conv.JointOutputs.Select(o => Num(o.Value) + " " + o.Key.Code))
                : Num(conv.OutputQuantity) + " " + conv.Output?.Code;
            string extra = conv.ExtraCost > 0 ? " (+" + Num(conv.ExtraCost) + " time bonus)" : "";
            string returned = conv.Returned != null && conv.Returned.Count > 0 ? " (returns " + string.Join(", ", conv.Returned.Select(r => r.Key.Code)) + ")" : "";
            return $"{conv.Source}: {inputs}{extra} -> {outputs}{returned}";
        }

        Dictionary<CollectibleObject, double> RelativeValues(CollectibleIndex index, Dictionary<CollectibleObject, double> values, HashSet<CollectibleObject> excluded, Dictionary<CollectibleObject, string> origin)
        {
            var result = new Dictionary<CollectibleObject, double>();
            foreach (RelativeValueRule rule in rules.Relative)
            {
                int star = rule.Code.IndexOf('*');
                foreach (CollectibleObject c in index.Match(rule.Code))
                {
                    if (values.ContainsKey(c) || excluded.Contains(c) || result.ContainsKey(c)) continue;
                    string target = rule.Of;
                    if (star >= 0)
                    {
                        var pattern = new AssetLocation(rule.Code.StartsWith("item:") || rule.Code.StartsWith("block:") ? rule.Code.Substring(rule.Code.IndexOf(':') + 1) : rule.Code);
                        int pathStar = pattern.Path.IndexOf('*');
                        if (pathStar < 0) continue; // the only * is in the domain: nothing to carry over
                        string prefix = pattern.Path.Substring(0, pathStar);
                        string suffix = pattern.Path.Substring(pathStar + 1);
                        string path = c.Code.Path;
                        if (path.Length < prefix.Length + suffix.Length) continue;
                        string captured = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
                        target = rule.Of.Replace("*", captured);
                    }
                    double best = 0;
                    foreach (CollectibleObject source in index.Match(target))
                    {
                        if (values.TryGetValue(source, out double v) && v > best) best = v;
                    }
                    if (best > 0)
                    {
                        result[c] = best * rule.Ratio;
                        origin[c] = $"relativeValues rule: {rule.Ratio} x {target}";
                        ruleSourceOf[c] = rule.Source;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// For each unvalued collectible, find valued ones whose code differs in exactly one dash-separated part,
        /// trying the last part first (rock type, colour, orientation...) and never the first (the item family).
        /// </summary>
        /// <summary>The variant group codes (as the block/item type defines them) whose states differ between two variants.</summary>
        internal static string DifferingGroups(CollectibleObject a, CollectibleObject b)
        {
            var va = a.Variant;
            var vb = b.Variant;
            if (va == null || vb == null || va.Count == 0) return "?";
            var diff = new List<string>();
            foreach (var kv in va)
            {
                if (!vb.TryGetValue(kv.Key, out string other) || other != kv.Value) diff.Add(kv.Key);
            }
            return diff.Count == 0 ? "?" : string.Join("+", diff);
        }

        static bool Apply(CollectibleIndex index, string pattern, double value, Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, bool overrideBlacklist, bool perLitre = false)
        {
            bool any = false;
            foreach (CollectibleObject c in index.Match(pattern))
            {
                any = true;
                if (!overrideBlacklist && excluded.Contains(c)) continue; // default files never resurrect blacklisted things
                if (value > 0)
                {
                    double itemsPerLitre = perLitre ? EmcRegistry.ItemsPerLitre(c) : 0;
                    if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                    fixedValues[c] = itemsPerLitre > 0 ? value / itemsPerLitre : value;
                    excluded.Remove(c);
                }
                else
                {
                    fixedValues.Remove(c);
                    excluded.Add(c);
                }
            }
            return any;
        }

        /// <summary>Rounds away floating-point noise (33.333333334 → 33.3333) without changing meaningful precision.</summary>
        static double Tidy(double v)
        {
            if (v >= 1000) return Math.Round(v, 1);
            return Math.Round(v, 4);
        }
    }
}
