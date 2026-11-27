using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.Server;

public class EntitySpawnerResult
{
	private List<SpawnOppurtunity> spawnPositions;

	private SpawnState spawnState;

	private int quantityToSpawn;

	public EntitySpawnerResult(List<SpawnOppurtunity> spawnPositions, SpawnState spawnState)
	{
		this.spawnPositions = spawnPositions;
		this.spawnState = spawnState;
		quantityToSpawn = spawnState.NextGroupSize;
	}

	public void Spawn(ServerMain server, ServerSystemEntitySpawner entitySpawner)
	{
		long nextHerdId = server.GetNextHerdId();
		if (server.SpawnDebug)
		{
			SpawnOppurtunity spawnOppurtunity = spawnPositions[0];
			ServerMain.Logger.Notification("Spawn {0}x {1} @{2}/{3}/{4}", spawnPositions.Count, spawnOppurtunity.ForType.Code, (int)spawnOppurtunity.Pos.X, (int)spawnOppurtunity.Pos.Y, (int)spawnOppurtunity.Pos.Z);
		}
		BlockPos blockPos = new BlockPos(0);
		RuntimeSpawnConditions runtime = spawnState.ForType.Server.SpawnConditions.Runtime;
		int num = 0;
		foreach (SpawnOppurtunity spawnPosition in spawnPositions)
		{
			if (quantityToSpawn-- <= 0)
			{
				break;
			}
			EntityProperties properties = spawnPosition.ForType;
			if (entitySpawner.CheckCanSpawnAt(properties, runtime, blockPos.SetAndCorrectDimension(spawnPosition.Pos)))
			{
				AssetLocation code = properties.Code;
				if (server.EventManager.TriggerTrySpawnEntity(server.blockAccessor, ref properties, spawnPosition.Pos, nextHerdId))
				{
					DoSpawn(server, properties, spawnPosition.Pos, nextHerdId, code);
					num++;
				}
			}
		}
		ServerMain.FrameProfiler.Mark(spawnState.profilerName);
		if (num < quantityToSpawn)
		{
			spawnState.SpawnableAmountGlobal += quantityToSpawn - num;
		}
	}

	private static void DoSpawn(ServerMain server, EntityProperties entityType, Vec3d spawnPosition, long herdid, AssetLocation originalType)
	{
		Entity entity = server.Api.ClassRegistry.CreateEntity(entityType);
		if (entity is EntityAgent entityAgent)
		{
			entityAgent.HerdId = herdid;
		}
		EntityPos pos = entity.Pos;
		pos.SetPosWithDimension(spawnPosition);
		pos.SetYaw((float)((IWorldAccessor)server).Rand.NextDouble() * ((float)Math.PI * 2f));
		entity.Pos.SetFrom(pos);
		entity.PositionBeforeFalling.Set(pos.X, pos.Y, pos.Z);
		if (entityType.Code != originalType)
		{
			entity.Attributes.SetString("originaltype", originalType.ToString());
		}
		entity.Attributes.SetString("origin", "entityspawner");
		server.DelayedSpawnQueue.Enqueue(entity);
	}
}
