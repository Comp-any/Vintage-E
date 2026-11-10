using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public class ClientEventAPI : IClientEventAPI, IEventAPI
{
	private static string strThreadException = "Cannot call this method outside the main thread. Not thread safe to do so. You can use the thread safe method .EnqueueMainThreadTask() to queue up the register on the main thread instead.";

	private ClientMain game;

	internal Dictionary<int, ItemRenderDelegate>[][] itemStackRenderersByTarget;

	public event MouseEventDelegate MouseDown;

	public event MouseEventDelegate MouseUp;

	public event MouseEventDelegate MouseMove;

	public event MouseWheelEventDelegate MouseWheelMove;

	public event KeyEventDelegate KeyDown;

	public event KeyEventDelegate KeyUp;

	public event FileDropDelegate FileDrop;

	public event PlayerEventDelegate PlayerJoin;

	public event PlayerEventDelegate PlayerLeave;

	public event PlayerEventDelegate PlayerEntitySpawn;

	public event PlayerEventDelegate PlayerEntityDespawn;

	public event Action BlockTexturesLoaded;

	public event IsPlayerReadyDelegate IsPlayerReady;

	public event OnGamePauseResume PauseResume;

	public event Action LeaveWorld;

	public event Action LeftWorld;

	public event ChatLineDelegate ChatMessage;

	public event BlockChangedDelegate BlockChanged;

	public event Vintagestory.API.Common.Func<ActiveSlotChangeEventArgs, EnumHandling> BeforeActiveSlotChanged;

	public event Action<ActiveSlotChangeEventArgs> AfterActiveSlotChanged;

	public event ChunkDirtyDelegate ChunkDirty;

	public event Action LevelFinalize;

	public event Action HotkeysChanged;

	public event ClientChatLineDelegate OnSendChatMessage;

	public event EntityDeathDelegate OnEntityDeath;

	public event MatchGridRecipeDelegate MatchesGridRecipe;

	public event MatchRecipeDelegate MatchesRecipe;

	public event PlayerEventDelegate PlayerDeath;

	public event TestBlockAccessDelegate OnTestBlockAccess;

	public event TestBlockAccessClaimDelegate OnTestBlockAccessClaim;

	public event MapRegionLoadedDelegate MapRegionLoaded;

	public event MapRegionUnloadDelegate MapRegionUnloaded;

	public event PlayerCommonDelegate PlayerDimensionChanged;

	public event EntityMountDelegate EntityMounted;

	public event EntityMountDelegate EntityUnmounted;

	public event Action<BlockDamage> OnUnBreakingBlock
	{
		add
		{
			game.eventManager.OnUnBreakingBlock += value;
		}
		remove
		{
			game.eventManager.OnUnBreakingBlock -= value;
		}
	}

	public event Action<BlockDamage> OnPlayerBreakingBlock
	{
		add
		{
			game.eventManager.OnPlayerBreakingBlock += value;
		}
		remove
		{
			game.eventManager.OnPlayerBreakingBlock -= value;
		}
	}

	public event Action<BlockDamage> OnPlayerBrokenBlock
	{
		add
		{
			game.eventManager.OnPlayerBrokenBlock += value;
		}
		remove
		{
			game.eventManager.OnPlayerBrokenBlock -= value;
		}
	}

	public event OnGetClimateDelegate OnGetClimate
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnGetClimate += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnGetClimate -= value;
			}
		}
	}

	public event TestBlockAccessDelegate TestBlockAccess
	{
		add
		{
			OnTestBlockAccess += value;
		}
		remove
		{
			OnTestBlockAccess -= value;
		}
	}

	public event OnGetWindSpeedDelegate OnGetWindSpeed
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnGetWindSpeed += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnGetWindSpeed -= value;
			}
		}
	}

	public event IngameErrorDelegate InGameError
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.InGameError += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.InGameError -= value;
			}
		}
	}

	public event IngameDiscoveryDelegate InGameDiscovery
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.InGameDiscovery += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.InGameDiscovery -= value;
			}
		}
	}

	public event Action ColorsPresetChanged
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.ColorPresetChanged += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.ColorPresetChanged -= value;
			}
		}
	}

	public event EntityDelegate OnEntitySpawn
	{
		add
		{
			game.eventManager?.OnEntitySpawn.Add(value);
		}
		remove
		{
			game.eventManager?.OnEntitySpawn.Remove(value);
		}
	}

	public event EntityDelegate OnEntityLoaded
	{
		add
		{
			game.eventManager?.OnEntityLoaded.Add(value);
		}
		remove
		{
			game.eventManager?.OnEntityLoaded.Remove(value);
		}
	}

	public event EntityDespawnDelegate OnEntityDespawn
	{
		add
		{
			game.eventManager?.OnEntityDespawn.Add(value);
		}
		remove
		{
			game.eventManager?.OnEntityDespawn.Remove(value);
		}
	}

	public event ActionBoolReturn ReloadShader
	{
		add
		{
			game.eventManager?.OnReloadShaders.Add(value);
		}
		remove
		{
			game.eventManager?.OnReloadShaders.Remove(value);
		}
	}

	public event Action ReloadTextures
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnReloadTextures += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnReloadTextures -= value;
			}
		}
	}

	public event Action ReloadShapes
	{
		add
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnReloadShapes += value;
			}
		}
		remove
		{
			ClientEventManager eventManager = game.eventManager;
			if (eventManager != null)
			{
				eventManager.OnReloadShapes -= value;
			}
		}
	}

	public ClientEventAPI(ClientMain game)
	{
		this.game = game;
		game.eventManager?.OnNewServerToClientChatLine.Add(onChatLine);
		game.eventManager?.OnPlayerDeath.Add(playerDeath);
		int num = Enum.GetNames(typeof(EnumItemRenderTarget)).Length;
		itemStackRenderersByTarget = new Dictionary<int, ItemRenderDelegate>[2][];
		itemStackRenderersByTarget[0] = new Dictionary<int, ItemRenderDelegate>[num];
		itemStackRenderersByTarget[1] = new Dictionary<int, ItemRenderDelegate>[num];
		for (int i = 0; i < num; i++)
		{
			itemStackRenderersByTarget[0][i] = new Dictionary<int, ItemRenderDelegate>();
			itemStackRenderersByTarget[1][i] = new Dictionary<int, ItemRenderDelegate>();
		}
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
				game.Logger.Error("Mod exception during event " + eventName + ". Will skip to next event");
				game.Logger.Error(e);
				onException?.Invoke();
			}
		}
	}

	private void playerDeath(int clientid, int livesLeft)
	{
		IClientPlayer player = game.AllPlayers.FirstOrDefault((IPlayer plr) => plr.ClientId == clientid) as IClientPlayer;
		Trigger(PlayerDeath?.GetInvocationList(), "PlayerDeath", (PlayerEventDelegate dele) =>
		{
			dele?.Invoke(player);
		});
	}

	public EnumWorldAccessResponse TriggerTestBlockAccess(IPlayer player, BlockSelection blockSel, EnumBlockAccessFlags accessType, ref string claimant, LandClaim claim, EnumWorldAccessResponse response)
	{
		if (OnTestBlockAccess != null)
		{
			Delegate[] invocationList = OnTestBlockAccess.GetInvocationList();
			for (int i = 0; i < invocationList.Length; i++)
			{
				response = ((TestBlockAccessDelegate)invocationList[i])(player, blockSel, accessType, ref claimant, response);
			}
		}
		if (OnTestBlockAccessClaim != null)
		{
			Delegate[] invocationList = OnTestBlockAccessClaim.GetInvocationList();
			for (int i = 0; i < invocationList.Length; i++)
			{
				response = ((TestBlockAccessClaimDelegate)invocationList[i])(player, blockSel, accessType, ref claimant, claim, response);
			}
		}
		return response;
	}

	public void TriggerMapregionLoaded(Vec2i mapCoord, IMapRegion mapregion)
	{
		Trigger(MapRegionLoaded?.GetInvocationList(), "MapRegionLoaded", (MapRegionLoadedDelegate dele) =>
		{
			dele?.Invoke(mapCoord, mapregion);
		});
	}

	public void TriggerMapregionUnloaded(Vec2i mapCoord, IMapRegion mapregion)
	{
		Trigger(MapRegionUnloaded?.GetInvocationList(), "MapRegionUnloaded", (MapRegionUnloadDelegate dele) =>
		{
			dele?.Invoke(mapCoord, mapregion);
		});
	}

	public void TriggerChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
	{
		Trigger(ChunkDirty?.GetInvocationList(), "ChunkDirty", (ChunkDirtyDelegate dele) =>
		{
			dele?.Invoke(chunkCoord, chunk, reason);
		});
	}

	private void onChatLine(int groupId, string message, EnumChatType chattype, string data)
	{
		Trigger(ChatMessage?.GetInvocationList(), "ChatMessage", (ChatLineDelegate dele) =>
		{
			dele?.Invoke(groupId, message, chattype, data);
		});
	}

	public void TriggerBlockChanged(BlockPos pos, Block oldBlock)
	{
		Trigger(BlockChanged?.GetInvocationList(), "BlockChanged", (BlockChangedDelegate dele) =>
		{
			dele?.Invoke(pos, oldBlock);
		});
	}

	public void TriggerPlayerJoin(IClientPlayer plr)
	{
		Trigger(PlayerJoin?.GetInvocationList(), "PlayerJoin", (PlayerEventDelegate dele) =>
		{
			dele?.Invoke(plr);
		});
	}

	public void TriggerPlayerLeave(IClientPlayer plr)
	{
		Trigger(PlayerLeave?.GetInvocationList(), "PlayerLeave", (PlayerEventDelegate dele) =>
		{
			dele?.Invoke(plr);
		});
	}

	public void TriggerPlayerEntitySpawn(IClientPlayer plr)
	{
		Trigger(PlayerEntitySpawn?.GetInvocationList(), "PlayerEntitySpawn", (PlayerEventDelegate dele) =>
		{
			dele?.Invoke(plr);
		});
	}

	public void TriggerPlayerEntityDespawn(IClientPlayer plr)
	{
		Trigger(PlayerEntityDespawn?.GetInvocationList(), "PlayerEntityDespawn", (PlayerEventDelegate dele) =>
		{
			dele?.Invoke(plr);
		});
	}

	public bool TriggerFileDrop(string filename)
	{
		if (FileDrop == null)
		{
			return false;
		}
		FileDropEvent fileDropEvent = new FileDropEvent
		{
			Filename = filename
		};
		Delegate[] invocationList = FileDrop.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			FileDropDelegate fileDropDelegate = (FileDropDelegate)invocationList[i];
			try
			{
				fileDropDelegate(fileDropEvent);
				if (fileDropEvent.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event FileDrop. Will skip to next event");
				game.Logger.Error(e);
			}
		}
		return fileDropEvent.Handled;
	}

	public void TriggerPauseResume(bool pause)
	{
		Trigger(PauseResume?.GetInvocationList(), "PauseResume", (OnGamePauseResume dele) =>
		{
			dele?.Invoke(pause);
		});
	}

	public void TriggerMouseDown(MouseEvent ev)
	{
		if (MouseDown == null)
		{
			return;
		}
		Delegate[] invocationList = MouseDown.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			MouseEventDelegate mouseEventDelegate = (MouseEventDelegate)invocationList[i];
			try
			{
				mouseEventDelegate(ev);
				if (ev.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event MouseDown. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void TriggerMouseUp(MouseEvent ev)
	{
		if (MouseUp == null)
		{
			return;
		}
		Delegate[] invocationList = MouseUp.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			MouseEventDelegate mouseEventDelegate = (MouseEventDelegate)invocationList[i];
			try
			{
				mouseEventDelegate(ev);
				if (ev.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event MouseUp. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void TriggerMouseMove(MouseEvent ev)
	{
		if (MouseMove == null)
		{
			return;
		}
		Delegate[] invocationList = MouseMove.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			MouseEventDelegate mouseEventDelegate = (MouseEventDelegate)invocationList[i];
			try
			{
				mouseEventDelegate(ev);
				if (ev.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event MouseMove. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void TriggerMouseWheelMove(MouseWheelEventArgs ev)
	{
		if (MouseWheelMove == null)
		{
			return;
		}
		Delegate[] invocationList = MouseWheelMove.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			MouseWheelEventDelegate mouseWheelEventDelegate = (MouseWheelEventDelegate)invocationList[i];
			try
			{
				mouseWheelEventDelegate(ev);
				if (ev.IsHandled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event MouseWheelMove. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void TriggerKeyDown(KeyEvent ev)
	{
		if (KeyDown == null)
		{
			return;
		}
		Delegate[] invocationList = KeyDown.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			KeyEventDelegate keyEventDelegate = (KeyEventDelegate)invocationList[i];
			try
			{
				keyEventDelegate(ev);
				if (ev.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event KeyDown. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void TriggerKeyUp(KeyEvent ev)
	{
		if (KeyUp == null)
		{
			return;
		}
		Delegate[] invocationList = KeyUp.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			KeyEventDelegate keyEventDelegate = (KeyEventDelegate)invocationList[i];
			try
			{
				keyEventDelegate(ev);
				if (ev.Handled)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event KeyUp. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	internal bool TriggerBeforeActiveSlotChanged(ILogger logger, int fromSlot, int toSlot)
	{
		ActiveSlotChangeEventArgs arg = new ActiveSlotChangeEventArgs(fromSlot, toSlot);
		return BeforeActiveSlotChanged?.InvokeSafeCancellable(logger, "BeforeActiveSlotChanged", arg) ?? true;
	}

	internal void TriggerAfterActiveSlotChanged(ILogger logger, int fromSlot, int toSlot)
	{
		ActiveSlotChangeEventArgs arg = new ActiveSlotChangeEventArgs(fromSlot, toSlot);
		AfterActiveSlotChanged?.InvokeSafe(logger, "AfterActiveSlotChanged", arg);
	}

	public void TriggerLevelFinalize()
	{
		Trigger(LevelFinalize?.GetInvocationList(), "LevelFinalize", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public void TriggerLeaveWorld()
	{
		Trigger(LeaveWorld?.GetInvocationList(), "LeaveWorld", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public void TriggerLeftWorld()
	{
		Trigger(LeftWorld?.GetInvocationList(), "LeftWorld", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public void TriggerHotkeysChanged()
	{
		Trigger(HotkeysChanged?.GetInvocationList(), "HotkeysChanged", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public void TriggerBlockTexturesLoaded()
	{
		Trigger(BlockTexturesLoaded?.GetInvocationList(), "BlockTexturesLoaded", (Action dele) =>
		{
			dele?.Invoke();
		});
	}

	public bool TriggerIsPlayerReady()
	{
		if (IsPlayerReady == null)
		{
			return true;
		}
		EnumHandling handling = EnumHandling.PassThrough;
		bool flag = true;
		Delegate[] invocationList = IsPlayerReady.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			IsPlayerReadyDelegate isPlayerReadyDelegate = (IsPlayerReadyDelegate)invocationList[i];
			try
			{
				bool flag2 = isPlayerReadyDelegate(ref handling);
				if (handling != EnumHandling.PassThrough)
				{
					flag &= flag2;
				}
				if (handling == EnumHandling.PreventSubsequent)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event IsPlayerReady. Will skip to next event");
				game.Logger.Error(e);
			}
		}
		return flag;
	}

	public void RegisterRenderer(IRenderer renderer, EnumRenderStage stage, string profilingName = null)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call this method outside the main thread. Not thread safe to do so.");
		}
		game.eventManager?.RegisterRenderer(renderer, stage, profilingName);
	}

	public void RegisterRenderer(IRenderer renderer, EnumRenderStage renderStage, string profilingName, double reservedFirstOrder, double reservedLastOrder, Type firstType)
	{
		RegisterRenderer(renderer, renderStage, profilingName);
		game.eventManager?.ReserveRenderOrderRange(renderer, renderStage, reservedFirstOrder, reservedLastOrder, firstType);
	}

	public void RegisterItemstackRenderer(CollectibleObject forObj, ItemRenderDelegate rendererDelegate, EnumItemRenderTarget target)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call this method outside the main thread. Not thread safe to do so.");
		}
		itemStackRenderersByTarget[(int)forObj.ItemClass][(int)target][forObj.Id] = rendererDelegate;
	}

	public void UnregisterItemstackRenderer(CollectibleObject forObj, EnumItemRenderTarget target)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call this method outside the main thread. Not thread safe to do so.");
		}
		itemStackRenderersByTarget[(int)forObj.ItemClass][(int)target].Remove(forObj.Id);
	}

	public void UnregisterRenderer(IRenderer renderer, EnumRenderStage stage)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call this method outside the main thread. Not thread safe to do so.");
		}
		game.eventManager?.UnregisterRenderer(renderer, stage);
	}

	public void RegisterReloadShapes(Action handler)
	{
		ClientEventManager eventManager = game.eventManager;
		if (eventManager != null)
		{
			eventManager.OnReloadShapes += handler;
		}
	}

	public void UnregisterReloadShapes(Action handler)
	{
		ClientEventManager eventManager = game.eventManager;
		if (eventManager != null)
		{
			eventManager.OnReloadShapes -= handler;
		}
	}

	public void RegisterOnLeaveWorld(Action handler)
	{
		LeaveWorld += handler;
	}

	public void PushEvent(string eventName, IAttribute data = null)
	{
		EnumHandling handling = EnumHandling.PassThrough;
		for (int i = 0; i < game.eventManager?.EventBusListeners.Count; i++)
		{
			EventBusListener eventBusListener = game.eventManager?.EventBusListeners[i];
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
		if (game.eventManager == null)
		{
			return;
		}
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		for (int i = 0; i < game.eventManager.EventBusListeners.Count; i++)
		{
			if (!(game.eventManager.EventBusListeners[i].priority >= priority))
			{
				game.eventManager?.EventBusListeners.Insert(i, new EventBusListener
				{
					handler = OnEvent,
					priority = priority,
					filterByName = filterByEventName
				});
				return;
			}
		}
		game.eventManager?.EventBusListeners.Add(new EventBusListener
		{
			handler = OnEvent,
			priority = priority,
			filterByName = filterByEventName
		});
	}

	public void UnregisterEventBusListener(EventBusListenerDelegate OnEvent)
	{
		if (game.eventManager == null)
		{
			return;
		}
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		List<EventBusListener> eventBusListeners = game.eventManager.EventBusListeners;
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
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterGameTickListener(OnGameTick, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterGameTickListener(OnGameTick, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterGameTickListener(OnGameTick, pos, null, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterCallback(OnTimePassed, millisecondDelay);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay, bool permittedWhilePaused)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterCallback(OnTimePassed, millisecondDelay, permittedWhilePaused);
	}

	public long RegisterCallback(Action<IWorldAccessor, BlockPos, float> OnTimePassed, BlockPos pos, int millisecondDelay)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		return game.RegisterCallback(OnTimePassed, pos, millisecondDelay);
	}

	public void UnregisterCallback(long listenerId)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		game.UnregisterCallback(listenerId);
	}

	public void UnregisterGameTickListener(long listenerId)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException(strThreadException);
		}
		game.UnregisterGameTickListener(listenerId);
	}

	public void EnqueueMainThreadTask(Action action, string code)
	{
		game.EnqueueMainThreadTask(action, code);
	}

	internal void TriggerSendChatMessage(int groupId, ref string message, ref EnumHandling handling)
	{
		if (OnSendChatMessage == null)
		{
			return;
		}
		Delegate[] invocationList = OnSendChatMessage.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			ClientChatLineDelegate clientChatLineDelegate = (ClientChatLineDelegate)invocationList[i];
			try
			{
				clientChatLineDelegate(groupId, ref message, ref handling);
				if (handling == EnumHandling.PreventSubsequent)
				{
					break;
				}
			}
			catch (Exception e)
			{
				game.Logger.Error("Mod exception during event OnSendChatMessage. Will skip to next event");
				game.Logger.Error(e);
			}
		}
	}

	public void RegisterAsyncParticleSpawner(ContinousParticleSpawnTaskDelegate handler)
	{
		lock (game.asyncParticleSpawnersLock)
		{
			game.asyncParticleSpawners.Add(handler);
		}
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
}
