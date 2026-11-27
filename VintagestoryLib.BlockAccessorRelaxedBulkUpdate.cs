using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common.Database;

namespace Vintagestory.Common;

public class BlockAccessorRelaxedBulkUpdate : BlockAccessorBase, IBulkBlockAccessor, IBlockAccessor
{
	protected bool synchronize;

	protected bool relight;

	protected bool debug;

	protected bool storeOldBlockEntityData;

	public readonly Dictionary<BlockPos, BlockUpdate> StagedBlocks = new Dictionary<BlockPos, BlockUpdate>();

	public readonly Dictionary<BlockPos, BlockUpdate> LightSources = new Dictionary<BlockPos, BlockUpdate>();

	private readonly Queue<BlockBreakTask> _blockBreakTasks = new Queue<BlockBreakTask>();

	protected readonly HashSet<ChunkPosCompact> dirtyChunkPositions = new HashSet<ChunkPosCompact>();

	public bool ReadFromStagedByDefault { get; set; }

	Dictionary<BlockPos, BlockUpdate> IBulkBlockAccessor.StagedBlocks => StagedBlocks;

	public event Action<IBulkBlockAccessor> BeforeCommit;

	public BlockAccessorRelaxedBulkUpdate(WorldMap worldmap, IWorldAccessor worldAccessor, bool synchronize, bool relight, bool debug)
		: base(worldmap, worldAccessor)
	{
		this.synchronize = synchronize;
		this.relight = relight;
		this.debug = debug;
	}

	public override int GetBlockId(int posX, int posY, int posZ, int layer)
	{
		if (ReadFromStagedByDefault && StagedBlocks.TryGetValue(new BlockPos(posX, posY, posZ), out var value))
		{
			switch (layer)
			{
			default:
				if (value.NewSolidBlockId >= 0)
				{
					return value.NewSolidBlockId;
				}
				break;
			case 2:
			case 3:
				if (value.NewFluidBlockId >= 0)
				{
					return value.NewFluidBlockId;
				}
				break;
			case 4:
				return GetMostSolidBlock(posX, posY, posZ).Id;
			}
		}
		return GetNonStagedBlockId(posX, posY, posZ, layer);
	}

	public override int GetBlockId(BlockPos pos, int layer)
	{
		if (ReadFromStagedByDefault && StagedBlocks.TryGetValue(pos, out var value))
		{
			switch (layer)
			{
			default:
				if (value.NewSolidBlockId >= 0)
				{
					return value.NewSolidBlockId;
				}
				break;
			case 2:
			case 3:
				if (value.NewFluidBlockId >= 0)
				{
					return value.NewFluidBlockId;
				}
				break;
			case 4:
				return GetMostSolidBlock(pos).Id;
			}
		}
		return GetNonStagedBlockId(pos.X, pos.InternalY, pos.Z, layer);
	}

	public override Block GetMostSolidBlock(int x, int y, int z)
	{
		if (ReadFromStagedByDefault && StagedBlocks.TryGetValue(new BlockPos(x, y, z), out var value))
		{
			if (value.NewSolidBlockId >= 0)
			{
				return worldmap.Blocks[value.NewSolidBlockId];
			}
			if (value.NewFluidBlockId > 0)
			{
				Block block = worldmap.Blocks[value.NewFluidBlockId];
				if (block.SideSolid.Any)
				{
					return block;
				}
			}
		}
		return base.GetMostSolidBlock(x, y, z);
	}

	protected virtual int GetNonStagedBlockId(int posX, int posY, int posZ, int layer)
	{
		if ((posX | posY | posZ) < 0 || posX >= worldmap.MapSizeX || posZ >= worldmap.MapSizeZ)
		{
			return 0;
		}
		return worldmap.GetChunkAtPos(posX, posY, posZ)?.UnpackAndReadBlock(worldmap.ChunkSizedIndex3D(posX & 0x1F, posY & 0x1F, posZ & 0x1F), layer) ?? 0;
	}

	public override Block GetBlockOrNull(int posX, int posY, int posZ, int layer = 4)
	{
		if ((posX | posY | posZ) < 0 || posX >= worldmap.MapSizeX || posZ >= worldmap.MapSizeZ)
		{
			return null;
		}
		if (ReadFromStagedByDefault && StagedBlocks.TryGetValue(new BlockPos(posX, posY, posZ), out var value) && value.NewSolidBlockId >= 0)
		{
			return worldmap.Blocks[value.NewSolidBlockId];
		}
		IWorldChunk chunkAtPos = worldmap.GetChunkAtPos(posX, posY, posZ);
		if (chunkAtPos != null)
		{
			return worldmap.Blocks[chunkAtPos.UnpackAndReadBlock(worldmap.ChunkSizedIndex3D(posX & 0x1F, posY & 0x1F, posZ & 0x1F), layer)];
		}
		return null;
	}

	public override void SetBlock(int newBlockId, BlockPos pos, ItemStack byItemstack = null)
	{
		if (worldmap.Blocks[newBlockId].ForFluidsLayer)
		{
			SetFluidBlock(newBlockId, pos);
		}
		else if ((pos.X | pos.Y | pos.Z) >= 0 && (pos.dimension != 0 || (pos.X < worldmap.MapSizeX && pos.Y < worldmap.MapSizeY && pos.Z < worldmap.MapSizeZ)))
		{
			if (StagedBlocks.TryGetValue(pos, out var value))
			{
				value.NewSolidBlockId = newBlockId;
				value.ByStack = byItemstack;
				return;
			}
			BlockPos blockPos = pos.Copy();
			StagedBlocks[blockPos] = new BlockUpdate
			{
				NewSolidBlockId = newBlockId,
				ByStack = byItemstack,
				Pos = blockPos
			};
		}
	}

	public override void SetBlock(int blockId, BlockPos pos, int layer)
	{
		switch (layer)
		{
		case 2:
			SetFluidBlock(blockId, pos);
			break;
		case 1:
			SetBlock(blockId, pos);
			break;
		default:
			throw new ArgumentException("Layer must be solid or fluid");
		}
	}

	public void SetFluidBlock(int blockId, BlockPos pos)
	{
		if ((pos.X | pos.Y | pos.Z) >= 0 && (pos.dimension != 0 || (pos.X < worldmap.MapSizeX && pos.Y < worldmap.MapSizeY && pos.Z < worldmap.MapSizeZ)))
		{
			if (StagedBlocks.TryGetValue(pos, out var value))
			{
				value.NewFluidBlockId = blockId;
				return;
			}
			BlockPos blockPos = pos.Copy();
			StagedBlocks[blockPos] = new BlockUpdate
			{
				NewFluidBlockId = blockId,
				Pos = blockPos
			};
		}
	}

	public override bool SetDecor(Block block, BlockPos pos, BlockFacing onFace)
	{
		return SetDecor(block, pos, new DecorBits(onFace));
	}

	public override bool SetDecor(Block block, BlockPos pos, int decorIndex)
	{
		if ((pos.X | pos.Y | pos.Z) < 0 || (pos.dimension == 0 && (pos.X >= worldmap.MapSizeX || pos.Y >= worldmap.MapSizeY || pos.Z >= worldmap.MapSizeZ)))
		{
			return false;
		}
		DecorUpdate item = new DecorUpdate
		{
			faceAndSubposition = decorIndex,
			decorId = block.Id
		};
		if (StagedBlocks.TryGetValue(pos, out var value))
		{
			BlockUpdate blockUpdate = value;
			if (blockUpdate.Decors == null)
			{
				blockUpdate.Decors = new List<DecorUpdate>();
			}
			value.Decors.Add(new DecorUpdate
			{
				faceAndSubposition = decorIndex,
				decorId = block.Id
			});
		}
		else
		{
			BlockPos blockPos = pos.Copy();
			List<DecorUpdate> list = new List<DecorUpdate>();
			list.Add(item);
			StagedBlocks[blockPos] = new BlockUpdate
			{
				Pos = blockPos,
				Decors = list
			};
		}
		return true;
	}

	public override List<BlockUpdate> Commit()
	{
		BeforeCommit?.Invoke(this);
		ReadFromStagedByDefault = false;
		IWorldChunk worldChunk = null;
		int num = -1;
		int num2 = -1;
		int num3 = -1;
		List<BlockUpdate> list = new List<BlockUpdate>(StagedBlocks.Count);
		HashSet<BlockPos> hashSet = new HashSet<BlockPos>();
		List<BlockPos> list2 = new List<BlockPos>();
		dirtyChunkPositions.Clear();
		WorldMap worldMap = worldmap;
		IList<Block> blocks = worldMap.Blocks;
		if (_blockBreakTasks.Count == 0 && StagedBlocks.Count == 0 && LightSources.Count == 0)
		{
			return list;
		}
		foreach (BlockBreakTask blockBreakTask in _blockBreakTasks)
		{
			BlockPos pos = blockBreakTask.Pos;
			int num4 = pos.X / 32;
			int num5 = pos.InternalY / 32;
			int num6 = pos.Z / 32;
			bool flag = false;
			if (num4 != num || num5 != num2 || num6 != num3)
			{
				worldChunk = worldMap.GetChunk(num = num4, num2 = num5, num3 = num6);
				if (worldChunk == null)
				{
					continue;
				}
				worldChunk.Unpack();
				flag = true;
			}
			if (worldChunk != null)
			{
				int index3d = worldMap.ChunkSizedIndex3D(pos.X & 0x1F, pos.Y & 0x1F, pos.Z & 0x1F);
				blocks[worldChunk.Data[index3d]].OnBlockBroken(worldAccessor, blockBreakTask.Pos, blockBreakTask.byPlayer, blockBreakTask.DropQuantityMultiplier);
				if (flag)
				{
					dirtyChunkPositions.Add(new ChunkPosCompact(num4, num5, num6));
				}
			}
		}
		BlockPos key;
		BlockUpdate value;
		foreach (KeyValuePair<BlockPos, BlockUpdate> stagedBlock in StagedBlocks)
		{
			stagedBlock.Deconstruct(out key, out value);
			BlockPos blockPos = key;
			BlockUpdate blockUpdate = value;
			int num7 = blockPos.X / 32;
			int num8 = blockPos.InternalY / 32;
			int num9 = blockPos.Z / 32;
			if (num7 != num || num8 != num2 || num9 != num3)
			{
				worldChunk = worldMap.GetChunk(num = num7, num2 = num8, num3 = num9);
				if (worldChunk == null)
				{
					continue;
				}
				worldChunk.Unpack();
				dirtyChunkPositions.Add(new ChunkPosCompact(num7, num8, num9));
			}
			if (worldChunk == null)
			{
				continue;
			}
			int index3d2 = worldMap.ChunkSizedIndex3D(blockPos.X & 0x1F, blockPos.Y & 0x1F, blockPos.Z & 0x1F);
			int num10 = ((blockUpdate.NewSolidBlockId >= 0) ? blockUpdate.NewSolidBlockId : blockUpdate.NewFluidBlockId);
			if (num10 < 0)
			{
				num10 = 0;
			}
			Block block = blocks[num10];
			blockUpdate.OldBlockId = worldChunk.Data[index3d2];
			Dictionary<int, Block> subDecors = worldChunk.GetSubDecors(this, blockPos);
			if (subDecors != null && subDecors.Count > 0)
			{
				value = blockUpdate;
				if (value.OldDecors == null)
				{
					value.OldDecors = new List<DecorUpdate>();
				}
				foreach (var (faceAndSubposition, block3) in subDecors)
				{
					blockUpdate.OldDecors.Add(new DecorUpdate
					{
						faceAndSubposition = faceAndSubposition,
						decorId = block3.BlockId
					});
				}
			}
			if (storeOldBlockEntityData && worldAccessor.Blocks[blockUpdate.OldBlockId].EntityClass != null)
			{
				TreeAttribute treeAttribute = new TreeAttribute();
				GetBlockEntity(blockUpdate.Pos)?.ToTreeAttributes(treeAttribute);
				blockUpdate.OldBlockEntityData = treeAttribute.ToBytes();
			}
			blockUpdate.OldFluidBlockId = worldChunk.Data.GetFluid(index3d2);
			if (blockUpdate.NewSolidBlockId >= 0)
			{
				worldChunk.Data[index3d2] = blockUpdate.NewSolidBlockId;
			}
			if (blockUpdate.NewFluidBlockId >= 0)
			{
				worldChunk.Data.SetFluid(index3d2, blockUpdate.NewFluidBlockId);
				if (blockUpdate.NewSolidBlockId == 0)
				{
					block = blocks[blockUpdate.NewFluidBlockId];
				}
			}
			worldChunk.BreakAllDecorFast(worldAccessor, blockPos, index3d2, callOnBrokenAsDecor: false);
			list.Add(blockUpdate);
			hashSet.Add(blockUpdate.Pos);
			if (blockUpdate.NewSolidBlockId > 0 || blockUpdate.NewFluidBlockId > 0)
			{
				worldChunk.Empty = false;
			}
			if (relight && block.GetLightHsv(this, blockPos)[2] > 0)
			{
				LightSources[blockPos] = blockUpdate;
			}
			if (blockPos.dimension == 0)
			{
				UpdateRainHeightMap(blocks[blockUpdate.OldBlockId], block, blockPos, worldChunk.MapChunk);
			}
		}
		foreach (KeyValuePair<BlockPos, BlockUpdate> stagedBlock2 in StagedBlocks)
		{
			stagedBlock2.Deconstruct(out key, out value);
			BlockPos blockPos2 = key;
			BlockUpdate blockUpdate2 = value;
			int newSolidBlockId = blockUpdate2.NewSolidBlockId;
			if (newSolidBlockId < 0 || (blockUpdate2.ExchangeOnly && blocks[newSolidBlockId].EntityClass == null) || (blockUpdate2.OldBlockId == newSolidBlockId && (blockUpdate2.ByStack == null || blocks[blockUpdate2.OldBlockId].EntityClass == null)))
			{
				continue;
			}
			int num12 = blockPos2.X / 32;
			int num13 = blockPos2.InternalY / 32;
			int num14 = blockPos2.Z / 32;
			if (num12 != num || num13 != num2 || num14 != num3)
			{
				worldChunk = worldMap.GetChunk(num = num12, num2 = num13, num3 = num14);
				if (worldChunk == null)
				{
					continue;
				}
				worldChunk.Unpack();
			}
			if (worldChunk != null)
			{
				if (blockUpdate2.ExchangeOnly)
				{
					worldChunk.GetLocalBlockEntityAtBlockPos(blockPos2).OnExchanged(blocks[newSolidBlockId]);
					continue;
				}
				blocks[blockUpdate2.OldBlockId].OnBlockRemoved(worldMap.World, blockPos2);
				blocks[newSolidBlockId].OnBlockPlaced(worldMap.World, blockPos2, blockUpdate2.ByStack);
			}
		}
		foreach (KeyValuePair<BlockPos, BlockUpdate> item in StagedBlocks.Where((KeyValuePair<BlockPos, BlockUpdate> b) => b.Value.Decors != null))
		{
			item.Deconstruct(out key, out value);
			BlockPos blockPos3 = key;
			BlockUpdate blockUpdate3 = value;
			int num15 = blockPos3.X / 32;
			int num16 = blockPos3.InternalY / 32;
			int num17 = blockPos3.Z / 32;
			if (num15 != num || num16 != num2 || num17 != num3)
			{
				worldChunk = worldMap.GetChunk(num = num15, num2 = num16, num3 = num17);
				if (worldChunk == null)
				{
					continue;
				}
				worldChunk.Unpack();
				dirtyChunkPositions.Add(new ChunkPosCompact(num15, num16, num17));
			}
			if (worldChunk == null)
			{
				continue;
			}
			int index3d3 = worldMap.ChunkSizedIndex3D(blockPos3.X & 0x1F, blockPos3.Y & 0x1F, blockPos3.Z & 0x1F);
			foreach (DecorUpdate decor in blockUpdate3.Decors)
			{
				int decorId = decor.decorId;
				Block block4 = blocks[decorId];
				if (decorId > 0)
				{
					worldChunk.SetDecor(block4, index3d3, decor.faceAndSubposition);
					worldChunk.Empty = false;
				}
			}
			list2.Add(blockPos3.Copy());
		}
		if (relight)
		{
			foreach (BlockPos key2 in LightSources.Keys)
			{
				StagedBlocks.Remove(key2);
			}
			worldMap.UpdateLightingBulk(StagedBlocks);
			worldMap.UpdateLightingBulk(LightSources);
		}
		foreach (ChunkPosCompact dirtyChunkPosition in dirtyChunkPositions)
		{
			worldMap.MarkChunkDirty(dirtyChunkPosition.X, dirtyChunkPosition.Y, dirtyChunkPosition.Z, priority: true);
		}
		if (synchronize)
		{
			worldMap.SendBlockUpdateBulk(hashSet, relight);
			worldMap.SendDecorUpdateBulk(list2);
		}
		StagedBlocks.Clear();
		LightSources.Clear();
		dirtyChunkPositions.Clear();
		_blockBreakTasks.Clear();
		return list;
	}

	public override void Rollback()
	{
		StagedBlocks.Clear();
		LightSources.Clear();
		_blockBreakTasks.Clear();
	}

	public override void ExchangeBlock(int blockId, BlockPos pos)
	{
		if ((pos.X | pos.Y | pos.Z) >= 0 && (pos.dimension != 0 || (pos.X < worldmap.MapSizeX && pos.Y < worldmap.MapSizeY && pos.Z < worldmap.MapSizeZ)))
		{
			BlockPos blockPos = pos.Copy();
			StagedBlocks[blockPos] = new BlockUpdate
			{
				NewSolidBlockId = blockId,
				Pos = blockPos,
				ExchangeOnly = true
			};
		}
	}

	public override void BreakBlock(BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
	{
		_blockBreakTasks.Enqueue(new BlockBreakTask
		{
			Pos = pos,
			byPlayer = byPlayer,
			DropQuantityMultiplier = dropQuantityMultiplier
		});
	}

	public int GetStagedBlockId(int posX, int posY, int posZ)
	{
		if (StagedBlocks.TryGetValue(new BlockPos(posX, posY, posZ), out var value) && value.NewSolidBlockId >= 0)
		{
			return value.NewSolidBlockId;
		}
		return GetNonStagedBlockId(posX, posY, posZ, 1);
	}

	public int GetStagedBlockId(BlockPos pos)
	{
		if (StagedBlocks.TryGetValue(pos, out var value) && value.NewSolidBlockId >= 0)
		{
			return value.NewSolidBlockId;
		}
		return GetNonStagedBlockId(pos.X, pos.InternalY, pos.Z, 1);
	}

	public void SetChunks(Vec2i chunkCoord, IWorldChunk[] chunksCol)
	{
		throw new NotImplementedException();
	}

	public void PostCommitCleanup(List<BlockUpdate> updatedBlocks)
	{
		FixWaterfalls(updatedBlocks);
	}

	private void FixWaterfalls(List<BlockUpdate> updatedBlocks)
	{
		Dictionary<BlockPos, BlockPos> dictionary = new Dictionary<BlockPos, BlockPos>();
		BlockPos blockPos = new BlockPos(0);
		HashSet<BlockPos> hashSet = new HashSet<BlockPos>(updatedBlocks.Select((BlockUpdate b) => b.Pos).ToList());
		List<int> list = (from b in worldmap.Blocks
			where b.IsLiquid()
			select b.Id).ToList();
		foreach (BlockUpdate updatedBlock in updatedBlocks)
		{
			if (updatedBlock.OldFluidBlockId <= 0 || !list.Contains(updatedBlock.OldFluidBlockId))
			{
				continue;
			}
			BlockFacing[] aLLFACES = BlockFacing.ALLFACES;
			foreach (BlockFacing facing in aLLFACES)
			{
				blockPos.Set(updatedBlock.Pos).Offset(facing);
				blockPos.SetDimension(updatedBlock.Pos.dimension);
				if (!hashSet.Contains(blockPos))
				{
					dictionary.TryAdd(blockPos.Copy(), updatedBlock.Pos.Copy());
				}
			}
		}
		int num2 = -1;
		int num3 = -1;
		int num4 = -1;
		IWorldChunk worldChunk = null;
		foreach (KeyValuePair<BlockPos, BlockPos> item in dictionary)
		{
			int num5 = item.Value.X / 32;
			int num6 = item.Value.InternalY / 32;
			int num7 = item.Value.Z / 32;
			if (num5 != num2 || num6 != num3 || num7 != num4)
			{
				worldChunk = worldmap.GetChunk(num2 = num5, num3 = num6, num4 = num7);
				if (worldChunk == null)
				{
					continue;
				}
				worldChunk.Unpack();
				dirtyChunkPositions.Add(new ChunkPosCompact(num5, num6, num7));
			}
			if (worldChunk != null)
			{
				int index3d = worldmap.ChunkSizedIndex3D(item.Key.X & 0x1F, item.Key.Y & 0x1F, item.Key.Z & 0x1F);
				Block block = worldmap.Blocks[worldChunk.Data[index3d]];
				if (block.IsLiquid())
				{
					block.OnNeighbourBlockChange(worldAccessor, item.Key, item.Value);
				}
				else
				{
					worldmap.Blocks[worldChunk.Data.GetFluid(index3d)].OnNeighbourBlockChange(worldAccessor, item.Key, item.Value);
				}
			}
		}
		foreach (ChunkPosCompact dirtyChunkPosition in dirtyChunkPositions)
		{
			worldmap.MarkChunkDirty(dirtyChunkPosition.X, dirtyChunkPosition.Y, dirtyChunkPosition.Z, priority: true);
		}
		dirtyChunkPositions.Clear();
		worldmap.SendBlockUpdateBulk(dictionary.Keys, relight);
	}
}
