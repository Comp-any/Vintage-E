using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BEBehaviorClutterBookshelfWithLore(BlockEntity blockentity) : BEBehaviorClutterBookshelf(blockentity)
{
	public string LoreCode;

	private string[] colors = new string[16]
	{
		"aged-orangebrown", "aged-orange", "aged-darkgreen", "aged-darkgray", "aged-cherryred", "aged-brickred", "aged-darkolive", "aged-darkbeige", "aged-olive", "aged-purpleorange",
		"aged-gray", "rotten-gray", "rotten-brown", "rotten-rust", "rotten-purple", "rotten-green"
	};

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
	{
		base.FromTreeAttributes(tree, worldAccessForResolve);
		LoreCode = tree.GetString("loreCode");
	}

	public override void OnBlockPlaced(ItemStack byItemStack = null)
	{
		if (byItemStack != null)
		{
			LoreCode = byItemStack?.Attributes.GetString("loreCode", "jonas");
		}
		base.OnBlockPlaced(byItemStack);
	}

	public bool OnInteract(IPlayer byPlayer)
	{
		if (LoreCode == null)
		{
			return false;
		}
		string text = colors[GameMath.MurmurHash3Mod(Pos.X, Pos.Y, Pos.Z, colors.Length)];
		ItemStack itemStack = new ItemStack(Api.World.GetItem(new AssetLocation("lore-book-" + text)));
		itemStack.Attributes.SetString("category", LoreCode);
		if (!byPlayer.InventoryManager.TryGiveItemstack(itemStack))
		{
			EntityPos pos = byPlayer.Entity.Pos;
			Api.World.SpawnItemEntity(itemStack, pos.XYZ);
		}
		Api.World.Logger.Audit("{0} Took 1x{1} from {2} at {3}.", byPlayer.PlayerName, itemStack.Collectible.Code, Block.Code, Pos);
		LoreCode = null;
		Blockentity.MarkDirty(redrawOnClient: true);
		return true;
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		if (LoreCode != null)
		{
			tree.SetString("loreCode", LoreCode);
		}
	}

	public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
	{
		MaybeInitialiseMesh_OffThread();
		if (LoreCode != null)
		{
			mesher.AddMeshData(genMesh(new AssetLocation("shapes/block/clutter/" + Type + "-book.json")));
		}
		return base.OnTesselation(mesher, tessThreadTesselator);
	}

	public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
	{
		base.GetBlockInfo(forPlayer, dsc);
		if (Api is ICoreClientAPI coreClientAPI && coreClientAPI.Settings.Bool["extendedDebugInfo"])
		{
			dsc.AppendLine("lore code:" + LoreCode);
		}
	}

	private MeshData genMesh(AssetLocation assetLocation)
	{
		return ObjectCacheUtil.GetOrCreate(Api, "bookshelflorebook-" + Variant + " - " + assetLocation.Path + "-" + rotateX + "-" + rotateY + "-" + rotateZ, () =>
		{
			Shape shape = Api.Assets.TryGet(assetLocation).ToObject<Shape>();
			(Api as ICoreClientAPI).Tesselator.TesselateShape(Block, shape, out var modeldata, new Vec3f(rotateX * (180f / (float)Math.PI), rotateY * (180f / (float)Math.PI), rotateZ * (180f / (float)Math.PI)));
			if (Variant == "half")
			{
				modeldata.Translate(-0.5f * GameMath.Sin(rotateY), 0f, -0.5f * GameMath.Cos(rotateY));
			}
			return modeldata;
		});
	}
}
