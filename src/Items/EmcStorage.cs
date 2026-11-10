using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>An item that holds EMC in its stack attributes (Klein stars).</summary>
    public interface IEmcStorageItem
    {
        double GetCapacity(ItemStack stack);
    }

    /// <summary>Reading/writing EMC stored on item stacks.</summary>
    public static class StoredEmc
    {
        public const string AttributeKey = "vintageeStoredEmc";

        public static double Get(ItemStack stack) => stack?.Attributes?.GetDouble(AttributeKey, 0) ?? 0;

        public static void Set(ItemStack stack, double value)
        {
            if (stack == null) return;
            if (double.IsNaN(value) || value < 0) value = 0;
            if (value <= 0) stack.Attributes.RemoveAttribute(AttributeKey);
            else stack.Attributes.SetDouble(AttributeKey, value);
        }

        public static double Capacity(ItemStack stack) => (stack?.Collectible as IEmcStorageItem)?.GetCapacity(stack) ?? 0;
    }

    /// <summary>
    /// Everything that can pay for EMC-powered items: the player's personal EMC first, then any Klein stars they
    /// carry (hotbar, then backpack). Server side only.
    /// </summary>
    public static class EmcWallet
    {
        /// <summary>The inventories that count as carried, in the order they are gone through.</summary>
        public static readonly string[] CarriedInventories = { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.characterInvClassName };

        public static IEnumerable<ItemSlot> CarriedSlots(IPlayer player)
        {
            var inv = player?.InventoryManager;
            if (inv == null) yield break;
            foreach (string name in CarriedInventories)
            {
                IInventory own = inv.GetOwnInventory(name);
                if (own == null) continue;
                foreach (ItemSlot slot in own)
                {
                    if (slot?.Itemstack != null) yield return slot;
                }
            }
        }

        public static double Balance(IPlayer player)
        {
            double total = PlayerEmc.GetEmc(player);
            foreach (ItemSlot slot in CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is IEmcStorageItem) total += StoredEmc.Get(slot.Itemstack);
            }
            return total;
        }

        /// <summary>Takes <paramref name="amount"/> EMC if the player has it all; otherwise takes nothing and returns false.</summary>
        public static bool TryPay(IPlayer player, double amount)
        {
            if (amount <= 0) return true;
            if (double.IsNaN(amount) || double.IsInfinity(amount)) return false;
            if (player == null) return false;

            double personal = PlayerEmc.GetEmc(player);
            // Personal EMC covers it, as it nearly always does: nothing to take from Klein stars, so no need to go
            // through the inventory looking for them (twice: once to add them up, once to take from them).
            if (personal >= amount)
            {
                PlayerEmc.SetEmc(player, personal - amount);
                return true;
            }
            if (Balance(player) + 1e-9 < amount) return false;
            double fromPersonal = Math.Min(personal, amount);
            if (fromPersonal > 0) PlayerEmc.SetEmc(player, personal - fromPersonal);
            double left = amount - fromPersonal;

            foreach (ItemSlot slot in CarriedSlots(player))
            {
                if (left <= 1e-9) break;
                if (!(slot.Itemstack.Collectible is IEmcStorageItem)) continue;
                double stored = StoredEmc.Get(slot.Itemstack);
                if (stored <= 0) continue;
                double take = Math.Min(stored, left);
                StoredEmc.Set(slot.Itemstack, stored - take);
                slot.MarkDirty();
                left -= take;
            }
            return true;
        }
    }
}
