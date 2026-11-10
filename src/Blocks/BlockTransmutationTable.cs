using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Right-click to open the transmutation GUI. The server remembers which table you opened so it can check you stay
    /// near it. A hopper or chute pointing into the table feeds it items to burn into its owner's EMC (see
    /// <see cref="BETransmutationTable"/>); the owner is whoever placed it.
    /// </summary>
    public class BlockTransmutationTable : Block
    {
        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
            if (placed && world.Side == EnumAppSide.Server) (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BETransmutationTable)?.SetOwner(byPlayer);
            return placed;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var mod = VintageEModSystem.Of(api);
            if (world.Side == EnumAppSide.Server)
            {
                if (byPlayer is IServerPlayer sp) mod.Transmutation?.OpenTableSession(sp, blockSel.Position);
                // Tables placed before they had a block entity get one now; one with no owner yet becomes yours.
                var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BETransmutationTable;
                if (be == null && EntityClass != null)
                {
                    world.BlockAccessor.SpawnBlockEntity(EntityClass, blockSel.Position);
                    be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BETransmutationTable;
                }
                if (be != null && be.OwnerUid == null) be.SetOwner(byPlayer);
            }
            else
            {
                mod.OpenTransmutationDialog(blockSel.Position);
            }
            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:blockhelp-opentable", MouseButton = EnumMouseButton.Right }
            };
        }
    }
}
