using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Common.Database;

namespace Vintagestory.Server;

public class ServerSystemBlockSimulation : ServerSystem
{
	private class BlockPosWithExtraObject
	{
		public BlockPos pos;

		public object extra;

		public BlockPosWithExtraObject(BlockPos pos, object extra)
		{
			this.pos = pos;
			this.extra = extra;
		}
	}

	private ConcurrentQueue<object> queuedTicks = new ConcurrentQueue<object>();

	private Dictionary<long, ServerChunk> chunksToBeTicked = new Dictionary<long, ServerChunk>();

	private object clientIdsLock = new object();

	private List<int> clientIds = new List<int>();

	private Random rand = new Random();

	[ThreadStatic]
	private static FastMemoryStream reusableSendingStream;

	private List<Packet_BlockEntity> blockEntitiesPacked = new List<Packet_BlockEntity>();

	private List<BlockPos> noblockEntities = new List<BlockPos>();

	private List<Packet_BlockEntity> playerBlockEntitiesPacked = new List<Packet_BlockEntity>();

	private HashSet<BlockPos> positionsDone = new HashSet<BlockPos>();

	private BlockPos tmpPos = new BlockPos(0);

	private FluidBlockPos tmpLiquidPos = new FluidBlockPos();

	public ServerSystemBlockSimulation(ServerMain server)
		: base(server)
	{
		server.RegisterGameTickListener(UpdateEvery100ms, 100);
		server.PacketHandlers[3] = HandleBlockPlaceOrBreak;
		server.PacketHandlers[22] = HandleBlockEntityPacket;
		server.OnHandleBlockInteract = HandleBlockInteract;
	}

	public override void OnBeginInitialization()
	{
		server.api.RegisterBlock(new Block
		{
			DrawType = EnumDrawType.Empty,
			MatterState = EnumMatterState.Gas,
			BlockMaterial = EnumBlockMaterial.Air,
			Code = new AssetLocation("air"),
			Sounds = new BlockSounds(),
			RenderPass = EnumChunkRenderPass.Liquid,
			Replaceable = 9999,
			MaterialDensity = 1,
			LightAbsorption = 0,
			CollisionBoxes = null,
			SelectionBoxes = null,
			RainPermeable = true,
			SideSolid = new SmallBoolArray(0),
			SideAo = new SmallBoolArray(0),
			AllSidesOpaque = false
		});
		Item item = new Item(0);
		server.api.RegisterItem(new Item
		{
			Code = new AssetLocation("air")
		});
		for (int i = 1; i < 4000; i++)
		{
			server.Items.Add(item);
		}
		server.api.eventapi.ChunkColumnLoaded += Event_ChunkColumnLoaded;
	}

	private void Event_ChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
	{
	}

	public override void OnLoadAssets()
	{
		server.api.Logger.VerboseDebug("Block simulation resolving collectibles");
		server.LoadCollectibles(server.Items, server.Blocks);
		IList<Block> blocks = server.Blocks;
		for (int i = 0; i < blocks.Count; i++)
		{
			Block block = blocks[i];
			if (block == null)
			{
				continue;
			}
			AssetLocation code = block.Code;
			if (block.Drops != null)
			{
				BlockDropItemStack[] drops = block.Drops;
				for (int j = 0; j < drops.Length; j++)
				{
					drops[j].Resolve(server, "Block ", code);
				}
			}
			if (block.CreativeInventoryStacks != null)
			{
				for (int k = 0; k < block.CreativeInventoryStacks.Length; k++)
				{
					CreativeTabAndStackList creativeTabAndStackList = block.CreativeInventoryStacks[k];
					for (int l = 0; l < creativeTabAndStackList.Stacks.Length; l++)
					{
						creativeTabAndStackList.Stacks[l].Resolve(server, "Creative inventory stack of block ", code);
					}
				}
			}
			if (block.CombustibleProps?.SmeltedStack != null)
			{
				block.CombustibleProps.SmeltedStack.Resolve(server, "Smeltedstack of Block ", code);
			}
			if (block.NutritionProps?.EatenStack != null)
			{
				block.NutritionProps.EatenStack.Resolve(server, "Eatenstack of Block ", code);
			}
			if (block.TransitionableProps != null)
			{
				TransitionableProperties[] transitionableProps = block.TransitionableProps;
				foreach (TransitionableProperties transitionableProperties in transitionableProps)
				{
					if (transitionableProperties.Type != EnumTransitionType.None)
					{
						transitionableProperties.TransitionedStack?.Resolve(server, transitionableProperties.Type.ToString() + " Transition stack of Block ", code);
					}
				}
			}
			if (block.GrindingProps?.GroundStack != null)
			{
				block.GrindingProps.GroundStack.Resolve(server, "Grinded stack of Block ", code);
				if (block.GrindingProps.usedObsoleteNotation)
				{
					server.api.Logger.Warning("Block code {0}: Property GrindedStack is obsolete, please use GroundStack instead", block.Code);
				}
			}
			if (block.CrushingProps?.CrushedStack != null)
			{
				block.CrushingProps.CrushedStack.Resolve(server, "Crushed stack of Block ", code);
			}
		}
		server.api.Logger.VerboseDebug("Resolved blocks stacks");
		((List<Item>)server.Items).ForEach((Item item) =>
		{
			if (item != null)
			{
				AssetLocation code2 = item.Code;
				if (code2 != null)
				{
					CreativeTabAndStackList[] creativeInventoryStacks = item.CreativeInventoryStacks;
					if (creativeInventoryStacks != null)
					{
						for (int n = 0; n < creativeInventoryStacks.Length; n++)
						{
							JsonItemStack[] stacks = creativeInventoryStacks[n].Stacks;
							for (int num = 0; num < stacks.Length; num++)
							{
								stacks[num].Resolve(server, "Creative inventory stack of Item ", code2);
							}
						}
					}
					if (item.CombustibleProps != null && item.CombustibleProps.SmeltedStack != null)
					{
						item.CombustibleProps.SmeltedStack.Resolve(server, "Combustible props for Item ", code2);
					}
					if (item.NutritionProps?.EatenStack != null)
					{
						item.NutritionProps.EatenStack.Resolve(server, "Eatenstack of Item ", code2);
					}
					if (item.TransitionableProps != null)
					{
						TransitionableProperties[] transitionableProps2 = item.TransitionableProps;
						foreach (TransitionableProperties transitionableProperties2 in transitionableProps2)
						{
							if (transitionableProperties2.Type != EnumTransitionType.None)
							{
								transitionableProperties2.TransitionedStack?.Resolve(server, transitionableProperties2.Type.ToString() + " Transition stack of Item ", code2);
							}
						}
					}
					if (item.GrindingProps?.GroundStack != null)
					{
						item.GrindingProps.GroundStack.Resolve(server, "Grinded stack of item ", code2);
						if (item.GrindingProps.usedObsoleteNotation)
						{
							server.api.Logger.Warning("Item code {0}: Property GrindedStack is obsolete, please use GroundStack instead", item.Code);
						}
					}
					if (item.CrushingProps?.CrushedStack != null)
					{
						item.CrushingProps.CrushedStack.Resolve(server, "Crushed stack of item ", code2);
					}
				}
			}
		});
		server.api.Logger.VerboseDebug("Resolved items stacks");
	}

	public override void OnBeginConfiguration()
	{
		IChatCommandApi chatCommands = server.api.ChatCommands;
		CommandArgumentParsers parsers = server.api.ChatCommands.Parsers;
		_ = server.api;
		chatCommands.Get("debug").BeginSub("bt").WithDesc("Block ticking debug subsystem")
			.BeginSub("at")
			.WithDesc("Tick a block at given position")
			.WithArgs(parsers.WorldPosition("position"))
			.HandleWith(onTickBlockCmd)
			.EndSub()
			.BeginSub("qi")
			.WithDesc("Queue info")
			.HandleWith(onTickQueueCmd)
			.EndSub()
			.BeginSub("qc")
			.WithDesc("Clear tick queue")
			.HandleWith(onTickQueueClearCmd)
			.EndSub()
			.EndSub();
		base.OnBeginConfiguration();
	}

	private TextCommandResult onTickQueueClearCmd(TextCommandCallingArgs args)
	{
		queuedTicks = new ConcurrentQueue<object>();
		return TextCommandResult.Success("Queue is now cleared");
	}

	private TextCommandResult onTickQueueCmd(TextCommandCallingArgs args)
	{
		return TextCommandResult.Success(queuedTicks.Count + " elements in queue");
	}

	private TextCommandResult onTickBlockCmd(TextCommandCallingArgs args)
	{
		try
		{
			BlockPos asBlockPos = (args[0] as Vec3d).AsBlockPos;
			Block block = server.Api.World.BlockAccessor.GetBlock(asBlockPos);
			if (tryTickBlock(block, asBlockPos))
			{
				return TextCommandResult.Success(string.Concat(new string[5]
				{
					"Accepted tick [block=",
					block.Code,
					"] at [",
					asBlockPos.ToString(),
					"]"
				}));
			}
			return TextCommandResult.Success(string.Concat(new string[5]
			{
				"Declined tick [block=",
				block.Code,
				"] at [",
				asBlockPos.ToString(),
				"]"
			}));
		}
		catch (Exception ex)
		{
			ServerMain.Logger.Error(ex);
			return TextCommandResult.Success("An unexpected error occurred trying to tick block: " + ex.Message);
		}
	}

	public override void OnBeginModsAndConfigReady()
	{
		IList<Block> blocks = server.Blocks;
		for (int i = 0; i < blocks.Count; i++)
		{
			blocks[i]?.OnLoadedNative(server.api);
		}
		server.api.Logger.Debug("Block simulation loaded blocks");
		((List<Item>)server.Items).ForEach((Item item) =>
		{
			item?.OnLoadedNative(server.api);
		});
		server.api.Logger.Debug("Block simulation loaded items");
	}

	public override void OnPlayerJoin(ServerPlayer player)
	{
		lock (clientIdsLock)
		{
			clientIds.Add(player.ClientId);
		}
	}

	public override void OnPlayerDisconnect(ServerPlayer player)
	{
		lock (clientIdsLock)
		{
			clientIds.Remove(player.ClientId);
		}
	}

	private void HandleBlockEntityPacket(Packet_Client packet, ConnectedClient client)
	{
		Packet_BlockEntityPacket blockEntityPacket = packet.BlockEntityPacket;
		server.WorldMap.GetBlockEntity(new BlockPos(blockEntityPacket.X, blockEntityPacket.Y, blockEntityPacket.Z))?.OnReceivedClientPacket(client.Player, blockEntityPacket.Packetid, blockEntityPacket.Data);
	}

	internal void HandleBlockPlaceOrBreak(Packet_Client packet, ConnectedClient client)
	{
		Packet_ClientBlockPlaceOrBreak blockPlaceOrBreak = packet.BlockPlaceOrBreak;
		BlockSelection blockSelection = new BlockSelection
		{
			DidOffset = (blockPlaceOrBreak.DidOffset > 0),
			Face = BlockFacing.ALLFACES[blockPlaceOrBreak.OnBlockFace],
			Position = new BlockPos(blockPlaceOrBreak.X, blockPlaceOrBreak.Y, blockPlaceOrBreak.Z),
			HitPosition = new Vec3d(CollectibleNet.DeserializeDouble(blockPlaceOrBreak.HitX), CollectibleNet.DeserializeDouble(blockPlaceOrBreak.HitY), CollectibleNet.DeserializeDouble(blockPlaceOrBreak.HitZ)),
			SelectionBoxIndex = blockPlaceOrBreak.SelectionBoxIndex,
			SelectionBoxId = blockPlaceOrBreak.SelectionBoxId
		};
		if (client.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator)
		{
			return;
		}
		EnumWorldAccessResponse enumWorldAccessResponse;
		if ((enumWorldAccessResponse = server.WorldMap.TestBlockAccess(client.Player, blockSelection, EnumBlockAccessFlags.BuildOrBreak, out string claimant)) != EnumWorldAccessResponse.Granted)
		{
			RevertBlockInteractions(client.Player, blockSelection.Position);
			string code = "noprivilege-buildbreak-" + enumWorldAccessResponse.ToString().ToLowerInvariant();
			if (claimant == null)
			{
				claimant = "?";
			}
			else if (claimant.StartsWithOrdinal("custommessage-"))
			{
				code = "noprivilege-buildbreak-" + claimant.Substring("custommessage-".Length);
			}
			client.Player.SendIngameError(code, null, claimant);
			return;
		}
		if (blockPlaceOrBreak.Mode == 2)
		{
			if (server.BlockAccessor.GetChunkAtBlockPos(blockSelection.Position) is WorldChunk worldChunk)
			{
				worldChunk.BreakDecor(server, blockSelection.Position, blockSelection.Face);
				worldChunk.MarkModified();
			}
			return;
		}
		Block block = server.WorldMap.RelaxedBlockAccess.GetBlock(blockSelection.Position, 2);
		int oldBlockId = ((!block.SideSolid.Any) ? server.WorldMap.RelaxedBlockAccess.GetBlock(blockSelection.Position).Id : block.BlockId);
		ItemStack withItemStack = client.Player.inventoryMgr.ActiveHotbarSlot?.Itemstack;
		if (!TryModifyBlockInWorld(client.Player, blockPlaceOrBreak))
		{
			RevertBlockInteractions(client.Player, blockSelection.Position);
			return;
		}
		server.TriggerNeighbourBlocksUpdate(blockSelection.Position);
		switch (blockPlaceOrBreak.Mode)
		{
		case 1:
			server.EventManager.TriggerDidPlaceBlock(client.Player, oldBlockId, blockSelection, withItemStack);
			break;
		case 0:
			server.EventManager.TriggerDidBreakBlock(client.Player, oldBlockId, blockSelection);
			break;
		}
	}

	internal void HandleBlockInteract(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		Packet_ClientHandInteraction handInteraction = packet.HandInteraction;
		if (client.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator || handInteraction.UseType == 0 || handInteraction.MouseButton != 2)
		{
			return;
		}
		BlockPos blockPos = new BlockPos(handInteraction.X, handInteraction.Y, handInteraction.Z);
		BlockFacing face = BlockFacing.ALLFACES[handInteraction.OnBlockFace];
		Vec3d hitPosition = new Vec3d(CollectibleNet.DeserializeDoublePrecise(handInteraction.HitX), CollectibleNet.DeserializeDoublePrecise(handInteraction.HitY), CollectibleNet.DeserializeDoublePrecise(handInteraction.HitZ));
		BlockSelection blockSelection = new BlockSelection
		{
			Position = blockPos,
			Face = face,
			HitPosition = hitPosition,
			SelectionBoxIndex = handInteraction.SelectionBoxIndex,
			SelectionBoxId = handInteraction.SelectionBoxId
		};
		if (server.Config.AntiAbuse >= EnumProtectionLevel.Basic && !((IPlayer)player).IsInInteractionRangeOf(blockPos, 0f))
		{
			ServerMain.Logger.Audit("{0} tried to interact with a block out of range.", player.PlayerName);
			RevertBlockInteractions(client.Player, blockSelection.Position);
			return;
		}
		EnumWorldAccessResponse enumWorldAccessResponse;
		if ((enumWorldAccessResponse = server.WorldMap.TestBlockAccess(client.Player, blockSelection, EnumBlockAccessFlags.Use)) != EnumWorldAccessResponse.Granted)
		{
			RevertBlockInteractions(client.Player, blockSelection.Position);
			string code = "noprivilege-use-" + enumWorldAccessResponse.ToString().ToLowerInvariant();
			LandClaim blockingLandClaimant = server.WorldMap.GetBlockingLandClaimant(client.Player, blockSelection.Position, EnumBlockAccessFlags.BuildOrBreak);
			client.Player.SendIngameError(code, null, blockingLandClaimant?.LastKnownOwnerName);
			return;
		}
		Block block = server.BlockAccessor.GetBlock(blockPos);
		EntityControls controls = player.Entity.Controls;
		float secondsPassed = (float)(server.ElapsedMilliseconds - controls.UsingBeginMS) / 1000f;
		switch ((EnumHandInteractNw)handInteraction.EnumHandInteract)
		{
		default:
			return;
		case EnumHandInteractNw.StartBlockUse:
			controls.HandUse = (block.OnBlockInteractStart(server, player, blockSelection) ? EnumHandInteract.BlockInteract : EnumHandInteract.None);
			controls.UsingBeginMS = server.ElapsedMilliseconds;
			controls.UsingCount = 0;
			server.EventManager.TriggerDidUseBlock(client.Player, blockSelection);
			return;
		case EnumHandInteractNw.CancelBlockUse:
		{
			while (controls.HandUse != EnumHandInteract.None && controls.UsingCount < handInteraction.UsingCount)
			{
				callOnUsingBlock(player, block, blockSelection, ref secondsPassed, callStop: false);
			}
			EnumItemUseCancelReason cancelReason = (EnumItemUseCancelReason)handInteraction.CancelReason;
			controls.HandUse = ((!block.OnBlockInteractCancel(secondsPassed, server, player, blockSelection, cancelReason)) ? EnumHandInteract.BlockInteract : EnumHandInteract.None);
			return;
		}
		case EnumHandInteractNw.StopBlockUse:
			while (controls.HandUse != EnumHandInteract.None && controls.UsingCount < handInteraction.UsingCount)
			{
				callOnUsingBlock(player, block, blockSelection, ref secondsPassed);
			}
			if (controls.HandUse != EnumHandInteract.None)
			{
				controls.HandUse = EnumHandInteract.None;
				block.OnBlockInteractStop(secondsPassed, server, player, blockSelection);
			}
			return;
		case EnumHandInteractNw.StepBlockUse:
			break;
		}
		while (controls.HandUse != EnumHandInteract.None && controls.UsingCount < handInteraction.UsingCount)
		{
			callOnUsingBlock(player, block, blockSelection, ref secondsPassed);
		}
	}

	private void callOnUsingBlock(ServerPlayer player, Block block, BlockSelection blockSel, ref float secondsPassed, bool callStop = true)
	{
		EntityControls controls = player.Entity.Controls;
		controls.HandUse = (block.OnBlockInteractStep(secondsPassed, server, player, blockSel) ? EnumHandInteract.BlockInteract : EnumHandInteract.None);
		controls.UsingCount++;
		if (callStop && controls.HandUse == EnumHandInteract.None)
		{
			block.OnBlockInteractStop(secondsPassed, server, player, blockSel);
		}
		secondsPassed += 0.02f;
	}

	private void RevertBlockInteractions(IServerPlayer targetPlayer, BlockPos pos)
	{
		RevertBlockInteraction2(targetPlayer, pos, sendPlayerData: false);
		RevertBlockInteraction2(targetPlayer, pos.AddCopy(BlockFacing.NORTH), sendPlayerData: false);
		RevertBlockInteraction2(targetPlayer, pos.AddCopy(BlockFacing.EAST), sendPlayerData: false);
		RevertBlockInteraction2(targetPlayer, pos.AddCopy(BlockFacing.SOUTH), sendPlayerData: false);
		RevertBlockInteraction2(targetPlayer, pos.AddCopy(BlockFacing.UP), sendPlayerData: false);
		RevertBlockInteraction2(targetPlayer, pos.AddCopy(BlockFacing.DOWN), sendPlayerData: false);
		server.SendOwnPlayerData(targetPlayer);
	}

	private void RevertBlockInteraction2(IServerPlayer targetPlayer, BlockPos pos, bool sendPlayerData = true)
	{
		server.SendSetBlock(targetPlayer, server.WorldMap.RawRelaxedBlockAccess.GetBlockId(pos), pos.X, pos.InternalY, pos.Z);
		BlockEntity blockEntity = server.WorldMap.RawRelaxedBlockAccess.GetBlockEntity(pos);
		if (blockEntity != null)
		{
			server.SendBlockEntity(targetPlayer, blockEntity);
		}
		if (sendPlayerData)
		{
			server.SendOwnPlayerData(targetPlayer);
		}
	}

	private bool TryModifyBlockInWorld(ServerPlayer player, Packet_ClientBlockPlaceOrBreak cmd)
	{
		BlockPos blockPos = new BlockPos(cmd.X, cmd.Y, cmd.Z);
		Vec3d hitPosition = new Vec3d(CollectibleNet.DeserializeDouble(cmd.HitX), CollectibleNet.DeserializeDouble(cmd.HitY), CollectibleNet.DeserializeDouble(cmd.HitZ));
		ItemSlot activeHotbarSlot = player.inventoryMgr.ActiveHotbarSlot;
		bool flag = server.PlayerHasPrivilege(player.ClientId, Privilege.pickingrange);
		if (server.Config.AntiAbuse >= EnumProtectionLevel.Basic && !flag && !((IPlayer)player).IsInInteractionRangeOf(blockPos, 0.7f))
		{
			ServerMain.Logger.Audit("Client {0} tried to break/place a block out of range | {1}", player.PlayerName, activeHotbarSlot.Itemstack?.ToString());
			activeHotbarSlot.MarkDirty();
			return false;
		}
		BlockSelection blockSelection = new BlockSelection
		{
			Face = BlockFacing.ALLFACES[cmd.OnBlockFace],
			Position = blockPos,
			HitPosition = hitPosition,
			SelectionBoxIndex = cmd.SelectionBoxIndex,
			DidOffset = (cmd.DidOffset > 0)
		};
		if (cmd.Mode == 1)
		{
			if (activeHotbarSlot == null || activeHotbarSlot.Itemstack == null)
			{
				ServerMain.Logger.Audit("{0} tried to place a block but rejected because the client hand is empty", player.PlayerName);
				return false;
			}
			if (activeHotbarSlot.Itemstack.Class != EnumItemClass.Block)
			{
				ServerMain.Logger.Audit("{0} tried to place a block but rejected because the itemstack in client hand is not a block {1}", player.PlayerName, activeHotbarSlot.Itemstack);
				return false;
			}
			int id = activeHotbarSlot.Itemstack.Id;
			Block block = server.Blocks[id];
			if (block == null)
			{
				ServerMain.Logger.Audit("{0} tried to place a block of id: {1} , which does not exist", player.PlayerName, id);
				return false;
			}
			Block block2 = server.WorldMap.RawRelaxedBlockAccess.GetBlock(blockPos, (!block.ForFluidsLayer) ? 1 : 2);
			if (!block2.IsReplacableBy(block))
			{
				JsonObject jsonObject = block.Attributes?["ignoreServerReplaceableTest"];
				if ((jsonObject == null || !jsonObject.Exists || !jsonObject.AsBool()) && server.Blocks[id].decorBehaviorFlags == 0)
				{
					ServerMain.Logger.Audit("{0} tried to place a block but rejected because the client tried to overwrite an existing, non-replacable old: {1}, new: {2}", player.PlayerName, block2.Code?.ToString(), block.Code?.ToString());
					return false;
				}
			}
			if (IsAnyPlayerInBlock(blockPos, block, player))
			{
				ServerMain.Logger.Audit("{0} tried to place a block but rejected because it would intersect with another player", player.PlayerName);
				return false;
			}
			string failureCode = "";
			if (!block.TryPlaceBlock(server, player, activeHotbarSlot.Itemstack, blockSelection, ref failureCode))
			{
				ServerMain.Logger.Audit("{0} tried to place a block but rejected because OnPlaceBlock returns false. Failure code {1}", player.PlayerName, failureCode);
				return false;
			}
			if (server.WorldMap.GetChunk(blockSelection.Position) is ServerChunk serverChunk)
			{
				serverChunk.BlocksPlaced++;
				serverChunk.DirtyForSaving = true;
			}
			if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
			{
				activeHotbarSlot.Itemstack.StackSize--;
				if (activeHotbarSlot.Itemstack.StackSize <= 0)
				{
					activeHotbarSlot.Itemstack = null;
					server.BroadcastHotbarSlot(player);
				}
				activeHotbarSlot.MarkDirty();
			}
		}
		else
		{
			Block block3 = server.WorldMap.RelaxedBlockAccess.GetBlock(blockPos, 2);
			int index = ((!block3.SideSolid.Any) ? server.WorldMap.RelaxedBlockAccess.GetBlock(blockPos, 1).Id : block3.BlockId);
			Block block4 = (blockSelection.Block = server.Blocks[index]);
			IItemStack itemstack = activeHotbarSlot.Itemstack;
			int num = 0;
			if (itemstack != null)
			{
				num = itemstack.Collectible.GetToolTier(activeHotbarSlot);
			}
			int requiredMiningTier = block4.GetRequiredMiningTier(server.World, blockPos);
			if (player.WorldData.CurrentGameMode != EnumGameMode.Creative && requiredMiningTier > num)
			{
				ServerMain.Logger.Audit("{0} tried to break a block but rejected because his tools mining tier is too low {1} {2}/{3}", player.PlayerName, block4.Code.ToString(), num, requiredMiningTier);
				return false;
			}
			float dropQuantityMultiplier = 1f;
			EnumHandling handling = EnumHandling.PassThrough;
			server.EventManager.TriggerBreakBlock(player, blockSelection, ref dropQuantityMultiplier, ref handling);
			if (handling == EnumHandling.PassThrough)
			{
				if (itemstack != null)
				{
					itemstack.Collectible.OnBlockBrokenWith(server, player.Entity, activeHotbarSlot, blockSelection, dropQuantityMultiplier);
				}
				else
				{
					block4.OnBlockBroken(server, blockPos, player, dropQuantityMultiplier);
				}
				if (server.WorldMap.GetChunk(blockSelection.Position) is ServerChunk serverChunk2)
				{
					serverChunk2.BlocksRemoved++;
					serverChunk2.DirtyForSaving = true;
				}
			}
			else
			{
				server.WorldMap.MarkBlockModified(blockSelection.Position);
				server.WorldMap.MarkBlockEntityDirty(blockSelection.Position);
			}
			if (activeHotbarSlot.Itemstack == null && itemstack != null)
			{
				server.BroadcastHotbarSlot(player);
			}
		}
		player.client.IsInventoryDirty = true;
		return true;
	}

	internal bool IsAnyPlayerInBlock(BlockPos pos, Block block, IPlayer ignorePlayer)
	{
		Cuboidf[] collisionBoxes = block.GetCollisionBoxes(server.BlockAccessor, pos);
		if (collisionBoxes == null)
		{
			return false;
		}
		IPlayer[] allOnlinePlayers = server.AllOnlinePlayers;
		foreach (IPlayer player in allOnlinePlayers)
		{
			if (player.Entity == null || player.ClientId == ignorePlayer?.ClientId)
			{
				continue;
			}
			for (int j = 0; j < collisionBoxes.Length; j++)
			{
				if (CollisionTester.AabbIntersect(collisionBoxes[j], pos.X, pos.Y, pos.Z, player.Entity.SelectionBox, player.Entity.Pos.XYZ))
				{
					return true;
				}
			}
		}
		return false;
	}

	public override int GetUpdateInterval()
	{
		return server.Config.BlockTickInterval;
	}

	private void UpdateEvery100ms(float t1)
	{
		HandleDirtyAndUpdatedBlocks();
		SendDirtyBlockEntities();
	}

	private void HandleDirtyAndUpdatedBlocks()
	{
		int num = 0;
		while (server.UpdatedBlocks.Count > 0 && num++ < 500)
		{
			BlockPos pos = server.UpdatedBlocks.Dequeue();
			server.TriggerNeighbourBlocksUpdate(pos);
		}
		Vec4i result;
		while (!server.DirtyBlocks.IsEmpty && server.DirtyBlocks.TryDequeue(out result))
		{
			server.SendSetBlock(server.BlockAccessor.GetBlockRaw(result.X, result.Y, result.Z).Id, result.X, result.Y, result.Z, result.W, exchangeOnly: true);
		}
		if (!server.ModifiedBlocks.IsEmpty)
		{
			List<BlockPos> list = new List<BlockPos>();
			BlockPos result2;
			while (!server.ModifiedBlocks.IsEmpty && server.ModifiedBlocks.TryDequeue(out result2))
			{
				Block block = server.WorldMap.RelaxedBlockAccess.GetBlock(result2, 2);
				if (block.Id != 0)
				{
					block.OnNeighbourBlockChange(server, result2, result2);
				}
				server.WorldMap.RelaxedBlockAccess.GetBlock(result2, 1).OnNeighbourBlockChange(server, result2, result2);
				list.Add(result2);
			}
			server.SendSetBlocksPacket(list, 47);
		}
		if (!server.ModifiedBlocksNoRelight.IsEmpty)
		{
			List<BlockPos> list2 = new List<BlockPos>();
			BlockPos result3;
			while (!server.ModifiedBlocksNoRelight.IsEmpty && server.ModifiedBlocksNoRelight.TryDequeue(out result3))
			{
				server.WorldMap.RelaxedBlockAccess.GetBlock(result3).OnNeighbourBlockChange(server, result3, result3);
				list2.Add(result3);
			}
			server.SendSetBlocksPacket(list2, 63);
		}
		if (server.ModifiedBlocksMinimal.Count > 0)
		{
			server.SendSetBlocksPacket(server.ModifiedBlocksMinimal, 70);
			server.ModifiedBlocksMinimal.Clear();
		}
		if (!server.ModifiedDecors.IsEmpty)
		{
			List<BlockPos> list3 = new List<BlockPos>();
			BlockPos result4;
			while (!server.ModifiedDecors.IsEmpty && server.ModifiedDecors.TryDequeue(out result4))
			{
				list3.Add(result4);
			}
			server.SendSetDecorsPackets(list3);
		}
	}

	private void SendDirtyBlockEntities()
	{
		if (server.DirtyBlockEntities.IsEmpty)
		{
			return;
		}
		blockEntitiesPacked.Clear();
		noblockEntities.Clear();
		positionsDone.Clear();
		ConcurrentQueue<BlockPos> dirtyBlockEntities = server.DirtyBlockEntities;
		if (!dirtyBlockEntities.IsEmpty)
		{
			using FastMemoryStream ms = reusableSendingStream ?? (reusableSendingStream = new FastMemoryStream());
			BlockPos result;
			while (!dirtyBlockEntities.IsEmpty && server.DirtyBlockEntities.TryDequeue(out result))
			{
				if (positionsDone.Add(result))
				{
					BlockEntity blockEntity = server.WorldMap.GetBlockEntity(result);
					if (blockEntity != null)
					{
						blockEntitiesPacked.Add(BlockEntityToPacket(blockEntity, ms));
					}
					else
					{
						noblockEntities.Add(result);
					}
				}
			}
		}
		if (blockEntitiesPacked.Count <= 0 && noblockEntities.Count <= 0)
		{
			return;
		}
		foreach (ConnectedClient value in server.Clients.Values)
		{
			if (value.State == EnumClientState.Offline)
			{
				continue;
			}
			playerBlockEntitiesPacked.Clear();
			foreach (Packet_BlockEntity item in blockEntitiesPacked)
			{
				long index3d = server.WorldMap.ChunkIndex3D(new ChunkPos(item.PosX / 32, item.PosY / 32, item.PosZ / 32));
				if (value.DidSendChunk(index3d))
				{
					playerBlockEntitiesPacked.Add(item);
				}
			}
			if (playerBlockEntitiesPacked.Count + noblockEntities.Count > 0)
			{
				SendBlockEntitiesPacket(value, playerBlockEntitiesPacked, noblockEntities);
			}
		}
	}

	public override void OnServerTick(float dt)
	{
		if (server.RunPhase != EnumServerRunPhase.RunGame)
		{
			return;
		}
		int num = 0;
		while (!queuedTicks.IsEmpty && num < server.Config.MaxMainThreadBlockTicks)
		{
			if (!queuedTicks.TryDequeue(out var result))
			{
				continue;
			}
			Block block = null;
			try
			{
				if (result is FluidBlockPos)
				{
					BlockPos pos = (BlockPos)result;
					block = server.api.World.BlockAccessor.GetBlock(pos, 2);
					block.OnServerGameTick(server.api.World, pos);
				}
				else if (result is BlockPos)
				{
					BlockPos pos2 = (BlockPos)result;
					block = server.api.World.BlockAccessor.GetBlock(pos2);
					block.OnServerGameTick(server.api.World, pos2);
				}
				else
				{
					BlockPosWithExtraObject blockPosWithExtraObject = (BlockPosWithExtraObject)result;
					block = server.api.World.BlockAccessor.GetBlock(blockPosWithExtraObject.pos);
					block.OnServerGameTick(server.api.World, blockPosWithExtraObject.pos, blockPosWithExtraObject.extra);
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Exception thrown in block.OnServerGameTick() for block code '{0}':", block?.Code);
				ServerMain.Logger.Error(e);
			}
			num++;
		}
	}

	public override void OnSeparateThreadTick()
	{
		if (server.RunPhase != EnumServerRunPhase.RunGame)
		{
			return;
		}
		chunksToBeTicked.Clear();
		int blockTickChunkRange = server.Config.BlockTickChunkRange;
		lock (clientIdsLock)
		{
			foreach (int clientId in clientIds)
			{
				if (!server.Clients.TryGetValue(clientId, out var value) || value.State != EnumClientState.Playing)
				{
					continue;
				}
				ChunkPos chunkPos = server.WorldMap.ChunkPosFromChunkIndex3D(value.Entityplayer.InChunkIndex3d);
				for (int i = -blockTickChunkRange; i <= blockTickChunkRange; i++)
				{
					for (int j = -blockTickChunkRange; j <= blockTickChunkRange; j++)
					{
						for (int k = -blockTickChunkRange; k <= blockTickChunkRange; k++)
						{
							int chunkX = chunkPos.X + i;
							int chunkY = chunkPos.Y + j;
							int chunkZ = chunkPos.Z + k;
							if (!server.WorldMap.IsValidChunkPos(chunkX, chunkY, chunkZ))
							{
								continue;
							}
							long num = server.WorldMap.ChunkIndex3D(chunkX, chunkY, chunkZ, chunkPos.Dimension);
							ServerChunk serverChunk = server.WorldMap.GetServerChunk(num);
							if (serverChunk != null)
							{
								chunksToBeTicked[num] = serverChunk;
								serverChunk.MarkFresh();
								if (serverChunk.MapChunk != null)
								{
									serverChunk.MapChunk.MarkFresh();
								}
							}
						}
					}
				}
			}
		}
		foreach (KeyValuePair<long, ServerChunk> item in chunksToBeTicked)
		{
			try
			{
				tickChunk(item.Key, item.Value);
			}
			catch (Exception e)
			{
				ServerMain.Logger.Warning("Exception thrown when trying to tick a chunk.");
				ServerMain.Logger.Warning(e);
			}
		}
	}

	private void tickChunk(long index3d, ServerChunk chunk)
	{
		ChunkPos chunkPos = server.WorldMap.ChunkPosFromChunkIndex3D(index3d);
		int num = 32 * chunkPos.X;
		int num2 = 32 * chunkPos.Y;
		int num3 = 32 * chunkPos.Z;
		tmpPos.SetDimension(chunkPos.Dimension);
		tmpLiquidPos.SetDimension(chunkPos.Dimension);
		chunk.Unpack();
		float num4 = (int)((float)server.Config.RandomBlockTicksPerChunk * server.Calendar.SpeedOfTime / 60f);
		int num5 = (int)num4 + ((server.rand.Value.NextDouble() < (double)(num4 - (float)(int)num4)) ? 1 : 0);
		for (int i = 0; i < num5; i++)
		{
			int num6 = rand.Next(32);
			int num7 = rand.Next(32);
			int num8 = rand.Next(32);
			int index3d2 = server.WorldMap.ChunkSizedIndex3D(num6, num8, num7);
			int fluid = chunk.Data.GetFluid(index3d2);
			if (fluid != 0)
			{
				tryTickBlock(server.WorldMap.Blocks[fluid], tmpLiquidPos.Set(num + num6, num2 + num8, num3 + num7));
				continue;
			}
			fluid = chunk.Data[index3d2];
			if (fluid != 0)
			{
				tryTickBlock(server.WorldMap.Blocks[fluid], tmpPos.Set(num + num6, num2 + num8, num3 + num7));
			}
		}
	}

	private bool tryTickBlock(Block block, BlockPos atPos)
	{
		if (!block.ShouldReceiveServerGameTicks(server.api.World, atPos, rand, out var extra))
		{
			return false;
		}
		if (extra == null)
		{
			queuedTicks.Enqueue(atPos.Copy());
		}
		else
		{
			queuedTicks.Enqueue(new BlockPosWithExtraObject(atPos.Copy(), extra));
		}
		return true;
	}

	private Packet_BlockEntity BlockEntityToPacket(BlockEntity blockEntity, FastMemoryStream ms)
	{
		ms.Reset();
		BinaryWriter stream = new BinaryWriter(ms);
		TreeAttribute treeAttribute = new TreeAttribute();
		blockEntity.ToTreeAttributes(treeAttribute);
		treeAttribute.ToBytes(stream);
		string text = ServerMain.ClassRegistry.blockEntityTypeToClassnameMapping[blockEntity.GetType()];
		byte[] array = ms.ToArray();
		Packet_BlockEntity packet_BlockEntity = new Packet_BlockEntity();
		packet_BlockEntity.Classname = text;
		packet_BlockEntity.PosX = blockEntity.Pos.X;
		packet_BlockEntity.PosY = blockEntity.Pos.InternalY;
		packet_BlockEntity.PosZ = blockEntity.Pos.Z;
		packet_BlockEntity.SetData(array);
		if (server.doNetBenchmark)
		{
			server.packetBenchmarkBlockEntitiesBytes.TryGetValue(text, out var value);
			server.packetBenchmarkBlockEntitiesBytes[text] = value + array.Length;
		}
		return packet_BlockEntity;
	}

	private void SendBlockEntitiesPacket(ConnectedClient client, List<Packet_BlockEntity> blockEntitiesPacked, List<BlockPos> noBlockEntities)
	{
		Packet_BlockEntity[] array = new Packet_BlockEntity[blockEntitiesPacked.Count + noBlockEntities.Count];
		int num = 0;
		foreach (Packet_BlockEntity item in blockEntitiesPacked)
		{
			array[num++] = item;
		}
		for (int i = 0; i < noBlockEntities.Count; i++)
		{
			BlockPos blockPos = noBlockEntities[i];
			array[num++] = new Packet_BlockEntity
			{
				Classname = null,
				Data = null,
				PosX = blockPos.X,
				PosY = blockPos.InternalY,
				PosZ = blockPos.Z
			};
		}
		Packet_BlockEntities packet_BlockEntities = new Packet_BlockEntities();
		packet_BlockEntities.SetBlockEntitites(array);
		server.SendPacket(client.Id, new Packet_Server
		{
			Id = 48,
			BlockEntities = packet_BlockEntities
		});
	}
}
