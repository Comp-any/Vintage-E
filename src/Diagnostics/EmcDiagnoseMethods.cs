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

        static string N(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- what became of each recipe

        enum MethodOutcome { Used, Beaten, Waiting }

        /// <summary>The method conversions of a mapping, by recipe.</summary>
        static Dictionary<MethodRecipe, List<Conversion>> MethodConversions(Ctx x)
        {
            var result = new Dictionary<MethodRecipe, List<Conversion>>();
            foreach (Conversion conv in x.Report?.Conversions ?? new List<Conversion>())
            {
                if (conv.Method?.Recipe == null) continue;
                if (!result.TryGetValue(conv.Method.Recipe, out var list)) result[conv.Method.Recipe] = list = new List<Conversion>();
                list.Add(conv);
            }
            return result;
        }

        static IEnumerable<CollectibleObject> OutputsOf(Conversion conv) => conv.JointOutputs != null ? conv.JointOutputs.Select(o => o.Key) : new[] { conv.Output };

        /// <summary>What one conversion costs per run at today's values (ingredients, costs, less what's handed back), or null with what's missing.</summary>
        static double? RunCost(Ctx x, Conversion conv, List<CollectibleObject> lacking)
        {
            var free = new HashSet<CollectibleObject>(x.Report.Free);
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
            if (first != null && OutputsOf(first).All(o => o != null && x.Report.Values.ContainsKey(o))) return MethodOutcome.Beaten;
            if (first != null) RunCost(x, first, lacking);
            lacking = lacking.Where(l => l != null).Distinct().ToList();
            return MethodOutcome.Waiting;
        }

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
            return x.Mod.Methods.Units.TryGetValue(unit, out MethodEnergyUnit u) && !string.IsNullOrEmpty(u.Name) ? u.Name : unit;
        }

        /// <summary>One line for a recipe with what became of it.</summary>
        static void RecipeLine(Ctx x, MethodRecipe recipe, List<Conversion> convs, MethodRecipeResult result, int indent)
        {
            EmcReport r = x.R;
            string head = recipe.Code + (recipe.ReadFrom != null ? " " + x.L("diag-meth-readfrom", recipe.ReadFrom) : "");
            if (result != null && result.Disabled) { r.Add(head + ": " + x.L("diag-meth-off"), EmcLineStyle.Note, indent); return; }
            if (convs == null || convs.Count == 0)
            {
                r.Add(head + ": " + x.L("diag-meth-skipped", result?.Skipped ?? x.L("diag-meth-notread")) + (result?.BadCode != null ? Suggest(x, result.BadCode) : ""), EmcLineStyle.Warn, indent);
                return;
            }
            MethodOutcome outcome = OutcomeOf(x, convs, out Conversion used, out List<CollectibleObject> lacking);
            Conversion shown = used ?? convs[0];
            string variants = convs.Count > 1 ? " " + x.L("diag-meth-copies", convs.Count) : "";
            var missing = new List<CollectibleObject>();
            double? cost = RunCost(x, shown, missing);
            double per = cost.HasValue && shown.JointOutputs == null && shown.OutputQuantity > 0 ? cost.Value / shown.OutputQuantity : double.NaN;
            string costText = !double.IsNaN(per) ? " = " + x.L("diag-meth-each", F(per)) : cost.HasValue ? " = " + x.L("diag-meth-run", F(cost.Value)) : "";
            r.Add(head + variants + ": " + RecipeText(x, shown) + costText, outcome == MethodOutcome.Waiting ? EmcLineStyle.Warn : EmcLineStyle.Text, indent);
            switch (outcome)
            {
                case MethodOutcome.Used: x.Note(x.L("diag-meth-used"), indent + 1); break;
                case MethodOutcome.Beaten:
                    CollectibleObject o = OutputsOf(shown).FirstOrDefault();
                    string by = o != null && x.Report.Via.TryGetValue(o, out Conversion via) && via != null ? Linkify(x, EmcMapper.Describe(via)) : o != null && x.Report.Origins.TryGetValue(o, out string orig) ? Linkify(x, orig) : "?";
                    x.Note(x.L("diag-meth-beaten", o != null ? EmcReport.Ref(o) + " " + F(x.V(o)) : "?", by), indent + 1);
                    break;
                default:
                    x.Note(x.L("diag-meth-waiting", lacking.Count == 0 ? "?" : string.Join(", ", lacking.Take(4).Select(EmcReport.Ref))), indent + 1);
                    break;
            }
            if (result != null && result.MissingVariants > 0) x.Note(x.L("diag-meth-missingvariants", result.MissingVariants), indent + 1);
        }

        // ---------------------------------------------------------------- the check-up's cards

        /// <summary>The check-up's cards for other mods' crafting methods. Returns how many found something worth fixing.</summary>
        static int MethodsSummary(Ctx x, bool full)
        {
            MethodSnapshot snap = x.Mod.Methods;
            var unseen = UnseenRecipeData(x);
            if (snap.Docs.Count == 0 && unseen.Count == 0) return 0;
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-meth"));
            int problems = 0;
            problems += DocumentsCard(x, snap);
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
        static int DocumentsCard(Ctx x, MethodSnapshot snap)
        {
            EmcReport r = x.R;
            var docs = snap.Docs;
            int files = docs.Count(d => d.Route == "file"), code = docs.Count(d => d.Route == "code");
            var broken = docs.Where(d => d.Error != null).ToList();
            var waiting = docs.Where(d => d.Error == null && d.MissingMod != null).ToList();
            int warnings = docs.Where(d => d.Usable).Sum(d => d.Problems.Count(p => !p.Error));
            int left = docs.Where(d => d.Usable).Sum(d => d.Problems.Count(p => p.Error)) + snap.Problems.Count(p => p.Error);
            string line = x.L("diag-meth-docs", snap.Mods.Count(), files, code, snap.MethodCount, snap.RecipeCount);
            int problems = 0;
            if (broken.Count > 0 || snap.ListenerErrors.Count > 0 || left > 0) { problems++; x.Warn(line); }
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
            foreach (var (mod, error) in snap.ListenerErrors)
            {
                r.Add(x.L("diag-meth-listener", mod == "?" ? x.L("diag-meth-unknownmod") : ModName(x, mod), error), EmcLineStyle.Warn, 1);
                x.Fix(x.L("diag-meth-listener-fix"));
            }
            foreach (MethodProblem p in snap.Problems) r.Add(p.ToString(), p.Error ? EmcLineStyle.Warn : EmcLineStyle.Note, 1);
            foreach (MethodDoc d in waiting) x.Note(x.L("diag-meth-waitingmod", d.Source, d.MissingMod), 1);
            if (left > 0 || warnings > 0) x.Fix(x.L("diag-meth-docs-fix"));
            return problems;
        }

        /// <summary>Card 2: recipes read, used, skipped (grouped by why, each with where).</summary>
        static int RecipesCard(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs)
        {
            EmcReport r = x.R;
            var results = x.Report.MethodResults;
            int read = results.Count, off = results.Count(rr => rr.Disabled);
            var skipped = results.Where(rr => !rr.Disabled && rr.Skipped != null).ToList();
            var parseSkipped = x.Mod.Methods.UsableDocs.SelectMany(d => d.Problems.Where(p => p.Error).Select(p => (d, p))).ToList();
            int used = results.Count(rr => !rr.Disabled && rr.Skipped == null && convs.TryGetValue(rr.Recipe, out var cl) && OutcomeOf(x, cl, out _, out _) == MethodOutcome.Used);
            string line = x.L("diag-meth-recipes", read, used, skipped.Count + parseSkipped.Count, off);
            int problems = 0;
            if (skipped.Count + parseSkipped.Count > 0) { problems++; x.Warn(line); } else x.Ok(line);
            x.Explain(x.L("diag-meth-recipes-why"));
            foreach (var g in skipped.GroupBy(s => Regex.Replace(s.Skipped, @"\S+:\S+", "…")).OrderByDescending(g => g.Count()).Take(8))
            {
                var firstOne = g.First();
                r.Add(x.L("diag-meth-skipline", g.Count(), firstOne.Skipped), EmcLineStyle.Text, 1);
                foreach (var s in g.Take(3))
                    x.Note(s.Recipe.Method.Doc.Source + " " + s.Recipe.Path + (s.BadCode != null ? Suggest(x, s.BadCode) : ""), 2);
            }
            foreach (var (d, p) in parseSkipped.Take(8)) r.Add(d.Source + " " + p, EmcLineStyle.Text, 1);
            if (parseSkipped.Count > 8) x.Note(x.L("diag-more", parseSkipped.Count - 8));
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
            var unpriced = units.Where(p => p.Price <= 0 && used.Contains(p.Code)).ToList();
            int problems = 0;
            if (units.Count == 0) x.Ok(x.L("diag-meth-prices-none"));
            else if (unpriced.Count > 0 && prices.ChargeEnergy) { problems++; x.Warn(x.L("diag-meth-prices-unpriced", unpriced.Count, units.Count)); }
            else x.Ok(x.L("diag-meth-prices", units.Count));
            x.Explain(x.L("diag-meth-prices-why"));
            foreach (MethodUnitPrice p in units)
            {
                r.Add(x.L("diag-meth-priceline", p.Code, p.Name ?? p.Code, N(p.Price), p.SourceText ?? "?"), p.Price <= 0 && used.Contains(p.Code) ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                foreach (string g in p.Generators) x.Note(g, 2);
                if (!p.Declared)
                {
                    string near = x.Mod.Methods.Units.Keys.OrderBy(k => MethodParser.Distance(k, p.Code)).FirstOrDefault();
                    if (near != null && MethodParser.Distance(near, p.Code) <= Math.Max(3, p.Code.Length / 3)) x.Note(x.L("diag-meth-unit-maybe", near), 2);
                }
                double s = SuggestedPrice(p);
                if (p.Price <= 0 && s > 0) r.Add(FixLink(x, "energyprice", p.Code + "=" + s.ToString("R", CultureInfo.InvariantCulture), x.L("diag-fix-energyprice", N(s))), EmcLineStyle.Text, 2);
                if (p.Price <= 0) x.Note(x.L("diag-meth-price-byhand", p.Code), 2);
            }
            if (!prices.ChargeEnergy) x.Note(x.L("diag-meth-energy-off"), 1);
            if (!prices.ChargeHeat) x.Note(x.L("diag-meth-heat-off"), 1);
            if (!prices.ChargeWear) x.Note(x.L("diag-meth-wear-off"), 1);
            x.Note(x.L("diag-meth-heatrate", N(prices.HeatRate), prices.HeatSource), 1);
            if (prices.SecondRate > 0) x.Note(x.L("diag-meth-secondrate", N(prices.SecondRate)), 1);
            return problems;
        }

        sealed class PowerFarm { public MethodRecipe Recipe; public double PerSecond, EnergyEmc, Seconds; public string Unit; public double Price; }

        /// <summary>Recipes in use whose power cost alone adds more than the limit per second of machine time.</summary>
        static List<PowerFarm> PowerFarms(Ctx x, Dictionary<MethodRecipe, List<Conversion>> convs, out int unknown)
        {
            var result = new List<PowerFarm>();
            unknown = 0;
            foreach (var kv in convs)
            {
                if (OutcomeOf(x, kv.Value, out Conversion used, out _) != MethodOutcome.Used || used.Costs == null) continue;
                var energy = used.Costs.Where(k => k.Kind == "energy" && k.Emc > 0).ToList();
                if (energy.Count == 0) continue;
                double secs = kv.Key.Costs.Seconds;
                if (secs <= 0) { unknown++; continue; }
                var top = energy.OrderByDescending(k => k.Emc).First();
                double emc = energy.Sum(k => k.Emc);
                result.Add(new PowerFarm { Recipe = kv.Key, PerSecond = emc / secs, EnergyEmc = emc, Seconds = secs, Unit = top.Unit, Price = top.Price });
            }
            return result.OrderByDescending(f => f.PerSecond).ToList();
        }

        /// <summary>The highest price for a unit at which none of its recipes in use makes more than the limit a second from power.</summary>
        static double SafePrice(List<PowerFarm> farms, string unit)
        {
            double best = double.PositiveInfinity;
            foreach (PowerFarm f in farms.Where(f => f.Unit == unit && f.PerSecond > 0))
            {
                double p = f.Price * FreePowerLimit / f.PerSecond;
                if (p < best) best = p;
            }
            return double.IsPositiveInfinity(best) ? 0 : Math.Floor(best * 1e6) / 1e6;
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
                double safe = SafePrice(farms, unit);
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
            int usedTotal = 0, beatenTotal = 0;
            var lines = new List<(string text, MethodDef m)>();
            foreach (var g in byMethod)
            {
                int used = 0, beaten = 0, waiting = 0;
                foreach (var kv in g)
                {
                    switch (OutcomeOf(x, kv.Value, out _, out _))
                    {
                        case MethodOutcome.Used: used++; break;
                        case MethodOutcome.Beaten: beaten++; break;
                        default: waiting++; break;
                    }
                }
                usedTotal += used;
                beatenTotal += beaten;
                lines.Add((x.L("diag-meth-countline", g.Key.DisplayName, g.Key.Code, used, beaten, waiting), g.Key));
            }
            x.Info(x.L("diag-meth-count", usedTotal, beatenTotal));
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
                bool byproduct = lacking.Count > 0 && first.Returned != null && lacking.All(l => first.Returned.Any(b => b.Key == l));
                stuck.Add((kv.Key, lacking, byproduct));
            }
            if (stuck.Count == 0) return 0;
            x.Warn(x.L("diag-meth-stuck", stuck.Count, stuck.Count(s => s.byproduct)));
            x.Explain(x.L("diag-meth-stuck-why"));
            foreach (var s in stuck.Take(8))
            {
                r.Add(s.recipe.FullCode + ": " + x.L(s.byproduct ? "diag-meth-stuck-byproduct" : "diag-meth-stuck-ingredient", string.Join(", ", s.lacking.Take(4).Select(EmcReport.Ref))), EmcLineStyle.Text, 1);
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
            var withCost = x.Report.Conversions.Where(c => c.Method != null && c.Output != null && c.ExtraCost > 0).ToList();
            var others = x.Report.Conversions.Where(c => c.Method == null && c.Output != null).GroupBy(c => c.Output).ToDictionary(g => g.Key, g => g.ToList());
            var copies = new List<(Conversion method, Conversion copy)>();
            foreach (Conversion m in withCost)
            {
                if (!others.TryGetValue(m.Output, out var candidates)) continue;
                string sig = Signature(m);
                Conversion copy = candidates.FirstOrDefault(c => c.ExtraCost < m.ExtraCost && Signature(c) == sig);
                if (copy != null) copies.Add((m, copy));
            }
            var dead = new List<(MethodDef m, string entry)>();
            foreach (MethodDef m in x.Mod.Methods.AllMethods)
                foreach (string s in m.Supersedes)
                    if (!x.Report.SupersedeHits.Contains(s)) dead.Add((m, s));
            if (copies.Count == 0 && dead.Count == 0)
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

        static List<Unseen> UnseenRecipeData(Ctx x)
        {
            var byDomain = new Dictionary<string, Unseen>(StringComparer.Ordinal);
            Unseen Of(string d) => byDomain.TryGetValue(d, out Unseen u) ? u : byDomain[d] = new Unseen { Domain = d };
            var covered = new HashSet<string>(x.Mod.Methods.Docs.Select(d => d.ModId), StringComparer.Ordinal);
            try
            {
                // Recipe lists Vintage-E finds by reflection, by a squashed name, so a folder that one of them loads isn't listed.
                var labels = ConversionCollector.DiscoverRecipeLists(x.Sapi, x.Sapi.Logger).Select(l => Regex.Replace((l.Label ?? "") + " " + (l.ElementType?.Name ?? ""), "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant()).ToList();
                foreach (IAsset asset in x.Sapi.Assets.GetMany("recipes/", null, false))
                {
                    string domain = asset.Location.Domain;
                    if (BaseDomains.Contains(domain) || covered.Contains(domain)) continue;
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
            var cfg = x.Mod.Config;
            var methods = x.Mod.Methods.AllMethods.ToList();
            var off = methods.Where(m => MethodSnapshot.IsDisabled(m, null, cfg.DisabledMethods)).ToList();
            var deadOff = DeadDisabledMethods(x.Mod);
            var deadPrices = DeadEnergyPrices(x.Mod);
            if (off.Count == 0 && deadOff.Count == 0 && deadPrices.Count == 0 && cfg.DisabledMethods.Count == 0) return 0;
            if (off.Count > 0 || cfg.DisabledMethods.Count > 0)
            {
                x.Info(x.L("diag-meth-off-card", off.Count, cfg.DisabledMethods.Count));
                foreach (string entry in cfg.DisabledMethods.Where(e => !deadOff.Contains(e)))
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

        /// <summary>DisabledMethods entries that name no loaded mod, method or recipe.</summary>
        static List<string> DeadDisabledMethods(VintageEModSystem mod)
        {
            var methods = mod.Methods.AllMethods.ToList();
            var result = new List<string>();
            foreach (string e in mod.Config.DisabledMethods ?? new List<string>())
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
            return (mod.Config.EnergyEmc ?? new Dictionary<string, double>()).Keys.Where(k => !string.IsNullOrWhiteSpace(k) && !known.Contains(k.Trim().ToLowerInvariant()) && !known.Contains("game:" + k.Trim().ToLowerInvariant())).ToList();
        }

        // ---------------------------------------------------------------- /emc methods

        static void MethodsReport(Ctx x, string arg)
        {
            EmcReport r = x.R;
            arg = (arg ?? "").Trim();
            MethodSnapshot snap = x.Mod.Methods;
            if (arg.StartsWith("export", StringComparison.OrdinalIgnoreCase))
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
                DocumentsCard(x, snap);
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
            foreach (MethodDef m in snap.AllMethods.Where(m => (mod == null || m.ModId == mod) && (method == null || m.Code == method)))
            {
                r.Section(x.L("diag-sec-meth-one", m.DisplayName, m.Code));
                bool off = MethodSnapshot.IsDisabled(m, null, x.Mod.Config.DisabledMethods);
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
            foreach (string mod in x.Mod.Methods.Mods.Take(6)) x.R.Add(Link(ActionMethodCheck, mod, x.L("diag-q-methodcheck", ModName(x, mod))));
            x.R.Add(FixLink(x, "methodsreload", "", x.L("diag-fix-methodsreload")));
            x.R.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- what Vintage-E understood, for the mod's author

        /// <summary>Writes ModData/vintagee/methods-&lt;modid&gt;.json: every recipe as Vintage-E read it, with its costs, prices and what became of it.</summary>
        static string Export(Ctx x, string modId)
        {
            var docs = x.Mod.Methods.Docs.Where(d => d.ModId == modId).ToList();
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
                            double? cost = RunCost(x, c, null);
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
            var docs = x.Mod.Methods.Docs.Where(d => d.ModId == modId).ToList();
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
                int used = 0, beaten = 0, waiting = 0, skipped = 0, off = 0;
                Conversion example = null;
                foreach (MethodRecipe recipe in m.Recipes)
                {
                    results.TryGetValue(recipe, out MethodRecipeResult res);
                    if (res?.Disabled == true) { off++; continue; }
                    if (!convs.TryGetValue(recipe, out var list) || list.Count == 0)
                    {
                        skipped++;
                        r.Add(recipe.Code + ": " + x.L("diag-meth-skipped", res?.Skipped ?? x.L("diag-meth-notread")) + (res?.BadCode != null ? Suggest(x, res.BadCode) : ""), EmcLineStyle.Warn, 1);
                        x.Note(recipe.Path, 2);
                        continue;
                    }
                    switch (OutcomeOf(x, list, out Conversion u, out var lacking))
                    {
                        case MethodOutcome.Used: used++; example ??= u; break;
                        case MethodOutcome.Beaten: beaten++; if (beaten <= 10) RecipeLine(x, recipe, list, res, 1); break;
                        default: waiting++; if (waiting <= 10) RecipeLine(x, recipe, list, res, 1); break;
                    }
                }
                x.Info(x.L("diag-methodcheck-counts", used, beaten, waiting, skipped, off));
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
                    double? cost = RunCost(x, example, null);
                    if (cost.HasValue && example.JointOutputs == null) r.Add(x.L("diag-methodcheck-total", F(cost.Value), N(example.OutputQuantity), F(cost.Value / Math.Max(1e-9, example.OutputQuantity)), EmcReport.Ref(example.Output)), EmcLineStyle.Total, 3);
                    else if (cost.HasValue) r.Add(x.L("diag-meth-run", F(cost.Value)), EmcLineStyle.Total, 3);
                }
            }
            r.Section(x.L("diag-sec-meth-export", modId));
            Export(x, modId);
            MethodsBack(x);
        }

        // ---------------------------------------------------------------- draftmethods: a starting document from data Vintage-E can't read

        static readonly Regex InputField = new Regex(@"^(input|ingredient|in)(\d*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex OutputField = new Regex(@"^(output|result|out|product)(\d*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

        /// <summary>A reader's recipe template guessed from one sample recipe's field names; notes say what was guessed.</summary>
        static JObject GuessTemplate(JObject sample, List<string> notes)
        {
            var inputs = new JArray();
            var outputs = new JArray();
            var tmpl = new JObject();
            bool wildcardIn = false, wildcardOut = false;
            foreach (JProperty p in sample.Properties())
            {
                if (p.Value.Type != JTokenType.String) continue;
                string val = (string)p.Value;
                Match m;
                if ((m = InputField.Match(p.Name)).Success)
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string q = FieldLike(sample, p.Name + "Quantity", p.Name + "Qty", p.Name + "Count", p.Name + "StackSize", m.Groups[2].Value.Length == 0 ? "Quantity" : "\0");
                    if (q != null) s["quantity"] = "$" + q;
                    string t = FieldLike(sample, p.Name + "Type");
                    if (t != null) s["type"] = "$" + t;
                    if (val.Contains('*')) { s["name"] = "wildcard"; wildcardIn = true; }
                    inputs.Add(s);
                }
                else if (LiquidOutField.IsMatch(p.Name))
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string l = FieldLike(sample, p.Name + "Litres", p.Name + "Liters", "OutputLitres");
                    if (l != null) s["litres"] = "$" + l; else notes.Add("the liquid output " + p.Name + " has no litres field: check its amount");
                    outputs.Add(s);
                }
                else if ((m = OutputField.Match(p.Name)).Success)
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string q = FieldLike(sample, p.Name + "Quantity", p.Name + "Qty", p.Name + "Count", p.Name + "StackSize");
                    if (q != null) s["quantity"] = "$" + q;
                    string t = FieldLike(sample, p.Name + "Type");
                    if (t != null) s["type"] = "$" + t;
                    if (val.Contains("{wildcard}")) wildcardOut = true;
                    outputs.Add(s);
                }
                else if (LiquidField.IsMatch(p.Name))
                {
                    var s = new JObject { ["code"] = "$" + p.Name };
                    string l = FieldLike(sample, p.Name + "Litres", p.Name + "Liters", "Litres", "Liters");
                    if (l != null) s["litres"] = "$" + l; else notes.Add("the liquid " + p.Name + " has no litres field: check its amount");
                    inputs.Add(s);
                }
            }
            string time = sample.Properties().Select(p => p.Name).FirstOrDefault(n => TimeField.IsMatch(n));
            string code = FieldLike(sample, "Code", "Name", "Id");
            if (code != null) tmpl["code"] = "$" + code;
            tmpl["inputs"] = inputs;
            tmpl["outputs"] = outputs;
            if (time != null) tmpl["costs"] = new JObject { ["seconds"] = "$" + time };
            if (inputs.Count == 0) notes.Add("no field looked like an ingredient: fill \"inputs\" by hand");
            if (outputs.Count == 0) notes.Add("no field looked like a result: fill \"outputs\" by hand");
            if (wildcardOut && !wildcardIn) notes.Add("an output uses {wildcard} but no input has a *: check which input it comes from");
            notes.Add("no energy cost is known: add \"energy\": { \"unit\": ..., \"amount\": ... } to \"costs\" if the machine uses power, and declare the unit");
            var unknown = sample.Properties().Where(p => !InputField.IsMatch(p.Name) && !OutputField.IsMatch(p.Name) && !LiquidField.IsMatch(p.Name) && !LiquidOutField.IsMatch(p.Name)
                && !TimeField.IsMatch(p.Name) && !p.Name.EndsWith("Quantity", StringComparison.OrdinalIgnoreCase) && !p.Name.EndsWith("Type", StringComparison.OrdinalIgnoreCase)
                && !p.Name.EndsWith("Litres", StringComparison.OrdinalIgnoreCase) && p.Name != code).Select(p => p.Name).ToList();
            if (unknown.Count > 0) notes.Add("fields not used (a tool, a die, a heat level, a second ingredient?): " + string.Join(", ", unknown));
            return tmpl;
        }

        static void DraftMethods(Ctx x, string modId)
        {
            EmcReport r = x.R;
            modId = (modId ?? "").Trim().ToLowerInvariant();
            r.Section(x.L("diag-sec-draftmethods", ModName(x, modId)));
            Unseen u = UnseenRecipeData(x).FirstOrDefault(un => un.Domain == modId);
            if (u == null)
            {
                bool has = x.Mod.Methods.Docs.Any(d => d.ModId == modId);
                x.Ok(has ? x.L("diag-draftmethods-has", modId) : x.L("diag-draftmethods-nothing", modId));
                if (has) r.Add(Link(ActionMethodCheck, modId, x.L("diag-q-methodcheck", ModName(x, modId))));
                MethodsBack(x);
                return;
            }
            var methods = new JArray();
            var allNotes = new List<string>();
            foreach (var kv in u.Attributes)
            {
                var notes = new List<string>();
                CollectibleObject first = kv.Value[0];
                string family = first.Code.Path.Split('-')[0];
                string pattern = (first.ItemClass == EnumItemClass.Block ? "block:" : "item:") + first.Code.Domain + ":" + family + (first.Code.Path.Contains('-') ? "-*" : "");
                JObject sample = (first.Attributes?.Token?[kv.Key] as JArray)?.FirstOrDefault() as JObject;
                if (sample == null) continue;
                JObject tmpl = GuessTemplate(sample, notes);
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
                    JToken file = asset == null ? null : JToken.Parse(asset.ToText());
                    sample = file as JObject ?? (file as JArray)?.FirstOrDefault() as JObject;
                }
                catch (Exception e) { notes.Add("its first file could not be read (" + e.Message + ")"); }
                if (sample == null) continue;
                JObject tmpl = GuessTemplate(sample, notes);
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
            foreach (string line in json.Split('\n')) r.Add(line.Replace("[[", "").Replace("{{", "").Replace("}}", ""), EmcLineStyle.Note);
            MethodsBack(x);
        }

        // ---------------------------------------------------------------- one item, one mod, a trace

        /// <summary>The item report's lines: every method recipe that makes it (and what became of it), and the ones that use it.</summary>
        static void MethodLinesForItem(Ctx x, CollectibleObject c)
        {
            if (x.Report == null || x.Report.FromCache || x.Report.Conversions == null) return;
            EmcReport r = x.R;
            var convs = MethodConversions(x);
            var makes = convs.Where(kv => kv.Value.Any(conv => OutputsOf(conv).Contains(c))).ToList();
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
                var mine = kv.Value.Where(conv => OutputsOf(conv).Contains(c)).ToList();
                RecipeLine(x, kv.Key, mine, res, 1);
                Conversion shown = mine.FirstOrDefault(conv => x.Report.Via.TryGetValue(c, out Conversion v) && v == conv) ?? mine[0];
                foreach (MethodCostLine k in shown.Costs ?? new List<MethodCostLine>()) x.Note(CostText(x, k), 2);
                if (shown.Method.OutputNotes != null && shown.Method.OutputNotes.TryGetValue(c, out string note)) x.Note(note, 2);
            }
            if (makes.Count > 8) x.Note(x.L("diag-more", makes.Count - 8));
            foreach (MethodRecipeResult rr in notMade.Take(6))
                r.Add(x.L("diag-item-notmadeby", rr.Recipe.FullCode, rr.Disabled ? x.L("diag-meth-off") : rr.Skipped) + (rr.Disabled ? "  " + FixLink(x, "method", "on:" + rr.Recipe.Method.Code, x.L("diag-fix-method-on", rr.Recipe.Method.Code)) : ""), EmcLineStyle.Warn, 1);
            foreach (var kv in uses) r.Add(x.L("diag-item-usedin", kv.Key.FullCode, kv.Key.Method.DisplayName), EmcLineStyle.Note, 1);
        }

        /// <summary>The mod report's section on the mod's own crafting methods.</summary>
        static void MethodLinesForMod(Ctx x, string domain)
        {
            MethodSnapshot snap = x.Mod.Methods;
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
            int errs = docs.Sum(d => d.Problems.Count(p => p.Error));
            string line = x.L("diag-mod-methods", methods, recipes, errs);
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
                if (p == null || p.Source == "none" || p.Source == "undeclared") weak.Add(x.L("diag-trace-meth-noprice", UnitName(x, k.Unit)));
                else if (p.Source == "suggestion") weak.Add(x.L("diag-trace-meth-suggested", UnitName(x, k.Unit)));
            }
            MethodStack main = conv.Method.Recipe?.Outputs.FirstOrDefault(o => o.Main) ?? (conv.Method.Recipe?.Outputs.Count == 1 ? conv.Method.Recipe.Outputs[0] : null);
            if (main != null && main.Chance < 0.1 && conv.Output == c) weak.Add(x.L("diag-trace-meth-rare", (main.Chance * 100).ToString("0.##", CultureInfo.InvariantCulture)));
            return weak.Count == 0 ? null : string.Join("; ", weak);
        }

        // ---------------------------------------------------------------- start-up, what changed, symptoms, fixes

        static int MethodStartup(Ctx x)
        {
            int problems = 0;
            foreach (var (mod, error) in x.Mod.Methods.ListenerErrors)
            {
                problems++;
                x.Problem(x.L("diag-meth-listener", mod == "?" ? x.L("diag-meth-unknownmod") : ModName(x, mod), error));
                x.Explain(x.L("diag-meth-listener-why"));
                x.Fix(x.L("diag-meth-listener-fix"));
            }
            if (x.Mod.MethodChangeBeforeStart) x.Info(x.L("diag-meth-earlychange", string.Join(", ", x.Mod.MethodChangeRequests.Keys)));
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
                        MethodSnapshot snap = x.Mod.Methods;
                        x.Info(x.L("diag-sym-meth-1", snap.Docs.Count, snap.MethodCount));
                        if (held != null && !snap.Docs.Any(d => d.ModId == held.Code.Domain))
                        {
                            x.Note(x.L("diag-sym-meth-1-none", ModName(x, held.Code.Domain)), 1);
                            if (UnseenRecipeData(x).Any(u => u.Domain == held.Code.Domain)) r.Add(Link(ActionDraftMethods, held.Code.Domain, x.L("diag-meth-draft")), EmcLineStyle.Text, 1);
                        }
                        x.Info(x.L("diag-sym-meth-2"));
                        if (fresh) RecipesCard(x, convs);
                        x.Info(x.L("diag-sym-meth-3"));
                        SwitchedOffCard(x);
                        x.Info(x.L("diag-sym-meth-4"));
                        if (fresh) StuckCard(x, convs);
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
                    r.Add(FixLink(x, "methodcost", "energy=false", x.L("diag-fix-methodcost", "ChargeMethodEnergy", "false")), EmcLineStyle.Text, 1);
                    x.Note(x.L("diag-sym-powerfarm-also"), 0);
                    break;
                case "chancevalue":
                    {
                        var rare = new List<(MethodRecipe recipe, CollectibleObject c, double chance)>();
                        foreach (var kv in convs)
                        {
                            if (OutcomeOf(x, kv.Value, out Conversion used, out _) != MethodOutcome.Used || used.Output == null) continue;
                            MethodStack main = kv.Key.Outputs.FirstOrDefault(o => o.Main) ?? (kv.Key.Outputs.Count == 1 ? kv.Key.Outputs[0] : null);
                            if (main != null && main.Chance < 1) rare.Add((kv.Key, used.Output, main.Chance));
                        }
                        if (held != null) rare = rare.OrderByDescending(t => t.c == held).ThenBy(t => t.chance).ToList(); else rare = rare.OrderBy(t => t.chance).ToList();
                        if (rare.Count == 0) x.Ok(x.L("diag-sym-chance-none"));
                        foreach (var (recipe, c, chance) in rare.Take(8))
                        {
                            r.Add(x.L("diag-sym-chance-line", EmcReport.Ref(c), F(x.V(c)), recipe.FullCode, (chance * 100).ToString("0.##", CultureInfo.InvariantCulture), N(1 / chance)), EmcLineStyle.Text, 1);
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
                            int n = cfg.DisabledMethods.RemoveAll(e => string.Equals(e?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                            if (n == 0) return L("diag-done-nothing", key);
                            mod.SaveConfig();
                            mod.RequestRemap("/emc diagnose method on");
                            return L("diag-done-method-on", key);
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
                        double safe = SafePrice(PowerFarms(x, MethodConversions(x), out _), unit);
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
