using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public abstract class BlockShapeFromAttributes : Block, IWrenchOrientable, ITextureFlippable, IContainedMeshSource
{
	protected bool colSelBoxEditMode;

	protected bool transformEditMode;

	protected float rotInterval = (float)Math.PI / 8f;

	public IDictionary<string, CompositeTexture> blockTextures;

	public Dictionary<string, Vintagestory.API.Datastructures.OrderedDictionary<string, CompositeTexture>> OverrideTextureGroups;

	protected Dictionary<string, MeshData> meshDictionary;

	protected Dictionary<string, MeshData> meshDictionaryLOD2;

	protected string inventoryMeshDictionary;

	protected string blockForLogging;

	public bool AllowRandomizeDims = true;

	public SkillItem[] extraWrenchModes;

	private byte[] noLight = new byte[3];

	public abstract string ClassType { get; }

	public abstract IEnumerable<IShapeTypeProps> AllTypes { get; }

	public abstract void LoadTypes();

	public abstract IShapeTypeProps GetTypeProps(string code, ItemStack stack, BEBehaviorShapeFromAttributes be);

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		extraWrenchModes = new SkillItem[3]
		{
			new SkillItem
			{
				Code = new AssetLocation("ns"),
				Name = Lang.Get("Offset 1 Voxel North/South")
			},
			new SkillItem
			{
				Code = new AssetLocation("ew"),
				Name = Lang.Get("Offset 1 Voxel East/West")
			},
			new SkillItem
			{
				Code = new AssetLocation("ud"),
				Name = Lang.Get("Offset 1 Voxel Up/Down")
			}
		};
		if (api is ICoreClientAPI coreClientAPI)
		{
			extraWrenchModes[0].WithIcon(coreClientAPI, coreClientAPI.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/movens.svg"), 48, 48, 5, -1));
			extraWrenchModes[1].WithIcon(coreClientAPI, coreClientAPI.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/moveew.svg"), 48, 48, 5, -1));
			extraWrenchModes[2].WithIcon(coreClientAPI, coreClientAPI.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/moveud.svg"), 48, 48, 5, -1));
			meshDictionary = ObjectCacheUtil.GetOrCreate(api, ClassType + "Meshes", () => new Dictionary<string, MeshData>());
			meshDictionaryLOD2 = ObjectCacheUtil.GetOrCreate(api, ClassType + "MeshesLOD2", () => new Dictionary<string, MeshData>());
			inventoryMeshDictionary = ClassType + "MeshesInventory";
			blockForLogging = ClassType + "block";
			coreClientAPI.Event.RegisterEventBusListener(OnEventBusEvent);
			foreach (IShapeTypeProps allType in AllTypes)
			{
				if (Textures.TryGetValue(allType.Code + ":" + allType.FirstTexture, out var value))
				{
					allType.TexPos = coreClientAPI.BlockTextureAtlas[value.Baked.BakedName];
				}
			}
			blockTextures = Attributes["textures"].AsObject<IDictionary<string, CompositeTexture>>();
		}
		else
		{
			LoadTypes();
			OverrideTextureGroups = Attributes["overrideTextureGroups"].AsObject<Dictionary<string, Vintagestory.API.Datastructures.OrderedDictionary<string, CompositeTexture>>>();
		}
		AllowRandomizeDims = Attributes?["randomizeDimensions"].AsBool(defaultValue: true) ?? false;
	}

	public override void OnUnloaded(ICoreAPI api)
	{
		base.OnUnloaded(api);
		if (extraWrenchModes != null)
		{
			extraWrenchModes[0].Dispose();
			extraWrenchModes[1].Dispose();
			extraWrenchModes[2].Dispose();
		}
		if (!(api is ICoreClientAPI coreClientAPI))
		{
			return;
		}
		if (meshDictionary != null)
		{
			foreach (MeshData value in meshDictionary.Values)
			{
				value.Dispose();
			}
			ObjectCacheUtil.Delete(coreClientAPI, ClassType + "Meshes");
			meshDictionary = null;
		}
		if (meshDictionaryLOD2 != null)
		{
			foreach (MeshData value2 in meshDictionaryLOD2.Values)
			{
				value2.Dispose();
			}
			ObjectCacheUtil.Delete(coreClientAPI, ClassType + "MeshesLOD2");
			meshDictionaryLOD2 = null;
		}
		if (inventoryMeshDictionary == null)
		{
			return;
		}
		Dictionary<string, MultiTextureMeshRef> dictionary = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(coreClientAPI, inventoryMeshDictionary);
		if (dictionary == null)
		{
			return;
		}
		foreach (MultiTextureMeshRef value3 in dictionary.Values)
		{
			value3.Dispose();
		}
		ObjectCacheUtil.Delete(coreClientAPI, inventoryMeshDictionary);
	}

	public override void OnCollectTextures(ICoreAPI api, ITextureLocationDictionary textureDict)
	{
		OverrideTextureGroups = Attributes["overrideTextureGroups"].AsObject<Dictionary<string, Vintagestory.API.Datastructures.OrderedDictionary<string, CompositeTexture>>>();
		base.api = api;
		LoadTypes();
		foreach (IShapeTypeProps allType in AllTypes)
		{
			allType.ShapeResolved = api.Assets.TryGet(allType.ShapePath)?.ToObject<Shape>();
			if (allType.ShapeResolved == null)
			{
				api.Logger.Error("Block {0}: Could not find {1}, type {2} shape '{3}'.", Code, ClassType, allType.Code, allType.ShapePath);
				continue;
			}
			if (Lod2Shape != null && api is ICoreClientAPI coreClientAPI)
			{
				Shape cachedShape = coreClientAPI.TesselatorManager.GetCachedShape(Lod2Shape.Base);
				cachedShape.KeepLoaded();
				allType.ShapeLOD2Resolved = cachedShape;
				textureDict.AddTextureLocation(new AssetLocationAndSource("game", "block/lod2/clutter/" + allType.Code, "Clutter LOD2:", "game", allType.Code));
			}
			FastSmallDictionary<string, CompositeTexture> fastSmallDictionary = new FastSmallDictionary<string, CompositeTexture>(1);
			textureDict.CollectAndBakeTexturesFromShape(allType.ShapeResolved, fastSmallDictionary, allType.ShapePath);
			allType.FirstTexture = fastSmallDictionary.GetFirstKey();
			foreach (KeyValuePair<string, CompositeTexture> item in fastSmallDictionary)
			{
				Textures.Add(allType.Code + ":" + item.Key, item.Value);
			}
		}
		if (OverrideTextureGroups != null)
		{
			foreach (KeyValuePair<string, Vintagestory.API.Datastructures.OrderedDictionary<string, CompositeTexture>> overrideTextureGroup in OverrideTextureGroups)
			{
				string message = string.Concat("Block ", Code, ": override texture group ", overrideTextureGroup.Key);
				foreach (KeyValuePair<string, CompositeTexture> item2 in overrideTextureGroup.Value)
				{
					item2.Value.Bake(api.Assets);
					item2.Value.Baked.TextureSubId = textureDict.GetOrAddTextureLocation(new AssetLocationAndSource(item2.Value.Baked.BakedName, message, Code));
				}
			}
		}
		base.OnCollectTextures(api, textureDict);
	}

	public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
	{
		EnumHandling handling = EnumHandling.PassThrough;
		BlockBehavior[] blockBehaviors = BlockBehaviors;
		for (int i = 0; i < blockBehaviors.Length; i++)
		{
			blockBehaviors[i].OnNeighbourBlockChange(world, pos, neibpos, ref handling);
			if (handling == EnumHandling.PreventSubsequent)
			{
				return;
			}
		}
		if (handling == EnumHandling.PassThrough && (this == snowCovered1 || this == snowCovered2 || this == snowCovered3) && pos.X == neibpos.X && pos.Z == neibpos.Z && pos.Y + 1 == neibpos.Y && world.BlockAccessor.GetBlock(neibpos).Id != 0)
		{
			world.BlockAccessor.ExchangeBlock(notSnowCovered.Id, pos);
			world.BlockAccessor.MarkBlockDirty(pos);
			world.BlockAccessor.MarkBlockEntityDirty(pos);
		}
	}

	public override void OnServerGameTick(IWorldAccessor world, BlockPos pos, object extra = null)
	{
		if (extra is string && (string)extra == "melt")
		{
			if (this == snowCovered3)
			{
				world.BlockAccessor.ExchangeBlock(snowCovered2.Id, pos);
			}
			else if (this == snowCovered2)
			{
				world.BlockAccessor.ExchangeBlock(snowCovered1.Id, pos);
			}
			else if (this == snowCovered1)
			{
				world.BlockAccessor.ExchangeBlock(notSnowCovered.Id, pos);
			}
			world.BlockAccessor.MarkBlockDirty(pos);
			world.BlockAccessor.MarkBlockEntityDirty(pos);
		}
	}

	private void OnEventBusEvent(string eventName, ref EnumHandling handling, IAttribute data)
	{
		switch (eventName)
		{
		case "oncloseeditselboxes":
		case "oneditselboxes":
		case "onapplyselboxes":
			onSelBoxEditorEvent(eventName, data);
			break;
		}
	}

	private void onSelBoxEditorEvent(string eventName, IAttribute data)
	{
		TreeAttribute treeAttribute = data as TreeAttribute;
		if (treeAttribute?.GetInt("nowblockid") != Id)
		{
			return;
		}
		colSelBoxEditMode = eventName == "oneditselboxes";
		BlockPos blockPos = treeAttribute.GetBlockPos("pos");
		if (colSelBoxEditMode)
		{
			BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(blockPos);
			IShapeTypeProps typeProps = GetTypeProps(bEBehavior?.Type, null, bEBehavior);
			if (typeProps != null)
			{
				if (typeProps.ColSelBoxes == null)
				{
					typeProps.ColSelBoxes = new Cuboidf[1] { Cuboidf.Default() };
				}
				SelectionBoxes = typeProps.ColSelBoxes;
			}
		}
		if (eventName == "onapplyselboxes")
		{
			BEBehaviorShapeFromAttributes bEBehavior2 = GetBEBehavior<BEBehaviorShapeFromAttributes>(blockPos);
			IShapeTypeProps typeProps2 = GetTypeProps(bEBehavior2?.Type, null, bEBehavior2);
			if (typeProps2 != null)
			{
				typeProps2.ColSelBoxes = SelectionBoxes;
				SelectionBoxes = new Cuboidf[1] { Cuboidf.Default() };
			}
		}
	}

	public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
	{
		return GetCollisionBoxes(blockAccessor, pos);
	}

	public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior == null)
		{
			return base.GetCollisionBoxes(blockAccessor, pos);
		}
		IShapeTypeProps typeProps = GetTypeProps(bEBehavior.Type, null, bEBehavior);
		return getCollisionBoxes(blockAccessor, pos, bEBehavior, typeProps);
	}

	private Cuboidf[] getCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, BEBehaviorShapeFromAttributes bect, IShapeTypeProps cprops)
	{
		if (cprops?.ColSelBoxes == null)
		{
			return base.GetCollisionBoxes(blockAccessor, pos);
		}
		if (colSelBoxEditMode)
		{
			return cprops.ColSelBoxes;
		}
		long key = ((long)(bect.offsetX * 255f + 255f) << 45) | ((long)(bect.offsetY * 255f + 255f) << 36) | ((long)(bect.offsetZ * 255f + 255f) << 27) | ((long)((bect.rotateY + ((bect.rotateY < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI)) << 18) | ((long)((bect.rotateX + ((bect.rotateX < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI)) << 9) | (long)((bect.rotateZ + ((bect.rotateZ < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI));
		if (cprops.ColSelBoxesByHashkey.TryGetValue(key, out var value))
		{
			return value;
		}
		value = (cprops.ColSelBoxesByHashkey[key] = new Cuboidf[cprops.ColSelBoxes.Length]);
		for (int i = 0; i < value.Length; i++)
		{
			value[i] = cprops.ColSelBoxes[i].RotatedCopy(bect.rotateX * (180f / (float)Math.PI), bect.rotateY * (180f / (float)Math.PI), bect.rotateZ * (180f / (float)Math.PI), new Vec3d(0.5, 0.5, 0.5)).ClampTo(Vec3f.Zero, Vec3f.One).OffsetCopy(bect.offsetX, bect.offsetY, bect.offsetZ);
		}
		return value;
	}

	public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior == null)
		{
			return base.GetCollisionBoxes(blockAccessor, pos);
		}
		IShapeTypeProps typeProps = GetTypeProps(bEBehavior.Type, null, bEBehavior);
		if (typeProps?.SelBoxes == null)
		{
			return getCollisionBoxes(blockAccessor, pos, bEBehavior, typeProps);
		}
		if (colSelBoxEditMode)
		{
			return typeProps.ColSelBoxes ?? typeProps.SelBoxes;
		}
		long key = ((long)(bEBehavior.offsetX * 255f + 255f) << 45) | ((long)(bEBehavior.offsetY * 255f + 255f) << 36) | ((long)(bEBehavior.offsetZ * 255f + 255f) << 27) | ((long)((bEBehavior.rotateY + ((bEBehavior.rotateY < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI)) << 18) | ((long)((bEBehavior.rotateX + ((bEBehavior.rotateX < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI)) << 9) | (long)((bEBehavior.rotateZ + ((bEBehavior.rotateZ < 0f) ? ((float)Math.PI * 2f) : 0f)) * (180f / (float)Math.PI));
		if (typeProps.SelBoxesByHashkey == null)
		{
			typeProps.SelBoxesByHashkey = new Dictionary<long, Cuboidf[]>();
		}
		if (typeProps.SelBoxesByHashkey.TryGetValue(key, out var value))
		{
			return value;
		}
		value = (typeProps.SelBoxesByHashkey[key] = new Cuboidf[typeProps.SelBoxes.Length]);
		for (int i = 0; i < value.Length; i++)
		{
			value[i] = typeProps.SelBoxes[i].RotatedCopy(bEBehavior.rotateX * (180f / (float)Math.PI), bEBehavior.rotateY * (180f / (float)Math.PI), bEBehavior.rotateZ * (180f / (float)Math.PI), new Vec3d(0.5, 0.5, 0.5)).ClampTo(Vec3f.Zero, Vec3f.One).OffsetCopy(bEBehavior.offsetX, bEBehavior.offsetY, bEBehavior.offsetZ);
		}
		return value;
	}

	public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
	{
		base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
		Dictionary<string, MultiTextureMeshRef> orCreate = ObjectCacheUtil.GetOrCreate(capi, inventoryMeshDictionary, () => new Dictionary<string, MultiTextureMeshRef>());
		string code = itemstack.Attributes.GetString("type", "");
		IShapeTypeProps typeProps = GetTypeProps(code, itemstack, null);
		if (typeProps == null)
		{
			return;
		}
		float radX = itemstack.Attributes.GetFloat("rotX");
		float radY = itemstack.Attributes.GetFloat("rotY");
		float radZ = itemstack.Attributes.GetFloat("rotZ");
		string text = itemstack.Attributes.GetString("overrideTextureCode");
		string key = typeProps.HashKey + "-" + radX + "-" + radY + "-" + radZ + "-" + text;
		if (!orCreate.TryGetValue(key, out var value))
		{
			MeshData orCreateMesh = GetOrCreateMesh(typeProps, null, text);
			orCreateMesh = orCreateMesh.Clone().Rotate(radX, radY, radZ);
			value = (orCreate[key] = capi.Render.UploadMultiTextureMesh(orCreateMesh));
		}
		renderinfo.ModelRef = value;
		if (transformEditMode)
		{
			return;
		}
		switch (target)
		{
		case EnumItemRenderTarget.Ground:
			if (typeProps.GroundTransform != null)
			{
				renderinfo.Transform = typeProps.GroundTransform;
			}
			break;
		case EnumItemRenderTarget.Gui:
			if (typeProps.GuiTransform != null)
			{
				renderinfo.Transform = typeProps.GuiTransform;
			}
			break;
		case EnumItemRenderTarget.HandTp:
			if (typeProps.TpTransform != null)
			{
				renderinfo.Transform = typeProps.TpTransform;
			}
			break;
		case EnumItemRenderTarget.HandFp:
		case EnumItemRenderTarget.HandTpOff:
			break;
		}
	}

	public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
	{
		ItemStack itemStack = base.OnPickBlock(world, pos);
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior != null)
		{
			itemStack.Attributes.SetString("type", bEBehavior.Type);
			if (bEBehavior.overrideTextureCode != null)
			{
				itemStack.Attributes.SetString("overrideTextureCode", bEBehavior.overrideTextureCode);
			}
		}
		return itemStack;
	}

	public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
	{
		bool flag = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
		if (flag)
		{
			BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position);
			if (bEBehavior != null)
			{
				BlockPos blockPos = (blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position);
				double y = byPlayer.Entity.Pos.X - ((double)blockPos.X + blockSel.HitPosition.X);
				double x = (double)(float)byPlayer.Entity.Pos.Z - ((double)blockPos.Z + blockSel.HitPosition.Z);
				float defaultValue = (float)(int)Math.Round((float)Math.Atan2(y, x) / rotInterval) * rotInterval;
				bEBehavior.rotateX = byItemStack.Attributes.GetFloat("rotX");
				bEBehavior.rotateY = byItemStack.Attributes.GetFloat("rotY", defaultValue);
				bEBehavior.rotateZ = byItemStack.Attributes.GetFloat("rotZ");
				string text = byItemStack.Attributes.GetString("overrideTextureCode");
				if (text != null)
				{
					bEBehavior.overrideTextureCode = text;
				}
				bEBehavior.OnBlockPlaced(byItemStack);
			}
		}
		return flag;
	}

	public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior != null)
		{
			IShapeTypeProps typeProps = GetTypeProps(bEBehavior.Type, null, bEBehavior);
			if (typeProps == null)
			{
				base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);
				return;
			}
			blockModelData = GetOrCreateMesh(typeProps, null, bEBehavior.overrideTextureCode).Clone().Rotate(bEBehavior.rotateX, bEBehavior.rotateY + typeProps.Rotation.Y * ((float)Math.PI / 180f), bEBehavior.rotateZ);
			decalModelData = GetOrCreateMesh(typeProps, decalTexSource, bEBehavior.overrideTextureCode).Clone().Rotate(bEBehavior.rotateX, bEBehavior.rotateY + typeProps.Rotation.Y * ((float)Math.PI / 180f), bEBehavior.rotateZ);
		}
		else
		{
			base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);
		}
	}

	public virtual MeshData GetOrCreateMesh(IShapeTypeProps cprops, ITexPositionSource overrideTexturesource = null, string overrideTextureCode = null, bool forLOD2 = false)
	{
		Dictionary<string, MeshData> dictionary = meshDictionary;
		ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
		if (overrideTexturesource != null || !dictionary.TryGetValue(cprops.Code + "-" + overrideTextureCode, out var modeldata))
		{
			modeldata = new MeshData(4, 3);
			Shape shapeResolved = cprops.ShapeResolved;
			ITexPositionSource texPositionSource = overrideTexturesource;
			if (texPositionSource == null)
			{
				ShapeTextureSource shapeTextureSource = new ShapeTextureSource(coreClientAPI, shapeResolved, cprops.ShapePath.ToString());
				texPositionSource = shapeTextureSource;
				if (blockTextures != null)
				{
					foreach (KeyValuePair<string, CompositeTexture> blockTexture in blockTextures)
					{
						if (blockTexture.Value.Baked == null)
						{
							blockTexture.Value.Bake(coreClientAPI.Assets);
						}
						shapeTextureSource.textures[blockTexture.Key] = blockTexture.Value;
					}
				}
				if (cprops.Textures != null)
				{
					foreach (KeyValuePair<string, CompositeTexture> texture in cprops.Textures)
					{
						CompositeTexture compositeTexture = texture.Value.Clone();
						compositeTexture.Bake(coreClientAPI.Assets);
						shapeTextureSource.textures[texture.Key] = compositeTexture;
					}
				}
				if (overrideTextureCode != null && cprops.TextureFlipCode != null && OverrideTextureGroups[cprops.TextureFlipGroupCode].TryGetValue(overrideTextureCode, out var value))
				{
					value.Bake(coreClientAPI.Assets);
					shapeTextureSource.textures[cprops.TextureFlipCode] = value;
				}
			}
			if (shapeResolved == null)
			{
				return modeldata;
			}
			coreClientAPI.Tesselator.TesselateShape(blockForLogging, shapeResolved, out modeldata, texPositionSource, null, 0, 0, 0);
			if (cprops.TexPos == null)
			{
				api.Logger.Warning("No texture previously loaded for clutter block " + cprops.Code);
				cprops.TexPos = (texPositionSource as ShapeTextureSource)?.firstTexPos;
				cprops.TexPos.RndColors = new int[30];
			}
			if (overrideTexturesource == null)
			{
				dictionary[cprops.Code + "-" + overrideTextureCode] = modeldata;
			}
		}
		return modeldata;
	}

	public override EnumBlockMaterial GetBlockMaterial(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
	{
		if (pos == null)
		{
			string code = stack.Attributes.GetString("type", "");
			return GetTypeProps(code, stack, null)?.Material ?? EnumBlockMaterial.Stone;
		}
		BEBehaviorShapeFromAttributes bEBehaviorShapeFromAttributes = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorShapeFromAttributes>();
		return GetTypeProps(bEBehaviorShapeFromAttributes?.Type, null, bEBehaviorShapeFromAttributes)?.Material ?? EnumBlockMaterial.Stone;
	}

	public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
	{
		if (pos == null)
		{
			string code = stack.Attributes.GetString("type", "");
			return GetTypeProps(code, stack, null)?.LightHsv ?? noLight;
		}
		BEBehaviorShapeFromAttributes bEBehaviorShapeFromAttributes = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorShapeFromAttributes>();
		return GetTypeProps(bEBehaviorShapeFromAttributes?.Type, null, bEBehaviorShapeFromAttributes)?.LightHsv ?? noLight;
	}

	public override int GetColor(ICoreClientAPI capi, BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		IShapeTypeProps typeProps = GetTypeProps(bEBehavior?.Type, null, bEBehavior);
		if (typeProps?.TexPos != null)
		{
			return typeProps.TexPos.AvgColor;
		}
		return base.GetColor(capi, pos);
	}

	public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		return GetTypeProps(bEBehavior?.Type, null, bEBehavior)?.CanAttachBlockAt(new Vec3f(bEBehavior.rotateX, bEBehavior.rotateY, bEBehavior.rotateZ), blockFace, attachmentArea) ?? base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
	}

	public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		IShapeTypeProps typeProps = GetTypeProps(bEBehavior?.Type, null, bEBehavior);
		if (typeProps?.TexPos != null)
		{
			return typeProps.TexPos.RndColors[(rndIndex < 0) ? capi.World.Rand.Next(typeProps.TexPos.RndColors.Length) : rndIndex];
		}
		return base.GetRandomColor(capi, pos, facing, rndIndex);
	}

	public override string GetHeldItemName(ItemStack itemStack)
	{
		string text = itemStack.Attributes.GetString("type", "");
		return Lang.GetMatching(Code.Domain + ":" + ClassType + "-" + text.Replace("/", "-"));
	}

	public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior != null && bEBehavior.overrideTextureCode != null)
		{
			string matchingIfExists = Lang.GetMatchingIfExists(bEBehavior.GetFullCode() + "-" + bEBehavior.overrideTextureCode);
			if (matchingIfExists != null)
			{
				return matchingIfExists;
			}
		}
		return Lang.GetMatching(bEBehavior?.GetFullCode() ?? "Unknown");
	}

	public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		return base.GetPlacedBlockInfo(world, pos, forPlayer) + Lang.GetMatchingIfExists(Code.Domain + ":" + ClassType + "desc-" + bEBehavior?.Type?.Replace("/", "-"));
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		string text = inSlot.Itemstack.Attributes.GetString("type", "");
		string ifExists = Lang.GetIfExists(Code.Domain + ":" + ClassType + "desc-" + text.Replace("/", "-"));
		if (ifExists != null)
		{
			dsc.AppendLine(ifExists);
		}
	}

	public void Rotate(EntityAgent byEntity, BlockSelection blockSel, int dir)
	{
		GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position).Rotate(byEntity, blockSel, dir);
	}

	public void FlipTexture(BlockPos pos, string newTextureCode)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		bEBehavior.overrideTextureCode = newTextureCode;
		bEBehavior.loadMesh();
		bEBehavior.Blockentity.MarkDirty(redrawOnClient: true);
	}

	public Vintagestory.API.Datastructures.OrderedDictionary<string, CompositeTexture> GetAvailableTextures(BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		IShapeTypeProps typeProps = GetTypeProps(bEBehavior?.Type, null, bEBehavior);
		if (typeProps != null && typeProps.TextureFlipGroupCode != null)
		{
			return OverrideTextureGroups[typeProps.TextureFlipGroupCode];
		}
		return null;
	}

	public virtual string BaseCodeForName()
	{
		return Code.Domain + ":" + ClassType + "-";
	}

	public virtual MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
	{
		string code = slot.Itemstack.Attributes.GetString("type", "");
		return GetOrCreateMesh(GetTypeProps(code, slot.Itemstack, null));
	}

	public virtual string GetMeshCacheKey(ItemSlot slot)
	{
		string code = slot.Itemstack.Attributes.GetString("type", "");
		IShapeTypeProps typeProps = GetTypeProps(code, slot.Itemstack, null);
		return slot.Itemstack.Collectible?.ToString() + "-" + typeProps.Code;
	}
}
