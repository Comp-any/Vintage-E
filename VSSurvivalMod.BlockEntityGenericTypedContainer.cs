using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BlockEntityGenericTypedContainer : BlockEntityOpenableContainer, IRotatable, IClaimTraverseable
{
	internal InventoryGeneric inventory;

	public string type;

	public string defaultType = "normal-generic";

	public int quantitySlots = 16;

	public int quantityColumns = 4;

	public string inventoryClassName = "chest";

	public string dialogTitleLangCode = "chestcontents";

	public bool retrieveOnly;

	public bool isPerPlayer;

	private float meshangle;

	private MeshData ownMesh;

	public Cuboidf[] collisionSelectionBoxes;

	private Vec3f rendererRot = new Vec3f();

	public virtual float MeshAngle
	{
		get
		{
			return meshangle;
		}
		set
		{
			meshangle = value;
			rendererRot.Y = value * (180f / (float)Math.PI);
		}
	}

	public virtual string DialogTitle => Lang.Get(dialogTitleLangCode);

	public override InventoryBase Inventory => inventory;

	public override string InventoryClassName => inventoryClassName;

	private BlockEntityAnimationUtil animUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

	public override void Initialize(ICoreAPI api)
	{
		defaultType = Block.Attributes?["defaultType"]?.AsString() ?? defaultType;
		if (type == null)
		{
			type = defaultType;
		}
		if (inventory == null)
		{
			InitInventory(Block);
		}
		base.Initialize(api);
		Inventory.OnInventoryOpened -= OnInventoryOpened;
		Inventory.OnInventoryClosed -= OnInventoryClosed;
	}

	public override void OnPlacementBySchematic(ICoreServerAPI api, IBlockAccessor blockAccessor, BlockPos pos, Dictionary<int, Dictionary<int, int>> replaceBlocks, int centerrockblockid, Block layerBlock, bool resolveImports)
	{
		base.OnPlacementBySchematic(api, blockAccessor, pos, replaceBlocks, centerrockblockid, layerBlock, resolveImports);
		if (inventory is InventoryPerPlayer inventoryPerPlayer)
		{
			inventoryPerPlayer.OnPlacementBySchematic();
		}
	}

	public override void OnBlockPlaced(ItemStack byItemStack = null)
	{
		if (byItemStack?.Attributes != null)
		{
			string text = byItemStack.Attributes.GetString("type", defaultType);
			bool flag = byItemStack.Attributes.GetBool("isPerPlayer");
			if (text != type || flag != isPerPlayer)
			{
				type = text;
				isPerPlayer = flag;
				InitInventory(Block);
				LateInitInventory();
			}
		}
		base.OnBlockPlaced();
	}

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
	{
		string text = type;
		type = tree.GetString("type", defaultType);
		MeshAngle = tree.GetFloat("meshAngle", MeshAngle);
		bool flag = isPerPlayer;
		isPerPlayer = tree.GetBool("isPerPlayer");
		if (inventory == null)
		{
			if (tree.HasAttribute("forBlockId"))
			{
				InitInventory(worldForResolving.GetBlock((ushort)tree.GetInt("forBlockId")));
			}
			else if (tree.HasAttribute("forBlockCode"))
			{
				InitInventory(worldForResolving.GetBlock(new AssetLocation(tree.GetString("forBlockCode"))));
			}
			else
			{
				if (tree.GetTreeAttribute("inventory").GetInt("qslots") == 8)
				{
					quantitySlots = 8;
					inventoryClassName = "basket";
					dialogTitleLangCode = "basketcontents";
					if (type == null)
					{
						type = "reed";
					}
				}
				InitInventory(null);
			}
		}
		else if (type != text || flag != isPerPlayer)
		{
			InitInventory(Block);
			if (Api == null)
			{
				Api = worldForResolving.Api;
			}
			LateInitInventory();
		}
		if (Api != null && Api.Side == EnumAppSide.Client)
		{
			ownMesh = null;
			MarkDirty(redrawOnClient: true);
		}
		base.FromTreeAttributes(tree, worldForResolving);
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		if (Block != null)
		{
			tree.SetString("forBlockCode", Block.Code.ToShortString());
		}
		if (isPerPlayer)
		{
			tree.SetBool("isPerPlayer", isPerPlayer);
		}
		if (type == null)
		{
			type = defaultType;
		}
		tree.SetString("type", type);
		tree.SetFloat("meshAngle", MeshAngle);
	}

	protected virtual void InitInventory(Block block)
	{
		if (block?.Attributes != null)
		{
			collisionSelectionBoxes = block.Attributes["collisionSelectionBoxes"]?[type]?.AsObject<Cuboidf[]>();
			inventoryClassName = block.Attributes["inventoryClassName"].AsString(inventoryClassName);
			dialogTitleLangCode = block.Attributes["dialogTitleLangCode"][type].AsString(dialogTitleLangCode);
			quantitySlots = block.Attributes["quantitySlots"][type].AsInt(quantitySlots);
			quantityColumns = block.Attributes["quantityColumns"][type].AsInt(4);
			retrieveOnly = block.Attributes["retrieveOnly"][type].AsBool();
			if (block.Attributes["typedOpenSound"][type].Exists)
			{
				OpenSound = block.Attributes["typedOpenSound"][type].AsObject<SoundAttributes?>(null, Block.Code.Domain, randomPitch: true) ?? OpenSound;
			}
			if (block.Attributes["typedCloseSound"][type].Exists)
			{
				CloseSound = block.Attributes["typedCloseSound"][type].AsObject<SoundAttributes?>(null, Block.Code.Domain, randomPitch: true) ?? CloseSound;
			}
		}
		if (isPerPlayer)
		{
			inventory = new InventoryPerPlayer(quantitySlots, null, null);
		}
		else
		{
			inventory = new InventoryGeneric(quantitySlots, null, null);
		}
		inventory.BaseWeight = 1f;
		inventory.OnGetSuitability = (ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge) => (isMerge ? (inventory.BaseWeight + 3f) : (inventory.BaseWeight + 1f)) + (float)((sourceSlot.Inventory is InventoryBasePlayer) ? 1 : 0);
		inventory.OnGetAutoPullFromSlot = GetAutoPullFromSlot;
		container.Reset();
		if (block?.Attributes != null)
		{
			if (block.Attributes["spoilSpeedMulByFoodCat"][type].Exists)
			{
				inventory.PerishableFactorByFoodCategory = block.Attributes["spoilSpeedMulByFoodCat"][type].AsObject<Dictionary<EnumFoodCategory, float>>();
			}
			if (block.Attributes["transitionSpeedMul"][type].Exists)
			{
				inventory.TransitionableSpeedMulByType = block.Attributes["transitionSpeedMul"][type].AsObject<Dictionary<EnumTransitionType, float>>();
			}
		}
		inventory.PutLocked = retrieveOnly;
		inventory.OnInventoryOpened += OnInvOpened;
		inventory.OnInventoryClosed += OnInvClosed;
	}

	public virtual void LateInitInventory()
	{
		Inventory.LateInitialize(InventoryClassName + "-" + Pos, Api);
		Inventory.ResolveBlocksOrItems();
		InventoryBase inventoryBase = Inventory;
		if ((object)inventoryBase.Pos == null)
		{
			inventoryBase.Pos = Pos;
		}
		container.LateInit();
		MarkDirty();
	}

	private ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
	{
		if (atBlockFace == BlockFacing.DOWN)
		{
			return inventory.FirstOrDefault((ItemSlot slot) => !slot.Empty);
		}
		return null;
	}

	protected virtual void OnInvOpened(IPlayer player)
	{
		OnInventoryOpened(player);
		inventory.PutLocked = retrieveOnly && player.WorldData.CurrentGameMode != EnumGameMode.Creative;
		if (Api.Side == EnumAppSide.Client)
		{
			OpenLid();
		}
	}

	public void OpenLid()
	{
		BlockEntityAnimationUtil blockEntityAnimationUtil = animUtil;
		if (blockEntityAnimationUtil != null && !blockEntityAnimationUtil.activeAnimationsByAnimCode.ContainsKey("lidopen"))
		{
			animUtil?.StartAnimation(new AnimationMetaData
			{
				Animation = "lidopen",
				Code = "lidopen",
				AnimationSpeed = 1.8f,
				EaseOutSpeed = 6f,
				EaseInSpeed = 15f
			});
		}
	}

	public void CloseLid()
	{
		BlockEntityAnimationUtil blockEntityAnimationUtil = animUtil;
		if (blockEntityAnimationUtil != null && blockEntityAnimationUtil.activeAnimationsByAnimCode.ContainsKey("lidopen"))
		{
			animUtil?.StopAnimation("lidopen");
		}
	}

	protected virtual void OnInvClosed(IPlayer player)
	{
		OnInventoryClosed(player);
		if (LidOpenEntityId.Count == 0)
		{
			CloseLid();
			if (Inventory.Empty && player.WorldData.CurrentGameMode == EnumGameMode.Survival)
			{
				JsonItemStack jsonItemStack = Block.Attributes["changeIntoWhenEmpty"][type].AsObject<JsonItemStack>();
				if (jsonItemStack != null)
				{
					Api.World.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), Pos, 0.5, player);
					jsonItemStack.Resolve(Api.World, $"Container {Block.Code} changeIntoWhenEmpty");
					Api.World.BlockAccessor.SetBlock(jsonItemStack.ResolvedItemstack.Block.Id, Pos, jsonItemStack.ResolvedItemstack);
				}
			}
		}
		inventory.PutLocked = retrieveOnly;
		GuiDialogBlockEntity guiDialogBlockEntity = invDialog;
		invDialog = null;
		if (guiDialogBlockEntity != null && guiDialogBlockEntity.IsOpened())
		{
			guiDialogBlockEntity?.TryClose();
		}
		guiDialogBlockEntity?.Dispose();
	}

	public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
	{
		if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			inventory.PutLocked = false;
		}
		if (Api.World is IServerWorldAccessor)
		{
			byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityInventory", DialogTitle, (byte)quantityColumns, inventory);
			((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, 5000, data);
			byPlayer.InventoryManager.OpenInventory(inventory);
			data = SerializerUtil.Serialize(new OpenContainerLidPacket(byPlayer.Entity.EntityId, LidOpenEntityId.Count > 0));
			((ICoreServerAPI)Api).Network.BroadcastBlockEntityPacket(Pos, 5001, data, (IServerPlayer)byPlayer);
		}
		return true;
	}

	private MeshData GenMesh(ITesselatorAPI tesselator)
	{
		BlockGenericTypedContainer blockGenericTypedContainer = Block as BlockGenericTypedContainer;
		if (Block == null)
		{
			blockGenericTypedContainer = (BlockGenericTypedContainer)(Block = Api.World.BlockAccessor.GetBlock(Pos) as BlockGenericTypedContainer);
		}
		if (blockGenericTypedContainer == null)
		{
			return null;
		}
		int num = (Block.Attributes?["rndTexNum"][type]?.AsInt()).GetValueOrDefault();
		string text = "typedContainerMeshes" + Block.Code.ToShortString();
		Dictionary<string, MeshData> orCreate = ObjectCacheUtil.GetOrCreate(Api, text, () => new Dictionary<string, MeshData>());
		string text2 = Block.Attributes?["shape"][type].AsString();
		if (text2 == null)
		{
			return null;
		}
		Shape value = null;
		if (animUtil != null)
		{
			string key = "typedContainerShapes";
			Dictionary<string, Shape> orCreate2 = ObjectCacheUtil.GetOrCreate(Api, key, () => new Dictionary<string, Shape>());
			string key2 = Block.FirstCodePart() + type + blockGenericTypedContainer.Subtype + "--" + text2 + "-" + num;
			if (!orCreate2.TryGetValue(key2, out value))
			{
				value = (orCreate2[key2] = blockGenericTypedContainer.GetShape(Api as ICoreClientAPI, text2));
			}
		}
		string key3 = type + blockGenericTypedContainer.Subtype + "-" + num;
		if (orCreate.TryGetValue(key3, out var value2) && value2 != null)
		{
			if (animUtil != null && animUtil.renderer == null)
			{
				animUtil.InitializeAnimator(type + "-" + text + "-" + blockGenericTypedContainer.Subtype, value2, value, rendererRot);
			}
			return value2;
		}
		if (num > 0)
		{
			num = GameMath.MurmurHash3Mod(Pos.X, Pos.Y, Pos.Z, num);
		}
		if (animUtil != null)
		{
			if (animUtil.renderer == null)
			{
				GenericContainerTextureSource texSource = new GenericContainerTextureSource
				{
					blockTextureSource = tesselator.GetTextureSource(Block, num),
					curType = type
				};
				value2 = animUtil.InitializeAnimator(type + "-" + text + "-" + blockGenericTypedContainer.Subtype, value, texSource, rendererRot);
			}
			return orCreate[key3] = value2;
		}
		value2 = blockGenericTypedContainer.GenMesh(Api as ICoreClientAPI, type, text2, tesselator, new Vec3f(), num);
		return orCreate[key3] = value2;
	}

	public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
	{
		if (!base.OnTesselation(mesher, tesselator))
		{
			if (ownMesh == null)
			{
				ownMesh = GenMesh(tesselator);
				if (ownMesh == null)
				{
					return false;
				}
			}
			mesher.AddMeshData(ownMesh.Clone().Rotate(0f, MeshAngle, 0f));
		}
		return true;
	}

	public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
	{
		MeshAngle = tree.GetFloat("meshAngle");
		MeshAngle -= (float)degreeRotation * ((float)Math.PI / 180f);
		tree.SetFloat("meshAngle", MeshAngle);
	}

	public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
	{
		base.GetBlockInfo(forPlayer, dsc);
		if (isPerPlayer)
		{
			dsc.AppendLine(Lang.Get("blockdesc-perplayerloot"));
		}
	}

	public bool AllowTraverse()
	{
		if (!retrieveOnly)
		{
			return isPerPlayer;
		}
		return true;
	}
}
