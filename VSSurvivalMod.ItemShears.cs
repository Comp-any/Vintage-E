using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class ItemShears : Item
{
	public virtual int MultiBreakQuantity => 5;

	public virtual bool CanMultiBreak(Block block)
	{
		return block.BlockMaterial == EnumBlockMaterial.Leaves;
	}

	public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
	{
		float num = base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
		int remainingDurability = itemslot.Itemstack.Collectible.GetRemainingDurability(itemslot.Itemstack);
		DamageNearbyBlocks(player, blockSel, remainingResistance - num, remainingDurability);
		return num;
	}

	private void DamageNearbyBlocks(IPlayer player, BlockSelection blockSel, float damage, int leftDurability)
	{
		Block block = player.Entity.World.BlockAccessor.GetBlock(blockSel.Position);
		if (!CanMultiBreak(block))
		{
			return;
		}
		Vec3d hitPos = blockSel.Position.ToVec3d().Add(blockSel.HitPosition);
		IEnumerable<BlockPos> enumerable = from x in GetNearblyMultibreakables(player.Entity.World, blockSel.Position, hitPos)
			orderby x.Value
			select x.Key;
		int num = Math.Min(MultiBreakQuantity, leftDurability);
		foreach (BlockPos item in enumerable)
		{
			if (num == 0)
			{
				break;
			}
			BlockFacing opposite = BlockFacing.FromNormal(player.Entity.Pos.GetViewVector()).Opposite;
			if (player.Entity.World.Claims.TryAccess(player, item, EnumBlockAccessFlags.BuildOrBreak))
			{
				player.Entity.World.BlockAccessor.DamageBlock(item, opposite, damage);
				num--;
			}
		}
	}

	public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1f)
	{
		Block block = world.BlockAccessor.GetBlock(blockSel.Position);
		if (!(byEntity is EntityPlayer) || itemslot.Itemstack == null)
		{
			return true;
		}
		IPlayer player = world.PlayerByUid((byEntity as EntityPlayer).PlayerUID);
		breakMultiBlock(blockSel.Position, player);
		if (!CanMultiBreak(block))
		{
			return true;
		}
		Vec3d hitPos = blockSel.Position.ToVec3d().Add(blockSel.HitPosition);
		IOrderedEnumerable<KeyValuePair<BlockPos, float>> orderedEnumerable = from x in GetNearblyMultibreakables(world, blockSel.Position, hitPos)
			orderby x.Value
			select x;
		itemslot.Itemstack.Collectible.GetRemainingDurability(itemslot.Itemstack);
		int num = 0;
		foreach (KeyValuePair<BlockPos, float> item in orderedEnumerable)
		{
			if (player.Entity.World.Claims.TryAccess(player, item.Key, EnumBlockAccessFlags.BuildOrBreak))
			{
				breakMultiBlock(item.Key, player);
				DamageItem(world, byEntity, itemslot);
				num++;
				if (num >= MultiBreakQuantity || itemslot.Itemstack == null)
				{
					break;
				}
			}
		}
		return true;
	}

	protected virtual void breakMultiBlock(BlockPos pos, IPlayer plr)
	{
		api.World.BlockAccessor.BreakBlock(pos, plr);
		api.World.BlockAccessor.MarkBlockDirty(pos);
	}

	private Vintagestory.API.Datastructures.OrderedDictionary<BlockPos, float> GetNearblyMultibreakables(IWorldAccessor world, BlockPos pos, Vec3d hitPos)
	{
		Vintagestory.API.Datastructures.OrderedDictionary<BlockPos, float> orderedDictionary = new Vintagestory.API.Datastructures.OrderedDictionary<BlockPos, float>();
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				for (int k = -1; k <= 1; k++)
				{
					if (i != 0 || j != 0 || k != 0)
					{
						BlockPos blockPos = pos.AddCopy(i, j, k);
						if (CanMultiBreak(world.BlockAccessor.GetBlock(blockPos)))
						{
							orderedDictionary.Add(blockPos, hitPos.SquareDistanceTo((double)blockPos.X + 0.5, (double)blockPos.Y + 0.5, (double)blockPos.Z + 0.5));
						}
					}
				}
			}
		}
		return orderedDictionary;
	}
}
