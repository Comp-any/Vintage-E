using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common;

/// <summary>
/// Represents a crafting recipe to be made on the crafting grid.
/// </summary>
/// <example><code language="json">
///             {
///             	"ingredientPattern": "GS,S_",
///             	"ingredients": {
///             		"G": {
///             			"type": "item",
///             			"code": "drygrass"
///             		},
///             		"S": {
///             			"type": "item",
///             			"code": "stick"
///             		}
///             	},
///             	"width": 2,
///             	"height": 2,
///             	"output": {
///             		"type": "item",
///             		"code": "firestarter"
///             	}
///             }
/// </code></example>
[DocumentAsJson]
public class GridRecipe : RecipeBase, IByteSerializable, IConcreteCloneable<GridRecipe>, ICloneable
{
	/// <summary>
	/// The resulting stack when the recipe is created.
	/// </summary>
	[DocumentAsJson("Required", "", false)]
	public CraftingRecipeIngredient? Output { get; set; }

	/// <summary>
	/// The recipes ingredients in any order, including the code used in the ingredient pattern.
	/// <br />Note: from game version 1.20.4, this becomes <b>null on server-side</b> after completion of recipe resolving during server start-up phase
	/// <br />From game version 1.22.0, this is always null client-side. Use GridRecipe.GetIngredientById() if you need to find ingredients by their id-code (which should normally match the code used in the ingredient pattern)
	/// </summary>
	[DocumentAsJson("Required", "", false)]
	public Dictionary<string, CraftingRecipeIngredient>? Ingredients { get; set; }

	/// <summary>
	/// The pattern of the ingredient. Order for a 3x3 recipe:<br />
	/// 1 2 3<br />
	/// 4 5 6<br />
	/// 7 8 9<br />
	/// Order for a 2x2 recipe:<br />
	/// 1 2<br />
	/// 3 4<br />
	/// Commas separate each horizontal row, and an underscore ( _ ) marks a space as empty.
	/// <br />Note: from game version 1.20.4, this becomes <b>null on server-side</b> after completion of recipe resolving during server start-up phase
	/// </summary>
	[DocumentAsJson("Required", "", false)]
	public string? IngredientPattern { get; set; }

	/// <summary>
	/// Required grid width for crafting this recipe 
	/// </summary>
	[DocumentAsJson("Recommended", "3", false)]
	public int Width { get; set; } = 3;

	/// <summary>
	/// Required grid height for crafting this recipe 
	/// </summary>
	[DocumentAsJson("Recommended", "3", false)]
	public int Height { get; set; } = 3;

	/// <summary>
	/// Info used by the handbook. By default, all recipes for an object will appear in a single preview. This allows you to split grid recipe previews into multiple.
	/// </summary>
	[DocumentAsJson("Optional", "0", false)]
	public int RecipeGroup { get; set; }

	/// <summary>
	/// Whether the order of input items should be respected
	/// </summary>
	[DocumentAsJson("Optional", "False", false)]
	public bool Shapeless { get; set; }

	/// <summary>
	/// List of ingredients for each slot of the crafting grid
	/// </summary>
	public CraftingRecipeIngredient?[]? ResolvedIngredients { get; set; }

	public override IEnumerable<IRecipeIngredient> RecipeIngredients
	{
		get
		{
			CraftingRecipeIngredient?[]? resolvedIngredients = ResolvedIngredients;
			return ((resolvedIngredients != null) ? resolvedIngredients.OfType<IRecipeIngredient>() : null) ?? Ingredients?.Values ?? throw new InvalidOperationException($"Grid recipe '{Name}' has no ingredients specified or ingredients are failed to resolve");
		}
	}

	public override IRecipeOutput RecipeOutput => Output ?? throw new InvalidOperationException($"Grid recipe '{Name}' has no output specified");

	public override void OnParsed(IWorldAccessor world)
	{
		if (Ingredients == null)
		{
			return;
		}
		foreach (KeyValuePair<string, CraftingRecipeIngredient> ingredient in Ingredients)
		{
			ingredient.Deconstruct(out var key, out var value);
			string text = key;
			value = value;
			if (value.Id == null)
			{
				key = (value.Id = text);
			}
		}
	}

	/// <summary>
	/// Turns Ingredients into IItemStacks
	/// </summary>
	/// <param name="world"></param>
	/// <returns>True on successful resolve</returns>
	public override bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
	{
		if (Output == null)
		{
			world.Logger.Error($"Grid Recipe '{Name}' has no output specified.");
			return false;
		}
		if (IngredientPattern == null)
		{
			world.Logger.Error($"Grid Recipe with output '{Output.Code}' has no ingredient pattern.");
			return false;
		}
		if (Ingredients == null)
		{
			world.Logger.Error($"Grid Recipe with output '{Output.Code}' has no ingredients.");
			return false;
		}
		IngredientPattern = IngredientPattern.Replace(",", "").Replace("\t", "").Replace("\r", "")
			.Replace("\n", "")
			.DeDuplicate();
		if (Width * Height != IngredientPattern.Length)
		{
			world.Logger.Error($"Grid Recipe with output '{Output.Code}' has and incorrect ingredient pattern length.");
			return false;
		}
		ResolvedIngredients = new CraftingRecipeIngredient[Width * Height];
		for (int i = 0; i < IngredientPattern.Length; i++)
		{
			char c = IngredientPattern[i];
			if (c != ' ' && c != '_')
			{
				string text = c.ToString().DeDuplicate();
				if (!Ingredients.TryGetValue(text, out CraftingRecipeIngredient value))
				{
					world.Logger.Error($"Grid Recipe with output '{Output.Code}' contains an ingredient pattern code '{text}' but supplies no ingredient for it.");
					return false;
				}
				if (!value.Resolve(world, "Grid recipe", this))
				{
					world.Logger.Error($"Grid Recipe with output '{Output.Code}' contains an ingredient that cannot be resolved: {value}");
					return false;
				}
				CraftingRecipeIngredient craftingRecipeIngredient = value.Clone();
				craftingRecipeIngredient.Id = text;
				ResolvedIngredients[i] = craftingRecipeIngredient;
			}
		}
		if (!Output.Resolve(world, "Grid recipe"))
		{
			world.Logger.Error($"Grid Recipe '{Name}': Output {Output.Code} cannot be resolved.");
			return false;
		}
		return true;
	}

	public virtual void FreeRAMServer()
	{
		IngredientPattern = null;
		Ingredients = null;
	}

	/// <summary>
	/// Puts the crafted ItemStack into the output slot and 
	/// consumes the required items from the input slots
	/// </summary>
	/// <param name="inputSlots"></param>
	/// <param name="byPlayer"></param>
	/// <param name="gridWidth"></param>
	public virtual bool ConsumeInput(IPlayer byPlayer, ItemSlot[] inputSlots, int gridWidth)
	{
		if (ResolvedIngredients == null)
		{
			return false;
		}
		CraftingRecipeIngredient[] array = new CraftingRecipeIngredient[ResolvedIngredients.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = ResolvedIngredients[i]?.Clone();
		}
		if (Shapeless)
		{
			IRecipeIngredient[] ingredients = array;
			return ConsumeInputShapeLess(byPlayer, inputSlots, ingredients);
		}
		int num = inputSlots.Length / gridWidth;
		if (gridWidth < Width || num < Height)
		{
			return false;
		}
		for (int j = 0; j <= gridWidth - Width; j++)
		{
			for (int k = 0; k <= num - Height; k++)
			{
				int colStart = j;
				int rowStart = k;
				int width = Width;
				IRecipeIngredient[] ingredients = array;
				if (MatchesAtPosition(colStart, rowStart, inputSlots, gridWidth, width, ingredients))
				{
					int width2 = Width;
					int colStart2 = j;
					int rowStart2 = k;
					ingredients = ResolvedIngredients;
					if (!ConsumeInputAt(byPlayer, inputSlots, gridWidth, width2, colStart2, rowStart2, ingredients))
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	/// <summary>
	/// Check if this recipe matches given ingredients
	/// </summary>
	/// <param name="forPlayer">The player for trait testing. Can be null.</param>
	/// <param name="ingredients"></param>
	/// <param name="gridWidth"></param>
	/// <returns></returns>
	public virtual bool Matches(IPlayer forPlayer, IWorldAccessor world, ItemSlot[] ingredients, int gridWidth)
	{
		if (ResolvedIngredients == null)
		{
			return false;
		}
		if (!forPlayer.Entity.Api.Event.TriggerMatchesRecipe(forPlayer, this, ingredients))
		{
			return false;
		}
		if (!forPlayer.Entity.Api.Event.TriggerMatchesRecipe(forPlayer, this, ingredients, gridWidth))
		{
			return false;
		}
		int num = ingredients.Length / gridWidth;
		if (gridWidth < Width || num < Height)
		{
			return false;
		}
		if (Shapeless)
		{
			IRecipeIngredient[] resolvedIngredients = ResolvedIngredients;
			return MatchesShapeLess(ingredients, world, resolvedIngredients);
		}
		for (int i = 0; i <= gridWidth - Width; i++)
		{
			for (int j = 0; j <= num - Height; j++)
			{
				int colStart = i;
				int rowStart = j;
				int width = Width;
				IRecipeIngredient[] resolvedIngredients = ResolvedIngredients;
				if (MatchesAtPosition(colStart, rowStart, ingredients, gridWidth, width, resolvedIngredients))
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Serialized the recipe
	/// </summary>
	/// <param name="writer"></param>
	public override void ToBytes(BinaryWriter writer)
	{
		if (Ingredients == null || ResolvedIngredients == null || IngredientPattern == null || Output == null)
		{
			throw new InvalidOperationException("Some of required grid recipe fields are null, cant serialize it to bytes");
		}
		base.ToBytes(writer);
		writer.Write(Width);
		writer.Write(Height);
		writer.Write(Shapeless);
		Output.ToBytes(writer);
		for (int i = 0; i < ResolvedIngredients.Length; i++)
		{
			if (ResolvedIngredients[i] == null)
			{
				writer.Write(value: true);
				continue;
			}
			writer.Write(value: false);
			ResolvedIngredients[i]?.ToBytes(writer);
		}
		writer.Write(RecipeGroup);
		writer.Write(IngredientPattern);
	}

	/// <summary>
	/// Deserializes the recipe
	/// </summary>
	/// <param name="reader"></param>
	/// <param name="resolver"></param>
	public override void FromBytes(BinaryReader reader, IWorldAccessor resolver)
	{
		base.FromBytes(reader, resolver);
		Width = reader.ReadInt32();
		Height = reader.ReadInt32();
		Shapeless = reader.ReadBoolean();
		Output = new CraftingRecipeIngredient();
		Output.FromBytes(reader, resolver);
		ResolvedIngredients = new CraftingRecipeIngredient[Width * Height];
		for (int i = 0; i < ResolvedIngredients.Length; i++)
		{
			if (!reader.ReadBoolean())
			{
				CraftingRecipeIngredient craftingRecipeIngredient = new CraftingRecipeIngredient();
				craftingRecipeIngredient.FromBytes(reader, resolver);
				craftingRecipeIngredient.Resolve(resolver, "Grid recipes deserialized", this);
				ResolvedIngredients[i] = craftingRecipeIngredient;
			}
		}
		RecipeGroup = reader.ReadInt32();
		IngredientPattern = reader.ReadString().DeDuplicate();
	}

	/// <summary>
	/// Creates a deep copy
	/// </summary>
	/// <returns></returns>
	public override GridRecipe Clone()
	{
		GridRecipe gridRecipe = new GridRecipe();
		CloneTo(gridRecipe);
		return gridRecipe;
	}

	protected override void CloneTo(object recipe)
	{
		base.CloneTo(recipe);
		if (!(recipe is GridRecipe gridRecipe))
		{
			throw new ArgumentException("CloneTo should take object of same class or it subclass");
		}
		gridRecipe.Output = Output?.Clone();
		gridRecipe.Ingredients = new Dictionary<string, CraftingRecipeIngredient>();
		if (Ingredients != null)
		{
			foreach (var (key, craftingRecipeIngredient2) in Ingredients)
			{
				gridRecipe.Ingredients[key] = craftingRecipeIngredient2.Clone();
			}
		}
		gridRecipe.IngredientPattern = IngredientPattern;
		gridRecipe.Width = Width;
		gridRecipe.Height = Height;
		gridRecipe.RecipeGroup = RecipeGroup;
		gridRecipe.Shapeless = Shapeless;
		if (ResolvedIngredients != null)
		{
			gridRecipe.ResolvedIngredients = new CraftingRecipeIngredient[ResolvedIngredients.Length];
			for (int i = 0; i < ResolvedIngredients.Length; i++)
			{
				gridRecipe.ResolvedIngredients[i] = ResolvedIngredients[i]?.Clone();
			}
		}
	}
}
