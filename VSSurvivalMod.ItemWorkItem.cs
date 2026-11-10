using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class ItemWorkItem : Item, IAnvilWorkable
{
	private static int nextMeshRefId;

	public bool isBlisterSteel;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		isBlisterSteel = Variant["metal"] == "blistersteel";
	}

	public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
	{
		if (!itemstack.Attributes.HasAttribute("voxels"))
		{
			MultiTextureMeshRef orCreate = ObjectCacheUtil.GetOrCreate(capi, "clearWorkItem" + Variant["metal"], () =>
			{
				byte[,,] voxels = new byte[16, 6, 16];
				ItemIngot.CreateVoxelsFromIngot(capi, ref voxels);
				MeshData data = GenMesh(capi, itemstack, voxels);
				return capi.Render.UploadMultiTextureMesh(data);
			});
			renderinfo.ModelRef = orCreate;
			base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
			return;
		}
		int num = itemstack.Attributes.GetInt("meshRefId", -1);
		if (num == -1)
		{
			num = ++nextMeshRefId;
		}
		renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, num.ToString() ?? "", () =>
		{
			byte[,,] voxels = GetVoxels(itemstack);
			MeshData data = GenMesh(capi, itemstack, voxels);
			return capi.Render.UploadMultiTextureMesh(data);
		});
		itemstack.Attributes.SetInt("meshRefId", num);
		base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
	}

	public static MeshData GenMesh(ICoreClientAPI capi, ItemStack workitemStack, byte[,,] voxels)
	{
		if (workitemStack == null)
		{
			return null;
		}
		MeshData meshData = new MeshData(24, 36);
		meshData.CustomBytes = new CustomMeshDataPartByte
		{
			Conversion = DataConversion.NormalizedFloat,
			Count = meshData.VerticesCount,
			InterleaveSizes = new int[1] { 1 },
			Instanced = false,
			InterleaveOffsets = new int[1],
			InterleaveStride = 1,
			Values = new byte[meshData.VerticesCount]
		};
		TextureAtlasPosition textureAtlasPosition;
		TextureAtlasPosition position;
		if (workitemStack.Collectible.FirstCodePart() == "ironbloom")
		{
			textureAtlasPosition = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("anvil-copper")), "ironbloom");
			position = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("ingotpile")), "iron");
		}
		else
		{
			position = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("ingotpile")), workitemStack.Collectible.Variant["metal"]);
			textureAtlasPosition = position;
		}
		MeshData cubeOnlyScaleXyz = CubeMeshUtil.GetCubeOnlyScaleXyz(1f / 32f, 1f / 32f, new Vec3f(1f / 32f, 1f / 32f, 1f / 32f));
		CubeMeshUtil.SetXyzFacesAndPacketNormals(cubeOnlyScaleXyz);
		cubeOnlyScaleXyz.CustomBytes = new CustomMeshDataPartByte
		{
			Conversion = DataConversion.NormalizedFloat,
			Count = cubeOnlyScaleXyz.VerticesCount,
			Values = new byte[cubeOnlyScaleXyz.VerticesCount]
		};
		for (int i = 0; i < 6; i++)
		{
			cubeOnlyScaleXyz.AddTextureId(position.atlasTextureId);
		}
		cubeOnlyScaleXyz.XyzFaces = (byte[])CubeMeshUtil.CubeFaceIndices.Clone();
		cubeOnlyScaleXyz.XyzFacesCount = 6;
		cubeOnlyScaleXyz.Rgba.Fill(byte.MaxValue);
		MeshData meshData2 = cubeOnlyScaleXyz.Clone();
		for (int j = 0; j < cubeOnlyScaleXyz.Uv.Length; j++)
		{
			if (j % 2 > 0)
			{
				cubeOnlyScaleXyz.Uv[j] = position.y1 + cubeOnlyScaleXyz.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Height;
				meshData2.Uv[j] = textureAtlasPosition.y1 + meshData2.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Height;
			}
			else
			{
				cubeOnlyScaleXyz.Uv[j] = position.x1 + cubeOnlyScaleXyz.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Width;
				meshData2.Uv[j] = textureAtlasPosition.x1 + meshData2.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Width;
			}
		}
		MeshData meshData3 = cubeOnlyScaleXyz.Clone();
		MeshData meshData4 = meshData2.Clone();
		for (int k = 0; k < 16; k++)
		{
			for (int l = 0; l < 6; l++)
			{
				for (int m = 0; m < 16; m++)
				{
					EnumVoxelMaterial enumVoxelMaterial = (EnumVoxelMaterial)voxels[k, l, m];
					if (enumVoxelMaterial != EnumVoxelMaterial.Empty)
					{
						float num = (float)k / 16f;
						float num2 = 0.625f + (float)l / 16f;
						float num3 = (float)m / 16f;
						MeshData meshData5 = ((enumVoxelMaterial == EnumVoxelMaterial.Metal) ? cubeOnlyScaleXyz : meshData2);
						MeshData meshData6 = ((enumVoxelMaterial == EnumVoxelMaterial.Metal) ? meshData3 : meshData4);
						for (int n = 0; n < meshData5.xyz.Length; n += 3)
						{
							meshData6.xyz[n] = num + meshData5.xyz[n];
							meshData6.xyz[n + 1] = num2 + meshData5.xyz[n + 1];
							meshData6.xyz[n + 2] = num3 + meshData5.xyz[n + 2];
						}
						float num4 = 32f / (float)capi.BlockTextureAtlas.Size.Width;
						float num5 = num * num4;
						float num6 = num2 * 32f / (float)capi.BlockTextureAtlas.Size.Width;
						float num7 = num3 * num4;
						for (int num8 = 0; num8 < meshData5.Uv.Length; num8 += 2)
						{
							meshData6.Uv[num8] = meshData5.Uv[num8] + GameMath.Mod(num5 + num6, num4);
							meshData6.Uv[num8 + 1] = meshData5.Uv[num8 + 1] + GameMath.Mod(num7 + num6, num4);
						}
						for (int num9 = 0; num9 < meshData6.CustomBytes.Values.Length; num9++)
						{
							byte b = (byte)GameMath.Clamp(10 * (Math.Abs(k - 8) + Math.Abs(m - 8) + Math.Abs(l - 2)), 100, 250);
							meshData6.CustomBytes.Values[num9] = (byte)((enumVoxelMaterial != EnumVoxelMaterial.Metal) ? b : 0);
						}
						meshData.AddMeshData(meshData6);
					}
				}
			}
		}
		return meshData;
	}

	public virtual int VoxelCountForHandbook(ItemStack stack)
	{
		return GetVoxels(stack).Cast<byte>().Count((byte voxel) => voxel == 1);
	}

	public static byte[,,] GetVoxels(ItemStack workitemStack)
	{
		return BlockEntityAnvil.deserializeVoxels(workitemStack.Attributes.GetBytes("voxels"));
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		int recipeId = inSlot.Itemstack.Attributes.GetInt("selectedRecipeId");
		SmithingRecipe smithingRecipe = api.GetSmithingRecipes().FirstOrDefault((SmithingRecipe r) => r.RecipeId == recipeId);
		if (smithingRecipe == null)
		{
			dsc.AppendLine("Unknown work item");
			return;
		}
		dsc.AppendLine(Lang.Get("Unfinished {0}", smithingRecipe.Output.ResolvedItemstack.GetName()));
	}

	public int GetRequiredAnvilTier(ItemStack stack)
	{
		string key = Variant["metal"];
		int num = 0;
		if (api.ModLoader.GetModSystem<SurvivalCoreSystem>().metalsByCode.TryGetValue(key, out var value))
		{
			num = value.Tier - 1;
		}
		JsonObject attributes = stack.Collectible.Attributes;
		if (attributes != null && attributes["requiresAnvilTier"].Exists)
		{
			num = stack.Collectible.Attributes["requiresAnvilTier"].AsInt(num);
		}
		return num;
	}

	public List<SmithingRecipe> GetMatchingRecipes(ItemStack stack)
	{
		stack = GetBaseMaterial(stack);
		return (from r in api.GetSmithingRecipes()
			where r.Ingredient.SatisfiesAsIngredient(stack)
			orderby r.Output.ResolvedItemstack.Collectible.Code
			select r).ToList();
	}

	public bool CanWork(ItemStack stack)
	{
		float temperature = stack.Collectible.GetTemperature(api.World, stack);
		float meltingPoint = stack.Collectible.GetMeltingPoint(api.World, null, new DummySlot(stack));
		JsonObject attributes = stack.Collectible.Attributes;
		if (attributes != null && attributes["workableTemperature"].Exists)
		{
			return stack.Collectible.Attributes["workableTemperature"].AsFloat(meltingPoint / 2f) <= temperature;
		}
		return temperature >= meltingPoint / 2f;
	}

	public ItemStack TryPlaceOn(ItemStack stack, BlockEntityAnvil beAnvil)
	{
		if (beAnvil.WorkItemStack != null)
		{
			return null;
		}
		try
		{
			beAnvil.Voxels = BlockEntityAnvil.deserializeVoxels(stack.Attributes.GetBytes("voxels"));
			beAnvil.SelectedRecipeId = stack.Attributes.GetInt("selectedRecipeId");
		}
		catch (Exception)
		{
		}
		return stack.Clone();
	}

	public ItemStack GetBaseMaterial(ItemStack stack)
	{
		return new ItemStack(api.World.GetItem(AssetLocation.Create("ingot-" + Variant["metal"], Attributes?["baseMaterialDomain"].AsString("game"))) ?? throw new Exception(string.Format("Base material for {0} not found, there is no item with code 'ingot-{1}'", stack.Collectible.Code, Variant["metal"])));
	}

	public EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
	{
		if (beAnvil.SelectedRecipe.Name.Path == "plate" || beAnvil.SelectedRecipe.Name.Path == "blistersteel")
		{
			return EnumHelveWorkableMode.TestSufficientVoxelsWorkable;
		}
		return EnumHelveWorkableMode.NotWorkable;
	}
}
