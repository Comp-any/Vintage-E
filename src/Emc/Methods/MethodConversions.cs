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
        /// <summary>Variants switched off by a DisabledMethods entry with "?name=value".</summary>
        public int DisabledVariants;
        /// <summary>Choices of an anyOf slot that couldn't be made (a code no loaded mod has), with the first reason.</summary>
        public int FailedChoices;
        public string ChoiceFailure;
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
        const int MaxVariants = 4000, MaxToolCombos = 32, MaxChoices = 32;

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
                // The server's own documents may replace any list: the server owner decides what counts there.
                if (!by.Doc.FromServer && owner != by.ModId && !by.Doc.Requires(owner)) { SupersedeRefused[key] = owner; continue; }
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
            // 0. A slot with alternatives ("anyOf": 1 fat or 10 lard) makes one copy of the recipe per choice; the cheapest
            // counts, as with any two recipes for one thing. A choice that can't be made (its mod isn't loaded) is left out.
            var slots = recipe.Inputs.Concat(recipe.Tools).Where(st => st.AnyOf != null).ToList();
            long choices = 1;
            foreach (MethodStack slot in slots)
            {
                choices *= slot.AnyOf.Count;
                if (choices > MaxChoices) { result.Skipped = "its anyOf alternatives make more than " + MaxChoices + " combinations"; return; }
            }
            if (slots.Count == 0) { CollectChoice(recipe, recipe.Inputs, recipe.Tools, result); return; }
            var picked = new Dictionary<MethodStack, MethodStack>();
            string firstSkip = null, firstBad = null;
            void Pick(int depth)
            {
                if (depth == slots.Count)
                {
                    var ins = recipe.Inputs.Select(st => st.AnyOf != null ? picked[st] : st).ToList();
                    var tools = recipe.Tools.Select(st => st.AnyOf != null ? picked[st] : st).ToList();
                    CollectChoice(recipe, ins, tools, result);
                    if (result.Skipped != null)
                    {
                        result.FailedChoices++;
                        result.ChoiceFailure ??= result.Skipped;
                        firstSkip ??= result.Skipped;
                        firstBad ??= result.BadCode;
                        result.Skipped = null;
                        result.BadCode = null;
                    }
                    return;
                }
                foreach (MethodStack alt in slots[depth].AnyOf) { picked[slots[depth]] = alt; Pick(depth + 1); }
            }
            Pick(0);
            if (result.Conversions == 0 && firstSkip != null) { result.Skipped = firstSkip; result.BadCode = firstBad; }
        }

        /// <summary>One choice of each anyOf slot: the named *s expanded into variants.</summary>
        void CollectChoice(MethodRecipe recipe, List<MethodStack> ins, List<MethodStack> tools, MethodRecipeResult result)
        {
            MethodDef m = recipe.Method;
            // 1. The named *s (of ingredients and tools): what each matches, grouped by what the * stood for.
            var named = ins.Concat(tools).Where(i => i.Name != null).ToList();
            var groups = new List<(string name, MethodStack stack, List<(string value, CollectibleObject[] items)> values)>();
            foreach (MethodStack st in named)
            {
                if (MethodParser.Placeholder.IsMatch(st.Code)) { result.Skipped = "the named input " + st.Code + " can't use another {name} in its own code"; return; }
                var byValue = new Dictionary<string, List<CollectibleObject>>(StringComparer.Ordinal);
                foreach (CollectibleObject c in Resolve(st.Code, st.Type))
                {
                    string captured = Capture(st.Code, c);
                    if (captured == null || !VariantAllowed(st, captured)) continue;
                    if (!byValue.TryGetValue(captured, out var list)) byValue[captured] = list = new List<CollectibleObject>();
                    list.Add(c);
                }
                if (byValue.Count == 0) { result.Skipped = "the input " + st.Code + " matches nothing"; result.BadCode = st.Code; return; }
                groups.Add((st.Name, st, byValue.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value.ToArray())).ToList()));
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
                if (depth == groups.Count)
                {
                    if (MethodSnapshot.IsVariantDisabled(m, recipe, captures, config.DisabledMethods)) { result.DisabledVariants++; return; }
                    CollectVariant(recipe, ins, tools, result, captures, picked, m);
                    return;
                }
                foreach (var (value, items) in groups[depth].values)
                {
                    captures[groups[depth].name] = value;
                    picked[groups[depth].stack] = items;
                    Expand(depth + 1);
                }
            }
            Expand(0);
        }

        /// <summary>A variant with no number for it in a table (and no default): left out, like one with an output that doesn't exist.</summary>
        static void NoNumber(MethodRecipeResult result, MethodTable t, Dictionary<string, string> captures)
        {
            result.MissingVariants++;
            string note = "a table by \"" + t.By + "\" has no number for " + (captures.TryGetValue(t.By ?? "", out string v) ? "\"" + v + "\"" : "it") + " and no \"default\", so that variant is left out";
            if (result.Notes.Count < 8 && !result.Notes.Contains(note)) result.Notes.Add(note);
        }

        /// <summary>One copy of a recipe, with every named * filled in.</summary>
        void CollectVariant(MethodRecipe recipe, List<MethodStack> ins, List<MethodStack> tools, MethodRecipeResult result, Dictionary<string, string> captures, Dictionary<MethodStack, CollectibleObject[]> picked, MethodDef m)
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
            // A number of a stack for this variant (its table's), or false when the table has none.
            bool Get(MethodStack st, string field, double fixedValue, out double v)
            {
                double? got = st.Get(field, fixedValue, captures);
                v = got ?? 0;
                if (got == null) { NoNumber(result, st.Tables[field], captures); return false; }
                return true;
            }

            var inputs = new List<ConversionInput>();
            var returned = new List<KeyValuePair<CollectibleObject, double>>();
            var optional = new HashSet<CollectibleObject>();
            foreach (MethodStack st in ins)
            {
                CollectibleObject[] choices = picked.TryGetValue(st, out var p) ? p : Resolve(Fill(st.Code, captures), st.Type).Where(c => VariantAllowed(st, Capture(Fill(st.Code, captures), c))).ToArray();
                if (choices.Length == 0) { Missing(st.Code); return; }
                if (!Get(st, "quantity", st.Quantity, out double amount) || !Get(st, "chance", st.Chance, out double chance)) return;
                double qty = st.IsLitres ? amount * PortionsPerLitre(choices[0]) : amount;
                qty *= chance;
                // An ingredient that is rarely used up is like a tool's wear: never worked out backwards from the recipe.
                inputs.Add(new ConversionInput { Choices = choices, Quantity = qty, NoInfer = chance < 0.1, Note = chance < 1 ? Pct(chance) + " chance of being used up: counts as " + Num(qty) : null });
                if (st.Returns != null)
                {
                    CollectibleObject back = ResolveOne(Fill(st.Returns.Code, captures), st.Returns.Type);
                    if (back == null) { Missing(st.Returns.Code); return; }
                    if (!Get(st.Returns, "quantity", st.Returns.Quantity, out double ra) || !Get(st.Returns, "chance", st.Returns.Chance, out double rc)) return;
                    // Handed back only when the ingredient is used up.
                    double rq = (st.Returns.IsLitres ? ra * PortionsPerLitre(back) : ra) * rc * chance;
                    returned.Add(new KeyValuePair<CollectibleObject, double>(back, rq));
                    if (!st.Returns.WaitForValue) optional.Add(back);
                }
            }

            // Tools: a catalyst costs nothing; wear and breaking cost that share of the tool, by each tool's own durability.
            var catalysts = new List<CollectibleObject>();
            var worn = new List<List<(double qty, CollectibleObject[] choices, string note)>>();
            foreach (MethodStack t in tools)
            {
                string code = Fill(t.Code, captures);
                CollectibleObject[] choices = picked.TryGetValue(t, out var tp) ? tp : Resolve(code, t.Type).Where(c => VariantAllowed(t, Capture(code, c))).ToArray();
                if (choices.Length == 0) { Missing(t.Code); return; }
                if (!Get(t, "wear", t.Wear, out double wear) || !Get(t, "breakChance", t.BreakChance, out double breaks) || !Get(t, "quantity", t.Quantity, out double count)) return;
                if (!Prices.ChargeWear || (wear <= 0 && breaks <= 0)) { catalysts.AddRange(choices); continue; }
                var byQty = new List<(double qty, CollectibleObject[] choices, string note)>();
                foreach (var g in choices.GroupBy(c => WearShare(c, wear, breaks) * count).OrderBy(g => g.Key))
                {
                    if (g.Key <= 0)
                    {
                        catalysts.AddRange(g);
                        string freeWear = "the tool " + g.First().Code + " has no durability, so its wear costs nothing";
                        if (wear > 0 && !result.Notes.Contains(freeWear)) result.Notes.Add(freeWear);
                        continue;
                    }
                    int dur = Durability(g.First());
                    string note = (count != 1 ? Num(count) + " × " : "") + (wear > 0 && dur > 0 ? "tool wear: " + Num(wear) + " of " + dur + " durability" : "") + (breaks > 0 ? (wear > 0 && dur > 0 ? ", " : "") + "breaks " + Pct(Math.Min(1, breaks)) + (breaks > 1 ? " (" + Num(breaks) + " expected)" : "") + " of the time" : "");
                    byQty.Add((g.Key, g.ToArray(), note));
                }
                if (byQty.Count > 0) worn.Add(byQty);
            }

            // Outputs: everything counts as its average (chance × amount; a oneOf entry its share of the weights).
            var outs = new List<(CollectibleObject c, double qty, bool main, string note, double share, bool wait)>();
            foreach (MethodStack o in recipe.Outputs)
            {
                CollectibleObject c = ResolveOne(Fill(o.Code, captures), o.Type);
                if (c == null) { Missing(o.Code); return; }
                if (!Get(o, "quantity", o.Quantity, out double amount) || !Get(o, "chance", o.Chance, out double chance) || !Get(o, "share", o.Share, out double share)) return;
                double qty = (o.IsLitres ? amount * PortionsPerLitre(c) : amount) * chance;
                outs.Add((c, qty, o.Main, chance < 1 ? Pct(chance) + " chance: counts as " + Num(qty) : null, share, o.WaitForValue));
            }
            if (recipe.OneOf.Count > 0)
            {
                var weights = new List<double>();
                foreach (MethodStack o in recipe.OneOf) { if (!Get(o, "weight", o.Weight, out double w)) return; weights.Add(w); }
                double total = weights.Sum();
                for (int i = 0; i < recipe.OneOf.Count; i++)
                {
                    MethodStack o = recipe.OneOf[i];
                    CollectibleObject c = ResolveOne(Fill(o.Code, captures), o.Type);
                    if (c == null) { Missing(o.Code); return; }
                    if (!Get(o, "quantity", o.Quantity, out double amount) || !Get(o, "chance", o.Chance, out double chance) || !Get(o, "share", o.Share, out double share)) return;
                    double part = weights[i] / total;
                    double qty = (o.IsLitres ? amount * PortionsPerLitre(c) : amount) * chance * part;
                    outs.Add((c, qty, false, "one of " + recipe.OneOf.Count + ", weight " + Num(weights[i]) + " of " + Num(total) + (chance < 1 ? ", " + Pct(chance) + " chance" : "") + ": counts as " + Num(qty), share, o.WaitForValue));
                }
            }
            // The same thing twice counts once with both amounts; something that goes in and comes back out (a catalyst
            // written as an ingredient) is netted off.
            var merged = outs.GroupBy(o => o.c).Select(g => (c: g.Key, qty: g.Sum(o => o.qty), main: g.Any(o => o.main), note: string.Join("; ", g.Select(o => o.note).Where(n => n != null)), share: g.Sum(o => o.share), wait: g.All(o => o.wait))).ToList();
            for (int i = 0; i < merged.Count; i++)
            {
                var o = merged[i];
                int k = inputs.FindIndex(inp => inp.Choices.Length == 1 && inp.Choices[0] == o.c);
                if (k < 0) continue;
                double net = Math.Min(o.qty, inputs[k].Quantity);
                inputs[k].Quantity -= net;
                merged[i] = (o.c, o.qty - net, o.main, o.note, o.share, o.wait);
                if (inputs[k].Quantity <= 1e-9) inputs.RemoveAt(k);
            }
            merged.RemoveAll(o => o.qty <= 1e-9 && !o.main);
            if (merged.Count == 0 || merged.Any(o => o.main && o.qty <= 1e-9))
            {
                VariantFailed(result, "every output is also an ingredient: it makes nothing new");
                return;
            }

            List<MethodCostLine> costs = CostLines(recipe, captures, result, out double seconds);
            if (costs == null) return;
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

            var mainOut = merged.Count == 1 && recipe.OneOf.Count == 0 ? merged[0] : merged.FirstOrDefault(o => o.main);
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
                    Method = new MethodRef { Method = m, Recipe = recipe, Captures = hasCaptures ? new Dictionary<string, string>(captures) : null, Catalysts = catalysts.Distinct().ToList(), OutputNotes = notes, Seconds = seconds }
                };
                foreach (ConversionInput inp in inputs) conv.Inputs.Add(new ConversionInput { Choices = inp.Choices, Quantity = inp.Quantity, NoInfer = inp.NoInfer, Note = inp.Note });
                foreach (var (qty, choices, note) in combo) conv.Inputs.Add(new ConversionInput { Choices = choices, Quantity = qty, NoInfer = true, Note = note });
                var back = new List<KeyValuePair<CollectibleObject, double>>(returned);
                var notWaited = new HashSet<CollectibleObject>(optional);
                if (joint)
                {
                    conv.JointOutputs = merged.Select(o => new KeyValuePair<CollectibleObject, double>(o.c, o.qty)).ToList();
                    // Shared out by weight when the document gives "share"s, evenly otherwise.
                    if (merged.Any(o => Math.Abs(o.share - 1) > 1e-12)) conv.JointShares = merged.Select(o => o.share).ToList();
                    conv.OutputQuantity = 1;
                }
                else
                {
                    conv.Output = mainOut.c;
                    conv.OutputQuantity = mainOut.qty;
                    conv.Method.Byproducts = new HashSet<CollectibleObject>();
                    foreach (var o in merged)
                    {
                        if (o.c == mainOut.c) continue;
                        back.Add(new KeyValuePair<CollectibleObject, double>(o.c, o.qty));
                        conv.Method.Byproducts.Add(o.c);
                        if (!o.wait) notWaited.Add(o.c);
                    }
                }
                if (back.Count > 0) conv.Returned = back.GroupBy(b => b.Key).Select(g => new KeyValuePair<CollectibleObject, double>(g.Key, g.Sum(b => b.Value))).ToList();
                if (conv.Returned != null && notWaited.Count > 0) conv.OptionalReturned = new HashSet<CollectibleObject>(notWaited.Where(c => conv.Returned.Any(r => r.Key == c)));
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

        /// <summary>
        /// A recipe's extra costs for one variant, each priced (and charged unless its kind is switched off), with its
        /// machine time; null when a table has no number for the variant.
        /// </summary>
        List<MethodCostLine> CostLines(MethodRecipe recipe, Dictionary<string, string> captures, MethodRecipeResult result, out double seconds)
        {
            seconds = 0;
            var lines = new List<MethodCostLine>();
            MethodCosts c = recipe.Costs;
            bool Get(MethodTable t, double fixedValue, out double v)
            {
                v = fixedValue;
                if (t == null) return true;
                if (t.TryGet(captures, out v)) return true;
                NoNumber(result, t, captures);
                return false;
            }
            for (int i = 0; i < c.Energy.Count; i++)
            {
                var (unit, fixedAmount) = c.Energy[i];
                if (!Get(i < c.EnergyTables.Count ? c.EnergyTables[i] : null, fixedAmount, out double amount)) return null;
                if (amount <= 0) continue;
                MethodUnitPrice p = Prices.Unit(unit);
                lines.Add(new MethodCostLine { Kind = "energy", Unit = p.Code, Amount = amount, Price = p.Price, PriceSource = p.SourceText, Charged = Prices.ChargeEnergy, Emc = Prices.ChargeEnergy ? amount * p.Price : 0 });
            }
            double heat = 0;
            for (int i = 0; i < c.Heat.Count; i++)
            {
                var tables = i < c.HeatTables.Count ? c.HeatTables[i] : (null, null);
                if (!Get(tables.Temperature, c.Heat[i].Temperature, out double temp) || !Get(tables.Seconds, c.Heat[i].Seconds, out double secs)) return null;
                if (temp > 0 && secs > 0) heat += temp * secs;
            }
            if (heat > 0) lines.Add(new MethodCostLine { Kind = "heat", Amount = heat, Price = Prices.HeatRate, PriceSource = Prices.HeatSource, Charged = Prices.ChargeHeat, Emc = Prices.ChargeHeat ? heat * Prices.HeatRate : 0 });
            if (!Get(c.SecondsTable, c.Seconds, out seconds)) return null;
            if (seconds > 0 && Prices.SecondRate > 0)
                lines.Add(new MethodCostLine { Kind = "time", Amount = seconds, Price = Prices.SecondRate, PriceSource = "your setting MethodEmcPerSecond", Emc = seconds * Prices.SecondRate });
            if (!Get(c.EmcTable, c.Emc, out double flat)) return null;
            if (flat > 0) lines.Add(new MethodCostLine { Kind = "emc", Amount = flat, Price = 1, PriceSource = "the recipe", Emc = flat });
            return lines;
        }

        /// <summary>The share of a tool one run uses up: durability lost ÷ its durability, plus the chance it breaks (more than 1 for a step repeated).</summary>
        static double WearShare(CollectibleObject c, double wear, double breakChance)
        {
            int dur = Durability(c);
            double share = (wear > 0 && dur > 0 ? wear / dur : 0) + breakChance;
            return Math.Round(share, 9);
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
