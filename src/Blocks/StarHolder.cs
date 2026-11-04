using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>A block entity that keeps a Klein star inside and charges it (collectors, relays).</summary>
    public interface IStarHolder
    {
        StarHolder Star { get; }
    }

    /// <summary>
    /// ProjectE's Klein star slot on collectors and relays, without a window: right-click the block with a star to
    /// put it in, right-click with an empty hand to take it back; while it's in, the block's EMC flows into it first.
    /// </summary>
    public class StarHolder
    {
        const string Key = "vintageeStar";
        public ItemStack Star;

        public bool Full => Star != null && StoredEmc.Get(Star) >= StoredEmc.Capacity(Star) - 1e-6;

        /// <summary>Moves up to <paramref name="available"/> EMC into the star; returns how much it took.</summary>
        public double Charge(double available)
        {
            if (Star == null || available < 1) return 0;
            double move = System.Math.Min(System.Math.Floor(available), StoredEmc.Capacity(Star) - StoredEmc.Get(Star));
            if (move <= 0) return 0;
            StoredEmc.Set(Star, StoredEmc.Get(Star) + move);
            return move;
        }

        /// <summary>A plain right-click (no sneak): a star in hand goes in, an empty hand takes it out. Returns true if handled (server side does the work).</summary>
        public bool Interact(BlockEntity be, IPlayer byPlayer)
        {
            ItemSlot hand = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            bool putIn = Star == null && hand?.Itemstack?.Collectible is IEmcStorageItem && hand.Itemstack.StackSize == 1;
            bool takeOut = Star != null && hand?.Empty == true;
            if (!putIn && !takeOut) return false;
            if (be.Api.Side == EnumAppSide.Client) return true;
            if (!be.Api.World.Claims.TryAccess(byPlayer, be.Pos, EnumBlockAccessFlags.Use)) return true;
            if (putIn)
            {
                Star = hand.TakeOut(1);
                hand.MarkDirty();
            }
            else
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(Star, true)) be.Api.World.SpawnItemEntity(Star, be.Pos.ToVec3d().Add(0.5, 1, 0.5));
                Star = null;
            }
            be.Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), be.Pos.X + 0.5, be.Pos.Y + 0.5, be.Pos.Z + 0.5, null, true, 8, 0.6f);
            be.MarkDirty(true);
            return true;
        }

        public void Drop(IWorldAccessor world, BlockPos pos)
        {
            if (Star == null) return;
            world.SpawnItemEntity(Star, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            Star = null;
        }

        public void ToTree(ITreeAttribute tree)
        {
            if (Star != null) tree.SetItemstack(Key, Star); else tree.RemoveAttribute(Key);
        }

        public void FromTree(ITreeAttribute tree, IWorldAccessor world)
        {
            Star = tree.GetItemstack(Key);
            if (Star != null && !Star.ResolveBlockOrItem(world)) Star = null;
        }

        public void Info(StringBuilder dsc)
        {
            if (Star == null) return;
            dsc.AppendLine(Lang.Get("vintagee:condenser-charging", Star.GetName(), EmcFormat.Format(StoredEmc.Get(Star)), EmcFormat.Format(StoredEmc.Capacity(Star))));
        }
    }
}
