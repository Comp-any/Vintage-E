using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// The matter furnaces' window: the game's own firepit window, unchanged, with a mode button in its bottom right
    /// (normal / heating, see <see cref="BEMatterFurnace.HeatingMode"/>). Only matter furnaces open this; the game's
    /// firepits keep the game's window. The button is a second composer laid over the firepit window's empty bottom
    /// right corner (right of the fuel slot, under the output slot), placed again whenever the firepit rebuilds its
    /// window (a cooking pot going in or out changes its height).
    /// </summary>
    public class GuiDialogMatterFurnace : GuiDialogBlockEntityFirepit
    {
        const string ButtonComposer = "vintageefurnacemode";
        const double ButtonWidth = 64, ButtonHeight = 22;

        readonly BEMatterFurnace furnace;

        public GuiDialogMatterFurnace(string title, InventoryBase inventory, BlockPos pos, SyncedTreeAttribute tree, ICoreClientAPI capi, BEMatterFurnace furnace)
            : base(title, inventory, pos, tree, capi)
        {
            this.furnace = furnace;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeModeButton();
            // The firepit rebuilds its window (on the main thread, queued) whenever a slot changes; queue the button
            // after it, so it's placed on the rebuilt window.
            Inventory.SlotModified += OnSlotModified;
        }

        public override void OnGuiClosed()
        {
            Inventory.SlotModified -= OnSlotModified;
            base.OnGuiClosed();
        }

        void OnSlotModified(int slot) => capi.Event.EnqueueMainThreadTask(ComposeModeButton, "vintageefurnacemodebtn");

        /// <summary>Called when the furnace's mode changes (the server's answer to the button).</summary>
        public void UpdateModeButton()
        {
            if (IsOpened()) ComposeModeButton();
        }

        void ComposeModeButton()
        {
            GuiComposer main = SingleComposer;
            if (main?.Bounds == null) return;
            main.Bounds.CalcWorldBounds();
            double scale = RuntimeEnv.GUIScale;
            double pad = GuiStyle.ElementToDialogPadding;
            // The firepit window's outer box, in unscaled units, and the button just inside its bottom right corner.
            double left = main.Bounds.absX / scale, top = main.Bounds.absY / scale;
            double right = left + main.Bounds.OuterWidth / scale, bottom = top + main.Bounds.OuterHeight / scale;
            double x = right - pad - ButtonWidth, y = bottom - pad - ButtonHeight - 4;

            bool heating = furnace?.HeatingMode == true;
            string label = Lang.Get(heating ? "vintagee:furnace-mode-heating" : "vintagee:furnace-mode-normal");
            ElementBounds area = ElementBounds.Fixed(EnumDialogArea.None, x, y, ButtonWidth, ButtonHeight);
            ElementBounds button = ElementBounds.Fixed(0, 0, ButtonWidth, ButtonHeight);
            area.WithChildren(button);

            Composers[ButtonComposer]?.Dispose();
            Composers[ButtonComposer] = capi.Gui.CreateCompo(ButtonComposer + BlockEntityPosition, area)
                .BeginChildElements(area)
                    .AddSmallButton(label, OnModeClicked, button, EnumButtonStyle.Small, "modebutton")
                    .AddHoverText(Lang.Get(heating ? "vintagee:furnace-mode-heating-tip" : "vintagee:furnace-mode-normal-tip"),
                        CairoFont.WhiteSmallText(), 260, button.FlatCopy(), "modetip")
                .EndChildElements()
                .Compose();
        }

        bool OnModeClicked()
        {
            furnace?.RequestToggleMode();
            return true;
        }
    }
}
