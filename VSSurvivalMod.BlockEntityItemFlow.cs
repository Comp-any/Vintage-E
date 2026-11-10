using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;

namespace Vintagestory.GameContent;

public class BlockEntityItemFlow : BlockEntityOpenableContainer
{
	internal InventoryGeneric inventory;

	public BlockFacing[] PullFaces = Array.Empty<BlockFacing>();

	public BlockFacing[] PushFaces = Array.Empty<BlockFacing>();

	public BlockFacing[] AcceptFromFaces = Array.Empty<BlockFacing>();

	public string inventoryClassName = "hopper";

	public string ItemFlowObjectLangCode = "hopper-contents";

	public int QuantitySlots = 4;

	protected float itemFlowRate = 1f;

	public BlockFacing LastReceivedFromDir;

	public int MaxHorizontalTravel = 3;

	private int checkRateMs;

	private float itemFlowAccum;

	public SoundAttributes TumbleSound;

	public virtual float ItemFlowRate => itemFlowRate;

	public override InventoryBase Inventory => inventory;

	public override string InventoryClassName => inventoryClassName;

	public BlockEntityItemFlow()
	{
		OpenSound = new SoundAttributes(AssetLocation.Create("sounds/block/hopperopen"), withRandomPitch: true);
		CloseSound.Location = null;
		TumbleSound = new SoundAttributes(AssetLocation.Create("sounds/block/hoppertumble"), withRandomPitch: true)
		{
			Range = 8f,
			Volume = new NatFloat(0.5f, 0f, EnumDistribution.UNIFORM)
		};
	}

	private void InitInventory()
	{
		parseBlockProperties();
		if (inventory == null)
		{
			inventory = new InventoryGeneric(QuantitySlots, null, null);
			inventory.OnInventoryClosed += OnInvClosed;
			inventory.OnInventoryOpened += OnInvOpened;
			inventory.SlotModified += OnSlotModified;
			inventory.OnGetAutoPushIntoSlot = GetAutoPushIntoSlot;
			inventory.OnGetAutoPullFromSlot = GetAutoPullFromSlot;
		}
	}

	private void parseBlockProperties()
	{
		if (Block?.Attributes == null)
		{
			return;
		}
		if (Block.Attributes["pullFaces"].Exists)
		{
			string[] array = Block.Attributes["pullFaces"].AsArray<string>();
			PullFaces = new BlockFacing[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				PullFaces[i] = BlockFacing.FromCode(array[i]);
			}
		}
		if (Block.Attributes["pushFaces"].Exists)
		{
			string[] array2 = Block.Attributes["pushFaces"].AsArray<string>();
			PushFaces = new BlockFacing[array2.Length];
			for (int j = 0; j < array2.Length; j++)
			{
				PushFaces[j] = BlockFacing.FromCode(array2[j]);
			}
		}
		if (Block.Attributes["acceptFromFaces"].Exists)
		{
			string[] array3 = Block.Attributes["acceptFromFaces"].AsArray<string>();
			AcceptFromFaces = new BlockFacing[array3.Length];
			for (int k = 0; k < array3.Length; k++)
			{
				AcceptFromFaces[k] = BlockFacing.FromCode(array3[k]);
			}
		}
		itemFlowRate = Block.Attributes["item-flowrate"].AsFloat(itemFlowRate);
		checkRateMs = Block.Attributes["item-checkrateMs"].AsInt(200);
		inventoryClassName = Block.Attributes["inventoryClassName"].AsString(inventoryClassName);
		ItemFlowObjectLangCode = Block.Attributes["itemFlowObjectLangCode"].AsString(ItemFlowObjectLangCode);
		QuantitySlots = Block.Attributes["quantitySlots"].AsInt(QuantitySlots);
	}

	private ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
	{
		PushFaces.Contains(atBlockFace, null);
		return null;
	}

	private ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
	{
		if (PullFaces.Contains(atBlockFace, null) || AcceptFromFaces.Contains(atBlockFace, null))
		{
			return inventory[0];
		}
		return null;
	}

	private void OnSlotModified(int slot)
	{
		Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
	}

	protected virtual void OnInvOpened(IPlayer player)
	{
		inventory.PutLocked = false;
	}

	protected virtual void OnInvClosed(IPlayer player)
	{
		invDialog?.Dispose();
		invDialog = null;
	}

	public override void Initialize(ICoreAPI api)
	{
		InitInventory();
		base.Initialize(api);
		if (api is ICoreServerAPI)
		{
			RegisterDelayedCallback((float dt) =>
			{
				RegisterGameTickListener(MoveItem, checkRateMs);
			}, 10 + api.World.Rand.Next(200));
		}
		TumbleSound = Block.Attributes?["tumbleSound"]?.AsObject<SoundAttributes?>(null, Block.Code.Domain, randomPitch: true) ?? TumbleSound;
	}

	public void MoveItem(float dt)
	{
		itemFlowAccum = Math.Min(itemFlowAccum + ItemFlowRate, Math.Max(1f, ItemFlowRate * 2f));
		if (itemFlowAccum < 1f)
		{
			return;
		}
		if (PushFaces != null && PushFaces.Length != 0 && !inventory.Empty)
		{
			ItemStack? itemstack = inventory.First((ItemSlot slot) => !slot.Empty).Itemstack;
			BlockFacing blockFacing = PushFaces[Api.World.Rand.Next(PushFaces.Length)];
			int num = itemstack.Attributes.GetInt("chuteDir", -1);
			BlockFacing blockFacing2 = ((num >= 0 && PushFaces.Contains(BlockFacing.ALLFACES[num], null)) ? BlockFacing.ALLFACES[num] : null);
			if (blockFacing2 != null)
			{
				if (Api.World.BlockAccessor.GetChunkAtBlockPos(Pos.AddCopy(blockFacing2)) == null)
				{
					return;
				}
				if (!TrySpitOut(blockFacing2) && !TryPushInto(blockFacing2) && !TrySpitOut(blockFacing) && blockFacing != blockFacing2.Opposite && !TryPushInto(blockFacing) && PullFaces.Length != 0)
				{
					BlockFacing blockFacing3 = PullFaces[Api.World.Rand.Next(PullFaces.Length)];
					if (blockFacing3.IsHorizontal && !TryPushInto(blockFacing3))
					{
						TrySpitOut(blockFacing3);
					}
				}
			}
			else
			{
				if (Api.World.BlockAccessor.GetChunkAtBlockPos(Pos.AddCopy(blockFacing)) == null)
				{
					return;
				}
				if (!TrySpitOut(blockFacing) && !TryPushInto(blockFacing) && PullFaces != null && PullFaces.Length != 0)
				{
					BlockFacing blockFacing4 = PullFaces[Api.World.Rand.Next(PullFaces.Length)];
					if (blockFacing4.IsHorizontal && !TryPushInto(blockFacing4))
					{
						TrySpitOut(blockFacing4);
					}
				}
			}
		}
		if (PullFaces != null && PullFaces.Length != 0 && inventory.Empty)
		{
			BlockFacing inputFace = PullFaces[Api.World.Rand.Next(PullFaces.Length)];
			TryPullFrom(inputFace);
		}
	}

	private void TryPullFrom(BlockFacing inputFace)
	{
		BlockPos blockPos = Pos.AddCopy(inputFace);
		BlockEntityContainer blockEntity = Api.World.BlockAccessor.GetBlock(blockPos).GetBlockEntity<BlockEntityContainer>(blockPos);
		if (blockEntity == null)
		{
			return;
		}
		if (blockEntity.Block is BlockChute blockChute)
		{
			string?[]? array = blockChute.Attributes["pushFaces"].AsArray<string>();
			if (array != null && array.Contains(inputFace.Opposite.Code))
			{
				return;
			}
		}
		ItemSlot autoPullFromSlot = blockEntity.Inventory.GetAutoPullFromSlot(inputFace.Opposite);
		ItemSlot itemSlot = ((autoPullFromSlot == null) ? null : inventory.GetBestSuitedSlot(autoPullFromSlot).slot);
		BlockEntityItemFlow blockEntityItemFlow = blockEntity as BlockEntityItemFlow;
		if (autoPullFromSlot == null || itemSlot == null || (blockEntityItemFlow != null && !itemSlot.Empty))
		{
			return;
		}
		ItemStackMoveOperation op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.DirectMerge, (int)itemFlowAccum);
		int num = autoPullFromSlot.Itemstack.Attributes.GetInt("chuteQHTravelled");
		if (num >= MaxHorizontalTravel)
		{
			return;
		}
		int num2 = autoPullFromSlot.TryPutInto(itemSlot, ref op);
		if (num2 > 0)
		{
			if (blockEntityItemFlow != null)
			{
				itemSlot.Itemstack.Attributes.SetInt("chuteQHTravelled", inputFace.IsHorizontal ? (num + 1) : 0);
				itemSlot.Itemstack.Attributes.SetInt("chuteDir", inputFace.Opposite.Index);
			}
			else
			{
				itemSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
				itemSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
			}
			autoPullFromSlot.MarkDirty();
			itemSlot.MarkDirty();
			MarkDirty();
			blockEntityItemFlow?.MarkDirty();
		}
		if (num2 > 0 && Api.World.Rand.NextDouble() < 0.2)
		{
			Api.World.PlaySoundAt(TumbleSound, Pos, 0.0);
			itemFlowAccum -= num2;
		}
	}

	private bool TryPushInto(BlockFacing outputFace)
	{
		BlockPos blockPos = Pos.AddCopy(outputFace);
		BlockEntityContainer blockEntity = Api.World.BlockAccessor.GetBlock(blockPos).GetBlockEntity<BlockEntityContainer>(blockPos);
		if (blockEntity != null)
		{
			ItemSlot itemSlot = inventory.FirstOrDefault((ItemSlot slot) => !slot.Empty);
			if ((itemSlot?.Itemstack?.StackSize).GetValueOrDefault() == 0)
			{
				return false;
			}
			int num = itemSlot.Itemstack.Attributes.GetInt("chuteQHTravelled");
			int value = itemSlot.Itemstack.Attributes.GetInt("chuteDir");
			if (outputFace.IsHorizontal && num >= MaxHorizontalTravel)
			{
				return false;
			}
			itemSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
			itemSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
			ItemSlot autoPushIntoSlot = blockEntity.Inventory.GetAutoPushIntoSlot(outputFace.Opposite, itemSlot);
			BlockEntityItemFlow blockEntityItemFlow = blockEntity as BlockEntityItemFlow;
			if (autoPushIntoSlot != null && (blockEntityItemFlow == null || autoPushIntoSlot.Empty))
			{
				int requestedQuantity = (int)itemFlowAccum;
				ItemStackMoveOperation op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.DirectMerge, requestedQuantity);
				int num2 = itemSlot.TryPutInto(autoPushIntoSlot, ref op);
				if (num2 > 0)
				{
					if (Api.World.Rand.NextDouble() < 0.2)
					{
						Api.World.PlaySoundAt(TumbleSound, Pos, 0.0);
					}
					if (blockEntityItemFlow != null)
					{
						autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteQHTravelled", outputFace.IsHorizontal ? (num + 1) : 0);
						if (blockEntityItemFlow is BlockEntityArchimedesScrew)
						{
							autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteDir", BlockFacing.UP.Index);
						}
						else
						{
							autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteDir", outputFace.Index);
						}
					}
					else
					{
						autoPushIntoSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
						autoPushIntoSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
					}
					itemSlot.MarkDirty();
					autoPushIntoSlot.MarkDirty();
					MarkDirty();
					blockEntityItemFlow?.MarkDirty();
					itemFlowAccum -= num2;
					return true;
				}
				itemSlot.Itemstack.Attributes.SetInt("chuteDir", value);
			}
		}
		return false;
	}

	private bool TrySpitOut(BlockFacing outputFace)
	{
		if (Api.World.BlockAccessor.GetBlock(Pos.AddCopy(outputFace)).Replaceable >= 6000)
		{
			ItemSlot? itemSlot = inventory.FirstOrDefault((ItemSlot slot) => !slot.Empty);
			ItemStack itemStack = itemSlot.TakeOut((int)itemFlowAccum);
			itemFlowAccum -= itemStack.StackSize;
			itemStack.Attributes.RemoveAttribute("chuteQHTravelled");
			itemStack.Attributes.RemoveAttribute("chuteDir");
			float num = outputFace.Normalf.X / 10f + ((float)Api.World.Rand.NextDouble() / 20f - 0.05f) * (float)Math.Sign(outputFace.Normalf.X);
			float num2 = outputFace.Normalf.Y / 10f + ((float)Api.World.Rand.NextDouble() / 20f - 0.05f) * (float)Math.Sign(outputFace.Normalf.Y);
			float num3 = outputFace.Normalf.Z / 10f + ((float)Api.World.Rand.NextDouble() / 20f - 0.05f) * (float)Math.Sign(outputFace.Normalf.Z);
			Api.World.SpawnItemEntity(itemStack, Pos.ToVec3d().Add(0.5 + (double)(outputFace.Normalf.X / 2f), 0.5 + (double)(outputFace.Normalf.Y / 2f), 0.5 + (double)(outputFace.Normalf.Z / 2f)), new Vec3d(num, num2, num3));
			itemSlot.MarkDirty();
			MarkDirty();
			return true;
		}
		return false;
	}

	public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
	{
		if (Api.World is IServerWorldAccessor)
		{
			byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityItemFlowDialog", Lang.Get(ItemFlowObjectLangCode), 4, inventory);
			((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, 5000, data);
			byPlayer.InventoryManager.OpenInventory(inventory);
		}
		return true;
	}

	public override void OnReceivedServerPacket(int packetid, byte[] data)
	{
		base.OnReceivedServerPacket(packetid, data);
	}

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
	{
		InitInventory();
		int num = tree.GetInt("lastReceivedFromDir");
		if (num < 0)
		{
			LastReceivedFromDir = null;
		}
		else
		{
			LastReceivedFromDir = BlockFacing.ALLFACES[num];
		}
		base.FromTreeAttributes(tree, worldForResolving);
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		tree.SetInt("lastReceivedFromDir", LastReceivedFromDir?.Index ?? (-1));
	}

	public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
	{
		if (Block is BlockChute)
		{
			foreach (BlockEntityBehavior behavior in Behaviors)
			{
				behavior.GetBlockInfo(forPlayer, sb);
			}
			sb.AppendLine(Lang.Get("Transporting: {0}", inventory[0].Empty ? Lang.Get("nothing") : (inventory[0].StackSize + "x " + inventory[0].GetStackName())));
			sb.AppendLine("\u00a0                                                           \u00a0");
		}
		else
		{
			base.GetBlockInfo(forPlayer, sb);
			sb.AppendLine(Lang.Get("Contents: {0}", inventory[0].Empty ? Lang.Get("Empty") : (inventory[0].StackSize + "x " + inventory[0].GetStackName())));
		}
	}

	public override void OnBlockBroken(IPlayer byPlayer = null)
	{
		if (Api.World is IServerWorldAccessor)
		{
			DropContents();
		}
		base.OnBlockBroken(byPlayer);
	}

	private void DropContents()
	{
		Vec3d position = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
		foreach (ItemSlot item in inventory)
		{
			if (item.Itemstack != null)
			{
				item.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
				item.Itemstack.Attributes.RemoveAttribute("chuteDir");
				Api.World.SpawnItemEntity(item.Itemstack, position);
				item.Itemstack = null;
				item.MarkDirty();
			}
		}
	}

	public override void OnBlockRemoved()
	{
		if (Api.World is IServerWorldAccessor)
		{
			DropContents();
		}
		base.OnBlockRemoved();
	}

	public override void OnExchanged(Block block)
	{
		base.OnExchanged(block);
		parseBlockProperties();
	}
}
