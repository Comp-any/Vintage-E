using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BlockEntitySpawner : BlockEntity
{
	public BESpawnerData Data = new BESpawnerData().initDefaults();

	protected HashSet<long> spawnedEntities = new HashSet<long>();

	protected GuiDialogSpawner dlg;

	protected CollisionTester collisionTester = new CollisionTester();

	protected bool requireSpawnOnWallSide;

	protected virtual long GetNextHerdId()
	{
		return (Api as ICoreServerAPI).WorldManager.GetNextUniqueId();
	}

	public override void Initialize(ICoreAPI api)
	{
		base.Initialize(api);
		if (api.Side == EnumAppSide.Server)
		{
			RegisterGameTickListener(OnGameTick, 2000);
			if (Data.InternalCapacity > 0)
			{
				(api as ICoreServerAPI).Event.OnEntityDespawn += Event_OnEntityDespawn;
			}
		}
	}

	private void Event_OnEntityDespawn(Entity entity, EntityDespawnData despawnData)
	{
		if ((despawnData == null || despawnData.Reason == EnumDespawnReason.Unload || despawnData.Reason == EnumDespawnReason.Expire) && spawnedEntities.Contains(entity.EntityId))
		{
			bool flag = false;
			try
			{
				EntityBehaviorHealth? behavior = entity.GetBehavior<EntityBehaviorHealth>();
				flag = behavior != null && behavior.Health > 0f;
			}
			catch (Exception)
			{
			}
			if (flag)
			{
				Data.InternalCharge++;
			}
		}
	}

	protected virtual void OnGameTick(float dt)
	{
		if (Data.EntityCodes == null || Data.EntityCodes.Length == 0)
		{
			Data.LastSpawnTotalHours = Api.World.Calendar.TotalHours;
			return;
		}
		if (Data.InternalCapacity > 0)
		{
			double num = Api.World.Calendar.TotalHours - Data.LastChargeUpdateTotalHours;
			if (num > 0.015)
			{
				Data.InternalCharge = Math.Min(Data.InternalCapacity, Data.InternalCharge + num * Data.RechargePerHour);
				Data.LastChargeUpdateTotalHours = Api.World.Calendar.TotalHours;
			}
		}
		ICoreServerAPI coreServerAPI = Api as ICoreServerAPI;
		int num2 = coreServerAPI.World.Rand.Next(Data.EntityCodes.Length);
		TreeAttribute treeAttribute = new TreeAttribute();
		treeAttribute.SetString("type", new AssetLocation(Data.EntityCodes[num2]));
		treeAttribute.SetBlockPos("pos", Pos);
		coreServerAPI.Event.PushEvent("onattemptspawnerspawn", treeAttribute);
		EntityProperties entityType = Api.World.GetEntityType(treeAttribute.GetString("type"));
		if ((Data.InternalCapacity > 0 && Data.InternalCharge < 1.0) || (Data.LastSpawnTotalHours + (double)Data.InGameHourInterval > Api.World.Calendar.TotalHours && Data.InitialSpawnQuantity <= 0) || !IsAreaLoaded() || (Data.SpawnOnlyAfterImport && !Data.WasImported))
		{
			return;
		}
		if (Data.SpawnRangeMode > EnumSpawnRangeMode.IgnorePlayerRange)
		{
			IPlayer player = Api.World.NearestPlayer(Pos.X, Pos.InternalY, Pos.Z);
			if (player?.Entity?.Pos == null)
			{
				return;
			}
			double num3 = player.Entity.Pos.SquareDistanceTo(Pos.ToVec3d());
			if ((Data.SpawnRangeMode == EnumSpawnRangeMode.WhenInRange && num3 > (double)(Data.MinPlayerRange * Data.MinPlayerRange)) || (Data.SpawnRangeMode == EnumSpawnRangeMode.WhenOutsideOfRange && num3 < (double)(Data.MaxPlayerRange * Data.MaxPlayerRange)) || (Data.SpawnRangeMode == EnumSpawnRangeMode.WithinMinMaxRange && (num3 < (double)(Data.MinPlayerRange * Data.MinPlayerRange) || num3 > (double)(Data.MaxPlayerRange * Data.MaxPlayerRange))))
			{
				return;
			}
		}
		if (entityType == null)
		{
			return;
		}
		long[] array = spawnedEntities.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			coreServerAPI.World.LoadedEntities.TryGetValue(array[i], out var value);
			if (value == null || !value.Alive)
			{
				spawnedEntities.Remove(array[i]);
			}
		}
		if (spawnedEntities.Count >= Data.MaxCount)
		{
			Data.LastSpawnTotalHours = Api.World.Calendar.TotalHours;
			return;
		}
		Cuboidf entityBoxRel = new Cuboidf
		{
			X1 = (0f - entityType.CollisionBoxSize.X) / 2f,
			Z1 = (0f - entityType.CollisionBoxSize.X) / 2f,
			X2 = entityType.CollisionBoxSize.X / 2f,
			Z2 = entityType.CollisionBoxSize.X / 2f,
			Y2 = entityType.CollisionBoxSize.Y
		}.OmniNotDownGrowBy(0.1f);
		Cuboidf cuboidf = new Cuboidf
		{
			X1 = (0f - entityType.CollisionBoxSize.X) / 2f,
			Z1 = (0f - entityType.CollisionBoxSize.X) / 2f,
			X2 = entityType.CollisionBoxSize.X / 2f,
			Z2 = entityType.CollisionBoxSize.X / 2f,
			Y2 = entityType.CollisionBoxSize.Y
		};
		int groupSize = Data.GroupSize;
		long num4 = 0L;
		Vec3d vec3d = new Vec3d();
		BlockPos blockPos = new BlockPos(Pos.dimension);
		while (groupSize-- > 0)
		{
			for (int j = 0; j < 15; j++)
			{
				vec3d.SetWithDimension(Pos).Add(0.5 + (double)Data.SpawnArea.MinX + Api.World.Rand.NextDouble() * (double)Data.SpawnArea.SizeX, (double)Data.SpawnArea.MinY + Api.World.Rand.NextDouble() * (double)Data.SpawnArea.SizeY, 0.5 + (double)Data.SpawnArea.MinZ + Api.World.Rand.NextDouble() * (double)Data.SpawnArea.SizeZ);
				if (collisionTester.IsColliding(Api.World.BlockAccessor, entityBoxRel, vec3d, alsoCheckTouch: false))
				{
					continue;
				}
				if (requireSpawnOnWallSide)
				{
					bool flag = false;
					int num5 = 0;
					while (!flag && num5 < 6)
					{
						BlockFacing blockFacing = BlockFacing.ALLFACES[num5];
						blockPos.Set(vec3d).Add(blockFacing.Normali);
						flag = Api.World.BlockAccessor.IsSideSolid(blockPos.X, blockPos.Y, blockPos.Z, blockFacing.Opposite);
						if (flag)
						{
							Cuboidd cuboidd = cuboidf.ToDouble().Translate(vec3d);
							Cuboidd cuboidd2 = Cuboidf.Default().ToDouble().Translate(blockPos);
							switch (blockFacing.Index)
							{
							case 0:
								vec3d.Z -= cuboidd2.Z2 - cuboidd.Z1 + 0.009999999776482582;
								break;
							case 1:
								vec3d.X += cuboidd2.X1 - cuboidd.X2 - 0.009999999776482582;
								break;
							case 2:
								vec3d.Z += cuboidd2.Z1 - cuboidd.Z2 - 0.009999999776482582;
								break;
							case 3:
								vec3d.X -= cuboidd2.X2 - cuboidd.X1 + 0.009999999776482582;
								break;
							case 4:
								vec3d.Y += cuboidd2.Y1 - cuboidd.Y2 - 0.009999999776482582;
								break;
							case 5:
								vec3d.Y -= cuboidd2.Y2 - cuboidd.Y1 + 0.009999999776482582;
								break;
							}
						}
						num5++;
					}
					if (!flag)
					{
						continue;
					}
				}
				if (num4 == 0L)
				{
					num4 = GetNextHerdId();
				}
				DoSpawn(entityType, vec3d, num4);
				Data.LastSpawnTotalHours = Api.World.Calendar.TotalHours;
				if (Data.InitialQuantitySpawned > 0)
				{
					Data.InitialQuantitySpawned--;
				}
				decrementSpawnCount();
				return;
			}
		}
		void decrementSpawnCount()
		{
			if (Data.RemoveAfterSpawnCount > 0)
			{
				Data.RemoveAfterSpawnCount--;
				if (Data.RemoveAfterSpawnCount == 0)
				{
					Api.World.BlockAccessor.SetBlock(0, Pos);
				}
			}
		}
	}

	public override void OnBlockPlaced(ItemStack byItemStack = null)
	{
		base.OnBlockPlaced(byItemStack);
		Data.LastSpawnTotalHours = Api.World.Calendar.TotalHours;
		if (byItemStack == null)
		{
			Data.InternalCharge = Data.InternalCapacity;
			return;
		}
		byte[] bytes = byItemStack.Attributes.GetBytes("spawnerData");
		if (bytes == null)
		{
			return;
		}
		try
		{
			Data = SerializerUtil.Deserialize<BESpawnerData>(bytes);
		}
		catch
		{
			Data = new BESpawnerData().initDefaults();
		}
	}

	public override void OnBlockRemoved()
	{
		base.OnBlockRemoved();
		if (Api is ICoreServerAPI coreServerAPI)
		{
			coreServerAPI.Event.OnEntityDespawn -= Event_OnEntityDespawn;
		}
	}

	public override void OnBlockUnloaded()
	{
		base.OnBlockUnloaded();
		if (Api is ICoreServerAPI coreServerAPI)
		{
			coreServerAPI.Event.OnEntityDespawn -= Event_OnEntityDespawn;
		}
	}

	protected virtual void DoSpawn(EntityProperties entityType, Vec3d spawnPosition, long herdid)
	{
		if (Data.InternalCapacity > 0)
		{
			Data.InternalCharge--;
		}
		MarkDirty(redrawOnClient: true);
		Entity entity = Api.World.ClassRegistry.CreateEntity(entityType);
		if (entity is EntityAgent entityAgent)
		{
			entityAgent.HerdId = herdid;
		}
		entity.Pos.SetPosWithDimension(spawnPosition);
		entity.Pos.SetYaw((float)Api.World.Rand.NextDouble() * ((float)Math.PI * 2f));
		entity.Attributes.SetString("origin", "entityspawner");
		Api.World.SpawnEntity(entity);
		spawnedEntities.Add(entity.EntityId);
	}

	public bool IsAreaLoaded()
	{
		ICoreServerAPI coreServerAPI = Api as ICoreServerAPI;
		int num = coreServerAPI.WorldManager.MapSizeX / 32;
		int num2 = coreServerAPI.WorldManager.MapSizeY / 32;
		int num3 = coreServerAPI.WorldManager.MapSizeZ / 32;
		int num4 = GameMath.Clamp((Pos.X + Data.SpawnArea.MinX) / 32, 0, num - 1);
		int num5 = GameMath.Clamp((Pos.X + Data.SpawnArea.MaxX) / 32, 0, num - 1);
		int num6 = GameMath.Clamp((Pos.Y + Data.SpawnArea.MinY) / 32, 0, num2 - 1);
		int num7 = GameMath.Clamp((Pos.Y + Data.SpawnArea.MaxY) / 32, 0, num2 - 1);
		int num8 = GameMath.Clamp((Pos.Z + Data.SpawnArea.MinZ) / 32, 0, num3 - 1);
		int num9 = GameMath.Clamp((Pos.Z + Data.SpawnArea.MaxZ) / 32, 0, num3 - 1);
		for (int i = num4; i <= num5; i++)
		{
			for (int j = num6; j <= num7; j++)
			{
				for (int k = num8; k <= num9; k++)
				{
					if (coreServerAPI.WorldManager.GetChunk(i, j, k) == null)
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	internal void OnInteract(IPlayer byPlayer)
	{
		if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
		{
			return;
		}
		if (Api.Side == EnumAppSide.Server)
		{
			(Api as ICoreServerAPI).Network.SendBlockEntityPacket(byPlayer as IServerPlayer, Pos, 1000, SerializerUtil.Serialize(Data));
			return;
		}
		dlg = new GuiDialogSpawner(Pos, Api as ICoreClientAPI);
		dlg.spawnerData = Data;
		dlg.TryOpen();
		dlg.OnClosed += () =>
		{
			dlg?.Dispose();
			dlg = null;
		};
	}

	public override void OnReceivedServerPacket(int packetid, byte[] bytes)
	{
		if (packetid == 1000)
		{
			Data = SerializerUtil.Deserialize<BESpawnerData>(bytes);
			GuiDialogSpawner guiDialogSpawner = dlg;
			if (guiDialogSpawner != null && guiDialogSpawner.IsOpened())
			{
				dlg.UpdateFromServer(Data);
			}
		}
	}

	public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] bytes)
	{
		if (packetid == 1001)
		{
			if (fromPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
			{
				Api.Logger.Audit($"{fromPlayer.PlayerName} not in creative mode tried to set spawner data at {Pos}");
			}
			else
			{
				Data = SerializerUtil.Deserialize<BESpawnerData>(bytes);
				MarkDirty();
			}
		}
	}

	public override void ToTreeAttributes(ITreeAttribute tree)
	{
		base.ToTreeAttributes(tree);
		Data.ToTreeAttributes(tree);
		tree["spawnedEntities"] = new LongArrayAttribute(spawnedEntities.ToArray());
	}

	public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
	{
		base.FromTreeAttributes(tree, worldAccessForResolve);
		Data = new BESpawnerData();
		Data.FromTreeAttributes(tree);
		long[] array = (tree["spawnedEntities"] as LongArrayAttribute)?.value;
		spawnedEntities = new HashSet<long>((array == null) ? Array.Empty<long>() : array);
	}

	public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
	{
		base.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
		if (resolveImports)
		{
			Data.WasImported = true;
			Data.LastSpawnTotalHours = 0.0;
		}
	}

	public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
	{
		base.GetBlockInfo(forPlayer, dsc);
		if (Api is ICoreClientAPI coreClientAPI && coreClientAPI.Settings.Bool["extendedDebugInfo"])
		{
			dsc.AppendLine("Charge capacity: " + Data.InternalCapacity);
			dsc.AppendLine("Internal charge: " + Data.InternalCharge);
		}
	}
}
