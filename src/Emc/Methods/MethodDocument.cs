using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageE
{
    // Other mods' crafting methods: what one mod sends Vintage-E (a "method document", from a file or from its code), as
    // read. docs/crafting-methods.md describes the format for mod authors.

    /// <summary>Something wrong (or worth knowing) in a method document, with where in it.</summary>
    public sealed class MethodProblem
    {
        /// <summary>The JSON path inside the document ("methods[0].recipes[3].outputs[1].chance"), or "" for the whole document.</summary>
        public string Path = "";
        public string Message;
        /// <summary>True when something isn't used because of it (a recipe, a method, the whole document); false for a warning (an unknown key, read anyway).</summary>
        public bool Error;
        /// <summary>A code that matched nothing, for the doctor's "did you mean".</summary>
        public string BadCode;
        public override string ToString() => (Path.Length > 0 ? Path + ": " : "") + Message;
    }

    /// <summary>One method document: a file in config/vintagee-methods/, or what one mod's code sent.</summary>
    public sealed class MethodDoc
    {
        /// <summary>The mod it is for (the file's domain, the key a mod's code sent it under, or a Vintage-E file's first requiresMod).</summary>
        public string ModId;
        /// <summary>"assets/vintagecreation/config/vintagee-methods/press.json", or "code (vintagecreation)".</summary>
        public string Source;
        /// <summary>"file" or "code".</summary>
        public string Route;
        public int Format;
        public List<string> RequiresMods = new List<string>();
        /// <summary>The first required mod that isn't loaded (the document is then left out), or null.</summary>
        public string MissingMod;
        /// <summary>Why the whole document can't be used (not JSON, a newer format...), or null.</summary>
        public string Error;
        public Dictionary<string, MethodEnergyUnit> Units = new Dictionary<string, MethodEnergyUnit>(StringComparer.Ordinal);
        public List<MethodDef> Methods = new List<MethodDef>();
        public List<MethodProblem> Problems = new List<MethodProblem>();
        /// <summary>The document as read, with the recipes its readers found, for the cache's fingerprint and "what changed".</summary>
        public string Canonical = "";
        public bool Usable => Error == null && MissingMod == null;
        public int RecipeCount => Methods.Sum(m => m.Recipes.Count);
    }

    public sealed class MethodEnergyUnit
    {
        public string Code;
        public string Name;
        /// <summary>The mod's suggested price in EMC per unit, or null.</summary>
        public double? EmcPerUnit;
        public List<MethodGenerator> Generators = new List<MethodGenerator>();
        public string ModId;
        public string Path;
    }

    /// <summary>What one of the mod's engines burns to make its energy: <see cref="Quantity"/> of <see cref="Burns"/> make <see cref="Makes"/> units.</summary>
    public sealed class MethodGenerator
    {
        public string Burns;
        public double Quantity = 1;
        public double Makes;
        public string Note;
    }

    public sealed class MethodDef
    {
        public string Code;
        /// <summary>The name as sent: a lang key or plain text (see <see cref="DisplayName"/>).</summary>
        public string Name;
        public string Machine;
        public string Description;
        public ConversionKind Kind = ConversionKind.Crafting;
        public List<string> Supersedes = new List<string>();
        public List<MethodRecipe> Recipes = new List<MethodRecipe>();
        /// <summary>Recipes its readers made, of all its recipes.</summary>
        public int ReadRecipes;
        public string ModId;
        public string Path;
        public MethodDoc Doc;

        /// <summary>The name in words: a lang key is looked up (on the server, in English), plain text kept.</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return Code;
                try
                {
                    if (Name.Contains(':') && !Name.Contains(' ') && Vintagestory.API.Config.Lang.HasTranslation(Name, false, false)) return Vintagestory.API.Config.Lang.Get(Name);
                }
                catch { /* no lang yet */ }
                return Name;
            }
        }
    }

    public sealed class MethodRecipe
    {
        public string Code;
        public string Note;
        public List<MethodStack> Inputs = new List<MethodStack>();
        public List<MethodStack> Tools = new List<MethodStack>();
        public List<MethodStack> Outputs = new List<MethodStack>();
        public List<MethodStack> OneOf = new List<MethodStack>();
        public MethodCosts Costs = new MethodCosts();
        public MethodDef Method;
        public string Path;
        /// <summary>For a recipe a reader made: where it was read from ("block vintagecreation:mechpress-north, pressRecipes[2]").</summary>
        public string ReadFrom;
        /// <summary>Method code + "/" + recipe code: what DisabledMethods and the doctor's links name.</summary>
        public string FullCode => Method?.Code + "/" + Code;
    }

    /// <summary>An ingredient, a tool or an output of a recipe.</summary>
    public sealed class MethodStack
    {
        /// <summary>The code with its domain (wildcards and {name}s still in it).</summary>
        public string Code;
        /// <summary>Item or block; null = whichever the code finds (an item first).</summary>
        public EnumItemClass? Type;
        public double Quantity = 1;
        /// <summary>Set when the amount was given in litres (a liquid): then <see cref="Quantity"/> is litres.</summary>
        public bool IsLitres;
        /// <summary>Names what this ingredient's * matches, so other codes can use {name}.</summary>
        public string Name;
        public string[] AllowedVariants, SkipVariants;
        /// <summary>For an output: how often it comes (1 = always). For an ingredient: how often it is used up.</summary>
        public double Chance = 1;
        public bool Main;
        /// <summary>For a oneOf entry: its weight.</summary>
        public double Weight = 1;
        /// <summary>For a tool: durability it loses per run, and the chance it breaks.</summary>
        public double Wear, BreakChance;
        /// <summary>For an ingredient: what is handed back after the run (an empty bucket).</summary>
        public MethodStack Returns;
        public string Path;
    }

    public sealed class MethodCosts
    {
        public List<(string Unit, double Amount)> Energy = new List<(string, double)>();
        public double HeatTemperature, HeatSeconds;
        public double Seconds;
        public double Emc;
        public bool Any => Energy.Count > 0 || HeatSeconds > 0 || Seconds > 0 || Emc > 0;
    }

    /// <summary>Reads method documents (format 1), checking every part and keeping a problem for each thing that's off.</summary>
    public static class MethodParser
    {
        /// <summary>The newest format this version reads.</summary>
        public const int Format = 1;

        static readonly Regex NameRule = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);
        static readonly HashSet<string> TopKeys = new HashSet<string> { "format", "requiresMod", "energyUnits", "methods", "name", "description" };
        static readonly HashSet<string> UnitKeys = new HashSet<string> { "name", "emcPerUnit", "generators" };
        static readonly HashSet<string> GeneratorKeys = new HashSet<string> { "burns", "quantity", "makes", "note" };
        static readonly HashSet<string> MethodKeys = new HashSet<string> { "code", "name", "machine", "description", "kind", "supersedes", "recipes", "readers" };
        static readonly HashSet<string> RecipeKeys = new HashSet<string> { "code", "inputs", "tools", "outputs", "oneOf", "costs", "note", "enabled" };
        static readonly HashSet<string> InputKeys = new HashSet<string> { "code", "type", "quantity", "litres", "name", "allowedVariants", "skipVariants", "returns", "chance" };
        static readonly HashSet<string> ToolKeys = new HashSet<string> { "code", "type", "wear", "breakChance", "allowedVariants", "skipVariants" };
        static readonly HashSet<string> OutputKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance", "main" };
        static readonly HashSet<string> OneOfKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance", "weight" };
        static readonly HashSet<string> ReturnKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance" };
        static readonly HashSet<string> CostKeys = new HashSet<string> { "energy", "heat", "seconds", "emc" };
        static readonly HashSet<string> ReaderKeys = new HashSet<string> { "from", "collectible", "attribute", "folder", "domain", "list", "recipe" };

        /// <summary>
        /// Reads one document. <paramref name="sapi"/> is needed for readers (they look into the game's blocks, items and
        /// assets); without it they are skipped with a problem. Never throws.
        /// </summary>
        public static MethodDoc Parse(string json, string modId, string source, string route, ICoreServerAPI sapi, CollectibleIndex index = null)
        {
            var doc = new MethodDoc { ModId = (modId ?? "").Trim().ToLowerInvariant(), Source = source, Route = route };
            JToken root;
            try { root = JToken.Parse(json ?? ""); }
            catch (JsonReaderException e)
            {
                doc.Error = "not valid JSON: " + e.Message;
                doc.Problems.Add(new MethodProblem { Message = doc.Error, Error = true });
                return doc;
            }
            catch (Exception e)
            {
                doc.Error = "could not be read: " + e.Message;
                doc.Problems.Add(new MethodProblem { Message = doc.Error, Error = true });
                return doc;
            }
            try { Read(doc, root, sapi, index); }
            catch (Exception e)
            {
                // A shape nothing expected (an object where a list goes deep inside): the document is left out, the server goes on.
                doc.Error = "could not be read: " + e.GetType().Name + ": " + e.Message;
                doc.Problems.Add(new MethodProblem { Message = doc.Error, Error = true });
                doc.Methods.Clear();
            }
            return doc;
        }

        static void Read(MethodDoc doc, JToken root, ICoreServerAPI sapi, CollectibleIndex index)
        {
            if (!(root is JObject top))
            {
                doc.Error = "the document is not a JSON object ({ ... })";
                doc.Problems.Add(new MethodProblem { Message = doc.Error, Error = true });
                return;
            }
            Unknown(doc, top, TopKeys, "");
            JToken fmt = top["format"];
            if (fmt == null)
            {
                doc.Format = 1;
                doc.Problems.Add(new MethodProblem { Path = "format", Message = "no \"format\": read as format 1 (add \"format\": 1)" });
            }
            else if (fmt.Type != JTokenType.Integer || (int)fmt < 1)
            {
                doc.Error = "\"format\" must be a whole number, 1 for this version";
                doc.Problems.Add(new MethodProblem { Path = "format", Message = doc.Error, Error = true });
                return;
            }
            else
            {
                doc.Format = (int)fmt;
                if (doc.Format > Format)
                {
                    doc.Error = "format " + doc.Format + " is newer than this Vintage-E reads (" + Format + "): update Vintage-E";
                    doc.Problems.Add(new MethodProblem { Path = "format", Message = doc.Error, Error = true });
                    return;
                }
            }
            JToken req = top["requiresMod"];
            if (req != null)
            {
                if (req.Type == JTokenType.String) doc.RequiresMods.Add(((string)req).Trim().ToLowerInvariant());
                else if (req is JArray ra) doc.RequiresMods.AddRange(ra.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim().ToLowerInvariant()));
                else doc.Problems.Add(new MethodProblem { Path = "requiresMod", Message = "\"requiresMod\" must be a mod id or a list of them" });
                doc.RequiresMods.RemoveAll(string.IsNullOrEmpty);
            }
            if (doc.ModId.Length == 0 || doc.ModId == VintageEModSystem.ModId) doc.ModId = doc.RequiresMods.FirstOrDefault() ?? (doc.ModId.Length == 0 ? "?" : doc.ModId);

            if (top["energyUnits"] is JObject units)
            {
                foreach (JProperty p in units.Properties())
                {
                    if (p.Name.StartsWith("//", StringComparison.Ordinal)) continue;
                    string path = "energyUnits." + p.Name;
                    if (!(p.Value is JObject uo)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an energy unit must be an object { \"name\": ..., \"emcPerUnit\": ... }", Error = true }); continue; }
                    Unknown(doc, uo, UnitKeys, path);
                    string code = WithDomain(p.Name.Trim().ToLowerInvariant(), doc.ModId);
                    var unit = new MethodEnergyUnit { Code = code, Name = Str(uo["name"]) ?? p.Name, ModId = doc.ModId, Path = path };
                    JToken price = uo["emcPerUnit"];
                    if (price != null)
                    {
                        if (Number(price, out double v) && v >= 0) unit.EmcPerUnit = v;
                        else doc.Problems.Add(new MethodProblem { Path = path + ".emcPerUnit", Message = "\"emcPerUnit\" must be a number, 0 or more" });
                    }
                    if (uo["generators"] is JArray gens)
                    {
                        for (int g = 0; g < gens.Count; g++)
                        {
                            string gp = path + ".generators[" + g + "]";
                            if (!(gens[g] is JObject go)) { doc.Problems.Add(new MethodProblem { Path = gp, Message = "a generator must be an object { \"burns\": code, \"makes\": amount }" }); continue; }
                            Unknown(doc, go, GeneratorKeys, gp);
                            string burns = Str(go["burns"]);
                            double makes = 0, qty = 1;
                            if (go["quantity"] != null && (!Number(go["quantity"], out qty) || qty <= 0)) { doc.Problems.Add(new MethodProblem { Path = gp + ".quantity", Message = "\"quantity\" must be more than 0" }); continue; }
                            if (burns == null || !Number(go["makes"], out makes) || makes <= 0) { doc.Problems.Add(new MethodProblem { Path = gp, Message = "a generator needs \"burns\" (a code) and \"makes\" (more than 0)" }); continue; }
                            unit.Generators.Add(new MethodGenerator { Burns = WithDomain(burns, doc.ModId), Quantity = qty, Makes = makes, Note = Str(go["note"]) });
                        }
                    }
                    else if (uo["generators"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".generators", Message = "\"generators\" must be a list" });
                    if (doc.Units.ContainsKey(code)) doc.Problems.Add(new MethodProblem { Path = path, Message = "the unit " + code + " is declared twice; the first counts" });
                    else doc.Units[code] = unit;
                }
            }
            else if (top["energyUnits"] != null) doc.Problems.Add(new MethodProblem { Path = "energyUnits", Message = "\"energyUnits\" must be an object (unit code → unit)" });

            if (!(top["methods"] is JArray methods))
            {
                doc.Problems.Add(new MethodProblem { Path = "methods", Message = top["methods"] == null ? "no \"methods\": nothing to read" : "\"methods\" must be a list", Error = top["methods"] != null });
                if (top["methods"] != null) doc.Error = "\"methods\" must be a list";
                Canonicalise(doc, top, new List<JObject>());
                return;
            }
            var generated = new List<JObject>();
            var codes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < methods.Count; i++)
            {
                string path = "methods[" + i + "]";
                if (!(methods[i] is JObject mo)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a method must be an object", Error = true }); continue; }
                Unknown(doc, mo, MethodKeys, path);
                string code = Str(mo["code"]);
                if (string.IsNullOrWhiteSpace(code)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "a method needs a \"code\" (modid:name); it is left out", Error = true }); continue; }
                code = WithDomain(code.Trim().ToLowerInvariant(), doc.ModId);
                if (!codes.Add(code)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "the method " + code + " is in this document twice; the second is left out", Error = true }); continue; }
                var m = new MethodDef { Code = code, Name = Str(mo["name"]), Machine = Str(mo["machine"]), Description = Str(mo["description"]), ModId = doc.ModId, Path = path, Doc = doc };
                if (m.Machine != null) m.Machine = WithDomain(m.Machine, doc.ModId);
                string kind = Str(mo["kind"]);
                if (kind == null || kind.Equals("crafting", StringComparison.OrdinalIgnoreCase)) m.Kind = ConversionKind.Crafting;
                else if (kind.Equals("processing", StringComparison.OrdinalIgnoreCase)) m.Kind = ConversionKind.Processing;
                else doc.Problems.Add(new MethodProblem { Path = path + ".kind", Message = "\"kind\" is \"crafting\" or \"processing\"; \"" + kind + "\" is read as crafting" });
                if (mo["supersedes"] is JArray sup) m.Supersedes.AddRange(sup.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(t => t.Length > 0));
                else if (mo["supersedes"]?.Type == JTokenType.String) m.Supersedes.Add(((string)mo["supersedes"]).Trim());
                else if (mo["supersedes"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".supersedes", Message = "\"supersedes\" must be a list of recipe class or list names" });

                if (mo["recipes"] is JArray recipes)
                {
                    for (int r = 0; r < recipes.Count; r++) ReadRecipe(doc, m, recipes[r], path + ".recipes[" + r + "]", null);
                }
                else if (mo["recipes"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".recipes", Message = "\"recipes\" must be a list", Error = true });

                if (mo["readers"] is JArray readers)
                {
                    for (int k = 0; k < readers.Count; k++) ReadReader(doc, m, readers[k], path + ".readers[" + k + "]", sapi, index, generated);
                }
                else if (mo["readers"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".readers", Message = "\"readers\" must be a list", Error = true });
                if (mo["recipes"] == null && mo["readers"] == null) doc.Problems.Add(new MethodProblem { Path = path, Message = "the method " + code + " has no \"recipes\" and no \"readers\"" });
                doc.Methods.Add(m);
            }
            Canonicalise(doc, top, generated);
        }

        static void Canonicalise(MethodDoc doc, JObject top, List<JObject> generated)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(top.ToString(Formatting.None));
            foreach (JObject g in generated) sb.Append('\n').Append(g.ToString(Formatting.None));
            doc.Canonical = sb.ToString();
        }

        /// <summary>One recipe. <paramref name="readFrom"/> is set for recipes a reader made.</summary>
        static void ReadRecipe(MethodDoc doc, MethodDef m, JToken token, string path, string readFrom)
        {
            if (!(token is JObject ro)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a recipe must be an object", Error = true }); return; }
            Unknown(doc, ro, RecipeKeys, path);
            if (ro["enabled"] != null && ro["enabled"].Type == JTokenType.Boolean && !(bool)ro["enabled"]) return;
            var recipe = new MethodRecipe { Method = m, Path = path, ReadFrom = readFrom, Note = Str(ro["note"]) };
            recipe.Code = (Str(ro["code"]) ?? "").Trim();
            if (recipe.Code.Length == 0) recipe.Code = "#" + (m.Recipes.Count + 1);
            if (m.Recipes.Any(r => r.Code == recipe.Code)) recipe.Code += "#" + (m.Recipes.Count + 1);
            int before = doc.Problems.Count(p => p.Error);

            ReadStacks(doc, ro["inputs"], path + ".inputs", InputKeys, recipe.Inputs, "input");
            ReadStacks(doc, ro["tools"], path + ".tools", ToolKeys, recipe.Tools, "tool");
            ReadStacks(doc, ro["outputs"], path + ".outputs", OutputKeys, recipe.Outputs, "output");
            ReadStacks(doc, ro["oneOf"], path + ".oneOf", OneOfKeys, recipe.OneOf, "oneof");
            if (recipe.Outputs.Count == 0 && recipe.OneOf.Count == 0 && doc.Problems.Count(p => p.Error) == before)
                doc.Problems.Add(new MethodProblem { Path = path, Message = "the recipe has no \"outputs\" (or \"oneOf\"); it is left out", Error = true });
            int mains = recipe.Outputs.Count(o => o.Main);
            if (mains > 1) doc.Problems.Add(new MethodProblem { Path = path + ".outputs", Message = "more than one output is marked \"main\"; the recipe is left out", Error = true });
            ReadCosts(doc, ro["costs"], path + ".costs", recipe.Costs);
            if (recipe.Inputs.Count == 0 && !recipe.Costs.Any && doc.Problems.Count(p => p.Error) == before)
                doc.Problems.Add(new MethodProblem { Path = path, Message = "the recipe has no inputs and no costs: it would make something from nothing; it is left out", Error = true });

            // Every {name} used must be named by an input's *.
            var named = new HashSet<string>(recipe.Inputs.Where(i => i.Name != null).Select(i => i.Name), StringComparer.Ordinal);
            foreach (MethodStack s in recipe.Inputs.Concat(recipe.Tools).Concat(recipe.Outputs).Concat(recipe.OneOf).Concat(recipe.Inputs.Where(i => i.Returns != null).Select(i => i.Returns)))
            {
                foreach (Match mt in Placeholder.Matches(s.Code ?? ""))
                {
                    if (!named.Contains(mt.Groups[1].Value)) doc.Problems.Add(new MethodProblem { Path = s.Path + ".code", Message = "{" + mt.Groups[1].Value + "} has nothing to fill it: no input names its * \"" + mt.Groups[1].Value + "\"", Error = true });
                }
            }
            if (doc.Problems.Count(p => p.Error) > before) return; // left out: a problem says why
            m.Recipes.Add(recipe);
            if (readFrom != null) m.ReadRecipes++;
        }

        internal static readonly Regex Placeholder = new Regex(@"\{([a-z0-9_]+)\}", RegexOptions.Compiled);

        static void ReadStacks(MethodDoc doc, JToken token, string path, HashSet<string> keys, List<MethodStack> into, string role)
        {
            if (token == null) return;
            if (!(token is JArray arr)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "must be a list", Error = true }); return; }
            for (int i = 0; i < arr.Count; i++)
            {
                MethodStack s = ReadStack(doc, arr[i], path + "[" + i + "]", keys, role);
                if (s != null) into.Add(s);
            }
        }

        static MethodStack ReadStack(MethodDoc doc, JToken token, string path, HashSet<string> keys, string role)
        {
            if (!(token is JObject so)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "must be an object { \"code\": ... }", Error = true }); return null; }
            Unknown(doc, so, keys, path);
            string code = Str(so["code"]);
            if (string.IsNullOrWhiteSpace(code)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "needs a \"code\"", Error = true }); return null; }
            var s = new MethodStack { Path = path };
            code = code.Trim();
            if (code.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { s.Type = EnumItemClass.Item; code = code.Substring(5); }
            else if (code.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { s.Type = EnumItemClass.Block; code = code.Substring(6); }
            s.Code = WithDomain(code, doc.ModId);
            string type = Str(so["type"]);
            if (type != null)
            {
                if (type.Equals("item", StringComparison.OrdinalIgnoreCase)) s.Type = EnumItemClass.Item;
                else if (type.Equals("block", StringComparison.OrdinalIgnoreCase)) s.Type = EnumItemClass.Block;
                else doc.Problems.Add(new MethodProblem { Path = path + ".type", Message = "\"type\" is \"item\" or \"block\"" });
            }
            if (so["litres"] != null)
            {
                if (!Number(so["litres"], out double l) || l <= 0) { doc.Problems.Add(new MethodProblem { Path = path + ".litres", Message = "\"litres\" must be more than 0", Error = true }); return null; }
                s.Quantity = l;
                s.IsLitres = true;
                if (so["quantity"] != null) doc.Problems.Add(new MethodProblem { Path = path, Message = "both \"litres\" and \"quantity\": the litres count" });
            }
            else if (so["quantity"] != null)
            {
                if (!Number(so["quantity"], out double q) || q <= 0) { doc.Problems.Add(new MethodProblem { Path = path + ".quantity", Message = "\"quantity\" must be more than 0 (a number, or { \"avg\": n, \"var\": m })", Error = true }); return null; }
                s.Quantity = q;
            }
            if (so["chance"] != null && !Chance(doc, so["chance"], path + ".chance", out s.Chance)) return null;
            if (so["breakChance"] != null && !Chance(doc, so["breakChance"], path + ".breakChance", out s.BreakChance, allowZero: true)) return null;
            if (so["wear"] != null && (!Number(so["wear"], out s.Wear) || s.Wear < 0)) { doc.Problems.Add(new MethodProblem { Path = path + ".wear", Message = "\"wear\" must be 0 or more (durability per run)", Error = true }); return null; }
            if (so["weight"] != null && (!Number(so["weight"], out s.Weight) || s.Weight <= 0)) { doc.Problems.Add(new MethodProblem { Path = path + ".weight", Message = "\"weight\" must be more than 0", Error = true }); return null; }
            if (so["main"] != null) s.Main = so["main"].Type == JTokenType.Boolean && (bool)so["main"];
            string name = Str(so["name"]);
            if (name != null)
            {
                name = name.Trim().ToLowerInvariant();
                if (!NameRule.IsMatch(name)) { doc.Problems.Add(new MethodProblem { Path = path + ".name", Message = "\"name\" may only have letters, digits and _", Error = true }); return null; }
                if (CountStars(s.Code) != 1) { doc.Problems.Add(new MethodProblem { Path = path + ".name", Message = "a named input needs exactly one * in its code (in the path, not the domain)", Error = true }); return null; }
                s.Name = name;
            }
            s.AllowedVariants = Strings(so["allowedVariants"]);
            s.SkipVariants = Strings(so["skipVariants"]);
            if (so["returns"] != null)
            {
                s.Returns = ReadStack(doc, so["returns"], path + ".returns", ReturnKeys, "return");
                if (s.Returns == null) return null;
            }
            if (role == "output" || role == "oneof" || role == "return")
            {
                if (CountStars(s.Code) > 0 || s.Code.StartsWith("*:", StringComparison.Ordinal)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "an output can't be a wildcard (use {name} for what an input's * matched)", Error = true }); return null; }
            }
            return s;
        }

        static int CountStars(string code)
        {
            if (code == null) return 0;
            int colon = code.IndexOf(':');
            string path = colon >= 0 ? code.Substring(colon + 1) : code;
            return path.Count(ch => ch == '*');
        }

        static bool Chance(MethodDoc doc, JToken t, string path, out double chance, bool allowZero = false)
        {
            chance = 1;
            double v;
            if (t.Type == JTokenType.String)
            {
                string text = ((string)t).Trim();
                bool pct = text.EndsWith("%", StringComparison.Ordinal);
                if (!double.TryParse(pct ? text.Substring(0, text.Length - 1).Trim() : text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "\"" + text + "\" isn't a chance (write 0.25 or \"25%\")", Error = true }); return false; }
                if (pct) v /= 100;
            }
            else if (!Number(t, out v)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a chance is a number from 0 to 1, or a percent like \"25%\"", Error = true }); return false; }
            if (double.IsNaN(v) || v > 1 || v < 0 || (!allowZero && v <= 0))
            {
                doc.Problems.Add(new MethodProblem { Path = path, Message = v > 1 ? "a chance can't be over 100% (" + v.ToString("0.###", CultureInfo.InvariantCulture) + "; for 25% write 0.25 or \"25%\")" : "a chance must be above 0", Error = true });
                return false;
            }
            chance = v;
            return true;
        }

        static void ReadCosts(MethodDoc doc, JToken token, string path, MethodCosts costs)
        {
            if (token == null) return;
            if (!(token is JObject co)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "\"costs\" must be an object", Error = true }); return; }
            Unknown(doc, co, CostKeys, path);
            JToken e = co["energy"];
            if (e != null)
            {
                IEnumerable<JToken> list = e is JArray ea ? ea : new[] { e };
                int k = 0;
                foreach (JToken one in list)
                {
                    string ep = path + ".energy" + (e is JArray ? "[" + k + "]" : "");
                    k++;
                    if (!(one is JObject eo) || Str(eo["unit"]) == null || !Number(eo["amount"], out double amount) || amount < 0)
                    {
                        doc.Problems.Add(new MethodProblem { Path = ep, Message = "energy is { \"unit\": code, \"amount\": 0 or more }", Error = true });
                        continue;
                    }
                    Unknown(doc, eo, new HashSet<string> { "unit", "amount" }, ep);
                    if (amount > 0) costs.Energy.Add((WithDomain(Str(eo["unit"]).Trim().ToLowerInvariant(), doc.ModId), amount));
                }
            }
            if (co["heat"] != null)
            {
                if (!(co["heat"] is JObject ho) || !Number(ho["temperature"], out double temp) || !Number(ho["seconds"], out double secs) || temp < 0 || secs < 0)
                    doc.Problems.Add(new MethodProblem { Path = path + ".heat", Message = "heat is { \"temperature\": °C, \"seconds\": s }", Error = true });
                else { Unknown(doc, ho, new HashSet<string> { "temperature", "seconds" }, path + ".heat"); costs.HeatTemperature = temp; costs.HeatSeconds = temp > 0 ? secs : 0; }
            }
            if (co["seconds"] != null && (!Number(co["seconds"], out costs.Seconds) || costs.Seconds < 0)) { costs.Seconds = 0; doc.Problems.Add(new MethodProblem { Path = path + ".seconds", Message = "\"seconds\" must be 0 or more", Error = true }); }
            if (co["emc"] != null && (!Number(co["emc"], out costs.Emc) || costs.Emc < 0)) { costs.Emc = 0; doc.Problems.Add(new MethodProblem { Path = path + ".emc", Message = "\"emc\" must be 0 or more", Error = true }); }
        }

        // ---------------------------------------------------------------- readers: recipes read from another mod's own data

        /// <summary>
        /// A reader turns recipe data a mod already keeps (a block attribute's list, a folder of recipe files) into recipes
        /// of this method, through a template: in its strings "$Field" is a field of the source recipe (dots go deeper,
        /// "$a.0.b"; any case) and "${Field}" puts a field's text into a longer string. An object with "each": "$List" in a
        /// list stands for one entry per element of that list.
        /// </summary>
        static void ReadReader(MethodDoc doc, MethodDef m, JToken token, string path, ICoreServerAPI sapi, CollectibleIndex index, List<JObject> generated)
        {
            if (!(token is JObject rd)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a reader must be an object", Error = true }); return; }
            Unknown(doc, rd, ReaderKeys, path);
            string from = (Str(rd["from"]) ?? "").Trim().ToLowerInvariant();
            if (!(rd["recipe"] is JObject template)) { doc.Problems.Add(new MethodProblem { Path = path + ".recipe", Message = "a reader needs a \"recipe\" template", Error = true }); return; }
            if (sapi == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "readers can only be read on a running server", Error = true }); return; }
            var sources = new List<(JToken data, string where)>();
            if (from == "attribute")
            {
                string pattern = Str(rd["collectible"]), attribute = Str(rd["attribute"]);
                if (pattern == null || attribute == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an attribute reader needs \"collectible\" (a code or wildcard) and \"attribute\"", Error = true }); return; }
                index ??= new CollectibleIndex(sapi.World.Collectibles);
                string full = pattern.StartsWith("item:", StringComparison.OrdinalIgnoreCase) || pattern.StartsWith("block:", StringComparison.OrdinalIgnoreCase)
                    ? pattern.Substring(0, pattern.IndexOf(':') + 1) + WithDomain(pattern.Substring(pattern.IndexOf(':') + 1), doc.ModId)
                    : WithDomain(pattern, doc.ModId);
                var matches = index.Match(full).ToList();
                if (matches.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".collectible", Message = "no block or item matches " + full, Error = true, BadCode = full }); return; }
                foreach (CollectibleObject c in matches)
                {
                    JToken data = Walk(c.Attributes?.Token, attribute);
                    if (data == null) continue;
                    string where = (c.ItemClass == EnumItemClass.Block ? "block " : "item ") + c.Code + ", " + attribute;
                    if (data is JArray arr) for (int i = 0; i < arr.Count; i++) sources.Add((arr[i], where + "[" + i + "]"));
                    else sources.Add((data, where));
                }
                if (sources.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".attribute", Message = "none of the " + matches.Count + " matching blocks/items has the attribute " + attribute, Error = true }); return; }
            }
            else if (from == "assets")
            {
                string folder = Str(rd["folder"]);
                if (folder == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an assets reader needs \"folder\" (e.g. recipes/mymachine)", Error = true }); return; }
                string domain = (Str(rd["domain"]) ?? doc.ModId).Trim().ToLowerInvariant();
                string list = Str(rd["list"]);
                List<IAsset> assets;
                try { assets = sapi.Assets.GetMany(folder.Trim().TrimEnd('/') + "/", domain, true); }
                catch (Exception e) { doc.Problems.Add(new MethodProblem { Path = path + ".folder", Message = "could not list " + domain + ":" + folder + ": " + e.Message, Error = true }); return; }
                foreach (IAsset asset in assets.OrderBy(a => a.Location.ToString(), StringComparer.Ordinal))
                {
                    if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    JToken file;
                    try { file = JToken.Parse(asset.ToText()); }
                    catch (Exception e) { doc.Problems.Add(new MethodProblem { Path = path, Message = asset.Location + " isn't valid JSON: " + e.Message }); continue; }
                    JToken data = list == null ? file : Walk(file, list);
                    if (data == null) continue;
                    string where = asset.Location.ToString();
                    if (data is JArray arr) for (int i = 0; i < arr.Count; i++) sources.Add((arr[i], where + "[" + i + "]"));
                    else sources.Add((data, where));
                }
                if (sources.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".folder", Message = "no recipe files in " + domain + ":" + folder, Error = true }); return; }
            }
            else { doc.Problems.Add(new MethodProblem { Path = path + ".from", Message = "\"from\" is \"attribute\" or \"assets\"", Error = true }); return; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int made = 0;
            // The recipe's own code is optional: a field missing from it leaves the recipe without a code, not out.
            var body = (JObject)template.DeepClone();
            JToken codeTemplate = body["code"];
            body.Remove("code");
            foreach (var (data, where) in sources)
            {
                JToken result = Eval(body, data);
                if (!(result is JObject recipe)) continue;
                if (codeTemplate != null && Eval(codeTemplate, data) is JValue codeValue) recipe["code"] = codeValue;
                string key = recipe.ToString(Formatting.None);
                if (!seen.Add(key)) continue; // the same list on every variant of a block
                generated.Add(recipe);
                ReadRecipe(doc, m, recipe, path + " (" + where + ")", where);
                made++;
            }
            if (made == 0) doc.Problems.Add(new MethodProblem { Path = path, Message = "the reader found " + sources.Count + " source entries but made no recipe from them (check the template's $fields)", Error = true });
        }

        /// <summary>A field inside a token by a dotted path ("a.b.0.c"), names compared ignoring case; null when it isn't there.</summary>
        internal static JToken Walk(JToken token, string path)
        {
            if (token == null) return null;
            path = (path ?? "").Trim();
            if (path.Length == 0 || path == ".") return token;
            foreach (string part in path.Split('.'))
            {
                if (part.Length == 0) continue;
                if (token is JObject o)
                {
                    JProperty p = o.Properties().FirstOrDefault(pr => string.Equals(pr.Name, part, StringComparison.OrdinalIgnoreCase));
                    if (p == null) return null;
                    token = p.Value;
                }
                else if (token is JArray a && int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                {
                    if (i < 0 || i >= a.Count) return null;
                    token = a[i];
                }
                else return null;
                if (token == null || token.Type == JTokenType.Null) return null;
            }
            return token;
        }

        static readonly Regex TemplateField = new Regex(@"\$\{([^{}]*)\}", RegexOptions.Compiled);

        /// <summary>Fills a reader's template from one source entry. Null where a field it needs is missing (that part is left out).</summary>
        internal static JToken Eval(JToken template, JToken source)
        {
            switch (template)
            {
                case JValue v when v.Type == JTokenType.String:
                    {
                        string s = (string)v;
                        if (s.StartsWith("$", StringComparison.Ordinal) && !s.StartsWith("${", StringComparison.Ordinal))
                        {
                            JToken found = Walk(source, s.Substring(1));
                            return found?.DeepClone();
                        }
                        if (!s.Contains("${")) return new JValue(s);
                        bool missing = false;
                        string filled = TemplateField.Replace(s, m =>
                        {
                            JToken f = Walk(source, m.Groups[1].Value);
                            if (f == null || f is JContainer) { missing = true; return ""; }
                            return f.Type == JTokenType.String ? (string)f : f.ToString(Formatting.None);
                        });
                        return missing ? null : new JValue(filled);
                    }
                case JObject o:
                    {
                        var result = new JObject();
                        foreach (JProperty p in o.Properties())
                        {
                            if (p.Name == "each") continue;
                            JToken val = Eval(p.Value, source);
                            if (val == null)
                            {
                                if (p.Name == "code") return null; // a stack (or recipe) without its code is left out
                                continue;
                            }
                            result[p.Name] = val;
                        }
                        return result;
                    }
                case JArray a:
                    {
                        var result = new JArray();
                        foreach (JToken el in a)
                        {
                            if (el is JObject eo && eo["each"]?.Type == JTokenType.String)
                            {
                                string each = (string)eo["each"];
                                JToken list = Walk(source, each.StartsWith("$", StringComparison.Ordinal) ? each.Substring(1) : each);
                                if (list is JArray items) foreach (JToken item in items) { JToken one = Eval(eo, item); if (one != null) result.Add(one); }
                                continue;
                            }
                            JToken val = Eval(el, source);
                            if (val != null) result.Add(val);
                        }
                        return result;
                    }
                default:
                    return template?.DeepClone();
            }
        }

        // ---------------------------------------------------------------- small helpers

        /// <summary>The first block/item a code or wildcard names (for showing a machine), or null.</summary>
        public static CollectibleObject FindOne(IWorldAccessor world, string pattern)
        {
            if (world == null || string.IsNullOrWhiteSpace(pattern)) return null;
            string p = pattern.Trim();
            EnumItemClass? type = null;
            if (p.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { type = EnumItemClass.Item; p = p.Substring(5); }
            else if (p.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { type = EnumItemClass.Block; p = p.Substring(6); }
            if (p.IndexOfAny(new[] { '*', '?', '@', '{' }) < 0)
            {
                var loc = new AssetLocation(p);
                CollectibleObject item = type == EnumItemClass.Block ? null : world.GetItem(loc);
                Block block = type == EnumItemClass.Item ? null : world.GetBlock(loc);
                return item ?? (block != null && block.Id != 0 ? block : null);
            }
            foreach (CollectibleObject c in world.Collectibles)
            {
                if (c?.Code != null && CollectibleIndex.Matches(pattern, c)) return c;
            }
            return null;
        }

        /// <summary>A code with a domain: one without gets <paramref name="domain"/>, as in the game's own asset files.</summary>
        internal static string WithDomain(string code, string domain)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0 || code.Contains(':')) return code;
            return (string.IsNullOrEmpty(domain) || domain == "?" ? "game" : domain) + ":" + code;
        }

        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.ToString(Formatting.None) : null;

        static string[] Strings(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.String) return new[] { (string)t };
            if (t is JArray a) return a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).ToArray();
            return null;
        }

        /// <summary>A number, or the game's { "avg": n, "var": m } (its average). Strings holding a number count too (a reader's field).</summary>
        internal static bool Number(JToken t, out double value)
        {
            value = 0;
            if (t == null) return false;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { value = (double)t; return !double.IsNaN(value) && !double.IsInfinity(value); }
            if (t.Type == JTokenType.String) return double.TryParse(((string)t).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
            if (t is JObject o)
            {
                JToken avg = o.Properties().FirstOrDefault(p => string.Equals(p.Name, "avg", StringComparison.OrdinalIgnoreCase))?.Value;
                return avg != null && Number(avg, out value);
            }
            return false;
        }

        static void Unknown(MethodDoc doc, JObject o, HashSet<string> known, string path)
        {
            foreach (JProperty p in o.Properties())
            {
                if (p.Name.StartsWith("//", StringComparison.Ordinal) || known.Contains(p.Name)) continue;
                string near = known.OrderBy(k => Distance(k.ToLowerInvariant(), p.Name.ToLowerInvariant())).First();
                bool close = Distance(near.ToLowerInvariant(), p.Name.ToLowerInvariant()) <= Math.Max(2, p.Name.Length / 3);
                doc.Problems.Add(new MethodProblem { Path = (path.Length > 0 ? path + "." : "") + p.Name, Message = "unknown key \"" + p.Name + "\", ignored" + (close ? " (did you mean \"" + near + "\"?)" : "") });
            }
        }

        internal static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
