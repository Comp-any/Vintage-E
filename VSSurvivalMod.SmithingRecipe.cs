using System;
using Vintagestory.API;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent;

[DocumentAsJson]
public class SmithingRecipe : LayeredVoxelRecipe, IConcreteCloneable<SmithingRecipe>, ICloneable
{
	public AssetLocation? Code;

	public override int QuantityLayers => 6;

	public override string RecipeCategoryCode => "smithing";

	protected override bool RotateRecipe => true;

	protected override void FillPlaceHolder(string key, string value)
	{
		if (Code != null)
		{
			Code = Code.CopyWithPath(Code.Path.Replace("{" + key + "}", value));
		}
		base.FillPlaceHolder(key, value);
	}

	public override SmithingRecipe Clone()
	{
		SmithingRecipe smithingRecipe = new SmithingRecipe();
		CloneTo(smithingRecipe);
		smithingRecipe.Code = Code?.Clone();
		return smithingRecipe;
	}
}
