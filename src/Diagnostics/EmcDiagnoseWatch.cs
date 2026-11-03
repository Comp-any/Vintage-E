using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc diagnose 1.15.2: refusals the table and tablet made recently (watch), a suggested value for anything
    /// missing (pins), and a drafted compatibility file for a mod whose raw materials have no value (draftcompat).
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionWatch = "watch", ActionDraft = "draftcompat";

        // ---------------------------------------------------------------- watch: refusals as they happen

        public sealed class Refusal
        {
            public DateTime At;
            public string Player;
            public string Code;
            public string[] Args;
            public string ItemKey;
        }

        static readonly object refusalsLock = new object();
        static readonly List<Refusal> refusals = new List<Refusal>();
        const int MaxRefusals = 300;

        /// <summary>Called by the transmutation service whenever it turns a request down. Cheap; always on.</summary>
        public static void RecordRefusal(IServerPlayer player, string code, string[] args, ItemStack stack)
        {
            if (string.IsNullOrEmpty(code)) return;
            var r = new Refusal { At = DateTime.Now, Player = player?.PlayerName ?? "?", Code = code, Args = args ?? Array.Empty<string>(), ItemKey = stack?.Collectible?.Code != null ? PlayerEmc.KeyOf(stack.Collectible) : null };
            lock (refusalsLock)
            {
                refusals.Add(r);
                if (refusals.Count > MaxRefusals) refusals.RemoveRange(0, refusals.Count - MaxRefusals);
            }
        }

        public static void ClearRefusals()
        {
            lock (refusalsLock) refusals.Clear();
        }

        static void Watch(Ctx x, string arg)
        {
            EmcReport r = x.R;
            int minutes = 10;
            if (!string.IsNullOrEmpty(arg) && !int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)) minutes = 10;
            minutes = Math.Clamp(minutes, 1, 24 * 60);
            r.Section(x.L("diag-sec-watch", minutes));
            r.Add(x.L("diag-watch-help"), EmcLineStyle.Note);
            List<Refusal> recent;
            lock (refusalsLock) recent = refusals.Where(f => f.At > DateTime.Now.AddMinutes(-minutes)).ToList();
            if (recent.Count == 0)
            {
                x.Ok(x.L("diag-watch-none", minutes));
                x.Note(x.L("diag-watch-none-how"), 0);
                r.Section(x.L("diag-sec-next"));
                r.Add(Link(ActionWatch, "60", x.L("diag-q-watch-longer")));
                r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
                return;
            }
            r.Add(x.L("diag-watch-found", recent.Count, recent.Select(f => f.Player).Distinct().Count()), EmcLineStyle.Total);
            foreach (var g in recent.GroupBy(f => f.Code).OrderByDescending(g => g.Count()))
            {
                r.Section(x.L("diag-watch-code", g.Count(), Lang.GetL(x.LangCode, "vintagee:error-" + g.Key, "{0}", "{1}")));
                r.Add(x.L("diag-watch-means-" + (WatchExplained.Contains(g.Key) ? g.Key : "other")), EmcLineStyle.Note);
                foreach (var byItem in g.GroupBy(f => f.ItemKey ?? "").OrderByDescending(gi => gi.Count()).Take(12))
                {
                    CollectibleObject c = byItem.Key.Length > 0 ? PlayerEmc.Resolve(x.Sapi.World, byItem.Key) : null;
                    var players = byItem.Select(f => f.Player).Distinct().ToList();
                    string who = string.Join(", ", players.Take(4)) + (players.Count > 4 ? ", ..." : "");
                    string line = (c != null ? EmcReport.Ref(c) : x.L("diag-watch-noitem")) + " ×" + byItem.Count() + " (" + who + ", " + x.L("diag-watch-last", byItem.Max(f => f.At).ToString("HH:mm:ss")) + ")";
                    r.Add(line, EmcLineStyle.Text, 1);
                    if (c == null) continue;
                    switch (g.Key)
                    {
                        case "novalue":
                            r.Add(Link(ActionItem, c.Code.ToString(), x.L("diag-why-link")) + "  " + Link("setvalue", byItem.Key, x.L("diag-fix-setvalue")), EmcLineStyle.Text, 2);
                            break;
                        case "notransmute":
                            string p = x.Mod.Config.NoTransmute.LastOrDefault(q => CollectibleIndex.Matches(q, c));
                            if (p != null) r.Add(FixLink(x, "notransmute", p, x.L("diag-fix-notransmute")), EmcLineStyle.Text, 2);
                            else x.Note(x.L("diag-item-notransmute-fix-file"), 2);
                            break;
                        case "hascontents":
                            r.Add(Link(ActionItem, c.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 2);
                            break;
                        case "unknown":
                            if (x.Player != null && players.Contains(x.Player.PlayerName)) r.Add(FixLink(x, "learn", byItem.Key, x.L("diag-fix-learn", EmcExplain.NameOf(c))), EmcLineStyle.Text, 2);
                            break;
                    }
                }
                switch (g.Key)
                {
                    case "hascontents":
                        r.Add(FixLink(x, "setting", "ProtectContainersWithContents=false", x.L("diag-fix-setting", "ProtectContainersWithContents", "false")), EmcLineStyle.Text, 1);
                        break;
                    case "toomanyexact":
                        r.Add(FixLink(x, "setting", "MaxExactKnowledge=" + (x.Mod.Config.MaxExactKnowledge * 2), x.L("diag-fix-setting", "MaxExactKnowledge", (x.Mod.Config.MaxExactKnowledge * 2).ToString())), EmcLineStyle.Text, 1);
                        break;
                }
            }
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionWatch, minutes.ToString(), x.L("diag-q-watch-again")));
            r.Add(Link(ActionWatch, (minutes * 6).ToString(), x.L("diag-q-watch-longer")));
            r.Add(FixLink(x, "clearwatch", "", x.L("diag-fix-clearwatch")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        static readonly HashSet<string> WatchExplained = new HashSet<string> { "novalue", "notransmute", "hascontents", "unknown", "notenoughemc", "toomanyexact", "noaccess", "cursorbusy", "alreadyknown" };

        // ---------------------------------------------------------------- pins: a suggested value

        /// <summary>
        /// A value to suggest for an unvalued item: the middle value of its valued variants, else of its mod's other items in
        /// the same family of names, else nothing. Says where the number comes from.
        /// </summary>
        static (double value, string why) SuggestValue(Ctx x, CollectibleObject c)
        {
            if (x.Index == null || c?.Code == null) return (0, null);
            EmcMappingReport report = x.Report;
            string family = c.Code.Domain + ":" + c.Code.Path.Split('-')[0];
            var siblings = x.Index.Match(family + "-*").Concat(x.Index.Match(family)).Where(s => s != c && report.Values.TryGetValue(s, out double v) && v > 0).Select(s => report.Values[s]).OrderBy(v => v).ToList();
            if (siblings.Count > 0) return (Tidy(siblings[siblings.Count / 2]), x.L("diag-suggest-siblings", siblings.Count));
            // Items of other families that share a word with it (a mod's "fish-...", "fishfillet-..." and "fishraw-...").
            string word = c.Code.Path.Split('-')[0];
            var related = report.Values.Where(kv => kv.Key?.Code != null && kv.Key.Code.Domain == c.Code.Domain && kv.Value > 0 && kv.Key != c
                && (kv.Key.Code.Path.StartsWith(word, StringComparison.Ordinal) || word.StartsWith(kv.Key.Code.Path.Split('-')[0], StringComparison.Ordinal)) && kv.Key.Code.Path.Split('-')[0] != word)
                .Select(kv => kv.Value).OrderBy(v => v).ToList();
            if (related.Count >= 3) return (Tidy(related[related.Count / 2]), x.L("diag-suggest-related", related.Count));
            return (0, null);
        }

        static double Tidy(double v)
        {
            if (v <= 0) return 0;
            if (v >= 100) return Math.Round(v);
            if (v >= 10) return Math.Round(v * 2) / 2;
            return Math.Round(v * 100) / 100;
        }

        /// <summary>The "pin it at N" line for an unvalued item, when something can be suggested.</summary>
        static void SuggestPin(Ctx x, CollectibleObject c)
        {
            var (value, why) = SuggestValue(x, c);
            if (value <= 0) return;
            x.R.Add(FixLink(x, "pin", VintageEModSystem.ConfigKey(c) + "=" + value.ToString("R", CultureInfo.InvariantCulture), x.L("diag-fix-pin", F(value))) + " " + x.L("diag-suggest-why", why), EmcLineStyle.Text, 1);
        }

        /// <summary>Every missing item of a mod that has a suggestion: pinned at once.</summary>
        static int PinSuggested(VintageEModSystem mod, Ctx x, string domain, out int skipped)
        {
            int n = 0;
            skipped = 0;
            foreach (CollectibleObject c in x.Report.Missing.Where(m => m.Code.Domain == domain).ToList())
            {
                var (value, _) = SuggestValue(x, c);
                if (value <= 0) { skipped++; continue; }
                mod.Config.FixedValues[VintageEModSystem.ConfigKey(c)] = value;
                n++;
            }
            if (n > 0) { mod.SaveConfig(); mod.RequestRemap("/emc diagnose pin all"); }
            return n;
        }

        // ---------------------------------------------------------------- draftcompat: a compatibility file to adjust and share

        static void DraftCompat(Ctx x, string domain)
        {
            EmcReport r = x.R;
            domain = (domain ?? "").ToLowerInvariant();
            r.Section(x.L("diag-sec-draft", ModName(x, domain)));
            if (domain.Length == 0 || domain == "game" || domain == "vintagee" || x.Index == null || !x.Index.All.Any(c => c.Code.Domain == domain))
            {
                x.Warn(x.L("diag-draft-nomod", domain));
                r.Add(Link(ActionMods, "", x.L("diag-q-mods")));
                return;
            }
            EmcMappingReport report = x.Report;
            // The raw materials: missing items nothing makes. Items waiting on them follow once these have values.
            var raw = report.Missing.Where(c => c.Code.Domain == domain && !(report.MissingRecipes.TryGetValue(c, out var recipes) && recipes.Count > 0)).OrderBy(c => c.Code.Path, StringComparer.Ordinal).ToList();
            var waiting = report.Missing.Where(c => c.Code.Domain == domain).Except(raw).ToList();
            if (raw.Count == 0)
            {
                x.Ok(x.L("diag-draft-nothing", waiting.Count));
                r.Add(Link(ActionMod, domain, x.L("diag-q-thismod", ModName(x, domain))));
                return;
            }
            string name = ModName(x, domain);
            string modVersion = "?";
            try { modVersion = x.Sapi.ModLoader.GetMod(domain)?.Info?.Version ?? "?"; } catch { }
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"name\": ").Append(Json(name.Contains(" (") ? name.Substring(0, name.IndexOf(" (")) : name)).Append(",\n");
            sb.Append("  \"requiresMod\": ").Append(Json(domain)).Append(",\n");
            sb.Append("  \"//\": ").Append(Json("Drafted by /emc diagnose draftcompat on " + DateTime.Now.ToString("yyyy-MM-dd") + " for " + name + " " + modVersion + " with Vintage-E " + (x.Mod.Version ?? "?") +
                ". Last-resort values for raw materials nothing the mapper reads makes; a recipe, trader price, ratio rule or variant always wins over them. Each number is a guess from related items (see its // line) - adjust them to what the item is worth next to the base game's (stone 1, copper ingot 128, a fish 24), then share the file on the Vintage-E Mod DB page so it can ship with the mod.")).Append(",\n");
            sb.Append("  \"values\": {\n");
            int withSuggestion = 0;
            // Wildcards where every variant of a family is missing and gets the same suggestion.
            var families = raw.GroupBy(c => c.Code.Path.Split('-')[0]).ToList();
            var lines = new List<string>();
            foreach (var g in families)
            {
                var items = g.ToList();
                var suggestions = items.Select(c => SuggestValue(x, c)).ToList();
                bool allSame = items.Count > 1 && suggestions.All(s => s.value > 0 && Math.Abs(s.value - suggestions[0].value) < 1e-9) && !x.Index.Match(domain + ":" + g.Key + "-*").Except(items).Any(o => report.Values.ContainsKey(o));
                if (allSame)
                {
                    string key = domain + ":" + g.Key + "-*";
                    lines.Add("    " + Json("//" + key) + ": " + Json(items.Count + " variants, none valued: " + suggestions[0].why) + ",");
                    lines.Add("    " + Json(key) + ": " + suggestions[0].value.ToString("R", CultureInfo.InvariantCulture) + ",");
                    withSuggestion += items.Count;
                    continue;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    string key = items[i].Code.ToString();
                    var (value, why) = suggestions[i];
                    if (value > 0) withSuggestion++;
                    lines.Add("    " + Json("//" + key) + ": " + Json(value > 0 ? why : "no related item has a value: set this by hand (0 means no EMC on purpose)") + ",");
                    lines.Add("    " + Json(key) + ": " + (value > 0 ? value.ToString("R", CultureInfo.InvariantCulture) : "1") + ",");
                }
            }
            if (lines.Count > 0) lines[lines.Count - 1] = lines[lines.Count - 1].TrimEnd(',');
            foreach (string l in lines) sb.Append(l).Append('\n');
            sb.Append("  }\n}\n");
            string file = null;
            try
            {
                file = Path.Combine(VintageEModSystem.DataFolder(x.Sapi), "vintagee-emc-compat-" + domain + ".json");
                File.WriteAllText(file, sb.ToString());
            }
            catch (Exception e) { x.Sapi.Logger.Warning("Vintage-E: could not write the drafted compat file: {0}", e.Message); }
            r.Add(x.L("diag-draft-made", raw.Count, withSuggestion, raw.Count - withSuggestion, waiting.Count), EmcLineStyle.Total);
            if (file != null) x.Ok(x.L("diag-draft-saved", file));
            r.Add(x.L("diag-draft-how1"), EmcLineStyle.Text, 1);
            r.Add(x.L("diag-draft-how2", VintageEModSystem.ModDbUrl), EmcLineStyle.Text, 1);
            x.Note(x.L("diag-draft-paste", x.Mod.Version ?? "?", name, modVersion, raw.Count), 2);
            r.Add(x.L("diag-draft-how3"), EmcLineStyle.Text, 1);
            if (withSuggestion > 0) r.Add(FixLink(x, "pinall", domain, x.L("diag-fix-pinall", withSuggestion)), EmcLineStyle.Text, 1);
            r.Section(x.L("diag-draft-text"));
            foreach (string line in sb.ToString().Split('\n')) r.Add(line.Replace("[[", "").Replace("{{", ""), EmcLineStyle.Note);
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionMod, domain, x.L("diag-q-thismod", name)));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        static string Json(string s) => Newtonsoft.Json.JsonConvert.ToString(s ?? "");
    }
}
