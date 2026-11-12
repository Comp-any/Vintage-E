using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The doctor's side of other mods' crafting methods (1.23.0): the check-up's cards, /emc methods, the item, mod and
    /// trace lines, four symptoms, fixes (switch a method off or on, set an energy unit's price), a checker for mod
    /// authors (methodcheck) and a starting document drafted from recipe data Vintage-E can't read (draftmethods).
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionMethods = "methods", ActionMethodCheck = "methodcheck", ActionDraftMethods = "draftmethods";

        /// <summary>EMC per second of machine time from power alone above which the doctor warns (a quarter of an Energy Collector MK1).</summary>
        public const double FreePowerLimit = 1.0;

        /// <summary>A number as the reports show it: up to 4 decimals, and 4 significant digits below 0.01 (a price of 0.00002 isn't 0).</summary>
        static string N(double d)
        {
            if (d == 0 || double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
            if (Math.Abs(d) >= 0.01) return d.ToString("0.####", CultureInfo.InvariantCulture);
            int decimals = Math.Min(15, 3 - (int)Math.Floor(Math.Log10(Math.Abs(d))));
            return Math.Round(d, decimals).ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        }

        /// <summary><paramref name="v"/> rounded down to 3 significant digits, so the price shown is never above the real one.</summary>
        static double FloorSignificant(double v)
        {
            if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double step = Math.Pow(10, Math.Floor(Math.Log10(v)) - 2);
            return Math.Floor(v / step * (1 + 1e-12)) * step;
        }

        /// <summary>
        /// The methods a report talks about: those the mapping was made with, so its MethodResults and conversions match
        /// (a collect since then, waiting for its remap, would otherwise make every recipe "not read").
        /// </summary>
        static MethodSnapshot Snap(Ctx x) => x.Report?.Methods ?? x.Mod.Methods ?? MethodSnapshot.Empty;

        /// <summary>A copy of a list the game thread may change while a report is built on a worker.</summary>
        static List<T> Copy<T>(IEnumerable<T> items)
        {
            for (int i = 0; ; i++)
            {
                try { return items == null ? new List<T>() : new List<T>(items); }
                catch (InvalidOperationException) when (i < 3) { }
            }
        }

        // ---------------------------------------------------------------- what became of each recipe

        /// <summary>Used (it gave something its value), beaten (a cheaper way won), set (its outputs have a set value: base, pinned, a value file) or waiting.</summary>
        enum MethodOutcome { Used, Beaten, Set, Waiting }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EmcMappingReport, Dictionary<MethodRecipe, List<Conversion>>> conversionsByReport = new System.Runtime.CompilerServices.ConditionalWeakTable<EmcMappingReport, Dictionary<MethodRecipe, List<Conversion>>>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EmcMappingReport, HashSet<CollectibleObject>> freeByReport = new System.Runtime.CompilerServices.ConditionalWeakTable<EmcMappingReport, HashSet<CollectibleObject>>();

        /// <summary>The method conversions of a mapping, by recipe (worked out once per mapping).</summary>
        static Dictionary<MethodRecipe, List<Conversion>> MethodConversions(Ctx x)
        {
            if (x.Report == null) return new Dictionary<MethodRecipe, List<Conversion>>();
            return conversionsByReport.GetValue(x.Report, report =>
            {
                var result = new Dictionary<MethodRecipe, List<Conversion>>();
                foreach (Conversion conv in report.Conversions ?? new List<Conversion>())
                {
                    if (conv.Method?.Recipe == null) continue;
                    if (!result.TryGetValue(conv.Method.Recipe, out var list)) result[conv.Method.Recipe] = list = new List<Conversion>();
                    list.Add(conv);
                }
                return result;
            });
        }

        static HashSet<CollectibleObject> FreeSet(Ctx x) => freeByReport.GetValue(x.Report, report => new HashSet<CollectibleObject>(report.Free ?? new List<CollectibleObject>()));

        static IEnumerable<CollectibleObject> OutputsOf(Conversion conv) => conv.JointOutputs != null ? conv.JointOutputs.Select(o => o.Key) : new[] { conv.Output };

        /// <summary>Whether a conversion makes <paramref name="c"/>: as its output, one it shares out, or a byproduct taken off its cost.</summary>
        static bool MakesIt(Conversion conv, CollectibleObject c)
            => OutputsOf(conv).Contains(c) || (conv.Returned != null && conv.Method?.Byproducts != null && conv.Method.Byproducts.Contains(c) && conv.Returned.Any(r => r.Key == c));

        /// <summary>The mod a code belongs to ("item:", "block:" and wildcards allowed).</summary>
        static string DomainOf(string code)
        {
            code = code ?? "";
            if (code.StartsWith("item:", StringComparison.OrdinalIgnoreCase) || code.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) code = code.Substring(code.IndexOf(':') + 1);
            int colon = code.IndexOf(':');
            return colon > 0 ? code.Substring(0, colon).ToLowerInvariant() : "game";
        }

        /// <summary>A code that matches nothing because its mod isn't loaded (a recipe for an optional mod): nothing to fix.</summary>
        static bool ForAbsentMod(Ctx x, string badCode)
        {
            if (badCode == null) return false;
            string domain = DomainOf(badCode);
            if (domain == "game" || domain.Contains('*')) return false;
            try { return x.Sapi.ModLoader.GetMod(domain) == null; } catch { return false; }
        }

        /// <summary>What one conversion costs per run at today's values (ingredients, costs, less what's handed back), or null with what's missing.</summary>
        static double? RunCost(Ctx x, Conversion conv, List<CollectibleObject> lacking)
        {
            var free = FreeSet(x);
            double cost = conv.ExtraCost;
            foreach (ConversionInput input in conv.Inputs)
            {
                double best = double.MaxValue;
                foreach (CollectibleObject c in input.Choices)
                {
                    if (free.Contains(c)) { best = 0; break; }
                    double v = x.V(c);
                    if (v > 0 && v < best) best = v;
                }
                if (best == double.MaxValue) { lacking?.Add(input.Choices.FirstOrDefault()); continue; }
                cost += best * input.Quantity;
            }
            if (conv.Returned != null)
                foreach (var r in conv.Returned)
                {
                    double v = x.V(r.Key);
                    if (v <= 0) { lacking?.Add(r.Key); continue; }
                    cost -= v * r.Value;
                }
            if (lacking != null && lacking.Count > 0) return null;
            return cost;
        }

        /// <summary>Used (it gave something its value), beaten (a cheaper way won) or waiting (something it needs has no value).</summary>
        static MethodOutcome OutcomeOf(Ctx x, List<Conversion> convs, out Conversion used, out List<CollectibleObject> lacking)
        {
            used = null;
            lacking = new List<CollectibleObject>();
            foreach (Conversion conv in convs)
            {
                foreach (CollectibleObject o in OutputsOf(conv))
                {
                    if (o != null && x.Report.Via.TryGetValue(o, out Conversion via) && via == conv) { used = conv; return MethodOutcome.Used; }
                }
            }
            Conversion first = convs.FirstOrDefault();
            if (first != null && OutputsOf(first).All(o => o != null && x.Report.Values.ContainsKey(o)))
                // Beaten by another recipe, or nothing to beat: a value that is set (base, pinned, a value file) isn't made at all.
                return OutputsOf(first).Any(o => x.Report.Via.TryGetValue(o, out Conversion via) && via != null) ? MethodOutcome.Beaten : MethodOutcome.Set;
            if (first != null) RunCost(x, first, lacking);
            lacking = lacking.Where(l => l != null).Distinct().ToList();
            return MethodOutcome.Waiting;
        }

        /// <summary>Why a waiting recipe gives nothing a value, in words (what it needs, or why it needs nothing and still waits).</summary>
        static string WaitingText(Ctx x, Conversion conv, List<CollectibleObject> lacking)
        {
            if (lacking.Count > 0) return x.L("diag-meth-waiting", string.Join(", ", lacking.Take(4).Select(EmcReport.Ref)));
            CollectibleObject black = conv == null ? null : OutputsOf(conv).FirstOrDefault(o => o != null && x.Report.Blacklisted.Contains(o));
            if (black != null) return x.L("diag-meth-waiting-blacklisted", EmcReport.Ref(black));
            double? cost = conv == null ? null : RunCost(x, conv, new List<CollectibleObject>());
            if (cost.HasValue && cost.Value <= 0) return x.L("diag-meth-waiting-free", F(cost.Value));
            return x.L("diag-meth-waiting-unknown");
        }

        /// <summary>A waiting recipe there's nothing to do about: its output is blacklisted.</summary>
        static bool WaitsOnBlacklist(Ctx x, Conversion conv, List<CollectibleObject> lacking)
            => lacking.Count == 0 && conv != null && OutputsOf(conv).Any(o => o != null && x.Report.Blacklisted.Contains(o));

        /// <summary>"2 [[copper ingot]] + 400 kN·s → 1 [[plate]] + 0.5 [[bits]]" for one conversion.</summary>
        static string RecipeText(Ctx x, Conversion conv)
        {
            string Choice(ConversionInput i) => i.Choices.Length == 0 ? "?" : EmcReport.Ref(i.Choices[0]) + (i.Choices.Length > 1 ? " " + x.L("diag-meth-orchoices", i.Choices.Length - 1) : "");
            var parts = conv.Inputs.Select(i => N(i.Quantity) + " " + Choice(i) + (i.NoInfer ? " " + x.L("diag-meth-wear") : "")).ToList();
            foreach (MethodCostLine k in conv.Costs ?? new List<MethodCostLine>())
            {
                if (k.Kind == "energy") parts.Add(N(k.Amount) + " " + UnitName(x, k.Unit));
                else if (k.Kind == "heat") parts.Add(N(k.Amount) + " °C·s");
                else if (k.Kind == "time") parts.Add(N(k.Amount) + " s");
                else parts.Add(N(k.Amount) + " EMC");
            }
            string ins = parts.Count == 0 ? x.L("diag-meth-nothing") : string.Join(" + ", parts);
            string outs = conv.JointOutputs != null
                ? string.Join(" + ", conv.JointOutputs.Select(o => N(o.Value) + " " + EmcReport.Ref(o.Key)))
                : N(conv.OutputQuantity) + " " + EmcReport.Ref(conv.Output) + (conv.Returned != null && conv.Method?.Byproducts != null && conv.Returned.Any(r => conv.Method.Byproducts.Contains(r.Key))
                    ? " + " + string.Join(" + ", conv.Returned.Where(r => conv.Method.Byproducts.Contains(r.Key)).Select(r => N(r.Value) + " " + EmcReport.Ref(r.Key)))
                    : "");
            return ins + " → " + outs;
        }

        static string UnitName(Ctx x, string unit)
        {
            if (unit == null) return "?";
            if (x.Report?.MethodPrices != null && x.Report.MethodPrices.Units.TryGetValue(unit, out MethodUnitPrice p) && !string.IsNullOrEmpty(p.Name)) return p.Name;
            return Snap(x).Units.TryGetValue(unit, out MethodEnergyUnit u) && !string.IsNullOrEmpty(u.Name) ? u.Name : unit;
        }

        /// <summary>One line for a recipe with what became of it.</summary>
        static void RecipeLine(Ctx x, MethodRecipe recipe, List<Conversion> convs, MethodRecipeResult result, int indent)
        {
            EmcReport r = x.R;
            string head = recipe.Code + (recipe.ReadFrom != null ? " " + x.L("diag-meth-readfrom", recipe.ReadFrom) : "");
            if (result != null && result.Disabled) { r.Add(head + ": " + x.L("diag-meth-off"), EmcLineStyle.Note, indent); return; }
            if (convs == null || convs.Count == 0)
            {
                bool absent = ForAbsentMod(x, result?.BadCode);
                r.Add(head + ": " + x.L("diag-meth-skipped", result?.Skipped ?? x.L("diag-meth-notread")) + (result?.BadCode != null && !absent ? Suggest(x, result.BadCode) : ""), absent ? EmcLineStyle.Note : EmcLineStyle.Warn, indent);
                if (absent) x.Note(x.L("diag-meth-absentmod", DomainOf(result.BadCode)), indent + 1);
                return;
            }
            MethodOutcome outcome = OutcomeOf(x, convs, out Conversion used, out List<CollectibleObject> lacking);
            Conversion shown = used ?? convs[0];
            string variants = convs.Count > 1 ? " " + x.L("diag-meth-variants", convs.Count) : "";
            var missing = new List<CollectibleObject>();
            double? cost = RunCost(x, shown, missing);
            double per = cost.HasValue && shown.JointOutputs == null && shown.OutputQuantity > 0 ? cost.Value / shown.OutputQuantity : double.NaN;
            string costText = !double.IsNaN(per) ? " = " + x.L("diag-meth-each", F(per)) : cost.HasValue ? " = " + x.L("diag-meth-run", F(cost.Value)) : "";
            r.Add(head + variants + ": " + RecipeText(x, shown) + costText, outcome == MethodOutcome.Waiting && !WaitsOnBlacklist(x, shown, lacking) ? EmcLineStyle.Warn : EmcLineStyle.Text, indent);
            switch (outcome)
            {
                case MethodOutcome.Used: x.Note(x.L("diag-meth-used"), indent + 1); break;
                case MethodOutcome.Beaten:
                    {
                        CollectibleObject o = OutputsOf(shown).FirstOrDefault(oo => oo != null && x.Report.Via.TryGetValue(oo, out Conversion vv) && vv != null) ?? OutputsOf(shown).FirstOrDefault();
                        string by = o != null && x.Report.Via.TryGetValue(o, out Conversion via) && via != null ? Linkify(x, EmcMapper.Describe(via)) : o != null && x.Report.Origins.TryGetValue(o, out string orig) ? Linkify(x, orig) : "?";
                        x.Note(x.L("diag-meth-beaten", o != null ? EmcReport.Ref(o) + " " + F(x.V(o)) : "?", by), indent + 1);
                        break;
                    }
                case MethodOutcome.Set:
                    {
                        CollectibleObject o = OutputsOf(shown).FirstOrDefault();
                        string from = o != null && x.Report.Origins.TryGetValue(o, out string orig) ? Linkify(x, orig) : "?";
                        x.Note(x.L("diag-meth-setvalue", o != null ? EmcReport.Ref(o) + " " + F(x.V(o)) : "?", from), indent + 1);
                        break;
                    }
                default:
                    x.Note(WaitingText(x, shown, lacking), indent + 1);
                    break;
            }
            if (result != null && result.MissingVariants > 0) x.Note(x.L("diag-meth-missingvariants", result.MissingVariants), indent + 1);
            if (result != null && result.FailedVariants > 0) x.Note(x.L("diag-meth-failedvariants", result.FailedVariants, result.VariantFailure ?? "?"), indent + 1);
            if (result != null) foreach (string note in result.Notes) x.Note(note, indent + 1);
        }

        // ---------------------------------------------------------------- the check-up's cards

        /// <summary>The check-up's cards for other mods' crafting methods. Returns how many found something worth fixing.</summary>
        static int MethodsSummary(Ctx x, bool full)
        {
            MethodSnapshot snap = Snap(x);
            var unseen = UnseenRecipeData(x);
            // Documents that wait for a mod that isn't loaded (Vintage-E's own files for other mods) are normal: no section for them alone.
            if (!snap.Docs.Any(d => d.Usable || d.Error != null) && snap.ListenerErrors.Count == 0 && snap.Problems.Count == 0 && unseen.Count == 0) return 0;
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-meth"));
            int problems = 0;
            // Listener errors are a start-up problem the check-up's start-up card already shows (and counts).
            problems += DocumentsCard(x, snap, full, true);
            bool fresh = x.Report != null && !x.Report.FromCache && x.Report.MethodPrices != null;
            if (snap.MethodCount > 0 && !fresh)
            {
                x.Info(x.L("diag-meth-cached"));
                r.Add(FixLink(x, "recalc", "", x.L("diag-fix-recalc")), EmcLineStyle.Text, 1);
            }
            else if (snap.MethodCount > 0)
            {
                var convs = MethodConversions(x);
                problems += RecipesCard(x, convs);
                problems += PricesCard(x);
                problems += FreePowerCard(x, convs);
                CostsCountCard(x, convs);
                problems += StuckCard(x, convs);
                problems += FreeCopiesCard(x);
            }
            UnseenCard(x, unseen, full);
            problems += SwitchedOffCard(x);
            r.Add(Link(ActionMethods, "", x.L("diag-q-methods")), EmcLineStyle.Text, 1);
            return problems;
        }

        /// <summary>Card 1: the documents, by mod, and what's wrong with them.</summary>
        static int DocumentsCard(Ctx x, MethodSnapshot snap, bool full, bool listenerErrorsElsewhere)
        {
            EmcReport r = x.R;
            var docs = snap.Docs;
            // By file and by code count the documents in use, like the mods before them; broken and waiting ones have lines of their own.
            int files = docs.Count(d => d.Usable && d.Route == "file"), code = docs.Count(d => d.Usable && d.Route == "code");
            var broken = docs.Where(d => d.Error != null).ToList();
            var waiting = docs.Where(d => d.Error == null && d.MissingMod != null).ToList();
            int warnings = docs.Where(d => d.Usable).Sum(d => d.Problems.Count(p => !p.Error));
            int left = docs.Where(d => d.Usable).Sum(d => d.Problems.Count(p => p.Error && !ForAbsentMod(x, p.BadCode))) + snap.Problems.Count(p => p.Error);
            bool listener = snap.ListenerErrors.Count > 0 && !listenerErrorsElsewhere;
            string line = x.L("diag-meth-docs", snap.Mods.Count(), files, code, snap.MethodCount, snap.RecipeCount);
            int problems = 0;
            if (broken.Count > 0 || listener || left > 0) { problems++; x.Warn(line); }
            else if (docs.Count == 0) x.Info(x.L("diag-meth-docs-none"));
            else if (warnings > 0) x.Info(line);
            else x.Ok(line);
            x.Explain(x.L("diag-meth-docs-why"));
            foreach (var g in docs.Where(d => d.Usable).GroupBy(d => d.ModId).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int errs = g.Sum(d => d.Problems.Count(p => p.Error)), warns = g.Sum(d => d.Problems.Count(p => !p.Error));
                r.Add(ModName(x, g.Key) + ": " + x.L("diag-meth-docline", string.Join(", ", g.Select(d => d.Route).Distinct()), g.Max(d => d.Format), g.Sum(d => d.Methods.Count), g.Sum(d => d.RecipeCount), errs, warns)
                    + "  " + Link(ActionMethods, g.Key, x.L("diag-meth-look")) + " " + Link(ActionMethodCheck, g.Key, x.L("diag-meth-check")), errs > 0 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
            }
            foreach (MethodDoc d in broken) r.Add(x.L("diag-meth-broken", d.Source, d.Error), EmcLineStyle.Warn, 1);
            if (!listenerErrorsElsewhere)
                foreach (var (mod, error) in snap.ListenerErrors)
                {
                    r.Add(x.L("diag-meth-listener", mod == "?" ? x.L("diag-meth-unknownmod") : ModName(x, mod), error), EmcLineStyle.Warn, 1);
                    x.Fix(x.L("diag-meth-listener-fix"));
                }
            foreach (MethodProblem p in snap.Problems) r.Add(p.ToString(), p.Error ? EmcLineStyle.Warn : EmcLineStyle.Note, 1);
            // Files for mods that aren't loaded (Vintage-E's own for other mods among them) are normal: a few by name, the rest counted.
            int waitShown = full ? 30 : 3;
            foreach (MethodDoc d in waiting.Take(waitShown)) x.Note(x.L("diag-meth-waitingmod", d.Source, d.MissingMod), 1);
            if (waiting.Count > waitShown) x.Note(x.L("diag-meth-waitingmore", waiting.Count - waitShown), 1);
            if (left > 0 || warnings > 0) x.Fix(x.L("diag-meth-docs-fix"));
            return problems;
        }

        /// <summary>Card 2: recipes read, used, skipped (grouped by why, each with where).</summary>
        static int RecipesCard(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs)
        {
            EmcReport r = x.R;
            var results = x.Report.MethodResults;
            int read = results.Count, off = results.Count(rr => rr.Disabled);
            var allSkipped = results.Where(rr => !rr.Disabled && rr.Skipped != null).ToList();
            // A recipe for a mod that isn't loaded (an optional extra) is left out as it should be: counted, not a problem.
            var optional = allSkipped.Where(rr => ForAbsentMod(x, rr.BadCode)).ToList();
            var skipped = allSkipped.Except(optional).ToList();
            var parseProblems = Snap(x).UsableDocs.SelectMany(d => d.Problems.Where(p => p.Error && !ForAbsentMod(x, p.BadCode)).Select(p => (d, p))).ToList();
            int used = results.Count(rr => !rr.Disabled && rr.Skipped == null && convs.TryGetValue(rr.Recipe, out var cl) && OutcomeOf(x, cl, out _, out _) == MethodOutcome.Used);
            string line = x.L("diag-meth-recipes", read, used, allSkipped.Count, off);
            int problems = 0;
            if (skipped.Count + parseProblems.Count > 0) { problems++; x.Warn(line); } else x.Ok(line);
            x.Explain(x.L("diag-meth-recipes-why"));
            foreach (var g in skipped.GroupBy(s => Regex.Replace(s.Skipped, @"\S+:\S+", "…")).OrderByDescending(g => g.Count()).Take(8))
            {
                var firstOne = g.First();
                r.Add(x.L("diag-meth-skipline", g.Count(), firstOne.Skipped), EmcLineStyle.Text, 1);
                foreach (var s in g.Take(3))
                    x.Note(s.Recipe.Method.Doc.Source + " " + s.Recipe.Path + (s.BadCode != null ? Suggest(x, s.BadCode) : ""), 2);
            }
            if (optional.Count > 0) x.Note(x.L("diag-meth-optional", optional.Count, string.Join(", ", optional.Select(o => DomainOf(o.BadCode)).Distinct().Take(6))), 1);
            if (parseProblems.Count > 0) r.Add(x.L("diag-meth-parseproblems", parseProblems.Count), EmcLineStyle.Text, 1);
            foreach (var (d, p) in parseProblems.Take(8)) r.Add(d.Source + " " + p, EmcLineStyle.Text, 2);
            if (parseProblems.Count > 8) x.Note(x.L("diag-more", parseProblems.Count - 8), 2);
            if (problems > 0) x.Fix(x.L("diag-meth-recipes-fix"));
            return problems;
        }

        /// <summary>The energy units recipes use (declared or not), with their prices.</summary>
        static List<MethodUnitPrice> UsedUnits(Ctx x)
        {
            var prices = x.Report?.MethodPrices;
            if (prices == null) return new List<MethodUnitPrice>();
            var used = new HashSet<string>(x.Report.Conversions.Where(c => c.Costs != null).SelectMany(c => c.Costs).Where(k => k.Kind == "energy").Select(k => k.Unit), StringComparer.Ordinal);
            List<MethodUnitPrice> all;
            lock (prices.Units) all = prices.Units.Values.ToList();
            return all.Where(p => p.Declared || used.Contains(p.Code)).OrderBy(p => p.Code, StringComparer.Ordinal).ToList();
        }

        /// <summary>The price the doctor suggests for a unit: what its engines make it cost, else the mod's suggestion; 0 if neither.</summary>
        static double SuggestedPrice(MethodUnitPrice p) => p.GeneratorPrice > 0 ? p.GeneratorPrice : p.Suggested ?? 0;

        /// <summary>Card 3: each energy unit's price and where it came from; a warning for one with none.</summary>
        static int PricesCard(Ctx x)
        {
            EmcReport r = x.R;
            var prices = x.Report.MethodPrices;
            var units = UsedUnits(x);
            var used = new HashSet<string>(x.Report.Conversions.Where(c => c.Costs != null).SelectMany(c => c.Costs).Where(k => k.Kind == "energy").Select(k => k.Unit), StringComparer.Ordinal);
            // A price of 0 set on purpose (EnergyEmc) is a choice, not something missing.
            var unpriced = units.Where(p => p.Price <= 0 && used.Contains(p.Code) && p.Source != "setting").ToList();
            int problems = 0;
            if (units.Count == 0) x.Ok(x.L("diag-meth-prices-none"));
            else if (unpriced.Count > 0 && prices.ChargeEnergy) { problems++; x.Warn(x.L("diag-meth-prices-unpriced", unpriced.Count, units.Count(p => used.Contains(p.Code)))); }
            else x.Ok(x.L("diag-meth-prices", units.Count(p => used.Contains(p.Code)), units.Count));
            x.Explain(x.L("diag-meth-prices-why"));
            foreach (MethodUnitPrice p in units)
            {
                bool missing = p.Price <= 0 && used.Contains(p.Code) && p.Source != "setting";
                r.Add(x.L("diag-meth-priceline", p.Code, p.Name ?? p.Code, N(p.Price), p.SourceText ?? "?") + (used.Contains(p.Code) ? "" : " " + x.L("diag-meth-unit-unused")), missing ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                foreach (string g in p.Generators) if (g != p.SourceText) x.Note(g, 2);
                if (!p.Declared)
                {
                    string near = Snap(x).Units.Keys.OrderBy(k => MethodParser.Distance(k, p.Code)).FirstOrDefault();
                    if (near != null && MethodParser.Distance(near, p.Code) <= Math.Max(3, p.Code.Length / 3)) x.Note(x.L("diag-meth-unit-maybe", near), 2);
                }
                double s = SuggestedPrice(p);
                if (missing && s > 0) r.Add(FixLink(x, "energyprice", p.Code + "=" + s.ToString("R", CultureInfo.InvariantCulture), x.L("diag-fix-energyprice", N(s))), EmcLineStyle.Text, 2);
                if (missing) x.Note(x.L("diag-meth-price-byhand", p.Code), 2);
            }
            if (!prices.ChargeEnergy) { x.Note(x.L("diag-meth-energy-off"), 1); r.Add(FixLink(x, "methodcost", "energy=true", x.L("diag-fix-methodcost", "ChargeMethodEnergy", "true")), EmcLineStyle.Text, 2); }
            if (!prices.ChargeHeat) { x.Note(x.L("diag-meth-heat-off"), 1); r.Add(FixLink(x, "methodcost", "heat=true", x.L("diag-fix-methodcost", "ChargeMethodHeat", "true")), EmcLineStyle.Text, 2); }
            if (!prices.ChargeWear) { x.Note(x.L("diag-meth-wear-off"), 1); r.Add(FixLink(x, "methodcost", "wear=true", x.L("diag-fix-methodcost", "ChargeMethodWear", "true")), EmcLineStyle.Text, 2); }
            x.Note(x.L("diag-meth-heatrate", N(prices.HeatRate), prices.HeatSource), 1);
            if (prices.SecondRate > 0) x.Note(x.L("diag-meth-secondrate", N(prices.SecondRate)), 1);
            return problems;
        }

        sealed class PowerFarm
        {
            public MethodRecipe Recipe;
            public double PerSecond, EnergyEmc, Seconds;
            /// <summary>The unit that adds the most, and its price.</summary>
            public string Unit;
            public double Price;
            /// <summary>Every unit the recipe uses → (its EMC per run, its price a unit).</summary>
            public Dictionary<string, (double emc, double price)> ByUnit = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        }

        static PowerFarm PowerUse(MethodRecipe recipe, Conversion conv)
        {
            if (conv?.Costs == null) return null;
            var energy = conv.Costs.Where(k => k.Kind == "energy" && k.Emc > 0).ToList();
            double secs = recipe.Costs.Seconds;
            if (energy.Count == 0 || secs <= 0) return null;
            var top = energy.OrderByDescending(k => k.Emc).First();
            var f = new PowerFarm { Recipe = recipe, EnergyEmc = energy.Sum(k => k.Emc), Seconds = secs, Unit = top.Unit, Price = top.Price };
            f.PerSecond = f.EnergyEmc / secs;
            foreach (var g in energy.GroupBy(k => k.Unit ?? "")) f.ByUnit[g.Key] = (g.Sum(k => k.Emc), g.First().Price);
            return f;
        }

        /// <summary>Recipes in use whose power cost alone adds more than the limit per second of machine time.</summary>
        static List<PowerFarm> PowerFarms(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs, out int unknown)
        {
            var result = new List<PowerFarm>();
            unknown = 0;
            foreach (var kv in convs)
            {
                if (OutcomeOf(x, kv.Value, out Conversion used, out _) != MethodOutcome.Used || used.Costs == null) continue;
                if (!used.Costs.Any(k => k.Kind == "energy" && k.Emc > 0)) continue;
                PowerFarm f = PowerUse(kv.Key, used);
                if (f == null) { unknown++; continue; }
                result.Add(f);
            }
            return result.OrderByDescending(f => f.PerSecond).ToList();
        }

        /// <summary>
        /// The highest price for a unit at which no recipe using it makes more than the limit a second from power: every
        /// copy of every recipe with a time, beaten ones too (a lower price can make one the cheapest way), with the other
        /// units it uses at their prices. 0 when lowering this unit alone can't get one under the limit.
        /// </summary>
        static double SafePrice(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs, string unit)
        {
            double best = double.PositiveInfinity;
            foreach (var kv in convs)
            {
                foreach (Conversion conv in kv.Value)
                {
                    PowerFarm f = PowerUse(kv.Key, conv);
                    if (f == null || !f.ByUnit.TryGetValue(unit, out var mine) || mine.emc <= 0 || mine.price <= 0) continue;
                    double others = f.EnergyEmc - mine.emc;
                    double room = FreePowerLimit * f.Seconds - others;
                    if (room <= 0) return 0;
                    double p = mine.price * room / mine.emc;
                    if (p < best) best = p;
                }
            }
            return double.IsPositiveInfinity(best) ? 0 : FloorSignificant(best);
        }

        /// <summary>Card 4: windmills and water wheels make power for nothing, so power charged into a value is EMC from nothing.</summary>
        static int FreePowerCard(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs)
        {
            EmcReport r = x.R;
            var farms = PowerFarms(x, convs, out int unknown);
            var over = farms.Where(f => f.PerSecond > FreePowerLimit).ToList();
            if (farms.Count == 0 && unknown == 0) return 0;
            if (over.Count == 0)
            {
                x.Ok(x.L("diag-meth-power-ok", farms.Count, F(farms.Count > 0 ? farms[0].PerSecond : 0), F(FreePowerLimit)));
                x.Explain(x.L("diag-meth-power-why"));
                if (unknown > 0) x.Note(x.L("diag-meth-power-unknown", unknown));
                return 0;
            }
            x.Warn(x.L("diag-meth-power-over", over.Count, F(over[0].PerSecond), F(FreePowerLimit)));
            x.Explain(x.L("diag-meth-power-why"));
            foreach (PowerFarm f in over.Take(8))
                r.Add(x.L("diag-meth-power-line", f.Recipe.FullCode, F(f.EnergyEmc), N(f.Seconds), F(f.PerSecond)), EmcLineStyle.Text, 1);
            foreach (string unit in over.Select(f => f.Unit).Distinct())
            {
                double safe = SafePrice(x, convs, unit);
                if (safe > 0) r.Add(FixLink(x, "safeprice", unit, x.L("diag-fix-safeprice", unit, N(safe))), EmcLineStyle.Text, 1);
            }
            foreach (string method in over.Select(f => f.Recipe.Method.Code).Distinct().Take(4))
                r.Add(FixLink(x, "method", "off:" + method, x.L("diag-fix-method-off", method)), EmcLineStyle.Text, 1);
            if (unknown > 0) x.Note(x.L("diag-meth-power-unknown", unknown));
            x.Fix(x.L("diag-meth-power-fix"));
            return 1;
        }

        /// <summary>Card 5 (info): per method, how many things take their value from it and how many are made more cheaply another way.</summary>
        static void CostsCountCard(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs)
        {
            EmcReport r = x.R;
            var byMethod = convs.GroupBy(kv => kv.Key.Method).ToList();
            if (byMethod.Count == 0) return;
            int usedTotal = 0, beatenTotal = 0, setTotal = 0;
            var lines = new List<(string text, MethodDef m)>();
            foreach (var g in byMethod)
            {
                int used = 0, beaten = 0, set = 0, waiting = 0;
                foreach (var kv in g)
                {
                    switch (OutcomeOf(x, kv.Value, out _, out _))
                    {
                        case MethodOutcome.Used: used++; break;
                        case MethodOutcome.Beaten: beaten++; break;
                        case MethodOutcome.Set: set++; break;
                        default: waiting++; break;
                    }
                }
                usedTotal += used;
                beatenTotal += beaten;
                setTotal += set;
                lines.Add((x.L("diag-meth-countline", g.Key.DisplayName, g.Key.Code, used, beaten, waiting, set), g.Key));
            }
            x.Info(x.L("diag-meth-count", usedTotal, beatenTotal, setTotal));
            x.Explain(x.L("diag-meth-count-why"));
            foreach (var (text, m) in lines) r.Add(text + "  " + Link(ActionMethods, m.Code, x.L("diag-meth-look")), EmcLineStyle.Text, 1);
        }

        /// <summary>Card 6: recipes that give nothing a value because an ingredient, or something they also make, has none.</summary>
        static int StuckCard(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs)
        {
            EmcReport r = x.R;
            var stuck = new List<(MethodRecipe recipe, List<CollectibleObject> lacking, bool byproduct)>();
            foreach (var kv in convs)
            {
                if (OutcomeOf(x, kv.Value, out _, out List<CollectibleObject> lacking) != MethodOutcome.Waiting) continue;
                Conversion first = kv.Value[0];
                if (WaitsOnBlacklist(x, first, lacking)) continue; // blacklisted on purpose: nothing to fix
                // What it also makes, not what it hands back (a bucket): those are the recipe's byproducts.
                bool byproduct = lacking.Count > 0 && first.Method?.Byproducts != null && lacking.All(l => first.Method.Byproducts.Contains(l));
                stuck.Add((kv.Key, lacking, byproduct));
            }
            if (stuck.Count == 0) return 0;
            x.Warn(x.L("diag-meth-stuck", stuck.Count, stuck.Count(s => s.byproduct)));
            x.Explain(x.L("diag-meth-stuck-why"));
            foreach (var s in stuck.Take(8))
            {
                r.Add(s.recipe.FullCode + ": " + (s.lacking.Count == 0 ? WaitingText(x, convs[s.recipe][0], s.lacking) : x.L(s.byproduct ? "diag-meth-stuck-byproduct" : "diag-meth-stuck-ingredient", string.Join(", ", s.lacking.Take(4).Select(EmcReport.Ref)))), EmcLineStyle.Text, 1);
                foreach (CollectibleObject l in s.lacking.Take(2))
                {
                    r.Add(Link(ActionItem, l.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 2);
                    SuggestPin(x, l);
                }
            }
            if (stuck.Count > 8) x.Note(x.L("diag-more", stuck.Count - 8));
            x.Fix(x.L("diag-meth-stuck-fix"));
            return 1;
        }

        static string Signature(Conversion conv)
        {
            var ins = conv.Inputs.Where(i => !i.NoInfer).Select(i => string.Join("|", i.Choices.Select(c => c.Code.ToString()).OrderBy(s => s, StringComparer.Ordinal)) + "x" + N(i.Quantity)).OrderBy(s => s, StringComparer.Ordinal);
            return conv.Output?.Code + "<=" + string.Join("+", ins) + "/" + N(conv.OutputQuantity);
        }

        /// <summary>Card 7: free copies of a method's recipes (read elsewhere without the cost), and supersede entries that match nothing.</summary>
        static int FreeCopiesCard(Ctx x)
        {
            EmcReport r = x.R;
            var owners = x.Report.RecipeListOwners ?? new Dictionary<string, string>();
            // Only a copy read from one of the method's own mod's recipe lists (or a mod it names in requiresMod) can be
            // superseded; hand crafting, the game's lists and other mods' lists are other ways of making it, which may be cheaper.
            bool OwnList(Conversion copy, MethodDef def) => copy.Source != null && owners.TryGetValue(copy.Source, out string owner) && owner != null
                && (owner == def.ModId || def.Doc.RequiresMods.Contains(owner));
            var withCost = x.Report.Conversions.Where(c => c.Method != null && c.Output != null && c.ExtraCost > 0).ToList();
            var others = x.Report.Conversions.Where(c => c.Method == null && c.Output != null && c.Source != null && owners.ContainsKey(c.Source)).GroupBy(c => c.Output).ToDictionary(g => g.Key, g => g.ToList());
            var copies = new List<(Conversion method, Conversion copy)>();
            foreach (Conversion m in withCost)
            {
                if (!others.TryGetValue(m.Output, out var candidates)) continue;
                string sig = Signature(m);
                Conversion copy = candidates.FirstOrDefault(c => c.ExtraCost < m.ExtraCost && Signature(c) == sig && OwnList(c, m.Method.Method));
                if (copy != null) copies.Add((m, copy));
            }
            // Entries that named nothing, of methods in use this mapping (a switched-off method's entries replace nothing on purpose).
            var inUse = new HashSet<MethodDef>(x.Report.Conversions.Where(c => c.Method?.Method != null).Select(c => c.Method.Method));
            var dead = new List<(MethodDef m, string entry)>();
            var refused = new List<(MethodDef m, string entry, string owner)>();
            foreach (MethodDef m in Snap(x).AllMethods.Where(inUse.Contains))
                foreach (string s in m.Supersedes)
                {
                    if (x.Report.SupersedeRefused.TryGetValue(s, out string owner)) refused.Add((m, s, owner));
                    else if (!x.Report.SupersedeHits.Contains(s)) dead.Add((m, s));
                }
            if (copies.Count == 0 && dead.Count == 0 && refused.Count == 0)
            {
                if (x.Report.SupersededLists.Count > 0) { x.Ok(x.L("diag-meth-copies-superseded", x.Report.SupersededLists.Count)); foreach (var kv in x.Report.SupersededLists) x.Note(kv.Key + " ← " + kv.Value); }
                return 0;
            }
            int problems = 0;
            if (copies.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-meth-copies", copies.Count));
                x.Explain(x.L("diag-meth-copies-why"));
                foreach (var g in copies.GroupBy(c => c.copy.Source).Take(6))
                {
                    var (method, copy) = g.First();
                    r.Add(x.L("diag-meth-copyline", g.Count(), copy.Source, method.Method.Method.Code), EmcLineStyle.Text, 1);
                    x.Note(EmcReport.Ref(method.Output) + ": " + Linkify(x, EmcMapper.Describe(copy)), 2);
                    x.Note(x.L("diag-meth-copy-fix", copy.Source, method.Method.ModId), 2);
                }
                x.Note(x.L("diag-meth-copy-comment", VintageEModSystem.ModDbUrl), 1);
            }
            if (dead.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-meth-supersede-dead", dead.Count));
                foreach (var (m, entry) in dead.Take(8)) r.Add(m.Code + ": \"" + entry + "\"", EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-meth-supersede-dead-fix"));
            }
            if (refused.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-meth-supersede-refused-head", refused.Count));
                foreach (var (m, entry, owner) in refused.Take(8)) r.Add(x.L("diag-meth-supersede-refused", m.Code, entry, ModName(x, owner)), EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-meth-supersede-refused-fix"));
            }
            return problems;
        }

        /// <summary>Recipe data another mod keeps where Vintage-E doesn't look: folders under recipes/ and block/item attributes named …Recipes.</summary>
        sealed class Unseen
        {
            public string Domain;
            public SortedDictionary<string, int> Folders = new SortedDictionary<string, int>(StringComparer.Ordinal);
            /// <summary>Attribute name → the blocks/items that have it.</summary>
            public SortedDictionary<string, List<CollectibleObject>> Attributes = new SortedDictionary<string, List<CollectibleObject>>(StringComparer.Ordinal);
        }

        static readonly HashSet<string> KnownRecipeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "grid", "smithing", "knapping", "clayforming", "barrel", "alloy", "cooking", "cookingpot" };
        static readonly HashSet<string> BaseDomains = new HashSet<string>(StringComparer.Ordinal) { "game", "survival", "creative", "vintagee" };

        /// <summary>The mods whose recipe data Vintage-E can't read (for the self-test, which drafts a method file for each).</summary>
        public static List<string> UnseenDomains(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            var x = new Ctx { Mod = mod, Sapi = sapi, LangCode = "en", R = new EmcReport(), Report = mod.LastReport, Index = new CollectibleIndex(sapi.World.Collectibles) };
            return UnseenRecipeData(x).Select(u => u.Domain).ToList();
        }

        static List<Unseen> UnseenRecipeData(Ctx x)
        {
            var byDomain = new Dictionary<string, Unseen>(StringComparer.Ordinal);
            Unseen Of(string d) => byDomain.TryGetValue(d, out Unseen u) ? u : byDomain[d] = new Unseen { Domain = d };
            var snap = Snap(x);
            // A mod is covered by its own documents, and by Vintage-E's files for it (they say so with requiresMod).
            var covered = new HashSet<string>(snap.Docs.Select(d => d.ModId).Concat(snap.Docs.SelectMany(d => d.RequiresMods)), StringComparer.Ordinal);
            try
            {
                // Recipe lists Vintage-E found on mod systems when mapping (by a squashed name), so a folder that one of them
                // loads isn't listed. Taken from the mapping: reading mod systems' properties here would run other mods'
                // code on the report's worker thread. (A mapping from the cache has none: then they're read here, as a
                // background mapping does anyway.)
                IEnumerable<string> names = x.Report?.RecipeListOwners != null && x.Report.RecipeListOwners.Count > 0
                    ? x.Report.RecipeListOwners.Keys.Select(l => l + " " + (x.Report.RecipeListTypes != null && x.Report.RecipeListTypes.TryGetValue(l, out string t) ? t : ""))
                    : ConversionCollector.DiscoverRecipeLists(x.Sapi, x.Sapi.Logger).Select(l => (l.Label ?? "") + " " + (l.ElementType?.Name ?? ""));
                var labels = names.Select(l => Regex.Replace(l, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant()).ToList();
                foreach (IAsset asset in x.Sapi.Assets.GetMany("recipes/", null, false))
                {
                    string domain = asset.Location.Domain;
                    if (BaseDomains.Contains(domain) || covered.Contains(domain)) continue;
                    // A mod's files for another mod (compatibility/<mod>/recipes/...) are listed under that mod's recipes
                    // but keep their own path: those are the game's recipe types (grid ones, usually), which are read.
                    if (!asset.Location.Path.StartsWith("recipes/", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] parts = asset.Location.Path.Split('/');
                    if (parts.Length < 3) continue;
                    string folder = parts[1];
                    if (KnownRecipeFolders.Contains(folder)) continue;
                    string squashed = Regex.Replace(folder, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
                    if (squashed.Length > 2 && labels.Any(l => l.Contains(squashed))) continue;
                    Unseen u = Of(domain);
                    u.Folders[folder] = u.Folders.TryGetValue(folder, out int n) ? n + 1 : 1;
                }
            }
            catch (Exception e) { x.Sapi.Logger.VerboseDebug("Vintage-E: could not list recipe folders: {0}", e.Message); }
            foreach (CollectibleObject c in x.Index?.All ?? (IReadOnlyList<CollectibleObject>)Array.Empty<CollectibleObject>())
            {
                string domain = c.Code.Domain;
                if (BaseDomains.Contains(domain) || covered.Contains(domain)) continue;
                if (!(c.Attributes?.Token is JObject attrs)) continue;
                foreach (JProperty p in attrs.Properties())
                {
                    if (!p.Name.EndsWith("recipes", StringComparison.OrdinalIgnoreCase) || !(p.Value is JArray arr) || arr.Count == 0 || !(arr[0] is JObject)) continue;
                    Unseen u = Of(domain);
                    if (!u.Attributes.TryGetValue(p.Name, out var list)) u.Attributes[p.Name] = list = new List<CollectibleObject>();
                    list.Add(c);
                }
            }
            return byDomain.Values.Where(u => u.Folders.Count + u.Attributes.Count > 0).OrderBy(u => u.Domain, StringComparer.Ordinal).ToList();
        }

        /// <summary>Card 8 (info): mods whose recipe data Vintage-E can't read, so their outputs are valued another way or guessed.</summary>
        static void UnseenCard(Ctx x, List<Unseen> unseen, bool full)
        {
            if (unseen.Count == 0) return;
            EmcReport r = x.R;
            x.Info(x.L("diag-meth-unseen", unseen.Count, string.Join(", ", unseen.Take(5).Select(u => u.Domain)) + (unseen.Count > 5 ? ", …" : "")));
            x.Explain(x.L("diag-meth-unseen-why"));
            foreach (Unseen u in unseen.Take(full ? 30 : 8))
            {
                var parts = u.Folders.Select(f => "recipes/" + f.Key + " (" + f.Value + ")").Concat(u.Attributes.Select(a => a.Key + " (" + x.L("diag-meth-unseen-on", a.Value.Count, a.Value[0].Code) + ")"));
                r.Add(ModName(x, u.Domain) + ": " + string.Join(", ", parts) + "  " + Link(ActionDraftMethods, u.Domain, x.L("diag-meth-draft")), EmcLineStyle.Text, 1);
            }
            x.Note(x.L("diag-meth-unseen-comment", VintageEModSystem.ModDbUrl), 1);
        }

        /// <summary>Card 9: switched-off methods (with Enable) and DisabledMethods/EnergyEmc entries that name nothing.</summary>
        static int SwitchedOffCard(Ctx x)
        {
            EmcReport r = x.R;
            var disabled = Copy(x.Mod.Config.DisabledMethods);
            var methods = Snap(x).AllMethods.ToList();
            var off = methods.Where(m => MethodSnapshot.IsDisabled(m, null, disabled)).ToList();
            var deadOff = DeadDisabledMethods(x.Mod);
            var deadPrices = DeadEnergyPrices(x.Mod);
            if (off.Count == 0 && deadOff.Count == 0 && deadPrices.Count == 0 && disabled.Count == 0) return 0;
            if (off.Count > 0 || disabled.Count > 0)
            {
                x.Info(x.L("diag-meth-off-card", off.Count, disabled.Count));
                foreach (string entry in disabled.Where(e => !deadOff.Contains(e)))
                    r.Add(entry + "  " + FixLink(x, "method", "on:" + entry, x.L("diag-fix-method-on", entry)), EmcLineStyle.Text, 1);
            }
            if (deadOff.Count + deadPrices.Count > 0)
            {
                x.Warn(x.L("diag-meth-dead", deadOff.Count + deadPrices.Count));
                foreach (string e in deadOff) r.Add("DisabledMethods: " + e, EmcLineStyle.Text, 1);
                foreach (string e in deadPrices) r.Add("EnergyEmc: " + e, EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-meth-dead-fix"));
                return 1;
            }
            return 0;
        }

        /// <summary>Whether the switched-off card would show nothing at all (nothing switched off, no stale entries).</summary>
        static bool SwitchedOffCardShowsNothing(Ctx x) => Copy(x.Mod.Config.DisabledMethods).Count == 0 && DeadEnergyPrices(x.Mod).Count == 0;

        /// <summary>DisabledMethods entries that name no loaded mod, method or recipe.</summary>
        static List<string> DeadDisabledMethods(VintageEModSystem mod)
        {
            var methods = mod.Methods.AllMethods.ToList();
            var result = new List<string>();
            foreach (string e in Copy(mod.Config.DisabledMethods))
            {
                if (string.IsNullOrWhiteSpace(e)) continue;
                var single = new[] { e };
                bool any = methods.Any(m => MethodSnapshot.IsDisabled(m, null, single) || m.Recipes.Any(rc => MethodSnapshot.IsDisabled(m, rc, single)));
                if (!any) result.Add(e);
            }
            return result;
        }

        /// <summary>EnergyEmc entries for units no document declares and no recipe uses (only once methods have been collected).</summary>
        static List<string> DeadEnergyPrices(VintageEModSystem mod)
        {
            if (!mod.Methods.Collected) return new List<string>();
            var known = new HashSet<string>(mod.Methods.Units.Keys, StringComparer.Ordinal);
            foreach (MethodDef m in mod.Methods.AllMethods) foreach (MethodRecipe rc in m.Recipes) foreach (var (unit, _) in rc.Costs.Energy) known.Add(unit);
            return Copy(mod.Config.EnergyEmc?.Keys).Where(k => !string.IsNullOrWhiteSpace(k) && !known.Contains(k.Trim().ToLowerInvariant()) && !known.Contains("game:" + k.Trim().ToLowerInvariant())).ToList();
        }

        // ---------------------------------------------------------------- /emc methods

        static void MethodsReport(Ctx x, string arg)
        {
            EmcReport r = x.R;
            arg = (arg ?? "").Trim();
            MethodSnapshot snap = Snap(x);
            if (arg.Equals("export", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                string modId = arg.Substring(6).Trim().ToLowerInvariant();
                r.Section(x.L("diag-sec-meth-export", modId));
                Export(x, modId);
                MethodsBack(x);
                return;
            }
            string mod = null, method = null;
            if (arg.Length > 0)
            {
                string a = arg.ToLowerInvariant();
                if (snap.AllMethods.Any(m => m.Code == a)) method = a;
                else if (snap.Docs.Any(d => d.ModId == a)) mod = a;
                else
                {
                    r.Section(x.L("diag-sec-meth"));
                    x.Warn(x.L("diag-meth-nosuch", arg));
                    var near = snap.AllMethods.Select(m => m.Code).Concat(snap.Mods).OrderBy(c => MethodParser.Distance(c, a)).FirstOrDefault();
                    if (near != null) r.Add(Link(ActionMethods, near, x.L("diag-meth-maybe", near)), EmcLineStyle.Text, 1);
                    MethodsBack(x);
                    return;
                }
            }
            if (mod == null && method == null)
            {
                r.Section(x.L("diag-sec-meth"));
                r.Add(x.L("diag-meth-help"), EmcLineStyle.Note);
                if (snap.Docs.Count == 0) x.Note(x.L("diag-meth-docs-none-why"), 0);
                DocumentsCard(x, snap, true, false);
                if (x.Report != null && !x.Report.FromCache && x.Report.MethodPrices != null && snap.MethodCount > 0)
                {
                    var convsAll = MethodConversions(x);
                    PricesCard(x);
                    FreePowerCard(x, convsAll);
                    StuckCard(x, convsAll);
                    FreeCopiesCard(x);
                }
                UnseenCard(x, UnseenRecipeData(x), true);
                SwitchedOffCard(x);
            }
            var convs = MethodConversions(x);
            var results = (x.Report?.MethodResults ?? new List<MethodRecipeResult>()).GroupBy(rr => rr.Recipe).ToDictionary(g => g.Key, g => g.First());
            var disabled = Copy(x.Mod.Config.DisabledMethods);
            if (mod != null && !snap.AllMethods.Any(m => m.ModId == mod))
            {
                // Every document of the mod is broken or waiting for a mod: say so rather than show nothing.
                r.Section(x.L("diag-sec-mod-methods"));
                foreach (MethodDoc d in snap.Docs.Where(d => d.ModId == mod))
                {
                    if (d.Error != null) x.Warn(x.L("diag-meth-broken", d.Source, d.Error));
                    else if (d.MissingMod != null) x.Info(x.L("diag-meth-waitingmod", d.Source, d.MissingMod));
                    else x.Info(x.L("diag-methodcheck-doc", d.Source, d.Route, d.Format, d.Methods.Count, d.RecipeCount));
                }
                r.Add(Link(ActionMethodCheck, mod, x.L("diag-meth-check")), EmcLineStyle.Text, 1);
            }
            foreach (MethodDef m in snap.AllMethods.Where(m => (mod == null || m.ModId == mod) && (method == null || m.Code == method)))
            {
                r.Section(x.L("diag-sec-meth-one", m.DisplayName, m.Code));
                bool off = MethodSnapshot.IsDisabled(m, null, disabled);
                string head = x.L("diag-meth-head", ModName(x, m.ModId), m.Recipes.Count, x.L(m.Kind == ConversionKind.Processing ? "diag-meth-kind-processing" : "diag-meth-kind-crafting"), m.Doc.Source);
                if (off) x.Info(head + " " + x.L("diag-meth-isoff")); else x.Ok(head);
                if (!string.IsNullOrEmpty(m.Machine))
                {
                    CollectibleObject machine = MethodParser.FindOne(x.Sapi.World, m.Machine);
                    x.Note(x.L("dumpgui-method-machine", machine != null ? EmcReport.Ref(machine) : m.Machine), 1);
                }
                if (!string.IsNullOrEmpty(m.Description)) x.Note(m.Description, 1);
                if (m.ReadRecipes > 0) x.Note(x.L("diag-meth-readcount", m.ReadRecipes), 1);
                r.Add(off ? FixLink(x, "method", "on:" + m.Code, x.L("diag-fix-method-on", m.Code)) : FixLink(x, "method", "off:" + m.Code, x.L("diag-fix-method-off", m.Code)), EmcLineStyle.Text, 1);
                if (x.Report == null || x.Report.FromCache) { x.Note(x.L("diag-meth-cached"), 1); continue; }
                int shown = 0, limit = method != null ? 200 : 40;
                foreach (MethodRecipe recipe in m.Recipes)
                {
                    if (++shown > limit) { x.Note(x.L("diag-more", m.Recipes.Count - limit), 1); break; }
                    convs.TryGetValue(recipe, out var list);
                    results.TryGetValue(recipe, out MethodRecipeResult res);
                    RecipeLine(x, recipe, list, res, 1);
                    if (method != null && list != null && list.Count > 0 && list[0].Costs != null)
                        foreach (MethodCostLine k in list[0].Costs) x.Note(CostText(x, k), 2);
                    if (method != null && !string.IsNullOrEmpty(recipe.Note)) x.Note(recipe.Note, 2);
                }
            }
            MethodsBack(x);
        }

        static string CostText(Ctx x, MethodCostLine k)
        {
            switch (k.Kind)
            {
                case "energy": return k.Charged ? x.L("dumpgui-cost-energy", N(k.Amount), UnitName(x, k.Unit), N(k.Price), F(k.Emc), k.PriceSource ?? "?") : x.L("dumpgui-cost-energy-off", N(k.Amount), UnitName(x, k.Unit));
                case "heat": return k.Charged ? x.L("dumpgui-cost-heat", N(k.Amount), N(k.Price), F(k.Emc), k.PriceSource ?? "?") : x.L("dumpgui-cost-heat-off", N(k.Amount));
                case "time": return x.L("dumpgui-cost-time", N(k.Amount), N(k.Price), F(k.Emc));
                default: return x.L("dumpgui-cost-emc", F(k.Emc));
            }
        }

        static void MethodsBack(Ctx x)
        {
            x.R.Section(x.L("diag-sec-next"));
            x.R.Add(Link(ActionMethods, "", x.L("diag-q-methods")));
            foreach (string mod in Snap(x).Mods.Take(6)) x.R.Add(Link(ActionMethodCheck, mod, x.L("diag-q-methodcheck", ModName(x, mod))));
            x.R.Add(FixLink(x, "methodsreload", "", x.L("diag-fix-methodsreload")));
            x.R.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- what Vintage-E understood, for the mod's author

        /// <summary>Writes ModData/vintagee/methods-&lt;modid&gt;.json: every recipe as Vintage-E read it, with its costs, prices and what became of it.</summary>
        static string Export(Ctx x, string modId)
        {
            var docs = Snap(x).Docs.Where(d => d.ModId == modId).ToList();
            if (docs.Count == 0) { x.Warn(x.L("diag-meth-nodocs", modId)); return null; }
            var convs = MethodConversions(x);
            var results = (x.Report?.MethodResults ?? new List<MethodRecipeResult>()).GroupBy(rr => rr.Recipe).ToDictionary(g => g.Key, g => g.First());
            var root = new JObject
            {
                ["mod"] = modId,
                ["vintagee"] = x.Mod.Version ?? "?",
                ["format"] = MethodParser.Format,
                ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                ["fromCache"] = x.Report == null || x.Report.FromCache
            };
            var jdocs = new JArray();
            foreach (MethodDoc d in docs)
            {
                var jd = new JObject { ["source"] = d.Source, ["route"] = d.Route, ["format"] = d.Format, ["usable"] = d.Usable };
                if (d.Error != null) jd["error"] = d.Error;
                if (d.MissingMod != null) jd["waitingFor"] = d.MissingMod;
                jd["problems"] = new JArray(d.Problems.Select(p => (JToken)new JObject { ["path"] = p.Path, ["message"] = p.Message, ["leftOut"] = p.Error }));
                var jm = new JArray();
                foreach (MethodDef m in d.Methods)
                {
                    var jr = new JArray();
                    foreach (MethodRecipe recipe in m.Recipes)
                    {
                        var o = new JObject { ["code"] = recipe.Code, ["path"] = recipe.Path };
                        if (recipe.ReadFrom != null) o["readFrom"] = recipe.ReadFrom;
                        results.TryGetValue(recipe, out MethodRecipeResult res);
                        if (res?.Disabled == true) o["state"] = "switched off";
                        else if (res?.Skipped != null) { o["state"] = "skipped"; o["why"] = res.Skipped; }
                        if (res != null && res.MissingVariants > 0) o["variantsMissing"] = res.MissingVariants;
                        if (res != null && res.FailedVariants > 0) { o["variantsLeftOut"] = res.FailedVariants; o["variantsLeftOutWhy"] = res.VariantFailure; }
                        if (res != null && res.Notes.Count > 0) o["notes"] = new JArray(res.Notes);
                        if (convs.TryGetValue(recipe, out var list) && list.Count > 0)
                        {
                            MethodOutcome outcome = OutcomeOf(x, list, out Conversion used, out var lacking);
                            o["state"] = outcome.ToString().ToLowerInvariant();
                            if (lacking.Count > 0) o["waitingFor"] = new JArray(lacking.Select(l => l.Code.ToString()));
                            Conversion c = used ?? list[0];
                            o["copies"] = list.Count;
                            o["inputs"] = new JArray(c.Inputs.Select(i => (JToken)new JObject { ["choices"] = new JArray(i.Choices.Take(12).Select(ch => ch.Code.ToString())), ["choiceCount"] = i.Choices.Length, ["quantity"] = i.Quantity, ["toolWear"] = i.NoInfer }));
                            if (c.JointOutputs != null) o["sharedOutputs"] = new JArray(c.JointOutputs.Select(jo => (JToken)new JObject { ["code"] = jo.Key.Code.ToString(), ["quantity"] = jo.Value }));
                            else o["output"] = new JObject { ["code"] = c.Output?.Code.ToString(), ["quantity"] = c.OutputQuantity };
                            if (c.Returned != null) o["takenOff"] = new JArray(c.Returned.Select(b => (JToken)new JObject { ["code"] = b.Key.Code.ToString(), ["quantity"] = b.Value }));
                            if (c.Costs != null) o["costs"] = new JArray(c.Costs.Select(k => (JToken)new JObject { ["kind"] = k.Kind, ["unit"] = k.Unit, ["amount"] = k.Amount, ["price"] = k.Price, ["emc"] = k.Emc, ["priceFrom"] = k.PriceSource, ["charged"] = k.Charged }));
                            // Only a whole cost: one with an ingredient or byproduct still unvalued would be too low.
                            double? cost = RunCost(x, c, new List<CollectibleObject>());
                            if (cost.HasValue) o["costPerRun"] = cost.Value;
                            foreach (CollectibleObject outC in OutputsOf(c)) if (outC != null && x.Report.Values.TryGetValue(outC, out double v)) { o["outputValue"] = v; break; }
                        }
                        jr.Add(o);
                    }
                    jm.Add(new JObject { ["code"] = m.Code, ["name"] = m.DisplayName, ["machine"] = m.Machine, ["kind"] = m.Kind.ToString().ToLowerInvariant(), ["supersedes"] = new JArray(m.Supersedes), ["recipes"] = jr });
                }
                jd["methods"] = jm;
                jdocs.Add(jd);
            }
            root["documents"] = jdocs;
            if (x.Report?.MethodPrices != null)
            {
                var units = new JObject();
                foreach (MethodUnitPrice p in UsedUnits(x).Where(p => p.ModId == modId || !p.Declared)) units[p.Code] = new JObject { ["name"] = p.Name, ["price"] = p.Price, ["from"] = p.SourceText, ["generators"] = new JArray(p.Generators) };
                root["energyUnits"] = units;
            }
            string file = null;
            try
            {
                file = Path.Combine(VintageEModSystem.DataFolder(x.Sapi), "methods-" + Regex.Replace(modId, "[^a-z0-9_.-]", "_") + ".json");
                File.WriteAllText(file, root.ToString(Formatting.Indented));
                x.Ok(x.L("diag-meth-exported", file));
            }
            catch (Exception e) { x.Warn(x.L("diag-meth-export-failed", e.Message)); }
            return file;
        }

        // ---------------------------------------------------------------- methodcheck: for the mod's author

        static void MethodCheck(Ctx x, string modId)
        {
            EmcReport r = x.R;
            modId = (modId ?? "").Trim().ToLowerInvariant();
            r.Section(x.L("diag-sec-methodcheck", ModName(x, modId)));
            r.Add(x.L("diag-methodcheck-help"), EmcLineStyle.Note);
            var docs = Snap(x).Docs.Where(d => d.ModId == modId).ToList();
            if (docs.Count == 0)
            {
                x.Warn(x.L("diag-meth-nodocs", modId));
                x.Note(x.L("diag-meth-docs-none-why"), 1);
                if (UnseenRecipeData(x).Any(u => u.Domain == modId)) r.Add(Link(ActionDraftMethods, modId, x.L("diag-meth-draft")), EmcLineStyle.Text, 1);
                MethodsBack(x);
                return;
            }
            foreach (MethodDoc d in docs)
            {
                string head = x.L("diag-methodcheck-doc", d.Source, d.Route, d.Format, d.Methods.Count, d.RecipeCount);
                if (d.Error != null) { x.Warn(head); r.Add(d.Error, EmcLineStyle.Warn, 1); continue; }
                if (d.MissingMod != null) { x.Info(head); x.Note(x.L("diag-meth-waitingmod", d.Source, d.MissingMod), 1); continue; }
                int errs = d.Problems.Count(p => p.Error), warns = d.Problems.Count(p => !p.Error);
                if (errs > 0) x.Warn(head + " " + x.L("diag-methodcheck-problems", errs, warns));
                else if (warns > 0) x.Info(head + " " + x.L("diag-methodcheck-problems", errs, warns));
                else x.Ok(head);
                foreach (MethodProblem p in d.Problems.Take(60))
                    r.Add((p.Error ? x.L("diag-methodcheck-leftout") + " " : "") + p + (p.BadCode != null ? Suggest(x, p.BadCode) : ""), p.Error ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                if (d.Problems.Count > 60) x.Note(x.L("diag-more", d.Problems.Count - 60));
            }
            if (x.Report == null || x.Report.FromCache)
            {
                x.Note(x.L("diag-meth-cached"), 0);
                MethodsBack(x);
                return;
            }
            var convs = MethodConversions(x);
            var results = x.Report.MethodResults.GroupBy(rr => rr.Recipe).ToDictionary(g => g.Key, g => g.First());
            foreach (MethodDef m in docs.Where(d => d.Usable).SelectMany(d => d.Methods))
            {
                r.Section(x.L("diag-sec-meth-one", m.DisplayName, m.Code));
                int used = 0, beaten = 0, set = 0, waiting = 0, skipped = 0, off = 0, optional = 0;
                Conversion example = null;
                foreach (MethodRecipe recipe in m.Recipes)
                {
                    results.TryGetValue(recipe, out MethodRecipeResult res);
                    if (res?.Disabled == true) { off++; continue; }
                    if (!convs.TryGetValue(recipe, out var list) || list.Count == 0)
                    {
                        if (ForAbsentMod(x, res?.BadCode)) { optional++; continue; }
                        skipped++;
                        r.Add(recipe.Code + ": " + x.L("diag-meth-skipped", res?.Skipped ?? x.L("diag-meth-notread")) + (res?.BadCode != null ? Suggest(x, res.BadCode) : ""), EmcLineStyle.Warn, 1);
                        x.Note(recipe.Path, 2);
                        continue;
                    }
                    switch (OutcomeOf(x, list, out Conversion u, out var lacking))
                    {
                        case MethodOutcome.Used: used++; example ??= u; if (res != null && (res.FailedVariants > 0 || res.Notes.Count > 0)) RecipeLine(x, recipe, list, res, 1); break;
                        case MethodOutcome.Beaten: beaten++; if (beaten <= 10) RecipeLine(x, recipe, list, res, 1); break;
                        case MethodOutcome.Set: set++; if (set <= 5) RecipeLine(x, recipe, list, res, 1); break;
                        default: waiting++; if (waiting <= 10) RecipeLine(x, recipe, list, res, 1); break;
                    }
                }
                x.Info(x.L("diag-methodcheck-counts", used, beaten, waiting, skipped, off, set));
                if (m.SwitchedOffInDocument > 0) x.Note(x.L("diag-methodcheck-enabledoff", m.SwitchedOffInDocument), 1);
                if (optional > 0) x.Note(x.L("diag-meth-optional", optional, string.Join(", ", m.Recipes.Select(rc => results.TryGetValue(rc, out var rr) ? rr.BadCode : null).Where(b => ForAbsentMod(x, b)).Select(DomainOf).Distinct().Take(6))), 1);
                foreach (string s in m.Supersedes)
                {
                    if (x.Report.SupersedeRefused.TryGetValue(s, out string owner)) r.Add(x.L("diag-meth-supersede-refused", m.Code, s, ModName(x, owner)), EmcLineStyle.Warn, 1);
                    else if (!x.Report.SupersedeHits.Contains(s)) r.Add(x.L("diag-methodcheck-supersede-dead", s), EmcLineStyle.Warn, 1);
                    else x.Note(x.L("diag-methodcheck-supersede-ok", s), 1);
                }
                if (example != null)
                {
                    x.Note(x.L("diag-methodcheck-example"), 1);
                    r.Add(RecipeText(x, example), EmcLineStyle.Text, 2);
                    foreach (ConversionInput i in example.Inputs)
                    {
                        CollectibleObject pick = i.Choices.Where(c => x.V(c) > 0).OrderBy(c => x.V(c)).FirstOrDefault();
                        r.Add("+ " + N(i.Quantity) + " " + (pick != null ? EmcReport.Ref(pick) + " × " + F(x.V(pick)) + " = " + F(x.V(pick) * i.Quantity) : EmcReport.Ref(i.Choices.FirstOrDefault()) + " " + x.L("diag-meth-novalue")), EmcLineStyle.Text, 3);
                        if (i.Note != null) x.Note(i.Note, 4);
                    }
                    foreach (MethodCostLine k in example.Costs ?? new List<MethodCostLine>()) r.Add("+ " + CostText(x, k), EmcLineStyle.Text, 3);
                    if (example.Returned != null) foreach (var b in example.Returned) r.Add("- " + N(b.Value) + " " + EmcReport.Ref(b.Key) + " × " + F(x.V(b.Key)) + " = " + F(x.V(b.Key) * b.Value), EmcLineStyle.Text, 3);
                    double? cost = RunCost(x, example, new List<CollectibleObject>());
                    if (cost.HasValue && example.JointOutputs == null) r.Add(x.L("diag-methodcheck-total", F(cost.Value), N(example.OutputQuantity), F(cost.Value / Math.Max(1e-9, example.OutputQuantity)), EmcReport.Ref(example.Output)), EmcLineStyle.Total, 3);
                    else if (cost.HasValue) r.Add(x.L("diag-meth-run", F(cost.Value)), EmcLineStyle.Total, 3);
                }
            }
            r.Section(x.L("diag-sec-meth-export", modId));
            Export(x, modId);
            MethodsBack(x);
        }

        // ---------------------------------------------------------------- draftmethods: a starting document from data Vintage-E can't read

        static readonly Regex InputField = new Regex(@"^(inputs?|ingredients?|in)(\d*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex OutputField = new Regex(@"^(outputs?|results?|out|products?)(\d*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex LiquidField = new Regex(@"^(liquid|fluid)(\d*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex LiquidOutField = new Regex(@"^(liquidoutput|outputliquid|fluidoutput|outputfluid)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TimeField = new Regex(@"^(seconds|time|duration|processingtime|processseconds|crafttime)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The field of <paramref name="o"/> named like one of <paramref name="names"/> (any case), or null.</summary>
        static string FieldLike(JObject o, params string[] names)
        {
            foreach (string n in names)
            {
                JProperty p = o.Properties().FirstOrDefault(pr => string.Equals(pr.Name, n, StringComparison.OrdinalIgnoreCase));
                if (p != null) return p.Name;
            }
            return null;
        }

        /// <summary>
        /// A stack template for a stack written the game's way, as an object ({ "type", "code", "stackSize" or "quantity",
        /// "litres", "name" }): each field read from <paramref name="prefix"/> ("$output." for a field, "$" inside "each").
        /// Null when the object has no code.
        /// </summary>
        static JObject StackFromObject(JObject stack, string prefix, bool input, ref bool wildcard)
        {
            string code = FieldLike(stack, "code", "item", "block");
            if (code == null || stack[code]?.Type != JTokenType.String) return null;
            var s = new JObject { ["code"] = prefix + code };
            string t = FieldLike(stack, "type");
            if (t != null) s["type"] = prefix + t;
            string q = FieldLike(stack, "stackSize", "quantity", "count", "qty");
            if (q != null) s["quantity"] = prefix + q;
            string l = FieldLike(stack, "litres", "liters");
            if (l != null) s["litres"] = prefix + l;
            if (input)
            {
                string n = FieldLike(stack, "name");
                if (n != null) s["name"] = prefix + n;
                string av = FieldLike(stack, "allowedVariants");
                if (av != null) s["allowedVariants"] = prefix + av;
                string sv = FieldLike(stack, "skipVariants");
                if (sv != null) s["skipVariants"] = prefix + sv;
                if (((string)stack[code]).Contains('*')) wildcard = true;
            }
            return s;
        }

        /// <summary>A reader's recipe template guessed from one sample recipe's field names; notes say what was guessed.</summary>
        static JObject GuessTemplate(JObject sample, List<string> notes)
        {
            var inputs = new JArray();
            var outputs = new JArray();
            var tmpl = new JObject();
            var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool wildcardIn = false, wildcardOut = false;
            foreach (JProperty p in sample.Properties())
            {
                bool isIn = InputField.IsMatch(p.Name), isOut = !isIn && OutputField.IsMatch(p.Name) && !LiquidOutField.IsMatch(p.Name);
                // Stacks written the game's way: an object, a list of them, or (grid recipes) a map of keys to them.
                if ((isIn || isOut) && p.Value is JObject one)
                {
                    JObject first = one.Properties().All(pp => pp.Value is JObject) && one.Count > 0 && FieldLike(one, "code", "item", "block") == null ? (JObject)one.Properties().First().Value : null;
                    JObject st = first != null ? StackFromObject(first, "$", isIn, ref wildcardIn) : StackFromObject(one, "$" + p.Name + ".", isIn, ref wildcardIn);
                    if (st == null) continue;
                    if (first != null)
                    {
                        st = new JObject(new JProperty("each", "$" + p.Name), st.Properties());
                        notes.Add(p.Name + " is a map of keys to stacks, as grid recipes have: each is counted once, so check the quantities against the pattern");
                    }
                    JObject src = first ?? one;
                    if (isOut && ((string)src[FieldLike(src, "code", "item", "block")] ?? "").Contains("{")) wildcardOut = true;
                    (isIn ? inputs : outputs).Add(st);
                    usedFields.Add(p.Name);
                    continue;
                }
                if ((isIn || isOut) && p.Value is JArray list && list.Count > 0)
                {
                    if (list[0] is JObject el)
                    {
                        JObject st = StackFromObject(el, "$", isIn, ref wildcardIn);
                        if (st == null) continue;
                        (isIn ? inputs : outputs).Add(new JObject(new JProperty("each", "$" + p.Name), st.Properties()));
                        usedFields.Add(p.Name);
                    }
                    else if (list[0].Type == JTokenType.String)
                    {
                        (isIn ? inputs : outputs).Add(new JObject { ["each"] = "$" + p.Name, ["code"] = "$" });
                        usedFields.Add(p.Name);
                        notes.Add(p.Name + " is a list of codes: each counts as one; check the quantities");
                    }
                    continue;
                }
                if (p.Value.Type != JTokenType.String) continue;
                string val = (string)p.Value;
                Match m;
                if ((m = InputField.Match(p.Name)).Success)
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string q = FieldLike(sample, p.Name + "Quantity", p.Name + "Qty", p.Name + "Count", p.Name + "StackSize", m.Groups[2].Value.Length == 0 ? "Quantity" : "\0");
                    if (q != null) { s["quantity"] = "$" + q; usedFields.Add(q); }
                    string t = FieldLike(sample, p.Name + "Type");
                    if (t != null) { s["type"] = "$" + t; usedFields.Add(t); }
                    if (val.Contains('*')) { s["name"] = "wildcard"; wildcardIn = true; }
                    inputs.Add(s);
                    usedFields.Add(p.Name);
                }
                else if (LiquidOutField.IsMatch(p.Name))
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string l = FieldLike(sample, p.Name + "Litres", p.Name + "Liters", "OutputLitres");
                    if (l != null) { s["litres"] = "$" + l; usedFields.Add(l); } else notes.Add("the liquid output " + p.Name + " has no litres field: check its amount");
                    outputs.Add(s);
                    usedFields.Add(p.Name);
                }
                else if ((m = OutputField.Match(p.Name)).Success)
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string q = FieldLike(sample, p.Name + "Quantity", p.Name + "Qty", p.Name + "Count", p.Name + "StackSize");
                    if (q != null) { s["quantity"] = "$" + q; usedFields.Add(q); }
                    string t = FieldLike(sample, p.Name + "Type");
                    if (t != null) { s["type"] = "$" + t; usedFields.Add(t); }
                    if (val.Contains("{wildcard}")) wildcardOut = true;
                    outputs.Add(s);
                    usedFields.Add(p.Name);
                }
                else if (LiquidField.IsMatch(p.Name))
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string l = FieldLike(sample, p.Name + "Litres", p.Name + "Liters", "Litres", "Liters");
                    if (l != null) { s["litres"] = "$" + l; usedFields.Add(l); } else notes.Add("the liquid " + p.Name + " has no litres field: check its amount");
                    inputs.Add(s);
                    usedFields.Add(p.Name);
                }
            }
            string time = sample.Properties().Select(p => p.Name).FirstOrDefault(n => TimeField.IsMatch(n));
            string code = FieldLike(sample, "Code", "Name", "Id");
            string enabled = FieldLike(sample, "enabled");
            if (code != null) { tmpl["code"] = "$" + code; usedFields.Add(code); }
            if (enabled != null) { tmpl["enabled"] = "$" + enabled; usedFields.Add(enabled); }
            tmpl["inputs"] = inputs;
            tmpl["outputs"] = outputs;
            if (time != null) { tmpl["costs"] = new JObject { ["seconds"] = "$" + time }; usedFields.Add(time); }
            if (inputs.Count == 0) notes.Add("no field looked like an ingredient: fill \"inputs\" by hand");
            if (outputs.Count == 0) notes.Add("no field looked like a result: fill \"outputs\" by hand");
            if (wildcardOut && !wildcardIn) notes.Add("an output uses a {placeholder} but no input has a *: check which input it comes from");
            notes.Add("no energy cost is known: add \"energy\": { \"unit\": ..., \"amount\": ... } to \"costs\" if the machine uses power, and declare the unit");
            var unknown = sample.Properties().Select(p => p.Name).Where(n => !usedFields.Contains(n)).ToList();
            if (unknown.Count > 0) notes.Add("fields not used (a tool, a die, a heat level, a second ingredient?): " + string.Join(", ", unknown));
            return tmpl;
        }

        /// <summary>JSON shown as report text: "{{", "}}" and "[[" are the report's own link marks, so they get a space, not removed.</summary>
        static string NoMarkup(string text) => text.Replace("[[", "[ [").Replace("]]", "] ]").Replace("{{", "{ {").Replace("}}", "} }");

        static void DraftMethods(Ctx x, string modId)
        {
            EmcReport r = x.R;
            modId = (modId ?? "").Trim().ToLowerInvariant();
            r.Section(x.L("diag-sec-draftmethods", ModName(x, modId)));
            Unseen u = UnseenRecipeData(x).FirstOrDefault(un => un.Domain == modId);
            if (u == null)
            {
                bool has = Snap(x).Docs.Any(d => d.ModId == modId);
                x.Ok(has ? x.L("diag-draftmethods-has", modId) : x.L("diag-draftmethods-nothing", modId));
                if (has) r.Add(Link(ActionMethodCheck, modId, x.L("diag-q-methodcheck", ModName(x, modId))));
                MethodsBack(x);
                return;
            }
            var methods = new JArray();
            var allNotes = new List<string>();
            var samples = new List<(string where, JObject sample)>();
            foreach (var kv in u.Attributes)
            {
                var notes = new List<string>();
                CollectibleObject first = kv.Value[0];
                string family = first.Code.Path.Split('-')[0];
                string pattern = (first.ItemClass == EnumItemClass.Block ? "block:" : "item:") + first.Code.Domain + ":" + family + (first.Code.Path.Contains('-') ? "-*" : "");
                JObject sample = (first.Attributes?.Token?[kv.Key] as JArray)?.FirstOrDefault() as JObject;
                if (sample == null) continue;
                JObject tmpl = GuessTemplate(sample, notes);
                samples.Add((kv.Key + " on " + first.Code, sample));
                string mcode = modId + ":" + Regex.Replace(kv.Key.ToLowerInvariant().Replace("recipes", ""), "[^a-z0-9_]", "");
                if (mcode.EndsWith(":")) mcode += family;
                methods.Add(new JObject
                {
                    ["//"] = "Drafted from the attribute " + kv.Key + " on " + kv.Value.Count + " block(s)/item(s), e.g. " + first.Code + ". " + string.Join(" ", notes.Select(n => "Check: " + n + ".")),
                    ["code"] = mcode,
                    ["name"] = EmcExplain.NameOf(first),
                    ["machine"] = pattern,
                    ["kind"] = "processing",
                    ["readers"] = new JArray(new JObject { ["from"] = "attribute", ["collectible"] = pattern, ["attribute"] = kv.Key, ["recipe"] = tmpl })
                });
                allNotes.AddRange(notes.Select(n => kv.Key + ": " + n));
            }
            foreach (var kv in u.Folders)
            {
                var notes = new List<string>();
                JObject sample = null;
                try
                {
                    IAsset asset = x.Sapi.Assets.GetMany("recipes/" + kv.Key + "/", modId, true).FirstOrDefault(a => a.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                    JToken fileToken = asset == null ? null : JToken.Parse(asset.ToText());
                    sample = fileToken as JObject ?? (fileToken as JArray)?.FirstOrDefault() as JObject;
                }
                catch (Exception e) { notes.Add("its first file could not be read (" + e.Message + ")"); }
                if (sample == null)
                {
                    // Say why the folder has no method in the draft, rather than leave it out without a word.
                    allNotes.Add("recipes/" + kv.Key + ": left out of the draft: " + (notes.Count > 0 ? string.Join("; ", notes) : "its first file isn't a recipe object (or a list of them)"));
                    continue;
                }
                JObject tmpl = GuessTemplate(sample, notes);
                samples.Add(("recipes/" + kv.Key, sample));
                methods.Add(new JObject
                {
                    ["//"] = "Drafted from the folder recipes/" + kv.Key + " (" + kv.Value + " files). " + string.Join(" ", notes.Select(n => "Check: " + n + ".")),
                    ["code"] = modId + ":" + Regex.Replace(kv.Key.ToLowerInvariant(), "[^a-z0-9_]", ""),
                    ["name"] = kv.Key,
                    ["kind"] = "processing",
                    ["readers"] = new JArray(new JObject { ["from"] = "assets", ["folder"] = "recipes/" + kv.Key, ["domain"] = modId, ["recipe"] = tmpl })
                });
                allNotes.AddRange(notes.Select(n => "recipes/" + kv.Key + ": " + n));
            }
            string modVersion = "?";
            try { modVersion = x.Sapi.ModLoader.GetMod(modId)?.Info?.Version ?? "?"; } catch { }
            var doc = new JObject
            {
                ["//"] = "Drafted by /emc diagnose draftmethods on " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " for " + ModName(x, modId) + " " + modVersion + " with Vintage-E " + (x.Mod.Version ?? "?")
                    + ". Each method reads the mod's own recipe data; the field names were guessed, so check every \"//\" note, then put the file in assets/" + modId + "/config/vintagee-methods/ (or share it on the Vintage-E Mod DB page). docs/crafting-methods.md describes the format.",
                ["format"] = MethodParser.Format,
                ["requiresMod"] = modId,
                ["methods"] = methods
            };
            string json = doc.ToString(Formatting.Indented);
            // Check it the way it would be read.
            MethodDoc test = MethodParser.Parse(json, modId, "draft", "file", x.Sapi, x.Index);
            string file = null;
            try
            {
                file = Path.Combine(VintageEModSystem.DataFolder(x.Sapi), "vintagee-methods-" + Regex.Replace(modId, "[^a-z0-9_.-]", "_") + ".json");
                File.WriteAllText(file, json);
            }
            catch (Exception e) { x.Sapi.Logger.Warning("Vintage-E: could not write the drafted methods file: {0}", e.Message); }
            r.Add(x.L("diag-draftmethods-made", methods.Count, test.RecipeCount, test.Problems.Count(p => p.Error)), EmcLineStyle.Total);
            if (file != null) x.Ok(x.L("diag-draft-saved", file));
            foreach (string n in allNotes.Take(20)) x.Note("• " + n, 1);
            foreach (MethodProblem p in test.Problems.Where(p => p.Error).Take(10)) r.Add(p.ToString(), EmcLineStyle.Warn, 1);
            r.Add(x.L("diag-draftmethods-how"), EmcLineStyle.Text, 1);
            x.Note(x.L("diag-meth-unseen-comment", VintageEModSystem.ModDbUrl), 1);
            r.Section(x.L("diag-draft-text"));
            foreach (string line in json.Split('\n')) r.Add(NoMarkup(line), EmcLineStyle.Note);
            // What the guesses were made from, so the field names can be checked against the real data.
            r.Section(x.L("diag-sec-draftmethods-samples"));
            foreach (var (where, sample) in samples)
            {
                string text = sample.ToString(Formatting.None);
                if (text.Length > 1500) text = text.Substring(0, 1500) + " …";
                r.Add(where + ": " + NoMarkup(text), EmcLineStyle.Note);
            }
            MethodsBack(x);
        }

        // ---------------------------------------------------------------- one item, one mod, a trace

        /// <summary>The item report's lines: every method recipe that makes it (and what became of it), and the ones that use it.</summary>
        static void MethodLinesForItem(Ctx x, CollectibleObject c)
        {
            if (x.Report == null || x.Report.FromCache || x.Report.Conversions == null) return;
            EmcReport r = x.R;
            var convs = MethodConversions(x);
            var makes = convs.Where(kv => kv.Value.Any(conv => MakesIt(conv, c))).ToList();
            var results = x.Report.MethodResults.GroupBy(rr => rr.Recipe).ToDictionary(g => g.Key, g => g.First());
            // Recipes that would make it but weren't read (skipped, switched off).
            var notMade = results.Values.Where(rr => (rr.Disabled || rr.Skipped != null) && !convs.ContainsKey(rr.Recipe)
                && rr.Recipe.Outputs.Concat(rr.Recipe.OneOf).Any(o => CollectibleIndex.Matches(MethodParser.Placeholder.Replace(o.Code, "*"), c))).ToList();
            var uses = convs.Where(kv => kv.Value.Any(conv => conv.Inputs.Any(i => Array.IndexOf(i.Choices, c) >= 0))).Take(6).ToList();
            if (makes.Count + notMade.Count + uses.Count == 0) return;
            r.Section(x.L("diag-sec-item-methods"));
            foreach (var kv in makes.Take(8))
            {
                results.TryGetValue(kv.Key, out MethodRecipeResult res);
                r.Add(x.L("diag-item-madeby", kv.Key.Method.DisplayName, ModName(x, kv.Key.Method.ModId)), EmcLineStyle.Text, 0);
                var mine = kv.Value.Where(conv => MakesIt(conv, c)).ToList();
                RecipeLine(x, kv.Key, mine, res, 1);
                Conversion shown = mine.FirstOrDefault(conv => x.Report.Via.TryGetValue(c, out Conversion v) && v == conv) ?? mine[0];
                foreach (MethodCostLine k in shown.Costs ?? new List<MethodCostLine>()) x.Note(CostText(x, k), 2);
                if (shown.Method.OutputNotes != null && shown.Method.OutputNotes.TryGetValue(c, out string note)) x.Note(note, 2);
            }
            if (makes.Count > 8) x.Note(x.L("diag-more", makes.Count - 8));
            foreach (MethodRecipeResult rr in notMade.Take(6))
                r.Add(x.L("diag-item-notmadeby", rr.Recipe.FullCode, rr.Disabled ? x.L("diag-meth-off") : rr.Skipped) + (rr.Disabled ? "  " + FixLink(x, "method", "on:" + rr.Recipe.FullCode, x.L("diag-fix-method-on", rr.Recipe.FullCode)) : ""), EmcLineStyle.Warn, 1);
            foreach (var kv in uses) r.Add(x.L("diag-item-usedin", kv.Key.FullCode, kv.Key.Method.DisplayName), EmcLineStyle.Note, 1);
        }

        /// <summary>The mod report's section on the mod's own crafting methods.</summary>
        static void MethodLinesForMod(Ctx x, string domain)
        {
            MethodSnapshot snap = Snap(x);
            var docs = snap.Docs.Where(d => d.ModId == domain).ToList();
            EmcReport r = x.R;
            if (docs.Count == 0)
            {
                Unseen u = UnseenRecipeData(x).FirstOrDefault(un => un.Domain == domain);
                if (u == null) return;
                r.Section(x.L("diag-sec-mod-methods"));
                x.Info(x.L("diag-mod-unseen", u.Folders.Count + u.Attributes.Count));
                foreach (var f in u.Folders) x.Note("recipes/" + f.Key + " (" + f.Value + ")", 1);
                foreach (var a in u.Attributes) x.Note(a.Key + " (" + x.L("diag-meth-unseen-on", a.Value.Count, a.Value[0].Code) + ")", 1);
                r.Add(Link(ActionDraftMethods, domain, x.L("diag-meth-draft")), EmcLineStyle.Text, 1);
                return;
            }
            r.Section(x.L("diag-sec-mod-methods"));
            int methods = docs.Where(d => d.Usable).Sum(d => d.Methods.Count), recipes = docs.Where(d => d.Usable).Sum(d => d.RecipeCount);
            int errs = docs.Sum(d => d.Problems.Count(p => p.Error && !ForAbsentMod(x, p.BadCode))) + docs.Count(d => d.Error != null);
            int leftOut = x.Report?.MethodResults?.Count(rr => rr.Recipe.Method.ModId == domain && rr.Skipped != null && !rr.Disabled && !ForAbsentMod(x, rr.BadCode)) ?? 0;
            string line = x.L("diag-mod-methods", methods, recipes, errs, leftOut);
            errs += leftOut;
            if (errs > 0) x.Warn(line); else x.Ok(line);
            if (x.Report != null && !x.Report.FromCache && x.Report.Via != null)
            {
                var through = x.Report.Via.Where(kv => kv.Value?.Method?.ModId == domain).Select(kv => kv.Key).ToList();
                var withPower = through.Where(c => x.Report.Via[c].Costs?.Any(k => k.Kind == "energy" && k.Emc > 0) == true).ToList();
                x.Note(x.L("diag-mod-methods-valued", through.Count, withPower.Count), 1);
                foreach (CollectibleObject c in through.Take(6)) x.Note(EmcReport.Ref(c) + " " + F(x.V(c)), 2);
                // The ingredients holding back the most of its recipes.
                var blockers = new Dictionary<CollectibleObject, int>();
                foreach (var kv in MethodConversions(x).Where(kv => kv.Key.Method.ModId == domain))
                {
                    if (OutcomeOf(x, kv.Value, out _, out var lacking) != MethodOutcome.Waiting) continue;
                    foreach (CollectibleObject l in lacking) blockers[l] = blockers.TryGetValue(l, out int n) ? n + 1 : 1;
                }
                foreach (var kv in blockers.OrderByDescending(b => b.Value).Take(6))
                    r.Add(x.L("diag-blocker", EmcReport.Ref(kv.Key), kv.Value) + "  " + Link(ActionItem, kv.Key.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 2);
                foreach (MethodUnitPrice p in UsedUnits(x).Where(p => p.ModId == domain)) x.Note(x.L("diag-meth-priceline", p.Code, p.Name ?? p.Code, N(p.Price), p.SourceText ?? "?"), 1);
            }
            r.Add(Link(ActionMethods, domain, x.L("diag-meth-look")) + " " + Link(ActionMethodCheck, domain, x.L("diag-meth-check")), EmcLineStyle.Text, 1);
        }

        /// <summary>For the trace's weak links: a method value resting on an unpriced or suggested power price, or a rare chance.</summary>
        static string MethodWeakness(Ctx x, Conversion conv, CollectibleObject c)
        {
            if (conv?.Method == null) return null;
            var weak = new List<string>();
            foreach (MethodCostLine k in conv.Costs ?? new List<MethodCostLine>())
            {
                if (k.Kind != "energy") continue;
                MethodUnitPrice p = x.Report?.MethodPrices != null && x.Report.MethodPrices.Units.TryGetValue(k.Unit ?? "", out MethodUnitPrice up) ? up : null;
                // A mod's suggested price is the usual way a unit is priced, so only a unit with none is worth a word.
                if (p == null || p.Source == "none" || p.Source == "undeclared") weak.Add(x.L("diag-trace-meth-noprice", UnitName(x, k.Unit)));
            }
            MethodStack main = conv.Method.Recipe?.Outputs.FirstOrDefault(o => o.Main) ?? (conv.Method.Recipe?.Outputs.Count == 1 ? conv.Method.Recipe.Outputs[0] : null);
            if (main != null && main.Chance < 0.1 && conv.Output == c) weak.Add(x.L("diag-trace-meth-rare", (main.Chance * 100).ToString("0.##", CultureInfo.InvariantCulture)));
            return weak.Count == 0 ? null : string.Join("; ", weak);
        }

        // ---------------------------------------------------------------- start-up, what changed, symptoms, fixes

        static int MethodStartup(Ctx x)
        {
            int problems = 0;
            foreach (var (mod, error) in Snap(x).ListenerErrors)
            {
                problems++;
                x.Problem(x.L("diag-meth-listener", mod == "?" ? x.L("diag-meth-unknownmod") : ModName(x, mod), error));
                x.Explain(x.L("diag-meth-listener-why"));
                x.Fix(x.L("diag-meth-listener-fix"));
            }
            if (x.Mod.MethodChangeBeforeStart) x.Info(x.L("diag-meth-earlychange", string.Join(", ", Copy(x.Mod.MethodChangeRequests.Keys))));
            return problems;
        }

        /// <summary>"What changed": methods added, removed or changed per mod since the mapping before.</summary>
        static void MethodListChanges(Ctx x)
        {
            var prev = x.Mod.PreviousMethods;
            var now = x.Mod.CurrentMethods;
            if (prev == null || now == null) return;
            var added = now.Keys.Except(prev.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var removed = prev.Keys.Except(now.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var changed = now.Keys.Intersect(prev.Keys).Where(k => prev[k] != now[k]).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (added.Count + removed.Count + changed.Count == 0) { if (now.Count > 0) x.Ok(x.L("diag-changes-methods-same", now.Count)); return; }
            x.Warn(x.L("diag-changes-methods-differ"));
            foreach (string m in added) x.R.Add(x.L("diag-changes-methods-added", ModName(x, m)) + "  " + Link(ActionMethods, m, x.L("diag-meth-look")), EmcLineStyle.Text, 1);
            foreach (string m in removed) x.R.Add(x.L("diag-changes-methods-removed", m), EmcLineStyle.Text, 1);
            foreach (string m in changed) x.R.Add(x.L("diag-changes-methods-changed", ModName(x, m)) + "  " + Link(ActionMethods, m, x.L("diag-meth-look")), EmcLineStyle.Text, 1);
            if (x.Report?.Via != null)
            {
                var affected = new HashSet<string>(added.Concat(changed), StringComparer.Ordinal);
                int n = x.Report.Via.Count(kv => kv.Value?.Method != null && affected.Contains(kv.Value.Method.ModId));
                if (n > 0) x.Note(x.L("diag-changes-methods-values", n), 1);
            }
        }

        static readonly string[] MethodSymptoms = { "methodnovalue", "powercost", "powerfarm", "chancevalue" };

        /// <summary>The four crafting-method symptoms. Returns false for any other id.</summary>
        static bool MethodSymptom(Ctx x, string id)
        {
            if (!MethodSymptoms.Contains(id)) return false;
            EmcReport r = x.R;
            CollectibleObject held = x.Held?.Collectible;
            bool fresh = x.Report != null && !x.Report.FromCache && x.Report.MethodPrices != null;
            var convs = fresh ? MethodConversions(x) : new Dictionary<MethodRecipe, List<Conversion>>();
            switch (id)
            {
                case "methodnovalue":
                    {
                        // Step by step: is there a document, was the recipe read, is it switched off, what is it waiting for, did something beat it.
                        MethodSnapshot snap = Snap(x);
                        x.Info(x.L("diag-sym-meth-1", snap.Docs.Count, snap.MethodCount));
                        if (held != null && !snap.Docs.Any(d => d.ModId == held.Code.Domain))
                        {
                            x.Note(x.L("diag-sym-meth-1-none", ModName(x, held.Code.Domain)), 1);
                            if (UnseenRecipeData(x).Any(u => u.Domain == held.Code.Domain)) r.Add(Link(ActionDraftMethods, held.Code.Domain, x.L("diag-meth-draft")), EmcLineStyle.Text, 1);
                        }
                        x.Info(x.L("diag-sym-meth-2"));
                        if (fresh) RecipesCard(x, convs);
                        x.Info(x.L("diag-sym-meth-3"));
                        if (SwitchedOffCardShowsNothing(x)) x.Ok(x.L("diag-sym-meth-3-none"));
                        else SwitchedOffCard(x);
                        x.Info(x.L("diag-sym-meth-4"));
                        if (fresh && StuckCard(x, convs) == 0) x.Ok(x.L("diag-sym-meth-4-none"));
                        if (held != null) MethodLinesForItem(x, held);
                        else r.Add(Link(ActionHand, "", x.L("diag-q-item")), EmcLineStyle.Text, 1);
                        if (!fresh) r.Add(FixLink(x, "recalc", "", x.L("diag-fix-recalc")), EmcLineStyle.Text, 1);
                        break;
                    }
                case "powercost":
                    if (held != null) MethodLinesForItem(x, held);
                    else x.Note(x.L("diag-item-holdit"), 0);
                    if (fresh) PricesCard(x);
                    x.Note(x.L("diag-sym-powercost-also"), 0);
                    break;
                case "powerfarm":
                    if (fresh) FreePowerCard(x, convs);
                    if (x.Mod.Config.ChargeMethodEnergy) r.Add(FixLink(x, "methodcost", "energy=false", x.L("diag-fix-methodcost", "ChargeMethodEnergy", "false")), EmcLineStyle.Text, 1);
                    else r.Add(FixLink(x, "methodcost", "energy=true", x.L("diag-fix-methodcost", "ChargeMethodEnergy", "true")), EmcLineStyle.Text, 1);
                    x.Note(x.L("diag-sym-powerfarm-also"), 0);
                    break;
                case "chancevalue":
                    {
                        // chance here is what one run makes on average (quantity × chance): one item takes 1 / that many runs.
                        var rare = new List<(MethodRecipe recipe, CollectibleObject c, double chance)>();
                        foreach (var kv in convs)
                        {
                            if (OutcomeOf(x, kv.Value, out Conversion used, out _) != MethodOutcome.Used || used.Output == null) continue;
                            MethodStack main = kv.Key.Outputs.FirstOrDefault(o => o.Main) ?? (kv.Key.Outputs.Count == 1 ? kv.Key.Outputs[0] : null);
                            if (main != null && main.Chance < 1) rare.Add((kv.Key, used.Output, used.OutputQuantity));
                        }
                        if (held != null) rare = rare.OrderByDescending(t => t.c == held).ThenBy(t => t.chance).ToList(); else rare = rare.OrderBy(t => t.chance).ToList();
                        if (rare.Count == 0) x.Ok(x.L("diag-sym-chance-none"));
                        foreach (var (recipe, c, chance) in rare.Take(8))
                        {
                            MethodStack mainOut = recipe.Outputs.FirstOrDefault(o => o.Main) ?? recipe.Outputs.FirstOrDefault();
                            r.Add(x.L("diag-sym-chance-line", EmcReport.Ref(c), F(x.V(c)), recipe.FullCode, ((mainOut?.Chance ?? 1) * 100).ToString("0.##", CultureInfo.InvariantCulture), N(1 / Math.Max(1e-9, chance))), EmcLineStyle.Text, 1);
                            SuggestPinValued(x, c);
                        }
                        x.Note(x.L("diag-sym-chance-also"), 0);
                        break;
                    }
            }
            r.Add(Link(ActionMethods, "", x.L("diag-q-methods")));
            return true;
        }

        /// <summary>The crafting-method fixes. Null when <paramref name="what"/> isn't one of them.</summary>
        static string ApplyMethodFix(VintageEModSystem mod, string what, string target, string lang)
        {
            string L(string key, params object[] args) => Lang.GetL(lang, "vintagee:" + key, args);
            VintageEConfig cfg = mod.Config;
            switch (what)
            {
                case "method":
                    {
                        // on:<entry> takes the entry out of DisabledMethods; off:<code> adds it.
                        int colon = target.IndexOf(':');
                        if (colon <= 0) return L("diag-done-nothing", target);
                        string verb = target.Substring(0, colon), key = target.Substring(colon + 1).Trim();
                        if (key.Length == 0) return L("diag-done-nothing", target);
                        if (verb == "on")
                        {
                            // The key is an entry, a method's code or one recipe's: take out every entry that switches it off
                            // (the mod's id, a wildcard), and say which, as an entry may switch off more than this.
                            string k = key.ToLowerInvariant();
                            MethodDef def = mod.Methods.AllMethods.FirstOrDefault(m => m.Code == k) ?? mod.Methods.AllMethods.FirstOrDefault(m => m.Recipes.Any(rc => rc.FullCode.ToLowerInvariant() == k));
                            MethodRecipe rec = def?.Recipes.FirstOrDefault(rc => rc.FullCode.ToLowerInvariant() == k);
                            bool Hits(string e)
                            {
                                if (string.Equals(e?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return true;
                                if (def == null) return false;
                                var one = new[] { e };
                                // For a whole method: entries for it or for any of its recipes; for a recipe: those for it.
                                return rec != null ? MethodSnapshot.IsDisabled(def, rec, one) : MethodSnapshot.IsDisabled(def, null, one) || def.Recipes.Any(rc => MethodSnapshot.IsDisabled(def, rc, one));
                            }
                            var removed = cfg.DisabledMethods.Where(Hits).ToList();
                            if (removed.Count == 0) return L("diag-done-nothing", key);
                            cfg.DisabledMethods.RemoveAll(e => removed.Contains(e));
                            mod.SaveConfig();
                            mod.RequestRemap("/emc diagnose method on");
                            return L("diag-done-method-on", key, string.Join(", ", removed));
                        }
                        if (!cfg.DisabledMethods.Any(e => string.Equals(e?.Trim(), key, StringComparison.OrdinalIgnoreCase))) cfg.DisabledMethods.Add(key);
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose method off");
                        return L("diag-done-method-off", key);
                    }
                case "energyprice":
                    {
                        int eq = target.LastIndexOf('=');
                        if (eq <= 0 || !double.TryParse(target.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsNaN(v) || double.IsInfinity(v)) return L("diag-done-nothing", target);
                        string unit = target.Substring(0, eq).Trim().ToLowerInvariant();
                        cfg.EnergyEmc[unit] = v;
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose energy price");
                        return L("diag-done-energyprice", unit, N(v));
                    }
                case "safeprice":
                    {
                        string unit = target.Trim().ToLowerInvariant();
                        var x = new Ctx { Mod = mod, Sapi = mod.Sapi, LangCode = lang, R = new EmcReport(), Report = mod.LastReport, Index = new CollectibleIndex(mod.Sapi.World.Collectibles) };
                        if (x.Report == null || x.Report.FromCache || x.Report.MethodPrices == null) return L("diag-meth-cached");
                        double safe = SafePrice(x, MethodConversions(x), unit);
                        if (safe <= 0) return L("diag-done-nothing", unit);
                        cfg.EnergyEmc[unit] = safe;
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose safe energy price");
                        return L("diag-done-energyprice", unit, N(safe));
                    }
                case "methodcost":
                    {
                        int eq = target.IndexOf('=');
                        if (eq <= 0 || !bool.TryParse(target.Substring(eq + 1), out bool on)) return L("diag-done-nothing", target);
                        switch (target.Substring(0, eq).Trim().ToLowerInvariant())
                        {
                            case "energy": cfg.ChargeMethodEnergy = on; break;
                            case "heat": cfg.ChargeMethodHeat = on; break;
                            case "wear": cfg.ChargeMethodWear = on; break;
                            default: return L("diag-done-nothing", target);
                        }
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose method costs");
                        return L("diag-done-methodcost", target.Substring(0, eq), on ? L("diag-on") : L("diag-off"));
                    }
                case "methodsreload":
                    mod.CollectMethods("/emc diagnose: ask the mods again");
                    mod.RequestRemap("/emc diagnose: crafting methods collected again");
                    return L("diag-done-methodsreload", mod.Methods.MethodCount, mod.Methods.RecipeCount, mod.Methods.Mods.Count());
                default:
                    return null;
            }
        }

        /// <summary>How many DisabledMethods and EnergyEmc entries name nothing (for the safe fixes).</summary>
        static int MethodSafeFixCount(VintageEModSystem mod) => DeadDisabledMethods(mod).Count + DeadEnergyPrices(mod).Count;

        static int ApplyMethodSafeFixes(VintageEModSystem mod, List<string> removed)
        {
            int n = 0;
            foreach (string e in DeadDisabledMethods(mod)) { mod.Config.DisabledMethods.Remove(e); removed.Add(e); n++; }
            foreach (string e in DeadEnergyPrices(mod)) { mod.Config.EnergyEmc.Remove(e); removed.Add(e); n++; }
            return n;
        }
    }
}
