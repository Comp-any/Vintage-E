using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public class BEBehaviorClutterBookshelf : BEBehaviorShapeFromAttributes
{
	public string Variant;

	public string Type2;

	private BookShelfVariantGroup vgroup
	{
		get
		{
			(Block as BlockClutterBookshelf).variantGroupsByCode.TryGetValue(Variant, out var value);
			return value;
		}
	}

	public BEBehaviorClutterBookshelf(BlockEntity blockentity)
		: base(blockentity)
	{
	}

	public override void OnBlockPlaced(ItemStack byItemStack = null)
	{
		Variant = byItemStack?.Attributes.GetString("variant") ?? Variant;
		if (Variant != null)
		{
			BookShelfVariantGroup bookShelfVariantGroup = vgroup;
			if (bookShelfVariantGroup != null && bookShelfVariantGroup.DoubleSided)
			{
				Type = (Block as BlockClutterBookshelf).RandomType(Variant);
				Type2 = (Block as BlockClutterBookshelf).RandomType(Variant);
				loadMesh();
				Blockentity.MarkDirty(redrawOnClient: true);
				return;
			}
		}
		base.OnBlockPlaced(byItemStack);
	}

	public override void loadMesh()
	{
		if (Type == null || Api.Side == EnumAppSide.Server || Variant == null)
		{
			return;
		}
		IShapeTypeProps typeProps = clutterBlock.GetTypeProps(Type, null, this);
		if (typeProps != null)
		{
			mesh = createMesh(typeProps);
			if (typeProps.ShapeLOD2Resolved != null)
			{
				lod2mesh = createMesh(typeProps, forLOD2: true);
			}
		}
	}

	public override MeshData createMesh(IShapeTypeProps cprops, bool forLOD2 = false)
	{
		bool flag = offsetX == 0f && offsetY == 0f && offsetZ == 0f;
		float num = rotateY + cprops.Rotation.Y * ((float)Math.PI / 180f);
		MeshData orCreateMesh = clutterBlock.GetOrCreateMesh(cprops, null, null, forLOD2);
		MeshData meshData = ((!((rotateX == 0f && num == 0f && rotateZ == 0f) & flag)) ? orCreateMesh.Clone().Rotate(BEBehaviorShapeFromAttributes.Origin, rotateX, num, rotateZ) : orCreateMesh);
		if (!flag)
		{
			meshData.Translate(offsetX, offsetY, offsetZ);
		}
		return meshData;
	}

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
	{
		base.FromTreeAttributes(tree, worldAccessForResolve);
		string type = Type2;
		Variant = tree.GetString("variant");
		Type2 = tree.GetString("type2");
		if (worldAccessForResolve.Side == EnumAppSide.Client && Api != null && (mesh == null || type != Type2))
		{
			MaybeInitialiseMesh_OnMainThread();
			Blockentity.MarkDirty(redrawOnClient: true);
		}
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		tree.SetString("variant", Variant);
		tree.SetString("type2", Type2);
	}
}
