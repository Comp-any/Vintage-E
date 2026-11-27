using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.Server;

public class ServerSystemEntitySpawner : ServerSystem
{
	private bool multithreaded = true;

	private const int yWiggle = 3;

	private const int xzWiggle = 5;

	private const int chunksize = 32;

	private const int chunkRange = 3;

	private const float globalMultiplier = 1f;

	private int GraceTimerDayNotify = -1;

	private double GraceTimeUntilTotalDays = 5.0;

	private List<SpawnArea> spawnAreas = new List<SpawnArea>();

	private List<SpawnState> spawnStates = new List<SpawnState>();

	private HashSet<long> chunkColumnCoordsTmp = new HashSet<long>();

	private CollisionTester collisionTester = new CollisionTester();

	private Dictionary<AssetLocation, EntityProperties> entityTypesByCode = new Dictionary<AssetLocation, EntityProperties>();

	private Random rand = new Random();

	private ushort[] heightMap;

	private ICachingBlockAccessor cachingBlockAccessor;

	private float slowaccum;

	private bool first = true;

	private int SeaLevel;

	private int SkyHeight;

	private BlockPos spawnPosition = new BlockPos();

	private int[] shuffledY = new int[7] { -3, -2, -1, 0, 1, 2, 3 };

	private int surfaceMapTryID;

	private bool errorLoggedThisTick;

	public volatile bool paused;

	private ConcurrentQueue<EntitySpawnerResult> readyToSpawn = new ConcurrentQueue<EntitySpawnerResult>();

	private readonly BlockPos tmpPos = new BlockPos(0);

	public ServerSystemEntitySpawner(ServerMain server)
		: base(server)
	{
		multithreaded = !server.ReducedServerThreads;
	}

	public override void OnBeginModsAndConfigReady()
	{
		List<EntityProperties> entityTypes = server.EntityTypes;
		for (int i = 0; i < entityTypes.Count; i++)
		{
			EntityProperties entityProperties = entityTypes[i];
			entityTypesByCode[entityProperties.Code] = entityProperties;
		}
	}

	public override void OnBeginGameReady(SaveGame savegame)
	{
		base.OnBeginGameReady(savegame);
		cachingBlockAccessor = server.GetCachingBlockAccessor(synchronize: false, relight: false);
		server.RegisterGameTickListener(SpawnReadyMobs, 250);
		if (multithreaded)
		{
			Thread thread = TyronThreadPool.CreateDedicatedThread(new SpawnerOffthread(this).Start, "physicsManagerHelper");
			thread.IsBackground = true;
			thread.Priority = Thread.CurrentThread.Priority;
			server.Serverthreads.Add(thread);
		}
		if (savegame.IsNewWorld || !savegame.ModData.ContainsKey("graceTimeUntilTotalDays"))
		{
			string s = savegame.WorldConfiguration.GetString("graceTimer", "5");
			GraceTimeUntilTotalDays = 5.0;
			double.TryParse(s, out GraceTimeUntilTotalDays);
			if (!savegame.IsNewWorld && !savegame.ModData.ContainsKey("graceTimeUntilTotalDays"))
			{
				int num = Math.Max(1, savegame.WorldConfiguration.GetAsInt("daysPerMonth", 12));
				double num2 = 1f / 3f + (float)(num * 4);
				GraceTimeUntilTotalDays += num2;
			}
			else
			{
				GraceTimeUntilTotalDays += server.Calendar.TotalDays;
			}
			savegame.ModData["graceTimeUntilTotalDays"] = SerializerUtil.Serialize(GraceTimeUntilTotalDays);
		}
		else
		{
			GraceTimeUntilTotalDays = SerializerUtil.Deserialize<double>(savegame.ModData["graceTimeUntilTotalDays"]);
		}
		Dictionary<AssetLocation, Block[]> searchCache = new Dictionary<AssetLocation, Block[]>();
		LCGRandom lCGRandom = new LCGRandom();
		lCGRandom.SetWorldSeed(server.World.Seed);
		foreach (EntityProperties entityType in server.EntityTypes)
		{
			(entityType.Server.SpawnConditions?.Runtime)?.Initialise(lCGRandom, server, entityType.Code.ToShortString(), searchCache);
		}
	}

	public override void Dispose()
	{
		cachingBlockAccessor?.Dispose();
		cachingBlockAccessor = null;
		readyToSpawn.Clear();
	}

	public override void OnServerPause()
	{
		paused = true;
	}

	public override void OnServerResume()
	{
		paused = false;
	}

	private void SpawnReadyMobs(float dt)
	{
		if (server.SaveGameData.EntitySpawning)
		{
			EntitySpawnerResult result;
			while (!readyToSpawn.IsEmpty && readyToSpawn.TryDequeue(out result))
			{
				result.Spawn(server, this);
			}
			PrepareForSpawning(dt);
		}
	}

	internal void PrepareForSpawning(float dt)
	{
		slowaccum += dt;
		int num = (int)server.Calendar.TotalDays;
		double num2 = GraceTimeUntilTotalDays - server.Calendar.TotalDays + 0.25;
		if (GraceTimerDayNotify != num && num2 >= 0.0)
		{
			GraceTimerDayNotify = num;
			if ((int)num2 > 1)
			{
				server.SendMessageToGeneral(Lang.Get("server-xdaysleft", (int)num2), EnumChatType.Notification);
			}
			if ((int)num2 == 1)
			{
				server.SendMessageToGeneral(Lang.Get("server-1dayleft"), EnumChatType.Notification);
			}
			if ((int)num2 == 0)
			{
				server.SendMessageToGeneral(Lang.Get("server-monsterbegins"), EnumChatType.Notification);
			}
		}
		if (first || slowaccum > 10f)
		{
			LoadViableSpawnAreas();
			slowaccum = 0f;
			first = false;
		}
		if (!multithreaded)
		{
			FindMobSpawnPositions_offthread(dt);
		}
	}

	internal void FindMobSpawnPositions_offthread(float dt)
	{
		if (server.Clients.IsEmpty)
		{
			return;
		}
		ReloadSpawnStates_offthread();
		cachingBlockAccessor.Begin();
		SeaLevel = server.SeaLevel;
		SkyHeight = server.WorldMap.MapSizeY - SeaLevel;
		errorLoggedThisTick = false;
		List<SpawnState> list = spawnStates;
		List<SpawnArea> list2 = spawnAreas;
		for (int i = 0; i < list2.Count; i++)
		{
			SpawnArea spawnArea = list2[i];
			for (int j = 0; j < spawnArea.ChunkColumnCoords.Length; j++)
			{
				long num = spawnArea.ChunkColumnCoords[j];
				int baseX = (int)num;
				int baseZ = (int)(num >> 32);
				TrySpawnSomethingAt_offthread(baseX, spawnArea.chunkY, baseZ, spawnArea.spawnCounts, list);
			}
		}
	}

	private void TrySpawnSomethingAt_offthread(int baseX, int baseY, int baseZ, Dictionary<AssetLocation, int> spawnCounts, List<SpawnState> spawnStates)
	{
		ServerWorldMap worldMap = server.WorldMap;
		heightMap = worldMap.GetMapChunk(baseX, baseZ)?.WorldGenTerrainHeightMap;
		if (heightMap == null)
		{
			return;
		}
		IWorldChunk[] array = new IWorldChunk[worldMap.ChunkMapSizeY];
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			IWorldChunk worldChunk = (array[i] = worldMap.GetChunk(baseX, i, baseZ));
			if (worldChunk == null)
			{
				flag = true;
				continue;
			}
			worldChunk.Unpack_ReadOnly();
			worldChunk.AcquireBlockReadLock();
		}
		try
		{
			if (!flag)
			{
				TrySpawnSomethingAt_offthrad(baseX, baseY, baseZ, spawnCounts, array, spawnStates);
			}
		}
		catch (Exception e)
		{
			if (!errorLoggedThisTick)
			{
				errorLoggedThisTick = true;
				server.World.Logger.Warning("Error when testing to spawn entities at " + baseX * 32 + "," + baseY * 32 + "," + baseZ * 32);
				server.World.Logger.Error(e);
			}
		}
		finally
		{
			for (int j = 0; j < array.Length; j++)
			{
				array[j]?.ReleaseBlockReadLock();
			}
		}
	}

	private void TrySpawnSomethingAt_offthrad(int baseX, int baseY, int baseZ, Dictionary<AssetLocation, int> spawnCounts, IWorldChunk[] chunkCol, List<SpawnState> spawnStates)
	{
		BlockPos blockPos = spawnPosition;
		int mapSizeY = server.WorldMap.MapSizeY;
		List<SpawnOppurtunity> list = new List<SpawnOppurtunity>();
		int num = surfaceMapTryID++;
		int seaLevel = SeaLevel;
		int skyHeight = SkyHeight;
		ushort[] array = heightMap;
		Random random = rand;
		int[] array2 = shuffledY;
		array2.Shuffle(random);
		for (int i = 0; i < array2.Length; i++)
		{
			int num2 = (baseY + array2[i]) * 32 + random.Next(32);
			if (num2 <= -3 || num2 >= mapSizeY + 3)
			{
				continue;
			}
			int x;
			int x2;
			blockPos.Set(x = baseX * 32 + random.Next(32), num2, x2 = baseZ * 32 + random.Next(32));
			foreach (SpawnState spawnState in spawnStates)
			{
				RuntimeSpawnConditions runtime = spawnState.ForType.Server.SpawnConditions.Runtime;
				if (spawnState.SpawnableAmountGlobal <= 0)
				{
					continue;
				}
				spawnCounts.TryGetValue(spawnState.ForType.Code, out var value);
				if (value > spawnState.SpawnCapScaledPerPlayer)
				{
					continue;
				}
				if (!runtime.TryOnlySurface)
				{
					double num3 = num2 + 3;
					double num4 = ((num3 > (double)seaLevel) ? (1.0 + (num3 - (double)seaLevel) / (double)skyHeight) : (num3 / (double)seaLevel));
					if ((double)runtime.MinY > num4)
					{
						continue;
					}
					num3 -= 6.0;
					num4 = ((num3 > (double)seaLevel) ? (1.0 + (num3 - (double)seaLevel) / (double)skyHeight) : (num3 / (double)seaLevel));
					if ((double)runtime.MaxY < num4)
					{
						continue;
					}
				}
				int num5 = 10;
				while (spawnState.NextGroupSize <= 0 && num5-- > 0)
				{
					float num6 = runtime.HerdSize.nextFloat();
					spawnState.NextGroupSize = (int)num6 + (((double)(num6 - (float)(int)num6) > random.NextDouble()) ? 1 : 0);
				}
				if (spawnState.NextGroupSize <= 0)
				{
					spawnState.NextGroupSize = -1;
					continue;
				}
				list.Clear();
				int num7 = spawnState.SelfAndCompanionProps.Length;
				EntityProperties entityProperties = spawnState.SelfAndCompanionProps[0];
				if (!server.EventManager.TriggerTrySpawnGroupNearOffthread(server.blockAccessor, entityProperties, blockPos))
				{
					continue;
				}
				num5 = spawnState.NextGroupSize * 4 + 5;
				for (int j = 0; j < num5 && list.Count < spawnState.NextGroupSize; j++)
				{
					blockPos.X = randomWithinSameChunk(x);
					blockPos.Z = randomWithinSameChunk(x2);
					if (runtime.TryOnlySurface)
					{
						int num8 = blockPos.Z % 32 * 32 + blockPos.X % 32;
						if (spawnState.surfaceMap == null)
						{
							spawnState.surfaceMap = new int[1024];
						}
						if (spawnState.surfaceMap[num8] == num)
						{
							continue;
						}
						spawnState.surfaceMap[num8] = num;
						blockPos.Y = array[num8] + 1;
					}
					else
					{
						blockPos.Y = Math.Max(1, num2 + random.Next(7) - 3);
					}
					if (blockPos.Y < 1 || blockPos.Y >= mapSizeY)
					{
						j++;
						continue;
					}
					double num9 = ((blockPos.Y > seaLevel) ? (1.0 + (double)(blockPos.Y - seaLevel) / (double)skyHeight) : ((double)blockPos.Y / (double)seaLevel));
					if ((double)runtime.MinY > num9 || (double)runtime.MaxY < num9)
					{
						j++;
						continue;
					}
					if (list.Count > 0 && num7 > 1)
					{
						int num10 = 1 + random.Next(num7 - 1);
						entityProperties = spawnState.SelfAndCompanionProps[num10];
					}
					Vec3d vec3d = CanSpawnAt_offthread(entityProperties, blockPos, runtime, chunkCol);
					if (vec3d != null)
					{
						list.Add(new SpawnOppurtunity
						{
							ForType = entityProperties,
							Pos = vec3d
						});
					}
					if (list.Count == 0)
					{
						j++;
					}
				}
				if (list.Count >= spawnState.NextGroupSize)
				{
					EntitySpawnerResult item = new EntitySpawnerResult(new List<SpawnOppurtunity>(list), spawnState);
					readyToSpawn.Enqueue(item);
					spawnState.SpawnableAmountGlobal -= spawnState.NextGroupSize;
					spawnState.NextGroupSize = -1;
				}
			}
		}
	}

	private int randomWithinSameChunk(int x)
	{
		return (x & -32) + (x + rand.Next(11) - 5 + 32) % 32;
	}

	public Vec3d CanSpawnAt_offthread(EntityProperties type, BlockPos spawnPosition, RuntimeSpawnConditions sc, IWorldChunk[] chunkCol)
	{
		ServerWorldMap worldMap = server.WorldMap;
		if (spawnPosition.Y <= 0 || spawnPosition.Y >= worldMap.MapSizeY)
		{
			return null;
		}
		BlockPos blockPos = tmpPos;
		IWorldAccessor world = worldMap.World;
		try
		{
			blockPos.Set(spawnPosition);
			ClimateCondition suitableClimateTemperatureRainfall;
			Block block;
			if (sc.TryOnlySurface)
			{
				suitableClimateTemperatureRainfall = GetSuitableClimateTemperatureRainfall(worldMap, sc);
				if (suitableClimateTemperatureRainfall == null)
				{
					return null;
				}
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos);
				if (!sc.CanSpawnInside(block))
				{
					return null;
				}
				blockPos.Y--;
				if (!chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos, 1).CanCreatureSpawnOn(cachingBlockAccessor, blockPos, type, sc))
				{
					return null;
				}
			}
			else
			{
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos);
				bool flag = false;
				for (int i = 0; i < 5; i++)
				{
					if (--blockPos.Y < 0)
					{
						break;
					}
					Block localBlockAtBlockPos_LockFree = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos);
					if (sc.CanSpawnInside(block) && localBlockAtBlockPos_LockFree.CanCreatureSpawnOn(cachingBlockAccessor, blockPos, type, sc))
					{
						flag = true;
						break;
					}
					spawnPosition.Y--;
					block = localBlockAtBlockPos_LockFree;
				}
				if (!flag)
				{
					return null;
				}
				suitableClimateTemperatureRainfall = GetSuitableClimateTemperatureRainfall(worldMap, sc);
				if (suitableClimateTemperatureRainfall == null)
				{
					return null;
				}
			}
			blockPos.Y++;
			IMapRegion mapRegion = worldMap.GetMapRegion(blockPos);
			worldMap.AddWorldGenForestShrub(suitableClimateTemperatureRainfall, mapRegion, blockPos);
			if (!sc.MatchesForestation(suitableClimateTemperatureRainfall))
			{
				return null;
			}
			double num = 1E-07;
			Cuboidf[] collisionBoxes = block.GetCollisionBoxes(server.BlockAccessor, blockPos);
			if (collisionBoxes != null && collisionBoxes.Length != 0)
			{
				num += (double)(collisionBoxes[0].MaxY % 1f);
			}
			Vec3d vec3d = new Vec3d((double)spawnPosition.X + 0.5, (double)spawnPosition.Y + num, (double)spawnPosition.Z + 0.5);
			Cuboidf entityBoxRel = type.SpawnCollisionBox.OmniNotDownGrowBy(0.1f);
			if (collisionTester.IsColliding(server.BlockAccessor, entityBoxRel, vec3d, alsoCheckTouch: false))
			{
				return null;
			}
			IPlayer player = server.NearestPlayer(vec3d.X, vec3d.Y, vec3d.Z);
			if (player?.Entity != null && !player.Entity.CanSpawnNearby(type, vec3d, sc))
			{
				return null;
			}
			if (sc.HabitatSize > 0 && (type.Habitat == EnumHabitat.Underwater || type.Habitat == EnumHabitat.Sea))
			{
				int habitatSize = sc.HabitatSize;
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos.Set(vec3d).Add(BlockFacing.NORTH, habitatSize));
				if (!sc.CanSpawnInside(block))
				{
					return null;
				}
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos.Set(vec3d).Add(BlockFacing.EAST, habitatSize));
				if (!sc.CanSpawnInside(block))
				{
					return null;
				}
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos.Set(vec3d).Add(BlockFacing.WEST, habitatSize));
				if (!sc.CanSpawnInside(block))
				{
					return null;
				}
				block = chunkCol[blockPos.Y / 32].GetLocalBlockAtBlockPos_LockFree(world, blockPos.Set(vec3d).Add(BlockFacing.SOUTH, habitatSize));
				if (!sc.CanSpawnInside(block))
				{
					return null;
				}
			}
			return vec3d;
		}
		catch (Exception e)
		{
			if (!errorLoggedThisTick)
			{
				errorLoggedThisTick = true;
				server.World.Logger.Warning("Error when testing to spawn entity {0} at position {1}, can report to dev team but otherwise should do no harm.", type.Code.ToShortString(), spawnPosition);
				server.World.Logger.Error(e);
			}
			return null;
		}
	}

	public bool CheckCanSpawnAt(EntityProperties type, RuntimeSpawnConditions sc, BlockPos spawnPosition)
	{
		ServerWorldMap worldMap = server.WorldMap;
		if (spawnPosition.Y <= 0 || spawnPosition.Y >= worldMap.MapSizeY)
		{
			return false;
		}
		IBlockAccessor blockAccessor = worldMap.World.BlockAccessor;
		try
		{
			Block block = blockAccessor.GetBlock(spawnPosition);
			if (!sc.CanSpawnInside(block))
			{
				return false;
			}
			spawnPosition.Y--;
			return blockAccessor.GetBlock(spawnPosition, 1).CanCreatureSpawnOn(blockAccessor, spawnPosition, type, sc);
		}
		catch (Exception e)
		{
			server.World.Logger.Warning("Error when re-testing to spawn entity {0} at position {1}, can report to dev team but otherwise should do no harm.", type.Code.ToShortString(), spawnPosition);
			server.World.Logger.Error(e);
			return false;
		}
	}

	private ClimateCondition GetSuitableClimateTemperatureRainfall(ServerWorldMap worldMap, RuntimeSpawnConditions sc)
	{
		int y = tmpPos.Y;
		tmpPos.Y = (int)((double)server.seaLevel * 1.09);
		ClimateCondition worldGenClimateAt = worldMap.getWorldGenClimateAt(tmpPos, temperatureRainfallOnly: true);
		if (worldGenClimateAt == null)
		{
			tmpPos.Y = y;
			return null;
		}
		if (sc.ClimateValueMode != EnumGetClimateMode.WorldGenValues)
		{
			worldMap.GetClimateAt(tmpPos, worldGenClimateAt, sc.ClimateValueMode, server.Calendar.TotalDays);
		}
		tmpPos.Y = y;
		if (!sc.MatchesClimate(worldGenClimateAt))
		{
			return null;
		}
		return worldGenClimateAt;
	}

	private void LoadViableSpawnAreas()
	{
		List<SpawnArea> list = new List<SpawnArea>();
		HashSet<long> hashSet = chunkColumnCoordsTmp;
		ServerWorldMap worldMap = server.WorldMap;
		foreach (ConnectedClient value2 in server.Clients.Values)
		{
			if (!value2.IsPlayingClient || value2.Entityplayer == null)
			{
				continue;
			}
			EntityPos pos = value2.Entityplayer.Pos;
			if (pos.Dimension != 0)
			{
				continue;
			}
			int num = (int)pos.X / 32;
			int num2 = (int)pos.Y / 32;
			int num3 = (int)pos.Z / 32;
			SpawnArea spawnArea = new SpawnArea();
			spawnArea.chunkY = num2;
			hashSet.Clear();
			for (int i = -3; i <= 3; i++)
			{
				int num4 = num + i;
				for (int j = -3; j <= 3; j++)
				{
					int num5 = num3 + j;
					bool flag = false;
					for (int k = Math.Max(-3, -num2); k <= 3; k++)
					{
						IWorldChunk chunk = worldMap.GetChunk(num4, num2 + k, num5);
						if (chunk == null)
						{
							continue;
						}
						flag = true;
						Entity[] entities = chunk.Entities;
						if (entities == null)
						{
							continue;
						}
						for (int l = 0; l < entities.Length; l++)
						{
							Entity entity = entities[l];
							int value = 0;
							if (entity != null)
							{
								spawnArea.spawnCounts.TryGetValue(entity.Code, out value);
							}
							else if (l >= chunk.EntitiesCount)
							{
								break;
							}
							spawnArea.spawnCounts[entity.Code] = value + 1;
						}
					}
					if (flag)
					{
						hashSet.Add(((long)num5 << 32) + num4);
					}
				}
			}
			if (hashSet.Count > 0)
			{
				spawnArea.ChunkColumnCoords = hashSet.ToArray();
				spawnArea.ChunkColumnCoords.Shuffle(rand);
				list.Add(spawnArea);
			}
		}
		spawnAreas = list;
	}

	internal void ReloadSpawnStates_offthread()
	{
		double num = GraceTimeUntilTotalDays - server.Calendar.TotalDays;
		Dictionary<AssetLocation, int> dictionary = new Dictionary<AssetLocation, int>();
		foreach (Entity value5 in server.LoadedEntities.Values)
		{
			if (!(value5.Code == null))
			{
				string text = value5.Attributes.GetString("originaltype");
				AssetLocation assetLocation = ((text == null) ? value5.Code : new AssetLocation(text));
				dictionary.TryGetValue(assetLocation, out var value);
				dictionary[assetLocation] = value + 1;
				QuantityByGroup quantityByGroup = value5.Properties.Server.SpawnConditions?.Runtime?.MaxQuantityByGroup;
				if (quantityByGroup != null && WildcardUtil.Match(quantityByGroup.Code, assetLocation))
				{
					dictionary.TryGetValue(quantityByGroup.Code, out value);
					dictionary[quantityByGroup.Code] = value + 1;
				}
			}
		}
		List<SpawnState> list = new List<SpawnState>();
		Random random = rand;
		int num2 = server.AllOnlinePlayers.Length;
		foreach (EntityProperties entityType in server.EntityTypes)
		{
			RuntimeSpawnConditions runtimeSpawnConditions = entityType.Server.SpawnConditions?.Runtime;
			if (runtimeSpawnConditions == null || runtimeSpawnConditions.MaxQuantity == 0 || (num > 0.0 && runtimeSpawnConditions.Group == "hostile") || random.NextDouble() >= 1.0 * runtimeSpawnConditions.Chance)
			{
				continue;
			}
			dictionary.TryGetValue(entityType.Code, out var value2);
			float num3 = 1f + Math.Max(0f, (float)(num2 - 1) * server.Config.SpawnCapPlayerScaling * runtimeSpawnConditions.SpawnCapPlayerScaling);
			int num4 = (int)((float)runtimeSpawnConditions.MaxQuantity * num3 - (float)value2);
			if (runtimeSpawnConditions.MaxQuantityByGroup != null)
			{
				dictionary.TryGetValue(runtimeSpawnConditions.MaxQuantityByGroup.Code, out var value3);
				num4 = Math.Min(num4, (int)((float)runtimeSpawnConditions.MaxQuantityByGroup.MaxQuantity * num3) - value3);
			}
			if (num4 <= 0)
			{
				continue;
			}
			bool num5 = runtimeSpawnConditions.Companions != null && runtimeSpawnConditions.Companions.Length != 0;
			List<EntityProperties> list2 = new List<EntityProperties> { entityType };
			if (num5)
			{
				AssetLocation[] companions = runtimeSpawnConditions.Companions;
				for (int i = 0; i < companions.Length; i++)
				{
					if (entityTypesByCode.TryGetValue(companions[i], out var value4))
					{
						list2.Add(value4);
					}
					else if (!runtimeSpawnConditions.doneInitialLoad)
					{
						ServerMain.Logger.Warning("Entity with code {0} has defined a companion spawn {1}, but no such entity type found.", entityType.Code, runtimeSpawnConditions.Companions[i]);
					}
				}
			}
			runtimeSpawnConditions.doneInitialLoad = true;
			list.Add(new SpawnState
			{
				ForType = entityType,
				profilerName = "testspawn " + entityType.Code,
				SpawnableAmountGlobal = num4,
				SpawnCapScaledPerPlayer = (int)((float)runtimeSpawnConditions.MaxQuantity * num3 / (float)num2),
				SelfAndCompanionProps = list2.ToArray()
			});
		}
		spawnStates = list;
	}

	public bool ShouldExit()
	{
		if (!server.stopped)
		{
			return server.exitState.Exit;
		}
		return true;
	}
}
