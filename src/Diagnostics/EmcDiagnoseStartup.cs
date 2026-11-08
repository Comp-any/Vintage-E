using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// What other mods did at start-up, from the game's own messages in server-main.log: mods the game skipped for a
    /// missing dependency, mods that crashed while starting (which can stop the game from reaching Vintage-E's own
    /// start-up step, so no values get mapped), JSON patches that failed and recipes the game could not resolve.
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionStartup = "startup";

        sealed class StartupLog
        {
            /// <summary>Mods the game skipped, with what they said was missing.</summary>
            public readonly List<(string mod, List<string> missing)> Skipped = new List<(string, List<string>)>();
            /// <summary>Error lines from other mods (or the game about them) while starting, already anonymised.</summary>
            public readonly List<string> Crashes = new List<string>();
            /// <summary>Failed JSON patches by the mod that ships them.</summary>
            public readonly Dictionary<string, int> FailedPatches = new Dictionary<string, int>();
            /// <summary>Recipes and drops the game could not resolve, by the mod of the missing code.</summary>
            public readonly Dictionary<string, int> Unresolved = new Dictionary<string, int>();
            public string Error;
            public bool Found => Skipped.Count > 0 || Crashes.Count > 0 || FailedPatches.Count > 0 || Unresolved.Count > 0;
        }

        static readonly Regex DepHeader = new Regex(@"\[(Error|Warning)\] \[([a-z0-9_-]+)\] Could not resolve some dependencies", RegexOptions.Compiled);
        static readonly Regex DepLine = new Regex(@"\[(Error|Warning)\] \[([a-z0-9_-]+)\]\s+(\S+) - (.+)$", RegexOptions.Compiled);
        static readonly Regex PatchFail = new Regex(@"\[Error\] Patch \d+ \(target: [^)]*\) in ([a-z0-9_-]+):", RegexOptions.Compiled);
        static readonly Regex Unresolvable = new Regex(@"Failed resolving (?:a blocks itemdrop or smeltedstack|crafting recipe ingredient|[a-z ]+) with code ([a-z0-9_-]+):", RegexOptions.Compiled);
        static readonly Regex ModTag = new Regex(@"^\S+ \S+ \[(Error|Fatal)\] \[([a-z0-9_-]+)\]", RegexOptions.Compiled);

        /// <summary>Reads this session's start-up from the log (from the game's "Mods, sorted by dependency" line on).</summary>
        static StartupLog ReadStartupLog(Ctx x)
        {
            var log = new StartupLog();
            try
            {
                string file = Path.Combine(x.Sapi.DataBasePath, "Logs", "server-main.log");
                if (!File.Exists(file)) { log.Error = x.L("diag-log-nofile", file); return log; }
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, fs.Length - 4 * 1024 * 1024);
                fs.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                if (start > 0) reader.ReadLine();
                var lines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
                // Only this start: the log can hold earlier runs.
                // The dependency errors come right after "Found N mods" and before "Mods, sorted by dependency".
                int from = lines.FindLastIndex(l => l.Contains("Found ") && l.Contains(" mods ("));
                if (from < 0) from = lines.FindLastIndex(l => l.Contains("Mods, sorted by dependency"));
                if (from < 0) from = 0;
                // Everything after the first player joined is not start-up any more.
                int to = lines.FindIndex(from, l => l.Contains("[Event] ") && l.Contains(" joins.") || l.Contains("Dedicated server now running") || l.Contains("[Vintage-E self-test]"));
                if (to < 0) to = lines.Count;
                string currentDepMod = null;
                for (int i = from; i < to; i++)
                {
                    line = lines[i];
                    if (!LogLevel.IsMatch(line)) { currentDepMod = null; continue; }
                    Match m = DepHeader.Match(line);
                    if (m.Success)
                    {
                        currentDepMod = m.Groups[2].Value;
                        if (!log.Skipped.Any(s => s.mod == currentDepMod)) log.Skipped.Add((currentDepMod, new List<string>()));
                        continue;
                    }
                    if (currentDepMod != null)
                    {
                        m = DepLine.Match(line);
                        if (m.Success && m.Groups[2].Value == currentDepMod)
                        {
                            var entry = log.Skipped.First(s => s.mod == currentDepMod);
                            string dep = m.Groups[3].Value, why = m.Groups[4].Value.Trim();
                            entry.missing.Add(why.StartsWith("Missing", StringComparison.OrdinalIgnoreCase) ? dep : dep + " (" + why + ")");
                            continue;
                        }
                        currentDepMod = null;
                    }
                    m = PatchFail.Match(line);
                    if (m.Success) { Bump(log.FailedPatches, m.Groups[1].Value); continue; }
                    m = Unresolvable.Match(line);
                    if (m.Success) { Bump(log.Unresolved, m.Groups[1].Value); continue; }
                    if (line.Contains("[Warning]")) continue;
                    // An error from or about another mod while starting.
                    bool ours = line.IndexOf("vintagee", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Vintage-E", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("VintageE", StringComparison.Ordinal) >= 0;
                    if (ours) continue;
                    string low = line.ToLowerInvariant();
                    bool about = ModTag.IsMatch(line) || low.Contains("exception") || low.Contains("mod ") && (low.Contains("failed") || low.Contains("could not") || low.Contains("unable"));
                    if (!about) continue;
                    if (log.Crashes.Count < 40) log.Crashes.Add(Anonymise(line));
                    // Keep the first stack line, which names the mod's code.
                    if (i + 1 < to && lines[i + 1].TrimStart().StartsWith("at ", StringComparison.Ordinal) && log.Crashes.Count < 40) log.Crashes.Add("    " + Anonymise(lines[i + 1].Trim()));
                }
            }
            catch (Exception e) { log.Error = x.L("diag-log-error", e.Message); }
            return log;
        }

        static void Bump(Dictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out int n) ? n + 1 : 1;

        /// <summary>The start-up check: this mod's own crashed steps, a late mapping, /emc taken, and what other mods did.</summary>
        static int Startup(Ctx x, bool full)
        {
            EmcReport r = x.R;
            VintageEModSystem mod = x.Mod;
            if (full) r.Section(x.L("diag-sec-startup"));
            int problems = 0;

            foreach (string err in mod.StartupErrors)
            {
                problems++;
                x.Problem(x.L("diag-startup-step", err));
                x.Explain(x.L("diag-startup-step-why"));
                x.Fix(x.L("diag-startup-step-fix"));
            }
            if (mod.MappingLateReason != null)
            {
                problems++;
                x.Warn(x.L("diag-startup-late", mod.MappingLateReason));
                x.Explain(x.L("diag-startup-late-why"));
                x.Fix(x.L("diag-startup-late-fix"));
            }
            if (mod.CommandConflict)
            {
                problems++;
                x.Problem(x.L("diag-startup-command"));
                x.Explain(x.L("diag-startup-command-why"));
                x.Fix(x.L("diag-startup-command-fix"));
            }

            StartupLog log = ReadStartupLog(x);
            if (log.Error != null) { x.Note(log.Error, 0); return problems; }

            if (log.Crashes.Count > 0)
            {
                problems++;
                x.Problem(x.L("diag-startup-crashes", log.Crashes.Count));
                x.Explain(x.L(x.Report == null || mod.MappingLateReason != null ? "diag-startup-crashes-why-hit" : "diag-startup-crashes-why"));
                int show = full ? log.Crashes.Count : Math.Min(6, log.Crashes.Count);
                for (int i = 0; i < show; i++) r.Add(log.Crashes[i], EmcLineStyle.Text, 1);
                if (show < log.Crashes.Count) r.Add(x.L("diag-startup-more", log.Crashes.Count - show), EmcLineStyle.Note, 1);
                x.Fix(x.L("diag-startup-crashes-fix"));
            }
            if (log.Skipped.Count > 0)
            {
                string list = string.Join(", ", log.Skipped.Take(full ? 100 : 8).Select(s => s.missing.Count == 0 ? s.mod : x.L("diag-startup-needs", s.mod, string.Join(", ", s.missing))));
                if (!full && log.Skipped.Count > 8) list += ", …";
                x.Info(x.L("diag-startup-skipped", log.Skipped.Count, list));
                x.Explain(x.L("diag-startup-skipped-why"));
                x.Fix(x.L("diag-startup-skipped-fix"));
            }
            if (log.FailedPatches.Count > 0)
            {
                x.Info(x.L("diag-startup-patches", log.FailedPatches.Values.Sum(), string.Join(", ", log.FailedPatches.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " (" + kv.Value + ")"))));
                x.Explain(x.L("diag-startup-patches-why"));
            }
            if (log.Unresolved.Count > 0)
            {
                x.Info(x.L("diag-startup-unresolved", log.Unresolved.Values.Sum(), string.Join(", ", log.Unresolved.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " (" + kv.Value + ")"))));
                x.Explain(x.L("diag-startup-unresolved-why"));
            }
            if (!log.Found && problems == 0)
            {
                x.Ok(x.L("diag-startup-clean"));
                x.Explain(x.L("diag-startup-clean-why"));
            }
            if (!full) r.Add(Link(ActionStartup, "", x.L("diag-q-startup")), EmcLineStyle.Text, 1);
            return problems;
        }
    }
}
