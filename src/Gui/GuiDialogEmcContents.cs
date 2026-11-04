using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc contents: every block/item that holds things (buckets, pots, bags...) or keeps what it's made
    /// of on the item (round shields, lanterns, chests, chiseled blocks...), with the mode each one uses:
    /// <list type="bullet">
    /// <item>item only - burns and is created as the plain item;</item>
    /// <item>item + contents - worth the item and what it holds or is made of; you learn and create exactly that version;</item>
    /// <item>item price only - exactly that version, but it costs (and burns for) only the item; its contents are free.</item>
    /// </list>
    /// Admins click names to select them (or "Select shown"), then press a mode button. "Default" goes back to the built-in choice.
    /// </summary>
    public class GuiDialogEmcContents : GuiDialog
    {
        const double ListWidth = 600;
        const double ListHeight = 320;
        const int PerPage = 40;

        sealed class Entry
        {
            public CollectibleObject Collectible;
            public string Key;
            public string Name;
            public string Code;
            public string Domain;
            public StackDataKind Kind;
            public StackDataMode Mode;
            public StackDataMode DefaultMode;
            public bool FromConfig;
        }

        readonly Action<string[], int> onSetMode;
        readonly HashSet<string> selected = new HashSet<string>();
        /// <summary>The last entry clicked without Shift: where a Shift-click range starts.</summary>
        string anchor;
        readonly List<Entry> all = new List<Entry>();
        readonly string version;
        List<Entry> shown = new List<Entry>();
        string search = "";
        string domainFilter = "*";
        string modeFilter = "*";
        string kindFilter = "*";
        int page;
        bool composing;
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcContents(ICoreClientAPI capi, EmcContentsPacket packet, GuiDialogEmcContents previous, Action<string[], int> onSetMode) : base(capi)
        {
            this.onSetMode = onSetMode;
            version = packet.Version;
            string[] keys = packet.Keys ?? Array.Empty<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                CollectibleObject c = PlayerEmc.Resolve(capi.World, keys[i]);
                if (c?.Code == null) continue;
                string name;
                try { name = c.GetHeldItemName(new ItemStack(c)); }
                catch { name = c.Code.ToShortString(); }
                all.Add(new Entry
                {
                    Collectible = c,
                    Key = keys[i],
                    Name = name,
                    Code = c.Code.ToString(),
                    Domain = c.Code.Domain,
                    Kind = (StackDataKind)Get(packet.Kinds, i),
                    Mode = (StackDataMode)Get(packet.Modes, i),
                    DefaultMode = (StackDataMode)Get(packet.DefaultModes, i),
                    FromConfig = packet.FromConfig != null && i < packet.FromConfig.Length && packet.FromConfig[i]
                });
            }
            all.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.Ordinal));
            if (previous != null)
            {
                search = previous.search;
                domainFilter = all.Any(e => e.Domain == previous.domainFilter) ? previous.domainFilter : "*";
                modeFilter = previous.modeFilter;
                kindFilter = previous.kindFilter;
                page = previous.page;
                var known = new HashSet<string>(all.Select(e => e.Key));
                foreach (string k in previous.selected) if (known.Contains(k)) selected.Add(k);
            }
            Compose();
        }

        static byte Get(byte[] arr, int i) => arr != null && i < arr.Length ? arr[i] : (byte)0;

        static string ModeLabel(StackDataMode mode) => Lang.Get("vintagee:contents-mode-" + StackDataTable.ModeName(mode));

        void Compose()
        {
            ClearComposers();
            double top = GuiStyle.TitleBarHeight + 6;
            // The help is measured so the filters always sit below it, however long it wraps.
            string help = Lang.Get("vintagee:contents-help", all.Count, version);
            double helpHeight = GuiLayout.TextHeight(capi, CairoFont.WhiteDetailText(), help, ListWidth + 30);
            ElementBounds helpBounds = ElementBounds.Fixed(0, top, ListWidth + 30, helpHeight);
            double row = top + helpHeight + 8;
            ElementBounds searchBounds = ElementBounds.Fixed(0, row, 180, 30);
            ElementBounds domainBounds = ElementBounds.Fixed(190, row, 140, 30);
            ElementBounds kindBounds = ElementBounds.Fixed(340, row, 130, 30);
            ElementBounds modeBounds = ElementBounds.Fixed(480, row, 150, 30);

            ElementBounds listBounds = ElementBounds.Fixed(0, row + 42, ListWidth, ListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);

            double bottom = row + 42 + ListHeight + 14;
            ElementBounds prevBounds = ElementBounds.Fixed(0, bottom, 36, 30);
            ElementBounds countBounds = ElementBounds.Fixed(44, bottom + 6, 250, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(300, bottom, 36, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(ListWidth - 90, bottom, 90, 30);

            double selRow = bottom + 40;
            double selWidth = ListWidth - 240;
            double selHeight = Math.Max(30, Math.Max(GuiLayout.TextHeight(capi, CairoFont.WhiteDetailText(), Lang.Get("vintagee:contents-select"), selWidth),
                GuiLayout.TextHeight(capi, CairoFont.WhiteDetailText(), Lang.Get("vintagee:missing-selected", new string('W', 40)), selWidth)));
            ElementBounds selectedBounds = ElementBounds.Fixed(0, selRow + 4, selWidth, selHeight);
            ElementBounds selectAllBounds = ElementBounds.Fixed(ListWidth - 230, selRow, 104, 30);
            ElementBounds clearBounds = ElementBounds.Fixed(ListWidth - 120, selRow, 120, 30);

            double modeRow = selRow + selHeight + 8;
            double w = (ListWidth - 30) / 4;
            ElementBounds itemBounds = ElementBounds.Fixed(0, modeRow, w, 30);
            ElementBounds fullBounds = ElementBounds.Fixed(w + 10, modeRow, w, 30);
            ElementBounds priceBounds = ElementBounds.Fixed(2 * (w + 10), modeRow, w, 30);
            ElementBounds defaultBounds = ElementBounds.Fixed(3 * (w + 10), modeRow, w, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(helpBounds, searchBounds, domainBounds, kindBounds, modeBounds, clipBounds, prevBounds, countBounds, nextBounds, closeBounds,
                selectedBounds, selectAllBounds, clearBounds, itemBounds, fullBounds, priceBounds, defaultBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string[] domains = all.Select(e => e.Domain).Distinct().OrderBy(d => d == "game" ? "" : d, StringComparer.Ordinal).ToArray();
            string[] domainValues = new[] { "*" }.Concat(domains).ToArray();
            string[] domainNames = new[] { Lang.Get("vintagee:missing-allmods") }.Concat(domains.Select(d => d + " (" + all.Count(e => e.Domain == d) + ")")).ToArray();
            string[] kindValues = { "*", "0", "1", "2" };
            string[] kindNames =
            {
                Lang.Get("vintagee:contents-allkinds"),
                Lang.Get("vintagee:contents-kind-contents") + " (" + all.Count(e => e.Kind == StackDataKind.Contents) + ")",
                Lang.Get("vintagee:contents-kind-material") + " (" + all.Count(e => e.Kind == StackDataKind.Material) + ")",
                Lang.Get("vintagee:contents-kind-chiseled") + " (" + all.Count(e => e.Kind == StackDataKind.Chiseled) + ")"
            };
            string[] modeValues = { "*", "0", "1", "2" };
            string[] modeNames =
            {
                Lang.Get("vintagee:contents-allmodes"),
                ModeLabel(StackDataMode.Item) + " (" + all.Count(e => e.Mode == StackDataMode.Item) + ")",
                ModeLabel(StackDataMode.Full) + " (" + all.Count(e => e.Mode == StackDataMode.Full) + ")",
                ModeLabel(StackDataMode.ItemPrice) + " (" + all.Count(e => e.Mode == StackDataMode.ItemPrice) + ")"
            };

            composing = true;
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-contents", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:contents-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(help, CairoFont.WhiteDetailText(), helpBounds)
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .AddDropDown(domainValues, domainNames, Math.Max(0, Array.IndexOf(domainValues, domainFilter)), (c, s) => { domainFilter = c ?? "*"; Reset(); }, domainBounds, "domain")
                    .AddDropDown(kindValues, kindNames, Math.Max(0, Array.IndexOf(kindValues, kindFilter)), (c, s) => { kindFilter = c ?? "*"; Reset(); }, kindBounds, "kind")
                    .AddDropDown(modeValues, modeNames, Math.Max(0, Array.IndexOf(modeValues, modeFilter)), (c, s) => { modeFilter = c ?? "*"; Reset(); }, modeBounds, "mode")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddSmallButton("<", () => { page--; ResetScroll(); Refill(); return true; }, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), countBounds, "count")
                    .AddSmallButton(">", () => { page++; ResetScroll(); Refill(); return true; }, nextBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), selectedBounds, "selected")
                    .AddSmallButton(Lang.Get("vintagee:missing-selectshown"), OnSelectShown, selectAllBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-clearselection"), () => { selected.Clear(); Refill(); return true; }, clearBounds)
                    .AddSmallButton(ModeLabel(StackDataMode.Item), () => SetMode((int)StackDataMode.Item), itemBounds)
                    .AddSmallButton(ModeLabel(StackDataMode.Full), () => SetMode((int)StackDataMode.Full), fullBounds)
                    .AddSmallButton(ModeLabel(StackDataMode.ItemPrice), () => SetMode((int)StackDataMode.ItemPrice), priceBounds)
                    .AddSmallButton(Lang.Get("vintagee:contents-mode-default"), () => SetMode(-1), defaultBounds)
                .EndChildElements()
                .Compose();
            composing = false;
            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get("vintagee:gui-search-missing"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            Refill();
        }

        void Reset()
        {
            page = 0;
            ResetScroll();
            Refill();
        }

        void Refill()
        {
            if (composing || SingleComposer == null) return;
            try
            {
                string needle = search.Trim().ToLowerInvariant();
                shown = all.Where(e =>
                        (domainFilter == "*" || e.Domain == domainFilter) &&
                        (kindFilter == "*" || ((int)e.Kind).ToString() == kindFilter) &&
                        (modeFilter == "*" || ((int)e.Mode).ToString() == modeFilter) &&
                        (needle.Length == 0 || e.Code.ToLowerInvariant().Contains(needle) || e.Name.ToLowerInvariant().Contains(needle)))
                    .ToList();
                int pages = Math.Max(1, (shown.Count + PerPage - 1) / PerPage);
                page = Math.Clamp(page, 0, pages - 1);

                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText();
                CairoFont pickedFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.55, 1, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
                CairoFont detailFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                CairoFont changedFont = CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.85, 0.45, 1 });
                foreach (Entry e in shown.Skip(page * PerPage).Take(PerPage))
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(e.Collectible); }
                    catch { /* no icon */ }
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 32, 6, EnumFloat.Inline) { VerticalAlign = EnumVerticalAlign.Middle });
                    Entry entry = e;
                    bool picked = selected.Contains(e.Key);
                    comps.Add(new LinkTextComponent(capi, (picked ? "✔ " : "") + e.Name + "  ", picked ? pickedFont : nameFont, _ => Toggle(entry)) { VerticalAlign = EnumVerticalAlign.Middle });
                    string kind = Lang.Get("vintagee:contents-kind-" + (e.Kind == StackDataKind.Contents ? "contents" : e.Kind == StackDataKind.Material ? "material" : "chiseled"));
                    string detail = ModeLabel(e.Mode) + (e.FromConfig ? " " + Lang.Get("vintagee:contents-setbyyou", ModeLabel(e.DefaultMode)) : " " + Lang.Get("vintagee:contents-isdefault"));
                    comps.Add(new RichTextComponent(capi, detail, e.FromConfig ? changedFont : detailFont) { VerticalAlign = EnumVerticalAlign.Middle });
                    comps.Add(new RichTextComponent(capi, "  ·  " + kind + "  ·  " + e.Code + "\n", detailFont) { VerticalAlign = EnumVerticalAlign.Middle });
                }
                if (shown.Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get(all.Count == 0 ? "vintagee:contents-empty" : "vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));

                GuiElementRichtext list = SingleComposer.GetRichtext("list");
                if (list == null) return;
                float scroll = (float)-list.Bounds.fixedY;
                GuiTextures.Release(list); // the old link components own GL textures the element won't free
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
                SingleComposer.GetDynamicText("count")?.SetNewText(Lang.Get("vintagee:missing-page", page + 1, pages, shown.Count, all.Count));
                UpdateStatus();
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the contents list: {0}", ex);
            }
        }

        void UpdateStatus(string text = null)
        {
            if (SingleComposer == null) return;
            if (text == null)
            {
                if (selected.Count == 0) text = Lang.Get("vintagee:contents-select");
                else if (selected.Count == 1)
                {
                    string name = all.FirstOrDefault(x => selected.Contains(x.Key))?.Name ?? selected.First();
                    text = Lang.Get("vintagee:missing-selected", name.Length <= 40 ? name : name.Substring(0, 39) + "…");
                }
                else text = Lang.Get("vintagee:missing-selectedmany", selected.Count);
            }
            SingleComposer.GetDynamicText("selected")?.SetNewText(text);
        }

        void Toggle(Entry e)
        {
            int from = anchor == null ? -1 : shown.FindIndex(x => x.Key == anchor);
            int to = shown.IndexOf(e);
            if (GuiDialogEmcMissing.ShiftHeld(capi) && from >= 0 && to >= 0)
            {
                // Shift-click: everything from the last clicked entry to this one (in the order shown) is selected.
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) selected.Add(shown[i].Key);
            }
            else
            {
                if (!selected.Remove(e.Key)) selected.Add(e.Key);
                anchor = e.Key;
            }
            Refill();
        }

        bool OnSelectShown()
        {
            foreach (Entry e in shown) selected.Add(e.Key);
            Refill();
            return true;
        }

        bool SetMode(int mode)
        {
            string[] keys = all.Where(e => selected.Contains(e.Key)).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:contents-select")); return true; }
            onSetMode?.Invoke(keys, mode);
            UpdateStatus(Lang.Get("vintagee:contents-sending", keys.Length));
            return true;
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            Reset();
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
