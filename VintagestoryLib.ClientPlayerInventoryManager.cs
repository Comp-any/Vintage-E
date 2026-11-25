using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public class ClientPlayerInventoryManager : PlayerInventoryManager
{
	public ItemSlot currentHoveredSlot;

	private ClientMain game;

	public override ItemSlot CurrentHoveredSlot
	{
		get
		{
			return currentHoveredSlot;
		}
		set
		{
			currentHoveredSlot = value;
			game.api.Input.TriggerOnMouseEnterSlot(value);
		}
	}

	public override int ActiveHotbarSlotNumber
	{
		get
		{
			return base.ActiveHotbarSlotNumber;
		}
		set
		{
			int activeHotbarSlotNumber = base.ActiveHotbarSlotNumber;
			if (value == activeHotbarSlotNumber)
			{
				return;
			}
			if (player == game.player && game.eventManager != null)
			{
				if (!game.eventManager.TriggerBeforeActiveSlotChanged(game, activeHotbarSlotNumber, value))
				{
					return;
				}
				game.SendPacketClient(ClientPackets.SelectedHotbarSlot(value));
			}
			base.ActiveHotbarSlotNumber = value;
			if (player == game.player)
			{
				game.eventManager?.TriggerAfterActiveSlotChanged(game, activeHotbarSlotNumber, value);
			}
		}
	}

	public ClientPlayerInventoryManager(OrderedDictionary<string, InventoryBase> AllInventories, IPlayer player, ClientMain game)
		: base(AllInventories, player)
	{
		this.game = game;
	}

	public void SetActiveHotbarSlotNumberFromServer(int slotid)
	{
		int activeHotbarSlotNumber = base.ActiveHotbarSlotNumber;
		base.ActiveHotbarSlotNumber = slotid;
		if (player == game.player)
		{
			game.eventManager?.TriggerAfterActiveSlotChanged(game, activeHotbarSlotNumber, slotid);
		}
	}

	public override void NotifySlot(IPlayer player, ItemSlot slot)
	{
	}

	public override bool DropItem(ItemSlot slot, bool fullStack = false)
	{
		if (slot?.Itemstack == null)
		{
			return false;
		}
		int num = ((!fullStack) ? 1 : slot.Itemstack.StackSize);
		EnumHandling handling = EnumHandling.PassThrough;
		slot.Itemstack.Collectible.OnHeldDropped(game, game.player, slot, num, ref handling);
		if (handling != EnumHandling.PassThrough)
		{
			return false;
		}
		if (num >= slot.Itemstack.StackSize && slot == game.player.inventoryMgr.ActiveHotbarSlot && game.EntityPlayer.Controls.HandUse != EnumHandInteract.None)
		{
			EnumHandInteract handUse = game.EntityPlayer.Controls.HandUse;
			if (!game.EntityPlayer.TryStopHandAction(true, EnumItemUseCancelReason.Dropped))
			{
				return false;
			}
			if (slot.StackSize <= 0)
			{
				slot.Itemstack = null;
				slot.MarkDirty();
			}
			game.SendHandInteraction(2, game.BlockSelection, game.EntitySelection, handUse, EnumHandInteractNw.CancelHeldItemUse, firstEvent: false, EnumItemUseCancelReason.Dropped);
		}
		IInventory ownInventory = GetOwnInventory("ground");
		ItemStackMoveOperation op = new ItemStackMoveOperation(game, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge, num);
		op.ActingPlayer = game.player;
		slot.TryPutInto(ownInventory[0], ref op);
		int tabIndex = 0;
		if (slot.Inventory is CreativeInventoryTab creativeInventoryTab)
		{
			tabIndex = creativeInventoryTab.TabIndex;
		}
		Packet_Client packetClient = new Packet_Client
		{
			Id = 8,
			MoveItemstack = new Packet_MoveItemstack
			{
				Quantity = num,
				SourceInventoryId = slot.Inventory.InventoryID,
				SourceSlot = slot.Inventory.GetSlotId(slot),
				SourceLastChanged = slot.Inventory.LastChanged,
				TargetInventoryId = ownInventory.InventoryID,
				TargetSlot = 0,
				TargetLastChanged = ownInventory.LastChanged,
				TabIndex = tabIndex
			}
		};
		game.SendPacketClient(packetClient);
		return true;
	}

	public override void BroadcastHotbarSlot()
	{
	}
}
