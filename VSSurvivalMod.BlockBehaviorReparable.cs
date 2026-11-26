using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

[DocumentAsJson]
[AddDocumentationProperty("Reparability", "The amount of glue needed for a full repair (abstract units corresponding to 1 resin, PLUS ONE), e.g. 5 resin is shown as 6.   0 means unspecified (we don't use the repair system), -1 means cannot be repaired will alway shatter.", "System.Int32", "Recommended", "0", false)]
public class BlockBehaviorReparable : BlockBehavior
{
	public BlockBehaviorReparable(Block block)
		: base(block)
	{
	}

	public virtual void Initialize(string type, BEBehaviorShapeFromAttributes bec)
	{
		BlockShapeFromAttributes clutterBlock = bec.clutterBlock;
		IShapeTypeProps typeProps = clutterBlock.GetTypeProps(type, null, bec);
		if (typeProps != null)
		{
			int num = typeProps.Reparability;
			if (num == 0)
			{
				num = clutterBlock.Attributes["reparability"].AsInt();
			}
			bec.reparability = num;
			if (num == 1)
			{
				bec.repairState = 1f;
			}
		}
	}

	public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropChanceMultiplier, ref EnumHandling handling)
	{
		handling = EnumHandling.PreventDefault;
		BEBehaviorShapeFromAttributes bEBehavior = block.GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior == null || ShatterWhenBroken(world, bEBehavior, GetRule(world)))
		{
			if (byPlayer is IServerPlayer serverPlayer)
			{
				serverPlayer.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "clutter-didshatter", Lang.GetMatchingL(serverPlayer.LanguageCode, bEBehavior?.GetFullCode() ?? ((string)block.Code)));
			}
			world.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), pos, 0.0, null, randomizePitch: false, 12f);
			return Array.Empty<ItemStack>();
		}
		ItemStack itemStack = block.OnPickBlock(world, pos);
		itemStack.Attributes.SetBool("collected", value: true);
		return new ItemStack[1] { itemStack };
	}

	public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
	{
		BEBehaviorShapeFromAttributes bEBehavior = block.GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior == null || bEBehavior.Collected)
		{
			return base.GetPlacedBlockInfo(world, pos, forPlayer);
		}
		if (world.Claims.TestAccess(forPlayer, pos, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted)
		{
			return "";
		}
		EnumClutterDropRule rule = GetRule(world);
		if (rule == EnumClutterDropRule.Reparable)
		{
			if (bEBehavior.reparability > 0)
			{
				int num = GameMath.Clamp((int)(bEBehavior.repairState * 100.001f), 0, 100);
				if (num < 100)
				{
					return Lang.Get("clutter-reparable") + "\n" + Lang.Get("{0}% repaired", num) + "\n";
				}
				return Lang.Get("clutter-fullyrepaired", num) + "\n";
			}
			if (bEBehavior.reparability < 0)
			{
				return Lang.Get("clutter-willshatter") + "\n";
			}
		}
		if (rule == EnumClutterDropRule.NeverObtain)
		{
			return Lang.Get("clutter-willshatter") + "\n";
		}
		return "";
	}

	public virtual bool ShatterWhenBroken(IWorldAccessor world, BEBehaviorShapeFromAttributes bec, EnumClutterDropRule configRule)
	{
		if (bec.Collected)
		{
			return false;
		}
		return configRule switch
		{
			EnumClutterDropRule.NeverObtain => true, 
			EnumClutterDropRule.AlwaysObtain => false, 
			EnumClutterDropRule.Reparable => world.Rand.NextDouble() > (double)bec.repairState, 
			_ => true, 
		};
	}

	public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
	{
		if (world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
		{
			_ = byPlayer.Entity;
			ItemSlot activeHotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
			float itemRepairAmount = GetItemRepairAmount(world, activeHotbarSlot);
			if (itemRepairAmount > 0f)
			{
				EnumClutterDropRule rule = GetRule(world);
				string text = null;
				string text2 = null;
				if (rule == EnumClutterDropRule.Reparable)
				{
					BEBehaviorShapeFromAttributes bEBehavior = base.block.GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position);
					if (bEBehavior == null)
					{
						text = "clutter-error";
					}
					else if (bEBehavior.repairState < 1f && bEBehavior.reparability > 1)
					{
						if (itemRepairAmount < 0.001f)
						{
							text = "clutter-gluehardened";
						}
						else
						{
							bEBehavior.repairState += itemRepairAmount * 5f / (float)(bEBehavior.reparability - 1);
							if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
							{
								if (activeHotbarSlot.Itemstack.Collectible is IBlockMealContainer blockMealContainer)
								{
									float num = blockMealContainer.GetQuantityServings(world, activeHotbarSlot.Itemstack) - 1f;
									blockMealContainer.SetQuantityServings(world, activeHotbarSlot.Itemstack, num);
									if (num <= 0f)
									{
										string text3 = activeHotbarSlot.Itemstack.Collectible.Attributes["emptiedBlockCode"].AsString();
										if (text3 != null)
										{
											Block block = world.GetBlock(new AssetLocation(text3));
											if (block != null)
											{
												activeHotbarSlot.Itemstack = new ItemStack(block);
											}
										}
									}
									activeHotbarSlot.MarkDirty();
								}
								else
								{
									CollectibleObject collectible = activeHotbarSlot.Itemstack.Collectible;
									BlockLiquidContainerBase cont = collectible as BlockLiquidContainerBase;
									if (cont != null)
									{
										ItemStack content = cont.GetContent(activeHotbarSlot.Itemstack);
										float num2 = content.ItemAttributes["repairGain"].AsFloat(0.2f);
										float num3 = cont.GetContentProps(content)?.ItemsPerLitre ?? 100f;
										int moved = (int)(itemRepairAmount / num2 * num3);
										cont.SplitStackAndPerformAction(byPlayer.Entity, activeHotbarSlot, (ItemStack stack) => cont.TryTakeContent(stack, moved)?.StackSize ?? 0);
										activeHotbarSlot.MarkDirty();
									}
									else
									{
										activeHotbarSlot.TakeOut(1);
									}
								}
							}
							text = "clutter-repaired";
							text2 = bEBehavior.GetFullCode();
							if (world.Side == EnumAppSide.Client)
							{
								AssetLocation location = AssetLocation.Create("sounds/player/gluerepair");
								world.PlaySoundAt(location, blockSel.Position, 0.0, byPlayer, randomizePitch: true, 8f);
							}
						}
					}
					else
					{
						text = "clutter-norepair";
					}
				}
				else
				{
					text = ((rule == EnumClutterDropRule.AlwaysObtain) ? "clutter-alwaysobtain" : "clutter-neverobtain");
				}
				if (byPlayer is IServerPlayer serverPlayer && text != null)
				{
					if (text2 == null)
					{
						serverPlayer.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, text);
					}
					else
					{
						serverPlayer.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, text, Lang.GetMatchingL(serverPlayer.LanguageCode, text2));
					}
				}
				handling = EnumHandling.Handled;
				return true;
			}
		}
		handling = EnumHandling.PassThrough;
		return false;
	}

	private float GetItemRepairAmount(IWorldAccessor world, ItemSlot slot)
	{
		if (slot.Empty)
		{
			return 0f;
		}
		ItemStack itemstack = slot.Itemstack;
		JsonObject attributes = itemstack.Collectible.Attributes;
		if (attributes != null && attributes["repairGain"].Exists)
		{
			return itemstack.Collectible.Attributes["repairGain"].AsFloat(0.2f);
		}
		if (itemstack.Collectible is IBlockMealContainer blockMealContainer)
		{
			ItemStack[] nonEmptyContents = blockMealContainer.GetNonEmptyContents(world, itemstack);
			if (nonEmptyContents.Length != 0 && nonEmptyContents[0] != null && nonEmptyContents[0].Collectible.Code.PathStartsWith("glueportion"))
			{
				return nonEmptyContents[0].Collectible.Attributes["repairGain"].AsFloat(0.2f);
			}
			string recipeCode = blockMealContainer.GetRecipeCode(world, itemstack);
			if (recipeCode == null)
			{
				return 0f;
			}
			Item item = world.GetItem(new AssetLocation(recipeCode));
			if (item != null)
			{
				JsonObject attributes2 = item.Attributes;
				if (attributes2 != null && attributes2["repairGain"].Exists)
				{
					return item.Attributes["repairGain"].AsFloat(0.2f) * Math.Min(1f, blockMealContainer.GetQuantityServings(world, itemstack));
				}
			}
		}
		if (itemstack.Collectible is BlockLiquidContainerBase blockLiquidContainerBase)
		{
			ItemStack content = blockLiquidContainerBase.GetContent(itemstack);
			if (content != null)
			{
				JsonObject itemAttributes = content.ItemAttributes;
				if (itemAttributes != null && itemAttributes["repairGain"].Exists)
				{
					return content.ItemAttributes["repairGain"].AsFloat(0.2f) * Math.Min(1f, ((float)content.StackSize / blockLiquidContainerBase.GetContentProps(itemstack)?.ItemsPerLitre) ?? 1f);
				}
			}
		}
		return 0f;
	}

	protected EnumClutterDropRule GetRule(IWorldAccessor world)
	{
		string text = world.Config.GetString("clutterObtainable", "ifrepaired").ToLowerInvariant();
		if (text == "yes")
		{
			return EnumClutterDropRule.AlwaysObtain;
		}
		if (text == "no")
		{
			return EnumClutterDropRule.NeverObtain;
		}
		return EnumClutterDropRule.Reparable;
	}
}
