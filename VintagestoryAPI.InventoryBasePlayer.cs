using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common;

/// <summary>
/// Abstract class used for all inventories that are "on" the player. Any inventory not inheriting from this class will not be stored to the savegame as part of the players inventory.
/// </summary>
public abstract class InventoryBasePlayer : InventoryBase, IOwnedInventory
{
	/// <summary>
	/// The player ID for the inventory.
	/// </summary>
	protected string playerUID;

	public override bool RemoveOnClose => false;

	/// <summary>
	/// The owning player of this inventory
	/// </summary>
	public IPlayer Player => Api.World.PlayerByUid(playerUID);

	public Entity Owner => Player.Entity;

	public InventoryBasePlayer(string className, string playerUID, ICoreAPI api)
		: base(className, playerUID, api)
	{
		this.playerUID = playerUID;
	}

	public InventoryBasePlayer(string inventoryID, ICoreAPI api)
		: base(inventoryID, api)
	{
		playerUID = instanceID;
	}

	public override bool CanPlayerAccess(IPlayer player, EntityPos position)
	{
		return player.PlayerUID == playerUID;
	}

	public override bool HasOpened(IPlayer player)
	{
		if (!(player.PlayerUID == playerUID))
		{
			return base.HasOpened(player);
		}
		return true;
	}

	public override void DropAll(Vec3d pos, int maxStackSize = 0)
	{
		int despawnSeconds = (Player?.Entity?.Properties.Attributes)?["droppedItemsOnDeathTimer"].AsInt(GlobalConstants.TimeToDespawnPlayerInventoryDrops) ?? GlobalConstants.TimeToDespawnPlayerInventoryDrops;
		for (int i = 0; i < Count; i++)
		{
			ItemSlot itemSlot = this[i];
			if (itemSlot.Itemstack == null)
			{
				continue;
			}
			EnumHandling handling = EnumHandling.PassThrough;
			itemSlot.Itemstack.Collectible.OnHeldDropped(Api.World, Api.World.PlayerByUid(playerUID), itemSlot, itemSlot.Itemstack.StackSize, ref handling);
			if (handling != EnumHandling.PassThrough)
			{
				continue;
			}
			dirtySlots.Add(i);
			if (maxStackSize > 0)
			{
				while (itemSlot.StackSize > 0)
				{
					ItemStack itemstack = itemSlot.TakeOut(GameMath.Clamp(itemSlot.StackSize, 1, maxStackSize));
					spawnItemEntity(itemstack, pos, despawnSeconds);
				}
			}
			else
			{
				spawnItemEntity(itemSlot.Itemstack, pos, despawnSeconds);
			}
			itemSlot.Itemstack = null;
		}
	}

	protected void spawnItemEntity(ItemStack itemstack, Vec3d pos, int despawnSeconds)
	{
		Entity? entity = Api.World.SpawnItemEntity(itemstack, pos);
		entity.WatchedAttributes.SetString("byPlayerUid", playerUID);
		entity.Attributes.SetInt("minsecondsToDespawn", despawnSeconds);
		if (entity.GetBehavior("timeddespawn") is ITimedDespawn timedDespawn)
		{
			timedDespawn.DespawnSeconds = despawnSeconds;
		}
	}
}
