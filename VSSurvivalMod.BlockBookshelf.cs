using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BlockBookshelf : BlockShapeMaterialFromAttributes
{
	public Dictionary<string, int[]> UsableSlots;

	public override string MeshKey { get; } = "BookshelfMeshes";

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		LoadTypes();
	}

	public override void LoadTypes()
	{
		base.LoadTypes();
		UsableSlots = Attributes["usableSlots"].AsObject<Dictionary<string, int[]>>();
		ArgumentNullException.ThrowIfNull(UsableSlots, "UsableSlots");
	}

	public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
	{
		BlockEntityBookshelf blockEntityBookshelf = blockAccessor.GetBlockEntity(pos) as BlockEntityBookshelf;
		if (blockEntityBookshelf?.UsableSlots != null)
		{
			List<Cuboidf> list = new List<Cuboidf>
			{
				new Cuboidf(0f, 0f, 0f, 1f, 1f, 0.1f),
				new Cuboidf(0f, 0f, 0f, 1f, 0.0625f, 0.5f),
				new Cuboidf(0f, 0.9375f, 0f, 1f, 1f, 0.5f),
				new Cuboidf(0f, 0f, 0f, 0.0625f, 1f, 0.5f),
				new Cuboidf(0.9375f, 0f, 0f, 1f, 1f, 0.5f)
			};
			for (int i = 0; i < 14; i++)
			{
				if (!blockEntityBookshelf.UsableSlots.Contains(i))
				{
					list.Add(new Cuboidf());
					continue;
				}
				float num = (float)(i % 7) * 2f / 16f + 11f / 160f;
				float num2 = (float)(i / 7) * 7.5f / 16f;
				float z = 13f / 32f;
				Cuboidf item = new Cuboidf(num, num2 + 0.0625f, 0.0625f, num + 19f / 160f, num2 + 0.4375f, z);
				list.Add(item);
			}
			for (int j = 0; j < list.Count; j++)
			{
				list[j] = list[j].RotatedCopy(0f, (blockEntityBookshelf?.MeshAngleRad ?? 0f) * (180f / (float)Math.PI), 0f, new Vec3d(0.5, 0.5, 0.5));
			}
			return list.ToArray();
		}
		return new Cuboidf[1] { new Cuboidf(0f, 0f, 0f, 1f, 1f, 0.5f).RotatedCopy(0f, (blockEntityBookshelf?.MeshAngleRad ?? 0f) * (180f / (float)Math.PI), 0f, new Vec3d(0.5, 0.5, 0.5)) };
	}

	public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
	{
		BlockEntityBookshelf blockEntityBookshelf = blockAccessor.GetBlockEntity(pos) as BlockEntityBookshelf;
		return new Cuboidf[1] { new Cuboidf(0f, 0f, 0f, 1f, 1f, 0.5f).RotatedCopy(0f, (blockEntityBookshelf?.MeshAngleRad ?? 0f) * (180f / (float)Math.PI), 0f, new Vec3d(0.5, 0.5, 0.5)) };
	}

	public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
	{
		BlockEntityBookshelf blockEntity = GetBlockEntity<BlockEntityBookshelf>(pos);
		if (blockEntity != null && blockEntity.Type != null && blockEntity.Material != null)
		{
			float[] values = Matrixf.Create().Translate(0.5f, 0.5f, 0.5f).RotateY(blockEntity.MeshAngleRad)
				.Translate(-0.5f, -0.5f, -0.5f)
				.Values;
			blockModelData = GetOrCreateMesh(blockEntity.Type, blockEntity.Material).Clone().MatrixTransform(values);
			decalModelData = GetOrCreateMesh(blockEntity.Type, blockEntity.Material, null, decalTexSource).Clone().MatrixTransform(values);
		}
		else
		{
			base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);
		}
	}

	public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
	{
		return true;
	}

	public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
	{
		if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityBookshelf blockEntityBookshelf)
		{
			return blockEntityBookshelf.OnInteract(byPlayer, blockSel);
		}
		return base.OnBlockInteractStart(world, byPlayer, blockSel);
	}
}
