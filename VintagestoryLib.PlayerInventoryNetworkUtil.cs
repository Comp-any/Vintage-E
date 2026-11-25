using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.Common;

public class PlayerInventoryNetworkUtil : InventoryNetworkUtil
{
	public PlayerInventoryNetworkUtil(InventoryBase inv, ICoreAPI api)
		: base(inv, api)
	{
	}

	public override void UpdateFromPacket(IWorldAccessor world, Packet_InventoryUpdate packet)
	{
		ItemStack itemStack = null;
		ItemSlot itemSlot = inv[packet.SlotId];
		if (IsOwnHotbarSlotClient(itemSlot))
		{
			itemStack = itemSlot.Itemstack;
			if (itemStack != null)
			{
				ItemStack itemStack2 = ItemStackFromPacket(world, packet.ItemStack);
				if (itemStack2 == null || itemStack.Collectible != itemStack2.Collectible)
				{
					IClientPlayer player = (world as IClientWorldAccessor).Player;
					itemStack.Collectible.OnHeldInteractCancel(0f, itemSlot, player.Entity, player.CurrentBlockSelection, player.CurrentEntitySelection, EnumItemUseCancelReason.Destroyed);
				}
			}
		}
		base.UpdateFromPacket(world, packet);
	}

	private bool IsOwnHotbarSlotClient(ItemSlot slot)
	{
		return (Api as ICoreClientAPI)?.World.Player.InventoryManager.ActiveHotbarSlot == slot;
	}
}
