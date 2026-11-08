using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace VintageE
{
    /// <summary>
    /// The Arcane Tablet's crafting window (1.19.0): the player's Emc Craft grid (a game crafting grid of its own),
    /// its output, and the EMC balance. Opening and closing it opens and closes the grid on both sides; closing
    /// empties it (see <see cref="EmcCraft"/>).
    /// </summary>
    public class GuiDialogEmcCraft : GuiDialog
    {
        readonly VintageEModSystem mod;
        readonly InventoryCraftingGrid grid;
        long tickListener;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => false;

        public GuiDialogEmcCraft(ICoreClientAPI capi, VintageEModSystem mod) : base(capi)
        {
            this.mod = mod;
            grid = mod.EmcCrafting.GridOf(capi.World.Player);
            Compose();
        }

        void Compose()
        {
            double slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double top = GuiStyle.TitleBarHeight;
            double gridWidth = EmcCraft.GridSize * slot;
            double width = gridWidth + 2 * slot + 40;
            CairoFont detail = CairoFont.WhiteDetailText();
            string hint = Lang.Get("vintagee:emccraft-hint");
            double hintHeight = GuiLayout.TextHeight(capi, detail, hint, width);

            ElementBounds emcBounds = ElementBounds.Fixed(0, top + 4, width, 24);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, top + 34, EmcCraft.GridSize, EmcCraft.GridSize);
            ElementBounds arrowBounds = ElementBounds.Fixed(gridWidth + 6, top + 34 + slot, slot, slot);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, gridWidth + slot + 20, top + 34 + slot, 1, 1);
            ElementBounds hintBounds = ElementBounds.Fixed(0, top + 34 + EmcCraft.GridSize * slot + 10, width, hintHeight);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(emcBounds, gridBounds, arrowBounds, outputBounds, hintBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            int[] cells = new int[EmcCraft.GridSize * EmcCraft.GridSize];
            for (int i = 0; i < cells.Length; i++) cells[i] = i;

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-emccraft", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:emccraft-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallishText(), emcBounds, "emc")
                    .AddItemSlotGrid(grid, SendInvPacket, EmcCraft.GridSize, cells, gridBounds, "grid")
                    .AddStaticText("→", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), arrowBounds)
                    .AddItemSlotGrid(grid, SendInvPacket, 1, new[] { cells.Length }, outputBounds, "output")
                    .AddStaticText(hint, detail, hintBounds)
                .EndChildElements()
                .Compose();
            UpdateEmc();
        }

        void SendInvPacket(object packet) => capi.Network.SendPacketClient(packet);

        void UpdateEmc()
        {
            SingleComposer?.GetDynamicText("emc")?.SetNewText(Lang.Get("vintagee:emccraft-emc", EmcFormat.Format(PlayerEmc.GetEmc(capi.World.Player))));
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            IPlayer player = capi.World.Player;
            if (!player.InventoryManager.HasInventory(grid)) player.InventoryManager.OpenInventory(grid);
            mod.SendEmcCraft(new EmcCraftPacket { Action = EmcCraftAction.Open });
            tickListener = capi.Event.RegisterGameTickListener(_ => UpdateEmc(), 250);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) { capi.Event.UnregisterGameTickListener(tickListener); tickListener = 0; }
            IPlayer player = capi.World.Player;
            // The server empties the grid as it closes; clear this side's copy so a reopened window starts empty.
            for (int i = 0; i < grid.Count; i++) if (grid[i] != null) grid[i].Itemstack = null;
            if (player.InventoryManager.HasInventory(grid)) player.InventoryManager.CloseInventory(grid);
            mod.SendEmcCraft(new EmcCraftPacket { Action = EmcCraftAction.Close });
        }
    }
}
