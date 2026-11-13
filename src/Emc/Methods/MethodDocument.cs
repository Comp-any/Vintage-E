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
        /// <summary>"assets/vintagecreation/config/vintagee-methods/press.json", "code (vintagecreation)" or "ModConfig/vintagee-methods/press.json".</summary>
        public string Source;
        /// <summary>"file", "code" or "server" (the server's own, in ModConfig/vintagee-methods/).</summary>
        public string Route;
        public int Format;
        /// <summary>Mods that must be loaded; an entry "a|b" is met by either.</summary>
        public List<string> RequiresMods = new List<string>();
        /// <summary>The domain codes without one get ("domain" in the document; else the mod's).</summary>
        public string Domain;
        /// <summary>The document as sent (for /emc methods copy).</summary>
        public string Original;
        /// <summary>Whether <paramref name="mod"/> is one of the mods it requires (an "a|b" entry counts for both).</summary>
        public bool Requires(string mod) => mod != null && RequiresMods.Any(r => r.Split('|').Any(a => a.Trim() == mod));
        /// <summary>The server's own documents may replace other mods' methods and supersede any recipe list.</summary>
        public bool FromServer => Route == "server";
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
        /// <summary>Only made by burning fuel (no windmills, no water wheels): the free-power check leaves it alone.</summary>
        public bool OnlyFromFuel;
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
        /// <summary>Recipes the document itself switches off ("enabled": false), which aren't in <see cref="Recipes"/>.</summary>
        public int SwitchedOffInDocument;
        public string ModId;
        public string Path;
        public MethodDoc Doc;
        /// <summary>Each recipe as read, by code (after "like"), so a later one can copy it with "like".</summary>
        internal readonly Dictionary<string, JObject> Raw = new Dictionary<string, JObject>(StringComparer.Ordinal);

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
        /// <summary>For an ingredient or tool slot with alternatives ("anyOf"): the alternatives (then <see cref="Code"/> is null).</summary>
        public List<MethodStack> AnyOf;
        /// <summary>For an output shared out with others: its weight in the split (1 each by default).</summary>
        public double Share = 1;
        /// <summary>Whether the document gave <see cref="Share"/> (else outputs split evenly, as in 1.23.0).</summary>
        public bool ShareGiven;
        /// <summary>How many times its step is done ("repeat"): a tool may be used up that many times per run.</summary>
        public double Repeat = 1;
        /// <summary>For a byproduct: false = with no value yet it counts as nothing, instead of the recipe waiting for one.</summary>
        public bool WaitForValue = true;
        /// <summary>Numbers that depend on the variant, by field ("quantity", "chance", "wear", "breakChance", "weight", "share").</summary>
        public Dictionary<string, MethodTable> Tables;
        public string Path;

        /// <summary>A number of this stack for one variant: its table's (null when the table has no number for it), or <paramref name="fixedValue"/>.</summary>
        public double? Get(string field, double fixedValue, Dictionary<string, string> captures)
        {
            if (Tables == null || !Tables.TryGetValue(field, out MethodTable t)) return fixedValue;
            return t.TryGet(captures, out double v) ? v : (double?)null;
        }

        /// <summary>Every stack in this one (itself, its alternatives, what it hands back).</summary>
        public IEnumerable<MethodStack> All()
        {
            if (AnyOf != null) { foreach (MethodStack a in AnyOf) foreach (MethodStack x in a.All()) yield return x; yield break; }
            yield return this;
            if (Returns != null) yield return Returns;
        }
    }

    /// <summary>
    /// A number that depends on the variant: { "by": "metal", "values": { "iron": 600, "copper": 400 }, "default": 400 }
    /// is the number for what the input named "metal" matched. With no number for it and no default, that variant is left out.
    /// </summary>
    public sealed class MethodTable
    {
        public string By;
        public Dictionary<string, double> Values = new Dictionary<string, double>(StringComparer.Ordinal);
        public double? Default;
        /// <summary>A multiplier (a step's "repeat").</summary>
        public double Scale = 1;

        public bool TryGet(Dictionary<string, string> captures, out double value)
        {
            value = 0;
            if (captures != null && By != null && captures.TryGetValue(By, out string v) && Values.TryGetValue(v, out double x)) { value = x * Scale; return true; }
            if (Default.HasValue) { value = Default.Value * Scale; return true; }
            return false;
        }

        public MethodTable Scaled(double by) => new MethodTable { By = By, Values = Values, Default = Default, Scale = Scale * by };
    }

    public sealed class MethodCosts
    {
        public List<(string Unit, double Amount)> Energy = new List<(string, double)>();
        /// <summary>By energy entry: its table, or null for a plain number.</summary>
        public List<MethodTable> EnergyTables = new List<MethodTable>();
        /// <summary>Heat, one entry per step that heats (°C, seconds).</summary>
        public List<(double Temperature, double Seconds)> Heat = new List<(double, double)>();
        public List<(MethodTable Temperature, MethodTable Seconds)> HeatTables = new List<(MethodTable, MethodTable)>();
        public double Seconds;
        public MethodTable SecondsTable;
        public double Emc;
        public MethodTable EmcTable;
        public double HeatTemperature => Heat.Count > 0 ? Heat[0].Temperature : 0;
        public double HeatSeconds => Heat.Count > 0 ? Heat[0].Seconds : 0;
        public bool Any => Energy.Count > 0 || Heat.Count > 0 || Seconds > 0 || Emc > 0 || SecondsTable != null || EmcTable != null;

        /// <summary>Every table in the costs.</summary>
        public IEnumerable<MethodTable> Tables()
        {
            foreach (MethodTable t in EnergyTables) if (t != null) yield return t;
            foreach (var (a, b) in HeatTables) { if (a != null) yield return a; if (b != null) yield return b; }
            if (SecondsTable != null) yield return SecondsTable;
            if (EmcTable != null) yield return EmcTable;
        }
    }

    /// <summary>Reads method documents (format 1), checking every part and keeping a problem for each thing that's off.</summary>
    public static class MethodParser
    {
        /// <summary>The newest format this version reads.</summary>
        public const int Format = 1;

        static readonly Regex NameRule = new Regex("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
        static readonly HashSet<string> TopKeys = new HashSet<string> { "format", "requiresMod", "energyUnits", "methods", "name", "description", "domain", "$schema" };
        static readonly HashSet<string> UnitKeys = new HashSet<string> { "name", "emcPerUnit", "generators", "onlyFromFuel" };
        static readonly HashSet<string> GeneratorKeys = new HashSet<string> { "burns", "quantity", "makes", "note" };
        static readonly HashSet<string> MethodKeys = new HashSet<string> { "code", "name", "machine", "description", "kind", "supersedes", "recipes", "readers" };
        static readonly HashSet<string> RecipeKeys = new HashSet<string> { "code", "inputs", "tools", "outputs", "oneOf", "costs", "note", "enabled", "like", "steps" };
        static readonly HashSet<string> StepKeys = new HashSet<string> { "inputs", "tools", "costs", "repeat", "note" };
        static readonly HashSet<string> InputKeys = new HashSet<string> { "code", "type", "quantity", "litres", "name", "allowedVariants", "skipVariants", "returns", "chance", "anyOf" };
        static readonly HashSet<string> ToolKeys = new HashSet<string> { "code", "type", "quantity", "wear", "breakChance", "allowedVariants", "skipVariants", "name", "anyOf" };
        static readonly HashSet<string> OutputKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance", "main", "share", "waitForValue" };
        static readonly HashSet<string> OneOfKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance", "weight", "share", "waitForValue" };
        static readonly HashSet<string> ReturnKeys = new HashSet<string> { "code", "type", "quantity", "litres", "chance", "waitForValue" };
        static readonly HashSet<string> AnyOfKeys = new HashSet<string> { "anyOf" };
        static readonly HashSet<string> TableKeys = new HashSet<string> { "by", "values", "default" };
        static readonly HashSet<string> CostKeys = new HashSet<string> { "energy", "heat", "seconds", "emc" };
        static readonly HashSet<string> ReaderKeys = new HashSet<string> { "from", "collectible", "attribute", "folder", "domain", "list", "recipe" };

        /// <summary>
        /// Reads one document. <paramref name="sapi"/> is needed for readers (they look into the game's blocks, items and
        /// assets); without it they are skipped with a problem. Never throws.
        /// </summary>
        public static MethodDoc Parse(string json, string modId, string source, string route, ICoreServerAPI sapi, CollectibleIndex index = null)
        {
            var doc = new MethodDoc { ModId = (modId ?? "").Trim().ToLowerInvariant(), Source = source, Route = route, Original = json };
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
            // "a|b": either mod will do.
            for (int i = 0; i < doc.RequiresMods.Count; i++) doc.RequiresMods[i] = string.Join("|", doc.RequiresMods[i].Split('|').Select(a => a.Trim()).Where(a => a.Length > 0));
            doc.RequiresMods.RemoveAll(string.IsNullOrEmpty);
            // For "a|b", the one that's loaded (when known here) is the mod the document is for.
            string firstRequired = doc.RequiresMods.Select(r =>
            {
                string[] alts = r.Split('|');
                try { return alts.FirstOrDefault(a => sapi?.ModLoader.IsModEnabled(a) == true) ?? alts[0]; } catch { return alts[0]; }
            }).FirstOrDefault();
            string domainKey = Str(top["domain"])?.Trim().ToLowerInvariant();
            if (domainKey != null && !NameRule.IsMatch(domainKey)) { doc.Problems.Add(new MethodProblem { Path = "domain", Message = "\"domain\" must be a mod id (letters, digits, _ and -)" }); domainKey = null; }
            // Vintage-E's own files, and the server's, are for the mod they name (the domain, or the first mod they require).
            if (doc.ModId.Length == 0 || doc.ModId == VintageEModSystem.ModId || doc.FromServer) doc.ModId = domainKey ?? firstRequired ?? (doc.FromServer ? "server" : doc.ModId.Length == 0 ? "?" : doc.ModId);
            // Codes without a domain get the document's "domain", else the mod's (the server's own documents: the game's).
            doc.Domain = domainKey ?? (doc.FromServer ? "game" : doc.ModId);

            if (top["energyUnits"] is JObject units)
            {
                foreach (JProperty p in units.Properties())
                {
                    if (p.Name.StartsWith("//", StringComparison.Ordinal)) continue;
                    string path = "energyUnits." + p.Name;
                    if (!(p.Value is JObject uo)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an energy unit must be an object { \"name\": ..., \"emcPerUnit\": ... }", Error = true }); continue; }
                    Unknown(doc, uo, UnitKeys, path);
                    string code = WithDomain(p.Name.Trim().ToLowerInvariant(), doc.Domain);
                    var unit = new MethodEnergyUnit { Code = code, Name = Str(uo["name"]) ?? p.Name, ModId = doc.ModId, Path = path };
                    if (uo["onlyFromFuel"] != null)
                    {
                        bool? fuel = Bool(uo["onlyFromFuel"]);
                        if (fuel == null) doc.Problems.Add(new MethodProblem { Path = path + ".onlyFromFuel", Message = "\"onlyFromFuel\" is true or false" });
                        else unit.OnlyFromFuel = fuel.Value;
                    }
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
                            unit.Generators.Add(new MethodGenerator { Burns = WithDomainTyped(burns, doc.Domain), Quantity = qty, Makes = makes, Note = Str(go["note"]) });
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
                code = WithDomain(code.Trim().ToLowerInvariant(), doc.Domain);
                if (!codes.Add(code)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "the method " + code + " is in this document twice; the second is left out", Error = true }); continue; }
                var m = new MethodDef { Code = code, Name = Str(mo["name"]), Machine = Str(mo["machine"]), Description = Str(mo["description"]), ModId = doc.ModId, Path = path, Doc = doc };
                if (m.Machine != null) m.Machine = WithDomainTyped(m.Machine, doc.Domain);
                string kind = Str(mo["kind"]);
                if (kind == null || kind.Equals("crafting", StringComparison.OrdinalIgnoreCase)) m.Kind = ConversionKind.Crafting;
                else if (kind.Equals("processing", StringComparison.OrdinalIgnoreCase)) m.Kind = ConversionKind.Processing;
                else doc.Problems.Add(new MethodProblem { Path = path + ".kind", Message = "\"kind\" is \"crafting\" or \"processing\"; \"" + kind + "\" is read as crafting" });
                if (mo["supersedes"] is JArray sup) m.Supersedes.AddRange(sup.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(t => t.Length > 0));
                else if (mo["supersedes"]?.Type == JTokenType.String) m.Supersedes.Add(((string)mo["supersedes"]).Trim());
                else if (mo["supersedes"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".supersedes", Message = "\"supersedes\" must be a list of recipe class or list names" });

                if (mo["recipes"] is JArray recipes)
                {
                    for (int r = 0; r < recipes.Count; r++) ReadRecipe(doc, m, recipes[r], path + ".recipes[" + r + "]", null, null, "#" + (r + 1));
                }
                else if (mo["recipes"] != null) doc.Problems.Add(new MethodProblem { Path = path + ".recipes", Message = "\"recipes\" must be a list", Error = true });

                if (mo["readers"] is JArray readers)
                {
                    for (int k = 0; k < readers.Count; k++) ReadReader(doc, m, readers[k], path + ".readers[" + k + "]", sapi, index, generated, k);
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

        /// <summary>
        /// One recipe. <paramref name="readFrom"/> is set for recipes a reader made; <paramref name="domain"/> is the domain
        /// codes without one get (the data's own, for a reader; null = the document's); <paramref name="defaultCode"/>
        /// its name when it has no "code" (its place in the list, so a DisabledMethods entry keeps naming the same one).
        /// </summary>
        static void ReadRecipe(MethodDoc doc, MethodDef m, JToken token, string path, string readFrom, string domain, string defaultCode)
        {
            if (!(token is JObject ro)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a recipe must be an object", Error = true }); return; }
            Unknown(doc, ro, RecipeKeys, path);
            string code = (Str(ro["code"]) ?? "").Trim();
            if (code.Length == 0) code = defaultCode ?? "#" + (m.Recipes.Count + 1);
            // "like": a copy of an earlier recipe of the method, with the keys given here put in its place ("costs" key by key).
            if (ro["like"] != null)
            {
                string like = Str(ro["like"])?.Trim();
                if (string.IsNullOrEmpty(like) || !m.Raw.TryGetValue(like, out JObject baseRecipe))
                {
                    doc.Problems.Add(new MethodProblem { Path = path + ".like", Message = "\"like\": no recipe \"" + like + "\" comes before this one in the method; the recipe is left out", Error = true });
                    return;
                }
                var merged = (JObject)baseRecipe.DeepClone();
                merged.Remove("code");
                merged.Remove("enabled");
                foreach (JProperty p in ro.Properties())
                {
                    if (p.Name == "like") continue;
                    if (p.Name == "costs" && p.Value is JObject pc && merged["costs"] is JObject mc) foreach (JProperty q in pc.Properties()) mc[q.Name] = q.Value.DeepClone();
                    else merged[p.Name] = p.Value.DeepClone();
                }
                ro = merged;
            }
            // Two recipes with one code: the later gets "#n" after it, and "like" names it so.
            if (m.Raw.ContainsKey(code)) code += "#" + (m.Raw.Count + 1);
            m.Raw[code] = ro;
            // A recipe switched off can still be copied with "like" (a template for the ones after it).
            if (ro["enabled"] != null && Bool(ro["enabled"]) == false) { m.SwitchedOffInDocument++; return; }
            var recipe = new MethodRecipe { Method = m, Path = path, ReadFrom = readFrom, Note = Str(ro["note"]), Code = code };
            int before = doc.Problems.Count(p => p.Error);
            string dom = string.IsNullOrEmpty(domain) ? doc.Domain : domain;

            ReadStacks(doc, ro["inputs"], path + ".inputs", InputKeys, recipe.Inputs, "input", dom);
            ReadStacks(doc, ro["tools"], path + ".tools", ToolKeys, recipe.Tools, "tool", dom);
            ReadStacks(doc, ro["outputs"], path + ".outputs", OutputKeys, recipe.Outputs, "output", dom);
            ReadStacks(doc, ro["oneOf"], path + ".oneOf", OneOfKeys, recipe.OneOf, "oneof", dom);
            ReadCosts(doc, ro["costs"], path + ".costs", recipe.Costs, dom);
            ReadSteps(doc, ro["steps"], path + ".steps", recipe, dom);

            // Named *s: each slot's names (an anyOf slot's alternatives may name theirs alike), at most 8 slots, no two alike.
            var slotNames = recipe.Inputs.Concat(recipe.Tools)
                .Select(slot => slot.All().Where(st => st.Name != null).Select(st => st.Name).Distinct().ToList())
                .Where(l => l.Count > 0).ToList();
            if (slotNames.Count > MaxNames) doc.Problems.Add(new MethodProblem { Path = path + ".inputs", Message = "more than " + MaxNames + " named inputs; the recipe is left out", Error = true });
            foreach (string dup in slotNames.SelectMany(l => l).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key))
                doc.Problems.Add(new MethodProblem { Path = path + ".inputs", Message = "two inputs are named \"" + dup + "\"; the recipe is left out", Error = true });
            var named = new HashSet<string>(slotNames.SelectMany(l => l), StringComparer.Ordinal);

            if (recipe.Outputs.Count == 0 && recipe.OneOf.Count == 0 && doc.Problems.Count(p => p.Error) == before)
                doc.Problems.Add(new MethodProblem { Path = path, Message = "the recipe has no \"outputs\" (or \"oneOf\"); it is left out", Error = true });
            int mains = recipe.Outputs.Count(o => o.Main);
            if (mains > 1) doc.Problems.Add(new MethodProblem { Path = path + ".outputs", Message = "more than one output is marked \"main\"; the recipe is left out", Error = true });
            bool wornTool = recipe.Tools.SelectMany(t => t.All()).Any(t => t.Wear > 0 || t.BreakChance > 0 || (t.Tables != null && (t.Tables.ContainsKey("wear") || t.Tables.ContainsKey("breakChance"))));
            if (recipe.Inputs.Count == 0 && !recipe.Costs.Any && !wornTool && doc.Problems.Count(p => p.Error) == before)
                doc.Problems.Add(new MethodProblem { Path = path, Message = "the recipe has no inputs and no costs: it would make something from nothing; it is left out", Error = true });

            var every = recipe.Inputs.Concat(recipe.Tools).Concat(recipe.Outputs).Concat(recipe.OneOf).SelectMany(st => st.All()).ToList();
            // Every {name} used must be named by an input's *, and so must every table's "by".
            foreach (MethodStack st in every)
            {
                foreach (Match mt in Placeholder.Matches(st.Code ?? ""))
                    if (!named.Contains(mt.Groups[1].Value)) doc.Problems.Add(new MethodProblem { Path = st.Path + ".code", Message = "{" + mt.Groups[1].Value + "} has nothing to fill it: no input names its * \"" + mt.Groups[1].Value + "\"", Error = true });
                if (st.Tables != null)
                    foreach (var kv in st.Tables)
                        if (!named.Contains(kv.Value.By)) doc.Problems.Add(new MethodProblem { Path = st.Path + "." + kv.Key + ".by", Message = "the table's \"by\": \"" + kv.Value.By + "\" isn't the name of an input's *", Error = true });
            }
            foreach (MethodTable t in recipe.Costs.Tables())
                if (!named.Contains(t.By)) doc.Problems.Add(new MethodProblem { Path = path + ".costs", Message = "a cost's table has \"by\": \"" + t.By + "\", which isn't the name of an input's *", Error = true });
            if (doc.Problems.Count(p => p.Error) > before) return; // left out: a problem says why
            m.Recipes.Add(recipe);
            if (readFrom != null) m.ReadRecipes++;
        }

        /// <summary>
        /// "steps": a process done in turns (sequenced assembly), each with its own ingredients, tools and costs, "repeat"
        /// times: all of it counts as one recipe, with what each step uses and costs added up.
        /// </summary>
        static void ReadSteps(MethodDoc doc, JToken token, string path, MethodRecipe recipe, string dom)
        {
            if (token == null) return;
            if (!(token is JArray steps)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "\"steps\" must be a list of steps { \"inputs\", \"tools\", \"costs\", \"repeat\" }", Error = true }); return; }
            bool secondsTable = recipe.Costs.SecondsTable != null, emcTable = recipe.Costs.EmcTable != null;
            for (int i = 0; i < steps.Count; i++)
            {
                string sp = path + "[" + i + "]";
                if (!(steps[i] is JObject so)) { doc.Problems.Add(new MethodProblem { Path = sp, Message = "a step must be an object", Error = true }); continue; }
                Unknown(doc, so, StepKeys, sp);
                double repeat = 1;
                if (so["repeat"] != null && !ReadNum(doc, so["repeat"], sp + ".repeat", NumRule.Positive, "\"repeat\" must be more than 0", out repeat, out _, tables: false)) continue;
                var ins = new List<MethodStack>();
                var tools = new List<MethodStack>();
                ReadStacks(doc, so["inputs"], sp + ".inputs", InputKeys, ins, "input", dom);
                ReadStacks(doc, so["tools"], sp + ".tools", ToolKeys, tools, "tool", dom);
                foreach (MethodStack st in ins.SelectMany(x => x.All())) ScaleStack(st, repeat, "quantity");
                foreach (MethodStack st in tools.SelectMany(x => x.All())) { ScaleStack(st, repeat, "wear"); ScaleStack(st, repeat, "breakChance"); st.Repeat *= repeat; }
                recipe.Inputs.AddRange(ins);
                recipe.Tools.AddRange(tools);
                var c = new MethodCosts();
                ReadCosts(doc, so["costs"], sp + ".costs", c, dom);
                for (int k = 0; k < c.Energy.Count; k++)
                {
                    recipe.Costs.Energy.Add((c.Energy[k].Unit, c.Energy[k].Amount * repeat));
                    recipe.Costs.EnergyTables.Add(c.EnergyTables[k]?.Scaled(repeat));
                }
                for (int k = 0; k < c.Heat.Count; k++)
                {
                    recipe.Costs.Heat.Add((c.Heat[k].Temperature, c.Heat[k].Seconds * repeat));
                    recipe.Costs.HeatTables.Add((c.HeatTables[k].Temperature, c.HeatTables[k].Seconds?.Scaled(repeat)));
                }
                // Seconds and flat EMC add up; a table for them can't be added to another step's, so give the total in one place.
                if ((c.SecondsTable != null && (secondsTable || recipe.Costs.Seconds > 0)) || (secondsTable && c.Seconds > 0)
                    || (c.EmcTable != null && (emcTable || recipe.Costs.Emc > 0)) || (emcTable && c.Emc > 0))
                {
                    doc.Problems.Add(new MethodProblem { Path = sp + ".costs", Message = "a table for \"seconds\" or \"emc\" can't be added to another step's: give the whole recipe's in one place", Error = true });
                    continue;
                }
                recipe.Costs.Seconds += c.Seconds * repeat;
                recipe.Costs.Emc += c.Emc * repeat;
                if (c.SecondsTable != null) { recipe.Costs.SecondsTable = c.SecondsTable.Scaled(repeat); secondsTable = true; }
                if (c.EmcTable != null) { recipe.Costs.EmcTable = c.EmcTable.Scaled(repeat); emcTable = true; }
                string note = Str(so["note"]);
                if (note != null) recipe.Note = recipe.Note == null ? note : recipe.Note + "; " + note;
            }
        }

        static void ScaleStack(MethodStack s, double by, string field)
        {
            if (by == 1) return;
            switch (field)
            {
                case "quantity": s.Quantity *= by; break;
                case "wear": s.Wear *= by; break;
                case "breakChance": s.BreakChance *= by; break;
            }
            if (s.Tables != null && s.Tables.TryGetValue(field, out MethodTable t)) s.Tables[field] = t.Scaled(by);
        }

        internal static readonly Regex Placeholder = new Regex(@"\{([A-Za-z0-9_-]+)\}", RegexOptions.Compiled);

        /// <summary>Most named inputs one recipe may have (each multiplies its copies).</summary>
        const int MaxNames = 8;

        static void ReadStacks(MethodDoc doc, JToken token, string path, HashSet<string> keys, List<MethodStack> into, string role, string domain)
        {
            if (token == null) return;
            if (!(token is JArray arr)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "must be a list", Error = true }); return; }
            for (int i = 0; i < arr.Count; i++)
            {
                MethodStack s = ReadStack(doc, arr[i], path + "[" + i + "]", keys, role, domain);
                if (s != null) into.Add(s);
            }
        }

        enum NumRule { Positive, NonNegative, Chance, ChanceOrZero }

        /// <summary>
        /// A number: plain, the game's { "avg", "var" }, a formula ("=2 * 30"), a percent for a chance ("25%"), or a
        /// table by variant ({ "by": "metal", "values": { ... }, "default": n }; only where <paramref name="tables"/>).
        /// For a table, <paramref name="value"/> is its default or first number. False (with a problem) when it's wrong.
        /// </summary>
        static bool ReadNum(MethodDoc doc, JToken t, string path, NumRule rule, string message, out double value, out MethodTable table, bool tables = true)
        {
            table = null;
            value = 0;
            if (t is JObject o && o["by"] != null)
            {
                if (!tables) { doc.Problems.Add(new MethodProblem { Path = path, Message = "this number can't be a table by variant", Error = true }); return false; }
                Unknown(doc, o, TableKeys, path);
                string by = Str(o["by"])?.Trim();
                if (string.IsNullOrEmpty(by)) { doc.Problems.Add(new MethodProblem { Path = path + ".by", Message = "a table needs \"by\": the name of the input whose * picks the number", Error = true }); return false; }
                if (!(o["values"] is JObject values) || !values.Properties().Any(p => !p.Name.StartsWith("//", StringComparison.Ordinal)))
                {
                    doc.Problems.Add(new MethodProblem { Path = path + ".values", Message = "a table needs \"values\": { \"iron\": 600, \"copper\": 400, ... }", Error = true });
                    return false;
                }
                table = new MethodTable { By = by };
                foreach (JProperty p in values.Properties())
                {
                    if (p.Name.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!OneNum(doc, p.Value, path + ".values." + p.Name, rule, message, out double v)) return false;
                    table.Values[p.Name] = v;
                }
                if (o["default"] != null)
                {
                    if (!OneNum(doc, o["default"], path + ".default", rule, message, out double d)) return false;
                    table.Default = d;
                }
                value = table.Default ?? table.Values.Values.First();
                return true;
            }
            return OneNum(doc, t, path, rule, message, out value);
        }

        static bool OneNum(MethodDoc doc, JToken t, string path, NumRule rule, string message, out double value)
        {
            if (rule == NumRule.Chance || rule == NumRule.ChanceOrZero) return Chance(doc, t, path, out value, rule == NumRule.ChanceOrZero);
            if (!Number(t, out value, out string error)) { doc.Problems.Add(new MethodProblem { Path = path, Message = error ?? message, Error = true }); return false; }
            if (rule == NumRule.Positive ? value <= 0 : value < 0) { doc.Problems.Add(new MethodProblem { Path = path, Message = message, Error = true }); return false; }
            return true;
        }

        static void Table(MethodStack s, string field, MethodTable t)
        {
            if (t == null) return;
            s.Tables ??= new Dictionary<string, MethodTable>(StringComparer.Ordinal);
            s.Tables[field] = t;
        }

        static MethodStack ReadStack(MethodDoc doc, JToken token, string path, HashSet<string> keys, string role, string domain)
        {
            if (!(token is JObject so)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "must be an object { \"code\": ... }", Error = true }); return null; }
            // An ingredient or tool slot that takes one of several things, each with its own amount: "1 fat or 10 lard".
            if (so["anyOf"] != null && (role == "input" || role == "tool"))
            {
                Unknown(doc, so, AnyOfKeys, path);
                if (!(so["anyOf"] is JArray alts) || alts.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".anyOf", Message = "\"anyOf\" must be a list of one or more ingredients, each with its own code and amount", Error = true }); return null; }
                var slot = new MethodStack { Path = path, AnyOf = new List<MethodStack>() };
                var altKeys = new HashSet<string>(keys);
                altKeys.Remove("anyOf");
                for (int i = 0; i < alts.Count; i++)
                {
                    if (alts[i] is JObject ao && ao["anyOf"] != null) { doc.Problems.Add(new MethodProblem { Path = path + ".anyOf[" + i + "]", Message = "an anyOf can't hold another anyOf", Error = true }); return null; }
                    MethodStack alt = ReadStack(doc, alts[i], path + ".anyOf[" + i + "]", altKeys, role, domain);
                    if (alt == null) return null;
                    slot.AnyOf.Add(alt);
                }
                return slot;
            }
            Unknown(doc, so, keys, path);
            string code = Str(so["code"]);
            if (string.IsNullOrWhiteSpace(code)) { doc.Problems.Add(new MethodProblem { Path = path + ".code", Message = "needs a \"code\"", Error = true }); return null; }
            var s = new MethodStack { Path = path };
            code = code.Trim();
            if (code.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { s.Type = EnumItemClass.Item; code = code.Substring(5); }
            else if (code.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { s.Type = EnumItemClass.Block; code = code.Substring(6); }
            s.Code = WithDomain(code, domain ?? doc.Domain);
            string type = Str(so["type"]);
            if (type != null)
            {
                if (type.Equals("item", StringComparison.OrdinalIgnoreCase)) s.Type = EnumItemClass.Item;
                else if (type.Equals("block", StringComparison.OrdinalIgnoreCase)) s.Type = EnumItemClass.Block;
                else doc.Problems.Add(new MethodProblem { Path = path + ".type", Message = "\"type\" is \"item\" or \"block\"" });
            }
            MethodTable t;
            if (so["litres"] != null)
            {
                if (!ReadNum(doc, so["litres"], path + ".litres", NumRule.Positive, "\"litres\" must be more than 0", out double l, out t)) return null;
                s.Quantity = l;
                s.IsLitres = true;
                Table(s, "quantity", t);
                if (so["quantity"] != null) doc.Problems.Add(new MethodProblem { Path = path, Message = "both \"litres\" and \"quantity\": the litres count" });
            }
            else if (so["quantity"] != null)
            {
                if (!ReadNum(doc, so["quantity"], path + ".quantity", NumRule.Positive, "\"quantity\" must be more than 0 (a number, { \"avg\": n, \"var\": m }, a formula \"=...\" or a table by variant)", out double q, out t)) return null;
                s.Quantity = q;
                Table(s, "quantity", t);
            }
            if (so["chance"] != null) { if (!ReadNum(doc, so["chance"], path + ".chance", NumRule.Chance, null, out s.Chance, out t)) return null; Table(s, "chance", t); }
            if (so["breakChance"] != null) { if (!ReadNum(doc, so["breakChance"], path + ".breakChance", NumRule.ChanceOrZero, null, out s.BreakChance, out t)) return null; Table(s, "breakChance", t); }
            if (so["wear"] != null) { if (!ReadNum(doc, so["wear"], path + ".wear", NumRule.NonNegative, "\"wear\" must be 0 or more (durability per run)", out s.Wear, out t)) return null; Table(s, "wear", t); }
            if (so["weight"] != null) { if (!ReadNum(doc, so["weight"], path + ".weight", NumRule.Positive, "\"weight\" must be more than 0", out s.Weight, out t)) return null; Table(s, "weight", t); }
            if (so["share"] != null) { if (!ReadNum(doc, so["share"], path + ".share", NumRule.Positive, "\"share\" must be more than 0 (its weight when the cost is shared out)", out s.Share, out t)) return null; Table(s, "share", t); s.ShareGiven = true; }
            if (so["main"] != null) s.Main = Bool(so["main"]) == true;
            if (so["waitForValue"] != null)
            {
                bool? wait = Bool(so["waitForValue"]);
                if (wait == null) doc.Problems.Add(new MethodProblem { Path = path + ".waitForValue", Message = "\"waitForValue\" is true or false" });
                else s.WaitForValue = wait.Value;
            }
            string name = Str(so["name"]);
            if (name != null)
            {
                name = name.Trim();
                if (!NameRule.IsMatch(name)) { doc.Problems.Add(new MethodProblem { Path = path + ".name", Message = "\"name\" may only have letters, digits, _ and -", Error = true }); return null; }
                int stars = CountStars(s.Code);
                // A reader's template names every input the same way; one with an exact code has nothing to capture.
                if (stars > 1) { doc.Problems.Add(new MethodProblem { Path = path + ".name", Message = "a named input needs exactly one * in its code (in the path, not the domain)", Error = true }); return null; }
                if (stars == 1) s.Name = name;
            }
            s.AllowedVariants = Strings(so["allowedVariants"]);
            s.SkipVariants = Strings(so["skipVariants"]);
            if (so["returns"] != null)
            {
                s.Returns = ReadStack(doc, so["returns"], path + ".returns", ReturnKeys, "return", domain);
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
            if (t.Type == JTokenType.String && !((string)t).TrimStart().StartsWith("=", StringComparison.Ordinal))
            {
                string text = ((string)t).Trim();
                bool pct = text.EndsWith("%", StringComparison.Ordinal);
                if (!double.TryParse(pct ? text.Substring(0, text.Length - 1).Trim() : text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "\"" + text + "\" isn't a chance (write 0.25 or \"25%\")", Error = true }); return false; }
                if (pct) v /= 100;
            }
            else if (!Number(t, out v, out string error)) { doc.Problems.Add(new MethodProblem { Path = path, Message = error ?? "a chance is a number from 0 to 1, or a percent like \"25%\"", Error = true }); return false; }
            if (double.IsNaN(v) || v > 1 || v < 0 || (!allowZero && v <= 0))
            {
                doc.Problems.Add(new MethodProblem { Path = path, Message = v > 1 ? "a chance can't be over 100% (" + v.ToString("0.###", CultureInfo.InvariantCulture) + "; for 25% write 0.25 or \"25%\")" : "a chance must be above 0", Error = true });
                return false;
            }
            chance = v;
            return true;
        }

        static void ReadCosts(MethodDoc doc, JToken token, string path, MethodCosts costs, string dom)
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
                    if (!(one is JObject eo) || Str(eo["unit"]) == null || eo["amount"] == null)
                    {
                        doc.Problems.Add(new MethodProblem { Path = ep, Message = "energy is { \"unit\": code, \"amount\": 0 or more }", Error = true });
                        continue;
                    }
                    Unknown(doc, eo, new HashSet<string> { "unit", "amount" }, ep);
                    if (!ReadNum(doc, eo["amount"], ep + ".amount", NumRule.NonNegative, "energy \"amount\" must be 0 or more", out double amount, out MethodTable t)) continue;
                    if (amount > 0 || t != null)
                    {
                        costs.Energy.Add((WithDomain(Str(eo["unit"]).Trim().ToLowerInvariant(), doc.Domain), amount));
                        costs.EnergyTables.Add(t);
                    }
                }
            }
            if (co["heat"] != null)
            {
                IEnumerable<JToken> list = co["heat"] is JArray ha ? ha : new[] { co["heat"] };
                int k = 0;
                foreach (JToken one in list)
                {
                    string hp = path + ".heat" + (co["heat"] is JArray ? "[" + k + "]" : "");
                    k++;
                    if (!(one is JObject ho) || ho["temperature"] == null || ho["seconds"] == null)
                    {
                        doc.Problems.Add(new MethodProblem { Path = hp, Message = "heat is { \"temperature\": °C, \"seconds\": s }", Error = true });
                        continue;
                    }
                    Unknown(doc, ho, new HashSet<string> { "temperature", "seconds" }, hp);
                    if (!ReadNum(doc, ho["temperature"], hp + ".temperature", NumRule.NonNegative, "heat \"temperature\" must be 0 or more", out double temp, out MethodTable tt)) continue;
                    if (!ReadNum(doc, ho["seconds"], hp + ".seconds", NumRule.NonNegative, "heat \"seconds\" must be 0 or more", out double secs, out MethodTable st)) continue;
                    if ((temp > 0 || tt != null) && (secs > 0 || st != null))
                    {
                        costs.Heat.Add((temp, secs));
                        costs.HeatTables.Add((tt, st));
                    }
                }
            }
            if (co["seconds"] != null)
            {
                if (ReadNum(doc, co["seconds"], path + ".seconds", NumRule.NonNegative, "\"seconds\" must be 0 or more", out double v, out MethodTable t)) { costs.Seconds = v; costs.SecondsTable = t; }
            }
            if (co["emc"] != null)
            {
                if (ReadNum(doc, co["emc"], path + ".emc", NumRule.NonNegative, "\"emc\" must be 0 or more", out double v, out MethodTable t)) { costs.Emc = v; costs.EmcTable = t; }
            }
        }

        // ---------------------------------------------------------------- readers: recipes read from another mod's own data

        /// <summary>
        /// A reader turns recipe data a mod already keeps (a block attribute's list, a folder of recipe files) into recipes
        /// of this method, through a template: in its strings "$Field" is a field of the source recipe (dots go deeper,
        /// "$a.0.b"; any case) and "${Field}" puts a field's text into a longer string. An object with "each": "$List" in a
        /// list stands for one entry per element of that list.
        /// </summary>
        static void ReadReader(MethodDoc doc, MethodDef m, JToken token, string path, ICoreServerAPI sapi, CollectibleIndex index, List<JObject> generated, int readerIndex)
        {
            if (!(token is JObject rd)) { doc.Problems.Add(new MethodProblem { Path = path, Message = "a reader must be an object", Error = true }); return; }
            Unknown(doc, rd, ReaderKeys, path);
            string from = (Str(rd["from"]) ?? "").Trim().ToLowerInvariant();
            if (!(rd["recipe"] is JObject template)) { doc.Problems.Add(new MethodProblem { Path = path + ".recipe", Message = "a reader needs a \"recipe\" template", Error = true }); return; }
            if (sapi == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "readers can only be read on a running server", Error = true }); return; }
            var sources = new List<(JToken data, string where, string domain)>();
            if (from == "attribute")
            {
                string pattern = Str(rd["collectible"]), attribute = Str(rd["attribute"]);
                if (pattern == null || attribute == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an attribute reader needs \"collectible\" (a code or wildcard) and \"attribute\"", Error = true }); return; }
                index ??= new CollectibleIndex(sapi.World.Collectibles);
                string full = pattern.StartsWith("item:", StringComparison.OrdinalIgnoreCase) || pattern.StartsWith("block:", StringComparison.OrdinalIgnoreCase)
                    ? pattern.Substring(0, pattern.IndexOf(':') + 1) + WithDomain(pattern.Substring(pattern.IndexOf(':') + 1), doc.Domain)
                    : WithDomain(pattern, doc.Domain);
                var matches = index.Match(full).ToList();
                if (matches.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".collectible", Message = "no block or item matches " + full, Error = true, BadCode = full }); return; }
                foreach (CollectibleObject c in matches)
                {
                    JToken data = Walk(c.Attributes?.Token, attribute);
                    if (data == null) continue;
                    string where = (c.ItemClass == EnumItemClass.Block ? "block " : "item ") + c.Code + ", " + attribute;
                    if (data is JArray arr) for (int i = 0; i < arr.Count; i++) sources.Add((arr[i], where + "[" + i + "]", c.Code.Domain));
                    else sources.Add((data, where, c.Code.Domain));
                }
                if (sources.Count == 0) { doc.Problems.Add(new MethodProblem { Path = path + ".attribute", Message = "none of the " + matches.Count + " matching blocks/items has the attribute " + attribute, Error = true }); return; }
            }
            else if (from == "assets")
            {
                string folder = Str(rd["folder"]);
                if (folder == null) { doc.Problems.Add(new MethodProblem { Path = path, Message = "an assets reader needs \"folder\" (e.g. recipes/mymachine)", Error = true }); return; }
                string domain = (Str(rd["domain"]) ?? doc.Domain).Trim().ToLowerInvariant();
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
                    if (data is JArray arr) for (int i = 0; i < arr.Count; i++) sources.Add((arr[i], where + "[" + i + "]", asset.Location.Domain));
                    else sources.Add((data, where, asset.Location.Domain));
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
            foreach (var (data, where, sourceDomain) in sources)
            {
                JToken result = Eval(body, data);
                if (!(result is JObject recipe)) continue;
                if (codeTemplate != null && Eval(codeTemplate, data) is JValue codeValue) recipe["code"] = codeValue;
                string key = recipe.ToString(Formatting.None);
                if (!seen.Add(key)) continue; // the same list on every variant of a block
                generated.Add(recipe);
                made++;
                ReadRecipe(doc, m, recipe, path + " (" + where + ")", where, sourceDomain, "r" + (readerIndex + 1) + "#" + made);
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

        /// <summary>Where a reader's template is: the entry (or list element) it reads, with the one around it ("$^Field").</summary>
        sealed class Scope
        {
            public JToken Data;
            /// <summary>The key of this element when "each" went through an object ("$@key"), or null.</summary>
            public string Key;
            /// <summary>Its place when "each" went through a list ("$@index"), or -1.</summary>
            public int Index = -1;
            public Scope Parent;
        }

        /// <summary>Fills a reader's template from one source entry. Null where a field it needs is missing (that part is left out).</summary>
        internal static JToken Eval(JToken template, JToken source) => Eval(template, new Scope { Data = source });

        /// <summary>A field by its template name: "a.b" in this entry, "^a" in the one around it, "@key" / "@index" for where "each" is.</summary>
        static JToken Lookup(Scope scope, string field)
        {
            field = (field ?? "").Trim();
            while (field.StartsWith("^", StringComparison.Ordinal))
            {
                scope = scope.Parent;
                field = field.Substring(1);
                if (scope == null) return null;
            }
            if (field.Equals("@key", StringComparison.OrdinalIgnoreCase)) return scope.Key == null ? null : new JValue(scope.Key);
            if (field.Equals("@index", StringComparison.OrdinalIgnoreCase)) return scope.Index < 0 ? null : new JValue(scope.Index);
            return Walk(scope.Data, field);
        }

        /// <summary>A field's value for a formula: a number, text (a list of texts joined with commas, as a grid pattern), or null.</summary>
        static object FormulaValue(Scope scope, string field)
        {
            JToken t = Lookup(scope, field);
            switch (t)
            {
                case null: return null;
                case JValue v when v.Type == JTokenType.Integer || v.Type == JTokenType.Float: return (double)v;
                case JValue v when v.Type == JTokenType.Boolean: return (bool)v;
                case JValue v when v.Type == JTokenType.String: return (string)v;
                case JArray a when a.All(x => x.Type == JTokenType.String): return string.Join(",", a.Select(x => (string)x));
                case JObject o when Number(o, out double avg): return avg;
                default: return t.ToString(Formatting.None);
            }
        }

        static JToken Eval(JToken template, Scope scope)
        {
            switch (template)
            {
                case JValue v when v.Type == JTokenType.String:
                    {
                        string s = (string)v;
                        // A formula: worked out here, where its $fields are known.
                        if (s.StartsWith("=", StringComparison.Ordinal) && !s.StartsWith("=!", StringComparison.Ordinal))
                        {
                            if (MethodExpression.TryEvaluate(s.Substring(1), f => FormulaValue(scope, f), out double value, out bool missing, out string error)) return new JValue(value);
                            return missing ? null : new JValue("=!" + error);
                        }
                        if (s.StartsWith("$", StringComparison.Ordinal) && !s.StartsWith("${", StringComparison.Ordinal))
                        {
                            JToken found = Lookup(scope, s.Substring(1));
                            return found?.DeepClone();
                        }
                        if (!s.Contains("${")) return new JValue(s);
                        bool gone = false;
                        string filled = TemplateField.Replace(s, m =>
                        {
                            JToken f = Lookup(scope, m.Groups[1].Value);
                            if (f == null || f is JContainer) { gone = true; return ""; }
                            return f.Type == JTokenType.String ? (string)f : f.ToString(Formatting.None);
                        });
                        return gone ? null : new JValue(filled);
                    }
                case JObject o:
                    {
                        var result = new JObject();
                        foreach (JProperty p in o.Properties())
                        {
                            if (p.Name == "each") continue;
                            JToken val = Eval(p.Value, scope);
                            if (val == null)
                            {
                                if (p.Name == "code") return null; // a stack (or recipe) without its code is left out
                                continue;
                            }
                            // An ingredient slot whose alternatives all were left out is left out with them.
                            if (p.Name == "anyOf" && val is JArray none && none.Count == 0) return null;
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
                                JToken list = Lookup(scope, each.StartsWith("$", StringComparison.Ordinal) ? each.Substring(1) : each);
                                if (list is JArray items)
                                {
                                    for (int i = 0; i < items.Count; i++) { JToken one = Eval(eo, new Scope { Data = items[i], Index = i, Parent = scope }); if (one != null) result.Add(one); }
                                }
                                // An object (a map of keys to stacks, as grid recipes keep their ingredients): one per value, "$@key" its key.
                                else if (list is JObject map)
                                {
                                    foreach (JProperty item in map.Properties()) { JToken one = Eval(eo, new Scope { Data = item.Value, Key = item.Name, Parent = scope }); if (one != null) result.Add(one); }
                                }
                                continue;
                            }
                            JToken val = Eval(el, scope);
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

        /// <summary>Like <see cref="WithDomain"/>, keeping an "item:"/"block:" prefix in front.</summary>
        internal static string WithDomainTyped(string code, string domain)
        {
            code = (code ?? "").Trim();
            foreach (string prefix in new[] { "item:", "block:" })
                if (code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return code.Substring(0, prefix.Length) + WithDomain(code.Substring(prefix.Length), domain);
            return WithDomain(code, domain);
        }

        /// <summary>true/false, also written as a string ("true"); null for anything else.</summary>
        static bool? Bool(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.String && bool.TryParse(((string)t).Trim(), out bool b)) return b;
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
        internal static bool Number(JToken t, out double value) => Number(t, out value, out _);

        /// <summary>
        /// Like <see cref="Number(JToken, out double)"/>, also reading a formula ("=2 * 30", "=round(4.5)"); a formula with
        /// $fields only works in a reader's template, which works it out first. <paramref name="error"/> says what's wrong with a formula.
        /// </summary>
        internal static bool Number(JToken t, out double value, out string error)
        {
            value = 0;
            error = null;
            if (t == null) return false;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { value = (double)t; return !double.IsNaN(value) && !double.IsInfinity(value); }
            if (t.Type == JTokenType.String)
            {
                string text = ((string)t).Trim();
                // A reader's formula that didn't work: "=!" + why.
                if (text.StartsWith("=!", StringComparison.Ordinal)) { error = text.Substring(2); return false; }
                if (text.StartsWith("=", StringComparison.Ordinal))
                    return MethodExpression.TryEvaluate(text.Substring(1), null, out value, out _, out error);
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
            }
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
