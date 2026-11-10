using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class ItemScythe : ItemShears
{
	private string[] allowedPrefixes;

	private string[] disallowedSuffixes;

	private SkillItem[] modes;

	private bool trimMode;

	public override int MultiBreakQuantity => 5;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		allowedPrefixes = Attributes["codePrefixes"].AsArray<string>();
		disallowedSuffixes = Attributes["disallowedSuffixes"].AsArray<string>();
		ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
		modes = new SkillItem[2]
		{
			new SkillItem
			{
				Code = new AssetLocation("trim grass"),
				Name = Lang.Get("Trim grass")
			},
			new SkillItem
			{
				Code = new AssetLocation("remove grass"),
				Name = Lang.Get("Remove grass")
			}
		};
		if (coreClientAPI != null)
		{
			modes[0].WithIcon(coreClientAPI, coreClientAPI.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/scythetrim.svg"), 48, 48, 5, -1));
			modes[0].TexturePremultipliedAlpha = false;
			modes[1].WithIcon(coreClientAPI, coreClientAPI.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/scytheremove.svg"), 48, 48, 5, -1));
			modes[1].TexturePremultipliedAlpha = false;
		}
	}

	public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
	{
		return base.GetHeldInteractionHelp(inSlot).Append(new WorldInteraction
		{
			ActionLangCode = "heldhelp-settoolmode",
			HotKeyCode = "toolmodeselect"
		});
	}

	public override bool CanMultiBreak(Block block)
	{
		for (int i = 0; i < allowedPrefixes.Length; i++)
		{
			if (!block.Code.PathStartsWith(allowedPrefixes[i]))
			{
				continue;
			}
			if (disallowedSuffixes != null)
			{
				for (int j = 0; j < disallowedSuffixes.Length; j++)
				{
					if (block.Code.Path.EndsWithOrdinal(disallowedSuffixes[j]))
					{
						return false;
					}
				}
			}
			return true;
		}
		return false;
	}

	public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
	{
		base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
		if (blockSel != null)
		{
			IPlayer player = (byEntity as EntityPlayer)?.Player;
			if (byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
			{
				byEntity.Attributes.SetBool("didBreakBlocks", value: false);
				byEntity.Attributes.SetBool("didPlayScytheSound", value: false);
				handling = EnumHandHandling.PreventDefault;
			}
		}
	}

	public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
	{
		performActions(secondsPassed, byEntity, slot, blockSelection);
		if (api.Side == EnumAppSide.Server)
		{
			return true;
		}
		return secondsPassed < 2f;
	}

	public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
	{
		performActions(secondsPassed, byEntity, slot, blockSelection);
	}

	private void performActions(float secondsPassed, EntityAgent byEntity, ItemSlot slot, BlockSelection blockSelection)
	{
		if (blockSelection == null)
		{
			return;
		}
		api.World.BlockAccessor.GetBlock(blockSelection.Position);
		IPlayer player = (byEntity as EntityPlayer)?.Player;
		bool flag = CanMultiBreak(api.World.BlockAccessor.GetBlock(blockSelection.Position));
		if (flag && secondsPassed > 0.75f && !byEntity.Attributes.GetBool("didPlayScytheSound"))
		{
			api.World.PlaySoundAt(new AssetLocation("sounds/tool/scythe1"), byEntity, player, randomizePitch: true, 16f);
			byEntity.Attributes.SetBool("didPlayScytheSound", value: true);
		}
		if (flag && secondsPassed > 1.05f && !byEntity.Attributes.GetBool("didBreakBlocks"))
		{
			if (byEntity.World.Side == EnumAppSide.Server && byEntity.World.Claims.TryAccess(player, blockSelection.Position, EnumBlockAccessFlags.BuildOrBreak))
			{
				trimMode = slot.Itemstack.Attributes.GetInt("toolMode") == 0;
				OnBlockBrokenWith(byEntity.World, byEntity, slot, blockSelection);
			}
			byEntity.Attributes.SetBool("didBreakBlocks", value: true);
		}
	}

	protected override void breakMultiBlock(BlockPos pos, IPlayer plr)
	{
		if (trimMode)
		{
			Block block = api.World.BlockAccessor.GetBlock(pos);
			Block block2 = api.World.GetBlock(block.CodeWithVariant("tallgrass", "eaten"));
			bool flag = block.Variant.ContainsKey("tallgrass");
			if (flag && block == block2)
			{
				return;
			}
			if (flag && block2 != null)
			{
				api.World.BlockAccessor.BreakBlock(pos, plr);
				api.World.BlockAccessor.MarkBlockDirty(pos);
				api.World.BlockAccessor.SetBlock(block2.BlockId, pos);
				if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityTransient blockEntityTransient)
				{
					blockEntityTransient.ConvertToOverride = block.Code.ToShortString();
				}
				return;
			}
		}
		base.breakMultiBlock(pos, plr);
	}

	public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
	{
		return modes;
	}

	public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
	{
		slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
	}

	public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
	{
		return slot.Itemstack.Attributes.GetInt("toolMode");
	}

	public override void OnUnloaded(ICoreAPI api)
	{
		int num = 0;
		while (modes != null && num < modes.Length)
		{
			modes[num]?.Dispose();
			num++;
		}
	}
}
