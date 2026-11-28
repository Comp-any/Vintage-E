using System;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// A machine that works for one player (1.29.2, Project Expansion's Power Flowers and EMC Links): whoever placed it,
    /// or whoever an admin sets with /emc setowner. EMC it holds for its owner travels with the block when it's broken.
    /// </summary>
    public interface IOwnedMachine
    {
        string OwnerUid { get; }
        string OwnerName { get; }
        void SetOwner(string uid, string name);
        /// <summary>EMC it holds that would be lost if it were broken (a flower's bank, a link's buffer).</summary>
        decimal Held { get; set; }
    }

    public static class OwnedMachine
    {
        /// <summary>Where a broken machine's EMC is kept in its dropped block.</summary>
        public const string HeldKey = "vintageeHeldEmc";

        /// <summary>The owner, if they're in the game now (their EMC can only be paid or spent then).</summary>
        public static IServerPlayer OnlineOwner(ICoreAPI api, string ownerUid)
        {
            if (ownerUid == null || !(api is ICoreServerAPI sapi)) return null;
            return sapi.World.PlayerByUid(ownerUid) is IServerPlayer p && p.Entity != null && p.ConnectionState == EnumClientState.Playing ? p : null;
        }

        /// <summary>The tier of a collector, relay, Power Flower or EMC Link from its code ("...-mk7" is 7), or 0.</summary>
        public static int Tier(CollectibleObject c)
        {
            Match m = c?.Code == null ? null : Regex.Match(c.Code.Path, @"-mk(\d+)$");
            return m != null && m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }
    }

    /// <summary>Block class for Power Flowers and EMC Links: a machine that belongs to whoever placed it.</summary>
    public class BlockOwnedMachine : BlockEmcMachine
    {
        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
            if (placed && world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(blockSel.Position) is IOwnedMachine owned)
            {
                if (byPlayer != null) owned.SetOwner(byPlayer.PlayerUID, byPlayer.PlayerName);
                decimal held = EmcAmount.Read(byItemStack?.Attributes, OwnedMachine.HeldKey);
                if (held > 0) owned.Held = EmcAmount.Add(owned.Held, held);
            }
            return placed;
        }

        /// <summary>One with no owner (placed by a command or a world edit) becomes whoever right-clicks it first.</summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer != null && world.BlockAccessor.GetBlockEntity(blockSel.Position) is IOwnedMachine owned && owned.OwnerUid == null
                && !(byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemMatterUpgrader))
            {
                if (world.Side == EnumAppSide.Server) owned.SetOwner(byPlayer.PlayerUID, byPlayer.PlayerName);
                return true;
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            ItemStack[] drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            if (world.BlockAccessor.GetBlockEntity(pos) is IOwnedMachine owned && owned.Held > 0 && drops != null)
            {
                foreach (ItemStack d in drops)
                {
                    if (d?.Collectible != this) continue;
                    EmcAmount.Write(d.Attributes, OwnedMachine.HeldKey, owned.Held);
                    owned.Held = 0;
                    break;
                }
            }
            return drops;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            decimal held = EmcAmount.Read(inSlot?.Itemstack?.Attributes, OwnedMachine.HeldKey);
            if (held > 0) dsc.AppendLine(Vintagestory.API.Config.Lang.Get("vintagee:owned-held", EmcFormat.Total(held)));
        }
    }
}
