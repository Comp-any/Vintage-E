namespace Vintagestory.API.Common;

public struct RecipeIngredientConsumeProperties
{
	/// <summary>
	/// Should ingredient be consumed on crafting.<br />
	/// If set to true <see cref="P:Vintagestory.API.Common.RecipeIngredientConsumeProperties.Quantity" /> will be removed from ItemStack.
	/// Other wise <see cref="P:Vintagestory.API.Common.RecipeIngredientConsumeProperties.DurabilityChange" /> will be applied to item, if it is not equal to 0.
	/// </summary>
	public bool Consume { get; set; }

	/// <summary>
	/// How ItemStack size will be reduced.
	/// </summary>
	public int Quantity { get; set; }

	/// <summary>
	/// Durability change on being used for crafting.<br />
	/// Positive number will result in durability increase. Negative in decrease.
	/// </summary>
	public int DurabilityChange { get; set; }

	/// <summary>
	/// Equal to -DurabilityChange. Durability of item will be reduced by this amount if it is not consumed
	/// </summary>
	public int DurabilityCost { get; set; }

	/// <summary>
	/// If set to False item will not break when it durability reaches zero.
	/// </summary>
	public bool BreakOnZeroDurability { get; set; }
}
