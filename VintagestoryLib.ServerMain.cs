using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VSPlatform;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Common.Datastructures;
using Vintagestory.Server.Network;
using Vintagestory.Server.Systems;
using VintagestoryLib.Server.Systems;

namespace Vintagestory.Server;

public sealed class ServerMain : GameMain, IServerWorldAccessor, IWorldAccessor, IShutDownMonitor
{
	internal ConcurrentDictionary<int, ClientLastLogin> RecentClientLogins = new ConcurrentDictionary<int, ClientLastLogin>(2, 16);

	public static IXPlatformInterface xPlatInterface;

	public GameExitState exitState;

	private bool suspended;

	public ThreadLocal<Random> rand = new ThreadLocal<Random>(() => new Random(Environment.TickCount));

	public bool Saving;

	public bool SendChunks = true;

	public bool AutoGenerateChunks = true;

	public bool stopped;

	public PlayerSpawnPos mapMiddleSpawnPos;

	public static Logger Logger;

	[ThreadStatic]
	public static FrameProfilerUtil FrameProfiler;

	public AssetManager AssetManager;

	internal EnumServerRunPhase RunPhase = EnumServerRunPhase.Standby;

	public bool readyToAutoSave = true;

	public List<Thread> Serverthreads = new List<Thread>();

	public readonly CancellationTokenSource ServerThreadsCts;

	internal List<ServerThread> ServerThreadLoops = new List<ServerThread>();

	internal ServerSystem[] Systems;

	public ServerEventManager ModEventManager;

	public CoreServerEventManager EventManager;

	public PlayerDataManager PlayerDataManager;

	public ServerUdpNetwork ServerUdpNetwork;

	private Thread ClientPacketParsingThread;

	public ServerWorldMap WorldMap;

	internal int CurrentPort;

	internal string CurrentIp;

	public readonly ConcurrentTagRegistry CollectibleTagRegistry;

	public readonly ConcurrentTagRegistryFast EntityTagRegistry;

	public static ClassRegistry ClassRegistry;

	public bool Standalone;

	private Stopwatch lastFramePassedTime = new Stopwatch();

	public Stopwatch totalUnpausedTime = new Stopwatch();

	public Stopwatch totalUpTime = new Stopwatch();

	public HashSet<string> AllPrivileges = new HashSet<string>();

	public Dictionary<string, string> PrivilegeDescriptions = new Dictionary<string, string>();

	internal int serverConsoleId = -1;

	private readonly CancellationTokenSource _consoleThreadsCts;

	internal ServerConsoleClient ServerConsoleClient;

	private ServerConsole serverConsole;

	public StatsCollection[] StatsCollector = new StatsCollection[4]
	{
		new StatsCollection(),
		new StatsCollection(),
		new StatsCollection(),
		new StatsCollection()
	};

	public int StatsCollectorIndex;

	public CachingConcurrentDictionary<int, ConnectedClient> Clients = new CachingConcurrentDictionary<int, ConnectedClient>();

	public Dictionary<string, ServerPlayer> PlayersByUid = new Dictionary<string, ServerPlayer>();

	public long TotalSentBytes;

	public long TotalSentBytesUdp;

	public long TotalReceivedBytes;

	public long TotalReceivedBytesUdp;

	public ServerConfig Config;

	public bool ConfigNeedsSaving;

	public bool ReducedServerThreads;

	internal long lastDisconnectTotalMs;

	private int lastClientId;

	public ConcurrentQueue<BlockPos> DirtyBlockEntities = new ConcurrentQueue<BlockPos>();

	public ConcurrentQueue<BlockPos> ModifiedBlocks = new ConcurrentQueue<BlockPos>();

	public ConcurrentQueue<Vec4i> DirtyBlocks = new ConcurrentQueue<Vec4i>();

	public ConcurrentQueue<BlockPos> ModifiedDecors = new ConcurrentQueue<BlockPos>();

	public ConcurrentQueue<BlockPos> ModifiedBlocksNoRelight = new ConcurrentQueue<BlockPos>();

	public List<BlockPos> ModifiedBlocksMinimal = new List<BlockPos>();

	public Queue<BlockPos> UpdatedBlocks = new Queue<BlockPos>();

	internal int nextFreeBlockId;

	public Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, ITreeGenerator> TreeGeneratorsByTreeCode = new Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, ITreeGenerator>();

	public Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, EntityProperties> EntityTypesByCode = new Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, EntityProperties>();

	internal List<EntityProperties> entityTypesCached;

	internal List<string> entityCodesCached;

	public int nextFreeItemId;

	internal Dictionary<EnumClientAwarenessEvent, List<Action<ClientStatistics>>> clientAwarenessEvents;

	internal ServerSystemClientAwareness clientAwarenessSystem;

	internal ServerSystemHeartbeat HeartbeatSystem;

	public object mainThreadTasksLock = new object();

	private Queue<Action> mainThreadTasks = new Queue<Action>();

	public StartServerArgs serverStartArgs;

	public ServerProgramArgs progArgs;

	public string[] RawCmdLineArgs;

	public int TickPosition;

	internal ChunkServerThread chunkThread;

	internal object suspendLock = new object();

	private int subsequentSuspendFailures;

	public int ExitCode;

	private int nextClientID = 1;

	internal DateTime statsupdate;

	public Dictionary<Timer, Timer.Tick> Timers = new Dictionary<Timer, Timer.Tick>();

	private bool ignoreDisconnectCalls;

	public bool debugProcessConnectionQueue;

	internal float[] blockLightLevels = new float[32]
	{
		0.062f, 0.102f, 0.142f, 0.182f, 0.222f, 0.262f, 0.302f, 0.342f, 0.382f, 0.422f,
		0.462f, 0.502f, 0.542f, 0.582f, 0.622f, 0.662f, 0.702f, 0.742f, 0.782f, 0.822f,
		0.862f, 0.902f, 0.943f, 0.985f, 1f, 1f, 1f, 1f, 1f, 1f,
		1f, 1f
	};

	internal float[] sunLightLevels = new float[32]
	{
		0.062f, 0.102f, 0.142f, 0.182f, 0.222f, 0.262f, 0.302f, 0.342f, 0.382f, 0.422f,
		0.462f, 0.502f, 0.542f, 0.582f, 0.622f, 0.662f, 0.702f, 0.742f, 0.782f, 0.822f,
		0.862f, 0.902f, 0.943f, 0.985f, 1f, 1f, 1f, 1f, 1f, 1f,
		1f, 1f
	};

	internal int sunBrightness = 24;

	internal int seaLevel = 110;

	private CollisionTester collTester = new CollisionTester();

	internal ClientPacketHandler<Packet_Client, ConnectedClient>[] PacketHandlers = new ClientPacketHandler<Packet_Client, ConnectedClient>[255];

	public HandleClientCustomUdpPacket HandleCustomUdpPackets;

	internal bool[] PacketHandlingOnConnectingAllowed = new bool[255];

	public List<QueuedClient> ConnectionQueue = new List<QueuedClient>();

	internal ConcurrentQueue<ReceivedClientPacket> ClientPackets = new ConcurrentQueue<ReceivedClientPacket>();

	internal List<int> DisconnectedClientsThisTick = new List<int>();

	[ThreadStatic]
	private static BoxedPacket reusableBuffer;

	private readonly List<BoxedArray> reusableBuffersDisposalList = new List<BoxedArray>();

	internal bool doNetBenchmark;

	internal SortedDictionary<int, int> packetBenchmark = new SortedDictionary<int, int>();

	internal SortedDictionary<string, int> packetBenchmarkBlockEntitiesBytes = new SortedDictionary<string, int>();

	internal SortedDictionary<int, int> packetBenchmarkBytes = new SortedDictionary<int, int>();

	internal SortedDictionary<int, int> udpPacketBenchmark = new SortedDictionary<int, int>();

	internal SortedDictionary<int, int> udpPacketBenchmarkBytes = new SortedDictionary<int, int>();

	private readonly BoxedPacket_ServerAssets serverAssetsPacket = new BoxedPacket_ServerAssets();

	private bool serverAssetsSentLocally;

	private bool worldMetaDataPacketAlreadySentToSinglePlayer;

	internal GameCalendar GameWorldCalendar;

	internal long lastCalendarFullUpdateSentToClient;

	internal long lastCalendarUpdateSentToClient;

	public bool DebugPrivileges;

	public HashSet<string> ClearPlayerInvs = new HashSet<string>();

	internal bool SpawnDebug;

	internal ServerCoreAPI api;

	internal HandleHandInteractionDelegate OnHandleBlockInteract;

	internal readonly CachingConcurrentDictionary<long, Entity> LoadedEntities = new CachingConcurrentDictionary<long, Entity>();

	public List<Entity> EntitySpawnSendQueue = new List<Entity>(10);

	public List<KeyValuePair<Entity, EntityDespawnData>> EntityDespawnSendQueue = new List<KeyValuePair<Entity, EntityDespawnData>>(10);

	public ConcurrentQueue<Entity> DelayedSpawnQueue = new ConcurrentQueue<Entity>();

	internal static int spawnSendQueue;

	public Dictionary<string, string> EntityCodeRemappings = new Dictionary<string, string>();

	internal ConcurrentDictionary<long, ServerMapChunk> loadedMapChunks = new ConcurrentDictionary<long, ServerMapChunk>(4, 256);

	internal ConcurrentDictionary<long, ServerMapRegion> loadedMapRegions = new ConcurrentDictionary<long, ServerMapRegion>(4, 32);

	internal ConcurrentDictionary<int, IMiniDimension> LoadedMiniDimensions = new ConcurrentDictionary<int, IMiniDimension>(4, 2);

	internal SaveGame SaveGameData;

	internal ChunkDataPool serverChunkDataPool;

	internal FastRWLock loadedChunksLock;

	internal Dictionary<long, ServerChunk> loadedChunks = new Dictionary<long, ServerChunk>(2000);

	internal object requestedChunkColumnsLock = new object();

	internal UniqueQueue<long> requestedChunkColumns = new UniqueQueue<long>();

	internal ConcurrentDictionary<long, int> ChunkColumnRequested = new ConcurrentDictionary<long, int>(2, 128);

	internal ConcurrentQueue<long> unloadedChunks = new ConcurrentQueue<long>();

	internal HashSet<long> forceLoadedChunkColumns = new HashSet<long>();

	internal ConcurrentQueue<ChunkColumnLoadRequest> simpleLoadRequests = new ConcurrentQueue<ChunkColumnLoadRequest>();

	internal ConcurrentQueue<long> deleteChunkColumns = new ConcurrentQueue<long>();

	internal ConcurrentQueue<long> deleteMapRegions = new ConcurrentQueue<long>();

	internal ConcurrentQueue<KeyValuePair<HorRectanglei, ChunkLoadOptions>> fastChunkQueue = new ConcurrentQueue<KeyValuePair<HorRectanglei, ChunkLoadOptions>>();

	internal ConcurrentQueue<KeyValuePair<Vec2i, ChunkPeekOptions>> peekChunkColumnQueue = new ConcurrentQueue<KeyValuePair<Vec2i, ChunkPeekOptions>>();

	internal ConcurrentQueue<ChunkLookupRequest> testChunkExistsQueue = new ConcurrentQueue<ChunkLookupRequest>();

	private ChunkLoadOptions defaultOptions = new ChunkLoadOptions();

	public bool requiresRemaps;

	public override IWorldAccessor World => this;

	protected override WorldMap worldmap => WorldMap;

	public int Seed => SaveGameData.Seed;

	public string SavegameIdentifier => SaveGameData.SavegameIdentifier;

	public bool Suspended => suspended;

	FrameProfilerUtil IWorldAccessor.FrameProfiler => FrameProfiler;

	public override ClassRegistry ClassRegistryInt
	{
		get
		{
			return ClassRegistry;
		}
		set
		{
			ClassRegistry = value;
		}
	}

	public int ServerConsoleId => serverConsoleId;

	public NetServer[] MainSockets { get; set; } = new NetServer[2];

	public UNetServer[] UdpSockets { get; set; } = new UNetServer[2];

	ILogger IWorldAccessor.Logger => Logger;

	IAssetManager IWorldAccessor.AssetManager => AssetManager;

	public EnumAppSide Side => EnumAppSide.Server;

	public ICoreAPI Api => api;

	public IChunkProvider ChunkProvider => WorldMap;

	public ILandClaimAPI Claims => WorldMap;

	public EntityPos DefaultSpawnPosition => EntityPosFromSpawnPos(SaveGameData.DefaultSpawn ?? mapMiddleSpawnPos);

	public float[] BlockLightLevels => blockLightLevels;

	public float[] SunLightLevels => sunLightLevels;

	public int SeaLevel => seaLevel;

	public int SunBrightness => sunBrightness;

	public bool IsDedicatedServer { get; }

	public IBlockAccessor BlockAccessor => WorldMap.RelaxedBlockAccess;

	public IBulkBlockAccessor BulkBlockAccessor => WorldMap.BulkBlockAccess;

	Random IWorldAccessor.Rand => rand.Value;

	public long ElapsedMilliseconds => totalUnpausedTime.ElapsedMilliseconds;

	public List<EntityProperties> EntityTypes => entityTypesCached ?? (entityTypesCached = EntityTypesByCode.Values.ToList());

	public List<string> EntityTypeCodes => entityCodesCached ?? (entityCodesCached = makeEntityCodesCache());

	public int DefaultEntityTrackingRange => MagicNum.DefaultEntityTrackingRange;

	List<GridRecipe> IWorldAccessor.GridRecipes => GridRecipes;

	List<CollectibleObject> IWorldAccessor.Collectibles => Collectibles;

	IList<Block> IWorldAccessor.Blocks => Blocks;

	IList<Item> IWorldAccessor.Items => Items;

	List<EntityProperties> IWorldAccessor.EntityTypes => EntityTypes;

	List<string> IWorldAccessor.EntityTypeCodes => EntityTypeCodes;

	public Dictionary<string, string> RemappedEntities => EntityCodeRemappings;

	public string WorldName => SaveGameData.WorldName;

	public Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, ITreeGenerator> TreeGenerators => TreeGeneratorsByTreeCode;

	public IPlayer[] AllOnlinePlayers => (from c in Clients.Values
		select c.Player into c
		where c != null
		select c).ToArray();

	public IPlayer[] AllPlayers => PlayersByUid.Values.ToArray();

	public bool EntityDebugMode => Config.EntityDebugMode;

	IClassRegistryAPI IWorldAccessor.ClassRegistry => api.ClassRegistry;

	public CollisionTester CollisionTester => collTester;

	ConcurrentDictionary<long, Entity> IServerWorldAccessor.LoadedEntities => LoadedEntities;

	public override Vec3i MapSize => WorldMap.MapSize;

	ITreeAttribute IWorldAccessor.Config => SaveGameData?.WorldConfiguration;

	public override IBlockAccessor blockAccessor => WorldMap.RelaxedBlockAccess;

	public IGameCalendar Calendar => GameWorldCalendar;

	public bool ShuttingDown => RunPhase >= EnumServerRunPhase.Shutdown;

	public long[] LoadedChunkIndices => loadedChunks.Keys.ToArray();

	public long[] LoadedMapChunkIndices => loadedMapChunks.Keys.ToArray();

	private void HandleRequestJoin(Packet_Client packet, ConnectedClient client)
	{
		FrameProfiler.Mark("reqjoin-before");
		Logger.VerboseDebug("HandleRequestJoin: Begin. Player: {0}", client?.PlayerName);
		ServerPlayer player = client.Player;
		player.LanguageCode = packet.RequestJoin.Language ?? Lang.CurrentLocale;
		if (client.IsSinglePlayerClient)
		{
			player.serverdata.RoleCode = Config.Roles.MaxBy((PlayerRole v) => v.PrivilegeLevel).Code;
		}
		Logger.VerboseDebug("HandleRequestJoin: Before set name");
		client.Entityplayer.SetName(player.PlayerName);
		api.networkapi.SendChannelsPacket(player);
		SendPacket(player, ServerPackets.LevelInitialize(Config.MaxChunkRadius * MagicNum.ServerChunkSize));
		Logger.VerboseDebug("HandleRequestJoin: After Level initialize");
		SendLevelProgress(player, 100, Lang.Get("Generating world..."));
		FrameProfiler.Mark("reqjoin-1");
		SendWorldMetaData(player);
		FrameProfiler.Mark("reqjoin-2");
		SendServerAssets(player);
		FrameProfiler.Mark("reqjoin-3");
		client.ServerAssetsSent = true;
		using FastMemoryStream fastMemoryStream = new FastMemoryStream();
		SendPlayerEntity(player, fastMemoryStream);
		FrameProfiler.Mark("reqjoin-4");
		ServerSystem[] systems = Systems;
		for (int num = 0; num < systems.Length; num++)
		{
			systems[num].OnPlayerJoin(player);
		}
		EventManager.TriggerPlayerJoin(player);
		BroadcastPlayerData(player, sendInventory: true, sendPrivileges: true);
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value != client && value.Entityplayer != null)
			{
				fastMemoryStream.Reset();
				SendInitialPlayerDataForOthers(value.Player, player, fastMemoryStream);
			}
		}
		Logger.VerboseDebug("HandleRequestJoin: After broadcastplayerdata. hotbarslot: " + player.inventoryMgr.ActiveHotbarSlot);
		ItemStack itemStack = player.inventoryMgr?.ActiveHotbarSlot?.Itemstack;
		ItemStack itemStack2 = player.Entity?.LeftHandItemSlot?.Itemstack;
		SendPacket(player, new Packet_Server
		{
			SelectedHotbarSlot = new Packet_SelectedHotbarSlot
			{
				SlotNumber = player.InventoryManager.ActiveHotbarSlotNumber,
				ClientId = player.ClientId,
				Itemstack = ((itemStack == null) ? null : StackConverter.ToPacket(itemStack)),
				OffhandStack = ((itemStack2 == null) ? null : StackConverter.ToPacket(itemStack2))
			},
			Id = 53
		});
		SendPacket(player, ServerPackets.LevelFinalize());
		Logger.VerboseDebug("HandleRequestJoin: After LevelFinalize");
		if (client.IsNewEntityPlayer)
		{
			EventManager.TriggerPlayerCreate(client.Player);
		}
		systems = Systems;
		for (int num = 0; num < systems.Length; num++)
		{
			systems[num].OnPlayerJoinPost(player);
		}
		FrameProfiler.Mark("reqjoin-after");
	}

	private void HandleClientLoaded(Packet_Client packet, ConnectedClient client)
	{
		ServerPlayer player = client.Player;
		player.Entity.WatchedAttributes.MarkAllDirty();
		client.State = EnumClientState.Connected;
		client.MillisecsAtConnect = totalUnpausedTime.ElapsedMilliseconds;
		joinLeaveDeathMessage(Lang.Get("player-joined", player.PlayerName), EnumChatType.JoinLeave, player);
		Logger.Event($"{player.PlayerName} {client.Socket.RemoteEndPoint()} joins.");
		string text = Config.WelcomeMessage.Replace("{playername}", "{0}");
		string message;
		try
		{
			message = string.Format(text, player.PlayerName);
		}
		catch (Exception)
		{
			Logger.Warning("Error formatting welcome message: \"" + text.Replace("{", "{{").Replace("}", "}}") + "\" for player " + player.PlayerName);
			message = text;
		}
		SendMessage(player, GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
		EventManager.TriggerPlayerNowPlaying(client.Player);
		if (Config.RepairMode)
		{
			SendMessage(player, GlobalConstants.GeneralChatGroup, "Server is in repair mode, you are now in spectator mode. If you are not already there, fly to the area that crashes and let the chunks load, then exit the game and run in normal mode.", EnumChatType.Notification);
			client.Player.WorldData.CurrentGameMode = EnumGameMode.Spectator;
			client.Player.WorldData.NoClip = true;
			client.Player.WorldData.FreeMove = true;
			client.Player.WorldData.MoveSpeedMultiplier = 1f;
			broadCastModeChange(client.Player);
		}
		SendRoles(player);
	}

	public void SendRoles(IServerPlayer player)
	{
		Packet_Roles packet_Roles = new Packet_Roles();
		packet_Roles.SetRoles(Config.RolesByCode.Select((KeyValuePair<string, PlayerRole> val) => new Packet_Role
		{
			Code = val.Value.Code,
			PrivilegeLevel = val.Value.PrivilegeLevel
		}).ToArray());
		SendPacket(player, new Packet_Server
		{
			Id = 76,
			Roles = packet_Roles
		});
	}

	public void BroadcastRoles()
	{
		Packet_Roles packet_Roles = new Packet_Roles();
		packet_Roles.SetRoles(Config.RolesByCode.Select((KeyValuePair<string, PlayerRole> val) => new Packet_Role
		{
			Code = val.Value.Code,
			PrivilegeLevel = val.Value.PrivilegeLevel
		}).ToArray());
		BroadcastPacket(new Packet_Server
		{
			Id = 76,
			Roles = packet_Roles
		});
	}

	public void broadCastModeChange(IServerPlayer player)
	{
		BroadcastPacket(new Packet_Server
		{
			Id = 46,
			ModeChange = new Packet_PlayerMode
			{
				PlayerUID = player.PlayerUID,
				FreeMove = (player.WorldData.FreeMove ? 1 : 0),
				GameMode = (int)player.WorldData.CurrentGameMode,
				MoveSpeed = CollectibleNet.SerializeFloat(player.WorldData.MoveSpeedMultiplier),
				NoClip = (player.WorldData.NoClip ? 1 : 0),
				ViewDistance = player.WorldData.LastApprovedViewDistance,
				PickingRange = CollectibleNet.SerializeFloat(player.WorldData.PickingRange),
				FreeMovePlaneLock = (int)player.WorldData.FreeMovePlaneLock
			}
		});
	}

	private void HandlePlayerReady(Packet_Client packet, ConnectedClient client)
	{
		client.LastActivityTotalMs = ElapsedMilliseconds;
		client.State = EnumClientState.Playing;
		WorldMap.SendClaims(client.Player, WorldMap.All, null);
		EventManager.TriggerPlayerReady(client.Player);
	}

	private void HandleRequestModeChange(Packet_Client p, ConnectedClient client)
	{
		Packet_PlayerMode requestModeChange = p.RequestModeChange;
		int id = client.Id;
		string playerUID = requestModeChange.PlayerUID;
		ConnectedClient clientByUID = GetClientByUID(playerUID);
		if (client.Player == null)
		{
			Logger.Notification("Mode change request from a player without player object?! Ignoring.");
			return;
		}
		if (clientByUID == null)
		{
			ReplyMessage(client.Player, "No such target client found.", EnumChatType.CommandError);
			return;
		}
		ServerWorldPlayerData worldData = clientByUID.WorldData;
		worldData.DesiredViewDistance = requestModeChange.ViewDistance;
		if (worldData.Viewdistance != requestModeChange.ViewDistance)
		{
			Logger.Notification("Player {0} requested new view distance: {1}", client.PlayerName, requestModeChange.ViewDistance);
			if (clientByUID.IsSinglePlayerClient)
			{
				Config.MaxChunkRadius = Math.Max(Config.MaxChunkRadius, requestModeChange.ViewDistance / 32);
				Logger.Notification($"Upped server view distance to: {requestModeChange.ViewDistance}, because player is in singleplayer");
			}
		}
		worldData.Viewdistance = requestModeChange.ViewDistance;
		bool flag;
		bool flag2;
		bool flag3;
		if (playerUID != client.WorldData.PlayerUID)
		{
			flag = PlayerHasPrivilege(id, Privilege.freemove) && PlayerHasPrivilege(id, Privilege.commandplayer);
			flag2 = PlayerHasPrivilege(id, Privilege.gamemode) && PlayerHasPrivilege(id, Privilege.commandplayer);
			flag3 = PlayerHasPrivilege(id, Privilege.pickingrange) && PlayerHasPrivilege(id, Privilege.commandplayer);
		}
		else
		{
			flag = PlayerHasPrivilege(id, Privilege.freemove);
			flag2 = PlayerHasPrivilege(id, Privilege.gamemode);
			flag3 = PlayerHasPrivilege(id, Privilege.pickingrange);
		}
		if (flag)
		{
			worldData.FreeMove = requestModeChange.FreeMove > 0;
			worldData.NoClip = requestModeChange.NoClip > 0;
			worldData.MoveSpeedMultiplier = CollectibleNet.DeserializeFloat(requestModeChange.MoveSpeed);
			try
			{
				worldData.FreeMovePlaneLock = (EnumFreeMovAxisLock)requestModeChange.FreeMovePlaneLock;
			}
			catch (Exception)
			{
			}
		}
		else if (((requestModeChange.FreeMove > 0) ^ worldData.FreeMove) || (worldData.NoClip ^ (requestModeChange.NoClip > 0)) || worldData.MoveSpeedMultiplier != CollectibleNet.DeserializeFloat(requestModeChange.MoveSpeed))
		{
			ReplyMessage(client.Player, "Not allowed to change fly mode, noclip or move speed. Missing privilege or not allowed in this world.", EnumChatType.CommandError);
		}
		EnumGameMode enumGameMode = EnumGameMode.Guest;
		try
		{
			enumGameMode = (EnumGameMode)requestModeChange.GameMode;
		}
		catch (Exception)
		{
		}
		if (flag2)
		{
			EnumGameMode gameMode = worldData.GameMode;
			worldData.GameMode = enumGameMode;
			if (gameMode != enumGameMode)
			{
				for (int i = 0; i < Systems.Length; i++)
				{
					Systems[i].OnPlayerSwitchGameMode(clientByUID.Player);
				}
				EventManager.TriggerPlayerChangeGamemode(clientByUID.Player);
				if (enumGameMode == EnumGameMode.Guest || enumGameMode == EnumGameMode.Survival)
				{
					worldData.MoveSpeedMultiplier = 1f;
				}
			}
		}
		else if (worldData.GameMode != enumGameMode)
		{
			ReplyMessage(client.Player, "Not allowed to change game mode. Missing privilege or not allowed in this world.", EnumChatType.CommandError);
		}
		if (flag3)
		{
			worldData.PickingRange = CollectibleNet.DeserializeFloat(requestModeChange.PickingRange);
		}
		else if (worldData.PickingRange != CollectibleNet.DeserializeFloat(requestModeChange.PickingRange))
		{
			ReplyMessage(client.Player, "Not allowed to change picking range. Missing privilege or not allowed in this world.", EnumChatType.CommandError);
		}
		bool flag4 = worldData.GameMode == EnumGameMode.Creative || worldData.GameMode == EnumGameMode.Spectator;
		worldData.FreeMove = (worldData.FreeMove & flag4) || worldData.GameMode == EnumGameMode.Spectator;
		worldData.NoClip &= flag4;
		worldData.RenderMetaBlocks = requestModeChange.RenderMetaBlocks > 0;
		clientByUID.Entityplayer.Controls.IsFlying = worldData.FreeMove || clientByUID.Entityplayer.Controls.Gliding;
		clientByUID.Entityplayer.Controls.MovespeedMultiplier = worldData.MoveSpeedMultiplier;
		BroadcastPacket(new Packet_Server
		{
			Id = 46,
			ModeChange = new Packet_PlayerMode
			{
				PlayerUID = playerUID,
				FreeMove = (worldData.FreeMove ? 1 : 0),
				GameMode = (int)worldData.GameMode,
				MoveSpeed = CollectibleNet.SerializeFloat(worldData.MoveSpeedMultiplier),
				NoClip = (worldData.NoClip ? 1 : 0),
				ViewDistance = worldData.LastApprovedViewDistance,
				PickingRange = CollectibleNet.SerializeFloat(worldData.PickingRange),
				FreeMovePlaneLock = (int)worldData.FreeMovePlaneLock
			}
		});
		clientByUID.Player.Entity.UpdatePartitioning();
		clientByUID.Player.Entity.Controls.NoClip = worldData.NoClip;
	}

	private void HandleChatLine(Packet_Client packet, ConnectedClient client)
	{
		string message = packet.Chatline.Message.Trim();
		int num = packet.Chatline.Groupid;
		if (num < -1)
		{
			num = 0;
		}
		HandleChatMessage(client.Player, num, message);
	}

	public void BroadcastHotbarSlot(IServerPlayer ofPlayer, bool skipSelf = true)
	{
		IServerPlayer[] skipPlayers = ((!skipSelf) ? Array.Empty<IServerPlayer>() : new IServerPlayer[1] { ofPlayer });
		if (ofPlayer.InventoryManager?.ActiveHotbarSlot == null)
		{
			if (ofPlayer.InventoryManager == null)
			{
				Logger.Error("BroadcastHotbarSlot: InventoryManager is null?! Ignoring.");
			}
			else
			{
				Logger.Error("BroadcastHotbarSlot: ActiveHotbarSlot is null?! Ignoring.");
			}
			return;
		}
		ItemStack itemstack = ofPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
		ItemStack itemStack = ofPlayer.Entity?.LeftHandItemSlot?.Itemstack;
		BroadcastPacket(new Packet_Server
		{
			SelectedHotbarSlot = new Packet_SelectedHotbarSlot
			{
				ClientId = ofPlayer.ClientId,
				SlotNumber = ofPlayer.InventoryManager.ActiveHotbarSlotNumber,
				Itemstack = ((itemstack == null) ? null : StackConverter.ToPacket(itemstack)),
				OffhandStack = ((itemStack == null) ? null : StackConverter.ToPacket(itemStack))
			},
			Id = 53
		}, skipPlayers);
	}

	private void HandleLeave(Packet_Client packet, ConnectedClient client)
	{
		DisconnectPlayer(client, (packet.Leave.Reason == 1) ? Lang.Get("The Players client crashed") : null);
	}

	private void HandleMoveKeyChange(Packet_Client packet, ConnectedClient client)
	{
		EntityControls entityControls = ((client.Entityplayer.MountedOn == null) ? client.Entityplayer.Controls : client.Entityplayer.MountedOn.Controls);
		if (entityControls != null)
		{
			client.previousControls.SetFrom(entityControls);
			entityControls.UpdateFromPacket(packet.MoveKeyChange.Down > 0, packet.MoveKeyChange.Key);
			if (client.previousControls.ToInt() != entityControls.ToInt())
			{
				client.LastActivityTotalMs = ElapsedMilliseconds;
				entityControls.Dirty = true;
				client.Player.TriggerInWorldAction((EnumEntityAction)packet.MoveKeyChange.Key, packet.MoveKeyChange.Down > 0);
			}
		}
	}

	private void HandleEntityPacket(Packet_Client packet, ConnectedClient client)
	{
		Packet_EntityPacket entityPacket = packet.EntityPacket;
		if (LoadedEntities.TryGetValue(entityPacket.EntityId, out var value))
		{
			value.OnReceivedClientPacket(client.Player, entityPacket.Packetid, entityPacket.Data);
		}
	}

	public void HandleChatMessage(IServerPlayer player, int groupid, string message)
	{
		if (groupid > 0 && !PlayerDataManager.PlayerGroupsById.ContainsKey(groupid))
		{
			SendMessage(player, GlobalConstants.ServerInfoChatGroup, "No such group exists on this server.", EnumChatType.CommandError);
		}
		else
		{
			if (string.IsNullOrEmpty(message))
			{
				return;
			}
			if (message.StartsWith('/'))
			{
				string text = message.Split(new char[1] { ' ' })[0].Replace("/", "");
				text = text.ToLowerInvariant();
				string args = ((message.IndexOf(' ') < 0) ? "" : message.Substring(message.IndexOf(' ') + 1));
				api.commandapi.Execute(text, player, groupid, args);
			}
			else
			{
				if (message.StartsWith('.'))
				{
					return;
				}
				if (!player.HasPrivilege(Privilege.chat))
				{
					SendMessage(player, groupid, Lang.Get("No privilege to chat"), EnumChatType.CommandError);
					return;
				}
				if (ElapsedMilliseconds - Clients[player.ClientId].LastChatMessageTotalMs < Config.ChatRateLimitMs)
				{
					SendMessage(player, groupid, Lang.Get("Chat not sent. Rate limited to 1 chat message per {0} seconds", (float)Config.ChatRateLimitMs / 1000f), EnumChatType.CommandError);
					return;
				}
				Clients[player.ClientId].LastChatMessageTotalMs = ElapsedMilliseconds;
				message = message.Replace(">", "&gt;").Replace("<", "&lt;");
				string data = "from: " + player.Entity.EntityId + ",withoutPrefix:" + message;
				string text2 = message;
				BoolRef boolRef = new BoolRef();
				EventManager.TriggerOnplayerChat(player, groupid, ref message, ref data, boolRef);
				if (!boolRef.value)
				{
					SendMessageToGroup(groupid, message, EnumChatType.OthersMessage, player, data);
					player.SendMessage(groupid, message, EnumChatType.OwnMessage, data);
					Logger.Chat($"{groupid} | {player.PlayerName}: {text2.Replace("{", "{{").Replace("}", "}}")}");
				}
			}
		}
	}

	private void HandleQueryClientPacket(ConnectedClient client, Packet_Client packet)
	{
		if (packet.Id == 33)
		{
			if (Config.LoginFloodProtection)
			{
				int hashCode = client.Socket.RemoteEndPoint().Address.GetHashCode();
				int tickCount = Environment.TickCount;
				if (RecentClientLogins.TryGetValue(hashCode, out var value))
				{
					if (tickCount - value.LastTickCount < 500 && tickCount - value.LastTickCount >= 0)
					{
						value.LastTickCount = tickCount;
						value.Times++;
						RecentClientLogins[hashCode] = value;
						if (Config.TemporaryIpBlockList && value.Times > 50)
						{
							string text = client.Socket.RemoteEndPoint().Address.ToString();
							TcpNetConnection.blockedIps.Add(text);
							Logger.Notification($"Client {client.Id} | {text} send too many request. Adding to blocked IP's");
						}
						DisconnectPlayer(client, "Too many requests", "Your client is sending too many requests");
						return;
					}
					value.LastTickCount = tickCount;
					value.Times = 0;
					RecentClientLogins[hashCode] = value;
				}
				else
				{
					RecentClientLogins[hashCode] = new ClientLastLogin
					{
						LastTickCount = tickCount,
						Times = 1
					};
				}
			}
			client.LoginToken = Guid.NewGuid().ToString();
			ServerUdpNetwork.connectingClients.Add(client.LoginToken, client);
			SendPacket(client.Id, new Packet_Server
			{
				Id = 77,
				Token = new Packet_LoginTokenAnswer
				{
					Token = client.LoginToken
				}
			});
		}
		else
		{
			DisconnectPlayer(client, "", "Query complete");
		}
	}

	private void HandlePlayerIdentification(Packet_Client p, ConnectedClient client)
	{
		Packet_ClientIdentification identification = p.Identification;
		if (identification == null)
		{
			DisconnectPlayer(client, null, Lang.Get("Invalid join data!"));
			return;
		}
		if ("1.22.6" != identification.NetworkVersion)
		{
			DisconnectPlayer(client, null, Lang.Get("disconnect-wrongversion", identification.ShortGameVersion, identification.NetworkVersion, "1.22.7", "1.22.6"));
			return;
		}
		if (!client.IsSinglePlayerClient && Config.IsPasswordProtected() && identification.ServerPassword != Config.Password)
		{
			Logger.Event($"{identification.Playername} fails to join (invalid server password).");
			DisconnectPlayer(client, null, Lang.Get("Password is invalid"));
			return;
		}
		if ((Config.WhitelistMode == EnumWhitelistMode.On || (Config.WhitelistMode == EnumWhitelistMode.Default && IsDedicatedServer)) && !client.IsLocalConnection)
		{
			PlayerEntry playerWhitelist = PlayerDataManager.GetPlayerWhitelist(identification.Playername, identification.PlayerUID);
			if (playerWhitelist == null)
			{
				DisconnectPlayer(client, null, "This server only allows whitelisted players to join. You are not on the whitelist.");
				return;
			}
			if (playerWhitelist.UntilDate < DateTime.Now)
			{
				DisconnectPlayer(client, null, "This server only allows whitelisted players to join. Your whitelist entry has expired.");
				return;
			}
		}
		if (identification.Playername == null || identification.PlayerUID == null)
		{
			client.IsNewClient = true;
			Logger.Event($"{identification.Playername} fails to join (player name or playeruid null value sent).");
			DisconnectPlayer(client, null, "Invalid join data");
		}
		PlayerEntry playerBan = PlayerDataManager.GetPlayerBan(identification.Playername, identification.PlayerUID);
		if (playerBan != null && playerBan.UntilDate > DateTime.Now)
		{
			Logger.Event($"{identification.Playername} fails to join (banned).");
			DisconnectPlayer(client, null, Lang.Get("banned-until-reason", playerBan.IssuedByPlayerName, playerBan.UntilDate, playerBan.Reason));
			return;
		}
		client.SentPlayerUid = identification.PlayerUID;
		Logger.Notification("Client {0} uid {1} attempting identification. Name: {2}", client.Id, identification.PlayerUID, identification.Playername);
		string playername = identification.Playername;
		Regex regex = new Regex("^(\\w|-){1,16}$");
		if (string.IsNullOrEmpty(playername) || !regex.IsMatch(playername))
		{
			Logger.Event($"{client.Socket.RemoteEndPoint()} can't join (invalid Playername: {playername}).");
			DisconnectPlayer(client, null, Lang.Get("Your playername contains not allowed characters or is not set. Are you using a hacked client?"));
			return;
		}
		if (client.IsSinglePlayerClient || !Config.VerifyPlayerAuth)
		{
			string entitlements = (client.IsSinglePlayerClient ? GlobalConstants.SinglePlayerEntitlements : null);
			PreFinalizePlayerIdentification(identification, client, entitlements);
			return;
		}
		ServerPlayerData serverPlayerData = PlayerDataManager.GetServerPlayerData(identification.PlayerUID);
		if ((serverPlayerData == null || (!serverPlayerData.HasPrivilege(Privilege.controlserver, Config.RolesByCode) && !serverPlayerData.HasPrivilege("ignoremaxclients", Config.RolesByCode))) && Enumerable.Count(Clients, (KeyValuePair<int, ConnectedClient> c) => c.Value.State.IsAdmitted()) >= Config.MaxClients && ConnectionQueue.Count >= Config.MaxClientsInQueue)
		{
			DisconnectPlayer(client, null, Lang.Get("Server and Connection queue is full ({0} max clients, {1} queue size). Please try again later.", Config.MaxClients, Config.MaxClientsInQueue));
		}
		VerifyPlayerWithAuthServer(identification, client);
	}

	public ServerMain(StartServerArgs serverargs, string[] cmdlineArgsRaw, ServerProgramArgs progArgs, bool isDedicatedServer = true)
	{
		IsDedicatedServer = isDedicatedServer;
		if (Logger == null)
		{
			Logger = new ServerLogger(progArgs);
		}
		serverStartArgs = serverargs;
		_consoleThreadsCts = new CancellationTokenSource();
		ServerThreadsCts = new CancellationTokenSource();
		WildcardUtil.ClearRegexCache();
		CollectibleTagRegistry = new ConcurrentTagRegistry(Logger, "Server Collectibles");
		EntityTagRegistry = new ConcurrentTagRegistryFast(Logger, "Server Entities");
		GenericComplexConditionConverter.StaticInit(CollectibleTagRegistry, EntityTagRegistry);
		CollectibleTagSetConverter.StaticInit(CollectibleTagRegistry);
		EntityTagSetConverter.StaticInit(EntityTagRegistry);
		Logger.TraceLog = progArgs.TraceLog;
		RawCmdLineArgs = cmdlineArgsRaw;
		this.progArgs = progArgs;
		if (progArgs.SetConfigAndExit != null)
		{
			string path = "serverconfig.json";
			if (!File.Exists(Path.Combine(GamePaths.Config, path)))
			{
				Logger?.Notification("serverconfig.json not found, creating new one");
				ServerSystemLoadConfig.GenerateConfig(this);
			}
			else
			{
				ServerSystemLoadConfig.LoadConfig(this);
			}
			JObject obj = JToken.Parse(progArgs.SetConfigAndExit) as JObject;
			JObject jObject = JToken.FromObject(Config) as JObject;
			foreach (KeyValuePair<string, JToken> item in obj)
			{
				JToken jToken = jObject[item.Key];
				if (jToken == null)
				{
					Logger?.Notification("No such setting '" + item.Key + "'. Ignoring.");
					ExitCode = 404;
					return;
				}
				if (jToken is JObject jObject2)
				{
					jObject2.Merge(item.Value);
					Logger?.Notification("Ok, values merged for {0}.", item.Key);
				}
				else
				{
					jObject[item.Key] = item.Value;
					Logger?.Notification("Ok, value {0} set for {1}.", item.Value, item.Key);
				}
			}
			try
			{
				Config = jObject.ToObject<ServerConfig>();
			}
			catch (Exception ex)
			{
				Logger?.Notification("Failed saving config, you are likely suppling an incorrect value type (e.g. a number for a boolean setting). See server-debug.log for exception.");
				Logger?.VerboseDebug("Failed saving config from --setConfig. Exception:");
				Logger?.VerboseDebug(LoggerBase.CleanStackTrace(ex.ToString()));
				ExitCode = 500;
				return;
			}
			ExitCode = 200;
			ServerSystemLoadConfig.SaveConfig(this);
			Logger?.Dispose();
			return;
		}
		if (progArgs.GenConfigAndExit)
		{
			ServerSystemLoadConfig.GenerateConfig(this);
			ServerSystemLoadConfig.SaveConfig(this);
			if (Logger != null)
			{
				Logger.Notification("Config generated.");
				Logger.Dispose();
			}
			return;
		}
		if (progArgs.ReducedThreads)
		{
			ReducedServerThreads = true;
		}
		ServerConsoleClient = new ServerConsoleClient(serverConsoleId)
		{
			FallbackPlayerName = "Admin",
			IsNewClient = false
		};
		ServerConsoleClient.WorldData = new ServerWorldPlayerData
		{
			PlayerUID = "Admin"
		};
		FrameProfiler = new FrameProfilerUtil(Logger.Notification);
		AnimatorBase.logAntiSpam.Clear();
		if (IsDedicatedServer)
		{
			serverConsole = new ServerConsole(this, _consoleThreadsCts.Token);
		}
		foreach (Thread serverthread in Serverthreads)
		{
			serverthread?.Start();
		}
		totalUpTime.Start();
	}

	private Thread CreateThread(string name, ServerSystem[] serversystems, CancellationToken cancellationToken)
	{
		ServerThread serverThread = new ServerThread(this, name, cancellationToken);
		ServerThreadLoops.Add(serverThread);
		serverThread.serversystems = serversystems;
		return TyronThreadPool.CreateDedicatedThread(serverThread.Process, name);
	}

	public Thread CreateBackgroundThread(string name, ThreadStart starter)
	{
		Thread thread = TyronThreadPool.CreateDedicatedThread(starter, name);
		thread.Priority = Thread.CurrentThread.Priority;
		Serverthreads.Add(thread);
		return thread;
	}

	public void AddServerThread(string name, IAsyncServerSystem modsystem)
	{
		ServerSystem serverSystem = new ServerSystemAsync(this, name, modsystem);
		Thread thread = CreateThread(name, new ServerSystem[1] { serverSystem }, ServerThreadsCts.Token);
		Serverthreads.Add(thread);
		Array.Resize(ref Systems, Systems.Length + 1);
		Systems[Systems.Length - 1] = serverSystem;
		if (RunPhase >= EnumServerRunPhase.RunGame)
		{
			thread.Start();
		}
	}

	public void PreLaunch()
	{
		if (!ReducedServerThreads)
		{
			ClientPacketParsingThread = CreateBackgroundThread("clientPacketsParser", new ClientPacketParserOffthread(this).Start);
		}
	}

	public void StandbyLaunch()
	{
		MainSockets[1] = new TcpNetServer();
		UdpSockets[1] = new UdpNetServer(Clients);
		ServerSystemLoadConfig.EnsureConfigExists(this);
		ServerSystemLoadConfig.LoadConfig(this);
		startSockets();
		if (HeartbeatSystem == null)
		{
			HeartbeatSystem = new ServerSystemHeartbeat(this);
		}
		HeartbeatSystem.StandbyLaunch();
		Logger.Event("Server launched in standby mode. Full launch will commence on first connection attempt. Only /stop and /stats commands will be functioning");
	}

	public void Launch()
	{
		loadedChunksLock = new FastRWLock(this);
		serverChunkDataPool = new ChunkDataPool(MagicNum.ServerChunkSize, this);
		InitBasicPacketHandlers();
		RuntimeEnv.ServerMainThreadId = Environment.CurrentManagedThreadId;
		ModEventManager = new ServerEventManager(this);
		EventManager = new CoreServerEventManager(this, ModEventManager);
		PlayerDataManager = new PlayerDataManager(this);
		ServerSystemModHandler serverSystemModHandler = new ServerSystemModHandler(this);
		EnterRunPhase(EnumServerRunPhase.Start);
		ServerSystemCompressChunks serverSystemCompressChunks = new ServerSystemCompressChunks(this);
		ServerSystemRelight serverSystemRelight = new ServerSystemRelight(this);
		chunkThread = new ChunkServerThread(this, "chunkdbthread", ServerThreadsCts.Token);
		ServerThreadLoops.Add(chunkThread);
		ServerSystemSupplyChunkCommands serverSystemSupplyChunkCommands = new ServerSystemSupplyChunkCommands(this, chunkThread);
		ChunkServerThread chunkServerThread = chunkThread;
		ServerSystem[] array = new ServerSystem[3];
		ServerSystemSupplyChunks serverSystemSupplyChunks = (ServerSystemSupplyChunks)(array[0] = new ServerSystemSupplyChunks(this, chunkThread));
		ServerSystemLoadAndSaveGame serverSystemLoadAndSaveGame = (ServerSystemLoadAndSaveGame)(array[1] = new ServerSystemLoadAndSaveGame(this, chunkThread));
		ServerSystemUnloadChunks serverSystemUnloadChunks = (ServerSystemUnloadChunks)(array[2] = new ServerSystemUnloadChunks(this, chunkThread));
		chunkServerThread.serversystems = array;
		Thread thread = new Thread(chunkThread.Process);
		thread.Name = "chunkdbthread";
		thread.IsBackground = true;
		ServerSystemBlockSimulation serverSystemBlockSimulation = new ServerSystemBlockSimulation(this);
		ServerUdpNetwork = new ServerUdpNetwork(this);
		Thread thread2 = new Thread(new ServerUdpQueue(this, ServerUdpNetwork).DedicatedThreadLoop);
		thread2.Name = "UdpSending";
		thread2.IsBackground = true;
		Serverthreads.AddRange(new Thread[5]
		{
			thread,
			CreateThread("CompressChunks", new ServerSystem[1] { serverSystemCompressChunks }, ServerThreadsCts.Token),
			CreateThread("Relight", new ServerSystem[1] { serverSystemRelight }, ServerThreadsCts.Token),
			CreateThread("ServerBlockTicks", new ServerSystem[1] { serverSystemBlockSimulation }, ServerThreadsCts.Token),
			thread2
		});
		if (HeartbeatSystem == null)
		{
			HeartbeatSystem = new ServerSystemHeartbeat(this);
		}
		HeartbeatSystem.Launch();
		Systems = new ServerSystem[33]
		{
			new ServerSystemUpnp(this),
			(clientAwarenessSystem = new ServerSystemClientAwareness(this)),
			new ServerSystemLoadConfig(this),
			new ServerSystemNotifyPing(this),
			new ServerSystemKickAfkPlayers(this),
			new PlayerAntiAbuseMonitor(this),
			serverSystemModHandler,
			new ServerySystemPlayerGroups(this),
			new ServerSystemEntitySimulation(this),
			new ServerSystemCalendar(this),
			new ServerSystemCommands(this),
			new CmdPlayer(this),
			new ServerSystemInventory(this),
			new ServerSystemAutoSaveGame(this),
			serverSystemCompressChunks,
			serverSystemSupplyChunks,
			serverSystemSupplyChunkCommands,
			serverSystemRelight,
			new ServerSystemSendChunks(this),
			serverSystemUnloadChunks,
			new ServerSystemRemapperAssistant(this),
			new ServerSystemBlockIdRemapper(this),
			new ServerSystemItemIdRemapper(this),
			new ServerSystemEntityCodeRemapper(this),
			new ServerSystemMacros(this),
			new ServerSystemEntitySpawner(this),
			new ServerSystemWorldAmbient(this),
			HeartbeatSystem,
			serverSystemLoadAndSaveGame,
			serverSystemBlockSimulation,
			ServerUdpNetwork,
			new ServerSystemBlockLogger(this),
			new ServerSystemMonitor(this)
		};
		if (xPlatInterface == null)
		{
			xPlatInterface = XPlatformInterfaces.GetInterface();
		}
		Logger.StoryEvent(Lang.Get("It begins..."));
		Logger.Event("Launching server...");
		PlayerDataManager.Load();
		Logger.StoryEvent(Lang.Get("It senses..."));
		Logger.Event("Server v1.22.7, network v1.22.6, api v1.22.0");
		totalUnpausedTime.Start();
		AssetManager = new AssetManager(GamePaths.AssetsPath, EnumAppSide.Server);
		if (progArgs.AddOrigin != null)
		{
			foreach (string item in progArgs.AddOrigin)
			{
				string[] directories = Directory.GetDirectories(item);
				for (int i = 0; i < directories.Length; i++)
				{
					string name = new DirectoryInfo(directories[i]).Name;
					AssetManager.CustomAppOrigins.Add(new PathOrigin(name, directories[i]));
				}
			}
		}
		EnterRunPhase(EnumServerRunPhase.Initialization);
		WorldMap = new ServerWorldMap(this);
		Logger.Event("Loading configuration...");
		EnterRunPhase(EnumServerRunPhase.Configuration);
		if (exitState.Exit)
		{
			return;
		}
		AfterConfigLoaded();
		LoadAssets();
		Logger.Event("Building assets...");
		EnterRunPhase(EnumServerRunPhase.LoadAssets);
		if (exitState.Exit)
		{
			return;
		}
		if (AssetManager.TryGet("blocktypes/plant/reedpapyrus-free.json", loadAsset: false) != null)
		{
			string message = (Standalone ? "blocktypes/plant/reedpapyrus-free.json file detected, which breaks stuff. That means this is an incorrectly updated 1.16 server! When up update a server, make sure to delete the old server installation files (but keep the data folder)" : "blocktypes/plant/reedpapyrus-free.json file detected, which breaks the game. Possible corrupted installation. Please uninstall the game, delete the folder %appdata%/VintageStory, then reinstall.");
			Logger.Fatal(message);
			throw new ApplicationException(message);
		}
		CollectibleTagRegistry.Lock();
		EntityTagRegistry.Lock();
		Logger.Notification("Tags registry was locked.");
		FinalizeAssets();
		Logger.Event("Server assets loaded, parsed, registered and finalized");
		Logger.Event("Initialising systems...");
		EnterRunPhase(EnumServerRunPhase.LoadGamePre);
		if (exitState.Exit)
		{
			return;
		}
		AfterSaveGameLoaded();
		if (exitState.Exit)
		{
			return;
		}
		Logger.StoryEvent(Lang.Get("A world unbroken..."));
		EnterRunPhase(EnumServerRunPhase.GameReady);
		if (exitState.Exit)
		{
			return;
		}
		EnterRunPhase(EnumServerRunPhase.WorldReady);
		if (exitState.Exit)
		{
			return;
		}
		StartBuildServerAssetsPacket();
		Logger.StoryEvent(Lang.Get("The center unfolding..."));
		Logger.Event("Starting world generators...");
		ModEventManager.TriggerWorldgenStartup();
		if (exitState.Exit)
		{
			return;
		}
		Logger.Event("Begin game ticking...");
		Logger.StoryEvent(Lang.Get("...and calls to you."));
		EnterRunPhase(EnumServerRunPhase.RunGame);
		if (exitState.Exit)
		{
			return;
		}
		Logger.Notification("Starting server threads");
		foreach (Thread serverthread in Serverthreads)
		{
			serverthread.TryStart();
		}
		bool flag = IsDedicatedServer || MainSockets[1] != null;
		string text;
		if (IsDedicatedServer)
		{
			text = Lang.Get("Dedicated Server");
		}
		else
		{
			text = (flag ? Lang.Get("Threaded Server") : Lang.Get("Singleplayer Server"));
		}
		string text2;
		if (flag)
		{
			text2 = ((CurrentIp == null) ? Lang.Get(" on Port {0} and all ips", CurrentPort) : Lang.Get(" on Port {0} and ip {1}", CurrentPort, CurrentIp));
		}
		else
		{
			text2 = "";
		}
		Logger.Event("{0} now running{1}!", text, text2);
		Logger.StoryEvent(Lang.Get("Return again."));
		if ((Config.WhitelistMode == EnumWhitelistMode.Default || Config.WhitelistMode == EnumWhitelistMode.On) && !Config.AdvertiseServer)
		{
			Logger.Notification("Please be aware that as of 1.20, servers default configurations have changed - servers no longer register themselves to the public servers list and are invite-only (whitelisted) out of the box. If you desire so, you can enable server advertising with '/serverconfig advertise on' and disable the whitelist mode with '/serverconfig whitelistmode off'");
		}
		AssetManager.UnloadUnpatchedAssets();
	}

	internal void EnterRunPhase(EnumServerRunPhase runPhase)
	{
		RunPhase = runPhase;
		if (runPhase == EnumServerRunPhase.Start || runPhase == EnumServerRunPhase.Exit)
		{
			return;
		}
		Logger.Notification("Entering runphase " + runPhase);
		Logger.VerboseDebug("Entering runphase " + runPhase);
		ServerSystem[] systems = Systems;
		foreach (ServerSystem serverSystem in systems)
		{
			switch (runPhase)
			{
			case EnumServerRunPhase.Initialization:
				suspended = true;
				serverSystem.OnBeginInitialization();
				break;
			case EnumServerRunPhase.Configuration:
				serverSystem.OnBeginConfiguration();
				break;
			case EnumServerRunPhase.LoadAssets:
				serverSystem.OnLoadAssets();
				break;
			case EnumServerRunPhase.AssetsFinalize:
				serverSystem.OnFinalizeAssets();
				break;
			case EnumServerRunPhase.LoadGamePre:
				serverSystem.OnBeginModsAndConfigReady();
				break;
			case EnumServerRunPhase.GameReady:
				serverSystem.OnBeginGameReady(SaveGameData);
				break;
			case EnumServerRunPhase.WorldReady:
				serverSystem.OnBeginWorldReady();
				break;
			case EnumServerRunPhase.RunGame:
				suspended = false;
				serverSystem.OnBeginRunGame();
				break;
			case EnumServerRunPhase.Shutdown:
				serverSystem.OnBeginShutdown();
				break;
			}
		}
	}

	public void AfterConfigLoaded()
	{
		ServerConsoleClient.Player = new ServerConsolePlayer(this, ServerConsoleClient.WorldData);
		if (IsDedicatedServer && MainSockets[1] == null && UdpSockets[1] == null)
		{
			MainSockets[1] = new TcpNetServer();
			UdpSockets[1] = new UdpNetServer(Clients);
			startSockets();
		}
		string[] array = Privilege.AllCodes();
		for (int i = 0; i < array.Length; i++)
		{
			AllPrivileges.Add(array[i]);
			PrivilegeDescriptions.Add(array[i], array[i]);
		}
	}

	private void FinalizeAssets()
	{
		foreach (EntityProperties entityType in EntityTypes)
		{
			BlockDropItemStack[] array = entityType.Drops;
			if (array == null)
			{
				continue;
			}
			for (int i = 0; i < array.Length; i++)
			{
				if (!array[i].Resolve(this, "Entity ", entityType.Code))
				{
					array = (entityType.Drops = array.RemoveAt(i));
					i--;
				}
			}
		}
		ModEventManager.TriggerFinalizeAssets();
		EnterRunPhase(EnumServerRunPhase.AssetsFinalize);
	}

	private void AfterSaveGameLoaded()
	{
		WorldMap.Init(SaveGameData.MapSizeX, SaveGameData.MapSizeY, SaveGameData.MapSizeZ);
		Logger.Notification("Server map set");
		if (MainSockets[1] == null)
		{
			startSockets();
		}
		PlayerRole playerRole = new PlayerRole
		{
			Name = "Server",
			Code = "server",
			PrivilegeLevel = 9999,
			Privileges = AllPrivileges.ToList(),
			Color = Color.LightSteelBlue
		};
		Config.RolesByCode.Add("server", playerRole);
		ServerConsoleClient.serverdata = new ServerPlayerData();
		ServerConsoleClient.serverdata.SetRole(playerRole);
		Logger.Debug("Unique crafting ingredients count: " + FastSearchRecipesByIngredient.Count);
	}

	private void startSockets()
	{
		if (progArgs.Ip != null)
		{
			CurrentIp = progArgs.Ip;
		}
		else if (Config.Ip != null)
		{
			CurrentIp = Config.Ip;
		}
		if (progArgs.Port != null)
		{
			if (!int.TryParse(progArgs.Port, out CurrentPort))
			{
				CurrentPort = Config.Port;
			}
		}
		else
		{
			CurrentPort = Config.Port;
		}
		if (!ReducedServerThreads)
		{
			ClientPacketParsingThread.TryStart();
		}
		MainSockets[1]?.SetIpAndPort(CurrentIp, CurrentPort);
		MainSockets[1]?.Start();
		UdpSockets[1]?.SetIpAndPort(CurrentIp, CurrentPort);
		UdpSockets[1]?.Start();
	}

	public void Process()
	{
		TickPosition = 0;
		if (Suspended)
		{
			Thread.Sleep(2);
			return;
		}
		if (RunPhase == EnumServerRunPhase.Standby)
		{
			ProcessMain();
			Thread.Sleep(5);
			return;
		}
		FrameProfiler.Begin("{0} players online - ", Clients.Count - ConnectionQueue.Count);
		TickPosition++;
		ServerThread.SleepMs = ((!Clients.IsEmpty) ? 2 : ((int)Config.TickTime));
		lastFramePassedTime.Restart();
		TickPosition++;
		try
		{
			long elapsedMilliseconds = totalUnpausedTime.ElapsedMilliseconds;
			if (FrameProfiler.Enabled)
			{
				for (int i = 0; i < Systems.Length; i++)
				{
					ServerSystem serverSystem = Systems[i];
					long num = elapsedMilliseconds - serverSystem.millisecondsSinceStart;
					if (num > serverSystem.GetUpdateInterval())
					{
						serverSystem.millisecondsSinceStart = elapsedMilliseconds;
						serverSystem.OnServerTick((float)num / 1000f);
						FrameProfiler.Mark(serverSystem.FrameprofilerName);
					}
					TickPosition++;
				}
				FrameProfiler.Mark("ss-tick");
				EventManager.TriggerGameTickDebug(elapsedMilliseconds, this);
				TickPosition++;
				FrameProfiler.Mark("ev-tick");
				int num2 = 0;
				while (!FrameProfilerUtil.offThreadProfiles.IsEmpty && num2++ < 25)
				{
					FrameProfilerUtil.offThreadProfiles.TryDequeue(out var result);
					Logger.Notification(result);
				}
			}
			else
			{
				for (int j = 0; j < Systems.Length; j++)
				{
					ServerSystem serverSystem2 = Systems[j];
					long num = elapsedMilliseconds - serverSystem2.millisecondsSinceStart;
					if (num > serverSystem2.GetUpdateInterval())
					{
						serverSystem2.millisecondsSinceStart = elapsedMilliseconds;
						serverSystem2.OnServerTick((float)num / 1000f);
					}
					TickPosition++;
				}
				EventManager.TriggerGameTick(elapsedMilliseconds, this);
				TickPosition++;
			}
			ProcessMain();
			TickPosition++;
			if ((DateTime.UtcNow - statsupdate).TotalSeconds >= 2.0)
			{
				processConnectionQueue();
				statsupdate = DateTime.UtcNow;
				StatsCollectorIndex = (StatsCollectorIndex + 1) % 4;
				StatsCollector[StatsCollectorIndex].statTotalPackets = 0;
				StatsCollector[StatsCollectorIndex].statTotalUdpPackets = 0;
				StatsCollector[StatsCollectorIndex].statTotalPacketsLength = 0;
				StatsCollector[StatsCollectorIndex].statTotalUdpPacketsLength = 0;
				StatsCollector[StatsCollectorIndex].tickTimeTotal = 0L;
				StatsCollector[StatsCollectorIndex].ticksTotal = 0L;
				for (int k = 0; k < 10; k++)
				{
					StatsCollector[StatsCollectorIndex].tickTimes[k] = 0L;
				}
			}
			long elapsedMilliseconds2 = lastFramePassedTime.ElapsedMilliseconds;
			StatsCollection statsCollection = StatsCollector[StatsCollectorIndex];
			statsCollection.tickTimeTotal += elapsedMilliseconds2;
			statsCollection.ticksTotal++;
			statsCollection.tickTimes[statsCollection.tickTimeIndex] = elapsedMilliseconds2;
			statsCollection.tickTimeIndex = (statsCollection.tickTimeIndex + 1) % statsCollection.tickTimes.Length;
			if (elapsedMilliseconds2 > 500 && totalUnpausedTime.ElapsedMilliseconds > 5000 && !stopped)
			{
				Logger.Warning("Server overloaded. A tick took {0}ms to complete.", elapsedMilliseconds2);
			}
			FrameProfiler.Mark("timers-updated");
			int num3 = (int)Math.Max(0f, Config.TickTime - (float)elapsedMilliseconds2);
			if (num3 > 0)
			{
				Thread.Sleep(num3);
				FrameProfiler.Mark("sleep");
			}
			TickPosition++;
		}
		catch (Exception e)
		{
			Logger.Fatal(e);
		}
		FrameProfiler.End();
	}

	public void ProcessMain()
	{
		if (MainSockets == null)
		{
			return;
		}
		ProcessMainThreadTasks();
		FrameProfiler.Mark("mtasks");
		if (ReducedServerThreads)
		{
			PacketParsingLoop();
			FrameProfiler.Mark("mt-net-pp");
		}
		ReceivedClientPacket result;
		while (ClientPackets.TryDequeue(out result))
		{
			try
			{
				HandleClientPacket_mainthread(result);
			}
			catch (Exception e)
			{
				if (IsDedicatedServer)
				{
					Logger.Warning("Exception at client " + result.client.Id + ". Disconnecting client.");
					DisconnectPlayer(result.client, "Threw an exception at the server", "An action you (or your client) did caused an unhandled exception");
				}
				Logger.Error(e);
			}
		}
		DisconnectedClientsThisTick.Clear();
		FrameProfiler.Mark("net-read-done");
		TickPosition++;
		foreach (KeyValuePair<Timer, Timer.Tick> timer in Timers)
		{
			timer.Key.Update(timer.Value);
		}
		TickPosition++;
	}

	public bool Suspend(bool newSuspendState, int maxWaitMilliseconds = 60000)
	{
		if (newSuspendState == suspended)
		{
			return true;
		}
		if (maxWaitMilliseconds < 100)
		{
			throw new InvalidOperationException("maxWaitMilliseconds must not be below 100");
		}
		if (Monitor.TryEnter(suspendLock, 10000))
		{
			try
			{
				suspended = newSuspendState;
				if (suspended)
				{
					totalUnpausedTime.Stop();
					ServerSystem[] systems = Systems;
					for (int i = 0; i < systems.Length; i++)
					{
						systems[i].OnServerPause();
					}
					while (maxWaitMilliseconds > 0 && (ServerThreadLoops.Any((ServerThread st) => !st.paused && st.Alive && st.threadName != "ServerConsole") || !api.eventapi.CanSuspendServer()))
					{
						Thread.Sleep(10);
						maxWaitMilliseconds -= 10;
					}
					if (maxWaitMilliseconds <= 0 && suspended)
					{
						Logger.Warning("Server suspend requested, but reached max wait time. A thread is possibly deadlocked/in an endless loop. Will resume again.");
						Logger.Warning("The following threads were not suspended: {0}", string.Join(",", from st in ServerThreadLoops
							where !st.paused && st.Alive && st.threadName != "ServerConsole"
							select st.threadName));
						suspended = false;
						totalUnpausedTime.Start();
						systems = Systems;
						for (int i = 0; i < systems.Length; i++)
						{
							systems[i].OnServerResume();
						}
						api.eventapi.ResumeServer();
						Logger.Notification("Server ticking has been resumed");
						subsequentSuspendFailures++;
						if (subsequentSuspendFailures >= 20)
						{
							Logger.Fatal("Server suspend failed {0} times in a row. This usually indicates an engine bug. In order to prevent data loss, server will stop now.", subsequentSuspendFailures);
							Stop("Server autosave failure", EnumExitMode.SoftExit);
						}
						return false;
					}
					Logger.Notification("Server ticking has been suspended");
				}
				else
				{
					totalUnpausedTime.Start();
					ServerSystem[] systems = Systems;
					for (int i = 0; i < systems.Length; i++)
					{
						systems[i].OnServerResume();
					}
					api.eventapi.ResumeServer();
					Logger.Notification("Server ticking has been resumed");
				}
				bool flag = maxWaitMilliseconds > 0;
				if (flag && suspended)
				{
					subsequentSuspendFailures = 0;
				}
				return flag;
			}
			finally
			{
				Monitor.Exit(suspendLock);
			}
		}
		return false;
	}

	public void AttemptShutdown(string reason, int timeout)
	{
		if (Environment.CurrentManagedThreadId == RuntimeEnv.MainThreadId)
		{
			Stop(reason, EnumExitMode.SoftExit);
			return;
		}
		if (RunPhase == EnumServerRunPhase.RunGame)
		{
			EnqueueMainThreadTask(() =>
			{
				Stop(reason, EnumExitMode.SoftExit);
			});
			for (int num = 0; num < timeout / 15; num++)
			{
				if (stopped)
				{
					return;
				}
				Thread.Sleep(15);
			}
		}
		Stop("Forced: " + reason, EnumExitMode.SoftExit);
	}

	public void Stop(string reason, EnumExitMode exitMode, string finalLogMessage = null, EnumLogType finalLogType = EnumLogType.Notification)
	{
		if (RunPhase == EnumServerRunPhase.Exit || stopped)
		{
			return;
		}
		stopped = true;
		exitState.Mode = exitMode;
		if (FrameProfiler == null)
		{
			FrameProfiler = new FrameProfilerUtil((string message) =>
			{
				Logger.Notification(message);
			});
			FrameProfiler.Begin(null);
		}
		try
		{
			ServerConfig config = Config;
			if (config != null && config.RepairMode)
			{
				foreach (ConnectedClient value in Clients.Values)
				{
					if (value.Player?.WorldData != null)
					{
						value.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
						value.Player.WorldData.FreeMove = false;
						value.Player.WorldData.NoClip = false;
					}
				}
			}
			ConnectedClient[] array = Clients.Values.ToArray();
			foreach (ConnectedClient client in array)
			{
				string text = "Server shutting down - " + reason;
				DisconnectPlayer(client, text, text);
			}
		}
		catch (Exception exception)
		{
			LogShutdownException(exception);
		}
		Logger.Notification("Server stop requested, begin shutdown sequence. Stop reason: {0}", reason);
		if (reason.Contains("Exception"))
		{
			Logger.StoryEvent(Lang.Get("Something went awry...please check the program logs... ({0})", reason));
		}
		try
		{
			Suspend(newSuspendState: true, 10000);
		}
		catch (Exception exception2)
		{
			LogShutdownException(exception2);
		}
		Thread.Sleep(20);
		try
		{
			EnterRunPhase(EnumServerRunPhase.Shutdown);
		}
		catch (Exception exception3)
		{
			LogShutdownException(exception3);
		}
		try
		{
			if (Blocks != null)
			{
				foreach (Block block in Blocks)
				{
					block?.OnUnloaded(api);
				}
			}
		}
		catch (Exception exception4)
		{
			LogShutdownException(exception4);
		}
		try
		{
			if (Items != null)
			{
				foreach (Item item in Items)
				{
					item?.OnUnloaded(api);
				}
			}
		}
		catch (Exception exception5)
		{
			LogShutdownException(exception5);
		}
		Logger.Event("Shutting down {0} server threads... ", Serverthreads.Count);
		_consoleThreadsCts.Cancel();
		Logger.Event("Killed console thread");
		Logger.StoryEvent(Lang.Get("Alone again..."));
		ServerThread.shouldExit = true;
		int num2 = 120;
		bool flag = false;
		int num3 = num2;
		while (num3-- > 0)
		{
			Thread.Sleep(500);
			flag = Serverthreads.Aggregate(seed: false, (bool flag2, Thread t) => flag2 || t.IsAlive);
			if (!flag)
			{
				break;
			}
			if (num3 >= num2 - 10 || num3 % 4 != 0)
			{
				continue;
			}
			string text2 = "";
			for (int num4 = 0; num4 < Serverthreads.Count; num4++)
			{
				if (Serverthreads[num4].IsAlive)
				{
					text2 = Serverthreads[num4].Name;
					break;
				}
			}
			Logger.Event("Waiting for a server thread ({2}) to shut down ({0}/{1})...", num3 / 2, num2 / 2, text2);
		}
		if (flag)
		{
			string text3 = string.Join(", ", from t in Serverthreads
				where t.IsAlive
				select t.Name);
			Logger.Event("One or more server threads {0} didn't shut down within {1}ms, forcefully shutting them down...", text3, num2 * 500);
			ServerThreadsCts.Cancel();
		}
		else
		{
			Logger.Event("All threads gracefully shut down");
		}
		WildcardUtil.ClearRegexCache();
		Logger.StoryEvent(Lang.Get("Time to rest."));
		if (exitMode != EnumExitMode.HardExit)
		{
			Logger.Event("Doing last tick...");
			try
			{
				ProcessMain();
			}
			catch (Exception exception6)
			{
				LogShutdownException(exception6);
			}
		}
		Logger.Event("Stopped the server!");
		ServerThread.shouldExit = false;
		for (int num5 = 0; num5 < MainSockets.Length; num5++)
		{
			MainSockets[num5]?.Dispose();
		}
		for (int num6 = 0; num6 < UdpSockets.Length; num6++)
		{
			UdpSockets[num6]?.Dispose();
		}
		EnterRunPhase(EnumServerRunPhase.Exit);
		if (finalLogMessage != null)
		{
			Logger.Log(finalLogType, finalLogMessage);
		}
		Logger.ClearWatchers();
	}

	private void LogShutdownException(Exception exception)
	{
		if (Logger == null)
		{
			throw new Exception("Unable to log shutdown error because Logger is null", exception);
		}
		Logger.Error("While shutting down the server:");
		Logger.Error(exception);
	}

	public void Dispose()
	{
		serverAssetsPacket.Dispose();
		serverAssetsSentLocally = false;
		worldMetaDataPacketAlreadySentToSinglePlayer = false;
		if (Systems != null)
		{
			ServerSystem[] systems = Systems;
			for (int i = 0; i < systems.Length; i++)
			{
				systems[i].Dispose();
			}
		}
		lock (reusableBuffersDisposalList)
		{
			foreach (BoxedArray reusableBuffersDisposal in reusableBuffersDisposalList)
			{
				reusableBuffersDisposal.Dispose();
			}
			reusableBuffersDisposalList.Clear();
		}
		TyronThreadPool.Inst.Dispose();
		ClassRegistry = null;
		Logger?.Dispose();
		Logger = null;
		_consoleThreadsCts.Dispose();
		serverConsole?.Dispose();
		ServerThreadsCts.Dispose();
		rand?.Dispose();
	}

	public bool DidExit()
	{
		return RunPhase == EnumServerRunPhase.Exit;
	}

	public void ReceiveServerConsole(string message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return;
		}
		if (message.StartsWith('/'))
		{
			string text = message.Split(new char[1] { ' ' })[0].Replace("/", "");
			string text2 = ((message.IndexOf(' ') < 0) ? "" : message.Substring(message.IndexOf(' ') + 1));
			Logger.Notification("Handling Console Command /{0} {1}", text, text2);
			api.commandapi.Execute(text, new TextCommandCallingArgs
			{
				Caller = new Caller
				{
					Type = EnumCallerType.Console,
					CallerRole = "admin",
					CallerPrivileges = new string[1] { "*" },
					FromChatGroupId = GlobalConstants.ConsoleGroup
				},
				RawArgs = new CmdArgs(text2)
			}, (TextCommandResult result) =>
			{
				if (result.StatusMessage != null)
				{
					Logger.Notification(result.StatusMessage);
				}
			});
		}
		else if (!message.StartsWith('.'))
		{
			BroadcastMessageToAllGroups($"<strong>Admin:</strong>{message}", EnumChatType.AllGroups);
			Logger.Chat(string.Format("{0}: {1}", ServerConsoleClient.PlayerName, message.Replace("{", "{{").Replace("}", "}}")));
		}
	}

	public string GetSaveFilepath()
	{
		if (Config.WorldConfig.SaveFileLocation != null)
		{
			return Config.WorldConfig.SaveFileLocation;
		}
		return Path.Combine(GamePaths.Saves, GamePaths.DefaultSaveFilenameWithoutExtension + ".vcdbs");
	}

	public int GenerateClientId()
	{
		if (nextClientID + 1 < 0)
		{
			nextClientID = 1;
		}
		return nextClientID++;
	}

	public void DisconnectPlayer(ConnectedClient client, string othersKickmessage = null, string hisKickMessage = null)
	{
		if (client == null || ignoreDisconnectCalls || !Clients.ContainsKey(client.Id))
		{
			return;
		}
		if (client.State == EnumClientState.Queued)
		{
			lock (ConnectionQueue)
			{
				ConnectionQueue.RemoveAll((QueuedClient e) => e.Client.Id == client.Id);
			}
		}
		ServerPlayer player = client.Player;
		if (!client.IsNewClient || player != null || !string.IsNullOrEmpty(hisKickMessage))
		{
			ignoreDisconnectCalls = true;
			try
			{
				SendPacket(client.Id, ServerPackets.DisconnectPlayer(hisKickMessage));
			}
			catch
			{
			}
			Logger.Notification($"Client {client.Id} disconnected: {hisKickMessage}");
			ignoreDisconnectCalls = false;
		}
		lastDisconnectTotalMs = totalUpTime.ElapsedMilliseconds;
		if (client.IsNewClient)
		{
			Clients.Remove(client.Id);
			client.CloseConnection();
		}
		else if (player != null)
		{
			if (othersKickmessage != null && othersKickmessage.Length > 0)
			{
				Logger.Audit("Client {0} got removed: '{1}' ({2})", client.PlayerName, othersKickmessage, hisKickMessage);
			}
			else
			{
				Logger.Audit("Client {0} disconnected.", client.PlayerName);
			}
			EventManager.TriggerPlayerDisconnect(player);
			ServerSystem[] systems = Systems;
			for (int num = 0; num < systems.Length; num++)
			{
				systems[num].OnPlayerDisconnect(player);
			}
			BroadcastPacket(new Packet_Server
			{
				Id = 41,
				PlayerData = new Packet_PlayerData
				{
					PlayerUID = player.PlayerUID,
					ClientId = -99
				}
			}, player);
			EntityPlayer entity = player.Entity;
			if (entity != null)
			{
				Logger.Debug("Despawn player entity {0}.", client.PlayerName);
				DespawnEntity(entity, new EntityDespawnData
				{
					Reason = EnumDespawnReason.Disconnect
				});
			}
			string playerName = client.PlayerName;
			Clients.Remove(client.Id);
			player.client = null;
			if (client.State.ConnectedOrPlaying())
			{
				othersKickmessage = ((othersKickmessage != null) ? string.Format(Lang.Get("Player {0} got removed. Reason: {1}", playerName, othersKickmessage), default(ReadOnlySpan<object>)) : string.Format(Lang.Get("Player {0} left.", playerName), default(ReadOnlySpan<object>)));
				joinLeaveDeathMessage(othersKickmessage, EnumChatType.JoinLeave, player);
				Logger.Event(othersKickmessage);
			}
			client.CloseConnection();
		}
		else
		{
			Clients.Remove(client.Id);
			client.CloseConnection();
		}
	}

	public void joinLeaveDeathMessage(string message, EnumChatType chatType, IServerPlayer player)
	{
		if (player != null)
		{
			PlayerGroupMembership[] groups = player.Groups;
			foreach (PlayerGroupMembership playerGroupMembership in groups)
			{
				SendMessageToGroup(playerGroupMembership.GroupUid, message, chatType, (chatType == EnumChatType.JoinLeave) ? player : null);
			}
		}
		if (Clients.Count < MagicNum.PublicJoinLeaveDeathMessagesThreshold)
		{
			SendMessageToGeneral(message, chatType, (chatType == EnumChatType.JoinLeave) ? player : null);
		}
	}

	private void processConnectionQueue()
	{
		if (Config.MaxClientsInQueue <= 0 || stopped)
		{
			if (debugProcessConnectionQueue)
			{
				Logger.Debug("Nobody in queue. Will not process queue");
			}
			return;
		}
		List<QueuedClient> list = null;
		QueuedClient[] array = null;
		int count;
		lock (ConnectionQueue)
		{
			count = ConnectionQueue.Count;
			if (debugProcessConnectionQueue)
			{
				Logger.Debug("Checking {0} in queue", count);
			}
			if (count > 0)
			{
				int maxClients = Config.MaxClients;
				int num = Clients.Count - count;
				int num2 = Math.Max(0, maxClients - num);
				if (num2 > 0)
				{
					list = new List<QueuedClient>();
					for (int i = 0; i < num2; i++)
					{
						if (ConnectionQueue.Count > 0)
						{
							QueuedClient nextPlayer = ConnectionQueue.First();
							ConnectionQueue.RemoveAll((QueuedClient e) => e.Client.Id == nextPlayer.Client.Id);
							list.Add(nextPlayer);
						}
					}
					if (debugProcessConnectionQueue)
					{
						Logger.Debug("Will admit {0} players", num2);
					}
				}
				else if (debugProcessConnectionQueue)
				{
					Logger.Debug("Cant admit anyone. {0} - {1} is <=0", Clients.Count, count);
				}
				array = ConnectionQueue.ToArray();
			}
		}
		if (count <= 0)
		{
			return;
		}
		if (list != null)
		{
			foreach (QueuedClient item in list)
			{
				if (debugProcessConnectionQueue)
				{
					Logger.Debug("Ok. Admitted {0}", item.Client.PlayerName);
				}
				FinalizePlayerIdentification(item.Identification, item.Client, item.Entitlements);
			}
		}
		if (array != null)
		{
			for (int num3 = 0; num3 < array.Length; num3++)
			{
				QueuedClient queuedClient = array[num3];
				Packet_Server packet = new Packet_Server
				{
					Id = 82,
					QueuePacket = new Packet_QueuePacket
					{
						Position = num3 + 1
					}
				};
				SendPacket(queuedClient.Client.Id, packet);
			}
		}
	}

	public int GetPlayingClients()
	{
		return Enumerable.Count(Clients, (KeyValuePair<int, ConnectedClient> c) => c.Value.State == EnumClientState.Playing);
	}

	public int GetAllowedChunkRadius(ConnectedClient client)
	{
		int num = (int)Math.Ceiling((float)((client.WorldData == null) ? 128 : client.WorldData.Viewdistance) / (float)MagicNum.ServerChunkSize);
		int result = Math.Min(Config.MaxChunkRadius, num);
		if (client.IsSinglePlayerClient)
		{
			return num;
		}
		return result;
	}

	public FuzzyEntityPos GetSpawnPosition(string playerUID = null, bool onlyGlobalDefaultSpawn = false, bool consumeSpawn = false)
	{
		PlayerSpawnPos playerSpawnPos = null;
		ServerPlayerData serverPlayerData = GetServerPlayerData(playerUID);
		ServerPlayer serverPlayer = PlayerByUid(playerUID) as ServerPlayer;
		PlayerRole playerRole = serverPlayerData.GetPlayerRole(this);
		float radius = 0f;
		if (playerRole.ForcedSpawn != null && !onlyGlobalDefaultSpawn)
		{
			playerSpawnPos = playerRole.ForcedSpawn;
			if (consumeSpawn && playerSpawnPos != null && playerSpawnPos.RemainingUses >= 0)
			{
				playerSpawnPos.RemainingUses--;
				if (playerSpawnPos.RemainingUses <= 0)
				{
					playerRole.ForcedSpawn = null;
				}
			}
		}
		if (playerSpawnPos == null && serverPlayer?.WorldData != null && !onlyGlobalDefaultSpawn)
		{
			playerSpawnPos = (serverPlayer.WorldData as ServerWorldPlayerData).SpawnPosition;
			if (consumeSpawn && playerSpawnPos != null && playerSpawnPos.RemainingUses >= 0)
			{
				playerSpawnPos.RemainingUses--;
				if (playerSpawnPos.RemainingUses <= 0)
				{
					(serverPlayer.WorldData as ServerWorldPlayerData).SpawnPosition = null;
				}
			}
		}
		if (playerSpawnPos == null && !onlyGlobalDefaultSpawn)
		{
			playerSpawnPos = playerRole.DefaultSpawn;
			if (consumeSpawn && playerSpawnPos != null && playerSpawnPos.RemainingUses >= 0)
			{
				playerSpawnPos.RemainingUses--;
				if (playerSpawnPos.RemainingUses <= 0)
				{
					playerRole.DefaultSpawn = null;
				}
			}
		}
		if (playerSpawnPos == null)
		{
			playerSpawnPos = SaveGameData.DefaultSpawn ?? mapMiddleSpawnPos;
			playerSpawnPos.RemainingUses = 99;
			radius = World.Config.GetAsInt("spawnRadius");
		}
		FuzzyEntityPos fuzzyEntityPos = EntityPosFromSpawnPos(playerSpawnPos);
		fuzzyEntityPos.Radius = radius;
		fuzzyEntityPos.UsesLeft = playerSpawnPos.RemainingUses;
		return fuzzyEntityPos;
	}

	public EntityPos GetJoinPosition(ConnectedClient client)
	{
		PlayerRole playerRole = client.ServerData.GetPlayerRole(this);
		if (playerRole.ForcedSpawn != null)
		{
			return EntityPosFromSpawnPos(playerRole.ForcedSpawn);
		}
		EntityPos pos = client.Entityplayer.Pos;
		if (pos.AnyNaN())
		{
			Logger.Error("Player " + client.PlayerName + " has an impossible (bugged) Pos, placing player at world spawn.");
			pos.SetFrom(DefaultSpawnPosition);
		}
		return pos;
	}

	private FuzzyEntityPos EntityPosFromSpawnPos(PlayerSpawnPos playerSpawn)
	{
		if (!playerSpawn.y.HasValue || playerSpawn.y == 0)
		{
			playerSpawn.y = WorldMap.GetTerrainGenSurfacePosY(playerSpawn.x, playerSpawn.z);
			if (!playerSpawn.y.HasValue)
			{
				return null;
			}
		}
		if (!WorldMap.IsValidPos(playerSpawn.x, playerSpawn.y.Value, playerSpawn.z))
		{
			if (Config.RepairMode)
			{
				int num = SaveGameData.MapSizeX / 2;
				int num2 = SaveGameData.MapSizeZ / 2;
				return new FuzzyEntityPos(num, WorldMap.GetTerrainGenSurfacePosY(num, num2), num2);
			}
			throw new Exception("Invalid spawn coordinates found. It is outside the world map.");
		}
		return new FuzzyEntityPos((double)playerSpawn.x + 0.5, playerSpawn.y.Value, (double)playerSpawn.z + 0.5)
		{
			Pitch = (float)Math.PI,
			Yaw = ((!playerSpawn.yaw.HasValue) ? ((float)rand.Value.NextDouble() * 2f * (float)Math.PI) : playerSpawn.yaw.Value)
		};
	}

	private void LoadAssets()
	{
		Logger.Notification("Start discovering assets");
		int num = AssetManager.InitAndLoadBaseAssets(Logger);
		Logger.Notification("Found {0} base assets in total", num);
	}

	public ConnectedClient GetClientByPlayername(string playerName)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.PlayerName.ToLowerInvariant() == playerName.ToLowerInvariant())
			{
				return value;
			}
		}
		return null;
	}

	public void GetOnlineOrOfflinePlayer(string targetPlayerName, Action<EnumServerResponse, string> onPlayerReceived)
	{
		ConnectedClient clientByPlayername = GetClientByPlayername(targetPlayerName);
		if (clientByPlayername == null)
		{
			AuthServerComm.ResolvePlayerName(targetPlayerName, (EnumServerResponse result, string playeruid) =>
			{
				EnqueueMainThreadTask(() =>
				{
					onPlayerReceived(result, playeruid);
					FrameProfiler.Mark("onplayerreceived");
				});
			});
		}
		else
		{
			onPlayerReceived(EnumServerResponse.Good, clientByPlayername.WorldData.PlayerUID);
		}
	}

	public void GetOnlineOrOfflinePlayerByUid(string targetPlayeruid, Action<EnumServerResponse, string> onPlayerReceived)
	{
		ConnectedClient clientByUID = GetClientByUID(targetPlayeruid);
		if (clientByUID == null)
		{
			AuthServerComm.ResolvePlayerUid(targetPlayeruid, (EnumServerResponse result, string playername) =>
			{
				EnqueueMainThreadTask(() =>
				{
					onPlayerReceived(result, playername);
					FrameProfiler.Mark("onplayerreceived");
				});
			});
		}
		else
		{
			onPlayerReceived(EnumServerResponse.Good, clientByUID.WorldData.PlayerUID);
		}
	}

	public ConnectedClient GetClient(int id)
	{
		if (id == serverConsoleId)
		{
			return ServerConsoleClient;
		}
		if (!Clients.ContainsKey(id))
		{
			return null;
		}
		return Clients[id];
	}

	public ConnectedClient GetClientByUID(string playerUID)
	{
		if (ServerConsoleClient.WorldData.PlayerUID.Equals(playerUID, StringComparison.InvariantCultureIgnoreCase))
		{
			return ServerConsoleClient;
		}
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.WorldData?.PlayerUID != null && value.WorldData.PlayerUID.Equals(playerUID, StringComparison.InvariantCultureIgnoreCase))
			{
				return value;
			}
		}
		return null;
	}

	internal void RemapItem(Item removedItem)
	{
		while (removedItem.ItemId >= Items.Count)
		{
			Items.Add(new Item
			{
				ItemId = Items.Count,
				IsMissing = true
			});
		}
		if (Items[removedItem.ItemId] != null && Items[removedItem.ItemId].Code != null)
		{
			Item item = Items[removedItem.ItemId];
			Items[removedItem.ItemId] = new Item();
			ItemsByCode.Remove(item.Code);
			RegisterItem(item);
		}
		Items[removedItem.ItemId] = removedItem;
		nextFreeItemId = Math.Max(nextFreeItemId, removedItem.ItemId + 1);
	}

	internal void FillMissingItem(int ItemId, Item Item)
	{
		Item item = new Item(0);
		while (ItemId >= Items.Count)
		{
			Items.Add(item);
		}
		Item.ItemId = ItemId;
		Items[ItemId] = Item;
		ItemsByCode[Item.Code] = Item;
		nextFreeItemId = Math.Max(nextFreeItemId, Item.ItemId + 1);
	}

	internal void RemapBlock(Block removedBlock)
	{
		new FastSmallDictionary<string, CompositeTexture>("all", new CompositeTexture(new AssetLocation("unknown")));
		while (removedBlock.BlockId >= Blocks.Count)
		{
			Blocks.Add(new Block
			{
				BlockId = Blocks.Count,
				IsMissing = true
			});
		}
		if (Blocks[removedBlock.BlockId] != null && Blocks[removedBlock.BlockId].Code != null)
		{
			Block block = Blocks[removedBlock.BlockId];
			Blocks[removedBlock.BlockId] = new Block
			{
				BlockId = removedBlock.Id
			};
			BlocksByCode.Remove(block.Code);
			RegisterBlock(block);
		}
		Blocks[removedBlock.BlockId] = removedBlock;
		nextFreeBlockId = Math.Max(nextFreeBlockId, removedBlock.BlockId + 1);
	}

	internal void FillMissingBlock(int blockId, Block block)
	{
		block.BlockId = blockId;
		Blocks[blockId] = block;
		BlocksByCode[block.Code] = block;
		nextFreeBlockId = Math.Max(nextFreeBlockId, block.BlockId + 1);
	}

	public void RegisterBlock(Block block)
	{
		if (block.Code == null || block.Code.Path.Length == 0)
		{
			throw new Exception(Lang.Get("Attempted to register Block with no code. Must use a unique code"));
		}
		if (BlocksByCode.ContainsKey(block.Code))
		{
			throw new Exception(Lang.Get("Block must have a unique code ('{0}' is already in use). This is often caused right after a game update when there are old installation files left behind. Try full uninstall and reinstall.", block.Code));
		}
		if (block.Sounds == null)
		{
			block.Sounds = new BlockSounds();
		}
		if (nextFreeBlockId >= Blocks.Count)
		{
			FastSmallDictionary<string, CompositeTexture> textures = new FastSmallDictionary<string, CompositeTexture>("all", new CompositeTexture(new AssetLocation("unknown")));
			(Blocks as BlockList).PreAlloc(nextFreeBlockId + 1);
			while (Blocks.Count <= nextFreeBlockId)
			{
				Blocks.Add(new Block
				{
					Textures = textures,
					Code = new AssetLocation("unknown"),
					BlockId = Blocks.Count,
					DrawType = EnumDrawType.Cube,
					MatterState = EnumMatterState.Solid,
					IsMissing = true,
					Replaceable = 1
				});
			}
		}
		block.BlockId = nextFreeBlockId;
		Blocks[nextFreeBlockId] = block;
		BlocksByCode.Add(block.Code, block);
		nextFreeBlockId++;
	}

	internal void RegisterItem(Item item)
	{
		if (item.Code == null || item.Code.Path.Length == 0)
		{
			throw new Exception(Lang.Get("Attempted to register Item with no code. Must use a unique code"));
		}
		if (ItemsByCode.ContainsKey(item.Code))
		{
			throw new Exception(Lang.Get("Attempted to register Item with code {0}, but an item with such code already exists. Must use a unique code", item.Code));
		}
		if (nextFreeItemId >= Items.Count)
		{
			while (Items.Count <= nextFreeItemId)
			{
				Items.Add(new Item
				{
					Textures = new Dictionary<string, CompositeTexture> { 
					{
						"all",
						new CompositeTexture(new AssetLocation("unknown"))
					} },
					Code = new AssetLocation("unknown"),
					ItemId = Items.Count,
					MatterState = EnumMatterState.Solid,
					IsMissing = true
				});
			}
		}
		item.ItemId = nextFreeItemId;
		Items[nextFreeItemId] = item;
		ItemsByCode.Add(item.Code, item);
		nextFreeItemId++;
	}

	public Item GetItem(int itemId)
	{
		if (Items.Count <= itemId)
		{
			return null;
		}
		return Items[itemId];
	}

	public Block GetBlock(int blockId)
	{
		return Blocks[blockId];
	}

	public EntityProperties GetEntityType(AssetLocation entityCode)
	{
		EntityTypesByCode.TryGetValue(entityCode, out var value);
		return value;
	}

	public void SetSeaLevel(int seaLevel)
	{
		this.seaLevel = seaLevel;
	}

	public void SetBlockLightLevels(float[] lightLevels)
	{
		blockLightLevels = lightLevels;
	}

	public void SetSunLightLevels(float[] lightLevels)
	{
		sunLightLevels = lightLevels;
	}

	internal void SetSunBrightness(int lightlevel)
	{
		sunBrightness = lightlevel;
	}

	private List<string> makeEntityCodesCache()
	{
		ICollection<AssetLocation> keys = EntityTypesByCode.Keys;
		List<string> list = new List<string>(keys.Count);
		foreach (AssetLocation item in keys)
		{
			list.Add(item.ToShortString());
		}
		return list;
	}

	public ConnectedClient GetConnectedClient(string playerUID)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.WorldData?.PlayerUID == playerUID)
			{
				return value;
			}
		}
		return null;
	}

	public IWorldPlayerData GetWorldPlayerData(string playerUID)
	{
		if (playerUID == null)
		{
			return null;
		}
		PlayerDataManager.WorldDataByUID.TryGetValue(playerUID, out var value);
		if (value == null)
		{
			return GetConnectedClient(playerUID)?.WorldData;
		}
		return value;
	}

	public ServerPlayerData FindServerPlayerDataByLastKnownPlayerName(string playerName)
	{
		foreach (ServerPlayerData value in PlayerDataManager.PlayerDataByUid.Values)
		{
			if (value.LastKnownPlayername.ToLowerInvariant() == playerName.ToLowerInvariant())
			{
				return value;
			}
		}
		return null;
	}

	public ServerPlayerData GetServerPlayerData(string playeruid)
	{
		PlayerDataManager.PlayerDataByUid.TryGetValue(playeruid, out var value);
		return value;
	}

	public bool PlayerHasPrivilege(int clientid, string privilege)
	{
		if (privilege == null)
		{
			return true;
		}
		if (clientid == serverConsoleId)
		{
			return true;
		}
		if (!Clients.ContainsKey(clientid))
		{
			return false;
		}
		return Clients[clientid].ServerData.HasPrivilege(privilege, Config.RolesByCode);
	}

	public int PlaySoundAt(SoundAttributes sound, double x, double y, double z, int dimension, IPlayer dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		if (sound.Location == null)
		{
			return 0;
		}
		int? clientId = null;
		if (dualCallByPlayer != null)
		{
			clientId = GetConnectedClient(dualCallByPlayer.PlayerUID)?.Id;
		}
		PlaySoundAtExceptPlayer(sound.Location, x, y, z, clientId, sound.Pitch.nextFloat(1f, rand.Value), sound.Range, sound.Volume.nextFloat(volumeMultiplier, rand.Value));
		return 0;
	}

	public int PlaySoundAt(SoundAttributes sound, BlockPos pos, double yOffsetFromCenter, IPlayer dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		return PlaySoundAt(sound, (double)pos.X + 0.5, (double)pos.InternalY + 0.5 + yOffsetFromCenter, (double)pos.Z + 0.5, pos.dimension, dualCallByPlayer, volumeMultiplier);
	}

	public int PlaySoundAt(SoundAttributes sound, Entity atEntity, IPlayer dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		float num = 0f;
		if (atEntity.SelectionBox != null)
		{
			num = atEntity.SelectionBox.Y2 / 2f;
		}
		else if (atEntity.Properties?.CollisionBoxSize != null)
		{
			num = atEntity.Properties.CollisionBoxSize.Y / 2f;
		}
		return PlaySoundAt(sound, atEntity.Pos.X, atEntity.Pos.InternalY + (double)num, atEntity.Pos.Z, atEntity.Pos.Dimension, dualCallByPlayer, volumeMultiplier);
	}

	public int PlaySoundAt(SoundAttributes sound, IPlayer atPlayer, IPlayer dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		if (atPlayer == null)
		{
			return 0;
		}
		return PlaySoundAt(sound, atPlayer.Entity, dualCallByPlayer, volumeMultiplier);
	}

	public int PlaySoundFor(SoundAttributes sound, IPlayer forPlayer, float volumeMultiplier = 1f)
	{
		if (sound.Location == null)
		{
			return 0;
		}
		SendSound(forPlayer as IServerPlayer, sound.Location, 0.0, 0.0, 0.0, sound.Pitch.nextFloat(1f, rand.Value), sound.Range, sound.Volume.nextFloat(volumeMultiplier, rand.Value));
		return 0;
	}

	public void PlaySoundAt(string location, IPlayer atPlayer, IPlayer ignorePlayerUID = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(new AssetLocation(location), atPlayer, ignorePlayerUID, randomizePitch, range, volume);
	}

	public void PlaySoundAt(AssetLocation location, double posx, double posy, double posz, IPlayer dualCallByPlayer, EnumSoundType soundType, float pitch, float range = 32f, float volume = 1f)
	{
		PlaySoundAtExceptPlayer(location, posx, posy, posz, dualCallByPlayer?.ClientId, pitch, range, volume, soundType);
	}

	public void PlaySoundAt(AssetLocation location, Entity entity, IPlayer dualCallByPlayer = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		float num = 0f;
		if (entity.SelectionBox != null)
		{
			num = entity.SelectionBox.Y2 / 2f;
		}
		else if (entity.Properties?.CollisionBoxSize != null)
		{
			num = entity.Properties.CollisionBoxSize.Y / 2f;
		}
		PlaySoundAt(location, entity.Pos.X, entity.Pos.InternalY + (double)num, entity.Pos.Z, dualCallByPlayer, randomizePitch, range, volume);
	}

	public void PlaySoundAt(AssetLocation location, IPlayer atPlayer, IPlayer ignorePlayerUID = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		if (atPlayer != null)
		{
			PlaySoundAt(location, atPlayer.Entity, ignorePlayerUID, randomizePitch, range, volume);
		}
	}

	public void PlaySoundAt(AssetLocation location, Entity entity, IPlayer ignorePlayerUID, float pitch, float range = 32f, float volume = 1f)
	{
		float num = 0f;
		if (entity.SelectionBox != null)
		{
			num = entity.SelectionBox.Y2 / 2f;
		}
		else if (entity.Properties?.CollisionBoxSize != null)
		{
			num = entity.Properties.CollisionBoxSize.Y / 2f;
		}
		PlaySoundAt(location, entity.Pos.X, entity.Pos.InternalY + (double)num, entity.Pos.Z, ignorePlayerUID, pitch, range, volume);
	}

	public void PlaySoundAt(string location, double posx, double posy, double posz, IPlayer ignorePlayerUID = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(new AssetLocation(location), posx, posy, posz, ignorePlayerUID, randomizePitch, range, volume);
	}

	public void PlaySoundAt(AssetLocation location, BlockPos pos, double yOffsetFromCenter, IPlayer ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(location, (double)pos.X + 0.5, (double)pos.InternalY + 0.5 + yOffsetFromCenter, (double)pos.Z + 0.5, ignorePlayerUid, randomizePitch, range, volume);
	}

	public void PlaySoundAt(string location, Entity entity, IPlayer dualCallByPlayer = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(new AssetLocation(location), entity.Pos.X, entity.Pos.InternalY, entity.Pos.Z, dualCallByPlayer, randomizePitch, range, volume);
	}

	public void PlaySoundAt(AssetLocation location, double posx, double posy, double posz, IPlayer ignorePlayerUID = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		if (!(location == null))
		{
			int? clientId = null;
			if (ignorePlayerUID != null)
			{
				clientId = GetConnectedClient(ignorePlayerUID.PlayerUID)?.Id;
			}
			float pitch = (randomizePitch ? RandomPitch() : 1f);
			PlaySoundAtExceptPlayer(location, posx, posy, posz, clientId, pitch, range, volume);
		}
	}

	public void PlaySoundAt(AssetLocation location, double posx, double posy, double posz, IPlayer ignorePlayerUID, float pitch, float range = 32f, float volume = 1f)
	{
		if (!(location == null))
		{
			int? clientId = null;
			if (ignorePlayerUID != null)
			{
				clientId = GetConnectedClient(ignorePlayerUID.PlayerUID)?.Id;
			}
			PlaySoundAtExceptPlayer(location, posx, posy, posz, clientId, pitch, range, volume);
		}
	}

	public void PlaySoundFor(AssetLocation location, IPlayer forPlayer, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		float pitch = (randomizePitch ? RandomPitch() : 1f);
		SendSound(forPlayer as IServerPlayer, location, 0.0, 0.0, 0.0, pitch, range, volume);
	}

	public void PlaySoundFor(AssetLocation location, IPlayer forPlayer, float pitch, float range = 32f, float volume = 1f)
	{
		SendSound(forPlayer as IServerPlayer, location, 0.0, 0.0, 0.0, pitch, range, volume);
	}

	public void PlaySoundAtExceptPlayer(AssetLocation location, double posx, double posy, double posz, int? clientId = null, float pitch = 1f, float range = 32f, float volume = 1f, EnumSoundType soundType = EnumSoundType.Sound)
	{
		if (location == null)
		{
			return;
		}
		foreach (ConnectedClient value in Clients.Values)
		{
			if (clientId != value.Id && value.State == EnumClientState.Playing && value.Position.InRangeOf(posx, posy, posz, range * range))
			{
				SendSound(value.Player, location, posx, posy, posz, pitch, range, volume, soundType);
			}
		}
	}

	public void TriggerNeighbourBlocksUpdate(BlockPos pos)
	{
		Block block = null;
		try
		{
			block = WorldMap.RelaxedBlockAccess.GetBlock(pos, 2);
			if (block.IsLiquid())
			{
				block.OnNeighbourBlockChange(this, pos, pos);
			}
			BlockPos blockPos = new BlockPos(pos.dimension);
			BlockFacing[] aLLFACES = BlockFacing.ALLFACES;
			foreach (BlockFacing blockFacing in aLLFACES)
			{
				blockPos.Set(pos).Offset(blockFacing);
				if (!worldmap.IsValidPos(blockPos))
				{
					continue;
				}
				Block block2 = WorldMap.RelaxedBlockAccess.GetBlock(blockPos);
				block2.OnNeighbourBlockChange(this, blockPos, pos);
				if (block2.ForFluidsLayer)
				{
					continue;
				}
				UpdateLiquid(blockPos, pos);
				if (blockFacing == BlockFacing.UP && !block2.SideSolid.All)
				{
					BlockPos blockPos2 = blockPos.Copy();
					for (int j = 0; j < 4; j++)
					{
						blockPos2.IterateHorizontalOffsets(j);
						UpdateLiquid(blockPos2, blockPos);
					}
				}
			}
		}
		catch (Exception e)
		{
			Logger.Warning($"Error ticking neighbour block update: {pos} {block?.Code}");
			Logger.Warning(e);
		}
	}

	private void UpdateLiquid(BlockPos neibPos, BlockPos pos)
	{
		Block block = WorldMap.RelaxedBlockAccess.GetBlock(neibPos, 2);
		if (block.BlockId == 0)
		{
			return;
		}
		EnumHandling handling = EnumHandling.PassThrough;
		BlockBehavior[] blockBehaviors = block.BlockBehaviors;
		for (int i = 0; i < blockBehaviors.Length; i++)
		{
			blockBehaviors[i].OnNeighbourBlockChange(this, neibPos, pos, ref handling);
			if (handling == EnumHandling.PreventSubsequent)
			{
				break;
			}
		}
	}

	internal Entity GetEntity(long entityId)
	{
		LoadedEntities.TryGetValue(entityId, out var value);
		return value;
	}

	public override bool IsValidPos(BlockPos pos)
	{
		return WorldMap.IsValidPos(pos);
	}

	public override Block GetBlock(BlockPos pos)
	{
		return WorldMap.RelaxedBlockAccess.GetBlock(pos);
	}

	public bool IsFullyLoadedChunk(BlockPos pos)
	{
		return ((ServerChunk)WorldMap.GetChunk(pos))?.NotAtEdge ?? false;
	}

	public Entity SpawnItemEntity(ItemStack itemstack, Vec3d position, Vec3d velocity = null)
	{
		if (itemstack == null || itemstack.StackSize <= 0)
		{
			return null;
		}
		Entity entity = EntityItem.FromItemstack(itemstack, position, velocity, this);
		SpawnEntity(entity);
		return entity;
	}

	public Entity SpawnItemEntity(ItemStack itemstack, BlockPos pos, Vec3d velocity = null)
	{
		return SpawnItemEntity(itemstack, pos.ToVec3d().Add(0.5), velocity);
	}

	public bool LoadEntity(Entity entity, long fromChunkIndex3d)
	{
		try
		{
			if (Config.RepairMode)
			{
				SaveGameData.LastEntityId = Math.Max(SaveGameData.LastEntityId, entity.EntityId);
			}
			EntityProperties entityType = api.World.GetEntityType(entity.Code);
			if (entityType == null)
			{
				Logger.Warning("Couldn't load entity class {0} saved type code {1} - its Type is null! Will remove from chunk, sorry!", entity.GetType(), entity.Code);
				return false;
			}
			entity.Initialize(entityType.Clone(), api, fromChunkIndex3d);
			entity.AfterInitialized(onFirstSpawn: false);
			if (!LoadedEntities.TryAdd(entity.EntityId, entity))
			{
				Logger.Warning("Couldn't add entity {0}, type {1} to list of loaded entities (duplicate entityid)! Will remove from chunk, sorry!", entity.EntityId, entity.Properties.Code);
				return false;
			}
			entity.OnEntityLoaded();
			EventManager.TriggerEntityLoaded(entity);
			return true;
		}
		catch (Exception e)
		{
			Logger.Error("Couldn't add entity type {0} at {1} due to exception in code. Will remove from chunk, sorry!", entity.Code, entity.Pos.OnlyPosToString());
			Logger.Error(e);
			return false;
		}
	}

	public void SpawnEntity(Entity entity)
	{
		SpawnEntity(entity, GetEntityType(entity.Code));
	}

	public void SpawnPriorityEntity(Entity entity)
	{
		SpawnEntity_internal(GetEntityType(entity.Code), entity);
		ServerUdpNetwork.physicsManager.SendPrioritySpawn(entity, Clients.Values);
	}

	public void SpawnEntity(Entity entity, EntityProperties type)
	{
		SpawnEntity_internal(GetEntityType(entity.Code), entity);
		lock (EntitySpawnSendQueue)
		{
			EntitySpawnSendQueue.Add(entity);
		}
	}

	private void SpawnEntity_internal(EntityProperties type, Entity entity)
	{
		if (Config.RepairMode && !(entity is EntityPlayer))
		{
			Logger.Warning("Rejected one entity spawn. Server in repair mode. Will not spawn new entities.");
			return;
		}
		long num = ++SaveGameData.LastEntityId;
		long inChunkIndex3d = WorldMap.ChunkIndex3D(entity.Pos);
		entity.EntityId = num;
		entity.DespawnReason = null;
		if (type == null)
		{
			Logger.Error("Couldn't spawn entity {0} with id {1} and code {2} - it's Type is null!", entity.GetType(), num, entity.Code);
			return;
		}
		entity.Initialize(type.Clone(), api, inChunkIndex3d);
		entity.AfterInitialized(onFirstSpawn: true);
		AddEntityToChunk(entity, (int)entity.Pos.X, (int)entity.Pos.InternalY, (int)entity.Pos.Z);
		if (!LoadedEntities.TryAdd(num, entity))
		{
			Logger.Warning("SpawnEntity: Duplicate entity id discovered, will updating SaveGameData.LastEntityId to reflect this. This was likely caused by an ungraceful server exit.");
			RemoveEntityFromChunk(entity, (int)entity.Pos.X, (int)entity.Pos.InternalY, (int)entity.Pos.Z);
			SaveGameData.LastEntityId = LoadedEntities.Max((KeyValuePair<long, Entity> val) => val.Value.EntityId);
			num = (entity.EntityId = ++SaveGameData.LastEntityId);
			if (!LoadedEntities.TryAdd(num, entity))
			{
				Logger.Warning("SpawnEntity: Still not able to add entity after updating LastEntityId. Looks like a programming error. Killing server...");
				throw new Exception("Unable to spawn entity");
			}
			AddEntityToChunk(entity, (int)entity.Pos.X, (int)entity.Pos.InternalY, (int)entity.Pos.Z);
		}
		entity.OnEntitySpawn();
		EventManager.TriggerEntitySpawned(entity);
	}

	public long GetNextHerdId()
	{
		return ++SaveGameData.LastHerdId;
	}

	public void DespawnEntity(Entity entity, EntityDespawnData despawnData)
	{
		entity.OnEntityDespawn(despawnData);
		FrameProfiler.Mark("despawned-1-", entity.Code.Path);
		LoadedEntities.TryRemove(entity.EntityId, out var _);
		if ((despawnData == null || despawnData.Reason != EnumDespawnReason.Unload) && !RemoveEntityFromChunk(entity, (int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z) && entity is EntityPlayer entityPlayer)
		{
			ServerChunk serverChunk = WorldMap.GetServerChunk(entity.InChunkIndex3d);
			Logger.Warning("Unable to remove player entity {0} from chunk @ {1}, CI {2}. {3}", entityPlayer.Player?.PlayerName, entity.Pos, entity.InChunkIndex3d, (serverChunk == null) ? "Chunk is not loaded" : "Was not in the loaded chunk");
		}
		EntityDespawnSendQueue.Add(new KeyValuePair<Entity, EntityDespawnData>(entity, entity.DespawnReason));
		entity.State = EnumEntityState.Despawned;
		FrameProfiler.Mark("despawned-2-", entity.Code.Path);
		EventManager.TriggerEntityDespawned(entity, despawnData);
	}

	private void AddEntityToChunk(Entity entity, int x, int y, int z)
	{
		WorldMap.GetServerChunk(x / MagicNum.ServerChunkSize, y / MagicNum.ServerChunkSize, z / MagicNum.ServerChunkSize)?.AddEntity(entity);
	}

	private bool RemoveEntityFromChunk(Entity entity, int x, int y, int z)
	{
		return WorldMap.GetServerChunk(entity.InChunkIndex3d)?.RemoveEntity(entity.EntityId) ?? false;
	}

	public Entity GetNearestEntity(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches = null)
	{
		return GetEntitiesAround(position, horRange, vertRange, matches).MinBy((Entity entity) => entity.Pos.SquareDistanceTo(position));
	}

	public Entity GetEntityById(long entityId)
	{
		LoadedEntities.TryGetValue(entityId, out var value);
		return value;
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return EventManager.AddGameTickListener(OnGameTick, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return EventManager.AddGameTickListener(OnGameTick, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay)
	{
		return EventManager.AddDelayedCallback(OnTimePassed, millisecondDelay);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return EventManager.AddGameTickListener(OnGameTick, pos, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return EventManager.AddGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return EventManager.AddGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterCallback(Action<IWorldAccessor, BlockPos, float> OnTimePassed, BlockPos pos, int millisecondDelay)
	{
		return EventManager.AddDelayedCallback(OnTimePassed, pos, millisecondDelay);
	}

	public long RegisterCallbackUnique(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, int millisecondInterval)
	{
		return EventManager.AddSingleDelayedCallback(OnGameTick, pos, millisecondInterval);
	}

	public void UnregisterCallback(long callbackId)
	{
		if (callbackId > 0)
		{
			EventManager.RemoveDelayedCallback(callbackId);
		}
	}

	public void UnregisterGameTickListener(long listenerId)
	{
		if (listenerId > 0)
		{
			EventManager.RemoveGameTickListener(listenerId);
		}
	}

	public void SpawnParticles(float quantity, int color, Vec3d minPos, Vec3d maxPos, Vec3f minVelocity, Vec3f maxVelocity, float lifeLength, float gravityEffect, float scale = 1f, EnumParticleModel model = EnumParticleModel.Quad, IPlayer dualCallByPlayer = null)
	{
		SimpleParticleProperties simpleParticleProperties = new SimpleParticleProperties(quantity, quantity, color, minPos, maxPos, minVelocity, maxVelocity, lifeLength, gravityEffect);
		simpleParticleProperties.ParticleModel = model;
		simpleParticleProperties.MinSize = (simpleParticleProperties.MaxSize = scale);
		SpawnParticles(simpleParticleProperties, dualCallByPlayer);
	}

	public void SpawnParticles(IParticlePropertiesProvider provider, IPlayer dualCallByPlayer = null)
	{
		string particlePropertyProviderClassName = ClassRegistry.ParticleProviderTypeToClassnameMapping[provider.GetType()];
		Packet_SpawnParticles packet_SpawnParticles = new Packet_SpawnParticles();
		packet_SpawnParticles.ParticlePropertyProviderClassName = particlePropertyProviderClassName;
		using (MemoryStream memoryStream = new MemoryStream())
		{
			BinaryWriter writer = new BinaryWriter(memoryStream);
			provider.ToBytes(writer);
			packet_SpawnParticles.SetData(memoryStream.ToArray());
		}
		Packet_Server p = new Packet_Server
		{
			Id = 61,
			SpawnParticles = packet_SpawnParticles
		};
		provider.BeginParticle();
		Vec3d pos = provider.Pos;
		long index3d = WorldMap.ChunkIndex3D((int)pos.X / 32, (int)pos.Y / 32, (int)pos.Z / 32);
		Serialize_(p);
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.IsPlayingClient && value.Player != dualCallByPlayer && value.DidSendChunk(index3d))
			{
				SendPacket(value.Id, reusableBuffer);
			}
		}
	}

	public void SpawnCubeParticles(Vec3d pos, ItemStack stack, float radius, int quantity, float scale = 0.5f, IPlayer dualCallByPlayer = null, Vec3f velocity = null)
	{
		SpawnParticles(new StackCubeParticles(pos, stack, radius, quantity, scale, velocity), dualCallByPlayer);
	}

	public void SpawnCubeParticles(BlockPos blockpos, Vec3d pos, float radius, int quantity, float scale = 0.5f, IPlayer dualCallByPlayer = null, Vec3f velocity = null)
	{
		SpawnParticles(new BlockCubeParticles(this, blockpos, pos, radius, quantity, scale, velocity), dualCallByPlayer);
	}

	public void CreateExplosion(BlockPos pos, EnumBlastType blastType, double destructionRadius, double injureRadius, float blockDropChanceMultiplier = 1f, string ignitedByPlayerUid = null)
	{
		destructionRadius = GameMath.Clamp(destructionRadius, 1.0, 16.0);
		double num = Math.Max(1.2000000476837158 * destructionRadius, injureRadius);
		if (num > (double)ShapeUtil.MaxShells)
		{
			throw new ArgumentOutOfRangeException("Radius cannot be greater than " + (int)((float)ShapeUtil.MaxShells / 1.2f));
		}
		FastVec3f[] cubicShellNormalizedVectors = ShapeUtil.GetCubicShellNormalizedVectors((int)num);
		double num2 = 0.800000011920929 * destructionRadius;
		double num3 = 0.4000000059604645 * destructionRadius;
		BlockPos blockPos = new BlockPos(pos.dimension);
		int num4 = (int)Math.Ceiling(num);
		BlockPos minPos = pos.AddCopy(-num4);
		BlockPos maxPos = pos.AddCopy(num4);
		WorldMap.PrefetchBlockAccess.PrefetchBlocks(minPos, maxPos);
		DamageSource testSrc = new DamageSource
		{
			Source = EnumDamageSource.Explosion,
			SourcePos = pos.ToVec3d(),
			Type = EnumDamageType.BluntAttack
		};
		Entity[] entitiesAround = GetEntitiesAround(pos.ToVec3d(), (float)injureRadius + 2f, (float)injureRadius + 2f, (Entity e) => e.ShouldReceiveDamage(testSrc, (float)injureRadius));
		Dictionary<long, double> dictionary = new Dictionary<long, double>();
		for (int num5 = 0; num5 < entitiesAround.Length; num5++)
		{
			dictionary[entitiesAround[num5].EntityId] = 0.0;
		}
		ExplosionSmokeParticles explosionSmokeParticles = new ExplosionSmokeParticles();
		explosionSmokeParticles.basePos = new Vec3d((double)pos.X + 0.5, (double)pos.Y + 0.5, (double)pos.Z + 0.5);
		Dictionary<BlockPos, Block> dictionary2 = new Dictionary<BlockPos, Block>();
		Cuboidd cuboidd = Block.DefaultCollisionBox.ToDouble();
		Vec3f vec3f = new Vec3f();
		for (int num6 = 0; num6 < cubicShellNormalizedVectors.Length; num6++)
		{
			double num7;
			double val = (num7 = num2 + rand.Value.NextDouble() * num3);
			double num8 = injureRadius;
			double num9 = Math.Max(val, injureRadius);
			FastVec3f vec = cubicShellNormalizedVectors[num6];
			for (double num10 = 0.0; num10 < num9; num10 += 0.25)
			{
				blockPos.Set(pos.X + (int)((double)vec.X * num10 + 0.5), pos.Y + (int)((double)vec.Y * num10 + 0.5), pos.Z + (int)((double)vec.Z * num10 + 0.5));
				if (!worldmap.IsValidPos(blockPos))
				{
					break;
				}
				num7 -= 0.25;
				num8 -= 0.25;
				if (!dictionary2.ContainsKey(blockPos))
				{
					Block block = WorldMap.PrefetchBlockAccess.GetBlock(blockPos);
					double blastResistance = block.GetBlastResistance(this, blockPos, vec3f.Set(vec), blastType);
					num7 -= blastResistance;
					if (num7 > 0.0)
					{
						dictionary2[blockPos.Copy()] = block;
						num8 -= blastResistance;
					}
					if (num7 <= 0.0 && blastResistance > 0.0)
					{
						num8 = 0.0;
					}
				}
				if (num7 <= 0.0 && num8 <= 0.0)
				{
					break;
				}
				if (!(num8 > 0.0))
				{
					continue;
				}
				foreach (Entity entity in entitiesAround)
				{
					cuboidd.Set(blockPos.X, blockPos.Y, blockPos.Z, blockPos.X + 1, blockPos.Y + 1, blockPos.Z + 1);
					if (cuboidd.IntersectsOrTouches(entity.SelectionBox, entity.Pos.X, entity.Pos.Y, entity.Pos.Z))
					{
						dictionary[entity.EntityId] = Math.Max(dictionary[entity.EntityId], num8);
					}
				}
			}
		}
		foreach (Entity entity2 in entitiesAround)
		{
			double num13 = dictionary[entity2.EntityId];
			if (num13 != 0.0)
			{
				double num14 = Math.Max(injureRadius / Math.Max(0.5, injureRadius - num13), num13);
				if (!(num14 < 0.25))
				{
					DamageSource damageSource = new DamageSource
					{
						Source = EnumDamageSource.Explosion,
						Type = EnumDamageType.BluntAttack,
						SourcePos = new Vec3d((double)pos.X + 0.5, pos.Y, (double)pos.Z + 0.5)
					};
					entity2.ReceiveDamage(damageSource, (float)num14);
				}
			}
		}
		explosionSmokeParticles.AddBlocks(dictionary2);
		foreach (KeyValuePair<BlockPos, Block> item in dictionary2)
		{
			if (item.Value.BlockMaterial != EnumBlockMaterial.Air)
			{
				item.Value.OnBlockExploded(this, item.Key, pos, blastType, ignitedByPlayerUid);
			}
		}
		WorldMap.BulkBlockAccess.Commit();
		foreach (KeyValuePair<BlockPos, Block> item2 in dictionary2)
		{
			TriggerNeighbourBlocksUpdate(item2.Key);
		}
		string text = "effect/smallexplosion";
		if (destructionRadius > 12.0)
		{
			text = "effect/largeexplosion";
		}
		else if (destructionRadius > 6.0)
		{
			text = "effect/mediumexplosion";
		}
		PlaySoundAt("sounds/" + text, (double)pos.X + 0.5, (double)pos.InternalY + 0.5, (double)pos.Z + 0.5, null, randomizePitch: false, (float)(24.0 * Math.Pow(destructionRadius, 0.5)));
		SimpleParticleProperties explosionFireParticles = ExplosionParticles.ExplosionFireParticles;
		float num15 = (float)destructionRadius / 3f;
		explosionFireParticles.MinPos.Set(pos.X, pos.Y, pos.Z);
		explosionFireParticles.MinQuantity = 100f * num15;
		explosionFireParticles.AddQuantity = (int)(20.0 * Math.Pow(destructionRadius, 0.75));
		SpawnParticles(explosionFireParticles);
		AdvancedParticleProperties explosionFireTrailCubicles = ExplosionParticles.ExplosionFireTrailCubicles;
		explosionFireTrailCubicles.Velocity = new NatFloat[3]
		{
			NatFloat.createUniform(0f, 8f + num15),
			NatFloat.createUniform(3f + num15, 3f + num15),
			NatFloat.createUniform(0f, 8f + num15)
		};
		explosionFireTrailCubicles.basePos.Set((double)pos.X + 0.5, (double)pos.InternalY + 0.5, (double)pos.Z + 0.5);
		explosionFireTrailCubicles.GravityEffect = NatFloat.createUniform(0.5f, 0f);
		explosionFireTrailCubicles.LifeLength = NatFloat.createUniform(1.5f * num15, 0.5f);
		explosionFireTrailCubicles.Quantity = NatFloat.createUniform(30f * num15, 10f);
		float num16 = (float)Math.Pow(num15, 0.75);
		explosionFireTrailCubicles.Size = NatFloat.createUniform(0.5f * num16, 0.2f * num16);
		explosionFireTrailCubicles.SecondaryParticles[0].Size = NatFloat.createUniform(0.25f * (float)Math.Pow(num15, 0.5), 0.05f * num16);
		SpawnParticles(explosionFireTrailCubicles);
		SpawnParticles(explosionSmokeParticles);
		TreeAttribute treeAttribute = new TreeAttribute();
		treeAttribute.SetBlockPos("pos", pos);
		treeAttribute.SetInt("blasttype", (int)blastType);
		treeAttribute.SetDouble("destructionRadius", destructionRadius);
		treeAttribute.SetDouble("injureRadius", injureRadius);
		api.eventapi.PushEvent("onexplosion", treeAttribute);
	}

	public IWorldPlayerData GetWorldPlayerData(int clientID)
	{
		if (!Clients.ContainsKey(clientID))
		{
			return null;
		}
		return Clients[clientID].WorldData;
	}

	public IPlayer NearestPlayer(double x, double y, double z)
	{
		IPlayer result = null;
		float num = float.MaxValue;
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.State == EnumClientState.Playing && value.Entityplayer != null)
			{
				float num2 = value.Position.SquareDistanceTo(x, y, z);
				if (num2 < num)
				{
					num = num2;
					result = value.Player;
				}
			}
		}
		return result;
	}

	public IPlayer[] GetPlayersAround(Vec3d position, float horRange, float vertRange, ActionConsumable<IPlayer> matches = null)
	{
		List<IPlayer> list = new List<IPlayer>();
		float horRangeSq = horRange * horRange;
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.State == EnumClientState.Playing && value.Entityplayer != null && value.Position.InRangeOf(position, horRangeSq, vertRange) && (matches == null || matches(value.Player)))
			{
				list.Add(value.Player);
			}
		}
		return list.ToArray();
	}

	public IPlayer PlayerByUid(string playerUid)
	{
		if (playerUid == null)
		{
			return null;
		}
		PlayersByUid.TryGetValue(playerUid, out var value);
		return value;
	}

	public void EnqueueMainThreadTask(Action task)
	{
		if (task == null)
		{
			throw new ArgumentNullException();
		}
		lock (mainThreadTasksLock)
		{
			mainThreadTasks.Enqueue(task);
		}
	}

	public void ProcessMainThreadTasks()
	{
		if (FrameProfiler != null && FrameProfiler.Enabled)
		{
			FrameProfiler.Enter("mainthreadtasks");
			while (mainThreadTasks.Count > 0)
			{
				Action action;
				lock (mainThreadTasksLock)
				{
					action = mainThreadTasks.Dequeue();
				}
				action();
				if (action.Target != null)
				{
					string code = action.Target.GetType().ToString();
					FrameProfiler.Mark(code);
				}
			}
			FrameProfiler.Leave();
			return;
		}
		while (mainThreadTasks.Count > 0)
		{
			Action action2;
			lock (mainThreadTasksLock)
			{
				action2 = mainThreadTasks.Dequeue();
			}
			action2();
		}
	}

	public void HighlightBlocks(IPlayer player, int slotId, List<BlockPos> blocks, List<int> colors, EnumHighlightBlocksMode mode = EnumHighlightBlocksMode.Absolute, EnumHighlightShape shape = EnumHighlightShape.Arbitrary, float scale = 1f)
	{
		SendHighlightBlocksPacket((IServerPlayer)player, slotId, blocks, colors, mode, shape, scale);
	}

	public void HighlightBlocks(IPlayer player, int slotId, List<BlockPos> blocks, EnumHighlightBlocksMode mode = EnumHighlightBlocksMode.Absolute, EnumHighlightShape shape = EnumHighlightShape.Arbitrary)
	{
		SendHighlightBlocksPacket((IServerPlayer)player, slotId, blocks, null, mode, shape);
	}

	private void InitBasicPacketHandlers()
	{
		PacketHandlers[1] = HandlePlayerIdentification;
		PacketHandlers[11] = HandleRequestJoin;
		PacketHandlers[20] = HandleRequestModeChange;
		PacketHandlers[4] = HandleChatLine;
		PacketHandlers[14] = HandleLeave;
		PacketHandlers[21] = HandleMoveKeyChange;
		PacketHandlers[31] = HandleEntityPacket;
		PacketHandlers[26] = HandleClientLoaded;
		PacketHandlers[29] = HandlePlayerReady;
		PacketHandlers[34] = HandleRequestPositionTcp;
		PacketHandlingOnConnectingAllowed[1] = true;
		PacketHandlingOnConnectingAllowed[14] = true;
		PacketHandlingOnConnectingAllowed[34] = true;
	}

	private void HandleRequestPositionTcp(Packet_Client packet, ConnectedClient player)
	{
		player.FallBackToTcp = true;
		Logger.Debug($"UDP: Client {player.Id} [{player.PlayerName}] requests to get positions over TCP.");
	}

	public void PacketParsingLoop()
	{
		for (int i = 0; i < MainSockets.Length; i++)
		{
			NetServer netServer = MainSockets[i];
			if (netServer != null)
			{
				NetIncomingMessage msg;
				while ((msg = netServer.ReadMessage()) != null)
				{
					ProcessNetMessage(msg);
				}
			}
		}
	}

	private void ProcessNetMessage(NetIncomingMessage msg)
	{
		if (RunPhase == EnumServerRunPhase.Shutdown || exitState.Exit || msg.SenderConnection == null)
		{
			return;
		}
		switch (msg.Type)
		{
		case NetworkMessageType.Connect:
		{
			if (RunPhase == EnumServerRunPhase.Standby)
			{
				EnqueueMainThreadTask(Launch);
			}
			NetConnection senderConnection = msg.SenderConnection;
			lastClientId = GenerateClientId();
			ConnectedClient connectedClient = (senderConnection.client = new ConnectedClient(lastClientId)
			{
				Socket = senderConnection
			});
			connectedClient.Ping.SetTimeoutThreshold(Config.ClientConnectionTimeout);
			connectedClient.Ping.TimeReceivedUdp = ElapsedMilliseconds;
			ClientPackets.Enqueue(new ReceivedClientPacket(connectedClient));
			break;
		}
		case NetworkMessageType.Data:
		{
			ConnectedClient client2 = msg.SenderConnection.client;
			if (client2 != null)
			{
				TotalReceivedBytes += msg.messageLength;
				ParseClientPacket_offthread(client2, msg.message, msg.messageLength);
			}
			break;
		}
		case NetworkMessageType.Disconnect:
		{
			ConnectedClient client = msg.SenderConnection.client;
			if (client != null)
			{
				DisconnectedClientsThisTick.Add(client.Id);
				ClientPackets.Enqueue(new ReceivedClientPacket(client, ""));
			}
			break;
		}
		}
	}

	private void ParseClientPacket_offthread(ConnectedClient client, byte[] data, int length)
	{
		Packet_Client packet_Client = new Packet_Client();
		try
		{
			Packet_ClientSerializer.DeserializeBuffer(data, length, packet_Client);
		}
		catch
		{
			packet_Client = null;
		}
		ReceivedClientPacket item;
		if (packet_Client == null)
		{
			DisconnectedClientsThisTick.Add(client.Id);
			item = new ReceivedClientPacket(client, (client.Player == null) ? "" : "Network error: invalid client packet");
		}
		else
		{
			item = new ReceivedClientPacket(client, packet_Client);
		}
		ClientPackets.Enqueue(item);
	}

	private void HandleClientPacket_mainthread(ReceivedClientPacket cpk)
	{
		ConnectedClient client = cpk.client;
		Packet_Client packet = cpk.packet;
		if (cpk.type == ReceivedClientPacketType.NewConnection)
		{
			if (!DisconnectedClientsThisTick.Contains(client.Id))
			{
				client.Initialise();
				Clients[client.Id] = client;
				string value = (client.IsSinglePlayerClient ? "Dummy connection" : "TCP");
				Logger.Notification($"A Client attempts connecting via {value} on {client.Socket.RemoteEndPoint()}, assigning client id " + client.Id);
			}
			return;
		}
		if (cpk.type == ReceivedClientPacketType.Disconnect)
		{
			if (client.Player != null && cpk.disconnectReason.Length == 0)
			{
				Logger.Event("Client " + client.Id + " disconnected.");
				EventManager.TriggerPlayerLeave(client.Player);
			}
			DisconnectPlayer(client, null, cpk.disconnectReason);
			return;
		}
		if (client.IsNewClient && packet.Id != 1 && packet.Id != 2 && packet.Id != 14 && packet.Id != 34)
		{
			HandleQueryClientPacket(client, packet);
			if (FrameProfiler.Enabled)
			{
				FrameProfiler.Mark("net-read-", packet.Id);
			}
			return;
		}
		ClientPacketHandler<Packet_Client, ConnectedClient> clientPacketHandler = PacketHandlers[packet.Id];
		if (clientPacketHandler != null && (client.Player != null || PacketHandlingOnConnectingAllowed[packet.Id]))
		{
			if (client.Player == null || client.Player.client == client)
			{
				clientPacketHandler(packet, client);
				if (FrameProfiler.Enabled)
				{
					FrameProfiler.Mark("net-read-", packet.Id);
				}
			}
		}
		else
		{
			Logger.Error("Unhandled player packet: {0}, clientid:{1}", packet.Id, client.Id);
			if (FrameProfiler.Enabled)
			{
				FrameProfiler.Mark("net-readerror-", packet.Id);
			}
		}
	}

	private void VerifyPlayerWithAuthServer(Packet_ClientIdentification packet, ConnectedClient client)
	{
		Logger.Debug("Client uid {0}, mp token {1}: Verifying with auth server", packet.PlayerUID, packet.MpToken);
		AuthServerComm.ValidatePlayerWithServer(packet.MpToken, packet.Playername, packet.PlayerUID, client.LoginToken, (EnumServerResponse result, string entitlements, string errorReason) =>
		{
			EnqueueMainThreadTask(() =>
			{
				if (Clients.ContainsKey(client.Id))
				{
					if (result == EnumServerResponse.Good)
					{
						PreFinalizePlayerIdentification(packet, client, entitlements);
						FrameProfiler.Mark("finalizeplayeridentification");
					}
					else if (result == EnumServerResponse.Bad)
					{
						switch (errorReason)
						{
						case "missingmptoken":
						case "missingmptokenv2":
						case "missingaccount":
						case "banned":
						case "serverbanned":
						case "badplayeruid":
							DisconnectPlayer(client, null, Lang.Get("servervalidate-error-" + errorReason));
							break;
						default:
							DisconnectPlayer(client, null, Lang.Get("Auth server reports issue " + errorReason));
							break;
						}
					}
					else
					{
						DisconnectPlayer(client, null, Lang.Get("Unable to check wether your game session is ok, auth server probably offline. Please try again later. If you are the server owner, check server-main.log and server-debug.log for details"));
					}
				}
			});
		});
	}

	private void PreFinalizePlayerIdentification(Packet_ClientIdentification packet, ConnectedClient client, string entitlements)
	{
		int maxClients = Config.MaxClients;
		if (Enumerable.Count(Clients, (KeyValuePair<int, ConnectedClient> c) => c.Value.State.IsAdmitted()) >= maxClients || Clients.Contains((KeyValuePair<int, ConnectedClient> c) => c.Value.State == EnumClientState.Queued))
		{
			ServerPlayerData orCreateServerPlayerData = PlayerDataManager.GetOrCreateServerPlayerData(packet.PlayerUID);
			if (!orCreateServerPlayerData.HasPrivilege(Privilege.controlserver, Config.RolesByCode) && !orCreateServerPlayerData.HasPrivilege("ignoremaxclients", Config.RolesByCode))
			{
				tryEnqueuePlayer(packet, client, entitlements, maxClients);
				return;
			}
		}
		FinalizePlayerIdentification(packet, client, entitlements);
	}

	private void tryEnqueuePlayer(Packet_ClientIdentification packet, ConnectedClient client, string entitlements, int maxClients)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (packet.PlayerUID.Equals(value.SentPlayerUid, StringComparison.InvariantCultureIgnoreCase) && client.Id != value.Id)
			{
				Logger.Event("{0} joined again, killing previous client.", packet.Playername);
				DisconnectPlayer(value);
				break;
			}
		}
		if (Config.MaxClientsInQueue > 0)
		{
			int count;
			lock (ConnectionQueue)
			{
				count = ConnectionQueue.Count;
			}
			if (count < Config.MaxClientsInQueue)
			{
				client.State = EnumClientState.Queued;
				int count2;
				lock (ConnectionQueue)
				{
					foreach (QueuedClient item in ConnectionQueue)
					{
						if (item.Client.Id == client.Id)
						{
							return;
						}
					}
					ConnectionQueue.Add(new QueuedClient(client, packet, entitlements));
					count2 = ConnectionQueue.Count;
				}
				Packet_Server packet2 = new Packet_Server
				{
					Id = 82,
					QueuePacket = new Packet_QueuePacket
					{
						Position = count2
					}
				};
				Logger.Notification($"Player {packet.Playername} was put into the connection queue at position {count2}");
				SendPacket(client.Id, packet2);
				return;
			}
		}
		DisconnectPlayer(client, null, Lang.Get("Server and Connection queue is full ({0} max clients, {1} queue size). Please try again later.", maxClients, Config.MaxClientsInQueue));
	}

	private void FinalizePlayerIdentification(Packet_ClientIdentification packet, ConnectedClient client, string entitlements)
	{
		client.State = EnumClientState.Admitted;
		if (RunPhase == EnumServerRunPhase.Shutdown || (client.Socket is TcpNetConnection { TcpSocket: var tcpSocket } && (tcpSocket == null || !tcpSocket.Connected)))
		{
			return;
		}
		string playername = packet.Playername;
		Logger.VerboseDebug("Received identification packet from " + playername);
		bool flag = false;
		foreach (ConnectedClient value2 in Clients.Values)
		{
			bool flag2 = packet.PlayerUID.Equals(value2.SentPlayerUid, StringComparison.InvariantCultureIgnoreCase);
			if (flag2 && client.Id != value2.Id)
			{
				Logger.Event("{0} joined again, killing previous client.", packet.Playername);
				DisconnectPlayer(value2);
				break;
			}
			if (flag2)
			{
				flag = true;
			}
		}
		if (!flag)
		{
			Logger.Notification("Was about to finalize player ident, but player {0} is no longer online. Ignoring.", packet.Playername);
			return;
		}
		if (client.ServerDidReceiveUdp)
		{
			Logger.Debug($"UDP: Client {client.Id} did send UDP");
		}
		else if (!client.FallBackToTcp)
		{
			TyronThreadPool.QueueTask(async () =>
			{
				for (int i = 0; i < 20; i++)
				{
					await Task.Delay(500);
					if (client.State == EnumClientState.Offline)
					{
						return;
					}
					if (client.ServerDidReceiveUdp)
					{
						break;
					}
				}
				if (!client.ServerDidReceiveUdp)
				{
					Logger.Debug($"UDP: Server did not receive any UDP packets from Client {client.Id}, telling the client to send positions over TCP.");
					Packet_Server packet2 = new Packet_Server
					{
						Id = 78
					};
					SendPacket(client.Id, packet2);
					client.FallBackToTcp = true;
					ServerUdpNetwork.connectingClients.Remove(client.LoginToken);
				}
				else
				{
					Logger.Debug($"UDP: Client {client.Id} did send UDP");
				}
			}, $"UDPServerDidReceiveUdp_{client.Id}");
		}
		string playerUID = packet.PlayerUID;
		client.LoadOrCreatePlayerData(this, playername, playerUID);
		client.Player.client = client;
		client.WorldData.EntityPlayer.WatchedAttributes.SetString("playerUID", playerUID);
		client.WorldData.Viewdistance = packet.ViewDistance;
		client.WorldData.RenderMetaBlocks = packet.RenderMetaBlocks > 0;
		if (client.Socket is TcpNetConnection { TcpSocket: { } tcpSocket2 } tcpNetConnection2)
		{
			tcpNetConnection2.SetLengthLimit(client.WorldData.GameMode == EnumGameMode.Creative);
			tcpSocket2.ReceiveBufferSize = 65536;
			if (tcpSocket2.ReceiveBufferSize > 65536)
			{
				tcpSocket2.ReceiveBufferSize = 32768;
			}
		}
		if (entitlements != null)
		{
			string[] array = entitlements.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string text in array)
			{
				client.Player.Entitlements.Add(new Entitlement
				{
					Code = text,
					Name = Lang.Get("entitlement-" + text)
				});
			}
		}
		if (client.Player.HasPrivilege("staffentitlement"))
		{
			client.Player.Entitlements.Add(new Entitlement
			{
				Code = "staff",
				Name = Lang.Get("entitlement-staff")
			});
		}
		PlayersByUid[playerUID] = client.Player;
		EntityPos entityPos = (client.IsNewEntityPlayer ? GetSpawnPosition(playerUID) : GetJoinPosition(client));
		if (!client.IsNewEntityPlayer && entityPos.X == 0.0 && entityPos.Y == 0.0 && entityPos.Z == 0.0 && entityPos.Pitch == 0f && entityPos.Roll == 0f)
		{
			Logger.Warning("Player {0} is at position 0/0/0? Did something get corrupted? Placing player to the global default spawn position...", client.PlayerName);
			entityPos = GetSpawnPosition(playerUID);
		}
		if (client.IsSinglePlayerClient && Config.MaxChunkRadius != packet.ViewDistance / 32)
		{
			Config.MaxChunkRadius = Math.Max(Config.MaxChunkRadius, packet.ViewDistance / 32);
			Logger.Notification($"Upped server view distance to: {packet.ViewDistance}, because player is in singleplayer");
		}
		SendPacket(client.Player, new Packet_Server
		{
			Id = 51,
			EntityPosition = ServerPackets.getEntityPositionPacket(GetSpawnPosition(playerUID, onlyGlobalDefaultSpawn: true), client.Entityplayer, 0)
		});
		if (World.Config.GetString("spawnRadius").ToInt() > 0 && client.IsNewEntityPlayer)
		{
			Logger.Notification("Delayed join, attempt random spawn position.");
			SendLevelProgress(client.Player, 99, Lang.Get("Loading spawn chunk..."));
			SendServerIdentification(client.Player);
			SpawnPlayerRandomlyAround(client, playername, entityPos, 15);
			client.IsNewClient = false;
			return;
		}
		if (WorldMap.IsPosLoaded(entityPos.AsBlockPos))
		{
			SendServerIdentification(client.Player);
			client.Entityplayer.Pos.SetFrom(entityPos);
			SpawnEntity(client.Entityplayer);
			client.Entityplayer.SetName(playername);
			Logger.Notification("Placing {0} at {1} {2} {3}", playername, entityPos.X, entityPos.Y, entityPos.Z);
			SendServerReady(client.Player);
		}
		else
		{
			Logger.Notification("Delayed join, need to load one spawn chunk first.");
			SendLevelProgress(client.Player, 99, Lang.Get("Loading spawn chunk..."));
			SendServerIdentification(client.Player);
			KeyValuePair<HorRectanglei, ChunkLoadOptions> item = new KeyValuePair<HorRectanglei, ChunkLoadOptions>(new HorRectanglei((int)entityPos.X / 32, (int)entityPos.Z / 32, (int)entityPos.X / 32, (int)entityPos.Z / 32), new ChunkLoadOptions
			{
				OnLoaded = () =>
				{
					Clients.TryGetValue(client.Id, out var value);
					if (value != null)
					{
						client.CurrentChunkSentRadius = 0;
						EntityPos entityPos2 = (client.IsNewEntityPlayer ? GetSpawnPosition(playerUID) : GetJoinPosition(value));
						client.Entityplayer.Pos.SetFrom(entityPos2);
						SpawnEntity(client.Entityplayer);
						client.WorldData.EntityPlayer.SetName(playername);
						Logger.Notification("Placing {0} at {1} {2} {3}", playername, entityPos2.X, entityPos2.Y, entityPos2.Z);
						SendServerReady(client.Player);
					}
				}
			});
			fastChunkQueue.Enqueue(item);
			Logger.VerboseDebug("Spawn chunk load request enqueued.");
		}
		client.IsNewClient = false;
	}

	public void LocateRandomPosition(Vec3d centerPos, float radius, int tries, ActionConsumable<BlockPos> testThisPosition, Action<BlockPos> onSearchOver)
	{
		Vec3d targetPos = centerPos.Clone();
		float num = (float)Math.PI * 2f * (float)rand.Value.NextDouble();
		float num2 = GameMath.Sqrt(rand.Value.NextDouble()) * radius;
		targetPos.X += Math.Sin(num) * (double)num2;
		targetPos.Z += Math.Cos(num) * (double)num2;
		BlockPos asBlockPos = targetPos.AsBlockPos;
		if (tries <= 0)
		{
			onSearchOver(null);
			return;
		}
		if (WorldMap.IsPosLoaded(asBlockPos) && testThisPosition(asBlockPos))
		{
			onSearchOver(asBlockPos);
			return;
		}
		KeyValuePair<HorRectanglei, ChunkLoadOptions> item = new KeyValuePair<HorRectanglei, ChunkLoadOptions>(new HorRectanglei((int)targetPos.X / 32, (int)targetPos.Z / 32, (int)targetPos.X / 32, (int)targetPos.Z / 32), new ChunkLoadOptions
		{
			OnLoaded = () =>
			{
				BlockPos asBlockPos2 = targetPos.AsBlockPos;
				if (WorldMap.IsPosLoaded(asBlockPos2) && testThisPosition(asBlockPos2))
				{
					onSearchOver(asBlockPos2);
				}
				else
				{
					LocateRandomPosition(centerPos, radius, tries - 1, testThisPosition, onSearchOver);
				}
			}
		});
		Logger.Event("Searching for chunk column suitable for player spawn");
		Logger.StoryEvent("...");
		fastChunkQueue.Enqueue(item);
	}

	private void SpawnPlayerRandomlyAround(ConnectedClient client, string playername, EntityPos centerPos, int tries)
	{
		float radius = World.Config.GetString("spawnRadius").ToInt();
		LocateRandomPosition(centerPos.XYZ, radius, tries, (BlockPos pos) => ServerSystemSupplyChunks.AdjustForSaveSpawnSpot(this, pos, client.Player, rand.Value), (BlockPos pos) =>
		{
			EntityPos entityPos = centerPos.Copy();
			if (pos == null)
			{
				entityPos.X += rand.Value.NextDouble() * 2.0 * (double)radius - (double)radius + 0.5;
				entityPos.Z += rand.Value.NextDouble() * 2.0 * (double)radius - (double)radius + 0.5;
			}
			else
			{
				entityPos.X += (double)pos.X + 0.5 - entityPos.X;
				entityPos.Y += (double)pos.Y - entityPos.Y;
				entityPos.Z += (double)pos.Z + 0.5 - entityPos.Z;
			}
			SpawnPlayerHere(client, playername, entityPos);
		});
	}

	private void SpawnPlayerHere(ConnectedClient client, string playername, EntityPos targetPos)
	{
		Clients.TryGetValue(client.Id, out var value);
		if (value != null)
		{
			client.CurrentChunkSentRadius = 0;
			client.Entityplayer.Pos.SetFrom(targetPos);
			SpawnEntity(client.Entityplayer);
			client.WorldData.EntityPlayer.SetName(playername);
			Logger.Notification("Placing {0} at {1} {2} {3}", playername, targetPos.X, targetPos.Y, targetPos.Z);
			SendServerReady(client.Player);
		}
	}

	public void SendArbitraryUdpPacket(Packet_UdpPacket packet, params IServerPlayer[] players)
	{
		for (int i = 0; i < players.Length; i++)
		{
			SendPacket(((ServerPlayer)players[i]).client, packet);
		}
	}

	public void SendArbitraryPacket(byte[] data, params IServerPlayer[] players)
	{
		for (int i = 0; i < players.Length; i++)
		{
			SendPacket(players[i], data);
		}
	}

	public void SendArbitraryPacket(Packet_Server packet, params IServerPlayer[] players)
	{
		Serialize_(packet);
		foreach (IServerPlayer serverPlayer in players)
		{
			if (serverPlayer == null || serverPlayer.ConnectionState == EnumClientState.Offline)
			{
				break;
			}
			SendPacket(serverPlayer.ClientId, reusableBuffer);
		}
	}

	internal void SendBlockEntity(IServerPlayer targetPlayer, BlockEntity blockentity)
	{
		Packet_BlockEntity[] array = new Packet_BlockEntity[1];
		int num = 0;
		MemoryStream memoryStream = new MemoryStream();
		BinaryWriter stream = new BinaryWriter(memoryStream);
		TreeAttribute treeAttribute = new TreeAttribute();
		blockentity.ToTreeAttributes(treeAttribute);
		treeAttribute.ToBytes(stream);
		array[num] = new Packet_BlockEntity
		{
			Classname = ClassRegistry.blockEntityTypeToClassnameMapping[blockentity.GetType()],
			Data = memoryStream.ToArray(),
			PosX = blockentity.Pos.X,
			PosY = blockentity.Pos.InternalY,
			PosZ = blockentity.Pos.Z
		};
		Packet_BlockEntities packet_BlockEntities = new Packet_BlockEntities();
		packet_BlockEntities.SetBlockEntitites(array);
		SendPacket(targetPlayer, new Packet_Server
		{
			Id = 48,
			BlockEntities = packet_BlockEntities
		});
	}

	public void SendBlockEntityMessagePacket(IServerPlayer player, int x, int y, int z, int packetId, byte[] data = null)
	{
		Packet_BlockEntityMessage packet_BlockEntityMessage = new Packet_BlockEntityMessage
		{
			PacketId = packetId,
			X = x,
			Y = y,
			Z = z
		};
		packet_BlockEntityMessage.SetData(data);
		SendPacket(player, new Packet_Server
		{
			Id = 44,
			BlockEntityMessage = packet_BlockEntityMessage
		});
	}

	public void SendEntityPacket(IServerPlayer player, long entityId, int packetId, byte[] data = null)
	{
		Packet_EntityPacket packet_EntityPacket = new Packet_EntityPacket
		{
			Packetid = packetId,
			EntityId = entityId
		};
		packet_EntityPacket.SetData(data);
		SendPacket(player, new Packet_Server
		{
			Id = 67,
			EntityPacket = packet_EntityPacket
		});
	}

	public void BroadcastEntityPacket(long entityId, int packetId, byte[] data = null)
	{
		Packet_EntityPacket packet_EntityPacket = new Packet_EntityPacket
		{
			Packetid = packetId,
			EntityId = entityId
		};
		packet_EntityPacket.SetData(data);
		BroadcastPacket(new Packet_Server
		{
			Id = 67,
			EntityPacket = packet_EntityPacket
		});
	}

	public void BroadcastBlockEntityPacket(int x, int y, int z, int packetId, byte[] data = null, params IServerPlayer[] skipPlayers)
	{
		Packet_BlockEntityMessage packet_BlockEntityMessage = new Packet_BlockEntityMessage
		{
			PacketId = packetId,
			X = x,
			Y = y,
			Z = z
		};
		packet_BlockEntityMessage.SetData(data);
		BroadcastPacket(new Packet_Server
		{
			Id = 44,
			BlockEntityMessage = packet_BlockEntityMessage
		}, skipPlayers);
	}

	public void SendMessageToGeneral(string message, EnumChatType chatType, IServerPlayer exceptPlayer = null, string data = null)
	{
		SendMessageToGroup(GlobalConstants.GeneralChatGroup, message, chatType, exceptPlayer, data);
	}

	public void SendMessageToGroup(int groupid, string message, EnumChatType chatType, IServerPlayer exceptPlayer = null, string data = null)
	{
		bool flag = groupid == GlobalConstants.AllChatGroups || groupid == GlobalConstants.GeneralChatGroup || groupid == GlobalConstants.CurrentChatGroup || groupid == GlobalConstants.ServerInfoChatGroup || groupid == GlobalConstants.InfoLogChatGroup;
		foreach (ConnectedClient value in Clients.Values)
		{
			if ((exceptPlayer == null || value.Id != exceptPlayer.ClientId) && value.State.ConnectedOrPlaying() && (flag || value.ServerData.PlayerGroupMemberShips.ContainsKey(groupid)))
			{
				SendMessage(value.Player, groupid, message, chatType, data);
			}
		}
	}

	public void BroadcastMessageToAllGroups(string message, EnumChatType chatType, string data = null)
	{
		Logger.Notification("Message to all in group " + GlobalConstants.GeneralChatGroup + ": {0}", message);
		foreach (ConnectedClient value in Clients.Values)
		{
			SendMessage(value.Player, GlobalConstants.AllChatGroups, message, chatType, data);
		}
	}

	public void SendMessageToCurrentCh(IServerPlayer player, string message, EnumChatType chatType, string data = null)
	{
		SendMessage(player, GlobalConstants.CurrentChatGroup, message, chatType);
	}

	public void ReplyMessage(IServerPlayer player, string message, EnumChatType chatType, string data = null)
	{
		SendMessage(player, GlobalConstants.CurrentChatGroup, message, chatType, data);
	}

	public void SendMessage(Caller caller, string message, EnumChatType chatType, string data = null)
	{
		SendMessage(caller.Player as IServerPlayer, caller.FromChatGroupId, message, chatType, data);
	}

	public void SendMessage(IServerPlayer player, int groupid, string message, EnumChatType chatType, string data = null)
	{
		if (groupid == GlobalConstants.ConsoleGroup)
		{
			Logger.Notification(message);
		}
		else
		{
			SendPacket(player, ServerPackets.ChatLine(groupid, message, chatType, data));
		}
	}

	public void SendIngameError(IServerPlayer player, string errorCode, string text = null, params object[] langparams)
	{
		SendPacket(player, ServerPackets.IngameError(errorCode, text, langparams));
	}

	public void SendIngameDiscovery(IServerPlayer player, string discoveryCode, string text = null, params object[] langparams)
	{
		SendPacket(player, ServerPackets.IngameDiscovery(discoveryCode, text, langparams));
	}

	[Obsolete("Use Serialize_ and reusableBuffer where possible, for better performance")]
	public byte[] Serialize(Packet_Server p)
	{
		return Packet_ServerSerializer.SerializeToBytes(p);
	}

	internal int Serialize_(Packet_Server p)
	{
		if (reusableBuffer == null)
		{
			reusableBuffer = new BoxedPacket();
			lock (reusableBuffersDisposalList)
			{
				reusableBuffersDisposalList.Add(reusableBuffer);
			}
		}
		return reusableBuffer.Serialize(p);
	}

	internal void SendSetBlock(int blockId, int posX, int posY, int posZ, int exceptClientid = -1, bool exchangeOnly = false)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (exceptClientid != value.Id && value.State.ConnectedOrPlaying() && value.Player != null)
			{
				SendSetBlock(value.Player, blockId, posX, posY, posZ, exchangeOnly);
			}
		}
	}

	internal void BroadcastUnloadMapRegion(long index)
	{
		WorldMap.MapRegionPosFromIndex2D(index, out var x, out var z);
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.State.ConnectedOrPlaying() && value.Player != null)
			{
				Packet_UnloadMapRegion unloadMapRegion = new Packet_UnloadMapRegion
				{
					RegionX = x,
					RegionZ = z
				};
				SendPacket(value.Player, new Packet_Server
				{
					Id = 74,
					UnloadMapRegion = unloadMapRegion
				});
				value.RemoveMapRegionSent(index);
			}
		}
	}

	internal void SendSetBlock(IServerPlayer player, int blockId, int posX, int posY, int posZ, bool exchangeOnly = false)
	{
		if (Clients[player.ClientId].DidSendChunk(WorldMap.ChunkIndex3D(posX / 32, posY / 32, posZ / 32)))
		{
			if (exchangeOnly)
			{
				Packet_ServerExchangeBlock exchangeBlock = new Packet_ServerExchangeBlock
				{
					X = posX,
					Y = posY,
					Z = posZ,
					BlockType = blockId
				};
				SendPacket(player, new Packet_Server
				{
					Id = 58,
					ExchangeBlock = exchangeBlock
				});
			}
			else
			{
				Packet_ServerSetBlock setBlock = new Packet_ServerSetBlock
				{
					X = posX,
					Y = posY,
					Z = posZ,
					BlockType = blockId
				};
				SendPacket(player, new Packet_Server
				{
					Id = 7,
					SetBlock = setBlock
				});
			}
		}
	}

	public void SendSetBlocksPacket(List<BlockPos> positions, int packetId)
	{
		if (positions.Count != 0)
		{
			byte[] setBlocks = BlockTypeNet.PackSetBlocksList(positions, WorldMap.RelaxedBlockAccess);
			Packet_ServerSetBlocks packet_ServerSetBlocks = new Packet_ServerSetBlocks();
			packet_ServerSetBlocks.SetSetBlocks(setBlocks);
			BroadcastPacket(new Packet_Server
			{
				Id = packetId,
				SetBlocks = packet_ServerSetBlocks
			});
		}
	}

	public void SendSetDecorsPackets(List<BlockPos> positions)
	{
		if (positions.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<long, WorldChunk> item in WorldMap.PositionsToUniqueChunks(positions))
		{
			if (item.Value != null)
			{
				byte[] setDecors = BlockTypeNet.PackSetDecorsList(item.Value, item.Key, WorldMap.RelaxedBlockAccess);
				Packet_ServerSetDecors packet_ServerSetDecors = new Packet_ServerSetDecors();
				packet_ServerSetDecors.SetSetDecors(setDecors);
				BroadcastPacket(new Packet_Server
				{
					Id = 71,
					SetDecors = packet_ServerSetDecors
				});
			}
		}
	}

	public void SendHighlightBlocksPacket(IServerPlayer player, int slotId, List<BlockPos> justBlocks, List<int> colors, EnumHighlightBlocksMode mode, EnumHighlightShape shape, float scale = 1f)
	{
		byte[] blocks = BlockTypeNet.PackBlocksPositions(justBlocks);
		Packet_HighlightBlocks packet_HighlightBlocks = new Packet_HighlightBlocks();
		packet_HighlightBlocks.SetBlocks(blocks);
		packet_HighlightBlocks.Mode = (int)mode;
		packet_HighlightBlocks.Shape = (int)shape;
		packet_HighlightBlocks.Slotid = slotId;
		packet_HighlightBlocks.Scale = CollectibleNet.SerializeFloatVeryPrecise(scale);
		if (colors != null)
		{
			packet_HighlightBlocks.SetColors(colors.ToArray());
		}
		SendPacket(player, new Packet_Server
		{
			Id = 52,
			HighlightBlocks = packet_HighlightBlocks
		});
	}

	public void SendSound(IServerPlayer player, AssetLocation location, double x, double y, double z, float pitch, float range, float volume, EnumSoundType soundType = EnumSoundType.Sound)
	{
		Packet_ServerSound sound = new Packet_ServerSound
		{
			Name = location.ToString(),
			X = CollectibleNet.SerializeFloat((float)x),
			Y = CollectibleNet.SerializeFloat((float)y),
			Z = CollectibleNet.SerializeFloat((float)z),
			Range = CollectibleNet.SerializeFloat(range),
			Pitch = CollectibleNet.SerializeFloatPrecise(pitch),
			Volume = CollectibleNet.SerializeFloatPrecise(volume),
			SoundType = (int)soundType
		};
		SendPacket(player, new Packet_Server
		{
			Id = 18,
			Sound = sound
		});
	}

	public void BroadcastPacket(Packet_Server packet, params IServerPlayer[] skipPlayers)
	{
		BroadcastArbitraryPacket(packet, skipPlayers);
		if (doNetBenchmark)
		{
			recordInBenchmark(packet.Id, reusableBuffer.Length);
		}
	}

	internal void BroadcastArbitraryPacket(byte[] data, params IServerPlayer[] skipPlayers)
	{
		foreach (ConnectedClient client in Clients.Values)
		{
			if (client.State != EnumClientState.Offline && client.State != EnumClientState.Queued && (skipPlayers == null || !skipPlayers.Any((IServerPlayer plr) => plr?.ClientId == client.Id)))
			{
				SendPacket(client.Player, data);
			}
		}
	}

	internal void BroadcastArbitraryPacket(Packet_Server packet, params IServerPlayer[] skipPlayers)
	{
		Serialize_(packet);
		byte[] array = null;
		bool compressed = false;
		foreach (ConnectedClient client in Clients.Values)
		{
			if (client.State != EnumClientState.Offline && client.State != EnumClientState.Queued && (skipPlayers == null || !skipPlayers.Any((IServerPlayer plr) => plr?.ClientId == client.Id)))
			{
				if (array == null)
				{
					array = client.Socket.PreparePacketForSending(reusableBuffer, Config.CompressPackets, out compressed);
				}
				SendPreparedPacket(client, array, compressed);
				if (client.Socket is DummyNetConnection)
				{
					array = null;
				}
			}
		}
	}

	internal void BroadcastArbitraryUdpPacket(Packet_UdpPacket data, params IServerPlayer[] skipPlayers)
	{
		foreach (ConnectedClient client in Clients.Values)
		{
			if (client.State != EnumClientState.Offline && client.State != EnumClientState.Queued && (skipPlayers == null || skipPlayers.All((IServerPlayer plr) => plr?.ClientId != client.Id)))
			{
				SendPacket(client, data);
			}
		}
	}

	private void recordInBenchmark(int packetId, int dataLength)
	{
		if (packetBenchmark.ContainsKey(packetId))
		{
			packetBenchmark[packetId]++;
			packetBenchmarkBytes[packetId] += dataLength;
		}
		else
		{
			packetBenchmark[packetId] = 1;
			packetBenchmarkBytes[packetId] = dataLength;
		}
	}

	public void SendPacket(int clientId, Packet_Server packet)
	{
		int dataLength = Serialize_(packet);
		if (doNetBenchmark)
		{
			recordInBenchmark(packet.Id, dataLength);
		}
		SendPacket(clientId, reusableBuffer);
	}

	public void SendPacketFast(int clientId, Packet_Server packet)
	{
		if (!Clients.TryGetValue(clientId, out var value) || !value.IsSinglePlayerClient || !DummyNetConnection.SendServerPacketDirectly(packet))
		{
			SendPacket(clientId, packet);
		}
	}

	public void SendPacket(ConnectedClient client, Packet_UdpPacket packet)
	{
		ServerUdpNetwork.SendPacket_Threadsafe(client, packet);
	}

	internal void SendPacketBlocking(ConnectedClient client, Packet_UdpPacket packet)
	{
		if (client.FallBackToTcp)
		{
			Packet_Server packet2 = new Packet_Server
			{
				Id = 79,
				UdpPacket = packet
			};
			SendPacket(client.Id, packet2);
		}
		else if (client.IsSinglePlayerClient)
		{
			UdpSockets[0].SendToClient(client.Id, packet);
		}
		else
		{
			int byteCount = UdpSockets[1].SendToClient(client.Id, packet);
			UpdateUdpStatsAndBenchmark(packet, byteCount);
		}
	}

	internal void UpdateUdpStatsAndBenchmark(Packet_UdpPacket packet, int byteCount)
	{
		StatsCollector[StatsCollectorIndex].statTotalUdpPackets++;
		StatsCollector[StatsCollectorIndex].statTotalUdpPacketsLength += byteCount;
		TotalSentBytesUdp += byteCount;
		if (doNetBenchmark)
		{
			if (!udpPacketBenchmark.TryAdd(packet.Id, 1))
			{
				udpPacketBenchmark[packet.Id]++;
				udpPacketBenchmarkBytes[packet.Id] += byteCount;
			}
			else
			{
				udpPacketBenchmarkBytes[packet.Id] = byteCount;
			}
		}
	}

	public void SendPacket(IServerPlayer player, Packet_Server packet)
	{
		if (player != null && player.ConnectionState != EnumClientState.Offline)
		{
			SendPacket(player.ClientId, packet);
		}
	}

	private void SendPacket(IServerPlayer player, byte[] packetBytes)
	{
		if (player != null && player.ConnectionState != EnumClientState.Offline)
		{
			SendPacket(player.ClientId, packetBytes);
		}
	}

	private void SendPacket(int clientId, byte[] packetBytes)
	{
		bool compressed = false;
		if (packetBytes.Length > 5120 && Config.CompressPackets && !Clients[clientId].IsSinglePlayerClient)
		{
			packetBytes = Compression.Compress(packetBytes);
			compressed = true;
		}
		StatsCollection obj = StatsCollector[StatsCollectorIndex];
		obj.statTotalPackets++;
		obj.statTotalPacketsLength += packetBytes.Length;
		TotalSentBytes += packetBytes.Length;
		EnumSendResult enumSendResult = EnumSendResult.Ok;
		if (!Clients.TryGetValue(clientId, out var client))
		{
			return;
		}
		try
		{
			enumSendResult = client.Socket.Send(packetBytes, compressed);
		}
		catch (Exception e)
		{
			Logger.Error("Network exception:.");
			Logger.Error(e);
			DisconnectPlayer(client, "Lost connection");
			return;
		}
		if (enumSendResult == EnumSendResult.Disconnected)
		{
			EnqueueMainThreadTask(() =>
			{
				DisconnectPlayer(client, "Lost connection/disconnected");
				FrameProfiler.Mark("disconnectplayer");
			});
		}
	}

	private void SendPacket(int clientId, BoxedPacket box)
	{
		if (Clients.TryGetValue(clientId, out var value))
		{
			EnumSendResult result = value.Socket.HiPerformanceSend(box, Logger, Config.CompressPackets);
			HandleSendingResult(result, box.LengthSent, value);
		}
	}

	private void SendPreparedPacket(ConnectedClient client, byte[] packetBytes, bool compressed)
	{
		EnumSendResult result = client.Socket.SendPreparedPacket(packetBytes, compressed, Logger);
		HandleSendingResult(result, packetBytes.Length, client);
	}

	private void HandleSendingResult(EnumSendResult result, int lengthSent, ConnectedClient client)
	{
		switch (result)
		{
		case EnumSendResult.Ok:
		{
			StatsCollection obj = StatsCollector[StatsCollectorIndex];
			obj.statTotalPackets++;
			obj.statTotalPacketsLength += lengthSent;
			TotalSentBytes += lengthSent;
			break;
		}
		case EnumSendResult.Error:
			DisconnectPlayer(client, "Lost connection");
			break;
		default:
			EnqueueMainThreadTask(() =>
			{
				DisconnectPlayer(client, "Lost connection/disconnected");
				FrameProfiler.Mark("disconnectplayer");
			});
			break;
		}
	}

	private void SendPlayerEntity(IServerPlayer player, FastMemoryStream ms)
	{
		SendPacket(player, new Packet_Server
		{
			Id = 40,
			Entities = new Packet_Entities
			{
				Entities = new Packet_Entity[1] { ServerPackets.GetEntityPacket(player.Entity, ms, new BinaryWriter(ms)) },
				EntitiesCount = 1,
				EntitiesLength = 1
			}
		});
	}

	public void SendServerAssets(IServerPlayer player)
	{
		if (player == null || player.ConnectionState == EnumClientState.Offline)
		{
			return;
		}
		if (serverAssetsPacket.Length == 0)
		{
			if (serverAssetsPacket.packet == null)
			{
				WaitOnBuildServerAssetsPacket();
			}
			if (serverAssetsPacket.Length == 0)
			{
				if (Clients.TryGetValue(player.ClientId, out var value) && value.IsSinglePlayerClient)
				{
					if (serverAssetsSentLocally || DummyNetConnection.SendServerAssetsPacketDirectly(serverAssetsPacket.packet))
					{
						return;
					}
				}
				else
				{
					serverAssetsPacket.Serialize(serverAssetsPacket.packet);
				}
			}
		}
		SendPacket(player.ClientId, serverAssetsPacket);
	}

	private void StartBuildServerAssetsPacket()
	{
		TyronThreadPool.QueueTask((Action)BuildServerAssetsPacket, "serverassetspacket");
		Logger.VerboseDebug("Starting to build server assets packet");
	}

	private void WaitOnBuildServerAssetsPacket()
	{
		int num = 500;
		while (serverAssetsPacket.Length == 0 && serverAssetsPacket.packet == null && num-- > 0)
		{
			Thread.Sleep(20);
		}
		if (serverAssetsPacket.Length == 0 && serverAssetsPacket.packet == null)
		{
			Logger.Error("Waiting on buildServerAssetsPacket thread for longer than 10 seconds timeout, trying again ... this may take a while!");
			BuildServerAssetsPacket();
		}
	}

	private void BuildServerAssetsPacket()
	{
		try
		{
			using FastMemoryStream ms = new FastMemoryStream();
			Packet_ServerAssets packet_ServerAssets = new Packet_ServerAssets();
			List<Packet_BlockType> list = new List<Packet_BlockType>();
			int num = 0;
			foreach (Block block in Blocks)
			{
				try
				{
					list.Add(BlockTypeNet.GetBlockTypePacket(block, api.ClassRegistry, ms));
					block.FreeRAMServer();
				}
				catch (Exception e)
				{
					Logger.Fatal("Failed networking encoding block {0}:", block.Code);
					Logger.Fatal(e);
					throw new Exception("SendServerAssets failed. See log files.");
				}
				if (num++ % 1000 == 999)
				{
					Thread.Sleep(5);
				}
			}
			packet_ServerAssets.SetBlocks(list.ToArray());
			Thread.Sleep(5);
			List<Packet_ItemType> list2 = new List<Packet_ItemType>();
			for (int i = 0; i < Items.Count; i++)
			{
				Item item = Items[i];
				if (item != null && !(item.Code == null))
				{
					try
					{
						list2.Add(ItemTypeNet.GetItemTypePacket(item, api.ClassRegistry, ms));
						item.FreeRAMServer();
					}
					catch (Exception e2)
					{
						Logger.Fatal("Failed network encoding block {0}:", item.Code);
						Logger.Fatal(e2);
						throw new Exception("SendServerAssets failed. See log files.");
					}
					if (i % 1000 == 999)
					{
						Thread.Sleep(5);
					}
				}
			}
			packet_ServerAssets.SetItems(list2.ToArray());
			Thread.Sleep(5);
			Packet_EntityType[] array = new Packet_EntityType[EntityTypesByCode.Count];
			num = 0;
			foreach (EntityProperties entityType in EntityTypes)
			{
				try
				{
					array[num++] = EntityTypeNet.EntityPropertiesToPacket(entityType, ms);
					entityType.Client?.FreeRAMServer();
				}
				catch (Exception e3)
				{
					Logger.Fatal("Failed network encoding entity type {0}:", entityType?.Code);
					Logger.Fatal(e3);
					throw new Exception("SendServerAssets failed. See log files.");
				}
				if (num % 100 == 99)
				{
					Thread.Sleep(5);
				}
			}
			packet_ServerAssets.SetEntities(array);
			Thread.Sleep(5);
			Packet_Recipes[] array2 = new Packet_Recipes[recipeRegistries.Count];
			num = 0;
			foreach (KeyValuePair<string, RecipeRegistryBase> recipeRegistry in recipeRegistries)
			{
				array2[num++] = RecipesToPacket(recipeRegistry.Value, recipeRegistry.Key, this, ms);
			}
			packet_ServerAssets.SetRecipes(array2);
			Thread.Sleep(5);
			Packet_TagRegistry[] array3 = new Packet_TagRegistry[2];
			string[] array4 = new string[EntityTagRegistry.nameToTagBitOffset.Count];
			ushort[] array5 = new ushort[EntityTagRegistry.nameToTagBitOffset.Count];
			int num2 = 0;
			string key;
			ushort value;
			foreach (KeyValuePair<string, ushort> item2 in EntityTagRegistry.nameToTagBitOffset)
			{
				item2.Deconstruct(out key, out value);
				string text = key;
				ushort num3 = value;
				array4[num2] = text;
				array5[num2] = num3;
				num2++;
			}
			Array.Sort(array5, array4);
			Packet_TagRegistry packet_TagRegistry = new Packet_TagRegistry();
			packet_TagRegistry.RegistryName = "entities";
			packet_TagRegistry.SetTagNames(array4);
			array3[0] = packet_TagRegistry;
			string[] array6 = new string[CollectibleTagRegistry.nameToTagHandle.Count];
			ushort[] array7 = new ushort[CollectibleTagRegistry.nameToTagHandle.Count];
			int num4 = 0;
			foreach (KeyValuePair<string, ushort> item3 in CollectibleTagRegistry.nameToTagHandle)
			{
				item3.Deconstruct(out key, out value);
				string text2 = key;
				ushort num5 = value;
				array6[num4] = text2;
				array7[num4] = num5;
				num4++;
			}
			Array.Sort(array7, array6);
			Packet_TagRegistry packet_TagRegistry2 = new Packet_TagRegistry();
			packet_TagRegistry2.RegistryName = "collectibles";
			packet_TagRegistry2.SetTagNames(array6);
			array3[1] = packet_TagRegistry2;
			packet_ServerAssets.SetTagRegistries(array3);
			Thread.Sleep(5);
			Packet_Server packet_Server = new Packet_Server
			{
				Id = 19,
				Assets = packet_ServerAssets
			};
			Logger.VerboseDebug("Finished building server assets packet");
			if (IsDedicatedServer)
			{
				serverAssetsPacket.Serialize(packet_Server);
				return;
			}
			serverAssetsPacket.packet = packet_Server;
			if (DummyNetConnection.SendServerPacketDirectly(CreatePacketIdentification(controlServerPrivilege: true)))
			{
				if (DummyNetConnection.SendServerAssetsPacketDirectly(packet_Server))
				{
					serverAssetsSentLocally = true;
					worldMetaDataPacketAlreadySentToSinglePlayer = DummyNetConnection.SendServerPacketDirectly(WorldMetaDataPacket());
				}
				Logger.VerboseDebug("Single player: sent server assets packet to client");
			}
		}
		catch (ThreadAbortException)
		{
		}
	}

	private static Packet_Recipes RecipesToPacket(RecipeRegistryBase reg, string code, ServerMain world, FastMemoryStream ms)
	{
		if (reg is RecipeRegistryGeneric<GridRecipe> recipeRegistryGeneric)
		{
			recipeRegistryGeneric.ToBytes(world, out var data, out var quantity, ms);
			recipeRegistryGeneric.FreeRAMServer();
			return new Packet_Recipes
			{
				Code = code,
				Data = data,
				Quantity = quantity
			};
		}
		reg.ToBytes(world, out var data2, out var quantity2);
		return new Packet_Recipes
		{
			Code = code,
			Data = data2,
			Quantity = quantity2
		};
	}

	private void SendWorldMetaData(IServerPlayer player)
	{
		if (!worldMetaDataPacketAlreadySentToSinglePlayer || !Clients.TryGetValue(player.ClientId, out var value) || value == null || !value.IsSinglePlayerClient)
		{
			SendPacket(player, WorldMetaDataPacket());
		}
	}

	internal Packet_Server WorldMetaDataPacket()
	{
		float[] array = blockLightLevels;
		Packet_WorldMetaData packet_WorldMetaData = new Packet_WorldMetaData();
		int[] array2 = new int[array.Length];
		int[] array3 = new int[sunLightLevels.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = CollectibleNet.SerializeFloat(array[i]);
			array3[i] = CollectibleNet.SerializeFloat(sunLightLevels[i]);
		}
		packet_WorldMetaData.SetBlockLightlevels(array2);
		packet_WorldMetaData.SetSunLightlevels(array3);
		packet_WorldMetaData.SunBrightness = sunBrightness;
		packet_WorldMetaData.SetWorldConfiguration((SaveGameData.WorldConfiguration as TreeAttribute).ToBytes());
		packet_WorldMetaData.SeaLevel = seaLevel;
		return new Packet_Server
		{
			Id = 21,
			WorldMetaData = packet_WorldMetaData
		};
	}

	private void SendLevelProgress(IServerPlayer player, int percentcomplete, string status)
	{
		Packet_ServerLevelProgress levelDataChunk = new Packet_ServerLevelProgress
		{
			PercentComplete = percentcomplete,
			Status = status
		};
		Packet_Server packet = new Packet_Server
		{
			Id = 5,
			LevelDataChunk = levelDataChunk
		};
		SendPacket(player, packet);
	}

	private void SendServerReady(IServerPlayer player)
	{
		Logger.Audit("{0} joined.", player.PlayerName);
		SendPacket(player, new Packet_Server
		{
			Id = 73,
			ServerReady = new Packet_ServerReady()
		});
	}

	private void SendServerIdentification(ServerPlayer player)
	{
		if (serverAssetsSentLocally && player.client.IsSinglePlayerClient)
		{
			((DummyUdpNetServer)UdpSockets[0]).Client.Player = player;
		}
		else
		{
			SendPacket(player, CreatePacketIdentification(player.HasPrivilege("controlserver")));
		}
	}

	private Packet_Server CreatePacketIdentification(bool controlServerPrivilege)
	{
		List<Packet_ModId> list = (from mod in api.ModLoader.Mods
			where mod.Info.Side.IsUniversal()
			select new Packet_ModId
			{
				Modid = mod.Info.ModID,
				Name = mod.Info.Name,
				Networkversion = mod.Info.NetworkVersion,
				Version = mod.Info.Version,
				RequiredOnClient = mod.Info.RequiredOnClient
			}).ToList();
		Packet_ServerIdentification packet_ServerIdentification = new Packet_ServerIdentification
		{
			GameVersion = "1.22.7",
			NetworkVersion = "1.22.6",
			ServerName = Config.ServerName,
			Seed = SaveGameData.Seed,
			SavegameIdentifier = SaveGameData.SavegameIdentifier,
			MapSizeX = WorldMap.MapSizeX,
			MapSizeY = WorldMap.MapSizeY,
			MapSizeZ = WorldMap.MapSizeZ,
			RegionMapSizeX = WorldMap.RegionMapSizeX,
			RegionMapSizeY = WorldMap.RegionMapSizeY,
			RegionMapSizeZ = WorldMap.RegionMapSizeZ,
			PlayStyle = SaveGameData.PlayStyle,
			PlayListCode = api.WorldManager.CurrentPlayStyle?.PlayListCode,
			RequireRemapping = ((controlServerPrivilege && requiresRemaps) ? 1 : 0)
		};
		Logger.Notification((Config.DisableAutoRemap ? ("Sending server identification with remap " + requiresRemaps) : "Sending server identification") + ". Server control privilege is " + controlServerPrivilege);
		packet_ServerIdentification.SetMods(list.ToArray());
		packet_ServerIdentification.SetWorldConfiguration((SaveGameData.WorldConfiguration as TreeAttribute).ToBytes());
		if (Config.ModIdBlackList != null && Config.ModIdWhiteList == null)
		{
			packet_ServerIdentification.SetServerModIdBlackList(Config.ModIdBlackList);
		}
		if (Config.ModIdWhiteList != null)
		{
			packet_ServerIdentification.SetServerModIdWhiteList(Config.ModIdWhiteList);
		}
		return new Packet_Server
		{
			Id = 1,
			Identification = packet_ServerIdentification
		};
	}

	public void BroadcastPlayerData(IServerPlayer owningPlayer, bool sendInventory = true, bool sendPrivileges = false)
	{
		Packet_Server packet = ((ServerWorldPlayerData)owningPlayer.WorldData).ToPacket(owningPlayer, sendInventory, sendPrivileges);
		Packet_Server packet2 = ((ServerWorldPlayerData)owningPlayer.WorldData).ToPacketForOtherPlayers(owningPlayer);
		SendPacket(owningPlayer, packet);
		BroadcastPacket(packet2, owningPlayer);
	}

	public void SendOwnPlayerData(IServerPlayer owningPlayer, bool sendInventory = true, bool sendPrivileges = false)
	{
		Packet_Server packet = ((ServerWorldPlayerData)owningPlayer.WorldData).ToPacket(owningPlayer, sendInventory, sendPrivileges);
		SendPacket(owningPlayer, packet);
	}

	public void SendInitialPlayerDataForOthers(IServerPlayer owningPlayer, IServerPlayer toPlayer, FastMemoryStream ms)
	{
		Packet_Server packet = ((ServerWorldPlayerData)owningPlayer.WorldData).ToPacketForOtherPlayers(owningPlayer);
		SendPacket(toPlayer, packet);
	}

	public void BroadcastPlayerPings()
	{
		Packet_ServerPlayerPing packet_ServerPlayerPing = new Packet_ServerPlayerPing();
		ICollection<ConnectedClient> values = Clients.Values;
		int count = values.Count;
		int[] array = new int[count];
		int[] array2 = new int[count];
		int num = 0;
		foreach (ConnectedClient item in values)
		{
			if (item.State.ConnectedOrPlaying())
			{
				array[num] = item.Id;
				array2[num] = (int)(1000f * item.Player.Ping);
				num++;
			}
		}
		packet_ServerPlayerPing.SetPings(array2);
		packet_ServerPlayerPing.SetClientIds(array);
		Packet_Server packet = new Packet_Server
		{
			Id = 3,
			PlayerPing = packet_ServerPlayerPing
		};
		BroadcastArbitraryPacket(packet);
	}

	public void SendServerRedirect(IServerPlayer player, string host, string name)
	{
		Packet_Server packet = new Packet_Server
		{
			Id = 29,
			Redirect = new Packet_ServerRedirect
			{
				Host = host,
				Name = name
			}
		};
		SendPacket(player, packet);
	}

	public void UpdateEntityChunk(Entity entity, long newChunkIndex3d)
	{
		IWorldChunk chunk = worldmap.GetChunk(newChunkIndex3d);
		if (chunk != null)
		{
			worldmap.GetChunk(entity.InChunkIndex3d)?.RemoveEntity(entity.EntityId);
			chunk.AddEntity(entity);
			entity.InChunkIndex3d = newChunkIndex3d;
		}
	}

	public int SetMiniDimension(IMiniDimension dimension, int index)
	{
		LoadedMiniDimensions[index] = dimension;
		return index;
	}

	public IMiniDimension GetMiniDimension(int index)
	{
		LoadedMiniDimensions.TryGetValue(index, out var value);
		return value;
	}

	public ServerChunk GetLoadedChunk(long index3d)
	{
		ServerChunk value = null;
		loadedChunksLock.AcquireReadLock();
		try
		{
			loadedChunks.TryGetValue(index3d, out value);
			return value;
		}
		finally
		{
			loadedChunksLock.ReleaseReadLock();
		}
	}

	public void SendChunk(int chunkX, int chunkY, int chunkZ, IServerPlayer player, bool onlyIfInRange)
	{
		ServerPlayer serverPlayer = player as ServerPlayer;
		if (serverPlayer?.Entity == null || serverPlayer?.client == null)
		{
			return;
		}
		ConnectedClient client = serverPlayer.client;
		long item = WorldMap.ChunkIndex3D(chunkX, chunkY, chunkZ);
		long num = WorldMap.MapChunkIndex2D(chunkX, chunkZ);
		float num2 = client.WorldData.Viewdistance + 16;
		if (!onlyIfInRange || client.Entityplayer.Pos.InHorizontalRangeOf(chunkX * 32 + 16, chunkZ * 32 + 16, num2 * num2))
		{
			if (!client.DidSendMapChunk(num))
			{
				client.forceSendMapChunks.Add(num);
			}
			client.forceSendChunks.Add(item);
		}
	}

	public void BroadcastChunk(int chunkX, int chunkY, int chunkZ, bool onlyIfInRange)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.Entityplayer == null)
			{
				continue;
			}
			long item = WorldMap.ChunkIndex3D(chunkX, chunkY, chunkZ);
			long num = WorldMap.MapChunkIndex2D(chunkX, chunkZ);
			float num2 = value.WorldData.Viewdistance + 16;
			if (!onlyIfInRange || value.Entityplayer.Pos.InHorizontalRangeOf(chunkX * 32 + 16, chunkZ * 32 + 16, num2 * num2))
			{
				if (!value.DidSendMapChunk(num))
				{
					value.forceSendMapChunks.Add(num);
				}
				value.forceSendChunks.Add(item);
			}
		}
	}

	public void BroadcastChunkColumn(int chunkX, int chunkZ, bool onlyIfInRange)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.Entityplayer == null)
			{
				continue;
			}
			float num = value.WorldData.Viewdistance + 16;
			if (!onlyIfInRange || value.Entityplayer.Pos.InHorizontalRangeOf(chunkX * 32 + 16, chunkZ * 32 + 16, num * num))
			{
				for (int i = 0; i < WorldMap.ChunkMapSizeY; i++)
				{
					long item = WorldMap.ChunkIndex3D(chunkX, i, chunkZ);
					value.forceSendChunks.Add(item);
				}
			}
		}
	}

	public void ResendMapChunk(int chunkX, int chunkZ, bool onlyIfInRange)
	{
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.Entityplayer != null)
			{
				long item = WorldMap.MapChunkIndex2D(chunkX, chunkZ);
				float num = value.WorldData.Viewdistance + 16;
				if (!onlyIfInRange || value.Entityplayer.Pos.InHorizontalRangeOf(chunkX * 32 + 16, chunkZ * 32 + 16, num * num))
				{
					value.forceSendMapChunks.Add(item);
				}
			}
		}
	}

	public void BroadcastMapRegion(int regionX, int regionZ, bool onlyIfInRange = true)
	{
		int regionSize = worldmap.RegionSize;
		foreach (ConnectedClient value in Clients.Values)
		{
			if (value.Entityplayer != null)
			{
				int num = value.WorldData.Viewdistance + regionSize / 2;
				if (!onlyIfInRange || value.Entityplayer.Pos.InHorizontalRangeOf(regionX * regionSize + regionSize / 2, regionZ * regionSize + regionSize / 2, num * num))
				{
					long item = WorldMap.MapRegionIndex2D(regionX, regionZ);
					value.forceSendMapRegions.Add(item);
				}
			}
		}
	}

	public void LoadChunkColumnFast(int chunkX, int chunkZ, ChunkLoadOptions options = null)
	{
		if (options == null)
		{
			options = defaultOptions;
		}
		long mapindex2d = WorldMap.MapChunkIndex2D(chunkX, chunkZ);
		if (options.KeepLoaded)
		{
			AddChunkColumnToForceLoadedList(mapindex2d);
		}
		if (!IsChunkColumnFullyLoaded(chunkX, chunkZ))
		{
			KeyValuePair<HorRectanglei, ChunkLoadOptions> item = new KeyValuePair<HorRectanglei, ChunkLoadOptions>(new HorRectanglei(chunkX, chunkZ, chunkX, chunkZ), options);
			fastChunkQueue.Enqueue(item);
		}
		else
		{
			options.OnLoaded?.Invoke();
		}
	}

	public void LoadChunkColumnFast(int chunkX1, int chunkZ1, int chunkX2, int chunkZ2, ChunkLoadOptions options = null)
	{
		if (options == null)
		{
			options = defaultOptions;
		}
		if (options.KeepLoaded)
		{
			for (int i = chunkX1; i <= chunkX2; i++)
			{
				for (int j = chunkZ1; j <= chunkZ2; j++)
				{
					long mapindex2d = WorldMap.MapChunkIndex2D(i, j);
					AddChunkColumnToForceLoadedList(mapindex2d);
				}
			}
		}
		KeyValuePair<HorRectanglei, ChunkLoadOptions> item = new KeyValuePair<HorRectanglei, ChunkLoadOptions>(new HorRectanglei(chunkX1, chunkZ1, chunkX2, chunkZ2), options);
		fastChunkQueue.Enqueue(item);
	}

	public void PeekChunkColumn(int chunkX, int chunkZ, ChunkPeekOptions options)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options argument must not be null");
		}
		if (options.OnGenerated == null)
		{
			throw new ArgumentNullException("options.OnGenerated must not be null (there is no point to calling this method otherwise)");
		}
		KeyValuePair<Vec2i, ChunkPeekOptions> item = new KeyValuePair<Vec2i, ChunkPeekOptions>(new Vec2i(chunkX, chunkZ), options);
		peekChunkColumnQueue.Enqueue(item);
	}

	public void TestChunkExists(int chunkX, int chunkY, int chunkZ, Action<bool> onTested, EnumChunkType type)
	{
		testChunkExistsQueue.Enqueue(new ChunkLookupRequest(chunkX, chunkY, chunkZ, onTested)
		{
			Type = type
		});
	}

	public void LoadChunkColumn(int chunkX, int chunkZ, bool keepLoaded = false)
	{
		long num = WorldMap.MapChunkIndex2D(chunkX, chunkZ);
		if (keepLoaded)
		{
			AddChunkColumnToForceLoadedList(num);
		}
		if (!IsChunkColumnFullyLoaded(chunkX, chunkZ))
		{
			lock (requestedChunkColumnsLock)
			{
				requestedChunkColumns.Enqueue(num);
			}
		}
	}

	public void AddChunkColumnToForceLoadedList(long mapindex2d)
	{
		forceLoadedChunkColumns.Add(mapindex2d);
	}

	public void RemoveChunkColumnFromForceLoadedList(long mapindex2d)
	{
		forceLoadedChunkColumns.Remove(mapindex2d);
	}

	public bool IsChunkColumnFullyLoaded(int chunkX, int chunkZ)
	{
		long num = 2097152L;
		num *= num;
		long num2 = WorldMap.ChunkIndex3D(chunkX, 0, chunkZ);
		loadedChunksLock.AcquireReadLock();
		try
		{
			for (long num3 = 0L; num3 < WorldMap.ChunkMapSizeY; num3++)
			{
				if (!loadedChunks.ContainsKey(num2 + num3 * num))
				{
					return false;
				}
			}
		}
		finally
		{
			loadedChunksLock.ReleaseReadLock();
		}
		return true;
	}

	public void CreateChunkColumnForDimension(int cx, int cz, int dim)
	{
		int chunkMapSizeY = WorldMap.ChunkMapSizeY;
		ServerMapChunk serverMapChunk = (ServerMapChunk)WorldMap.GetMapChunk(cx, cz);
		int num = dim * 32768 / 32;
		loadedChunksLock.AcquireWriteLock();
		try
		{
			for (int i = 0; i < chunkMapSizeY; i++)
			{
				long key = WorldMap.ChunkIndex3D(cx, num + i, cz);
				ServerChunk serverChunk = ServerChunk.CreateNew(serverChunkDataPool);
				serverChunk.serverMapChunk = serverMapChunk;
				loadedChunks[key] = serverChunk;
				serverChunk.MarkToPack();
			}
		}
		finally
		{
			loadedChunksLock.ReleaseWriteLock();
		}
	}

	public void LoadChunkColumnForDimension(int cx, int cz, int dim)
	{
		ChunkColumnLoadRequest chunkColumnLoadRequest = new ChunkColumnLoadRequest(WorldMap.MapChunkIndex2D(cx, cz), cx, cz, -1, 6, this);
		chunkColumnLoadRequest.dimension = dim;
		simpleLoadRequests.Enqueue(chunkColumnLoadRequest);
	}

	public void ForceSendChunkColumn(IServerPlayer player, int cx, int cz, int dimension)
	{
		ConnectedClient client = ((ServerPlayer)player).client;
		int chunkMapSizeY = WorldMap.ChunkMapSizeY;
		for (int i = 0; i < chunkMapSizeY; i++)
		{
			long item = WorldMap.ChunkIndex3D(cx, i, cz, dimension);
			client.forceSendChunks.Add(item);
		}
	}

	public bool BlockingTestMapRegionExists(int regionX, int regionZ)
	{
		return chunkThread.gameDatabase.MapRegionExists(regionX, regionZ);
	}

	public bool BlockingTestMapChunkExists(int chunkX, int chunkZ)
	{
		return chunkThread.gameDatabase.MapChunkExists(chunkX, chunkZ);
	}

	public IServerChunk[] BlockingLoadChunkColumn(int chunkX, int chunkZ)
	{
		ChunkColumnLoadRequest chunkRequest = new ChunkColumnLoadRequest(0L, chunkX, chunkZ, -1, 0, this);
		return chunkThread.loadsavechunks.TryLoadChunkColumn(chunkRequest);
	}
}
