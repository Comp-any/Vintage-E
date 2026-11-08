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
    public class GuiDialogTransmutation : GuiDialog
    {
        const int Cols = 9;
        const int Rows = 6;
        const int PerPage = Cols * Rows;

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
        readonly string[] slotKeys = new string[PerPage];
        string search = "";
        int page;
        double shownEmc = -1;
        string[] shownKnown;
        int shownTableVersion = -1;
        string shownMagnet;
        /// <summary>The filter mode the "only these / all except these" button was drawn with.</summary>
        bool composedExclude;
        long tickListener;

        bool IsTablet => tablePos == null;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogTransmutation(ICoreClientAPI capi, VintageEModSystem mod, BlockPos tablePos) : base(capi)
        {
            this.mod = mod;
            this.tablePos = tablePos?.Copy();
            string uid = capi.World.Player.PlayerUID;
            burnInv = new DisplayInventory(capi, "burn-" + uid, 1);
            learnInv = new DisplayInventory(capi, "learn-" + uid, 1);
            chargeInv = new DisplayInventory(capi, "charge-" + uid, 1);
            knownInv = new DisplayInventory(capi, "known-" + uid, PerPage);
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

            // Header: EMC on the left, the learn and burn slots on the right; the hint wraps under the EMC.
            double leftWidth = width - 3 * slot - 80;
            string burnHint = Lang.Get("vintagee:gui-burn-hint");
            double burnHintHeight = GuiLayout.TextHeight(capi, detail, burnHint, leftWidth);
            // EMC on its own line, the known count under it: a big balance (billions) no longer runs into the slots.
            ElementBounds emcBounds = ElementBounds.Fixed(0, top + 4, leftWidth, 28);
            ElementBounds knownBounds = ElementBounds.Fixed(0, top + 30, leftWidth, 22);
            ElementBounds burnHintBounds = ElementBounds.Fixed(0, top + 52, leftWidth, burnHintHeight);
            ElementBounds burnSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - slot, top + 8, 1, 1);
            ElementBounds burnLabelBounds = ElementBounds.Fixed(width - slot - 14, top + 10 + slot, slot + 28, 20);
            ElementBounds learnSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - 2 * slot - 28, top + 8, 1, 1);
            ElementBounds learnLabelBounds = ElementBounds.Fixed(width - 2 * slot - 42, top + 10 + slot, slot + 28, 20);
            ElementBounds chargeSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - 3 * slot - 56, top + 8, 1, 1);
            ElementBounds chargeLabelBounds = ElementBounds.Fixed(width - 3 * slot - 70, top + 10 + slot, slot + 28, 20);

            double row = Math.Max(top + 52 + burnHintHeight, top + 32 + slot) + 8;
            ElementBounds searchBounds = ElementBounds.Fixed(0, row, width - 180, 30);
            ElementBounds sortBounds = ElementBounds.Fixed(width - 170, row, 170, 30);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, row + 40, Cols, Rows);

            double pageRow = row + 40 + Rows * slot + 8;
            ElementBounds prevBounds = ElementBounds.Fixed(width / 2 - 96, pageRow, 36, 30);
            ElementBounds pageBounds = ElementBounds.Fixed(width / 2 - 56, pageRow + 5, 112, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(width / 2 + 60, pageRow, 36, 30);

            string help = Lang.Get("vintagee:gui-create-hint");
            double helpHeight = GuiLayout.TextHeight(capi, detail, help, width);
            ElementBounds helpBounds = ElementBounds.Fixed(0, pageRow + 38, width, helpHeight);

            var children = new List<ElementBounds> { emcBounds, knownBounds, burnHintBounds, burnSlotBounds, burnLabelBounds, learnSlotBounds, learnLabelBounds, chargeSlotBounds, chargeLabelBounds, searchBounds, sortBounds, gridBounds, prevBounds, pageBounds, nextBounds, helpBounds };

            // An Arcane Tablet adds a button for its crafting window (1.19.0).
            bool arcane = IsTablet && EmcCraft.CarriesArcaneTablet(capi.World.Player);
            ElementBounds craftBounds = null;
            double extra = 0;
            if (arcane)
            {
                craftBounds = ElementBounds.Fixed(0, pageRow + 38 + helpHeight + 6, 200, 28);
                children.Add(craftBounds);
                extra = 36;
            }

            // The tablet's magnet: a title row with the filter mode, the magnet slot with its hint, then the filter slots.
            ElementBounds magnetTitleBounds = null, modeBounds = null, magnetSlotBounds = null, magnetHintBounds = null, filterLabelBounds = null, filterBounds = null;
            string magnetHint = Lang.Get("vintagee:magnet-hint", TabletMagnet.Radius);
            if (IsTablet)
            {
                double y = pageRow + 38 + helpHeight + 10 + extra;
                magnetTitleBounds = ElementBounds.Fixed(0, y + 4, width - 230, 24);
                modeBounds = ElementBounds.Fixed(width - 220, y, 220, 28);
                double hintWidth = width - slot - 12;
                double magnetHintHeight = GuiLayout.TextHeight(capi, detail, magnetHint, hintWidth);
                magnetSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y + 34, 1, 1);
                magnetHintBounds = ElementBounds.Fixed(slot + 12, y + 34, hintWidth, magnetHintHeight);
                double fy = y + 34 + Math.Max(slot, magnetHintHeight) + 8;
                // The line above the filter changes with the magnet and mode; leave room for the longest so it never runs into the slots.
                double labelHeight = new[] { "vintagee:magnet-none", "vintagee:magnet-filter-only", "vintagee:magnet-filter-except", "vintagee:magnet-filter-empty" }
                    .Max(k => GuiLayout.TextHeight(capi, detail, Lang.Get(k), width));
                filterLabelBounds = ElementBounds.Fixed(0, fy, width, labelHeight);
                filterBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, fy + labelHeight + 4, TabletMagnet.FilterSlots, 1);
                children.AddRange(new[] { magnetTitleBounds, modeBounds, magnetSlotBounds, magnetHintBounds, filterLabelBounds, filterBounds });
            }

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(children.ToArray());

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            string title = Lang.Get(IsTablet ? "vintagee:gui-tablet-title" : "vintagee:gui-table-title");
            string[] sortNames = { Lang.Get("vintagee:gui-sort-valuedesc"), Lang.Get("vintagee:gui-sort-valueasc"), Lang.Get("vintagee:gui-sort-nameasc"), Lang.Get("vintagee:gui-sort-namedesc") };

            GuiComposer composer = capi.Gui
                .CreateCompo("vintagee-transmutation", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallishText(), emcBounds, "emc")
                    .AddDynamicText("", detail, knownBounds, "known")
                    .AddStaticText(burnHint, detail, burnHintBounds)
                    .AddItemSlotGrid(burnInv, OnSlotPacket, 1, burnSlotBounds, "burnslot")
                    .AddStaticText(Lang.Get("vintagee:gui-burn-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), burnLabelBounds)
                    .AddItemSlotGrid(learnInv, OnSlotPacket, 1, learnSlotBounds, "learnslot")
                    .AddStaticText(Lang.Get("vintagee:gui-learn-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), learnLabelBounds)
                    .AddItemSlotGrid(chargeInv, OnSlotPacket, 1, chargeSlotBounds, "chargeslot")
                    .AddStaticText(Lang.Get("vintagee:gui-charge-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), chargeLabelBounds)
                    .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "search")
                    .AddDropDown(SortValues, sortNames, GameMath.Clamp(sortMode, 0, 3), OnSort, sortBounds, "sort")
                    .AddItemSlotGrid(knownInv, OnSlotPacket, Cols, gridBounds, "knowngrid")
                    .AddSmallButton("<", OnPrevPage, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageBounds, "page")
                    .AddSmallButton(">", OnNextPage, nextBounds)
                    .AddStaticText(help, detail, helpBounds);
            if (arcane) composer.AddSmallButton(Lang.Get("vintagee:emccraft-open"), () => { mod.OpenEmcCraftDialog(toggle: false); return true; }, craftBounds, EnumButtonStyle.Normal, "emccraft");
            if (IsTablet)
            {
                composedExclude = TabletMagnet.Excludes(TabletMagnet.FindTablet(capi.World.Player)?.Itemstack);
                composer
                    .AddStaticText(Lang.Get("vintagee:magnet-title"), CairoFont.WhiteSmallishText(), magnetTitleBounds)
                    .AddSmallButton(Lang.Get(composedExclude ? "vintagee:magnet-mode-except" : "vintagee:magnet-mode-only"), OnMagnetMode, modeBounds, EnumButtonStyle.Small, "magnetmode")
                    .AddItemSlotGrid(magnetInv, OnSlotPacket, 1, magnetSlotBounds, "magnetslot")
                    .AddStaticText(magnetHint, detail, magnetHintBounds)
                    .AddDynamicText("", detail, filterLabelBounds, "filterlabel")
                    .AddItemSlotGrid(filterInv, OnSlotPacket, TabletMagnet.FilterSlots, filterBounds, "filtergrid");
            }
            SingleComposer = composer.EndChildElements().Compose();

            SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vintagee:gui-search"));
            if (search.Length > 0) SingleComposer.GetTextInput("search").SetValue(search, false);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            tickListener = capi.Event.RegisterGameTickListener(OnTick, 250);
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
            if (tablePos != null && player?.Entity != null && player.Entity.Pos.SquareDistanceTo(tablePos.X + 0.5, tablePos.Y + 0.5, tablePos.Z + 0.5) > 10 * 10)
            {
                TryClose(); // walked away from the table
                return;
            }
            // The knowledge array is replaced whenever it changes, so comparing the reference is enough.
            string[] known = PlayerEmc.GetKnown(player);
            if (!ReferenceEquals(known, shownKnown) || shownTableVersion != mod.TableVersion) Refresh(true);
            else if (Math.Abs(EmcNow(player) - shownEmc) > 1e-9) Refresh(false);
            if (IsTablet) RefreshMagnet();
        }

        void OnTableUpdated() => capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Refresh(true); }, "vintagee-refresh");

        /// <summary>
        /// Keys the server said it learned, and the balance it reported, shown at once: the player's attributes
        /// take a moment (sometimes seconds) to sync back, and the window shouldn't wait for them.
        /// </summary>
        readonly HashSet<string> pendingKnown = new HashSet<string>();
        double? pendingEmc;

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

        double EmcNow(IPlayer player)
        {
            double synced = PlayerEmc.GetEmc(player);
            if (pendingEmc == null) return synced;
            // Once the synced balance has caught up, the server's number is no longer needed.
            if (Math.Abs(synced - pendingEmc.Value) < 1e-9) pendingEmc = null;
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
            double emc = EmcNow(player);
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
            for (int i = 0; i < PerPage; i++)
            {
                int idx = page * PerPage + i;
                knownInv[i].Itemstack = idx < entries.Count ? entries[idx].Stack.Clone() : null;
                slotKeys[i] = idx < entries.Count ? entries[idx].Key : null;
            }

            shownEmc = emc;
            // The full number while it fits; past that, a short form (1.23 billion) so the header stays readable.
            CairoFont emcFont = CairoFont.WhiteSmallishText();
            string emcText = Lang.Get("vintagee:gui-emc", EmcFormat.Format(emc));
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            if (emcFont.GetTextExtents(emcText).Width / scale > SingleComposer.GetDynamicText("emc").Bounds.fixedWidth - 4) emcText = Lang.Get("vintagee:gui-emc", EmcFormat.Short(emc));
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
            ItemStack tablet = TabletMagnet.FindTablet(capi.World.Player)?.Itemstack;
            ItemStack magnet = TabletMagnet.GetMagnet(tablet, capi.World);
            string[] filter = TabletMagnet.GetFilter(tablet);
            bool exclude = TabletMagnet.Excludes(tablet);
            string signature = (magnet?.Collectible.Code.ToString() ?? "-") + "|" + exclude + "|" + string.Join(",", filter);
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
            if (exclude != composedExclude)
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

        bool OnMagnetMode()
        {
            mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetMode });
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
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetSwap });
                return;
            }

            if (click.Inventory == filterInv)
            {
                // An item on the cursor goes in the filter (it stays on the cursor); an empty cursor clears the slot.
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.MagnetFilter, Mode = click.SlotId });
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
