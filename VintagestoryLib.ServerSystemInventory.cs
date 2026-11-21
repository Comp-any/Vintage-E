using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Vintagestory.Server;

internal class ServerSystemInventory : ServerSystem
{
	private bool ItemFilterEnabled;

	private List<InventoryBase> dirtySlots2Clear = new List<InventoryBase>();

	public ServerSystemInventory(ServerMain server)
		: base(server)
	{
		server.RegisterGameTickListener(SendDirtySlots, 30);
		server.RegisterGameTickListener(OnUsingTick, 20);
		server.RegisterGameTickListener(UpdateTransitionStates, 4000);
		server.PacketHandlers[13] = HandleSelectedHotbarSlot;
		server.PacketHandlers[7] = HandleActivateInventorySlot;
		server.PacketHandlers[10] = HandleCreateItemstack;
		server.PacketHandlers[8] = HandleMoveItemstack;
		server.PacketHandlers[9] = HandleFlipItemStacks;
		server.PacketHandlers[25] = HandleHandInteraction;
		server.PacketHandlers[27] = HandleToolMode;
		server.PacketHandlers[30] = HandleInvOpenClose;
	}

	public override void OnBeginGameReady(SaveGame savegame)
	{
		base.OnBeginGameReady(savegame);
		ItemFilterEnabled = server.api.World.Config.GetBool("itemFilterEnabled");
	}

	public override void OnPlayerDisconnect(ServerPlayer player)
	{
		base.OnPlayerDisconnect(player);
		secureMouseSlotContents(player);
		(player.InventoryManager as ServerPlayerInventoryManager)?.OnPlayerDisconnect();
	}

	private void secureMouseSlotContents(ServerPlayer player)
	{
		InventoryBase inventoryBase = player.InventoryManager.GetOwnInventory("mouse") as InventoryBase;
		try
		{
			if (inventoryBase != null && !inventoryBase.Empty)
			{
				ItemStackMoveOperation op = new ItemStackMoveOperation(server.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge);
				player.InventoryManager.TryTransferAway(inventoryBase[0], ref op, onlyPlayerInventory: true);
				if (!inventoryBase.Empty && player.Entity != null)
				{
					inventoryBase.DropAll(player.Entity.Pos.XYZ);
				}
			}
		}
		catch (Exception ex)
		{
			ServerMain.Logger.Error("Error while securing mouse slot contents for player {0}, inv: {1}, exception: {2}", player?.PlayerName, inventoryBase?[0], ex);
		}
	}

	public override void OnBeginShutdown()
	{
		IPlayer[] allOnlinePlayers = server.AllOnlinePlayers;
		foreach (IPlayer player in allOnlinePlayers)
		{
			secureMouseSlotContents(player as ServerPlayer);
		}
		base.OnBeginShutdown();
	}

	private void HandleSelectedHotbarSlot(Packet_Client packet, ConnectedClient client)
	{
		int activeSlot = client.Player.ActiveSlot;
		int slotNumber = packet.SelectedHotbarSlot.SlotNumber;
		if (server.EventManager.TriggerBeforeActiveSlotChanged(client.Player, activeSlot, slotNumber))
		{
			if (itemFilter("hotbarslot", client.Player, (InventoryBase)client.Player.inventoryMgr.GetHotbarInventory(), slotNumber))
			{
				server.BroadcastHotbarSlot(client.Player, skipSelf: false);
				return;
			}
			client.Player.ActiveSlot = slotNumber;
			client.Player.InventoryManager.ActiveHotbarSlot.Inventory.DropSlotIfHot(client.Player.InventoryManager.ActiveHotbarSlot, client.Player);
			server.BroadcastHotbarSlot(client.Player);
			(client.Player.Entity.AnimManager as PlayerAnimationManager)?.OnActiveSlotChanged(client.Player.InventoryManager.ActiveHotbarSlot);
			server.EventManager.TriggerAfterActiveSlotChanged(client.Player, activeSlot, slotNumber);
		}
		else
		{
			server.BroadcastHotbarSlot(client.Player, skipSelf: false);
		}
	}

	private void HandleInvOpenClose(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		string inventoryId = packet.InvOpenedClosed.InventoryId;
		if (player.InventoryManager.GetInventory(inventoryId, out var invFound))
		{
			if (packet.InvOpenedClosed.Opened > 0)
			{
				player.InventoryManager.OpenInventory(invFound);
			}
			else
			{
				player.InventoryManager.CloseInventory(invFound);
			}
		}
	}

	private void HandleToolMode(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		ItemSlot activeHotbarSlot = player.InventoryManager.ActiveHotbarSlot;
		if (activeHotbarSlot.Itemstack != null)
		{
			Packet_ToolMode toolMode = packet.ToolMode;
			BlockSelection blockSelection = new BlockSelection
			{
				Position = new BlockPos(toolMode.X, toolMode.Y, toolMode.Z),
				Face = BlockFacing.ALLFACES[toolMode.Face],
				HitPosition = new Vec3d(CollectibleNet.DeserializeDouble(toolMode.HitX), CollectibleNet.DeserializeDouble(toolMode.HitY), CollectibleNet.DeserializeDouble(toolMode.HitZ)),
				SelectionBoxIndex = toolMode.SelectionBoxIndex
			};
			activeHotbarSlot.Itemstack.Collectible.SetToolMode(activeHotbarSlot, player, blockSelection, packet.ToolMode.Mode);
		}
	}

	private void OnUsingTick(float dt)
	{
		foreach (ServerPlayer value in server.PlayersByUid.Values)
		{
			if (value.ConnectionState != EnumClientState.Playing)
			{
				continue;
			}
			ItemSlot activeHotbarSlot = value.inventoryMgr.ActiveHotbarSlot;
			if (value.Entity.Controls.LeftMouseDown && value.WorldData.CurrentGameMode == EnumGameMode.Survival && value.CurrentBlockSelection?.Position != null && activeHotbarSlot.Itemstack != null)
			{
				activeHotbarSlot.Itemstack.Collectible.OnBlockBreaking(value, value.CurrentBlockSelection, activeHotbarSlot, 99f, dt, value.blockBreakingCounter);
				value.blockBreakingCounter++;
			}
			else
			{
				value.blockBreakingCounter = 0;
			}
			if (!value.Entity.LeftHandItemSlot.Empty)
			{
				value.Entity.LeftHandItemSlot.Itemstack.Collectible.OnHeldIdle(value.Entity.LeftHandItemSlot, value.Entity);
			}
			if ((value.Entity.Controls.HandUse == EnumHandInteract.None || value.Entity.Controls.HandUse == EnumHandInteract.BlockInteract) && activeHotbarSlot != null)
			{
				if (activeHotbarSlot.Itemstack != null)
				{
					activeHotbarSlot.Itemstack.Collectible.OnHeldIdle(activeHotbarSlot, value.Entity);
				}
			}
			else if (activeHotbarSlot != null && activeHotbarSlot.Itemstack != null)
			{
				float secondsPassed = (float)(server.ElapsedMilliseconds - value.Entity.Controls.UsingBeginMS) / 1000f;
				int stackSize = activeHotbarSlot.StackSize;
				callOnUsing(activeHotbarSlot, value, value.CurrentUsingBlockSelection ?? value.CurrentBlockSelection, value.CurrentUsingEntitySelection ?? value.CurrentEntitySelection, ref secondsPassed);
				if (activeHotbarSlot.StackSize <= 0)
				{
					activeHotbarSlot.Itemstack = null;
				}
				if (stackSize != activeHotbarSlot.StackSize)
				{
					activeHotbarSlot.MarkDirty();
				}
			}
			else
			{
				value.Entity.Controls.HandUse = EnumHandInteract.None;
			}
		}
	}

	private void UpdateTransitionStates(float dt)
	{
		ItemFilterEnabled = server.api.World.Config.GetBool("itemFilterEnabled");
		foreach (ConnectedClient value in server.Clients.Values)
		{
			if (!value.IsPlayingClient)
			{
				continue;
			}
			foreach (InventoryBase value2 in value.Player.inventoryMgr.Inventories.Values)
			{
				if (!(value2 is InventoryBasePlayer) || value2 is InventoryPlayerCreative)
				{
					continue;
				}
				foreach (ItemSlot item in value2)
				{
					item.Itemstack?.Collectible?.UpdateAndGetTransitionStates(server, item);
				}
			}
		}
	}

	private void SendDirtySlots(float dt)
	{
		foreach (ConnectedClient value in server.Clients.Values)
		{
			if (!value.IsPlayingClient)
			{
				continue;
			}
			foreach (InventoryBase value2 in value.Player.inventoryMgr.Inventories.Values)
			{
				if (!value2.IsDirty)
				{
					continue;
				}
				if (value2 is InventoryCharacter)
				{
					value.Player.BroadcastPlayerData();
				}
				foreach (int dirtySlot in value2.dirtySlots)
				{
					Packet_Server slotUpdatePacket = (value2.InvNetworkUtil as InventoryNetworkUtil).getSlotUpdatePacket(value.Player, dirtySlot);
					if (slotUpdatePacket != null)
					{
						server.SendPacket(value.Id, slotUpdatePacket);
						ItemSlot itemSlot = value2[dirtySlot];
						if (itemSlot != null && itemSlot == value.Player.inventoryMgr.ActiveHotbarSlot)
						{
							value.Player.inventoryMgr.BroadcastHotbarSlot();
						}
					}
				}
				dirtySlots2Clear.Add(value2);
			}
		}
		foreach (InventoryBase item in dirtySlots2Clear)
		{
			item.dirtySlots.Clear();
		}
		dirtySlots2Clear.Clear();
	}

	public override void OnPlayerJoin(ServerPlayer player)
	{
		foreach (InventoryBase value in player.inventoryMgr.Inventories.Values)
		{
			value.AfterBlocksLoaded(server);
		}
		for (int i = 0; i < PlayerInventoryManager.defaultInventories.Length; i++)
		{
			string key = PlayerInventoryManager.defaultInventories[i] + "-" + player.WorldData.PlayerUID;
			if (!player.InventoryManager.Inventories.ContainsKey(key))
			{
				CreateNewInventory(player, PlayerInventoryManager.defaultInventories[i]);
			}
			if (player.WorldData.CurrentGameMode == EnumGameMode.Creative || PlayerInventoryManager.defaultInventories[i] != "creative")
			{
				player.inventoryMgr.Inventories[key].Open(player);
			}
		}
		OnPlayerSwitchGameMode(player);
	}

	private string CreateNewInventory(ServerPlayer player, string inventoryClassName)
	{
		string text = inventoryClassName + "-" + player.PlayerUID;
		InventoryBasePlayer inventoryBasePlayer = (InventoryBasePlayer)ServerMain.ClassRegistry.CreateInventory(inventoryClassName, text, server.api);
		player.SetInventory(inventoryBasePlayer);
		inventoryBasePlayer.AfterBlocksLoaded(server);
		return text;
	}

	public override void OnPlayerSwitchGameMode(ServerPlayer player)
	{
		IInventory ownInventory = player.InventoryManager.GetOwnInventory("creative");
		IInventory ownInventory2 = player.InventoryManager.GetOwnInventory("craftinggrid");
		if (player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			ownInventory?.Open(player);
			ownInventory2?.Close(player);
		}
		if (player.WorldData.CurrentGameMode == EnumGameMode.Guest || player.WorldData.CurrentGameMode == EnumGameMode.Survival)
		{
			ownInventory?.Close(player);
			ownInventory2?.Open(player);
		}
	}

	private void HandleHandInteraction(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		Packet_ClientHandInteraction handInteraction = packet.HandInteraction;
		if (handInteraction.EnumHandInteract >= 4)
		{
			server.OnHandleBlockInteract(packet, client);
			return;
		}
		int num = handInteraction.SlotId;
		IInventory value;
		if (handInteraction.InventoryId == null)
		{
			if (handInteraction.SlotId >= 10)
			{
				string invID = "backpack-" + player.PlayerUID;
				player.InventoryManager.GetInventory(invID, out var invFound);
				value = invFound;
				num -= 10;
			}
			else
			{
				value = player.inventoryMgr.GetHotbarInventory();
			}
		}
		else
		{
			player.InventoryManager.Inventories.TryGetValue(handInteraction.InventoryId, out value);
		}
		if (value == null)
		{
			ServerMain.Logger.Audit("{0} tried to interact with an inventory that does not exist: {1}.", player.PlayerName, handInteraction.InventoryId);
			return;
		}
		if (!value.HasOpened(player))
		{
			ServerMain.Logger.Audit("{0} tried to interact with an inventory that they did not open before: {1}.", player.PlayerName, value.InventoryID);
			return;
		}
		ItemSlot itemSlot = value[num];
		if (itemSlot == null || itemSlot.Itemstack == null)
		{
			return;
		}
		BlockSelection blockSelection = null;
		EntitySelection entitySelection = null;
		EnumHandInteract useType = (EnumHandInteract)handInteraction.UseType;
		if (useType == EnumHandInteract.None || handInteraction.MouseButton != 2)
		{
			return;
		}
		BlockPos position = new BlockPos(handInteraction.X, handInteraction.Y, handInteraction.Z);
		BlockFacing face = BlockFacing.ALLFACES[handInteraction.OnBlockFace];
		Vec3d hitPosition = new Vec3d(CollectibleNet.DeserializeDoublePrecise(handInteraction.HitX), CollectibleNet.DeserializeDoublePrecise(handInteraction.HitY), CollectibleNet.DeserializeDoublePrecise(handInteraction.HitZ));
		if (handInteraction.X != 0 || handInteraction.Y != 0 || handInteraction.Z != 0)
		{
			blockSelection = new BlockSelection
			{
				Position = position,
				Face = face,
				HitPosition = hitPosition,
				SelectionBoxIndex = handInteraction.SelectionBoxIndex
			};
		}
		if (handInteraction.OnEntityId != 0L)
		{
			server.LoadedEntities.TryGetValue(handInteraction.OnEntityId, out var value2);
			if (value2 == null)
			{
				return;
			}
			entitySelection = new EntitySelection
			{
				Face = face,
				HitPosition = hitPosition,
				Entity = value2,
				Position = value2.Pos.XYZ
			};
		}
		if (server.Config.AntiAbuse >= EnumProtectionLevel.Basic && (blockSelection != null || entitySelection != null) && !((blockSelection?.Position != null) ? ((IPlayer)player).IsInInteractionRangeOf(blockSelection.Position, 0f) : player.IsInInteractionRangeOf(entitySelection.Entity, 0f)))
		{
			ServerMain.Logger.Audit("{0} tried to interact with {1} out of range.", player.PlayerName, (blockSelection?.Position != null) ? "a block" : "an entity");
			if (blockSelection != null)
			{
				(server.GetBlock(blockSelection.Position)?.GetBlockEntity<BlockEntity>(blockSelection.Position))?.MarkDirty();
			}
			return;
		}
		player.CurrentUsingBlockSelection = blockSelection;
		player.CurrentUsingEntitySelection = entitySelection;
		EntityControls controls = player.Entity.Controls;
		float secondsPassed = (float)(server.ElapsedMilliseconds - controls.UsingBeginMS) / 1000f;
		EnumHandling handling = EnumHandling.PassThrough;
		server.EventManager.TriggerHandInteraction(player, (EnumHandInteractNw)handInteraction.EnumHandInteract, secondsPassed, ref handling);
		if (handling == EnumHandling.PreventDefault)
		{
			return;
		}
		switch ((EnumHandInteractNw)handInteraction.EnumHandInteract)
		{
		case EnumHandInteractNw.StartHeldItemUse:
		{
			EnumHandHandling handling2 = EnumHandHandling.NotHandled;
			itemSlot.Itemstack.Collectible.OnHeldUseStart(itemSlot, player.Entity, blockSelection, entitySelection, useType, handInteraction.FirstEvent > 0, ref handling2);
			controls.HandUse = ((handling2 != EnumHandHandling.NotHandled) ? useType : EnumHandInteract.None);
			controls.UsingBeginMS = server.ElapsedMilliseconds;
			controls.UsingCount = 0;
			break;
		}
		case EnumHandInteractNw.CancelHeldItemUse:
		{
			int num3 = 0;
			while (controls.HandUse != EnumHandInteract.None && controls.UsingCount < handInteraction.UsingCount && num3++ < 5000)
			{
				callOnUsing(itemSlot, player, blockSelection, entitySelection, ref secondsPassed, callStop: false);
			}
			if (num3 >= 5000)
			{
				ServerMain.Logger.Warning("CancelHeldItemUse packet: Excess (5000+) UseStep calls from {2} on item {0}, would require {1} more steps to complete. Will abort.", itemSlot.Itemstack?.GetName(), handInteraction.UsingCount - controls.UsingCount, player.PlayerName);
			}
			EnumItemUseCancelReason cancelReason = (EnumItemUseCancelReason)handInteraction.CancelReason;
			if (itemSlot.Itemstack == null)
			{
				controls.HandUse = EnumHandInteract.None;
			}
			else
			{
				controls.HandUse = itemSlot.Itemstack.Collectible.OnHeldUseCancel(secondsPassed, itemSlot, player.Entity, blockSelection, entitySelection, cancelReason);
			}
			break;
		}
		case EnumHandInteractNw.StopHeldItemUse:
			if (controls.HandUse != EnumHandInteract.None)
			{
				int num2 = 0;
				while (controls.HandUse != EnumHandInteract.None && controls.UsingCount < handInteraction.UsingCount && num2++ < 5000)
				{
					callOnUsing(itemSlot, player, blockSelection, entitySelection, ref secondsPassed);
				}
				if (num2 >= 5000)
				{
					ServerMain.Logger.Warning("StopHeldItemUse packet: Excess (5000+) UseStep calls from {2} on item {0}, would require {1} more steps to complete. Will abort.", itemSlot.Itemstack?.GetName(), handInteraction.UsingCount - controls.UsingCount, player.PlayerName);
				}
				controls.HandUse = EnumHandInteract.None;
				itemSlot.Itemstack?.Collectible.OnHeldUseStop(secondsPassed, itemSlot, player.Entity, blockSelection, entitySelection, useType);
			}
			break;
		}
		if (itemSlot.StackSize <= 0)
		{
			itemSlot.Itemstack = null;
		}
	}

	private void HandleFlipItemStacks(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		string sourceInventoryId = packet.Flipitemstacks.SourceInventoryId;
		if (player.InventoryManager.GetInventory(sourceInventoryId, out var invFound))
		{
			if (itemFilter("flip", player, invFound, packet.Flipitemstacks.SourceSlot))
			{
				return;
			}
			(invFound.InvNetworkUtil as InventoryNetworkUtil).HandleClientPacket(player, packet.Id, packet);
		}
		if (player.inventoryMgr.IsVisibleHandSlot(sourceInventoryId, packet.Flipitemstacks.TargetSlot))
		{
			server.BroadcastHotbarSlot(player);
		}
	}

	private void HandleMoveItemstack(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		string sourceInventoryId = packet.MoveItemstack.SourceInventoryId;
		string targetInventoryId = packet.MoveItemstack.TargetInventoryId;
		if (player.InventoryManager.GetInventory(sourceInventoryId, out var invFound))
		{
			if (itemFilter("move", player, invFound, packet.MoveItemstack.SourceSlot))
			{
				return;
			}
			(invFound.InvNetworkUtil as InventoryNetworkUtil).HandleClientPacket(player, packet.Id, packet);
			if (player.inventoryMgr.IsVisibleHandSlot(sourceInventoryId, packet.MoveItemstack.SourceSlot))
			{
				server.BroadcastHotbarSlot(player);
			}
		}
		if (player.inventoryMgr.IsVisibleHandSlot(targetInventoryId, packet.MoveItemstack.TargetSlot))
		{
			server.BroadcastHotbarSlot(player);
		}
	}

	private void HandleCreateItemstack(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		Packet_CreateItemstack createItemstack = packet.CreateItemstack;
		player.InventoryManager.GetInventory(createItemstack.TargetInventoryId, out var invFound);
		ItemSlot itemSlot = invFound?[createItemstack.TargetSlot];
		if (player.WorldData.CurrentGameMode == EnumGameMode.Creative && itemSlot != null)
		{
			ItemStack itemStack = (itemSlot.Itemstack = StackConverter.FromPacket(createItemstack.Itemstack, server));
			itemSlot.MarkDirty();
			ServerMain.Logger.Audit("{0} creative mode created item stack {1}x{2}", player.PlayerName, itemStack.StackSize, itemStack.GetName());
		}
		else
		{
			Packet_Server slotUpdatePacket = (((InventoryBase)player.InventoryManager.Inventories[createItemstack.TargetInventoryId]).InvNetworkUtil as InventoryNetworkUtil).getSlotUpdatePacket(player, createItemstack.TargetSlot);
			if (slotUpdatePacket != null)
			{
				server.SendPacket(player.ClientId, slotUpdatePacket);
			}
		}
	}

	private void HandleActivateInventorySlot(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		string targetInventoryId = packet.ActivateInventorySlot.TargetInventoryId;
		if (player.InventoryManager.GetInventory(targetInventoryId, out var invFound))
		{
			if (itemFilter("activate", player, invFound, packet.ActivateInventorySlot.TargetSlot))
			{
				return;
			}
			(invFound.InvNetworkUtil as InventoryNetworkUtil).HandleClientPacket(player, packet.Id, packet);
		}
		else
		{
			ServerMain.Logger.Warning("Got activate inventory slot packet on inventory " + targetInventoryId + " but no such inventory currently opened?");
		}
		if (player.inventoryMgr.IsVisibleHandSlot(targetInventoryId, packet.ActivateInventorySlot.TargetSlot))
		{
			server.BroadcastHotbarSlot(player);
		}
	}

	private bool itemFilter(string action, ServerPlayer player, InventoryBase invFound, int targetSlot)
	{
		if (!ItemFilterEnabled)
		{
			return false;
		}
		ItemSlot itemSlot = invFound?[targetSlot];
		if (itemSlot == null || itemSlot.Empty || itemSlot.Itemstack.Collectible == null)
		{
			return false;
		}
		if (itemSlot.Itemstack.Collectible.Code == (AssetLocation)"gear-rusty" && itemSlot.StackSize > 1000)
		{
			ServerMain.Logger.Warning("Player {0} has a rusty gear stack of '{1}'. Will delete.", player.PlayerName, itemSlot.StackSize);
			ServerMain.Logger.Audit("Player {0} has a rusty gear stack of '{1}'. Will delete.", player.PlayerName, itemSlot.StackSize);
			itemSlot.Itemstack = null;
			itemSlot.MarkDirty();
			return true;
		}
		ITreeAttribute treeAttribute = itemSlot.Itemstack.Attributes.GetTreeAttribute("buffs");
		int num = 0;
		if (treeAttribute != null)
		{
			while (treeAttribute.HasAttribute(num.ToString()))
			{
				TreeAttribute treeAttribute2 = treeAttribute[num.ToString()] as TreeAttribute;
				if (treeAttribute2?.GetString("code") == "hardened" && treeAttribute2.GetFloat("multiplier") > 2f)
				{
					ServerMain.Logger.Warning("Player {0} has a buffable item '{1}' with a multiplier of {2}", player.PlayerName, itemSlot.Itemstack.GetName(), treeAttribute2.GetFloat("multiplier"));
					ServerMain.Logger.Audit("Player {0} has a buffable item '{1}' with a multiplier of {2}", player.PlayerName, itemSlot.Itemstack.GetName(), treeAttribute2.GetFloat("multiplier"));
				}
				num++;
			}
		}
		return false;
	}

	private void callOnUsing(ItemSlot slot, ServerPlayer player, BlockSelection blockSel, EntitySelection entitySel, ref float secondsPassed, bool callStop = true)
	{
		EntityControls controls = player.Entity.Controls;
		EnumHandInteract handUse = controls.HandUse;
		if (!slot.Empty)
		{
			controls.UsingCount++;
			controls.HandUse = slot.Itemstack.Collectible.OnHeldUseStep(secondsPassed, slot, player.Entity, blockSel, entitySel);
			if (callStop && controls.HandUse == EnumHandInteract.None)
			{
				slot.Itemstack?.Collectible.OnHeldUseStop(secondsPassed, slot, player.Entity, blockSel, entitySel, handUse);
			}
		}
		secondsPassed += 0.02f;
	}
}
