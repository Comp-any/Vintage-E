using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// 1.23.0: other mods' crafting methods. Reads documents from strings (every key, every problem), fills readers'
    /// templates, runs the solver on made-up recipes (shared-out recipes with several ingredients, tool wear never
    /// worked backwards), then sends a real document through the game's event bus as another mod would, maps with it,
    /// checks the values, costs, explanations and doctor reports, and maps again without it: every value must be back.
    /// </summary>
    public partial class SelfTest
    {
        void CheckMethods()
        {
            try { CheckMethodParser(); } catch (Exception e) { Check(false, "methods: the parser checks threw " + e); }
            try { CheckMethodReaders(); } catch (Exception e) { Check(false, "methods: the reader checks threw " + e); }
            try { CheckMethodSolver(); } catch (Exception e) { Check(false, "methods: the solver checks threw " + e); }
            try { CheckMethodsLive(); } catch (Exception e) { Check(false, "methods: the live checks threw " + e); }
            try { DraftForUnseenMods(); } catch (Exception e) { Check(false, "methods: drafting for other mods threw " + e); }
        }

        /// <summary>A drafted method file for every loaded mod whose recipe data Vintage-E can't read (the 50-mod test reads them to write Vintage-E's own method files).</summary>
        void DraftForUnseenMods()
        {
            List<string> domains = EmcDiagnose.UnseenDomains(mod, sapi);
            Line("methods: " + domains.Count + " mod(s) keep recipe data Vintage-E can't read: " + string.Join(", ", domains));
            foreach (string domain in domains.Take(40))
            {
                EmcReport r = EmcDiagnose.Run(mod, sapi, null, "en", "draftmethods", domain, null);
                string text = r.PlainText(sapi.World);
                File.WriteAllText(Path.Combine(VintageEModSystem.DataFolder(sapi), "diagnose-draftmethods-" + domain + ".txt"), text);
                Check(!text.Contains("vintagee:diag-"), "methods: drafted a method file for " + domain + " (" + Line1(text, "Drafted") + ")");
            }
            // And the author's check for every mod that sends crafting methods (Vintage-E's own files for other mods included).
            foreach (string domain in mod.Methods.Mods.Where(m => m != VintageEModSystem.ModId).Take(40).ToList())
            {
                string text = EmcDiagnose.Run(mod, sapi, null, "en", "methodcheck", domain, null).PlainText(sapi.World);
                File.WriteAllText(Path.Combine(VintageEModSystem.DataFolder(sapi), "diagnose-methodcheck-" + domain + ".txt"), text);
                Check(!text.Contains("vintagee:diag-"), "methods: checked the crafting methods of " + domain + " (" + Line1(text, " used, ") + ")");
            }
        }

        static MethodDoc ParseTest(string json) => MethodParser.Parse(json, "vintagee", "self-test", "file", null);

        void CheckMethodParser()
        {
            MethodDoc d = ParseTest(@"{
                ""format"": 1,
                ""energyUnits"": { ""testpower"": { ""name"": ""TP"", ""emcPerUnit"": 2, ""generators"": [ { ""burns"": ""game:charcoal"", ""makes"": 16 } ] } },
                ""methods"": [ {
                    ""code"": ""press"", ""name"": ""Press"", ""kind"": ""processing"", ""machine"": ""block:vintagee:transmutationtable"", ""supersedes"": [ ""SomeRecipe"" ],
                    ""recipes"": [
                        { ""code"": ""a"", ""inputs"": [ { ""code"": ""game:ingot-*"", ""name"": ""metal"", ""quantity"": 2, ""allowedVariants"": [ ""copper"" ] }, { ""code"": ""game:waterportion"", ""litres"": 1.5, ""returns"": { ""code"": ""game:bucket"" } } ],
                          ""tools"": [ { ""code"": ""game:hammer-*"", ""wear"": 1, ""breakChance"": ""5%"" } ],
                          ""outputs"": [ { ""code"": ""game:metalplate-{metal}"", ""main"": true }, { ""code"": ""game:metalbit-{metal}"", ""quantity"": { ""avg"": 2, ""var"": 1 }, ""chance"": ""25%"" } ],
                          ""costs"": { ""energy"": [ { ""unit"": ""testpower"", ""amount"": 400 } ], ""heat"": { ""temperature"": 900, ""seconds"": 10 }, ""seconds"": 4, ""emc"": 3 },
                          ""note"": ""hello"" },
                        { ""code"": ""b"", ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""oneOf"": [ { ""code"": ""game:nugget-nativecopper"", ""weight"": 3 }, { ""code"": ""game:nugget-nativegold"", ""weight"": 1 } ] }
                    ] } ] }");
            Check(d.Usable && d.Format == 1 && d.Methods.Count == 1 && d.Methods[0].Recipes.Count == 2, "methods: a document is read (" + d.Methods.Count + " methods, " + d.RecipeCount + " recipes, " + string.Join("; ", d.Problems) + ")");
            MethodDef m = d.Methods.FirstOrDefault();
            MethodRecipe a = m?.Recipes.FirstOrDefault();
            Check(m != null && m.Code == "vintagee:press" && m.Kind == ConversionKind.Processing && m.Supersedes.Contains("SomeRecipe") && m.Machine == "block:vintagee:transmutationtable", "methods: a method's code gets its domain, its kind, machine and supersedes are read");
            Check(d.Units.TryGetValue("vintagee:testpower", out MethodEnergyUnit u) && u.EmcPerUnit == 2 && u.Generators.Count == 1 && u.Generators[0].Makes == 16, "methods: an energy unit with a suggested price and a generator");
            Check(a != null && a.Inputs.Count == 2 && a.Inputs[0].Name == "metal" && a.Inputs[0].Quantity == 2 && a.Inputs[0].AllowedVariants?.Contains("copper") == true, "methods: a named input with a quantity and allowed variants");
            Check(a != null && a.Inputs[1].IsLitres && a.Inputs[1].Quantity == 1.5 && a.Inputs[1].Returns?.Code == "game:bucket", "methods: litres and what's handed back");
            Check(a != null && a.Tools.Count == 1 && a.Tools[0].Wear == 1 && Math.Abs(a.Tools[0].BreakChance - 0.05) < 1e-12, "methods: a tool with wear and a percent break chance");
            Check(a != null && a.Outputs.Count == 2 && a.Outputs[0].Main && a.Outputs[1].Quantity == 2 && Math.Abs(a.Outputs[1].Chance - 0.25) < 1e-12, "methods: a main output, an average amount ({ avg, var }) and \"25%\"");
            Check(a != null && a.Costs.Energy.Count == 1 && a.Costs.Energy[0].Unit == "vintagee:testpower" && a.Costs.HeatTemperature == 900 && a.Costs.HeatSeconds == 10 && a.Costs.Seconds == 4 && a.Costs.Emc == 3 && a.Note == "hello",
                "methods: every kind of cost and the note");
            MethodRecipe b = m?.Recipes.ElementAtOrDefault(1);
            Check(b != null && b.OneOf.Count == 2 && b.OneOf[0].Weight == 3, "methods: a oneOf table with weights");
            Check(!string.IsNullOrEmpty(d.Canonical), "methods: the document's text is kept for the cache's fingerprint");

            // Problems: each with where it is.
            MethodDoc bad = ParseTest(@"{ ""format"": 1, ""methodz"": [], ""methods"": [ { ""code"": ""x"", ""recipes"": [
                { ""code"": ""chance"", ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""game:gravel-granite"", ""chance"": 1.5 } ] },
                { ""code"": ""wild"", ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""game:gravel-*"" } ] },
                { ""code"": ""noname"", ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""game:gravel-{rock}"" } ] },
                { ""code"": ""twostars"", ""inputs"": [ { ""code"": ""game:*-*"", ""name"": ""rock"" } ], ""outputs"": [ { ""code"": ""game:gravel-{rock}"" } ] },
                { ""code"": ""nothing"", ""outputs"": [ { ""code"": ""game:gravel-granite"" } ] },
                { ""code"": ""twomains"", ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""game:gravel-granite"", ""main"": true }, { ""code"": ""game:sand-granite"", ""main"": true } ] },
                { ""code"": ""ok"", ""inputs"": [ { ""code"": ""game:stone-granite"", ""quantiy"": 2 } ], ""outputs"": [ { ""code"": ""game:gravel-granite"" } ] }
            ] } ] }");
            string P(string path) => string.Join("; ", bad.Problems.Where(p => p.Path.Contains(path)).Select(p => p.ToString()));
            Check(bad.Problems.Any(p => p.Path == "methodz" && p.Message.Contains("did you mean \"methods\"")), "methods: an unknown key is named with a \"did you mean\" (" + P("methodz") + ")");
            Check(bad.Problems.Any(p => p.Error && p.Path.EndsWith(".chance") && p.Message.Contains("over 100%")), "methods: a chance over 100% is refused (" + P("chance") + ")");
            Check(bad.Problems.Any(p => p.Error && p.Message.Contains("can't be a wildcard")), "methods: an output can't be a wildcard");
            Check(bad.Problems.Any(p => p.Error && p.Message.Contains("{rock} has nothing to fill it")), "methods: a {name} no input names is refused");
            Check(bad.Problems.Any(p => p.Error && p.Message.Contains("exactly one *")), "methods: a named input needs exactly one *");
            Check(bad.Problems.Any(p => p.Error && p.Message.Contains("from nothing")), "methods: a recipe with no inputs and no costs is refused");
            Check(bad.Problems.Any(p => p.Error && p.Message.Contains("more than one output is marked")), "methods: two main outputs are refused");
            Check(bad.Methods.Count == 1 && bad.Methods[0].Recipes.Count == 1 && bad.Methods[0].Recipes[0].Code == "ok" && bad.Problems.Any(p => p.Path.EndsWith("quantiy") && p.Message.Contains("\"quantity\"")),
                "methods: only the good recipe is kept, a misspelt key is a warning with a suggestion (" + bad.Methods.FirstOrDefault()?.Recipes.Count + " kept)");

            MethodDoc newer = ParseTest(@"{ ""format"": 2, ""methods"": [] }");
            Check(!newer.Usable && newer.Error != null && newer.Error.Contains("newer"), "methods: a newer format is left out, saying to update (" + newer.Error + ")");
            MethodDoc notJson = ParseTest("{ not json");
            Check(!notJson.Usable && notJson.Error != null && notJson.Error.Contains("JSON"), "methods: a document that isn't JSON is left out (" + notJson.Error + ")");
            MethodDoc noFormat = ParseTest(@"{ ""methods"": [ { ""code"": ""x"", ""recipes"": [ { ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""game:gravel-granite"" } ] } ] } ] }");
            Check(noFormat.Usable && noFormat.Format == 1 && noFormat.Problems.Any(p => p.Path == "format"), "methods: no format reads as format 1 with a warning");
            MethodDoc odd = ParseTest(@"{ ""format"": 1, ""methods"": { ""code"": ""x"" } }");
            Check(!odd.Usable, "methods: \"methods\" that isn't a list leaves the document out");
            MethodDoc shapes = ParseTest(@"{ ""format"": 1, ""methods"": [ { ""code"": ""x"", ""recipes"": [ { ""inputs"": { ""code"": 5 }, ""outputs"": [ 7 ] } ] } ] }");
            Check(shapes.Usable && shapes.RecipeCount == 0 && shapes.Problems.Any(p => p.Error), "methods: wrong shapes deep inside leave that recipe out, not the server");
            Check(MethodSnapshot.IsDisabled(m, a, new[] { "vintagee" }) && MethodSnapshot.IsDisabled(m, a, new[] { "vintagee:press" }) && MethodSnapshot.IsDisabled(m, a, new[] { "vintagee:press/a" })
                && !MethodSnapshot.IsDisabled(m, a, new[] { "vintagee:press/b" }) && MethodSnapshot.IsDisabled(m, a, new[] { "vintagee:pr*" }), "methods: DisabledMethods takes a mod, a method, one recipe or a wildcard");
        }

        void CheckMethodReaders()
        {
            JObject source = JObject.Parse(@"{ ""Die"": ""plate"", ""input"": ""game:ingot-*"", ""InputQuantity"": 2, ""Output"": ""game:metalplate-{wildcard}"", ""Seconds"": 4,
                ""Steps"": [ { ""Item"": ""game:stick"", ""N"": 1 }, { ""Item"": ""game:rope"", ""N"": 2 } ] }");
            JObject template = JObject.Parse(@"{ ""inputs"": [ { ""code"": ""$Input"", ""quantity"": ""$InputQuantity"", ""name"": ""wildcard"" }, { ""code"": ""$Input2"" }, { ""each"": ""$Steps"", ""code"": ""$Item"", ""quantity"": ""$N"" } ],
                ""tools"": [ { ""code"": ""vintagee:die-${Die}"" } ], ""outputs"": [ { ""code"": ""$output"" } ], ""costs"": { ""seconds"": ""$Seconds"" } }");
            var filled = MethodParser.Eval(template, source) as JObject;
            Check(filled != null && (string)filled["inputs"]?[0]?["code"] == "game:ingot-*" && (int?)filled["inputs"]?[0]?["quantity"] == 2, "methods: a reader fills $Field from the source, ignoring case (" + filled?.ToString(Newtonsoft.Json.Formatting.None) + ")");
            Check(filled != null && (filled["inputs"] as JArray)?.Count == 3 && (string)filled["inputs"][1]["code"] == "game:stick" && (string)filled["inputs"][2]["code"] == "game:rope",
                "methods: a reader leaves out a stack whose code is missing, and \"each\" makes one per element");
            Check(filled != null && (string)filled["tools"]?[0]?["code"] == "vintagee:die-plate" && (double?)filled["costs"]?["seconds"] == 4, "methods: ${Field} inside a longer string");
            Check(MethodParser.Walk(source, "steps.1.item")?.ToString() == "game:rope", "methods: a dotted path walks into lists");
            // Read through the parser: the reader's recipe becomes a normal recipe (here with sapi, from attributes).
            MethodDoc d = MethodParser.Parse(@"{ ""format"": 1, ""methods"": [ { ""code"": ""reader"", ""readers"": [ { ""from"": ""attribute"", ""collectible"": ""block:game:nosuchblock-*"", ""attribute"": ""x"", ""recipe"": { ""inputs"": [], ""outputs"": [] } } ] } ] }",
                "vintagee", "self-test", "file", sapi);
            Check(d.Problems.Any(p => p.Error && p.Message.Contains("no block or item matches")), "methods: a reader whose blocks don't exist says so");
            MethodDoc assets = MethodParser.Parse(@"{ ""format"": 1, ""methods"": [ { ""code"": ""grid"", ""readers"": [ { ""from"": ""assets"", ""folder"": ""recipes/grid"", ""domain"": ""game"",
                ""recipe"": { ""code"": ""$code"", ""inputs"": [ { ""each"": ""$ingredients.P"", ""code"": ""$code"" } ], ""outputs"": [ { ""code"": ""$output.code"", ""type"": ""$output.type"" } ] } } ] } ] }",
                "vintagee", "self-test", "file", sapi);
            Check(assets.Usable && assets.Methods.Count == 1 && !assets.Problems.Any(p => p.Message.Contains("no recipe files")), "methods: an assets reader finds the game's own recipe files (" + assets.Methods.FirstOrDefault()?.ReadRecipes + " recipes made, " + assets.Problems.Count + " problems)");
        }

        void CheckMethodSolver()
        {
            // A shared-out recipe with two ingredients and a cost: what isn't valued already shares the rest.
            var s = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["a"] = 10, ["b"] = 20, ["y"] = 12 } };
            s.JointEdges.Add(new EmcSolver<string>.JointEdge { Choices = new[] { new[] { "a" }, new[] { "b" } }, Quantities = new[] { 1.0, 1.0 }, ExtraCost = 6, Outputs = new[] { "x", "y" }, OutputQuantities = new[] { 1.0, 1.0 } });
            s.Solve();
            Check(s.Values.TryGetValue("x", out double xv) && Math.Abs(xv - 24) < 1e-9, "methods: a shared-out recipe with several ingredients: 10 + 20 + 6 - 12 = 24 (" + xv + ")");
            var s2 = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["a"] = 10 } };
            s2.JointEdges.Add(new EmcSolver<string>.JointEdge { Choices = new[] { new[] { "a" } }, Quantities = new[] { 2.0 }, Outputs = new[] { "p", "q" }, OutputQuantities = new[] { 1.0, 0.5 } });
            s2.Solve();
            s2.Values.TryGetValue("p", out double pv);
            s2.Values.TryGetValue("q", out double qv);
            Check(Math.Abs(pv - 10) < 1e-9 && Math.Abs(qv - 20) < 1e-9, "methods: two unvalued outputs share the cost evenly (" + pv + ", " + qv + ")");
            // Worked backwards into its one unvalued ingredient, when every output has a value.
            var s3 = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["a"] = 10, ["x"] = 50, ["y"] = 20 } };
            s3.JointEdges.Add(new EmcSolver<string>.JointEdge { Choices = new[] { new[] { "a" }, new[] { "m" } }, Quantities = new[] { 1.0, 2.0 }, Outputs = new[] { "x", "y" }, OutputQuantities = new[] { 1.0, 1.0 } });
            s3.Solve();
            Check(s3.Values.TryGetValue("m", out double mv) && Math.Abs(mv - 30) < 1e-9, "methods: a shared-out recipe's one unknown ingredient is worked out backwards: (50 + 20 - 10) / 2 = 30 (" + mv + ")");
            // A tool's wear is never worked out backwards (without the flag, the hammer would be worth 990 000).
            var s4 = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["a"] = 10, ["out"] = 1000 } };
            s4.Edges.Add(new EmcSolver<string>.Edge { Output = "out", OutputQuantity = 1, Choices = new[] { new[] { "a" }, new[] { "hammer" } }, Quantities = new[] { 1.0, 0.001 }, NoInfer = new[] { false, true } });
            s4.Solve();
            var s5 = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["a"] = 10, ["out"] = 1000 } };
            s5.Edges.Add(new EmcSolver<string>.Edge { Output = "out", OutputQuantity = 1, Choices = new[] { new[] { "a" }, new[] { "hammer" } }, Quantities = new[] { 1.0, 0.001 } });
            s5.Solve();
            Check(!s4.Values.ContainsKey("hammer") && s5.Values.ContainsKey("hammer"), "methods: a tool's wear is never worked out backwards (with the flag: " + (s4.Values.ContainsKey("hammer") ? s4.Values["hammer"].ToString() : "none") + "; without: " + (s5.Values.TryGetValue("hammer", out double hv) ? hv.ToString() : "none") + ")");
            var s6 = new EmcSolver<string> { Fixed = new Dictionary<string, double> { ["out"] = 5 } };
            s6.Edges.Add(new EmcSolver<string>.Edge { Output = "out", OutputQuantity = 1, Choices = new[] { new[] { "hammer" } }, Quantities = new[] { 0.01 }, Reversible = true, NoInfer = new[] { true } });
            s6.Solve();
            Check(!s6.Values.ContainsKey("hammer"), "methods: nor through a one-ingredient process");
        }

        const string TestDocument = @"{
            ""format"": 1,
            ""energyUnits"": {
                ""testpower"": { ""name"": ""test power"", ""emcPerUnit"": 2 },
                ""testgen"": { ""name"": ""generator power"", ""emcPerUnit"": 99, ""generators"": [ { ""burns"": ""game:charcoal"", ""makes"": 16, ""note"": ""the test engine"" } ] }
            },
            ""methods"": [
                { ""code"": ""selftest-press"", ""name"": ""Self-test press"", ""machine"": ""block:vintagee:transmutationtable"", ""kind"": ""processing"",
                  ""supersedes"": [ ""NoSuchRecipeList"" ],
                  ""recipes"": [
                    { ""code"": ""dark"", ""inputs"": [ { ""code"": ""game:ingot-copper"" } ], ""outputs"": [ { ""code"": ""vintagee:matter-dark"" } ],
                      ""costs"": { ""energy"": { ""unit"": ""testpower"", ""amount"": 100 }, ""seconds"": 400 }, ""note"": ""made by the self-test"" },
                    { ""code"": ""red"", ""inputs"": [ { ""code"": ""game:ingot-copper"" } ], ""outputs"": [ { ""code"": ""vintagee:matter-red"", ""chance"": ""25%"" } ], ""costs"": { ""emc"": 22 } },
                    { ""code"": ""plates"", ""inputs"": [ { ""code"": ""game:ingot-*"", ""name"": ""metal"", ""allowedVariants"": [ ""copper"", ""tinbronze"" ] } ],
                      ""outputs"": [ { ""code"": ""game:metalplate-{metal}"" } ], ""costs"": { ""energy"": { ""unit"": ""testgen"", ""amount"": 10 } } },
                    { ""code"": ""stone"", ""inputs"": [ { ""code"": ""game:ingot-copper"" } ], ""tools"": [ { ""code"": ""game:hammer-copper"", ""wear"": 1 } ],
                      ""outputs"": [ { ""code"": ""vintagee:philosophersstone"" } ], ""costs"": { ""energy"": { ""unit"": ""testpower"", ""amount"": 100 }, ""seconds"": 10 } },
                    { ""code"": ""typo"", ""inputs"": [ { ""code"": ""game:ingot-coper"" } ], ""outputs"": [ { ""code"": ""vintagee:matter-dark"" } ] }
                  ] },
                { ""code"": ""selftest-off"", ""name"": ""Switched off"", ""recipes"": [ { ""inputs"": [ { ""code"": ""game:stone-granite"" } ], ""outputs"": [ { ""code"": ""vintagee:matter-red"" } ] } ] }
            ] }";

        void OnTestCollect(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (data is ITreeAttribute tree) tree.SetString("vintagee", TestDocument);
        }

        void CheckMethodsLive()
        {
            EmcMappingReport before = mod.LastReport;
            if (before == null) { Check(false, "methods: EMC not mapped"); return; }
            var values = before.Values.ToDictionary(kv => kv.Key, kv => kv.Value);
            string fingerprintBefore = mod.Methods.Fingerprint;
            Check(mod.Methods.Collected, "methods: crafting methods were collected at start-up (" + mod.Methods.Docs.Count + " documents)");
            var savedOff = mod.Config.DisabledMethods.ToList();
            var savedPrices = new Dictionary<string, double>(mod.Config.EnergyEmc);
            Item plateItem = sapi.World.GetItem(new AssetLocation("game:metalplate-copper"));
            double copperPlate = plateItem != null && before.Values.TryGetValue(plateItem, out double cp) ? cp : 0;
            EventBusListenerDelegate listener = OnTestCollect;
            sapi.Event.RegisterEventBusListener(listener, 0.5, MethodRegistry.CollectEvent);
            try
            {
                mod.Config.DisabledMethods.Add("vintagee:selftest-off");
                mod.CollectMethods("self-test");
                MethodDoc doc = mod.Methods.Docs.FirstOrDefault(dd => dd.Route == "code" && dd.ModId == "vintagee");
                Check(doc != null && doc.Usable && doc.Methods.Count == 2 && doc.RecipeCount == 6, "methods: a document sent through the game's event bus is read (" + (doc == null ? "none" : doc.Methods.Count + " methods, " + doc.RecipeCount + " recipes; " + string.Join("; ", doc.Problems)) + ")");
                Check(mod.Methods.Fingerprint != fingerprintBefore, "methods: the cache's fingerprint changes with the methods");
                EmcMappingReport report = mod.RecalculateEmc("self-test: crafting methods");
                Check(report != null, "methods: mapped with the test methods");
                if (report == null) return;
                CollectibleObject Find(string code) => (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                double V(string code) { CollectibleObject c = Find(code); return c != null && report.Values.TryGetValue(c, out double v) ? v : 0; }
                var convs = report.Conversions.Where(c => c.Method != null).ToList();
                // What one costs by the recipe, at the values as mapped (the value itself is the cheapest way, so at most this).
                double Each(string recipe)
                {
                    Conversion c = convs.FirstOrDefault(cv => cv.Method.Recipe.Code == recipe);
                    if (c == null || c.OutputQuantity <= 0) return -1;
                    double sum = c.ExtraCost;
                    foreach (ConversionInput i in c.Inputs) sum += i.Quantity * i.Choices.Where(ch => report.Values.ContainsKey(ch)).Select(ch => report.Values[ch]).DefaultIfEmpty(double.NaN).Min();
                    return sum / c.OutputQuantity;
                }
                Check(Math.Abs(Each("dark") - 328) < 0.01 && V("vintagee:matter-dark") <= 328.01, "methods: copper ingot 128 + 100 test power at 2 EMC = 328 a dark matter, and dark matter is worth at most that (" + Each("dark") + ", " + V("vintagee:matter-dark") + ")");
                Check(Math.Abs(Each("red") - 600) < 0.01 && V("vintagee:matter-red") <= 600.01, "methods: a 25% chance makes the output cost four runs: (128 + 22) / 0.25 = 600 (" + Each("red") + ", " + V("vintagee:matter-red") + ")");
                Check(Math.Abs(Each("plates") - 148) < 0.01 || Math.Abs(Each("plates") - (V("game:ingot-tinbronze") + 20)) < 0.01, "methods: {metal} fills in, and generator power is priced from what the engine burns: 128 + 10 × (32 / 16) = 148 (" + Each("plates") + ")");
                if (copperPlate > 148) Check(V("game:metalplate-copper") <= 148.01, "methods: the copper plate takes the method's cheaper price (" + V("game:metalplate-copper") + ", was " + copperPlate + ")");
                var plates = convs.Where(c => c.Method.Recipe.Code == "plates").ToList();
                Check(plates.Count == 2 && plates.All(c => c.Costs?.FirstOrDefault()?.PriceSource?.Contains("the test engine") == true), "methods: allowed variants make one copy per metal, priced by the generator (" + plates.Count + " copies, " + plates.FirstOrDefault()?.Costs?.FirstOrDefault()?.PriceSource + ")");
                Conversion stone = convs.FirstOrDefault(c => c.Method.Recipe.Code == "stone");
                ConversionInput wear = stone?.Inputs.FirstOrDefault(i => i.NoInfer);
                int dur = 0;
                try { CollectibleObject hammer = sapi.World.GetItem(new AssetLocation("game:hammer-copper")); dur = hammer?.GetMaxDurability(new ItemStack(hammer)) ?? 0; } catch { }
                Check(wear != null && dur > 0 && Math.Abs(wear.Quantity - 1.0 / dur) < 1e-9, "methods: a tool's wear costs 1 / its durability of the tool (" + wear?.Quantity + " of a hammer with " + dur + ")");
                var results = report.MethodResults;
                MethodRecipeResult typo = results.FirstOrDefault(rr => rr.Recipe.Code == "typo");
                Check(typo != null && typo.Skipped != null && typo.BadCode == "game:ingot-coper", "methods: a code that matches nothing leaves the recipe out and names the code (" + typo?.Skipped + ")");
                Check(results.Any(rr => rr.Recipe.Method.Code == "vintagee:selftest-off" && rr.Disabled) && !convs.Any(c => c.Method.Method.Code == "vintagee:selftest-off"), "methods: a switched-off method makes nothing");
                Check(!report.SupersedeHits.Contains("NoSuchRecipeList"), "methods: a supersede entry that matches nothing is noted");

                // The explanation and the doctor.
                Item dark = sapi.World.GetItem(new AssetLocation("vintagee:matter-dark"));
                string dump = EmcExplain.Dump(mod, sapi, new ItemStack(dark), null, false, "en").PlainText(sapi.World);
                Check(dump.Contains("Self-test press") && dump.Contains("of power at 2 EMC") && dump.Contains("made by the self-test") && !dump.Contains("vintagee:dumpgui"),
                    "methods: /emc dump explains the method, its power and price, and the recipe's note (" + Line1(dump, "power") + ")");
                File.WriteAllText(Path.Combine(VintageEModSystem.DataFolder(sapi), "dump-methods-matter-dark.txt"), dump);
                string Diag(string action, string arg)
                {
                    EmcReport r = EmcDiagnose.Run(mod, sapi, null, "en", action, arg, null);
                    string text = r.PlainText(sapi.World);
                    File.WriteAllText(Path.Combine(VintageEModSystem.DataFolder(sapi), "diagnose-methods-" + (action.Length == 0 ? "overview" : action) + (arg.Length > 0 ? "-" + arg.Replace(':', '_').Replace(' ', '_') : "") + ".txt"), text);
                    Check(r.Lines.Count >= 3 && !text.Contains("vintagee:diag-") && !text.Contains("vintagee:dumpgui-"), $"methods: diagnose {action} {arg}: {r.Lines.Count} lines, every lang key found");
                    return text;
                }
                string overview = Diag("", "");
                Check(overview.Contains("Other mods' crafting methods") && overview.Contains("Crafting methods: 1 mod(s)"), "methods: the check-up has the crafting-methods cards (" + Line1(overview, "Crafting methods:") + ")");
                Check(overview.Contains("Free power:") && overview.Contains("more than 1 EMC a second"), "methods: the check-up warns about the stone at 20 EMC a second from power (" + Line1(overview, "Free power") + ")");
                Check(overview.Contains("Recipes:") && overview.Contains("left out"), "methods: the check-up lists the recipe left out (" + Line1(overview, "Recipes:") + ")");
                string all = Diag("methods", "");
                Check(all.Contains("selftest-press") && all.Contains("dark") && all.Contains("Used: this is where the value comes from"), "methods: /emc methods lists the recipes and what became of them");
                string one = Diag("methods", "vintagee:selftest-press");
                Check(one.Contains("test power") && one.Contains("game:ingot-coper"), "methods: one method's report has its costs and the code that matches nothing (" + Line1(one, "coper") + ")");
                Check(Line1(one, "coper").Contains("game:ingot-copper"), "methods: the code that matches nothing gets a \"did you mean\" (" + Line1(one, "coper") + ")");
                string check = Diag("methodcheck", "vintagee");
                Check(check.Contains("A worked example") && check.Contains("saved to"), "methods: methodcheck gives a worked example and writes the export");
                Check(File.Exists(Path.Combine(VintageEModSystem.DataFolder(sapi), "methods-vintagee.json")), "methods: the export file is written");
                string item = Diag("item", "vintagee:matter-dark");
                Check(item.Contains("Made by Self-test press"), "methods: the item report says which method makes it (" + Line1(item, "Made by") + ")");
                string trace = Diag("trace", "vintagee:matter-dark");
                Check(trace.Contains("of power at 2 EMC"), "methods: the trace shows the power cost");
                Diag("mod", "vintagee");
                Diag("draftmethods", "game");
                Diag("changes", "");
                foreach (string sym in new[] { "methodnovalue", "powercost", "powerfarm", "chancevalue" }) Diag("symptom", sym);
                string chance = Diag("symptom", "chancevalue");
                Check(chance.Contains("25%") && chance.Contains("about 4 runs"), "methods: the chance symptom explains the 1 / chance rule (" + Line1(chance, "runs") + ")");

                // Fixes: a price, a cost kind, a method off.
                string outcome = EmcDiagnose.ApplyFix(mod, null, "energyprice", "vintagee:testpower=1", "en");
                Check(mod.Config.EnergyEmc.TryGetValue("vintagee:testpower", out double set) && set == 1 && outcome.Contains("testpower"), "methods: the price fix sets EnergyEmc (" + outcome + ")");
                outcome = EmcDiagnose.ApplyFix(mod, null, "method", "off:vintagee:selftest-press", "en");
                Check(mod.Config.DisabledMethods.Contains("vintagee:selftest-press"), "methods: the switch-off fix adds to DisabledMethods (" + outcome + ")");
                outcome = EmcDiagnose.ApplyFix(mod, null, "method", "on:vintagee:selftest-press", "en");
                Check(!mod.Config.DisabledMethods.Contains("vintagee:selftest-press"), "methods: the switch-on fix takes it out again (" + outcome + ")");
                report = mod.RecalculateEmc("self-test: crafting methods, price 1");
                convs = report?.Conversions.Where(c => c.Method != null).ToList() ?? new List<Conversion>();
                Check(report != null && Math.Abs(Each("dark") - 228) < 0.01 && V("vintagee:matter-dark") <= 228.01, "methods: with the price at 1, dark matter costs 128 + 100 = 228 (" + Each("dark") + ")");

                // A mod saying its methods changed is counted (the remap it asks for comes a second later).
                int asked = mod.MethodChangeRequests.TryGetValue("vintagee", out int n0) ? n0 : 0;
                sapi.Event.PushEvent(MethodRegistry.ChangedEvent, new StringAttribute("vintagee"));
                Check(mod.MethodChangeRequests.TryGetValue("vintagee", out int n1) && n1 == asked + 1, "methods: vintagee:methodschanged is heard");
            }
            finally
            {
                sapi.Event.UnregisterEventBusListener(listener);
                mod.Config.DisabledMethods.Clear();
                mod.Config.DisabledMethods.AddRange(savedOff);
                mod.Config.EnergyEmc.Clear();
                foreach (var kv in savedPrices) mod.Config.EnergyEmc[kv.Key] = kv.Value;
                mod.SaveConfig();
                mod.CollectMethods("self-test: done");
            }
            EmcMappingReport after = mod.RecalculateEmc("self-test: crafting methods removed");
            if (after == null) { Check(false, "methods: mapped again without the test methods"); return; }
            int changed = values.Count(kv => !after.Values.TryGetValue(kv.Key, out double v) || Math.Abs(v - kv.Value) > 1e-6) + after.Values.Keys.Count(k => !values.ContainsKey(k));
            Check(changed == 0 && mod.Methods.Fingerprint == fingerprintBefore, "methods: without the test methods every value is back as it was (" + changed + " differ)");
        }
    }
}
