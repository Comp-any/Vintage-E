using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    public class GuiDialogCondenser : GuiDialogBlockEntity
    {
        const int Cols = 10;
        readonly BEEnergyCondenser be;
        long tickListener;

        public GuiDialogCondenser(string title, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, BEEnergyCondenser be)
            : base(title, inventory, pos, capi)
        {
            this.be = be;
            if (IsDuplicate) return;
            Compose();
        }

        void Compose()
        {
            double slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            int storage = Inventory.Count - 1;
            int rows = (storage + Cols - 1) / Cols;
            double width = Cols * slot;
            double top = GuiStyle.TitleBarHeight + 4;

            ElementBounds targetBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, top, 1, 1);
            ElementBounds targetLabel = ElementBounds.Fixed(slot + 8, top, width - slot - 8, 20);
            ElementBounds infoBounds = ElementBounds.Fixed(slot + 8, top + 20, width - slot - 8, 42);
            ElementBounds storageBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, top + slot + 14, Cols, rows);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(targetBounds, targetLabel, infoBounds, storageBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            ClearComposers();
            SingleComposer = capi.Gui
                .CreateCompo("vintagee-condenser" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, new[] { BEEnergyCondenser.TargetSlot }, targetBounds, "target")
                    .AddStaticText(Lang.Get("vintagee:condenser-targetlabel"), CairoFont.WhiteDetailText(), targetLabel)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), infoBounds, "info")
                    .AddItemSlotGrid(Inventory, DoSendPacket, Cols, Enumerable.Range(1, storage).ToArray(), storageBounds, "storage")
                .EndChildElements()
                .Compose();
            UpdateInfo();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            tickListener = capi.Event.RegisterGameTickListener(Guard.Tick(capi.Logger, "the condenser window", UpdateInfo), 250);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            tickListener = 0;
        }

        public void UpdateInfo()
        {
            if (SingleComposer == null || be == null) return;
            double target = be.TargetValue;
            string text = target > 0
                ? Lang.Get("vintagee:condenser-progress", EmcFormat.Total(be.Stored), EmcFormat.Format(target), (int)Math.Min(100, (double)be.Stored / target * 100))
                : Lang.Get("vintagee:condenser-notarget", EmcFormat.Total(be.Stored));
            if (be.IncomingPerSecond > 0) text += "\n" + Lang.Get("vintagee:condenser-incoming", EmcFormat.Format(be.IncomingPerSecond));
            SingleComposer.GetDynamicText("info")?.SetNewText(text);
        }
    }
}
