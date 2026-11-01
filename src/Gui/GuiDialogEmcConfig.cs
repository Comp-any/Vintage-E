using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc configure: every server setting on the left (searchable), each with a "reset" link that puts
    /// it back to its default; the picked one on the right with what it does, its default, and an editor that fits it - a
    /// switch for on/off settings, a box for numbers, and a text area for lists (one entry per line) and tables
    /// ("key = value" per line). Save sends it to the server, which checks it, saves ModConfig/vintagee.json and applies
    /// it straight away, then sends the settings back. "Reset all" puts every setting back to its default (after asking).
    /// <para>The list keeps its scroll position when a setting is picked or saved, and the description is measured so the
    /// editor always sits below it.</para>
    /// </summary>
    public class GuiDialogEmcConfig : GuiDialog
    {
        const double ListWidth = 300;
        const double ListHeight = 440;
        const double PaneWidth = 470;
        const double AreaHeight = 200;
        /// <summary>
        /// Lines of a list/table shown at once. The text area draws all its text into one texture, and a few hundred lines
        /// (a long FixedValues table, say) made that taller than the graphics card allows, which showed as a black box.
        /// </summary>
        const int PageLines = 100;

        readonly Action<string, string> onSave;
        readonly Action<string> onReset;
        string[] names = Array.Empty<string>();
        Dictionary<string, string> values = new Dictionary<string, string>();
        Dictionary<string, string> defaults = new Dictionary<string, string>();
        string selected;
        string search = "";
        string status = "";
        bool statusIsError;
        /// <summary>Unsaved text in the editor, kept across redraws.</summary>
        string draft;
        bool composing;
        /// <summary>How far the settings list is scrolled, kept across redraws.</summary>
        float listScroll;
        // A list/table being edited, a page at a time: all its lines, which of them are on screen, and the edited page text.
        List<string> lines;
        string linesFor;
        int[] shown = Array.Empty<int>();
        int page;
        string find = "";
        string pageText;
        ElementBounds listClip, areaClip;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcConfig(ICoreClientAPI capi, EmcConfigPacket packet, Action<string, string> onSave, Action<string> onReset = null) : base(capi)
        {
            this.onSave = onSave;
            this.onReset = onReset;
            Load(packet);
            selected = packet.Selected ?? names.FirstOrDefault();
            Compose();
        }

        void Load(EmcConfigPacket packet)
        {
            names = packet.Names ?? Array.Empty<string>();
            values = new Dictionary<string, string>();
            defaults = new Dictionary<string, string>();
            for (int i = 0; i < names.Length; i++)
            {
                values[names[i]] = packet.Values != null && i < packet.Values.Length ? packet.Values[i] : "";
                if (packet.Defaults != null && i < packet.Defaults.Length) defaults[names[i]] = packet.Defaults[i];
            }
        }

        /// <summary>The server answered a change: new values and what happened.</summary>
        public void Update(EmcConfigPacket packet)
        {
            Load(packet);
            if (!string.IsNullOrEmpty(packet.Selected)) selected = packet.Selected;
            status = packet.Message ?? "";
            statusIsError = packet.Error;
            if (!packet.Error) { draft = null; linesFor = null; } // saved: show what the server has now
            Compose();
        }

        static string KindOf(string name)
        {
            FieldInfo f = ConfigureCommand.Field(name);
            return f == null ? "text" : ConfigureCommand.KindOf(f);
        }

        static string About(string name) => ConfigureCommand.Field(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

        static string Short(string kind, string value)
        {
            if (kind == "list" || kind == "table")
            {
                int n = string.IsNullOrWhiteSpace(value) ? 0 : value.Split('\n').Count(l => l.Trim().Length > 0);
                return n == 0 ? "(empty)" : "(" + n + ")";
            }
            return value.Length > 18 ? value.Substring(0, 18) + "…" : value;
        }

        bool IsDefault(string name) => !defaults.TryGetValue(name, out string d) || (values.TryGetValue(name, out string v) && v == d);

        void Compose()
        {
            ClearComposers();
            CairoFont detail = CairoFont.WhiteDetailText();
            double top = GuiStyle.TitleBarHeight + 4;
            ElementBounds searchBounds = ElementBounds.Fixed(0, top, ListWidth, 30);
            ElementBounds listBounds = ElementBounds.Fixed(0, top + 40, ListWidth, ListHeight);
            listClip = listBounds.ForkBoundingParent();
            ElementBounds listInset = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds listBar = listInset.CopyOffsetedSibling(listInset.fixedWidth + 4).WithFixedWidth(20);

            string kind = selected == null ? "text" : KindOf(selected);
            string current = selected != null && values.TryGetValue(selected, out string v) ? v : "";
            string shownText = draft ?? current;
            bool isList = kind == "list" || kind == "table";
            if (isList)
            {
                if (linesFor != selected)
                {
                    lines = (draft ?? current).Replace("\r", "").Split('\n').ToList();
                    if (lines.Count == 1 && lines[0].Length == 0) lines.Clear();
                    linesFor = selected;
                    page = 0;
                    find = "";
                    pageText = null;
                }
                else ApplyPage();
                shownText = PageView();
            }

            // The pane, top to bottom, each part below the one above it.
            double x = ListWidth + 40;
            double y = top;
            ElementBounds nameBounds = ElementBounds.Fixed(x, y, PaneWidth, 30);
            y += 34;
            string about = selected == null ? "" : About(selected);
            double aboutHeight = GuiLayout.TextHeight(capi, detail, about, PaneWidth);
            ElementBounds aboutBounds = ElementBounds.Fixed(x, y, PaneWidth, aboutHeight);
            y += aboutHeight + 6;
            string defaultLine = selected != null && defaults.TryGetValue(selected, out string def)
                ? Lang.Get(IsDefault(selected) ? "vintagee:cfg-isdefault" : "vintagee:cfg-default", Short(kind, def))
                : "";
            CairoFont grey = CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 });
            double defaultHeight = GuiLayout.TextHeight(capi, grey, defaultLine, PaneWidth);
            ElementBounds defaultBounds = ElementBounds.Fixed(x, y, PaneWidth, defaultHeight);
            y += defaultHeight + 4;
            string hint = Lang.Get("vintagee:cfg-hint-" + kind);
            double hintHeight = GuiLayout.TextHeight(capi, grey, hint, PaneWidth);
            ElementBounds hintBounds = ElementBounds.Fixed(x, y, PaneWidth, hintHeight);
            y += hintHeight + 4;
            double pagerTop = y;
            if (isList) y += 36;
            double editorTop = y;
            double editorHeight = kind == "bool" ? 40 : kind == "list" || kind == "table" ? AreaHeight : 30;
            string[] options = kind == "choice" ? ConfigureCommand.ChoicesOf(selected) : null;
            y += editorHeight + 10;

            double statusHeight = Math.Max(20, GuiLayout.TextHeight(capi, detail, status, PaneWidth));
            ElementBounds statusBounds = ElementBounds.Fixed(x, y, PaneWidth, statusHeight);
            y += statusHeight + 8;
            // Buttons at the bottom: level with the end of the list, or lower if the pane needs the room.
            double buttonsY = Math.Max(top + 40 + ListHeight + 10, y);
            ElementBounds closeBounds = ElementBounds.Fixed(0, buttonsY, 90, 30);
            ElementBounds resetAllBounds = ElementBounds.Fixed(100, buttonsY, 130, 30);
            ElementBounds saveBounds = ElementBounds.Fixed(x + PaneWidth - 100, buttonsY, 100, 30);
            ElementBounds revertBounds = ElementBounds.Fixed(x + PaneWidth - 210, buttonsY, 100, 30);
            ElementBounds resetBounds = ElementBounds.Fixed(x + PaneWidth - 370, buttonsY, 150, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(searchBounds, listClip, listBar, nameBounds, aboutBounds, defaultBounds, hintBounds, statusBounds, saveBounds, revertBounds, resetBounds, closeBounds, resetAllBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composing = true;
            GuiComposer c = capi.Gui
                .CreateCompo("vintagee-config", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:cfg-window-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .BeginClip(listClip)
                        .AddInset(listInset, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(v2 => Scroll("list", v2), listBar, "listbar")
                    .AddStaticText(selected ?? "", CairoFont.WhiteSmallishText().WithWeight(Cairo.FontWeight.Bold), nameBounds)
                    .AddStaticText(about, detail, aboutBounds)
                    .AddStaticText(defaultLine, grey, defaultBounds)
                    .AddStaticText(hint, grey, hintBounds);

            if (kind == "bool")
            {
                ElementBounds switchBounds = ElementBounds.Fixed(x, editorTop, 40, 40);
                ElementBounds switchLabel = ElementBounds.Fixed(x + 50, editorTop + 6, 200, 30);
                c.AddSwitch(on => { draft = on ? "true" : "false"; }, switchBounds, "switch")
                 .AddStaticText(Lang.Get("vintagee:cfg-on"), CairoFont.WhiteSmallText(), switchLabel);
                bgBounds.WithChildren(switchBounds, switchLabel);
            }
            else if (kind == "list" || kind == "table")
            {
                // Find box and pages: only PageLines lines are in the editor at once.
                ElementBounds findBounds = ElementBounds.Fixed(x, pagerTop, 200, 30);
                ElementBounds prevBounds = ElementBounds.Fixed(x + 210, pagerTop, 36, 30);
                ElementBounds pageLabel = ElementBounds.Fixed(x + 250, pagerTop + 6, PaneWidth - 300, 22);
                ElementBounds nextBounds = ElementBounds.Fixed(x + PaneWidth - 46, pagerTop, 36, 30);
                c.AddTextInput(findBounds, OnFind, CairoFont.WhiteSmallText(), "find")
                 .AddSmallButton("<", () => TurnPage(-1), prevBounds)
                 .AddDynamicText(PageLabel(), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), pageLabel, "pagelabel")
                 .AddSmallButton(">", () => TurnPage(1), nextBounds);
                bgBounds.WithChildren(findBounds, prevBounds, pageLabel, nextBounds);
                ElementBounds areaBounds = ElementBounds.Fixed(0, 0, PaneWidth - 30, AreaHeight);
                areaClip = ElementBounds.Fixed(x, editorTop, PaneWidth - 30, AreaHeight);
                areaClip.WithChild(areaBounds);
                ElementBounds areaInset = areaClip.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
                ElementBounds areaBar = areaInset.CopyOffsetedSibling(areaInset.fixedWidth + 4).WithFixedWidth(20);
                c.AddInset(areaInset, 3)
                 .BeginClip(areaClip)
                    .AddTextArea(areaBounds, t => { if (!composing) { pageText = t; UpdateAreaScroll(); } }, CairoFont.WhiteSmallText(), "area")
                 .EndClip()
                 .AddVerticalScrollbar(v2 => Scroll("area", v2), areaBar, "areabar");
                bgBounds.WithChildren(areaInset, areaClip, areaBar);
            }
            else if (options != null)
            {
                // A drop-down of the setting's words, each with a short explanation (vintagee:cfg-choice-<setting>-<word>).
                ElementBounds dropBounds = ElementBounds.Fixed(x, editorTop, 320, 30);
                string[] labels = options.Select(o =>
                {
                    string key = "vintagee:cfg-choice-" + selected.ToLowerInvariant() + "-" + o.ToLowerInvariant();
                    string label = Lang.Get(key);
                    return label == key ? o : label;
                }).ToArray();
                int index = Math.Max(0, Array.FindIndex(options, o => string.Equals(o, shownText, StringComparison.OrdinalIgnoreCase)));
                c.AddDropDown(options, labels, index, (code, on) => { if (!composing && on) draft = code; }, dropBounds, "dropdown");
                bgBounds.WithChildren(dropBounds);
            }
            else
            {
                ElementBounds inputBounds = ElementBounds.Fixed(x, editorTop, 220, 30);
                c.AddTextInput(inputBounds, t => { if (!composing) draft = t; }, CairoFont.WhiteSmallText(), "input");
                bgBounds.WithChildren(inputBounds);
            }

            CairoFont statusFont = statusIsError ? CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.5, 0.45, 1 }) : CairoFont.WhiteDetailText().WithColor(new double[] { 0.55, 1, 0.55, 1 });
            c.AddStaticText(status, statusFont, statusBounds)
             .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-resetall"), OnResetAll, resetAllBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-resetone"), () => { if (selected != null) AskReset(selected); return true; }, resetBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-revert"), OnRevert, revertBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-save"), OnSave, saveBounds)
             .EndChildElements();
            SingleComposer = c.Compose();

            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get("vintagee:cfg-search"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            if (kind == "bool") SingleComposer.GetSwitch("switch")?.SetValue(shownText == "true");
            else if (kind == "list" || kind == "table")
            {
                SingleComposer.GetTextArea("area")?.SetValue(shownText, false);
                GuiElementTextInput findInput = SingleComposer.GetTextInput("find");
                findInput?.SetPlaceHolderText(Lang.Get("vintagee:cfg-find"));
                if (find.Length > 0) findInput?.SetValue(find, false);
            }
            else if (options == null) SingleComposer.GetTextInput("input")?.SetValue(shownText, false);
            composing = false;
            if (kind == "list" || kind == "table")
            {
                UpdateAreaScroll();
                GuiElementTextArea editor = SingleComposer.GetTextArea("area");
                if (editor != null) editor.OnCursorMoved = (cx, cy) => KeepCaretInView(cy);
            }
            FillList();
        }

        void FillList()
        {
            if (SingleComposer == null) return;
            string needle = search.Trim().ToLowerInvariant();
            var comps = new List<RichTextComponentBase>();
            CairoFont normal = CairoFont.WhiteSmallText();
            CairoFont picked = CairoFont.WhiteSmallText().WithColor(new double[] { 0.55, 1, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
            CairoFont detail = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
            CairoFont resetFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.6, 0.8, 1, 1 });
            foreach (string name in names)
            {
                if (needle.Length > 0 && !name.ToLowerInvariant().Contains(needle) && !About(name).ToLowerInvariant().Contains(needle)) continue;
                string n = name;
                comps.Add(new LinkTextComponent(capi, name, name == selected ? picked : normal, _ => Pick(n)));
                comps.Add(new RichTextComponent(capi, "  " + Short(KindOf(name), values.TryGetValue(name, out string v) ? v : ""), detail));
                // Settings that aren't at their default get a reset link at the right of their row.
                if (!IsDefault(name)) comps.Add(new LinkTextComponent(capi, Lang.Get("vintagee:cfg-resetlink"), resetFont, _ => AskReset(n)) { Float = EnumFloat.Right });
                comps.Add(new RichTextComponent(capi, "\n", detail));
            }
            if (comps.Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get("vintagee:missing-nomatch"), normal));
            GuiElementRichtext list = SingleComposer.GetRichtext("list");
            if (list == null) return;
            list.SetNewText(comps.ToArray());
            list.Bounds.CalcWorldBounds();
            float scroll = listScroll;
            GuiElementScrollbar bar = SingleComposer.GetScrollbar("listbar");
            if (bar != null)
            {
                bar.Bounds.CalcWorldBounds();
                bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                scroll = Math.Clamp(scroll, 0, (float)Math.Max(0, list.Bounds.fixedHeight - ListHeight));
                bar.CurrentYPosition = scroll;
            }
            listScroll = scroll;
            list.Bounds.fixedY = -scroll;
            list.Bounds.CalcWorldBounds();
        }

        void UpdateAreaScroll()
        {
            GuiElementTextArea area = SingleComposer?.GetTextArea("area");
            GuiElementScrollbar bar = SingleComposer?.GetScrollbar("areabar");
            if (area == null || bar == null) return;
            bar.SetHeights((float)AreaHeight, (float)Math.Max(AreaHeight, area.Bounds.fixedHeight));
        }

        /// <summary>Scrolls the list/table editor so the line being edited stays in view while typing or moving the caret.</summary>
        void KeepCaretInView(double caretY)
        {
            GuiElementTextArea area = SingleComposer?.GetTextArea("area");
            GuiElementScrollbar bar = SingleComposer?.GetScrollbar("areabar");
            if (area == null) return;
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            double y = caretY / scale;
            double line = CairoFont.WhiteSmallText().GetFontExtents().Height / scale + 4;
            double scroll = -area.Bounds.fixedY;
            if (y < scroll) scroll = y;
            else if (y + line > scroll + AreaHeight) scroll = y + line - AreaHeight;
            else return;
            scroll = Math.Clamp(scroll, 0, Math.Max(0, area.Bounds.fixedHeight - AreaHeight));
            area.Bounds.fixedY = -scroll;
            area.Bounds.CalcWorldBounds();
            if (bar != null)
            {
                UpdateAreaScroll();
                bar.CurrentYPosition = (float)scroll;
            }
        }

        void Scroll(string key, float value)
        {
            if (SingleComposer == null) return;
            if (key == "list") listScroll = value;
            ElementBounds b = key == "list" ? SingleComposer.GetRichtext("list")?.Bounds : SingleComposer.GetTextArea("area")?.Bounds;
            if (b == null) return;
            b.fixedY = -value;
            b.CalcWorldBounds();
        }

        /// <summary>Puts the edited page back into the whole list (lines added or removed on it are added or removed there).</summary>
        void ApplyPage()
        {
            if (pageText == null || lines == null) return;
            var edited = pageText.Replace("\r", "").Split('\n').ToList();
            if (edited.Count > 0 && edited[edited.Count - 1].Length == 0) edited.RemoveAt(edited.Count - 1);
            int at = shown.Length > 0 ? shown.Min() : lines.Count;
            foreach (int i in shown.OrderByDescending(i => i)) if (i < lines.Count) lines.RemoveAt(i);
            lines.InsertRange(Math.Min(at, lines.Count), edited);
            pageText = null;
            draft = string.Join("\n", lines);
        }

        /// <summary>Which lines match the find box, and the current page of them as editor text.</summary>
        string PageView()
        {
            string needle = find.Trim();
            int[] matching = Enumerable.Range(0, lines.Count)
                .Where(i => needle.Length == 0 || lines[i].IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            int pages = Math.Max(1, (matching.Length + PageLines - 1) / PageLines);
            page = Math.Clamp(page, 0, pages - 1);
            shown = matching.Skip(page * PageLines).Take(PageLines).ToArray();
            matchCount = matching.Length;
            return string.Join("\n", shown.Select(i => lines[i]));
        }

        int matchCount;

        string PageLabel()
        {
            if (lines == null) return "";
            if (matchCount == 0) return Lang.Get(find.Trim().Length > 0 ? "vintagee:cfg-lines-nomatch" : "vintagee:cfg-lines-empty");
            int first = page * PageLines + 1, last = Math.Min(matchCount, (page + 1) * PageLines);
            return Lang.Get(find.Trim().Length > 0 ? "vintagee:cfg-lines-found" : "vintagee:cfg-lines", first, last, matchCount);
        }

        /// <summary>Shows another page (or the find results) in the editor without redrawing the window, so typing in the find box keeps going.</summary>
        void ShowPage()
        {
            if (SingleComposer == null || lines == null) return;
            ApplyPage();
            string text = PageView();
            composing = true;
            GuiElementTextArea area = SingleComposer.GetTextArea("area");
            area?.SetValue(text, false);
            composing = false;
            if (area != null)
            {
                area.Bounds.fixedY = 0;
                area.Bounds.CalcWorldBounds();
            }
            UpdateAreaScroll();
            GuiElementScrollbar bar = SingleComposer.GetScrollbar("areabar");
            if (bar != null) bar.CurrentYPosition = 0;
            SingleComposer.GetDynamicText("pagelabel")?.SetNewText(PageLabel());
        }

        bool TurnPage(int by)
        {
            ApplyPage();
            page += by;
            ShowPage();
            return true;
        }

        void OnFind(string text)
        {
            if (composing || (text ?? "") == find) return;
            ApplyPage();
            find = text ?? "";
            page = 0;
            ShowPage();
        }

        void Pick(string name)
        {
            if (name == selected) return;
            ApplyPage();
            selected = name;
            draft = null;
            linesFor = null;
            status = "";
            Recompose();
        }

        /// <summary>Redraws after the current click has been handled (the clicked element belongs to the old layout).</summary>
        void Recompose() => capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Compose(); }, "vintagee-config");

        void OnSearch(string text)
        {
            if (composing || (text ?? "") == search) return;
            search = text ?? "";
            listScroll = 0;
            FillList();
        }

        bool OnSave()
        {
            if (selected == null) return true;
            ApplyPage();
            string text = draft ?? (values.TryGetValue(selected, out string v) ? v : "");
            onSave?.Invoke(selected, text);
            status = Lang.Get("vintagee:cfg-saving");
            statusIsError = false;
            return true;
        }

        bool OnRevert()
        {
            draft = null;
            linesFor = null;
            pageText = null;
            status = "";
            Recompose();
            return true;
        }

        void AskReset(string name)
        {
            string def = defaults.TryGetValue(name, out string d) ? Short(KindOf(name), d) : "?";
            new GuiDialogConfirm(capi, Lang.Get("vintagee:cfg-resetone-confirm", name, def), ok =>
            {
                if (!ok) return;
                selected = name;
                draft = null;
                status = Lang.Get("vintagee:cfg-saving");
                statusIsError = false;
                onReset?.Invoke(name);
            }).TryOpen();
        }

        bool OnResetAll()
        {
            new GuiDialogConfirm(capi, Lang.Get("vintagee:cfg-resetall-confirm"), ok =>
            {
                if (!ok) return;
                draft = null;
                status = Lang.Get("vintagee:cfg-saving");
                statusIsError = false;
                onReset?.Invoke("*");
            }).TryOpen();
            return true;
        }
    }
}
