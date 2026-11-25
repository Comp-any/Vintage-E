using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// Vintage-E's doctor, handed over to the mod Vintage Doctor when that is installed (1.26.0).
    ///
    /// Vintage Doctor (github.com/Comp-any/Vintage-Doctor, 1.2.0 or newer) is a doctor for the whole game and every
    /// mod: crashes, the log, mods that didn't start, lag, the Mods folder. With it installed on the server and on
    /// the player's own game, a second window that looks the same and checks half the same things is one too many,
    /// so Vintage-E shows its EMC pages inside Vintage Doctor instead: Ctrl+H and /emc diagnose open that window at
    /// the page "emc", the pages and their fix buttons are the ones built here, and what Vintage Doctor already
    /// checks for every mod (the log, what other mods did at start-up, clashing keys, who patches the game's code)
    /// is left to it. Without Vintage Doctor, or with it on one side only, nothing changes.
    ///
    /// Everything goes over the game's event bus, in the way Vintage Doctor's docs/api.md describes ("A whole doctor
    /// of your own"), so Vintage-E doesn't reference that mod and runs the same without it:
    /// "vintagedoctor:topics" (which pages are there), "vintagedoctor:page" (build one; the slow ones are delivered
    /// with "vintagedoctor:deliver"), "vintagedoctor:fix" (a fix button), "vintagedoctor:collect" (a card on the
    /// check-up), "vintagedoctor:about" (a card on its page about one item or block), "vintagedoctor:clientfacts"
    /// (what this player's game knows, sent along to the server) and "vintagedoctor:open" (open the window).
    /// </summary>
    public sealed class DoctorBridge
    {
        public const string DoctorModId = "vintagedoctor";
        /// <summary>Vintage Doctor's page for Vintage-E: /doctor emc [what] [which].</summary>
        public const string Topic = "emc";
        /// <summary>The world setting that tells players' games the server hands its doctor over.</summary>
        public const string WorldKey = "vintageeDoctorHandover";
        public const string FixPrefix = "vintagee:";
        public const string TopicsEvent = "vintagedoctor:topics", PageEvent = "vintagedoctor:page", DeliverEvent = "vintagedoctor:deliver", FixEvent = "vintagedoctor:fix",
            CollectEvent = "vintagedoctor:collect", AboutEvent = "vintagedoctor:about", ClientFactsEvent = "vintagedoctor:clientfacts", OpenEvent = "vintagedoctor:open";

        /// <summary>
        /// For the self-test: pages asked for from the console get the copy buttons too (they are for a window, so a
        /// console's page has none), so that Vintage Doctor itself can be seen to take them. Also for a Vintage Doctor
        /// that adds its own (1.7.0 and newer): the ones before it still need Vintage-E's to arrive whole.
        /// </summary>
        public static bool TestCopyButtons;

        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly ICoreClientAPI capi;

        DoctorBridge(VintageEModSystem mod, ICoreServerAPI sapi, ICoreClientAPI capi)
        {
            this.mod = mod;
            this.sapi = sapi;
            this.capi = capi;
        }

        /// <summary>Vintage Doctor is installed on this side, in a version that can take a mod's doctor over (1.2.0).</summary>
        public static bool Installed(ICoreAPI api)
        {
            try
            {
                if (api?.ModLoader == null || !api.ModLoader.IsModEnabled(DoctorModId)) return false;
                return AtLeast(api.ModLoader.GetMod(DoctorModId)?.Info?.Version, 1, 2);
            }
            catch { return false; }
        }

        /// <summary>Whether a version text ("1.2.0", "v1.10.3-rc.1") is at least major.minor.</summary>
        public static bool AtLeast(string version, int major, int minor)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            string[] parts = version.Trim().TrimStart('v', 'V').Split('-')[0].Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int b)) return false;
            return a > major || (a == major && b >= minor);
        }

        /// <summary>This side's Vintage Doctor, as its version text ("" when it isn't installed or is switched off).</summary>
        public static string VersionOn(ICoreAPI api)
        {
            try { return api?.ModLoader != null && api.ModLoader.IsModEnabled(DoctorModId) ? api.ModLoader.GetMod(DoctorModId)?.Info?.Version ?? "" : ""; }
            catch { return ""; }
        }

        /// <summary>
        /// From 1.7.0 Vintage Doctor's window puts its own "Copy this for a report" on every card that isn't a pass,
        /// whichever mod the card comes from. Vintage-E's own button under each finding would be a second one there.
        /// </summary>
        public static bool AddsItsOwnCopy(string doctorVersion) => AtLeast(doctorVersion, 1, 7);

        /// <summary>
        /// Whether a page built for Vintage Doctor gets Vintage-E's copy buttons (1.26.9), as <see cref="EmcDiagnose.Run"/>
        /// takes it: false when the window that will show the page adds its own, null otherwise (with them whenever a
        /// player asked, as for Vintage-E's own window). The window is the player's, so their game's Vintage Doctor
        /// decides; when their game didn't say (nothing sent along: no Vintage Doctor there, and the page goes to
        /// their chat as text), the server's does.
        /// </summary>
        public static bool? CopyButtonsFor(string playersDoctor, string serversDoctor)
        {
            if (TestCopyButtons) return true;
            return AddsItsOwnCopy(string.IsNullOrWhiteSpace(playersDoctor) ? serversDoctor : playersDoctor) ? false : (bool?)null;
        }

        // ---------------------------------------------------------------- the server's side

        /// <summary>
        /// On the server: tells players' games whether the doctor is handed over (through the world's settings, set
        /// afresh at every start), and when it is, answers Vintage Doctor's questions. Null when it isn't.
        /// </summary>
        public static DoctorBridge ForServer(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            bool on = Installed(sapi);
            try { sapi.World.Config?.SetBool(WorldKey, on); }
            catch (Exception e) { mod.Mod.Logger.Warning("Could not tell players' games whether the doctor is handed over to Vintage Doctor: {0}", e.Message); }
            if (!on) return null;
            var bridge = new DoctorBridge(mod, sapi, null);
            bridge.Listen(sapi);
            mod.Mod.Logger.Notification("Vintage Doctor is installed: the EMC doctor's pages are shown there (/doctor emc), and /emc diagnose opens it for players who have it too.");
            return bridge;
        }

        /// <summary>For the self-test: a bridge that answers when asked directly, without Vintage Doctor being installed.</summary>
        public static DoctorBridge ForTest(VintageEModSystem mod, ICoreServerAPI sapi) => new DoctorBridge(mod, sapi, null);

        void Listen(ICoreServerAPI api)
        {
            api.Event.RegisterEventBusListener(OnTopics, 0.5, TopicsEvent);
            api.Event.RegisterEventBusListener(OnPage, 0.5, PageEvent);
            api.Event.RegisterEventBusListener(OnFix, 0.5, FixEvent);
            api.Event.RegisterEventBusListener(OnCollect, 0.5, CollectEvent);
            api.Event.RegisterEventBusListener(OnAbout, 0.5, AboutEvent);
        }

        /// <summary>
        /// Asks Vintage Doctor to open its window at an EMC page on a player's game (for /emc diagnose). False when
        /// that player's game doesn't have Vintage Doctor: the caller then shows Vintage-E's own doctor.
        /// </summary>
        public bool OpenFor(IServerPlayer player, string action, string arg)
        {
            if (sapi == null || player == null) return false;
            try
            {
                var tree = new TreeAttribute();
                tree.SetString("topic", Topic);
                tree.SetString("argument", Join(action, arg));
                tree.SetString("player", player.PlayerUID);
                sapi.Event.PushEvent(OpenEvent, tree);
                return tree.GetBool("opened");
            }
            catch (Exception e)
            {
                mod.Mod.Logger.Warning("Vintage-E: could not ask Vintage Doctor to open its window for {0} ({1}); showing the EMC doctor's own.", player.PlayerName, e.Message);
                return false;
            }
        }

        static bool ForServerSide(IAttribute data, out ITreeAttribute tree)
        {
            tree = data as ITreeAttribute;
            return tree != null && tree.GetString("side") == "server";
        }

        public void OnTopics(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!ForServerSide(data, out ITreeAttribute tree)) return;
            Guard.Run(mod.Mod.Logger, "telling Vintage Doctor about the EMC pages", () =>
            {
                ITreeAttribute page = tree.GetOrAddTreeAttribute("topics").GetOrAddTreeAttribute(Topic);
                page.SetString("mod", VintageEModSystem.ModId);
                // A lang key: Vintage Doctor shows it in the asking player's language.
                page.SetString("question", "vintagee:doctor-question");
                page.SetBool("ownHeadings", true);
            });
        }

        IServerPlayer PlayerOf(ITreeAttribute tree)
        {
            string uid = tree.GetString("player");
            return string.IsNullOrEmpty(uid) ? null : sapi.World.PlayerByUid(uid) as IServerPlayer;
        }

        /// <summary>The console may; a player needs the privilege the doctor has always needed.</summary>
        static bool Allowed(IServerPlayer player) => player == null || player.HasPrivilege(Privilege.controlserver);

        /// <summary>"item game:ingot-copper" → the doctor's action and its argument.</summary>
        public static void Split(string argument, out string action, out string arg)
        {
            argument = (argument ?? "").Trim();
            int space = argument.IndexOf(' ');
            action = space < 0 ? argument : argument.Substring(0, space);
            arg = space < 0 ? "" : argument.Substring(space + 1).Trim();
        }

        public static string Join(string action, string arg)
        {
            action = (action ?? "").Trim();
            if (action == EmcDiagnose.ActionOverview) action = "";
            return (action + " " + (arg ?? "").Trim()).Trim();
        }

        /// <summary>What the asking player's own Vintage-E sent along (its version, table, look, the block looked at); null from the console.</summary>
        static EmcDiagnoseRequestPacket ClientOf(ITreeAttribute tree)
        {
            EmcDiagnoseRequestPacket client = null;
            try
            {
                byte[] bytes = (tree["clientfacts"] as ITreeAttribute)?.GetBytes(VintageEModSystem.ModId, null);
                if (bytes != null && bytes.Length > 0) client = SerializerUtil.Deserialize<EmcDiagnoseRequestPacket>(bytes);
            }
            catch { client = null; }
            // Without it, the block the player looks at still comes from Vintage Doctor itself.
            if (client == null && tree.GetBool("hasLookedAt"))
            {
                client = new EmcDiagnoseRequestPacket
                {
                    HasLookedAt = true, LookedAtX = tree.GetInt("lookedAtX"), LookedAtY = tree.GetInt("lookedAtY"), LookedAtZ = tree.GetInt("lookedAtZ"), LookedAtDim = tree.GetInt("lookedAtDim")
                };
            }
            return client;
        }

        ItemStack HeldOf(ITreeAttribute tree, IServerPlayer player)
        {
            ItemStack held = null;
            try
            {
                byte[] bytes = tree.GetBytes("held", null);
                if (bytes != null && bytes.Length > 0)
                {
                    held = new ItemStack(bytes);
                    if (!held.ResolveBlockOrItem(sapi.World)) held = null;
                }
            }
            catch { held = null; }
            return held ?? player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        }

        public void OnPage(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!ForServerSide(data, out ITreeAttribute tree) || tree.GetString("topic") != Topic) return;
            Guard.Run(mod.Mod.Logger, "building an EMC page for Vintage Doctor", () => Page(tree));
        }

        void Page(ITreeAttribute tree)
        {
            string lang = tree.GetString("language") ?? "en";
            IServerPlayer player = PlayerOf(tree);
            if (!Allowed(player))
            {
                WriteLine(tree, "⚠ " + Lang.GetL(lang, "vintagee:error-noprivilege"));
                return;
            }
            Split(tree.GetString("argument"), out string action, out string arg);
            string request = tree.GetString("request") ?? "";
            // The reports that read blocks and inventories are quick and run here; the rest are worked out off the
            // game thread (the deeper ones map afresh, which can take seconds) and delivered when they are done.
            bool live = EmcDiagnose.ReadsLiveState(action);
            if (!live)
            {
                tree.SetBool("later", true);
                tree.SetInt("seconds", 180);
            }
            mod.BuildDiagnosis(player, lang, action, arg, HeldOf(tree, player), ClientOf(tree), (report, failure) =>
            {
                ITreeAttribute into = live ? tree : new TreeAttribute();
                if (failure != null || report == null)
                {
                    mod.Mod.Logger.Error("Vintage-E: an EMC page for Vintage Doctor failed: {0}", failure);
                    WriteLine(into, "⚠ " + Lang.GetL(lang, "vintagee:doctor-page-failed", failure?.Message ?? "?"));
                }
                else
                {
                    try { mod.SaveReport(report, "emc-diagnose.txt"); } catch { }
                    Write(report, into);
                }
                if (live) return;
                into.SetString("request", request);
                sapi.Event.PushEvent(DeliverEvent, into);
            });
        }

        public void OnFix(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!ForServerSide(data, out ITreeAttribute tree)) return;
            string id = tree.GetString("id") ?? "";
            if (!id.StartsWith(FixPrefix, StringComparison.Ordinal)) return;
            Guard.Run(mod.Mod.Logger, "doing one of the EMC doctor's fixes for Vintage Doctor", () => Fix(tree, id.Substring(FixPrefix.Length)));
        }

        void Fix(ITreeAttribute tree, string what)
        {
            string lang = tree.GetString("language") ?? "en";
            IServerPlayer player = PlayerOf(tree);
            if (!Allowed(player))
            {
                SetResult(tree, "⚠ " + Lang.GetL(lang, "vintagee:error-noprivilege"));
                return;
            }
            string request = tree.GetString("request") ?? "";
            // A fix that changed the settings is followed by a mapping in the background: its result comes when the new values are in.
            bool remapping = false;
            mod.ApplyDoctorFix(player, lang, what, tree.GetString("argument") ?? "", outcome =>
            {
                if (!remapping) { SetResult(tree, outcome); return; }
                var done = new TreeAttribute();
                done.SetString("request", request);
                SetResult(done, outcome);
                sapi.Event.PushEvent(DeliverEvent, done);
            }, () =>
            {
                remapping = true;
                tree.SetBool("later", true);
                tree.SetInt("seconds", 240);
            });
        }

        /// <summary>Vintage-E's fixes say "⚠ ..." when they didn't work; Vintage Doctor wants the words and a flag.</summary>
        public static void SetResult(ITreeAttribute tree, string outcome)
        {
            outcome = outcome ?? "";
            bool failed = outcome.StartsWith("⚠", StringComparison.Ordinal);
            tree.SetString("result", failed ? outcome.Substring(1).TrimStart() : outcome);
            if (failed) tree.SetBool("failed", true);
        }

        /// <summary>A card on Vintage Doctor's check-up (and on its page for Vintage-E): how the EMC values are doing, with a link to the EMC pages.</summary>
        public void OnCollect(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!ForServerSide(data, out ITreeAttribute tree)) return;
            string only = tree.GetString("mod");
            if (only != null && only != VintageEModSystem.ModId) return;
            Guard.Run(mod.Mod.Logger, "the EMC card on Vintage Doctor's check-up", () =>
            {
                string lang = tree.GetString("language") ?? "en";
                ITreeAttribute card = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute(FixPrefix + "checkup");
                card.SetString("mod", VintageEModSystem.ModId);
                int things = mod.CheckupProblems;
                if (mod.LastReport == null)
                {
                    card.SetString("level", "problem");
                    card.SetString("title", Lang.GetL(lang, "vintagee:doctor-card-nomapping"));
                }
                else if (things > 0)
                {
                    card.SetString("level", "warning");
                    card.SetString("title", Lang.GetL(lang, "vintagee:doctor-card-things", things));
                }
                else
                {
                    card.SetString("level", "ok");
                    card.SetString("title", Lang.GetL(lang, "vintagee:doctor-card-ok", mod.Emc.Count));
                }
                card.SetString("link", Topic);
                card.SetString("linkLabel", Lang.GetL(lang, "vintagee:doctor-card-link"));
            });
        }

        /// <summary>A card on Vintage Doctor's pages about one item or block: its EMC value, with a link to the doctor's page about it.</summary>
        public void OnAbout(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!ForServerSide(data, out ITreeAttribute tree)) return;
            Guard.Run(mod.Mod.Logger, "the EMC card on Vintage Doctor's page about an item", () =>
            {
                CollectibleObject c = Subject(sapi.World, tree.GetString("subject"));
                if (c?.Code == null) return;
                string lang = tree.GetString("language") ?? "en";
                ITreeAttribute card = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute(FixPrefix + "value");
                card.SetString("mod", VintageEModSystem.ModId);
                card.SetString("level", "info");
                double value = mod.Emc.GetValue(c);
                card.SetString("title", value > 0 ? Lang.GetL(lang, "vintagee:doctor-about-value", EmcFormat.Format(value)) : Lang.GetL(lang, "vintagee:doctor-about-novalue"));
                card.SetString("link", Topic);
                card.SetString("linkArgument", EmcDiagnose.ActionItem + " " + c.Code);
                card.SetString("linkLabel", value > 0 ? Lang.GetL(lang, "vintagee:doctor-about-link") : Lang.GetL(lang, "vintagee:doctor-about-link-novalue"));
            });
        }

        /// <summary>"item:game:ingot-copper" or "block:game:anvil-copper", as Vintage Doctor names the thing a page is about.</summary>
        public static CollectibleObject Subject(IWorldAccessor world, string subject)
        {
            if (string.IsNullOrEmpty(subject) || world == null) return null;
            try
            {
                if (subject.StartsWith("block:", StringComparison.Ordinal)) return world.GetBlock(new AssetLocation(subject.Substring(6)));
                if (subject.StartsWith("item:", StringComparison.Ordinal)) return world.GetItem(new AssetLocation(subject.Substring(5)));
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------------- a player's own side

        /// <summary>
        /// On a player's game with Vintage Doctor installed: sends what this side knows along with each request for an
        /// EMC page, and does the one fix that happens here (the window for setting a value). Null without Vintage Doctor.
        /// </summary>
        public static DoctorBridge ForClient(VintageEModSystem mod, ICoreClientAPI capi)
        {
            if (!Installed(capi)) return null;
            var bridge = new DoctorBridge(mod, null, capi);
            capi.Event.RegisterEventBusListener(bridge.OnClientFacts, 0.5, ClientFactsEvent);
            capi.Event.RegisterEventBusListener(bridge.OnClientFix, 0.5, FixEvent);
            return bridge;
        }

        /// <summary>
        /// Whether this player's doctor key and /emc diagnose open Vintage Doctor: it is installed here, the server has
        /// it too (its channel is connected), and the server's Vintage-E said it hands its doctor over.
        /// </summary>
        public bool Active
        {
            get
            {
                try
                {
                    return capi != null && capi.World?.Config?.GetBool(WorldKey, false) == true
                        && capi.Network.GetChannelState(DoctorModId) == EnumChannelState.Connected;
                }
                catch { return false; }
            }
        }

        /// <summary>Opens Vintage Doctor's window here at an EMC page. False when it didn't open: the caller then shows Vintage-E's own.</summary>
        public bool Open(string action, string arg)
        {
            if (capi == null) return false;
            try
            {
                var tree = new TreeAttribute();
                tree.SetString("topic", Topic);
                tree.SetString("argument", Join(action, arg));
                capi.Event.PushEvent(OpenEvent, tree);
                return tree.GetBool("opened");
            }
            catch (Exception e)
            {
                mod.Mod.Logger.Warning("Vintage-E: could not open Vintage Doctor's window ({0}); showing the EMC doctor's own.", e.Message);
                return false;
            }
        }

        void OnClientFacts(string eventName, ref EnumHandling handling, IAttribute data)
        {
            // Only for the EMC pages: the rest of Vintage Doctor has no use for it.
            if (!(data is ITreeAttribute tree) || tree.GetString("topic") != Topic || !(tree["facts"] is ITreeAttribute facts)) return;
            Guard.Run(mod.Mod.Logger, "sending this game's details along to Vintage Doctor", () =>
            {
                Split(tree.GetString("argument"), out string action, out string arg);
                byte[] bytes = SerializerUtil.Serialize(mod.DiagnoseRequest(action, arg));
                if (bytes != null && bytes.Length > 0 && bytes.Length <= 60000) facts.SetBytes(VintageEModSystem.ModId, bytes);
            });
        }

        void OnClientFix(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || tree.GetString("side") != "client" || tree.GetString("id") != FixPrefix + "setvalue") return;
            Guard.Run(mod.Mod.Logger, "opening the window that sets a value, from Vintage Doctor", () =>
            {
                string key = tree.GetString("argument") ?? "";
                CollectibleObject c = PlayerEmc.Resolve(capi.World, key);
                if (c?.Code == null)
                {
                    tree.SetString("result", Lang.Get("vintagee:doctor-setvalue-unknown", key));
                    tree.SetBool("failed", true);
                    return;
                }
                mod.OpenSetValueFor(key);
                tree.SetString("result", Lang.Get("vintagee:doctor-setvalue-opened", EmcExplain.NameOf(c)));
            });
        }

        // ---------------------------------------------------------------- a report, in Vintage Doctor's words

        /// <summary>
        /// Puts a report's lines into the tree Vintage Doctor reads a page from. The two mods' pages are built the same
        /// way (lines with a style and an indent; the styles even have the same numbers), so only the links differ.
        /// </summary>
        public static void Write(EmcReport report, ITreeAttribute tree)
        {
            int n = report?.Lines.Count ?? 0;
            var texts = new string[n];
            var styles = new byte[n];
            var indents = new byte[n];
            for (int i = 0; i < n; i++)
            {
                EmcReportLine line = report.Lines[i];
                texts[i] = Convert(line.Text);
                styles[i] = (byte)line.Style;
                indents[i] = (byte)Math.Max(0, Math.Min(line.Indent, 20));
            }
            tree["texts"] = new StringArrayAttribute(texts);
            tree.SetBytes("styles", styles);
            tree.SetBytes("indents", indents);
        }

        static void WriteLine(ITreeAttribute tree, string card)
        {
            tree["texts"] = new StringArrayAttribute(new[] { card });
            tree.SetBytes("styles", new[] { (byte)EmcLineStyle.Check });
            tree.SetBytes("indents", new byte[] { 0 });
        }

        static string Clean(string s) => (s ?? "").Replace("{", "").Replace("}", "").Replace('|', '/');

        /// <summary>
        /// One line of a report with its links turned into Vintage Doctor's:
        /// [[i:code]] and [[b:code]] → [[item:code]] and [[block:code]];
        /// {{fix-what|target;then;thenArg|label}} → a fix button for the fix vintagee:&lt;what&gt; (Vintage Doctor shows the page again itself);
        /// {{setvalue|key|label}} → a fix that runs on the player's side (it opens a window there);
        /// {{copy|text|label}} → its copy button, {{copy|text||label}} (the text is the button's target there);
        /// {{log||label}} and {{startup||label}} → Vintage Doctor's own pages for those;
        /// any other {{action|arg|label}} → its page "emc" with "action arg".
        /// </summary>
        public static string Convert(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf("[[", StringComparison.Ordinal) < 0 && text.IndexOf("{{", StringComparison.Ordinal) < 0)) return text ?? "";
            return EmcReport.AnyToken.Replace(text, m =>
            {
                if (m.Groups[1].Success)
                {
                    string key = PlayerEmc.PlainKeyOf(m.Groups[1].Value) ?? "";
                    if (key.Length > 2 && key[1] == ':' && (key[0] == 'i' || key[0] == 'b')) return "[[" + (key[0] == 'b' ? "block:" : "item:") + key.Substring(2) + "]]";
                    return key;
                }
                string action = m.Groups[2].Value, arg = Clean(m.Groups[3].Value), label = Clean(m.Groups[4].Value);
                if (action.StartsWith("fix-", StringComparison.Ordinal)) return "{{fix|" + FixPrefix + action.Substring(4) + "|" + arg.Split(';')[0] + "|" + label + "}}";
                if (action == "setvalue") return "{{fix|c:bus:" + FixPrefix + "setvalue|" + arg + "|" + label + "}}";
                if (action == EmcReport.CopyAction) return "{{copy|" + arg + "||" + label + "}}";
                if (action == EmcDiagnose.ActionLog) return "{{open|log|" + VintageEModSystem.ModId + "|" + label + "}}";
                if (action == EmcDiagnose.ActionStartup) return "{{open|startup||" + label + "}}";
                return "{{open|" + Topic + "|" + Join(action, arg) + "|" + label + "}}";
            });
        }
    }
}
