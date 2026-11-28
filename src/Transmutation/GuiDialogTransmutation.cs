using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// The transmutation GUI shared by the table and the tablet.
    /// Top: personal EMC and the burn slot. Then a searchable, sortable, paged grid of everything you have learned.
    /// The tablet also has its magnet: a slot for a Black Hole Band or Void Ring and a row of filter slots.
    /// All actions are requests to the server; nothing changes locally.
    /// <para>Known entries are resolved once and kept (names, stacks, values), so opening the window and typing in the
    /// search box only filter and sort; a value update from the server re-reads the values.</para>
    /// </summary>
    /// <summary>
    /// How much of the table and tablet window is shown, so that it fits on the screen (1.26.3). With everything in it
    /// the tablet's window is about 1,030 GUI pixels tall: taller than a 1440-pixel screen at a GUI scale of 1.4, and
    /// than any 1080-pixel one above 1.0. Two things give: the long explanations are folded away behind the Help button
    /// until the player asks for them, and where the window still doesn't fit the grid of known items loses rows
    /// (there are pages anyway).
    ///
    /// The explanations are hidden whenever the window opens and shown only once Help is clicked, until it closes
    /// (1.26.4). 1.26.3 showed them by itself whenever the window fitted with them, which made them come and go with
    /// the window's height: putting a magnet in (which adds the filter's row) hid them.
    /// </summary>
    public static class TabletLayout
    {
        public const int MaxRows = 6, MinRows = 3;

        /// <param name="help">Whether the long explanations are shown.</param>
        /// <param name="screenHeight">The screen's height in GUI pixels.</param>
        /// <param name="height">The window's height in GUI pixels with the explanations shown or not, and that many rows.</param>
        /// <returns>The rows of known items to show.</returns>
        /// <summary>Whether a tablet's window is the Arcane Tablet's: its own title and the Emc Craft button.</summary>
        public static bool Arcane(ItemStack tablet) => tablet?.Collectible is ItemArcaneTablet;

        public static int Rows(bool help, double screenHeight, System.Func<bool, int, double> height)
        {
            int rows = MaxRows;
            while (rows > MinRows && height(help, rows) > screenHeight) rows--;
            return rows;
        }
    }

    public class GuiDialogTransmutation : GuiDialog
    {
        const int Cols = 9;
        const int MaxPerPage = Cols * TabletLayout.MaxRows;
        /// <summary>The rows of known items in the window as it is drawn now, and whether its long explanations are shown.</summary>
        int rows = TabletLayout.MaxRows;
        /// <summary>Hidden each time the window opens (a new window is made each time); the Help button shows them until it closes.</summary>
        bool helpShown;
        int PerPage => Cols * rows;

        readonly VintageEModSystem mod;
        readonly BlockPos tablePos;
        readonly DisplayInventory burnInv;
        /// <summary>Drop an item here to learn it without burning it.</summary>
        readonly DisplayInventory learnInv;
        readonly DisplayInventory chargeInv;
        readonly DisplayInventory knownInv;
        readonly DisplayInventory magnetInv;
        readonly DisplayInventory filterInv;

        sealed class Entry
        {
            public string Key;
            /// <summary>Exact versions of something switched to "item only" show as the plain item, once.</summary>
            public string ShownKey;
            public ItemStack Stack;
            public double Value;
            public string Name;
            /// <summary>Lower-case name and code, for the search box.</summary>
            public string Search;
            public int TableVersion;
        }

        /// <summary>Resolved knowledge entries, kept between openings (cleared when the world is left).</summary>
        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        /// <summary>Drops the entries (they hold a world's item stacks; called when a world ends).</summary>
        public static void ForgetWorld() { cache.Clear(); }
        static IWorldAccessor cacheWorld;

        /// <summary>0 = most EMC first, 1 = least EMC first, 2 = name A-Z, 3 = name Z-A. Remembered while the game runs.</summary>
        static int sortMode;
        static readonly string[] SortValues = { "0", "1", "2", "3" };

        List<Entry> entries = new List<Entry>();
        /// <summary>The knowledge key behind each grid slot on the current page.</summary>
        readonly string[] slotKeys = new string[MaxPerPage];
        string search = "";
        int page;
        decimal shownEmc = -1m;
        string[] shownKnown;
        int shownTableVersion = -1;
        string shownMagnet;
        /// <summary>The filter mode the "only these / all except these" button was drawn with.</summary>
        bool composedExclude;
        /// <summary>Whether the window was drawn with a magnet in the tablet: the filter is only shown with one (1.26.3).</summary>
        bool composedMagnet;
        /// <summary>Whether the window was drawn with "Burn what I mine" ticked (1.29.3).</summary>
        bool composedMine;
        long tickListener;

        bool IsTablet => tablePos == null;

        public override string ToggleKeyCombinationCode => null;

        /// <summary>
        /// The tablet this window was opened from (1.26.5): its inventory's ID and its slot's number. The window stays
        /// with it until it closes, wherever the hotbar's selection goes meanwhile.
        /// </summary>
        string openedInv;
        int openedSlot = -1;
        bool composedArcane;

        /// <summary>The tablet the window is for: the one it was opened from while that slot holds a tablet, otherwise the one in the hand or the first carried.</summary>
        ItemStack TabletNow() => IsTablet ? TabletMagnet.FindTablet(capi.World.Player, openedInv, openedSlot)?.Itemstack : null;

        /// <param name="tabletSlot">The tablet that was right-clicked; without it (the key), the one in the hand or the first carried.</param>
        public GuiDialogTransmutation(ICoreClientAPI capi, VintageEModSystem mod, BlockPos tablePos, ItemSlot tabletSlot = null) : base(capi)
        {
            this.mod = mod;
            this.tablePos = tablePos?.Copy();
            if (tablePos == null)
            {
                ItemSlot opened = tabletSlot?.Itemstack?.Collectible is ItemTransmutationTablet ? tabletSlot : TabletMagnet.FindTablet(capi.World.Player);
                TabletMagnet.Locate(opened, out openedInv, out openedSlot);
            }
            string uid = capi.World.Player.PlayerUID;
            burnInv = new DisplayInventory(capi, "burn-" + uid, 1);
            learnInv = new DisplayInventory(capi, "learn-" + uid, 1);
            chargeInv = new DisplayInventory(capi, "charge-" + uid, 1);
            knownInv = new DisplayInventory(capi, "known-" + uid, MaxPerPage);
            magnetInv = new DisplayInventory(capi, "magnet-" + uid, 1);
            filterInv = new DisplayInventory(capi, "magnetfilter-" + uid, TabletMagnet.FilterSlots);
            if (cacheWorld != capi.World) { cache.Clear(); cacheWorld = capi.World; }
            Compose();
            mod.OnEmcTableUpdated += OnTableUpdated;
        }

        void Compose()
        {
            double slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double width = Cols * slot;
            double top = GuiStyle.TitleBarHeight;
            CairoFont detail = CairoFont.WhiteDetailText();
            ItemStack tabletNow = TabletNow();
            // An Arcane Tablet adds a button for its crafting window (1.19.0), and has its own name in the title (1.26.3).
            // It goes by the tablet that was opened (1.26.5): until then an Arcane Tablet anywhere in the inventory
            // turned a plain Transmutation Tablet's window into the Arcane one.
            bool arcane = TabletLayout.Arcane(tabletNow);
            composedArcane = arcane;
            // The filter (its mode button and its slots) is for a magnet: without one in the tablet it isn't shown.
            bool hasMagnet = IsTablet && TabletMagnet.GetMagnet(tabletNow, capi.World) != null;
            // "Burn what I mine" (1.29.3) uses the same filter, so the filter shows for it too.
            bool mineOn = IsTablet && TabletMining.On(tabletNow);
            bool showFilter = hasMagnet || mineOn;
            composedMine = mineOn;

            // The three long explanations. They are folded away behind the Help button until the player clicks it.
            double leftWidth = width - 3 * slot - 80;
            double hintWidth = width - slot - 12;
            string burnHint = Lang.Get("vintagee:gui-burn-hint");
            string help = Lang.Get("vintagee:gui-create-hint");
            string magnetHint = Lang.Get("vintagee:magnet-hint", TabletMagnet.Radius);
            double burnHintHeight = GuiLayout.TextHeight(capi, detail, burnHint, leftWidth);
            double helpHeight = GuiLayout.TextHeight(capi, detail, help, width);
            double magnetHintHeight = GuiLayout.TextHeight(capi, detail, magnetHint, hintWidth);
            // The line by the filter changes with the magnet and mode; leave room for the longest so it never runs into the slots.
            string[] filterStates = { "vintagee:magnet-none", "vintagee:magnet-filter-only", "vintagee:magnet-filter-except", "vintagee:magnet-filter-empty" };
            double labelHeightWide = filterStates.Max(k => GuiLayout.TextHeight(capi, detail, Lang.Get(k), width));
            double labelHeightNarrow = filterStates.Max(k => GuiLayout.TextHeight(capi, detail, Lang.Get(k), hintWidth));

            ElementBounds emcBounds = null, knownBounds = null, helpButtonBounds = null, burnHintBounds = null, burnSlotBounds = null, burnLabelBounds = null,
                learnSlotBounds = null, learnLabelBounds = null, chargeSlotBounds = null, chargeLabelBounds = null, searchBounds = null, sortBounds = null, gridBounds = null,
                prevBounds = null, pageBounds = null, nextBounds = null, helpBounds = null, craftBounds = null,
                magnetTitleBounds = null, modeBounds = null, magnetSlotBounds = null, magnetHintBounds = null, filterLabelBounds = null, filterBounds = null,
                mineBounds = null;

            // Lays the window out with the explanations shown or not and that many rows of known items; returns its height.
            double Layout(bool showHelp, int gridRows)
            {
                // Header: EMC on the left with the known count and the Help button under it, the three slots on the right;
                // the hint wraps under the button. EMC has its own line, so a big balance (billions) doesn't run into the slots.
                emcBounds = ElementBounds.Fixed(0, top + 4, leftWidth, 28);
                knownBounds = ElementBounds.Fixed(0, top + 30, leftWidth, 22);
                helpButtonBounds = ElementBounds.Fixed(0, top + 56, 110, 24);
                burnHintBounds = ElementBounds.Fixed(0, top + 86, leftWidth, burnHintHeight);
                burnSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - slot, top + 8, 1, 1);
                burnLabelBounds = ElementBounds.Fixed(width - slot - 14, top + 10 + slot, slot + 28, 20);
                learnSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - 2 * slot - 28, top + 8, 1, 1);
                learnLabelBounds = ElementBounds.Fixed(width - 2 * slot - 42, top + 10 + slot, slot + 28, 20);
                chargeSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - 3 * slot - 56, top + 8, 1, 1);
                chargeLabelBounds = ElementBounds.Fixed(width - 3 * slot - 70, top + 10 + slot, slot + 28, 20);

                double row = Math.Max(showHelp ? top + 86 + burnHintHeight : top + 80, top + 32 + slot) + 8;
                searchBounds = ElementBounds.Fixed(0, row, width - 180, 30);
                sortBounds = ElementBounds.Fixed(width - 170, row, 170, 30);
                gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, row + 40, Cols, gridRows);

                double pageRow = row + 40 + gridRows * slot + 8;
                prevBounds = ElementBounds.Fixed(width / 2 - 96, pageRow, 36, 30);
                pageBounds = ElementBounds.Fixed(width / 2 - 56, pageRow + 5, 112, 24);
                nextBounds = ElementBounds.Fixed(width / 2 + 60, pageRow, 36, 30);
                helpBounds = ElementBounds.Fixed(0, pageRow + 38, width, helpHeight);
                double bottom = pageRow + 38 + (showHelp ? helpHeight : 0);

                if (arcane)
                {
                    craftBounds = ElementBounds.Fixed(0, bottom + 6, 200, 28);
                    bottom += 36;
                }

                // The tablet's magnet: a title row with the filter mode, the magnet slot, then the filter slots. With the
                // explanations shown, the magnet's is beside its slot and the filter's line under it; without, that line is beside the slot.
                // Without a magnet there is no mode button and no row of filter slots, only the slot and its line.
                if (IsTablet)
                {
                    double y = bottom + 10;
                    magnetTitleBounds = ElementBounds.Fixed(0, y + 4, width - 230, 24);
                    modeBounds = ElementBounds.Fixed(width - 220, y, 220, 28);
                    magnetSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y + 34, 1, 1);
                    magnetHintBounds = ElementBounds.Fixed(slot + 12, y + 34, hintWidth, magnetHintHeight);
                    double fy;
                    if (showHelp)
                    {
                        fy = y + 34 + Math.Max(slot, magnetHintHeight) + 8;
                        filterLabelBounds = ElementBounds.Fixed(0, fy, width, labelHeightWide);
                        fy += labelHeightWide + 4;
                    }
                    else
                    {
                        filterLabelBounds = ElementBounds.Fixed(slot + 12, y + 34 + Math.Max(0, (slot - labelHeightNarrow) / 2), hintWidth, labelHeightNarrow);
                        fy = y + 34 + Math.Max(slot, labelHeightNarrow) + 6;
                    }
                    filterBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, fy, TabletMagnet.FilterSlots, 1);
                    bottom = showFilter ? fy + slot : fy - 4;
                    mineBounds = ElementBounds.Fixed(0, bottom + 8, 320, 28);
                    bottom += 40;
                }
                // The frame around it, and a little air above and below so it clears the screen's edges.
                return bottom + 2 * GuiStyle.ElementToDialogPadding + 24;
            }

            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            double screen = capi.Render.FrameHeight / scale;
            rows = TabletLayout.Rows(helpShown, screen, Layout);
            Layout(helpShown, rows);

            var children = new List<ElementBounds> { emcBounds, knownBounds, helpButtonBounds, burnSlotBounds, burnLabelBounds, learnSlotBounds, learnLabelBounds, chargeSlotBounds, chargeLabelBounds, searchBounds, sortBounds, gridBounds, prevBounds, pageBounds, nextBounds };
            if (helpShown) { children.Add(burnHintBounds); children.Add(helpBounds); }
            if (arcane) children.Add(craftBounds);
            if (IsTablet)
            {
                children.AddRange(new[] { magnetTitleBounds, magnetSlotBounds, filterLabelBounds });
                if (showFilter) { children.Add(modeBounds); children.Add(filterBounds); }
                children.Add(mineBounds);
                if (helpShown) children.Add(magnetHintBounds);
            }

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(children.ToArray());

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            string title = Lang.Get(!IsTablet ? "vintagee:gui-table-title" : arcane ? "vintagee:gui-arcanetablet-title" : "vintagee:gui-tablet-title");
            string[] sortNames = { Lang.Get("vintagee:gui-sort-valuedesc"), Lang.Get("vintagee:gui-sort-valueasc"), Lang.Get("vintagee:gui-sort-nameasc"), Lang.Get("vintagee:gui-sort-namedesc") };
            int[] shownSlots = Enumerable.Range(0, PerPage).ToArray();

            GuiComposer composer = capi.Gui
                .CreateCompo("vintagee-transmutation", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallishText(), emcBounds, "emc")
                    .AddDynamicText("", detail, knownBounds, "known")
                    .AddSmallButton(Lang.Get(helpShown ? "vintagee:gui-help-hide" : "vintagee:gui-help-show"), OnHelpToggle, helpButtonBounds, EnumButtonStyle.Small, "helpbutton")
                    .AddItemSlotGrid(burnInv, OnSlotPacket, 1, burnSlotBounds, "burnslot")
                    .AddStaticText(Lang.Get("vintagee:gui-burn-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), burnLabelBounds)
                    .AddItemSlotGrid(learnInv, OnSlotPacket, 1, learnSlotBounds, "learnslot")
                    .AddStaticText(Lang.Get("vintagee:gui-learn-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), learnLabelBounds)
                    .AddItemSlotGrid(chargeInv, OnSlotPacket, 1, chargeSlotBounds, "chargeslot")
                    .AddStaticText(Lang.Get("vintagee:gui-charge-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), chargeLabelBounds)
                    .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "search")
                    .AddDropDown(SortValues, sortNames, GameMath.Clamp(sortMode, 0, 3), OnSort, sortBounds, "sort")
                    .AddItemSlotGrid(knownInv, OnSlotPacket, Cols, shownSlots, gridBounds, "knowngrid")
                    .AddSmallButton("<", OnPrevPage, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageBounds, "page")
                    .AddSmallButton(">", OnNextPage, nextBounds);
            if (helpShown)
            {
                composer
                    .AddStaticText(burnHint, detail, burnHintBounds)
                    .AddStaticText(help, detail, helpBounds);
            }
            else
            {
                // Folded away: the same words when the pointer is over the Help button.
                composer.AddHoverText(burnHint + "\n\n" + help + (IsTablet ? "\n\n" + magnetHint : ""), CairoFont.WhiteSmallText(), 420, helpButtonBounds.FlatCopy(), "helptip");
            }
            if (arcane) composer.AddSmallButton(Lang.Get("vintagee:emccraft-open"), () => { mod.OpenEmcCraftDialog(toggle: false); return true; }, craftBounds, EnumButtonStyle.Normal, "emccraft");
            if (IsTablet)
            {
                composedExclude = TabletMagnet.Excludes(tabletNow);
                composedMagnet = hasMagnet;
                composer.AddStaticText(Lang.Get("vintagee:magnet-title"), CairoFont.WhiteSmallishText(), magnetTitleBounds);
                if (showFilter) composer.AddSmallButton(Lang.Get(composedExclude ? "vintagee:magnet-mode-except" : "vintagee:magnet-mode-only"), OnMagnetMode, modeBounds, EnumButtonStyle.Small, "magnetmode");
                composer.AddItemSlotGrid(magnetInv, OnSlotPacket, 1, magnetSlotBounds, "magnetslot");
                if (helpShown) composer.AddStaticText(magnetHint, detail, magnetHintBounds);
                composer.AddDynamicText("", detail, filterLabelBounds, "filterlabel");
                if (showFilter) composer.AddItemSlotGrid(filterInv, OnSlotPacket, TabletMagnet.FilterSlots, filterBounds, "filtergrid");
                composer.AddSmallButton(Lang.Get(mineOn ? "vintagee:mine-on" : "vintagee:mine-off"), OnMineToggle, mineBounds, EnumButtonStyle.Small, "minebutton");
                composer.AddHoverText(Lang.Get("vintagee:mine-hint"), CairoFont.WhiteSmallText(), 400, mineBounds.FlatCopy(), "minetip");
            }
            SingleComposer = composer.EndChildElements().Compose();

            SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vintagee:gui-search"));
            if (search.Length > 0) SingleComposer.GetTextInput("search").SetValue(search, false);
        }

        /// <summary>The Help button: shows the long explanations or folds them away again, for as long as this window is open.</summary>
        bool OnHelpToggle()
        {
            helpShown = !helpShown;
            // Drawn again a frame later: this runs from the button's own click.
            capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) { Compose(); Refresh(true); } }, "vintagee-table-recompose");
            return true;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            tickListener = capi.Event.RegisterGameTickListener(Guard.Tick(capi.Logger, "the tablet window", OnTick), 250);
            Refresh(true);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            tickListener = 0;
        }

        void OnTick(float dt)
        {
            IPlayer player = capi.World.Player;
            // The server's own reach (TableUseRange, sent with the table; a block more, as the two sides' positions differ a
            // little). It was a fixed 10 blocks, so with the default of 8 the window stayed open where the server refused it.
            double reach = (mod.Emc?.TableUseRange ?? 8) + 1;
            if (tablePos != null && player?.Entity != null && player.Entity.Pos.SquareDistanceTo(tablePos.X + 0.5, tablePos.Y + 0.5, tablePos.Z + 0.5) > reach * reach)
            {
                TryClose(); // walked away from the table
                return;
            }
            // The knowledge array is replaced whenever it changes, so comparing the reference is enough.
            string[] known = PlayerEmc.GetKnown(player);
            if (!ReferenceEquals(known, shownKnown) || shownTableVersion != mod.TableVersion) Refresh(true);
            else if (EmcNow(player) != shownEmc) Refresh(false);
            if (IsTablet) RefreshMagnet();
        }

        void OnTableUpdated() => capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Refresh(true); }, "vintagee-refresh");

        /// <summary>
        /// Keys the server said it learned, and the balance it reported, shown at once: the player's attributes
        /// take a moment (sometimes seconds) to sync back, and the window shouldn't wait for them.
        /// </summary>
        readonly HashSet<string> pendingKnown = new HashSet<string>();
        decimal? pendingEmc;

        public void OnServerResult(TransmuteResultPacket packet)
        {
            if (packet == null) return;
            bool rebuild = false;
            if (packet.LearnedKeys != null)
            {
                foreach (string k in packet.LearnedKeys) if (!string.IsNullOrEmpty(k) && pendingKnown.Add(k)) rebuild = true;
            }
            if (packet.HasEmc) pendingEmc = packet.Emc;
            if (rebuild || packet.HasEmc) Refresh(rebuild);
        }

        /// <summary>The player's knowledge plus what the server just confirmed; pending keys drop out once the real list has them.</summary>
        string[] KnownNow(IPlayer player)
        {
            string[] known = PlayerEmc.GetKnown(player);
            if (pendingKnown.Count == 0) return known;
            pendingKnown.ExceptWith(known);
            if (pendingKnown.Count == 0) return known;
            var merged = new List<string>(known);
            merged.AddRange(pendingKnown);
            return merged.ToArray();
        }

        decimal EmcNow(IPlayer player)
        {
            decimal synced = PlayerEmc.GetEmc(player);
            if (pendingEmc == null) return synced;
            // Once the synced balance has caught up, the server's number is no longer needed.
            if (synced == pendingEmc.Value) pendingEmc = null;
            return pendingEmc ?? synced;
        }

        Entry GetEntry(string key)
        {
            // A key that failed to resolve is cached as null (it stays broken); a cached entry is good while the table is the same.
            if (cache.TryGetValue(key, out Entry e)) { if (e == null) return null; if (e.TableVersion == mod.TableVersion) return e; }
            ItemStack stack = TransmutationService.Prototype(mod, capi.World, key);
            if (stack == null) { cache[key] = null; return null; }
            // A clutter block or tapestry with no type is a broken thing (learned before 1.17.5, or by the Tome): never offer it.
            if (StackDataUtil.NeedsData(stack.Collectible, capi.World) && StackDataUtil.KeptData(stack) == null) { cache[key] = null; return null; }
            if (e == null || e.Stack?.Collectible != stack.Collectible)
            {
                string name;
                try { name = stack.GetName(); }
                catch { name = stack.Collectible.Code.ToShortString(); }
                e = new Entry { Key = key, Name = name, Search = (name ?? "").ToLowerInvariant() + "\n" + stack.Collectible.Code.ToString().ToLowerInvariant() };
            }
            e.Stack = stack;
            e.ShownKey = StackDataUtil.KeptData(stack) == null ? PlayerEmc.KeyOf(stack.Collectible) : key;
            e.Value = mod.Emc.GetValue(stack);
            e.TableVersion = mod.TableVersion;
            cache[key] = e;
            return e;
        }

        /// <param name="rebuildList">Re-read knowledge and re-filter; otherwise only the EMC text and page change.</param>
        void Refresh(bool rebuildList)
        {
            if (SingleComposer == null) return;
            IPlayer player = capi.World.Player;
            decimal emc = EmcNow(player);
            string[] known = KnownNow(player);

            if (rebuildList)
            {
                string needle = search.Trim().ToLowerInvariant();
                var seen = new HashSet<string>();
                var list = new List<Entry>(known.Length);
                foreach (string k in known)
                {
                    Entry e = GetEntry(k);
                    if (e == null || e.Value <= 0 || mod.Emc.IsNoTransmute(e.Stack.Collectible)) continue;
                    if (!seen.Add(e.ShownKey)) continue;
                    if (needle.Length > 0 && !e.Search.Contains(needle)) continue;
                    list.Add(e);
                }
                entries = Sort(list);
                shownKnown = PlayerEmc.GetKnown(player); // the synced array: the tick rebuilds when the server's copy changes
                shownTableVersion = mod.TableVersion;
            }

            int pages = Math.Max(1, (entries.Count + PerPage - 1) / PerPage);
            page = GameMath.Clamp(page, 0, pages - 1);
            for (int i = 0; i < MaxPerPage; i++)
            {
                // Slots past the rows on show stay empty (the grid has fewer rows on a small screen).
                int idx = i < PerPage ? page * PerPage + i : int.MaxValue;
                knownInv[i].Itemstack = idx < entries.Count ? entries[idx].Stack.Clone() : null;
                slotKeys[i] = idx < entries.Count ? entries[idx].Key : null;
            }

            shownEmc = emc;
            // The full number while it fits; past that, a short form (1.23 billion) so the header stays readable.
            CairoFont emcFont = CairoFont.WhiteSmallishText();
            string emcText = Lang.Get("vintagee:gui-emc", EmcFormat.Total(emc));
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            if (emcFont.GetTextExtents(emcText).Width / scale > SingleComposer.GetDynamicText("emc").Bounds.fixedWidth - 4) emcText = Lang.Get("vintagee:gui-emc", EmcFormat.ShortTotal(emc));
            SingleComposer.GetDynamicText("emc").SetNewText(emcText);
            SingleComposer.GetDynamicText("known").SetNewText(Lang.Get("vintagee:gui-known", known.Length));
            SingleComposer.GetDynamicText("page").SetNewText((page + 1) + " / " + pages);
            if (IsTablet) RefreshMagnet(true);
        }

        static List<Entry> Sort(List<Entry> list)
        {
            IOrderedEnumerable<Entry> sorted;
            switch (sortMode)
            {
                case 1: sorted = list.OrderBy(e => e.Value); break;
                case 2: sorted = list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase); break;
                case 3: sorted = list.OrderByDescending(e => e.Name, StringComparer.CurrentCultureIgnoreCase); break;
                default: sorted = list.OrderByDescending(e => e.Value); break;
            }
            return sorted
                .ThenBy(e => e.Stack.Collectible.Code.ToString(), StringComparer.Ordinal)
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Shows the tablet's magnet and filter (the tablet's stored data is synced with the inventory).</summary>
        void RefreshMagnet(bool force = false)
        {
            ItemStack tablet = TabletNow();
            ItemStack magnet = TabletMagnet.GetMagnet(tablet, capi.World);
            string[] filter = TabletMagnet.GetFilter(tablet);
            bool exclude = TabletMagnet.Excludes(tablet);
            string signature = (magnet?.Collectible.Code.ToString() ?? "-") + "|" + exclude + "|" + TabletMining.On(tablet) + "|" + string.Join(",", filter);
            if (!force && signature == shownMagnet) return;
            shownMagnet = signature;

            magnetInv[0].Itemstack = magnet?.Clone();
            for (int i = 0; i < TabletMagnet.FilterSlots; i++)
            {
                CollectibleObject c = filter[i] == null ? null : PlayerEmc.Resolve(capi.World, filter[i]);
                ItemStack shown = null;
                if (c != null) { try { shown = new ItemStack(c); } catch { /* no icon */ } }
                filterInv[i].Itemstack = shown;
            }
            if (exclude != composedExclude || (magnet != null) != composedMagnet || TabletLayout.Arcane(tablet) != composedArcane || TabletMining.On(tablet) != composedMine)
            {
                // The button's label is drawn when composing, so draw the window again with the other label, a frame
                // later: this can run from the search box's own text-changed callback.
                capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) { Compose(); Refresh(true); } }, "vintagee-table-recompose");
                return;
            }
            string state = magnet == null ? Lang.Get("vintagee:magnet-none")
                : exclude ? Lang.Get("vintagee:magnet-filter-except") : filter.All(f => f == null) ? Lang.Get("vintagee:magnet-filter-empty") : Lang.Get("vintagee:magnet-filter-only");
            SingleComposer?.GetDynamicText("filterlabel")?.SetNewText(state);
        }

        void OnSearchChanged(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            page = 0;
            Refresh(true);
        }

        void OnSort(string code, bool selected)
        {
            if (!int.TryParse(code, out int mode)) return;
            sortMode = GameMath.Clamp(mode, 0, 3);
            page = 0;
            Refresh(true);
        }

        bool OnPrevPage()
        {
            page--;
            Refresh(false);
            return true;
        }

        bool OnNextPage()
        {
            page++;
            Refresh(false);
            return true;
        }

        bool OnMineToggle()
        {
            mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MineBurn, TabletInv = openedInv, TabletSlot = openedSlot + 1 });
            return true;
        }

        bool OnMagnetMode()
        {
            mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetMode, TabletInv = openedInv, TabletSlot = openedSlot + 1 });
            return true;
        }

        void OnSlotPacket(object packet)
        {
            if (!(packet is DisplaySlotClick click) || click.Button == EnumMouseButton.Wheel) return;
            ItemSlot cursor = capi.World.Player.InventoryManager.MouseItemSlot;

            if (click.Inventory == burnInv)
            {
                if (cursor?.Itemstack == null) return;
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Burn, Mode = click.Button == EnumMouseButton.Right ? 1 : 0 });
                return;
            }

            if (click.Inventory == chargeInv)
            {
                if (cursor?.Itemstack == null) return;
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Charge, Mode = click.Button == EnumMouseButton.Right ? 1 : 0 });
                return;
            }

            if (click.Inventory == learnInv)
            {
                if (cursor?.Itemstack == null) return;
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Learn });
                return;
            }

            if (click.Inventory == magnetInv)
            {
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetSwap, TabletInv = openedInv, TabletSlot = openedSlot + 1 });
                return;
            }

            if (click.Inventory == filterInv)
            {
                // An item on the cursor goes in the filter (it stays on the cursor); an empty cursor clears the slot.
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetFilter, Mode = click.SlotId, TabletInv = openedInv, TabletSlot = openedSlot + 1 });
                return;
            }

            if (click.Inventory == knownInv)
            {
                ItemStack shown = knownInv[click.SlotId]?.Itemstack;
                string key = click.SlotId >= 0 && click.SlotId < PerPage ? slotKeys[click.SlotId] : null;
                if (shown == null || key == null) return;
                if (click.Ctrl)
                {
                    // Ctrl + right-click forgets an entry, after asking (you'd have to burn one again to learn it back).
                    if (click.Button == EnumMouseButton.Right)
                    {
                        string name;
                        try { name = shown.GetName(); } catch { name = shown.Collectible?.Code?.ToShortString() ?? "?"; }
                        new GuiDialogConfirm(capi, Lang.Get("vintagee:gui-forget-confirm", name), ok =>
                        {
                            if (ok) mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Forget, Key = key });
                        }).TryOpen();
                    }
                    return;
                }
                int mode = click.Shift ? 2 : (click.Button == EnumMouseButton.Right ? 1 : 0);
                if (mode != 2 && cursor?.Itemstack != null && cursor.Itemstack.Collectible != shown.Collectible)
                {
                    // Holding something different: ignore, so dragging items across the grid never creates anything by accident.
                    return;
                }
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Create, Key = key, Mode = mode });
            }
        }

        public override void Dispose()
        {
            mod.OnEmcTableUpdated -= OnTableUpdated;
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            base.Dispose();
        }
    }
}
