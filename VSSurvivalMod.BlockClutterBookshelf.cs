using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class BlockClutterBookshelf : BlockShapeFromAttributes
{
	private string classtype;

	public Vintagestory.API.Datastructures.OrderedDictionary<string, BookShelfVariantGroup> variantGroupsByCode = new Vintagestory.API.Datastructures.OrderedDictionary<string, BookShelfVariantGroup>();

	public string basePath;

	private AssetLocation woodbackPanelShapePath;

	public override string ClassType => classtype;

	public override IEnumerable<IShapeTypeProps> AllTypes
	{
		get
		{
			List<IShapeTypeProps> list = new List<IShapeTypeProps>();
			foreach (BookShelfVariantGroup value in variantGroupsByCode.Values)
			{
				list.AddRange(value.typesByCode.Values);
			}
			return list;
		}
	}

	public override void LoadTypes()
	{
		variantGroupsByCode = Attributes["variantGroups"].AsObject<Vintagestory.API.Datastructures.OrderedDictionary<string, BookShelfVariantGroup>>();
		basePath = Attributes["shapeBasePath"].AsString();
		classtype = Attributes["classtype"].AsString("bookshelf");
		List<JsonItemStack> list = new List<JsonItemStack>();
		woodbackPanelShapePath = AssetLocation.Create("shapes/" + basePath + "/" + Attributes["woodbackPanelShapePath"].AsString() + ".json", Code.Domain);
		foreach (KeyValuePair<string, BookShelfVariantGroup> item in variantGroupsByCode)
		{
			item.Value.block = this;
			BookShelfTypeProps[] types;
			if (item.Value.DoubleSided)
			{
				JsonItemStack jsonItemStack = new JsonItemStack
				{
					Code = Code,
					Type = EnumItemClass.Block,
					Attributes = new JsonObject(JToken.Parse("{ \"variant\": \"" + item.Key + "\" }"))
				};
				jsonItemStack.Resolve(api.World, ClassType + " type");
				list.Add(jsonItemStack);
				types = item.Value.types;
				foreach (BookShelfTypeProps bookShelfTypeProps in types)
				{
					item.Value.typesByCode[bookShelfTypeProps.Code] = bookShelfTypeProps;
					bookShelfTypeProps.Variant = item.Key;
					bookShelfTypeProps.group = item.Value;
				}
				item.Value.types = null;
				continue;
			}
			types = item.Value.types;
			foreach (BookShelfTypeProps bookShelfTypeProps2 in types)
			{
				item.Value.typesByCode[bookShelfTypeProps2.Code] = bookShelfTypeProps2;
				bookShelfTypeProps2.Variant = item.Key;
				bookShelfTypeProps2.group = item.Value;
				JsonItemStack jsonItemStack2 = new JsonItemStack();
				jsonItemStack2.Code = Code;
				jsonItemStack2.Type = EnumItemClass.Block;
				jsonItemStack2.Attributes = new JsonObject(JToken.Parse("{ \"type\": \"" + bookShelfTypeProps2.Code + "\", \"variant\": \"" + bookShelfTypeProps2.Variant + "\" }"));
				JsonItemStack jsonItemStack3 = jsonItemStack2;
				jsonItemStack3.Resolve(api.World, ClassType + " type");
				list.Add(jsonItemStack3);
			}
			item.Value.types = null;
		}
		CreativeInventoryStacks = new CreativeTabAndStackList[1]
		{
			new CreativeTabAndStackList
			{
				Stacks = list.ToArray(),
				Tabs = new string[2] { "general", "clutter" }
			}
		};
	}

	public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null)
	{
		BEBehaviorClutterBookshelf bEBehavior = GetBEBehavior<BEBehaviorClutterBookshelf>(pos);
		if (bEBehavior != null && bEBehavior.Variant != null)
		{
			variantGroupsByCode.TryGetValue(bEBehavior.Variant, out var value);
			if (value != null && value.DoubleSided)
			{
				int num = (int)(bEBehavior.rotateY * (180f / (float)Math.PI));
				if (num < 0)
				{
					num += 360;
				}
				switch (num)
				{
				case 0:
				case 180:
					return blockFace.IsAxisWE;
				case 90:
				case 270:
					return blockFace.IsAxisNS;
				}
			}
		}
		return base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
	}

	public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
	{
		ItemStack itemStack = base.OnPickBlock(world, pos);
		BEBehaviorClutterBookshelf bEBehavior = GetBEBehavior<BEBehaviorClutterBookshelf>(pos);
		if (bEBehavior != null)
		{
			itemStack.Attributes.SetString("type", bEBehavior.Type);
			itemStack.Attributes.SetString("variant", bEBehavior.Variant);
		}
		return itemStack;
	}

	public override IShapeTypeProps GetTypeProps(string code, ItemStack stack, BEBehaviorShapeFromAttributes be)
	{
		if (code == null)
		{
			return null;
		}
		string text = ((stack != null) ? stack.Attributes.GetString("variant") : (be as BEBehaviorClutterBookshelf)?.Variant);
		if (text == null)
		{
			return null;
		}
		if (variantGroupsByCode.TryGetValue(text, out var value))
		{
			if (value.DoubleSided)
			{
				string text2;
				string text3;
				if (be != null)
				{
					text2 = (be as BEBehaviorClutterBookshelf).Type;
					text3 = (be as BEBehaviorClutterBookshelf).Type2;
				}
				else
				{
					if (!stack.Attributes.HasAttribute("type1"))
					{
						stack.Attributes.SetString("type1", RandomType(text));
						stack.Attributes.SetString("type2", RandomType(text));
					}
					text2 = stack.Attributes.GetString("type1");
					text3 = stack.Attributes.GetString("type2");
				}
				if (!value.typesByCode.TryGetValue(text2, out var value2))
				{
					value2 = value.typesByCode.First((KeyValuePair<string, BookShelfTypeProps> ele) => true).Value;
				}
				if (!value.typesByCode.TryGetValue(text3, out var value3))
				{
					value3 = value2;
				}
				BookShelfTypeProps bookShelfTypeProps = new BookShelfTypeProps();
				bookShelfTypeProps.group = value;
				bookShelfTypeProps.Code = text + "-" + text2 + "-" + text3;
				bookShelfTypeProps.Type1 = text2;
				bookShelfTypeProps.Type2 = text3;
				bookShelfTypeProps.ShapeResolved = value2.ShapeResolved;
				bookShelfTypeProps.ShapeResolved2 = value3.ShapeResolved;
				bookShelfTypeProps.Variant = text;
				bookShelfTypeProps.TexPos = value.texPos;
				return bookShelfTypeProps;
			}
			value.typesByCode.TryGetValue(code, out var value4);
			return value4;
		}
		return null;
	}

	public override MeshData GetOrCreateMesh(IShapeTypeProps cprops, ITexPositionSource overrideTexturesource = null, string overrideTextureCode = null, bool forLOD2 = false)
	{
		Dictionary<string, MeshData> dictionary = (forLOD2 ? meshDictionaryLOD2 : meshDictionary);
		ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
		if (cprops.ShapeLOD2Resolved == null)
		{
			forLOD2 = false;
		}
		BookShelfTypeProps bookShelfTypeProps = cprops as BookShelfTypeProps;
		if (overrideTexturesource == null && dictionary.TryGetValue(bookShelfTypeProps.HashKey, out var value))
		{
			return value;
		}
		value = new MeshData(4, 3);
		Shape shape = ((!forLOD2) ? cprops.ShapeResolved : cprops.ShapeLOD2Resolved?.Clone());
		if (shape == null)
		{
			return value;
		}
		ITexPositionSource texPositionSource = overrideTexturesource;
		ShapeTextureSource shapeTextureSource = null;
		if (forLOD2 || texPositionSource == null)
		{
			string text = cprops.ShapePath.ToString();
			shapeTextureSource = new ShapeTextureSource(coreClientAPI, shape, forLOD2 ? Lod2Shape.Base.Path : text);
			texPositionSource = shapeTextureSource;
			if (blockTextures != null && !forLOD2)
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
			if (forLOD2)
			{
				AssetLocation value2 = null;
				SetLOD2Texture(coreClientAPI, shapeTextureSource, shape, "front", new AssetLocation("game", "block/lod2/clutter/" + cprops.Code), null);
				ShapeElement shapeElement = FindElement(cprops.ShapeResolved.Elements, "e");
				object obj;
				if (shapeElement == null)
				{
					obj = null;
				}
				else
				{
					ShapeElementFace[]? facesResolved = shapeElement.FacesResolved;
					obj = ((facesResolved != null) ? facesResolved[1] : null);
				}
				ShapeElementFace shapeElementFace = (ShapeElementFace)obj;
				string text2 = shapeElementFace?.Texture;
				if ((text2 == null || !cprops.ShapeResolved.Textures.TryGetValue(text2, out value2)) && !cprops.ShapeResolved.Textures.TryGetValue("shelf-ruined", out value2))
				{
					cprops.ShapeResolved.Textures.TryGetValue("bookshelf", out value2);
				}
				SetLOD2Texture(coreClientAPI, shapeTextureSource, shape, "bookshelf", value2, shapeElementFace);
				ShapeElement shapeElement2 = FindElement(cprops.ShapeResolved.Elements, "n");
				object obj2;
				if (shapeElement2 == null)
				{
					obj2 = null;
				}
				else
				{
					ShapeElementFace[]? facesResolved2 = shapeElement2.FacesResolved;
					obj2 = ((facesResolved2 != null) ? facesResolved2[0] : null);
				}
				shapeElementFace = (ShapeElementFace)obj2;
				text2 = shapeElementFace?.Texture;
				if ((text2 == null || !cprops.ShapeResolved.Textures.TryGetValue(text2, out value2)) && !cprops.ShapeResolved.Textures.TryGetValue("sides-ruined", out value2))
				{
					cprops.ShapeResolved.Textures.TryGetValue("sides", out value2);
				}
				SetLOD2Texture(coreClientAPI, shapeTextureSource, shape, "back", value2, shapeElementFace);
			}
		}
		coreClientAPI.Tesselator.TesselateShape(blockForLogging, shape, out value, texPositionSource, null, 0, 0, 0);
		if (bookShelfTypeProps.Variant == "full" || bookShelfTypeProps.group.DoubleSided)
		{
			value.Translate(0f, 0f, 0.5f);
			shape = ((!(bookShelfTypeProps.Variant == "full")) ? bookShelfTypeProps.ShapeResolved2 : coreClientAPI.Assets.TryGet(woodbackPanelShapePath)?.ToObject<Shape>());
			texPositionSource = new ShapeTextureSource(coreClientAPI, shape, ((bookShelfTypeProps.Variant == "full") ? woodbackPanelShapePath : bookShelfTypeProps.ShapePath2).ToString());
			if (blockTextures != null && shapeTextureSource != null)
			{
				foreach (KeyValuePair<string, CompositeTexture> blockTexture2 in blockTextures)
				{
					if (blockTexture2.Value.Baked == null)
					{
						blockTexture2.Value.Bake(coreClientAPI.Assets);
					}
					shapeTextureSource.textures[blockTexture2.Key] = blockTexture2.Value;
				}
			}
			coreClientAPI.Tesselator.TesselateShape(blockForLogging, shape, out var modeldata, texPositionSource, null, 0, 0, 0);
			modeldata.Rotate(0f, (float)Math.PI, 0f).Translate(0f, 0f, -0.5f);
			value.AddMeshData(modeldata);
		}
		if (cprops.TexPos == null)
		{
			cprops.TexPos = (texPositionSource as ShapeTextureSource)?.firstTexPos;
			cprops.TexPos.RndColors = new int[30];
		}
		if (bookShelfTypeProps.group.texPos == null)
		{
			bookShelfTypeProps.group.texPos = cprops.TexPos;
		}
		if (overrideTexturesource == null)
		{
			dictionary[bookShelfTypeProps.HashKey] = value;
		}
		return value;
	}

	private void SetLOD2Texture(ICoreClientAPI capi, ShapeTextureSource stexSource, Shape shape, string texName, AssetLocation sideTex, ShapeElementFace face)
	{
		if (!(sideTex != null))
		{
			return;
		}
		CompositeTexture compositeTexture = new CompositeTexture(sideTex);
		compositeTexture.Bake(capi.Assets);
		stexSource.textures[texName] = compositeTexture;
		if (face == null || shape == null)
		{
			return;
		}
		ShapeElementFace[] array = FindElement(shape.Elements, "bottom")?.FacesResolved;
		if (array == null)
		{
			return;
		}
		ShapeElementFace[] array2 = array;
		foreach (ShapeElementFace shapeElementFace in array2)
		{
			if (shapeElementFace.Texture == texName)
			{
				shapeElementFace.Uv = face.Uv;
			}
		}
	}

	private ShapeElement FindElement(ShapeElement[] baseElements, string name)
	{
		if (baseElements == null || baseElements.Length == 0)
		{
			return null;
		}
		ShapeElement shapeElement = baseElements[0];
		if (shapeElement.Name == name)
		{
			return shapeElement;
		}
		if (shapeElement.Children == null)
		{
			return null;
		}
		ShapeElement[] children = shapeElement.Children;
		foreach (ShapeElement shapeElement2 in children)
		{
			if (shapeElement2 == null)
			{
				continue;
			}
			if (shapeElement2.Name == name)
			{
				return shapeElement2;
			}
			if (shapeElement2.Children == null)
			{
				continue;
			}
			ShapeElement[] children2 = shapeElement2.Children;
			foreach (ShapeElement shapeElement3 in children2)
			{
				if (shapeElement3.Name == name)
				{
					return shapeElement3;
				}
			}
		}
		return null;
	}

	public string RandomType(string variant)
	{
		if (variantGroupsByCode == null)
		{
			return null;
		}
		BookShelfVariantGroup bookShelfVariantGroup = variantGroupsByCode[variant];
		int index = api.World.Rand.Next(bookShelfVariantGroup.typesByCode.Count);
		return bookShelfVariantGroup.typesByCode.GetKeyAtIndex(index);
	}

	public override string GetHeldItemName(ItemStack itemStack)
	{
		string text = itemStack.Attributes.GetString("type", "");
		string text2 = itemStack.Attributes.GetString("variant", "");
		return Lang.GetMatching(Code.Domain + ":" + ((text.Length == 0) ? ("bookshelf-" + text2) : text.Replace("/", "-")));
	}

	public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
	{
		BEBehaviorClutterBookshelf bEBehavior = GetBEBehavior<BEBehaviorClutterBookshelf>(pos);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(Lang.GetMatching(Code.Domain + ":" + (bEBehavior?.Type?.Replace("/", "-") ?? "unknown")));
		bEBehavior?.GetBlockInfo(forPlayer, stringBuilder);
		stringBuilder.AppendLine();
		BlockBehavior[] blockBehaviors = BlockBehaviors;
		foreach (BlockBehavior blockBehavior in blockBehaviors)
		{
			stringBuilder.Append(blockBehavior.GetPlacedBlockInfo(world, pos, forPlayer));
		}
		return stringBuilder.ToString();
	}

	public override string BaseCodeForName()
	{
		return Code.Domain + ":";
	}
}
