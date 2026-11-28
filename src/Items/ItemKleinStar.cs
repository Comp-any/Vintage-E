using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Klein stars: portable EMC batteries. Right-click to empty one into your personal EMC, sneak + right-click to
    /// fill it from your personal EMC. EMC-powered rings, amulets and gadgets draw from carried stars when your
    /// personal EMC runs out, and sneak + right-clicking a collector or relay with one charges it.
    /// Combining stars in the crafting grid keeps the EMC they hold. A star's EMC value includes what it holds.
    /// Capacity comes from the "capacity" attribute.
    /// </summary>
    public class ItemKleinStar : Item, IEmcStorageItem
    {
        public double GetCapacity(ItemStack stack) => Attributes?["capacity"].AsDouble(50000) ?? 50000;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // Let blocks that take stars (collectors, relays) handle sneak-clicks themselves.
            if (blockSel != null && byEntity.Controls.Sneak && byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is IEmcInteractable)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null || slot.Itemstack == null) return;

            decimal stored = StoredEmc.Get(slot.Itemstack);
            decimal capacity = StoredEmc.Capacity(slot.Itemstack);
            if (byEntity.Controls.Sneak)
            {
                decimal move = Math.Min(PlayerEmc.GetEmc(player), capacity - stored);
                if (move <= 0) return;
                PlayerEmc.AddEmcExact(player, -move);
                StoredEmc.Set(slot.Itemstack, stored + move);
            }
            else
            {
                if (stored <= 0) return;
                PlayerEmc.AddEmcExact(player, stored);
                StoredEmc.Set(slot.Itemstack, 0m);
            }
            slot.MarkDirty();
            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), byEntity, null, true, 8, 0.6f);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (inSlot?.Itemstack == null) return;
            dsc.AppendLine(Lang.Get("vintagee:kleinstar-stored", EmcFormat.Total(StoredEmc.Get(inSlot.Itemstack)), EmcFormat.Format(GetCapacity(inSlot.Itemstack))));
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
            if (outputSlot?.Itemstack == null) return;
            decimal sum = 0m;
            foreach (ItemSlot slot in allInputslots)
            {
                if (slot?.Itemstack?.Collectible is IEmcStorageItem) sum = EmcAmount.Add(sum, StoredEmc.Get(slot.Itemstack));
            }
            StoredEmc.Set(outputSlot.Itemstack, Math.Min(sum, StoredEmc.Capacity(outputSlot.Itemstack)));
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-kleinstar-empty", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-kleinstar-fill", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right }
            };
        }
    }
}
