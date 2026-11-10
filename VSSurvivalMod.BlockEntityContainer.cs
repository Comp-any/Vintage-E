using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public abstract class BlockEntityContainer : BlockEntity, IBlockEntityContainer
{
	protected InWorldContainer container;

	public abstract InventoryBase Inventory { get; }

	public abstract string InventoryClassName { get; }

	IInventory IBlockEntityContainer.Inventory => Inventory;

	protected BlockEntityContainer()
	{
		container = new InWorldContainer(() => Inventory, "inventory");
	}

	public override void Initialize(ICoreAPI api)
	{
		base.Initialize(api);
		string inventoryID = InventoryClassName + "-" + Pos;
		Inventory.LateInitialize(inventoryID, api);
		Inventory.Pos = Pos;
		container.Init(Api, () => Pos, () =>
		{
			MarkDirty(redrawOnClient: true);
		});
		RegisterGameTickListener(OnTick, 10000);
	}

	protected virtual void OnTick(float dt)
	{
		container.OnTick(dt);
	}

	public override void OnBlockPlaced(ItemStack byItemStack = null)
	{
		base.OnBlockPlaced(byItemStack);
		if (byItemStack?.Block is BlockContainer blockContainer)
		{
			ItemStack[] contents = blockContainer.GetContents(Api.World, byItemStack);
			if (contents != null && contents.Length > Inventory.Count)
			{
				throw new InvalidOperationException($"OnBlockPlaced stack copy failed. Trying to set {contents.Length} stacks on an inventory with {Inventory.Count} slots");
			}
			int num = 0;
			while (contents != null && num < contents.Length)
			{
				Inventory[num].Itemstack = contents[num]?.Clone();
				num++;
			}
		}
	}

	public override void OnBlockBroken(IPlayer byPlayer = null)
	{
		if (Api is ICoreServerAPI)
		{
			if (!Inventory.Empty)
			{
				StringBuilder stringBuilder = new StringBuilder($"{byPlayer?.PlayerName} broke container {Block.Code} at {Pos} dropped: ");
				foreach (ItemSlot item in Inventory)
				{
					if (item.Itemstack != null)
					{
						stringBuilder.Append(item.Itemstack.StackSize).Append("x ").Append(item.Itemstack.Collectible?.Code)
							.Append(", ");
					}
				}
				Api.Logger.Audit(stringBuilder.ToString());
			}
			Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
		}
		base.OnBlockBroken(byPlayer);
	}

	public ItemStack[] GetNonEmptyContentStacks(bool cloned = true)
	{
		List<ItemStack> list = new List<ItemStack>();
		foreach (ItemSlot item in Inventory)
		{
			if (!item.Empty)
			{
				list.Add(cloned ? item.Itemstack.Clone() : item.Itemstack);
			}
		}
		return list.ToArray();
	}

	public ItemStack[] GetContentStacks(bool cloned = true)
	{
		List<ItemStack> list = new List<ItemStack>();
		foreach (ItemSlot item in Inventory)
		{
			list.Add(cloned ? item.Itemstack?.Clone() : item.Itemstack);
		}
		return list.ToArray();
	}

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
	{
		base.FromTreeAttributes(tree, worldForResolving);
		container.FromTreeAttributes(tree, worldForResolving);
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		container.ToTreeAttributes(tree);
	}

	public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
	{
		container.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
	}

	public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
	{
		container.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
	}

	public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
	{
		base.GetBlockInfo(forPlayer, dsc);
		container.ReloadRoom();
		float num = container.GetPerishRate();
		if (Inventory is InventoryGeneric)
		{
			InventoryGeneric inventoryGeneric = Inventory as InventoryGeneric;
			if (inventoryGeneric.TransitionableSpeedMulByType != null && inventoryGeneric.TransitionableSpeedMulByType.TryGetValue(EnumTransitionType.Perish, out var value))
			{
				num *= value;
			}
			if (inventoryGeneric.PerishableFactorByFoodCategory != null)
			{
				dsc.AppendLine(Lang.Get("Stored food perish speed:"));
				foreach (KeyValuePair<EnumFoodCategory, float> item in inventoryGeneric.PerishableFactorByFoodCategory)
				{
					string text = Lang.Get("foodcategory-" + item.Key.ToString().ToLowerInvariant());
					dsc.AppendLine(Lang.Get("- {0}: {1}x", text, Math.Round(num * item.Value, 2)));
				}
				if (inventoryGeneric.PerishableFactorByFoodCategory.Count != Enum.GetValues(typeof(EnumFoodCategory)).Length)
				{
					dsc.AppendLine(Lang.Get("- {0}: {1}x", Lang.Get("food_perish_speed_other"), Math.Round(num, 2)));
				}
				return;
			}
		}
		dsc.AppendLine(Lang.Get("Stored food perish speed: {0}x", Math.Round(num, 2)));
	}

	public virtual void DropContents(Vec3d atPos)
	{
	}

	public virtual void CheckInventoryClearedMidTick()
	{
	}

	protected virtual void Dispose()
	{
		GetBehavior<BEBehaviorContainedBagInventory>()?.Dispose();
	}
}
