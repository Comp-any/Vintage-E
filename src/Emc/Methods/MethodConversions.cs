using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>What became of one recipe of another mod's crafting method in a mapping.</summary>
    public sealed class MethodRecipeResult
    {
        public MethodRecipe Recipe;
        /// <summary>Conversions made from it (one per variant, and per tool durability).</summary>
        public int Conversions;
        /// <summary>Variants (what a named * matched) left out because one of their outputs or ingredients doesn't exist.</summary>
        public int MissingVariants;
        /// <summary>Why it was left out entirely, or null.</summary>
        public string Skipped;
        /// <summary>The code that matched nothing, when that's why.</summary>
        public string BadCode;
        public bool Disabled;
        /// <summary>Copies left out for a reason of their own (it makes itself, it nets to nothing), and the last such reason.</summary>
        public int FailedVariants;
        public string VariantFailure;
        /// <summary>Things worth knowing about how it was read (a tool with no durability, so its wear is free).</summary>
        public List<string> Notes = new List<string>();
    }

    public partial class ConversionCollector
    {
        /// <summary>Other mods' crafting methods to read (none when null).</summary>
        public MethodSnapshot Methods;
        /// <summary>The prices of their extra costs for this mapping.</summary>
        public MethodPrices Prices;
        public readonly List<MethodRecipeResult> MethodResults = new List<MethodRecipeResult>();
        /// <summary>Recipe lists left out because a crafting method describes them: list label → method code.</summary>
        public readonly Dictionary<string, string> SupersededLists = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The "supersedes" entries that matched a recipe list (the rest name nothing, for the doctor).</summary>
        public readonly HashSet<string> SupersedeHits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>"supersedes" entries that named another mod's recipe list (entry → the list's mod), which a method may not replace.</summary>
        public readonly Dictionary<string, string> SupersedeRefused = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every recipe list found on a mod system: its label → the mod it belongs to (null = unknown).</summary>
        public readonly Dictionary<string, string> RecipeListOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> RecipeListTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Most combinations of named *s one recipe may expand to, and of tool durabilities per combination.</summary>
        const int MaxVariants = 4000, MaxToolCombos = 32;

        /// <summary>
        /// Recipe list names (labels, recipe class names) → the method that supersedes them. Only methods that made at
        /// least one conversion this mapping count: one whose recipes are all switched off or left out replaces nothing,
        /// so the list it describes stays.
        /// </summary>
        Dictionary<string, MethodDef> SupersedeTargets()
        {
            var result = new Dictionary<string, MethodDef>(StringComparer.OrdinalIgnoreCase);
            if (Methods == null) return result;
            var made = new HashSet<MethodDef>(MethodResults.Where(r => r.Conversions > 0).Select(r => r.Recipe.Method));
            foreach (MethodDef m in Methods.AllMethods)
            {
                if (!made.Contains(m)) continue;
                foreach (string s in m.Supersedes) if (!result.ContainsKey(s)) result[s] = m;
            }
            return result;
        }

        /// <summary>
        /// The method that supersedes this list, or null. A method may only replace a list held by its own mod (or one
        /// its document requires): another mod's, or the game's, stays, so one document can't wipe out smithing.
        /// </summary>
        string SupersededBy(DiscoveredRecipeList found, Dictionary<string, MethodDef> targets)
        {
            if (targets.Count == 0) return null;
            foreach (string key in new[] { found.Label ?? "", found.ElementType?.Name ?? "", found.ElementType?.FullName ?? "" })
            {
                if (key.Length == 0 || !targets.TryGetValue(key, out MethodDef by)) continue;
                string owner = found.Owner ?? "?";
                if (owner != by.ModId && !by.Doc.RequiresMods.Contains(owner)) { SupersedeRefused[key] = owner; continue; }
                SupersedeHits.Add(key);
                return by.Code;
            }
            return null;
        }

        /// <summary>One copy (variant) of a recipe was left out for a reason of its own; the others still count.</summary>
        static void VariantFailed(MethodRecipeResult result, string why)
        {
            result.FailedVariants++;
            result.VariantFailure = why;
        }

        /// <summary>Every recipe of every switched-on crafting method, as conversions with their extra costs.</summary>
        void CollectFromMethods()
        {
            if (Methods == null || Prices == null) return;
            foreach (MethodDef m in Methods.AllMethods)
            {
                int made = 0;
                foreach (MethodRecipe recipe in m.Recipes)
                {
                    var result = new MethodRecipeResult { Recipe = recipe };
                    MethodResults.Add(result);
                    if (MethodSnapshot.IsDisabled(m, recipe, config.DisabledMethods)) { result.Disabled = true; continue; }
                    try { CollectMethodRecipe(recipe, result); }
                    catch (Exception e)
                    {
                        result.Skipped = "could not be read: " + e.GetType().Name + ": " + e.Message;
                        logger.Warning("EMC: crafting method {0}, recipe {1}: {2}", m.Code, recipe.Code, e);
                    }
                    if (result.Conversions == 0 && result.Skipped == null)
                        result.Skipped = result.VariantFailure ?? (result.MissingVariants > 0 ? "none of its " + result.MissingVariants + " variants has every output and ingredient it names" : "nothing could be made from it");
                    made += result.Conversions;
                }
                Count("method " + m.Code, made);
            }
        }

        string MethodSource(MethodDef m)
        {
            string modName = m.ModId;
            try { modName = sapi.ModLoader.GetMod(m.ModId)?.Info?.Name ?? m.ModId; } catch { }
            return m.DisplayName + " (" + modName + ")";
        }

        void CollectMethodRecipe(MethodRecipe recipe, MethodRecipeResult result)
        {
            MethodDef m = recipe.Method;
            // 1. The named *s: what each matches, grouped by what the * stood for.
            var named = recipe.Inputs.Where(i => i.Name != null).ToList();
            var groups = new List<(string name, MethodStack stack, List<(string value, CollectibleObject[] items)> values)>();
            foreach (MethodStack s in named)
            {
                if (MethodParser.Placeholder.IsMatch(s.Code)) { result.Skipped = "the named input " + s.Code + " can't use another {name} in its own code"; return; }
                var byValue = new Dictionary<string, List<CollectibleObject>>(StringComparer.Ordinal);
                foreach (CollectibleObject c in Resolve(s.Code, s.Type))
                {
                    string captured = Capture(s.Code, c);
                    if (captured == null || !VariantAllowed(s, captured)) continue;
                    if (!byValue.TryGetValue(captured, out var list)) byValue[captured] = list = new List<CollectibleObject>();
                    list.Add(c);
                }
                if (byValue.Count == 0) { result.Skipped = "the input " + s.Code + " matches nothing"; result.BadCode = s.Code; return; }
                groups.Add((s.Name, s, byValue.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value.ToArray())).ToList()));
            }
            long combos = 1;
            foreach (var g in groups)
            {
                combos *= g.values.Count;
                if (combos > MaxVariants) { result.Skipped = "its named *s make more than " + MaxVariants + " combinations"; return; }
            }

            var captures = new Dictionary<string, string>(StringComparer.Ordinal);
            var picked = new Dictionary<MethodStack, CollectibleObject[]>();
            void Expand(int depth)
            {
                if (result.Skipped != null) return;
                if (depth == groups.Count) { CollectVariant(recipe, result, captures, picked, m); return; }
                foreach (var (value, items) in groups[depth].values)
                {
                    captures[groups[depth].name] = value;
                    picked[groups[depth].stack] = items;
                    Expand(depth + 1);
                }
            }
            Expand(0);
        }

        /// <summary>One copy of a recipe, with every named * filled in.</summary>
        void CollectVariant(MethodRecipe recipe, MethodRecipeResult result, Dictionary<string, string> captures, Dictionary<MethodStack, CollectibleObject[]> picked, MethodDef m)
        {
            bool hasCaptures = captures.Count > 0;
            // Something a named * filled in that doesn't exist (no plate of that metal): this variant isn't made, as in the game.
            bool Missing(string code)
            {
                if (hasCaptures && MethodParser.Placeholder.IsMatch(code)) { result.MissingVariants++; return true; }
                result.Skipped = "nothing has the code " + Fill(code, captures);
                result.BadCode = Fill(code, captures);
                return true;
            }

            var inputs = new List<ConversionInput>();
            var returned = new List<KeyValuePair<CollectibleObject, double>>();
            foreach (MethodStack s in recipe.Inputs)
            {
                CollectibleObject[] choices = picked.TryGetValue(s, out var p) ? p : Resolve(Fill(s.Code, captures), s.Type).Where(c => VariantAllowed(s, Capture(Fill(s.Code, captures), c))).ToArray();
                if (choices.Length == 0) { Missing(s.Code); return; }
                double qty = s.IsLitres ? s.Quantity * PortionsPerLitre(choices[0]) : s.Quantity;
                qty *= s.Chance;
                // An ingredient that is rarely used up is like a tool's wear: never worked out backwards from the recipe.
                inputs.Add(new ConversionInput { Choices = choices, Quantity = qty, NoInfer = s.Chance < 0.1, Note = s.Chance < 1 ? Pct(s.Chance) + " chance of being used up: counts as " + Num(qty) : null });
                if (s.Returns != null)
                {
                    CollectibleObject back = ResolveOne(Fill(s.Returns.Code, captures), s.Returns.Type);
                    if (back == null) { Missing(s.Returns.Code); return; }
                    // Handed back only when the ingredient is used up.
                    double rq = (s.Returns.IsLitres ? s.Returns.Quantity * PortionsPerLitre(back) : s.Returns.Quantity) * s.Returns.Chance * s.Chance;
                    returned.Add(new KeyValuePair<CollectibleObject, double>(back, rq));
                }
            }

            // Tools: a catalyst costs nothing; wear and breaking cost that share of the tool, by each tool's own durability.
            var catalysts = new List<CollectibleObject>();
            var worn = new List<List<(double qty, CollectibleObject[] choices, string note)>>();
            foreach (MethodStack t in recipe.Tools)
            {
                string code = Fill(t.Code, captures);
                CollectibleObject[] choices = Resolve(code, t.Type).Where(c => VariantAllowed(t, Capture(code, c))).ToArray();
                if (choices.Length == 0) { Missing(t.Code); return; }
                if (!Prices.ChargeWear || (t.Wear <= 0 && t.BreakChance <= 0)) { catalysts.AddRange(choices); continue; }
                var byQty = new List<(double qty, CollectibleObject[] choices, string note)>();
                foreach (var g in choices.GroupBy(c => WearShare(c, t)).OrderBy(g => g.Key))
                {
                    if (g.Key <= 0)
                    {
                        catalysts.AddRange(g);
                        string freeWear = "the tool " + g.First().Code + " has no durability, so its wear costs nothing";
                        if (t.Wear > 0 && !result.Notes.Contains(freeWear)) result.Notes.Add(freeWear);
                        continue;
                    }
                    int dur = Durability(g.First());
                    string note = (t.Wear > 0 && dur > 0 ? "tool wear: " + Num(t.Wear) + " of " + dur + " durability" : "") + (t.BreakChance > 0 ? (t.Wear > 0 && dur > 0 ? ", " : "") + "breaks " + Pct(t.BreakChance) + " of the time" : "");
                    byQty.Add((g.Key, g.ToArray(), note));
                }
                if (byQty.Count > 0) worn.Add(byQty);
            }

            // Outputs: everything counts as its average (chance × amount; a oneOf entry its share of the weights).
            var outs = new List<(CollectibleObject c, double qty, bool main, string note)>();
            foreach (MethodStack o in recipe.Outputs)
            {
                CollectibleObject c = ResolveOne(Fill(o.Code, captures), o.Type);
                if (c == null) { Missing(o.Code); return; }
                double qty = (o.IsLitres ? o.Quantity * PortionsPerLitre(c) : o.Quantity) * o.Chance;
                outs.Add((c, qty, o.Main, o.Chance < 1 ? Pct(o.Chance) + " chance: counts as " + Num(qty) : null));
            }
            if (recipe.OneOf.Count > 0)
            {
                double total = recipe.OneOf.Sum(o => o.Weight);
                foreach (MethodStack o in recipe.OneOf)
                {
                    CollectibleObject c = ResolveOne(Fill(o.Code, captures), o.Type);
                    if (c == null) { Missing(o.Code); return; }
                    double share = o.Weight / total;
                    double qty = (o.IsLitres ? o.Quantity * PortionsPerLitre(c) : o.Quantity) * o.Chance * share;
                    outs.Add((c, qty, false, "one of " + recipe.OneOf.Count + ", weight " + Num(o.Weight) + " of " + Num(total) + (o.Chance < 1 ? ", " + Pct(o.Chance) + " chance" : "") + ": counts as " + Num(qty)));
                }
            }
            // The same thing twice counts once with both amounts; something that goes in and comes back out (a catalyst
            // written as an ingredient) is netted off.
            var merged = outs.GroupBy(o => o.c).Select(g => (c: g.Key, qty: g.Sum(o => o.qty), main: g.Any(o => o.main), note: string.Join("; ", g.Select(o => o.note).Where(n => n != null)))).ToList();
            for (int i = 0; i < merged.Count; i++)
            {
                var o = merged[i];
                int k = inputs.FindIndex(inp => inp.Choices.Length == 1 && inp.Choices[0] == o.c);
                if (k < 0) continue;
                double net = Math.Min(o.qty, inputs[k].Quantity);
                inputs[k].Quantity -= net;
                merged[i] = (o.c, o.qty - net, o.main, o.note);
                if (inputs[k].Quantity <= 1e-9) inputs.RemoveAt(k);
            }
            merged.RemoveAll(o => o.qty <= 1e-9 && !o.main);
            if (merged.Count == 0 || merged.Any(o => o.main && o.qty <= 1e-9))
            {
                VariantFailed(result, "every output is also an ingredient: it makes nothing new");
                return;
            }

            List<MethodCostLine> costs = CostLines(recipe);
            double extra = costs.Sum(c => c.Emc);
            if (inputs.Count == 0 && worn.Count == 0 && extra <= 0)
            {
                VariantFailed(result, "it has no ingredients and its costs come to 0 EMC here (an energy unit with no price?), so it would make something from nothing");
                return;
            }

            // One conversion per combination of tool durabilities (usually one).
            var toolCombos = new List<List<(double qty, CollectibleObject[] choices, string note)>> { new List<(double, CollectibleObject[], string)>() };
            foreach (var options in worn)
            {
                var next = new List<List<(double, CollectibleObject[], string)>>();
                foreach (var combo in toolCombos) foreach (var opt in options) next.Add(new List<(double, CollectibleObject[], string)>(combo) { opt });
                toolCombos = next;
                if (toolCombos.Count > MaxToolCombos)
                {
                    // Too many: each tool once, at its middle durability.
                    toolCombos = new List<List<(double, CollectibleObject[], string)>> { worn.Select(w => (w[w.Count / 2].qty, w.SelectMany(x => x.choices).ToArray(), w[w.Count / 2].note)).ToList() };
                    break;
                }
            }

            var mainOut = merged.Count == 1 ? merged[0] : merged.FirstOrDefault(o => o.main);
            bool joint = mainOut.c == null;
            var notes = merged.Where(o => !string.IsNullOrEmpty(o.note)).GroupBy(o => o.c).ToDictionary(g => g.Key, g => g.First().note);
            foreach (var combo in toolCombos)
            {
                var conv = new Conversion
                {
                    Kind = m.Kind,
                    Source = MethodSource(m),
                    ExtraCost = extra,
                    Costs = costs,
                    Method = new MethodRef { Method = m, Recipe = recipe, Captures = hasCaptures ? new Dictionary<string, string>(captures) : null, Catalysts = catalysts.Distinct().ToList(), OutputNotes = notes }
                };
                foreach (ConversionInput inp in inputs) conv.Inputs.Add(new ConversionInput { Choices = inp.Choices, Quantity = inp.Quantity, NoInfer = inp.NoInfer, Note = inp.Note });
                foreach (var (qty, choices, note) in combo) conv.Inputs.Add(new ConversionInput { Choices = choices, Quantity = qty, NoInfer = true, Note = note });
                var back = new List<KeyValuePair<CollectibleObject, double>>(returned);
                if (joint)
                {
                    conv.JointOutputs = merged.Select(o => new KeyValuePair<CollectibleObject, double>(o.c, o.qty)).ToList();
                    conv.OutputQuantity = 1;
                }
                else
                {
                    conv.Output = mainOut.c;
                    conv.OutputQuantity = mainOut.qty;
                    conv.Method.Byproducts = new HashSet<CollectibleObject>();
                    foreach (var o in merged) if (o.c != mainOut.c) { back.Add(new KeyValuePair<CollectibleObject, double>(o.c, o.qty)); conv.Method.Byproducts.Add(o.c); }
                }
                if (back.Count > 0) conv.Returned = back.GroupBy(b => b.Key).Select(g => new KeyValuePair<CollectibleObject, double>(g.Key, g.Sum(b => b.Value))).ToList();
                string why = AddMethodConversion(conv);
                if (why != null) { VariantFailed(result, why); continue; }
                result.Conversions++;
            }
        }

        /// <summary>Like <see cref="AddIfSane"/>, but a recipe with no ingredients is fine when it costs something (a pump: power → water).</summary>
        string AddMethodConversion(Conversion conv)
        {
            if (conv.JointOutputs == null && conv.OutputQuantity <= 0) return "it makes none of its output";
            foreach (ConversionInput input in conv.Inputs)
            {
                CollectibleObject[] outs = conv.JointOutputs != null ? conv.JointOutputs.Select(o => o.Key).ToArray() : new[] { conv.Output };
                if (input.Choices.Any(c => outs.Contains(c)))
                {
                    if (input.Choices.All(c => outs.Contains(c))) return "it makes itself (an output is the only choice for an ingredient)";
                    input.Choices = input.Choices.Where(c => !outs.Contains(c)).ToArray();
                }
            }
            if (conv.Inputs.Count == 0 && conv.ExtraCost <= 0) return "it makes something from nothing";
            Conversions.Add(conv);
            return null;
        }

        /// <summary>A recipe's extra costs, each priced (and charged unless its kind is switched off).</summary>
        List<MethodCostLine> CostLines(MethodRecipe recipe)
        {
            var lines = new List<MethodCostLine>();
            MethodCosts c = recipe.Costs;
            foreach (var (unit, amount) in c.Energy)
            {
                MethodUnitPrice p = Prices.Unit(unit);
                lines.Add(new MethodCostLine { Kind = "energy", Unit = p.Code, Amount = amount, Price = p.Price, PriceSource = p.SourceText, Charged = Prices.ChargeEnergy, Emc = Prices.ChargeEnergy ? amount * p.Price : 0 });
            }
            if (c.HeatSeconds > 0 && c.HeatTemperature > 0)
            {
                double amount = c.HeatTemperature * c.HeatSeconds;
                lines.Add(new MethodCostLine { Kind = "heat", Amount = amount, Price = Prices.HeatRate, PriceSource = Prices.HeatSource, Charged = Prices.ChargeHeat, Emc = Prices.ChargeHeat ? amount * Prices.HeatRate : 0 });
            }
            if (c.Seconds > 0 && Prices.SecondRate > 0)
                lines.Add(new MethodCostLine { Kind = "time", Amount = c.Seconds, Price = Prices.SecondRate, PriceSource = "your setting MethodEmcPerSecond", Emc = c.Seconds * Prices.SecondRate });
            if (c.Emc > 0) lines.Add(new MethodCostLine { Kind = "emc", Amount = c.Emc, Price = 1, PriceSource = "the recipe", Emc = c.Emc });
            return lines;
        }

        /// <summary>The share of a tool one run uses up: durability lost ÷ its durability, plus the chance it breaks.</summary>
        static double WearShare(CollectibleObject c, MethodStack t)
        {
            int dur = Durability(c);
            double share = (t.Wear > 0 && dur > 0 ? t.Wear / dur : 0) + t.BreakChance;
            return Math.Round(Math.Min(1, share), 9);
        }

        static int Durability(CollectibleObject c)
        {
            try { return c.GetMaxDurability(new ItemStack(c)); }
            catch { return 0; }
        }

        /// <summary>Every block/item a code (wildcards allowed) matches, of the given type when set.</summary>
        CollectibleObject[] Resolve(string code, EnumItemClass? type)
        {
            if (string.IsNullOrWhiteSpace(code)) return Array.Empty<CollectibleObject>();
            string key = (type == EnumItemClass.Block ? "block:" : type == EnumItemClass.Item ? "item:" : "") + code;
            if (choiceCache.TryGetValue("method|" + key, out var cached)) return cached;
            CollectibleObject[] found = index.Match(key).ToArray();
            choiceCache["method|" + key] = found;
            return found;
        }

        /// <summary>The one block/item a code names (an item first when the type isn't given), or null.</summary>
        CollectibleObject ResolveOne(string code, EnumItemClass? type)
        {
            CollectibleObject[] all = Resolve(code, type);
            if (all.Length == 0) return null;
            return all.FirstOrDefault(c => c.ItemClass == EnumItemClass.Item) ?? all[0];
        }

        /// <summary>What the single * of <paramref name="pattern"/>'s path matched in <paramref name="c"/>'s code, or null.</summary>
        internal static string Capture(string pattern, CollectibleObject c)
        {
            if (c?.Code == null || pattern == null) return null;
            int colon = pattern.IndexOf(':');
            string path = colon >= 0 ? pattern.Substring(colon + 1) : pattern;
            int star = path.IndexOf('*');
            if (star < 0 || path.IndexOf('*', star + 1) >= 0) return "";
            string prefix = path.Substring(0, star), suffix = path.Substring(star + 1);
            string actual = c.Code.Path;
            if (actual.Length < prefix.Length + suffix.Length || !actual.StartsWith(prefix, StringComparison.Ordinal) || !actual.EndsWith(suffix, StringComparison.Ordinal)) return null;
            return actual.Substring(prefix.Length, actual.Length - prefix.Length - suffix.Length);
        }

        static bool VariantAllowed(MethodStack s, string captured)
        {
            if (captured == null) return false;
            if (captured.Length == 0) return true; // no single * to filter on
            if (s.AllowedVariants != null && s.AllowedVariants.Length > 0 && !s.AllowedVariants.Contains(captured, StringComparer.Ordinal)) return false;
            if (s.SkipVariants != null && s.SkipVariants.Contains(captured, StringComparer.Ordinal)) return false;
            return true;
        }

        static string Fill(string code, Dictionary<string, string> captures)
        {
            if (code == null || captures.Count == 0 || code.IndexOf('{') < 0) return code;
            return MethodParser.Placeholder.Replace(code, mt => captures.TryGetValue(mt.Groups[1].Value, out string v) ? v : mt.Value);
        }

        static string Num(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
        static string Pct(double f) => (f * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}
