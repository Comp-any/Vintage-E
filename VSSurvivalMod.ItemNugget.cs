using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Vintagestory.GameContent;

public class ItemNugget : Item
{
	public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
	{
		ItemSlot itemSlot = allInputslots.FirstOrDefault((ItemSlot slot) => slot.Itemstack?.Collectible is ItemOre);
		if (itemSlot != null)
		{
			int num = itemSlot.Itemstack.ItemAttributes["metalUnits"].AsInt(5);
			outputSlot.Itemstack.StackSize = Math.Max(1, num / 5);
		}
		base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		CombustibleProperties combustibleProperties = GetCombustibleProperties(world, inSlot.Itemstack, null);
		if (combustibleProperties?.SmeltedStack == null)
		{
			base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
			return;
		}
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		string text = combustibleProperties.SmeltingType.ToString().ToLowerInvariant();
		int smeltedRatio = combustibleProperties.SmeltedRatio;
		float num = (float)combustibleProperties.SmeltedStack.ResolvedItemstack.StackSize * 100f / (float)smeltedRatio;
		string text2 = combustibleProperties.SmeltedStack.ResolvedItemstack.Collectible?.Variant?["metal"];
		string text3 = Lang.Get("material-" + text2);
		if (text2 == null)
		{
			text3 = combustibleProperties.SmeltedStack.ResolvedItemstack.GetName();
		}
		string value = Lang.Get("game:smeltdesc-" + text + "ore-plural", num.ToString("0.#"), text3);
		dsc.AppendLine(value);
	}
}
