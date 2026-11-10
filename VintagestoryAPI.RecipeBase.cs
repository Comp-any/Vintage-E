using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common;

/// <summary>
/// Creates a new base recipe type. Almost all recipe types extend from this.
/// </summary>
/// <typeparam name="TRecipe">The resulting recipe type.</typeparam>
[DocumentAsJson]
public abstract class RecipeBase : IRecipeBase, IByteSerializable, ICloneable, IConcreteCloneable<RecipeBase>
{
	public static readonly CollectiblePreSearchResultsCache CollectiblePreSearchResultsCache = new CollectiblePreSearchResultsCache();

	/// <summary>
	/// If set to false, the recipe will never be loaded.
	/// If loaded, you can use this field to disable recipes during runtime.
	/// </summary>
	[DocumentAsJson("Optional", "True", false)]
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Adds a name to this. Used for logging, and determining helve hammer workability for smithing recipes. Recipes for repairing objects must contain 'repair' in the name.
	/// </summary>
	[DocumentAsJson("Optional", "Asset Path", false)]
	public AssetLocation? Name { get; set; }

	/// <summary>
	/// Optional attribute data that you can attach any data to. Useful for code mods, but also required when using liquid ingredients.<br />
	/// See dough.json grid recipe file for example.
	/// </summary>
	[JsonConverter(typeof(JsonAttributesConverter))]
	[DocumentAsJson("Optional", "None", false)]
	public JsonObject? Attributes { get; set; }

	/// <summary>
	/// If set, only players with given trait can use this  See config/traits.json for a list of traits.
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public string? RequiresTrait { get; set; }

	/// <summary>
	/// If true, the output item will have its durability averaged over the input items
	/// </summary>
	[DocumentAsJson("Optional", "True", false)]
	public bool AverageDurability { get; set; } = true;

	/// <summary>
	/// If set, it will copy over the itemstack attributes from given ingredient code
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public string? CopyAttributesFrom { get; set; }

	/// <summary>
	/// Attributes from these ingredients will be merged into output itemstack
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public string[] MergeAttributesFrom { get; set; } = Array.Empty<string>();

	/// <summary>
	/// If '{code}' is used in ingredient code, allowed variants for this code should be specified in this map
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public Dictionary<string, string[]> AllowedVariants { get; set; } = new Dictionary<string, string[]>();

	/// <summary>
	/// If '{code}' is used in ingredient code, skip variants for this code should be specified in this map
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public Dictionary<string, string[]> SkipVariants { get; set; } = new Dictionary<string, string[]>();

	/// <summary>
	/// Used by the handbook. If false, will not appear in the "Created by" section
	/// </summary>
	[DocumentAsJson("Optional", "True", false)]
	public bool ShowInCreatedBy { get; set; } = true;

	/// <summary>
	/// The ID of the  Automatically generated when the recipe is loaded.
	/// </summary>
	public int RecipeId { get; set; }

	public abstract IEnumerable<IRecipeIngredient> RecipeIngredients { get; }

	public abstract IRecipeOutput RecipeOutput { get; }

	public abstract bool Resolve(IWorldAccessor world, string sourceForErrorLogging);

	public abstract RecipeBase Clone();

	object ICloneable.Clone()
	{
		return Clone();
	}

	public virtual void ToBytes(BinaryWriter writer)
	{
		writer.Write(Name?.ToShortString() ?? "");
		writer.Write(ShowInCreatedBy);
		writer.Write(AverageDurability);
		writer.Write(Attributes == null);
		if (Attributes?.Token != null)
		{
			writer.Write(Attributes.Token.ToString());
		}
		writer.Write(RequiresTrait != null);
		if (RequiresTrait != null)
		{
			writer.Write(RequiresTrait);
		}
		writer.Write(CopyAttributesFrom != null);
		if (CopyAttributesFrom != null)
		{
			writer.Write(CopyAttributesFrom);
		}
		writer.Write(AllowedVariants.Count);
		string key;
		string[] value;
		foreach (KeyValuePair<string, string[]> allowedVariant in AllowedVariants)
		{
			allowedVariant.Deconstruct(out key, out value);
			string value2 = key;
			string[] array = value;
			writer.Write(value2);
			writer.Write(array.Length);
			value = array;
			foreach (string value3 in value)
			{
				writer.Write(value3);
			}
		}
		writer.Write(SkipVariants.Count);
		foreach (KeyValuePair<string, string[]> skipVariant in SkipVariants)
		{
			skipVariant.Deconstruct(out key, out value);
			string value4 = key;
			string[] array2 = value;
			writer.Write(value4);
			writer.Write(array2.Length);
			value = array2;
			foreach (string value5 in value)
			{
				writer.Write(value5);
			}
		}
		writer.Write(MergeAttributesFrom.Length);
		value = MergeAttributesFrom;
		foreach (string value6 in value)
		{
			writer.Write(value6);
		}
	}

	public virtual void FromBytes(BinaryReader reader, IWorldAccessor resolver)
	{
		Name = new AssetLocation(reader.ReadString());
		ShowInCreatedBy = reader.ReadBoolean();
		AverageDurability = reader.ReadBoolean();
		if (!reader.ReadBoolean())
		{
			string json = reader.ReadString();
			Attributes = new JsonObject(JToken.Parse(json));
		}
		if (reader.ReadBoolean())
		{
			RequiresTrait = reader.ReadString().DeDuplicate();
		}
		if (reader.ReadBoolean())
		{
			CopyAttributesFrom = reader.ReadString().DeDuplicate();
		}
		int num = reader.ReadInt32();
		AllowedVariants = new Dictionary<string, string[]>();
		for (int i = 0; i < num; i++)
		{
			string key = reader.ReadString().DeDuplicate();
			int num2 = reader.ReadInt32();
			string[] array = new string[num2];
			for (int j = 0; j < num2; j++)
			{
				array[j] = reader.ReadString().DeDuplicate();
			}
			AllowedVariants[key] = array;
		}
		int num3 = reader.ReadInt32();
		SkipVariants = new Dictionary<string, string[]>();
		for (int k = 0; k < num3; k++)
		{
			string key2 = reader.ReadString().DeDuplicate();
			int num4 = reader.ReadInt32();
			string[] array2 = new string[num4];
			for (int l = 0; l < num4; l++)
			{
				array2[l] = reader.ReadString().DeDuplicate();
			}
			SkipVariants[key2] = array2;
		}
		int num5 = reader.ReadInt32();
		MergeAttributesFrom = new string[num5];
		for (int m = 0; m < num5; m++)
		{
			MergeAttributesFrom[m] = reader.ReadString().DeDuplicate();
		}
	}

	public virtual void OnParsed(IWorldAccessor world)
	{
	}

	public virtual IEnumerable<IRecipeBase> GenerateRecipesForAllIngredientCombinations(IWorldAccessor world)
	{
		Dictionary<string, HashSet<string>> nameToCodeMapping = GetNameToCodeMapping(world);
		if (nameToCodeMapping.Count <= 0)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<IRecipeBase>(this);
		}
		List<RecipeBase> list = new List<RecipeBase>();
		int num = 1;
		string key;
		HashSet<string> value;
		foreach (KeyValuePair<string, HashSet<string>> item in nameToCodeMapping)
		{
			item.Deconstruct(out key, out value);
			HashSet<string> hashSet = value;
			num *= hashSet.Count;
		}
		bool flag = true;
		int num2 = 1;
		foreach (KeyValuePair<string, HashSet<string>> item2 in nameToCodeMapping)
		{
			item2.Deconstruct(out key, out value);
			string variantCode = key;
			HashSet<string> hashSet2 = value;
			if (hashSet2.Count == 0)
			{
				continue;
			}
			string[] array = hashSet2.ToArray();
			for (int i = 0; i < num; i++)
			{
				string currentVariant = array[i / num2 % array.Length];
				RecipeBase recipeBase;
				if (flag)
				{
					recipeBase = Clone();
					list.Add(recipeBase);
				}
				else
				{
					recipeBase = list[i];
				}
				recipeBase.FillPlaceHolder(variantCode, currentVariant);
			}
			num2 *= array.Length;
			flag = false;
		}
		if (RecipeIngredients.Any((IRecipeIngredient ingredient) => ingredient.MatchingType == EnumRecipeMatchType.TagsOnly))
		{
			return list.SelectMany((RecipeBase recipe) => GenerateRecipesForTagOnlyIngredients(world, recipe));
		}
		return list;
	}

	protected virtual void FillPlaceHolder(string variantCode, string currentVariant)
	{
		foreach (IRecipeIngredient recipeIngredient in RecipeIngredients)
		{
			FillIngredientPlaceHolders(recipeIngredient, variantCode, currentVariant);
			recipeIngredient.SkipVariants = null;
			recipeIngredient.AllowedVariants = null;
		}
		RecipeOutput.FillPlaceHolder(variantCode, currentVariant);
	}

	public virtual void GenerateOutputStack(ItemSlot[] inputSlots, ItemSlot outputSlot)
	{
		if (RecipeOutput.ResolvedItemStack == null)
		{
			throw new InvalidOperationException($"Missing or errored output result for recipe '{Name}'");
		}
		outputSlot.Itemstack = RecipeOutput.ResolvedItemStack.Clone();
		ItemStack itemstack = outputSlot.Itemstack;
		if (CopyAttributesFrom != null)
		{
			ItemStack inputStackForIngredientId = GetInputStackForIngredientId(CopyAttributesFrom, inputSlots);
			if (inputStackForIngredientId != null)
			{
				ITreeAttribute treeAttribute = inputStackForIngredientId.Attributes.Clone();
				treeAttribute.MergeTree(itemstack.Attributes);
				itemstack.Attributes = treeAttribute;
			}
		}
		if (MergeAttributesFrom.Length != 0)
		{
			string[] mergeAttributesFrom = MergeAttributesFrom;
			foreach (string id in mergeAttributesFrom)
			{
				ItemStack inputStackForIngredientId2 = GetInputStackForIngredientId(id, inputSlots);
				if (inputStackForIngredientId2 != null)
				{
					ITreeAttribute treeAttribute2 = inputStackForIngredientId2.Attributes.Clone();
					treeAttribute2.MergeTree(itemstack.Attributes);
					itemstack.Attributes = treeAttribute2;
				}
			}
		}
		outputSlot.Itemstack.Collectible.OnCreatedByCrafting(inputSlots, outputSlot, this);
	}

	public virtual TGridCellElement? GetElementInGrid<TGridCellElement>(int row, int column, TGridCellElement[] cells, int gridWidth)
	{
		int num = cells.Length / gridWidth;
		if (row < 0 || column < 0 || row >= num || column >= gridWidth)
		{
			return default;
		}
		return cells[row * gridWidth + column];
	}

	public virtual int GetGridIndex<TGridCellElement>(int row, int col, TGridCellElement[] cells, int gridWidth)
	{
		int num = cells.Length / gridWidth;
		if (row < 0 || col < 0 || row >= num || col >= gridWidth)
		{
			return -1;
		}
		return row * gridWidth + col;
	}

	public static string ReplaceVariantsToRegex(string value, out List<string> variants, Dictionary<string, string>? allowedVariants = null)
	{
		variants = new List<string>();
		string text = value;
		while (text.Contains('{') && text.Contains('}'))
		{
			int num = text.IndexOf('{');
			int num2 = text.IndexOf('}');
			string text2 = text.Substring(num + 1, num2 - num - 1);
			text = ((allowedVariants == null || !allowedVariants.TryGetValue(text2, out string value2)) ? text.Replace("{" + text2 + "}", "@") : text.Replace("{" + text2 + "}", value2));
			variants.Add(text2);
		}
		text = WildCardToRegex(text);
		text = text.Replace("@", "([\\w-]+)");
		return "^" + text + "$";
	}

	public static string WildCardToRegex(string value)
	{
		return Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*");
	}

	protected virtual void CloneTo(object recipe)
	{
		RecipeBase obj = (recipe as RecipeBase) ?? throw new ArgumentException("CloneTo should take object of same class or it subclass");
		obj.Enabled = Enabled;
		obj.Name = Name;
		obj.Attributes = Attributes?.Clone();
		obj.RequiresTrait = RequiresTrait;
		obj.AverageDurability = AverageDurability;
		obj.CopyAttributesFrom = CopyAttributesFrom;
		obj.MergeAttributesFrom = MergeAttributesFrom;
		obj.AllowedVariants = AllowedVariants;
		obj.SkipVariants = SkipVariants;
		obj.ShowInCreatedBy = ShowInCreatedBy;
	}

	public virtual IRecipeIngredient? GetIngredientById(string id)
	{
		return RecipeIngredients.FirstOrDefault((IRecipeIngredient ingredient) => ingredient.Id == id);
	}

	protected virtual Dictionary<string, HashSet<string>> GetNameToCodeMapping(IWorldAccessor world)
	{
		Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>();
		foreach (IRecipeIngredient recipeIngredient in RecipeIngredients)
		{
			recipeIngredient.MatchingType = IRecipeIngredient.GetMatchType(recipeIngredient.Code?.ToString(), recipeIngredient.Name != null);
			switch (recipeIngredient.MatchingType)
			{
			case EnumRecipeMatchType.NamedWildcard:
				GetNameToCodeMappingForBasicWildcard(world, recipeIngredient, dictionary);
				break;
			case EnumRecipeMatchType.AdvancedWildcard:
				GetNameToCodeMappingForAdvancedWildcard(world, recipeIngredient, dictionary);
				break;
			}
		}
		return dictionary;
	}

	protected virtual ItemStack? GetInputStackForIngredientId(string id, ItemSlot[] inputSlots)
	{
		IRecipeIngredient ingredientById = GetIngredientById(id);
		if (ingredientById == null)
		{
			return null;
		}
		foreach (ItemSlot itemSlot in inputSlots)
		{
			if (!itemSlot.Empty)
			{
				ItemStack itemstack = itemSlot.Itemstack;
				if (itemstack != null && ingredientById.SatisfiesAsIngredient(itemstack) && itemstack.Collectible.MatchesForCrafting(itemstack, this, ingredientById))
				{
					return itemstack;
				}
			}
		}
		return null;
	}

	protected virtual void GetNameToCodeMappingForBasicWildcard(IWorldAccessor world, IRecipeIngredient ingredient, Dictionary<string, HashSet<string>> mappings)
	{
		List<CollectibleObject> orCreate = CollectiblePreSearchResultsCache.GetOrCreate(world, ingredient.Code, ingredient.Type);
		if (orCreate.Count == 0)
		{
			world.Logger.Warning("Crafting recipe {0} {1} does not resolve to any collectible. Grid recipe {2} will never match.", ingredient.Type, ingredient.Code, Name);
		}
		HashSet<string> hashSet = new HashSet<string>();
		int num = ingredient.Code.Path.IndexOf('*');
		int num2 = ingredient.Code.Path.Length - num - 1;
		bool flag = !ingredient.Tags.IsEmpty;
		foreach (CollectibleObject item2 in orCreate)
		{
			AssetLocation code = item2.Code;
			if (!(code == null) && (ingredient.SkipVariants == null || !WildcardUtil.MatchesVariants(ingredient.Code, code, ingredient.SkipVariants)) && WildcardUtil.Match(ingredient.Code, code, ingredient.AllowedVariants) && (!flag || ingredient.Tags.Matches(in item2.Tags)))
			{
				string path = code.Path;
				int num3 = num;
				path = path.Substring(num3, path.Length - num3);
				num3 = num2;
				string item = path.Substring(0, path.Length - num3).DeDuplicate();
				hashSet.Add(item);
			}
		}
		if (ingredient.Name == null)
		{
			string path = (ingredient.Name = ingredient.Code);
		}
		mappings[ingredient.Name] = hashSet;
	}

	protected virtual void GetNameToCodeMappingForAdvancedWildcard(IWorldAccessor world, IRecipeIngredient ingredient, Dictionary<string, HashSet<string>> mappings)
	{
		IEnumerable<CollectibleObject> orCreate = CollectiblePreSearchResultsCache.GetOrCreate(world, ingredient.Code, ingredient.Type);
		List<Dictionary<string, string>> list = GenerateAllowedVariantsCombinations(AllowedVariants);
		if (list.Count > 0)
		{
			foreach (Dictionary<string, string> item in list)
			{
				Dictionary<string, HashSet<string>> variantValuesFromAdvancedWildcard = GetVariantValuesFromAdvancedWildcard(ingredient, orCreate, item);
				AddVariantValuesToMapping(mappings, variantValuesFromAdvancedWildcard);
			}
			return;
		}
		Dictionary<string, HashSet<string>> variantValuesFromAdvancedWildcard2 = GetVariantValuesFromAdvancedWildcard(ingredient, orCreate, new Dictionary<string, string>());
		AddVariantValuesToMapping(mappings, variantValuesFromAdvancedWildcard2);
	}

	protected virtual Dictionary<string, HashSet<string>> GetVariantValuesFromAdvancedWildcard(IRecipeIngredient ingredient, IEnumerable<CollectibleObject> collectibles, Dictionary<string, string> allowedVariantsCombination)
	{
		Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>();
		Regex regex = new Regex(ReplaceVariantsToRegex(ingredient.Code.Path, out List<string> variants, allowedVariantsCombination));
		bool flag = !ingredient.Tags.IsEmpty;
		foreach (CollectibleObject item in collectibles.Where((CollectibleObject collectible) => WildcardUtil.Match(ingredient.Code.Domain, collectible.Code.Domain)))
		{
			Match match = regex.Match(item.Code.Path);
			if (match.Success && (!flag || ingredient.Tags.Matches(in item.Tags)))
			{
				AddVariantCombination(dictionary, allowedVariantsCombination);
				CollectVariantValues(match, variants, dictionary);
			}
		}
		return dictionary;
	}

	protected virtual void AddVariantCombination(Dictionary<string, HashSet<string>> variantValues, Dictionary<string, string> combination)
	{
		foreach (var (key, item) in combination)
		{
			if (!variantValues.ContainsKey(key))
			{
				variantValues[key] = new HashSet<string>();
			}
			variantValues[key].Add(item);
		}
	}

	protected virtual List<Dictionary<string, string>> GenerateAllowedVariantsCombinations(Dictionary<string, string[]> allowedVariants)
	{
		if (allowedVariants.Count == 0)
		{
			return new List<Dictionary<string, string>>();
		}
		string[] variants = allowedVariants.Keys.ToArray();
		List<Dictionary<string, string>> list = new List<Dictionary<string, string>>();
		GenerateAllowedVariantsCombinationsRecursive(0, variants, Array.Empty<(string, string)>(), allowedVariants, list);
		return list;
	}

	protected virtual void GenerateAllowedVariantsCombinationsRecursive(int index, string[] variants, IEnumerable<(string variant, string value)> combination, Dictionary<string, string[]> allowedVariants, List<Dictionary<string, string>> combinations)
	{
		if (index >= variants.Length)
		{
			combinations.Add(combination.ToDictionary(((string variant, string value) entry) => entry.variant, ((string variant, string value) entry) => entry.value));
			return;
		}
		string text = variants[index];
		string[] array = allowedVariants[text];
		foreach (string item in array)
		{
			IEnumerable<(string, string)> combination2 = combination.Append((text, item));
			GenerateAllowedVariantsCombinationsRecursive(index + 1, variants, combination2, allowedVariants, combinations);
		}
	}

	protected virtual void CollectVariantValues(Match match, List<string> variants, Dictionary<string, HashSet<string>> variantCodes)
	{
		for (int i = 0; i < variants.Count && i + 1 < match.Groups.Count; i++)
		{
			string value = match.Groups[i + 1].Value;
			string key = variants[i];
			if ((!AllowedVariants.ContainsKey(key) || AllowedVariants[key].Contains(value)) && (!SkipVariants.ContainsKey(key) || !SkipVariants[key].Contains(value)))
			{
				if (!variantCodes.ContainsKey(key))
				{
					variantCodes[key] = new HashSet<string>();
				}
				variantCodes[key].Add(value);
			}
		}
	}

	protected virtual void AddVariantValuesToMapping(Dictionary<string, HashSet<string>> mappings, Dictionary<string, HashSet<string>> variantValues)
	{
		foreach (var (key, hashSet2) in variantValues)
		{
			if (mappings.TryGetValue(key, out HashSet<string> value))
			{
				foreach (string item in hashSet2)
				{
					value.Add(item);
				}
			}
			else
			{
				mappings[key] = hashSet2;
			}
		}
	}

	protected virtual void FillIngredientPlaceHolders(IRecipeIngredient ingredient, string variantCode, string variantValue)
	{
		switch (ingredient.MatchingType)
		{
		case EnumRecipeMatchType.NamedWildcard:
			if (ingredient.Name == variantCode)
			{
				ingredient.FillPlaceHolder(variantCode, variantValue);
				ingredient.Code.Path = ingredient.Code.Path.Replace("*", variantValue);
				ingredient.MatchingType = IRecipeIngredient.GetMatchType(ingredient.Code.ToString());
			}
			break;
		case EnumRecipeMatchType.AdvancedWildcard:
			ingredient.FillPlaceHolder(variantCode, variantValue);
			ingredient.MatchingType = IRecipeIngredient.GetMatchType(ingredient.Code.ToString());
			break;
		}
		if (ingredient.ReturnedStack?.Code != null)
		{
			ingredient.ReturnedStack.Code.Path = ingredient.ReturnedStack.Code.Path.Replace("{" + variantCode + "}", variantValue);
		}
	}

	protected virtual bool MatchesAtPosition(ItemSlot[] inputSlots, IRecipeIngredient?[] ingredients)
	{
		int num = inputSlots.Length;
		return MatchesAtPosition(0, 0, inputSlots, num, num, ingredients);
	}

	protected virtual bool MatchesAtPosition(int colStart, int rowStart, ItemSlot[] inputSlots, int gridWidth, int recipeWidth, IRecipeIngredient?[] ingredients)
	{
		int num = inputSlots.Length / gridWidth;
		for (int i = 0; i < gridWidth; i++)
		{
			for (int j = 0; j < num; j++)
			{
				ItemStack inputStack = GetElementInGrid(j, i, inputSlots, gridWidth)?.Itemstack;
				IRecipeIngredient elementInGrid = GetElementInGrid(j - rowStart, i - colStart, ingredients, recipeWidth);
				if (!MatchStackToIngredient(inputStack, elementInGrid))
				{
					return false;
				}
			}
		}
		return true;
	}

	protected virtual bool MatchStackToIngredient(ItemStack? inputStack, IRecipeIngredient? ingredient)
	{
		if (inputStack != null && ingredient != null)
		{
			if (!ingredient.SatisfiesAsIngredient(inputStack))
			{
				return false;
			}
			if (!inputStack.Collectible.MatchesForCrafting(inputStack, this, ingredient))
			{
				return false;
			}
		}
		else if (inputStack != null || ingredient != null)
		{
			return false;
		}
		return true;
	}

	protected virtual bool ConsumeInputShapeLess(IPlayer byPlayer, ItemSlot[] inputSlots, IRecipeIngredient?[] ingredients)
	{
		List<IRecipeIngredient> list = new List<IRecipeIngredient>();
		List<IRecipeIngredient> wildcardAndToolIngredients = new List<IRecipeIngredient>();
		SeparateAndMergeIngredients(ingredients, list, wildcardAndToolIngredients);
		foreach (var (itemStack, inputSlot) in inputSlots.Select((ItemSlot slot) => (Itemstack: slot.Itemstack, slot: slot)))
		{
			if (itemStack != null)
			{
				ConsumeExactMatchIngredient(itemStack, inputSlot, inputSlots, list, byPlayer);
				ConsumeWildcardMatchIngredient(itemStack, inputSlot, inputSlots, wildcardAndToolIngredients, byPlayer);
			}
		}
		return list.Count == 0;
	}

	protected virtual bool ConsumeInputAt(IPlayer byPlayer, ItemSlot[] inputSlots, IRecipeIngredient?[] ingredients)
	{
		int num = inputSlots.Length;
		return ConsumeInputAt(byPlayer, inputSlots, num, num, 0, 0, ingredients);
	}

	protected virtual bool ConsumeInputAt(IPlayer byPlayer, ItemSlot[] inputSlots, int gridWidth, int recipeWidth, int colStart, int rowStart, IRecipeIngredient?[] ingredients)
	{
		int num = inputSlots.Length / gridWidth;
		for (int i = 0; i < gridWidth; i++)
		{
			for (int j = 0; j < num; j++)
			{
				ItemSlot elementInGrid = GetElementInGrid(j, i, inputSlots, gridWidth);
				IRecipeIngredient elementInGrid2 = GetElementInGrid(j - rowStart, i - colStart, ingredients, recipeWidth);
				if (elementInGrid2 != null && (elementInGrid2.ResolvedItemStack != null || elementInGrid2.MatchingType != EnumRecipeMatchType.Exact))
				{
					if (elementInGrid?.Itemstack == null)
					{
						return false;
					}
					int quantity = ((elementInGrid2.MatchingType != EnumRecipeMatchType.Exact) ? elementInGrid2.Quantity : (elementInGrid2.ResolvedItemStack?.StackSize ?? 0));
					elementInGrid.Itemstack.Collectible.OnConsumedByCrafting(inputSlots, elementInGrid, this, elementInGrid2, byPlayer, quantity);
				}
			}
		}
		return true;
	}

	protected virtual bool MatchesShapeLess(ItemSlot[] suppliedSlots, IWorldAccessor world, IRecipeIngredient?[] ingredients)
	{
		List<ItemStack> list = new List<ItemStack>();
		MergeStacks(suppliedSlots, list);
		if (!MatchWildcardIngredients(list, ingredients))
		{
			return false;
		}
		List<(ItemStack, IRecipeIngredient)> list2 = new List<(ItemStack, IRecipeIngredient)>();
		MergeIngredientStacks(suppliedSlots, list2, ingredients, world);
		if (list2.Count != list.Count)
		{
			return false;
		}
		return MatchIngredientStacks(list2, list);
	}

	protected virtual void MergeStacks(ItemSlot[] slots, List<ItemStack> stacks)
	{
		foreach (ItemStack suppliedStack in slots.Select((ItemSlot slot) => slot.Itemstack).OfType<ItemStack>())
		{
			ItemStack itemStack = stacks.Find((ItemStack stack) => stack.Satisfies(suppliedStack));
			if (itemStack != null)
			{
				itemStack.StackSize += suppliedStack.StackSize;
			}
			else
			{
				stacks.Add(suppliedStack.Clone());
			}
		}
	}

	protected virtual void MergeIngredientStacks(ItemSlot[] slots, List<(ItemStack stack, IRecipeIngredient ingredient)> stacks, IRecipeIngredient?[] ingredients, IWorldAccessor world)
	{
		foreach (IRecipeIngredient recipeIngredient in ingredients)
		{
			if (recipeIngredient == null || recipeIngredient.MatchingType != EnumRecipeMatchType.Exact)
			{
				continue;
			}
			ItemStack resolvedItemStack = recipeIngredient.ResolvedItemStack;
			if (resolvedItemStack == null)
			{
				continue;
			}
			ItemStack itemStack = null;
			foreach (var stack in stacks)
			{
				if (stack.stack.Equals(world, resolvedItemStack, GlobalConstants.IgnoredStackAttributes) && recipeIngredient.RecipeAttributes == null)
				{
					(itemStack, _) = stack;
					break;
				}
			}
			if (itemStack != null)
			{
				itemStack.StackSize += resolvedItemStack.StackSize;
			}
			else
			{
				stacks.Add((resolvedItemStack.Clone(), recipeIngredient));
			}
		}
	}

	protected virtual bool MatchWildcardIngredients(List<ItemStack> suppliedStacks, IRecipeIngredient?[] ingredients)
	{
		foreach (IRecipeIngredient item in from ingredient in ingredients.OfType<IRecipeIngredient>()
			where ingredient.MatchingType != EnumRecipeMatchType.Exact
			select ingredient)
		{
			bool flag = false;
			int num = 0;
			while (!flag && num < suppliedStacks.Count)
			{
				ItemStack itemStack = suppliedStacks[num];
				flag = item.Type == itemStack.Class && WildcardUtil.Match(item.Code, itemStack.Collectible.Code, item.AllowedVariants) && itemStack.StackSize >= item.Quantity && item.Tags.Matches(in itemStack.Collectible.Tags);
				flag &= itemStack.Collectible.MatchesForCrafting(itemStack, this, item);
				num++;
			}
			if (!flag)
			{
				return false;
			}
			suppliedStacks.RemoveAt(num - 1);
		}
		return true;
	}

	protected virtual bool MatchIngredientStacks(List<(ItemStack stack, IRecipeIngredient ingredient)> ingredientStacks, List<ItemStack> suppliedStacks)
	{
		bool flag = true;
		foreach (var ingredientStack in ingredientStacks)
		{
			ItemStack item = ingredientStack.stack;
			IRecipeIngredient item2 = ingredientStack.ingredient;
			bool flag2 = false;
			int num = 0;
			while (!flag2 && num < suppliedStacks.Count)
			{
				flag2 = item.Satisfies(suppliedStacks[num]) && item.StackSize <= suppliedStacks[num].StackSize && suppliedStacks[num].Collectible.MatchesForCrafting(suppliedStacks[num], this, item2);
				if (flag2)
				{
					suppliedStacks.RemoveAt(num);
				}
				num++;
			}
			flag &= flag2;
		}
		return flag;
	}

	protected virtual void SeparateAndMergeIngredients(IRecipeIngredient?[] ingredients, List<IRecipeIngredient> exactMatchIngredients, List<IRecipeIngredient> wildcardAndToolIngredients)
	{
		foreach (IRecipeIngredient item in ingredients.OfType<IRecipeIngredient>())
		{
			if (item.MatchingType != EnumRecipeMatchType.Exact || !item.ConsumeProperties.Consume)
			{
				wildcardAndToolIngredients.Add((IRecipeIngredient)item.Clone());
				continue;
			}
			ItemStack ingredientStack = item.ResolvedItemStack;
			if (ingredientStack != null)
			{
				ItemStack itemStack = exactMatchIngredients.Select((IRecipeIngredient element) => element.ResolvedItemStack).OfType<ItemStack>().FirstOrDefault((ItemStack element) => element.Satisfies(ingredientStack));
				if (itemStack != null)
				{
					itemStack.StackSize += ingredientStack.StackSize;
				}
				else
				{
					exactMatchIngredients.Add((IRecipeIngredient)item.Clone());
				}
			}
		}
	}

	protected virtual void ConsumeExactMatchIngredient(ItemStack inputStack, ItemSlot inputSlot, ItemSlot[] inputSlots, List<IRecipeIngredient> exactMatchIngredients, IPlayer byPlayer)
	{
		IRecipeIngredient recipeIngredient = exactMatchIngredients.Find((IRecipeIngredient ingredient) => ingredient.ResolvedItemStack?.Satisfies(inputStack) ?? false);
		ItemStack itemStack = recipeIngredient?.ResolvedItemStack;
		if (itemStack != null)
		{
			int stackSize = itemStack.StackSize;
			int num = Math.Min(stackSize, inputStack.StackSize);
			inputStack.Collectible.OnConsumedByCrafting(inputSlots, inputSlot, this, recipeIngredient, byPlayer, num);
			if (stackSize - num <= 0)
			{
				exactMatchIngredients.Remove(recipeIngredient);
			}
		}
	}

	protected virtual void ConsumeWildcardMatchIngredient(ItemStack inputStack, ItemSlot inputSlot, ItemSlot[] inputSlots, List<IRecipeIngredient> wildcardAndToolIngredients, IPlayer byPlayer)
	{
		IRecipeIngredient recipeIngredient = wildcardAndToolIngredients.Find((IRecipeIngredient ingredient) => ingredient.Type == inputStack.Class && WildcardUtil.Match(ingredient.Code, inputStack.Collectible.Code, ingredient.AllowedVariants) && ingredient.Tags.Matches(inputStack.Collectible.GetTags(inputStack)));
		if (recipeIngredient == null)
		{
			return;
		}
		int num = Math.Min(recipeIngredient.Quantity, inputStack.StackSize);
		inputStack.Collectible.OnConsumedByCrafting(inputSlots, inputSlot, this, recipeIngredient, byPlayer, num);
		if (!recipeIngredient.ConsumeProperties.Consume)
		{
			wildcardAndToolIngredients.Remove(recipeIngredient);
			return;
		}
		recipeIngredient.Quantity -= num;
		if (recipeIngredient.Quantity <= 0)
		{
			wildcardAndToolIngredients.Remove(recipeIngredient);
		}
	}

	protected virtual IEnumerable<IRecipeBase> GenerateRecipesForTagOnlyIngredients(IWorldAccessor world, IRecipeBase recipe)
	{
		Dictionary<string, List<IRecipeIngredient>> dictionary = new Dictionary<string, List<IRecipeIngredient>>();
		foreach (IRecipeIngredient item in recipe.RecipeIngredients.Where((IRecipeIngredient ingredient) => ingredient.MatchingType == EnumRecipeMatchType.TagsOnly))
		{
			List<IRecipeIngredient> value = FindConcreteIngredientsForTagOnlyIngredient(world, item).ToList();
			dictionary.Add(item.Id, value);
		}
		int num = 1;
		foreach (var (text2, list2) in dictionary)
		{
			if (list2.Count == 0)
			{
				world.Logger.VerboseDebug("Ingredient {0} on {1} has no valid concrete ingredients, cannot generate permutations.", text2, recipe.Name);
				return new _003C_003Ez__ReadOnlySingleElementList<IRecipeBase>(recipe);
			}
			num *= list2.Count;
			if (num > 10)
			{
				world.Logger.VerboseDebug("Generating permutations for {0} would result in {1} concrete generated recipes, which is above the current threshold of {2}. Will match this recipe during gameplay instead.", recipe.Name, num, 10);
				return new _003C_003Ez__ReadOnlySingleElementList<IRecipeBase>(recipe);
			}
		}
		world.Logger.VerboseDebug("Generating permutations for  {0} with {1} virtual ingredients into {2} concrete recipes...", recipe.Name, dictionary.Count, num);
		return GeneratePermutations(recipe, dictionary);
		static IEnumerable<IRecipeIngredient> FindConcreteIngredientsForTagOnlyIngredient(IWorldAccessor worldAccessor, IRecipeIngredient ingredientSpecification)
		{
			return (from collectible in worldAccessor.Items
				where collectible?.Code != null
				where ingredientSpecification.Tags.Matches(in collectible.Tags)
				select collectible).Select((Item collectible) =>
			{
				IRecipeIngredient recipeIngredient = (IRecipeIngredient)ingredientSpecification.Clone();
				recipeIngredient.Code = collectible.Code;
				recipeIngredient.Type = collectible.ItemClass;
				return recipeIngredient;
			});
		}
		static IEnumerable<IRecipeBase> GeneratePermutations(IRecipeBase recipeBase, Dictionary<string, List<IRecipeIngredient>> concreteIngredientsMap)
		{
			bool hadTagIngredient = false;
			foreach (IRecipeIngredient tagIngredient in recipeBase.RecipeIngredients.Where((IRecipeIngredient ingredient) => ingredient.MatchingType == EnumRecipeMatchType.TagsOnly))
			{
				hadTagIngredient = true;
				List<IRecipeIngredient> list3 = concreteIngredientsMap[tagIngredient.Id];
				foreach (IRecipeIngredient item2 in list3)
				{
					tagIngredient.Code = item2.Code;
					tagIngredient.Type = item2.Type;
					tagIngredient.MatchingType = EnumRecipeMatchType.Exact;
					IRecipeBase recipe2 = recipeBase.CloneAsInterface();
					foreach (IRecipeBase item3 in GeneratePermutations(recipe2, concreteIngredientsMap))
					{
						yield return item3;
					}
				}
			}
			if (!hadTagIngredient)
			{
				yield return recipeBase;
			}
		}
	}
}
