using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Vintagestory.Common;

public abstract class BlockAccessorBase : IBlockAccessor
{
	protected const int chunksize = 32;

	protected const int chunksizemask = 31;

	internal readonly WorldMap worldmap;

	internal IWorldAccessor worldAccessor;

	public int MapSizeX => worldmap.MapSizeX;

	public int MapSizeY => worldmap.MapSizeY;

	public int MapSizeZ => worldmap.MapSizeZ;

	public int ChunkSize => 32;

	public int RegionSize => worldmap.RegionSize;

	public Vec3i MapSize => worldmap.MapSize;

	public int RegionMapSizeX => worldmap.RegionMapSizeX;

	public int RegionMapSizeY => worldmap.RegionMapSizeY;

	public int RegionMapSizeZ => worldmap.RegionMapSizeZ;

	public bool UpdateSnowAccumMap { get; set; } = true;

	public BlockAccessorBase(WorldMap worldmap, IWorldAccessor worldAccessor)
	{
		this.worldmap = worldmap;
		this.worldAccessor = worldAccessor;
	}

	public virtual int GetBlockId(int posX, int posY, int posZ)
	{
		return GetBlockId(posX, posY, posZ, 0);
	}

	public virtual int GetBlockId(BlockPos pos)
	{
		return GetBlockId(pos.X, pos.InternalY, pos.Z, 0);
	}

	public virtual Block GetBlock(int posX, int posY, int posZ)
	{
		return GetBlockRaw(posX, posY, posZ);
	}

	public virtual Block GetBlock(BlockPos pos)
	{
		return GetBlock(pos, 0);
	}

	public Block GetBlock(BlockPos pos, int layer = 0)
	{
		return worldmap.Blocks[GetBlockId(pos.X, pos.InternalY, pos.Z, layer)];
	}

	public virtual int GetBlockId(BlockPos pos, int layer)
	{
		return GetBlockId(pos.X, pos.InternalY, pos.Z, layer);
	}

	public abstract int GetBlockId(int posX, int posY, int posZ, int layer);

	public abstract Block GetBlockOrNull(int posX, int posY, int posZ, int layer = 4);

	public virtual Block GetBlock(int blockid)
	{
		return worldmap.Blocks[blockid];
	}

	public virtual Block GetBlock(int posX, int posY, int posZ, int layer = 0)
	{
		return GetBlockRaw(posX, posY, posZ, layer);
	}

	public virtual Block GetBlockRaw(int posX, int posY, int posZ, int layer = 0)
	{
		return worldmap.Blocks[GetBlockId(posX, posY, posZ, layer)];
	}

	public virtual Block GetMostSolidBlock(BlockPos pos)
	{
		return GetBlock(pos, 4);
	}

	public virtual Block GetMostSolidBlock(int posX, int posY, int posZ)
	{
		return GetBlockRaw(posX, posY, posZ, 4);
	}

	public void SetBlockInternal(int blockId, BlockPos pos, IWorldChunk chunk, bool synchronize, bool relight, int layer, ItemStack byItemstack = null)
	{
		Block block = worldmap.Blocks[blockId];
		if (layer == 2 || (layer == 0 && block.ForFluidsLayer))
		{
			if (layer == 0)
			{
				SetSolidBlockInternal(0, pos, chunk, synchronize, relight, byItemstack);
			}
			SetFluidBlockInternal(blockId, pos, chunk, synchronize, relight);
		}
		else
		{
			if (layer != 0 && layer != 1)
			{
				throw new ArgumentException("Layer must be solid or fluid");
			}
			SetSolidBlockInternal(blockId, pos, chunk, synchronize, relight, byItemstack);
		}
	}

	protected void SetSolidBlockInternal(int blockId, BlockPos pos, IWorldChunk chunk, bool synchronize, bool relight, ItemStack byItemstack)
	{
		int num = pos.X / 32;
		int num2 = pos.InternalY / 32;
		int num3 = pos.Z / 32;
		int num4 = pos.X & 0x1F;
		int num5 = pos.Y & 0x1F;
		int num6 = pos.Z & 0x1F;
		int index3d = worldmap.ChunkSizedIndex3D(num4, num5, num6);
		int solidBlock = (chunk.Data as ChunkData).GetSolidBlock(index3d);
		chunk.Data[index3d] = blockId;
		if (blockId == 0)
		{
			MarkAdjacentNeighboursDirty(num, num2, num3, num4, num5, num6, pos);
		}
		Block block = worldmap.Blocks[blockId];
		Block block2 = worldmap.Blocks[solidBlock];
		UpdateRainHeightMap(block2, block, pos, chunk.MapChunk);
		if (synchronize)
		{
			worldmap.SendSetBlock(blockId, pos.X, pos.InternalY, pos.Z);
		}
		if (relight)
		{
			worldmap.UpdateLighting(solidBlock, blockId, pos);
		}
		if (blockId != solidBlock)
		{
			chunk.BreakAllDecorFast(worldAccessor, pos, index3d);
			block2.OnBlockRemoved(worldmap.World, pos);
			block.OnBlockPlaced(worldmap.World, pos, byItemstack);
			if (worldAccessor.GetBlock(blockId).DisplacesLiquids(this, pos))
			{
				chunk.Data.SetFluid(index3d, 0);
			}
			else
			{
				int fluid = chunk.Data.GetFluid(index3d);
				if (fluid != 0)
				{
					worldAccessor.GetBlock(fluid).OnNeighbourBlockChange(worldAccessor, pos, pos);
				}
			}
		}
		worldmap.MarkChunkDirty(num, num2, num3, priority: true);
		if (blockId != 0)
		{
			chunk.Empty = false;
			MarkAdjacentNeighboursDirty(num, num2, num3, num4, num5, num6, pos);
		}
	}

	protected void SetFluidBlockInternal(int fluidBlockid, BlockPos pos, IWorldChunk chunk, bool synchronize, bool relight)
	{
		int num = pos.X / 32;
		int num2 = pos.InternalY / 32;
		int num3 = pos.Z / 32;
		int num4 = pos.X & 0x1F;
		int num5 = pos.Y & 0x1F;
		int num6 = pos.Z & 0x1F;
		int index3d = worldmap.ChunkSizedIndex3D(num4, num5, num6);
		int fluid = chunk.Data.GetFluid(index3d);
		if (fluidBlockid != fluid)
		{
			chunk.Data.SetFluid(index3d, fluidBlockid);
			if (fluidBlockid != 0)
			{
				chunk.Empty = false;
				worldmap.MarkChunkDirty(num, num2, num3, priority: true);
			}
			if (worldmap.Blocks[(chunk.Data as ChunkData).GetSolidBlock(index3d)].RainPermeable)
			{
				UpdateRainHeightMap(worldmap.Blocks[fluid], worldmap.Blocks[fluidBlockid], pos, chunk.MapChunk);
			}
			MarkAdjacentNeighboursDirty(num, num2, num3, num4, num5, num6, pos);
			if (fluidBlockid == 0)
			{
				worldmap.MarkChunkDirty(num, num2, num3, priority: true);
			}
			if (synchronize)
			{
				worldmap.SendSetBlock(-fluidBlockid - 1, pos.X, pos.InternalY, pos.Z);
			}
			if (fluidBlockid != fluid)
			{
				worldmap.Blocks[fluidBlockid].OnBlockPlaced(worldmap.World, pos);
			}
		}
	}

	public void WalkStructures(BlockPos minpos, BlockPos maxpos, Action<GeneratedStructure> onStructure)
	{
		int max = worldmap.MapSizeX / worldmap.RegionSize;
		int max2 = worldmap.MapSizeZ / worldmap.RegionSize;
		Cuboidi with = new Cuboidi(minpos, maxpos);
		int num = 256;
		int num2 = GameMath.Clamp((minpos.X - num) / worldmap.RegionSize, 0, max);
		int num3 = GameMath.Clamp((minpos.Z - num) / worldmap.RegionSize, 0, max2);
		int num4 = GameMath.Clamp((maxpos.X + num) / worldmap.RegionSize, 0, max);
		int num5 = GameMath.Clamp((maxpos.Z + num) / worldmap.RegionSize, 0, max2);
		for (int i = num2; i <= num4; i++)
		{
			for (int j = num3; j <= num5; j++)
			{
				IMapRegion mapRegion = worldmap.GetMapRegion(i, j);
				if (mapRegion == null)
				{
					continue;
				}
				foreach (GeneratedStructure generatedStructure in mapRegion.GeneratedStructures)
				{
					if (generatedStructure.Location.IntersectsOrTouches(with))
					{
						onStructure(generatedStructure);
					}
				}
			}
		}
	}

	public void WalkStructures(BlockPos pos, Action<GeneratedStructure> onStructure)
	{
		int max = worldmap.MapSizeX / worldmap.RegionSize;
		int max2 = worldmap.MapSizeZ / worldmap.RegionSize;
		int num = 256;
		int num2 = GameMath.Clamp((pos.X - num) / worldmap.RegionSize, 0, max);
		int num3 = GameMath.Clamp((pos.Z - num) / worldmap.RegionSize, 0, max2);
		int num4 = GameMath.Clamp((pos.X + num) / worldmap.RegionSize, 0, max);
		int num5 = GameMath.Clamp((pos.Z + num) / worldmap.RegionSize, 0, max2);
		for (int i = num2; i <= num4; i++)
		{
			for (int j = num3; j <= num5; j++)
			{
				IMapRegion mapRegion = worldmap.GetMapRegion(i, j);
				if (mapRegion == null)
				{
					continue;
				}
				foreach (GeneratedStructure generatedStructure in mapRegion.GeneratedStructures)
				{
					if (generatedStructure.Location.Contains(pos))
					{
						onStructure(generatedStructure);
					}
				}
			}
		}
	}

	public void WalkBlocks(BlockPos minPos, BlockPos maxPos, Action<Block, int, int, int> onBlock, bool centerOrder = false)
	{
		int mapSizeX = worldmap.MapSizeX;
		int num = GameMath.Clamp(Math.Min(minPos.X, maxPos.X), 0, mapSizeX);
		int num2 = GameMath.Clamp(Math.Max(minPos.X, maxPos.X), 0, mapSizeX);
		mapSizeX = worldmap.MapSizeY;
		int num3 = GameMath.Clamp(Math.Min(minPos.Y, maxPos.Y), 0, mapSizeX);
		int num4 = GameMath.Clamp(Math.Max(minPos.Y, maxPos.Y), 0, mapSizeX);
		mapSizeX = worldmap.MapSizeZ;
		int num5 = GameMath.Clamp(Math.Min(minPos.Z, maxPos.Z), 0, mapSizeX);
		int num6 = GameMath.Clamp(Math.Max(minPos.Z, maxPos.Z), 0, mapSizeX);
		int num7 = num / 32;
		int num8 = num3 / 32;
		int num9 = num5 / 32;
		int num10 = num2 / 32;
		int num11 = num4 / 32;
		int num12 = num6 / 32;
		int num13 = minPos.dimension * 1024;
		ChunkData[] array = LoadChunksToCache(num7, num8 + num13, num9, num10, num11 + num13, num12, null);
		int num14 = num10 - num7 + 1;
		int num15 = num12 - num9 + 1;
		if (centerOrder)
		{
			int num16 = num2 - num;
			int num17 = num4 - num3;
			int num18 = num6 - num5;
			int num19 = num16 / 2;
			int num20 = num17 / 2;
			int num21 = num18 / 2;
			for (int i = 0; i <= num16; i++)
			{
				int num22 = i & 1;
				num22 = num19 - (1 - num22 * 2) * (i + num22) / 2;
				int num23 = num22 + num;
				int num24 = num23 / 32 - num7;
				for (int j = 0; j <= num17; j++)
				{
					int num25 = j & 1;
					num25 = num20 - (1 - num25 * 2) * (j + num25) / 2;
					int num26 = num25 + num3;
					int num27 = num26 % 32 * 32 * 32 + num23 % 32;
					int num28 = (num26 / 32 - num8) * num15 - num9;
					for (int k = 0; k <= num18; k++)
					{
						int num29 = k & 1;
						num29 = num21 - (1 - num29 * 2) * (k + num29) / 2;
						int num30 = num29 + num5;
						ChunkData chunkData = array[(num28 + num30 / 32) * num14 + num24];
						if (chunkData != null)
						{
							int index3d = num27 + num30 % 32 * 32;
							int fluid = chunkData.GetFluid(index3d);
							if (fluid != 0)
							{
								onBlock(worldmap.Blocks[fluid], num23, num26, num30);
							}
							fluid = chunkData.GetSolidBlock(index3d);
							onBlock(worldmap.Blocks[fluid], num23, num26, num30);
						}
					}
				}
			}
			return;
		}
		for (int l = num3; l <= num4; l++)
		{
			int num31 = (l / 32 - num8) * num15 - num9;
			for (int m = num5; m <= num6; m++)
			{
				int num32 = (num31 + m / 32) * num14 - num7;
				int num33 = (l % 32 * 32 + m % 32) * 32;
				for (int n = num; n <= num2; n++)
				{
					ChunkData chunkData2 = array[num32 + n / 32];
					if (chunkData2 != null)
					{
						int index3d2 = num33 + n % 32;
						int fluid2 = chunkData2.GetFluid(index3d2);
						if (fluid2 != 0)
						{
							onBlock(worldmap.Blocks[fluid2], n, l, m);
						}
						fluid2 = chunkData2.GetSolidBlock(index3d2);
						onBlock(worldmap.Blocks[fluid2], n, l, m);
					}
				}
			}
		}
	}

	protected virtual ChunkData[] LoadChunksToCache(int mincx, int mincy, int mincz, int maxcx, int maxcy, int maxcz, Action<int, int, int> onChunkMissing)
	{
		int num = maxcx - mincx + 1;
		int num2 = maxcy - mincy + 1;
		int num3 = maxcz - mincz + 1;
		ChunkData[] array = new ChunkData[num * num2 * num3];
		for (int i = mincy; i <= maxcy; i++)
		{
			int num4 = (i - mincy) * num3 - mincz;
			for (int j = mincz; j <= maxcz; j++)
			{
				int num5 = (num4 + j) * num - mincx;
				for (int k = mincx; k <= maxcx; k++)
				{
					IWorldChunk chunk = worldmap.GetChunk(k, i, j);
					if (chunk == null)
					{
						array[num5 + k] = null;
						onChunkMissing?.Invoke(k, i, j);
					}
					else
					{
						chunk.Unpack();
						array[num5 + k] = chunk.Data as ChunkData;
					}
				}
			}
		}
		return array;
	}

	public void SearchBlocks(BlockPos minPos, BlockPos maxPos, ActionConsumable<Block, BlockPos> onBlock, Action<int, int, int> onChunkMissing = null)
	{
		BlockPos blockPos = new BlockPos(minPos.dimension);
		int num = GameMath.Clamp(Math.Min(minPos.X, maxPos.X), 0, worldmap.MapSizeX);
		int num2 = GameMath.Clamp(Math.Min(minPos.Y, maxPos.Y), 0, worldmap.MapSizeY);
		int num3 = GameMath.Clamp(Math.Min(minPos.Z, maxPos.Z), 0, worldmap.MapSizeZ);
		int num4 = GameMath.Clamp(Math.Max(minPos.X, maxPos.X), 0, worldmap.MapSizeX);
		int num5 = GameMath.Clamp(Math.Max(minPos.Y, maxPos.Y), 0, worldmap.MapSizeY);
		int num6 = GameMath.Clamp(Math.Max(minPos.Z, maxPos.Z), 0, worldmap.MapSizeZ);
		int num7 = num / 32;
		int num8 = num2 / 32;
		int num9 = num3 / 32;
		int num10 = num4 / 32;
		int num11 = num5 / 32;
		int num12 = num6 / 32;
		int num13 = minPos.dimension * 1024;
		ChunkData[] array = LoadChunksToCache(num7, num8 + num13, num9, num10, num11 + num13, num12, onChunkMissing);
		int num14 = num10 - num7 + 1;
		int num15 = num12 - num9 + 1;
		for (int i = num; i <= num4; i++)
		{
			for (int j = num2; j <= num5; j++)
			{
				for (int k = num3; k <= num6; k++)
				{
					blockPos.Set(i, j, k);
					int index3d = (j % 32 * 32 + k % 32) * 32 + i % 32;
					int num16 = i / 32 - num7;
					int num17 = j / 32 - num8;
					int num18 = k / 32 - num9;
					IChunkBlocks chunkBlocks = array[(num17 * num15 + num18) * num14 + num16];
					if (chunkBlocks != null)
					{
						Block t = worldmap.Blocks[chunkBlocks[index3d]];
						if (!onBlock(t, blockPos))
						{
							return;
						}
					}
				}
			}
		}
	}

	public void SearchFluidBlocks(BlockPos minPos, BlockPos maxPos, ActionConsumable<Block, BlockPos> onBlock, Action<int, int, int> onChunkMissing = null)
	{
		BlockPos blockPos = new BlockPos(minPos.dimension);
		int num = GameMath.Clamp(Math.Min(minPos.X, maxPos.X), 0, worldmap.MapSizeX);
		int num2 = GameMath.Clamp(Math.Min(minPos.Y, maxPos.Y), 0, worldmap.MapSizeY);
		int num3 = GameMath.Clamp(Math.Min(minPos.Z, maxPos.Z), 0, worldmap.MapSizeZ);
		int num4 = GameMath.Clamp(Math.Max(minPos.X, maxPos.X), 0, worldmap.MapSizeX);
		int num5 = GameMath.Clamp(Math.Max(minPos.Y, maxPos.Y), 0, worldmap.MapSizeY);
		int num6 = GameMath.Clamp(Math.Max(minPos.Z, maxPos.Z), 0, worldmap.MapSizeZ);
		int num7 = num / 32;
		int num8 = num2 / 32;
		int num9 = num3 / 32;
		int num10 = num4 / 32;
		int num11 = num5 / 32;
		int num12 = num6 / 32;
		int num13 = minPos.dimension * 1024;
		ChunkData[] array = LoadChunksToCache(num7, num8 + num13, num9, num10, num11 + num13, num12, onChunkMissing);
		int num14 = num10 - num7 + 1;
		int num15 = num12 - num9 + 1;
		for (int i = num; i <= num4; i++)
		{
			for (int j = num2; j <= num5; j++)
			{
				for (int k = num3; k <= num6; k++)
				{
					blockPos.Set(i, j, k);
					int index3d = (j % 32 * 32 + k % 32) * 32 + i % 32;
					int num16 = i / 32 - num7;
					int num17 = j / 32 - num8;
					int num18 = k / 32 - num9;
					ChunkData chunkData = array[(num17 * num15 + num18) * num14 + num16];
					if (chunkData != null)
					{
						Block t = worldmap.Blocks[chunkData.GetFluid(index3d)];
						if (!onBlock(t, blockPos))
						{
							return;
						}
					}
				}
			}
		}
	}

	public Block GetBlock(AssetLocation code)
	{
		worldmap.BlocksByCode.TryGetValue(code, out var value);
		return value;
	}

	public void SetBlock(int blockId, BlockPos pos)
	{
		SetBlock(blockId, pos, null);
	}

	public abstract void SetBlock(int blockId, BlockPos pos, ItemStack byItemstack);

	public abstract void SetBlock(int blockId, BlockPos pos, int layer);

	public abstract void ExchangeBlock(int blockId, BlockPos pos);

	public void RedrawNeighbouringChunk(BlockPos pos, BlockFacing side = null)
	{
		int x = pos.X;
		int y = pos.Y;
		int z = pos.Z;
		int lx = x % 32;
		int ly = y % 32;
		int lz = z % 32;
		if (side != null)
		{
			if (side.Axis == EnumAxis.X)
			{
				ly = (lz = 1);
			}
			if (side.Axis == EnumAxis.Y)
			{
				lx = (lz = 1);
			}
			if (side.Axis == EnumAxis.Z)
			{
				lx = (ly = 1);
			}
		}
		MarkAdjacentNeighboursDirty(x / 32, y / 32, z / 32, lx, ly, lz, pos);
	}

	protected void MarkAdjacentNeighboursDirty(int cx, int cy, int cz, int lx, int ly, int lz, BlockPos pos)
	{
		lx = (lx * 2 - 31) / 31;
		ly = (ly * 2 - 31) / 31;
		lz = (lz * 2 - 31) / 31;
		if (lx != 0)
		{
			worldmap.MarkChunkDirty(cx + lx, cy, cz, priority: true, sunRelight: false, null, fireDirtyEvent: true, edgeOnly: true);
		}
		if (ly != 0)
		{
			worldmap.MarkChunkDirty(cx, cy + ly, cz, priority: true, sunRelight: false, null, fireDirtyEvent: true, edgeOnly: true);
		}
		if (lz != 0)
		{
			worldmap.MarkChunkDirty(cx, cy, cz + lz, priority: true, sunRelight: false, null, fireDirtyEvent: true, edgeOnly: true);
		}
	}

	[Obsolete("Please use BlockPos version instead for dimension awareness")]
	public bool IsValidPos(int posX, int posY, int posZ)
	{
		return worldmap.IsValidPos(posX, posY, posZ);
	}

	public bool IsValidPos(BlockPos pos)
	{
		return worldmap.IsValidPos(pos.X, pos.InternalY, pos.Z);
	}

	public virtual void BreakBlock(BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
	{
		GetBlock(pos).OnBlockBroken(worldAccessor, pos, byPlayer, dropQuantityMultiplier);
		worldmap.TriggerNeighbourBlockUpdate(pos);
	}

	public bool IsNotTraversable(BlockPos pos)
	{
		return worldmap.IsMovementRestrictedPos(pos.X, pos.Y, pos.Z, pos.dimension);
	}

	public bool IsNotTraversable(double x, double y, double z)
	{
		return worldmap.IsMovementRestrictedPos(x, y, z, 0);
	}

	public bool IsNotTraversable(double x, double y, double z, int dimension)
	{
		return worldmap.IsMovementRestrictedPos(x, y, z, dimension);
	}

	public virtual IWorldChunk GetChunk(int chunkX, int chunkY, int chunkZ)
	{
		return worldmap.GetChunk(chunkX, chunkY, chunkZ);
	}

	public virtual IWorldChunk GetChunkAtBlockPos(int posX, int posY, int posZ)
	{
		return worldmap.GetChunk(posX / 32, posY / 32, posZ / 32);
	}

	public virtual IWorldChunk GetChunkAtBlockPos(BlockPos pos)
	{
		return worldmap.GetChunk(pos.X / 32, pos.InternalY / 32, pos.Z / 32);
	}

	public virtual void MarkChunkDecorsModified(BlockPos pos)
	{
		if (worldAccessor.Side == EnumAppSide.Client)
		{
			worldAccessor.BlockAccessor.MarkBlockDirty(pos);
		}
		worldmap.MarkDecorsDirty(pos);
	}

	public virtual IMapChunk GetMapChunk(int chunkX, int chunkZ)
	{
		return worldmap.GetMapChunk(chunkX, chunkZ);
	}

	public virtual IMapChunk GetMapChunk(Vec2i chunkPos)
	{
		return worldmap.GetMapChunk(chunkPos.X, chunkPos.Y);
	}

	public virtual IMapRegion GetMapRegion(int regionX, int regionZ)
	{
		return worldmap.GetMapRegion(regionX, regionZ);
	}

	public virtual List<BlockUpdate> Commit()
	{
		return null;
	}

	public virtual void Rollback()
	{
	}

	public virtual void SpawnBlockEntity(string classname, BlockPos position, ItemStack byItemStack = null)
	{
		worldmap.SpawnBlockEntity(classname, position, byItemStack);
	}

	public virtual void SpawnBlockEntity(BlockEntity be)
	{
		worldmap.SpawnBlockEntity(be);
	}

	public virtual void RemoveBlockEntity(BlockPos position)
	{
		worldmap.RemoveBlockEntity(position);
	}

	public virtual BlockEntity GetBlockEntity(BlockPos position)
	{
		return worldmap.GetBlockEntity(position);
	}

	public void MarkBlockEntityDirty(BlockPos pos)
	{
		worldmap.MarkBlockEntityDirty(pos);
	}

	public void MarkBlockDirty(BlockPos pos, IPlayer skipPlayer = null)
	{
		worldmap.MarkBlockDirty(pos, skipPlayer);
	}

	public void MarkBlockModified(BlockPos pos)
	{
		worldmap.MarkBlockModified(pos);
	}

	public void MarkBlockDirty(BlockPos pos, Action OnRetesselated)
	{
		worldmap.MarkBlockDirty(pos, OnRetesselated);
	}

	public void TriggerNeighbourBlockUpdate(BlockPos pos)
	{
		worldmap.TriggerNeighbourBlockUpdate(pos);
	}

	public int GetLightLevel(int posX, int posY, int posZ, EnumLightLevelType type)
	{
		IWorldChunk chunkAtBlockPos = GetChunkAtBlockPos(posX, posY, posZ);
		if (chunkAtBlockPos == null || !worldmap.IsValidPos(posX, posY, posZ))
		{
			return worldAccessor.SunBrightness;
		}
		int num = 32;
		int index = (posY % num * num + posZ % num) * num + posX % num;
		ushort num2 = chunkAtBlockPos.Unpack_AndReadLight(index);
		int num3 = (num2 >> 5) & 0x1F;
		int num4 = num2 & 0x1F;
		switch (type)
		{
		case EnumLightLevelType.OnlySunLight:
			return num4;
		case EnumLightLevelType.OnlyBlockLight:
			return num3;
		case EnumLightLevelType.MaxLight:
			return Math.Max(num4, num3);
		case EnumLightLevelType.MaxTimeOfDayLight:
		{
			float dayLightStrength = worldAccessor.Calendar.GetDayLightStrength(posX, posZ);
			return Math.Max((int)((float)num4 * dayLightStrength), num3);
		}
		case EnumLightLevelType.TimeOfDaySunLight:
		{
			float dayLightStrength = worldAccessor.Calendar.GetDayLightStrength(posX, posZ);
			return (int)((float)num4 * dayLightStrength);
		}
		case EnumLightLevelType.Sunbrightness:
			return (int)(32f * worldAccessor.Calendar.GetDayLightStrength(posX, posZ));
		default:
			return -1;
		}
	}

	public int GetLightLevel(BlockPos pos, EnumLightLevelType type)
	{
		return GetLightLevel(pos.X, pos.InternalY, pos.Z, type);
	}

	public int GetTerrainMapheightAt(BlockPos pos)
	{
		int num = 32;
		IMapChunk mapChunk = GetMapChunk(pos.X / num, pos.Z / num);
		if (mapChunk == null || !worldmap.IsValidPos(pos.X, 0, pos.Z))
		{
			return 0;
		}
		int num2 = pos.Z % num * num + pos.X % num;
		return mapChunk.WorldGenTerrainHeightMap[num2];
	}

	public int GetRainMapHeightAt(int posX, int posZ)
	{
		IMapChunk mapChunk = GetMapChunk(posX / 32, posZ / 32);
		if (mapChunk == null || !worldmap.IsValidPos(posX, 0, posZ))
		{
			return 0;
		}
		int num = posZ % 32 * 32 + posX % 32;
		return mapChunk.RainHeightMap[num];
	}

	public int GetRainMapHeightAt(BlockPos pos)
	{
		IMapChunk mapChunk = GetMapChunk(pos.X / 32, pos.Z / 32);
		if (mapChunk == null || !worldmap.IsValidPos(pos.X, 0, pos.Z))
		{
			return 0;
		}
		int num = pos.Z % 32 * 32 + pos.X % 32;
		return mapChunk.RainHeightMap[num];
	}

	public IMapChunk GetMapChunkAtBlockPos(BlockPos pos)
	{
		IMapChunk mapChunk = GetMapChunk(pos.X / 32, pos.Z / 32);
		if (!worldmap.IsValidPos(pos.X, 0, pos.Z))
		{
			return null;
		}
		return mapChunk;
	}

	public ClimateCondition GetClimateAt(BlockPos pos, EnumGetClimateMode mode = EnumGetClimateMode.NowValues, double totalDays = 0.0)
	{
		if (mode == EnumGetClimateMode.NowValues)
		{
			totalDays = worldAccessor.Calendar.TotalDays;
		}
		return worldmap.GetClimateAt(pos, mode, totalDays);
	}

	public ClimateCondition GetClimateAt(BlockPos pos, ClimateCondition baseClimate, EnumGetClimateMode mode, double totalDays)
	{
		return worldmap.GetClimateAt(pos, baseClimate, mode, totalDays);
	}

	public ClimateCondition GetClimateAt(BlockPos pos, int climate)
	{
		return worldmap.GetClimateAt(pos, climate);
	}

	public Vec3d GetWindSpeedAt(Vec3d pos)
	{
		return worldmap.GetWindSpeedAt(pos);
	}

	public Vec3d GetWindSpeedAt(BlockPos pos)
	{
		return worldmap.GetWindSpeedAt(pos);
	}

	public void DamageBlock(BlockPos pos, BlockFacing facing, float damage)
	{
		worldmap.DamageBlock(pos, facing, damage);
	}

	public void UpdateRainHeightMap(Block oldBlock, Block newBlock, BlockPos pos, IMapChunk mapchunk)
	{
		if (mapchunk == null || pos.InternalY >= 32768)
		{
			return;
		}
		int num = pos.X & 0x1F;
		int num2 = pos.Z & 0x1F;
		bool rainPermeable = oldBlock.RainPermeable;
		bool rainPermeable2 = newBlock.RainPermeable;
		ushort num3 = mapchunk.RainHeightMap[num2 * 32 + num];
		ushort num4 = num3;
		if (!rainPermeable2)
		{
			num4 = (mapchunk.RainHeightMap[num2 * 32 + num] = Math.Max(num3, (ushort)pos.Y));
			if (num3 < pos.Y)
			{
				mapchunk.MarkDirty();
				if (UpdateSnowAccumMap && mapchunk is ServerMapChunk serverMapChunk)
				{
					float snowLevel = newBlock.GetSnowLevel(pos);
					if (serverMapChunk.snowAccumArray != null || snowLevel != 0f)
					{
						mapchunk.SnowAccum[new Vec2iStruct(pos.X, pos.Z).ToInChunkIndex] = snowLevel;
					}
				}
			}
		}
		if ((!rainPermeable & rainPermeable2) && num3 == pos.Y && pos.Y > 0)
		{
			int num5 = pos.Y - 1;
			while (worldmap.Blocks[GetBlockId(pos.X, num5, pos.Z, 3)].RainPermeable && num5 > 0)
			{
				num5--;
			}
			num4 = (mapchunk.RainHeightMap[num2 * 32 + num] = (ushort)num5);
			mapchunk.MarkDirty();
		}
		if (pos.Y > mapchunk.YMax && newBlock.BlockId != 0)
		{
			mapchunk.YMax = (ushort)pos.Y;
			mapchunk.MarkDirty();
		}
		if (UpdateSnowAccumMap && num4 <= num3 && mapchunk is ServerMapChunk { snowAccumArray: not null, snowAccumArray: var snowAccumArray })
		{
			snowAccumArray[new Vec2iStruct(pos.X, pos.Z).ToInChunkIndex] = 0f;
		}
	}

	public Vec4f GetLightRGBs(int posX, int posY, int posZ)
	{
		return worldmap.GetLightRGBSVec4f(posX, posY, posZ);
	}

	public int GetLightRGBsAsInt(int posX, int posY, int posZ)
	{
		return worldmap.GetLightRGBsAsInt(posX, posY, posZ);
	}

	public Vec4f GetLightRGBs(BlockPos pos)
	{
		return worldmap.GetLightRGBSVec4f(pos.X, pos.InternalY, pos.Z);
	}

	public IWorldChunk GetChunk(long chunkIndex3D)
	{
		return worldmap.GetChunk(chunkIndex3D);
	}

	public bool IsSideSolid(int x, int y, int z, BlockFacing facing)
	{
		int blockId = GetBlockId(x, y, z, 2);
		if (blockId == 0 || !worldmap.Blocks[blockId].SideSolid.Any)
		{
			blockId = GetBlockId(x, y, z, 1);
		}
		return worldmap.Blocks[blockId].SideSolid[facing.Index];
	}

	public int GetDistanceToRainFall(BlockPos pos, int horziontalSearchWidth = 4, int verticalSearchWidth = 1)
	{
		if (GetRainMapHeightAt(pos) <= pos.Y)
		{
			return 0;
		}
		BlockPos blockPos = new BlockPos(pos.dimension);
		Queue<Vec3i> queue = new Queue<Vec3i>();
		HashSet<Vec3i> hashSet = new HashSet<Vec3i>();
		queue.Enqueue(new Vec3i(pos.X, pos.Y, pos.Z));
		while (queue.Count > 0)
		{
			Vec3i vec3i = queue.Dequeue();
			for (int i = 0; i < 6; i++)
			{
				BlockFacing blockFacing = BlockFacing.ALLFACES[i];
				Vec3i vec3i2 = new Vec3i(vec3i.X + blockFacing.Normali.X, vec3i.Y + blockFacing.Normali.Y, vec3i.Z + blockFacing.Normali.Z);
				int num = Math.Abs(vec3i2.X - pos.X) + Math.Abs(vec3i2.Z - pos.Z);
				int num2 = Math.Abs(vec3i2.Y - pos.Y);
				if (num >= horziontalSearchWidth || num2 >= verticalSearchWidth || hashSet.Contains(vec3i2))
				{
					continue;
				}
				hashSet.Add(vec3i2);
				blockPos.Set(vec3i2.X, vec3i2.Y, vec3i2.Z);
				Block block = GetBlock(blockPos);
				if (!block.SideSolid[blockFacing.Index] && !block.SideSolid[blockFacing.Opposite.Index] && block.GetRetention(blockPos, blockFacing, EnumRetentionType.Sound) == 0 && block.GetRetention(blockPos, blockFacing.Opposite, EnumRetentionType.Sound) == 0)
				{
					if (GetRainMapHeightAt(blockPos) <= vec3i2.Y)
					{
						return num + num2;
					}
					queue.Enqueue(vec3i2);
				}
			}
		}
		return 99;
	}

	public void MarkAbsorptionChanged(int oldAbsorption, int newAbsorption, BlockPos pos)
	{
		worldmap.UpdateLightingAfterAbsorptionChange(oldAbsorption, newAbsorption, pos);
	}

	public void RemoveBlockLight(byte[] oldLightHsv, BlockPos pos)
	{
		worldmap.RemoveBlockLight(oldLightHsv, pos);
	}

	public virtual bool SetDecor(Block block, BlockPos pos, BlockFacing onFace)
	{
		return SetDecor(block, pos, new DecorBits(onFace));
	}

	public virtual bool SetDecor(Block block, BlockPos pos, int decorIndex)
	{
		IWorldChunk chunkAtBlockPos = GetChunkAtBlockPos(pos);
		if (chunkAtBlockPos == null)
		{
			return false;
		}
		int lX = pos.X & 0x1F;
		int lY = pos.Y & 0x1F;
		int lZ = pos.Z & 0x1F;
		int index3d = worldmap.ChunkSizedIndex3D(lX, lY, lZ);
		if (chunkAtBlockPos.SetDecor(block, index3d, decorIndex))
		{
			MarkChunkDecorsModified(pos);
			chunkAtBlockPos.MarkModified();
			return true;
		}
		return false;
	}

	public Block[] GetDecors(BlockPos position)
	{
		return GetChunkAtBlockPos(position)?.GetDecors(this, position);
	}

	public Dictionary<int, Block> GetSubDecors(BlockPos position)
	{
		return GetChunkAtBlockPos(position)?.GetSubDecors(this, position);
	}

	public Block GetDecor(BlockPos position, int faceAndSubPosition)
	{
		return GetChunkAtBlockPos(position)?.GetDecor(this, position, faceAndSubPosition);
	}

	public virtual bool BreakDecor(BlockPos pos, BlockFacing side = null, int? faceAndSubposition = null)
	{
		IWorldChunk chunkAtBlockPos = GetChunkAtBlockPos(pos);
		if (chunkAtBlockPos != null && chunkAtBlockPos.BreakDecor(worldAccessor, pos, side, faceAndSubposition))
		{
			MarkChunkDecorsModified(pos);
			chunkAtBlockPos.MarkModified();
			return true;
		}
		return false;
	}

	public virtual void SetDecorsBulk(long chunkIndex, Dictionary<int, Block> newDecors)
	{
		GetChunk(chunkIndex)?.SetDecors(newDecors);
		ChunkPos chunkPos = worldmap.ChunkPosFromChunkIndex3D(chunkIndex);
		worldmap.MarkChunkDirty(chunkPos.X, chunkPos.InternalY, chunkPos.Z, priority: true, sunRelight: false, null, fireDirtyEvent: false);
	}

	public T GetBlockEntity<T>(BlockPos position) where T : BlockEntity
	{
		return worldmap.GetBlockEntity(position) as T;
	}

	public IMiniDimension CreateMiniDimension(Vec3d position)
	{
		return new BlockAccessorMovable(this, position);
	}
}
