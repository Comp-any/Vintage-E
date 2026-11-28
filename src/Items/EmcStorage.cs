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

        /// <summary>The EMC in the stack, exact (see <see cref="EmcAmount"/>; a star from before 1.27.0 reads its double).</summary>
        public static decimal Get(ItemStack stack) => stack?.Attributes == null ? 0m : EmcAmount.Read(stack.Attributes, AttributeKey);

        public static void Set(ItemStack stack, decimal value)
        {
            if (stack == null) return;
            if (value <= 0) EmcAmount.Remove(stack.Attributes, AttributeKey);
            else EmcAmount.Write(stack.Attributes, AttributeKey, value);
        }

        public static decimal Capacity(ItemStack stack) => EmcAmount.From((stack?.Collectible as IEmcStorageItem)?.GetCapacity(stack) ?? 0);
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

        public static decimal Balance(IPlayer player)
        {
            decimal total = PlayerEmc.GetEmc(player);
            foreach (ItemSlot slot in CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is IEmcStorageItem) total = EmcAmount.Add(total, StoredEmc.Get(slot.Itemstack));
            }
            return total;
        }

        /// <summary>Takes <paramref name="cost"/> EMC (worked out from item values or rates) if the player has it all; otherwise takes nothing and returns false.</summary>
        public static bool TryPay(IPlayer player, double cost)
        {
            if (cost <= 0) return true;
            if (double.IsNaN(cost) || double.IsInfinity(cost)) return false;
            return TryPayExact(player, EmcAmount.From(cost));
        }

        /// <summary>Takes an exact amount if the player has it all; otherwise takes nothing and returns false.</summary>
        public static bool TryPayExact(IPlayer player, decimal amount)
        {
            if (amount <= 0) return true;
            if (player == null) return false;

            decimal personal = PlayerEmc.GetEmc(player);
            // Personal EMC covers it, as it nearly always does: nothing to take from Klein stars, so no need to go
            // through the inventory looking for them (twice: once to add them up, once to take from them).
            if (personal >= amount)
            {
                PlayerEmc.SetEmc(player, personal - amount);
                return true;
            }
            if (Balance(player) < amount) return false;
            decimal fromPersonal = Math.Min(personal, amount);
            if (fromPersonal > 0) PlayerEmc.SetEmc(player, personal - fromPersonal);
            decimal left = amount - fromPersonal;

            foreach (ItemSlot slot in CarriedSlots(player))
            {
                if (left <= 0) break;
                if (!(slot.Itemstack.Collectible is IEmcStorageItem)) continue;
                decimal stored = StoredEmc.Get(slot.Itemstack);
                if (stored <= 0) continue;
                decimal take = Math.Min(stored, left);
                StoredEmc.Set(slot.Itemstack, stored - take);
                slot.MarkDirty();
                left -= take;
            }
            return true;
        }
    }
}
