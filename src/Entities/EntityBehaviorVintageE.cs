using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Added to every player on the server. No fall damage with an active Swiftwolf's Rending Gale (or Ring of Arcana
    /// with its Swiftwolf effect on), while flying on one and for 15 seconds after the flight ends, or with a full set
    /// of gem armour. No fire damage while carrying an Ignition Ring, Volcanite Amulet or Ring of Arcana (ProjectE's
    /// fire protection, on or off) or wearing full gem armour. It's put first among the player's behaviours, ahead of
    /// the game's health behaviour (which takes the damage off when its turn comes).
    /// </summary>
    public class EntityBehaviorVintageE : EntityBehavior
    {
        public const string Code = "vintageeplayer";

        public EntityBehaviorVintageE(Entity entity) : base(entity) { }

        public override string PropertyName() => Code;

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            IPlayer player = (entity as EntityPlayer)?.Player;
            if (player == null || damage <= 0 || damageSource.Type == EnumDamageType.Heal) return;

            int gem = 0;
            IInventory gear = player.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear != null)
            {
                foreach (ItemSlot slot in gear)
                {
                    string path = slot?.Itemstack?.Collectible?.Code?.Path;
                    if (slot?.Itemstack?.Collectible?.Code?.Domain == VintageEModSystem.ModId && path != null && path.StartsWith("matterarmor-gem-")) gem++;
                }
            }

            bool fall = damageSource.Source == EnumDamageSource.Fall || damageSource.Type == EnumDamageType.Gravity;
            TrinketEffects fx = entity.Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets;
            if (fall && (gem >= 3 || TrinketEffects.HasGaleFlight(player) || fx?.FlightProtects(player) == true))
            {
                damage = 0;
                return;
            }
            bool fire = damageSource.Type == EnumDamageType.Fire;
            if (fire && (gem >= 3 || TrinketEffects.IsFireProof(player)))
            {
                damage = 0;
            }
        }
    }
}
