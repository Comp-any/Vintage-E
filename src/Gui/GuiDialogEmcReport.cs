using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The window /emc dump hand and /emc loop open: an item's value and everything that went into it, or the list of
    /// value loops. Blocks and items show as an icon and a name; click one to see its own value explained (Back goes
    /// back). The search box keeps the sections (or loops) that mention what you type. Copy puts the text on the
    /// clipboard; the server also saved it to a file, named at the bottom. Admins only (the server checks).
    /// </summary>
    public class GuiDialogEmcReport : GuiDialog
    {
        const double ListWidth = 640;
        const double ListHeight = 400;
        /// <summary>Roughly how many lines and item icons fit on one page before it's split.</summary>
        const int PageWeight = 140;

        readonly Action<string, byte[]> onOpenItem;
        readonly Action onOpenLoops;
        readonly Action<string> onSetValue;
        readonly Action<string, string> onDiagnose;
        readonly Stack<EmcReportPacket> history = new Stack<EmcReportPacket>();
        EmcReportPacket current;
        /// <summary>The next report replaces this one instead of going on top of it (Refresh).</summary>
        bool replaceNext;
        string search = "";
        int page;
        int pages = 1;
        bool composing;
        string status;
        /// <summary>Check lines (✓/⚠/•) whose details are shown; details are hidden until the line is clicked.</summary>
        readonly HashSet<int> expanded = new HashSet<int>();
        bool expandAll;

        public override string ToggleKeyCombinationCode => null;
        /// <summary>Above the handbook (0.2), below the hovered-item value box (0.3) it can open.</summary>
        public override double DrawOrder => 0.25;
        public override double InputOrder => 0.25;

        public bool IsDump => current?.Kind != EmcReport.LoopsKind && current?.Kind != EmcReport.DiagnoseKind;
        public bool IsDiagnose => current?.Kind == EmcReport.DiagnoseKind;

        public GuiDialogEmcReport(ICoreClientAPI capi, EmcReportPacket packet, Action<string, byte[]> onOpenItem, Action onOpenLoops, Action<string> onSetValue, Action<string, string> onDiagnose = null) : base(capi)
        {
            this.onOpenItem = onOpenItem;
            this.onOpenLoops = onOpenLoops;
            this.onSetValue = onSetValue;
            this.onDiagnose = onDiagnose;
            current = packet;
            Compose();
        }

        /// <summary>A new report from the server: on top of the one shown (Back returns to it), or in its place after Refresh.</summary>
        public void Show(EmcReportPacket packet)
        {
            if (packet == null) return;
            if (!replaceNext && current != null && !(current.Kind == packet.Kind && current.Key == packet.Key && (current.Stack == null) == (packet.Stack == null))) history.Push(current);
            if (!replaceNext && current != null && current.Kind != packet.Kind) search = "";
            replaceNext = false;
            current = packet;
            page = 0;
            status = null;
            expanded.Clear();
            expandAll = false;
            Compose();
        }

        void Compose()
        {
            ClearComposers();
            double top = GuiStyle.TitleBarHeight + 6;
            ElementBounds backBounds = ElementBounds.Fixed(0, top, 70, 30);
            ElementBounds searchBounds = ElementBounds.Fixed(80, top, 240, 30);
            ElementBounds expandBounds = ElementBounds.Fixed(330, top, 120, 30);
            ElementBounds refreshBounds = ElementBounds.Fixed(ListWidth - 100, top, 100, 30);

            ElementBounds listBounds = ElementBounds.Fixed(0, top + 42, ListWidth, ListHeight);
            ElementBounds clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);

            double bottom = top + 42 + ListHeight + 14;
            ElementBounds prevBounds = ElementBounds.Fixed(0, bottom, 36, 30);
            ElementBounds countBounds = ElementBounds.Fixed(44, bottom + 6, 150, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(200, bottom, 36, 30);
            double bw = 96;
            ElementBounds closeBounds = ElementBounds.Fixed(ListWidth - bw, bottom, bw, 30);
            ElementBounds copyBounds = ElementBounds.Fixed(ListWidth - 2 * bw - 8, bottom, bw, 30);
            ElementBounds handbookBounds = ElementBounds.Fixed(ListWidth - 3 * bw - 16, bottom, bw, 30);
            ElementBounds setBounds = ElementBounds.Fixed(ListWidth - 4 * bw - 24, bottom, bw, 30);

            string file = string.IsNullOrEmpty(current?.File) ? Lang.Get("vintagee:dump-nofile") : Lang.Get("vintagee:dump-file", current.File);
            double fileHeight = GuiLayout.TextHeight(capi, CairoFont.WhiteDetailText(), file, ListWidth);
            ElementBounds fileBounds = ElementBounds.Fixed(0, bottom + 40, ListWidth, Math.Max(20, fileHeight));
            ElementBounds statusBounds = ElementBounds.Fixed(0, bottom + 44 + fileBounds.fixedHeight, ListWidth, 20);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(backBounds, searchBounds, refreshBounds, clipBounds, prevBounds, countBounds, nextBounds, closeBounds, copyBounds, fileBounds, statusBounds);
            if (IsDump) bgBounds.WithChildren(handbookBounds, setBounds);
            if (IsDiagnose) bgBounds.WithChildren(expandBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composing = true;
            GuiComposer c = capi.Gui
                .CreateCompo("vintagee-report", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(string.IsNullOrEmpty(current?.Title) ? Lang.Get(IsDiagnose ? "vintagee:diag-title" : "vintagee:loopgui-title") : current.Title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddSmallButton(Lang.Get("vintagee:reportgui-back"), OnBack, backBounds, EnumButtonStyle.Normal, "back")
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .AddSmallButton(Lang.Get("vintagee:reportgui-refresh"), OnRefresh, refreshBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddSmallButton("<", () => { page--; ResetScroll(); Refill(); return true; }, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), countBounds, "count")
                    .AddSmallButton(">", () => { page++; ResetScroll(); Refill(); return true; }, nextBounds)
                    .AddSmallButton(Lang.Get("vintagee:reportgui-copy"), OnCopy, copyBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                    .AddStaticText(file, CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 }), fileBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), statusBounds, "status");
            if (IsDump)
            {
                c.AddSmallButton(Lang.Get("vintagee:reportgui-setvalue"), OnSetValue, setBounds)
                 .AddSmallButton(Lang.Get("vintagee:reportgui-handbook"), OnHandbook, handbookBounds);
            }
            if (IsDiagnose)
            {
                c.AddSmallButton(Lang.Get(expandAll ? "vintagee:reportgui-collapse" : "vintagee:reportgui-expand"), OnExpandAll, expandBounds, EnumButtonStyle.Normal, "expand");
            }
            SingleComposer = c.EndChildElements().Compose();
            composing = false;
            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get(IsDump ? "vintagee:reportgui-search" : IsDiagnose ? "vintagee:reportgui-searchdiag" : "vintagee:reportgui-searchloops"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            SingleComposer.GetButton("back").Enabled = history.Count > 0;
            Refill();
        }

        // ------------------------------------------------------------------ the text

        sealed class Line
        {
            public int Index;
            public string Text;
            public int Indent;
            public EmcLineStyle Style;
            public int Block;
            public string Plain;
            public int Refs;
            /// <summary>The check line this one is a detail of (its index), or -1.</summary>
            public int Header = -1;
            /// <summary>For a check line: how many detail lines follow it.</summary>
            public int Details;
        }

        List<Line> cachedLines;
        EmcReportPacket cachedFor;

        List<Line> Lines()
        {
            if (cachedFor == current && cachedLines != null) return cachedLines;
            var lines = new List<Line>();
            string[] texts = current?.Texts ?? Array.Empty<string>();
            for (int i = 0; i < texts.Length; i++)
            {
                string t = texts[i] ?? "";
                lines.Add(new Line
                {
                    Index = i,
                    Text = t,
                    Indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0,
                    Style = current.Styles != null && i < current.Styles.Length ? (EmcLineStyle)current.Styles[i] : EmcLineStyle.Text,
                    Block = current.Blocks != null && i < current.Blocks.Length ? current.Blocks[i] : 0,
                    Plain = EmcReport.Plain(capi.World, t),
                    Refs = EmcReport.AnyToken.Matches(t).Count
                });
            }
            // A check line (✓ / ⚠ / •) owns the indented lines and notes that follow it, until the next check, heading
            // or plain line at the margin: those are its details, hidden until it's clicked.
            Line header = null;
            foreach (Line line in lines)
            {
                if (line.Style == EmcLineStyle.Check && line.Indent == 0) { header = line; continue; }
                if (header != null && line.Block == header.Block && line.Style != EmcLineStyle.Heading && (line.Indent > 0 || line.Style == EmcLineStyle.Note))
                {
                    line.Header = header.Index;
                    header.Details++;
                }
                else header = null;
            }
            cachedFor = current;
            cachedLines = lines;
            return lines;
        }

        /// <summary>The lines the search keeps (whole blocks), split into pages that don't get too heavy to draw.</summary>
        List<List<Line>> Pages(List<Line> lines)
        {
            string needle = search.Trim().ToLowerInvariant();
            var blocks = lines.GroupBy(l => l.Block).ToList();
            var keep = blocks.Where(b => b.Key == 0 || needle.Length == 0 || b.Any(l => l.Plain.ToLowerInvariant().Contains(needle))).ToList();
            var result = new List<List<Line>>();
            var pageLines = new List<Line>();
            int weight = 0;
            foreach (var block in keep)
            {
                // Details stay hidden behind their check line unless it's expanded, or they're what the search found.
                var shown = block.Where(l => l.Header < 0 || expandAll || expanded.Contains(l.Header) || (needle.Length > 0 && l.Plain.ToLowerInvariant().Contains(needle))).ToList();
                int w = shown.Sum(l => 1 + l.Refs);
                if (pageLines.Count > 0 && weight + w > PageWeight)
                {
                    result.Add(pageLines);
                    pageLines = new List<Line>();
                    weight = 0;
                }
                pageLines.AddRange(shown);
                weight += w;
            }
            if (pageLines.Count > 0 || result.Count == 0) result.Add(pageLines);
            return result;
        }

        void Refill()
        {
            if (composing || SingleComposer == null) return;
            try
            {
                List<Line> all = Lines();
                List<List<Line>> split = Pages(all);
                pages = Math.Max(1, split.Count);
                page = Math.Clamp(page, 0, pages - 1);
                var comps = new List<RichTextComponentBase>();
                foreach (Line line in split[page]) AddLine(comps, line);
                if (all.Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get("vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));

                GuiElementRichtext list = SingleComposer.GetRichtext("list");
                if (list == null) return;
                float scroll = (float)-list.Bounds.fixedY;
                list.SetNewText(comps.ToArray());
                list.Bounds.CalcWorldBounds();
                GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
                if (bar != null)
                {
                    bar.Bounds.CalcWorldBounds();
                    bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                    scroll = Math.Clamp(scroll, 0, (float)Math.Max(0, list.Bounds.fixedHeight - ListHeight));
                    bar.CurrentYPosition = scroll;
                }
                list.Bounds.fixedY = -scroll;
                list.Bounds.CalcWorldBounds();
                SingleComposer.GetDynamicText("count")?.SetNewText(Lang.Get("vintagee:reportgui-page", page + 1, pages));
                SingleComposer.GetDynamicText("status")?.SetNewText(status ?? "");
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the report window: {0}", ex);
            }
        }

        static CairoFont FontFor(EmcLineStyle style, string text = null)
        {
            switch (style)
            {
                case EmcLineStyle.Check:
                    if (text != null && text.StartsWith("\u2713", StringComparison.Ordinal)) return CairoFont.WhiteSmallText().WithColor(new double[] { 0.6, 1, 0.6, 1 }).WithWeight(Cairo.FontWeight.Bold);
                    if (text != null && text.StartsWith("\u26a0", StringComparison.Ordinal)) return CairoFont.WhiteSmallText().WithColor(new double[] { 1, 0.6, 0.45, 1 }).WithWeight(Cairo.FontWeight.Bold);
                    return CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);
                case EmcLineStyle.Heading: return CairoFont.WhiteSmallishText().WithColor(new double[] { 1, 0.85, 0.45, 1 }).WithWeight(Cairo.FontWeight.Bold);
                case EmcLineStyle.Total: return CairoFont.WhiteSmallText().WithColor(new double[] { 0.6, 1, 0.6, 1 }).WithWeight(Cairo.FontWeight.Bold);
                case EmcLineStyle.Note: return CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 });
                case EmcLineStyle.Warn: return CairoFont.WhiteSmallText().WithColor(new double[] { 1, 0.6, 0.45, 1 });
                default: return CairoFont.WhiteSmallText();
            }
        }

        /// <summary>One line: its text, with each [[key]] as an item icon and a name to click.</summary>
        void AddLine(List<RichTextComponentBase> comps, Line line)
        {
            CairoFont font = FontFor(line.Style, line.Text);
            CairoFont linkFont = font.Clone().WithColor(new double[] { 0.55, 0.8, 1, 1 });
            int first = comps.Count;
            if (line.Style == EmcLineStyle.Heading && first > 0) comps.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteDetailText()));
            int start = comps.Count;
            string text = line.Text;
            int pos = 0;
            if (line.Style == EmcLineStyle.Check && line.Details > 0)
            {
                // The arrow opens and closes the details; the whole line is the link.
                bool open = expandAll || expanded.Contains(line.Index);
                int index = line.Index;
                comps.Add(new LinkTextComponent(capi, (open ? "\u25be " : "\u25b8 ") + line.Plain, font, _ => Toggle(index)) { VerticalAlign = EnumVerticalAlign.Middle });
                comps.Add(new RichTextComponent(capi, "\n", font));
                return;
            }
            foreach (System.Text.RegularExpressions.Match m in EmcReport.AnyToken.Matches(text))
            {
                if (m.Index > pos) comps.Add(new RichTextComponent(capi, text.Substring(pos, m.Index - pos), font) { VerticalAlign = EnumVerticalAlign.Middle });
                if (m.Groups[1].Success) AddRef(comps, m.Groups[1].Value, linkFont);
                else AddAction(comps, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, font);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) comps.Add(new RichTextComponent(capi, text.Substring(pos), font) { VerticalAlign = EnumVerticalAlign.Middle });
            comps.Add(new RichTextComponent(capi, "\n", font));
            if (line.Indent > 0 && comps.Count > start) comps[start].PaddingLeft += line.Indent * 18;
        }

        void AddRef(List<RichTextComponentBase> comps, string key, CairoFont linkFont)
        {
            CollectibleObject c = PlayerEmc.Resolve(capi.World, key);
            if (c?.Code == null)
            {
                comps.Add(new RichTextComponent(capi, key, linkFont) { VerticalAlign = EnumVerticalAlign.Middle });
                return;
            }
            ItemStack stack = null;
            try { stack = new ItemStack(c); }
            catch { /* no icon */ }
            if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 26, 3, EnumFloat.Inline, _ => Open(key)) { VerticalAlign = EnumVerticalAlign.Middle });
            comps.Add(new LinkTextComponent(capi, EmcExplain.NameOf(c), linkFont, _ => Open(key)) { VerticalAlign = EnumVerticalAlign.Middle });
        }

        /// <summary>An answer in a diagnose report: a button-like link that asks the server for the next report.</summary>
        void AddAction(List<RichTextComponentBase> comps, string action, string arg, string label, CairoFont font)
        {
            CairoFont actionFont = font.Clone().WithColor(new double[] { 1, 0.9, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
            bool fix = action.StartsWith("fix-", StringComparison.Ordinal) || action == "setvalue";
            if (fix) actionFont = actionFont.WithColor(new double[] { 0.6, 1, 0.6, 1 });
            comps.Add(new LinkTextComponent(capi, (fix ? "\u2692 " : "\u25b8 ") + label, actionFont, _ => Diagnose(action, arg)) { VerticalAlign = EnumVerticalAlign.Middle });
        }

        void Toggle(int index)
        {
            if (!expanded.Remove(index)) expanded.Add(index);
            if (expandAll)
            {
                // Closing one line while everything is open: keep the others open and drop back to per-line state.
                expandAll = false;
                foreach (Line l in Lines()) if (l.Details > 0 && l.Index != index) expanded.Add(l.Index);
                Compose();
                return;
            }
            Refill();
        }

        bool OnExpandAll()
        {
            expandAll = !expandAll;
            expanded.Clear();
            Compose(); // the button's label changes with it
            return true;
        }

        void Diagnose(string action, string arg)
        {
            if (action == "setvalue") { onSetValue?.Invoke(arg); return; }
            if (onDiagnose == null) return;
            SetStatus(Lang.Get("vintagee:reportgui-asking"));
            onDiagnose(action ?? "", arg ?? "");
        }

        void Open(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (IsDump && key == current?.Key) return;
            SetStatus(Lang.Get("vintagee:reportgui-asking"));
            onOpenItem?.Invoke(key, null);
        }

        void SetStatus(string text)
        {
            status = text;
            SingleComposer?.GetDynamicText("status")?.SetNewText(text ?? "");
        }

        // ------------------------------------------------------------------ buttons

        bool OnBack()
        {
            if (history.Count == 0) return true;
            current = history.Pop();
            page = 0;
            status = null;
            Compose();
            return true;
        }

        bool OnRefresh()
        {
            replaceNext = true;
            SetStatus(Lang.Get("vintagee:reportgui-asking"));
            if (IsDiagnose)
            {
                string key = current?.Key ?? "";
                int bar = key.IndexOf('|');
                Diagnose(bar < 0 ? key : key.Substring(0, bar), bar < 0 ? "" : key.Substring(bar + 1));
            }
            else if (IsDump) onOpenItem?.Invoke(current?.Key, current?.Stack);
            else onOpenLoops?.Invoke();
            return true;
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            foreach (Line line in Lines())
            {
                if (line.Style == EmcLineStyle.Heading && sb.Length > 0) sb.Append('\n');
                sb.Append(' ', line.Indent * 2).Append(line.Plain).Append('\n');
            }
            try { capi.Forms.SetClipboardText(sb.ToString().TrimEnd()); }
            catch (Exception ex) { capi.Logger.Warning("Vintage-E: clipboard not available: {0}", ex.Message); return true; }
            SetStatus(Lang.Get("vintagee:reportgui-copied"));
            return true;
        }

        bool OnSetValue()
        {
            if (!string.IsNullOrEmpty(current?.Key)) onSetValue?.Invoke(current.Key);
            return true;
        }

        bool OnHandbook()
        {
            CollectibleObject c = PlayerEmc.Resolve(capi.World, current?.Key);
            if (c?.Code == null) return true;
            // Same page code the survival mod's handbook uses for item stacks.
            string pageCode = (c.ItemClass == EnumItemClass.Block ? "block" : "item") + "-" + c.Code.ToShortString();
            if (capi.LinkProtocols != null && capi.LinkProtocols.TryGetValue("handbook", out Action<LinkTextComponent> open))
            {
                TryClose(); // the handbook opens underneath this window otherwise
                try { open?.Invoke(new LinkTextComponent("handbook://" + pageCode)); }
                catch (Exception ex) { capi.Logger.Warning("Vintage-E: could not open handbook page {0}: {1}", pageCode, ex.Message); }
            }
            return true;
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            page = 0;
            ResetScroll();
            Refill();
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list == null) return;
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }

        void ResetScroll()
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list != null) list.Bounds.fixedY = 0;
        }
    }
}
