using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    public class EmcValueRule
    {
        public string Pattern;
        public double Value;
        public string Source;
        /// <summary>From "litreValues": Value is per litre of liquid and gets divided by the liquid's itemsPerLitre.</summary>
        public bool PerLitre;
    }

    /// <summary>"game:metalbit-*" is worth 0.05 × "game:ingot-*" (the '*' part is carried over).</summary>
    public class RelativeValueRule
    {
        public string Code;
        public string Of;
        public double Ratio;
        public string Source;
    }

    public class SwapRule
    {
        public string From;
        public string To;
    }

    public class WorldTransmutationRules
    {
        /// <summary>Variant name → ordered list of values to cycle through (e.g. "rock" → granite, andesite, ...).</summary>
        public Dictionary<string, List<string>> VariantCycles = new Dictionary<string, List<string>>();
        /// <summary>Direct block swaps, tried before variant cycles. A single '*' in From is carried over into To.</summary>
        public List<SwapRule> Swaps = new List<SwapRule>();
        /// <summary>Blocks the philosopher's stone never touches.</summary>
        public List<string> Blacklist = new List<string>();
    }

    /// <summary>
    /// Everything data-driven that feeds the mapper, merged from every loaded mod's
    /// <c>assets/&lt;domain&gt;/config/vintagee-emc*.json</c>. This mod's file loads first and other
    /// mods load after it, so a mod can add or override values just by shipping that file
    /// (or JSON-patching ours). Within the merged list, later rules win.
    /// </summary>
    public class EmcRuleSet
    {
        public const string AssetPath = "config/vintagee-emc";

        public List<EmcValueRule> Values = new List<EmcValueRule>();
        /// <summary>"defaultValues": only used for items that nothing else (recipes, traders, variants) could value.</summary>
        public List<EmcValueRule> Defaults = new List<EmcValueRule>();
        public List<RelativeValueRule> Relative = new List<RelativeValueRule>();
        public List<string> Blacklist = new List<string>();
        /// <summary>"freeIngredients": blacklisted (no value of their own) but free in recipes that use them (a schematic that isn't used up).</summary>
        public List<string> FreeIngredients = new List<string>();
        /// <summary>"grayList": left out of /emc missing and never given a guessed value, but valued as soon as a recipe makes them.</summary>
        public List<string> GrayList = new List<string>();
        /// <summary>"recipes": recipes the mapper can't see, as "output = ingredients" lines (same format as the CustomRecipes setting).</summary>
        public List<string> Recipes = new List<string>();
        /// <summary>Exceptions to the blacklist ("allow": ["game:creature-butterfly-*"]), applied after it.</summary>
        public List<string> Allow = new List<string>();
        /// <summary>"traderCurrency": items worth exactly the measured EMC per unit of trader currency (rusty gears).</summary>
        public List<string> TraderCurrency = new List<string>();
        public WorldTransmutationRules WorldTransmutation = new WorldTransmutationRules();
        public List<string> LoadedFiles = new List<string>();

        /// <summary>"variantGuessing.copyAcross": variant groups that only change look or placement (rock, colour, facing...).</summary>
        public HashSet<string> CopyAcrossGroups = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>"variantGuessing.neverAcross": variant groups that change what a thing is (ore, metal, gem...).</summary>
        public HashSet<string> NeverAcrossGroups = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>"variantGuessing.otherGroups": copy across groups in neither list as a last resort ("copy") or not ("never").</summary>
        public bool CopyAcrossOtherGroups = true;
        /// <summary>"fuelReference": fuels whose value per degree-second values fuels nothing else could value.</summary>
        public List<string> FuelReference = new List<string>();
        /// <summary>"constructions": things built in stages (sailboats) - the construction entity and the item it ends up as, '*' carried over.</summary>
        public List<KeyValuePair<string, string>> Constructions = new List<KeyValuePair<string, string>>();
        /// <summary>"stackData": code/wildcard → "item" / "full" / "itemprice", the built-in modes for items that carry contents or a material.</summary>
        public List<KeyValuePair<string, string>> StackData = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// A compatibility value file: <c>assets/vintagee/config/vintagee-emc-compat/&lt;modid&gt;.json</c> (or any value
        /// file with a <c>"requiresMod"</c> key). It only loads when every mod it names is loaded and the server hasn't
        /// disabled it, and its values are a last resort (see <see cref="CompatPath"/>).
        /// </summary>
        public class CompatFile
        {
            /// <summary>The asset location ("vintagee:config/vintagee-emc-compat/primitivesurvival.json"): the key the DisabledCompat setting uses.</summary>
            public string Source;
            /// <summary>From the file's "name" key, or the file name.</summary>
            public string Name;
            /// <summary>From the file's "//" key.</summary>
            public string Description = "";
            public List<string> RequiredMods = new List<string>();
            /// <summary>The first required mod that isn't loaded, or null when they all are.</summary>
            public string MissingMod;
            /// <summary>Disabled in the server config (DisabledCompat).</summary>
            public bool Disabled;
            /// <summary>How many rules the file holds (values, defaults, litre values and ratio rules).</summary>
            public int RuleCount;
            public bool Loaded => MissingMod == null && !Disabled;
        }

        /// <summary>Every compatibility file found, loaded or not.</summary>
        public List<CompatFile> Compat = new List<CompatFile>();
        /// <summary>The DisabledCompat setting these rules were loaded with, so a change to it reloads them.</summary>
        public string DisabledKey = "";

        /// <summary>
        /// Compatibility files: <c>assets/vintagee/config/vintagee-emc-compat/&lt;modid&gt;.json</c>, each with a
        /// <c>"requiresMod"</c> key (one mod id or a list). A file only loads when every mod it names is loaded, and its
        /// values are a last resort: <c>values</c> and <c>litreValues</c> are treated as <c>defaultValues</c>, so a recipe,
        /// trader price, ratio rule or variant that values the item always wins over the file.
        /// </summary>
        public const string CompatPath = "config/vintagee-emc-compat/";

        /// <summary>Value files that threw while being read (file → error), for the doctor.</summary>
        public Dictionary<string, string> BrokenFiles = new Dictionary<string, string>();

        public static string DisabledKeyOf(IEnumerable<string> disabled) => string.Join("|", (disabled ?? Enumerable.Empty<string>()).Where(d => d != null).Select(d => d.Trim().ToLowerInvariant()).OrderBy(d => d, StringComparer.Ordinal));

        /// <summary>True if <paramref name="source"/> (an asset location) is on the DisabledCompat list, which may also name just the file ("primitivesurvival" or "primitivesurvival.json").</summary>
        public static bool IsDisabled(string source, IEnumerable<string> disabled)
        {
            if (disabled == null) return false;
            string file = source.Substring(source.LastIndexOf('/') + 1);
            string bare = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 5) : file;
            foreach (string d in disabled)
            {
                string t = d?.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (t.Equals(source, StringComparison.OrdinalIgnoreCase) || t.Equals(file, StringComparison.OrdinalIgnoreCase) || t.Equals(bare, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static EmcRuleSet Load(ICoreAPI api, ILogger logger, IEnumerable<string> disabledCompat = null)
        {
            var set = new EmcRuleSet { DisabledKey = DisabledKeyOf(disabledCompat) };
            List<IAsset> assets;
            try
            {
                assets = api.Assets.GetMany(AssetPath, null, true);
            }
            catch (Exception e)
            {
                logger.Error("Could not enumerate {0} assets: {1}", AssetPath, e.Message);
                return set;
            }

            // Parse first, so the load order can take the files' own keys into account.
            var parsed = new List<(IAsset asset, JObject root, bool compat)>();
            foreach (IAsset asset in assets)
            {
                if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    JObject root = JObject.Parse(asset.ToText());
                    List<string> required = RequiredMods(root);
                    bool compat = required.Count > 0 || asset.Location.Path.StartsWith(CompatPath, StringComparison.OrdinalIgnoreCase);
                    if (compat)
                    {
                        string source = asset.Location.ToString();
                        string file = asset.Location.Path.Substring(asset.Location.Path.LastIndexOf('/') + 1);
                        var info = new CompatFile
                        {
                            Source = source,
                            Name = (string)root["name"] ?? file.Substring(0, file.Length - 5),
                            Description = (string)root["//"] ?? "",
                            RequiredMods = required,
                            MissingMod = required.FirstOrDefault(id => !api.ModLoader.IsModEnabled(id)),
                            Disabled = IsDisabled(source, disabledCompat),
                            RuleCount = CountRules(root)
                        };
                        set.Compat.Add(info);
                        if (!info.Loaded)
                        {
                            logger.VerboseDebug("EMC: not loading {0}: {1}", source, info.Disabled ? "disabled in the server config" : "its mod '" + info.MissingMod + "' isn't loaded");
                            continue;
                        }
                    }
                    parsed.Add((asset, root, compat));
                }
                catch (Exception e)
                {
                    logger.Error("Skipping malformed EMC file {0}: {1}", asset.Location, e.Message);
                }
            }

            // Our own defaults first, then our compatibility files, then everybody else (stable, alphabetical) so they can override.
            var ordered = parsed
                .OrderBy(p => p.asset.Location.Domain == VintageEModSystem.ModId ? (p.compat ? 1 : 0) : 2)
                .ThenBy(p => p.asset.Location.Domain, StringComparer.Ordinal)
                .ThenBy(p => p.asset.Location.Path, StringComparer.Ordinal);

            foreach (var (asset, root, compat) in ordered)
            {
                // One mod's malformed file (a string where an object is expected) must not take the server down.
                try
                {
                    set.Merge(root, asset.Location.ToString(), lastResort: compat);
                    set.LoadedFiles.Add(asset.Location.ToString());
                }
                catch (Exception e)
                {
                    logger.Error("EMC value file {0} could not be read and is skipped: {1}", asset.Location, e.Message);
                    set.BrokenFiles[asset.Location.ToString()] = e.Message;
                }
            }
            set.Compat.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            logger.Notification("Loaded {0} EMC base value rules and {1} last-resort rules from {2} file(s): {3}", set.Values.Count, set.Defaults.Count, set.LoadedFiles.Count, string.Join(", ", set.LoadedFiles));
            var skipped = set.Compat.Where(c => !c.Loaded).ToList();
            if (skipped.Count > 0) logger.Notification("EMC: {0} compatibility file(s) not loaded: {1}", skipped.Count, string.Join(", ", skipped.Select(c => c.Name + (c.Disabled ? " (disabled)" : " (" + c.MissingMod + " isn't loaded)"))));
            return set;
        }

        static int CountRules(JObject root)
        {
            int n = 0;
            foreach (string key in new[] { "values", "defaultValues", "litreValues" })
            {
                if (root[key] is JObject o) n += o.Properties().Count(p => p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float);
            }
            if (root["relativeValues"] is JArray rel) n += rel.Count;
            return n;
        }

        /// <summary>The "requiresMod" key: a mod id, or a list of them.</summary>
        static List<string> RequiredMods(JObject root)
        {
            var ids = new List<string>();
            JToken tok = root["requiresMod"];
            if (tok == null) return ids;
            if (tok.Type == JTokenType.String) ids.Add((string)tok);
            else if (tok is JArray arr) ids.AddRange(arr.Values<string>());
            return ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).ToList();
        }

        void Merge(JObject root, string source, bool lastResort = false)
        {
            if (root["values"] is JObject values)
            {
                foreach (JProperty prop in values.Properties())
                {
                    if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        (lastResort ? Defaults : Values).Add(new EmcValueRule { Pattern = prop.Name, Value = prop.Value.Value<double>(), Source = source });
                    }
                }
            }
            if (root["defaultValues"] is JObject defaults)
            {
                foreach (JProperty prop in defaults.Properties())
                {
                    if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        Defaults.Add(new EmcValueRule { Pattern = prop.Name, Value = prop.Value.Value<double>(), Source = source });
                    }
                }
            }
            if (root["litreValues"] is JObject litres)
            {
                foreach (JProperty prop in litres.Properties())
                {
                    if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        (lastResort ? Defaults : Values).Add(new EmcValueRule { Pattern = prop.Name, Value = prop.Value.Value<double>(), Source = source, PerLitre = true });
                    }
                }
            }
            if (root["relativeValues"] is JArray rel)
            {
                foreach (JToken tok in rel)
                {
                    string code = (string)tok["code"], of = (string)tok["of"];
                    double ratio = tok["ratio"]?.Value<double>() ?? 0;
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(of) && ratio > 0)
                    {
                        Relative.Add(new RelativeValueRule { Code = code, Of = of, Ratio = ratio, Source = source });
                    }
                }
            }
            if (root["blacklist"] is JArray bl)
            {
                Blacklist.AddRange(bl.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["freeIngredients"] is JArray freeIng)
            {
                FreeIngredients.AddRange(freeIng.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["grayList"] is JArray gray)
            {
                GrayList.AddRange(gray.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["recipes"] is JArray recipes)
            {
                Recipes.AddRange(recipes.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["traderCurrency"] is JArray currency)
            {
                TraderCurrency.AddRange(currency.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["allow"] is JArray allow)
            {
                Allow.AddRange(allow.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["variantGuessing"] is JObject vg)
            {
                if (vg["copyAcross"] is JArray copy) CopyAcrossGroups.UnionWith(copy.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
                if (vg["neverAcross"] is JArray never)
                {
                    foreach (string g in never.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)))
                    {
                        NeverAcrossGroups.Add(g);
                        CopyAcrossGroups.Remove(g);
                    }
                }
                if (vg["otherGroups"] != null) CopyAcrossOtherGroups = !string.Equals((string)vg["otherGroups"], "never", StringComparison.OrdinalIgnoreCase);
            }
            if (root["fuelReference"] is JArray fuel)
            {
                FuelReference.AddRange(fuel.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["constructions"] is JArray cons)
            {
                foreach (JToken tok in cons)
                {
                    string entity = (string)tok["entity"], makes = (string)tok["makes"];
                    if (!string.IsNullOrEmpty(entity) && !string.IsNullOrEmpty(makes)) Constructions.Add(new KeyValuePair<string, string>(entity, makes));
                }
            }
            if (root["stackData"] is JObject sd)
            {
                foreach (JProperty prop in sd.Properties())
                {
                    if (prop.Name.StartsWith("//")) continue;
                    if (prop.Value.Type == JTokenType.String) StackData.Add(new KeyValuePair<string, string>(prop.Name, (string)prop.Value));
                }
            }
            if (root["worldTransmutation"] is JObject wt)
            {
                if (wt["variantCycles"] is JObject cycles)
                {
                    foreach (JProperty prop in cycles.Properties())
                    {
                        if (prop.Value is JArray arr)
                        {
                            WorldTransmutation.VariantCycles[prop.Name] = arr.Values<string>().ToList();
                        }
                    }
                }
                if (wt["swaps"] is JArray swaps)
                {
                    foreach (JToken tok in swaps)
                    {
                        string from = (string)tok["from"], to = (string)tok["to"];
                        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                        {
                            WorldTransmutation.Swaps.Add(new SwapRule { From = from, To = to });
                        }
                    }
                }
                if (wt["blacklist"] is JArray wbl)
                {
                    WorldTransmutation.Blacklist.AddRange(wbl.Values<string>());
                }
            }
        }
    }
}
