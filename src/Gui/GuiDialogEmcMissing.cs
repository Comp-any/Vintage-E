using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>Which list a <see cref="GuiDialogEmcMissing"/> window shows.</summary>
    public enum EmcListKind : byte
    {
        /// <summary>/emc missing: blocks/items without an EMC value.</summary>
        Missing = 0,
        /// <summary>/emc blacklist: everything the blacklist keeps from having a value.</summary>
        Blacklist = 1,
        /// <summary>/emc notransmute: valued items that can't be burned, learned or created.</summary>
        NoTransmute = 2,
        /// <summary>/emc graylist: kept out of the missing list and never given a guessed value, but valued by recipes.</summary>
        GrayList = 3
    }

    /// <summary>
    /// Window opened by /emc missing, /emc blacklist and /emc notransmute: a scrollable list with icons, a search bar, a
    /// mod filter and a blocks/items filter. Each can mix in a second list: "Show blacklisted" (missing window), untick
    /// "Hide missing" (blacklist window), "Show valued items" (no-transmute window); those entries are tagged and get
    /// their own button. Click an icon to open its handbook page; "Copy codes" copies the filtered list.
    /// <para>Admins click names to select them (click again to unselect; Shift-click selects everything between the last
    /// click and this one; "Select shown" selects everything the filters show), then use the buttons. The list refreshes
    /// after each change and the selection is kept where it still applies.</para>
    /// <para>Every text is measured and each row is placed below the one above it, so nothing overlaps whatever its
    /// length or the GUI scale.</para>
    /// </summary>
    public class GuiDialogEmcMissing : GuiDialog
    {
        const double ListWidth = 580;
        const double ListHeight = 320;
        const double Width = ListWidth + 26; // list + scrollbar
        const double ButtonWidth = 230;
        const int PerPage = 40; // keeps the rendered text small; page with the arrows for more

        sealed class Entry
        {
            public CollectibleObject Collectible;
            public string Key;
            string name;
            public string Name
            {
                get
                {
                    if (name != null) return name;
                    try { name = Collectible.GetHeldItemName(new ItemStack(Collectible)); }
                    catch { name = Collectible.Code.ToShortString(); }
                    return name ??= Collectible.Code.ToShortString();
                }
            }
            public string Code;
            public string Domain;
            public bool IsBlock;
            public string Why;
            /// <summary>From the other list (blacklisted in the missing window, missing in the blacklist window, valued in the no-transmute window).</summary>
            public bool Other;
        }

        /// <summary>The second list's switch per window kind ("Show blacklisted" off, "Hide missing" on, "Show valued" off); remembered while the game runs.</summary>
        static bool showBlacklisted;
        static bool hideMissing = true;
        static bool showValued;
        static bool hideMissingGray = true;

        readonly EmcMissingPacket packet;
        readonly Action<string[], double> onSetValue;
        readonly Action<string[], bool> onList;
        readonly Action<string, bool> onListMod;
        readonly Action<string[], bool> onGray;

        public EmcListKind Kind { get; }
        public bool IsBlacklist => Kind == EmcListKind.Blacklist;

        readonly HashSet<string> selected = new HashSet<string>();
        readonly List<Entry> all = new List<Entry>();
        List<Entry> shown = new List<Entry>();
        /// <summary>The last entry clicked without Shift: where a Shift-click range starts.</summary>
        string anchor;
        string search = "";
        string domainFilter = "*";
        string typeFilter = "*";
        int page;
        bool composing;
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        bool ShowOther => Kind switch
        {
            EmcListKind.Blacklist => !hideMissing,
            EmcListKind.GrayList => !hideMissingGray,
            EmcListKind.NoTransmute => showValued,
            _ => showBlacklisted
        };

        /// <param name="onList">Keys, then true to add them to this window's list (blacklist / no-transmute), false to take them off.</param>
        /// <param name="onListMod">The same for a whole mod (its domain).</param>
        public GuiDialogEmcMissing(ICoreClientAPI capi, EmcMissingPacket packet, GuiDialogEmcMissing previous = null,
            Action<string[], double> onSetValue = null, Action<string[], bool> onList = null, Action<string, bool> onListMod = null,
            Action<string[], bool> onGray = null) : base(capi)
        {
            this.packet = packet;
            this.onSetValue = onSetValue;
            this.onList = onList;
            this.onListMod = onListMod;
            this.onGray = onGray;
            Kind = packet.Kind != 0 ? (EmcListKind)packet.Kind : packet.Blacklist ? EmcListKind.Blacklist : EmcListKind.Missing;

            var seenKeys = new HashSet<string>();
            void Add(string[] keys, string[] why, bool other)
            {
                keys ??= Array.Empty<string>();
                for (int ki = 0; ki < keys.Length; ki++)
                {
                    string key = keys[ki];
                    if (key == null || !seenKeys.Add(key)) continue;
                    CollectibleObject c = PlayerEmc.Resolve(capi.World, key);
                    if (c?.Code == null) continue;
                    all.Add(new Entry
                    {
                        Collectible = c, Key = key, Code = c.Code.ToString(), Domain = c.Code.Domain,
                        IsBlock = c.ItemClass == EnumItemClass.Block, Other = other,
                        Why = why != null && ki < why.Length && !string.IsNullOrEmpty(why[ki]) ? why[ki] : null
                    });
                }
            }
            Add(packet.Keys, packet.Why, false);
            Add(packet.OtherKeys, packet.OtherWhy, true);
            all.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.Ordinal));
            if (previous != null)
            {
                search = previous.search;
                domainFilter = all.Any(e => e.Domain == previous.domainFilter) ? previous.domainFilter : "*";
                typeFilter = previous.typeFilter;
                page = previous.page;
                anchor = previous.anchor;
                var keys = new HashSet<string>(all.Select(e => e.Key));
                foreach (string k in previous.selected) if (keys.Contains(k)) selected.Add(k);
            }
            Compose();
        }

        string L(string missing, string blacklist, string noTransmute, string gray = null) => Lang.Get(Kind switch
        {
            EmcListKind.Blacklist => blacklist,
            EmcListKind.NoTransmute => noTransmute,
            EmcListKind.GrayList => gray ?? blacklist,
            _ => missing
        });

        void Compose()
        {
            ClearComposers();
            CairoFont detail = CairoFont.WhiteDetailText();
            double top = GuiStyle.TitleBarHeight + 6;

            List<Entry> listable = all.Where(e => !e.Other || ShowOther).ToList();
            int mainCount = all.Count(e => !e.Other), otherCount = all.Count - mainCount;
            bool canShowOther = otherCount > 0;

            // 1. Summary, full width, as tall as it needs.
            string summary = Kind switch
            {
                EmcListKind.Blacklist => Lang.Get(mainCount == 0 ? "vintagee:blacklist-summary-none" : "vintagee:blacklist-summary", mainCount, packet.ValueCount, packet.Version),
                EmcListKind.NoTransmute => Lang.Get(mainCount == 0 ? "vintagee:notransmute-summary-none" : "vintagee:notransmute-summary", mainCount, packet.ValueCount, packet.Version),
                EmcListKind.GrayList => Lang.Get(mainCount == 0 ? "vintagee:gray-summary-none" : "vintagee:gray-summary", mainCount, packet.ValueCount, packet.Version),
                _ => mainCount == 0
                    ? Lang.Get("vintagee:missing-summary-none", packet.CreativeTotal, packet.Version)
                    : Lang.Get("vintagee:missing-summary", mainCount, packet.CreativeTotal, packet.ValueCount, packet.Version)
            };
            double y = top;
            double summaryHeight = GuiLayout.TextHeight(capi, detail, summary, Width);
            ElementBounds summaryBounds = ElementBounds.Fixed(0, y, Width, summaryHeight);
            y += summaryHeight + 6;

            // 2. The second list's switch, on its own row.
            ElementBounds showSwitchBounds = null, showLabelBounds = null;
            if (canShowOther)
            {
                showSwitchBounds = ElementBounds.Fixed(0, y, 30, 30);
                showLabelBounds = ElementBounds.Fixed(38, y + 5, Width - 38, 24);
                y += 36;
            }

            // 3. Filters.
            ElementBounds searchBounds = ElementBounds.Fixed(0, y, 230, 30);
            ElementBounds domainBounds = ElementBounds.Fixed(240, y, 180, 30);
            ElementBounds typeBounds = ElementBounds.Fixed(430, y, Width - 430, 30);
            y += 42;

            // 4. The list.
            ElementBounds listBounds = ElementBounds.Fixed(0, y, ListWidth, ListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);
            y += ListHeight + 14;

            // 5. Paging, copy and close.
            ElementBounds prevBounds = ElementBounds.Fixed(0, y, 36, 30);
            ElementBounds countBounds = ElementBounds.Fixed(42, y + 6, Width - 42 - 36 - 236 - 8, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(Width - 236 - 44, y, 36, 30);
            ElementBounds copyBounds = ElementBounds.Fixed(Width - 230, y, 130, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(Width - 90, y, 90, 30);
            y += 40;

            // Rows with a text on the left and buttons on the right: the text gets the room left of the buttons and the
            // row is as tall as that text needs.
            double textWidth = Width - ButtonWidth - 12;
            double RowHeight(params string[] texts) => Math.Max(30, texts.Max(t => GuiLayout.TextHeight(capi, detail, t, textWidth)));

            // 6. Selection status / select shown / clear.
            double selHeight = RowHeight(Lang.Get("vintagee:missing-select"), Lang.Get("vintagee:missing-selected", new string('W', 40)));
            ElementBounds selectedBounds = ElementBounds.Fixed(0, y + 4, textWidth, selHeight);
            ElementBounds selectAllBounds = ElementBounds.Fixed(Width - ButtonWidth, y, 104, 30);
            ElementBounds clearBounds = ElementBounds.Fixed(Width - 120, y, 120, 30);
            y += selHeight + 8;

            // 7. Value / set value / (missing window) gray-list it / the window's own list button.
            ElementBounds valueBounds = ElementBounds.Fixed(0, y, 110, 30);
            ElementBounds setBounds = ElementBounds.Fixed(116, y, 92, 30);
            ElementBounds grayBounds = ElementBounds.Fixed(214, y, Width - ButtonWidth - 214 - 8, 30);
            ElementBounds listButtonBounds = ElementBounds.Fixed(Width - ButtonWidth, y, ButtonWidth, 30);
            bool grayButton = Kind == EmcListKind.Missing && onGray != null;
            y += 38;

            // 8. The second list's button, while it's shown.
            bool otherRow = canShowOther && ShowOther;
            string otherHint = L("vintagee:missing-unblacklisthint", "vintagee:blacklist-blacklisthint", "vintagee:notransmute-addhint", "vintagee:gray-addhint");
            ElementBounds otherHintBounds = null, otherButtonBounds = null;
            if (otherRow)
            {
                double h = RowHeight(otherHint);
                otherHintBounds = ElementBounds.Fixed(0, y + 4, textWidth, h);
                otherButtonBounds = ElementBounds.Fixed(Width - ButtonWidth, y, ButtonWidth, 30);
                y += h + 8;
            }

            // 9. Whole mod.
            double modHeight = RowHeight(Lang.Get("vintagee:missing-modhint-pick"), Lang.Get("vintagee:missing-modhint", new string('w', 24)));
            ElementBounds modHintBounds = ElementBounds.Fixed(0, y + 4, textWidth, modHeight);
            ElementBounds modButtonBounds = ElementBounds.Fixed(Width - ButtonWidth, y, ButtonWidth, 30);
            y += modHeight + 8;

            // 10. (Missing window) only the picked mod's missing ones, by their own codes: what has a value keeps it.
            bool modMissingRow = Kind == EmcListKind.Missing;
            ElementBounds modMissingHintBounds = null, modMissingButtonBounds = null;
            if (modMissingRow)
            {
                double h = RowHeight(Lang.Get("vintagee:missing-modmissinghint", 999, new string('w', 24)));
                modMissingHintBounds = ElementBounds.Fixed(0, y + 4, textWidth, h);
                modMissingButtonBounds = ElementBounds.Fixed(Width - ButtonWidth, y, ButtonWidth, 30);
            }

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(summaryBounds, searchBounds, domainBounds, typeBounds, clipBounds, prevBounds, countBounds, nextBounds, copyBounds, closeBounds,
                selectedBounds, selectAllBounds, clearBounds, valueBounds, setBounds, listButtonBounds, modButtonBounds, modHintBounds);
            if (canShowOther) bgBounds.WithChildren(showSwitchBounds, showLabelBounds);
            if (otherRow) bgBounds.WithChildren(otherHintBounds, otherButtonBounds);
            if (grayButton) bgBounds.WithChildren(grayBounds);
            if (modMissingRow) bgBounds.WithChildren(modMissingHintBounds, modMissingButtonBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string[] domains = listable.Select(e => e.Domain).Distinct().OrderBy(d => d == "game" ? "" : d, StringComparer.Ordinal).ToArray();
            string[] domainValues = new[] { "*" }.Concat(domains).ToArray();
            string[] domainNames = new[] { Lang.Get("vintagee:missing-allmods") }
                .Concat(domains.Select(d => d + " (" + listable.Count(e => e.Domain == d) + ")")).ToArray();
            string[] typeValues = { "*", "block", "item" };
            string[] typeNames =
            {
                Lang.Get("vintagee:missing-alltypes"),
                Lang.Get("vintagee:missing-blocks") + " (" + listable.Count(e => e.IsBlock) + ")",
                Lang.Get("vintagee:missing-items") + " (" + listable.Count(e => !e.IsBlock) + ")"
            };

            string listButton = L("vintagee:missing-blacklist", "vintagee:missing-unblacklist", "vintagee:notransmute-remove", "vintagee:gray-remove");
            string modButton = L("vintagee:missing-blacklistmod", "vintagee:missing-unblacklistmod", "vintagee:notransmute-removemod", "vintagee:gray-removemod");
            string otherButton = L("vintagee:missing-unblacklist", "vintagee:missing-blacklist", "vintagee:notransmute-add", "vintagee:gray-add");
            string showLabel = Kind switch
            {
                EmcListKind.Blacklist => Lang.Get("vintagee:blacklist-hidemissing", otherCount),
                EmcListKind.GrayList => Lang.Get("vintagee:blacklist-hidemissing", otherCount),
                EmcListKind.NoTransmute => Lang.Get("vintagee:notransmute-showvalued", otherCount),
                _ => Lang.Get("vintagee:missing-showblacklisted", otherCount)
            };

            composing = true;
            GuiComposer composer = capi.Gui
                .CreateCompo("vintagee-list-" + Kind, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(L("vintagee:missing-title", "vintagee:blacklist-title", "vintagee:notransmute-title", "vintagee:gray-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(summary, detail, summaryBounds)
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .AddDropDown(domainValues, domainNames, Math.Max(0, Array.IndexOf(domainValues, domainFilter)), OnDomain, domainBounds, "domain")
                    .AddDropDown(typeValues, typeNames, Math.Max(0, Array.IndexOf(typeValues, typeFilter)), OnType, typeBounds, "type")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddSmallButton("<", OnPrev, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), countBounds, "count")
                    .AddSmallButton(">", OnNext, nextBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-copy"), OnCopy, copyBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                    .AddDynamicText("", detail, selectedBounds, "selected")
                    .AddSmallButton(Lang.Get("vintagee:missing-selectshown"), OnSelectShown, selectAllBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-clearselection"), OnClearSelection, clearBounds)
                    .AddTextInput(valueBounds, null, CairoFont.WhiteSmallText(), "value")
                    .AddSmallButton(Lang.Get("vintagee:missing-set"), OnSet, setBounds)
                    .AddSmallButton(listButton, OnListButton, listButtonBounds)
                    .AddDynamicText("", detail, modHintBounds, "modhint")
                    .AddSmallButton(modButton, OnModButton, modButtonBounds);
            if (grayButton) composer.AddSmallButton(Lang.Get("vintagee:gray-add"), OnGrayButton, grayBounds);
            if (modMissingRow)
            {
                composer.AddDynamicText("", detail, modMissingHintBounds, "modmissinghint")
                        .AddSmallButton(Lang.Get("vintagee:missing-blacklistmodmissing"), OnModMissingButton, modMissingButtonBounds);
            }
            if (canShowOther)
            {
                composer.AddSwitch(OnToggleOther, showSwitchBounds, "showother", 26)
                        .AddStaticText(showLabel, detail, showLabelBounds);
            }
            if (otherRow)
            {
                composer.AddStaticText(otherHint, detail, otherHintBounds)
                        .AddSmallButton(otherButton, OnOtherButton, otherButtonBounds);
            }
            SingleComposer = composer.EndChildElements().Compose();
            SingleComposer.GetSwitch("showother")?.SetValue(Kind == EmcListKind.Blacklist ? hideMissing : Kind == EmcListKind.GrayList ? hideMissingGray : ShowOther);

            composing = false;
            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get("vintagee:gui-search-missing"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            SingleComposer.GetTextInput("value")?.SetPlaceHolderText(Lang.Get("vintagee:missing-valuehint"));
            Refill();
        }

        void Refill()
        {
            if (composing || SingleComposer == null) return;
            try
            {
                string needle = search.Trim().ToLowerInvariant();
                shown = all.Where(e =>
                        (!e.Other || ShowOther) &&
                        (domainFilter == "*" || e.Domain == domainFilter) &&
                        (typeFilter == "*" || (typeFilter == "block") == e.IsBlock) &&
                        (needle.Length == 0 || e.Code.ToLowerInvariant().Contains(needle) || e.Name.ToLowerInvariant().Contains(needle)))
                    .ToList();
                int pages = Math.Max(1, (shown.Count + PerPage - 1) / PerPage);
                page = Math.Clamp(page, 0, pages - 1);

                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText();
                CairoFont pickedFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.55, 1, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
                CairoFont codeFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                CairoFont whyFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.85, 0.75, 0.5, 1 });
                // Mixed-in entries: red for blacklisted, amber for missing, blue for valued.
                double[] otherColor = Kind switch
                {
                    EmcListKind.Blacklist => new double[] { 1, 0.8, 0.4, 1 },
                    EmcListKind.GrayList => new double[] { 1, 0.8, 0.4, 1 },
                    EmcListKind.NoTransmute => new double[] { 0.6, 0.8, 1, 1 },
                    _ => new double[] { 1, 0.55, 0.5, 1 }
                };
                CairoFont otherFont = CairoFont.WhiteSmallText().WithColor(otherColor);
                string otherTag = L("vintagee:missing-blacklistedtag", "vintagee:blacklist-missingtag", "vintagee:notransmute-valuedtag", "vintagee:blacklist-missingtag") + "  ";
                foreach (Entry e in shown.Skip(page * PerPage).Take(PerPage))
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(e.Collectible); }
                    catch { /* no icon for this one */ }
                    // Inline icons, one entry per line - the same layout the handbook uses for item lists.
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 32, 6, EnumFloat.Inline, OpenHandbook) { VerticalAlign = EnumVerticalAlign.Middle });
                    Entry entry = e;
                    bool picked = selected.Contains(e.Key);
                    comps.Add(new LinkTextComponent(capi, (picked ? "✔ " : "") + e.Name + "  ", picked ? pickedFont : e.Other ? otherFont : nameFont, _ => Toggle(entry)) { VerticalAlign = EnumVerticalAlign.Middle });
                    if (e.Other) comps.Add(new RichTextComponent(capi, otherTag, otherFont) { VerticalAlign = EnumVerticalAlign.Middle });
                    comps.Add(new RichTextComponent(capi, (e.IsBlock ? "block:" : "item:") + e.Code + "\n", codeFont) { VerticalAlign = EnumVerticalAlign.Middle });
                    if (!string.IsNullOrEmpty(e.Why)) comps.Add(new RichTextComponent(capi, "      " + e.Why + "\n", whyFont));
                }
                int listable = all.Count(e => !e.Other || ShowOther);
                if (shown.Count == 0)
                {
                    comps.Add(new RichTextComponent(capi, listable == 0 ? L("vintagee:missing-empty", "vintagee:blacklist-empty", "vintagee:notransmute-empty", "vintagee:gray-empty") : Lang.Get("vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));
                }

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
                    float max = (float)Math.Max(0, list.Bounds.fixedHeight - ListHeight);
                    scroll = Math.Clamp(scroll, 0, max);
                    bar.CurrentYPosition = scroll;
                }
                list.Bounds.fixedY = -scroll;
                list.Bounds.CalcWorldBounds();

                SingleComposer.GetDynamicText("count")?.SetNewText(Lang.Get("vintagee:missing-page", page + 1, pages, shown.Count, listable));
                UpdateStatus();
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the EMC list: {0}", ex);
            }
        }

        static string Short(string s) => s == null ? "" : s.Length <= 40 ? s : s.Substring(0, 39) + "…";

        void UpdateStatus(string text = null)
        {
            if (SingleComposer == null) return;
            if (text == null)
            {
                if (selected.Count == 0) text = Lang.Get("vintagee:missing-select");
                else if (selected.Count == 1)
                {
                    Entry e = all.FirstOrDefault(x => selected.Contains(x.Key));
                    text = Lang.Get("vintagee:missing-selected", Short(e?.Name ?? selected.First()));
                }
                else text = Lang.Get("vintagee:missing-selectedmany", selected.Count);
                // Selections survive changing the filters; say when some of them aren't shown.
                var shownKeys = new HashSet<string>(shown.Select(x => x.Key));
                int hidden = selected.Count(k => !shownKeys.Contains(k));
                if (hidden > 0) text += " " + Lang.Get("vintagee:missing-selectedhidden", hidden);
            }
            SingleComposer.GetDynamicText("selected")?.SetNewText(text);
            SingleComposer.GetDynamicText("modhint")?.SetNewText(domainFilter == "*"
                ? Lang.Get("vintagee:missing-modhint-pick")
                : Lang.Get("vintagee:missing-modhint", domainFilter));
            SingleComposer.GetDynamicText("modmissinghint")?.SetNewText(domainFilter == "*"
                ? Lang.Get("vintagee:missing-modmissinghint-pick")
                : Lang.Get("vintagee:missing-modmissinghint", all.Count(e => !e.Other && e.Domain == domainFilter), domainFilter));
        }

        public static bool ShiftHeld(ICoreClientAPI capi)
        {
            bool[] keys = capi.Input.KeyboardKeyStateRaw;
            if (keys == null) return false;
            int l = (int)GlKeys.LShift, r = (int)GlKeys.RShift;
            return (l < keys.Length && keys[l]) || (r < keys.Length && keys[r]);
        }

        void Toggle(Entry e)
        {
            int from = anchor == null ? -1 : shown.FindIndex(x => x.Key == anchor);
            int to = shown.IndexOf(e);
            if (ShiftHeld(capi) && from >= 0 && to >= 0)
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

        bool OnClearSelection()
        {
            selected.Clear();
            anchor = null;
            Refill();
            return true;
        }

        string[] SelectedKeys => all.Where(e => selected.Contains(e.Key)).Select(e => e.Key).ToArray();

        bool OnSet()
        {
            if (SingleComposer == null) return true;
            string[] keys = SelectedKeys;
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:missing-select")); return true; }
            string text = SingleComposer.GetTextInput("value")?.GetText() ?? "";
            if (!GuiDialogEmcSetValue.TryParse(text, out double v))
            {
                UpdateStatus(Lang.Get("vintagee:missing-badvalue"));
                return true;
            }
            if (onSetValue == null) return true;
            onSetValue(keys, v);
            UpdateStatus(Lang.Get("vintagee:missing-sendingmany", keys.Length, text));
            return true;
        }

        /// <summary>"Show blacklisted" / "Hide missing" / "Show valued items".</summary>
        void OnToggleOther(bool on)
        {
            if (composing) return;
            switch (Kind)
            {
                case EmcListKind.Blacklist: hideMissing = on; break;
                case EmcListKind.NoTransmute: showValued = on; break;
                case EmcListKind.GrayList: hideMissingGray = on; break;
                default: showBlacklisted = on; break;
            }
            page = 0;
            // Recompose after the click (the switch belongs to the current layout): adds or removes the extra button row.
            capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Compose(); }, "vintagee-missing");
        }

        /// <summary>The mixed-in entries' button: unblacklists blacklisted ones, blacklists missing ones, or stops valued ones transmuting.</summary>
        bool OnOtherButton()
        {
            string[] keys = all.Where(e => selected.Contains(e.Key) && e.Other).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(L("vintagee:missing-selectblacklisted", "vintagee:blacklist-selectmissing", "vintagee:notransmute-selectvalued", "vintagee:blacklist-selectmissing")); return true; }
            if (onList == null) return true;
            bool add = Kind != EmcListKind.Missing; // missing window: take blacklisted ones off; the others: put on this window's list
            onList(keys, add);
            UpdateStatus(Kind == EmcListKind.NoTransmute ? Lang.Get("vintagee:notransmute-adding", keys.Length)
                : Kind == EmcListKind.GrayList ? Lang.Get("vintagee:gray-adding", keys.Length)
                : Lang.Get(add ? "vintagee:missing-blacklistingmany" : "vintagee:missing-unblacklistingmany", keys.Length));
            return true;
        }

        /// <summary>The window's own button: blacklist (missing window), unblacklist (blacklist window), allow transmuting (no-transmute window).</summary>
        bool OnListButton()
        {
            string[] keys = all.Where(e => selected.Contains(e.Key) && !e.Other).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:missing-select")); return true; }
            if (onList == null) return true;
            bool add = Kind == EmcListKind.Missing;
            onList(keys, add);
            UpdateStatus(Kind == EmcListKind.NoTransmute ? Lang.Get("vintagee:notransmute-removing", keys.Length)
                : Kind == EmcListKind.GrayList ? Lang.Get("vintagee:gray-removing", keys.Length)
                : Lang.Get(add ? "vintagee:missing-blacklistingmany" : "vintagee:missing-unblacklistingmany", keys.Length));
            return true;
        }

        /// <summary>Missing window: put the selected ones on the gray list (no value until something makes them).</summary>
        bool OnGrayButton()
        {
            string[] keys = all.Where(e => selected.Contains(e.Key) && !e.Other).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:missing-select")); return true; }
            onGray?.Invoke(keys, true);
            UpdateStatus(Lang.Get("vintagee:gray-adding", keys.Length));
            return true;
        }

        /// <summary>Missing window: blacklist only the picked mod's missing blocks/items, by their own codes (its valued things keep their values).</summary>
        bool OnModMissingButton()
        {
            if (domainFilter == "*") { UpdateStatus(Lang.Get("vintagee:missing-modmissinghint-pick")); return true; }
            string[] keys = all.Where(e => !e.Other && e.Domain == domainFilter).Select(e => e.Key).ToArray();
            if (keys.Length == 0 || onList == null) return true;
            onList(keys, true);
            UpdateStatus(Lang.Get("vintagee:missing-blacklistingmany", keys.Length));
            return true;
        }

        bool OnModButton()
        {
            if (domainFilter == "*") { UpdateStatus(Lang.Get("vintagee:missing-modhint-pick")); return true; }
            if (domainFilter == "game" && Kind != EmcListKind.NoTransmute && Kind != EmcListKind.GrayList) { UpdateStatus(Lang.Get("vintagee:missing-nogame")); return true; }
            if (onListMod == null) return true;
            bool add = Kind == EmcListKind.Missing;
            onListMod(domainFilter, add);
            UpdateStatus(Kind == EmcListKind.NoTransmute ? Lang.Get("vintagee:notransmute-removingmod", domainFilter)
                : Kind == EmcListKind.GrayList ? Lang.Get("vintagee:gray-removingmod", domainFilter)
                : Lang.Get(add ? "vintagee:missing-blacklistingmod" : "vintagee:missing-unblacklistingmod", domainFilter));
            return true;
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list == null) return;
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }

        bool OnPrev()
        {
            page--;
            ResetScroll();
            Refill();
            return true;
        }

        bool OnNext()
        {
            page++;
            ResetScroll();
            Refill();
            return true;
        }

        void ResetScroll()
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list != null) list.Bounds.fixedY = 0;
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            page = 0;
            ResetScroll();
            Refill();
        }

        void OnDomain(string code, bool sel)
        {
            domainFilter = code ?? "*";
            page = 0;
            ResetScroll();
            Refill();
        }

        void OnType(string code, bool sel)
        {
            typeFilter = code ?? "*";
            page = 0;
            ResetScroll();
            Refill();
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            foreach (Entry e in shown) sb.Append('"').Append(e.IsBlock ? "block:" : "item:").Append(e.Code).Append("\": 0,\n");
            try { capi.Forms.SetClipboardText(sb.ToString()); }
            catch (Exception ex) { capi.Logger.Warning("Vintage-E: clipboard not available: {0}", ex.Message); return true; }
            capi.ShowChatMessage(Lang.Get("vintagee:missing-copied", shown.Count));
            return true;
        }

        void OpenHandbook(ItemStack stack)
        {
            // Same page code the survival mod's handbook uses for item stacks.
            string pageCode = stack.Class.Name() + "-" + stack.Collectible.Code.ToShortString();
            if (capi.LinkProtocols != null && capi.LinkProtocols.TryGetValue("handbook", out Action<LinkTextComponent> open))
            {
                try { open?.Invoke(new LinkTextComponent("handbook://" + pageCode)); }
                catch (Exception ex) { capi.Logger.Warning("Vintage-E: could not open handbook page {0}: {1}", pageCode, ex.Message); }
            }
        }
    }
}
