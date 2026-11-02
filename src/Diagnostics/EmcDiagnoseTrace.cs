using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc diagnose 1.15.3: a value's whole chain down to the raw materials it rests on (trace), the short console form
    /// (short), every safe fix at once (fix-safe), what the pin an admin just made says about the mapper (Lesson), the mod
    /// list kept with each snapshot (ModListChanges), and the client's own log sent with each request (ClientLogLines).
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionTrace = "trace", ActionShort = "short";

        // ---------------------------------------------------------------- trace

        static void Trace(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            r.Section(x.L("diag-sec-trace", EmcExplain.NameOf(c)));
            r.Add(x.L("diag-trace-help"), EmcLineStyle.Note);
            if (!report.Values.ContainsKey(c))
            {
                r.Add(x.L("diag-trace-novalue") + " " + Link(ActionItem, c.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Warn);
                Back(x);
                return;
            }
            int nodes = 0;
            var roots = new Dictionary<CollectibleObject, string>();
            var suspicious = new List<CollectibleObject>();
            TraceNode(x, c, 0, new HashSet<CollectibleObject>(), ref nodes, roots, suspicious);
            if (nodes >= 80) x.Note(x.L("diag-trace-cut"), 0);
            r.Section(x.L("diag-sec-trace-roots", roots.Count));
            r.Add(x.L("diag-trace-roots-help"), EmcLineStyle.Note);
            foreach (var kv in roots.OrderByDescending(kv => x.V(kv.Key)).Take(30))
            {
                r.Add(EmcReport.Ref(kv.Key) + " " + F(x.V(kv.Key)) + ": " + kv.Value, EmcLineStyle.Text, 1);
            }
            if (suspicious.Count > 0)
            {
                r.Section(x.L("diag-sec-trace-suspicious", suspicious.Count));
                r.Add(x.L("diag-trace-suspicious-help"), EmcLineStyle.Note);
                foreach (CollectibleObject s in suspicious.Distinct().Take(10))
                {
                    r.Add(EmcReport.Ref(s) + " " + F(x.V(s)), EmcLineStyle.Warn, 1);
                    if (report.Origins.TryGetValue(s, out string o)) x.Note(Linkify(x, o), 2);
                    SuggestPinValued(x, s);
                }
            }
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionItem, c.Code.ToString(), x.L("diag-q-item-short")));
            r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        /// <summary>One item in the trace: its value and origin, then (indented) the recipe's ingredients, each traced in turn.</summary>
        static void TraceNode(Ctx x, CollectibleObject c, int depth, HashSet<CollectibleObject> path, ref int nodes, Dictionary<CollectibleObject, string> roots, List<CollectibleObject> suspicious)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            if (nodes++ >= 80 || depth > 8) return;
            report.Origins.TryGetValue(c, out string origin);
            origin ??= "?";
            double v = x.V(c);
            bool viaRecipe = report.Via.TryGetValue(c, out Conversion conv) && conv != null && !origin.StartsWith("worked backwards", StringComparison.Ordinal);
            string kind = origin == "base value" ? x.L("diag-trace-k-base")
                : origin.StartsWith("default value", StringComparison.Ordinal) ? x.L("diag-trace-k-default")
                : origin.StartsWith("worked backwards", StringComparison.Ordinal) ? x.L("diag-trace-k-backwards")
                : origin.StartsWith("trader", StringComparison.Ordinal) ? x.L("diag-trace-k-trader")
                : origin.StartsWith("relativeValues", StringComparison.Ordinal) ? x.L("diag-trace-k-ratio")
                : origin.StartsWith("guessed", StringComparison.Ordinal) || origin.StartsWith("same as", StringComparison.Ordinal) ? x.L("diag-trace-k-guess")
                : viaRecipe ? x.L("diag-trace-k-recipe") : x.L("diag-trace-k-other");
            bool backwardsMulti = origin.StartsWith("worked backwards", StringComparison.Ordinal) && origin.Contains(" + ");
            r.Add(EmcReport.Ref(c) + " " + F(v) + " ← " + kind, backwardsMulti ? EmcLineStyle.Warn : EmcLineStyle.Text, depth);
            if (backwardsMulti) suspicious.Add(c);
            if (!viaRecipe)
            {
                // Something that doesn't rest on ingredients: a root of the chain.
                if (!roots.ContainsKey(c)) roots[c] = kind + (origin == "base value" || origin.StartsWith("default value", StringComparison.Ordinal) ? "" : ": " + Linkify(x, origin));
                if (origin.StartsWith("worked backwards", StringComparison.Ordinal)) x.Note(Linkify(x, origin), depth + 1);
                return;
            }
            x.Note(Linkify(x, EmcMapper.Describe(conv)), depth + 1);
            if (!path.Add(c)) { x.Note(x.L("diag-trace-loop"), depth + 1); return; }
            foreach (ConversionInput input in conv.Inputs)
            {
                CollectibleObject pick = null;
                double best = double.MaxValue;
                foreach (CollectibleObject choice in input.Choices)
                {
                    double cv = x.V(choice);
                    if (cv > 0 && cv < best) { best = cv; pick = choice; }
                    else if (pick == null && report.Free.Contains(choice)) pick = choice;
                }
                if (pick == null) { x.Note(x.L("diag-trace-noingredient"), depth + 1); continue; }
                TraceNode(x, pick, depth + 1, path, ref nodes, roots, suspicious);
                if (nodes >= 80) break;
            }
            path.Remove(c);
        }

        /// <summary>A pin suggestion for an item that has a (suspect) value: its family median, if it differs.</summary>
        static void SuggestPinValued(Ctx x, CollectibleObject c)
        {
            var (value, why) = SuggestValue(x, c);
            if (value <= 0 || Math.Abs(value - x.V(c)) < 1e-6) return;
            x.R.Add(FixLink(x, "pin", VintageEModSystem.ConfigKey(c) + "=" + value.ToString("R", CultureInfo.InvariantCulture), x.L("diag-fix-pin", F(value))) + " " + x.L("diag-suggest-why", why), EmcLineStyle.Text, 2);
        }

        // ---------------------------------------------------------------- short: only what's wrong

        static void Short(Ctx x, EmcDiagnoseRequestPacket client)
        {
            var full = new EmcReport { Kind = EmcReport.DiagnoseKind };
            var fx = new Ctx { Mod = x.Mod, Sapi = x.Sapi, LangCode = x.LangCode, R = full, Report = x.Report, Index = x.Index, Player = x.Player, Held = x.Held, ThenAction = ActionShort };
            int problems = 0;
            if (MappingState(fx, true))
            {
                problems = Mapping(fx, false);
                Client(fx, client, true);
                Log(fx, false);
            }
            else problems = 1;
            x.R.Section(x.L("diag-sec-short"));
            x.R.Add(problems == 0 ? x.L("diag-verdict-ok") : x.L("diag-short-problems", problems), problems == 0 ? EmcLineStyle.Total : EmcLineStyle.Warn);
            bool lastWasWarn = false;
            foreach (EmcReportLine line in full.Lines)
            {
                if (line.Style == EmcLineStyle.Warn && !line.Text.Contains("worth fixing")) { x.R.Add(line.Text, line.Style, 0); lastWasWarn = true; }
                else if (lastWasWarn && (line.Text.StartsWith("\u2192", StringComparison.Ordinal) || line.Text.Contains("{{fix-"))) x.R.Add(line.Text, line.Style, 1);
                else if (line.Style == EmcLineStyle.Heading || line.Style == EmcLineStyle.Total) lastWasWarn = false;
            }
            x.R.Add(Link(ActionOverview, "", x.L("diag-q-full")), EmcLineStyle.Text);
        }

        // ---------------------------------------------------------------- fix-safe

        /// <summary>How many of the fixes that can't make anything worse apply right now: dead settings entries, disabled compat files, a stale table.</summary>
        static int SafeFixCount(Ctx x)
        {
            int n = 0;
            if (x.Index != null)
            {
                VintageEConfig cfg = x.Mod.Config;
                n += cfg.BaseValues.Keys.Concat(cfg.FixedValues.Keys).Concat(cfg.Blacklist).Concat(cfg.Allow).Concat(cfg.NoTransmute).Concat(cfg.GrayList).Concat(cfg.FreeIngredients).Count(k => !string.IsNullOrWhiteSpace(k) && !x.Index.Match(k).Any());
            }
            n += x.Mod.Rules.Compat.Count(f => f.Disabled);
            return n;
        }

        static string ApplySafeFixes(VintageEModSystem mod, IServerPlayer player, string lang)
        {
            string L(string key, params object[] args) => Lang.GetL(lang, "vintagee:" + key, args);
            var index = new CollectibleIndex(mod.Sapi.World.Collectibles);
            VintageEConfig cfg = mod.Config;
            bool Dead(string k) => !string.IsNullOrWhiteSpace(k) && !index.Match(k).Any();
            var removed = new List<string>();
            foreach (string k in cfg.BaseValues.Keys.Where(Dead).ToList()) { cfg.BaseValues.Remove(k); removed.Add(k); }
            foreach (string k in cfg.FixedValues.Keys.Where(Dead).ToList()) { cfg.FixedValues.Remove(k); removed.Add(k); }
            foreach (var list in new[] { cfg.Blacklist, cfg.Allow, cfg.NoTransmute, cfg.GrayList, cfg.FreeIngredients })
            {
                foreach (string k in list.Where(Dead).ToList()) { list.Remove(k); removed.Add(k); }
            }
            var enabled = new List<string>();
            foreach (var f in mod.Rules.Compat.Where(f => f.Disabled).ToList())
            {
                cfg.DisabledCompat.RemoveAll(d => EmcRuleSet.IsDisabled(f.Source, new[] { d }));
                enabled.Add(f.Name);
            }
            mod.SaveConfig();
            if (removed.Count + enabled.Count > 0) mod.RecalculateEmc("/emc diagnose safe fixes");
            if (player != null) mod.ResendTable(player);
            return L("diag-done-safe", removed.Count, enabled.Count, string.Join(", ", removed.Concat(enabled).Take(8)));
        }

        // ---------------------------------------------------------------- learning from pins

        /// <summary>
        /// What a pin says about the mapper: when an admin pins an item whose value had been worked out backwards from a
        /// recipe with several ingredients, that recipe priced the item by what it replaces, and the same weakness will hit
        /// other items the same way. The outcome line names the recipe and the pattern, and points at the bug report, so
        /// the general fix can be asked for instead of pinning item after item.
        /// </summary>
        static string Lesson(VintageEModSystem mod, string configKey, string lang)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return "";
            CollectibleObject c = PlayerEmc.Resolve(mod.Sapi.World, configKey.StartsWith("block:", StringComparison.Ordinal) ? "b:" + configKey.Substring(6) : configKey.StartsWith("item:", StringComparison.Ordinal) ? "i:" + configKey.Substring(5) : configKey);
            if (c == null || !report.Origins.TryGetValue(c, out string origin) || string.IsNullOrEmpty(origin)) return "";
            string L(string key, params object[] args) => Lang.GetL(lang, "vintagee:" + key, args);
            if (origin.StartsWith("worked backwards", StringComparison.Ordinal) && origin.Contains(" + "))
            {
                // Which other items rest on the same recipe's output: they'll have moved with this pin.
                int others = report.Via.Count(kv => kv.Value != null && kv.Key != c && report.Origins.TryGetValue(kv.Key, out string o) && o.StartsWith("worked backwards", StringComparison.Ordinal) && o.Contains(" + "));
                return " " + L("diag-lesson-backwards", EmcReport.Plain(mod.Sapi.World, origin), others);
            }
            if (origin.StartsWith("guessed", StringComparison.Ordinal) || origin.StartsWith("same as", StringComparison.Ordinal))
            {
                return " " + L("diag-lesson-guess", EmcReport.Plain(mod.Sapi.World, origin));
            }
            if (origin.StartsWith("default value", StringComparison.Ordinal)) return " " + L("diag-lesson-default", EmcReport.Plain(mod.Sapi.World, origin));
            return "";
        }

        // ---------------------------------------------------------------- the mod list between two mappings

        static void ModListChanges(Ctx x)
        {
            var prev = x.Mod.PreviousMods;
            var now = x.Mod.CurrentMods;
            if (prev == null || now == null) { x.Note(x.L("diag-changes-mods-unknown"), 0); return; }
            var added = now.Keys.Except(prev.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var removed = prev.Keys.Except(now.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var updated = now.Keys.Intersect(prev.Keys).Where(k => prev[k] != now[k]).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (added.Count + removed.Count + updated.Count == 0) { x.Ok(x.L("diag-changes-mods-same", now.Count)); return; }
            x.Warn(x.L("diag-changes-mods-differ"));
            foreach (string m in added) x.R.Add(x.L("diag-changes-mod-added", ModName(x, m), now[m]), EmcLineStyle.Text, 1);
            foreach (string m in removed) x.R.Add(x.L("diag-changes-mod-removed", m, prev[m]), EmcLineStyle.Text, 1);
            foreach (string m in updated) x.R.Add(x.L("diag-changes-mod-updated", ModName(x, m), prev[m], now[m]), EmcLineStyle.Text, 1);
        }

        // ---------------------------------------------------------------- the client's own log

        /// <summary>The last Vintage-E error and warning lines of the player's client-main.log (called on the client).</summary>
        public static string[] ClientLogLines(ICoreClientAPI capi, int max)
        {
            try
            {
                string file = Path.Combine(capi.DataBasePath, "Logs", "client-main.log");
                if (!File.Exists(file)) return Array.Empty<string>();
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, fs.Length - 1024 * 1024);
                fs.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                if (start > 0) reader.ReadLine();
                var all = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    bool ours = line.IndexOf("vintagee", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Vintage-E", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("VintageE", StringComparison.Ordinal) >= 0;
                    if (ours && LogLevel.IsMatch(line)) all.Add(line.Length > 400 ? line.Substring(0, 400) : line);
                }
                return all.Skip(Math.Max(0, all.Count - max)).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
    }
}
