using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The EMC doctor's window (/emc diagnose, Ctrl+H): a checklist. At the top, how many checks passed, warned or
    /// failed, "Run checks again" and a progress bar; then one card per check with a coloured bar and dot, the check
    /// in words and a badge (OK / Look / Problem), which opens to show the details, what to do, and buttons for the
    /// fixes and the reports it links to; sections and questions in between; at the bottom the last run and Close.
    /// Built from the same report lines the text window shows, so every report (item, mod, trace...) renders here.
    /// </summary>
    /// <summary>
    /// Shapes drawn with Cairo (a card, a bar, a dot) as an interactive element: it keeps its own texture and draws it
    /// where its bounds are each frame, so it scrolls with the list. (A static draw is baked into the dialog's
    /// background once and stays put.)
    /// </summary>
    public class GuiElementCanvas : GuiElement
    {
        readonly Action<Context, ElementBounds> draw;
        LoadedTexture texture;

        public GuiElementCanvas(ICoreClientAPI capi, ElementBounds bounds, Action<Context, ElementBounds> draw) : base(capi, bounds)
        {
            this.draw = draw;
            texture = new LoadedTexture(capi);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();
            int w = Math.Max(1, (int)Math.Ceiling(Bounds.OuterWidth)), h = Math.Max(1, (int)Math.Ceiling(Bounds.OuterHeight));
            using var surface = new ImageSurface(Format.Argb32, w, h);
            using var ctx = new Context(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            // The drawing is at the origin of its own surface; the delegate only reads the bounds' size.
            draw(ctx, Bounds);
            generateTexture(surface, ref texture);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, Bounds);
        }

        public override void Dispose()
        {
            base.Dispose();
            texture?.Dispose();
        }
    }

    public class GuiDialogEmcDoctor : GuiDialog
    {
        const double Width = 740;
        const double ListHeight = 430;
        const double Pad = 12;
        const double CardX = 0, CardW = Width - 24;
        const double TitleX = 58, BadgeW = 110;

        static readonly double[] Green = { 0.42, 0.75, 0.30, 1 };
        static readonly double[] Orange = { 0.92, 0.62, 0.18, 1 };
        static readonly double[] Red = { 0.85, 0.30, 0.28, 1 };
        static readonly double[] Grey = { 0.55, 0.52, 0.48, 1 };
        static readonly double[] Blue = { 0.55, 0.8, 1, 1 };
        static readonly double[] Gold = { 0.80, 0.62, 0.30, 1 };
        static readonly double[] CardBg = { 0, 0, 0, 0.28 };
        static readonly double[] DetailBg = { 0, 0, 0, 0.18 };

        readonly Action<string, string> onDiagnose;
        readonly Action<string, byte[]> onOpenItem;
        readonly Action<string> onSetValue;
        readonly Stack<EmcReportPacket> history = new Stack<EmcReportPacket>();
        EmcReportPacket current;
        readonly HashSet<int> expanded = new HashSet<int>();
        bool busy;
        string busyText;
        DateTime receivedAt = DateTime.Now;
        bool composing;
        /// <summary>The next report replaces this one instead of going on top (Run checks again).</summary>
        bool replaceNext;

        public override string ToggleKeyCombinationCode => null;
        public override double DrawOrder => 0.25;
        public override double InputOrder => 0.25;

        public GuiDialogEmcDoctor(ICoreClientAPI capi, EmcReportPacket packet, Action<string, string> onDiagnose, Action<string, byte[]> onOpenItem, Action<string> onSetValue) : base(capi)
        {
            this.onDiagnose = onDiagnose;
            this.onOpenItem = onOpenItem;
            this.onSetValue = onSetValue;
            current = packet;
            Compose();
        }

        /// <summary>A new report from the server: on top of the one shown (Back returns to it), or in its place after Run checks again.</summary>
        public void Show(EmcReportPacket packet)
        {
            if (packet == null) return;
            if (!replaceNext && current != null && current.Key != packet.Key) history.Push(current);
            replaceNext = false;
            busy = false;
            busyText = null;
            current = packet;
            receivedAt = DateTime.Now;
            expanded.Clear();
            Compose();
        }

        // ------------------------------------------------------------------ the model

        enum Kind { Check, Heading, Text, Banner }
        enum State { Ok, Warn, Fail, Info }

        sealed class Entry
        {
            public Kind Kind;
            public State State;
            public string Title;      // the check in words, without its ✓/⚠/• mark
            public string Plain;
            public int Index;
            public EmcLineStyle Style;
            public int Indent;
            public readonly List<Detail> Details = new List<Detail>();
            public readonly List<(string action, string arg, string label)> Actions = new List<(string, string, string)>();
            public bool HasFix => Details.Any(d => d.Fix) || Actions.Any(a => a.action.StartsWith("fix-", StringComparison.Ordinal));
        }

        sealed class Detail
        {
            public string Text;       // with [[item]] refs, action tokens taken out
            public EmcLineStyle Style;
            public int Indent;
            public bool Fix;          // a "→ do this" line
            public readonly List<(string action, string arg, string label)> Actions = new List<(string, string, string)>();
        }

        List<Entry> cached;
        EmcReportPacket cachedFor;

        List<Entry> Entries()
        {
            if (cachedFor == current && cached != null) return cached;
            var entries = new List<Entry>();
            string[] texts = current?.Texts ?? Array.Empty<string>();
            Entry open = null;
            for (int i = 0; i < texts.Length; i++)
            {
                string t = texts[i] ?? "";
                EmcLineStyle style = current.Styles != null && i < current.Styles.Length ? (EmcLineStyle)current.Styles[i] : EmcLineStyle.Text;
                int indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0;
                if (style == EmcLineStyle.Check && indent == 0)
                {
                    var e = new Entry { Kind = Kind.Check, Index = i, Style = style, Indent = 0 };
                    if (t.StartsWith("✓", StringComparison.Ordinal)) { e.State = State.Ok; e.Title = t.Substring(1).Trim(); }
                    else if (t.StartsWith("⚠", StringComparison.Ordinal)) { e.State = State.Fail; e.Title = t.Substring(1).Trim(); }
                    else if (t.StartsWith("•", StringComparison.Ordinal)) { e.State = State.Info; e.Title = t.Substring(1).Trim(); }
                    else { e.State = State.Info; e.Title = t; }
                    e.Title = TakeActions(e.Title, e.Actions);
                    e.Plain = EmcReport.Plain(capi.World, e.Title);
                    entries.Add(e);
                    open = e;
                    continue;
                }
                if (style == EmcLineStyle.Heading)
                {
                    entries.Add(new Entry { Kind = Kind.Heading, Index = i, Title = t, Plain = EmcReport.Plain(capi.World, t), Style = style });
                    open = null;
                    continue;
                }
                bool detail = open != null && (indent > 0 || style == EmcLineStyle.Note);
                if (detail)
                {
                    var d = new Detail { Style = style, Indent = indent, Fix = t.StartsWith("→", StringComparison.Ordinal) };
                    d.Text = TakeActions(d.Fix ? t.Substring(1).Trim() : t, d.Actions);
                    open.Details.Add(d);
                    continue;
                }
                open = null;
                var plain = new Entry { Kind = i == 0 && (style == EmcLineStyle.Total || style == EmcLineStyle.Warn) ? Kind.Banner : Kind.Text, Index = i, Style = style, Indent = indent };
                plain.State = style == EmcLineStyle.Warn ? State.Fail : style == EmcLineStyle.Total ? State.Ok : State.Info;
                plain.Title = TakeActions(t, plain.Actions);
                plain.Plain = EmcReport.Plain(capi.World, plain.Title);
                entries.Add(plain);
            }
            // A check whose details hold a warning is a warning, not a failure, when the check itself passed; a ⚠ with
            // a fix is a problem; a ⚠ without one is something to look at.
            foreach (Entry e in entries.Where(e => e.Kind == Kind.Check && e.State == State.Fail))
            {
                if (!e.HasFix && !e.Details.Any(d => d.Style == EmcLineStyle.Warn)) e.State = State.Warn;
            }
            cachedFor = current;
            cached = entries;
            return entries;
        }

        /// <summary>Pulls the {{action|arg|label}} tokens out of a line into buttons, leaving the text.</summary>
        static string TakeActions(string text, List<(string, string, string)> actions)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("{{", StringComparison.Ordinal) < 0) return text ?? "";
            string rest = EmcReport.ActionToken.Replace(text, m => { actions.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)); return ""; });
            return rest.Trim();
        }

        // ------------------------------------------------------------------ composing

        void Compose()
        {
            ClearComposers();
            List<Entry> entries = Entries();
            int ok = entries.Count(e => e.Kind == Kind.Check && e.State == State.Ok);
            int warn = entries.Count(e => e.Kind == Kind.Check && e.State == State.Warn);
            int fail = entries.Count(e => e.Kind == Kind.Check && e.State == State.Fail);
            int checks = entries.Count(e => e.Kind == Kind.Check);

            double top = GuiStyle.TitleBarHeight + 8;
            CairoFont chipFont = CairoFont.WhiteSmallText();
            var chips = new List<(string text, double[] colour, double x, double w)>();
            double cx = 0;
            foreach (var (n, word, colour) in new[] { (ok, Lang.Get("vintagee:doctor-passed"), Green), (warn, Lang.Get("vintagee:doctor-warnings"), Orange), (fail, Lang.Get("vintagee:doctor-failed"), Red) })
            {
                string text = n + " " + word;
                double w = TextWidth(chipFont, text) + 22;
                chips.Add((text, colour, cx, w));
                cx += w + 10;
            }
            ElementBounds runBounds = ElementBounds.Fixed(Width - 160, top - 2, 160, 30);
            ElementBounds barBounds = ElementBounds.Fixed(0, top + 38, Width, 6);

            double listTop = top + 56;
            ElementBounds listBounds = ElementBounds.Fixed(0, listTop, Width, ListHeight);
            ElementBounds clipBounds = listBounds.ForkBoundingParent();
            ElementBounds scrollbarBounds = ElementBounds.Fixed(Width + 4, listTop, 20, ListHeight);

            double bottom = listTop + ListHeight + 12;
            ElementBounds backBounds = ElementBounds.Fixed(0, bottom, 80, 30);
            ElementBounds lastBounds = ElementBounds.Fixed(90, bottom + 6, 360, 24);
            ElementBounds copyBounds = ElementBounds.Fixed(Width - 190, bottom, 90, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(Width - 90, bottom, 90, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(runBounds, barBounds, clipBounds, scrollbarBounds, backBounds, lastBounds, copyBounds, closeBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composing = true;
            GuiComposer c = capi.Gui.CreateCompo("vintagee-doctor", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(string.IsNullOrEmpty(current?.Title) ? Lang.Get("vintagee:diag-title") : current.Title, () => TryClose())
                .BeginChildElements(bgBounds);

            // Summary chips and the progress bar.
            foreach (var chip in chips)
            {
                ElementBounds cb = ElementBounds.Fixed(chip.x, top, chip.w, 26);
                bgBounds.WithChild(cb);
                double[] colour = chip.colour;
                Canvas(c, cb, (ctx, b) => DrawChip(ctx, b, colour));
                c.AddStaticText(chip.text, chipFont.Clone().WithColor(colour), ElementBounds.Fixed(chip.x + 11, top + 4, chip.w, 24).WithParent(bgBounds));
            }
            c.AddSmallButton(busy ? Lang.Get("vintagee:doctor-scanning") : Lang.Get("vintagee:doctor-run"), OnRun, runBounds, EnumButtonStyle.Normal, "run");
            Canvas(c, barBounds, (ctx, b) => DrawBar(ctx, b, busy ? 0.55 : 1));

            // The cards.
            ElementBounds content = ElementBounds.Fixed(0, 0, Width, 10);
            c.BeginClip(clipBounds).BeginChildElements(content);
            double y = 0;
            foreach (Entry e in entries) y = AddEntry(c, content, e, y);
            if (entries.Count == 0) y = AddRow(c, content, Lang.Get("vintagee:missing-nomatch"), CairoFont.WhiteSmallText(), 0, y);
            content.fixedHeight = Math.Max(y + 4, ListHeight);
            c.EndChildElements().EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .AddSmallButton(Lang.Get("vintagee:reportgui-back"), OnBack, backBounds, EnumButtonStyle.Normal, "back")
                .AddDynamicText(busy ? (busyText ?? "") : Lang.Get("vintagee:doctor-lastrun", receivedAt.ToString("HH:mm:ss"), checks), CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 }), lastBounds, "last")
                .AddSmallButton(Lang.Get("vintagee:reportgui-copy"), OnCopy, copyBounds)
                .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds);
            SingleComposer = c.EndChildElements().Compose();
            composing = false;
            SingleComposer.GetButton("back").Enabled = history.Count > 0;
            SingleComposer.GetButton("run").Enabled = !busy;
            GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
            bar?.SetHeights((float)ListHeight, (float)content.fixedHeight);
            scrollContent = content;
        }

        ElementBounds scrollContent;

        /// <summary>Width (unscaled GUI units) of one line of text in a font.</summary>
        double TextWidth(CairoFont font, string text)
        {
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            return font.GetTextExtents(text ?? "").Width / scale;
        }

        /// <summary>Bounds inside the scrolling content, registered as its child so they move when it scrolls.</summary>
        static ElementBounds In(ElementBounds parent, ElementBounds bounds)
        {
            parent.WithChild(bounds);
            return bounds;
        }

        static GuiComposer Canvas(GuiComposer c, ElementBounds bounds, Action<Context, ElementBounds> draw)
        {
            c.AddInteractiveElement(new GuiElementCanvas(c.Api, bounds, draw));
            return c;
        }

        /// <summary>One entry: a card, a section heading, a banner or a plain row. Returns the y below it.</summary>
        double AddEntry(GuiComposer c, ElementBounds parent, Entry e, double y)
        {
            switch (e.Kind)
            {
                case Kind.Heading:
                    {
                        CairoFont f = CairoFont.WhiteSmallishText().WithColor(Gold).WithWeight(FontWeight.Bold);
                        y += 6;
                        y = AddRow(c, parent, e.Plain, f, 2, y);
                        ElementBounds line = In(parent, ElementBounds.Fixed(CardX, y - 2, CardW, 2));
                        Canvas(c, line, (ctx, b) => { ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], 0.35); ctx.Rectangle(0, 0, b.OuterWidth, GuiElement.scaled(1)); ctx.Fill(); });
                        return y + 6;
                    }
                case Kind.Banner:
                    {
                        double[] colour = e.State == State.Fail ? Red : Green;
                        CairoFont f = CairoFont.WhiteSmallText().WithColor(colour).WithWeight(FontWeight.Bold);
                        double h = GuiLayout.TextHeight(capi, f, e.Plain, CardW - 28) + 14;
                        ElementBounds box = In(parent, ElementBounds.Fixed(CardX, y, CardW, h));
                        Canvas(c, box, (ctx, b) => DrawCard(ctx, b, colour, DetailBg));
                        AddText(c, parent, e.Title, f, CardX + 16, y + 7, CardW - 28);
                        y = AddActions(c, parent, e.Actions, CardX + 16, y + h - 4, CardW - 28, dense: true);
                        return y + 8;
                    }
                case Kind.Text:
                    {
                        CairoFont f = e.Style == EmcLineStyle.Note ? CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 })
                            : e.Style == EmcLineStyle.Warn ? CairoFont.WhiteSmallText().WithColor(Red)
                            : e.Style == EmcLineStyle.Total ? CairoFont.WhiteSmallText().WithColor(Green).WithWeight(FontWeight.Bold)
                            : CairoFont.WhiteSmallText();
                        if (e.Title.Length > 0) y = AddText(c, parent, e.Title, f, CardX + 4 + e.Indent * 16, y, CardW - 8 - e.Indent * 16);
                        if (e.Actions.Count > 0) y = AddActions(c, parent, e.Actions, CardX + 4 + e.Indent * 16, y + 2, CardW - 8, dense: e.Title.Length == 0);
                        return y + 2;
                    }
                default:
                    return AddCard(c, parent, e, y);
            }
        }

        double AddCard(GuiComposer c, ElementBounds parent, Entry e, double y)
        {
            double[] colour = e.State == State.Ok ? Green : e.State == State.Warn ? Orange : e.State == State.Fail ? Red : Grey;
            bool canOpen = e.Details.Count > 0 || e.Actions.Count > 0;
            bool open = canOpen && expanded.Contains(e.Index);
            CairoFont titleFont = CairoFont.WhiteSmallText();
            double titleW = CardW - TitleX - BadgeW - 24;
            double titleH = GuiLayout.TextHeight(capi, titleFont, e.Plain, titleW);
            double headH = Math.Max(40, titleH + 14);

            // The details' height first, so the card can be drawn in one piece.
            double detailH = 0;
            var layout = new List<(Detail d, double y, double h, CairoFont f)>();
            if (open)
            {
                double dy = 8;
                foreach (Detail d in e.Details)
                {
                    CairoFont f = d.Fix ? CairoFont.WhiteSmallText().WithColor(new double[] { 0.92, 0.88, 0.75, 1 })
                        : d.Style == EmcLineStyle.Warn ? CairoFont.WhiteSmallText().WithColor(Red)
                        : d.Style == EmcLineStyle.Total ? CairoFont.WhiteSmallText().WithColor(Green)
                        : CairoFont.WhiteDetailText().WithColor(new double[] { 0.82, 0.80, 0.76, 1 });
                    string text = d.Fix ? "→ " + EmcReport.Plain(capi.World, d.Text) : EmcReport.Plain(capi.World, d.Text);
                    double h = text.Length > 0 ? GuiLayout.TextHeight(capi, f, text, CardW - TitleX - 20 - Math.Max(0, d.Indent - 1) * 14) : 0;
                    layout.Add((d, dy, h, f));
                    dy += h + (d.Actions.Count > 0 ? 34 : 0);
                }
                if (e.Actions.Count > 0) dy += 34;
                detailH = dy + 6;
            }
            double cardH = headH + detailH;
            ElementBounds box = In(parent, ElementBounds.Fixed(CardX, y, CardW, cardH));
            double hh = headH;
            Canvas(c, box, (ctx, b) => DrawCheckCard(ctx, b, colour, hh, open));

            // Arrow + title as one link that opens and closes the card.
            string arrow = canOpen ? (open ? "▾  " : "▸  ") : "    ";
            var comps = new List<RichTextComponentBase>();
            if (canOpen)
            {
                int index = e.Index;
                var link = new LinkTextComponent(capi, arrow, titleFont, _ => Toggle(index)) { VerticalAlign = EnumVerticalAlign.Middle };
                link.Font = titleFont.Clone().WithColor(new double[] { 0.85, 0.8, 0.7, 1 });
                comps.Add(link);
                AddRichParts(comps, e.Title, titleFont, index);
            }
            else
            {
                comps.Add(new RichTextComponent(capi, arrow, titleFont) { VerticalAlign = EnumVerticalAlign.Middle });
                AddRichParts(comps, e.Title, titleFont, -1);
            }
            c.AddRichtext(comps.ToArray(), In(parent, ElementBounds.Fixed(CardX + 22, y + (headH - titleH) / 2 + 1, titleW + 30, titleH + 4)));

            // The badge.
            string badge = e.State == State.Ok ? Lang.Get("vintagee:doctor-badge-ok") : e.State == State.Warn ? Lang.Get("vintagee:doctor-badge-warn") : e.State == State.Fail ? Lang.Get("vintagee:doctor-badge-fail") : Lang.Get("vintagee:doctor-badge-info");
            CairoFont badgeFont = CairoFont.WhiteDetailText().WithColor(colour);
            double bw = TextWidth(badgeFont, badge) + 18;
            ElementBounds bb = In(parent, ElementBounds.Fixed(CardX + CardW - bw - 12, y + (headH - 22) / 2, bw, 22));
            Canvas(c, bb, (ctx, b) => DrawChip(ctx, b, colour));
            c.AddRichtext(new RichTextComponentBase[] { new RichTextComponent(capi, badge, badgeFont) }, In(parent, ElementBounds.Fixed(CardX + CardW - bw - 12 + 9, y + (headH - 22) / 2 + 3, bw, 20)));

            if (open)
            {
                double baseY = y + headH;
                foreach (var (d, dy, h, f) in layout)
                {
                    double x = TitleX + Math.Max(0, d.Indent - 1) * 14;
                    string text = d.Fix ? "→ " + d.Text : d.Text;
                    if (text.Length > 0) AddText(c, parent, text, f, x, baseY + dy, CardW - x - 20);
                    if (d.Actions.Count > 0) AddActions(c, parent, d.Actions, x, baseY + dy + h + 2, CardW - x - 20, dense: true);
                }
                if (e.Actions.Count > 0) AddActions(c, parent, e.Actions, TitleX, baseY + detailH - 38, CardW - TitleX - 20, dense: true);
            }
            return y + cardH + 8;
        }

        /// <summary>Text with [[item]] refs as icon + link; returns the y below it.</summary>
        double AddText(GuiComposer c, ElementBounds parent, string text, CairoFont font, double x, double y, double width)
        {
            string plain = EmcReport.Plain(capi.World, text);
            double h = GuiLayout.TextHeight(capi, font, plain, width);
            var comps = new List<RichTextComponentBase>();
            AddRichParts(comps, text, font, -1);
            c.AddRichtext(comps.ToArray(), In(parent, ElementBounds.Fixed(x, y, width, h + 2)));
            return y + h;
        }

        double AddRow(GuiComposer c, ElementBounds parent, string text, CairoFont font, double x, double y)
        {
            double h = GuiLayout.TextHeight(capi, font, text, CardW - x);
            c.AddRichtext(new RichTextComponentBase[] { new RichTextComponent(capi, text, font) }, In(parent, ElementBounds.Fixed(x, y, CardW - x, h + 2)));
            return y + h;
        }

        /// <summary>The pieces of a line: text, and each [[key]] as an item icon and name that opens that item's report.</summary>
        void AddRichParts(List<RichTextComponentBase> comps, string text, CairoFont font, int toggleIndex)
        {
            CairoFont linkFont = font.Clone().WithColor(Blue);
            int pos = 0;
            foreach (System.Text.RegularExpressions.Match m in EmcReport.RefToken.Matches(text))
            {
                if (m.Index > pos) comps.Add(Piece(text.Substring(pos, m.Index - pos), font, toggleIndex));
                string key = m.Groups[1].Value;
                CollectibleObject col = PlayerEmc.Resolve(capi.World, key);
                if (col?.Code != null)
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(col); } catch { }
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 24, 3, EnumFloat.Inline, _ => OpenItem(key)) { VerticalAlign = EnumVerticalAlign.Middle });
                    var link = new LinkTextComponent(capi, EmcExplain.NameOf(col), linkFont, _ => OpenItem(key)) { VerticalAlign = EnumVerticalAlign.Middle };
                    link.Font = linkFont.Clone();
                    comps.Add(link);
                }
                else comps.Add(Piece(key, font, toggleIndex));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) comps.Add(Piece(text.Substring(pos), font, toggleIndex));
        }

        RichTextComponentBase Piece(string text, CairoFont font, int toggleIndex)
        {
            if (toggleIndex < 0) return new RichTextComponent(capi, text, font) { VerticalAlign = EnumVerticalAlign.Middle };
            var link = new LinkTextComponent(capi, text, font, _ => Toggle(toggleIndex)) { VerticalAlign = EnumVerticalAlign.Middle };
            link.Font = font.Clone();
            return link;
        }

        /// <summary>A row of buttons for a line's actions: fixes and the reports it opens. Returns the y below them.</summary>
        double AddActions(GuiComposer c, ElementBounds parent, List<(string action, string arg, string label)> actions, double x, double y, double width, bool dense)
        {
            if (actions.Count == 0) return y;
            CairoFont f = CairoFont.ButtonText();
            double bx = x;
            double rowY = y;
            foreach (var (action, arg, label) in actions)
            {
                bool fix = action.StartsWith("fix-", StringComparison.Ordinal) || action == "setvalue";
                string text = (fix ? "⚒ " : "") + label;
                double w = Math.Min(width, TextWidth(f, text) + 24);
                if (bx > x && bx + w > x + width) { bx = x; rowY += 32; }
                string a = action, g = arg;
                c.AddSmallButton(text, () => { Act(a, g); return true; }, In(parent, ElementBounds.Fixed(bx, rowY, w, 28)), EnumButtonStyle.Small);
                bx += w + 8;
            }
            return rowY + 30;
        }

        // ------------------------------------------------------------------ drawing

        static void DrawChip(Context ctx, ElementBounds b, double[] colour)
        {
            double r = GuiElement.scaled(4);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            ctx.SetSourceRGBA(0, 0, 0, 0.3);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 0.7);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
        }

        static void DrawBar(Context ctx, ElementBounds b, double fraction)
        {
            double r = GuiElement.scaled(3);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            ctx.SetSourceRGBA(0, 0, 0, 0.45);
            ctx.Fill();
            if (fraction <= 0) return;
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth * Math.Min(1, fraction), b.OuterHeight, r);
            ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], 0.9);
            ctx.Fill();
        }

        static void DrawCard(Context ctx, ElementBounds b, double[] colour, double[] bg)
        {
            double r = GuiElement.scaled(4);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            ctx.SetSourceRGBA(bg[0], bg[1], bg[2], bg[3]);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.35);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
            ctx.Rectangle(0, 0, GuiElement.scaled(3), b.OuterHeight);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 1);
            ctx.Fill();
        }

        static void DrawCheckCard(Context ctx, ElementBounds b, double[] colour, double headH, bool open)
        {
            DrawCard(ctx, b, colour, CardBg);
            if (open)
            {
                // A faint line under the header, and a slightly darker body.
                double hy = GuiElement.scaled(headH);
                ctx.Rectangle(GuiElement.scaled(3), hy, b.OuterWidth - GuiElement.scaled(3), b.OuterHeight - GuiElement.scaled(headH));
                ctx.SetSourceRGBA(0, 0, 0, 0.12);
                ctx.Fill();
                ctx.SetSourceRGBA(1, 1, 1, 0.08);
                ctx.Rectangle(GuiElement.scaled(12), hy, b.OuterWidth - GuiElement.scaled(24), GuiElement.scaled(1));
                ctx.Fill();
            }
            // The status dot, with a soft glow.
            double cx = GuiElement.scaled(38), cy = GuiElement.scaled(headH / 2), rad = GuiElement.scaled(6);
            ctx.Arc(cx, cy, rad * 1.8, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 0.18);
            ctx.Fill();
            ctx.Arc(cx, cy, rad, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 1);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.5);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
        }

        // ------------------------------------------------------------------ actions

        void Toggle(int index)
        {
            if (!expanded.Remove(index)) expanded.Add(index);
            float scroll = SingleComposer?.GetScrollbar("scrollbar")?.CurrentYPosition ?? 0;
            Compose();
            GuiElementScrollbar bar = SingleComposer?.GetScrollbar("scrollbar");
            if (bar != null) { bar.CurrentYPosition = scroll; OnScroll(scroll); }
        }

        void Act(string action, string arg)
        {
            if (action == "setvalue") { onSetValue?.Invoke(arg); return; }
            SetBusy(Lang.Get("vintagee:reportgui-asking"));
            onDiagnose?.Invoke(action ?? "", arg ?? "");
        }

        void OpenItem(string key)
        {
            SetBusy(Lang.Get("vintagee:reportgui-asking"));
            onOpenItem?.Invoke(key, null);
        }

        void SetBusy(string text)
        {
            busy = true;
            busyText = text;
            SingleComposer?.GetDynamicText("last")?.SetNewText(text ?? "");
            if (SingleComposer?.GetButton("run") is GuiElementTextButton run) run.Enabled = false;
        }

        bool OnRun()
        {
            replaceNext = true;
            string key = current?.Key ?? "";
            int bar = key.IndexOf('|');
            string action = bar < 0 ? key : key.Substring(0, bar), arg = bar < 0 ? "" : key.Substring(bar + 1);
            SetBusy(Lang.Get("vintagee:doctor-scanning"));
            onDiagnose?.Invoke(action, arg);
            return true;
        }

        bool OnBack()
        {
            if (history.Count == 0) return true;
            current = history.Pop();
            expanded.Clear();
            busy = false;
            Compose();
            return true;
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            string[] texts = current?.Texts ?? Array.Empty<string>();
            for (int i = 0; i < texts.Length; i++)
            {
                int indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0;
                sb.Append(' ', indent * 2).Append(EmcReport.Plain(capi.World, texts[i])).Append('\n');
            }
            try { capi.Forms.SetClipboardText(sb.ToString().TrimEnd()); }
            catch (Exception ex) { capi.Logger.Warning("Vintage-E: clipboard not available: {0}", ex.Message); return true; }
            SingleComposer?.GetDynamicText("last")?.SetNewText(Lang.Get("vintagee:reportgui-copied"));
            return true;
        }

        void OnScroll(float value)
        {
            if (scrollContent == null) return;
            scrollContent.fixedY = -value;
            scrollContent.CalcWorldBounds();
        }
    }
}
