using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Vintagestory.Server;

internal class ServerEventAPI : ServerAPIComponentBase, IServerEventAPI, IEventAPI
{
	public event TestBlockAccessDelegate OnTestBlockAccess;

	public event TestBlockAccessClaimDelegate OnTestBlockAccessClaim;

	public event ChunkDirtyDelegate ChunkDirty;

	public event ChunkColumnBeginLoadChunkThread BeginChunkColumnLoadChunkThread;

	public event ChunkColumnLoadedDelegate ChunkColumnLoaded;

	public event ChunkColumnUnloadDelegate ChunkColumnUnloaded;

	public event MapRegionLoadedDelegate MapRegionLoaded;

	public event MapRegionUnloadDelegate MapRegionUnloaded;

	public event PreventTerrainHeightSmoothingDelegate TerrainHeightSmoothingPrevention;

	public event EntityDeathDelegate OnEntityDeath;

	public event EntityMountDelegate EntityMounted;

	public event EntityMountDelegate EntityUnmounted;

	public event MountGaitReceivedDelegate MountGaitReceived;

	public event TrySpawnEntityDelegate OnTrySpawnEntity
	{
		add
		{
			server.ModEventManager.OnTrySpawnEntity += value;
		}
		remove
		{
			server.ModEventManager.OnTrySpawnEntity -= value;
		}
	}

	public event TrySpawnGroupDelegate OnTrySpawnGroupNearOffthread
	{
		add
		{
			server.ModEventManager.OnTrySpawnGroupNearOffthread += value;
		}
		remove
		{
			server.ModEventManager.OnTrySpawnGroupNearOffthread -= value;
		}
	}

	public event OnInteractDelegate OnPlayerInteractEntity
	{
		add
		{
			server.ModEventManager.OnPlayerInteractEntity += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerInteractEntity -= value;
		}
	}

	public event EntityDelegate OnEntitySpawn
	{
		add
		{
			server.ModEventManager.OnEntitySpawn += value;
		}
		remove
		{
			server.ModEventManager.OnEntitySpawn -= value;
		}
	}

	public event EntityDelegate OnEntityLoaded
	{
		add
		{
			server.ModEventManager.OnEntityLoaded += value;
		}
		remove
		{
			server.ModEventManager.OnEntityLoaded -= value;
		}
	}

	public event EntityDespawnDelegate OnEntityDespawn
	{
		add
		{
			server.ModEventManager.OnEntityDespawn += value;
		}
		remove
		{
			server.ModEventManager.OnEntityDespawn -= value;
		}
	}

	public event PlayerDelegate PlayerCreate
	{
		add
		{
			server.ModEventManager.OnPlayerCreate += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerCreate -= value;
		}
	}

	public event PlayerDelegate PlayerRespawn
	{
		add
		{
			server.ModEventManager.OnPlayerRespawn += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerRespawn -= value;
		}
	}

	public event PlayerDelegate PlayerJoin
	{
		add
		{
			server.ModEventManager.OnPlayerJoin += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerJoin -= value;
		}
	}

	public event PlayerDelegate PlayerNowPlaying
	{
		add
		{
			server.ModEventManager.OnPlayerNowPlaying += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerNowPlaying -= value;
		}
	}

	public event PlayerDelegate PlayerReady
	{
		add
		{
			server.ModEventManager.OnPlayerReady += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerReady -= value;
		}
	}

	public event PlayerDelegate PlayerLeave
	{
		add
		{
			server.ModEventManager.OnPlayerLeave += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerLeave -= value;
		}
	}

	public event PlayerDelegate PlayerDisconnect
	{
		add
		{
			server.ModEventManager.OnPlayerDisconnect += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerDisconnect -= value;
		}
	}

	public event PlayerChatDelegate PlayerChat
	{
		add
		{
			server.ModEventManager.OnPlayerChat += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerChat -= value;
		}
	}

	public event PlayerDeathDelegate PlayerDeath
	{
		add
		{
			server.ModEventManager.OnPlayerDeath += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerDeath -= value;
		}
	}

	public event PlayerDelegate PlayerSwitchGameMode
	{
		add
		{
			server.ModEventManager.OnPlayerChangeGamemode += value;
		}
		remove
		{
			server.ModEventManager.OnPlayerChangeGamemode -= value;
		}
	}

	public event Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling> BeforeActiveSlotChanged
	{
		add
		{
			server.ModEventManager.BeforeActiveSlotChanged += value;
		}
		remove
		{
			server.ModEventManager.BeforeActiveSlotChanged -= value;
		}
	}

	public event Action<IServerPlayer, ActiveSlotChangeEventArgs> AfterActiveSlotChanged
	{
		add
		{
			server.ModEventManager.AfterActiveSlotChanged += value;
		}
		remove
		{
			server.ModEventManager.AfterActiveSlotChanged -= value;
		}
	}

	public event BlockPlacedDelegate DidPlaceBlock
	{
		add
		{
			server.ModEventManager.DidPlaceBlock += value;
		}
		remove
		{
			server.ModEventManager.DidPlaceBlock -= value;
		}
	}

	public event BlockBrokenDelegate DidBreakBlock
	{
		add
		{
			server.ModEventManager.DidBreakBlock += value;
		}
		remove
		{
			server.ModEventManager.DidBreakBlock -= value;
		}
	}

	public event BlockBreakDelegate BreakBlock
	{
		add
		{
			server.ModEventManager.BreakBlock += value;
		}
		remove
		{
			server.ModEventManager.BreakBlock -= value;
		}
	}

	public event BlockUsedDelegate DidUseBlock
	{
		add
		{
			server.ModEventManager.DidUseBlock += value;
		}
		remove
		{
			server.ModEventManager.DidUseBlock -= value;
		}
	}

	public event CanUseDelegate CanUseBlock
	{
		add
		{
			server.ModEventManager.CanUseBlock += value;
		}
		remove
		{
			server.ModEventManager.CanUseBlock -= value;
		}
	}

	public event CanPlaceOrBreakDelegate CanPlaceOrBreakBlock
	{
		add
		{
			server.ModEventManager.CanPlaceOrBreakBlock += value;
		}
		remove
		{
			server.ModEventManager.CanPlaceOrBreakBlock -= value;
		}
	}

	public event MatchGridRecipeDelegate MatchesGridRecipe;

	public event MatchRecipeDelegate MatchesRecipe;

	public event SuspendServerDelegate ServerSuspend;

	public event ResumeServerDelegate ServerResume;

	public event PlayerCommonDelegate PlayerDimensionChanged;

	public event HandInteractDelegate HandInteract
	{
		add
		{
			server.ModEventManager.HandInteract += value;
		}
		remove
		{
			server.ModEventManager.HandInteract -= value;
		}
	}

	public event OnGetClimateDelegate OnGetClimate
	{
		add
		{
			server.ModEventManager.OnGetClimate += value;
		}
		remove
		{
			server.ModEventManager.OnGetClimate -= value;
		}
	}

	public event OnGetWindSpeedDelegate OnGetWindSpeed
	{
		add
		{
			server.ModEventManager.OnGetWindSpeed += value;
		}
		remove
		{
			server.ModEventManager.OnGetWindSpeed -= value;
		}
	}

	event Action IServerEventAPI.SaveGameLoaded
	{
		add
		{
			server.ModEventManager.OnSaveGameLoaded += value;
		}
		remove
		{
			server.ModEventManager.OnSaveGameLoaded -= value;
		}
	}

	event Action IServerEventAPI.SaveGameCreated
	{
		add
		{
			server.ModEventManager.OnSaveGameCreated += value;
		}
		remove
		{
			server.ModEventManager.OnSaveGameCreated -= value;
		}
	}

	event Action IServerEventAPI.WorldgenStartup
	{
		add
		{
			server.ModEventManager.OnWorldgenStartup += value;
		}
		remove
		{
			server.ModEventManager.OnWorldgenStartup -= value;
		}
	}

	event Action IServerEventAPI.PhysicsThreadStart
	{
		add
		{
			server.EventManager.OnStartPhysicsThread += value;
		}
		remove
		{
			server.EventManager.OnStartPhysicsThread -= value;
		}
	}

	event Action IServerEventAPI.AssetsFinalizers
	{
		add
		{
			server.ModEventManager.AssetsFinalizer += value;
		}
		remove
		{
			server.ModEventManager.AssetsFinalizer -= value;
		}
	}

	event Action IServerEventAPI.GameWorldSave
	{
		add
		{
			server.ModEventManager.OnGameWorldBeingSaved += value;
		}
		remove
		{
			server.ModEventManager.OnGameWorldBeingSaved -= value;
		}
	}

	public bool TriggerTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d position, long herdId)
	{
		return server.ModEventManager.TriggerTrySpawnEntity(blockAccessor, ref properties, position, herdId);
	}

	public ServerEventAPI(ServerMain server)
		: base(server)
	{
	}

	public void Trigger<T>(Delegate[] delegates, string eventName, Action<T> onDele, Action onException = null) where T : Delegate
	{
		if (delegates == null)
		{
			return;
		}
		for (int i = 0; i < delegates.Length; i++)
		{
			T obj = (T)delegates[i];
			try
			{
				onDele(obj);
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Mod exception during event " + eventName + ". Will skip to next event");
				ServerMain.Logger.Error(e);
				onException?.Invoke();
			}
		}
	}

	public IWorldGenHandler GetRegisteredWorldGenHandlers(string worldType)
	{
		server.ModEventManager.WorldgenHandlers.TryGetValue(worldType, out var value);
		return value;
	}

	public bool CanSuspendServer()
	{
		bool canSuspend = true;
		if (ServerSuspend == null)
		{
			return true;
		}
		Trigger(ServerSuspend?.GetInvocationList(), "ServerSuspend", (SuspendServerDelegate dele) =>
		{
			if (dele() == EnumSuspendState.Wait)
			{
				canSuspend = false;
			}
		});
		return canSuspend;
	}

	public void ResumeServer()
	{
		Trigger(ServerResume?.GetInvocationList(), "ServerResume", (ResumeServerDelegate dele) =>
		{
			dele?.Invoke();
		});
	}

	internal void OnServerStage(EnumServerRunPhase runPhase)
	{
		foreach (Action item in server.ModEventManager.serverRunPhaseDelegates[runPhase])
		{
			item();
		}
	}

	public EnumWorldAccessResponse TriggerTestBlockAccess(IPlayer player, BlockSelection blockSel, EnumBlockAccessFlags accessType, ref string claimant, LandClaim claim, EnumWorldAccessResponse response)
	{
		if (OnTestBlockAccess != null)
		{
			Delegate[] invocationList = OnTestBlockAccess.GetInvocationList();
			for (int i = 0; i < invocationList.Length; i++)
			{
				TestBlockAccessDelegate testBlockAccessDelegate = (TestBlockAccessDelegate)invocationList[i];
				try
				{
					response = testBlockAccessDelegate(player, blockSel, accessType, ref claimant, response);
				}
				catch (Exception e)
				{
					ServerMain.Logger.Error("Mod exception during event OnTestBlockAccess. Will skip to next event");
					ServerMain.Logger.Error(e);
				}
			}
		}
		if (OnTestBlockAccessClaim != null)
		{
			Delegate[] invocationList = OnTestBlockAccessClaim.GetInvocationList();
			for (int i = 0; i < invocationList.Length; i++)
			{
				TestBlockAccessClaimDelegate testBlockAccessClaimDelegate = (TestBlockAccessClaimDelegate)invocationList[i];
				try
				{
					response = testBlockAccessClaimDelegate(player, blockSel, accessType, ref claimant, claim, response);
				}
				catch (Exception e2)
				{
					ServerMain.Logger.Error("Mod exception during event OnTestBlockAccessClaim. Will skip to next event");
					ServerMain.Logger.Error(e2);
				}
			}
		}
		return response;
	}

	public void TriggerChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
	{
		Trigger(ChunkDirty?.GetInvocationList(), "ChunkDirty", (ChunkDirtyDelegate dele) =>
		{
			dele?.Invoke(chunkCoord, chunk, reason);
		});
	}

	public void TriggerBeginChunkColumnLoadChunkThread(IServerMapChunk mapChunk, int chunkX, int chunkZ, IWorldChunk[] chunks)
	{
		Trigger(BeginChunkColumnLoadChunkThread?.GetInvocationList(), "BeginChunkColumnLoadChunkThread", (ChunkColumnBeginLoadChunkThread dele) =>
		{
			dele?.Invoke(mapChunk, chunkX, chunkZ, chunks);
		});
	}

	public void TriggerChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
	{
		Trigger(ChunkColumnLoaded?.GetInvocationList(), "ChunkColumnLoaded", (ChunkColumnLoadedDelegate dele) =>
		{
			dele?.Invoke(chunkCoord, chunks);
		});
	}

	public void TriggerChunkColumnUnloaded(Vec3i chunkCoord)
	{
		Trigger(ChunkColumnUnloaded?.GetInvocationList(), "ChunkColumnUnloaded", (ChunkColumnUnloadDelegate dele) =>
		{
			dele?.Invoke(chunkCoord);
		});
	}

	public void TriggerMapRegionLoaded(Vec2i mapCoord, IMapRegion region)
	{
		Trigger(MapRegionLoaded?.GetInvocationList(), "MapRegionLoaded", (MapRegionLoadedDelegate dele) =>
		{
			dele?.Invoke(mapCoord, region);
		});
	}

	public void TriggerMapRegionUnloaded(Vec2i mapCoord, IMapRegion region)
	{
		Trigger(MapRegionUnloaded?.GetInvocationList(), "MapRegionUnloaded", (MapRegionUnloadDelegate dele) =>
		{
			dele?.Invoke(mapCoord, region);
		});
	}

	public void Timer(Action a, double interval)
	{
		server.Timers[new Timer
		{
			Interval = interval
		}] = () =>
		{
			a();
		};
	}

	public void GetWorldgenBlockAccessor(WorldGenThreadDelegate f)
	{
		server.ModEventManager.WorldgenBlockAccessor.Add(f);
	}

	public void MapRegionGeneration(MapRegionGeneratorDelegate handler, string worldType)
	{
		server.ModEventManager.GetOrCreateWorldGenHandler(worldType).OnMapRegionGen.Add(handler);
	}

	public void MapRegionNeighborsLoaded(MapRegionGeneratorDelegate handler, string worldType)
	{
		server.ModEventManager.GetOrCreateWorldGenHandler(worldType).OnMapRegionNeighborsLoaded.Add(handler);
	}

	public void MapChunkGeneration(MapChunkGeneratorDelegate handler, string worldType)
	{
		server.ModEventManager.GetOrCreateWorldGenHandler(worldType).OnMapChunkGen.Add(handler);
	}

	public void ChunkColumnGeneration(ChunkColumnGenerationDelegate handler, EnumWorldGenPass pass, string worldType)
	{
		server.ModEventManager.GetWorldGenHandler(worldType).OnChunkColumnGen[(int)pass].Add(handler);
	}

	public void PreventTerrainHeightSmoothing(PreventTerrainHeightSmoothingDelegate handler)
	{
		TerrainHeightSmoothingPrevention += handler;
	}

	public void IsTerrainHeightSmoothingPrevented(int chunkX, int chunkZ, BoolRef result)
	{
		Trigger(TerrainHeightSmoothingPrevention?.GetInvocationList(), "TerrainHeightSmoothingPrevention", (PreventTerrainHeightSmoothingDelegate dele) =>
		{
			dele?.Invoke(chunkX, chunkZ, result);
		});
	}

	public void InitWorldGenerator(Action handler, string worldType)
	{
		server.ModEventManager.GetOrCreateWorldGenHandler(worldType).OnInitWorldGen.Add(handler);
	}

	public object TriggerInitWorldGen()
	{
		server.ModEventManager.WorldgenHandlers.TryGetValue(server.SaveGameData.WorldType, out var value);
		if (value == null)
		{
			server.api.Logger.Error("This save game requires world generator " + server.SaveGameData.WorldType + " but no such generator was found! No terrain will generate!");
			value = new WorldGenHandler();
		}
		foreach (Action item in value.OnInitWorldGen)
		{
			try
			{
				server.api.Logger.VerboseDebug("Init worldgen for " + item.Target.GetType().Name);
				item();
			}
			catch (Exception e)
			{
				server.api.Logger.Error("Error during Init worldgen for " + item.Target.GetType().FullName);
				server.api.Logger.Error(e);
			}
		}
		server.api.Logger.VerboseDebug("Done all worldgens");
		return value;
	}

	public void WorldgenHook(WorldGenHookDelegate handler, string worldType, string hook)
	{
		server.ModEventManager.GetOrCreateWorldGenHandler(worldType).SpecialHooks[hook] = handler;
	}

	public void TriggerWorldgenHook(string hook, IBlockAccessor blockAccessor, BlockPos pos, string param)
	{
		server.ModEventManager.WorldgenHandlers.TryGetValue(server.SaveGameData.WorldType, out var value);
		if (value == null)
		{
			return;
		}
		try
		{
			if (value.SpecialHooks.TryGetValue(hook, out var value2))
			{
				value2(blockAccessor, pos, param);
			}
		}
		catch (Exception e)
		{
			ServerMain.Logger.Error("Mod exception during event TriggerWorldgenHook.");
			ServerMain.Logger.Error(e);
		}
	}

	public void ServerRunPhase(EnumServerRunPhase runPhase, Action action)
	{
		server.ModEventManager.serverRunPhaseDelegates[runPhase].Add(action);
	}

	public void PushEvent(string eventName, IAttribute data = null)
	{
		EnumHandling handling = EnumHandling.PassThrough;
		for (int i = 0; i < server.ModEventManager.EventBusListeners.Count; i++)
		{
			EventBusListener eventBusListener = server.ModEventManager.EventBusListeners[i];
			if (eventBusListener.filterByName == null || eventBusListener.filterByName.Equals(eventName))
			{
				eventBusListener.handler(eventName, ref handling, data);
			}
			if (handling == EnumHandling.PreventSubsequent)
			{
				break;
			}
		}
	}

	public void RegisterEventBusListener(EventBusListenerDelegate OnEvent, double priority = 0.5, string filterByEventName = null)
	{
		for (int i = 0; i < server.ModEventManager.EventBusListeners.Count; i++)
		{
			if (!(server.ModEventManager.EventBusListeners[i].priority >= priority))
			{
				server.ModEventManager.EventBusListeners.Insert(i, new EventBusListener
				{
					handler = OnEvent,
					priority = priority,
					filterByName = filterByEventName
				});
				return;
			}
		}
		server.ModEventManager.EventBusListeners.Add(new EventBusListener
		{
			handler = OnEvent,
			priority = priority,
			filterByName = filterByEventName
		});
	}

	public void UnregisterEventBusListener(EventBusListenerDelegate OnEvent)
	{
		List<EventBusListener> eventBusListeners = server.ModEventManager.EventBusListeners;
		for (int i = 0; i < eventBusListeners.Count; i++)
		{
			if (eventBusListeners[i].handler == OnEvent)
			{
				eventBusListeners.RemoveAt(i);
				break;
			}
		}
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return server.RegisterGameTickListener(OnGameTick, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return server.RegisterGameTickListener(OnGameTick, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return server.RegisterGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return server.RegisterGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return server.RegisterGameTickListener(OnGameTick, pos, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay)
	{
		return server.RegisterCallback(OnTimePassed, millisecondDelay);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay, bool permittedWhilePaused)
	{
		return server.RegisterCallback(OnTimePassed, millisecondDelay);
	}

	public long RegisterCallback(Action<IWorldAccessor, BlockPos, float> OnTimePassed, BlockPos pos, int millisecondDelay)
	{
		return server.RegisterCallback(OnTimePassed, pos, millisecondDelay);
	}

	public void UnregisterCallback(long listenerId)
	{
		server.UnregisterCallback(listenerId);
	}

	public void UnregisterGameTickListener(long listenerId)
	{
		server.UnregisterGameTickListener(listenerId);
	}

	public void EnqueueMainThreadTask(Action action, string code)
	{
		server.EnqueueMainThreadTask(action);
	}

	public void TriggerEntityDeath(Entity entity, DamageSource damageSourceForDeath)
	{
		Trigger(OnEntityDeath?.GetInvocationList(), "OnEntityDeath", (EntityDeathDelegate dele) =>
		{
			dele?.Invoke(entity, damageSourceForDeath);
		});
	}

	public bool TriggerMatchesRecipe(IPlayer forPlayer, GridRecipe gridRecipe, ItemSlot[] ingredients, int gridWidth)
	{
		if (MatchesGridRecipe == null)
		{
			return true;
		}
		return MatchesGridRecipe(forPlayer, gridRecipe, ingredients, gridWidth);
	}

	public bool TriggerMatchesRecipe(IPlayer forPlayer, IRecipeBase recipe, ItemSlot[] ingredients)
	{
		if (MatchesRecipe == null)
		{
			return true;
		}
		return MatchesRecipe(forPlayer, recipe, ingredients);
	}

	public void TriggerPlayerDimensionChanged(IPlayer player)
	{
		Trigger(PlayerDimensionChanged?.GetInvocationList(), "PlayerDimensionChanged", (PlayerCommonDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public void PlayerChunkTransition(IServerPlayer player)
	{
		server.clientAwarenessSystem?.TriggerEvent(EnumClientAwarenessEvent.ChunkTransition, player.ClientId);
	}

	public void TriggerEntityMounted(EntityAgent entityAgent, IMountableSeat entityRideableSeat)
	{
		Trigger(EntityMounted?.GetInvocationList(), "EntityMounted", (EntityMountDelegate dele) =>
		{
			dele?.Invoke(entityAgent, entityRideableSeat);
		});
	}

	public void TriggerEntityUnmounted(EntityAgent entityAgent, IMountableSeat entityRideableSeat)
	{
		Trigger(EntityUnmounted?.GetInvocationList(), "EntityUnmounted", (EntityMountDelegate dele) =>
		{
			dele?.Invoke(entityAgent, entityRideableSeat);
		});
	}

	public void TriggerMountGaitReceived(Entity mountEntity, string gaitCode)
	{
		Trigger(MountGaitReceived?.GetInvocationList(), "MountGaitReceived", (MountGaitReceivedDelegate dele) =>
		{
			dele?.Invoke(mountEntity, gaitCode);
		});
	}
}
