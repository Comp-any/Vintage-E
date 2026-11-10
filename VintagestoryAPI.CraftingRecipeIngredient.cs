using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common;

/// <summary>
/// A crafting recipe ingredient
/// </summary>
[DocumentAsJson]
public class CraftingRecipeIngredient : IRecipeIngredient, IRecipeIngredientBase, IByteSerializable, ICloneable, IRecipeOutput
{
	public static ITreeAttribute defaultEmptyAttributes = new TreeAttribute();

	/// <summary>
	/// Only used if we have not de-duplicated this CraftingRecipeIngredient (e.g. for RightClickConstruction ingredients)
	/// </summary>
	protected ItemStack? resolvedItemStack;

	public ITreeAttribute? ResolvedAttributes;

	private int deduplicationIndex = -1;

	private IWorldAccessor? world;

	/// <summary>
	/// Is the itemstack an item or a block?
	/// </summary>
	[DocumentAsJson("Recommended", "Block", false)]
	public EnumItemClass Type { get; set; }

	/// <summary>
	/// The code of the item or block.
	/// </summary>
	[DocumentAsJson("Optional", "All", false)]
	public AssetLocation? Code { get; set; } = new AssetLocation("*", "*");

	[DocumentAsJson("Optional", "None", false)]
	public ComplexTagCondition<TagSet> Tags { get; set; }

	/// <summary>
	/// Attaches a name to a wildcard in an ingredient. This is used to substitute the value into the output. Only required if using a wildcard.
	/// </summary>
	[DocumentAsJson("Required", "", false)]
	public string? Name { get; set; }

	/// <summary>
	/// Amount of items in this stacks
	/// </summary>
	[ProtoMember(3)]
	[DocumentAsJson("Optional", "1", false)]
	public int StackSize { get; set; } = 1;

	/// <summary>
	/// The quantity of the itemstack required for the recipe. Alias of <see cref="P:Vintagestory.API.Common.CraftingRecipeIngredient.StackSize" />.
	/// </summary>
	[DocumentAsJson("Recommended", "1", false)]
	public int Quantity
	{
		get
		{
			return StackSize;
		}
		set
		{
			StackSize = value;
		}
	}

	/// <summary>
	/// What attributes this itemstack must have to be a valid ingredient
	/// </summary>
	[JsonProperty]
	[JsonConverter(typeof(JsonAttributesConverter))]
	[DocumentAsJson("Optional", "None", false)]
	public JsonObject? Attributes { get; set; }

	/// <summary>
	/// Optional attribute data that you can attach any data to. Used for some specific instances in code mods.
	/// </summary>
	[JsonProperty]
	[JsonConverter(typeof(JsonAttributesConverter))]
	[DocumentAsJson("Optional", "None", false)]
	public JsonObject? RecipeAttributes { get; set; }

	/// <summary>
	/// Whether this crafting recipe ingredient should be regarded as a tool required to build this item.
	/// If true, the recipe will not consume the item but reduce its durability.
	/// </summary>
	[DocumentAsJson("Optional", "False", false)]
	public bool IsTool { get; set; }

	/// <summary>
	/// If <see cref="P:Vintagestory.API.Common.CraftingRecipeIngredient.Consume" /> is set to False, this is the durability cost when the recipe is created.
	/// </summary>
	[DocumentAsJson("Optional", "1", false)]
	public int ToolDurabilityCost { get; set; } = 1;

	/// <summary>
	/// When using a wildcard in the item/block code, setting this field will limit the allowed variants
	/// </summary>
	[DocumentAsJson("Optional", "Allow All", false)]
	public string[]? AllowedVariants { get; set; }

	/// <summary>
	/// When using a wildcard in the item/block code, setting this field will skip these variants
	/// </summary>
	[DocumentAsJson("Optional", "Skip None", false)]
	public string[]? SkipVariants { get; set; }

	/// <summary>
	/// If set, the crafting recipe will give back the consumed stack to be player upon crafting.
	/// Can also be used to produce multiple outputs for a recipe.
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public JsonItemStack? ReturnedStack { get; set; }

	/// <summary>
	/// Is used to reference recipe ingredient in the recipe, for example to specify where to copy attributes from.<br />
	/// If not specified will be set to the index of this ingredient in the array (starting from 1), or key of this ingredient in the object.
	/// </summary>
	[DocumentAsJson("Optional", "None", false)]
	public string Id { get; set; }

	/// <summary>
	/// Defines if recipe ingredient should be consumed on crafting.<br />
	/// Is equal to 'not IsTool'. Use this property instead of 'IsTool'.
	/// </summary>
	[DocumentAsJson("Optional", "True", false)]
	public bool Consume { get; set; } = true;

	/// <summary>
	/// Defines how item durability will be changed. Durability will be capped at maximum item durability, and wont reduce below 0, unless specific item class changes this behavior.
	/// </summary>
	[DocumentAsJson("Optional", "-1", false)]
	public int DurabilityChange { get; set; }

	/// <summary>
	/// Determines if and item will be destroyed when reaching zero durability.
	/// </summary>
	[DocumentAsJson("Optional", "True", false)]
	public bool Break { get; set; } = true;

	/// <summary>
	/// Defines how <see cref="P:Vintagestory.API.Common.CraftingRecipeIngredient.Code" /> will be used to match
	/// </summary>
	public EnumRecipeMatchType MatchingType { get; set; }

	/// <summary>
	/// The itemstack made from Code, Quantity and Attributes, populated by the engine.  For performance, consider referencing the ResolvedAttributes or Quantity of this CraftingRecipeIngredient instead of the Attributes or StackSize of the ResolvedItemStack, it may save having to clone the ResolvedItemStack
	/// </summary>
	public ItemStack? ResolvedItemStack
	{
		get
		{
			if (deduplicationIndex < 0 || resolvedItemStack != null)
			{
				return resolvedItemStack;
			}
			ItemStack itemStack = world.FastSearchRecipesByIngredient.GetAt(deduplicationIndex).Key.ResolvedItemStack;
			if (itemStack == null)
			{
				return null;
			}
			itemStack.StackSize = 1;
			if (StackSize != 1 || ResolvedAttributes != null)
			{
				itemStack = itemStack.Clone();
				itemStack.StackSize = StackSize;
			}
			if (ResolvedAttributes != null)
			{
				itemStack.Attributes = ResolvedAttributes;
			}
			else
			{
				if (defaultEmptyAttributes.Count != 0)
				{
					defaultEmptyAttributes = new TreeAttribute();
				}
				itemStack.Attributes = defaultEmptyAttributes;
			}
			return itemStack;
		}
		set
		{
			if (deduplicationIndex < 0)
			{
				resolvedItemStack = value;
			}
			else
			{
				world.FastSearchRecipesByIngredient.GetAt(deduplicationIndex).Key.ResolvedItemStack = value;
			}
		}
	}

	public RecipeIngredientConsumeProperties ConsumeProperties => GetConsumeProperties();

	[Obsolete("Use MatchingType")]
	public bool IsWildCard
	{
		get
		{
			return MatchingType != EnumRecipeMatchType.Exact;
		}
		set
		{
			MatchingType = (value ? MatchingType : EnumRecipeMatchType.Exact);
		}
	}

	/// <summary>
	/// For INPUT ingredients this should be used in place of the overload Resolve(IWorldAccessor world, string sourceForErrorLogging), to make use of the de-duplication / fast-search system for ingredients, essential for GridRecipes in the Handbook and in the Crafting Grid
	/// </summary>
	public virtual bool Resolve(IWorldAccessor world, string sourceForErrorLogging, IRecipeBase recipe)
	{
		if (IsTool)
		{
			Consume = false;
			DurabilityChange = -ToolDurabilityCost;
		}
		if (deduplicationIndex == -1)
		{
			this.world = world;
			deduplicationIndex = AddToFastSearchRecipes(world, recipe);
			if (resolvedItemStack != null)
			{
				ITreeAttribute attributes = resolvedItemStack.Attributes;
				if (attributes != null && attributes.Count > 0)
				{
					ResolvedAttributes = resolvedItemStack.Attributes;
				}
				ResolvedItemStack = resolvedItemStack;
				return true;
			}
			return Resolve(world, sourceForErrorLogging);
		}
		return true;
	}

	/// <summary>
	/// For INPUT slots you should normally call the overload Resolve(IWorldAccessor world, string sourceForErrorLogging, IRecipeBase recipe) otherwise you risk this ingredient/recipe being missed in the Handbook and in grid crafting.
	/// <br />However, it's totally fine to use this overload for OUTPUT slots or for special situations (e.g. RightClickConstruction in the world)
	/// </summary>
	public virtual bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
	{
		MatchingType = IRecipeIngredient.GetMatchType(Code?.ToString(), Name != null);
		if (ReturnedStack != null)
		{
			ReturnedStack.Resolve(world, $"{sourceForErrorLogging} recipe with output {Code}");
		}
		if (MatchingType != EnumRecipeMatchType.Exact)
		{
			return true;
		}
		if (MatchingType == EnumRecipeMatchType.Exact && (Code == null || Code == (AssetLocation)"*.*"))
		{
			world.Logger.Warning("Failed resolving crafting recipe ingredient with unspecified code in " + sourceForErrorLogging);
			return false;
		}
		ItemStack itemStack;
		if (Type == EnumItemClass.Block)
		{
			Block block = world.GetBlock(Code);
			if (block == null || block.IsMissing)
			{
				world.Logger.Warning($"Failed resolving crafting recipe ingredient with code {Code} in {sourceForErrorLogging}");
				return false;
			}
			itemStack = new ItemStack(block, Quantity);
		}
		else
		{
			Item item = world.GetItem(Code);
			if (item == null || item.IsMissing)
			{
				world.Logger.Warning($"Failed resolving crafting recipe ingredient with code {Code} in {sourceForErrorLogging}");
				return false;
			}
			itemStack = new ItemStack(item, Quantity);
		}
		if (Attributes != null && Attributes.ToAttribute() is ITreeAttribute treeAttribute)
		{
			ResolvedAttributes = treeAttribute;
			if (deduplicationIndex < 0)
			{
				itemStack.Attributes = treeAttribute;
			}
		}
		ResolvedItemStack = itemStack;
		return true;
	}

	private int AddToFastSearchRecipes(IWorldAccessor world, IRecipeBase recipe)
	{
		System.Collections.Generic.OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>> fastSearchRecipesByIngredient = world.FastSearchRecipesByIngredient;
		FastSearchCraftingRecipeIngredient fastSearchCraftingRecipeIngredient = new FastSearchCraftingRecipeIngredient(this);
		int num = fastSearchRecipesByIngredient.IndexOf(fastSearchCraftingRecipeIngredient);
		if (num >= 0)
		{
			KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>> at = fastSearchRecipesByIngredient.GetAt(num);
			Code = at.Key.Code;
			AllowedVariants = at.Key.AllowedVariants;
			SkipVariants = at.Key.SkipVariants;
			at.Value.AddIfNotPresent(recipe);
			return num;
		}
		num = fastSearchRecipesByIngredient.Count;
		fastSearchCraftingRecipeIngredient.MatchingType = IRecipeIngredient.GetMatchType(Code?.ToString(), Name != null);
		int num2 = 1;
		List<IRecipeBase> list = new List<IRecipeBase>(num2);
		CollectionsMarshal.SetCount(list, num2);
		Span<IRecipeBase> span = CollectionsMarshal.AsSpan(list);
		int index = 0;
		span[index] = recipe;
		fastSearchRecipesByIngredient.Add(fastSearchCraftingRecipeIngredient, list);
		return num;
	}

	public virtual bool SatisfiesAsIngredient(ItemStack inputStack, bool checkStackSize = true)
	{
		if (inputStack == null || inputStack.Collectible == null)
		{
			return false;
		}
		if (MatchingType != EnumRecipeMatchType.Exact)
		{
			if (Type != inputStack.Class)
			{
				return false;
			}
			if (Code != null && !WildcardUtil.Match(Code, inputStack.Collectible.Code, AllowedVariants))
			{
				return false;
			}
			if (checkStackSize && inputStack.StackSize < Quantity)
			{
				return false;
			}
			if (SkipVariants != null && WildcardUtil.Match(Code, inputStack.Collectible.Code, SkipVariants))
			{
				return false;
			}
			if (!CheckTags(inputStack, inputStack.Collectible))
			{
				return false;
			}
		}
		else
		{
			ItemStack itemStack = ResolvedItemStack;
			if (itemStack == null)
			{
				return false;
			}
			if (!itemStack.Satisfies(inputStack))
			{
				return false;
			}
			if (checkStackSize && inputStack.StackSize < Quantity)
			{
				return false;
			}
		}
		return true;
	}

	public virtual bool CheckTags(ItemStack inputStack, CollectibleObject collectible)
	{
		return CheckTags(Tags, inputStack, collectible);
	}

	public static bool CheckTags(ComplexTagCondition<TagSet> tags, ItemStack inputStack, CollectibleObject collectible)
	{
		return tags.Matches(collectible.GetTags(inputStack));
	}

	public virtual RecipeIngredientConsumeProperties GetConsumeProperties()
	{
		if (!Consume)
		{
			return new RecipeIngredientConsumeProperties
			{
				Consume = false,
				DurabilityChange = DurabilityChange,
				DurabilityCost = -DurabilityChange,
				Quantity = Quantity,
				BreakOnZeroDurability = Break
			};
		}
		if (IsTool)
		{
			return new RecipeIngredientConsumeProperties
			{
				Consume = false,
				DurabilityChange = -ToolDurabilityCost,
				DurabilityCost = ToolDurabilityCost,
				Quantity = Quantity,
				BreakOnZeroDurability = Break
			};
		}
		return new RecipeIngredientConsumeProperties
		{
			Consume = true,
			DurabilityChange = 0,
			DurabilityCost = 0,
			Quantity = Quantity,
			BreakOnZeroDurability = Break
		};
	}

	public virtual CraftingRecipeIngredient Clone()
	{
		CraftingRecipeIngredient craftingRecipeIngredient = new CraftingRecipeIngredient();
		CloneTo(craftingRecipeIngredient);
		return craftingRecipeIngredient;
	}

	[Obsolete("Use 'Clone()' instead")]
	public TResult CloneTo<TResult>() where TResult : CraftingRecipeIngredient, new()
	{
		return (TResult)Clone();
	}

	public override string ToString()
	{
		if (MatchingType != EnumRecipeMatchType.TagsOnly)
		{
			return $"{Type} code {Code}";
		}
		return $"{Type} tagged {Tags}";
	}

	/// <summary>
	/// Replaces {<paramref name="key" />} placeholders with <paramref name="value" /> in code an attributes
	/// </summary>
	/// <param name="key"></param>
	/// <param name="value"></param>
	public virtual void FillPlaceHolder(string key, string value)
	{
		if (Code != null)
		{
			Code = Code.CopyWithPath(Code.Path.Replace("{" + key + "}", value).DeDuplicate());
		}
		Attributes?.FillPlaceHolder(key, value);
		RecipeAttributes?.FillPlaceHolder(key, value);
	}

	public virtual void ToBytes(BinaryWriter writer)
	{
		writer.Write((int)MatchingType);
		writer.Write((int)Type);
		writer.Write(Code != null);
		if (Code != null)
		{
			writer.Write(Code.ToShortString());
		}
		writer.Write(Quantity);
		if (MatchingType == EnumRecipeMatchType.Exact)
		{
			ItemStack itemStack = resolvedItemStack;
			writer.Write(itemStack != null);
			itemStack?.ToBytes(writer);
		}
		writer.Write(Consume);
		writer.Write(DurabilityChange);
		writer.Write(AllowedVariants != null);
		if (AllowedVariants != null)
		{
			writer.Write(AllowedVariants.Length);
			for (int i = 0; i < AllowedVariants.Length; i++)
			{
				writer.Write(AllowedVariants[i]);
			}
		}
		writer.Write(SkipVariants != null);
		if (SkipVariants != null)
		{
			writer.Write(SkipVariants.Length);
			for (int j = 0; j < SkipVariants.Length; j++)
			{
				writer.Write(SkipVariants[j]);
			}
		}
		writer.Write(ReturnedStack?.ResolvedItemstack != null);
		if (ReturnedStack?.ResolvedItemstack != null)
		{
			ReturnedStack.ToBytes(writer);
		}
		string text = RecipeAttributes?.ToString();
		if (text != null)
		{
			writer.Write(value: true);
			writer.Write(text);
		}
		else
		{
			writer.Write(value: false);
		}
		writer.Write(Id ?? "");
		Tags.ToBytes(writer);
	}

	public virtual void FromBytes(BinaryReader reader, IWorldAccessor resolver)
	{
		MatchingType = (EnumRecipeMatchType)reader.ReadInt32();
		Type = (EnumItemClass)reader.ReadInt32();
		if (reader.ReadBoolean())
		{
			Code = new AssetLocation(reader.ReadString());
		}
		Quantity = reader.ReadInt32();
		if (MatchingType == EnumRecipeMatchType.Exact && reader.ReadBoolean())
		{
			resolvedItemStack = new ItemStack(reader, resolver);
		}
		Consume = reader.ReadBoolean();
		DurabilityChange = reader.ReadInt32();
		if (reader.ReadBoolean())
		{
			AllowedVariants = new string[reader.ReadInt32()];
			for (int i = 0; i < AllowedVariants.Length; i++)
			{
				AllowedVariants[i] = reader.ReadString().DeDuplicate();
			}
		}
		if (reader.ReadBoolean())
		{
			SkipVariants = new string[reader.ReadInt32()];
			for (int j = 0; j < SkipVariants.Length; j++)
			{
				SkipVariants[j] = reader.ReadString().DeDuplicate();
			}
		}
		if (reader.ReadBoolean())
		{
			JsonItemStack jsonItemStack = new JsonItemStack();
			jsonItemStack.FromBytes(reader, resolver.ClassRegistry);
			jsonItemStack.ResolvedItemStack?.ResolveBlockOrItem(resolver);
			ReturnedStack = jsonItemStack;
		}
		if (reader.ReadBoolean())
		{
			RecipeAttributes = new JsonObject(JToken.Parse(reader.ReadString()));
		}
		Id = reader.ReadString().DeDuplicate();
		Tags = ComplexTagConditionExtensions.FromBytes(reader);
	}

	protected virtual void CloneTo(object cloneTo)
	{
		if (cloneTo is CraftingRecipeIngredient craftingRecipeIngredient)
		{
			craftingRecipeIngredient.Code = Code?.Clone();
			craftingRecipeIngredient.Tags = Tags;
			craftingRecipeIngredient.Type = Type;
			craftingRecipeIngredient.Name = Name;
			craftingRecipeIngredient.Quantity = Quantity;
			craftingRecipeIngredient.IsTool = IsTool;
			craftingRecipeIngredient.ToolDurabilityCost = ToolDurabilityCost;
			craftingRecipeIngredient.MatchingType = MatchingType;
			craftingRecipeIngredient.Consume = Consume;
			craftingRecipeIngredient.DurabilityChange = DurabilityChange;
			craftingRecipeIngredient.Break = Break;
			craftingRecipeIngredient.AllowedVariants = AllowedVariants;
			craftingRecipeIngredient.SkipVariants = SkipVariants;
			craftingRecipeIngredient.resolvedItemStack = ResolvedItemStack?.Clone();
			craftingRecipeIngredient.ReturnedStack = ReturnedStack?.Clone();
			craftingRecipeIngredient.RecipeAttributes = RecipeAttributes?.Clone();
			craftingRecipeIngredient.Id = Id;
			craftingRecipeIngredient.Attributes = Attributes?.Clone();
			craftingRecipeIngredient.RecipeAttributes = RecipeAttributes?.Clone();
			craftingRecipeIngredient.world = world;
			craftingRecipeIngredient.deduplicationIndex = deduplicationIndex;
			craftingRecipeIngredient.ResolvedAttributes = ResolvedAttributes?.Clone();
		}
	}

	object ICloneable.Clone()
	{
		return Clone();
	}
}
