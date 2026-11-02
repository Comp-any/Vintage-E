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
            ActionPlayer = "player", ActionPlayers = "players", ActionBugReport = "bugreport", ActionSymptom = "symptom", ActionChanges = "changes";

        static readonly string[] AllActions =
        {
            ActionItem, ActionHand, ActionMod, ActionMods, ActionSporadic, ActionMapping, ActionOverview, ActionClient, ActionLog, ActionPatches,
            ActionOutliers, ActionClean, ActionPlayer, ActionPlayers, ActionBugReport, ActionSymptom, ActionChanges
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
                x.Fix(x.L("diag-client-versiondiff-fix"));
            }
            else x.Ok(x.L("diag-client-version", client.ClientVersion));

            int serverCount = x.Mod.Emc.Count;
            long serverHash = x.Mod.Emc.Fingerprint();
            if (client.ClientTableCount == 0)
            {
                x.Warn(x.L("diag-client-notable"));
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
                    bool bad = line.Contains("[Error]") || line.Contains("[Fatal]") || line.Contains("[Warning]") || line.Contains("Exception");
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
            foreach (string l in lines) r.Add(Explain(x, l), EmcLineStyle.Text, 1);
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

        static readonly Regex Ip = new Regex(@"\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b", RegexOptions.Compiled);
        static readonly Regex Uid = new Regex(@"\b[A-Za-z0-9+/_-]{20,}\b", RegexOptions.Compiled);
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
            if (otherFiles.Count > 0) x.Note(x.L("diag-patches-valuefiles", string.Join(", ", otherFiles)), 0);
            if (relevant.Count == 0) { x.Ok(x.L("diag-patches-none", mods.Count)); return; }
            r.Add(x.L("diag-patches-found", relevant.Count), EmcLineStyle.Text);
            foreach (ModPatches m in relevant.Take(heading ? 30 : 8))
            {
                var parts = new List<string>();
                if (m.Recipes > 0) parts.Add(x.L("diag-patches-recipes", m.Recipes));
                if (m.Processing > 0) parts.Add(x.L("diag-patches-processing", m.Processing));
                if (m.EmcValue > 0) parts.Add(x.L("diag-patches-emcvalue", m.EmcValue));
                if (m.Ours > 0) parts.Add(x.L("diag-patches-ours", m.Ours));
                r.Add(Link(ActionMod, m.Domain, ModName(x, m.Domain)) + ": " + string.Join(", ", parts), m.Ours > 0 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                if (heading) foreach (string ex in m.Examples) x.Note(ex, 2);
            }
            x.Note(x.L("diag-patches-why"), 0);
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
                    // Variants that differ by design (sizes, metals, gems) are only suspicious when worked backwards from another recipe.
                    if (origin == null || !(origin.StartsWith("worked backwards", StringComparison.Ordinal) || origin.StartsWith("guessed", StringComparison.Ordinal))) continue;
                    found.Add((kv.Key, kv.Value, median, origin));
                }
            }
            found = found.OrderByDescending(f => Math.Max(f.v / f.median, f.median / f.v)).ToList();
            if (found.Count == 0) { x.Ok(x.L("diag-outliers-none")); return; }
            x.Warn(x.L("diag-outliers-found", found.Count));
            r.Add(x.L("diag-outliers-help"), EmcLineStyle.Note, 1);
            foreach (var f in found.Take(heading ? 25 : 6))
            {
                r.Add(EmcReport.Ref(f.c) + " " + F(f.v) + " " + x.L("diag-outliers-vs", F(f.median)), EmcLineStyle.Text, 1);
                x.Note(Linkify(x, f.origin), 2);
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
                        return mod.RecalculateEmc("/emc diagnose") != null ? L("diag-done-recalc") : L("diag-done-recalc-failed");
                    case "allow":
                        mod.SetBlacklistedPattern(target, false);
                        mod.RecalculateEmc("/emc diagnose allow");
                        return L("diag-done-allow", target);
                    case "unpin":
                        mod.Config.FixedValues.Remove(target);
                        mod.SaveConfig();
                        mod.RecalculateEmc("/emc diagnose unpin");
                        return L("diag-done-unpin", target);
                    case "removekey":
                        {
                            VintageEConfig c = mod.Config;
                            int n = (c.BaseValues.Remove(target) ? 1 : 0) + (c.FixedValues.Remove(target) ? 1 : 0) + c.Blacklist.RemoveAll(k => k == target) + c.Allow.RemoveAll(k => k == target)
                                + c.NoTransmute.RemoveAll(k => k == target) + c.GrayList.RemoveAll(k => k == target) + c.FreeIngredients.RemoveAll(k => k == target);
                            mod.SaveConfig();
                            if (n > 0) mod.RecalculateEmc("/emc diagnose removekey");
                            return n > 0 ? L("diag-done-removekey", target) : L("diag-done-nothing", target);
                        }
                    case "ungray":
                        mod.Config.GrayList.RemoveAll(k => k == target);
                        mod.SaveConfig();
                        mod.RecalculateEmc("/emc diagnose ungray");
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

        static void BugReport(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-bugreport"));
            r.Add(x.L("diag-bugreport-help"), EmcLineStyle.Note);
            var sb = new StringBuilder();
            sb.Append("Vintage-E bug report, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("Vintage-E ").Append(x.Mod.Version ?? "?").Append(", Vintage Story ").Append(GameVersion.LongGameVersion).Append('\n');
            sb.Append("Server: ").Append(x.Sapi.Server.IsDedicated ? "dedicated" : "single player / hosted").Append(", ").Append((x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>()).Length).Append(" players online\n");
            sb.Append("\n== Mods ==\n");
            foreach (Mod m in x.Sapi.ModLoader.Mods.OrderBy(m => m.Info?.ModID, StringComparer.Ordinal))
            {
                if (m.Info == null) continue;
                sb.Append(m.Info.ModID).Append(' ').Append(m.Info.Version).Append(x.Sapi.ModLoader.IsModEnabled(m.Info.ModID) ? "" : " (disabled)").Append('\n');
            }
            sb.Append("\n== Diagnosis ==\n");
            var inner = new EmcReport { Kind = EmcReport.DiagnoseKind };
            var ix = new Ctx { Mod = x.Mod, Sapi = x.Sapi, LangCode = "en", R = inner, Report = x.Report, Index = x.Index, Player = null, Held = x.Held, ThenAction = ActionBugReport };
            if (MappingState(ix, true))
            {
                Mapping(ix, true);
                Patches(ix, true);
                Outliers(ix, true);
            }
            sb.Append(inner.PlainText(x.Sapi.World)).Append('\n');
            sb.Append("\n== Server log (Vintage-E errors and warnings, addresses and ids removed) ==\n");
            List<string> logLines = LogLines(x, 60, out string err);
            foreach (string l in logLines) sb.Append(l).Append('\n');
            if (err != null) sb.Append(err).Append('\n');
            sb.Append("\n== ModConfig/").Append(VintageEModSystem.ConfigFile).Append(" ==\n");
            try { sb.Append(Newtonsoft.Json.JsonConvert.SerializeObject(x.Mod.Config, Newtonsoft.Json.Formatting.Indented)); }
            catch (Exception e) { sb.Append("(could not serialize: ").Append(e.Message).Append(')'); }
            sb.Append('\n');
            string text = Anonymise(sb.ToString());
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
                    if (held != null)
                    {
                        if (x.Mod.Emc.IsNoTransmute(held)) { x.Warn(x.L("diag-item-notransmute")); r.Add(FixLink(x, "notransmute", VintageEModSystem.ConfigKey(held), x.L("diag-fix-notransmute"))); }
                        else x.Ok(x.L("diag-sym-notonlist"));
                        if (x.Mod.Emc.StackData.CarriesData(held) && x.Mod.Emc.StackData.ModeOf(held) == StackDataMode.Full && x.Mod.Config.ProtectContainersWithContents) x.Note(x.L("diag-stack-protected"), 0);
                        double each = x.Mod.Emc.GetValue(x.Held);
                        if (each <= 0 && x.Mod.Emc.GetValue(held) > 0) { x.Warn(x.L("diag-item-stackdiffers", F(each), F(x.Mod.Emc.GetValue(held)))); StackReasons(x, x.Held, x.Mod.Emc.GetValue(held), each); }
                    }
                    else x.Note(x.L("diag-sporadic-holdit"), 0);
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
                    else { x.Note(x.L("diag-sporadic-holdit"), 0); r.Add(Link(ActionHand, "", x.L("diag-q-item"))); }
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
