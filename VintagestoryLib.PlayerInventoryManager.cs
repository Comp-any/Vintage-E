using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.Common;

public abstract class PlayerInventoryManager : IPlayerInventoryManager
{
	public static string[] defaultInventories = new string[7] { "hotbar", "creative", "backpack", "ground", "mouse", "craftinggrid", "character" };

	public IPlayer player;

	public Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> Inventories;

	public IEnumerable<InventoryBase> InventoriesOrdered => Inventories.ValuesOrdered;

	public EnumTool? ActiveTool => ActiveHotbarSlot.Itemstack?.Collectible.GetTool(ActiveHotbarSlot);

	public EnumTool? OffhandTool => OffhandHotbarSlot.Itemstack?.Collectible.GetTool(OffhandHotbarSlot);

	public virtual int ActiveHotbarSlotNumber { get; set; }

	public ItemSlot ActiveHotbarSlot
	{
		get
		{
			string invID = "hotbar-" + player.PlayerUID;
			GetInventory(invID, out var invFound);
			int num = ((invFound != null && !invFound[10].Empty) ? 1 : 0);
			if (ActiveHotbarSlotNumber >= 10 + num)
			{
				invID = "backpack-" + player.PlayerUID;
				if (GetInventory(invID, out var invFound2))
				{
					return invFound2[ActiveHotbarSlotNumber - 10 - num];
				}
				return null;
			}
			return invFound?[ActiveHotbarSlotNumber];
		}
	}

	public ItemSlot OffhandHotbarSlot
	{
		get
		{
			string key = "hotbar-" + player.PlayerUID;
			if (Inventories.ContainsKey(key))
			{
				return Inventories[key][11];
			}
			return null;
		}
	}

	public ItemSlot MouseItemSlot
	{
		get
		{
			string key = "mouse-" + player.PlayerUID;
			if (Inventories.ContainsKey(key))
			{
				return Inventories[key][0];
			}
			return null;
		}
	}

	Dictionary<string, IInventory> IPlayerInventoryManager.Inventories
	{
		get
		{
			Dictionary<string, IInventory> dictionary = new Dictionary<string, IInventory>();
			foreach (KeyValuePair<string, InventoryBase> inventory in Inventories)
			{
				dictionary[inventory.Key] = inventory.Value;
			}
			return dictionary;
		}
	}

	public List<IInventory> OpenedInventories => ((IEnumerable<IInventory>)InventoriesOrdered.Where((InventoryBase inv) => inv.HasOpened(player))).ToList();

	public abstract ItemSlot CurrentHoveredSlot { get; set; }

	public abstract void BroadcastHotbarSlot();

	public PlayerInventoryManager(Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> AllInventories, IPlayer player)
	{
		Inventories = AllInventories;
		this.player = player;
	}

	public bool IsVisibleHandSlot(string invid, int slotNumber)
	{
		if (Inventories.ContainsKey(invid) && Inventories[invid] is InventoryPlayerHotbar)
		{
			if (ActiveHotbarSlotNumber != slotNumber)
			{
				return slotNumber == 10;
			}
			return true;
		}
		return false;
	}

	public string GetInventoryName(string inventoryClassName)
	{
		return inventoryClassName + "-" + player.PlayerUID;
	}

	public IInventory GetOwnInventory(string inventoryClassName)
	{
		if (Inventories.ContainsKey(GetInventoryName(inventoryClassName)))
		{
			return Inventories[GetInventoryName(inventoryClassName)];
		}
		return null;
	}

	public IInventory GetInventory(string inventoryClassName)
	{
		if (Inventories.ContainsKey(inventoryClassName))
		{
			return Inventories[inventoryClassName];
		}
		return null;
	}

	public ItemStack GetHotbarItemstack(int slotId)
	{
		string key = "hotbar-" + player.PlayerUID;
		if (Inventories.ContainsKey(key))
		{
			return Inventories[key][slotId].Itemstack;
		}
		return null;
	}

	public IInventory GetHotbarInventory()
	{
		string key = "hotbar-" + player.PlayerUID;
		if (Inventories.ContainsKey(key))
		{
			return Inventories[key];
		}
		return null;
	}

	public bool GetInventory(string invID, [MaybeNullWhen(false)] out InventoryBase invFound)
	{
		return Inventories.TryGetValue(invID, out invFound);
	}

	[Obsolete("Use GetBestSuitedSlot(ItemSlot sourceSlot, bool onlyPlayerInventory, ItemStackMoveOperation op, List<ItemSlot> skipSlots = null) instead")]
	public ItemSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot> skipSlots = null)
	{
		return GetBestSuitedSlot(sourceSlot, onlyPlayerInventory: true, op, skipSlots);
	}

	public ItemSlot GetBestSuitedSlot(ItemSlot sourceSlot, bool onlyPlayerInventory, ItemStackMoveOperation op = null, List<ItemSlot> skipSlots = null)
	{
		WeightedSlot weightedSlot = new WeightedSlot();
		foreach (InventoryBase item in InventoriesOrdered.Reverse())
		{
			if ((!onlyPlayerInventory || item is InventoryBasePlayer) && item.HasOpened(player) && item.CanPlayerAccess(player, new EntityPos()))
			{
				WeightedSlot bestSuitedSlot = item.GetBestSuitedSlot(sourceSlot, op, skipSlots);
				if (bestSuitedSlot.weight > weightedSlot.weight)
				{
					weightedSlot = bestSuitedSlot;
				}
			}
		}
		return weightedSlot.slot;
	}

	public bool TryGiveItemstack(ItemStack itemstack, bool slotNotifyEffect = false)
	{
		if (itemstack == null || itemstack.StackSize == 0)
		{
			return false;
		}
		ItemSlot itemSlot = new DummySlot(null);
		itemSlot.Itemstack = itemstack;
		ItemStackMoveOperation op = new ItemStackMoveOperation(player.Entity.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge, itemstack.StackSize);
		object[] array = TryTransferAway(itemSlot, ref op, onlyPlayerInventory: true, null, slotNotifyEffect);
		if (itemSlot.Itemstack == null)
		{
			itemstack.StackSize = 0;
		}
		return array != null;
	}

	public object[] TryTransferAway(ItemSlot sourceSlot, ref ItemStackMoveOperation op, bool onlyPlayerInventory, bool slotNotifyEffect = false)
	{
		return TryTransferAway(sourceSlot, ref op, onlyPlayerInventory, null, slotNotifyEffect);
	}

	public object[] TryTransferAway(ItemSlot sourceSlot, ref ItemStackMoveOperation op, bool onlyPlayerInventory, StringBuilder shiftClickDebugText, bool slotNotifyEffect = false)
	{
		if (sourceSlot.Itemstack == null || !sourceSlot.CanTake())
		{
			return null;
		}
		List<object> list = new List<object>();
		List<ItemSlot> list2 = new List<ItemSlot>();
		op.RequestedQuantity = sourceSlot.StackSize;
		ItemStack sourceStack = sourceSlot.Itemstack.Clone();
		int num = 0;
		while (num++ < 5000 && sourceSlot.StackSize > 0)
		{
			ItemSlot bestSuitedSlot = GetBestSuitedSlot(sourceSlot, onlyPlayerInventory, op, list2);
			if (bestSuitedSlot == null)
			{
				break;
			}
			list2.Add(bestSuitedSlot);
			int stackSize = bestSuitedSlot.StackSize;
			sourceSlot.TryPutInto(bestSuitedSlot, ref op);
			if (shiftClickDebugText != null)
			{
				if (stackSize != bestSuitedSlot.StackSize)
				{
					if (shiftClickDebugText.Length > 0)
					{
						shiftClickDebugText.Append(", ");
					}
					shiftClickDebugText.Append($"{bestSuitedSlot.StackSize - stackSize}x into {bestSuitedSlot.Inventory?.InventoryID}");
				}
				else if (bestSuitedSlot is ItemSlotBlackHole)
				{
					if (shiftClickDebugText.Length > 0)
					{
						shiftClickDebugText.Append(", ");
					}
					shiftClickDebugText.Append($"{op.RequestedQuantity}x into black hole slot");
				}
			}
			int notMovedQuantity = op.NotMovedQuantity;
			if (stackSize != bestSuitedSlot.StackSize && !bestSuitedSlot.Empty && bestSuitedSlot.Inventory is InventoryBasePlayer)
			{
				TreeAttribute treeAttribute = new TreeAttribute();
				treeAttribute["itemstack"] = new ItemstackAttribute(bestSuitedSlot.Itemstack.Clone());
				treeAttribute["byentityid"] = new LongAttribute((player?.Entity?.EntityId).GetValueOrDefault());
				player.Entity.Api.Event.PushEvent("onitemgrabbed", treeAttribute);
			}
			if ((stackSize != bestSuitedSlot.StackSize) & slotNotifyEffect)
			{
				bestSuitedSlot.MarkDirty();
				sourceSlot.MarkDirty();
				NotifySlot(player, bestSuitedSlot);
				if (bestSuitedSlot == ActiveHotbarSlot)
				{
					BroadcastHotbarSlot();
				}
			}
			if (sourceSlot.Inventory == null || sourceSlot is ItemSlotCreative)
			{
				if (bestSuitedSlot.Itemstack != null && bestSuitedSlot.Itemstack.StackSize != stackSize)
				{
					list.Add(new Packet_Client
					{
						CreateItemstack = new Packet_CreateItemstack
						{
							Itemstack = StackConverter.ToPacket(bestSuitedSlot.Itemstack),
							TargetInventoryId = bestSuitedSlot.Inventory.InventoryID,
							TargetLastChanged = bestSuitedSlot.Inventory.LastChanged,
							TargetSlot = bestSuitedSlot.Inventory.GetSlotId(bestSuitedSlot)
						},
						Id = 10
					});
				}
				if (notMovedQuantity == 0)
				{
					break;
				}
				continue;
			}
			list.Add(new Packet_Client
			{
				MoveItemstack = new Packet_MoveItemstack
				{
					SourceInventoryId = sourceSlot.Inventory.InventoryID,
					TargetInventoryId = bestSuitedSlot.Inventory.InventoryID,
					SourceSlot = sourceSlot.Inventory.GetSlotId(sourceSlot),
					TargetSlot = bestSuitedSlot.Inventory.GetSlotId(bestSuitedSlot),
					SourceLastChanged = sourceSlot.Inventory.LastChanged,
					TargetLastChanged = bestSuitedSlot.Inventory.LastChanged,
					Quantity = op.RequestedQuantity,
					Modifiers = (int)op.Modifiers,
					MouseButton = (int)op.MouseButton,
					Priority = (int)op.CurrentPriority
				},
				Id = 8
			});
			if (notMovedQuantity == 0 || sourceSlot.Empty || !sourceSlot.Itemstack.Satisfies(sourceStack))
			{
				break;
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.ToArray();
	}

	public void DiscardAll()
	{
		foreach (InventoryBase value in Inventories.Values)
		{
			if (value is InventoryBasePlayer)
			{
				value.DiscardAll();
			}
		}
	}

	public void OnDeath()
	{
		foreach (InventoryBase value in Inventories.Values)
		{
			if (value is InventoryBasePlayer)
			{
				value.OnOwningEntityDeath(player.Entity.Pos.XYZ);
			}
		}
	}

	public object OpenInventory(IInventory inventory)
	{
		Inventories[inventory.InventoryID] = (InventoryBase)inventory;
		return inventory.Open(player);
	}

	public object CloseInventory(IInventory inventory)
	{
		if (inventory.RemoveOnClose)
		{
			Inventories.Remove(inventory.InventoryID);
		}
		return inventory.Close(player);
	}

	public void CloseInventoryAndSync(IInventory inventory)
	{
		object packetClient = CloseInventory(inventory);
		if (player.Entity.Api is ICoreClientAPI coreClientAPI)
		{
			coreClientAPI.Network.SendPacketClient(packetClient);
		}
	}

	public bool HasInventory(IInventory inventory)
	{
		return Inventories.ContainsValue((InventoryBase)inventory);
	}

	public abstract void NotifySlot(IPlayer player, ItemSlot slot);

	public bool DropMouseSlotItems(bool fullStack)
	{
		return DropItem(MouseItemSlot, fullStack);
	}

	public bool DropHotbarSlotItems(bool fullStack)
	{
		return DropItem(ActiveHotbarSlot, fullStack);
	}

	public void DropAllInventoryItems(IInventory inventory)
	{
		foreach (ItemSlot item in inventory)
		{
			DropItem(item, fullStack: true);
		}
	}

	public abstract bool DropItem(ItemSlot mouseItemSlot, bool fullStack);

	public object TryTransferTo(ItemSlot sourceSlot, ItemSlot targetSlot, ref ItemStackMoveOperation op)
	{
		if (sourceSlot.Itemstack == null || !sourceSlot.CanTake() || targetSlot == null)
		{
			return null;
		}
		int stackSize = targetSlot.StackSize;
		sourceSlot.TryPutInto(targetSlot, ref op);
		if ((sourceSlot.Inventory == null || sourceSlot is ItemSlotCreative) && targetSlot.Itemstack != null && targetSlot.Itemstack.StackSize != stackSize)
		{
			return new Packet_Client
			{
				CreateItemstack = new Packet_CreateItemstack
				{
					Itemstack = StackConverter.ToPacket(targetSlot.Itemstack),
					TargetInventoryId = targetSlot.Inventory.InventoryID,
					TargetLastChanged = targetSlot.Inventory.LastChanged,
					TargetSlot = targetSlot.Inventory.GetSlotId(targetSlot)
				},
				Id = 10
			};
		}
		return new Packet_Client
		{
			MoveItemstack = new Packet_MoveItemstack
			{
				SourceInventoryId = sourceSlot.Inventory.InventoryID,
				TargetInventoryId = targetSlot.Inventory.InventoryID,
				SourceSlot = sourceSlot.Inventory.GetSlotId(sourceSlot),
				TargetSlot = targetSlot.Inventory.GetSlotId(targetSlot),
				SourceLastChanged = sourceSlot.Inventory.LastChanged,
				TargetLastChanged = targetSlot.Inventory.LastChanged,
				Quantity = Math.Max(0, targetSlot.StackSize - stackSize),
				Modifiers = (int)op.Modifiers,
				MouseButton = (int)op.MouseButton,
				Priority = (int)op.CurrentPriority
			},
			Id = 8
		};
	}

	public bool Find(System.Func<ItemSlot, bool> matcher)
	{
		foreach (IInventory openedInventory in OpenedInventories)
		{
			foreach (ItemSlot item in openedInventory)
			{
				if (matcher(item))
				{
					return true;
				}
			}
		}
		return false;
	}
}
