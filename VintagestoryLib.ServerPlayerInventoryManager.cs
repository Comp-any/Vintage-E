using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Vintagestory.Server;

public class ServerPlayerInventoryManager : PlayerInventoryManager
{
	private ServerMain server;

	public override ItemSlot CurrentHoveredSlot
	{
		get
		{
			throw new NotImplementedException("This information is not available on the server");
		}
		set
		{
			throw new NotImplementedException("This information is not available on the server");
		}
	}

	public ServerPlayerInventoryManager(Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> AllInventories, IPlayer player, ServerMain server)
		: base(AllInventories, player)
	{
		this.server = server;
	}

	public override void BroadcastHotbarSlot()
	{
		server.BroadcastHotbarSlot(player as ServerPlayer);
	}

	public override bool DropItem(ItemSlot slot, bool fullStack = false)
	{
		if (slot?.Itemstack == null)
		{
			return false;
		}
		int num = ((!fullStack) ? 1 : slot.Itemstack.StackSize);
		EnumHandling handling = EnumHandling.PassThrough;
		slot.Itemstack.Collectible.OnHeldDropped(server, player, slot, num, ref handling);
		if (handling != EnumHandling.PassThrough)
		{
			return false;
		}
		if (num >= slot.Itemstack.StackSize && slot == ActiveHotbarSlot && player.Entity.Controls.HandUse != EnumHandInteract.None)
		{
			if (!player.Entity.TryStopHandAction(true, EnumItemUseCancelReason.Dropped))
			{
				return false;
			}
			if (slot.StackSize <= 0)
			{
				slot.Itemstack = null;
				slot.MarkDirty();
			}
		}
		IInventory ownInventory = GetOwnInventory("ground");
		ItemStackMoveOperation op = new ItemStackMoveOperation(server, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, num);
		op.ActingPlayer = player;
		slot.TryPutInto(ownInventory[0], ref op);
		slot.MarkDirty();
		return true;
	}

	public override void NotifySlot(IPlayer toPlayer, ItemSlot slot)
	{
		if (slot.Inventory != null)
		{
			server.SendPacket(toPlayer as IServerPlayer, new Packet_Server
			{
				Id = 66,
				NotifySlot = new Packet_NotifySlot
				{
					InventoryId = slot.Inventory.InventoryID,
					SlotId = slot.Inventory.GetSlotId(slot)
				}
			});
		}
	}

	internal void OnPlayerDisconnect()
	{
		List<KeyValuePair<string, InventoryBase>> list = new List<KeyValuePair<string, InventoryBase>>();
		foreach (KeyValuePair<string, InventoryBase> inventory in Inventories)
		{
			if (!(inventory.Value is InventoryBasePlayer))
			{
				list.Add(inventory);
			}
		}
		foreach (KeyValuePair<string, InventoryBase> item in list)
		{
			CloseInventory(item.Value);
			Inventories.Remove(item.Key);
		}
	}
}
