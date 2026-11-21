using System.Collections.Generic;
using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// The Arcane Tablet's crafting window (1.19.0): the player's Emc Craft grid (a game crafting grid of its own),
    /// its output, and the EMC balance. Opening and closing it opens and closes the grid on both sides; closing
    /// empties it (see <see cref="EmcCraft"/>).
    /// </summary>
    /// <summary>
    /// When the Emc Craft window keeps the game running, and when it pauses it again (1.26.2).
    ///
    /// In single player the handbook pauses the game while it is open (its "Pause game" button). A paused game's
    /// built-in server does nothing: it doesn't handle the request to fill the grid, doesn't match the recipe or hand
    /// over what is crafted, and sends no slot to the screen. So Emc Craft clicked in a paused handbook showed an
    /// empty grid until the handbook was closed. A crafting window can't work in a paused game, so while this one is
    /// open under a handbook the game runs, and when the window closes with the handbook still open it is paused again.
    /// Only the handbook's pause is undone: the Esc menu's stays.
    /// </summary>
    public static class EmcCraftPause
    {
        /// <summary>Un-pause now: the game is paused and a handbook is what paused it.</summary>
        public static bool ShouldRun(bool paused, bool handbookOpen) => paused && handbookOpen;

        /// <summary>
        /// Pause again after the window closed: no Emc Craft window is open now, the handbook still is, and the
        /// handbook would have the game paused (single player, not opened to LAN, its Pause game on).
        /// </summary>
        public static bool ShouldPauseAgain(bool windowOpen, bool handbookOpen, bool singlePlayer, bool openedToLan, bool noHandbookPause, bool paused) =>
            !windowOpen && handbookOpen && singlePlayer && !openedToLan && !noHandbookPause && !paused;
    }

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
                    .AddStaticCustomDraw(arrowBounds, DrawArrow)
                    .AddItemSlotGrid(grid, SendInvPacket, 1, new[] { cells.Length }, outputBounds, "output")
                    .AddDynamicText(status ?? "", CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.45, 0.4, 1 }), statusBounds, "status")
                    .AddStaticText(hint, detail, hintBounds)
                .EndChildElements()
                .Compose();
            UpdateEmc();
        }

        void SendInvPacket(object packet) => capi.Network.SendPacketClient(packet);

        /// <summary>
        /// The arrow between the grid and the output, drawn (1.26.2). It was the character "→", which the game's font
        /// doesn't have on every computer: there it showed as an empty box.
        /// </summary>
        static void DrawArrow(Context ctx, ImageSurface surface, ElementBounds bounds)
        {
            double w = bounds.InnerWidth, h = bounds.InnerHeight;
            double y = bounds.drawY + h / 2, length = w * 0.6, x0 = bounds.drawX + (w - length) / 2, x1 = x0 + length, head = h * 0.2;
            double[] color = GuiStyle.DialogDefaultTextColor;
            ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
            ctx.LineWidth = Math.Max(2, h * 0.07);
            ctx.LineCap = LineCap.Round;
            ctx.LineJoin = LineJoin.Round;
            ctx.MoveTo(x0, y);
            ctx.LineTo(x1, y);
            ctx.Stroke();
            ctx.MoveTo(x1 - head, y - head);
            ctx.LineTo(x1, y);
            ctx.LineTo(x1 - head, y + head);
            ctx.Stroke();
        }

        // ---------------------------------------------------------------- a paused game (see EmcCraftPause)

        bool unpauseLogged;

        bool HandbookOpen()
        {
            try { return capi.OpenedGuis.Any(d => d is GuiDialogHandbook h && h.IsOpened()); }
            catch { return false; }
        }

        /// <summary>Un-pauses the game if the handbook has it paused. Called when the window opens, before every fill, and each frame while it is open.</summary>
        public void KeepRunning()
        {
            try
            {
                if (!capi.IsGamePaused || !EmcCraftPause.ShouldRun(true, HandbookOpen())) return;
                capi.PauseGame(false);
                if (unpauseLogged) return;
                unpauseLogged = true;
                capi.Logger.Notification("Vintage-E: Emc Craft: the handbook had the game paused; it runs while the Emc Craft window is open (a paused game can't fill the grid or craft) and is paused again when the window closes");
            }
            catch (Exception e) { Guard.Report(capi.Logger, "un-pausing the game for the Emc Craft window", e); }
        }

        public override void OnRenderGUI(float deltaTime)
        {
            // The handbook pauses again when it is opened (or its Pause game is switched on) with this window already open.
            if (capi.IsGamePaused) KeepRunning();
            base.OnRenderGUI(deltaTime);
        }

        /// <summary>After closing: back to the handbook's pause, a moment later so the server has taken the grid's contents back first.</summary>
        void PauseAgainLater()
        {
            // Nothing to do unless a handbook is open: then the game runs only because of this window (or should be paused anyway).
            if (!HandbookOpen() || capi.IsGamePaused) return;
            capi.Event.RegisterCallback(Guard.Tick(capi.Logger, "pausing the game again after the Emc Craft window", () =>
            {
                if (EmcCraftPause.ShouldPauseAgain(mod.EmcCraftWindowOpen, HandbookOpen(), capi.IsSinglePlayer, capi.OpenedToLan, capi.Settings.Bool["noHandbookPause"], capi.IsGamePaused))
                    capi.PauseGame(true);
            }), 500);
        }

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
            KeepRunning();
            IPlayer player = capi.World.Player;
            // The server's grid always starts empty (it gives back anything saved in it on joining); a copy the game sent
            // with the player data may still show those old stacks here, so clear this side to match.
            for (int i = 0; i < grid.Count; i++) if (grid[i]?.Itemstack != null) grid[i].Itemstack = null;
            EmcCraft.Attach(player, grid);
            IInventory heldNow = player.InventoryManager.GetInventory(grid.InventoryID);
            capi.Logger.Notification("Vintage-E: Emc Craft: window opened on {0}; the game holds {1}", grid.InventoryID,
                heldNow == null ? "no grid" : ReferenceEquals(heldNow, grid) ? "this grid" : "another grid (" + heldNow.GetType().Name + ")");
            slotChangesLogged = 0;
            grid.SlotModified -= OnSlotModified;
            grid.SlotModified += OnSlotModified;
            mod.SendEmcCraft(new EmcCraftPacket { Action = EmcCraftAction.Open });
            tickListener = capi.Event.RegisterGameTickListener(Guard.Tick(capi.Logger, "the Emc Craft window", UpdateEmc), 250);
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
            PauseAgainLater();
        }
    }
}
