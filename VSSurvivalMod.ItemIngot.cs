using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public class ItemIngot : Item, IAnvilWorkable
{
	private bool isBlisterSteel;

	public static int VoxelCount = 42;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		isBlisterSteel = Variant["metal"] == "blistersteel";
	}

	public string GetMetalType()
	{
		return LastCodePart();
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
		if (!CanWork(stack))
		{
			return null;
		}
		Item item = api.World.GetItem(new AssetLocation("workitem-" + Variant["metal"]));
		if (item == null)
		{
			return null;
		}
		ItemStack itemStack = new ItemStack(item);
		itemStack.Collectible.SetTemperature(api.World, itemStack, stack.Collectible.GetTemperature(api.World, stack));
		if (beAnvil.WorkItemStack == null)
		{
			CreateVoxelsFromIngot(api, ref beAnvil.Voxels, isBlisterSteel);
		}
		else
		{
			if (isBlisterSteel)
			{
				return null;
			}
			if (!string.Equals(beAnvil.WorkItemStack.Collectible.Variant["metal"], stack.Collectible.Variant["metal"]))
			{
				if (api.Side == EnumAppSide.Client)
				{
					(api as ICoreClientAPI).TriggerIngameError(this, "notequal", Lang.Get("Must be the same metal to add voxels"));
				}
				return null;
			}
			if (AddVoxelsFromIngot(ref beAnvil.Voxels) == 0)
			{
				if (api.Side == EnumAppSide.Client)
				{
					(api as ICoreClientAPI).TriggerIngameError(this, "requireshammering", Lang.Get("Try hammering down before adding additional voxels"));
				}
				return null;
			}
		}
		return itemStack;
	}

	public virtual int VoxelCountForHandbook(ItemStack stack)
	{
		return VoxelCount;
	}

	public static void CreateVoxelsFromIngot(ICoreAPI api, ref byte[,,] voxels, bool isBlisterSteel = false)
	{
		voxels = new byte[16, 6, 16];
		for (int i = 0; i < 7; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				for (int k = 0; k < 3; k++)
				{
					voxels[4 + i, j, 6 + k] = 1;
					if (isBlisterSteel)
					{
						if (api.World.Rand.NextDouble() < 0.5)
						{
							voxels[4 + i, j + 1, 6 + k] = 1;
						}
						if (api.World.Rand.NextDouble() < 0.5)
						{
							voxels[4 + i, j + 1, 6 + k] = 2;
						}
					}
				}
			}
		}
	}

	public static int AddVoxelsFromIngot(ref byte[,,] voxels)
	{
		int num = 0;
		for (int i = 0; i < 7; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				int k = 0;
				int num2 = 0;
				for (; k < 6; k++)
				{
					if (num2 >= 2)
					{
						break;
					}
					if (voxels[4 + i, k, 6 + j] == 0)
					{
						voxels[4 + i, k, 6 + j] = 1;
						num2++;
						num++;
					}
				}
			}
		}
		return num;
	}

	public ItemStack GetBaseMaterial(ItemStack stack)
	{
		return stack;
	}

	public EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
	{
		return EnumHelveWorkableMode.NotWorkable;
	}
}
