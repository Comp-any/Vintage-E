using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The doctor handed over to the mod Vintage Doctor (1.26.0, <see cref="DoctorBridge"/>).
    ///
    /// The first part runs on every test server: the bridge is asked directly, the way Vintage Doctor asks over the
    /// event bus, so it needs no Vintage Doctor. The second part only runs when Vintage Doctor is installed on the
    /// test server (the workflow's "doctor" input): there its own /doctor command is used from the console, and what
    /// it writes out has to be Vintage-E's pages.
    /// </summary>
    public partial class SelfTest
    {
        const string HandoverPageRequest = "vintagee-selftest-page", HandoverFixRequest = "vintagee-selftest-fix";
        DoctorBridge handoverBridge;
        ITreeAttribute handoverPage, handoverFix;

        static string[] Texts(ITreeAttribute tree) => (tree?["texts"] as StringArrayAttribute)?.value ?? Array.Empty<string>();

        /// <summary>A question as Vintage Doctor puts it on the event bus, from the server's console.</summary>
        static ITreeAttribute DoctorAsks(string topic, string argument, string request = null)
        {
            var tree = new TreeAttribute();
            tree.SetString("side", "server");
            tree.SetString("topic", topic);
            tree.SetString("argument", argument ?? "");
            tree.SetString("language", "en");
            tree.SetString("player", "");
            if (request != null) tree.SetString("request", request);
            return tree;
        }

        static ITreeAttribute DoctorFixes(string id, string argument, string request)
        {
            ITreeAttribute tree = DoctorAsks("", argument, request);
            tree.SetString("id", id);
            return tree;
        }

        /// <summary>Every link in a page is one Vintage Doctor knows: {{open|...}} or {{fix|...}} with four parts, and [[item:...]] or [[block:...]].</summary>
        static bool LinksAreTheDoctors(IEnumerable<string> texts, out string bad)
        {
            bad = null;
            foreach (string t in texts)
            {
                string rest = Regex.Replace(t, @"\{\{(open|fix|copy)\|[^|{}]*\|[^|{}]*\|[^{}|]*\}\}", "");
                rest = Regex.Replace(rest, @"\[\[(item|block):[^\[\]]+\]\]", "");
                if (rest.Contains("{{") || rest.Contains("[[")) { bad = t; return false; }
            }
            return true;
        }

        void OnHandoverDelivered(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree)) return;
            string request = tree.GetString("request");
            if (request == HandoverPageRequest) handoverPage = tree;
            if (request == HandoverFixRequest) handoverFix = tree;
        }

        void CheckDoctorHandover()
        {
            EnumHandling handling = EnumHandling.PassThrough;
            Check(DoctorBridge.AtLeast("1.2.0", 1, 2) && DoctorBridge.AtLeast("v1.10.3-rc.1", 1, 2) && DoctorBridge.AtLeast("2.0", 1, 2) && !DoctorBridge.AtLeast("1.1.9", 1, 2)
                && !DoctorBridge.AtLeast(null, 1, 2) && !DoctorBridge.AtLeast("soon", 1, 2), "handover: a Vintage Doctor new enough to take the doctor over (1.2.0) is told from an older one");

            // The links of a report, turned into Vintage Doctor's.
            string converted = DoctorBridge.Convert("See [[i:game:ingot-copper]] and [[b:game:anvil-copper]] {{fix-pin|i:game:ingot-copper;item;game:ingot-copper|Pin it}} "
                + "{{setvalue|i:game:stick|Set it}} {{item|game:stick|Its page}} {{overview||Back}} {{log||The log}} {{startup||Start-up}} {{mods||Mods}}");
            Check(converted == "See [[item:game:ingot-copper]] and [[block:game:anvil-copper]] {{fix|vintagee:pin|i:game:ingot-copper|Pin it}} "
                + "{{fix|c:bus:vintagee:setvalue|i:game:stick|Set it}} {{open|emc|item game:stick|Its page}} {{open|emc||Back}} {{open|log|vintagee|The log}} {{open|startup||Start-up}} {{open|emc|mods|Mods}}",
                "handover: a report's links become Vintage Doctor's (" + converted + ")");
            Check(DoctorBridge.Convert("plain words, 1 {2} three") == "plain words, 1 {2} three" && DoctorBridge.Convert(null) == "", "handover: a line without links is left as it is");
            DoctorBridge.Split("item game:ingot-copper", out string action, out string arg);
            DoctorBridge.Split("  ", out string noAction, out string noArg);
            Check(action == "item" && arg == "game:ingot-copper" && noAction == "" && noArg == "" && DoctorBridge.Join("item", "game:ingot-copper") == "item game:ingot-copper"
                && DoctorBridge.Join("overview", "") == "" && DoctorBridge.Join("", null) == "", "handover: a page's argument is the doctor's action and what it is about");

            // The check-up as Vintage-E's own window shows it, and as it is handed over.
            EmcReport own = EmcDiagnose.Run(mod, sapi, null, "en", "", "", null);
            EmcReport handed = EmcDiagnose.Run(mod, sapi, null, "en", "", "", null, null, null, handover: true);
            int ownStartup = own.Lines.Count(l => l.Text.Contains("{{startup|")), handedStartup = handed.Lines.Count(l => l.Text.Contains("{{startup|")), handedLog = handed.Lines.Count(l => l.Text.StartsWith("{{log|", StringComparison.Ordinal));
            string startupClean = Lang.GetL("en", "vintagee:diag-startup-clean");
            Check(ownStartup <= 1 && handedStartup == 1 && handedLog == 1 && handed.Lines.Count >= 5 && !handed.Lines.Any(l => l.Text.Contains(startupClean)),
                $"handover: handed over, the check-up leaves the log and other mods' start-up to Vintage Doctor, with one link to each ({handed.Lines.Count} lines, {own.Lines.Count} in Vintage-E's own; start-up links {handedStartup} and {ownStartup}, log links {handedLog})");
            string logClean = Lang.GetL("en", "vintagee:diag-log-clean");
            Check(!handed.Lines.Any(l => l.Text.Contains(logClean)), "handover: the log's own card isn't in the handed-over check-up");

            var written = new TreeAttribute();
            DoctorBridge.Write(handed, written);
            string[] texts = Texts(written);
            byte[] styles = written.GetBytes("styles", null), indents = written.GetBytes("indents", null);
            bool links = LinksAreTheDoctors(texts, out string badLine);
            Check(texts.Length == handed.Lines.Count && styles != null && indents != null && styles.Length == texts.Length && indents.Length == texts.Length && links
                && texts.Any(t => t.Contains("{{open|emc|mods|")) && texts.Any(t => t.Contains("{{open|log|vintagee|")) && texts.Any(t => t.Contains("{{open|startup||"))
                && styles.Contains((byte)EmcLineStyle.Heading) && styles.Contains((byte)EmcLineStyle.Check),
                "handover: a whole report is written the way Vintage Doctor reads a page: " + texts.Length + " lines with styles and indents, every link one of its own" + (badLine != null ? " (not this one: " + badLine + ")" : ""));

            // Every page the doctor has, handed over: built, and every link in it one Vintage Doctor knows.
            int pages = 0;
            string firstBad = null;
            foreach (var (a, b) in new[] { ("mapping", ""), ("mods", ""), ("item", "game:ingot-copper"), ("item", "game:nosuchitem"), ("checks", ""), ("health", ""), ("machines", ""), ("emccraft", ""),
                ("methods", ""), ("symptom", ""), ("novalue", ""), ("watch", ""), ("players", ""), ("changes", ""), ("clean", ""), ("bugreport", ""), ("client", ""), ("trace", "game:ingot-copper"), ("short", "") })
            {
                try
                {
                    var tree = new TreeAttribute();
                    DoctorBridge.Write(EmcDiagnose.Run(mod, sapi, null, "en", a, b, null, null, null, handover: true), tree);
                    bool fine = LinksAreTheDoctors(Texts(tree), out string bad);
                    if (Texts(tree).Length >= 2 && fine) pages++;
                    else firstBad ??= a + " " + b + ": " + (bad ?? Texts(tree).Length + " lines");
                }
                catch (Exception e) { firstBad ??= a + " " + b + " threw " + e.Message; }
            }
            Check(firstBad == null, "handover: every page of the doctor can be handed over (" + pages + " tried" + (firstBad != null ? "; not " + firstBad : "") + ")");

            // The bridge, asked the way Vintage Doctor asks.
            handoverBridge = DoctorBridge.ForTest(mod, sapi);
            ITreeAttribute which = DoctorAsks("", "");
            handoverBridge.OnTopics(DoctorBridge.TopicsEvent, ref handling, which);
            ITreeAttribute emc = (which["topics"] as ITreeAttribute)?[DoctorBridge.Topic] as ITreeAttribute;
            string question = emc?.GetString("question");
            Check(emc != null && emc.GetString("mod") == VintageEModSystem.ModId && emc.GetBool("ownHeadings") && question != null && question.IndexOf(' ') < 0
                && Lang.GetL("en", question) != question, "handover: the EMC page is offered, with its question as a text Vintage Doctor can translate (" + question + ")");
            ITreeAttribute clientSide = DoctorAsks("", "");
            clientSide.SetString("side", "client");
            handoverBridge.OnTopics(DoctorBridge.TopicsEvent, ref handling, clientSide);
            Check(clientSide["topics"] == null, "handover: the page is the server's; a player's own side offers nothing in its place");

            ITreeAttribute checkup = DoctorAsks("overview", "");
            handoverBridge.OnCollect(DoctorBridge.CollectEvent, ref handling, checkup);
            ITreeAttribute card = (checkup["results"] as ITreeAttribute)?[DoctorBridge.FixPrefix + "checkup"] as ITreeAttribute;
            Check(card != null && card.GetString("link") == DoctorBridge.Topic && !string.IsNullOrEmpty(card.GetString("title")) && card.GetString("level") == (mod.CheckupProblems > 0 ? "warning" : "ok"),
                "handover: a card for Vintage Doctor's check-up, with a link to the EMC pages (" + card?.GetString("level") + ": " + card?.GetString("title") + ")");
            ITreeAttribute othersPage = DoctorAsks("mod", "game");
            othersPage.SetString("mod", "game");
            handoverBridge.OnCollect(DoctorBridge.CollectEvent, ref handling, othersPage);
            Check(othersPage["results"] == null, "handover: nothing on another mod's page");

            ITreeAttribute copper = DoctorAsks("item", "game:ingot-copper");
            copper.SetString("subject", "item:game:ingot-copper");
            handoverBridge.OnAbout(DoctorBridge.AboutEvent, ref handling, copper);
            ITreeAttribute worth = (copper["results"] as ITreeAttribute)?[DoctorBridge.FixPrefix + "value"] as ITreeAttribute;
            Check(worth != null && worth.GetString("title") == "Worth 128 EMC" && worth.GetString("link") == DoctorBridge.Topic && worth.GetString("linkArgument") == "item game:ingot-copper",
                "handover: a card on Vintage Doctor's page about an item says what it is worth (" + worth?.GetString("title") + ")");
            ITreeAttribute trader = DoctorAsks("item", "");
            trader.SetString("subject", "item:game:creature-trader-female-artisan-cold");
            handoverBridge.OnAbout(DoctorBridge.AboutEvent, ref handling, trader);
            ITreeAttribute noValue = (trader["results"] as ITreeAttribute)?[DoctorBridge.FixPrefix + "value"] as ITreeAttribute;
            ITreeAttribute nothing = DoctorAsks("item", "");
            nothing.SetString("subject", "item:game:nosuchitem");
            handoverBridge.OnAbout(DoctorBridge.AboutEvent, ref handling, nothing);
            Check((noValue == null || noValue.GetString("title") == Lang.GetL("en", "vintagee:doctor-about-novalue")) && nothing["results"] == null,
                "handover: something without a value says so, and something that doesn't exist gets no card (" + (noValue?.GetString("title") ?? "the trader item isn't in this game") + ")");

            // A page that reads blocks comes at once; one that is worked out off the game thread comes later.
            ITreeAttribute live = DoctorAsks(DoctorBridge.Topic, "machines", "vintagee-selftest-live");
            handoverBridge.OnPage(DoctorBridge.PageEvent, ref handling, live);
            Check(!live.GetBool("later") && Texts(live).Length >= 2 && LinksAreTheDoctors(Texts(live), out _), "handover: a page that reads the world is there at once (" + Texts(live).Length + " lines)");
            sapi.Event.RegisterEventBusListener(OnHandoverDelivered, 0.5, DoctorBridge.DeliverEvent);
            ITreeAttribute slow = DoctorAsks(DoctorBridge.Topic, "item game:ingot-copper", HandoverPageRequest);
            handoverBridge.OnPage(DoctorBridge.PageEvent, ref handling, slow);
            Check(slow.GetBool("later") && slow.GetInt("seconds") == 180 && Texts(slow).Length == 0, "handover: a page that takes a while says it will come later");
            ITreeAttribute notOurs = DoctorAsks("someone-elses-page", "");
            handoverBridge.OnPage(DoctorBridge.PageEvent, ref handling, notOurs);
            Check(Texts(notOurs).Length == 0 && !notOurs.GetBool("later"), "handover: another mod's page is left alone");

            // Fixes.
            ITreeAttribute fix = DoctorFixes(DoctorBridge.FixPrefix + "clearwatch", "", "vintagee-selftest-fix-now");
            handoverBridge.OnFix(DoctorBridge.FixEvent, ref handling, fix);
            Check(fix.GetString("result") == Lang.GetL("en", "vintagee:diag-done-clearwatch") && !fix.GetBool("failed") && !fix.GetBool("later"), "handover: a fix runs and says what it did (" + fix.GetString("result") + ")");
            ITreeAttribute othersFix = DoctorFixes("othermod:something", "", "vintagee-selftest-fix-other");
            handoverBridge.OnFix(DoctorBridge.FixEvent, ref handling, othersFix);
            Check(othersFix.GetString("result") == null, "handover: another mod's fix is left alone");
            var failed = new TreeAttribute();
            DoctorBridge.SetResult(failed, "⚠ That didn't work");
            var worked = new TreeAttribute();
            DoctorBridge.SetResult(worked, "Done");
            Check(failed.GetString("result") == "That didn't work" && failed.GetBool("failed") && worked.GetString("result") == "Done" && !worked.GetBool("failed"),
                "handover: a fix that didn't work is passed on as failed, without the mark Vintage Doctor adds itself");

            bool installed = DoctorBridge.Installed(sapi);
            Check((mod.Doctor != null) == installed && sapi.World.Config.GetBool(DoctorBridge.WorldKey, false) == installed,
                "handover: on when Vintage Doctor is installed and off when it isn't, and players' games are told through the world's settings (installed here: " + installed + ")");
        }

        /// <summary>
        /// The copy button under each finding (1.26.7): which findings get one, what it holds, that it stays out of
        /// text for files and the console, and that the bridge writes it the way Vintage Doctor reads a copy button.
        /// At the end of the test run (it builds every page once more).
        /// </summary>
        void CheckCopyButtons()
        {
            Item copper = sapi.World.GetItem(new AssetLocation("game:ingot-copper"));
            string copperRef = EmcReport.Ref(copper), copperName = EmcExplain.NameOf(copper) + " (game:ingot-copper)";
            var r = new EmcReport { Kind = EmcReport.DiagnoseKind, Key = "machines|" };
            r.Section("Machines here");
            r.Add("✓ Nothing wrong with the furnaces", EmcLineStyle.Check);
            r.Add("All three were looked at.", EmcLineStyle.Note, 1);
            r.Add("⚠ The collector at 1, 2, 3 makes nothing {{machines||Look again}}", EmcLineStyle.Check);
            r.Add("It holds " + copperRef + " and a | bar {curly}", EmcLineStyle.Note, 1);
            r.Add("→ Give it light {{fix-light|here|Do it}}", EmcLineStyle.Text, 1);
            r.Add(EmcDiagnose.ProblemMark + " A patch failed", EmcLineStyle.Check);
            r.Add("• Just so you know", EmcLineStyle.Check);
            r.Add("• Pick one", EmcLineStyle.Check);
            r.Add("{{mods||The mods}} {{health||Health}}", EmcLineStyle.Text, 1);
            r.Add("• Three things", EmcLineStyle.Check);
            r.Add("one of them", EmcLineStyle.Note, 1);
            r.Add("A plain line", EmcLineStyle.Text);
            int linesBefore = r.Lines.Count;
            string plainBefore = r.PlainText(sapi.World);
            int added = EmcDiagnose.AddCopyButtons(mod, sapi, "en", r);
            bool IsCopy(EmcReportLine l) => l.Text.StartsWith("{{" + EmcReport.CopyAction + "|", StringComparison.Ordinal);
            int At(string start) => r.Lines.FindIndex(l => l.Text.StartsWith(start, StringComparison.Ordinal));
            int warn = At("⚠"), problem = At(EmcDiagnose.ProblemMark), three = At("• Three");
            bool placed = added == 3 && r.Lines.Count == linesBefore + 3 && r.Lines.Count(IsCopy) == 3
                && IsCopy(r.Lines[warn + 3]) && IsCopy(r.Lines[problem + 1]) && IsCopy(r.Lines[three + 2])
                && r.Lines.Where(IsCopy).All(l => l.Indent == 1 && l.Style == EmcLineStyle.Text);
            Check(placed, $"a copy button comes after each warning and problem and each note with something written behind it, and after nothing else ({added} added: a pass, a bare note and a note that is only buttons get none)");

            Match own = warn < 0 || warn + 3 >= r.Lines.Count ? Match.Empty : EmcReport.ActionToken.Match(r.Lines[warn + 3].Text);
            string text = own.Groups[2].Value, label = own.Groups[3].Value;
            string[] wanted =
            {
                "Vintage-E " + mod.Version + ", Vintage Story " + GameVersion.ShortGameVersion, "/emc diagnose machines", "\"Machines here\"",
                "Warning: The collector at 1, 2, 3 makes nothing", "It holds " + copperName + " and a / bar (curly)", "-> Give it light"
            };
            string lacks = wanted.FirstOrDefault(w => !text.Contains(w));
            Check(own.Success && own.Value == r.Lines[warn + 3].Text && own.Groups[1].Value == EmcReport.CopyAction && lacks == null && label == Lang.GetL("en", "vintagee:doctor-copy-warning")
                  && !text.Contains("Look again") && !text.Contains("Do it") && text.IndexOfAny(new[] { '|', '{', '}' }) < 0 && text.Split('\n').Length == 5,
                "a copy button holds its finding ready to paste: versions, the page, the finding, its details with items by name and code, the advice, no buttons" + (lacks != null ? " (missing: " + lacks + ")" : "")
                + " [" + text.Replace("\n", " / ") + "]");

            Check(r.PlainText(sapi.World) == plainBefore && r.Lines.Where(IsCopy).All(l => EmcReport.Plain(sapi.World, l.Text).Length == 0 && EmcReport.OnlyCopy(l.Text)) && !EmcReport.OnlyCopy(r.Lines[warn].Text),
                "the copy buttons are for a window: the report as text (the file, the console, Copy this report) is what it was without them");

            string doctors = DoctorBridge.Convert(own.Value);
            Match theirs = Regex.Match(doctors, @"^\{\{copy\|([^|{}]*)\|\|([^{}|]*)\}\}$");
            Check(theirs.Success && theirs.Groups[1].Value == text && theirs.Groups[2].Value == label,
                "handover: a copy button is written as Vintage Doctor's own, {{copy|text||label}}, with the same text");

            // A finding with more behind it than fits: cut at a whole line, said so, still one whole button.
            var big = new EmcReport { Kind = EmcReport.DiagnoseKind, Key = "|" };
            big.Add("⚠ A long list", EmcLineStyle.Check);
            for (int i = 0; i < 400; i++) big.Add("line " + i + " of the long list, with some words to make it longer", EmcLineStyle.Note, 1);
            EmcDiagnose.AddCopyButtons(mod, sapi, "en", big);
            string bigLine = big.Lines[big.Lines.Count - 1].Text, bigDoctors = DoctorBridge.Convert(bigLine);
            Match bigToken = EmcReport.ActionToken.Match(bigLine);
            Check(bigToken.Success && bigToken.Value == bigLine && bigToken.Groups[2].Value.Length <= EmcDiagnose.CopyTextMax && bigDoctors.Length < 4000
                  && bigToken.Groups[2].Value.EndsWith(Lang.GetL("en", "vintagee:doctor-copy-cut"), StringComparison.Ordinal) && bigToken.Groups[2].Value.Contains("line 0 of") && !bigToken.Groups[2].Value.Contains("line 399 of"),
                $"a finding too long for one button is cut at a whole line and says so ({bigToken.Groups[2].Value.Length} characters; Vintage Doctor cuts a line at 4,000, the button's is {bigDoctors.Length})");

            // A bug report is itself the thing to copy.
            var bug = new EmcReport { Kind = EmcReport.DiagnoseKind, Key = EmcDiagnose.ActionBugReport + "|" };
            bug.Add("⚠ Something", EmcLineStyle.Check);
            Check(EmcDiagnose.AddCopyButtons(mod, sapi, "en", bug) == 0 && bug.Lines.Count == 1, "the bug report page gets no copy buttons");

            // Every page, as a player's window gets it and as Vintage Doctor is handed it.
            int pages = 0, total = 0, longest = 0, onMapping = -1;
            string firstBad = null;
            foreach (var (a, b) in new[] { ("", ""), ("mapping", ""), ("mods", ""), ("item", "game:ingot-copper"), ("item", "game:nosuchitem"), ("checks", ""), ("health", ""), ("machines", ""), ("emccraft", ""),
                ("methods", ""), ("symptom", ""), ("novalue", ""), ("watch", ""), ("players", ""), ("changes", ""), ("clean", ""), ("bugreport", ""), ("client", ""), ("trace", "game:ingot-copper"), ("short", "") })
            {
                try
                {
                    EmcReport page = EmcDiagnose.Run(mod, sapi, null, "en", a, b, null, null, null, handover: true);
                    int before = page.Lines.Count, needing = 0;
                    foreach (EmcReportLine l in page.Lines)
                    {
                        if (l.Style == EmcLineStyle.Check && l.Indent == 0 && (l.Text.StartsWith("⚠", StringComparison.Ordinal) || l.Text.StartsWith(EmcDiagnose.ProblemMark, StringComparison.Ordinal))) needing++;
                    }
                    int buttons = EmcDiagnose.AddCopyButtons(mod, sapi, "en", page);
                    if (a == "mapping") onMapping = buttons;
                    var tree = new TreeAttribute();
                    DoctorBridge.Write(page, tree);
                    string[] texts = Texts(tree);
                    bool fine = LinksAreTheDoctors(texts, out string bad) && page.Lines.Count == before + buttons && page.Lines.Count(IsCopy) == buttons && texts.All(t => t.Length < 4000)
                        && (a == EmcDiagnose.ActionBugReport ? buttons == 0 : buttons >= needing);
                    if (fine) { pages++; total += buttons; longest = Math.Max(longest, texts.Where(t => t.StartsWith("{{copy|", StringComparison.Ordinal)).Select(t => t.Length).DefaultIfEmpty(0).Max()); }
                    else firstBad ??= a + " " + b + ": " + (bad ?? buttons + " buttons for " + needing + " warnings and problems, " + page.Lines.Count + " lines after " + before);
                }
                catch (Exception e) { firstBad ??= a + " " + b + " threw " + e.Message; }
            }
            // Asked for by a player, a page comes with them; from the console (no window) without.
            int forPlayer = EmcDiagnose.Run(mod, sapi, null, "en", "mapping", "", null, null, null, handover: true, copyButtons: true).Lines.Count(IsCopy);
            int forConsole = EmcDiagnose.Run(mod, sapi, null, "en", "mapping", "", null, null, null, handover: true).Lines.Count(IsCopy);
            Check(firstBad == null && total > 0 && forPlayer == onMapping && forConsole == 0, "every page of the doctor with its copy buttons can be handed to Vintage Doctor: each warning and problem has one, every line is under its 4,000 characters; a page for the console has none ("
                + pages + " pages, " + total + " buttons, the longest " + longest + " characters" + (firstBad != null ? "; not " + firstBad : "") + ")");
        }

        /// <summary>
        /// Later, when the page asked for above has had time to come: looks at it, then (with Vintage Doctor installed)
        /// uses its /doctor command from the console, then a fix that maps afresh in the background, then goes on.
        /// </summary>
        void CheckDoctorHandoverLater(Action then)
        {
            try
            {
                string[] texts = Texts(handoverPage);
                Check(handoverPage != null && texts.Length >= 3 && texts.Any(t => t.Contains("128")) && LinksAreTheDoctors(texts, out _),
                    "handover: the page that takes a while was delivered later, with the copper ingot's value in it (" + texts.Length + " lines)");
            }
            catch (Exception e) { Check(false, "handover: looking at the delivered page threw " + e); }

            var steps = new List<(string command, System.Func<string, string> missing, string what, Action before)>();
            try
            {
              if (DoctorBridge.Installed(sapi))
              {
                string Heading(string what)
                {
                    EmcReport r = EmcDiagnose.Run(mod, sapi, null, "en", what, "", null, null, null, handover: true);
                    return "== " + EmcReport.Plain(sapi.World, r.Lines.First(l => l.Style == EmcLineStyle.Heading).Text) + " ==";
                }
                string Missing(string text, params string[] wanted) => wanted.FirstOrDefault(w => !text.Contains(w));
                string machines = Heading("machines"), mapping = Heading("");
                string question = Lang.GetL("en", "vintagee:doctor-question");
                steps.Add(("/doctor emc machines", t => Missing(t, machines), "Vintage Doctor's /doctor emc machines is Vintage-E's machines page", null));
                steps.Add(("/doctor emc", t => Missing(t, mapping, "(/doctor emc mods)", "(/doctor log vintagee)", "(/doctor startup)"), "Vintage Doctor's /doctor emc is the EMC check-up, delivered a moment later, with links into both mods' pages", null));
                steps.Add(("/doctor emc item game:ingot-copper", t => Missing(t, "128", "(/doctor emc"), "Vintage Doctor's /doctor emc item ... is Vintage-E's page for the item", null));
                // Vintage Doctor's check-up is laid out by mod since its 1.3.0: Vintage-E's card and the button for its
                // page are under Vintage-E's own heading (where the card no longer starts with the mod's name).
                string OwnSection(string t)
                {
                    int at = t.IndexOf("== Vintage-E ", StringComparison.Ordinal);
                    if (at < 0) return "";
                    int end = t.IndexOf("\n== ", at + 3, StringComparison.Ordinal);
                    return end < 0 ? t.Substring(at) : t.Substring(at, end - at);
                }
                steps.Add(("/doctor", t => Missing(t, "== Vintage-E ") ?? Missing(OwnSection(t), "EMC", "(/doctor emc)", question + " (/doctor emc)"),
                    "Vintage Doctor's check-up has a section for Vintage-E, with its card and the button for its page", null));
                steps.Add(("/doctor item game:ingot-copper", t => Missing(t, "Vintage-E: Worth 128 EMC", "(/doctor emc item game:ingot-copper)"), "Vintage Doctor's page about an item has Vintage-E's card", null));
                // Last (the switch stays on until the steps are over): a page with its copy buttons, as a player's window
                // gets it. Vintage Doctor writes a button it knows as its label (and what it copies); one it couldn't
                // read would be left standing as {{copy|...}}.
                // The mapping's own page: another one than the steps above asked for, so nothing remembered is shown.
                int buttons = EmcDiagnose.Run(mod, sapi, null, "en", "mapping", "", null, null, null, handover: true, copyButtons: true).Lines.Count(l => l.Text.StartsWith("{{" + EmcReport.CopyAction + "|", StringComparison.Ordinal));
                if (buttons > 0)
                {
                    steps.Add(("/doctor emc mapping", t => t.Contains("{{copy|") ? "the copy buttons read as buttons (they were left as text)" : Missing(t, mapping, "Copy this"),
                        "Vintage Doctor takes the copy buttons under the findings of the EMC mapping page (" + buttons + " of them)", () => DoctorBridge.TestCopyButtons = true));
                }
                else Line("  the EMC mapping page has nothing to copy on this server: Vintage Doctor isn't shown a copy button");
              }
              else Line("  Vintage Doctor isn't installed on this server: its own /doctor command isn't tried");
            }
            catch (Exception e)
            {
                Check(false, "handover: getting ready to use Vintage Doctor's /doctor command threw " + e);
                steps.Clear();
            }
            DoctorSteps(steps, 0, () => DoctorFixWithRemap(then));
        }

        static string DoctorsLastPage => Path.Combine(GamePaths.DataPath, "ModData", DoctorBridge.DoctorModId, "last-report-server.txt");

        /// <summary>One /doctor command after the other from the console; each is given up to 40 s for Vintage Doctor to write the page out.</summary>
        void DoctorSteps(List<(string command, System.Func<string, string> missing, string what, Action before)> steps, int index, Action then)
        {
            if (index >= steps.Count) { DoctorBridge.TestCopyButtons = false; then(); return; }
            var (command, missing, what, before) = steps[index];
            try
            {
                before?.Invoke();
                if (File.Exists(DoctorsLastPage)) File.Delete(DoctorsLastPage);
                sapi.InjectConsole(command);
            }
            catch (Exception e)
            {
                Check(false, "handover: " + what + " (" + command + " threw " + e.Message + ")");
                DoctorSteps(steps, index + 1, then);
                return;
            }
            int tries = 0;
            Action<float> look = null;
            look = _ =>
            {
                string text = "", lacks = "the page";
                try
                {
                    if (File.Exists(DoctorsLastPage))
                    {
                        text = File.ReadAllText(DoctorsLastPage);
                        lacks = missing(text);
                    }
                }
                catch (Exception e) { lacks = e.Message; }
                if (lacks != null && ++tries < 80) { sapi.Event.RegisterCallback(look, 500); return; }
                Check(lacks == null, "handover: " + what + (lacks != null ? " (missing: " + lacks + "; got " + text.Length + " characters: " + Line1(text, "==") + ")" : ""));
                try { if (text.Length > 0) File.WriteAllText(Path.Combine(VintageEModSystem.DataFolder(sapi), "diagnose-doctor-" + (index + 1) + ".txt"), command + "\n\n" + text); } catch { }
                DoctorSteps(steps, index + 1, then);
            };
            sapi.Event.RegisterCallback(look, 500);
        }

        /// <summary>A fix that changes the values: its result comes when the mapping it starts in the background is done.</summary>
        void DoctorFixWithRemap(Action then)
        {
            EnumHandling handling = EnumHandling.PassThrough;
            ITreeAttribute fix;
            try
            {
                fix = DoctorFixes(DoctorBridge.FixPrefix + "recalc", "", HandoverFixRequest);
                handoverBridge.OnFix(DoctorBridge.FixEvent, ref handling, fix);
                Check(fix.GetBool("later") && fix.GetString("result") == null, "handover: a fix that maps afresh says its result will come later");
                if (!fix.GetBool("later")) { then(); return; }
            }
            catch (Exception e)
            {
                Check(false, "handover: the fix that maps afresh threw " + e);
                then();
                return;
            }
            int tries = 0;
            Action<float> look = null;
            look = _ =>
            {
                if (handoverFix == null && ++tries < 240) { sapi.Event.RegisterCallback(look, 500); return; }
                Check(handoverFix != null && handoverFix.GetString("result") == Lang.GetL("en", "vintagee:diag-done-recalc") && !handoverFix.GetBool("failed") && mod.LastReport != null,
                    "handover: its result was delivered when the new values were in (" + (handoverFix?.GetString("result") ?? "nothing came in two minutes") + ", after " + (tries * 0.5).ToString("0.#") + " s)");
                then();
            };
            sapi.Event.RegisterCallback(look, 500);
        }
    }
}
