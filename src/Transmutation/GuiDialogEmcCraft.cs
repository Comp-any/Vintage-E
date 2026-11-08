using System.Collections.Generic;
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
        readonly EmcCraftGrid grid;
        long tickListener;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => false;
        /// <summary>Above the handbook (0.2), which is where Emc Craft is opened from.</summary>
        public override double DrawOrder => 0.21;
        public override double InputOrder => 0.45;

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
            ElementBounds statusBounds = ElementBounds.Fixed(0, top + 34 + EmcCraft.GridSize * slot + 8, width, 2 * 18);
            ElementBounds hintBounds = ElementBounds.Fixed(0, top + 34 + EmcCraft.GridSize * slot + 8 + 2 * 18 + 6, width, hintHeight);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(emcBounds, gridBounds, arrowBounds, outputBounds, statusBounds, hintBounds);
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
                    .AddDynamicText(status ?? "", CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.45, 0.4, 1 }), statusBounds, "status")
                    .AddStaticText(hint, detail, hintBounds)
                .EndChildElements()
                .Compose();
            UpdateEmc();
        }

        void SendInvPacket(object packet) => capi.Network.SendPacketClient(packet);

        string status;
        int slotChangesLogged;
        bool contentsFixLogged;

        void OnSlotModified(int slotId)
        {
            if (slotChangesLogged >= 10) return;
            slotChangesLogged++;
            capi.Logger.Notification("Vintage-E: Emc Craft: slot {0} on this computer now holds {1}", slotId, grid[slotId]?.Itemstack?.ToString() ?? "nothing");
        }

        /// <summary>The server's grid after a fill: shown at once (the game's own slot updates follow on its next tick; see OnEmcCraftContents).</summary>
        public void CheckContents(List<byte[]> stacks)
        {
            if (!IsOpened()) return;
            int fixedSlots = 0;
            for (int i = 0; i < stacks.Count && i < EmcCraft.GridSize * EmcCraft.GridSize; i++)
            {
                ItemStack server = null;
                if (stacks[i] != null && stacks[i].Length > 0)
                {
                    server = new ItemStack(stacks[i]);
                    if (!server.ResolveBlockOrItem(capi.World)) continue;
                }
                ItemStack here = grid[i].Itemstack;
                bool same = server == null ? here == null : here != null && here.StackSize == server.StackSize && here.Equals(capi.World, server, GlobalConstants.IgnoredStackAttributes);
                if (same) continue;
                grid[i].Itemstack = server;
                grid[i].MarkDirty();
                fixedSlots++;
            }
            if (fixedSlots > 0)
            {
                if (!contentsFixLogged)
                {
                    contentsFixLogged = true;
                    IInventory held = capi.World.Player?.InventoryManager?.GetInventory(grid.InventoryID);
                    capi.Logger.Notification("Vintage-E: Emc Craft: showed {0} slot(s) of the server's grid ahead of the game's own update (the game is paused while the handbook is open in single player; the game holds {1})",
                        fixedSlots, held == null ? "no grid" : ReferenceEquals(held, grid) ? "this grid" : "another grid (" + held.GetType().Name + ")");
                }
            }
        }

        /// <summary>For the doctor: the grid this window shows, and whether the game's inventory under its ID is it.</summary>
        public string SyncInfo()
        {
            int items = 0;
            for (int i = 0; i < EmcCraft.GridSize * EmcCraft.GridSize; i++) if (grid[i]?.Itemstack != null) items++;
            IInventory held = capi.World.Player?.InventoryManager?.GetInventory(grid.InventoryID);
            return items + "|" + (held == null ? "-" : ReferenceEquals(held, grid) ? "1" : "0");
        }

        /// <summary>Why the last fill was refused (shown in red under the grid), or "" to clear it.</summary>
        public void ShowStatus(string text)
        {
            status = text;
            SingleComposer?.GetDynamicText("status")?.SetNewText(text ?? "");
        }

        void UpdateEmc()
        {
            SingleComposer?.GetDynamicText("emc")?.SetNewText(Lang.Get("vintagee:emccraft-emc", EmcFormat.Format(PlayerEmc.GetEmc(capi.World.Player))));
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            IPlayer player = capi.World.Player;
            EmcCraft.Attach(player, grid);
            IInventory heldNow = player.InventoryManager.GetInventory(grid.InventoryID);
            capi.Logger.Notification("Vintage-E: Emc Craft: window opened on {0}; the game holds {1}", grid.InventoryID,
                heldNow == null ? "no grid" : ReferenceEquals(heldNow, grid) ? "this grid" : "another grid (" + heldNow.GetType().Name + ")");
            slotChangesLogged = 0;
            grid.SlotModified -= OnSlotModified;
            grid.SlotModified += OnSlotModified;
            mod.SendEmcCraft(new EmcCraftPacket { Action = EmcCraftAction.Open });
            tickListener = capi.Event.RegisterGameTickListener(_ => UpdateEmc(), 250);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) { capi.Event.UnregisterGameTickListener(tickListener); tickListener = 0; }
            grid.SlotModified -= OnSlotModified;
            IPlayer player = capi.World.Player;
            // The server empties the grid as it closes; clear this side's copy so a reopened window starts empty.
            for (int i = 0; i < grid.Count; i++) if (grid[i] != null) grid[i].Itemstack = null;
            if (player.InventoryManager.HasInventory(grid)) player.InventoryManager.CloseInventory(grid);
            mod.SendEmcCraft(new EmcCraftPacket { Action = EmcCraftAction.Close });
        }
    }
}
