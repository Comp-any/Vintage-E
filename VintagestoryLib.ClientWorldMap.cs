using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public sealed class ClientWorldMap : WorldMap, IChunkProvider, ILandClaimAPI
{
	private ClientMain game;

	private ClientChunk EmptyChunk;

	internal ChunkIlluminator chunkIlluminator;

	internal ClientChunkDataPool chunkDataPool;

	public int ClientChunkSize;

	public int ServerChunkSize;

	public int MapChunkSize;

	public int regionSize;

	public int MaxViewDistance;

	internal ConcurrentDictionary<long, ClientMapRegion> MapRegions = new ConcurrentDictionary<long, ClientMapRegion>(4, 32);

	internal Dictionary<long, ClientMapChunk> MapChunks = new Dictionary<long, ClientMapChunk>();

	internal object chunksLock = new object();

	internal Dictionary<long, ClientChunk> chunks = new Dictionary<long, ClientChunk>();

	internal Dictionary<int, IMiniDimension> dimensions = new Dictionary<int, IMiniDimension>();

	private Vec3i mapsize = new Vec3i();

	public List<LandClaim> LandClaims = new List<LandClaim>();

	public Dictionary<string, PlayerRole> RolesByCode = new Dictionary<string, PlayerRole>();

	private int prevChunkX = -1;

	private int prevChunkY = -1;

	private int prevChunkZ = -1;

	private IWorldChunk prevChunk;

	private object LerpedClimateMapsLock = new object();

	private LimitedDictionary<long, int[]> LerpedClimateMaps = new LimitedDictionary<long, int[]>(10);

	public IBlockAccessor RelaxedBlockAccess;

	public IBlockAccessor CachingBlockAccess;

	public IBulkBlockAccessor BulkBlockAccess;

	public IBlockAccessor NoRelightBulkBlockAccess;

	public IBlockAccessor BulkMinimalBlockAccess;

	public object LightingTasksLock = new object();

	public Queue<UpdateLightingTask> LightingTasks = new Queue<UpdateLightingTask>();

	private bool lightsGo;

	private bool blockTexturesGo;

	private int regionMapSizeX;

	private int regionMapSizeY;

	private int regionMapSizeZ;

	private int[] placeHolderClimateMap;

	public static int seaLevel = 110;

	public override Vec3i MapSize => mapsize;

	ILogger IChunkProvider.Logger => ScreenManager.Platform.Logger;

	public override ILogger Logger => ScreenManager.Platform.Logger;

	public override int ChunkSize => ClientChunkSize;

	public override int ChunkSizeMask => ClientChunkSize - 1;

	public int MapRegionSizeInChunks => RegionSize / ServerChunkSize;

	public override int MapSizeX => mapsize.X;

	public override int MapSizeY => mapsize.Y;

	public override int MapSizeZ => mapsize.Z;

	internal int MapChunkMapSizeX => mapsize.X / ServerChunkSize;

	internal int MapChunkMapSizeY => mapsize.Y / ServerChunkSize;

	internal int MapChunkMapSizeZ => mapsize.Z / ServerChunkSize;

	public override int RegionMapSizeX => regionMapSizeX;

	public override int RegionMapSizeY => regionMapSizeY;

	public override int RegionMapSizeZ => regionMapSizeZ;

	public override IList<Block> Blocks => game.Blocks;

	public override Dictionary<AssetLocation, Block> BlocksByCode => game.BlocksByCode;

	public override IWorldAccessor World => game;

	public override int RegionSize => regionSize;

	public override List<LandClaim> All => LandClaims;

	public override bool DebugClaimPrivileges => false;

	public ClientWorldMap(ClientMain game)
	{
		this.game = game;
		ClientChunkSize = 32;
		chunkDataPool = new ClientChunkDataPool(ClientChunkSize, game);
		game.RegisterGameTickListener((float dt) =>
		{
			chunkDataPool.SlowDispose();
		}, 1033);
		ClientSettings.Inst.AddWatcher<int>("optimizeRamMode", updateChunkDataPoolTresholds);
		updateChunkDataPoolTresholds(ClientSettings.OptimizeRamMode);
		chunkIlluminator = new ChunkIlluminator(this, new BlockAccessorRelaxed(this, game, synchronize: false, relight: false), ClientChunkSize);
		RelaxedBlockAccess = new BlockAccessorRelaxed(this, game, synchronize: false, relight: true);
		CachingBlockAccess = new BlockAccessorCaching(this, game, synchronize: false, relight: true);
		BulkBlockAccess = new BlockAccessorRelaxedBulkUpdate(this, game, synchronize: false, relight: true, debug: false);
		NoRelightBulkBlockAccess = new BlockAccessorRelaxedBulkUpdate(this, game, synchronize: false, relight: false, debug: false);
		BulkMinimalBlockAccess = new BlockAccessorBulkMinimalUpdate(this, game, synchronize: false, debug: false);
	}

	private void updateChunkDataPoolTresholds(int optimizerammode)
	{
		switch (optimizerammode)
		{
		case 2:
			chunkDataPool.CacheSize = 1500;
			chunkDataPool.SlowDisposeThreshold = 1000;
			break;
		case 1:
			chunkDataPool.CacheSize = 2000;
			chunkDataPool.SlowDisposeThreshold = 1350;
			break;
		default:
			chunkDataPool.CacheSize = 5000;
			chunkDataPool.SlowDisposeThreshold = 3500;
			break;
		}
	}

	private void switchRedAndBlueChannels(int[] pixels)
	{
		for (int i = 0; i < pixels.Length; i++)
		{
			int num = pixels[i];
			int num2 = num & 0xFF;
			int num3 = (num >> 16) & 0xFF;
			pixels[i] = (int)(num & 0xFF00FF00u) | (num2 << 16) | num3;
		}
	}

	public void OnLightLevelsReceived()
	{
		if (blockTexturesGo)
		{
			OnBlocksAndLightLevelsReceived();
		}
		else
		{
			lightsGo = true;
		}
	}

	public void OnBlocksAndLightLevelsReceived()
	{
		EmptyChunk = ClientChunk.CreateNew(chunkDataPool);
		ushort num = (ushort)SunBrightness;
		EmptyChunk.Lighting.FillWithSunlight(num);
		chunkIlluminator.InitForWorld(game.Blocks, num, MapSizeX, MapSizeY, MapSizeZ);
		EmptyChunk.Empty = true;
	}

	public void BlockTexturesLoaded()
	{
		PopulateColorMaps();
		if (lightsGo)
		{
			OnBlocksAndLightLevelsReceived();
		}
		else
		{
			blockTexturesGo = true;
		}
	}

	public int LoadColorMaps()
	{
		int num = 0;
		foreach (KeyValuePair<string, ColorMap> colorMap in game.ColorMaps)
		{
			if (game.disposed)
			{
				return num;
			}
			ColorMap value = colorMap.Value;
			if (value.Texture?.Base == null)
			{
				game.Logger.Warning("Incorrect texture definition for color map entry {0}", game.ColorMaps.IndexOfKey(colorMap.Key));
				value.LoadIntoBlockTextureAtlas = false;
				continue;
			}
			AssetLocationAndSource assetLocationAndSource = new AssetLocationAndSource(value.Texture.Base.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
			assetLocationAndSource.AddToAllAtlasses = true;
			BitmapRef bitmapRef = game.Platform.CreateBitmapFromPng(game.AssetManager.Get(assetLocationAndSource));
			if (game.disposed)
			{
				return num;
			}
			if (value.LoadIntoBlockTextureAtlas)
			{
				value.BlockAtlasTextureSubId = game.BlockAtlasManager.GetOrAddTextureLocation(assetLocationAndSource);
				assetLocationAndSource.loadedAlready = 2;
				value.RectIndex = num + (value.ExtraFlags << 6);
				num++;
			}
			if (game.disposed)
			{
				return num;
			}
			value.Pixels = bitmapRef.Pixels;
			value.OuterSize = new Size2i(bitmapRef.Width, bitmapRef.Height);
			switchRedAndBlueChannels(value.Pixels);
		}
		return num;
	}

	public void PopulateColorMaps()
	{
		float[] array = (game.shUniforms.ColorMapRects4 = new float[160]);
		int num = 0;
		foreach (KeyValuePair<string, ColorMap> colorMap in game.ColorMaps)
		{
			ColorMap value = colorMap.Value;
			if (value.LoadIntoBlockTextureAtlas)
			{
				float num2 = (float)value.Padding / (float)game.BlockAtlasManager.Size.Width;
				float num3 = (float)value.Padding / (float)game.BlockAtlasManager.Size.Height;
				TextureAtlasPosition textureAtlasPosition = game.BlockAtlasManager.Positions[value.BlockAtlasTextureSubId];
				array[num++] = textureAtlasPosition.x1 + num2;
				array[num++] = textureAtlasPosition.y1 + num3;
				array[num++] = textureAtlasPosition.x2 - textureAtlasPosition.x1 - 2f * num2;
				array[num++] = textureAtlasPosition.y2 - textureAtlasPosition.y1 - 2f * num3;
			}
		}
	}

	public long MapRegionIndex2DFromClientChunkCoord(int chunkX, int chunkZ)
	{
		chunkX *= ClientChunkSize;
		chunkZ *= ClientChunkSize;
		return MapRegionIndex2D(chunkX / ServerChunkSize / MapRegionSizeInChunks, chunkZ / ServerChunkSize / MapRegionSizeInChunks);
	}

	public int[] TryLoadLerpedClimateMap(int chunkX, int chunkZ)
	{
		long key = MapRegionIndex2DFromClientChunkCoord(chunkX, chunkZ);
		int[] array = null;
		lock (LerpedClimateMapsLock)
		{
			array = LerpedClimateMaps[key];
		}
		if (array == null)
		{
			return null;
		}
		return array;
	}

	public int[] LoadOrCreateLerpedClimateMapOffthread(int chunkX, int chunkZ)
	{
		int[] array = TryLoadLerpedClimateMap(chunkX, chunkZ);
		if (array == null)
		{
			long key = MapRegionIndex2DFromClientChunkCoord(chunkX, chunkZ);
			_ = chunkX * ClientChunkSize / ServerChunkSize / MapRegionSizeInChunks;
			_ = chunkZ * ClientChunkSize / ServerChunkSize / MapRegionSizeInChunks;
			game.WorldMap.MapRegions.TryGetValue(key, out var value);
			if (value == null || value.ClimateMap == null || value.ClimateMap.InnerSize <= 0)
			{
				if (placeHolderClimateMap == null)
				{
					placeHolderClimateMap = GC.AllocateUninitializedArray<int>(RegionSize * RegionSize);
					placeHolderClimateMap.Fill(11842740);
				}
				return placeHolderClimateMap;
			}
			array = GameMath.BiLerpColorMap(value.ClimateMap, RegionSize / value.ClimateMap.InnerSize);
			lock (LerpedClimateMapsLock)
			{
				LerpedClimateMaps[key] = array;
			}
		}
		return array;
	}

	public float[] LoadOceanityCorners(int chunkX, int chunkZ)
	{
		long key = MapRegionIndex2DFromClientChunkCoord(chunkX, chunkZ);
		game.WorldMap.MapRegions.TryGetValue(key, out var value);
		if (value?.OceanMap != null && value.OceanMap.InnerSize > 0)
		{
			IntDataMap2D oceanMap = value.OceanMap;
			float num = (float)(chunkX * 32 % regionSize) / (float)regionSize * (float)oceanMap.InnerSize;
			float num2 = (float)(chunkZ * 32 % regionSize) / (float)regionSize * (float)oceanMap.InnerSize;
			float x = num + 1f;
			float z = num2 + 1f;
			return new float[4]
			{
				oceanMap.GetIntLerpedCorrectly(num, num2),
				oceanMap.GetIntLerpedCorrectly(x, num2),
				oceanMap.GetIntLerpedCorrectly(num, z),
				oceanMap.GetIntLerpedCorrectly(x, z)
			};
		}
		return null;
	}

	public ColorMapData getColorMapData(Block block, int posX, int posY, int posZ)
	{
		int num = GameMath.MurmurHash3Mod(posX, 0, posZ, 3);
		int num2 = GameMath.MurmurHash3Mod(posX, 1, posZ, 3);
		int climate = GetClimate(posX + num, posZ + num2);
		int adjustedTemperature = Climate.GetAdjustedTemperature((climate >> 16) & 0xFF, posY - seaLevel);
		int rainFall = Climate.GetRainFall((climate >> 8) & 0xFF, posY);
		int num3 = 0;
		if (block.SeasonColorMap != null && game.ColorMaps.TryGetValue(block.SeasonColorMap, out var value))
		{
			num3 = value.RectIndex + 1;
		}
		int num4 = 0;
		if (block.ClimateColorMap != null && game.ColorMaps.TryGetValue(block.ClimateColorMap, out var value2))
		{
			num4 = value2.RectIndex + 1;
		}
		return new ColorMapData((byte)num3, (byte)num4, (byte)adjustedTemperature, (byte)rainFall, block.Frostable);
	}

	public int ApplyColorMapOnRgba(string climateColorMap, string seasonColorMap, int color, int posX, int posY, int posZ, bool flipRb)
	{
		ColorMap climateMap = ((climateColorMap == null) ? null : game.ColorMaps[climateColorMap]);
		ColorMap seasonMap = ((seasonColorMap == null) ? null : game.ColorMaps[seasonColorMap]);
		return ApplyColorMapOnRgba(climateMap, seasonMap, color, posX, posY, posZ, flipRb);
	}

	public int ApplyColorMapOnRgba(ColorMap climateMap, ColorMap seasonMap, int color, int posX, int posY, int posZ, bool flipRb)
	{
		int num = GameMath.MurmurHash3Mod(posX, 0, posZ, 3);
		int num2 = GameMath.MurmurHash3Mod(posX, 1, posZ, 3);
		int climate = GetClimate(posX + num, posZ + num2);
		int temp = (climate >> 16) & 0xFF;
		int rainFall = Climate.GetRainFall((climate >> 8) & 0xFF, posY);
		EnumHemisphere hemisphere = game.Calendar.GetHemisphere(new BlockPos(posX, posY, posZ));
		return ApplyColorMapOnRgba(climateMap, seasonMap, color, rainFall, temp, flipRb, (float)GameMath.MurmurHash3Mod(posX, posY, posZ, 100) / 100f, (hemisphere == EnumHemisphere.South) ? 0.5f : 0f, posY - seaLevel);
	}

	public int ApplyColorMapOnRgba(string climateColorMap, string seasonColorMap, int color, int rain, int temp, bool flipRb, float seasonYPixelRel = 0f, float seasonXOffset = 0f)
	{
		ColorMap climateMap = ((climateColorMap == null) ? null : game.ColorMaps[climateColorMap]);
		ColorMap seasonMap = ((seasonColorMap == null) ? null : game.ColorMaps[seasonColorMap]);
		return ApplyColorMapOnRgba(climateMap, seasonMap, color, rain, temp, flipRb, seasonYPixelRel, seasonXOffset, 0);
	}

	public int ApplyColorMapOnRgba(ColorMap climateMap, ColorMap seasonMap, int color, int rain, int temp, bool flipRb, float seasonYPixelRel, float seasonXOffset, int heightAboveSealevel)
	{
		int num = -1;
		if (climateMap != null)
		{
			float num2 = climateMap.OuterSize.Width - 2 * climateMap.Padding;
			float num3 = climateMap.OuterSize.Height - 2 * climateMap.Padding;
			int num4 = (int)GameMath.Clamp((float)Climate.GetAdjustedTemperature(temp, heightAboveSealevel) / 255f * num2, -climateMap.Padding, climateMap.OuterSize.Width - 1);
			int num5 = (int)GameMath.Clamp((float)rain / 255f * num3, -climateMap.Padding, climateMap.OuterSize.Height - 1);
			num = climateMap.Pixels[(num5 + climateMap.Padding) * climateMap.OuterSize.Width + num4 + climateMap.Padding];
			if (flipRb)
			{
				int num6 = num & 0xFF;
				int num7 = (num >> 8) & 0xFF;
				int num8 = (num >> 16) & 0xFF;
				num = (((num >> 24) & 0xFF) << 24) | (num6 << 16) | (num7 << 8) | num8;
			}
		}
		if (seasonMap != null)
		{
			int num9 = (int)(GameMath.Mod(game.Calendar.YearRel + seasonXOffset, 1f) * (float)(seasonMap.OuterSize.Width - seasonMap.Padding));
			int num10 = (int)(seasonYPixelRel * (float)seasonMap.OuterSize.Height);
			int num11 = seasonMap.Pixels[(num10 + seasonMap.Padding) * seasonMap.OuterSize.Width + num9 + seasonMap.Padding];
			if (flipRb)
			{
				int num12 = num11 & 0xFF;
				int num13 = (num11 >> 8) & 0xFF;
				int num14 = (num11 >> 16) & 0xFF;
				num11 = (((num11 >> 24) & 0xFF) << 24) | (num12 << 16) | (num13 << 8) | num14;
			}
			float c2weight = GameMath.Clamp(0.5f - GameMath.Cos((float)temp / 42f) / 2.3f + (float)(Math.Max(0, 128 - temp) / 512) - (float)(Math.Max(0, temp - 130) / 200), 0f, 1f);
			num = ColorUtil.ColorOverlay(num, num11, c2weight);
		}
		return ColorUtil.ColorMultiplyEach(color, num);
	}

	public int GetClimate(int posX, int posZ)
	{
		if (posX < 0 || posZ < 0 || posX >= MapSizeX || posZ >= MapSizeZ)
		{
			return 0;
		}
		int[] array = TryLoadLerpedClimateMap(posX / ClientChunkSize, posZ / ClientChunkSize);
		if (array != null)
		{
			return array[posZ % RegionSize * RegionSize + posX % RegionSize];
		}
		int chunkX = posX / 32;
		int chunkZ = posZ / 32;
		long key = MapRegionIndex2DFromClientChunkCoord(chunkX, chunkZ);
		game.WorldMap.MapRegions.TryGetValue(key, out var value);
		if (value != null && value.ClimateMap != null && value.ClimateMap.InnerSize > 0)
		{
			float x = (float)(posX % RegionSize) / (float)RegionSize * (float)value.ClimateMap.InnerSize;
			float z = (float)(posZ % RegionSize) / (float)RegionSize * (float)value.ClimateMap.InnerSize;
			return value.ClimateMap.GetColorLerpedCorrectly(x, z);
		}
		return 11842740;
	}

	public int GetClimateFast(int[] map, int inRegionX, int inRegionZ)
	{
		return map[inRegionZ * RegionSize + inRegionX];
	}

	internal ClientChunk GetChunkAtBlockPos(int posX, int posY, int posZ)
	{
		int x = posX >> 5;
		int y = posY >> 5;
		int z = posZ >> 5;
		long key = MapUtil.Index3dL(x, y, z, index3dMulX, index3dMulZ);
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(key, out value);
			return value;
		}
	}

	public override IWorldChunk GetChunk(long index3d)
	{
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(index3d, out value);
			return value;
		}
	}

	public override WorldChunk GetChunk(BlockPos pos)
	{
		return GetClientChunk(pos.X / ClientChunkSize, pos.InternalY / ClientChunkSize, pos.Z / ClientChunkSize);
	}

	internal ClientChunk GetClientChunkAtBlockPos(BlockPos pos)
	{
		return GetClientChunk(pos.X / ClientChunkSize, pos.InternalY / ClientChunkSize, pos.Z / ClientChunkSize);
	}

	internal ClientChunk GetClientChunk(int chunkX, int chunkY, int chunkZ)
	{
		long key = MapUtil.Index3dL(chunkX, chunkY, chunkZ, index3dMulX, index3dMulZ);
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(key, out value);
			return value;
		}
	}

	public override IWorldChunk GetChunk(int chunkX, int chunkY, int chunkZ)
	{
		long key = MapUtil.Index3dL(chunkX, chunkY, chunkZ, index3dMulX, index3dMulZ);
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(key, out value);
			return value;
		}
	}

	internal ClientChunk GetClientChunk(long index3d)
	{
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(index3d, out value);
			return value;
		}
	}

	internal void LoadChunkFromPacket(Packet_ServerChunk p)
	{
		int x = p.X;
		int y = p.Y;
		int z = p.Z;
		byte[] blocks = p.Blocks;
		byte[] light = p.Light;
		byte[] lightSat = p.LightSat;
		byte[] liquids = p.Liquids;
		long chunkIndex3d = MapUtil.Index3dL(x, y, z, index3dMulX, index3dMulZ);
		ClientChunk chunk = null;
		try
		{
			chunk = ClientChunk.CreateNewCompressed(chunkDataPool, blocks, light, lightSat, liquids, p.Moddata, p.Compver);
			chunk.Empty = p.Empty > 0;
			chunk.clientmapchunk = GetMapChunk(x, z) as ClientMapChunk;
			chunk.LightPositions = new HashSet<int>();
			for (int i = 0; i < p.LightPositionsCount; i++)
			{
				chunk.LightPositions.Add(p.LightPositions[i]);
			}
		}
		catch (Exception ex)
		{
			game.Logger.Error("Unable to load client chunk at chunk coordinates {0},{1},{2}. Will ignore and replace with empty chunk. Thrown exception: {3}", x, y, z, ex.ToString());
			chunk = ClientChunk.CreateNew(chunkDataPool);
		}
		chunk.PreLoadBlockEntitiesFromPacket(p.BlockEntities, p.BlockEntitiesCount, game);
		if (p.DecorsPos != null && p.DecorsIds != null)
		{
			if (p.DecorsIdsCount < p.DecorsPosCount)
			{
				p.DecorsPosCount = p.DecorsIdsCount;
			}
			chunk.Decors = new Dictionary<int, Block>(p.DecorsPosCount);
			for (int j = 0; j < p.DecorsPosCount; j++)
			{
				chunk.Decors[p.DecorsPos[j]] = game.GetBlock(p.DecorsIds[j]);
			}
		}
		game.EnqueueMainThreadTask(() =>
		{
			bool flag = false;
			lock (chunksLock)
			{
				flag = chunks.ContainsKey(chunkIndex3d);
			}
			if (flag)
			{
				OverloadChunkMT(p, chunk);
			}
			else
			{
				loadChunkMT(p, chunk);
			}
		}, "loadchunk");
	}

	private void loadChunkMT(Packet_ServerChunk p, ClientChunk chunk)
	{
		int x = p.X;
		int y = p.Y;
		int z = p.Z;
		long num = MapUtil.Index3dL(x, y, z, index3dMulX, index3dMulZ);
		lock (chunksLock)
		{
			chunks[num] = chunk;
		}
		chunk.InitBlockEntitiesFromPacket(game);
		chunk.loadedFromServer = true;
		Vec3d vec3d = game.player?.Entity?.Pos?.XYZ;
		bool priority = vec3d != null && vec3d.HorizontalSquareDistanceTo(x * 32, z * 32) < 4096f;
		MarkChunkDirty(x, y, z, priority, false, null, false);
		if (y / 1024 == 1)
		{
			GetOrCreateDimension(x, y, z).ReceiveClientChunk(num, chunk, World);
		}
		else
		{
			SetChunksAroundDirty(x, y, z);
		}
		Vec3i vec3i = new Vec3i(x, y, z);
		game.api.eventapi.TriggerChunkDirty(vec3i, chunk, EnumChunkDirtyReason.NewlyLoaded);
		game.eventManager?.TriggerChunkLoaded(vec3i);
	}

	public IMiniDimension GetOrCreateDimension(int subDimensionId, Vec3d pos)
	{
		if (!dimensions.TryGetValue(subDimensionId, out var value))
		{
			value = new BlockAccessorMovable((BlockAccessorBase)World.BlockAccessor, pos);
			dimensions[subDimensionId] = value;
			value.SetSubDimensionId(subDimensionId);
		}
		return value;
	}

	public IMiniDimension GetOrCreateDimension(int cx, int cy, int cz)
	{
		int subDimensionId = BlockAccessorMovable.CalcSubDimensionId(cx, cz);
		return GetOrCreateDimension(subDimensionId, new Vec3d(cx * 32 % 16384, cy % 1024 * 32, cz * 32 % 16384));
	}

	private void OverloadChunkMT(Packet_ServerChunk p, ClientChunk newchunk)
	{
		int x = p.X;
		int y = p.Y;
		int z = p.Z;
		long num = MapUtil.Index3dL(x, y, z, index3dMulX, index3dMulZ);
		ClientChunk prevchunk;
		lock (chunksLock)
		{
			chunks.TryGetValue(num, out prevchunk);
		}
		if (prevchunk == null)
		{
			loadChunkMT(p, newchunk);
			return;
		}
		prevchunk.loadedFromServer = false;
		if (game.Platform.EllapsedMs - prevchunk.lastTesselationMs < 500)
		{
			game.EnqueueMainThreadTask(() =>
			{
				OverloadChunkMT(p, newchunk);
			}, "overloadchunkrequeue");
			return;
		}
		lock (chunksLock)
		{
			prevchunk.RemoveDataPoolLocations(game.chunkRenderer);
			foreach (KeyValuePair<BlockPos, BlockEntity> blockEntity in prevchunk.BlockEntities)
			{
				blockEntity.Value?.OnBlockUnloaded();
			}
			chunks[num] = newchunk;
			newchunk.Entities = prevchunk.Entities;
			newchunk.EntitiesCount = prevchunk.EntitiesCount;
			newchunk.InitBlockEntitiesFromPacket(game);
			newchunk.loadedFromServer = true;
			newchunk.quantityOverloads++;
		}
		if (!game.IsPaused)
		{
			game.RegisterCallback((float dt) =>
			{
				prevchunk.TryPackAndCommit();
			}, 5000);
		}
		game.eventManager?.TriggerChunkLoaded(new Vec3i(x, y, z));
		if (y / 1024 == 1)
		{
			GetOrCreateDimension(x, y, z).ReceiveClientChunk(num, newchunk, World);
		}
		MarkChunkDirty(x, y, z);
		SetChunksAroundDirty(x, y, z);
	}

	internal void GetNeighbouringChunks(ClientChunk[] neibchunks, int chunkX, int chunkY, int chunkZ)
	{
		lock (chunksLock)
		{
			int num = 0;
			for (int i = -1; i <= 1; i++)
			{
				for (int j = -1; j <= 1; j++)
				{
					for (int k = -1; k <= 1; k++)
					{
						long key = MapUtil.Index3dL(chunkX + i, chunkY + j, chunkZ + k, index3dMulX, index3dMulZ);
						chunks.TryGetValue(key, out var value);
						if (value == null || value.Empty)
						{
							value = EmptyChunk;
						}
						if (!value.ChunkHasData())
						{
							throw new Exception($"GEC: Chunk {chunkX + i} {chunkY + j} {chunkZ + k} has no more block data.");
						}
						neibchunks[num++] = value;
					}
				}
			}
		}
	}

	public void SetChunkDirty(long index3d, bool priority = false, bool relight = false, bool edgeOnly = false)
	{
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(index3d, out value);
		}
		if (value == null)
		{
			return;
		}
		value.shouldSunRelight |= relight;
		if (!relight)
		{
			value.FinishLightDoubleBuffering();
		}
		if (priority)
		{
			lock (game.dirtyChunksPriorityLock)
			{
				if (edgeOnly)
				{
					if (!game.dirtyChunksPriority.Contains(index3d))
					{
						game.dirtyChunksPriority.Enqueue(index3d | long.MinValue);
					}
				}
				else
				{
					game.dirtyChunksPriority.Enqueue(index3d);
				}
				return;
			}
		}
		lock (game.dirtyChunksLock)
		{
			if (edgeOnly)
			{
				if (!game.dirtyChunks.Contains(index3d))
				{
					game.dirtyChunks.Enqueue(index3d | long.MinValue);
				}
			}
			else
			{
				game.dirtyChunks.Enqueue(index3d);
			}
		}
	}

	public override void MarkChunkDirty(int cx, int cy, int cz, bool priority = false, bool sunRelight = false, Action OnRetesselated = null, bool fireEvent = true, bool edgeOnly = false)
	{
		if (!IsValidChunkPos(cx, cy, cz))
		{
			return;
		}
		long num = MapUtil.Index3dL(cx, cy, cz, index3dMulX, index3dMulZ);
		ClientChunk value = null;
		lock (chunksLock)
		{
			chunks.TryGetValue(num, out value);
		}
		if (value == null)
		{
			return;
		}
		int quantityDrawn = value.quantityDrawn;
		if (value.enquedForRedraw)
		{
			if (OnRetesselated != null)
			{
				game.eventManager?.RegisterOnChunkRetesselated(new Vec3i(cx, cy, cz), quantityDrawn, OnRetesselated);
			}
			if (fireEvent)
			{
				game.api.eventapi.TriggerChunkDirty(new Vec3i(cx, cy, cz), value, EnumChunkDirtyReason.MarkedDirty);
			}
			return;
		}
		value.shouldSunRelight = sunRelight;
		if (fireEvent)
		{
			game.api.eventapi.TriggerChunkDirty(new Vec3i(cx, cy, cz), value, EnumChunkDirtyReason.MarkedDirty);
		}
		int num2 = Math.Max(Math.Abs(cx - game.player.Entity.Pos.XInt / 32), Math.Abs(cz - game.player.Entity.Pos.ZInt / 32));
		if ((priority && num2 <= 2) || cy / 1024 == 1)
		{
			lock (game.dirtyChunksPriorityLock)
			{
				if (edgeOnly)
				{
					if (!game.dirtyChunksPriority.Contains(num))
					{
						game.dirtyChunksPriority.Enqueue(num | long.MinValue);
					}
				}
				else
				{
					game.dirtyChunksPriority.Enqueue(num);
					value.enquedForRedraw = true;
				}
				if (OnRetesselated != null)
				{
					game.eventManager?.RegisterOnChunkRetesselated(new Vec3i(cx, cy, cz), value.quantityDrawn, OnRetesselated);
				}
				return;
			}
		}
		lock (game.dirtyChunksLock)
		{
			if (edgeOnly)
			{
				if (!game.dirtyChunks.Contains(num))
				{
					game.dirtyChunks.Enqueue(num | long.MinValue);
				}
			}
			else
			{
				game.dirtyChunks.Enqueue(num);
				value.enquedForRedraw = true;
			}
			if (OnRetesselated != null)
			{
				game.eventManager?.RegisterOnChunkRetesselated(new Vec3i(cx, cy, cz), value.quantityDrawn, OnRetesselated);
			}
		}
	}

	public void SetChunksAroundDirty(int cx, int cy, int cz)
	{
		if (IsValidChunkPos(cx, cy, cz))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx, cy, cz);
		}
		if (IsValidChunkPos(cx - 1, cy, cz))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx - 1, cy, cz);
		}
		if (IsValidChunkPos(cx + 1, cy, cz))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx + 1, cy, cz);
		}
		if (BlockAccessorMovable.ChunkCoordsInSameDimension(cy, cy - 1) && IsValidChunkPos(cx, cy - 1, cz))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx, cy - 1, cz);
		}
		if (BlockAccessorMovable.ChunkCoordsInSameDimension(cy, cy + 1) && IsValidChunkPos(cx, cy + 1, cz))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx, cy + 1, cz);
		}
		if (IsValidChunkPos(cx, cy, cz - 1))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx, cy, cz - 1);
		}
		if (IsValidChunkPos(cx, cy, cz + 1))
		{
			MarkChunkDirty_OnNeighbourChunkLoad(cx, cy, cz + 1);
		}
	}

	private void MarkChunkDirty_OnNeighbourChunkLoad(int cx, int cy, int cz)
	{
		MarkChunkDirty(cx, cy, cz, false, false, null, false, true);
	}

	public bool IsValidChunkPosFast(int chunkX, int chunkY, int chunkZ)
	{
		if (chunkX >= 0 && chunkY >= 0 && chunkZ >= 0 && chunkX < ChunkMapSizeX && chunkY < chunkMapSizeY)
		{
			return chunkZ < ChunkMapSizeZ;
		}
		return false;
	}

	public bool IsChunkRendered(int cx, int cy, int cz)
	{
		if (GetChunk(cx, cy, cz) is ClientChunk clientChunk)
		{
			return clientChunk.quantityDrawn > 0;
		}
		return false;
	}

	public bool IsChunkRendered(EntityPos ePos)
	{
		ClientChunk chunkAtBlockPos = GetChunkAtBlockPos((int)ePos.X, (int)ePos.InternalY, (int)ePos.Z);
		if (chunkAtBlockPos != null)
		{
			return chunkAtBlockPos.quantityDrawn > 0;
		}
		return false;
	}

	public int UncheckedGetBlockId(int x, int y, int z)
	{
		ClientChunk chunkAtBlockPos = GetChunkAtBlockPos(x, y, z);
		if (chunkAtBlockPos != null)
		{
			int index3d = MapUtil.Index3d(x & 0x1F, y & 0x1F, z & 0x1F, 32, 32);
			chunkAtBlockPos.Unpack();
			return chunkAtBlockPos.Data[index3d];
		}
		return 0;
	}

	IWorldChunk IChunkProvider.GetChunk(int chunkX, int chunkY, int chunkZ)
	{
		ClientChunk clientChunk = GetClientChunk(chunkX, chunkY, chunkZ);
		clientChunk?.Unpack();
		return clientChunk;
	}

	IWorldChunk IChunkProvider.GetUnpackedChunkFast(int chunkX, int chunkY, int chunkZ, bool notRecentlyAccessed)
	{
		ClientChunk value = null;
		lock (chunksLock)
		{
			if (chunkX == prevChunkX && chunkY == prevChunkY && chunkZ == prevChunkZ)
			{
				if (notRecentlyAccessed && prevChunk != null)
				{
					prevChunk.Unpack();
				}
				return prevChunk;
			}
			prevChunkX = chunkX;
			prevChunkY = chunkY;
			prevChunkZ = chunkZ;
			long key = MapUtil.Index3dL(chunkX, chunkY, chunkZ, index3dMulX, index3dMulZ);
			chunks.TryGetValue(key, out value);
			prevChunk = value;
		}
		value?.Unpack();
		return value;
	}

	public override IWorldChunk GetChunkNonLocking(int chunkX, int chunkY, int chunkZ)
	{
		long key = MapUtil.Index3dL(chunkX, chunkY, chunkZ, index3dMulX, index3dMulZ);
		chunks.TryGetValue(key, out var value);
		return value;
	}

	public void OnMapSizeReceived(Vec3i mapSize, Vec3i regionMapSize)
	{
		mapsize = new Vec3i(mapSize.X, mapSize.Y, mapSize.Z);
		chunks = new Dictionary<long, ClientChunk>();
		chunkMapSizeY = mapSize.Y / 32;
		index3dMulX = 2097152;
		index3dMulZ = 2097152;
		regionMapSizeX = regionMapSize.X;
		regionMapSizeY = regionMapSize.Y;
		regionMapSizeZ = regionMapSize.Z;
	}

	public override IWorldChunk GetChunkAtPos(int posX, int posY, int posZ)
	{
		return GetChunkAtBlockPos(posX, posY, posZ);
	}

	public override void SpawnBlockEntity(string classname, BlockPos position, ItemStack byItemStack = null)
	{
		ClientChunk clientChunk = (ClientChunk)GetChunk(position);
		if (clientChunk != null)
		{
			Block localBlockAtBlockPos = clientChunk.GetLocalBlockAtBlockPos(game, position);
			BlockEntity blockEntity = ClientMain.ClassRegistry.CreateBlockEntity(classname);
			blockEntity.Pos = position.Copy();
			blockEntity.CreateBehaviors(localBlockAtBlockPos, game);
			blockEntity.Initialize(game.api);
			clientChunk.AddBlockEntity(blockEntity);
			blockEntity.OnBlockPlaced(byItemStack);
			clientChunk.MarkModified();
			MarkBlockEntityDirty(blockEntity.Pos);
		}
	}

	public override void SpawnBlockEntity(BlockEntity be)
	{
		ClientChunk chunkAtBlockPos = GetChunkAtBlockPos(be.Pos.X, be.Pos.Y, be.Pos.Z);
		if (chunkAtBlockPos != null)
		{
			chunkAtBlockPos.AddBlockEntity(be);
			chunkAtBlockPos.MarkModified();
			MarkBlockEntityDirty(be.Pos);
		}
	}

	public override void RemoveBlockEntity(BlockPos position)
	{
		ClientChunk clientChunkAtBlockPos = GetClientChunkAtBlockPos(position);
		if (clientChunkAtBlockPos != null)
		{
			GetBlockEntity(position)?.OnBlockRemoved();
			clientChunkAtBlockPos.RemoveBlockEntity(position);
		}
	}

	public override BlockEntity GetBlockEntity(BlockPos position)
	{
		return GetClientChunkAtBlockPos(position)?.GetLocalBlockEntityAtBlockPos(position);
	}

	public override void SendSetBlock(int blockId, int posX, int posY, int posZ)
	{
	}

	public override void SendExchangeBlock(int blockId, int posX, int posY, int posZ)
	{
	}

	public override void UpdateLighting(int oldblockid, int newblockid, BlockPos pos)
	{
		lock (LightingTasksLock)
		{
			LightingTasks.Enqueue(new UpdateLightingTask
			{
				oldBlockId = oldblockid,
				newBlockId = newblockid,
				pos = pos
			});
		}
		game.eventManager?.TriggerLightingUpdate(oldblockid, newblockid, pos);
	}

	public override void RemoveBlockLight(byte[] oldLightHsV, BlockPos pos)
	{
		lock (LightingTasksLock)
		{
			LightingTasks.Enqueue(new UpdateLightingTask
			{
				removeLightHsv = oldLightHsV,
				pos = pos
			});
		}
		game.eventManager?.TriggerLightingUpdate(0, 0, pos);
	}

	public override void UpdateLightingAfterAbsorptionChange(int oldAbsorption, int newAbsorption, BlockPos pos)
	{
		lock (LightingTasksLock)
		{
			LightingTasks.Enqueue(new UpdateLightingTask
			{
				oldBlockId = 0,
				newBlockId = 0,
				oldAbsorb = (byte)oldAbsorption,
				newAbsorb = (byte)newAbsorption,
				pos = pos,
				absorbUpdate = true
			});
		}
		game.eventManager?.TriggerLightingUpdate(0, 0, pos);
	}

	public override void UpdateLightingBulk(Dictionary<BlockPos, BlockUpdate> blockUpdates)
	{
		game.ShouldTesselateTerrain = false;
		lock (LightingTasksLock)
		{
			foreach (KeyValuePair<BlockPos, BlockUpdate> blockUpdate in blockUpdates)
			{
				int num = ((blockUpdate.Value.NewFluidBlockId >= 0) ? blockUpdate.Value.NewFluidBlockId : blockUpdate.Value.NewSolidBlockId);
				if (num >= 0)
				{
					LightingTasks.Enqueue(new UpdateLightingTask
					{
						oldBlockId = blockUpdate.Value.OldBlockId,
						newBlockId = num,
						pos = blockUpdate.Key
					});
				}
			}
		}
		game.ShouldTesselateTerrain = true;
		game.eventManager?.TriggerLightingUpdate(0, 0, null, blockUpdates);
	}

	public override void SendBlockUpdateBulk(IEnumerable<BlockPos> blockUpdates, bool doRelight)
	{
	}

	public override void SendBlockUpdateBulkMinimal(Dictionary<BlockPos, BlockUpdate> blockUpdates)
	{
	}

	public override void MarkBlockDirty(BlockPos pos, IPlayer skipPlayer = null)
	{
		game.eventManager?.TriggerBlockChanged(game, pos, null);
		MarkChunkDirty(pos.X / ClientChunkSize, pos.InternalY / ClientChunkSize, pos.Z / ClientChunkSize, priority: true);
	}

	public override void MarkBlockModified(BlockPos pos, bool doRelight = true)
	{
		game.eventManager?.TriggerBlockChanged(game, pos, null);
		MarkChunkDirty(pos.X / ClientChunkSize, pos.InternalY / ClientChunkSize, pos.Z / ClientChunkSize, priority: true);
	}

	public override void MarkBlockDirty(BlockPos pos, Action OnRetesselated)
	{
		game.eventManager?.TriggerBlockChanged(game, pos, null);
		MarkChunkDirty(pos.X / ClientChunkSize, pos.InternalY / ClientChunkSize, pos.Z / ClientChunkSize, priority: true, sunRelight: false, OnRetesselated);
	}

	public override void MarkBlockEntityDirty(BlockPos pos)
	{
	}

	public override void TriggerNeighbourBlockUpdate(BlockPos pos)
	{
	}

	public override IMapRegion GetMapRegion(int regionX, int regionZ)
	{
		MapRegions.TryGetValue(MapRegionIndex2D(regionX, regionZ), out var value);
		return value;
	}

	public override IMapChunk GetMapChunk(int chunkX, int chunkZ)
	{
		long key = MapChunkIndex2D(chunkX, chunkZ);
		MapChunks.TryGetValue(key, out var value);
		return value;
	}

	public void UnloadMapRegion(int regionX, int regionZ)
	{
		long key = MapRegionIndex2D(regionX, regionZ);
		if (MapRegions.TryGetValue(key, out var value))
		{
			game.api.eventapi.TriggerMapregionUnloaded(new Vec2i(regionX, regionZ), value);
			MapRegions.Remove(key);
		}
	}

	public override ClimateCondition GetClimateAt(BlockPos pos, EnumGetClimateMode mode = EnumGetClimateMode.WorldGenValues, double totalDays = 0.0)
	{
		int climate = GetClimate(pos.X, pos.Z);
		float posYRel = ((float)pos.Y - (float)seaLevel) / ((float)MapSizeY - (float)seaLevel);
		float scaledAdjustedTemperatureFloatClient = Climate.GetScaledAdjustedTemperatureFloatClient((climate >> 16) & 0xFF, pos.Y - seaLevel);
		float num = Climate.GetRainFall((climate >> 8) & 0xFF, pos.Y);
		float fertility = (float)Climate.GetFertility((int)num, scaledAdjustedTemperatureFloatClient, posYRel) / 255f;
		num /= 255f;
		ClimateCondition climate2 = new ClimateCondition
		{
			Temperature = scaledAdjustedTemperatureFloatClient,
			Rainfall = num,
			WorldgenRainfall = num,
			WorldGenTemperature = scaledAdjustedTemperatureFloatClient,
			Fertility = fertility,
			GeologicActivity = (float)(climate & 0xFF) / 255f
		};
		if (mode == EnumGetClimateMode.NowValues)
		{
			totalDays = game.Calendar.TotalDays;
		}
		game.eventManager?.TriggerOnGetClimate(ref climate2, pos, mode, totalDays);
		return climate2;
	}

	public override ClimateCondition GetClimateAt(BlockPos pos, ClimateCondition baseClimate, EnumGetClimateMode mode, double totalDays)
	{
		baseClimate.Rainfall = baseClimate.WorldgenRainfall;
		baseClimate.Temperature = baseClimate.WorldGenTemperature;
		game.eventManager?.TriggerOnGetClimate(ref baseClimate, pos, mode, totalDays);
		return baseClimate;
	}

	public override ClimateCondition GetClimateAt(BlockPos pos, int climate)
	{
		float scaledAdjustedTemperatureFloatClient = Climate.GetScaledAdjustedTemperatureFloatClient((climate >> 16) & 0xFF, pos.Y - seaLevel);
		float num = Climate.GetRainFall((climate >> 8) & 0xFF, pos.Y);
		float posYRel = ((float)pos.Y - (float)seaLevel) / ((float)MapSizeY - (float)seaLevel);
		ClimateCondition climate2 = new ClimateCondition
		{
			Temperature = scaledAdjustedTemperatureFloatClient,
			Rainfall = num / 255f,
			Fertility = (float)Climate.GetFertility((int)num, scaledAdjustedTemperatureFloatClient, posYRel) / 255f
		};
		game.eventManager?.TriggerOnGetClimate(ref climate2, pos, EnumGetClimateMode.NowValues, game.Calendar.TotalDays);
		return climate2;
	}

	public override Vec3d GetWindSpeedAt(BlockPos pos)
	{
		return GetWindSpeedAt(new Vec3d(pos.X, pos.Y, pos.Z));
	}

	public override Vec3d GetWindSpeedAt(Vec3d pos)
	{
		Vec3d windSpeed = new Vec3d();
		game.eventManager?.TriggerOnGetWindSpeed(pos, ref windSpeed);
		return windSpeed;
	}

	public override void DamageBlock(BlockPos pos, BlockFacing facing, float damage, IPlayer dualCallByPlayer = null)
	{
		Block block = RelaxedBlockAccess.GetBlock(pos);
		if (block.Id != 0)
		{
			game.damagedBlocks.TryGetValue(pos, out var value);
			if (value == null)
			{
				value = new BlockDamage
				{
					Position = pos,
					Block = block,
					Facing = facing,
					RemainingResistance = block.GetResistance(RelaxedBlockAccess, pos),
					LastBreakEllapsedMs = game.ElapsedMilliseconds,
					ByPlayer = game.player
				};
				game.damagedBlocks[pos.Copy()] = value;
			}
			value.RemainingResistance = GameMath.Clamp(value.RemainingResistance - damage, 0f, value.RemainingResistance);
			value.Facing = facing;
			if (value.Block != block)
			{
				value.RemainingResistance = block.GetResistance(RelaxedBlockAccess, pos);
			}
			value.Block = block;
			if (!(value.RemainingResistance <= 0f))
			{
				game.eventManager?.TriggerBlockBreaking(value);
			}
			value.LastBreakEllapsedMs = game.ElapsedMilliseconds;
		}
	}

	public override void MarkDecorsDirty(BlockPos pos)
	{
	}

	internal IPlayerRole GetRole(string roleCode)
	{
		RolesByCode.TryGetValue(roleCode, out var value);
		return value;
	}

	public void Add(LandClaim claim)
	{
		throw new InvalidOperationException("Not available on the client");
	}

	public bool Remove(LandClaim claim)
	{
		throw new InvalidOperationException("Not available on the client");
	}

	internal void Dispose()
	{
		(CachingBlockAccess as ICachingBlockAccessor)?.Dispose();
	}

	public override void SendDecorUpdateBulk(IEnumerable<BlockPos> updatedDecorPositions)
	{
		throw new NotImplementedException();
	}
}
