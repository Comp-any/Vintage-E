using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace Vintagestory.Server;

public class ServerSystemEntitySimulation : ServerSystem
{
	internal int trackingRangeSq = MagicNum.DefaultEntityTrackingRange * MagicNum.ServerChunkSize * MagicNum.DefaultEntityTrackingRange * MagicNum.ServerChunkSize;

	internal PhysicsManager physicsManager;

	private Dictionary<string, List<string>> deathMessagesCache;

	private int skippedDespawnTicks;

	public ServerSystemEntitySimulation(ServerMain server)
		: base(server)
	{
		server.RegisterGameTickListener(UpdateEvery1000ms, 1000);
		server.EventManager.OnGameWorldBeingSaved += EventManager_OnGameWorldBeingSaved;
		server.RegisterGameTickListener(UpdateEvery100ms, 100);
		server.clientAwarenessEvents[EnumClientAwarenessEvent.ChunkTransition].Add(OnPlayerLeaveChunk);
		server.PacketHandlers[17] = HandleEntityInteraction;
		server.PacketHandlers[12] = HandleSpecialKey;
		server.PacketHandlers[32] = HandleRuntimeSetting;
		server.EventManager.OnPlayerChat += EventManager_OnPlayerChat;
	}

	private void HandleRuntimeSetting(Packet_Client packet, ConnectedClient player)
	{
		player.Player.ImmersiveFpMode = packet.RuntimeSetting.ImmersiveFpMode > 0;
		player.Player.ItemCollectMode = packet.RuntimeSetting.ItemCollectMode;
	}

	private void EventManager_OnPlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
	{
		if (byPlayer.HasPrivilege("staffentitlement"))
		{
			double[] color = GlobalConstants.playerColorByEntitlement["staff"];
			message = string.Format("<font color=\"" + VtmlUtil.toHexColor(color) + "\"><strong>" + Lang.Get("serverstaffchatprefix") + " {0}:</strong></font> {1}", byPlayer.PlayerName, message);
			return;
		}
		if (byPlayer.Entitlements.Count > 0)
		{
			Entitlement entitlement = byPlayer.Entitlements[0];
			if (GlobalConstants.playerColorByEntitlement.TryGetValue(entitlement.Code, out var value))
			{
				message = string.Format("<font color=\"" + VtmlUtil.toHexColor(value) + "\"><strong>{0}:</strong></font> {1}", byPlayer.PlayerName, message);
				return;
			}
		}
		message = $"<strong>{byPlayer.PlayerName}:</strong> {message}";
	}

	private void EventManager_OnGameWorldBeingSaved()
	{
		if (server.RunPhase != EnumServerRunPhase.Shutdown)
		{
			server.EventManager.defragLists();
		}
	}

	public override int GetUpdateInterval()
	{
		return 20;
	}

	public override void OnBeginModsAndConfigReady()
	{
		server.EventManager.OnPlayerRespawn += OnPlayerRespawn;
		new ShapeTesselatorManager(server).LoadEntityShapes(server.EntityTypes, server.api);
	}

	public override void OnPlayerJoin(ServerPlayer player)
	{
		physicsManager.ForceClientUpdateTick(player.client);
	}

	public override void OnPlayerDisconnect(ServerPlayer player)
	{
		physicsManager.ForceClientUpdateTick(player.client);
	}

	private void OnPlayerLeaveChunk(ClientStatistics clientStats)
	{
		physicsManager.ForceClientUpdateTick(clientStats.client);
	}

	public override void OnServerTick(float dt)
	{
		foreach (ConnectedClient value in server.Clients.Values)
		{
			ConnectedClient client = value;
			ServerPlayer player;
			if (client.IsPlayingClient)
			{
				player = client.Player;
				player.Entity.PreviousBlockSelection = player.Entity.BlockSelection?.Position.Copy();
				server.RayTraceForSelection(player, ref player.Entity.BlockSelection, ref player.Entity.EntitySelection, bFilter, eFilter);
				if (player.Entity.BlockSelection != null)
				{
					bool firstTick = player.Entity.PreviousBlockSelection == null || player.Entity.BlockSelection.Position != player.Entity.PreviousBlockSelection;
					server.BlockAccessor.GetBlock(player.Entity.BlockSelection.Position).OnBeingLookedAt(player, player.Entity.BlockSelection, firstTick);
				}
			}
			bool bFilter(BlockPos pos, Block block)
			{
				if (block != null && block.RenderPass == EnumChunkRenderPass.Meta)
				{
					return client.WorldData.RenderMetaBlocks;
				}
				return true;
			}
			bool eFilter(Entity e)
			{
				if (e.IsInteractable)
				{
					return e.EntityId != player.Entity.EntityId;
				}
				return false;
			}
		}
		TickEntities(dt);
		SendPlayerEntityDeaths();
	}

	private void UpdateEvery100ms(float t1)
	{
		SendEntityDespawns();
		int count = server.DelayedSpawnQueue.Count;
		if (count <= 0)
		{
			return;
		}
		ServerMain.FrameProfiler.Enter("spawningentities");
		int num = MagicNum.MaxEntitySpawnsPerTick;
		if (count > num * 3)
		{
			num = count / 2;
		}
		count = Math.Min(count, num);
		Entity result;
		while (count-- > 0 && server.DelayedSpawnQueue.TryDequeue(out result))
		{
			try
			{
				server.SpawnEntity(result);
				ServerMain.FrameProfiler.Mark("spawning:", result.Code);
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error(e);
			}
		}
		ServerMain.FrameProfiler.Leave();
	}

	private void UpdateEvery1000ms(float dt)
	{
		foreach (Entity value in server.LoadedEntities.Values)
		{
			long num = server.WorldMap.ChunkIndex3D(value.Pos);
			if (value.InChunkIndex3d != num)
			{
				ServerChunk serverChunk = server.WorldMap.GetServerChunk(value.InChunkIndex3d);
				ServerChunk serverChunk2 = server.WorldMap.GetServerChunk(num);
				if (serverChunk2 != null)
				{
					serverChunk?.RemoveEntity(value.EntityId);
					serverChunk2.AddEntity(value);
					value.InChunkIndex3d = num;
				}
			}
		}
	}

	private void TickEntities(float dt)
	{
		List<KeyValuePair<Entity, EntityDespawnData>> list = new List<KeyValuePair<Entity, EntityDespawnData>>();
		ServerMain.FrameProfiler.Enter("tickentities");
		foreach (Entity value in server.LoadedEntities.Values)
		{
			if (!Dimensions.ShouldNotTick(value.Pos, value.Api))
			{
				try
				{
					value.OnGameTick(dt);
				}
				catch (Exception e)
				{
					if (value is EntityPlayer entityPlayer)
					{
						ServerMain.Logger.Warning("Exception while ticking player {0} ({1}) at {2}: ", entityPlayer.GetName(), entityPlayer.PlayerUID, entityPlayer.Pos);
						ServerMain.Logger.Error(e);
					}
					else
					{
						ServerMain.Logger.Warning("Exception while ticking entity {0} ({1}) at {2}: ", value.GetName(), value.Properties?.Class, value.Pos);
						ServerMain.Logger.Error(e);
					}
				}
			}
			if (value.ShouldDespawn)
			{
				list.Add(new KeyValuePair<Entity, EntityDespawnData>(value, value.DespawnReason));
			}
		}
		ServerMain.FrameProfiler.Enter("despawning");
		foreach (KeyValuePair<Entity, EntityDespawnData> item in list)
		{
			server.DespawnEntity(item.Key, item.Value);
			ServerMain.FrameProfiler.Mark("despawned-", item.Key.Code.Path);
		}
		ServerMain.FrameProfiler.Leave();
		ServerMain.FrameProfiler.Leave();
	}

	private void HandleEntityInteraction(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator)
		{
			return;
		}
		Packet_EntityInteraction p = packet.EntityInteraction;
		Entity[] entitiesAround = server.GetEntitiesAround(player.Entity.Pos.XYZ, player.WorldData.PickingRange + 10f, player.WorldData.PickingRange + 10f, (Entity e) => e.EntityId == p.EntityId);
		if (entitiesAround == null || entitiesAround.Length == 0)
		{
			ServerMain.Logger.Debug("HandleEntityInteraction received from client " + client.PlayerName + " but no such entity found in his range!");
			return;
		}
		Entity entity = entitiesAround[0];
		Cuboidd cuboidd = entity.SelectionBox.ToDouble().Translate(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
		EntityPos pos = client.Entityplayer.Pos;
		ItemStack itemStack = client.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
		float num = itemStack?.Collectible.GetAttackRange(itemStack) ?? GlobalConstants.DefaultAttackRange;
		double x = pos.X + client.Entityplayer.LocalEyePos.X;
		double y = pos.Y + client.Entityplayer.LocalEyePos.Y;
		double z = pos.Z + client.Entityplayer.LocalEyePos.Z;
		bool flag = p.MouseButton == 0;
		if (((cuboidd.ShortestDistanceFrom(x, y, z) > (double)(num * 2f)) & flag) || (flag && (((!server.Config.AllowPvP || !player.HasPrivilege("attackplayers")) && entity is EntityPlayer) || (!player.HasPrivilege("attackcreatures") && entity is EntityAgent))) || (entity is EntityPlayer entityPlayer && !(entityPlayer.Player is IServerPlayer { ConnectionState: EnumClientState.Playing })))
		{
			return;
		}
		Vec3d vec3d = new Vec3d(CollectibleNet.DeserializeDouble(p.HitX), CollectibleNet.DeserializeDouble(p.HitY), CollectibleNet.DeserializeDouble(p.HitZ));
		if (server.Config.AntiAbuse >= EnumProtectionLevel.Basic && !player.IsInInteractionRangeOf(entity))
		{
			ServerMain.Logger.Audit("{0} tried to interact with an entity out of range.", player.PlayerName);
			return;
		}
		if (p.EntityId != player.CurrentEntitySelection?.Entity?.EntityId)
		{
			player.Entity.EntitySelection = new EntitySelection
			{
				Entity = entity,
				SelectionBoxIndex = p.SelectionBoxIndex,
				Position = vec3d
			};
		}
		else
		{
			player.CurrentEntitySelection.Position = vec3d;
			player.CurrentEntitySelection.SelectionBoxIndex = p.SelectionBoxIndex;
		}
		EnumHandling handling = EnumHandling.PassThrough;
		server.EventManager.TriggerPlayerInteractEntity(entity, player, player.inventoryMgr.ActiveHotbarSlot, vec3d, p.MouseButton, ref handling);
		if (handling == EnumHandling.PassThrough)
		{
			entity.OnInteract(player.Entity, player.InventoryManager.ActiveHotbarSlot, vec3d, (p.MouseButton != 0) ? EnumInteractMode.Interact : EnumInteractMode.Attack);
		}
	}

	private void HandleSpecialKey(Packet_Client packet, ConnectedClient client)
	{
		int num = server.SaveGameData?.WorldConfiguration.GetString("playerlives", "-1").ToInt(-1) ?? (-1);
		if (num < 0 || num > client.WorldData.Deaths)
		{
			ServerMain.Logger.VerboseDebug("Received respawn request from {0}", client.PlayerName);
			server.EventManager.TriggerPlayerRespawn(client.Player);
		}
		else
		{
			client.Player.SendMessage(GlobalConstants.CurrentChatGroup, "Cannot revive! All lives used up.", EnumChatType.CommandError);
		}
	}

	private void OnPlayerRespawn(IServerPlayer player)
	{
		if (player.Entity == null || player.Entity.Alive)
		{
			ServerMain.Logger.VerboseDebug("Respawn key received but ignored. Cause: {0} || {1}", player.Entity == null, player.Entity.Alive);
			return;
		}
		FuzzyEntityPos pos = player.GetSpawnPosition(consumeSpawnUse: true);
		ConnectedClient client = server.Clients[player.ClientId];
		if (pos.UsesLeft >= 0)
		{
			if (pos.UsesLeft == 99)
			{
				player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "playerrespawn-nocustomspawnset");
			}
			else if (pos.UsesLeft > 0)
			{
				player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "You have re-emerged at your returning point. It will vanish after {0} more uses", pos.UsesLeft);
			}
			else if (pos.UsesLeft == 0)
			{
				player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "You have re-emerged at your returning point, which has now vanished.");
			}
		}
		if (pos.Radius > 0f)
		{
			server.LocateRandomPosition(pos.XYZ, pos.Radius, 10, (BlockPos spawnpos) => ServerSystemSupplyChunks.AdjustForSaveSpawnSpot(server, spawnpos, player, server.rand.Value), (BlockPos foundpos) =>
			{
				if (foundpos != null)
				{
					EntityPos entityPos = pos.Copy();
					entityPos.X = (double)foundpos.X + 0.5;
					entityPos.Y = foundpos.Y;
					entityPos.Z = (double)foundpos.Z + 0.5;
					teleport(client, entityPos);
				}
				else
				{
					teleport(client, pos);
				}
			});
		}
		else
		{
			teleport(client, pos);
		}
		ServerMain.Logger.VerboseDebug("Respawn key received. Teleporting player to spawn and reviving once chunks have loaded.");
	}

	private void teleport(ConnectedClient client, EntityPos targetPos)
	{
		EntityPlayer eplr = client.Player.Entity;
		eplr.TeleportTo(targetPos, () =>
		{
			eplr.Revive();
			server.ServerUdpNetwork.physicsManager.UpdateTrackedEntitiesStates(client);
		});
	}

	private void GenDeathMessagesCache()
	{
		deathMessagesCache = new Dictionary<string, List<string>>();
		foreach (KeyValuePair<string, string> allEntry in Lang.AvailableLanguages["en"].GetAllEntries())
		{
			AssetLocation assetLocation = new AssetLocation(allEntry.Key);
			if (assetLocation.PathStartsWith("deathmsg"))
			{
				List<string> list = new List<string>(assetLocation.Path.Split('-'));
				list.RemoveAt(list.Count - 1);
				string key = string.Join("-", list);
				List<string> list2;
				if (deathMessagesCache.ContainsKey(key))
				{
					list2 = deathMessagesCache[key];
				}
				else
				{
					list2 = new List<string>();
					deathMessagesCache[key] = list2;
				}
				list2.Add(assetLocation.Path);
			}
		}
	}

	private void SendPlayerEntityDeaths()
	{
		if (deathMessagesCache == null)
		{
			GenDeathMessagesCache();
		}
		List<ConnectedClient> list = server.Clients.Values.Where((ConnectedClient client) => client.State.ConnectedOrPlaying() && !client.Entityplayer.Alive && client.Entityplayer.DeadNotify).ToList();
		if (list.Count == 0)
		{
			return;
		}
		int num = server.SaveGameData?.WorldConfiguration.GetString("playerlives", "-1").ToInt(-1) ?? (-1);
		foreach (ConnectedClient item in list)
		{
			item.Entityplayer.DeadNotify = false;
			item.WorldData.Deaths++;
			server.EventManager.TriggerPlayerDeath(item.Player, item.Entityplayer.DeathReason);
			server.BroadcastPacket(new Packet_Server
			{
				Id = 45,
				PlayerDeath = new Packet_PlayerDeath
				{
					ClientId = item.Id,
					LivesLeft = ((num < 0) ? (-1) : Math.Max(0, num - item.WorldData.Deaths))
				}
			});
			DamageSource deathReason = item.Entityplayer.DeathReason;
			bool flag = !server.api.World.Config.GetBool("disableDeathMessages");
			string text = "";
			if (flag)
			{
				text = GetDeathMessage(item, deathReason);
				server.joinLeaveDeathMessage(text, EnumChatType.Notification, item.Player);
			}
			if (deathReason?.GetCauseEntity() is EntityPlayer entityPlayer)
			{
				string playerName = server.PlayerByUid(entityPlayer.PlayerUID).PlayerName;
				ServerMain.Logger.Audit(Lang.Get("{0} killed {1}, with item (if any): {2}", playerName, item.PlayerName, entityPlayer.RightHandItemSlot?.Itemstack?.Collectible.Code));
			}
			else
			{
				ServerMain.Logger.Audit(Lang.Get("{0} died. Death message: {1}", item.PlayerName, text));
			}
		}
	}

	private string GetDeathMessage(ConnectedClient client, DamageSource src)
	{
		if (src == null)
		{
			return Lang.Get("Player {0} died.", client.PlayerName);
		}
		Entity causeEntity = src.GetCauseEntity();
		if (causeEntity != null)
		{
			string key = "deathmsg-" + causeEntity.Code.Path.Replace("-", "");
			deathMessagesCache.TryGetValue(key, out var value);
			if (value != null && value.Count > 0)
			{
				return Lang.Get(value[server.rand.Value.Next(value.Count)], client.PlayerName);
			}
			string prefixAndCreatureName = causeEntity.GetPrefixAndCreatureName();
			return Lang.Get("Player {0} got killed by {1}", client.PlayerName, prefixAndCreatureName);
		}
		string text = null;
		if (src.Source == EnumDamageSource.Explosion)
		{
			text = "deathmsg-explosion";
		}
		else if (src.Type == EnumDamageType.Hunger)
		{
			text = "deathmsg-hunger";
		}
		else if (src.Type == EnumDamageType.Fire)
		{
			text = "deathmsg-fire-block";
		}
		else if (src.Type == EnumDamageType.Electricity)
		{
			text = "deathmsg-electricity-block";
		}
		else if (src.Source == EnumDamageSource.Fall)
		{
			text = "deathmsg-fall";
		}
		if (text != null)
		{
			deathMessagesCache.TryGetValue(text, out var value2);
			if (value2 != null && value2.Count > 0)
			{
				int index = server.rand.Value.Next(value2.Count);
				return Lang.Get(value2[index], client.PlayerName);
			}
		}
		return Lang.Get("Player {0} died.", client.PlayerName);
	}

	private void SendEntityDespawns()
	{
		if (server.EntityDespawnSendQueue.Count == 0)
		{
			return;
		}
		if (ServerMain.spawnSendQueue > 0 && skippedDespawnTicks < 15)
		{
			skippedDespawnTicks++;
			return;
		}
		skippedDespawnTicks = 0;
		Packet_EntityDespawn packet_EntityDespawn = new Packet_EntityDespawn();
		Packet_Server packet = new Packet_Server
		{
			Id = 36,
			EntityDespawn = packet_EntityDespawn
		};
		List<long> list = new List<long>();
		List<int> list2 = new List<int>();
		List<int> list3 = new List<int>();
		foreach (ConnectedClient value in server.Clients.Values)
		{
			list.Clear();
			list2.Clear();
			list3.Clear();
			foreach (KeyValuePair<Entity, EntityDespawnData> item in server.EntityDespawnSendQueue)
			{
				if (value.TrackedEntities.Contains(item.Key.EntityId))
				{
					list.Add(item.Key.EntityId);
					list2.Add((int)(item.Value?.Reason ?? EnumDespawnReason.Death));
					list3.Add((int)((item.Value?.DamageSourceForDeath == null) ? EnumDamageSource.Unknown : item.Value.DamageSourceForDeath.Source));
				}
			}
			packet_EntityDespawn.SetEntityId(list.ToArray());
			packet_EntityDespawn.SetDeathDamageSource(list3.ToArray());
			packet_EntityDespawn.SetDespawnReason(list2.ToArray());
			server.SendPacket(value.Id, packet);
		}
		server.EntityDespawnSendQueue.Clear();
	}
}
