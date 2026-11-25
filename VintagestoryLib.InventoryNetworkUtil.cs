using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Vintagestory.Common;

public class InventoryNetworkUtil : IInventoryNetworkUtil
{
	protected InventoryBase inv;

	private bool pauseInvUpdates;

	private Queue<Packet_InventoryUpdate> pkts = new Queue<Packet_InventoryUpdate>();

	public ICoreAPI Api { get; set; }

	public bool PauseInventoryUpdates
	{
		get
		{
			return pauseInvUpdates;
		}
		set
		{
			bool num = !value && pauseInvUpdates;
			pauseInvUpdates = value;
			if (num)
			{
				while (pkts.Count > 0)
				{
					Packet_InventoryUpdate packet = pkts.Dequeue();
					UpdateFromPacket(Api.World, packet);
				}
			}
		}
	}

	public InventoryNetworkUtil(InventoryBase inv, ICoreAPI api)
	{
		this.inv = inv;
		Api = api;
	}

	public virtual void HandleClientPacket(IPlayer byPlayer, int packetId, byte[] data)
	{
		Packet_Client packet_Client = new Packet_Client();
		Packet_ClientSerializer.DeserializeBuffer(data, data.Length, packet_Client);
		HandleClientPacket(byPlayer, packetId, packet_Client);
	}

	public virtual void HandleClientPacket(IPlayer byPlayer, int packetId, Packet_Client packet)
	{
		IWorldPlayerData worldData = byPlayer.WorldData;
		switch (packetId)
		{
		case 7:
			handleActivateInventorySlotPacket(byPlayer, packet, worldData);
			break;
		case 8:
			handleMoveItemStackPacket(byPlayer, packet);
			break;
		case 9:
			handleFlipItemstacksPacket(byPlayer, packet);
			break;
		}
	}

	private void handleFlipItemstacksPacket(IPlayer byPlayer, Packet_Client packet)
	{
		Packet_FlipItemstacks flipitemstacks = packet.Flipitemstacks;
		string[] array = new string[2] { flipitemstacks.SourceInventoryId, flipitemstacks.TargetInventoryId };
		int[] slotIds = new int[2] { flipitemstacks.SourceSlot, flipitemstacks.TargetSlot };
		long[] array2 = new long[2] { flipitemstacks.SourceLastChanged, flipitemstacks.TargetLastChanged };
		if (!SendDirtyInventoryContents(byPlayer, array[0], array2[0]) && !SendDirtyInventoryContents(byPlayer, array[1], array2[1]))
		{
			InventoryBase inventoryBase = (InventoryBase)byPlayer.InventoryManager.GetInventory(array[0]);
			InventoryBase inventoryBase2 = (InventoryBase)byPlayer.InventoryManager.GetInventory(array[1]);
			if (inventoryBase is ITabbedInventory)
			{
				((ITabbedInventory)inventoryBase).SetTab(packet.Flipitemstacks.SourceTabIndex);
			}
			if (inventoryBase2 is ITabbedInventory)
			{
				((ITabbedInventory)inventoryBase2).SetTab(packet.Flipitemstacks.TargetTabIndex);
			}
			if (inv.TryFlipItemStack(byPlayer, array, slotIds, array2))
			{
				NotifyPlayersItemstackMoved(byPlayer, array, slotIds);
			}
			else
			{
				RevertPlayerItemstackMove(byPlayer, array, slotIds);
			}
		}
	}

	private void handleMoveItemStackPacket(IPlayer byPlayer, Packet_Client packet)
	{
		string[] array = new string[2]
		{
			packet.MoveItemstack.SourceInventoryId,
			packet.MoveItemstack.TargetInventoryId
		};
		int[] array2 = new int[2]
		{
			packet.MoveItemstack.SourceSlot,
			packet.MoveItemstack.TargetSlot
		};
		if (SendDirtyInventoryContents(byPlayer, array[0], packet.MoveItemstack.SourceLastChanged) || SendDirtyInventoryContents(byPlayer, array[1], packet.MoveItemstack.TargetLastChanged))
		{
			InventoryBase inventoryBase = (InventoryBase)byPlayer.InventoryManager.GetInventory(array[1]);
			Api.World.Logger.Audit("Revert itemstack move command by {0} to move {1}x{4} from {2} to {3}", byPlayer.PlayerName, packet.MoveItemstack.Quantity, array[0], array[1], inventoryBase[array2[1]].GetStackName());
			return;
		}
		if (inv is ITabbedInventory)
		{
			((ITabbedInventory)inv).SetTab(packet.MoveItemstack.TabIndex);
		}
		ItemStackMoveOperation op = new ItemStackMoveOperation(Api.World, (EnumMouseButton)packet.MoveItemstack.MouseButton, (EnumModifierKey)packet.MoveItemstack.Modifiers, (EnumMergePriority)packet.MoveItemstack.Priority, packet.MoveItemstack.Quantity);
		op.ActingPlayer = byPlayer;
		ItemSlot[] slotsIfExists = inv.GetSlotsIfExists(byPlayer, array, array2);
		if (inv is InventoryBasePlayer && slotsIfExists[1] is ItemSlotGround && !slotsIfExists[0].Empty)
		{
			EnumHandling handling = EnumHandling.PassThrough;
			slotsIfExists[0].Itemstack.Collectible.OnHeldDropped(Api.World, byPlayer, slotsIfExists[0], packet.MoveItemstack.Quantity, ref handling);
			if (handling != EnumHandling.PassThrough)
			{
				return;
			}
		}
		AssetLocation assetLocation = slotsIfExists[0]?.Itemstack?.Collectible.Code;
		if (inv.TryMoveItemStack(byPlayer, array, array2, ref op))
		{
			Api.World.Logger.Audit("{0} moved {1}x{4} from {2} to {3}", byPlayer.PlayerName, packet.MoveItemstack.Quantity, array[0], array[1], assetLocation);
		}
		else
		{
			SendInventoryContents(byPlayer, array[0]);
			SendInventoryContents(byPlayer, array[1]);
		}
	}

	private void handleActivateInventorySlotPacket(IPlayer byPlayer, Packet_Client packet, IWorldPlayerData plrData)
	{
		Packet_ActivateInventorySlot activateInventorySlot = packet.ActivateInventorySlot;
		EnumMouseButton mouseButton = (EnumMouseButton)activateInventorySlot.MouseButton;
		long targetLastChanged = activateInventorySlot.TargetLastChanged;
		if (inv.lastChangedSinceServerStart < targetLastChanged)
		{
			SendInventoryContents(byPlayer, inv.InventoryID);
			return;
		}
		int targetSlot = activateInventorySlot.TargetSlot;
		IInventory inventory = inv;
		if (inv is ITabbedInventory)
		{
			((ITabbedInventory)inv).SetTab(packet.ActivateInventorySlot.TabIndex);
		}
		ItemSlot itemSlot = inventory[targetSlot];
		if (itemSlot == null)
		{
			Api.World.Logger.Warning("{0} left-clicked slot {1} in {2}, but target slot did not exist!", byPlayer?.PlayerName, targetSlot, inventory.InventoryID);
			SendInventoryContents(byPlayer, inv.InventoryID);
			return;
		}
		string text = "mouse-" + plrData.PlayerUID;
		ItemSlot itemSlot2 = byPlayer.InventoryManager.GetInventory(text)[0];
		if (itemSlot2 == null)
		{
			Api.World.Logger.Warning("{0} left-clicked slot {1}, but source slot did not exist!", byPlayer?.PlayerName, text);
			SendInventoryContents(byPlayer, inv.InventoryID);
			return;
		}
		ItemStackMoveOperation op = new ItemStackMoveOperation(Api.World, mouseButton, (EnumModifierKey)activateInventorySlot.Modifiers, (EnumMergePriority)activateInventorySlot.Priority);
		op.WheelDir = activateInventorySlot.Dir;
		op.ActingPlayer = byPlayer;
		if (mouseButton == EnumMouseButton.Wheel)
		{
			op.RequestedQuantity = 1;
		}
		string text2 = (itemSlot2.Empty ? "empty" : $"{itemSlot2.StackSize}x{itemSlot2.GetStackName()}");
		string text3 = (itemSlot.Empty ? "empty" : $"{itemSlot.StackSize}x{itemSlot.GetStackName()}");
		inventory.ActivateSlot(targetSlot, itemSlot2, ref op);
		string text4 = (itemSlot2.Empty ? "empty" : $"{itemSlot2.StackSize}x{itemSlot2.GetStackName()}");
		if (text2 != text4)
		{
			string text5 = (itemSlot.Empty ? "empty" : $"{itemSlot.StackSize}x{itemSlot.GetStackName()}");
			Api.World.Logger.Audit("{0} left clicked slot {1} in {2}. Before: (mouse: {3}, inv: {4}), after: (mouse: {5}, inv: {6})", op.ActingPlayer?.PlayerName, targetSlot, inventory.InventoryID, text2, text3, text4, text5);
		}
	}

	protected virtual bool SendDirtyInventoryContents(IPlayer owningPlayer, string inventoryId, long lastChangedClient)
	{
		InventoryBase inventoryBase = (InventoryBase)owningPlayer.InventoryManager.GetInventory(inventoryId);
		if (inventoryBase == null)
		{
			return false;
		}
		if (inventoryBase.lastChangedSinceServerStart > lastChangedClient)
		{
			SendInventoryContents(owningPlayer, inventoryId);
			return true;
		}
		return false;
	}

	protected virtual void RevertPlayerItemstackMove(IPlayer owningPlayer, string[] invIds, int[] slotIds)
	{
		ItemSlot[] slotsIfExists = inv.GetSlotsIfExists(owningPlayer, invIds, slotIds);
		if (slotsIfExists[0] != null && slotsIfExists[1] != null)
		{
			Packet_Server doubleUpdatePacket = getDoubleUpdatePacket(owningPlayer, invIds, slotIds);
			((ICoreServerAPI)Api).Network.SendArbitraryPacket(doubleUpdatePacket, (IServerPlayer)owningPlayer);
		}
	}

	protected virtual void SendInventoryContents(IPlayer owningPlayer, string inventoryId)
	{
		InventoryBase inventoryBase = (InventoryBase)owningPlayer.InventoryManager.GetInventory(inventoryId);
		if (inventoryBase != null)
		{
			Packet_InventoryContents inventoryContents = (inventoryBase.InvNetworkUtil as InventoryNetworkUtil).ToPacket(owningPlayer);
			Packet_Server packet = new Packet_Server
			{
				Id = 30,
				InventoryContents = inventoryContents
			};
			((ICoreServerAPI)Api).Network.SendArbitraryPacket(packet, (IServerPlayer)owningPlayer);
		}
	}

	public virtual void SendInventoryRollback(IServerPlayer byPlayer, int packetId, ReadOnlySpan<byte> packetData)
	{
		Packet_Client packet_Client = new Packet_Client();
		byte[] array = ArrayPool<byte>.Shared.Rent(packetData.Length);
		try
		{
			packetData.CopyTo(array);
			Packet_ClientSerializer.DeserializeBuffer(array, packetData.Length, packet_Client);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(array);
		}
		switch (packetId)
		{
		case 7:
			SendInventoryContents(byPlayer, inv.InventoryID);
			break;
		case 8:
			SendInventoryContents(byPlayer, packet_Client.MoveItemstack.SourceInventoryId);
			SendInventoryContents(byPlayer, packet_Client.MoveItemstack.TargetInventoryId);
			break;
		case 9:
		{
			Packet_FlipItemstacks flipitemstacks = packet_Client.Flipitemstacks;
			string[] invIds = new string[2] { flipitemstacks.SourceInventoryId, flipitemstacks.TargetInventoryId };
			int[] slotIds = new int[2] { flipitemstacks.SourceSlot, flipitemstacks.TargetSlot };
			RevertPlayerItemstackMove(byPlayer, invIds, slotIds);
			break;
		}
		}
	}

	protected virtual void NotifyPlayersItemstackMoved(IPlayer player, string[] invIds, int[] slotIds)
	{
		Packet_Server doubleUpdatePacket = getDoubleUpdatePacket(player, invIds, slotIds);
		((ICoreServerAPI)Api).Network.BroadcastArbitraryPacket(doubleUpdatePacket, (IServerPlayer)player);
	}

	public static Packet_Server getDoubleUpdatePacket(IPlayer player, string[] invIds, int[] slotIds)
	{
		IInventory inventory = player.InventoryManager.GetInventory(invIds[0]);
		IInventory inventory2 = player.InventoryManager.GetInventory(invIds[1]);
		ItemStack itemstack = inventory[slotIds[0]].Itemstack;
		ItemStack itemstack2 = inventory2[slotIds[1]].Itemstack;
		Packet_InventoryDoubleUpdate inventoryDoubleUpdate = new Packet_InventoryDoubleUpdate
		{
			ClientId = player.ClientId,
			InventoryId1 = invIds[0],
			InventoryId2 = invIds[1],
			SlotId1 = slotIds[0],
			SlotId2 = slotIds[1],
			ItemStack1 = ((itemstack != null) ? StackConverter.ToPacket(itemstack) : null),
			ItemStack2 = ((itemstack2 != null) ? StackConverter.ToPacket(itemstack2) : null)
		};
		return new Packet_Server
		{
			Id = 32,
			InventoryDoubleUpdate = inventoryDoubleUpdate
		};
	}

	internal virtual Packet_ItemStack[] CreatePacketItemStacks()
	{
		Packet_ItemStack[] array = new Packet_ItemStack[inv.CountForNetworkPacket];
		for (int i = 0; i < inv.CountForNetworkPacket; i++)
		{
			IItemStack itemstack = inv[i].Itemstack;
			if (itemstack != null)
			{
				MemoryStream memoryStream = new MemoryStream();
				BinaryWriter stream = new BinaryWriter(memoryStream);
				itemstack.Attributes.ToBytes(stream);
				array[i] = new Packet_ItemStack
				{
					ItemClass = (int)itemstack.Class,
					ItemId = itemstack.Id,
					StackSize = itemstack.StackSize,
					Attributes = memoryStream.ToArray()
				};
			}
			else
			{
				array[i] = new Packet_ItemStack
				{
					ItemClass = -1,
					ItemId = 0,
					StackSize = 0
				};
			}
		}
		return array;
	}

	public virtual Packet_InventoryContents ToPacket(IPlayer player)
	{
		Packet_InventoryContents packet_InventoryContents = new Packet_InventoryContents();
		packet_InventoryContents.ClientId = player.ClientId;
		packet_InventoryContents.InventoryId = inv.InventoryID;
		packet_InventoryContents.InventoryClass = inv.ClassName;
		Packet_ItemStack[] array = CreatePacketItemStacks();
		packet_InventoryContents.SetItemstacks(array, array.Length, array.Length);
		return packet_InventoryContents;
	}

	public virtual Packet_Server getSlotUpdatePacket(IPlayer player, int slotId)
	{
		ItemSlot itemSlot = inv[slotId];
		if (itemSlot == null)
		{
			return null;
		}
		ItemStack itemstack = itemSlot.Itemstack;
		Packet_ItemStack itemStack = null;
		if (itemstack != null)
		{
			itemStack = StackConverter.ToPacket(itemstack);
		}
		Packet_InventoryUpdate inventoryUpdate = new Packet_InventoryUpdate
		{
			ClientId = player.ClientId,
			InventoryId = inv.InventoryID,
			ItemStack = itemStack,
			SlotId = slotId
		};
		return new Packet_Server
		{
			Id = 31,
			InventoryUpdate = inventoryUpdate
		};
	}

	public virtual object DidOpen(IPlayer player)
	{
		if (inv.Api.Side != EnumAppSide.Client)
		{
			return null;
		}
		Packet_InvOpenClose invOpenedClosed = new Packet_InvOpenClose
		{
			InventoryId = inv.InventoryID,
			Opened = 1
		};
		return new Packet_Client
		{
			Id = 30,
			InvOpenedClosed = invOpenedClosed
		};
	}

	public virtual object DidClose(IPlayer player)
	{
		if (inv.Api.Side != EnumAppSide.Client)
		{
			return null;
		}
		Packet_InvOpenClose invOpenedClosed = new Packet_InvOpenClose
		{
			InventoryId = inv.InventoryID,
			Opened = 0
		};
		return new Packet_Client
		{
			Id = 30,
			InvOpenedClosed = invOpenedClosed
		};
	}

	public virtual void UpdateFromPacket(IWorldAccessor resolver, Packet_InventoryContents packet)
	{
		for (int i = 0; i < packet.ItemstacksCount; i++)
		{
			ItemSlot slot = inv[i];
			if (UpdateSlotStack(slot, ItemStackFromPacket(resolver, packet.Itemstacks[i])))
			{
				inv.DidModifyItemSlot(slot);
			}
		}
	}

	public virtual void UpdateFromPacket(IWorldAccessor resolver, Packet_InventoryUpdate packet)
	{
		if (PauseInventoryUpdates)
		{
			pkts.Enqueue(packet);
			return;
		}
		if (packet.SlotId >= inv.Count)
		{
			throw new Exception("Client received server InventoryUpdate for " + inv.InventoryID + ", slot " + packet.SlotId + " but max is " + (inv.Count - 1) + ". For " + inv.ClassName + " at " + inv.Pos);
		}
		ItemSlot itemSlot = inv[packet.SlotId];
		if (itemSlot != null)
		{
			UpdateSlotStack(itemSlot, ItemStackFromPacket(resolver, packet.ItemStack));
			inv.DidModifyItemSlot(itemSlot);
		}
	}

	public virtual void UpdateFromPacket(IWorldAccessor resolver, Packet_InventoryDoubleUpdate packet)
	{
		if (packet.InventoryId1 == inv.InventoryID)
		{
			ItemSlot slot = inv[packet.SlotId1];
			UpdateSlotStack(slot, ItemStackFromPacket(resolver, packet.ItemStack1));
			inv.DidModifyItemSlot(slot);
		}
		if (packet.InventoryId2 == inv.InventoryID)
		{
			ItemSlot slot2 = inv[packet.SlotId2];
			UpdateSlotStack(slot2, ItemStackFromPacket(resolver, packet.ItemStack2));
			inv.DidModifyItemSlot(slot2);
		}
	}

	protected ItemStack ItemStackFromPacket(IWorldAccessor resolver, Packet_ItemStack pItemStack)
	{
		if (pItemStack == null || ((pItemStack.ItemClass == -1) | (pItemStack.ItemId == 0)))
		{
			return null;
		}
		return StackConverter.FromPacket(pItemStack, resolver);
	}

	private bool UpdateSlotStack(ItemSlot slot, ItemStack newStack)
	{
		if (slot.Itemstack != null && newStack != null && slot.Itemstack.Collectible == newStack.Collectible)
		{
			newStack.TempAttributes = slot.Itemstack?.TempAttributes;
		}
		bool result = newStack == null != (slot.Itemstack == null) || (newStack != null && !newStack.Equals(Api.World, slot.Itemstack, GlobalConstants.IgnoredStackAttributes));
		slot.Itemstack = newStack;
		return result;
	}

	public object GetActivateSlotPacket(int slotId, ItemStackMoveOperation op)
	{
		Packet_ActivateInventorySlot packet_ActivateInventorySlot = new Packet_ActivateInventorySlot
		{
			MouseButton = (int)op.MouseButton,
			TargetInventoryId = inv.InventoryID,
			TargetSlot = slotId,
			TargetLastChanged = inv.lastChangedSinceServerStart,
			Modifiers = (int)op.Modifiers,
			Priority = (int)op.CurrentPriority,
			Dir = op.WheelDir
		};
		if (inv is ITabbedInventory)
		{
			packet_ActivateInventorySlot.TabIndex = ((ITabbedInventory)inv).CurrentTab.Index;
		}
		return new Packet_Client
		{
			Id = 7,
			ActivateInventorySlot = packet_ActivateInventorySlot
		};
	}

	public object GetFlipSlotsPacket(IInventory sourceInv, int sourceSlotId, int targetSlotId)
	{
		Packet_Client packet_Client = new Packet_Client
		{
			Id = 9,
			Flipitemstacks = new Packet_FlipItemstacks
			{
				SourceInventoryId = sourceInv.InventoryID,
				SourceLastChanged = ((InventoryBase)sourceInv).lastChangedSinceServerStart,
				SourceSlot = sourceSlotId,
				TargetInventoryId = inv.InventoryID,
				TargetLastChanged = inv.lastChangedSinceServerStart,
				TargetSlot = targetSlotId
			}
		};
		if (sourceInv is ITabbedInventory)
		{
			packet_Client.Flipitemstacks.SourceTabIndex = (sourceInv as ITabbedInventory).CurrentTab.Index;
		}
		if (sourceInv is CreativeInventoryTab)
		{
			packet_Client.Flipitemstacks.SourceTabIndex = (sourceInv as CreativeInventoryTab).TabIndex;
		}
		if (inv is ITabbedInventory)
		{
			packet_Client.Flipitemstacks.TargetTabIndex = (inv as ITabbedInventory).CurrentTab.Index;
		}
		if (inv is CreativeInventoryTab)
		{
			packet_Client.Flipitemstacks.TargetTabIndex = (inv as CreativeInventoryTab).TabIndex;
		}
		return packet_Client;
	}
}
