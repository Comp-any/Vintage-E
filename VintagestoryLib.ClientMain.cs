using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Common.Datastructures;

namespace Vintagestory.Client.NoObf;

public sealed class ClientMain : GameMain, IWorldIntersectionSupplier, IClientWorldAccessor, IWorldAccessor
{
	public const int millisecondsToTriggerNewFrame = 60;

	public GuiComposerManager GuiComposers;

	public bool clientPlayingFired;

	public GuiScreenRunningGame ScreenRunningGame;

	public bool[] PreviousKeyboardState;

	public bool[] KeyboardState;

	public bool[] KeyboardStateRaw;

	public bool mouseWorldInteractAnyway;

	public int MouseCurrentX;

	public int MouseCurrentY;

	public int particleLevel;

	public bool BlocksReceivedAndLoaded;

	public bool AssetsReceived;

	public bool DoneColorMaps;

	public bool DoneBlockAndItemShapeLoading;

	public bool AutoJumpEnabled;

	public bool IsPaused;

	public int FrameRateMode;

	public bool LagSimulation;

	public bool Spawned;

	public bool doReconnect;

	public bool exitToMainMenu;

	public bool disposed;

	public string disconnectReason;

	public string disconnectAction;

	public List<string> disconnectMissingMods;

	public string exitReason;

	public bool deleteWorld;

	public bool exitToDisconnectScreen;

	public bool ShouldRender2DOverlays;

	public bool AllowFreemove;

	public bool forceLiquidSelectable;

	public float timelapse;

	public float timelapsedCurrent;

	public float timelapseEnd;

	public Stopwatch InWorldStopwatch = new Stopwatch();

	public SystemMusicEngine MusicEngine;

	public bool AllowCameraControl = true;

	public bool AllowCharacterControl = true;

	public bool StartedConnecting;

	public bool UdpTryConnect;

	public bool FallBackToTcp;

	public bool threadsShouldExit;

	public bool ShouldRedrawAllBlocks;

	public bool doTransparentRenderPass = ClientSettings.TransparentRenderPass;

	public bool extendedDebugInfo;

	public bool HandSetAttackBuild;

	public bool HandSetAttackDestroy;

	public string ServerNetworkVersion;

	public string ServerGameVersion;

	public ServerPacketHandler<Packet_Server>[] PacketHandlers;

	public HandleServerCustomUdpPacket HandleCustomUdpPackets;

	public SystemModHandler modHandler;

	public double[] PerspectiveProjectionMat = new double[16];

	public double[] PerspectiveViewMat = new double[16];

	public const int ClientChunksize = 32;

	public const int ClientChunksizeMask = 31;

	public const int ClientChunksizebits = 5;

	public ClientPlayer player;

	public readonly ConcurrentTagRegistry CollectibleTagRegistry;

	public readonly ConcurrentTagRegistryFast EntityTagRegistry;

	public static ClassRegistry ClassRegistry;

	public Dictionary<string, ClientPlayer> PlayersByUid = new Dictionary<string, ClientPlayer>(10);

	public CachingConcurrentDictionary<string, ClientPlayer> PlayersByUid_Threadsafe = new CachingConcurrentDictionary<string, ClientPlayer>();

	public Dictionary<long, Entity> LoadedEntities = new Dictionary<long, Entity>(100);

	public EntityPos SpawnPosition;

	public ClientSystem[] clientSystems;

	public CancellationTokenSource MusicEngineCts;

	public Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, EntityProperties> EntityClassesByCode = new Vintagestory.API.Datastructures.OrderedDictionary<AssetLocation, EntityProperties>();

	private List<EntityProperties> entityTypesCached;

	private List<string> entityCodesCached;

	public Vintagestory.API.Datastructures.OrderedDictionary<string, string> DebugScreenInfo;

	public AmbientManager AmbientManager;

	public int[][] FastBlockTextureSubidsByBlockAndFace;

	public int textureSize = 32;

	public ShapeTesselatorManager TesselatorManager;

	public BlockTextureAtlasManager BlockAtlasManager;

	public ItemTextureAtlasManager ItemAtlasManager;

	public EntityTextureAtlasManager EntityAtlasManager;

	public BlendedTextureManager BlendedTextureManager;

	public int[] BleedOverlayTextureSubIdsByBlockId;

	private Dictionary<AssetLocation, LoadedTexture> texturesByLocation;

	private List<Thread> clientThreads = new List<Thread>();

	private CancellationTokenSource _clientThreadsCts;

	public ClientEventManager eventManager;

	public MacroManager macroManager;

	public PlayerCamera MainCamera;

	public ClientPlatformAbstract Platform;

	public ClientCoreAPI api;

	public ChunkTesselator TerrainChunkTesselator;

	public bool ShouldTesselateTerrain = true;

	public TerrainIlluminator terrainIlluminator;

	public ClientWorldMap WorldMap;

	public float DeltaTimeLimiter = -1f;

	public MeshRef quadModel;

	public const int DisconnectedIconAfterSeconds = 5;

	public bool Drawblockinfo = ClientSettings.ShowBlockInfoHud;

	public bool offScreenRendering = true;

	public NetClient MainNetClient;

	public UNetClient UdpNetClient;

	public ThreadLocal<Random> rand;

	public long LastReceivedMilliseconds;

	public ServerConnectData Connectdata;

	public bool IsSingleplayer;

	public bool OpenedToLan;

	public bool OpenedToInternet;

	public bool KillNextFrame;

	public FrustumCulling frustumCuller;

	public IColorPresets ColorPreset;

	public int TesselationThreadId;

	public DefaultShaderUniforms shUniforms = new DefaultShaderUniforms();

	private ClientSystemStartup csStartup;

	private bool ungrabbedFromDefocus;

	public SystemNetworkProcess networkProc;

	public SystemRenderDecals decalSystem;

	private Queue<ClientTask> reversedQueue = new Queue<ClientTask>();

	private Queue<ClientTask> holdingQueue = new Queue<ClientTask>();

	public ShaderProgramGui guiShaderProg;

	public string tickSummary;

	private LoadedTexture tmpTex;

	private double[] set3DProjectionTempMat4;

	public bool CurrentMatrixModeProjection;

	public StackMatrix4 MvMatrix;

	public StackMatrix4 PMatrix;

	private double[] identityMatrix;

	private double[] glScaleTempVec3;

	private double[] glRotateTempVec3;

	private float[] tmpMatrix = new float[16];

	private double[] tmpMatrixd = new double[16];

	public bool preventPlacementInLava = true;

	public MouseButtonState MouseStateRaw = new MouseButtonState();

	public MouseButtonState InWorldMouseState = new MouseButtonState();

	public bool PickBlock;

	public double MouseDeltaX;

	public double MouseDeltaY;

	public double DelayedMouseDeltaX;

	public double DelayedMouseDeltaY;

	private float rotationspeed;

	private float swimmingMouseSmoothing;

	public float mouseYaw;

	public float mousePitch;

	private Vec3f prevMountAngles = new Vec3f();

	private Dictionary<int, Block> noBlocks = new Dictionary<int, Block>();

	private int whitetexture;

	public MultiplayerServerEntry RedirectTo;

	private int lastWidth;

	private int lastHeight;

	public object MainThreadTasksLock = new object();

	public Queue<ClientTask> GameLaunchTasks;

	public Queue<ClientTask> MainThreadTasks;

	private CollisionTester collTester = new CollisionTester();

	internal SoundConfig? SoundConfig;

	internal Dictionary<AssetLocation, int> SoundIteration = new Dictionary<AssetLocation, int>();

	internal Queue<ILoadedSound> ActiveSounds = new Queue<ILoadedSound>();

	internal EnumRenderStage currentRenderStage;

	private long lastSkipTotalMs = -1000L;

	private int cntSkip;

	internal ClientGameCalendar GameWorldCalendar;

	internal bool ignoreServerCalendarUpdates;

	public HashSet<ChunkPos> chunkPositionsForRegenTrav = new HashSet<ChunkPos>();

	public object chunkPositionsLock = new object();

	public Queue<long> compactedClientChunks = new Queue<long>();

	public object compactSyncLock = new object();

	internal bool unbindSamplers;

	public object EntityLoadQueueLock = new object();

	public Stack<Entity> EntityLoadQueue = new Stack<Entity>();

	internal List<ModId> ServerMods;

	internal List<string> ServerModIdBlacklist = new List<string>();

	internal List<string> ServerModIdWhitelist = new List<string>();

	private TreeAttribute WorldConfig;

	internal List<GuiDialog> LoadedGuis = new List<GuiDialog>();

	internal List<GuiDialog> OpenedGuis = new List<GuiDialog>();

	internal Dictionary<int, PlayerGroup> OwnPlayerGroupsById = new Dictionary<int, PlayerGroup>();

	internal Dictionary<int, PlayerGroupMembership> OwnPlayerGroupMemembershipsById = new Dictionary<int, PlayerGroupMembership>();

	public int currentGroupid = GlobalConstants.GeneralChatGroup;

	public Dictionary<int, LimitedList<string>> ChatHistoryByPlayerGroup = new Dictionary<int, LimitedList<string>>();

	internal Dictionary<BlockPos, BlockDamage> damagedBlocks = new Dictionary<BlockPos, BlockDamage>();

	internal PickingRayUtil pickingRayUtil = new PickingRayUtil();

	public TrackedPlayerProperties playerProperties = new TrackedPlayerProperties();

	internal object asyncParticleSpawnersLock = new object();

	internal List<ContinousParticleSpawnTaskDelegate> asyncParticleSpawners = new List<ContinousParticleSpawnTaskDelegate>();

	internal List<IPointLight> pointlights = new List<IPointLight>();

	internal Dictionary<long, EntityRenderer> EntityRenderers = new Dictionary<long, EntityRenderer>();

	internal float[] toShadowMapSpaceMatrixFar = Mat4f.Create();

	internal float[] toShadowMapSpaceMatrixNear = Mat4f.Create();

	internal float[] shadowMvpMatrix = Mat4f.Create();

	internal Vec3f FogColorSky = new Vec3f();

	internal int skyGlowTextureId;

	internal int skyTextureId;

	internal int frameSeed;

	internal ChunkRenderer chunkRenderer;

	internal object dirtyChunksLastLock = new object();

	internal UniqueQueue<long> dirtyChunksLast = new UniqueQueue<long>();

	internal object dirtyChunksLock = new object();

	internal UniqueQueue<long> dirtyChunks = new UniqueQueue<long>();

	internal object dirtyChunksPriorityLock = new object();

	internal UniqueQueue<long> dirtyChunksPriority = new UniqueQueue<long>();

	internal object tesselatedChunksLock = new object();

	internal Queue<long> tesselatedChunks = new Queue<long>();

	internal object tesselatedChunksPriorityLock = new object();

	internal Queue<long> tesselatedChunksPriority = new Queue<long>();

	internal ServerInformation ServerInfo;

	internal bool SuspendMainThreadTasks;

	internal bool AssetLoadingOffThread;

	internal bool ServerReady;

	internal ParticleManager particleManager = new ParticleManager();

	public override IWorldAccessor World => this;

	protected override WorldMap worldmap => WorldMap;

	public int Seed => ServerInfo.Seed;

	public string SavegameIdentifier => ServerInfo.SavegameIdentifier;

	FrameProfilerUtil IWorldAccessor.FrameProfiler => ScreenManager.FrameProfiler;

	public long InWorldEllapsedMs => InWorldStopwatch.ElapsedMilliseconds;

	public bool LiquidSelectable => forceLiquidSelectable;

	public bool AmbientParticles
	{
		get
		{
			return ClientSettings.AmbientParticles;
		}
		set
		{
			ClientSettings.AmbientParticles = value;
		}
	}

	public IClientPlayer Player => player;

	public EntityPlayer EntityPlayer => player?.Entity;

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

	public EntityPos DefaultSpawnPosition => SpawnPosition;

	public BlockSelection BlockSelection
	{
		get
		{
			return EntityPlayer?.BlockSelection;
		}
		set
		{
			if (EntityPlayer != null)
			{
				EntityPlayer.BlockSelection = value;
			}
		}
	}

	public EntitySelection EntitySelection
	{
		get
		{
			return EntityPlayer?.EntitySelection;
		}
		set
		{
			if (EntityPlayer != null)
			{
				EntityPlayer.EntitySelection = value;
			}
		}
	}

	public long[] LoadedChunkIndices => WorldMap.chunks.Keys.ToArray();

	public long[] LoadedMapChunkIndices => WorldMap.MapChunks.Keys.ToArray();

	public float[] CurrentProjectionMatrix
	{
		get
		{
			for (int i = 0; i < 16; i++)
			{
				tmpMatrix[i] = (float)PMatrix.Top[i];
			}
			return tmpMatrix;
		}
	}

	public float[] CurrentModelViewMatrix
	{
		get
		{
			for (int i = 0; i < 16; i++)
			{
				tmpMatrix[i] = (float)MvMatrix.Top[i];
			}
			return tmpMatrix;
		}
	}

	public double[] CurrentModelViewMatrixd
	{
		get
		{
			for (int i = 0; i < 16; i++)
			{
				tmpMatrixd[i] = MvMatrix.Top[i];
			}
			return tmpMatrixd;
		}
	}

	public bool MouseGrabbed
	{
		get
		{
			return Platform.MouseGrabbed;
		}
		set
		{
			bool mouseGrabbed = Platform.MouseGrabbed;
			Platform.MouseGrabbed = value;
			if ((!mouseGrabbed & value) && DialogsOpened == 0)
			{
				player.inventoryMgr?.DropMouseSlotItems(fullStack: true);
				OnMouseMove(new MouseEvent(Width / 2, Height / 2));
			}
		}
	}

	public int Width => Platform.WindowSize.Width;

	public int Height => Platform.WindowSize.Height;

	public IBlockAccessor BlockAccessor => WorldMap.RelaxedBlockAccess;

	public IBulkBlockAccessor BulkBlockAccessor => WorldMap.BulkBlockAccess;

	IClassRegistryAPI IWorldAccessor.ClassRegistry => api.instancerapi;

	public Random Rand => rand.Value;

	public long ElapsedMilliseconds => Platform.EllapsedMs;

	public List<EntityProperties> EntityTypes => entityTypesCached ?? (entityTypesCached = EntityClassesByCode.Values.ToList());

	public List<string> EntityTypeCodes => entityCodesCached ?? (entityCodesCached = makeEntityCodesCache());

	public int DefaultEntityTrackingRange => 32;

	public ILogger Logger => Platform.Logger;

	public IAssetManager AssetManager => Platform.AssetManager;

	public EnumAppSide Side => EnumAppSide.Client;

	List<CollectibleObject> IWorldAccessor.Collectibles => Collectibles;

	List<GridRecipe> IWorldAccessor.GridRecipes => GridRecipes;

	IList<Block> IWorldAccessor.Blocks => Blocks;

	IList<Item> IWorldAccessor.Items => Items;

	List<EntityProperties> IWorldAccessor.EntityTypes => EntityTypes;

	public IPlayer[] AllOnlinePlayers => PlayersByUid.Values.Select((ClientPlayer player) => player).ToArray();

	public IPlayer[] AllPlayers => PlayersByUid.Values.Select((ClientPlayer player) => player).ToArray();

	float[] IWorldAccessor.BlockLightLevels => WorldMap.BlockLightLevels;

	float[] IWorldAccessor.SunLightLevels => WorldMap.SunLightLevels;

	public int SeaLevel => ClientWorldMap.seaLevel;

	public int MapSizeY => WorldMap.MapSizeY;

	int IWorldAccessor.SunBrightness => WorldMap.SunBrightness;

	public bool EntityDebugMode { get; set; }

	bool IClientWorldAccessor.ForceLiquidSelectable
	{
		get
		{
			return forceLiquidSelectable;
		}
		set
		{
			forceLiquidSelectable = value;
		}
	}

	public CollisionTester CollisionTester => collTester;

	Dictionary<long, Entity> IClientWorldAccessor.LoadedEntities => LoadedEntities;

	public Dictionary<int, IMiniDimension> MiniDimensions => ((ClientWorldMap)worldmap).dimensions;

	public ICoreAPI Api => api;

	public IChunkProvider ChunkProvider => WorldMap;

	public ILandClaimAPI Claims => WorldMap;

	public override Vec3i MapSize => WorldMap.MapSize;

	public ITreeAttribute Config => WorldConfig;

	public override IBlockAccessor blockAccessor => WorldMap.RelaxedBlockAccess;

	public IClientGameCalendar Calendar => GameWorldCalendar;

	IGameCalendar IWorldAccessor.Calendar => GameWorldCalendar;

	public int DialogsOpened => Enumerable.Count(OpenedGuis, (GuiDialog elem) => elem.DialogType == EnumDialogType.Dialog);

	public ClientPlayer GetPlayerFromEntityId(long entityId)
	{
		return PlayersByUid.Values.FirstOrDefault((ClientPlayer player) => player.Entity != null && player.Entity.EntityId == entityId);
	}

	public ClientPlayer GetPlayerFromClientId(int clientId)
	{
		return PlayersByUid.Values.FirstOrDefault((ClientPlayer player) => player.ClientId == clientId);
	}

	public Dictionary<long, IWorldChunk> GetAllChunks()
	{
		Dictionary<long, IWorldChunk> dictionary = new Dictionary<long, IWorldChunk>();
		lock (WorldMap.chunksLock)
		{
			foreach (KeyValuePair<long, ClientChunk> chunk in WorldMap.chunks)
			{
				if (chunk.Key < 4503599627370496L)
				{
					dictionary[chunk.Key] = chunk.Value;
				}
			}
			return dictionary;
		}
	}

	public ClientMain(GuiScreenRunningGame screenRunningGame, ClientPlatformAbstract platform)
	{
		ClientMain clientMain = this;
		RuntimeStats.Reset();
		Platform = platform;
		ShapeElement.Logger = platform.Logger;
		ScreenRunningGame = screenRunningGame;
		eventManager = new ClientEventManager(this);
		MainCamera = new PlayerCamera(this);
		WorldMap = new ClientWorldMap(this);
		terrainIlluminator = new TerrainIlluminator(this);
		AmbientManager = new AmbientManager(this);
		BlockAtlasManager = new BlockTextureAtlasManager(this);
		ItemAtlasManager = new ItemTextureAtlasManager(this);
		EntityAtlasManager = new EntityTextureAtlasManager(this);
		BlendedTextureManager = new BlendedTextureManager(this);
		TesselatorManager = new ShapeTesselatorManager(this);
		MeshData.Recycler = new MeshDataRecycler(this);
		if (ClientSettings.Inst.Bool.Get("disableMeshRecycler", defaultValue: false))
		{
			MeshData.Recycler.Dispose();
		}
		CollectibleTagRegistry = new ConcurrentTagRegistry(platform.Logger, "Client Collectibles");
		EntityTagRegistry = new ConcurrentTagRegistryFast(platform.Logger, "Client Entities");
		GenericComplexConditionConverter.StaticInit(CollectibleTagRegistry, EntityTagRegistry);
		CollectibleTagSetConverter.StaticInit(CollectibleTagRegistry);
		EntityTagSetConverter.StaticInit(EntityTagRegistry);
		DebugScreenInfo = new Vintagestory.API.Datastructures.OrderedDictionary<string, string>();
		DebugScreenInfo["fps"] = "";
		DebugScreenInfo["mem"] = "";
		DebugScreenInfo["triangles"] = "";
		DebugScreenInfo["occludedchunks"] = "";
		DebugScreenInfo["gpumemfrag"] = "";
		DebugScreenInfo["chunkstats"] = "";
		DebugScreenInfo["position"] = "";
		DebugScreenInfo["chunkpos"] = "";
		DebugScreenInfo["regpos"] = "";
		DebugScreenInfo["orientation"] = "";
		DebugScreenInfo["curblock"] = "";
		DebugScreenInfo["curblocktype"] = "";
		DebugScreenInfo["curblockentity"] = "";
		DebugScreenInfo["curblocklight"] = "";
		DebugScreenInfo["curblocklight2"] = "";
		DebugScreenInfo["entitycount"] = "";
		DebugScreenInfo["quadparticlepool"] = "";
		DebugScreenInfo["cubeparticlepool"] = "";
		DebugScreenInfo["incomingbytes"] = "";
		AutoJumpEnabled = false;
		MvMatrix = new StackMatrix4();
		PMatrix = new StackMatrix4();
		MvMatrix.Push(Mat4d.Create());
		PMatrix.Push(Mat4d.Create());
		whitetexture = -1;
		ShouldRender2DOverlays = true;
		AllowFreemove = true;
		AllowCameraControl = true;
		texturesByLocation = new Dictionary<AssetLocation, LoadedTexture>();
		FrameRateMode = 0;
		MainCamera.Fov = (float)ClientSettings.FieldOfView * ((float)Math.PI / 180f);
		ClientSettings.Inst.AddWatcher("leftDialogMargin", (int newvalue) =>
		{
			GuiStyle.LeftDialogMargin = newvalue;
		});
		ClientSettings.Inst.AddWatcher("rightDialogMargin", (int newvalue) =>
		{
			GuiStyle.RightDialogMargin = newvalue;
		});
		GuiStyle.LeftDialogMargin = ClientSettings.LeftDialogMargin;
		GuiStyle.RightDialogMargin = ClientSettings.RightDialogMargin;
		ClientSettings.Inst.AddWatcher<int>("fieldOfView", OnFowChanged);
		ClientSettings.Inst.AddWatcher("extendedDebugInfo", (bool newvalue) =>
		{
			clientMain.extendedDebugInfo = newvalue;
		});
		EntityDebugMode = ClientSettings.ShowEntityDebugInfo;
		ClientSettings.Inst.AddWatcher("showEntityDebugInfo", (bool newvalue) =>
		{
			clientMain.EntityDebugMode = newvalue;
		});
		ClientSettings.Inst.AddWatcher("showBlockInfo", (bool newvalue) =>
		{
			clientMain.Drawblockinfo = newvalue;
		});
		ClientSettings.Inst.AddWatcher("ssaa", (float newvalue) =>
		{
			platform.RebuildFrameBuffers();
		});
		ClientSettings.Inst.AddWatcher("ssaoQuality", (int newvalue) =>
		{
			ShaderRegistry.ReloadShaders();
			platform.RebuildFrameBuffers();
			clientMain.eventManager?.TriggerReloadShaders();
		});
		ClientSettings.Inst.AddWatcher("minbrightness", (float newvalue) =>
		{
			ShaderRegistry.ReloadShaders();
			clientMain.eventManager?.TriggerReloadShaders();
		});
		ClientSettings.Inst.AddWatcher("guiScale", (float val) =>
		{
			clientMain.GuiComposers.MarkAllDialogsForRecompose();
		});
		extendedDebugInfo = ClientSettings.ExtendedDebugInfo;
		particleLevel = ClientSettings.ParticleLevel;
		ClientSettings.Inst.AddWatcher("particleLevel", (int val) =>
		{
			clientMain.particleLevel = val;
		});
		ClientSettings.Inst.AddWatcher("immersiveFpMode", (bool newvalue) =>
		{
			clientMain.sendRuntimeSettings();
		});
		ClientSettings.Inst.AddWatcher("itemCollectMode", (int newvalue) =>
		{
			clientMain.sendRuntimeSettings();
		});
		ClientSettings.Inst.AddWatcher("renderMetaBlocks", (bool newvalue) =>
		{
			clientMain.player.worlddata.RenderMetablocks = newvalue;
			clientMain.player.worlddata.RequestMode(clientMain);
		});
		rotationspeed = 0.15f;
		swimmingMouseSmoothing = ClientSettings.SwimmingMouseSmoothing;
		ClientSettings.Inst.AddWatcher("swimmingMouseSmoothing", (float newvalue) =>
		{
			clientMain.swimmingMouseSmoothing = newvalue;
		});
		KeyboardState = new bool[512];
		PreviousKeyboardState = new bool[512];
		KeyboardStateRaw = new bool[512];
		for (int num = 0; num < 512; num++)
		{
			KeyboardState[num] = false;
			KeyboardStateRaw[num] = false;
			PreviousKeyboardState[num] = false;
		}
		glScaleTempVec3 = Vec3Utilsd.Create();
		glRotateTempVec3 = Vec3Utilsd.Create();
		identityMatrix = Mat4d.Identity(Mat4d.Create());
		set3DProjectionTempMat4 = Mat4d.Create();
		PacketHandlers = new ServerPacketHandler<Packet_Server>[256];
		csStartup = new ClientSystemStartup(this);
		MainThreadTasks = new Queue<ClientTask>();
		GameLaunchTasks = new Queue<ClientTask>();
		ClientSettings.Inst.AddWatcher<int>("viewDistance", ViewDistanceChanged);
		ClientSettings.Inst.AddWatcher<int>("vsyncMode", OnVsyncChanged);
		api = new ClientCoreAPI(this);
		GuiComposers = new GuiComposerManager(api);
		ColorPreset = new ColorPresets(this, api);
		eventManager?.AddGameTickListener(freeMouseOnDefocus, 500);
	}

	public void sendRuntimeSettings()
	{
		SendPacketClient(new Packet_Client
		{
			Id = 32,
			RuntimeSetting = new Packet_RuntimeSetting
			{
				ImmersiveFpMode = (ClientSettings.ImmersiveFpMode ? 1 : 0),
				ItemCollectMode = ClientSettings.ItemCollectMode
			}
		});
	}

	private void freeMouseOnDefocus(float t1)
	{
		if (!Platform.IsFocused && Platform.MouseGrabbed)
		{
			MouseGrabbed = false;
			ungrabbedFromDefocus = true;
		}
		if (!MouseGrabbed && ungrabbedFromDefocus && Platform.IsFocused)
		{
			MouseGrabbed = true;
			ungrabbedFromDefocus = false;
		}
	}

	private void OnFowChanged(int newValue)
	{
		MainCamera.Fov = (float)ClientSettings.FieldOfView * ((float)Math.PI / 180f);
		MainCamera.ZNear = GameMath.Clamp(0.1f - (float)ClientSettings.FieldOfView / 90f / 25f, 0.025f, 0.1f);
		Reset3DProjection();
	}

	public void Start()
	{
		Compression.Reset();
		Platform.ResetGamePauseAndUptimeState();
		disconnectAction = null;
		disconnectMissingMods = null;
		quadModel = Platform.UploadMesh(QuadMeshUtilExt.GetQuadModelData());
		FrustumCulling frustumCulling = new FrustumCulling();
		frustumCuller = frustumCulling;
		frustumCuller.UpdateViewDistance(ClientSettings.ViewDistance);
		ChunkTesselator terrainChunkTesselator = new ChunkTesselator(this);
		TerrainChunkTesselator = terrainChunkTesselator;
		Platform.AddOnCrash(OnCrashHandlerLeave.Create(this));
		rand = new ThreadLocal<Random>(() => new Random(Environment.TickCount));
		macroManager = new MacroManager(this);
		Platform.ShaderUniforms = shUniforms;
		_clientThreadsCts = new CancellationTokenSource();
		ClientSystem clientSystem = new SystemCompressChunks(this);
		Thread thread = new Thread(new ClientThread(this, "compresschunks", new ClientSystem[1] { clientSystem }, _clientThreadsCts.Token).Process);
		thread.IsBackground = true;
		thread.Start();
		thread.Name = "compresschunks";
		clientThreads.Add(thread);
		ClientSystem clientSystem2 = new SystemClientTickingBlocks(this);
		Thread thread2 = new Thread(new ClientThread(this, "blockticking", new ClientSystem[1] { clientSystem2 }, _clientThreadsCts.Token).Process);
		thread2.IsBackground = true;
		thread2.Start();
		thread2.Name = "blockticking";
		clientThreads.Add(thread2);
		ClientSystem clientSystem3 = new ClientSystemRelight(this);
		Thread thread3 = new Thread(new ClientThread(this, "relight", new ClientSystem[1] { clientSystem3 }, _clientThreadsCts.Token).Process);
		thread3.IsBackground = true;
		thread3.Start();
		thread3.Name = "relight";
		clientThreads.Add(thread3);
		ClientSystem clientSystem4 = new ChunkTesselatorManager(this);
		Thread thread4 = new Thread(new ClientThread(this, "tesselateterrain", new ClientSystem[1] { clientSystem4 }, _clientThreadsCts.Token).Process);
		thread4.IsBackground = true;
		thread4.Start();
		thread4.Name = "tesselateterrain";
		clientThreads.Add(thread4);
		TesselationThreadId = thread4.ManagedThreadId;
		ClientSystem clientSystem5 = new SystemChunkVisibilityCalc(this);
		Thread thread5 = new Thread(new ClientThread(this, "chunkvis", new ClientSystem[1] { clientSystem5 }, _clientThreadsCts.Token).Process);
		thread5.IsBackground = true;
		thread5.Start();
		thread5.Name = "chunkvis";
		clientThreads.Add(thread5);
		networkProc = new SystemNetworkProcess(this);
		Thread thread6 = new Thread(new ClientThread(this, "networkproc", new ClientSystem[1] { networkProc }, _clientThreadsCts.Token).Process);
		thread6.IsBackground = true;
		thread6.Start();
		thread6.Name = "networkproc";
		clientThreads.Add(thread6);
		ClientSystem clientSystem6 = new SystemRenderTerrain(this);
		Thread thread7 = new Thread(new ClientThread(this, "chunkculling", new ClientSystem[1] { clientSystem6 }, _clientThreadsCts.Token).Process);
		thread7.IsBackground = true;
		thread7.Start();
		thread7.Name = "chunkculling";
		clientThreads.Add(thread7);
		ClientSystem clientSystem7 = new SystemRenderParticles(this);
		Thread thread8 = new Thread(new ClientThread(this, "asyncparticles", new ClientSystem[1] { clientSystem7 }, _clientThreadsCts.Token).Process);
		thread8.IsBackground = true;
		thread8.Start();
		thread8.Name = "asyncparticles";
		clientThreads.Add(thread8);
		if (PhysicsBehaviorBase.collisionTester == null)
		{
			PhysicsBehaviorBase.collisionTester = new CachingCollisionTester();
		}
		new GeneralPacketHandler(this);
		new ClientSystemDebugCommands(this);
		new ClientSystemEntities(this);
		new SystemClientCommands(this);
		modHandler = new SystemModHandler(this);
		MusicEngineCts = new CancellationTokenSource();
		clientSystems = new ClientSystem[40]
		{
			new SystemSoundEngine(this),
			modHandler,
			new ClientSystemIntroMenu(this),
			new SystemHotkeys(this),
			(api.guiapi.guimgr = new GuiManager(this)),
			(MusicEngine = new SystemMusicEngine(this, MusicEngineCts.Token)),
			networkProc,
			clientSystem,
			clientSystem4,
			clientSystem3,
			clientSystem5,
			new SystemRenderFrameBufferDebug(this),
			new SystemUnloadChunks(this),
			new SystemPlayerSounds(this),
			new SystemRenderSkyColor(this),
			new SystemRenderNightSky(this),
			new SystemRenderSunMoon(this),
			new SystemCinematicCamera(this),
			new SystemRenderOITLayers(this),
			new SystemRenderShadowMap(this),
			new SystemRenderDebugWireframes(this),
			clientSystem6,
			new SystemRenderRiftTest(this),
			new SystemRenderEntities(this),
			(decalSystem = new SystemRenderDecals(this)),
			clientSystem7,
			new SystemRenderPlayerEffects(this),
			new SystemRenderInsideBlock(this),
			new SystemHighlightBlocks(this),
			new SystemSelectedBlockOutline(this),
			new SystemMouseInWorldInteractions(this),
			new SystemPlayerControl(this),
			new SystemRenderAim(this),
			new SystemRenderPlayerAimAcc(this),
			new SystemScreenshot(this),
			new SystemPlayerEnvAwarenessTracker(this),
			new SystemCalendar(this),
			new SystemVideoRecorder(this),
			clientSystem2,
			csStartup
		};
		ScreenManager.Platform.CheckGlError("init end");
		LastReceivedMilliseconds = Platform.EllapsedMs;
		Platform.GlDepthMask(flag: true);
		Platform.GlEnableDepthTest();
		Platform.GlCullFaceBack();
		Platform.GlEnableCullFace();
		ScreenManager.Platform.CheckGlError();
		Reset3DProjection();
		ScreenManager.Platform.CheckGlError();
	}

	public void OnOwnPlayerDataReceived()
	{
		ScreenRunningGame.BubbleUpEvent("maploaded");
		EntityPlayer.PhysicsUpdateWatcher = MainCamera.OnPlayerPhysicsTick;
		EntityPlayer.SetCurrentlyControlledPlayer();
		for (int i = 0; i < clientSystems.Length; i++)
		{
			clientSystems[i].OnOwnPlayerDataReceived();
		}
	}

	public void MainGameLoop(float deltaTime)
	{
		if (disposed)
		{
			return;
		}
		if (KillNextFrame)
		{
			SendLeave(0);
			exitReason = "client thread crash";
			DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit);
			KillNextFrame = false;
			return;
		}
		if (DeltaTimeLimiter > 0f)
		{
			deltaTime = DeltaTimeLimiter;
		}
		MainRenderLoop(deltaTime);
		ExecuteMainThreadTasks(deltaTime);
	}

	public void ExecuteMainThreadTasks(float deltaTime)
	{
		ScreenManager.FrameProfiler.Mark("beginMTT");
		if (GameLaunchTasks.Count > 0)
		{
			GameLaunchTasks.Dequeue().Action();
		}
		else
		{
			if (SuspendMainThreadTasks)
			{
				return;
			}
			lock (MainThreadTasksLock)
			{
				while (MainThreadTasks.Count > 0)
				{
					reversedQueue.Enqueue(MainThreadTasks.Dequeue());
				}
			}
			while (reversedQueue.Count > 0)
			{
				ClientTask clientTask = reversedQueue.Dequeue();
				clientTask.Action();
				if (extendedDebugInfo)
				{
					ScreenManager.FrameProfiler.Mark(clientTask.Code);
				}
				if (SuspendMainThreadTasks && reversedQueue.Count > 0)
				{
					requeueTasks();
				}
			}
			ScreenManager.FrameProfiler.Mark("doneMTT");
		}
	}

	private void requeueTasks()
	{
		lock (MainThreadTasksLock)
		{
			while (MainThreadTasks.Count > 0)
			{
				holdingQueue.Enqueue(MainThreadTasks.Dequeue());
			}
			while (reversedQueue.Count > 0)
			{
				MainThreadTasks.Enqueue(reversedQueue.Dequeue());
			}
			while (holdingQueue.Count > 0)
			{
				MainThreadTasks.Enqueue(holdingQueue.Dequeue());
			}
		}
	}

	public void TriggerRenderStage(EnumRenderStage stage, float dt)
	{
		ScreenManager.FrameProfiler.Mark("beginrenderstage-", stage);
		currentRenderStage = stage;
		eventManager?.TriggerRenderStage(stage, dt);
		Platform.CheckGlError("After render stage " + stage);
	}

	public void MainRenderLoop(float dt)
	{
		ScreenManager.FrameProfiler.Mark("mrl");
		if ((timelapsedCurrent += timelapse) > timelapseEnd)
		{
			timelapse = 0f;
			timelapsedCurrent = 0f;
			timelapseEnd = float.MaxValue;
			ShouldRender2DOverlays = true;
		}
		GameWorldCalendar.Timelapse = timelapsedCurrent;
		UpdateResize();
		UpdateFreeMouse();
		UpdateCameraYawPitch(dt);
		if (EntityPlayer?.Pos != null)
		{
			shUniforms.FlagFogDensity = AmbientManager.BlendedFlatFogDensity;
			shUniforms.FlatFogStartYPos = AmbientManager.BlendedFlatFogYPosForShader;
		}
		if (!IsPaused)
		{
			eventManager?.TriggerGameTick(InWorldEllapsedMs, this);
		}
		ScreenManager.FrameProfiler.Mark("gametick");
		if (LagSimulation)
		{
			Platform.ThreadSpinWait(10000000);
		}
		shUniforms.Update(dt, api);
		shUniforms.ZNear = MainCamera.ZNear;
		shUniforms.ZFar = MainCamera.ZFar;
		TriggerRenderStage(EnumRenderStage.Before, dt);
		Platform.GlEnableDepthTest();
		Platform.GlDepthMask(flag: true);
		ScreenManager.FrameProfiler.Mark("rendOpaque-12before");
		if (AmbientManager.ShadowQuality > 0 && (double)AmbientManager.DropShadowIntensity > 0.01)
		{
			TriggerRenderStage(EnumRenderStage.ShadowFar, dt);
			TriggerRenderStage(EnumRenderStage.ShadowFarDone, dt);
			if (AmbientManager.ShadowQuality > 1)
			{
				TriggerRenderStage(EnumRenderStage.ShadowNear, dt);
				TriggerRenderStage(EnumRenderStage.ShadowNearDone, dt);
			}
		}
		ScreenManager.FrameProfiler.Mark("rendOpaque-3shadows");
		GlMatrixModeModelView();
		GlLoadMatrix(MainCamera.CameraMatrix);
		double[] top = api.Render.PMatrix.Top;
		double[] top2 = api.Render.MvMatrix.Top;
		for (int i = 0; i < 16; i++)
		{
			PerspectiveProjectionMat[i] = top[i];
			PerspectiveViewMat[i] = top2[i];
		}
		frustumCuller.CalcFrustumEquations(player.Entity.Pos.AsBlockPos, top, top2);
		float num = (float)Math.Min(640, ClientSettings.ViewDistance) * ClientSettings.LodBias;
		float num2 = (float)Math.Min(640, ClientSettings.ViewDistance) * ClientSettings.LodBiasFar;
		if (ClientSettings.ViewDistance <= 64)
		{
			num2 = ClientSettings.ViewDistance;
		}
		frustumCuller.lod0BiasSq = num * num;
		frustumCuller.lod2BiasSq = num2 * num2;
		ScreenManager.FrameProfiler.Mark("rendOpaque-4setup");
		TriggerRenderStage(EnumRenderStage.Opaque, dt);
		ScreenManager.FrameProfiler.Mark("rendOpaque-4");
		if (doTransparentRenderPass)
		{
			ScreenManager.FrameProfiler.Enter("rendTransparent");
			Platform.LoadFrameBuffer(EnumFrameBuffer.Transparent);
			ScreenManager.FrameProfiler.Mark("rendTransp-fbloaded");
			Platform.ClearFrameBuffer(EnumFrameBuffer.Transparent);
			ScreenManager.FrameProfiler.Mark("rendTransp-bufscleared");
			TriggerRenderStage(EnumRenderStage.OIT, dt);
			Platform.UnloadFrameBuffer(EnumFrameBuffer.Transparent);
			ScreenManager.FrameProfiler.Mark("rendTranspDone");
			Platform.MergeTransparentRenderPass();
			ScreenManager.FrameProfiler.Leave();
		}
		Platform.GlDepthMask(flag: true);
		Platform.GlEnableDepthTest();
		Platform.GlCullFaceBack();
		Platform.GlEnableCullFace();
		TriggerRenderStage(EnumRenderStage.AfterOIT, dt);
	}

	public void RenderAfterPostProcessing(float dt)
	{
		if (DeltaTimeLimiter > 0f)
		{
			dt = DeltaTimeLimiter;
		}
		TriggerRenderStage(EnumRenderStage.AfterPostProcessing, dt);
	}

	public void RenderAfterFinalComposition(float dt)
	{
		if (DeltaTimeLimiter > 0f)
		{
			dt = DeltaTimeLimiter;
		}
		TriggerRenderStage(EnumRenderStage.AfterFinalComposition, dt);
		Platform.CheckGlErrorAlways("after final compo ");
	}

	public void RenderAfterBlit(float dt)
	{
		if (DeltaTimeLimiter > 0f)
		{
			dt = DeltaTimeLimiter;
		}
		TriggerRenderStage(EnumRenderStage.AfterBlit, dt);
		Platform.CheckGlErrorAlways("after blit");
	}

	public void RenderToDefaultFramebuffer(float dt)
	{
		if (DeltaTimeLimiter > 0f)
		{
			dt = DeltaTimeLimiter;
		}
		if (ShouldRender2DOverlays)
		{
			guiShaderProg = ShaderPrograms.Gui;
			guiShaderProg.Use();
			OrthoMode(Platform.WindowSize.Width, Platform.WindowSize.Height);
			TriggerRenderStage(EnumRenderStage.Ortho, dt);
			guiShaderProg.Stop();
		}
		ScreenManager.FrameProfiler.Mark("rendOrthoDone");
		PerspectiveMode();
		Platform.GlDepthFunc(EnumDepthFunction.Less);
		TriggerRenderStage(EnumRenderStage.Done, dt);
		ScreenManager.FrameProfiler.Mark("finfr");
		tickSummary = ScreenManager.FrameProfiler.summary;
	}

	public void Render2DBitmapFile(AssetLocation filename, float x, float y, float w, float h)
	{
		if (tmpTex == null)
		{
			tmpTex = new LoadedTexture(api);
		}
		GetOrLoadCachedTexture(filename, ref tmpTex);
		Render2DTexture(tmpTex.TextureId, x, y, w, h);
	}

	public void Render2DLoadedTexture(LoadedTexture texture, float posX, float posY, float z = 50f, Vec4f color = null)
	{
		Render2DTexture(texture.TextureId, posX, posY, texture.Width, texture.Height, z, color);
	}

	public void RenderTextureIntoTexture(LoadedTexture fromTexture, LoadedTexture intoTexture, float x1, float y1)
	{
		RenderTextureIntoTexture(fromTexture, 0f, 0f, fromTexture.Width, fromTexture.Height, intoTexture, x1, y1);
	}

	public FrameBufferRef CreateFrameBuffer(FramebufferAttrs fbattributes)
	{
		return Platform.CreateFramebuffer(fbattributes);
	}

	public FrameBufferRef CreateFrameBuffer(LoadedTexture intoTexture)
	{
		return Platform.CreateFramebuffer(new FramebufferAttrs("Render2DLoadedTexture", intoTexture.Width, intoTexture.Height)
		{
			Attachments = new FramebufferAttrsAttachment[1]
			{
				new FramebufferAttrsAttachment
				{
					AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
					Texture = new RawTexture
					{
						Width = intoTexture.Width,
						Height = intoTexture.Height,
						TextureId = intoTexture.TextureId
					}
				}
			}
		});
	}

	public void RenderTextureIntoTexture(LoadedTexture fromTexture, float sourceX, float sourceY, float sourceWidth, float sourceHeight, LoadedTexture intoTexture, float targetX, float targetY, float alphaTest = 0.005f)
	{
		FrameBufferRef fb = CreateFrameBuffer(intoTexture);
		RenderTextureIntoFrameBuffer(0, fromTexture, sourceX, sourceY, sourceWidth, sourceHeight, fb, targetX, targetY, alphaTest);
		DestroyFrameBuffer(fb);
	}

	public void DestroyFrameBuffer(FrameBufferRef fb)
	{
		Platform.DisposeFrameBuffer(fb, disposeTextures: false);
	}

	public void RenderTextureIntoFrameBuffer(int atlasTextureId, LoadedTexture fromTexture, float sourceX, float sourceY, float sourceWidth, float sourceHeight, FrameBufferRef fb, float targetX, float targetY, float alphaTest = 0.005f)
	{
		if (!disposed)
		{
			ShaderProgramBase currentShaderProgram = ShaderProgramBase.CurrentShaderProgram;
			currentShaderProgram?.Stop();
			ShaderProgramTexture2texture texture2texture = ShaderPrograms.Texture2texture;
			if (atlasTextureId == 0)
			{
				Platform.CurrentFrameBuffer = fb;
			}
			else
			{
				Platform.LoadFrameBuffer(fb, atlasTextureId);
			}
			Platform.GlDisableDepthTest();
			Platform.GlToggleBlend(alphaTest >= 0f);
			texture2texture.Use();
			texture2texture.Tex2d2D = fromTexture.TextureId;
			texture2texture.Texu = sourceX / (float)fromTexture.Width;
			texture2texture.Texv = sourceY / (float)fromTexture.Height;
			texture2texture.Texw = sourceWidth / (float)fromTexture.Width;
			texture2texture.Texh = sourceHeight / (float)fromTexture.Height;
			texture2texture.AlphaTest = alphaTest;
			texture2texture.Xs = targetX / (float)fb.Width;
			texture2texture.Ys = targetY / (float)fb.Height;
			texture2texture.Width = sourceWidth / (float)fb.Width;
			texture2texture.Height = sourceHeight / (float)fb.Height;
			Platform.RenderMesh(quadModel);
			Platform.GlEnableDepthTest();
			Platform.LoadFrameBuffer((currentShaderProgram?.PassName == "gui") ? EnumFrameBuffer.Default : EnumFrameBuffer.Primary);
			texture2texture.Stop();
			currentShaderProgram?.Use();
		}
	}

	public void Render2DTexture(int textureid, float x1, float y1, float width, float height, float z = 10f, Vec4f color = null)
	{
		Render2DTexture(quadModel, textureid, x1, y1, width, height, z, color);
	}

	public void Render2DTexture(MeshRef quadModel, int textureid, float x1, float y1, float width, float height, float z = 10f, Vec4f color = null)
	{
		guiShaderProg.RgbaIn = ((color == null) ? ColorUtil.WhiteArgbVec : color);
		guiShaderProg.ExtraGlow = 0;
		guiShaderProg.ApplyColor = 0;
		guiShaderProg.NoTexture = 0f;
		guiShaderProg.Tex2d2D = textureid;
		guiShaderProg.OverlayOpacity = 0f;
		guiShaderProg.NormalShaded = 0;
		GlPushMatrix();
		GlTranslate(x1, y1, z);
		GlScale(width, height, 0.0);
		GlScale(0.5, 0.5, 0.0);
		GlTranslate(1.0, 1.0, 0.0);
		guiShaderProg.ProjectionMatrix = CurrentProjectionMatrix;
		guiShaderProg.ModelViewMatrix = CurrentModelViewMatrix;
		Platform.RenderMesh(quadModel);
		GlPopMatrix();
	}

	public void Render2DTexture(MultiTextureMeshRef meshRef, float x1, float y1, float width, float height, float z = 10f, Vec4f color = null)
	{
		guiShaderProg.RgbaIn = ((color == null) ? ColorUtil.WhiteArgbVec : color);
		guiShaderProg.ExtraGlow = 0;
		guiShaderProg.ApplyColor = 0;
		guiShaderProg.NoTexture = 0f;
		guiShaderProg.OverlayOpacity = 0f;
		guiShaderProg.NormalShaded = 0;
		GlPushMatrix();
		GlTranslate(x1, y1, z);
		GlScale(width, height, 0.0);
		GlScale(0.5, 0.5, 0.0);
		GlTranslate(1.0, 1.0, 0.0);
		guiShaderProg.ProjectionMatrix = CurrentProjectionMatrix;
		guiShaderProg.ModelViewMatrix = CurrentModelViewMatrix;
		for (int i = 0; i < meshRef.meshrefs.Length; i++)
		{
			MeshRef vao = meshRef.meshrefs[i];
			guiShaderProg.BindTexture2D("tex2d", meshRef.textureids[i], 0);
			Platform.RenderMesh(vao);
		}
		GlPopMatrix();
	}

	public void Render2DTexture(int textureid, ModelTransform transform, Vec4f color = null)
	{
		guiShaderProg.RgbaIn = ((color == null) ? ColorUtil.WhiteArgbVec : color);
		guiShaderProg.ExtraGlow = 0;
		guiShaderProg.ApplyColor = 0;
		guiShaderProg.NoTexture = 0f;
		guiShaderProg.Tex2d2D = textureid;
		guiShaderProg.OverlayOpacity = 0f;
		guiShaderProg.NormalShaded = 0;
		GlPushMatrix();
		GlTranslate(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
		GlRotate(transform.Rotation.X, 1.0, 0.0, 0.0);
		GlRotate(transform.Rotation.Y, 0.0, 1.0, 0.0);
		GlRotate(transform.Rotation.Z, 0.0, 0.0, 1.0);
		GlScale(transform.ScaleXYZ.X, transform.ScaleXYZ.Y, 0.0);
		GlScale(0.5, 0.5, 0.0);
		GlTranslate(1.0, 1.0, 0.0);
		guiShaderProg.ProjectionMatrix = CurrentProjectionMatrix;
		guiShaderProg.ModelViewMatrix = CurrentModelViewMatrix;
		Platform.RenderMesh(quadModel);
		GlPopMatrix();
	}

	public void Render2DTextureFlipped(int textureid, float x1, float y1, float width, float height, float z = 10f, Vec4f color = null)
	{
		guiShaderProg.RgbaIn = ((color == null) ? ColorUtil.WhiteArgbVec : color);
		guiShaderProg.ExtraGlow = 0;
		guiShaderProg.ApplyColor = 0;
		guiShaderProg.NoTexture = 0f;
		guiShaderProg.Tex2d2D = textureid;
		guiShaderProg.NormalShaded = 0;
		GlPushMatrix();
		GlTranslate(x1, y1, z);
		GlScale(width, height, 0.0);
		GlScale(0.5, 0.5, 0.0);
		GlTranslate(1.0, 1.0, 0.0);
		GlRotate(180f, 1.0, 0.0, 0.0);
		guiShaderProg.ProjectionMatrix = CurrentProjectionMatrix;
		guiShaderProg.ModelViewMatrix = CurrentModelViewMatrix;
		Platform.RenderMesh(quadModel);
		GlPopMatrix();
	}

	public void Set3DProjection(float zfar, float fov)
	{
		float num = (float)Platform.WindowSize.Width / (float)Platform.WindowSize.Height;
		Mat4d.Perspective(set3DProjectionTempMat4, fov, num, MainCamera.ZNear, zfar);
		GlMatrixModeProjection();
		GlLoadMatrix(set3DProjectionTempMat4);
		shUniforms.ZNear = MainCamera.ZNear;
		shUniforms.ZFar = MainCamera.ZFar;
		GlMatrixModeModelView();
	}

	public void Reset3DProjection()
	{
		Set3DProjection(MainCamera.ZFar, MainCamera.Fov);
	}

	public void Set3DProjection(float zfar)
	{
		Set3DProjection(zfar, MainCamera.Fov);
	}

	public void GlMatrixModeModelView()
	{
		CurrentMatrixModeProjection = false;
	}

	public void GlMatrixModeProjection()
	{
		CurrentMatrixModeProjection = true;
	}

	public void GlLoadMatrix(double[] m)
	{
		if (CurrentMatrixModeProjection)
		{
			if (PMatrix.Count > 0)
			{
				PMatrix.Pop();
			}
			PMatrix.Push(m);
		}
		else
		{
			if (MvMatrix.Count > 0)
			{
				MvMatrix.Pop();
			}
			MvMatrix.Push(m);
		}
	}

	public void GlPopMatrix()
	{
		if (CurrentMatrixModeProjection)
		{
			if (PMatrix.Count > 1)
			{
				PMatrix.Pop();
			}
		}
		else if (MvMatrix.Count > 1)
		{
			MvMatrix.Pop();
		}
	}

	public void GlScale(double x, double y, double z)
	{
		double[] array = ((!CurrentMatrixModeProjection) ? MvMatrix.Top : PMatrix.Top);
		Vec3Utilsd.Set(glScaleTempVec3, x, y, z);
		Mat4d.Scale(array, array, glScaleTempVec3);
	}

	public void GlRotate(float angle, double x, double y, double z)
	{
		angle /= 360f;
		angle *= (float)Math.PI * 2f;
		double[] array = ((!CurrentMatrixModeProjection) ? MvMatrix.Top : PMatrix.Top);
		Vec3Utilsd.Set(glRotateTempVec3, x, y, z);
		Mat4d.Rotate(array, array, angle, glRotateTempVec3);
	}

	public void GlTranslate(Vec3d vec)
	{
		GlTranslate((float)vec.X, (float)vec.Y, (float)vec.Z);
	}

	public void GlTranslate(double x, double y, double z)
	{
		double[] array = ((!CurrentMatrixModeProjection) ? MvMatrix.Top : PMatrix.Top);
		Mat4d.Translate(array, array, new double[3] { x, y, z });
	}

	public void GlPushMatrix()
	{
		if (CurrentMatrixModeProjection)
		{
			PMatrix.Push(PMatrix.Top);
		}
		else
		{
			MvMatrix.Push(MvMatrix.Top);
		}
	}

	public void GlLoadIdentity()
	{
		if (CurrentMatrixModeProjection)
		{
			if (PMatrix.Count > 0)
			{
				PMatrix.Pop();
			}
			PMatrix.Push(identityMatrix);
		}
		else
		{
			if (MvMatrix.Count > 0)
			{
				MvMatrix.Pop();
			}
			MvMatrix.Push(identityMatrix);
		}
	}

	public void GlOrtho(double left, double right, double bottom, double top, double zNear, double zFar)
	{
		if (CurrentMatrixModeProjection)
		{
			Mat4d.Ortho(PMatrix.Top, left, right, bottom, top, zNear, zFar);
			return;
		}
		throw new Exception("Invalid call. CurrentMatrixModeProjection is false");
	}

	public void OrthoMode(int width, int height, bool inverseY = false)
	{
		GlMatrixModeProjection();
		GlPushMatrix();
		GlLoadIdentity();
		if (inverseY)
		{
			GlOrtho(0.0, width, 0.0, height, 0.4000000059604645, 20001.0);
		}
		else
		{
			GlOrtho(0.0, width, height, 0.0, 0.4000000059604645, 20001.0);
		}
		GlMatrixModeModelView();
		GlPushMatrix();
		GlLoadIdentity();
		GL.DepthRange(0f, 20000f);
		GlTranslate(0.0, 0.0, -19849.0);
	}

	public void PerspectiveMode()
	{
		GlMatrixModeProjection();
		GlPopMatrix();
		GlMatrixModeModelView();
		GlPopMatrix();
		GL.DepthRange(0f, 1f);
	}

	public void Connect()
	{
		Compression.Reset();
		MainNetClient.Connect(Connectdata.Host, Connectdata.Port, OnConnectionResult, OnDisconnected);
		if (ClientSettings.ForceUdpOverTcp && !IsSingleplayer)
		{
			FallBackToTcp = true;
		}
		else
		{
			UdpNetClient.Connect(Connectdata.Host, Connectdata.Port);
			if (UdpNetClient is UdpNetClient udpNetClient)
			{
				Logger.Notification($"UDP: connected on local endpoint: {udpNetClient.udpClient.Client.LocalEndPoint}");
			}
		}
		SendPacketClient(new Packet_Client
		{
			Id = 33
		});
	}

	private void OnDisconnected(Exception caughtException)
	{
		Compression.Reset();
		if (exitToDisconnectScreen)
		{
			return;
		}
		MainNetClient.Dispose();
		UdpNetClient.Dispose();
		TyronThreadPool.QueueTask(async () =>
		{
			await Task.Delay(1000);
			EnqueueMainThreadTask(() =>
			{
				Compression.Reset();
				if (!exitToMainMenu && !exitToDisconnectScreen)
				{
					disconnectReason = "The connection closed unexpectedly: " + caughtException.Message;
					Logger.Error("The connection closed unexpectedly.");
					Logger.Error(caughtException);
					DestroyGameSession(gotDisconnected: true, EnumExitMode.SoftExit);
				}
			}, "disconnect");
		}, "ClientOnDisconnected");
	}

	private void OnConnectionResult(ConnectionResult result)
	{
		Connectdata.Connected = result.connected;
		Connectdata.ErrorMessage = result.errorMessage;
		if (result.exception != null)
		{
			Logger.Warning("Error while connecting to server: {0}", result.errorMessage);
			Logger.Warning(LoggerBase.CleanStackTrace(result.exception.ToString()));
		}
	}

	public void SendRequestJoin()
	{
		Logger.VerboseDebug("Sending request to join server");
		SendPacketClient(ClientPackets.RequestJoin());
	}

	public void SendLeave(int reason)
	{
		SendPacketClient(ClientPackets.Leave(reason));
	}

	public byte[] Serialize(Packet_Client packet)
	{
		CitoMemoryStream citoMemoryStream = new CitoMemoryStream();
		Packet_ClientSerializer.Serialize(citoMemoryStream, packet);
		return citoMemoryStream.ToArray();
	}

	public void SendPacket(byte[] packet)
	{
		if (disposed || MainNetClient == null)
		{
			return;
		}
		try
		{
			MainNetClient.Send(packet);
		}
		catch (ObjectDisposedException caughtException)
		{
			OnDisconnected(caughtException);
		}
	}

	public void SendPacketClient(Packet_Client packetClient)
	{
		if (packetClient != null)
		{
			byte[] packet = Serialize(packetClient);
			SendPacket(packet);
		}
	}

	public void SendPingReply()
	{
		SendPacketClient(ClientPackets.PingReply());
	}

	public void Respawn()
	{
		SendPacketClient(ClientPackets.SpecialKeyRespawn());
		EntityPlayer.Pos.Motion.Set(0.0, 0.0, 0.0);
	}

	public void SendHandInteraction(int mouseButton, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType, EnumHandInteractNw state, bool firstEvent, EnumItemUseCancelReason cancelReason = EnumItemUseCancelReason.ReleasedMouse)
	{
		if (blockSel == null && entitySel == null)
		{
			SendPacketClient(new Packet_Client
			{
				Id = 25,
				HandInteraction = new Packet_ClientHandInteraction
				{
					SlotId = player.inventoryMgr.ActiveHotbarSlotNumber,
					MouseButton = mouseButton,
					EnumHandInteract = (int)state,
					UsingCount = EntityPlayer.Controls.UsingCount,
					UseType = (int)useType,
					CancelReason = (int)cancelReason,
					FirstEvent = (firstEvent ? 1 : 0)
				}
			});
		}
		else if (blockSel != null)
		{
			SendPacketClient(new Packet_Client
			{
				Id = 25,
				HandInteraction = new Packet_ClientHandInteraction
				{
					SlotId = player.inventoryMgr.ActiveHotbarSlotNumber,
					MouseButton = mouseButton,
					X = blockSel.Position.X,
					Y = blockSel.Position.InternalY,
					Z = blockSel.Position.Z,
					HitX = CollectibleNet.SerializeDoublePrecise(blockSel.HitPosition.X),
					HitY = CollectibleNet.SerializeDoublePrecise(blockSel.HitPosition.Y),
					HitZ = CollectibleNet.SerializeDoublePrecise(blockSel.HitPosition.Z),
					OnBlockFace = blockSel.Face.Index,
					SelectionBoxIndex = blockSel.SelectionBoxIndex,
					SelectionBoxId = blockSel.SelectionBoxId,
					EnumHandInteract = (int)state,
					UsingCount = EntityPlayer.Controls.UsingCount,
					UseType = (int)useType,
					CancelReason = (int)cancelReason,
					FirstEvent = (firstEvent ? 1 : 0)
				}
			});
		}
		else
		{
			SendPacketClient(new Packet_Client
			{
				Id = 25,
				HandInteraction = new Packet_ClientHandInteraction
				{
					SlotId = player.inventoryMgr.ActiveHotbarSlotNumber,
					MouseButton = mouseButton,
					HitX = CollectibleNet.SerializeDoublePrecise(entitySel.HitPosition.X),
					HitY = CollectibleNet.SerializeDoublePrecise(entitySel.HitPosition.Y),
					HitZ = CollectibleNet.SerializeDoublePrecise(entitySel.HitPosition.Z),
					OnBlockFace = entitySel.Face.Index,
					OnEntityId = entitySel.Entity.EntityId,
					SelectionBoxIndex = entitySel.SelectionBoxIndex,
					EnumHandInteract = (int)state,
					UsingCount = EntityPlayer.Controls.UsingCount,
					UseType = (int)useType,
					FirstEvent = (firstEvent ? 1 : 0)
				}
			});
		}
	}

	public bool tryAccess(BlockSelection blockSel, EnumBlockAccessFlags flag)
	{
		EnumWorldAccessResponse enumWorldAccessResponse = WorldMap.TestBlockAccess(player, BlockSelection, flag, out string claimant);
		if (enumWorldAccessResponse != EnumWorldAccessResponse.Granted)
		{
			string text = "noprivilege-" + ((flag == EnumBlockAccessFlags.Use) ? "use" : "buildbreak") + "-" + enumWorldAccessResponse.ToString().ToLowerInvariant();
			if (claimant == null)
			{
				claimant = "?";
			}
			else if (claimant.StartsWithOrdinal("custommessage-"))
			{
				text = "noprivilege-buildbreak-" + claimant.Substring("custommessage-".Length);
			}
			api.TriggerIngameError(this, text, Lang.Get("ingameerror-" + text, claimant));
			return false;
		}
		return true;
	}

	public bool OnPlayerTryPlace(BlockSelection blockSelection, ref string failureCode)
	{
		if (!WorldMap.IsValidPos(blockSelection.Position))
		{
			failureCode = "outsideworld";
			return false;
		}
		if (!tryAccess(blockSelection, EnumBlockAccessFlags.BuildOrBreak))
		{
			return false;
		}
		ItemStack itemstack = player.InventoryManager.ActiveHotbarSlot.Itemstack;
		if (itemstack != null && itemstack.Class == EnumItemClass.Block)
		{
			Block block = World.BlockAccessor.GetBlock(blockSelection.Position);
			Block block2 = World.BlockAccessor.GetBlock(blockSelection.Position, 2);
			if (preventPlacementInLava && block2.LiquidCode == "lava" && player.worlddata.CurrentGameMode != EnumGameMode.Creative)
			{
				failureCode = "toohottoplacehere";
				return false;
			}
			failureCode = null;
			if (itemstack.Block.TryPlaceBlock(this, player, itemstack, blockSelection, ref failureCode))
			{
				SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 1, 0));
				eventManager?.TriggerBlockChanged(this, blockSelection.Position, block);
				TriggerNeighbourBlocksUpdate(BlockSelection.Position);
				return true;
			}
			if (failureCode == null)
			{
				failureCode = "generic";
			}
		}
		return false;
	}

	public void OnPlayerTryDestroyBlock(BlockSelection blockSelection)
	{
		if (WorldMap.IsValidPos(blockSelection.Position) && tryAccess(blockSelection, EnumBlockAccessFlags.BuildOrBreak))
		{
			ItemSlot activeHotbarSlot = player.InventoryManager.ActiveHotbarSlot;
			IItemStack itemstack = player.InventoryManager.ActiveHotbarSlot.Itemstack;
			bool flag = true;
			Block block = blockSelection.Block ?? World.BlockAccessor.GetBlock(blockSelection.Position);
			if (itemstack != null)
			{
				flag = itemstack.Collectible.OnBlockBrokenWith(this, Player.Entity, activeHotbarSlot, blockSelection);
			}
			else
			{
				block.OnBlockBroken(World, blockSelection.Position, Player);
			}
			if (flag)
			{
				eventManager?.TriggerBlockChanged(this, blockSelection.Position, block);
				TriggerNeighbourBlocksUpdate(blockSelection.Position);
				SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 0, 0));
			}
		}
	}

	public void OnKeyDown(KeyEvent args)
	{
		if (disposed)
		{
			return;
		}
		api.eventapi.TriggerKeyDown(args);
		if (args.Handled)
		{
			return;
		}
		int keyCode = args.KeyCode;
		KeyboardStateRaw[keyCode] = true;
		if (args.Handled = ScreenManager.hotkeyManager.TriggerGlobalHotKey(args, this, player, keyUp: false))
		{
			return;
		}
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureAllInputs())
			{
				clientSystem.OnKeyDown(args);
				return;
			}
		}
		array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].OnKeyDown(args);
			if (args.Handled)
			{
				return;
			}
		}
		PreviousKeyboardState[keyCode] = KeyboardState[keyCode];
		KeyboardState[keyCode] = true;
		bool handled = ScreenManager.hotkeyManager.TriggerHotKey(args, this, player, AllowCharacterControl, keyUp: false);
		args.Handled = handled;
	}

	public void OnKeyUp(KeyEvent args)
	{
		if (disposed)
		{
			return;
		}
		int keyCode = args.KeyCode;
		KeyboardStateRaw[keyCode] = false;
		PreviousKeyboardState[keyCode] = KeyboardState[keyCode];
		KeyboardState[keyCode] = false;
		api.eventapi.TriggerKeyUp(args);
		if (args.Handled || (args.Handled = ScreenManager.hotkeyManager.TriggerGlobalHotKey(args, this, player, keyUp: true)))
		{
			return;
		}
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureAllInputs())
			{
				clientSystem.OnKeyUp(args);
				return;
			}
		}
		array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].OnKeyUp(args);
			if (args.Handled)
			{
				return;
			}
		}
		args.Handled = ScreenManager.hotkeyManager.TriggerHotKey(args, this, player, AllowCharacterControl, keyUp: true);
	}

	public void OnKeyPress(KeyEvent eventArgs)
	{
		if (disposed)
		{
			return;
		}
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureAllInputs())
			{
				clientSystem.OnKeyPress(eventArgs);
				return;
			}
		}
		array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].OnKeyPress(eventArgs);
			if (eventArgs.Handled)
			{
				break;
			}
		}
	}

	public void OnMouseDownRaw(MouseEvent args)
	{
		if (disposed)
		{
			return;
		}
		UpdateMouseButtonState(args.Button, MouseStateRaw, value: true);
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureRawMouse())
			{
				clientSystem.OnMouseDown(args);
				return;
			}
		}
		int num = (int)(args.Button + 240);
		PreviousKeyboardState[num] = KeyboardState[num];
		KeyboardState[num] = true;
		ScreenManager.hotkeyManager.OnMouseButton(this, args.Button, args.Modifiers, buttonDown: true);
	}

	private void UpdateMouseButtonState(EnumMouseButton button, MouseButtonState mouseState, bool value)
	{
		if (button == EnumMouseButton.Left)
		{
			mouseState.Left = value;
		}
		if (button == EnumMouseButton.Middle)
		{
			mouseState.Middle = value;
		}
		if (button == EnumMouseButton.Right)
		{
			mouseState.Right = value;
		}
	}

	public bool UpdateMouseButtonState(EnumMouseButton button, bool down)
	{
		MouseEvent mouseEvent = Platform.CreateMouseEvent(button);
		if (down)
		{
			api.eventapi.TriggerMouseDown(mouseEvent);
			if (mouseEvent.Handled)
			{
				return true;
			}
			ClientSystem[] array = clientSystems;
			foreach (ClientSystem clientSystem in array)
			{
				if (clientSystem.CaptureAllInputs())
				{
					clientSystem.OnMouseDown(mouseEvent);
					return true;
				}
			}
			array = clientSystems;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].OnMouseDown(mouseEvent);
				if (mouseEvent.Handled)
				{
					return true;
				}
			}
			UpdateMouseButtonState(button, InWorldMouseState, value: true);
		}
		else
		{
			api.eventapi.TriggerMouseUp(mouseEvent);
			if (mouseEvent.Handled)
			{
				return true;
			}
			UpdateMouseButtonState(button, InWorldMouseState, value: false);
			ClientSystem[] array = clientSystems;
			foreach (ClientSystem clientSystem2 in array)
			{
				if (clientSystem2.CaptureAllInputs())
				{
					clientSystem2.OnMouseUp(mouseEvent);
					return true;
				}
			}
			array = clientSystems;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].OnMouseUp(mouseEvent);
				if (mouseEvent.Handled)
				{
					return true;
				}
			}
		}
		return false;
	}

	public void OnMouseUpRaw(MouseEvent args)
	{
		int num = (int)(args.Button + 240);
		PreviousKeyboardState[num] = KeyboardState[num];
		KeyboardState[num] = false;
		UpdateMouseButtonState(args.Button, MouseStateRaw, value: false);
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureRawMouse())
			{
				clientSystem.OnMouseUp(args);
				return;
			}
		}
		ScreenManager.hotkeyManager.OnMouseButton(this, args.Button, args.Modifiers, buttonDown: false);
	}

	public void OnMouseWheel(MouseWheelEventArgs args)
	{
		api.eventapi.TriggerMouseWheelMove(args);
		if (args.IsHandled)
		{
			return;
		}
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureAllInputs())
			{
				clientSystem.OnMouseWheel(args);
				return;
			}
		}
		array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].OnMouseWheel(args);
			if (args.IsHandled)
			{
				break;
			}
		}
	}

	public void OnMouseMove(MouseEvent args)
	{
		api.eventapi.TriggerMouseMove(args);
		if (args.Handled)
		{
			return;
		}
		MouseCurrentX = (MouseGrabbed ? (Width / 2) : args.X);
		MouseCurrentY = (MouseGrabbed ? (Height / 2) : args.Y);
		MouseDeltaX += (double)(args.DeltaX * ClientSettings.MouseSensivity) / 100.0;
		MouseDeltaY += (double)(args.DeltaY * ClientSettings.MouseSensivity) / 100.0;
		ClientSystem[] array = clientSystems;
		foreach (ClientSystem clientSystem in array)
		{
			if (clientSystem.CaptureAllInputs())
			{
				clientSystem.OnMouseMove(args);
				return;
			}
		}
		array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].OnMouseMove(args);
			if (args.Handled)
			{
				break;
			}
		}
	}

	public void UpdateFreeMouse()
	{
		int keyCode = ScreenManager.hotkeyManager.HotKeys["togglemousecontrol"].CurrentMapping.KeyCode;
		bool flag = KeyboardState[keyCode];
		bool flag2 = OpenedGuis.Where((GuiDialog gui) => gui.DialogType == EnumDialogType.Dialog).Any((GuiDialog dlg) => dlg.PrefersUngrabbedMouse);
		bool flag3 = OpenedGuis.Any((GuiDialog gui) => gui.DisableMouseGrab);
		bool mouseGrabbed = MouseGrabbed;
		MouseGrabbed = ScreenManager.Platform.IsFocused && !exitToDisconnectScreen && !exitToMainMenu && BlocksReceivedAndLoaded && !flag3 && ((DialogsOpened == 0 || (ClientSettings.ImmersiveMouseMode && !flag2)) ^ flag);
		mouseWorldInteractAnyway = !MouseGrabbed && !flag2;
		if (!mouseGrabbed && MouseGrabbed)
		{
			float num = GameMath.AngleRadDistance(EntityPlayer.Pos.Yaw, EntityPlayer.WalkYaw);
			EntityPlayer.Pos.Yaw += num;
		}
	}

	public void UpdateCameraYawPitch(float dt)
	{
		double num = GameMath.Clamp(dt / (1f / 75f), 0f, 3f);
		double num2 = GameMath.Clamp((double)((float)ClientSettings.MouseSmoothing / 100f) * num, 0.009999999776482582, 1.0);
		float num3 = 0.5f * (float)num;
		if (player.Entity.Swimming && player.CameraMode == EnumCameraMode.FirstPerson && swimmingMouseSmoothing > 0f)
		{
			num2 = GameMath.Clamp(1f - swimmingMouseSmoothing, 0f, 1f);
		}
		double num4 = num2 * (MouseDeltaX - DelayedMouseDeltaX);
		double num5 = num2 * (MouseDeltaY - DelayedMouseDeltaY);
		DelayedMouseDeltaX += num4;
		DelayedMouseDeltaY += num5;
		EntityPlayer entityPlayer = EntityPlayer;
		if (entityPlayer != null && entityPlayer.MountedOn?.AngleMode == EnumMountAngleMode.FixateYaw && player.CameraMode != EnumCameraMode.FirstPerson)
		{
			float yaw = EntityPlayer.MountedOn.SeatPosition.Yaw;
			EntityPlayer.Pos.Yaw = yaw;
			EntityPlayer.BodyYaw = yaw;
			if (MainCamera.CameraMode == EnumCameraMode.FirstPerson)
			{
				mouseYaw = yaw;
			}
		}
		if (!AllowCameraControl || !Platform.IsFocused || !MouseGrabbed)
		{
			return;
		}
		EnumMountAngleMode enumMountAngleMode = EnumMountAngleMode.Unaffected;
		float? num6 = null;
		IMountableSeat mountedOn = EntityPlayer.MountedOn;
		if (EntityPlayer.MountedOn != null)
		{
			enumMountAngleMode = mountedOn.AngleMode;
			num6 = mountedOn.SeatPosition.Yaw;
		}
		if (player.CameraMode == EnumCameraMode.Overhead)
		{
			float num7 = GameMath.AngleRadDistance(EntityPlayer.Pos.Yaw, EntityPlayer.WalkYaw) * 0.4f;
			EntityPlayer.Pos.Yaw += num7;
			mouseYaw -= (float)(num4 * (double)rotationspeed * 1.0 / 75.0);
			mouseYaw = GameMath.Mod(mouseYaw, (float)Math.PI * 2f);
		}
		else
		{
			mouseYaw -= (float)(num4 * (double)rotationspeed * 1.0 / 75.0);
			if (EntityPlayer.HeadYawLimits != null)
			{
				AngleConstraint headYawLimits = EntityPlayer.HeadYawLimits;
				float val = GameMath.AngleRadDistance(headYawLimits.CenterRad, mouseYaw);
				mouseYaw = headYawLimits.CenterRad + GameMath.Clamp(val, 0f - headYawLimits.RangeRad, headYawLimits.RangeRad);
			}
			EntityPlayer.Pos.Yaw = mouseYaw;
		}
		bool flag = MainCamera.CameraMode == EnumCameraMode.FirstPerson && mountedOn != null;
		if ((enumMountAngleMode == EnumMountAngleMode.PushYaw || enumMountAngleMode == EnumMountAngleMode.Push) | flag)
		{
			float num8 = (0f - num3) * GameMath.AngleRadDistance(mountedOn.SeatPosition.Yaw, prevMountAngles.Y);
			prevMountAngles.Y += num8;
			if (enumMountAngleMode == EnumMountAngleMode.Push)
			{
				EntityPlayer.Pos.Roll -= GameMath.AngleRadDistance(mountedOn.SeatPosition.Roll, prevMountAngles.X);
				EntityPlayer.Pos.Pitch -= GameMath.AngleRadDistance(mountedOn.SeatPosition.Pitch, prevMountAngles.Z);
			}
			if (player.CameraMode == EnumCameraMode.Overhead)
			{
				EntityPlayer.WalkYaw += num8;
			}
			else
			{
				mouseYaw += num8;
				EntityPlayer.Pos.Yaw += num8;
				EntityPlayer.BodyYaw += num8;
			}
		}
		if ((enumMountAngleMode == EnumMountAngleMode.Fixate || enumMountAngleMode == EnumMountAngleMode.FixateYaw) && !flag)
		{
			EntityPlayer.Pos.Yaw = num6.Value;
		}
		if (enumMountAngleMode == EnumMountAngleMode.Fixate)
		{
			EntityPlayer.Pos.Pitch = EntityPlayer.MountedOn.SeatPosition.Pitch;
		}
		else
		{
			EntityPlayer.Pos.Pitch += (float)(num5 * (double)rotationspeed * 1.0 / 75.0 * (double)((!ClientSettings.InvertMouseYAxis) ? 1 : (-1)));
		}
		if (mountedOn != null)
		{
			prevMountAngles.Set(mountedOn.SeatPosition.Roll, prevMountAngles.Y, mountedOn.SeatPosition.Pitch);
		}
		EntityPlayer.Pos.Pitch = GameMath.Clamp(EntityPlayer.Pos.Pitch, 1.5857964f, 4.697389f);
		EntityPlayer.Pos.Yaw = GameMath.Mod(EntityPlayer.Pos.Yaw, (float)Math.PI * 2f);
		mousePitch = EntityPlayer.Pos.Pitch;
	}

	public void OnFocusChanged(bool focus)
	{
		if (!disposed)
		{
			eventManager?.TriggerGameWindowFocus(focus);
			if (!focus)
			{
				MouseStateRaw.Clear();
				InWorldMouseState.Clear();
			}
		}
	}

	public bool OnFileDrop(string filename)
	{
		return api.eventapi.TriggerFileDrop(filename);
	}

	public Item GetItem(int itemId)
	{
		if (itemId >= Items.Count)
		{
			throw new ArgumentOutOfRangeException($"Cannot get item of id {itemId}, item list count is only until {Items.Count}!");
		}
		return Items[itemId];
	}

	public Block GetBlock(int blockId)
	{
		if (blockId >= Blocks.Count)
		{
			return getOrCreateNoBlock(blockId);
		}
		return Blocks[blockId];
	}

	private Block getOrCreateNoBlock(int id)
	{
		if (!noBlocks.TryGetValue(id, out var value))
		{
			value = (noBlocks[id] = BlockList.getNoBlock(id, api));
		}
		return value;
	}

	public EntityProperties GetEntityType(AssetLocation entityCode)
	{
		EntityClassesByCode.TryGetValue(entityCode, out var value);
		return value;
	}

	public void ReloadTextures()
	{
		BlockAtlasManager.PauseRegenMipmaps().ReloadTextures();
		ItemAtlasManager.PauseRegenMipmaps().ReloadTextures();
		EntityAtlasManager.PauseRegenMipmaps().ReloadTextures();
		eventManager?.TriggerReloadTextures();
		BlockAtlasManager.ResumeRegenMipmaps();
		ItemAtlasManager.ResumeRegenMipmaps();
		EntityAtlasManager.ResumeRegenMipmaps();
		foreach (Block block in Blocks)
		{
			block.LoadTextureSubIdForBlockColor();
		}
	}

	public int WhiteTexture()
	{
		if (whitetexture == -1)
		{
			BitmapRef bmp = Platform.CreateBitmap(1, 1);
			int[] pixels = new int[1] { ColorUtil.ToRgba(255, 255, 255, 255) };
			Platform.SetBitmapPixelsArgb(bmp, pixels);
			whitetexture = Platform.LoadTexture(bmp);
		}
		return whitetexture;
	}

	public int GetOrLoadCachedTexture(AssetLocation name)
	{
		name = name.WithPathPrefixOnce("textures/");
		if (!texturesByLocation.TryGetValue(name, out var value))
		{
			byte[] array = Platform.AssetManager.TryGet(name)?.Data;
			if (array == null)
			{
				return 0;
			}
			BitmapRef bitmapRef = Platform.CreateBitmapFromPng(array, array.Length);
			value = (texturesByLocation[name] = new LoadedTexture(api, Platform.LoadTexture(bitmapRef), bitmapRef.Width, bitmapRef.Height));
			bitmapRef.Dispose();
		}
		return value.TextureId;
	}

	public void GetOrLoadCachedTexture(AssetLocation name, ref LoadedTexture intoTexture)
	{
		intoTexture.IgnoreUndisposed = true;
		if (!texturesByLocation.TryGetValue(name, out var value))
		{
			byte[] array = Platform.AssetManager.TryGet(name.Clone().WithPathPrefixOnce("textures/"))?.Data;
			if (array == null)
			{
				return;
			}
			BitmapRef bitmapRef = Platform.CreateBitmapFromPng(array, array.Length);
			value = (texturesByLocation[name] = new LoadedTexture(api, Platform.LoadTexture(bitmapRef), bitmapRef.Width, bitmapRef.Height));
			bitmapRef.Dispose();
		}
		if (value.TextureId != intoTexture.TextureId && intoTexture.TextureId != 0)
		{
			intoTexture.Dispose();
		}
		intoTexture.TextureId = value.TextureId;
		intoTexture.Width = value.Width;
		intoTexture.Height = value.Height;
	}

	public void GetOrLoadCachedTexture(AssetLocation name, BitmapRef bmp, ref LoadedTexture intoTexture)
	{
		if (!texturesByLocation.TryGetValue(name, out var value))
		{
			value = (texturesByLocation[name] = new LoadedTexture(api, Platform.LoadTexture(bmp), bmp.Width, bmp.Height));
		}
		if (value.TextureId != intoTexture.TextureId && intoTexture.TextureId != 0)
		{
			intoTexture.Dispose();
		}
		intoTexture.TextureId = value.TextureId;
		intoTexture.Width = value.Width;
		intoTexture.Height = value.Height;
	}

	public bool DeleteCachedTexture(AssetLocation name)
	{
		if (name == null || !texturesByLocation.TryGetValue(name, out var value))
		{
			return false;
		}
		texturesByLocation.Remove(name);
		value?.Dispose();
		return true;
	}

	public void PauseGame(bool paused)
	{
		if (IsSingleplayer && BlocksReceivedAndLoaded)
		{
			IsPaused = paused;
			Platform.SetGamePausedState(paused);
			api.eventapi.TriggerPauseResume(paused);
			if (paused)
			{
				GameWorldCalendar.Stop();
				InWorldStopwatch.Stop();
			}
			else
			{
				GameWorldCalendar.Start();
				InWorldStopwatch.Start();
			}
			World.Logger.Notification("Client pause state is now {0}", paused ? "on" : "off");
		}
	}

	private void ViewDistanceChanged(int newValue)
	{
		if (newValue != player.worlddata.LastApprovedViewDistance)
		{
			player.worlddata.RequestNewViewDistance(this);
		}
		frustumCuller.UpdateViewDistance(newValue);
	}

	private void OnVsyncChanged(int vsyncmode)
	{
		Platform.SetVSync(vsyncmode != 0);
	}

	public void FindCmd(string search)
	{
		ICoreClientAPI coreClientAPI = api;
		List<int> list = new List<int>();
		foreach (Block block in Blocks)
		{
			if (block.Code != null && block.Code.Path.Contains(search))
			{
				list.Add(block.BlockId);
			}
		}
		EntityPos defaultSpawnPosition = coreClientAPI.World.DefaultSpawnPosition;
		int num = (int)defaultSpawnPosition.X;
		int num2 = (int)defaultSpawnPosition.Z;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<long, ClientChunk> chunk in WorldMap.chunks)
		{
			chunk.Value.Unpack();
			if (chunk.Value.Data is ChunkData chunkData)
			{
				BlockPos blockPos = chunkData.FindFirst(list);
				if (!(blockPos == null))
				{
					long key = chunk.Key;
					int num3 = (int)(key % WorldMap.index3dMulX) * 32 + blockPos.X;
					int num4 = (int)(key / WorldMap.index3dMulX / WorldMap.index3dMulZ) * 32 + blockPos.Y;
					int num5 = (int)(key / WorldMap.index3dMulX % WorldMap.index3dMulZ) * 32 + blockPos.Z;
					stringBuilder.Append("\nFound at " + (num3 - num) + "," + num4 + "," + (num5 - num2));
				}
			}
		}
		if (stringBuilder.Length == 0)
		{
			stringBuilder.Append("No block matching '" + search + "' found");
		}
		coreClientAPI.ShowChatMessage(stringBuilder.ToString());
	}

	public float EyesInLavaDepth()
	{
		double y = MainCamera.CameraEyePos.Y;
		BlockPos blockPos = new BlockPos((int)MainCamera.CameraEyePos.X, (int)y, (int)MainCamera.CameraEyePos.Z);
		AssetLocation code = WorldMap.RelaxedBlockAccess.GetBlock(blockPos).Code;
		if (!(code != null) || !code.PathStartsWith("lava"))
		{
			return 0f;
		}
		float num = 1f - ((float)y - (float)(int)y);
		while (true)
		{
			AssetLocation code2 = WorldMap.RelaxedBlockAccess.GetBlock(blockPos.Up()).Code;
			if ((object)code2 == null || !code2.PathStartsWith("lava"))
			{
				break;
			}
			num++;
		}
		return num;
	}

	public float EyesInWaterDepth()
	{
		double y = MainCamera.CameraEyePos.Y;
		BlockPos blockPos = new BlockPos((int)MainCamera.CameraEyePos.X, (int)y, (int)MainCamera.CameraEyePos.Z);
		string liquidCode = WorldMap.RelaxedBlockAccess.GetBlock(blockPos, 2).LiquidCode;
		if (!(liquidCode == "water") && !(liquidCode == "seawater") && !(liquidCode == "saltwater"))
		{
			return 0f;
		}
		float num = 1f - ((float)y - (float)(int)y);
		liquidCode = WorldMap.RelaxedBlockAccess.GetBlock(blockPos.Up(), 2).LiquidCode;
		while (true)
		{
			switch (liquidCode)
			{
			case "water":
			case "seawater":
			case "saltwater":
				break;
			default:
				return num;
			}
			num++;
			liquidCode = WorldMap.RelaxedBlockAccess.GetBlock(blockPos.Up(), 2).LiquidCode;
		}
	}

	public int GetEyesInWaterColorShift()
	{
		double y = MainCamera.CameraEyePos.Y;
		string liquidCode = WorldMap.RelaxedBlockAccess.GetBlockRaw((int)MainCamera.CameraEyePos.X, (int)y, (int)MainCamera.CameraEyePos.Z, 2).LiquidCode;
		bool flag = liquidCode == "water" || liquidCode == "seawater" || liquidCode == "saltwater";
		liquidCode = WorldMap.RelaxedBlockAccess.GetBlockRaw((int)MainCamera.CameraEyePos.X, (int)(MainCamera.CameraEyePos.Y + 1.0), (int)MainCamera.CameraEyePos.Z, 2).LiquidCode;
		bool flag2 = liquidCode == "water" || liquidCode == "seawater" || liquidCode == "saltwater";
		if (flag & flag2)
		{
			return 100;
		}
		if (!flag)
		{
			return 0;
		}
		float num = (float)y - (float)(int)y;
		return (int)Math.Max(0f, Math.Min(100f, 600f * (1.04f - num)));
	}

	public int GetEyesInLavaColorShift()
	{
		double y = MainCamera.CameraEyePos.Y;
		AssetLocation code = WorldMap.RelaxedBlockAccess.GetBlockRaw((int)MainCamera.CameraEyePos.X, (int)y, (int)MainCamera.CameraEyePos.Z, 2).Code;
		bool flag = code != null && code.PathStartsWith("lava");
		code = WorldMap.RelaxedBlockAccess.GetBlockRaw((int)MainCamera.CameraEyePos.X, (int)(MainCamera.CameraEyePos.Y + 1.0), (int)MainCamera.CameraEyePos.Z, 2).Code;
		bool flag2 = code != null && code.PathStartsWith("lava");
		if (flag & flag2)
		{
			return 100;
		}
		if (!flag)
		{
			return 0;
		}
		float num = (float)y - (float)(int)y;
		return (int)Math.Max(0f, Math.Min(100f, 600f * (1.04f - num)));
	}

	public void RedrawAllBlocks()
	{
		ShouldRedrawAllBlocks = true;
	}

	public void OnResize()
	{
		Platform.GlViewport(0, 0, Platform.WindowSize.Width, Platform.WindowSize.Height);
		Reset3DProjection();
	}

	public void DoReconnect()
	{
		doReconnect = true;
	}

	public void ExitAndSwitchServer(MultiplayerServerEntry redirect)
	{
		if (IsSingleplayer)
		{
			Platform.ExitSinglePlayerServer(EnumExitMode.SoftExit);
		}
		RedirectTo = redirect;
		exitToMainMenu = true;
	}

	public MultiplayerServerEntry GetRedirect()
	{
		return RedirectTo;
	}

	public void DestroyGameSession(bool gotDisconnected, EnumExitMode exitMode)
	{
		if (exitToMainMenu || exitToDisconnectScreen)
		{
			return;
		}
		Platform.ShaderUniforms = new DefaultShaderUniforms();
		Logger.Notification("Destroying game session, waiting up to 200ms for client threads to exit");
		api.eventapi.TriggerLeaveWorld();
		threadsShouldExit = true;
		int num = 2;
		while (num-- > 0)
		{
			bool flag = true;
			foreach (Thread clientThread in clientThreads)
			{
				flag &= !clientThread.IsAlive;
			}
			if (flag)
			{
				break;
			}
			Thread.Sleep(100);
		}
		_clientThreadsCts.Cancel();
		if (IsSingleplayer && ScreenManager.Platform.IsServerRunning)
		{
			Logger.Notification("Stopping single player server");
			Platform.ExitSinglePlayerServer(exitMode);
		}
		RedirectTo = null;
		exitToMainMenu = !gotDisconnected;
		exitToDisconnectScreen = gotDisconnected;
		MouseGrabbed = false;
		api.eventapi.TriggerLeftWorld();
		Dispose();
	}

	private void UpdateResize()
	{
		if (lastWidth != Platform.WindowSize.Width || lastHeight != Platform.WindowSize.Height)
		{
			lastWidth = Platform.WindowSize.Width;
			lastHeight = Platform.WindowSize.Height;
			OnResize();
		}
	}

	public void EnqueueMainThreadTask(Action action, string code)
	{
		lock (MainThreadTasksLock)
		{
			MainThreadTasks.Enqueue(new ClientTask
			{
				Action = action,
				Code = code
			});
		}
	}

	public void EnqueueGameLaunchTask(Action action, string code)
	{
		if (!disposed)
		{
			GameLaunchTasks.Enqueue(new ClientTask
			{
				Action = action,
				Code = code
			});
		}
	}

	public void Dispose()
	{
		disposed = true;
		api.disposed = true;
		BlockChunkDataLayer.Dispose();
		MeshData.Recycler?.Dispose();
		MusicEngineCts?.Cancel();
		ClientSystem[] array = clientSystems;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Dispose(this);
		}
		Thread.Sleep(100);
		foreach (LoadedTexture value in texturesByLocation.Values)
		{
			value?.Dispose();
		}
		if (Blocks != null)
		{
			foreach (Block block in Blocks)
			{
				block?.OnUnloaded(api);
			}
		}
		if (Items != null)
		{
			foreach (Item item in Items)
			{
				item?.OnUnloaded(api);
			}
		}
		PhysicsBehaviorBase.collisionTester = null;
		quadModel?.Dispose();
		api?.Dispose();
		ItemAtlasManager.Dispose();
		BlockAtlasManager.Dispose();
		EntityAtlasManager.Dispose();
		TesselatorManager.Dispose();
		WorldMap.Dispose();
		_clientThreadsCts.Dispose();
		MusicEngineCts?.Dispose();
		MusicEngineCts = null;
		Platform.ClearOnCrash();
		ClientSettings.Inst.ClearWatchers();
		ScreenRunningGame.ScreenManager.registerSettingsWatchers();
		ScreenManager.hotkeyManager.ClearInGameHotKeyHandlers();
		VtmlUtil.TagConverters.Clear();
		eventManager?.Dispose();
		eventManager = null;
		rand?.Dispose();
		ClassRegistry = null;
		MainNetClient?.Dispose();
		UdpNetClient?.Dispose();
		Platform.Logger.ClearWatchers();
		MeshData.Recycler = null;
		BlockChunkDataLayer.blocksByPaletteIndex = null;
		Platform.AssetManager.UnloadExternalAssets(Logger);
		Platform.AssetManager.CustomModOrigins.Clear();
	}

	public int ApplyColorMapOnRgba(string climateColorMap, string seasonColorMap, int color, int posX, int posY, int posZ, bool flipRb = true)
	{
		return WorldMap.ApplyColorMapOnRgba(climateColorMap, seasonColorMap, color, posX, posY, posZ, flipRb);
	}

	public int ApplyColorMapOnRgba(ColorMap climateMap, ColorMap seasonMap, int color, int posX, int posY, int posZ, bool flipRb = true)
	{
		return WorldMap.ApplyColorMapOnRgba(climateMap, seasonMap, color, posX, posY, posZ, flipRb);
	}

	public int ApplyColorMapOnRgba(string climateColorMap, string seasonColorMap, int color, int rain, int temp, bool flipRb = true)
	{
		return WorldMap.ApplyColorMapOnRgba(climateColorMap, seasonColorMap, color, rain, temp, flipRb);
	}

	public void TryAttackEntity(EntitySelection selection)
	{
		if (selection != null)
		{
			Entity entity = selection.Entity;
			Cuboidd cuboidd = entity.SelectionBox.ToDouble().Translate(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
			EntityPos pos = EntityPlayer.Pos;
			ItemStack itemStack = Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
			float num = itemStack?.Collectible.GetAttackRange(itemStack) ?? GlobalConstants.DefaultAttackRange;
			if (cuboidd.ShortestDistanceFrom(pos.X + EntityPlayer.LocalEyePos.X, pos.Y + EntityPlayer.LocalEyePos.Y, pos.Z + EntityPlayer.LocalEyePos.Z) <= (double)num)
			{
				selection.Entity.OnInteract(EntityPlayer, player.inventoryMgr.ActiveHotbarSlot, selection.HitPosition, EnumInteractMode.Attack);
				SendPacketClient(ClientPackets.EntityInteraction(0, selection.Entity.EntityId, selection.Face, selection.HitPosition, selection.SelectionBoxIndex));
			}
		}
	}

	public void CloneBlockDamage(BlockPos sourcePos, BlockPos targetPos)
	{
		if (damagedBlocks.TryGetValue(sourcePos, out var value))
		{
			if (!damagedBlocks.TryGetValue(targetPos, out var value2))
			{
				Dictionary<BlockPos, BlockDamage> dictionary = damagedBlocks;
				BlockDamage blockDamage = new BlockDamage
				{
					Position = targetPos,
					Block = blockAccessor.GetBlock(targetPos),
					Facing = value.Facing,
					RemainingResistance = value.RemainingResistance,
					LastBreakEllapsedMs = value.LastBreakEllapsedMs,
					BeginBreakEllapsedMs = value.BeginBreakEllapsedMs,
					ByPlayer = value.ByPlayer,
					Tool = value.Tool,
					BreakingCounter = value.BreakingCounter
				};
				BlockDamage blockDamage2 = blockDamage;
				dictionary[targetPos] = blockDamage;
				value2 = blockDamage2;
			}
			else
			{
				value2.RemainingResistance = value.RemainingResistance;
				value2.LastBreakEllapsedMs = value.LastBreakEllapsedMs;
				value2.BeginBreakEllapsedMs = value.BeginBreakEllapsedMs;
				value2.Tool = value.Tool;
				value2.BreakingCounter = value.BreakingCounter;
			}
			eventManager?.TriggerBlockBreaking(value2);
		}
	}

	public void IncurBlockDamage(BlockSelection blockSelection, EnumTool? withTool, float damage)
	{
		Block block = blockAccessor.GetBlock(blockSelection.Position);
		BlockDamage blockDamage = loadOrCreateBlockDamage(blockSelection, block, withTool, null);
		long elapsedMilliseconds = ElapsedMilliseconds;
		int num = (int)(elapsedMilliseconds - blockDamage.LastBreakEllapsedMs);
		blockDamage.RemainingResistance = block.OnGettingBroken(player, blockSelection, player.inventoryMgr.ActiveHotbarSlot, blockDamage.RemainingResistance, (float)num / 1000f, blockDamage.BreakingCounter);
		blockDamage.BreakingCounter++;
		blockDamage.Facing = blockSelection.Face;
		if (blockDamage.Position != blockSelection.Position || blockDamage.Block != block)
		{
			blockDamage.RemainingResistance = block.GetResistance(BlockAccessor, blockSelection.Position);
			blockDamage.Block = block;
			blockDamage.Position = blockSelection.Position;
		}
		blockDamage.LastBreakEllapsedMs = elapsedMilliseconds;
	}

	public BlockDamage loadOrCreateBlockDamage(BlockSelection blockSelection, Block block, EnumTool? tool, IPlayer byPlayer)
	{
		damagedBlocks.TryGetValue(blockSelection.Position, out var value);
		if (value == null)
		{
			value = new BlockDamage
			{
				Position = blockSelection.Position.Copy(),
				Block = block,
				Facing = blockSelection.Face,
				RemainingResistance = block.GetResistance(BlockAccessor, blockSelection.Position),
				LastBreakEllapsedMs = ElapsedMilliseconds,
				BeginBreakEllapsedMs = ElapsedMilliseconds,
				ByPlayer = byPlayer,
				Tool = tool
			};
			damagedBlocks[blockSelection.Position.Copy()] = value;
		}
		else if (value.Tool != tool)
		{
			value.RemainingResistance = block.GetResistance(BlockAccessor, blockSelection.Position);
			value.Tool = tool;
		}
		return value;
	}

	public void SetCameraShake(float strength)
	{
		MainCamera.CameraShakeStrength = strength * ClientSettings.CameraShakeStrength;
	}

	public void AddCameraShake(float strength)
	{
		MainCamera.CameraShakeStrength += strength * ClientSettings.CameraShakeStrength;
	}

	public void ReduceCameraShake(float amount)
	{
		MainCamera.CameraShakeStrength = Math.Max(0f, MainCamera.CameraShakeStrength - amount * ClientSettings.CameraShakeStrength);
	}

	private List<string> makeEntityCodesCache()
	{
		ICollection<AssetLocation> keys = EntityClassesByCode.Keys;
		List<string> list = new List<string>(keys.Count);
		foreach (AssetLocation item in keys)
		{
			list.Add(item.ToShortString());
		}
		return list;
	}

	public IMiniDimension GetOrCreateDimension(int dimId, Vec3d pos)
	{
		return ((ClientWorldMap)worldmap).GetOrCreateDimension(dimId, pos);
	}

	public bool TryGetMiniDimension(Vec3i origin, out IMiniDimension dimension)
	{
		return MiniDimensions.TryGetValue(BlockAccessorMovable.CalcSubDimensionId(origin), out dimension);
	}

	public void SetBlocksPreviewDimension(int dimId)
	{
		Dimensions.BlocksPreviewSubDimension_Client = dimId;
	}

	public void SetChunkColumnVisible(int cx, int cz, int dimension)
	{
		for (int i = 0; i < worldmap.chunkMapSizeY; i++)
		{
			long key = worldmap.ChunkIndex3D(new ChunkPos(cx, i, cz, dimension));
			if (WorldMap.chunks.TryGetValue(key, out var value))
			{
				int bufIndex = ClientChunk.bufIndex;
				ClientChunk.bufIndex = 0;
				value.SetVisible(visible: true);
				ClientChunk.bufIndex = 1;
				value.SetVisible(visible: true);
				ClientChunk.bufIndex = bufIndex;
			}
		}
	}

	public Entity SpawnItemEntity(ItemStack itemstack, Vec3d position, Vec3d velocity = null)
	{
		return null;
	}

	public Entity SpawnItemEntity(ItemStack itemstack, BlockPos pos, Vec3d velocity = null)
	{
		return null;
	}

	public void SpawnEntity(Entity entity)
	{
	}

	public void SpawnPriorityEntity(Entity entity)
	{
	}

	public IPlayer[] GetPlayersAround(Vec3d position, float horRange, float vertRange, ActionConsumable<IPlayer> matches = null)
	{
		List<IPlayer> list = new List<IPlayer>();
		float horRangeSq = horRange * horRange;
		foreach (ClientPlayer value in PlayersByUid.Values)
		{
			if (value.Entity != null && value.Entity.Pos.InRangeOf(position, horRangeSq, vertRange) && (matches == null || matches(value)))
			{
				list.Add(value);
			}
		}
		return list.ToArray();
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

	public override Cuboidf[] GetBlockIntersectionBoxes(BlockPos pos)
	{
		return GetBlockIntersectionBoxes(pos, LiquidSelectable);
	}

	public override bool IsValidPos(BlockPos pos)
	{
		return WorldMap.IsValidPos(pos);
	}

	public void TrySetWorldConfig(byte[] configBytes)
	{
		if (WorldConfig == null && configBytes != null)
		{
			WorldConfig = new TreeAttribute();
			WorldConfig.FromBytes(configBytes);
		}
	}

	public override Block GetBlock(BlockPos pos)
	{
		return WorldMap.RelaxedBlockAccess.GetBlock(pos);
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (eventManager != null)
		{
			return eventManager.AddGameTickListener(OnGameTick, millisecondInterval, initialDelayOffsetMs);
		}
		return 0L;
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return eventManager.AddGameTickListener(OnGameTick, errorHandler, millisecondInterval, initialDelayOffsetMs);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay)
	{
		return RegisterCallback(OnTimePassed, millisecondDelay, permittedWhilePaused: false);
	}

	public long RegisterCallback(Action<float> OnTimePassed, int millisecondDelay, bool permittedWhilePaused)
	{
		if (IsPaused && !permittedWhilePaused)
		{
			Logger.Notification("Call to RegisterCallback while game is paused");
			if (ClientSettings.DeveloperMode && extendedDebugInfo)
			{
				throw new Exception("Call to RegisterCallback while game is paused. ExtendedDebug info and developermode is enabled, so will crash on this for reporting reasons.");
			}
		}
		if (eventManager != null)
		{
			return eventManager.AddDelayedCallback(OnTimePassed, millisecondDelay);
		}
		return 0L;
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (eventManager != null)
		{
			return eventManager.AddGameTickListener(OnGameTick, pos, millisecondInterval, initialDelayOffsetMs);
		}
		return 0L;
	}

	public long RegisterGameTickListener(Action<IWorldAccessor, BlockPos, float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (eventManager != null)
		{
			return eventManager.AddGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
		}
		return 0L;
	}

	public long RegisterGameTickListener(Action<float> OnGameTick, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		if (eventManager != null)
		{
			return eventManager.AddGameTickListener(OnGameTick, pos, errorHandler, millisecondInterval, initialDelayOffsetMs);
		}
		return 0L;
	}

	public long RegisterCallback(Action<IWorldAccessor, BlockPos, float> OnTimePassed, BlockPos pos, int millisecondDelay)
	{
		if (eventManager != null)
		{
			return eventManager.AddDelayedCallback(OnTimePassed, pos, millisecondDelay);
		}
		return 0L;
	}

	public long RegisterCallbackUnique(Action<IWorldAccessor, BlockPos, float> OnTimePassed, BlockPos pos, int millisecondDelay)
	{
		if (eventManager != null)
		{
			return eventManager.AddSingleDelayedCallback(OnTimePassed, pos, millisecondDelay);
		}
		return 0L;
	}

	public void UnregisterCallback(long listenerId)
	{
		eventManager?.RemoveDelayedCallback(listenerId);
	}

	public void UnregisterGameTickListener(long listenerId)
	{
		eventManager?.RemoveGameTickListener(listenerId);
	}

	public void TriggerNeighbourBlocksUpdate(BlockPos pos)
	{
		BlockFacing[] aLLFACES = BlockFacing.ALLFACES;
		foreach (BlockFacing blockFacing in aLLFACES)
		{
			BlockPos pos2 = pos.AddCopy(blockFacing.Normali.X, blockFacing.Normali.Y, blockFacing.Normali.Z);
			Block block = WorldMap.RelaxedBlockAccess.GetBlock(pos2);
			block.OnNeighbourBlockChange(this, pos2, pos);
			if (block.ForFluidsLayer)
			{
				continue;
			}
			Block block2 = WorldMap.RelaxedBlockAccess.GetBlock(pos2, 2);
			if (block2.BlockId == 0)
			{
				continue;
			}
			EnumHandling handling = EnumHandling.PassThrough;
			BlockBehavior[] blockBehaviors = block2.BlockBehaviors;
			for (int j = 0; j < blockBehaviors.Length; j++)
			{
				blockBehaviors[j].OnNeighbourBlockChange(this, pos2, pos, ref handling);
				if (handling == EnumHandling.PreventSubsequent)
				{
					break;
				}
			}
		}
	}

	public void SpawnParticles(float quantity, int color, Vec3d minPos, Vec3d maxPos, Vec3f minVelocity, Vec3f maxVelocity, float lifeLength, float gravityEffect, float scale = 1f, EnumParticleModel model = EnumParticleModel.Quad, IPlayer dualCallByPlayer = null)
	{
		particleManager.SpawnParticles(quantity, color, minPos, maxPos, minVelocity, maxVelocity, lifeLength, gravityEffect, scale, model);
	}

	public void SpawnParticles(IParticlePropertiesProvider particlePropertiesProvider, IPlayer dualCallByPlayer = null)
	{
		particleManager.SpawnParticles(particlePropertiesProvider);
	}

	public void SpawnCubeParticles(Vec3d pos, ItemStack itemstack, float radius, int quantity, float scale = 1f, IPlayer dualCallByPlayer = null, Vec3f velocity = null)
	{
		particleManager.SpawnParticles(new StackCubeParticles(pos, itemstack, radius, quantity, scale, velocity));
	}

	public void SpawnCubeParticles(BlockPos blockpos, Vec3d pos, float radius, int quantity, float scale = 1f, IPlayer dualCallByPlayer = null, Vec3f velocity = null)
	{
		particleManager.SpawnParticles(new BlockCubeParticles(this, blockpos, pos, radius, quantity, scale, velocity));
	}

	public bool PlayerHasPrivilege(int clientid, string privilege)
	{
		return true;
	}

	public void ShowChatMessage(string message)
	{
		eventManager?.TriggerNewServerChatLine(GlobalConstants.CurrentChatGroup, message, EnumChatType.Notification, null);
	}

	public void SendMessageToClient(string message)
	{
		eventManager?.TriggerNewModChatLine(GlobalConstants.CurrentChatGroup, message, EnumChatType.OwnMessage, null);
	}

	public void SendArbitraryPacket(byte[] data)
	{
		SendPacket(data);
	}

	public void SendBlockEntityPacket(int x, int y, int z, int packetId, byte[] data = null)
	{
		Packet_BlockEntityPacket packet_BlockEntityPacket = new Packet_BlockEntityPacket
		{
			Packetid = packetId,
			X = x,
			Y = y,
			Z = z
		};
		packet_BlockEntityPacket.SetData(data);
		SendPacketClient(new Packet_Client
		{
			Id = 22,
			BlockEntityPacket = packet_BlockEntityPacket
		});
	}

	public void SendEntityPacket(long entityId, int packetId, byte[] data = null)
	{
		Packet_EntityPacket packet_EntityPacket = new Packet_EntityPacket
		{
			Packetid = packetId,
			EntityId = entityId
		};
		packet_EntityPacket.SetData(data);
		SendPacketClient(new Packet_Client
		{
			Id = 31,
			EntityPacket = packet_EntityPacket
		});
	}

	public IPlayer NearestPlayer(double x, double y, double z)
	{
		IPlayer result = null;
		float num = -1f;
		foreach (ClientPlayer value in PlayersByUid_Threadsafe.Values)
		{
			if (value.Entity != null)
			{
				float num2 = value.Entity.Pos.SquareDistanceTo(x, y, z);
				if (num == -1f || num2 < num)
				{
					num = num2;
					result = value;
				}
			}
		}
		return result;
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

	public ColorMapData GetColorMapData(Block block, int posX, int posY, int posZ)
	{
		return WorldMap.getColorMapData(block, posX, posY, posZ);
	}

	public bool LoadEntity(Entity entity, long fromChunkIndex3d)
	{
		throw new InvalidOperationException("Cannot use LoadEntity on the Client side");
	}

	public AssetLocation ResolveSoundPath(AssetLocation location)
	{
		if (SoundConfig == null)
		{
			throw new Exception("soundconfig.json not loaded. Is it missing or are you trying to load sounds before the client has received the level finalize event? Trying to play: " + location);
		}
		SoundConfig.Soundsets.TryGetValue(location, out var value);
		if (value != null && value.Length != 0)
		{
			if (!SoundIteration.TryGetValue(location, out var value2) || Rand.NextDouble() < 0.35)
			{
				value2 = Rand.Next(value.Length);
				SoundIteration[location] = value2;
			}
			SoundIteration[location] = (SoundIteration[location] + 1) % value.Length;
			return value[value2];
		}
		if (location.EndsWithWildCard)
		{
			AssetCategory category = location.Category;
			if (category == null)
			{
				throw new ArgumentNullException(string.Concat(location, " is missing an asset category - probably it should have 'sounds/' at the front"));
			}
			int num = category.Code.Length + 1;
			string pathBegins = location.Path.Substring(num, location.Path.Length - num - 1);
			List<IAsset> manyInCategory = AssetManager.GetManyInCategory(category.Code, pathBegins, location.Domain, loadAsset: false);
			if (manyInCategory.Count > 0)
			{
				return manyInCategory[rand.Value.Next(manyInCategory.Count)].Location;
			}
		}
		return location;
	}

	public void PlaySound(AssetLocation location, bool randomizePitch = false, float volume = 1f)
	{
		PlaySoundAt(location, 0.0, 0.0, 0.0, null, randomizePitch, 32f, volume);
	}

	public int PlaySound(SoundAttributes sound)
	{
		return PlaySoundAt(sound, 0.0, 0.0, 0.0, 0);
	}

	public int PlaySoundAt(SoundAttributes sound, double x, double y, double z, int dimension, IPlayer? dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		if (sound == null)
		{
			return 0;
		}
		return PlaySoundAtInternal(sound.Location, x, y, z, sound.Volume.nextFloat(volumeMultiplier), sound.Pitch.nextFloat(), sound.Range, sound.Type);
	}

	public int PlaySoundAt(SoundAttributes sound, BlockPos pos, double yOffsetFromCenter, IPlayer? dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		return PlaySoundAt(sound, (double)pos.X + 0.5, (double)pos.InternalY + 0.5 + yOffsetFromCenter, (double)pos.Z + 0.5, pos.dimension, dualCallByPlayer, volumeMultiplier);
	}

	public int PlaySoundAt(SoundAttributes sound, Entity atEntity, IPlayer? dualCallByPlayer = null, float volumeMultiplier = 1f)
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

	public int PlaySoundAt(SoundAttributes sound, IPlayer atPlayer, IPlayer? dualCallByPlayer = null, float volumeMultiplier = 1f)
	{
		if (atPlayer == null)
		{
			atPlayer = Player;
		}
		return PlaySoundAt(sound, atPlayer.Entity, dualCallByPlayer, volumeMultiplier);
	}

	public int PlaySoundFor(SoundAttributes sound, IPlayer? forPlayer, float volumeMultiplier = 1f)
	{
		if (forPlayer == null)
		{
			forPlayer = Player;
		}
		return PlaySoundAt(sound, forPlayer.Entity, null, volumeMultiplier);
	}

	public void PlaySoundAt(AssetLocation? location, IPlayer? atPlayer, IPlayer? ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		if (atPlayer == null)
		{
			atPlayer = Player;
		}
		PlaySoundAt(location, atPlayer.Entity.Pos.X, atPlayer.Entity.Pos.InternalY, atPlayer.Entity.Pos.Z, volume, randomizePitch, range);
	}

	public void PlaySoundAt(AssetLocation? location, Entity atEntity, IPlayer? dualCallByPlayer = null, float pitch = 1f, float range = 32f, float volume = 1f)
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
		PlaySoundAtInternal(location, atEntity.Pos.X, atEntity.Pos.InternalY + (double)num, atEntity.Pos.Z, volume, pitch, range);
	}

	public void PlaySoundAt(AssetLocation? location, double posx, double posy, double posz, IPlayer? dualCallByPlayer = null, float pitch = 1f, float range = 32f, float volume = 1f)
	{
		PlaySoundAtInternal(location, posx, posy, posz, volume, pitch, range);
	}

	public void PlaySoundAt(AssetLocation? location, double posx, double posy, double posz, IPlayer? dualCallByPlayer, EnumSoundType soundType, float pitch = 1f, float range = 32f, float volume = 1f)
	{
		PlaySoundAtInternal(location, posx, posy, posz, volume, pitch, range, soundType);
	}

	public void PlaySoundAt(AssetLocation? location, Entity atEntity, IPlayer? ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
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
		PlaySoundAt(location, atEntity.Pos.X, atEntity.Pos.InternalY + (double)num, atEntity.Pos.Z, ignorePlayerUid, randomizePitch, range, volume);
	}

	public void PlaySoundAt(AssetLocation? location, double x, double y, double z, IPlayer? ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(location, x, y, z, volume, randomizePitch, range);
	}

	public void PlaySoundAt(AssetLocation? location, BlockPos pos, double yOffsetFromCenter, IPlayer? ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(location, (double)pos.X + 0.5, (double)pos.InternalY + 0.5 + yOffsetFromCenter, (double)pos.Z + 0.5, volume, randomizePitch, range);
	}

	public int PlaySoundAtAndGetDuration(AssetLocation? location, double x, double y, double z, IPlayer? ignorePlayerUid = null, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		return PlaySoundAt(location, x, y, z, volume, randomizePitch, range);
	}

	public void PlaySoundFor(AssetLocation? location, IPlayer? atPlayer, float pitch, float range = 32f, float volume = 1f)
	{
		if (atPlayer == null)
		{
			atPlayer = Player;
		}
		PlaySoundAtInternal(location, atPlayer.Entity.Pos.X, atPlayer.Entity.Pos.InternalY, atPlayer.Entity.Pos.Z, volume, pitch, range);
	}

	public void PlaySoundFor(AssetLocation? location, IPlayer? forPlayer, bool randomizePitch = true, float range = 32f, float volume = 1f)
	{
		PlaySoundAt(location, forPlayer, null, randomizePitch, range, volume);
	}

	public int PlaySoundAt(AssetLocation? location, double x, double y, double z, float volume, bool randomizePitch = true, float range = 32f)
	{
		float pitch = (randomizePitch ? RandomPitch() : 1f);
		return PlaySoundAtInternal(location, x, y, z, volume, pitch, range);
	}

	private int PlaySoundAtInternal(AssetLocation? location, double x, double y, double z, float volume, float pitch = 1f, float range = 32f, EnumSoundType soundType = EnumSoundType.Sound)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call PlaySound outside the main thread, it is not thread safe");
		}
		if (ActiveSounds.Count >= 250)
		{
			if (ElapsedMilliseconds - lastSkipTotalMs > 1000)
			{
				Logger.Notification("Play sound {0} skipped because max concurrent sounds of 250 reached ({1} more skipped)", location, cntSkip);
				cntSkip = 0;
				lastSkipTotalMs = ElapsedMilliseconds;
			}
			cntSkip++;
			return 0;
		}
		if (location == null)
		{
			return 0;
		}
		if (ClientSettings.SoundLevel == 0)
		{
			return 0;
		}
		location = ResolveSoundPath(location).Clone().WithPathAppendixOnce(".ogg");
		if (location == null)
		{
			return 0;
		}
		SoundParams sparams;
		if (x == 0.0 && y == 0.0 && z == 0.0)
		{
			sparams = new SoundParams(location)
			{
				RelativePosition = true,
				Range = range,
				SoundType = soundType
			};
		}
		else
		{
			if (player.Entity.Pos.SquareDistanceTo(x, y, z) > range * range)
			{
				return 0;
			}
			sparams = new SoundParams(location)
			{
				Position = new Vec3f((float)x, (float)y, (float)z),
				Range = range,
				SoundType = soundType
			};
		}
		sparams.Pitch = pitch;
		sparams.Volume *= volume;
		ScreenManager.soundAudioData.TryGetValue(location, out var audiodata);
		if (audiodata != null)
		{
			int num = audiodata.Load_Async(new MainThreadAction(this, () => StartPlaying(audiodata, sparams, location), "playSound"));
			if (num >= 0)
			{
				return num;
			}
			return 500;
		}
		Platform.Logger.Warning("Audio File not found: {0}", location);
		return 0;
	}

	private int StartPlaying(AudioData audiodata, SoundParams sparams, AssetLocation location)
	{
		if (EyesInWaterDepth() > 0f)
		{
			sparams.LowPassFilter = 0.06f;
		}
		ILoadedSound loadedSound = Platform.CreateAudio(sparams, audiodata, this);
		if (audiodata.Loaded == 3)
		{
			return StartPlaying(loadedSound, location);
		}
		((AudioMetaData)audiodata).AddOnLoaded(new MainThreadAction(this, () => StartPlaying(loadedSound, location), "soundplaying"));
		return 100;
	}

	public int StartPlaying(ILoadedSound loadedSound, AssetLocation location)
	{
		loadedSound.Start();
		if (EyesInWaterDepth() > 0f && loadedSound.Params.SoundType != EnumSoundType.Music && loadedSound.Params.SoundType != EnumSoundType.MusicGlitchunaffected)
		{
			loadedSound.SetPitchOffset(-0.15f);
		}
		if (EyesInWaterDepth() == 0f && SystemSoundEngine.NowReverbness >= 0.25f && (loadedSound.Params.Position == null || loadedSound.Params.Position == SystemSoundEngine.Zero || SystemSoundEngine.RoomLocation.ContainsOrTouches(loadedSound.Params.Position)))
		{
			loadedSound.SetReverb(SystemSoundEngine.NowReverbness);
		}
		if (ClientSettings.DeveloperMode && loadedSound.Channels > 1 && !loadedSound.Params.RelativePosition)
		{
			Platform.Logger.Warning("Audio File {0} is a stereo sound but loaded as a locational sound, will not attenuate correctly.", location);
		}
		ActiveSounds.Enqueue(loadedSound);
		return (int)(loadedSound.SoundLengthSeconds * 1000f);
	}

	public ILoadedSound? LoadSound(SoundParams sound)
	{
		if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
		{
			throw new InvalidOperationException("Cannot call LoadSound outside the main thread, it is not thread safe");
		}
		ArgumentNullException.ThrowIfNull(sound.Location, "sound.Location");
		sound.Location = ResolveSoundPath(sound.Location.Clone());
		sound.Location.WithPathAppendixOnce(".ogg");
		ScreenManager.soundAudioData.TryGetValue(sound.Location, out var value);
		if (value == null)
		{
			Platform.Logger.Warning("Audio File not found: {0}", sound.Location);
			return null;
		}
		ILoadedSound loadedSound = Platform.CreateAudio(sound, value, this);
		if (EyesInWaterDepth() > 0f)
		{
			loadedSound.SetPitchOffset(-0.2f);
		}
		if (EyesInWaterDepth() == 0f && SystemSoundEngine.NowReverbness >= 0.25f && (loadedSound.Params.Position == null || loadedSound.Params.Position == SystemSoundEngine.Zero || SystemSoundEngine.RoomLocation.ContainsOrTouches(loadedSound.Params.Position)))
		{
			loadedSound.SetReverb(SystemSoundEngine.NowReverbness);
		}
		ActiveSounds.Enqueue(loadedSound);
		return loadedSound;
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

	public void RegisterDialog(params GuiDialog[] dialogs)
	{
		foreach (GuiDialog dialog in dialogs)
		{
			if (!LoadedGuis.Contains(dialog))
			{
				int num = LoadedGuis.FindIndex((GuiDialog d) => d.InputOrder >= dialog.InputOrder);
				if (num < 0)
				{
					num = LoadedGuis.Count;
				}
				int num2 = LoadedGuis.FindIndex(num, (GuiDialog d) => d.InputOrder < dialog.InputOrder);
				if (num2 < 0)
				{
					num2 = LoadedGuis.Count;
				}
				int num3 = LoadedGuis.FindIndex(num, num2 - num, (GuiDialog d) => d.DrawOrder < dialog.DrawOrder);
				if (num3 < 0)
				{
					num3 = num2;
				}
				LoadedGuis.Insert(num3, dialog);
			}
		}
	}

	public void UnregisterDialog(GuiDialog dialog)
	{
		LoadedGuis.Remove(dialog);
		OpenedGuis.Remove(dialog);
	}

	public void HighlightBlocks(IPlayer player, int slotId, List<BlockPos> blocks, List<int> colors, EnumHighlightBlocksMode mode = EnumHighlightBlocksMode.Absolute, EnumHighlightShape shape = EnumHighlightShape.Arbitrary, float scale = 1f)
	{
		eventManager?.TriggerHighlightBlocks(player, slotId, blocks, colors, mode, shape, scale);
	}

	public void HighlightBlocks(IPlayer player, int slotId, List<BlockPos> blocks, EnumHighlightBlocksMode mode = EnumHighlightBlocksMode.Absolute, EnumHighlightShape shape = EnumHighlightShape.Arbitrary)
	{
		eventManager?.TriggerHighlightBlocks(player, slotId, blocks, null, mode, shape);
	}

	public void RemoveEntityRenderer(Entity forEntity)
	{
		EntityRenderers.TryGetValue(forEntity.EntityId, out var value);
		value?.Dispose();
		EntityRenderers.Remove(forEntity.EntityId);
		forEntity.Properties.Client.Renderer = null;
	}
}
