using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BlockClutterBookshelfWithLore : BlockClutterBookshelf
{
	private WorldInteraction[] interactions;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		if (api.Side == EnumAppSide.Client)
		{
			interactions = ObjectCacheUtil.GetOrCreate(api, "bookshelfWithLoreInteractions", () => new WorldInteraction[1]
			{
				new WorldInteraction
				{
					ActionLangCode = "blockhelp-takelorebook",
					MouseButton = EnumMouseButton.Right
				}
			});
		}
	}

	public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
	{
		base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
		MultiTextureMeshRef multiTextureMeshRef = genCombinedMesh(itemstack);
		if (multiTextureMeshRef != null)
		{
			renderinfo.ModelRef = multiTextureMeshRef;
		}
	}

	private MultiTextureMeshRef genCombinedMesh(ItemStack itemstack)
	{
		Dictionary<string, MultiTextureMeshRef> orCreate = ObjectCacheUtil.GetOrCreate(api, "combinedBookShelfWithLoreMeshRef", () => new Dictionary<string, MultiTextureMeshRef>());
		string text = itemstack.Attributes.GetString("type", itemstack.Attributes.GetString("type1"));
		if (text == null)
		{
			return null;
		}
		if (orCreate.TryGetValue(text, out var value))
		{
			return value;
		}
		MeshData orCreateMesh = GetOrCreateMesh(GetTypeProps(text, itemstack, null));
		AssetLocation location = new AssetLocation("shapes/block/clutter/" + text + "-book.json");
		Shape shape = api.Assets.TryGet(location).ToObject<Shape>();
		(api as ICoreClientAPI).Tesselator.TesselateShape(this, shape, out var modeldata);
		if (itemstack.Attributes.GetString("variant") == "half")
		{
			modeldata.Translate(0f, 0f, -0.5f);
		}
		modeldata.AddMeshData(orCreateMesh);
		return orCreate[text] = (api as ICoreClientAPI).Render.UploadMultiTextureMesh(modeldata);
	}

	public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
	{
		BEBehaviorClutterBookshelfWithLore bEBehavior = GetBEBehavior<BEBehaviorClutterBookshelfWithLore>(pos);
		ItemStack itemStack = base.OnPickBlock(world, pos);
		if (bEBehavior != null)
		{
			itemStack.Attributes.SetString("loreCode", bEBehavior.LoreCode);
		}
		return itemStack;
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		string text = inSlot.Itemstack.Attributes.GetString("loreCode");
		if (text != null)
		{
			dsc.AppendLine("lore code:" + text);
		}
	}

	public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
	{
		BEBehaviorClutterBookshelfWithLore bEBehavior = GetBEBehavior<BEBehaviorClutterBookshelfWithLore>(blockSel.Position);
		if (bEBehavior != null && bEBehavior.OnInteract(byPlayer))
		{
			return true;
		}
		return base.OnBlockInteractStart(world, byPlayer, blockSel);
	}

	public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
	{
		return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
	}

	public override void OnUnloaded(ICoreAPI api)
	{
		base.OnUnloaded(api);
		Dictionary<string, MultiTextureMeshRef> dictionary = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, "combinedBookShelfWithLoreMeshRef");
		if (dictionary == null)
		{
			return;
		}
		foreach (KeyValuePair<string, MultiTextureMeshRef> item in dictionary)
		{
			item.Value.Dispose();
		}
	}
}
