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
                string rest = Regex.Replace(t, @"\{\{(open|fix)\|[^|{}]*\|[^|{}]*\|[^{}|]*\}\}", "");
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
            Check(DoctorBridge.AtLeast("1.3.0", 1, 3) && DoctorBridge.AtLeast("v1.10.3-rc.1", 1, 3) && DoctorBridge.AtLeast("2.0", 1, 3) && !DoctorBridge.AtLeast("1.2.9", 1, 3)
                && !DoctorBridge.AtLeast(null, 1, 3) && !DoctorBridge.AtLeast("soon", 1, 3), "handover: a Vintage Doctor new enough to take the doctor over (1.3.0) is told from an older one");

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

            var steps = new List<(string command, System.Func<string, string> missing, string what)>();
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
                steps.Add(("/doctor emc machines", t => Missing(t, machines), "Vintage Doctor's /doctor emc machines is Vintage-E's machines page"));
                steps.Add(("/doctor emc", t => Missing(t, mapping, "(/doctor emc mods)", "(/doctor log vintagee)", "(/doctor startup)"), "Vintage Doctor's /doctor emc is the EMC check-up, delivered a moment later, with links into both mods' pages"));
                steps.Add(("/doctor emc item game:ingot-copper", t => Missing(t, "128", "(/doctor emc"), "Vintage Doctor's /doctor emc item ... is Vintage-E's page for the item"));
                // Vintage Doctor's check-up is laid out by mod: Vintage-E's card and the button for its page are under Vintage-E's own heading.
                string OwnSection(string t)
                {
                    int at = t.IndexOf("== Vintage-E ", StringComparison.Ordinal);
                    if (at < 0) return "";
                    int end = t.IndexOf("\n== ", at + 3, StringComparison.Ordinal);
                    return end < 0 ? t.Substring(at) : t.Substring(at, end - at);
                }
                steps.Add(("/doctor", t => Missing(t, "== Vintage-E ") ?? Missing(OwnSection(t), "EMC", "(/doctor emc)", question + " (/doctor emc)"),
                    "Vintage Doctor's check-up has a section for Vintage-E, with its card and the button for its page"));
                steps.Add(("/doctor item game:ingot-copper", t => Missing(t, "Vintage-E: Worth 128 EMC", "(/doctor emc item game:ingot-copper)"), "Vintage Doctor's page about an item has Vintage-E's card"));
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
        void DoctorSteps(List<(string command, System.Func<string, string> missing, string what)> steps, int index, Action then)
        {
            if (index >= steps.Count) { then(); return; }
            var (command, missing, what) = steps[index];
            try
            {
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
