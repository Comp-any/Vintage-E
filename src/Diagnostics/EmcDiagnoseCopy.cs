using System;
using System.Text;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// A copy button under each of the doctor's findings (1.26.7).
    ///
    /// Many findings end in "post a bug report" or "tell that mod's author", and until now the only thing to copy was
    /// the whole page. Each finding that isn't a pass now carries a button that puts that one finding on the
    /// clipboard, ready to paste: the versions, the page it is from, and the finding with everything behind it (the
    /// details and the advice, items by name and code, the buttons left out).
    ///
    /// The text is worked out here, on the server, and sits in the button: {{copy|text|label}}. So Vintage-E's own
    /// window and Vintage Doctor's (which gets it as its own {{copy|text||label}}, through its ordinary API for a
    /// page handed over) copy the same thing, and neither has to know how a finding is laid out.
    /// </summary>
    public static partial class EmcDiagnose
    {
        /// <summary>
        /// The most a copy button holds. Vintage Doctor cuts a line another mod hands it at 4,000 characters, and a
        /// button cut in two is shown as its raw text; this leaves room for the label and the brackets.
        /// </summary>
        public const int CopyTextMax = 3500;

        enum CopyLevel { None, Info, Warning, Problem }

        static CopyLevel LevelOf(string card)
        {
            if (card.StartsWith("✓", StringComparison.Ordinal)) return CopyLevel.None;
            if (card.StartsWith("⚠", StringComparison.Ordinal)) return CopyLevel.Warning;
            if (card.StartsWith(ProblemMark, StringComparison.Ordinal)) return CopyLevel.Problem;
            return CopyLevel.Info;
        }

        /// <summary>A line that belongs to the card above it, as both windows read a page: indented, or a note.</summary>
        static bool IsDetail(EmcReportLine line) =>
            line.Style != EmcLineStyle.Heading && !(line.Style == EmcLineStyle.Check && line.Indent == 0) && (line.Indent > 0 || line.Style == EmcLineStyle.Note);

        /// <summary>A line as it is pasted: items as "Name (code)", buttons left out.</summary>
        static string CopyPlain(ICoreServerAPI sapi, string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("{{", StringComparison.Ordinal) >= 0) text = EmcReport.ActionToken.Replace(text, "");
            return EmcReport.Plain(sapi.World, text).Trim();
        }

        /// <summary>
        /// Adds a line with a copy button after each warning and problem, and after each note that has something
        /// written behind it. Returns how many were added. A bug report (which is itself the thing to copy) gets none.
        /// </summary>
        public static int AddCopyButtons(VintageEModSystem mod, ICoreServerAPI sapi, string lang, EmcReport r)
        {
            if (r == null || r.Kind != EmcReport.DiagnoseKind) return 0;
            string key = r.Key ?? "";
            int bar = key.IndexOf('|');
            string action = bar < 0 ? key : key.Substring(0, bar), arg = bar < 0 ? "" : key.Substring(bar + 1);
            if (action == ActionBugReport) return 0;
            string L(string k, params object[] a) => Lang.GetL(lang, "vintagee:" + k, a);

            string doctor = null;
            try { if (sapi.ModLoader.IsModEnabled(DoctorBridge.DoctorModId)) doctor = sapi.ModLoader.GetMod(DoctorBridge.DoctorModId)?.Info?.Version; } catch { }
            string header = L("doctor-copy-header", mod.Version ?? "?", GameVersion.ShortGameVersion, sapi.Server.IsDedicated ? L("doctor-copy-dedicated") : L("doctor-copy-single"))
                + (string.IsNullOrEmpty(doctor) ? "" : L("doctor-copy-withdoctor", doctor));
            string command = "/emc diagnose" + (action.Length > 0 && action != ActionOverview ? " " + action : "") + (arg.Length > 0 ? " " + arg : "");

            int added = 0;
            string heading = null;
            for (int i = 0; i < r.Lines.Count; i++)
            {
                EmcReportLine card = r.Lines[i];
                if (card.Style == EmcLineStyle.Heading) { heading = CopyPlain(sapi, card.Text); continue; }
                if (card.Style != EmcLineStyle.Check || card.Indent != 0) continue;
                CopyLevel level = LevelOf(card.Text ?? "");
                int end = i + 1;
                while (end < r.Lines.Count && IsDetail(r.Lines[end])) end++;
                // A pass needs no report.
                if (level == CopyLevel.None) { i = end - 1; continue; }

                string title = card.Text ?? "";
                foreach (string mark in new[] { "⚠", ProblemMark, "•" })
                {
                    if (title.StartsWith(mark, StringComparison.Ordinal)) { title = title.Substring(mark.Length); break; }
                }
                var sb = new StringBuilder();
                sb.Append(header).Append('\n');
                sb.Append(string.IsNullOrEmpty(heading) ? L("doctor-copy-from", command) : L("doctor-copy-from-under", command, heading)).Append('\n');
                sb.Append(L(level == CopyLevel.Problem ? "doctor-badge-fail" : level == CopyLevel.Warning ? "doctor-badge-warn" : "doctor-badge-info")).Append(": ").Append(CopyPlain(sapi, title));
                bool cut = false;
                int behind = 0;
                for (int d = i + 1; d < end; d++)
                {
                    string text = CopyPlain(sapi, r.Lines[d].Text);
                    if (text.Length == 0) continue; // a line of buttons
                    behind++;
                    // The arrow before a piece of advice, in characters every program has.
                    if (text.StartsWith("→", StringComparison.Ordinal)) text = "-> " + text.Substring(1).TrimStart();
                    if (sb.Length + 1 + r.Lines[d].Indent * 2 + text.Length > CopyTextMax - 160) { cut = true; break; }
                    sb.Append('\n').Append(' ', Math.Max(1, r.Lines[d].Indent) * 2).Append(text);
                }
                // A note with nothing written behind it (a line of its own, or a list of buttons) says all it has in its title.
                if (level == CopyLevel.Info && behind == 0) { i = end - 1; continue; }
                if (cut) sb.Append('\n').Append(L("doctor-copy-cut"));
                string body = EmcReport.TokenSafe(sb.ToString());
                if (body.Length > CopyTextMax) body = body.Substring(0, CopyTextMax);
                string label = EmcReport.TokenSafe(L(level == CopyLevel.Problem ? "doctor-copy-problem" : level == CopyLevel.Warning ? "doctor-copy-warning" : "doctor-copy-info"));

                r.Lines.Insert(end, new EmcReportLine { Text = "{{" + EmcReport.CopyAction + "|" + body + "|" + label + "}}", Style = EmcLineStyle.Text, Indent = 1, Block = card.Block });
                added++;
                i = end; // the line just added
            }
            return added;
        }
    }
}
