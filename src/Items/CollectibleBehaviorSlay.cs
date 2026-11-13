using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// ProjectE's Dark Matter Sword / Red Matter Sword / Katar area attack: right-click hits every hostile creature
    /// within <see cref="TrinketEffects.SlayRadius"/> blocks ("slay hostile"); sneak + right-click every creature but
    /// players and pets ("slay all"). Costs EMC per creature hit. Property <c>damage</c> sets the hit.
    /// <code>"behaviors": [ { "name": "VintageESlay", "properties": { "damage": 8 } } ]</code>
    /// </summary>
    public class CollectibleBehaviorSlay : CollectibleBehavior, IExtraFunctionItem
    {
        float damage = 8;
        /// <summary>Last strike per player: once a swing, however fast they click.</summary>
        readonly System.Collections.Generic.Dictionary<string, long> lastMs = new System.Collections.Generic.Dictionary<string, long>();

        public CollectibleBehaviorSlay(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            damage = properties?["damage"].AsFloat(8) ?? 8;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (!firstEvent) return;
            // Sneaking at a block is the game's ground storage (the sword is put down): "slay all" is sneak + right-click in the air.
            if (byEntity.Controls.Sneak && blockSel != null) return;
            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server || !((byEntity as EntityPlayer)?.Player is IServerPlayer player)) return;
            long now = byEntity.World.ElapsedMilliseconds;
            if (lastMs.TryGetValue(player.PlayerUID, out long last) && now - last < 600) return; // once a swing
            lastMs[player.PlayerUID] = now;
            TrinketEffects fx = VintageEModSystem.Of(byEntity.Api)?.Trinkets;
            fx?.Slay(player, damage, byEntity.Controls.Sneak);
        }

        /// <summary>ProjectE's extra-function key on a sword or the katar: the same area strike as right-click (sneak: every creature).</summary>
        public bool DoExtraFunction(IServerPlayer player, ItemSlot slot)
        {
            long now = player.Entity.World.ElapsedMilliseconds;
            if (lastMs.TryGetValue(player.PlayerUID, out long last) && now - last < 600) return false;
            lastMs[player.PlayerUID] = now;
            TrinketEffects fx = VintageEModSystem.Of(player.Entity.Api)?.Trinkets;
            return (fx?.Slay(player, damage, player.Entity.Controls.Sneak) ?? 0) > 0;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-slay-hostile", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-slay-all", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right }
            };
        }
    }
}
