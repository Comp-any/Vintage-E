using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The second half of /emc diagnose (1.15.1): what the player's own game reports, the server log, other mods'
    /// patches, values out of line with their family, a mapping without other mods, a player's state, one-click fixes,
    /// a bug report, symptoms and what the last mapping changed.
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionClient = "client", ActionLog = "log", ActionPatches = "patches", ActionOutliers = "outliers", ActionClean = "clean",
            ActionPlayer = "player", ActionPlayers = "players", ActionBugReport = "bugreport", ActionSymptom = "symptom", ActionChanges = "changes", ActionNoValue = "novalue";

        static readonly string[] AllActions =
        {
            ActionItem, ActionHand, ActionMod, ActionMods, ActionSporadic, ActionMapping, ActionOverview, ActionClient, ActionLog, ActionPatches,
            ActionOutliers, ActionClean, ActionPlayer, ActionPlayers, ActionBugReport, ActionSymptom, ActionChanges, ActionWatch, ActionDraft, ActionTrace, ActionShort, ActionChecks, ActionBaseline, ActionProfit, ActionSkipped,
            ActionNoValue, ActionStartup, ActionHealth
        };

        /// <summary>A link that does something and then shows the report the player is on again: {{fix-<what>|target;then;thenArg|label}}.</summary>
        static string FixLink(Ctx x, string what, string target, string label)
            => Link("fix-" + what, (target ?? "").Replace(';', ',').Replace('|', '/') + ";" + x.ThenAction + ";" + (x.ThenArg ?? "").Replace(';', ','), label);

        // ---------------------------------------------------------------- 1, 2, 4: the player's game

        static void Client(Ctx x, EmcDiagnoseRequestPacket client, bool heading)
        {
            EmcReport r = x.R;
            if (heading) r.Section(x.L("diag-sec-client"));
            if (x.Player == null)
            {
                x.Note(x.L("diag-client-console"), 0);
                return;
            }
            if (client == null || !client.HasClientInfo)
            {
                x.Note(x.L("diag-client-noinfo"), 0);
                r.Add(Link(ActionClient, "", x.L("diag-q-client")));
                return;
            }
            string serverVersion = x.Mod.Version ?? "?";
            if (string.IsNullOrEmpty(client.ClientVersion)) { x.Warn(x.L("diag-client-noversion")); }
            else if (client.ClientVersion != serverVersion)
            {
                x.Warn(x.L("diag-client-versiondiff", client.ClientVersion, serverVersion));
                x.Explain(VersionImpact(x, client.ClientVersion, serverVersion));
                x.Fix(x.L("diag-client-versiondiff-fix"));
            }
            else x.Ok(x.L("diag-client-version", client.ClientVersion));

            int serverCount = x.Mod.Emc.Count;
            long serverHash = x.Mod.Emc.Fingerprint();
            if (client.ClientTableCount == 0)
            {
                x.Warn(x.L("diag-client-notable"));
                x.Explain(x.L("diag-client-notable-why"));
                x.Fix(x.L("diag-client-notable-fix"));
                r.Add(FixLink(x, "resend", "", x.L("diag-fix-resend")));
            }
            else if (client.ClientTableHash != serverHash || client.ClientTableCount != serverCount)
            {
                x.Warn(x.L("diag-client-stale", client.ClientTableCount, serverCount));
                x.Fix(x.L("diag-client-stale-fix"));
                r.Add(FixLink(x, "resend", "", x.L("diag-fix-resend")));
            }
            else x.Ok(x.L("diag-client-table", client.ClientTableCount, client.ClientTableVersion));

            if (client.ClientPatched < 3)
            {
                x.Warn(x.L("diag-client-patches", client.ClientPatched));
                x.Fix(x.L("diag-client-patches-fix"));
            }
            else x.Ok(x.L("diag-client-patches-ok"));

            if (client.ClientLog != null && client.ClientLog.Length > 0)
            {
                x.Warn(x.L("diag-client-log", client.ClientLog.Length));
                foreach (string l in client.ClientLog) r.Add(Explain(x, Anonymise(l)), EmcLineStyle.Text, 1);
            }
            else if (client.HasClientInfo) x.Ok(x.L("diag-client-log-clean"));
            ClientEnvironment(x, client);
            if (client.Stack != null && client.Stack.Length > 0)
            {
                if (!client.ClientHeldHasTooltip)
                {
                    x.Warn(x.L("diag-client-notooltip"));
                    x.Fix(x.L("diag-client-notooltip-fix"));
                }
                else x.Ok(x.L("diag-client-tooltip"));
            }
        }

        // ---------------------------------------------------------------- 3: the server log

        /// <summary>The last Vintage-E errors and warnings in Logs/server-main.log, in plain words.</summary>
        static List<string> LogLines(Ctx x, int max, out string error)
        {
            error = null;
            var result = new List<string>();
            try
            {
                string file = Path.Combine(x.Sapi.DataBasePath, "Logs", "server-main.log");
                if (!File.Exists(file)) { error = x.L("diag-log-nofile", file); return result; }
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, fs.Length - 2 * 1024 * 1024);
                fs.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                if (start > 0) reader.ReadLine(); // a partial line
                var all = new List<string>();
                string line;
                bool after = false;
                while ((line = reader.ReadLine()) != null)
                {
                    bool ours = line.IndexOf("vintagee", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Vintage-E", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("VintageE", StringComparison.Ordinal) >= 0;
                    bool bad = LogLevel.IsMatch(line) || (ours && line.Contains("Exception") && !line.Contains("[Notification]"));
                    if (ours && bad) { all.Add(line); after = true; }
                    else if (after && (line.StartsWith("   at ", StringComparison.Ordinal) || line.StartsWith("  at ", StringComparison.Ordinal))) { all.Add("    " + line.Trim()); }
                    else after = false;
                }
                int skip = Math.Max(0, all.Count - max);
                result.AddRange(all.Skip(skip).Select(Anonymise));
                if (skip > 0) result.Insert(0, x.L("diag-log-skipped", skip));
            }
            catch (Exception e) { error = x.L("diag-log-error", e.Message); }
            return result;
        }

        static void Log(Ctx x, bool heading)
        {
            EmcReport r = x.R;
            if (heading) r.Section(x.L("diag-sec-log"));
            List<string> lines = LogLines(x, heading ? 30 : 8, out string error);
            if (error != null) { x.Note(error, 0); return; }
            if (lines.Count == 0) { x.Ok(x.L("diag-log-clean")); return; }
            x.Warn(x.L("diag-log-found", lines.Count));
            foreach (string l in lines) r.Add(x.Compact ? l : Explain(x, l), EmcLineStyle.Text, 1);
            if (!heading) r.Add(Link(ActionLog, "", x.L("diag-q-log")));
            x.Fix(x.L("diag-log-fix"));
        }

        /// <summary>Adds a plain-words meaning after log lines the mod knows.</summary>
        static string Explain(Ctx x, string line)
        {
            string low = line.ToLowerInvariant();
            if (low.Contains("emc mapping failed")) return line + "\n      " + x.L("diag-log-meaning-mapfailed");
            if (low.Contains("could not read modconfig")) return line + "\n      " + x.L("diag-log-meaning-config");
            if (low.Contains("harmony") || low.Contains("could not patch")) return line + "\n      " + x.L("diag-log-meaning-patch");
            if (low.Contains("cache")) return line + "\n      " + x.L("diag-log-meaning-cache");
            if (low.Contains("could not write")) return line + "\n      " + x.L("diag-log-meaning-write");
            if (low.Contains("custom recipe") || low.Contains("customrecipe")) return line + "\n      " + x.L("diag-log-meaning-recipe");
            return line;
        }

        /// <summary>A log line whose own level is Error, Warning or Fatal (not one that merely quotes "[Error]").</summary>
        static readonly Regex LogLevel = new Regex(@"^\S+ \S+ \[(Error|Warning|Fatal)\]", RegexOptions.Compiled);
        static readonly Regex Ip = new Regex(@"\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b", RegexOptions.Compiled);
        static readonly Regex Uid = new Regex(@"\b(?=[A-Za-z0-9+/_-]*\d)(?=[A-Za-z0-9+/_-]*[a-z])(?=[A-Za-z0-9+/_-]*[A-Z])[A-Za-z0-9+/_-]{20,}\b", RegexOptions.Compiled);
        static readonly Regex HomePath = new Regex(@"(/home/|/Users/|[A-Z]:\\Users\\)[^/\\\s]+", RegexOptions.Compiled);

        /// <summary>Addresses, player ids and user folders taken out of a line before it goes into a report someone may post.</summary>
        static string Anonymise(string line)
        {
            line = Ip.Replace(line, "<address>");
            line = Uid.Replace(line, "<id>");
            line = HomePath.Replace(line, "$1<user>");
            return line;
        }

        // ---------------------------------------------------------------- 5: other mods changing the same things

        sealed class ModPatches { public string Domain; public int Total, Recipes, Processing, EmcValue, Ours, Types; public List<string> Examples = new List<string>(); }

        static List<ModPatches> OtherModsPatches(Ctx x)
        {
            var byDomain = new Dictionary<string, ModPatches>();
            List<IAsset> assets;
            try { assets = x.Sapi.Assets.GetMany("patches/", null, true); }
            catch (Exception e) { x.Sapi.Logger.Warning("Vintage-E: could not read the loaded patches: {0}", e.Message); return new List<ModPatches>(); }
            foreach (IAsset asset in assets)
            {
                string domain = asset.Location.Domain;
                if (domain == "game" || domain == "survival" || domain == "vintagee") continue;
                JArray patches;
                try
                {
                    JToken tok = JToken.Parse(asset.ToText());
                    patches = tok as JArray ?? new JArray(tok);
                }
                catch { continue; }
                if (!byDomain.TryGetValue(domain, out ModPatches m)) byDomain[domain] = m = new ModPatches { Domain = domain };
                foreach (JToken p in patches)
                {
                    if (!(p is JObject po)) continue;
                    m.Total++;
                    string file = ((string)po["file"] ?? "").ToLowerInvariant();
                    string path = ((string)po["path"] ?? "").ToLowerInvariant();
                    string value = po["value"]?.ToString()?.ToLowerInvariant() ?? "";
                    bool hit = false;
                    if (file.Contains("recipes/")) { m.Recipes++; hit = true; }
                    if (path.Contains("processedstack") || path.Contains("groundstoredprocess") || path.Contains("squeez") || path.Contains("combustible") || path.Contains("transitionable") || value.Contains("processedstack") || value.Contains("groundstoredprocessable")) { m.Processing++; hit = true; }
                    if (path.Contains("emcvalue") || value.Contains("emcvalue")) { m.EmcValue++; hit = true; }
                    if (file.StartsWith("vintagee:", StringComparison.Ordinal)) { m.Ours++; hit = true; }
                    if ((file.Contains("itemtypes/") || file.Contains("blocktypes/")) && !hit) m.Types++;
                    if (hit && m.Examples.Count < 3) m.Examples.Add(asset.Location.Path.Substring(asset.Location.Path.LastIndexOf('/') + 1) + " → " + ((string)po["file"] ?? "?"));
                }
            }
            return byDomain.Values.Where(m => m.Total > 0).OrderByDescending(m => m.Recipes + m.Processing + m.EmcValue + m.Ours).ThenBy(m => m.Domain, StringComparer.Ordinal).ToList();
        }

        static void Patches(Ctx x, bool heading)
        {
            EmcReport r = x.R;
            if (heading) r.Section(x.L("diag-sec-patches"));
            var mods = OtherModsPatches(x);
            var relevant = mods.Where(m => m.Recipes + m.Processing + m.EmcValue + m.Ours > 0).ToList();
            var otherFiles = x.Mod.Rules.LoadedFiles.Where(f => !f.StartsWith("vintagee:", StringComparison.Ordinal)).ToList();
            if (relevant.Count == 0) { x.Ok(x.L("diag-patches-none", mods.Count)); if (otherFiles.Count > 0) x.Note(x.L("diag-patches-valuefiles", string.Join(", ", otherFiles))); return; }
            x.Info(x.L("diag-patches-found", relevant.Count));
            if (otherFiles.Count > 0) x.Note(x.L("diag-patches-valuefiles", string.Join(", ", otherFiles)));
            foreach (ModPatches m in relevant.Take(heading ? 30 : 8))
            {
                var parts = new List<string>();
                if (m.Recipes > 0) parts.Add(x.L("diag-patches-recipes", m.Recipes));
                if (m.Processing > 0) parts.Add(x.L("diag-patches-processing", m.Processing));
                if (m.EmcValue > 0) parts.Add(x.L("diag-patches-emcvalue", m.EmcValue));
                if (m.Ours > 0) parts.Add(x.L("diag-patches-ours", m.Ours));
                r.Add(Link(ActionMod, m.Domain, ModName(x, m.Domain)) + ": " + string.Join(", ", parts), m.Ours > 0 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                if (heading && !x.Compact) foreach (string ex in m.Examples) x.Note(ex, 2);
            }
            x.Explain(x.L("diag-patches-why"));
            if (!heading) r.Add(Link(ActionPatches, "", x.L("diag-q-patches")));
        }

        // ---------------------------------------------------------------- 6: values out of line with their family

        static void Outliers(Ctx x, bool heading)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            if (heading) r.Section(x.L("diag-sec-outliers"));
            var families = report.Values.Where(kv => kv.Key?.Code != null && kv.Value > 0)
                .GroupBy(kv => kv.Key.Code.Domain + ":" + kv.Key.Code.Path.Split('-')[0])
                .Where(g => g.Count() >= 4);
            var found = new List<(CollectibleObject c, double v, double median, string origin)>();
            foreach (var g in families)
            {
                var sorted = g.Select(kv => kv.Value).OrderBy(v => v).ToList();
                double median = sorted[sorted.Count / 2];
                if (median <= 0) continue;
                foreach (var kv in g)
                {
                    double ratio = kv.Value / median;
                    if (ratio < 8 && ratio > 1.0 / 8) continue;
                    report.Origins.TryGetValue(kv.Key, out string origin);
                    // Only values taken from a recipe with several ingredients are suspect: "1 crown -> 20 gold bits" is the item's own
                    // worth, but "100 zinc sulfate + 1 fish -> taxidermy" prices the sulfate by what the borax it replaces costs.
                    if (origin == null || !origin.StartsWith("worked backwards", StringComparison.Ordinal) || !origin.Contains(" + ")) continue;
                    found.Add((kv.Key, kv.Value, median, origin));
                }
            }
            found = found.OrderByDescending(f => Math.Max(f.v / f.median, f.median / f.v)).ToList();
            if (found.Count == 0) { x.Ok(x.L("diag-outliers-none")); return; }
            x.Warn(x.L("diag-outliers-found", found.Count));
            x.Explain(x.L("diag-outliers-help"));
            foreach (var f in found.Take(heading ? 25 : 6))
            {
                if (x.Compact) r.Add(EmcReport.Ref(f.c) + " " + F(f.v) + " " + x.L("diag-outliers-vs", F(f.median)) + " <- " + f.origin, EmcLineStyle.Text, 1);
                else
                {
                    r.Add(EmcReport.Ref(f.c) + " " + F(f.v) + " " + x.L("diag-outliers-vs", F(f.median)), EmcLineStyle.Text, 1);
                    x.Note(Linkify(x, f.origin), 2);
                }
            }
            if (found.Count > (heading ? 25 : 6)) x.Note(x.L("diag-more", found.Count - (heading ? 25 : 6)));
            x.Fix(x.L("diag-outliers-fix"));
            if (!heading) r.Add(Link(ActionOutliers, "", x.L("diag-q-outliers")));
            r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
        }

        // ---------------------------------------------------------------- 7: a mapping without other mods

        static bool BaseDomain(CollectibleObject c) => c?.Code != null && (c.Code.Domain == "game" || c.Code.Domain == "vintagee");

        static bool BaseOnly(Conversion conv)
        {
            if (conv.Output != null && !BaseDomain(conv.Output)) return false;
            if (conv.JointOutputs != null && conv.JointOutputs.Any(o => !BaseDomain(o.Key))) return false;
            foreach (ConversionInput input in conv.Inputs)
            {
                if (input.Choices.Any(c => !BaseDomain(c)) && input.Choices.All(c => !BaseDomain(c))) return false;
            }
            if (conv.Returned != null && conv.Returned.Any(o => !BaseDomain(o.Key))) return false;
            return true;
        }

        static void Clean(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-clean"));
            r.Add(x.L("diag-clean-help"), EmcLineStyle.Note);
            var otherMods = x.Sapi.ModLoader.Mods.Where(m => m.Info?.ModID != null && m.Info.ModID != "game" && m.Info.ModID != "survival" && m.Info.ModID != "creative" && m.Info.ModID != "vintagee" && x.Sapi.ModLoader.IsModEnabled(m.Info.ModID)).ToList();
            if (x.Index == null || !x.Index.All.Any(c => !BaseDomain(c)))
            {
                x.Ok(x.L("diag-clean-nomods"));
                Back(x);
                return;
            }
            EmcMappingReport clean;
            try
            {
                var mapper = new EmcMapper(x.Sapi, x.Mod.Config, x.Mod.Rules, x.Sapi.Logger) { ConversionFilter = BaseOnly };
                clean = mapper.Run();
            }
            catch (Exception e)
            {
                x.Warn(x.L("diag-clean-failed", e.Message));
                Back(x);
                return;
            }
            EmcMappingReport live = x.Report;
            var changed = new List<(CollectibleObject c, double was, double now, string culprit)>();
            foreach (CollectibleObject c in x.Index.All.Where(BaseDomain))
            {
                bool inClean = clean.Values.TryGetValue(c, out double was), inLive = live.Values.TryGetValue(c, out double now);
                if (!inClean && !inLive) continue;
                if (inClean && inLive && Math.Abs(was - now) < 1e-6) continue;
                changed.Add((c, inClean ? was : 0, inLive ? now : 0, Culprit(live, c)));
            }
            if (changed.Count == 0)
            {
                x.Ok(x.L("diag-clean-same", otherMods.Count));
                Back(x);
                return;
            }
            x.Warn(x.L("diag-clean-changed", changed.Count, otherMods.Count));
            foreach (var g in changed.GroupBy(ch => ch.culprit ?? "?").OrderByDescending(g => g.Count()))
            {
                r.Section(g.Key == "?" ? x.L("diag-clean-unknown", g.Count()) : x.L("diag-clean-bymod", ModName(x, g.Key), g.Count()));
                foreach (var ch in g.OrderByDescending(ch => Math.Abs(Math.Log((ch.now > 0 ? ch.now : 1e-6) / (ch.was > 0 ? ch.was : 1e-6)))).Take(20))
                {
                    string line = EmcReport.Ref(ch.c) + ": " + (ch.was > 0 ? F(ch.was) : x.L("diag-none")) + " → " + (ch.now > 0 ? F(ch.now) : x.L("diag-none"));
                    r.Add(line, ch.now <= 0 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                    if (live.Origins.TryGetValue(ch.c, out string o)) x.Note(Linkify(x, o), 2);
                }
                if (g.Count() > 20) x.Note(x.L("diag-more", g.Count() - 20));
            }
            r.Section(x.L("diag-sec-next"));
            x.Fix(x.L("diag-clean-fix"));
            r.Add(Link(ActionOutliers, "", x.L("diag-q-outliers")));
            r.Add(Link(ActionPatches, "", x.L("diag-q-patches")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        /// <summary>Which other mod's recipe or item the live value of a base-game item comes through, if any.</summary>
        static string Culprit(EmcMappingReport live, CollectibleObject c)
        {
            var seen = new HashSet<CollectibleObject>();
            var queue = new Queue<CollectibleObject>();
            queue.Enqueue(c);
            int steps = 0;
            while (queue.Count > 0 && steps++ < 200)
            {
                CollectibleObject cur = queue.Dequeue();
                if (!seen.Add(cur)) continue;
                if (!live.Via.TryGetValue(cur, out Conversion conv) || conv == null) continue;
                foreach (CollectibleObject o in Involved(conv))
                {
                    if (!BaseDomain(o)) return o.Code.Domain;
                    if (o != cur) queue.Enqueue(o);
                }
            }
            return null;
        }

        static IEnumerable<CollectibleObject> Involved(Conversion conv)
        {
            if (conv.Output != null) yield return conv.Output;
            if (conv.JointOutputs != null) foreach (var o in conv.JointOutputs) yield return o.Key;
            foreach (ConversionInput i in conv.Inputs) foreach (CollectibleObject ch in i.Choices) yield return ch;
            if (conv.Returned != null) foreach (var o in conv.Returned) yield return o.Key;
        }

        // ---------------------------------------------------------------- 8: a player

        static void Players(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-players"));
            r.Add(x.L("diag-players-help"), EmcLineStyle.Note);
            foreach (IPlayer p in x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>())
            {
                r.Add(Link(ActionPlayer, p.PlayerName, p.PlayerName) + ": " + x.L("diag-players-line", F(PlayerEmc.GetEmc(p)), PlayerEmc.GetKnown(p).Length, p.WorldData?.CurrentGameMode.ToString() ?? "?"));
            }
            Back(x);
        }

        static void PlayerReport(Ctx x, string name)
        {
            EmcReport r = x.R;
            IPlayer p = (x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>()).FirstOrDefault(o => string.Equals(o.PlayerName, name, StringComparison.OrdinalIgnoreCase));
            r.Section(x.L("diag-sec-player", name));
            if (p == null)
            {
                x.Warn(x.L("diag-player-offline", name));
                x.Note(x.L("diag-player-offline-why"), 1);
                r.Add(Link(ActionPlayers, "", x.L("diag-q-players")));
                Back(x);
                return;
            }
            VintageEConfig cfg = x.Mod.Config;
            string[] known = PlayerEmc.GetKnown(p);
            int exact = PlayerEmc.CountExact(p);
            r.Add(x.L("diag-player-emc", F(PlayerEmc.GetEmc(p))), EmcLineStyle.Text);
            r.Add(x.L("diag-player-known", known.Length, exact, cfg.MaxExactKnowledge), exact >= cfg.MaxExactKnowledge ? EmcLineStyle.Warn : EmcLineStyle.Text);
            if (exact >= cfg.MaxExactKnowledge) x.Fix(x.L("diag-player-known-fix"));
            r.Add(x.L("diag-player-mode", p.WorldData?.CurrentGameMode.ToString() ?? "?"), EmcLineStyle.Text);
            if (p.WorldData?.CurrentGameMode == EnumGameMode.Creative) x.Note(x.L("diag-player-creative"), 1);
            bool admin = (p as IServerPlayer)?.HasPrivilege(Privilege.controlserver) ?? false;
            r.Add(admin ? x.L("diag-player-admin") : x.L("diag-player-notadmin"), EmcLineStyle.Note);
            if (x.Held?.Collectible?.Code != null)
            {
                string key = PlayerEmc.KeyOf(x.Held.Collectible);
                bool knows = PlayerEmc.Knows(p, key);
                r.Add(knows ? x.L("diag-player-knowsheld", EmcExplain.NameOf(x.Held.Collectible)) : x.L("diag-player-notknowsheld", EmcExplain.NameOf(x.Held.Collectible)), knows ? EmcLineStyle.Total : EmcLineStyle.Warn);
                if (!knows) x.Fix(x.L("diag-player-notknowsheld-fix", p.PlayerName));
            }
            x.Note(x.L("diag-player-clientnote"), 0);
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionPlayers, "", x.L("diag-q-players")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- 9: one-click fixes

        /// <summary>Does what a fix link asks (admins only; the caller checked). Returns one line saying what happened.</summary>
        public static string ApplyFix(VintageEModSystem mod, IServerPlayer player, string what, string target, string lang)
        {
            string L(string key, params object[] args) => Lang.GetL(lang, "vintagee:" + key, args);
            try
            {
                switch (what)
                {
                    case "resend":
                        mod.ResendTable(player);
                        return L("diag-done-resend");
                    case "recalc":
                        mod.RequestRemap("/emc diagnose");
                        return L("diag-done-recalc");
                    case "allow":
                        mod.SetBlacklistedPattern(target, false);
                        mod.RequestRemap("/emc diagnose allow");
                        return L("diag-done-allow", target);
                    case "unpin":
                        mod.Config.FixedValues.Remove(target);
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose unpin");
                        return L("diag-done-unpin", target);
                    case "removekey":
                        {
                            VintageEConfig c = mod.Config;
                            int n = (c.BaseValues.Remove(target) ? 1 : 0) + (c.FixedValues.Remove(target) ? 1 : 0) + c.Blacklist.RemoveAll(k => k == target) + c.Allow.RemoveAll(k => k == target)
                                + c.NoTransmute.RemoveAll(k => k == target) + c.GrayList.RemoveAll(k => k == target) + c.FreeIngredients.RemoveAll(k => k == target);
                            mod.SaveConfig();
                            if (n > 0) mod.RequestRemap("/emc diagnose removekey");
                            return n > 0 ? L("diag-done-removekey", target) : L("diag-done-nothing", target);
                        }
                    case "removekeys":
                        {
                            // Every settings entry that matches nothing, in one go.
                            VintageEConfig c = mod.Config;
                            var index = new CollectibleIndex(mod.Sapi.World.Collectibles);
                            bool Dead(string k) => !string.IsNullOrWhiteSpace(k) && !index.Match(k).Any();
                            var dead = c.BaseValues.Keys.Concat(c.FixedValues.Keys).Concat(c.Blacklist).Concat(c.Allow).Concat(c.NoTransmute).Concat(c.GrayList).Concat(c.FreeIngredients).Where(Dead).Distinct().ToList();
                            foreach (string k in dead)
                            {
                                c.BaseValues.Remove(k); c.FixedValues.Remove(k);
                                c.Blacklist.RemoveAll(e => e == k); c.Allow.RemoveAll(e => e == k); c.NoTransmute.RemoveAll(e => e == k); c.GrayList.RemoveAll(e => e == k); c.FreeIngredients.RemoveAll(e => e == k);
                            }
                            mod.SaveConfig();
                            if (dead.Count > 0) mod.RequestRemap("/emc diagnose removekeys");
                            return dead.Count > 0 ? L("diag-done-removekeys", dead.Count) : L("diag-done-nothing", "settings");
                        }
                    case "ungray":
                        mod.Config.GrayList.RemoveAll(k => k == target);
                        mod.SaveConfig();
                        mod.RequestRemap("/emc diagnose ungray");
                        return L("diag-done-ungray", target);
                    case "notransmute":
                        mod.SetNoTransmute(new[] { target }, false);
                        return L("diag-done-notransmute", target);
                    case "compat":
                        {
                            string msg = mod.SetCompatDisabled(target, false, lang, out bool error);
                            return (error ? "⚠ " : "") + msg;
                        }
                    case "setting":
                        {
                            int eq = target.IndexOf('=');
                            if (eq <= 0) return L("diag-done-nothing", target);
                            string msg = ConfigureCommand.SetFromText(mod, target.Substring(0, eq), target.Substring(eq + 1), lang);
                            return msg ?? L("diag-done-setting", target.Substring(0, eq), target.Substring(eq + 1));
                        }
                    case "pin":
                        {
                            int eq = target.IndexOf('=');
                            if (eq <= 0 || !double.TryParse(target.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return L("diag-done-nothing", target);
                            string key = target.Substring(0, eq);
                            string lesson = Lesson(mod, key, lang);
                            mod.Config.FixedValues[key] = v;
                            if (v > 0) mod.Config.Blacklist.Remove(key);
                            mod.SaveConfig();
                            mod.RequestRemap("/emc diagnose pin");
                            return L("diag-done-pin", key, EmcFormat.Format(v)) + lesson;
                        }
                    case "pinall":
                        {
                            var x = new Ctx { Mod = mod, Sapi = mod.Sapi, LangCode = lang, R = new EmcReport(), Report = mod.LastReport, Index = new CollectibleIndex(mod.Sapi.World.Collectibles) };
                            if (x.Report == null) return L("diag-notmapped");
                            int n = PinSuggested(mod, x, target, out int skipped);
                            return L("diag-done-pinall", n, skipped);
                        }
                    case "contentsmode":
                        {
                            int eq = target.IndexOf('=');
                            if (eq <= 0 || !StackDataTable.TryParseMode(target.Substring(eq + 1), out StackDataMode mode)) return L("diag-done-nothing", target);
                            var items = new CollectibleIndex(mod.Sapi.World.Collectibles).Match(target.Substring(0, eq)).ToList();
                            if (items.Count == 0) return L("diag-done-nothing", target);
                            mod.SetStackDataModes(items, mode);
                            return L("diag-done-contentsmode", target.Substring(0, eq), StackDataTable.ModeName(mode));
                        }
                    case "safe":
                        return ApplySafeFixes(mod, player, lang);
                    case "clearwatch":
                        ClearRefusals();
                        return L("diag-done-clearwatch");
                    case "learn":
                        {
                            CollectibleObject c = PlayerEmc.Resolve(mod.Sapi.World, target);
                            if (c == null) return L("diag-done-nothing", target);
                            return PlayerEmc.Learn(player, target) ? L("diag-done-learn", EmcExplain.NameOf(c)) : L("diag-done-learn-already", EmcExplain.NameOf(c));
                        }
                    default:
                        return L("diag-done-nothing", what);
                }
            }
            catch (Exception e)
            {
                mod.Sapi.Logger.Error("Vintage-E: diagnose fix '{0} {1}' failed: {2}", what, target, e);
                return "⚠ " + L("diag-done-failed", e.Message);
            }
        }

        // ---------------------------------------------------------------- 10: a bug report

        static readonly Regex OutlierLine = new Regex(@"^(\s*)(.+?) \((\S+:\S+)\) (\S+) \(its family's middle value is (\S+)\) ← worked backwards from (.+)$", RegexOptions.Compiled);
        static readonly Regex CountLine = new Regex(@"^  ([^:]+(?:: [^:]+)?): (\d+)$", RegexOptions.Compiled);
        static readonly Regex FamilyLine = new Regex(@"^    (\S+) \((\d+)\): .+ \((\S+:\S+)\)$", RegexOptions.Compiled);

        /// <summary>
        /// The same facts in fewer lines: outliers as code, value, family middle and source; runs of "name: count" and
        /// family lines joined on one line; the long source names shortened. Nothing is dropped.
        /// </summary>
        static List<string> Condense(List<string> lines)
        {
            var result = new List<string>();
            var run = new List<string>();
            string runKind = null;
            void Flush()
            {
                if (run.Count == 0) return;
                result.Add("  " + string.Join(runKind == "family" ? "; " : ", ", run));
                run.Clear(); runKind = null;
            }
            foreach (string raw in lines)
            {
                string line = raw.Replace("RecipeRegistrySystem.", "")
                    .Replace("patches to how items are processed (ground storage, squeezing, burning, drying)", "processing patches")
                    .Replace(" -> ", " → ").Replace(" <- ", " ← ");
                Match m = CountLine.Match(line);
                if (m.Success && !line.Contains("→"))
                {
                    if (runKind != "count") Flush();
                    runKind = "count"; run.Add(m.Groups[1].Value + " " + m.Groups[2].Value);
                    continue;
                }
                m = FamilyLine.Match(line);
                if (m.Success)
                {
                    if (runKind != "family") Flush();
                    runKind = "family"; run.Add(m.Groups[1].Value + " " + m.Groups[2].Value + " e.g. " + m.Groups[3].Value);
                    continue;
                }
                Flush();
                m = OutlierLine.Match(line);
                if (m.Success)
                {
                    result.Add(m.Groups[1].Value + m.Groups[3].Value + " " + m.Groups[4].Value + " (family " + m.Groups[5].Value + ") ← " + m.Groups[6].Value);
                    continue;
                }
                if (line.Trim() == "The biggest families:") continue;
                result.Add(line);
            }
            Flush();
            return result;
        }

        static void BugReport(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-bugreport"));
            r.Add(x.L("diag-bugreport-help"), EmcLineStyle.Note);
            var sb = new StringBuilder();
            sb.Append("Vintage-E ").Append(x.Mod.Version ?? "?").Append(" on Vintage Story ").Append(GameVersion.LongGameVersion)
              .Append(", ").Append(x.Sapi.Server.IsDedicated ? "dedicated server" : "single player / hosted")
              .Append(", ").Append((x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>()).Length).Append(" online, ")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');

            // The mod list, wrapped, so 60 mods take a few lines instead of 60.
            var mods = x.Sapi.ModLoader.Mods.Where(m => m.Info?.ModID != null && m.Info.ModID != "game" && m.Info.ModID != "survival" && m.Info.ModID != "creative")
                .OrderBy(m => m.Info.ModID, StringComparer.Ordinal)
                .Select(m => m.Info.ModID + " " + m.Info.Version + (x.Sapi.ModLoader.IsModEnabled(m.Info.ModID) ? "" : " (disabled)")).ToList();
            sb.Append("\n== Mods (").Append(mods.Count).Append(") ==\n");
            int col = 0;
            foreach (string m in mods)
            {
                if (col > 0 && col + m.Length + 2 > 110) { sb.Append('\n'); col = 0; }
                if (col > 0) { sb.Append(", "); col += 2; }
                sb.Append(m); col += m.Length;
            }
            sb.Append('\n');

            // The check-up, facts only: no advice, no explanations, one line per patching mod and per outlier.
            sb.Append("\n== Diagnosis ==\n");
            var inner = new EmcReport { Kind = EmcReport.DiagnoseKind };
            var ix = new Ctx { Mod = x.Mod, Sapi = x.Sapi, LangCode = "en", R = inner, Report = x.Report, Index = x.Index, Player = null, Held = x.Held, ThenAction = ActionBugReport, Compact = true };
            if (MappingState(ix, true))
            {
                Mapping(ix, true);
                Patches(ix, true);
                Outliers(ix, true);
            }
            // Lines that are only a link ("see the deeper checks") mean nothing on paper.
            inner.Lines.RemoveAll(l => EmcReport.ActionToken.Replace(l.Text, "").Trim().Length == 0);
            var diag = new List<string>();
            foreach (string line in inner.PlainText(x.Sapi.World, labelsOnly: true).Split('\n'))
            {
                // Fix links read as bare labels once the token is gone; the verdict repeats what's above.
                string t = line.TrimEnd();
                if (t.Length == 0 || t.Contains("worth fixing") || t.Contains("Nothing wrong with the mapping")) continue;
                diag.Add(t);
            }
            foreach (string t in Condense(diag)) sb.Append(t).Append('\n');

            sb.Append("\n== Server log (Vintage-E errors and warnings, addresses and ids removed) ==\n");
            List<string> logLines = LogLines(x, 40, out string err);
            if (logLines.Count == 0 && err == null) sb.Append("(none)\n");
            foreach (string l in logLines) sb.Append(l).Append('\n');
            if (err != null) sb.Append(err).Append('\n');

            // Only settings that differ from the defaults; the rest is implied.
            sb.Append("\n== ModConfig/").Append(VintageEModSystem.ConfigFile).Append(" (settings that differ from the defaults) ==\n");
            try
            {
                var live = JObject.FromObject(x.Mod.Config);
                var defaults = JObject.FromObject(new VintageEConfig());
                var changed = new JObject();
                foreach (JProperty prop in live.Properties())
                {
                    JToken def = defaults[prop.Name];
                    if (def == null || !JToken.DeepEquals(def, prop.Value)) changed[prop.Name] = prop.Value;
                }
                // One line: Name=value, …
                sb.Append(changed.Count == 0 ? "(all defaults)" : string.Join(", ", changed.Properties().Select(p => p.Name + "=" + p.Value.ToString(Newtonsoft.Json.Formatting.None))));
            }
            catch (Exception e) { sb.Append("(could not serialize: ").Append(e.Message).Append(')'); }
            sb.Append('\n');

            string text = HomePath.Replace(Ip.Replace(sb.ToString(), "<address>"), "$1<user>");
            text = Regex.Replace(text, @"\n{3,}", "\n\n"); text = Regex.Replace(text, @"== Diagnosis ==\n+== Is EMC mapped at all\? ==", "== Diagnosis ==");
            string file = null;
            try
            {
                file = Path.Combine(VintageEModSystem.DataFolder(x.Sapi), "bug-report.txt");
                File.WriteAllText(file, text);
            }
            catch (Exception e) { x.Sapi.Logger.Warning("Vintage-E: could not write the bug report: {0}", e.Message); }
            if (file != null) x.Ok(x.L("diag-bugreport-saved", file));
            x.Note(x.L("diag-bugreport-send"), 0);
            r.Section(x.L("diag-bugreport-text"));
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("== ", StringComparison.Ordinal)) r.Add(line.Trim('=', ' '), EmcLineStyle.Heading);
                else r.Add(line.Replace("[[", "").Replace("]]", "").Replace("{{", "").Replace("}}", ""), EmcLineStyle.Note);
            }
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- troubleshooting: an item shows no EMC value

        /// <summary>
        /// The README's troubleshooting list as a guided walk: each step says what to look at and links to the report or
        /// fix that does it, using what's known already (the held item, the player's game).
        /// </summary>
        static void NoValueGuide(Ctx x, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            CollectibleObject held = x.Held?.Collectible;
            r.Section(x.L("diag-sec-novalue"));
            r.Add(x.L("diag-novalue-help"), EmcLineStyle.Note);

            // 1. Does the tooltip say anything at all?
            x.Info(x.L("diag-novalue-1"));
            x.Note(x.L("diag-novalue-1-text"));
            if (x.Player == null) x.Note(x.L("diag-client-console"));
            else if (client == null || !client.HasClientInfo) r.Add(Link(ActionClient, "", x.L("diag-q-client")), EmcLineStyle.Text, 1);
            else
            {
                bool ok = client.ClientTableCount > 0 && client.ClientTableHash == x.Mod.Emc.Fingerprint() && client.ClientTableCount == x.Mod.Emc.Count
                          && (string.IsNullOrEmpty(client.ClientVersion) || client.ClientVersion == (x.Mod.Version ?? client.ClientVersion));
                x.Note(ok ? x.L("diag-novalue-1-ok") : x.L("diag-novalue-1-bad"));
                if (!ok) { r.Add(FixLink(x, "resend", "", x.L("diag-fix-resend")), EmcLineStyle.Text, 1); r.Add(Link(ActionClient, "", x.L("diag-q-client")), EmcLineStyle.Text, 1); }
            }

            // 2. Ask the mod why.
            x.Info(x.L("diag-novalue-2"));
            x.Note(x.L("diag-novalue-2-text"));
            if (held?.Code != null)
            {
                bool valued = x.Report != null && x.Report.Values.ContainsKey(held);
                x.Note(valued ? x.L("diag-novalue-2-heldvalued", EmcExplain.NameOf(held), F(x.V(held))) : x.L("diag-novalue-2-heldnone", EmcExplain.NameOf(held)));
                r.Add(Link(ActionHand, "", x.L("diag-q-held", EmcExplain.NameOf(held))), EmcLineStyle.Text, 1);
            }
            else r.Add(Link(ActionHand, "", x.L("diag-q-item")), EmcLineStyle.Text, 1);
            foreach (string reason in new[] { "blacklisted", "norecipe", "waiting", "loop", "gray" }) x.Note("- " + x.L("diag-novalue-2-" + reason));

            // 3. The exact stack, or some variants.
            x.Info(x.L("diag-novalue-3"));
            x.Note(x.L("diag-novalue-3-text"));
            r.Add(Link(ActionSymptom, "somevariants", x.L("diag-sym-somevariants")), EmcLineStyle.Text, 1);
            r.Add(Link(ActionSporadic, "", x.L("diag-q-sporadic")), EmcLineStyle.Text, 1);

            // 4. A whole mod.
            x.Info(x.L("diag-novalue-4"));
            x.Note(x.L("diag-novalue-4-text"));
            if (held?.Code != null && held.Code.Domain != "game" && held.Code.Domain != "vintagee") r.Add(Link(ActionMod, held.Code.Domain, x.L("diag-q-thismod", ModName(x, held.Code.Domain))), EmcLineStyle.Text, 1);
            r.Add(Link(ActionMods, "", x.L("diag-q-mods")), EmcLineStyle.Text, 1);

            // 5. It had a value before.
            x.Info(x.L("diag-novalue-5"));
            x.Note(x.L("diag-novalue-5-text"));
            r.Add(Link(ActionChanges, "", x.L("diag-q-changes")), EmcLineStyle.Text, 1);
            r.Add(FixLink(x, "recalc", "", x.L("diag-fix-recalc")), EmcLineStyle.Text, 1);

            // 6. Knowledge, not value.
            x.Info(x.L("diag-novalue-6"));
            x.Note(x.L("diag-novalue-6-text", x.Mod.Config.MaxExactKnowledge));
            if (held?.Code != null && x.Player != null && x.V(held) > 0) r.Add(FixLink(x, "learn", PlayerEmc.KeyOf(held), x.L("diag-fix-learn", EmcExplain.NameOf(held))), EmcLineStyle.Text, 1);
            r.Add(Link(ActionWatch, "", x.L("diag-q-watch")), EmcLineStyle.Text, 1);

            // 7. Still stuck.
            x.Info(x.L("diag-novalue-7"));
            x.Note(x.L("diag-novalue-7-text"));
            r.Add(Link(ActionBugReport, "", x.L("diag-q-bugreport")), EmcLineStyle.Text, 1);

            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- 11: symptoms

        static readonly string[] Symptoms = { "tooltipnoburn", "noclientvalues", "aftermod", "afterrestart", "onlyme", "somevariants", "wrongvalue", "containers", "cantcreate", "nothing" };

        static void Symptom(Ctx x, string id, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            id = (id ?? "").ToLowerInvariant();
            if (!Symptoms.Contains(id))
            {
                r.Section(x.L("diag-sec-symptoms"));
                r.Add(x.L("diag-symptoms-help"), EmcLineStyle.Note);
                foreach (string s in Symptoms) r.Add(Link(ActionSymptom, s, x.L("diag-sym-" + s)));
                r.Section(x.L("diag-sec-next"));
                r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
                return;
            }
            r.Section(x.L("diag-sym-" + id));
            r.Add(x.L("diag-sym-" + id + "-means"), EmcLineStyle.Note);
            CollectibleObject held = x.Held?.Collectible;
            switch (id)
            {
                case "tooltipnoburn":
                    r.Add(Link(ActionWatch, "", x.L("diag-q-watch")));
                    if (held != null)
                    {
                        if (x.Mod.Emc.IsNoTransmute(held)) { x.Warn(x.L("diag-item-notransmute")); r.Add(FixLink(x, "notransmute", VintageEModSystem.ConfigKey(held), x.L("diag-fix-notransmute"))); }
                        else x.Ok(x.L("diag-sym-notonlist"));
                        if (x.Mod.Emc.StackData.CarriesData(held) && x.Mod.Emc.StackData.ModeOf(held) == StackDataMode.Full && x.Mod.Config.ProtectContainersWithContents) x.Note(x.L("diag-stack-protected"), 0);
                        double each = x.Mod.Emc.GetValue(x.Held);
                        if (each <= 0 && x.Mod.Emc.GetValue(held) > 0) { x.Warn(x.L("diag-item-stackdiffers", F(each), F(x.Mod.Emc.GetValue(held)))); StackReasons(x, x.Held, x.Mod.Emc.GetValue(held), each); }
                    }
                    else x.Note(x.L("diag-item-holdit"), 0);
                    x.Note(x.L("diag-sym-tooltipnoburn-also"), 0);
                    r.Add(Link(ActionHand, "", x.L("diag-q-item")));
                    break;
                case "noclientvalues":
                    Client(x, client, false);
                    x.Note(x.L("diag-sym-noclientvalues-also"), 0);
                    break;
                case "aftermod":
                    Patches(x, false);
                    Log(x, false);
                    r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
                    r.Add(Link(ActionChanges, "", x.L("diag-q-changes")));
                    r.Add(Link(ActionMods, "", x.L("diag-q-mods")));
                    break;
                case "afterrestart":
                    x.Note(x.Report.FromCache ? x.L("diag-cache", x.Report.CachedAt ?? "?") : x.L("diag-sym-afterrestart-fresh"), 0);
                    Log(x, false);
                    r.Add(Link(ActionChanges, "", x.L("diag-q-changes")));
                    r.Add(FixLink(x, "recalc", "", x.L("diag-fix-recalc")));
                    break;
                case "onlyme":
                    if (x.Player != null) PlayerReportInline(x);
                    Client(x, client, false);
                    r.Add(Link(ActionPlayers, "", x.L("diag-q-players")));
                    break;
                case "somevariants":
                    if (held != null) Item(x, x.Held, true);
                    else { x.Note(x.L("diag-item-holdit"), 0); r.Add(Link(ActionHand, "", x.L("diag-q-item"))); }
                    x.Note(x.L("diag-sym-somevariants-also"), 0);
                    break;
                case "wrongvalue":
                    if (held != null) r.Add(x.L("diag-item-full") + " " + EmcReport.Ref(held));
                    Outliers(x, false);
                    r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
                    r.Add(Link(ActionChanges, "", x.L("diag-q-changes")));
                    break;
                case "containers":
                    r.Add(x.L("diag-sp-contents", x.Mod.Config.ProtectContainersWithContents ? x.L("diag-on") : x.L("diag-off")));
                    x.Note(x.L("diag-sp-contents-why"));
                    if (held != null && x.Mod.Emc.StackData.CarriesData(held)) StackReasons(x, x.Held, x.Mod.Emc.GetValue(held), x.Mod.Emc.GetValue(x.Held));
                    else x.Note(x.L("diag-sporadic-holdit"), 0);
                    break;
                case "cantcreate":
                    r.Add(Link(ActionWatch, "", x.L("diag-q-watch")));
                    if (x.Player != null) PlayerReportInline(x);
                    if (held != null)
                    {
                        if (x.Mod.Emc.IsNoTransmute(held)) x.Warn(x.L("diag-item-notransmute"));
                        if (x.Mod.Emc.GetValue(held) <= 0) r.Add(x.L("diag-held-novalue") + " " + Link(ActionItem, held.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Warn);
                    }
                    x.Note(x.L("diag-sym-cantcreate-also"), 0);
                    break;
                case "nothing":
                    MappingState(x, false);
                    Client(x, client, false);
                    Log(x, false);
                    r.Add(Link(ActionMapping, "", x.L("diag-q-mapping")));
                    break;
            }
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionSymptom, "", x.L("diag-q-symptoms")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        static void PlayerReportInline(Ctx x)
        {
            IPlayer p = x.Player;
            VintageEConfig cfg = x.Mod.Config;
            int exact = PlayerEmc.CountExact(p);
            x.R.Add(x.L("diag-player-known", PlayerEmc.GetKnown(p).Length, exact, cfg.MaxExactKnowledge), exact >= cfg.MaxExactKnowledge ? EmcLineStyle.Warn : EmcLineStyle.Text);
            if (x.Held?.Collectible?.Code != null)
            {
                string key = PlayerEmc.KeyOf(x.Held.Collectible);
                bool knows = PlayerEmc.Knows(p, key);
                x.R.Add(knows ? x.L("diag-held-known") : x.L("diag-held-unknown"), knows ? EmcLineStyle.Total : EmcLineStyle.Warn);
                if (!knows && x.Mod.Emc.GetValue(x.Held.Collectible) > 0) x.R.Add(FixLink(x, "learn", key, x.L("diag-fix-learn", EmcExplain.NameOf(x.Held.Collectible))));
            }
        }

        // ---------------------------------------------------------------- 12: what the last mapping changed

        static void Changes(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-changes"));
            Dictionary<string, double> prev = x.Mod.PreviousValues;
            if (prev == null)
            {
                x.Note(x.L("diag-changes-none"), 0);
                Back(x);
                return;
            }
            r.Add(x.L("diag-changes-help", x.Mod.PreviousValuesAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), x.Report.MappedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), EmcLineStyle.Note);
            ModListChanges(x);
            var now = x.Report.Values.Where(kv => kv.Key?.Code != null).ToDictionary(kv => PlayerEmc.KeyOf(kv.Key), kv => kv);
            var gained = new List<(CollectibleObject c, double v)>();
            var lost = new List<(string key, double v)>();
            var moved = new List<(CollectibleObject c, double was, double v)>();
            foreach (var kv in now)
            {
                if (!prev.TryGetValue(kv.Key, out double was)) gained.Add((kv.Value.Key, kv.Value.Value));
                else if (Math.Abs(was - kv.Value.Value) > 1e-6) moved.Add((kv.Value.Key, was, kv.Value.Value));
            }
            foreach (var kv in prev) if (!now.ContainsKey(kv.Key)) lost.Add((kv.Key, kv.Value));
            if (gained.Count + lost.Count + moved.Count == 0)
            {
                x.Ok(x.L("diag-changes-same"));
                Back(x);
                return;
            }
            r.Add(x.L("diag-changes-summary", gained.Count, lost.Count, moved.Count), lost.Count > 0 ? EmcLineStyle.Warn : EmcLineStyle.Total);
            if (lost.Count > 0)
            {
                r.Section(x.L("diag-changes-lost", lost.Count));
                foreach (var (key, v) in lost.OrderBy(l => l.key, StringComparer.Ordinal).Take(40))
                {
                    CollectibleObject c = PlayerEmc.Resolve(x.Sapi.World, key);
                    r.Add((c != null ? EmcReport.Ref(c) : key) + ": " + F(v) + " → " + x.L("diag-none") + (c != null ? "  " + Link(ActionItem, c.Code.ToString(), x.L("diag-why-link")) : ""), EmcLineStyle.Warn, 1);
                }
                if (lost.Count > 40) x.Note(x.L("diag-more", lost.Count - 40));
            }
            if (moved.Count > 0)
            {
                r.Section(x.L("diag-changes-moved", moved.Count));
                foreach (var g in moved.GroupBy(m => m.c.Code.Domain + ":" + m.c.Code.Path.Split('-')[0]).OrderByDescending(g => g.Count()).Take(25))
                {
                    var first = g.OrderByDescending(m => Math.Abs(Math.Log(m.v / Math.Max(m.was, 1e-6)))).First();
                    r.Add(g.Key + " (" + g.Count() + "): " + EmcReport.Ref(first.c) + " " + F(first.was) + " → " + F(first.v), EmcLineStyle.Text, 1);
                    if (x.Report.Origins.TryGetValue(first.c, out string o)) x.Note(Linkify(x, o), 2);
                }
            }
            if (gained.Count > 0)
            {
                r.Section(x.L("diag-changes-gained", gained.Count));
                foreach (var g in gained.GroupBy(m => m.c.Code.Domain + ":" + m.c.Code.Path.Split('-')[0]).OrderByDescending(g => g.Count()).Take(15))
                {
                    r.Add(g.Key + " (" + g.Count() + "): " + string.Join(", ", g.Take(3).Select(m => EmcReport.Ref(m.c) + " " + F(m.v))), EmcLineStyle.Text, 1);
                }
            }
            r.Section(x.L("diag-sec-next"));
            x.Note(x.L("diag-changes-why"), 0);
            r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }
    }
}
