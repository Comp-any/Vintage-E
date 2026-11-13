using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// ProjectE's gem armour effects. Vintage Story armour has three pieces, so the gem leggings carry what
    /// ProjectE splits between its leggings and boots.
    /// <list type="bullet">
    /// <item>Helmet: heals a tenth of your health a second; its night vision is switched with its key (Shift + N).</item>
    /// <item>Chestplate: feeds a tenth of your saturation a second while you're hungry.</item>
    /// <item>Leggings: twice the walking speed and twice as high jumps; you fall slowly, and with sneak fast instead;
    ///   sneaking pushes creatures and their projectiles away (3.5 blocks), and creatures you come down on take
    ///   damage; step assist (climb a full block without jumping) is switched with its key (X); no fall damage.</item>
    /// </list>
    /// All free, as in ProjectE.
    /// </summary>
    public static class GemArmor
    {
        public const string NightVisionKey = "vintageeNightVision";
        public const string StepAssistKey = "vintageeStepAssist";
        /// <summary>The leggings' walk speed and jump stats (added to the player's own).</summary>
        public const string StatCode = "vintageegem";
        public const float RepelRadius = 3.5f;

        /// <summary>The gem armour piece worn on <paramref name="part"/> (head, body, legs), or null.</summary>
        public static ItemSlot Worn(IPlayer player, string part)
        {
            IInventory gear = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear == null) return null;
            string path = "matterarmor-gem-" + part;
            foreach (ItemSlot slot in gear)
            {
                AssetLocation code = slot?.Itemstack?.Collectible?.Code;
                if (code != null && code.Domain == VintageEModSystem.ModId && code.Path == path) return slot;
            }
            return null;
        }

        /// <summary>Every half second for each player (server): heal, feed, and the leggings' speed and jump.</summary>
        public static void ServerTick(TrinketEffects fx, IServerPlayer player, bool healNow, bool feedNow)
        {
            EntityPlayer e = player.Entity;
            if (e == null) return;
            if (healNow && Worn(player, "head") != null)
            {
                ITreeAttribute health = e.WatchedAttributes.GetTreeAttribute("health");
                float cur = health?.GetFloat("currenthealth") ?? 0, max = health?.GetFloat("maxhealth") ?? 0;
                if (max > 0 && cur < max) e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, Math.Min(max - cur, max * 0.1f));
            }
            if (feedNow && Worn(player, "body") != null)
            {
                ITreeAttribute hunger = e.WatchedAttributes.GetTreeAttribute("hunger");
                float cur = hunger?.GetFloat("currentsaturation") ?? 0, max = hunger?.GetFloat("maxsaturation") ?? 0;
                if (max > 0 && cur < max) TrinketEffects.GiveSaturation(e, Math.Min(max - cur, max * 0.1f), false);
            }
            bool legs = Worn(player, "legs") != null;
            SetStat(e, "walkspeed", legs);
            SetStat(e, "jumpHeightMul", legs);
        }

        /// <summary>+1 on a player stat while worn (twice the base), only touching it when it changes (each change is sent to everyone).</summary>
        static void SetStat(EntityPlayer e, string stat, bool on)
        {
            bool has = false;
            try { has = e.Stats[stat]?.ValuesByKey.ContainsKey(StatCode) == true; } catch { }
            if (on && !has) e.Stats.Set(stat, StatCode, 1f, false);
            else if (!on && has) e.Stats.Remove(stat, StatCode);
        }

        /// <summary>Ten times a second while a player in gem leggings sneaks (server): push creatures away, hit what they land on.</summary>
        public static void LegsTick(TrinketEffects fx, IServerPlayer player, double verticalSpeed)
        {
            fx.RepelCreatures(player.Entity, RepelRadius);
            // ProjectE: falling faster than 0.08 blocks a tick (1.6 a second).
            if (verticalSpeed < -1.6) fx.Stomp(player.Entity, verticalSpeed);
        }

        /// <summary>The night vision or step assist key: switches it on the worn piece. Returns the message to show, or null.</summary>
        public static string Toggle(IServerPlayer player, bool nightVision)
        {
            ItemSlot slot = Worn(player, nightVision ? "head" : "legs");
            string lang = player.LanguageCode;
            if (slot?.Itemstack == null) return Lang.GetL(lang, nightVision ? "vintagee:gem-nohelmet" : "vintagee:gem-nolegs");
            string key = nightVision ? NightVisionKey : StepAssistKey;
            bool on = !slot.Itemstack.Attributes.GetBool(key);
            slot.Itemstack.Attributes.SetBool(key, on);
            slot.MarkDirty();
            return Lang.GetL(lang, nightVision ? (on ? "vintagee:gem-nightvision-on" : "vintagee:gem-nightvision-off") : (on ? "vintagee:gem-stepassist-on" : "vintagee:gem-stepassist-off"));
        }
    }

    /// <summary>
    /// The client half of the gem armour: night vision (the game's own night vision shader, as its night vision device
    /// uses), step assist, and falling slowly or (sneaking) fast. Your own movement is worked out on your computer
    /// in Vintage Story, so this has to run here.
    /// </summary>
    public class GemArmorClient : IRenderer
    {
        readonly ICoreClientAPI capi;
        float? baseStep;
        long lastJumpMs;

        /// <summary>After the game's night vision device, which sets the strength to 0 every frame when none is worn.</summary>
        public double RenderOrder => 0.5;
        public int RenderRange => 1;

        public GemArmorClient(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "vintagee-gemarmor");
            capi.Event.RegisterGameTickListener(Tick, 20);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            IPlayer player = capi.World?.Player;
            if (player == null) return;
            ItemSlot helmet = GemArmor.Worn(player, "head");
            if (helmet?.Itemstack?.Attributes?.GetBool(GemArmor.NightVisionKey) == true) capi.Render.ShaderUniforms.NightVisionStrength = 0.8f;
        }

        void Tick(float dt)
        {
            try { Move(dt); }
            catch (Exception e) { capi.Logger.VerboseDebug("Vintage-E gem armour: {0}", e.Message); }
        }

        void Move(float dt)
        {
            IClientPlayer player = capi.World?.Player;
            EntityPlayer e = player?.Entity;
            if (e == null) return;
            ItemSlot legs = GemArmor.Worn(player, "legs");

            // Step assist: a full block up without jumping (ProjectE: +0.4 on Minecraft's 0.6).
            EntityBehaviorControlledPhysics phys = e.GetBehavior<EntityBehaviorControlledPhysics>();
            if (phys != null)
            {
                baseStep ??= phys.StepHeight;
                bool step = legs?.Itemstack?.Attributes?.GetBool(GemArmor.StepAssistKey) == true;
                float want = step ? Math.Max(baseStep.Value, 1.05f) : baseStep.Value;
                if (Math.Abs(phys.StepHeight - want) > 1e-3) phys.StepHeight = want;
            }
            if (legs == null) return;

            long now = capi.World.ElapsedMilliseconds;
            if (e.Controls.Jump) lastJumpMs = now;
            bool flying = e.Controls.IsFlying || player.WorldData?.FreeMove == true;
            if (e.OnGround || e.Swimming || flying || e.Controls.IsClimbing) return;
            var m = e.Pos.Motion;
            if (e.Controls.Sneak)
            {
                // ProjectE's leggings: sneaking in the air drops you fast (not right after a jump).
                if (now - lastJumpMs > 250 && m.Y > -1.5) m.Y = Math.Max(-1.5, m.Y - 2.1 * dt);
            }
            else if (m.Y < 0)
            {
                // ProjectE's boots: falling is slowed by a tenth every tick.
                m.Y *= Math.Pow(0.9, dt * 20);
            }
        }

        public void Dispose() { }
    }
}
