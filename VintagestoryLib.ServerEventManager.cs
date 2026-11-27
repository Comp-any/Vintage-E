using System;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Vintagestory.Server;

public class ServerEventManager : EventManager
{
	internal ServerMain server;

	public Dictionary<EnumServerRunPhase, List<Action>> serverRunPhaseDelegates;

	public List<WorldGenThreadDelegate> WorldgenBlockAccessor = new List<WorldGenThreadDelegate>();

	public Dictionary<string, WorldGenHandler> WorldgenHandlers = new Dictionary<string, WorldGenHandler>();

	public List<EventBusListener> EventBusListeners = new List<EventBusListener>();

	public override ILogger Logger => ServerMain.Logger;

	public override string CommandPrefix => "/";

	public override long InWorldEllapsedMs => server.ElapsedMilliseconds;

	public event Action OnSaveGameCreated;

	public event Action OnSaveGameLoaded;

	public event Action AssetsFirstLoaded;

	public event Action AssetsFinalizer;

	public event Action OnGameWorldBeingSaved;

	public event Action OnWorldgenStartup;

	public event Action OnStartPhysicsThread;

	public event UpnpCompleteDelegate OnUpnpComplete;

	public event PlayerDelegate OnPlayerRespawn;

	public event PlayerDelegate OnPlayerJoin;

	public event PlayerDelegate OnPlayerNowPlaying;

	public event PlayerDelegate OnPlayerReady;

	public event PlayerDelegate OnPlayerLeave;

	public event PlayerDelegate OnPlayerDisconnect;

	public event PlayerChatDelegate OnPlayerChat;

	public event PlayerDeathDelegate OnPlayerDeath;

	public event HandInteractDelegate HandInteract;

	public event PlayerDelegate OnPlayerChangeGamemode;

	public event Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling> BeforeActiveSlotChanged;

	public event Action<IServerPlayer, ActiveSlotChangeEventArgs> AfterActiveSlotChanged;

	public event PlayerDelegate OnPlayerCreate;

	public event CanUseDelegate CanUseBlock;

	public event CanPlaceOrBreakDelegate CanPlaceOrBreakBlock;

	public event BlockUsedDelegate DidUseBlock;

	public event BlockPlacedDelegate DidPlaceBlock;

	public event BlockBrokenDelegate DidBreakBlock;

	public event BlockBreakDelegate BreakBlock;

	public event OnInteractDelegate OnPlayerInteractEntity;

	public event EntityDelegate OnEntitySpawn;

	public event EntityDespawnDelegate OnEntityDespawn;

	public event EntityDelegate OnEntityLoaded;

	public event TrySpawnEntityDelegate OnTrySpawnEntity;

	public event TrySpawnGroupDelegate OnTrySpawnGroupNearOffthread;

	public ServerEventManager(ServerMain server)
	{
		this.server = server;
		Init();
	}

	private void Init()
	{
		serverRunPhaseDelegates = new Dictionary<EnumServerRunPhase, List<Action>>();
		foreach (EnumServerRunPhase value in Enum.GetValues(typeof(EnumServerRunPhase)))
		{
			serverRunPhaseDelegates[value] = new List<Action>();
		}
	}

	public override bool HasPrivilege(string playerUid, string privilegecode)
	{
		if (!server.GetServerPlayerData(playerUid).HasPrivilege(privilegecode, server.Config.RolesByCode))
		{
			return playerUid == "console";
		}
		return true;
	}

	internal void RegisterOnServerRunPhase(EnumServerRunPhase runPhase, Action handler)
	{
		server.ModEventManager.serverRunPhaseDelegates[runPhase].Add(handler);
	}

	public virtual void TriggerUpnpComplete(bool success)
	{
		Trigger(OnUpnpComplete?.GetInvocationList(), "OnUpnpComplete", (UpnpCompleteDelegate dele) =>
		{
			dele?.Invoke(success);
		});
	}

	public virtual void TriggerEntityLoaded(Entity entity)
	{
		Trigger(OnEntityLoaded?.GetInvocationList(), "OnEntityLoaded", (EntityDelegate dele) =>
		{
			dele?.Invoke(entity);
		});
	}

	public virtual void TriggerEntitySpawned(Entity entity)
	{
		Trigger(OnEntitySpawn?.GetInvocationList(), "OnEntitySpawn", (EntityDelegate dele) =>
		{
			dele?.Invoke(entity);
		});
	}

	public virtual bool TriggerTrySpawnGroupNearOffthread(IBlockAccessor blockaccessor, EntityProperties props, BlockPos pos)
	{
		if (OnTrySpawnGroupNearOffthread == null)
		{
			return true;
		}
		bool flag = true;
		Delegate[] invocationList = OnTrySpawnGroupNearOffthread.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			TrySpawnGroupDelegate trySpawnGroupDelegate = (TrySpawnGroupDelegate)invocationList[i];
			try
			{
				flag = trySpawnGroupDelegate(blockaccessor, props, pos);
				if (!flag)
				{
					return false;
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Exception thrown during handling event OnTrySpawnGroupNearOffthread. Will skip over.");
				ServerMain.Logger.Error(e);
			}
		}
		return flag;
	}

	public virtual bool TriggerTrySpawnEntity(IBlockAccessor blockaccessor, ref EntityProperties properties, Vec3d position, long herdId)
	{
		if (OnTrySpawnEntity == null)
		{
			return true;
		}
		bool flag = true;
		Delegate[] invocationList = OnTrySpawnEntity.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			TrySpawnEntityDelegate trySpawnEntityDelegate = (TrySpawnEntityDelegate)invocationList[i];
			try
			{
				flag = trySpawnEntityDelegate(blockaccessor, ref properties, position, herdId);
				if (!flag)
				{
					return false;
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Exception thrown during handling event OnTrySpawnEntity. Will skip over.");
				ServerMain.Logger.Error(e);
			}
		}
		return flag;
	}

	public virtual void TriggerEntityDespawned(Entity entity, EntityDespawnData reason)
	{
		Trigger(OnEntityDespawn?.GetInvocationList(), "OnEntityDespawned", (EntityDespawnDelegate dele) =>
		{
			dele?.Invoke(entity, reason);
		});
	}

	public virtual void OnAssetsFirstLoaded()
	{
		Trigger(AssetsFirstLoaded?.GetInvocationList(), "AssetsFirstLoaded", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public virtual void TriggerFinalizeAssets()
	{
		Trigger(AssetsFinalizer?.GetInvocationList(), "FinalizeAssets", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public virtual void TriggerWorldgenStartup()
	{
		Trigger(OnWorldgenStartup?.GetInvocationList(), "OnWorldgenStartup", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public virtual void TriggerPhysicsThreadStart()
	{
		OnStartPhysicsThread?.Invoke();
	}

	public virtual void TriggerSaveGameLoaded()
	{
		Trigger(OnSaveGameLoaded?.GetInvocationList(), "OnSaveGameLoaded", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public virtual void TriggerSaveGameCreated()
	{
		Trigger(OnSaveGameCreated?.GetInvocationList(), "OnSaveGameCreated", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public virtual void TriggerGameWorldBeingSaved()
	{
		if (OnGameWorldBeingSaved == null)
		{
			return;
		}
		Delegate[] invocationList = OnGameWorldBeingSaved.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			Action action = (Action)invocationList[i];
			try
			{
				action();
				FrameProfilerUtil frameProfiler = ServerMain.FrameProfiler;
				if (frameProfiler != null && frameProfiler.Enabled)
				{
					ServerMain.FrameProfiler.Mark("beingsaved-", action.Target.ToString());
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Exception thrown during handling event OnGameWorldBeingSaved. Will skip over.");
				ServerMain.Logger.Error(e);
			}
		}
	}

	public virtual void TriggerDidUseBlock(IServerPlayer player, BlockSelection blockSel)
	{
		Trigger(DidUseBlock?.GetInvocationList(), "DidUseBlock", (BlockUsedDelegate dele) =>
		{
			dele?.Invoke(player, blockSel);
		});
	}

	public virtual void TriggerDidBreakBlock(IServerPlayer player, int oldBlockId, BlockSelection blockSel)
	{
		Trigger(DidBreakBlock?.GetInvocationList(), "DidBreakBlock", (BlockBrokenDelegate dele) =>
		{
			dele?.Invoke(player, oldBlockId, blockSel);
		});
	}

	public virtual void TriggerBreakBlock(IServerPlayer player, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
	{
		if (BreakBlock == null)
		{
			return;
		}
		Delegate[] invocationList = BreakBlock.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			BlockBreakDelegate blockBreakDelegate = (BlockBreakDelegate)invocationList[i];
			try
			{
				blockBreakDelegate?.Invoke(player, blockSel, ref dropQuantityMultiplier, ref handling);
				if (handling == EnumHandling.PreventSubsequent)
				{
					break;
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Mod exception during event BreakBlock. Will skip over");
				ServerMain.Logger.Error(e);
			}
		}
	}

	public virtual void TriggerPlayerInteractEntity(Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int mode, ref EnumHandling handling)
	{
		if (OnPlayerInteractEntity == null)
		{
			return;
		}
		Delegate[] invocationList = OnPlayerInteractEntity.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			OnInteractDelegate onInteractDelegate = (OnInteractDelegate)invocationList[i];
			try
			{
				onInteractDelegate?.Invoke(entity, byPlayer, slot, hitPosition, mode, ref handling);
				if (handling == EnumHandling.PreventSubsequent)
				{
					break;
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Mod exception during event BreakBlock. Will skip over");
				ServerMain.Logger.Error(e);
			}
		}
	}

	public virtual void TriggerDidPlaceBlock(IServerPlayer player, int oldBlockId, BlockSelection blockSel, ItemStack withItemStack)
	{
		Trigger(DidPlaceBlock?.GetInvocationList(), "DidPlaceBlock", (BlockPlacedDelegate dele) =>
		{
			dele?.Invoke(player, oldBlockId, blockSel, withItemStack);
		});
	}

	public virtual void TriggerPlayerLeave(IServerPlayer player)
	{
		Trigger(OnPlayerLeave?.GetInvocationList(), "OnPlayerLeave", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerDisconnect(IServerPlayer player)
	{
		Trigger(OnPlayerDisconnect?.GetInvocationList(), "OnPlayerDisconnect", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerCreate(IServerPlayer player)
	{
		Trigger(OnPlayerCreate?.GetInvocationList(), "OnPlayerCreate", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerJoin(IServerPlayer player)
	{
		Trigger(OnPlayerJoin?.GetInvocationList(), "OnPlayerJoin", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerNowPlaying(IServerPlayer player)
	{
		Trigger(OnPlayerNowPlaying?.GetInvocationList(), "OnPlayerNowPlaying", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerReady(IServerPlayer player)
	{
		Trigger(OnPlayerReady?.GetInvocationList(), "OnPlayerReady", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual void TriggerPlayerRespawn(IServerPlayer player)
	{
		Trigger(OnPlayerRespawn?.GetInvocationList(), "OnPlayerRespawn", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual bool TriggerBeforeActiveSlotChanged(IServerPlayer player, int fromSlot, int toSlot)
	{
		ActiveSlotChangeEventArgs arg = new ActiveSlotChangeEventArgs(fromSlot, toSlot);
		return BeforeActiveSlotChanged?.InvokeSafeCancellable(Logger, "BeforeActiveSlotChanged", player, arg) ?? true;
	}

	public virtual void TriggerAfterActiveSlotChanged(IServerPlayer player, int fromSlot, int toSlot)
	{
		ActiveSlotChangeEventArgs args = new ActiveSlotChangeEventArgs(fromSlot, toSlot);
		Trigger(AfterActiveSlotChanged?.GetInvocationList(), "AfterActiveSlotChanged", (Action<IServerPlayer, ActiveSlotChangeEventArgs> dele) =>
		{
			dele?.Invoke(player, args);
		});
	}

	public virtual void TriggerOnplayerChat(IServerPlayer player, int channelId, ref string message, ref string data, BoolRef consumed)
	{
		if (OnPlayerChat == null)
		{
			return;
		}
		Delegate[] invocationList = OnPlayerChat.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			PlayerChatDelegate playerChatDelegate = (PlayerChatDelegate)invocationList[i];
			try
			{
				playerChatDelegate(player, channelId, ref message, ref data, consumed);
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception: OnPlayerChat");
				Logger.Error(e);
			}
			if (consumed.value)
			{
				break;
			}
		}
	}

	public virtual void TriggerPlayerDeath(IServerPlayer player, DamageSource source)
	{
		Trigger(OnPlayerDeath?.GetInvocationList(), "OnPlayerDeath", (PlayerDeathDelegate dele) =>
		{
			dele?.Invoke(player, source);
		});
	}

	public virtual void TriggerHandInteraction(IServerPlayer player, EnumHandInteractNw enumHandInteract, float secondsPassed, ref EnumHandling handling)
	{
		Delegate[] array = HandInteract?.GetInvocationList();
		if (array == null)
		{
			return;
		}
		handling = EnumHandling.PassThrough;
		Delegate[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			HandInteractDelegate handInteractDelegate = (HandInteractDelegate)array2[i];
			try
			{
				EnumHandling handling2 = EnumHandling.PassThrough;
				handInteractDelegate(player, enumHandInteract, secondsPassed, ref handling2);
				if (handling2 == EnumHandling.PreventDefault)
				{
					handling = EnumHandling.PreventDefault;
				}
				if (handling2 == EnumHandling.PreventSubsequent)
				{
					handling = EnumHandling.PreventDefault;
					break;
				}
			}
			catch (Exception e)
			{
				ServerMain.Logger.Error("Mod exception during event HandInteraction. Will skip to next event");
				ServerMain.Logger.Error(e);
			}
		}
	}

	public virtual void TriggerPlayerChangeGamemode(IServerPlayer player)
	{
		Trigger(OnPlayerChangeGamemode?.GetInvocationList(), "OnPlayerChangeGamemode", (PlayerDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public virtual bool TriggerCanPlaceOrBreak(IServerPlayer player, BlockSelection blockSel, out string claimant)
	{
		claimant = null;
		if (CanPlaceOrBreakBlock == null)
		{
			return true;
		}
		bool flag = true;
		Delegate[] invocationList = CanPlaceOrBreakBlock.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			CanPlaceOrBreakDelegate canPlaceOrBreakDelegate = (CanPlaceOrBreakDelegate)invocationList[i];
			try
			{
				flag = flag && canPlaceOrBreakDelegate(player, blockSel, out claimant);
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception during CanPlaceOrBreak");
				Logger.Error(e);
				flag = false;
				break;
			}
		}
		return flag;
	}

	public virtual bool TriggerCanUse(IServerPlayer player, BlockSelection blockSel)
	{
		bool retval = true;
		Trigger(CanUseBlock?.GetInvocationList(), "CanUse", (CanUseDelegate dele) =>
		{
			retval = retval && dele(player, blockSel);
		}, () =>
		{
			retval = false;
		});
		return retval;
	}

	public WorldGenHandler GetWorldGenHandler(string worldType)
	{
		WorldgenHandlers.TryGetValue(worldType, out var value);
		return value;
	}

	public WorldGenHandler GetOrCreateWorldGenHandler(string worldType)
	{
		if (!WorldgenHandlers.TryGetValue(worldType, out var value))
		{
			value = (WorldgenHandlers[worldType] = new WorldGenHandler());
		}
		return value;
	}

	public void WipeAllDelegates()
	{
		serverRunPhaseDelegates.Clear();
		AssetsFirstLoaded = null;
		AssetsFinalizer = null;
		OnSaveGameLoaded = null;
		OnGameWorldBeingSaved = null;
		DidUseBlock = null;
		DidPlaceBlock = null;
		DidBreakBlock = null;
		OnPlayerRespawn = null;
		OnPlayerCreate = null;
		OnPlayerJoin = null;
		OnPlayerNowPlaying = null;
		OnPlayerLeave = null;
		OnPlayerDisconnect = null;
		OnPlayerChat = null;
		OnPlayerDeath = null;
		OnEntityLoaded = null;
		OnEntityDespawn = null;
		OnEntitySpawn = null;
		OnTrySpawnEntity = null;
		OnPlayerInteractEntity = null;
		CanUseBlock = null;
		CanPlaceOrBreakBlock = null;
		GameTickListenersEntity.Clear();
		DelayedCallbacksEntity.Clear();
		GameTickListenersBlock.Clear();
		DelayedCallbacksBlock.Clear();
		Logger.ClearWatchers();
		OnPlayerChangeGamemode = null;
		BeforeActiveSlotChanged = null;
		AfterActiveSlotChanged = null;
		foreach (KeyValuePair<string, WorldGenHandler> worldgenHandler in WorldgenHandlers)
		{
			worldgenHandler.Value.WipeAllHandlers();
		}
		Init();
	}

	public override long AddGameTickListener(Action<float> handler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
		{
			string StackTrace = Environment.StackTrace;
			server.EnqueueMainThreadTask(() =>
			{
				ServerMain.Logger.Error("Warning: Attempting to add an entity listener outside of the main thread. " + Thread.CurrentThread.Name + " This may produce a race condition!\r\n" + StackTrace);
			});
		}
		return AddGameTickListener(handler, null, millisecondInterval, initialDelayOffsetMs);
	}

	public override long AddGameTickListener(Action<float> handler, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
		{
			string StackTrace = Environment.StackTrace;
			server.EnqueueMainThreadTask(() =>
			{
				ServerMain.Logger.Error("Warning: Attempting to add an block listener outside of the main thread. " + Thread.CurrentThread.Name + " This may produce a race condition!\r\n" + StackTrace);
			});
		}
		return base.AddGameTickListener(handler, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public override long AddGameTickListener(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
		{
			string StackTrace = Environment.StackTrace;
			server.EnqueueMainThreadTask(() =>
			{
				ServerMain.Logger.Error("Warning: Attempting to add an block listener outside of the main thread. " + Thread.CurrentThread.Name + " This may produce a race condition!\r\n" + StackTrace);
			});
		}
		return base.AddGameTickListener(handler, pos, millisecondInterval, initialDelayOffsetMs);
	}

	public override long AddGameTickListener(Action<float> handler, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
		{
			string StackTrace = Environment.StackTrace;
			server.EnqueueMainThreadTask(() =>
			{
				ServerMain.Logger.Error("Warning: Attempting to add a entity listener outside of the main thread. " + Thread.CurrentThread.Name + " This may produce a race condition!\r\n" + StackTrace);
			});
		}
		return base.AddGameTickListener(handler, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public override long AddGameTickListener(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
		{
			string StackTrace = Environment.StackTrace;
			server.EnqueueMainThreadTask(() =>
			{
				ServerMain.Logger.Error("Warning: Attempting to add an block listener outside of the main thread. " + Thread.CurrentThread.Name + " This may produce a race condition!\r\n" + StackTrace);
			});
		}
		return base.AddGameTickListener(handler, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}
}
