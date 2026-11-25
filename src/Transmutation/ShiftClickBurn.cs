using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Shift-click an item in your own inventory while the Transmutation Table's or Tablet's window is open, and the
    /// whole stack is burned, as if it had been dropped on Burn (1.26.8). Only what can burn: anything else moves
    /// the way a shift-click always moves it.
    ///
    /// The table's Burn slot is no real slot (the window shows stacks and sends requests; nothing is ever put in
    /// it), so the game's own shift-click, which looks for a slot to put the stack in, never comes its way. This
    /// asks instead: the player's game catches the click before the game acts on it and sends the server
    /// "burn what is in this slot". Nothing moves on the player's side. The server looks at the slot itself, burns
    /// by the same rules as the Burn slot, and its change to the slot comes back the ordinary way. If the server
    /// says no, nothing has happened anywhere, so the two sides can't come to disagree about where a stack is.
    ///
    /// The catch is on <see cref="GuiElementItemSlotGridBase.SlotClick"/>, which every grid of slots in the game goes
    /// through for a click (the hotbar, the inventory window, a chest; read from 1.22.7). The game has no event for
    /// it. Skipped: the game then makes no move of its own and sends the server no click.
    ///
    /// Not burned this way, because a shift-click is too easily made:
    /// <list type="bullet">
    /// <item>a bag in one of the four bag slots (it would go with all that is in it);</item>
    /// <item>a container with things in it (a filled crate, a bag carried as an item), whatever the server's
    /// ProtectContainersWithContents says; what is valued together with its contents (a bowl of honey) does burn;</item>
    /// <item>a Klein star (dropped on Burn it is burned for what it holds; that stays a thing done on purpose);</item>
    /// <item>a Transmutation Tablet, the one in use above all.</item>
    /// </list>
    /// </summary>
    public static class ShiftClickBurn
    {
        public const string HarmonyId = "vintagee.shiftclickburn";

        static VintageEModSystem mod;
        static ICoreClientAPI capi;
        static bool patched;
        static bool failedOnce;

        /// <summary>Why the hook isn't in place (null when it is, or was never tried).</summary>
        public static string Error { get; private set; }
        public static bool Patched => patched;

        /// <summary>The game's method for a click on a slot in any grid of slots.</summary>
        public static MethodInfo Target() => AccessTools.Method(typeof(GuiElementItemSlotGridBase), "SlotClick",
            new[] { typeof(ICoreClientAPI), typeof(int), typeof(EnumMouseButton), typeof(bool), typeof(bool), typeof(bool) });

        /// <summary>The grid's inventory, which the hook reads to know which slot was clicked.</summary>
        public static FieldInfo InventoryField() => AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");

        /// <summary>Puts the hook in place on a player's game. Without it a shift-click is the game's own, as before.</summary>
        public static void Patch(VintageEModSystem system, ICoreClientAPI api)
        {
            mod = system;
            capi = api;
            if (patched) return;
            try
            {
                MethodInfo target = Target();
                FieldInfo inventory = InventoryField();
                if (target == null || inventory == null || !typeof(IInventory).IsAssignableFrom(inventory.FieldType))
                {
                    Error = "the game's slot click (GuiElementItemSlotGridBase.SlotClick and its inventory) wasn't found";
                    api.Logger.Warning("Vintage-E: {0}; shift-clicking an item won't burn it while the table's window is open.", Error);
                    return;
                }
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(typeof(ShiftClickBurn), nameof(Prefix)));
                patched = true;
                Error = null;
                api.Logger.Notification("Vintage-E: shift-click to burn: hooked the game's slot click ({0}.{1}).", target.DeclaringType?.Name, target.Name);
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                api.Logger.Warning("Vintage-E: couldn't hook the game's slot click ({0}); shift-clicking an item won't burn it while the table's window is open.", Error);
            }
        }

        /// <summary>The player left that world: its game isn't held on to. The hook stays and waits for the next one.</summary>
        public static void Forget(VintageEModSystem system)
        {
            if (mod != system) return;
            mod = null;
            capi = null;
        }

        /// <summary>False: the click was turned into a burn request, and the game does nothing with it.</summary>
        static bool Prefix(int slotId, EnumMouseButton mouseButton, bool shiftPressed, bool ctrlPressed, bool altPressed, IInventory ___inventory)
        {
            if (!shiftPressed) return true;
            try
            {
                return !TryBurn(___inventory, slotId, mouseButton, ctrlPressed, altPressed);
            }
            catch (Exception e)
            {
                if (!failedOnce)
                {
                    failedOnce = true;
                    capi?.Logger.Warning("Vintage-E: shift-click to burn failed ({0}: {1}); the game's own shift-click is used. Said once.", e.GetType().Name, e.Message);
                }
                return true;
            }
        }

        static bool TryBurn(IInventory inventory, int slotId, EnumMouseButton button, bool ctrl, bool alt)
        {
            if (mod == null || capi == null || inventory == null) return false;
            if (!Wanted(mod.TableWindowOpen, mod.Emc?.BurnBySlot == true, mod.ClientConfig?.ShiftClickBurns != false, button, ctrl, alt)) return false;
            IClientPlayer player = capi.World?.Player;
            if (player == null || slotId < 0 || slotId >= inventory.Count) return false;
            ItemSlot slot = inventory[slotId];
            if (!Eligible(player.PlayerUID, inventory, slot) || !CanBurn(mod, slot.Itemstack)) return false;
            mod.SendTransmuteRequest(new TransmuteRequestPacket
            {
                Action = TransmuteAction.BurnSlot,
                Key = slot.Itemstack.Collectible.Code.ToString(),
                SlotInv = inventory.InventoryID,
                Slot = slotId + 1
            });
            // What the game does at the end of every slot click, for whoever listens to it.
            try { capi.Input.TriggerOnMouseClickSlot(slot); } catch { }
            return true;
        }

        /// <summary>
        /// Whether a shift-click is one to look at at all: the table's window is open, the server knows the request
        /// (one before 1.26.8 would ignore it, and the click would do nothing at all), the player hasn't switched it
        /// off, and it is a plain left shift-click (with Ctrl or Alt, or the right button, it is the game's).
        /// </summary>
        public static bool Wanted(bool windowOpen, bool serverKnows, bool switchedOn, EnumMouseButton button, bool ctrl, bool alt) =>
            windowOpen && serverKnows && switchedOn && button == EnumMouseButton.Left && !ctrl && !alt;

        /// <summary>
        /// Whether a slot is one whose stack a shift-click may burn: a filled slot of that player's own hotbar or
        /// backpack, not one of the bag slots, not the hotbar's skill slot, and not holding a tablet.
        /// </summary>
        public static bool Eligible(string playerUid, IInventory inventory, ItemSlot slot)
        {
            if (string.IsNullOrEmpty(playerUid) || inventory == null || slot?.Itemstack?.Collectible?.Code == null) return false;
            string id = inventory.InventoryID;
            if (id != GlobalConstants.hotBarInvClassName + "-" + playerUid && id != GlobalConstants.backpackInvClassName + "-" + playerUid) return false;
            if (slot is ItemSlotBackpack || slot.GetType().Name == "ItemSlotSkill") return false;
            return !(slot.Itemstack.Collectible is ItemTransmutationTablet);
        }

        /// <summary>
        /// Whether a stack is one a shift-click burns, as far as can be told without asking who the player is. Both
        /// sides ask this: the player's game to decide whether the click is its own or the game's, the server before
        /// its own rules for burning (<see cref="TransmutationService.PlanBurn"/>), which have the last word.
        /// </summary>
        public static bool CanBurn(VintageEModSystem system, ItemStack stack)
        {
            if (system?.Emc == null || stack?.Collectible == null || stack.StackSize <= 0) return false;
            if (stack.Collectible is IEmcStorageItem || stack.Collectible is ItemTransmutationTablet) return false;
            // Something inside it: burned only where the item is valued and learned with what it holds (a bowl of
            // honey, a full bucket). A bag or a crate with things in it is never burned by a shift-click.
            StackDataTable data = system.Emc.StackData;
            bool keepsData = data != null && data.CarriesData(stack.Collectible) && data.ModeOf(stack.Collectible) != StackDataMode.Item;
            if (!keepsData && StackContents.HasNestedStacks(stack)) return false;
            if (system.Emc.IsNoTransmute(stack.Collectible)) return false;
            return system.Emc.GetValue(stack) > 0;
        }

        /// <summary>The slot a request names: one of that player's own hotbar or backpack slots, or null.</summary>
        public static ItemSlot SlotOf(IPlayer player, string inventoryId, int slotId)
        {
            if (player?.InventoryManager == null || string.IsNullOrEmpty(inventoryId) || slotId < 0) return null;
            foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory inventory = player.InventoryManager.GetOwnInventory(name);
                if (inventory == null || inventory.InventoryID != inventoryId) continue;
                return slotId < inventory.Count ? inventory[slotId] : null;
            }
            return null;
        }
    }
}
