using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Matter versions of the game's tools where the game's own code fixes the reach or pace, so red matter can do
    /// more than dark matter (the stats, speeds and damage are in the item files). Each is the game's class with one
    /// thing changed; everything else stays the game's.
    /// </summary>
    static class MatterTier
    {
        public static bool IsRed(CollectibleObject c) => c?.Variant?["material"] == "red";
    }

    /// <summary>
    /// Scythe. The game's cuts up to 5 plants in the 3×3×3 around the one cut. Dark matter: every plant there (up to 26).
    /// Red matter: also the ring around that, a 5×5 sweep three blocks high (up to 74).
    /// </summary>
    public class ItemMatterScythe : ItemScythe
    {
        public const int RedRadius = 2;
        public override int MultiBreakQuantity => 26;

        public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1)
        {
            bool sweep = MatterTier.IsRed(this) && blockSel != null && CanMultiBreak(world.BlockAccessor.GetBlock(blockSel.Position));
            bool result = base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
            if (!sweep || !(byEntity is EntityPlayer ep)) return result;
            IPlayer player = world.PlayerByUid(ep.PlayerUID);
            if (player == null) return result;
            BlockPos centre = blockSel.Position;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -RedRadius; dx <= RedRadius; dx++)
                    for (int dz = -RedRadius; dz <= RedRadius; dz++)
                    {
                        if (dx >= -1 && dx <= 1 && dz >= -1 && dz <= 1) continue; // the game's own sweep did these
                        BlockPos pos = centre.AddCopy(dx, dy, dz);
                        if (!CanMultiBreak(world.BlockAccessor.GetBlock(pos))) continue;
                        if (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                        breakMultiBlock(pos, player);
                    }
            return result;
        }
    }

    /// <summary>Prospecting pick. Red matter searches twice the server's node-search reach (node mode).</summary>
    public class ItemMatterPropick : ItemProspectingPick
    {
        protected override void ProbeBlockNodeMode(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, int radius)
        {
            base.ProbeBlockNodeMode(world, byEntity, itemslot, blockSel, MatterTier.IsRed(this) ? radius * 2 : radius);
        }
    }

    /// <summary>Smithing hammer. Red matter strikes the anvil twice per swing (one strike applies the tool mode once).</summary>
    public class ItemMatterHammer : ItemHammer
    {
        protected override void strikeAnvil(EntityAgent byEntity, ItemSlot slot, ItemStack strikingItem)
        {
            base.strikeAnvil(byEntity, slot, strikingItem);
            if (!MatterTier.IsRed(this)) return;
            // Only the anvil's own work (as the game's strike does), not merging anvil parts.
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            BlockSelection sel = player?.CurrentBlockSelection;
            if (sel == null || api.World.Side != EnumAppSide.Client) return;
            if (!(byEntity.World.BlockAccessor.GetBlock(sel.Position) is BlockAnvil)) return;
            if (!(byEntity.World.BlockAccessor.GetBlockEntity(sel.Position) is BlockEntityAnvil anvil)) return;
            anvil.OnUseOver(player, sel.SelectionBoxIndex);
        }
    }
}
