using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public class EntityPartitioning : ModSystem, IEntityPartitioning
{
	public delegate bool RangeTestDelegate(Entity e, double posX, double posY, double posZ, double radiuSq);

	public const int partitionsLength = 4;

	private const int gridSizeInBlocks = 8;

	private ICoreAPI api;

	private ICoreClientAPI capi;

	private ICoreServerAPI sapi;

	public Dictionary<long, EntityPartitionChunk> Partitions = new Dictionary<long, EntityPartitionChunk>();

	private const int chunkSize = 32;

	public double LargestTouchDistance;

	public override double ExecuteOrder()
	{
		return 0.0;
	}

	public override bool ShouldLoad(EnumAppSide side)
	{
		return true;
	}

	public override void Start(ICoreAPI api)
	{
		this.api = api;
		api.Event.PlayerDimensionChanged += Event_PlayerDimensionChanged;
	}

	private void Event_PlayerDimensionChanged(IPlayer byPlayer)
	{
		RePartitionPlayer(byPlayer.Entity);
	}

	public override void StartClientSide(ICoreClientAPI api)
	{
		capi = api;
		api.Event.RegisterGameTickListener(OnClientTick, 32);
	}

	public override void StartServerSide(ICoreServerAPI api)
	{
		sapi = api;
		api.Event.RegisterGameTickListener(OnServerTick, 32);
		api.Event.PlayerSwitchGameMode += OnSwitchedGameMode;
	}

	private void OnClientTick(float dt)
	{
		PartitionEntities(capi.World.LoadedEntities.Values);
	}

	private void OnServerTick(float dt)
	{
		PartitionEntities(((CachingConcurrentDictionary<long, Entity>)sapi.World.LoadedEntities).Values);
	}

	private void PartitionEntities(ICollection<Entity> entities)
	{
		long sizex = api.World.BlockAccessor.MapSizeX / 32;
		long sizez = api.World.BlockAccessor.MapSizeZ / 32;
		double num = 0.0;
		Dictionary<long, EntityPartitionChunk> partitions = Partitions;
		partitions.Clear();
		foreach (Entity entity in entities)
		{
			if (entity.IsCreature && entity.touchDistance > num)
			{
				num = entity.touchDistance;
			}
			EntityPos pos = entity.Pos;
			int num2 = (int)pos.X;
			int num3 = (int)pos.Z;
			int num4 = num3 / 8 % 4 * 4 + num2 / 8 % 4;
			if (num4 >= 0)
			{
				long key = MapUtil.Index3dL(num2 / 32, (int)pos.InternalY / 32, num3 / 32, sizex, sizez);
				if (!partitions.TryGetValue(key, out var value))
				{
					value = (partitions[key] = new EntityPartitionChunk());
				}
				List<Entity> entityListForPartitioning = value.Add(entity, num4);
				if (entity is EntityPlayer entityPlayer)
				{
					entityPlayer.entityListForPartitioning = entityListForPartitioning;
				}
			}
		}
		LargestTouchDistance = num;
	}

	public void RePartitionPlayer(EntityPlayer entity)
	{
		entity.entityListForPartitioning?.Remove(entity);
		PartitionEntities(new Entity[1] { entity });
	}

	private void OnSwitchedGameMode(IServerPlayer player)
	{
		RePartitionPlayer(player.Entity);
	}

	[Obsolete("In version 1.19.2 and later, this searches only entities which are Creatures, which is probably what the caller wants but you should specify EnumEntitySearchType explicitly")]
	public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
	{
		return GetNearestEntity(position, radius, matches, EnumEntitySearchType.Creatures);
	}

	public Entity GetNearestInteractableEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
	{
		if (matches == null)
		{
			return GetNearestEntity(position, radius, (Entity e) => e.IsInteractable, EnumEntitySearchType.Creatures);
		}
		return GetNearestEntity(position, radius, (Entity e) => matches(e) && e.IsInteractable, EnumEntitySearchType.Creatures);
	}

	public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches, EnumEntitySearchType searchType)
	{
		Entity nearestEntity = null;
		double num = radius * radius;
		double nearestDistanceSq = num;
		if (api is ICoreClientAPI)
		{
			WalkEntities(position.X, position.Y, position.Z, radius, (Entity e) =>
			{
				if (matches(e))
				{
					double num2 = e.Pos.SquareDistanceTo(position);
					if (num2 < nearestDistanceSq)
					{
						nearestDistanceSq = num2;
						nearestEntity = e;
					}
				}
				return true;
			}, null, searchType);
		}
		else
		{
			WalkEntities(position.X, position.Y, position.Z, radius, (Entity e) =>
			{
				double num2 = e.Pos.SquareDistanceTo(position);
				if (num2 < nearestDistanceSq && matches(e))
				{
					nearestDistanceSq = num2;
					nearestEntity = e;
				}
				return true;
			}, null, searchType);
		}
		return nearestEntity;
	}

	private bool onIsInRangeServer(Entity e, double posX, double posY, double posZ, double radiusSq)
	{
		EntityPos pos = e.Pos;
		double num = pos.X - posX;
		double num2 = pos.InternalY - posY;
		double num3 = pos.Z - posZ;
		return num * num + num2 * num2 + num3 * num3 < radiusSq;
	}

	private bool onIsInRangeClient(Entity e, double posX, double posY, double posZ, double radiusSq)
	{
		EntityPos pos = e.Pos;
		double num = pos.X - posX;
		double num2 = pos.InternalY - posY;
		double num3 = pos.Z - posZ;
		return num * num + num2 * num2 + num3 * num3 < radiusSq;
	}

	[Obsolete("In version 1.19.2 and later, this walks through Creature entities only, so recommended to call WalkEntityPartitions() specifying the type of search explicitly for clarity in the calling code")]
	public void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
	{
		WalkEntities(centerPos, radius, callback, EnumEntitySearchType.Creatures);
	}

	[Obsolete("In version 1.19.2 and later, use WalkEntities specifying the searchtype (Creatures or Inanimate) explitly in the calling code.")]
	public void WalkInteractableEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
	{
		WalkEntities(centerPos, radius, callback, EnumEntitySearchType.Creatures);
	}

	public void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback, EnumEntitySearchType searchType)
	{
		if (api.Side == EnumAppSide.Client)
		{
			WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, onIsInRangeClient, searchType);
		}
		else
		{
			WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, onIsInRangeServer, searchType);
		}
	}

	public void WalkEntityPartitions(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
	{
		WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, null, EnumEntitySearchType.Creatures);
	}

	public void WalkEntities(double centerPosX, double centerPosY, double centerPosZ, double radius, ActionConsumable<Entity> callback, RangeTestDelegate onRangeTest, EnumEntitySearchType searchType)
	{
		IBlockAccessor blockAccessor = api.World.BlockAccessor;
		long sizex = blockAccessor.MapSizeX / 32;
		long sizez = blockAccessor.MapSizeZ / 32;
		int num = blockAccessor.MapSizeX / 8 - 1;
		int num2 = blockAccessor.MapSizeY / 32 - 1;
		int num3 = blockAccessor.MapSizeZ / 8 - 1;
		int num4 = (int)GameMath.Clamp((centerPosX - radius) / 8.0, 0.0, num);
		int num5 = (int)GameMath.Clamp((centerPosX + radius) / 8.0, 0.0, num);
		int num6 = (int)GameMath.Clamp((centerPosY - radius) / 32.0, 0.0, num2);
		int num7 = (int)GameMath.Clamp((centerPosY + radius) / 32.0, 0.0, num2);
		int num8 = (int)GameMath.Clamp((centerPosZ - radius) / 8.0, 0.0, num3);
		int num9 = (int)GameMath.Clamp((centerPosZ + radius) / 8.0, 0.0, num3);
		double radiuSq = radius * radius;
		Dictionary<long, EntityPartitionChunk> partitions = Partitions;
		EntityPartitionChunk value = null;
		long num10 = -1L;
		for (int i = num6; i <= num7; i++)
		{
			for (int j = num8; j <= num9; j++)
			{
				int z = j / 4;
				int num11 = j % 4 * 4;
				long num12 = MapUtil.Index3dL(0, i, z, sizex, sizez);
				for (int k = num4; k <= num5; k++)
				{
					if (num12 + k / 4 != num10)
					{
						num10 = num12 + k / 4;
						partitions.TryGetValue(num10, out value);
					}
					if (value == null)
					{
						continue;
					}
					object obj;
					if (searchType != EnumEntitySearchType.Creatures)
					{
						List<Entity>[] inanimateEntities = value.InanimateEntities;
						obj = ((inanimateEntities != null) ? inanimateEntities[num11 + k % 4] : null);
					}
					else
					{
						obj = value.Entities[num11 + k % 4];
					}
					List<Entity> list = (List<Entity>)obj;
					if (list == null)
					{
						continue;
					}
					if (onRangeTest == null)
					{
						foreach (Entity item in list)
						{
							if (!callback(item))
							{
								return;
							}
						}
						continue;
					}
					foreach (Entity item2 in list)
					{
						if (onRangeTest(item2, centerPosX, centerPosY, centerPosZ, radiuSq) && !callback(item2))
						{
							return;
						}
					}
				}
			}
		}
	}
}
