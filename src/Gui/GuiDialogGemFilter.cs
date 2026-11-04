using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The Gem of Eternal Density's filter window ("Filter..." in its tool modes): a mode button and a row of filter
    /// slots, like the tablet's magnet. Click a slot while holding an item to list it (the item stays on the cursor);
    /// with an empty hand to clear it. The gem's data is read every quarter second, so the window follows the server.
    /// </summary>
    public class GuiDialogGemFilter : GuiDialog
    {
        readonly VintageEModSystem mod;
        readonly DisplayInventory filterInv;
        long tickListener;
        string shown;
        bool composedOnly;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;

        public GuiDialogGemFilter(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
            filterInv = new DisplayInventory(capi, "gemfilter-" + Guid.NewGuid().ToString("N").Substring(0, 8), TabletMagnet.FilterSlots);
            Compose();
        }

        ItemStack Gem => GemFilter.Find(capi.World.Player)?.Itemstack;

        void Compose()
        {
            ItemStack gem = Gem;
            composedOnly = GemFilter.Only(gem);
            double slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double width = TabletMagnet.FilterSlots * slot;
            double top = GuiStyle.TitleBarHeight + 4;
            CairoFont detail = CairoFont.WhiteDetailText();
            string help = Lang.Get("vintagee:density-filter-help");
            double helpHeight = GuiLayout.TextHeight(capi, detail, help, width);

            ElementBounds modeBounds = ElementBounds.Fixed(0, top, 220, 26);
            ElementBounds labelBounds = ElementBounds.Fixed(0, top + 34, width, 22);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, top + 58, TabletMagnet.FilterSlots, 1);
            ElementBounds helpBounds = ElementBounds.Fixed(0, top + 58 + slot + 8, width, helpHeight + 4);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(modeBounds, labelBounds, gridBounds, helpBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string title = gem?.GetName() ?? Lang.Get("vintagee:item-trinket-gemofeternaldensity");
            ClearComposers();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-gemfilter", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddSmallButton(Lang.Get(composedOnly ? "vintagee:density-mode-only" : "vintagee:density-mode-except"), OnMode, modeBounds, EnumButtonStyle.Small, "mode")
                    .AddDynamicText("", detail, labelBounds, "label")
                    .AddItemSlotGrid(filterInv, OnSlotPacket, TabletMagnet.FilterSlots, gridBounds, "grid")
                    .AddStaticText(help, detail, helpBounds)
                .EndChildElements()
                .Compose();
            shown = null;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            tickListener = capi.Event.RegisterGameTickListener(_ => Refresh(), 250);
            Refresh();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            tickListener = 0;
        }

        void Refresh()
        {
            ItemStack gem = Gem;
            if (gem == null) { TryClose(); return; }
            string[] filter = TabletMagnet.GetFilter(gem);
            bool only = GemFilter.Only(gem);
            string signature = only + "|" + string.Join(",", filter);
            if (signature == shown) return;
            shown = signature;
            if (only != composedOnly)
            {
                // The button's label is drawn when composing: draw the window again a frame later.
                capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) { Compose(); Refresh(); } }, "vintagee-gemfilter-recompose");
                return;
            }
            for (int i = 0; i < TabletMagnet.FilterSlots; i++)
            {
                CollectibleObject c = filter[i] == null ? null : PlayerEmc.Resolve(capi.World, filter[i]);
                ItemStack icon = null;
                if (c != null) { try { icon = new ItemStack(c); } catch { /* no icon */ } }
                filterInv[i].Itemstack = icon;
            }
            int listed = filter.Count(f => f != null);
            SingleComposer?.GetDynamicText("label")?.SetNewText(Lang.Get(only ? "vintagee:density-filter-only" : listed == 0 ? "vintagee:density-filter-none" : "vintagee:density-filter-except", listed));
        }

        bool OnMode()
        {
            mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.GemMode });
            return true;
        }

        void OnSlotPacket(object packet)
        {
            if (!(packet is DisplaySlotClick click) || click.Button == EnumMouseButton.Wheel || click.Inventory != filterInv) return;
            mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.GemFilter, Mode = click.SlotId });
        }
    }
}
