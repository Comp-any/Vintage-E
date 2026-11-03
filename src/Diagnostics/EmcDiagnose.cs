using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc diagnose: works out why EMC isn't working - for one block or item, for everything from one mod, for values
    /// that come and go, or for the whole mapping - and says in plain words what's wrong and what to do about it. The
    /// report ends with questions whose answers are links ({{action|arg|label}}): clicking one asks the server for the
    /// next, more specific report, so the player is led to the cause without knowing the mod's internals.
    /// <para>Actions: "" (check-up and questions), "hand"/"item" (the held or named block/item), "mod" (one mod, or the
    /// list of mods), "sporadic" (values that differ between stacks or players), "mapping" (every mapping check).</para>
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionItem = "item", ActionHand = "hand", ActionMod = "mod", ActionMods = "mods", ActionSporadic = "sporadic", ActionMapping = "mapping", ActionOverview = "overview";

        sealed class Ctx
        {
            public VintageEModSystem Mod;
            public ICoreServerAPI Sapi;
            public EmcMappingReport Report;
            public CollectibleIndex Index;
            public string LangCode;
            public EmcReport R;
            public IServerPlayer Player;
            public ItemStack Held;
            /// <summary>The report a fix link returns to (this report's action and argument).</summary>
            public string ThenAction = ActionOverview, ThenArg = "";
            /// <summary>For the bug report: results and facts only, no advice (→ lines) and no explanations.</summary>
            public bool Compact;
            public string L(string key, params object[] args) => Lang.GetL(LangCode, "vintagee:" + key, args);
            public double V(CollectibleObject c) => c != null && Report?.Values != null && Report.Values.TryGetValue(c, out double v) ? v : 0;
            /// <summary>A test that passed: one line, which the window shows collapsed with its details behind it.</summary>
            public void Ok(string text) => R.Add("✓ " + text, EmcLineStyle.Check);
            /// <summary>A test that found something: one line; the window shows its details and fixes when clicked.</summary>
            public void Warn(string text) => R.Add("⚠ " + text, EmcLineStyle.Check);
            /// <summary>A neutral finding (neither good nor bad) with details behind it.</summary>
            public void Info(string text) => R.Add("• " + text, EmcLineStyle.Check);
            public void Fix(string text) { if (!Compact) R.Add("→ " + text, EmcLineStyle.Text, 1); }
            public void Note(string text, int indent = 1) => R.Add(text, EmcLineStyle.Note, indent);
            /// <summary>Background that helps a reader but not a bug report.</summary>
            public void Explain(string text, int indent = 1) { if (!Compact) R.Add(text, EmcLineStyle.Note, indent); }
        }

        static string F(double v) => EmcFormat.Format(v);

        /// <summary>The block or item a player typed: a code with or without "item:"/"block:" and domain ("game:" when left out), or an i:/b: key.</summary>
        static CollectibleObject Find(Ctx x, string code)
        {
            if (string.IsNullOrWhiteSpace(code) || x.Index == null || code.Contains('*')) return null;
            CollectibleObject byKey = PlayerEmc.Resolve(x.Sapi.World, code);
            if (byKey?.Code != null) return byKey;
            List<CollectibleObject> found;
            try { found = x.Index.Match(code).ToList(); }
            catch { return null; }
            if (found.Count == 0) return null;
            // A block and an item can share a code: the one without a value is the one being asked about.
            return found.FirstOrDefault(c => x.Report != null && !x.Report.Values.ContainsKey(c)) ?? found[0];
        }
        static string Pct(int part, int total) => total == 0 ? "100%" : (part * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        static string Link(string action, string arg, string label) => "{{" + action + "|" + (arg ?? "") + "|" + label.Replace("{", "").Replace("}", "").Replace("|", "/") + "}}";

        /// <summary>Builds the report for one action. <paramref name="held"/> is the player's held stack (may be null).</summary>
        public static EmcReport Run(VintageEModSystem mod, ICoreServerAPI sapi, IServerPlayer player, string lang, string action, string arg, ItemStack held,
            EmcDiagnoseRequestPacket client = null, string outcome = null)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            arg = (arg ?? "").Trim();
            var r = new EmcReport { Kind = EmcReport.DiagnoseKind, Key = action + "|" + arg };
            var x = new Ctx { Mod = mod, Sapi = sapi, LangCode = lang, R = r, Player = player, Report = mod.LastReport, Held = held, ThenAction = action.Length == 0 ? ActionOverview : action, ThenArg = arg };
            r.Title = x.L("diag-title");
            if (!string.IsNullOrEmpty(outcome)) r.Add(outcome, outcome.StartsWith("\u26a0") ? EmcLineStyle.Warn : EmcLineStyle.Total);
            try { x.Index = new CollectibleIndex(sapi.World.Collectibles); }
            catch (Exception e) { sapi.Logger.Warning("Vintage-E: /emc diagnose could not index the collectibles: {0}", e.Message); }

            if (action == "troubleshoot" || action == "help" || action == "guide") action = ActionNoValue;
            // An item code or a mod id given straight: "/emc diagnose game:ingot-copper", "/emc diagnose primitivesurvival".
            if (action.Length > 0 && !AllActions.Contains(action))
            {
                if (Find(x, action) != null) { arg = action; action = ActionItem; }
                else if (sapi.ModLoader.IsModEnabled(action) || (x.Index?.All.Any(c => c.Code.Domain == action) ?? false)) { arg = action; action = ActionMod; }
                else { r.Add(x.L("diag-unknown", action), EmcLineStyle.Warn); action = ActionOverview; }
                r.Key = action + "|" + arg;
                x.ThenAction = action; x.ThenArg = arg;
            }

            if (action == ActionBugReport) { BugReport(x); return r; }
            if (action == ActionLog) { Log(x, true); Back(x); return r; }
            if (action == ActionStartup) { Startup(x, true); Back(x); return r; }
            if (action == ActionClient) { Client(x, client, true); Back(x); return r; }
            if (action == ActionPlayers) { Players(x); return r; }
            if (action == ActionPlayer) { if (arg.Length == 0) Players(x); else PlayerReport(x, arg); return r; }

            if (!MappingState(x, action == ActionMapping || action == ActionOverview || action == ""))
            {
                // No values at all: what other mods did at start-up is the first place to look.
                Startup(x, false);
                Log(x, false);
                Questions(x, held, client);
                return r;
            }

            // Cached values come without the recipes behind them; item and mod reports need those, so map once more.
            if ((action == ActionHand || action == ActionItem || action == ActionMod || action == ActionSporadic || action == ActionClean || action == ActionOutliers || action == ActionSymptom || action == ActionChanges
                || action == ActionChecks || action == ActionBaseline || action == ActionProfit || action == ActionSkipped || action == ActionTrace) && x.Report.FromCache)
            {
                EmcMappingReport fresh = mod.RecalculateEmc("/emc diagnose");
                if (fresh != null) { x.Report = fresh; r.Add(x.L("diag-remapped"), EmcLineStyle.Note); }
            }

            switch (action)
            {
                case ActionHand:
                case ActionItem:
                    ItemStack stack = held;
                    if (arg.Length > 0)
                    {
                        CollectibleObject c = Find(x, arg);
                        if (c == null) { r.Add(x.L("diag-item-notfound", arg), EmcLineStyle.Warn); Questions(x, held, client); return r; }
                        stack = held?.Collectible == c ? held : new ItemStack(c);
                    }
                    if (stack?.Collectible?.Code == null) { r.Add(x.L("diag-item-holdit"), EmcLineStyle.Warn); Questions(x, null, client); return r; }
                    r.Key = ActionItem + "|" + stack.Collectible.Code;
                    x.ThenAction = ActionItem; x.ThenArg = stack.Collectible.Code.ToString();
                    Item(x, stack, held != null && held.Collectible == stack.Collectible);
                    break;
                case ActionPatches: Patches(x, true); Back(x); break;
                case ActionWatch: Watch(x, arg); break;
                case ActionTrace:
                    {
                        CollectibleObject tc = arg.Length > 0 ? Find(x, arg) : held?.Collectible;
                        if (tc == null) { r.Add(arg.Length > 0 ? x.L("diag-item-notfound", arg) : x.L("diag-item-holdit"), EmcLineStyle.Warn); Questions(x, held, client); break; }
                        x.ThenAction = ActionTrace; x.ThenArg = tc.Code.ToString(); r.Key = ActionTrace + "|" + tc.Code;
                        Trace(x, tc);
                        break;
                    }
                case ActionShort: Short(x, client); break;
                case ActionChecks: Checks(x, client); break;
                case ActionBaseline: Baseline(x); break;
                case ActionProfit: Profit(x, full: true); Back(x); break;
                case ActionSkipped: SkippedRecipes(x, full: true); Back(x); break;
                case ActionDraft: DraftCompat(x, arg); break;
                case ActionOutliers: Outliers(x, true); Back(x); break;
                case ActionClean: Clean(x); break;
                case ActionSymptom: Symptom(x, arg, client); break;
                case ActionNoValue: NoValueGuide(x, client); break;
                case ActionChanges: Changes(x); break;
                case ActionMod:
                    if (arg.Length == 0) { Mods(x); break; }
                    ModReport(x, arg);
                    break;
                case ActionMods:
                    Mods(x);
                    break;
                case ActionSporadic:
                    Sporadic(x, held);
                    break;
                case ActionMapping:
                    Mapping(x, full: true);
                    Client(x, client, true);
                    Log(x, true);
                    Patches(x, true);
                    Outliers(x, true);
                    Questions(x, held, client);
                    break;
                default:
                    Mapping(x, full: false);
                    Client(x, client, true);
                    Log(x, false);
                    Patches(x, false);
                    Questions(x, held, client);
                    break;
            }
            return r;
        }

        // ---------------------------------------------------------------- the mapping as a whole

        /// <summary>Is there a mapping at all? Explains a failed or missing one. Returns false when nothing else can be checked.</summary>
        static bool MappingState(Ctx x, bool heading)
        {
            EmcReport r = x.R;
            VintageEModSystem mod = x.Mod;
            if (heading) r.Section(x.L("diag-sec-state"));
            if (mod.ConfigLoadError != null)
            {
                x.Warn(x.L("diag-config-broken", VintageEModSystem.ConfigFile, mod.ConfigLoadError));
                x.Fix(x.L("diag-config-broken-fix"));
            }
            if (x.Report == null)
            {
                x.Warn(x.L("diag-notmapped"));
                if (mod.LastMappingError != null)
                {
                    x.Warn(x.L("diag-mapfailed", mod.LastMappingErrorAt, mod.LastMappingError));
                    x.Fix(x.L("diag-mapfailed-fix1"));
                    x.Fix(x.L("diag-mapfailed-fix2"));
                    x.Fix(x.L("diag-mapfailed-fix3", mod.Version));
                }
                else
                {
                    x.Fix(x.L("diag-notmapped-fix"));
                }
                return false;
            }
            if (mod.LastMappingError != null && mod.LastMappingErrorAt > x.Report.MappedAt)
            {
                x.Warn(x.L("diag-mapfailed-old", mod.LastMappingErrorAt, mod.LastMappingError));
                x.Fix(x.L("diag-mapfailed-fix1"));
            }
            if (heading)
            {
                int valued = x.Report.CreativeTotal - x.Report.Missing.Count;
                string mapped = x.Report.FromCache
                    ? x.L("diag-mapped-cached", x.Report.Values.Count, x.Report.CachedAt ?? "?")
                    : x.L("diag-mapped", x.Report.Values.Count, x.Report.Milliseconds);
                if (valued * 10 < x.Report.CreativeTotal * 9) x.Warn(mapped); else x.Ok(mapped);
                r.Add(x.L("diag-coverage", valued, x.Report.CreativeTotal, Pct(valued, x.Report.CreativeTotal)), valued * 10 < x.Report.CreativeTotal * 9 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
            }
            return true;
        }

        /// <summary>The check-up: every known way the mapping can go wrong, each with what it means and what to do.</summary>
        static int Mapping(Ctx x, bool full)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            VintageEConfig cfg = x.Mod.Config;
            r.Section(x.L("diag-sec-checks"));
            int problems = 0;

            // Settings that switch most of the mapping off.
            if (cfg.DisableBuiltInBaseValues)
            {
                problems++;
                x.Warn(x.L("diag-nobuiltin", report.BaseCount));
                x.Explain(x.L("diag-nobuiltin-why"));
                x.Fix(x.L("diag-nobuiltin-fix"));
                r.Add(FixLink(x, "setting", "DisableBuiltInBaseValues=false", x.L("diag-fix-setting", "DisableBuiltInBaseValues", "false")), EmcLineStyle.Text, 1);
            }
            if (!cfg.InferValuesFromProcessing || !cfg.InferValuesFromCrafting || !cfg.InferFromVariantSiblings || !cfg.EstimateFuelAndFood || !cfg.UseTraderPrices)
            {
                var off = new List<string>();
                if (!cfg.InferValuesFromProcessing) off.Add("InferValuesFromProcessing");
                if (!cfg.InferValuesFromCrafting) off.Add("InferValuesFromCrafting");
                if (!cfg.InferFromVariantSiblings) off.Add("InferFromVariantSiblings");
                if (!cfg.EstimateFuelAndFood) off.Add("EstimateFuelAndFood");
                if (!cfg.UseTraderPrices) off.Add("UseTraderPrices");
                x.Info(x.L("diag-guessing-off", off.Count));
                x.Note(string.Join(", ", off));
                x.Explain(x.L("diag-guessing-off-why"));
            }
            if (cfg.FallbackValue > 0) { x.Info(x.L("diag-fallback", F(cfg.FallbackValue))); x.Explain(x.L("diag-fallback-why")); }

            // Value files and config entries that match nothing (a typo in a code, or a mod that isn't loaded).
            if (x.Index != null)
            {
                var dead = new List<string>();
                foreach (string key in cfg.BaseValues.Keys.Concat(cfg.FixedValues.Keys).Concat(cfg.Blacklist).Concat(cfg.Allow).Concat(cfg.NoTransmute).Concat(cfg.GrayList).Concat(cfg.FreeIngredients))
                {
                    if (!string.IsNullOrWhiteSpace(key) && !x.Index.Match(key).Any()) dead.Add(key);
                }
                if (dead.Count > 0)
                {
                    problems++;
                    x.Warn(x.L("diag-deadkeys", dead.Count, VintageEModSystem.ConfigFile));
                    foreach (string key in dead.Take(15)) r.Add(key + Suggest(x, key) + "  " + FixLink(x, "removekey", key, x.L("diag-fix-removekey")), EmcLineStyle.Text, 2);
                    if (dead.Count > 15) x.Note(x.L("diag-more", dead.Count - 15), 2);
                    x.Fix(x.L("diag-deadkeys-fix"));
                }
                else
                {
                    int n = cfg.BaseValues.Count + cfg.FixedValues.Count + cfg.Blacklist.Count + cfg.Allow.Count + cfg.NoTransmute.Count + cfg.GrayList.Count + cfg.FreeIngredients.Count;
                    x.Ok(n == 0 ? x.L("diag-keys-none", VintageEModSystem.ConfigFile) : x.L("diag-keys-ok", n));
                }
            }
            if (report.CustomRecipeProblems.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-customrecipes", report.CustomRecipeProblems.Count));
                foreach (string p in report.CustomRecipeProblems.Take(8)) r.Add(p, EmcLineStyle.Text, 2);
                x.Fix(x.L("diag-customrecipes-fix"));
            }

            // Value files.
            x.Ok(x.L("diag-files", x.Mod.Rules.LoadedFiles.Count, report.BaseCount));
            if (report.UnmatchedRules > 0) x.Note(x.L("diag-unmatchedrules", report.UnmatchedRules));
            if (report.SkippedRecipes > 0) x.Note(x.L("diag-skippedrecipes", report.SkippedRecipes));

            // Compatibility files.
            var compat = x.Mod.Rules.Compat;
            var disabled = compat.Where(f => f.Disabled).ToList();
            var loaded = compat.Where(f => f.Loaded).ToList();
            var notLoaded = compat.Where(f => f.MissingMod != null && !f.Disabled).ToList();
            if (disabled.Count > 0)
            {
                x.Warn(x.L("diag-compat-disabled", string.Join(", ", disabled.Select(f => f.Name))));
                x.Explain(x.L("diag-compat-disabled-why"));
                x.Fix(x.L("diag-compat-disabled-fix"));
                foreach (var f in disabled) r.Add(FixLink(x, "compat", f.Source, x.L("diag-fix-compat", f.Name)), EmcLineStyle.Text, 1);
            }
            if (loaded.Count > 0)
            {
                x.Ok(x.L("diag-compat-loaded", loaded.Count, loaded.Sum(f => report.CompatUsed.TryGetValue(f.Source, out int n) ? n : 0)));
                foreach (var f in loaded) x.Note(f.Name + ": " + (report.CompatUsed.TryGetValue(f.Source, out int n) ? n : 0));
            }
            if (full && notLoaded.Count > 0) x.Note(x.L("diag-compat-notloaded", string.Join(", ", notLoaded.Select(f => f.Name))));

            // Mods with poor coverage.
            var mods = ModCoverage(x);
            var poor = mods.Where(m => m.total >= 5 && m.missing * 4 > m.total).ToList();
            if (poor.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-poormods", poor.Count));
                foreach (var m in poor.Take(10)) r.Add(Link(ActionMod, m.domain, ModName(x, m.domain)) + ": " + x.L("diag-mod-line", m.total - m.missing, m.total, Pct(m.total - m.missing, m.total)), EmcLineStyle.Text, 2);
                x.Fix(x.L("diag-poormods-fix"));
            }
            else x.Ok(x.L("diag-mods-ok", mods.Count));

            // Loops.
            if (report.Loops.Count > 0)
            {
                problems++;
                x.Warn(x.L("diag-loops", report.Loops.Count, report.Loops.Sum(l => l.Count)));
                x.Explain(x.L("diag-loops-why"));
                foreach (var loop in report.Loops.Take(3)) r.Add(string.Join(" → ", loop.Take(6).Select(EmcReport.Ref)) + (loop.Count > 6 ? " → ..." : ""), EmcLineStyle.Text, 2);
                x.Fix(x.L("diag-loops-fix"));
            }
            else if (full) x.Ok(x.L("diag-loops-ok"));

            // Missing, by family and by reason.
            if (report.Missing.Count > 0)
            {
                int noRecipe = report.Missing.Count(c => report.MissingWhy.TryGetValue(c, out string w) && w == MissingAnalysis.NoRecipe);
                int inLoop = report.Missing.Count(c => report.LoopOf.ContainsKey(c));
                int waiting = report.Missing.Count - noRecipe - inLoop;
                string missing = x.L("diag-missing", report.Missing.Count, noRecipe, Math.Max(0, waiting), inLoop);
                if (report.Missing.Count * 10 > report.CreativeTotal) x.Warn(missing); else x.Info(missing);
                x.Note(x.L("diag-missing-families"));
                foreach (var fam in VintageEModSystem.MissingFamilies(report).Take(full ? 15 : 6))
                {
                    CollectibleObject sample = report.Missing.FirstOrDefault(c => c.Code.Domain + ":" + c.Code.Path.Split('-')[0] == fam.Key);
                    r.Add(fam.Key + " (" + fam.Value + ")" + (sample != null ? ": " + EmcReport.Ref(sample) : ""), EmcLineStyle.Text, 2);
                }
                x.Fix(x.L("diag-missing-fix"));
            }
            else x.Ok(x.L("diag-missing-none"));

            // Cache.
            if (report.FromCache) { x.Info(x.L("diag-cache", report.CachedAt ?? "?")); x.Explain(x.L("diag-cache-why")); r.Add(FixLink(x, "recalc", "", x.L("diag-fix-recalc")), EmcLineStyle.Text, 1); }
            if (full)
            {
                x.Info(x.L("diag-stats-head", report.ConversionCount, report.SourceCount));
                x.Note(x.L("diag-stats", report.DerivedCount, report.InferredCount, report.RelativeCount, report.TraderCount, report.SiblingCount + report.GuessCount + report.OtherModCount, report.DefaultCount, report.FuelCount + report.FoodCount));
                if (report.UnstableRemoved > 0 || report.LoopFrozen > 0) x.Note(x.L("diag-unstable", report.LoopFrozen, report.UnstableRemoved));
            }

            problems += ChecksSummary(x);
            problems += Startup(x, false);
            // No verdict line: the chips at the top of the window count the checks, and each ⚠ card says what to do.
            if (SafeFixCount(x) > 0) r.Add(FixLink(x, "safe", "", x.L("diag-fix-safe", SafeFixCount(x))), EmcLineStyle.Text, 1);
            return problems;
        }

        /// <summary>The check-up's count of things worth fixing, for the start-up log and the join message (no report shown).</summary>
        public static int CountProblems(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            if (mod.LastReport == null) return 1;
            var x = new Ctx { Mod = mod, Sapi = sapi, LangCode = "en", R = new EmcReport(), Report = mod.LastReport, Index = new CollectibleIndex(sapi.World.Collectibles) };
            int n = Mapping(x, full: false);
            if (mod.ConfigLoadError != null) n++;
            if (mod.LastMappingError != null && mod.LastMappingErrorAt > mod.LastReport.MappedAt) n++;
            return n;
        }

        /// <summary>A config key that matches nothing: the closest real code, if there is one.</summary>
        static string Suggest(Ctx x, string key)
        {
            string path = key;
            int colon = path.IndexOf(':');
            string domain = "";
            if (colon >= 0) { domain = path.Substring(0, colon); path = path.Substring(colon + 1); }
            if (domain == "item" || domain == "block") { colon = path.IndexOf(':'); if (colon >= 0) { domain = path.Substring(0, colon); path = path.Substring(colon + 1); } else domain = ""; }
            path = path.Replace("*", "");
            if (path.Length < 3 || x.Index == null) return "";
            string first = path.Split('-')[0];
            if (domain.Length > 0 && !x.Index.All.Any(c => c.Code.Domain == domain))
            {
                return " " + x.L("diag-deadkey-nomod", domain);
            }
            CollectibleObject close = x.Index.All.Where(c => (domain.Length == 0 || c.Code.Domain == domain) && CollectibleIndex.IsListed(c))
                .OrderBy(c => Distance(path, c.Code.Path)).FirstOrDefault(c => Distance(path, c.Code.Path) <= Math.Max(2, path.Length / 4));
            if (close == null) close = x.Index.All.FirstOrDefault(c => (domain.Length == 0 || c.Code.Domain == domain) && c.Code.Path.StartsWith(first, StringComparison.Ordinal) && CollectibleIndex.IsListed(c));
            return close == null ? "" : " " + x.L("diag-deadkey-maybe", EmcReport.Ref(close));
        }

        static int Distance(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 6) return 99;
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        // ---------------------------------------------------------------- mods

        static List<(string domain, int total, int missing, int blacklisted)> ModCoverage(Ctx x)
        {
            var result = new List<(string, int, int, int)>();
            if (x.Index == null) return result;
            var missing = new HashSet<CollectibleObject>(x.Report.Missing);
            var black = new HashSet<CollectibleObject>(x.Report.Blacklisted);
            foreach (var g in x.Index.All.Where(CollectibleIndex.IsListed).GroupBy(c => c.Code.Domain))
            {
                int total = 0, miss = 0, bl = 0;
                foreach (CollectibleObject c in g)
                {
                    if (black.Contains(c)) { bl++; continue; }
                    if (!x.Report.Values.ContainsKey(c) && !missing.Contains(c)) continue; // gray list
                    total++;
                    if (missing.Contains(c)) miss++;
                }
                result.Add((g.Key, total, miss, bl));
            }
            return result.OrderByDescending(m => m.Item3).ThenBy(m => m.Item1, StringComparer.Ordinal).ToList();
        }

        static string ModName(Ctx x, string domain)
        {
            if (domain == "game" || domain == "survival" || domain == "creative") return x.L("diag-basegame") + " (" + domain + ")";
            try
            {
                Mod m = x.Sapi.ModLoader.GetMod(domain);
                if (m?.Info?.Name != null) return m.Info.Name + " (" + domain + ")";
            }
            catch { /* unknown domain */ }
            return domain;
        }

        /// <summary>Every mod with content, with its coverage, each a link to its own report.</summary>
        static void Mods(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-mods"));
            r.Add(x.L("diag-mods-help"), EmcLineStyle.Note);
            foreach (var m in ModCoverage(x))
            {
                string line = Link(ActionMod, m.domain, ModName(x, m.domain)) + ": " + x.L("diag-mod-line", m.total - m.missing, m.total, Pct(m.total - m.missing, m.total));
                if (m.blacklisted > 0) line += " " + x.L("diag-mod-blacklisted", m.blacklisted);
                r.Add(line, m.total > 0 && m.missing * 4 > m.total ? EmcLineStyle.Warn : EmcLineStyle.Text);
            }
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        /// <summary>One mod: how much of it has values, why the rest hasn't, and what would fix it.</summary>
        static void ModReport(Ctx x, string domain)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            domain = domain.ToLowerInvariant();
            var cov = ModCoverage(x).FirstOrDefault(m => m.domain == domain);
            r.Section(x.L("diag-sec-mod", ModName(x, domain)));
            bool enabled = x.Sapi.ModLoader.IsModEnabled(domain);
            if (cov.domain == null)
            {
                if (!enabled && x.Sapi.ModLoader.Mods.All(m => m.Info?.ModID != domain))
                {
                    x.Warn(x.L("diag-mod-notinstalled", domain));
                    x.Fix(x.L("diag-mod-notinstalled-fix"));
                }
                else if (!enabled)
                {
                    x.Warn(x.L("diag-mod-disabled", domain));
                }
                else
                {
                    x.Note(x.L("diag-mod-noitems", domain), 0);
                    CompatFor(x, domain);
                }
                Back(x);
                return;
            }
            int valued = cov.total - cov.missing;
            r.Add(x.L("diag-mod-cover", valued, cov.total, Pct(valued, cov.total), cov.blacklisted), cov.missing * 4 > cov.total ? EmcLineStyle.Warn : EmcLineStyle.Total);
            CompatFor(x, domain);

            var missing = report.Missing.Where(c => c.Code.Domain == domain).ToList();
            if (missing.Count == 0)
            {
                x.Ok(x.L("diag-mod-allvalued"));
                r.Add(x.L("diag-mod-allvalued-hint"), EmcLineStyle.Note);
                Back(x);
                return;
            }

            // Why: no recipe at all, waiting on an ingredient, or in a loop.
            var noRecipe = missing.Where(c => report.MissingWhy.TryGetValue(c, out string w) && w == MissingAnalysis.NoRecipe).ToList();
            var inLoop = missing.Where(c => report.LoopOf.ContainsKey(c)).ToList();
            var waiting = missing.Except(noRecipe).Except(inLoop).ToList();
            r.Section(x.L("diag-sec-why"));
            if (noRecipe.Count > 0)
            {
                x.Warn(x.L("diag-mod-norecipe", noRecipe.Count));
                r.Add(x.L("diag-mod-norecipe-why"), EmcLineStyle.Note, 1);
                Families(x, noRecipe, 8);
                x.Fix(x.L("diag-mod-norecipe-fix1"));
                if (domain != "game") x.Fix(x.L("diag-mod-norecipe-fix2", domain));
                x.Fix(x.L("diag-mod-norecipe-fix3"));
            }
            if (waiting.Count > 0)
            {
                // The ingredients they're all waiting for: fixing those fixes everything made from them.
                var blockers = new Dictionary<CollectibleObject, int>();
                bool Valued(CollectibleObject o) => o != null && (report.Values.ContainsKey(o) || report.Free.Contains(o));
                foreach (CollectibleObject c in waiting)
                {
                    if (!report.MissingRecipes.TryGetValue(c, out var recipes)) continue;
                    foreach (CollectibleObject part in recipes.SelectMany(conv => MissingAnalysis.Lacking(conv, Valued)).Distinct())
                    {
                        blockers[part] = blockers.TryGetValue(part, out int n) ? n + 1 : 1;
                    }
                }
                x.Warn(x.L("diag-mod-waiting", waiting.Count));
                r.Add(x.L("diag-mod-waiting-why"), EmcLineStyle.Note, 1);
                foreach (var kv in blockers.OrderByDescending(kv => kv.Value).Take(10))
                {
                    string why = report.Blacklisted.Contains(kv.Key) ? x.L("diag-blocker-blacklisted")
                        : report.LoopOf.ContainsKey(kv.Key) ? x.L("diag-blocker-loop")
                        : report.MissingWhy.TryGetValue(kv.Key, out string w) && w == MissingAnalysis.NoRecipe ? x.L("diag-blocker-norecipe")
                        : x.L("diag-blocker-waiting");
                    r.Add(x.L("diag-blocker", EmcReport.Ref(kv.Key), kv.Value) + " " + why + "  " + Link(ActionItem, kv.Key.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 2);
                }
                x.Fix(x.L("diag-mod-waiting-fix"));
            }
            if (inLoop.Count > 0)
            {
                x.Warn(x.L("diag-mod-loops", inLoop.Count, inLoop.Select(c => report.LoopOf[c]).Distinct().Count()));
                x.Fix(x.L("diag-loops-fix"));
            }

            if (noRecipe.Count > 0 && domain != "game")
            {
                r.Add(Link(ActionDraft, domain, x.L("diag-q-draft", noRecipe.Count)), EmcLineStyle.Text);
            }
            int suggestible = missing.Count(m => SuggestValue(x, m).value > 0);
            if (suggestible > 0) r.Add(FixLink(x, "pinall", domain, x.L("diag-fix-pinall", suggestible)), EmcLineStyle.Text);
            NextSteps(x, domain, noRecipe.Count > 0 ? noRecipe : missing);
            r.Section(x.L("diag-sec-missinglist", missing.Count));
            r.Add(x.L("diag-missinglist-help"), EmcLineStyle.Note);
            foreach (CollectibleObject c in missing.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).Take(60))
            {
                r.Add(EmcReport.Ref(c) + "  " + Link(ActionItem, c.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 1);
            }
            if (missing.Count > 60) x.Note(x.L("diag-more", missing.Count - 60));
            Back(x);
        }

        static void Families(Ctx x, List<CollectibleObject> items, int max)
        {
            foreach (var g in items.GroupBy(c => c.Code.Path.Split('-')[0]).OrderByDescending(g => g.Count()).Take(max))
            {
                x.R.Add(g.Key + " (" + g.Count() + "): " + string.Join(", ", g.Take(3).Select(EmcReport.Ref)) + (g.Count() > 3 ? ", ..." : ""), EmcLineStyle.Text, 2);
            }
        }

        /// <summary>The compatibility file for a mod, if there is one, and its state.</summary>
        static void CompatFor(Ctx x, string domain)
        {
            var file = x.Mod.Rules.Compat.FirstOrDefault(f => f.RequiredMods.Any(m => string.Equals(m, domain, StringComparison.OrdinalIgnoreCase)));
            if (file == null)
            {
                if (domain != "game") x.Note(x.L("diag-compat-nofile", domain), 0);
                return;
            }
            if (file.Disabled)
            {
                x.Warn(x.L("diag-compat-filedisabled", file.Name));
                x.Fix(x.L("diag-compat-filedisabled-fix", file.Name));
                x.R.Add(FixLink(x, "compat", file.Source, x.L("diag-fix-compat", file.Name)), EmcLineStyle.Text, 1);
            }
            else if (file.MissingMod != null) x.Note(x.L("diag-compat-filewaiting", file.Name, file.MissingMod), 0);
            else x.Ok(x.L("diag-compat-fileactive", file.Name, x.Report.CompatUsed.TryGetValue(file.Source, out int n) ? n : 0, file.RuleCount));
        }

        // ---------------------------------------------------------------- one block or item

        static void Item(Ctx x, ItemStack stack, bool held)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            CollectibleObject c = stack.Collectible;
            VintageEConfig cfg = x.Mod.Config;
            r.Section(x.L("diag-sec-item", EmcExplain.NameOf(c)));
            r.Add(x.L("diag-item-code", c.Code, ModName(x, c.Code.Domain)), EmcLineStyle.Note);

            double plain = x.Mod.Emc.GetValue(c);
            double each = x.Mod.Emc.GetValue(stack);
            bool blacklisted = report.Blacklisted.Contains(c);
            bool free = report.Free.Contains(c);

            if (free)
            {
                x.Note(x.L("diag-item-free"), 0);
                x.Fix(x.L("diag-item-free-fix"));
            }
            else if (blacklisted)
            {
                x.Warn(x.L("diag-item-blacklisted"));
                string zero = cfg.FixedValues.Where(kv => kv.Value <= 0 && CollectibleIndex.Matches(kv.Key, c)).Select(kv => kv.Key).LastOrDefault()
                           ?? cfg.BaseValues.Where(kv => kv.Value <= 0 && CollectibleIndex.Matches(kv.Key, c)).Select(kv => kv.Key).LastOrDefault();
                string listed = cfg.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
                string file = x.Mod.Rules.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
                JsonObject attr = c.Attributes?["emcValue"];
                string cfgKey = VintageEModSystem.ConfigKey(c);
                if (zero != null) { r.Add(x.L("diag-bl-zero", zero, VintageEModSystem.ConfigFile), EmcLineStyle.Text, 1); x.Fix(x.L("diag-bl-zero-fix")); r.Add(FixLink(x, "unpin", zero, x.L("diag-fix-unpin")), EmcLineStyle.Text, 1); }
                else if (listed != null) { r.Add(x.L("diag-bl-config", listed, VintageEModSystem.ConfigFile), EmcLineStyle.Text, 1); x.Fix(x.L("diag-bl-config-fix", listed)); r.Add(FixLink(x, "allow", listed == cfgKey ? cfgKey : cfgKey, x.L("diag-fix-allow")), EmcLineStyle.Text, 1); }
                else if (attr != null && attr.Exists && attr.AsDouble(-1) == 0) { r.Add(x.L("diag-bl-attribute"), EmcLineStyle.Text, 1); x.Fix(x.L("diag-bl-attribute-fix")); }
                else if (file != null) { r.Add(x.L("diag-bl-file", file), EmcLineStyle.Text, 1); x.Fix(x.L("diag-bl-file-fix", cfgKey)); r.Add(FixLink(x, "allow", cfgKey, x.L("diag-fix-allow")), EmcLineStyle.Text, 1); }
                else x.Fix(x.L("diag-bl-fix-generic"));
                r.Add(Link("setvalue", PlayerEmc.KeyOf(c), x.L("diag-fix-setvalue")), EmcLineStyle.Text, 1);
            }
            else if (!report.Values.ContainsKey(c))
            {
                x.Warn(x.L("diag-item-novalue"));
                bool gray = report.Gray.Contains(c);
                if (gray)
                {
                    r.Add(x.L("diag-item-gray"), EmcLineStyle.Text, 1);
                    string g = cfg.GrayList.LastOrDefault(p => CollectibleIndex.Matches(p, c)) ?? x.Mod.Rules.GrayList.LastOrDefault(p => CollectibleIndex.Matches(p, c));
                    if (g != null) x.Note(x.L("diag-item-gray-pattern", g), 2);
                    if (cfg.GrayList.Contains(VintageEModSystem.ConfigKey(c))) r.Add(FixLink(x, "ungray", VintageEModSystem.ConfigKey(c), x.L("diag-fix-ungray")), EmcLineStyle.Text, 1);
                }
                MissingItem(x, c);
                SuggestPin(x, c);
                r.Add(Link("setvalue", PlayerEmc.KeyOf(c), x.L("diag-fix-setvalue")), EmcLineStyle.Text, 1);
            }
            else
            {
                x.Ok(x.L("diag-item-hasvalue", F(plain)));
                report.Origins.TryGetValue(c, out string origin);
                if (origin != null) x.Note(x.L("diag-item-origin", Linkify(x, origin)), 1);
                if (held && Math.Abs(each - plain) > 1e-6)
                {
                    r.Add(x.L("diag-item-stackdiffers", F(each), F(plain)), each <= 0 ? EmcLineStyle.Warn : EmcLineStyle.Text);
                    StackReasons(x, stack, plain, each);
                }
                if (x.Mod.Emc.IsNoTransmute(c))
                {
                    x.Warn(x.L("diag-item-notransmute"));
                    string p = cfg.NoTransmute.LastOrDefault(q => CollectibleIndex.Matches(q, c));
                    x.Fix(p != null ? x.L("diag-item-notransmute-fix-config", p) : x.L("diag-item-notransmute-fix-file"));
                    if (p != null) r.Add(FixLink(x, "notransmute", p, x.L("diag-fix-notransmute")), EmcLineStyle.Text, 1);
                }
                if (x.Player != null && !PlayerEmc.Knows(x.Player, PlayerEmc.KeyOf(c)))
                {
                    x.Note(x.L("diag-item-unknown"), 0);
                    r.Add(FixLink(x, "learn", PlayerEmc.KeyOf(c), x.L("diag-fix-learn", EmcExplain.NameOf(c))), EmcLineStyle.Text, 1);
                }
                r.Add(x.L("diag-item-seems-fine"), EmcLineStyle.Note);
                r.Add(x.L("diag-item-clientcheck"), EmcLineStyle.Note);
            }

            // Pins and value-file entries that nearly match (a typo).
            if (x.Index != null && !report.Values.ContainsKey(c))
            {
                string family = c.Code.Domain + ":" + c.Code.Path.Split('-')[0];
                var near = cfg.BaseValues.Keys.Concat(cfg.FixedValues.Keys).Where(k => !x.Index.Match(k).Any() && k.Replace("item:", "").Replace("block:", "").StartsWith(family, StringComparison.OrdinalIgnoreCase)).ToList();
                if (near.Count > 0)
                {
                    x.Warn(x.L("diag-item-nearkeys", string.Join(", ", near.Take(5))));
                    x.Fix(x.L("diag-item-nearkeys-fix", VintageEModSystem.ConfigKey(c)));
                }
            }

            // Variants that do have a value.
            if (!report.Values.ContainsKey(c) && !blacklisted && x.Index != null)
            {
                string family = c.Code.Domain + ":" + c.Code.Path.Split('-')[0];
                var siblings = x.Index.Match(family + "-*").Where(s => s != c && report.Values.ContainsKey(s)).Take(4).ToList();
                if (siblings.Count > 0)
                {
                    r.Add(x.L("diag-item-siblings", string.Join(", ", siblings.Select(s => EmcReport.Ref(s) + " " + F(report.Values[s])))), EmcLineStyle.Note);
                    if (c.VariantStrict != null)
                    {
                        var never = c.VariantStrict.Keys.Where(g => x.Mod.Rules.NeverAcrossGroups.Contains(g) || x.Mod.Rules.NeverAcrossGroups.Contains(c.Code.Path.Split('-')[0] + ":" + g)).ToList();
                        if (never.Count > 0) x.Note(x.L("diag-item-neveracross", string.Join(", ", never)), 1);
                    }
                }
            }

            if (!report.Values.ContainsKey(c) && !blacklisted && !free && !report.LoopOf.ContainsKey(c)) NextSteps(x, c.Code.Domain, new[] { c });
            r.Section(x.L("diag-sec-next"));
            r.Add(x.L("diag-item-full") + " " + EmcReport.Ref(c), EmcLineStyle.Text);
            if (report.Values.ContainsKey(c)) r.Add(Link(ActionTrace, c.Code.ToString(), x.L("diag-q-trace")));
            r.Add(Link(ActionMod, c.Code.Domain, x.L("diag-q-thismod", ModName(x, c.Code.Domain))));
            r.Add(Link(ActionSporadic, "", x.L("diag-q-sporadic")));
            r.Add(Link(ActionSymptom, "", x.L("diag-q-symptoms")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        static string Linkify(Ctx x, string origin)
        {
            if (string.IsNullOrEmpty(origin) || x.Index == null) return origin ?? "";
            return System.Text.RegularExpressions.Regex.Replace(origin, @"(?<![\w\[])([a-z0-9_]+:[a-z0-9_\-\.]+)", m =>
            {
                CollectibleObject c = PlayerEmc.Resolve(x.Sapi.World, m.Value);
                return c?.Code != null ? EmcReport.Ref(c) : m.Value;
            });
        }

        /// <summary>Why an item without a value has none, each cause with its fix.</summary>
        static void MissingItem(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            string cfgKey = VintageEModSystem.ConfigKey(c);
            if (report.LoopOf.TryGetValue(c, out int loop) && loop >= 0 && loop < report.Loops.Count)
            {
                r.Add(x.L("diag-item-inloop", string.Join(" → ", report.Loops[loop].Take(8).Select(EmcReport.Ref))), EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-item-inloop-fix", EmcReport.Ref(report.Loops[loop][0]), VintageEModSystem.ConfigKey(report.Loops[loop][0])));
                return;
            }
            if (!report.MissingRecipes.TryGetValue(c, out var recipes) || recipes.Count == 0)
            {
                r.Add(x.L("diag-item-norecipe"), EmcLineStyle.Text, 1);
                if (c.Code.Domain == "game") x.Note(x.L("diag-item-norecipe-game"), 1);
                else
                {
                    x.Note(x.L("diag-item-norecipe-mod", ModName(x, c.Code.Domain)), 1);
                    CompatFor(x, c.Code.Domain);
                }
                x.Fix(x.L("diag-item-norecipe-fix1", cfgKey));
                x.Fix(x.L("diag-item-norecipe-fix2", cfgKey, VintageEModSystem.ConfigFile));
                if (c.Code.Domain != "game") x.Fix(x.L("diag-item-norecipe-fix3", c.Code.Domain));
                return;
            }
            bool Valued(CollectibleObject o) => o != null && (report.Values.ContainsKey(o) || report.Free.Contains(o));
            var lacking = recipes.SelectMany(conv => MissingAnalysis.Lacking(conv, Valued)).Distinct().ToList();
            r.Add(x.L("diag-item-waiting", recipes.Count, lacking.Count), EmcLineStyle.Text, 1);
            foreach (CollectibleObject part in lacking.Take(8))
            {
                string why = report.Blacklisted.Contains(part) ? x.L("diag-blocker-blacklisted")
                    : report.LoopOf.ContainsKey(part) ? x.L("diag-blocker-loop")
                    : report.MissingWhy.TryGetValue(part, out string w) && w == MissingAnalysis.NoRecipe ? x.L("diag-blocker-norecipe")
                    : x.L("diag-blocker-waiting");
                r.Add(EmcReport.Ref(part) + " - " + why + "  " + Link(ActionItem, part.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Text, 2);
            }
            if (lacking.Count > 8) x.Note(x.L("diag-more", lacking.Count - 8), 2);
            x.Fix(x.L("diag-item-waiting-fix"));
            x.Fix(x.L("diag-item-waiting-fix2", cfgKey));
        }

        /// <summary>Why this exact stack is worth something else than the plain item.</summary>
        static void StackReasons(Ctx x, ItemStack stack, double plain, double each)
        {
            EmcReport r = x.R;
            CollectibleObject c = stack.Collectible;
            var data = x.Mod.Emc.StackData;
            if (x.Mod.Config.DurabilityScalesEmc && c.GetMaxDurability(stack) > 1 && c.GetRemainingDurability(stack) < c.GetMaxDurability(stack))
            {
                r.Add(x.L("diag-stack-worn", c.GetRemainingDurability(stack), c.GetMaxDurability(stack)), EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-stack-worn-fix"));
            }
            if (data.CarriesData(c))
            {
                StackDataMode mode = data.ModeOf(c);
                StackDataKind kind = data.KindOf(c);
                r.Add(x.L("diag-stack-data", kind, mode), EmcLineStyle.Text, 1);
                if (kind == StackDataKind.Material && x.Mod.Emc.ItemValue(stack) <= 0)
                {
                    x.Warn(x.L("diag-stack-material-novalue"));
                    x.Fix(x.L("diag-stack-material-fix", VintageEModSystem.ConfigKey(c)));
                }
                if (mode == StackDataMode.Full)
                {
                    var unvalued = new List<ItemStack>();
                    foreach (ItemStack inner in StackContents.NestedStacks(stack))
                    {
                        if (inner.Collectible == null && !inner.ResolveBlockOrItem(x.Sapi.World)) continue;
                        if (inner.Collectible != null && x.Mod.Emc.GetValue(inner) <= 0) unvalued.Add(inner);
                    }
                    if (unvalued.Count > 0)
                    {
                        x.Warn(x.L("diag-stack-contents-novalue", string.Join(", ", unvalued.Take(5).Select(s => EmcReport.Ref(s.Collectible)))));
                        if (x.Mod.Config.ProtectContainersWithContents) x.Note(x.L("diag-stack-protected"), 1);
                        x.Fix(x.L("diag-stack-contents-fix"));
                    }
                    else x.Note(x.L("diag-stack-contents", F(x.Mod.Emc.ContentsValue(stack))), 1);
                }
                x.Note(x.L("diag-stack-mode-fix", c.Code), 1);
            }
            if (c is IEmcStorageItem) x.Note(x.L("diag-stack-klein"), 1);
        }

        // ---------------------------------------------------------------- values that come and go

        static void Sporadic(Ctx x, ItemStack held)
        {
            EmcReport r = x.R;
            VintageEConfig cfg = x.Mod.Config;
            r.Section(x.L("diag-sec-sporadic"));
            r.Add(x.L("diag-sporadic-help"), EmcLineStyle.Note);

            r.Add(x.L("diag-sp-wear", cfg.DurabilityScalesEmc ? x.L("diag-on") : x.L("diag-off")), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-wear-why"));
            r.Add(x.L("diag-sp-contents", cfg.ProtectContainersWithContents ? x.L("diag-on") : x.L("diag-off")), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-contents-why"));
            r.Add(x.L("diag-sp-material"), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-material-why"));
            r.Add(x.L("diag-sp-knowledge", cfg.MaxExactKnowledge), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-knowledge-why"));
            r.Add(x.L("diag-sp-notransmute", x.Mod.Emc.NoTransmute.Count), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-notransmute-why"));
            r.Add(x.L("diag-sp-remap"), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-remap-why"));
            r.Add(x.L("diag-sp-cache", cfg.CacheEmcValues ? x.L("diag-on") : x.L("diag-off")), EmcLineStyle.Text);
            x.Note(x.L("diag-sp-cache-why"));
            if (x.Report.UnstableRemoved > 0 || x.Report.LoopFrozen > 0)
            {
                r.Add(x.L("diag-sp-unstable", x.Report.LoopFrozen, x.Report.UnstableRemoved), EmcLineStyle.Warn);
                x.Note(x.L("diag-sp-unstable-why"));
            }

            if (held?.Collectible?.Code != null)
            {
                r.Section(x.L("diag-sec-heldstack", EmcExplain.NameOf(held.Collectible)));
                double plain = x.Mod.Emc.GetValue(held.Collectible);
                double each = x.Mod.Emc.GetValue(held);
                r.Add(x.L("diag-held-values", F(each), F(plain)), Math.Abs(each - plain) > 1e-6 ? EmcLineStyle.Warn : EmcLineStyle.Total);
                if (Math.Abs(each - plain) > 1e-6) StackReasons(x, held, plain, each);
                else if (plain <= 0) r.Add(x.L("diag-held-novalue") + " " + Link(ActionItem, held.Collectible.Code.ToString(), x.L("diag-why-link")), EmcLineStyle.Warn);
                else x.Ok(x.L("diag-held-same"));
                if (x.Player != null)
                {
                    bool knows = PlayerEmc.Knows(x.Player, PlayerEmc.KeyOf(held.Collectible));
                    r.Add(knows ? x.L("diag-held-known") : x.L("diag-held-unknown"), EmcLineStyle.Note);
                }
            }
            else r.Add(x.L("diag-sporadic-holdit"), EmcLineStyle.Note);

            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionSporadic, "", x.L("diag-q-sporadic-again")));
            r.Add(Link(ActionHand, "", x.L("diag-q-item")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        // ---------------------------------------------------------------- the questions

        static void Back(Ctx x)
        {
            x.R.Section(x.L("diag-sec-next"));
            x.R.Add(Link(ActionMods, "", x.L("diag-q-mods")));
            x.R.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        /// <summary>What the player can look into next, as links; the held item is named so "this one" is unambiguous.</summary>
        /// <summary>
        /// What to do beyond this server when a mod's items can't be valued automatically: a comment on the Mod DB page
        /// asking for the mod to be covered, with the codes to paste, and a report to the other mod if it hides its recipes.
        /// </summary>
        static void NextSteps(Ctx x, string domain, IList<CollectibleObject> items)
        {
            if (items == null || items.Count == 0) return;
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-nextsteps"));
            string modName = ModName(x, domain);
            string codes = string.Join(", ", items.Take(12).Select(c => c.Code.ToString())) + (items.Count > 12 ? ", ... (" + items.Count + ")" : "");
            if (domain == "game" || domain == "vintagee")
            {
                r.Add(x.L("diag-next-game"), EmcLineStyle.Text);
                r.Add(x.L("diag-next-comment", VintageEModSystem.ModDbUrl), EmcLineStyle.Text, 1);
                x.Note(x.L("diag-next-paste-game", x.Mod.Version ?? "?", codes), 2);
            }
            else
            {
                r.Add(x.L("diag-next-mod", modName), EmcLineStyle.Text);
                r.Add(x.L("diag-next-comment", VintageEModSystem.ModDbUrl), EmcLineStyle.Text, 1);
                string version = "?";
                try { version = x.Sapi.ModLoader.GetMod(domain)?.Info?.Version ?? "?"; } catch { }
                x.Note(x.L("diag-next-paste-mod", x.Mod.Version ?? "?", modName, version, codes), 2);
                r.Add(x.L("diag-next-modauthor"), EmcLineStyle.Text, 1);
                r.Add(x.L("diag-next-meanwhile"), EmcLineStyle.Text, 1);
            }
            r.Add(Link(ActionBugReport, "", x.L("diag-q-bugreport")), EmcLineStyle.Text, 1);
        }

        static void Questions(Ctx x, ItemStack held, EmcDiagnoseRequestPacket client = null)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-questions"));
            r.Add(x.L("diag-questions-help"), EmcLineStyle.Note);
            r.Add(Link(ActionNoValue, "", x.L("diag-q-novalue")));
            r.Add(Link(ActionSymptom, "", x.L("diag-q-symptoms")));
            r.Add(Link(ActionWatch, "", x.L("diag-q-watch")));
            if (held?.Collectible?.Code != null)
            {
                r.Add(Link(ActionHand, "", x.L("diag-q-held", EmcExplain.NameOf(held.Collectible))));
            }
            else r.Add(Link(ActionHand, "", x.L("diag-q-item")));
            r.Add(Link(ActionMods, "", x.L("diag-q-mods")));
            r.Add(Link(ActionSporadic, "", x.L("diag-q-sporadic")));
            r.Add(Link(ActionMapping, "", x.L("diag-q-mapping")));
            r.Add(Link(ActionChecks, "", x.L("diag-q-checks")));
            r.Add(Link(ActionChanges, "", x.L("diag-q-changes")));
            r.Add(Link(ActionClean, "", x.L("diag-q-clean")));
            r.Add(Link(ActionPlayers, "", x.L("diag-q-players")));
            r.Add(Link(ActionBugReport, "", x.L("diag-q-bugreport")));
            if (x.Report != null && x.Report.Missing.Count > 0) r.Add(x.L("diag-q-missinglist"), EmcLineStyle.Note);
            r.Add(x.L("diag-q-console"), EmcLineStyle.Note);
        }
    }
}
