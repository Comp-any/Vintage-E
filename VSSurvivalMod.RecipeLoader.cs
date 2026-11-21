using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.ServerMods;

public sealed class RecipeLoader : ModSystem
{
	public override double ExecuteOrder()
	{
		return 1.0;
	}

	public override bool ShouldLoad(EnumAppSide forSide)
	{
		return forSide == EnumAppSide.Server;
	}

	public override void AssetsLoaded(ICoreAPI api)
	{
		ICoreServerAPI serverApi = api as ICoreServerAPI;
		if (serverApi != null)
		{
			bool classExclusiveRecipes = serverApi.World.Config.GetBool("classExclusiveRecipes", defaultValue: true);
			LoadAlloyRecipes(serverApi);
			LoadRecipes<GridRecipe>(serverApi, "grid", "recipes/grid", classExclusiveRecipes, (IRecipeBase r) =>
			{
				serverApi.RegisterCraftingRecipe(r as GridRecipe);
			});
			api.Logger.StoryEvent(Lang.Get("Grand inventions..."));
			LoadRecipes<SmithingRecipe>(serverApi, "smithing", "recipes/smithing", classExclusiveRecipes, (IRecipeBase r) =>
			{
				serverApi.RegisterSmithingRecipe(r as SmithingRecipe);
			});
			serverApi.World.Logger.StoryEvent(Lang.Get("Burning sparks..."));
			LoadRecipes<ClayFormingRecipe>(serverApi, "clay forming", "recipes/clayforming", classExclusiveRecipes, (IRecipeBase r) =>
			{
				serverApi.RegisterClayFormingRecipe(r as ClayFormingRecipe);
			});
			serverApi.World.Logger.StoryEvent(Lang.Get("Molded forms..."));
			LoadRecipes<KnappingRecipe>(serverApi, "knapping", "recipes/knapping", classExclusiveRecipes, (IRecipeBase r) =>
			{
				serverApi.RegisterKnappingRecipe(r as KnappingRecipe);
			});
			serverApi.World.Logger.StoryEvent(Lang.Get("Simple tools..."));
			LoadRecipes<BarrelRecipe>(serverApi, "barrel recipe", "recipes/barrel", classExclusiveRecipes, (IRecipeBase r) =>
			{
				serverApi.RegisterBarrelRecipe(r as BarrelRecipe);
			});
		}
	}

	public static void LoadAlloyRecipes(ICoreServerAPI api)
	{
		Dictionary<AssetLocation, AlloyRecipe> many = api.Assets.GetMany<AlloyRecipe>(api.Server.Logger, "recipes/alloy");
		foreach (var (assetLocation2, alloyRecipe2) in many)
		{
			if (alloyRecipe2.Enabled)
			{
				alloyRecipe2.Resolve(api.World, "alloy recipe " + assetLocation2);
				api.RegisterMetalAlloy(alloyRecipe2);
			}
		}
		api.World.Logger.Event("{0} metal alloys loaded", many.Count);
		api.World.Logger.StoryEvent(Lang.Get("Glimmers in the soil..."));
	}

	public static void LoadRecipes<TRecipe>(ICoreServerAPI api, string name, string path, bool classExclusiveRecipes, Action<IRecipeBase> registerDelegate) where TRecipe : IRecipeBase
	{
		Dictionary<AssetLocation, JToken> many = api.Assets.GetMany<JToken>(api.Logger, path);
		int num = 0;
		int loaded = 0;
		int failedResolveCount = 0;
		foreach (var (assetLocation2, jToken2) in many)
		{
			if (jToken2 is JObject token)
			{
				TRecipe val = token.ToObject<TRecipe>(assetLocation2.Domain);
				if (val == null)
				{
					api.Logger.Error($"Failed to parse {name} recipe: {assetLocation2}");
				}
				else
				{
					LoadRecipe(api, assetLocation2, val, classExclusiveRecipes, registerDelegate, ref loaded, ref failedResolveCount);
					num++;
				}
			}
			else
			{
				if (!(jToken2 is JArray jArray))
				{
					continue;
				}
				foreach (JToken item in jArray)
				{
					TRecipe val2 = item.ToObject<TRecipe>(assetLocation2.Domain);
					if (val2 == null)
					{
						api.Logger.Error($"Failed to parse {name} recipe: {assetLocation2}");
					}
					else
					{
						LoadRecipe(api, assetLocation2, val2, classExclusiveRecipes, registerDelegate, ref loaded, ref failedResolveCount);
						num++;
					}
				}
			}
		}
		if (failedResolveCount > 0)
		{
			api.Logger.Event($"{num} {name} recipes loaded from {many.Count} files, failed to resolve {failedResolveCount} recipes");
		}
		else
		{
			api.Logger.Event($"{num} {name} recipes loaded from {many.Count} files");
		}
		RecipeBase.CollectiblePreSearchResultsCache.Clear();
	}

	public static void LoadRecipe(ICoreServerAPI api, AssetLocation assetLocation, IRecipeBase recipe, bool classExclusiveRecipes, Action<IRecipeBase> registerDelegate, ref int loaded, ref int failedResolveCount)
	{
		if (!recipe.Enabled)
		{
			return;
		}
		if (!classExclusiveRecipes)
		{
			recipe.RequiresTrait = null;
		}
		if (recipe.Name == null)
		{
			recipe.Name = assetLocation;
		}
		recipe.OnParsed(api.World);
		foreach (IRecipeBase item in recipe.GenerateRecipesForAllIngredientCombinations(api.World))
		{
			if (item.Resolve(api.World, "RecipeLoader"))
			{
				registerDelegate(item);
				loaded++;
			}
			else
			{
				failedResolveCount++;
			}
		}
	}
}
