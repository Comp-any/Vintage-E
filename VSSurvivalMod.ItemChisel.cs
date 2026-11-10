using System;
using System.Linq;
using VSSurvivalMod.Systems.ChiselModes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class ItemChisel : Item
{
	public SkillItem[] ToolModes;

	private SkillItem addMatItem;

	public static bool AllowHalloweenEvent = true;

	public bool carvingTime
	{
		get
		{
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow.Month != 10)
			{
				return utcNow.Month == 11;
			}
			return true;
		}
	}

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		ToolModes = ObjectCacheUtil.GetOrCreate(api, "chiselToolModes", () =>
		{
			SkillItem[] array = new SkillItem[7]
			{
				new SkillItem
				{
					Code = new AssetLocation("1size"),
					Name = Lang.Get("1x1x1"),
					Data = new OneByChiselMode()
				},
				new SkillItem
				{
					Code = new AssetLocation("2size"),
					Name = Lang.Get("2x2x2"),
					Data = new TwoByChiselMode()
				},
				new SkillItem
				{
					Code = new AssetLocation("4size"),
					Name = Lang.Get("4x4x4"),
					Data = new FourByChiselMode()
				},
				new SkillItem
				{
					Code = new AssetLocation("8size"),
					Name = Lang.Get("8x8x8"),
					Data = new EightByChiselModeData()
				},
				new SkillItem
				{
					Code = new AssetLocation("rotate"),
					Name = Lang.Get("Rotate"),
					Data = new RotateChiselMode()
				},
				new SkillItem
				{
					Code = new AssetLocation("flip"),
					Name = Lang.Get("Flip"),
					Data = new FlipChiselMode()
				},
				new SkillItem
				{
					Code = new AssetLocation("rename"),
					Name = Lang.Get("Set name"),
					Data = new RenameChiselMode()
				}
			};
			ICoreClientAPI capi2 = api as ICoreClientAPI;
			if (capi2 != null)
			{
				array = array.Select((SkillItem i) =>
				{
					ChiselMode chiselMode = (ChiselMode)i.Data;
					return i.WithIcon(capi2, chiselMode.DrawAction(capi2));
				}).ToArray();
			}
			return array;
		});
		addMatItem = new SkillItem
		{
			Name = Lang.Get("chisel-addmat"),
			Code = new AssetLocation("addmat"),
			Enabled = false
		};
		if (api is ICoreClientAPI capi)
		{
			addMatItem = addMatItem.WithIcon(capi, "plus");
		}
	}

	public override void OnUnloaded(ICoreAPI api)
	{
		int num = 0;
		while (ToolModes != null && num < ToolModes.Length)
		{
			ToolModes[num]?.Dispose();
			num++;
		}
		addMatItem?.Dispose();
	}

	public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity)
	{
		ICoreClientAPI obj = api as ICoreClientAPI;
		if (obj != null && obj.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			return null;
		}
		return base.GetHeldTpUseAnimation(activeHotbarSlot, forEntity);
	}

	public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
	{
		ICoreClientAPI obj = api as ICoreClientAPI;
		if (obj != null && obj.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			return null;
		}
		return base.GetHeldTpHitAnimation(slot, byEntity);
	}

	public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
	{
		IPlayer player = (byEntity as EntityPlayer)?.Player;
		if ((byEntity == null || byEntity.LeftHandItemSlot?.Itemstack?.Collectible?.GetTool(byEntity.LeftHandItemSlot) != EnumTool.Hammer) && (player == null || player.WorldData.CurrentGameMode != EnumGameMode.Creative))
		{
			(api as ICoreClientAPI)?.TriggerIngameError(this, "nohammer", Lang.Get("Requires a hammer in the off hand"));
			handling = EnumHandHandling.PreventDefaultAction;
		}
		else if (!(blockSel?.Position == null))
		{
			BlockPos position = blockSel.Position;
			Block block = byEntity.World.BlockAccessor.GetBlock(position);
			ModSystemBlockReinforcement modSystem = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
			if (modSystem != null && modSystem.IsReinforced(position))
			{
				player.InventoryManager.ActiveHotbarSlot.MarkDirty();
			}
			else if (!byEntity.World.Claims.TryAccess(player, position, EnumBlockAccessFlags.BuildOrBreak))
			{
				player.InventoryManager.ActiveHotbarSlot.MarkDirty();
			}
			else if (!IsChiselingAllowedFor(api, position, block, player))
			{
				base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
			}
			else if (blockSel == null)
			{
				base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
			}
			else if (block is BlockChisel)
			{
				OnBlockInteract(byEntity.World, player, blockSel, isBreak: true, ref handling);
			}
		}
	}

	public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
	{
		base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
		if (handling == EnumHandHandling.PreventDefault)
		{
			return;
		}
		IPlayer player = (byEntity as EntityPlayer)?.Player;
		if (blockSel?.Position == null)
		{
			return;
		}
		BlockPos position = blockSel.Position;
		Block block = byEntity.World.BlockAccessor.GetBlock(position);
		if (block is BlockForge)
		{
			return;
		}
		if (player.InventoryManager.OffhandTool != EnumTool.Hammer && (player == null || player.WorldData.CurrentGameMode != EnumGameMode.Creative))
		{
			(api as ICoreClientAPI)?.TriggerIngameError(this, "nohammer", Lang.Get("Requires a hammer in the off hand"));
			handling = EnumHandHandling.PreventDefaultAction;
			return;
		}
		ModSystemBlockReinforcement modSystem = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
		if (modSystem != null && modSystem.IsReinforced(position))
		{
			player.InventoryManager.ActiveHotbarSlot.MarkDirty();
			return;
		}
		if (!byEntity.World.Claims.TryAccess(player, position, EnumBlockAccessFlags.BuildOrBreak))
		{
			player.InventoryManager.ActiveHotbarSlot.MarkDirty();
			return;
		}
		if (block is BlockGroundStorage)
		{
			ItemSlot firstNonEmptySlot = (api.World.BlockAccessor.GetBlockEntity(position) as BlockEntityGroundStorage).Inventory.FirstNonEmptySlot;
			if (firstNonEmptySlot != null && firstNonEmptySlot.Itemstack.Block != null && IsChiselingAllowedFor(api, position, firstNonEmptySlot.Itemstack.Block, player))
			{
				block = firstNonEmptySlot.Itemstack.Block;
			}
			if (block.Code.Path == "pumpkin-fruit-4" && (!carvingTime || !AllowHalloweenEvent))
			{
				player.InventoryManager.ActiveHotbarSlot.MarkDirty();
				api.World.BlockAccessor.MarkBlockDirty(position);
				return;
			}
		}
		if (!IsChiselingAllowedFor(api, position, block, player))
		{
			base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
			return;
		}
		if (block.Resistance > 100f)
		{
			if (api.Side == EnumAppSide.Client)
			{
				(api as ICoreClientAPI).TriggerIngameError(this, "tootoughtochisel", Lang.Get("This material is too strong to chisel"));
			}
			return;
		}
		if (blockSel == null)
		{
			base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
			return;
		}
		if (block is BlockChisel)
		{
			OnBlockInteract(byEntity.World, player, blockSel, isBreak: false, ref handling);
			return;
		}
		if (api is ICoreServerAPI coreServerAPI && coreServerAPI.Server.Config.LogBlockBreakPlace)
		{
			coreServerAPI.Logger.Build("{0} converted {1} to a chiseledblock at {2}", player.PlayerName, block.Code.ToString(), blockSel.Position);
		}
		Block block2 = byEntity.World.GetBlock(new AssetLocation("chiseledblock"));
		byEntity.World.BlockAccessor.SetBlock(block2.BlockId, blockSel.Position);
		if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityChisel blockEntityChisel)
		{
			blockEntityChisel.WasPlaced(block, null);
			if (carvingTime && block.Code.Path == "pumpkin-fruit-4")
			{
				blockEntityChisel.AddMaterial(api.World.GetBlock(new AssetLocation("creativeglow-35")));
			}
			handling = EnumHandHandling.PreventDefaultAction;
		}
	}

	public static bool IsChiselingAllowedFor(ICoreAPI api, BlockPos pos, Block block, IPlayer player)
	{
		if (block is BlockMicroBlock)
		{
			if (block is BlockChisel)
			{
				return true;
			}
			return false;
		}
		return IsValidChiselingMaterial(api, pos, block, player);
	}

	public static bool IsValidChiselingMaterial(ICoreAPI api, BlockPos pos, Block block, IPlayer player)
	{
		if (block is BlockChisel)
		{
			return true;
		}
		string text = api.World.Config.GetString("microblockChiseling");
		if (text == "off")
		{
			return false;
		}
		IConditionalChiselable conditionalChiselable = block.GetInterface<IConditionalChiselable>(api.World, pos);
		if (conditionalChiselable != null && ((conditionalChiselable != null && !conditionalChiselable.CanChisel(api.World, pos, player, out var errorCode)) || (conditionalChiselable != null && !conditionalChiselable.CanChisel(api.World, pos, player, out errorCode))))
		{
			(api as ICoreClientAPI)?.TriggerIngameError(conditionalChiselable, errorCode, Lang.Get(errorCode));
			return false;
		}
		bool flag = block.Attributes?["canChisel"].Exists ?? false;
		bool flag2 = block.Attributes?["canChisel"].AsBool() ?? false;
		if (flag2)
		{
			return true;
		}
		if (flag && !flag2)
		{
			return false;
		}
		if (block.DrawType != EnumDrawType.Cube && block.Shape?.Base.Path != "block/basic/cube")
		{
			return false;
		}
		if (block.HasBehavior<BlockBehaviorDecor>())
		{
			return false;
		}
		if (player != null && player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			return true;
		}
		if (block.BlockMaterial == EnumBlockMaterial.Water)
		{
			return false;
		}
		if (block.BlockMaterial == EnumBlockMaterial.Lava)
		{
			return false;
		}
		if (text == "stonewood")
		{
			if (block.Code.Path.Contains("mudbrick"))
			{
				return true;
			}
			if (block.BlockMaterial != EnumBlockMaterial.Wood && block.BlockMaterial != EnumBlockMaterial.Stone && block.BlockMaterial != EnumBlockMaterial.Ore)
			{
				return block.BlockMaterial == EnumBlockMaterial.Ceramic;
			}
			return true;
		}
		return true;
	}

	public void OnBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, bool isBreak, ref EnumHandHandling handling)
	{
		if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
		{
			byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
		}
		else if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityChisel blockEntityChisel)
		{
			int num = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Attributes.GetInt("materialId", -1);
			if (num >= 0)
			{
				blockEntityChisel.SetNowMaterialId(num);
			}
			blockEntityChisel.OnBlockInteract(byPlayer, blockSel, isBreak);
			handling = EnumHandHandling.PreventDefaultAction;
		}
	}

	public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
	{
		if (blockSel == null)
		{
			return null;
		}
		if (forPlayer.Entity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityChisel blockEntityChisel)
		{
			if (blockEntityChisel.BlockIds.Length <= 1)
			{
				addMatItem.Linebreak = true;
				return ToolModes.Append(addMatItem);
			}
			SkillItem[] array = new SkillItem[blockEntityChisel.BlockIds.Length + 1];
			for (int i = 0; i < blockEntityChisel.BlockIds.Length; i++)
			{
				Block block = api.World.GetBlock(blockEntityChisel.BlockIds[i]);
				ItemSlot dummySlot = new DummySlot();
				dummySlot.Itemstack = new ItemStack(block);
				array[i] = new SkillItem
				{
					Code = block.Code,
					Data = blockEntityChisel.BlockIds[i],
					Linebreak = (i % 7 == 0),
					Name = (block.GetInterface<ICustomChiselMaterialName>(api.World, null)?.GetName(dummySlot.Itemstack) ?? block.GetHeldItemName(dummySlot.Itemstack)),
					RenderHandler = (AssetLocation code, float dt, double atPosX, double atPosY) =>
					{
						float num = (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
						(api as ICoreClientAPI).Render.RenderItemstackToGui(dummySlot, atPosX + (double)(num / 2f), atPosY + (double)(num / 2f), 50.0, num / 2f, -1, shading: true, rotate: false, showStackSize: false);
					}
				};
			}
			array[^1] = addMatItem;
			addMatItem.Linebreak = (array.Length - 1) % 7 == 0;
			return ToolModes.Append(array);
		}
		return null;
	}

	public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
	{
		return slot.Itemstack.Attributes.GetInt("toolMode");
	}

	public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
	{
		if (blockSel == null)
		{
			return;
		}
		BlockPos position = blockSel.Position;
		ItemSlot mouseItemSlot = byPlayer.InventoryManager.MouseItemSlot;
		if (!mouseItemSlot.Empty && mouseItemSlot.Itemstack.Block != null)
		{
			BlockEntityChisel blockEntityChisel = api.World.BlockAccessor.GetBlockEntity(position) as BlockEntityChisel;
			ItemStack itemstack = mouseItemSlot.Itemstack;
			if (!IsValidChiselingMaterial(api, position, itemstack.Block, byPlayer))
			{
				return;
			}
			bool compareToPickBlock = byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative;
			_ = string.Empty;
			ICoreServerAPI coreServerAPI = api as ICoreServerAPI;
			Block block = itemstack.Block;
			if (block is BlockChisel)
			{
				int[] array = (itemstack.Attributes?["availMaterialQuantities"] as IntArrayAttribute)?.value;
				int[] array2 = (itemstack.Attributes?["materials"] as IntArrayAttribute)?.value;
				if (array2 != null)
				{
					for (int i = 0; i < array2.Length; i++)
					{
						if (array2[i] != 0 && array[i] > 0)
						{
							Block block2 = api.World.GetBlock(array2[i]);
							blockEntityChisel.AddMaterial(block2, out var _, compareToPickBlock: false, (ushort)array[i]);
						}
					}
					if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
					{
						mouseItemSlot.TakeOut(1);
						mouseItemSlot.MarkDirty();
					}
				}
			}
			else
			{
				blockEntityChisel.AddMaterial(block, out var isFull2, compareToPickBlock);
				if (!isFull2 && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
				{
					mouseItemSlot.TakeOut(1);
					mouseItemSlot.MarkDirty();
				}
			}
			if (coreServerAPI != null && coreServerAPI.Server.Config.LogBlockBreakPlace)
			{
				coreServerAPI.Logger.Build("{0} added chisel material {1} at {2}", byPlayer.PlayerName, block.Code.ToString(), position);
			}
			blockEntityChisel.MarkDirty();
			api.Event.PushEvent("keepopentoolmodedlg");
		}
		else if (toolMode > ToolModes.Length - 1)
		{
			int num = toolMode - ToolModes.Length;
			if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityChisel blockEntityChisel2 && blockEntityChisel2.BlockIds.Length > num)
			{
				slot.Itemstack.Attributes.SetInt("materialId", blockEntityChisel2.BlockIds[num]);
				slot.MarkDirty();
			}
		}
		else
		{
			slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
		}
	}
}
