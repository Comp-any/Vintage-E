using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Added to every player on the server. No fall damage with an active Swiftwolf's Rending Gale (or Ring of Arcana
    /// with its Swiftwolf effect on), while flying on one and for 15 seconds after the flight ends, with gem leggings
    /// or with a full set of gem armour. No fire damage while carrying an Ignition Ring, Volcanite Amulet or Ring of Arcana (ProjectE's
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

            int gem = ArmorPieces(player, "gem", out bool gemBody), red = ArmorPieces(player, "red", out _);

            bool fall = damageSource.Source == EnumDamageSource.Fall || damageSource.Type == EnumDamageType.Gravity;
            TrinketEffects fx = VintageEModSystem.Of(entity.Api)?.Trinkets;
            // Gem leggings carry ProjectE's gem boots, which stop fall damage on their own.
            if (fall && (gem >= 3 || GemArmor.Worn(player, "legs") != null || TrinketEffects.HasGaleFlight(player) || fx?.FlightProtects(player) == true))
            {
                damage = 0;
                return;
            }
            // Fire can't hurt a full set of red matter or gem armour (ProjectE); a gem chestplate shrugs off explosions.
            bool fire = damageSource.Type == EnumDamageType.Fire;
            if (fire && (gem >= 3 || red >= 3 || TrinketEffects.IsFireProof(player)))
            {
                damage = 0;
                return;
            }
            if (damageSource.Source == EnumDamageSource.Explosion && gemBody)
            {
                damage = 0;
            }
        }

        /// <summary>How many pieces of this mod's <paramref name="material"/> armour the player wears, and whether the chestplate is one of them.</summary>
        public static int ArmorPieces(IPlayer player, string material, out bool body)
        {
            int n = 0;
            body = false;
            IInventory gear = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear == null) return 0;
            string prefix = "matterarmor-" + material + "-";
            foreach (ItemSlot slot in gear)
            {
                CollectibleObject c = slot?.Itemstack?.Collectible;
                if (c?.Code == null || c.Code.Domain != VintageEModSystem.ModId || !c.Code.Path.StartsWith(prefix)) continue;
                n++;
                if (c.Code.Path.EndsWith("-body")) body = true;
            }
            return n;
        }
    }
}
