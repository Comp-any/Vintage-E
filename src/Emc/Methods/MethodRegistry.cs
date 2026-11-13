using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Every method document Vintage-E has, as collected at one moment (start-up, or when a mod says its recipes changed).
    /// Read-only once made, so a mapping on a worker thread can use it while the game thread collects the next one.
    /// </summary>
    public sealed class MethodSnapshot
    {
        public static readonly MethodSnapshot Empty = new MethodSnapshot();

        public readonly List<MethodDoc> Docs = new List<MethodDoc>();
        /// <summary>Other mods' code that threw while answering <see cref="MethodRegistry.CollectEvent"/>: mod id (or "?") → what it threw.</summary>
        public readonly List<(string mod, string error)> ListenerErrors = new List<(string, string)>();
        /// <summary>Problems across documents (a method code sent twice, one unit priced two ways).</summary>
        public readonly List<MethodProblem> Problems = new List<MethodProblem>();
        /// <summary>Energy units by code, first declaration winning.</summary>
        public readonly Dictionary<string, MethodEnergyUnit> Units = new Dictionary<string, MethodEnergyUnit>(StringComparer.Ordinal);
        public DateTime CollectedAt;
        public string Reason = "";
        /// <summary>Over every document's text: the cache remaps when it changes.</summary>
        public string Fingerprint = "";
        /// <summary>False for <see cref="Empty"/> (nothing collected yet).</summary>
        public bool Collected;

        public IEnumerable<MethodDoc> UsableDocs => Docs.Where(d => d.Usable);
        public IEnumerable<MethodDef> AllMethods => UsableDocs.SelectMany(d => d.Methods);
        public int MethodCount => UsableDocs.Sum(d => d.Methods.Count);
        public int RecipeCount => UsableDocs.Sum(d => d.RecipeCount);
        public IEnumerable<string> Mods => UsableDocs.Select(d => d.ModId).Distinct();

        /// <summary>Each mod's documents as one short hash (for "what changed").</summary>
        public Dictionary<string, string> HashByMod()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in Docs.GroupBy(d => d.ModId))
            {
                string text = string.Join("\n", g.OrderBy(d => d.Source, StringComparer.Ordinal).Select(d => d.Source + "|" + d.Canonical));
                using var sha = SHA256.Create();
                result[g.Key] = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Substring(0, 12);
            }
            return result;
        }

        /// <summary>
        /// Whether a DisabledMethods entry switches off this recipe: a mod id ("vintagecreation"), a method
        /// ("vintagecreation:press") or one recipe ("vintagecreation:press/plate"); "*" works as in codes.
        /// </summary>
        public static bool IsDisabled(MethodDef method, MethodRecipe recipe, IEnumerable<string> disabled)
        {
            if (disabled == null || method == null) return false;
            foreach (string raw in disabled)
            {
                string d = raw?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(d)) continue;
                if (d == method.ModId || Matches(d, method.Code)) return true;
                if (recipe != null && Matches(d, recipe.FullCode.ToLowerInvariant())) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether an entry switches off one variant: "vintagecreation:press/plate?metal=iron" (the recipe, method or mod,
        /// then name=value pairs joined by &amp;; "*" works in both).
        /// </summary>
        public static bool IsVariantDisabled(MethodDef method, MethodRecipe recipe, Dictionary<string, string> captures, IEnumerable<string> disabled)
        {
            if (disabled == null || method == null || captures == null || captures.Count == 0) return false;
            foreach (string raw in disabled)
            {
                string d = raw?.Trim();
                int q = d?.IndexOf('?') ?? -1;
                if (q <= 0) continue;
                string target = d.Substring(0, q).ToLowerInvariant();
                if (!(target == method.ModId || Matches(target, method.Code) || (recipe != null && Matches(target, recipe.FullCode.ToLowerInvariant())))) continue;
                bool all = true;
                foreach (string pair in d.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) { all = false; break; }
                    string name = pair.Substring(0, eq).Trim(), value = pair.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (!captures.TryGetValue(name, out string got) || !Matches(value, (got ?? "").ToLowerInvariant())) { all = false; break; }
                }
                if (all) return true;
            }
            return false;
        }

        /// <summary>The part of a DisabledMethods entry before a variant's "?name=value".</summary>
        public static string EntryTarget(string entry)
        {
            entry = entry?.Trim() ?? "";
            int q = entry.IndexOf('?');
            return q > 0 ? entry.Substring(0, q) : entry;
        }

        static bool Matches(string pattern, string code)
        {
            if (!pattern.Contains('*')) return pattern == code;
            string rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(code, rx);
        }
    }

    /// <summary>
    /// Gathers other mods' crafting methods: files in <c>assets/&lt;modid&gt;/config/vintagee-methods/</c>, and what mods'
    /// code puts into the <see cref="CollectEvent"/> event. Runs on the game thread only (it calls into other mods).
    /// </summary>
    public static class MethodRegistry
    {
        /// <summary>Pushed by Vintage-E with a TreeAttribute: each mod sets a string under its mod id, the JSON of its method document.</summary>
        public const string CollectEvent = "vintagee:collectmethods";
        /// <summary>Pushed by a mod whose recipes changed while the server runs: Vintage-E collects again and remaps. Its data may be a string attribute with the mod's id.</summary>
        public const string ChangedEvent = "vintagee:methodschanged";
        public const string AssetFolder = "config/vintagee-methods/";
        /// <summary>The server's own documents: VintagestoryData/ModConfig/vintagee-methods/*.json, read again on every collect.</summary>
        public const string ServerFolder = "vintagee-methods";

        /// <summary>The folder of the server's own method documents.</summary>
        public static string ServerPath => System.IO.Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, ServerFolder);
        /// <summary>Keys Vintage-E itself puts in the event's tree.</summary>
        public static readonly HashSet<string> ReservedKeys = new HashSet<string>(StringComparer.Ordinal) { "format", "vintageeVersion" };

        public static MethodSnapshot Collect(ICoreServerAPI sapi, ILogger logger, string reason, string version)
        {
            var snap = new MethodSnapshot { CollectedAt = DateTime.Now, Reason = reason ?? "", Collected = true };
            CollectibleIndex index = null;
            try { index = new CollectibleIndex(sapi.World.Collectibles); }
            catch (Exception e) { logger.Warning("EMC: crafting methods: could not index the blocks and items: {0}", e.Message); }

            // 1. Files.
            List<IAsset> assets = new List<IAsset>();
            try { assets = sapi.Assets.GetMany(AssetFolder, null, true); }
            catch (Exception e) { logger.Warning("EMC: crafting methods: could not list {0}: {1}", AssetFolder, e.Message); }
            foreach (IAsset asset in assets.OrderBy(a => a.Location.ToString(), StringComparer.Ordinal))
            {
                if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = asset.ToText(); }
                catch (Exception e) { text = null; logger.Warning("EMC: crafting methods: could not read {0}: {1}", asset.Location, e.Message); }
                snap.Docs.Add(MethodParser.Parse(text, asset.Location.Domain, "assets/" + asset.Location.Domain + "/" + asset.Location.Path, "file", sapi, index));
            }

            // 1b. The server's own: a server owner's changes, a copy of a mod's methods to edit, a mod author's draft. Read
            // from disk every time, so "/emc methods reload" picks up an edit without a restart.
            try
            {
                if (System.IO.Directory.Exists(ServerPath))
                {
                    foreach (string file in System.IO.Directory.GetFiles(ServerPath, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                    {
                        string text;
                        try { text = System.IO.File.ReadAllText(file); }
                        catch (Exception e) { text = null; logger.Warning("EMC: crafting methods: could not read {0}: {1}", file, e.Message); }
                        snap.Docs.Add(MethodParser.Parse(text, "", "ModConfig/" + ServerFolder + "/" + System.IO.Path.GetFileName(file), "server", sapi, index));
                    }
                }
            }
            catch (Exception e) { logger.Warning("EMC: crafting methods: could not list {0}: {1}", ServerPath, e.Message); }

            // 2. Other mods' code, through the game's event bus (no reference to Vintage-E needed).
            var tree = new TreeAttribute();
            tree.SetInt("format", MethodParser.Format);
            tree.SetString("vintageeVersion", version ?? "");
            try { sapi.Event.PushEvent(CollectEvent, tree); }
            catch (Exception e)
            {
                // The event bus stops at the first listener that throws: the ones after it are missed this time.
                string mod = ModOf(sapi, e) ?? "?";
                snap.ListenerErrors.Add((mod, e.GetType().Name + ": " + e.Message));
                logger.Error("EMC: crafting methods: mod '{0}' threw while sending its crafting methods; listeners after it were not asked this time: {1}", mod, e);
            }
            foreach (KeyValuePair<string, IAttribute> kv in tree)
            {
                if (ReservedKeys.Contains(kv.Key)) continue;
                string key = (kv.Key ?? "").Trim().ToLowerInvariant();
                int cut = key.IndexOfAny(new[] { ':', '/' });
                string modId = cut > 0 ? key.Substring(0, cut) : key;
                string json = kv.Value?.GetValue() as string;
                if (json == null)
                {
                    var bad = new MethodDoc { ModId = modId, Source = "code (" + kv.Key + ")", Route = "code", Error = "sent something that isn't a string: the document goes in as its JSON text (tree.SetString(modid, json))" };
                    bad.Problems.Add(new MethodProblem { Message = bad.Error, Error = true });
                    snap.Docs.Add(bad);
                    continue;
                }
                MethodDoc doc = MethodParser.Parse(json, modId, "code (" + kv.Key + ")", "code", sapi, index);
                if (!sapi.ModLoader.IsModEnabled(modId)) doc.Problems.Add(new MethodProblem { Message = "sent under \"" + kv.Key + "\", which isn't a loaded mod's id: put the document under your mod's id" });
                snap.Docs.Add(doc);
            }

            // 3. Required mods, and what clashes between documents. The server's own come first: a method or energy unit of
            // theirs replaces a mod's with the same code (that's how a server owner changes a mod's numbers).
            var seenMethods = new Dictionary<string, MethodDoc>(StringComparer.Ordinal);
            var unitFrom = new Dictionary<string, MethodDoc>(StringComparer.Ordinal);
            foreach (MethodDoc doc in snap.Docs.OrderBy(d => d.FromServer ? 0 : 1).ToList())
            {
                if (doc.Error != null) continue;
                // An entry "a|b" is met when either mod is loaded.
                doc.MissingMod = doc.RequiresMods.FirstOrDefault(id => !id.Split('|').Any(alt => sapi.ModLoader.IsModEnabled(alt.Trim())));
                if (doc.MissingMod != null) continue;
                foreach (MethodDef m in doc.Methods.ToList())
                {
                    if (seenMethods.TryGetValue(m.Code, out MethodDoc first))
                    {
                        if (first.FromServer && !doc.FromServer)
                            doc.Problems.Add(new MethodProblem { Path = m.Path, Message = "the method " + m.Code + " is replaced by the server's " + first.Source });
                        else
                        {
                            snap.Problems.Add(new MethodProblem { Path = doc.Source + " " + m.Path, Message = "the method " + m.Code + " also comes from " + first.Source + "; this one is left out", Error = true });
                            doc.Problems.Add(new MethodProblem { Path = m.Path, Message = "the method " + m.Code + " also comes from " + first.Source + "; this one is left out", Error = true });
                        }
                        doc.Methods.Remove(m);
                        continue;
                    }
                    seenMethods[m.Code] = doc;
                }
                foreach (var kv in doc.Units)
                {
                    if (snap.Units.TryGetValue(kv.Key, out MethodEnergyUnit was))
                    {
                        MethodDoc from = unitFrom[kv.Key];
                        if (from.FromServer && !doc.FromServer) doc.Problems.Add(new MethodProblem { Path = kv.Value.Path, Message = "the energy unit " + kv.Key + " is replaced by the server's " + from.Source });
                        else if (was.EmcPerUnit != kv.Value.EmcPerUnit || was.Generators.Count != kv.Value.Generators.Count || was.OnlyFromFuel != kv.Value.OnlyFromFuel)
                            snap.Problems.Add(new MethodProblem { Path = doc.Source + " " + kv.Value.Path, Message = "the energy unit " + kv.Key + " is declared by " + was.ModId + " too, differently; " + was.ModId + "'s counts" });
                        continue;
                    }
                    snap.Units[kv.Key] = kv.Value;
                    unitFrom[kv.Key] = doc;
                }
            }

            // 4. The fingerprint, and a line per mod for the log.
            var all = new StringBuilder();
            foreach (MethodDoc d in snap.Docs.OrderBy(d => d.ModId, StringComparer.Ordinal).ThenBy(d => d.Source, StringComparer.Ordinal))
                all.Append(d.Route).Append('|').Append(d.ModId).Append('|').Append(d.Source).Append('|').Append(d.Usable).Append('|').Append(d.Canonical).Append('\n');
            using (var sha = SHA256.Create()) snap.Fingerprint = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString())));
            foreach (var g in snap.Docs.GroupBy(d => d.ModId).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var usable = g.Where(d => d.Usable).ToList();
                int errors = g.Sum(d => d.Problems.Count(p => p.Error)), warnings = g.Sum(d => d.Problems.Count(p => !p.Error));
                if (usable.Count == 0 && g.All(d => d.MissingMod != null)) { logger.VerboseDebug("EMC: crafting methods for {0} not used: the mod isn't loaded", g.Key); continue; }
                logger.Notification("EMC: crafting methods from {0}: {1} methods, {2} recipes ({3}){4}", g.Key, usable.Sum(d => d.Methods.Count), usable.Sum(d => d.RecipeCount),
                    string.Join(", ", g.Select(d => d.Route).Distinct()), errors + warnings > 0 ? "; " + errors + " problems, " + warnings + " warnings (/emc diagnose methods " + g.Key + ")" : "");
            }
            return snap;
        }

        /// <summary>Which loaded mod's code an exception came from, by the assemblies on its stack.</summary>
        internal static string ModOf(ICoreServerAPI sapi, Exception e)
        {
            try
            {
                var own = typeof(MethodRegistry).Assembly;
                var frames = new System.Diagnostics.StackTrace(e, false).GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>();
                foreach (var frame in frames)
                {
                    var asm = frame.GetMethod()?.DeclaringType?.Assembly;
                    if (asm == null || asm == own || asm == typeof(ICoreAPI).Assembly || asm == typeof(object).Assembly) continue;
                    ModSystem system = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().Assembly == asm);
                    if (system?.Mod?.Info?.ModID != null) return system.Mod.Info.ModID;
                }
            }
            catch { /* best effort */ }
            return null;
        }
    }
}
