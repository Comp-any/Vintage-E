using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>Items that shoot something with the fire-projectile key (R by default, like ProjectE).</summary>
    public interface IVintageEShooter
    {
        bool CanShoot(ItemSlot slot);
        /// <summary>Server side. Returns true if something was fired.</summary>
        bool Shoot(IServerPlayer player, ItemSlot slot);
    }

    /// <summary>
    /// ProjectE's rings, stones, amulets, the repair talisman and the watch. Vintage Story has no ring slots, so
    /// these work from anywhere in your hotbar or backpack. Right-click switches one on or off (shown in its
    /// tooltip), except where ProjectE gives right-click another job:
    /// <list type="bullet">
    /// <item>Evertide/Volcanite Amulet: place water/lava where you click (they're always ready, no switch).</item>
    /// <item>Repair Talisman: always works while carried.</item>
    /// <item>Black Hole Band / Void Ring: right-click a water or lava source to remove it.</item>
    /// <item>Harvest Goddess Band: right-click farmland to plant the seeds you carry on farmland around it.</item>
    /// <item>Void Ring: sneak + right-click to teleport to where you look (up to 64 blocks).</item>
    /// </list>
    /// The tool-mode key picks Swiftwolf's flight/shield, the watch's direction, the density target, and switches each
    /// of the Ring of Arcana's four effects on or off. The fire-projectile key shoots water (Evertide), lava (Volcanite)
    /// or fire (Ignition Ring, Ring of Arcana). Effects run in <see cref="TrinketEffects"/>.
    /// </summary>
    public class ItemEmcTrinket : Item, IVintageEShooter
    {
        public const string ActiveKey = "vintageeActive";
        public const string ModeKey = "vintageeMode";
        /// <summary>Ring of Arcana: bit i set = effect <see cref="ArcanaModes"/>[i] switched off.</summary>
        public const string ArcanaOffKey = "vintageeArcanaOff";

        public static readonly string[] ArcanaModes = { "zero", "ignition", "harvest", "swiftwolf" };
        public static readonly string[] GaleModes = { "both", "flight", "shield" };
        /// <summary>Swiftwolf's Rending Gale speed choices (× running and flying speed; the EMC cost of moving scales with it).</summary>
        public static readonly float[] GaleSpeeds = { 1, 1.5f, 2, 3 };
        public const string SpeedKey = "vintageeSpeed";

        /// <summary>Index into <see cref="GaleSpeeds"/> picked on this Swiftwolf's Rending Gale.</summary>
        public static int GetSpeedLevel(ItemStack stack) => GameMath.Clamp(stack?.Attributes?.GetInt(SpeedKey) ?? 0, 0, GaleSpeeds.Length - 1);
        public static readonly string[] WatchModes = { "faster", "slower" };
        /// <summary>Body/Life Stone: saturation only (ProjectE's), or saturation and nutrition (the food groups too, for more EMC).</summary>
        public static readonly string[] FoodModes = { "saturation", "nutrition" };
        public bool UsesFoodModes => Kind == "bodystone" || Kind == "lifestone";
        public static bool FeedsNutrition(ItemStack stack) => stack?.Collectible is ItemEmcTrinket t && t.UsesFoodModes && GetMode(stack) == 1;
        public static readonly string[] DensityTargets = { "game:ingot-iron", "game:ingot-gold", "game:gem-diamond-rough", "vintagee:matter-dark", "vintagee:matter-red" };

        /// <summary>EMC per lava block (amulet click or shot), as ProjectE.</summary>
        public const double LavaCost = 32;
        public const int PlantRadius = 8;
        public const int TeleportRange = 64;

        SkillItem[] modes;
        SkillItem[] arcanaOn, arcanaOff;
        SkillItem[] galeOn, galeOff; // flight/shield choices then speeds, each drawn picked (on) or not

        string kind;

        /// <summary>The "type" variant (which ring, stone or gadget this is). Kept once read: it's asked dozens of times a tick.</summary>
        public string Kind
        {
            get
            {
                if (kind != null) return kind;
                string k = Variant?["type"];
                if (string.IsNullOrEmpty(k)) return ""; // not loaded yet: don't keep an empty answer
                return kind = k;
            }
        }

        public bool IsAmulet => Kind == "evertideamulet" || Kind == "volcaniteamulet";

        /// <summary>Always works while carried (ProjectE's talisman has no on/off).</summary>
        public bool IsAlwaysOn => Kind == "repairtalisman";

        public bool IsToggleable => !IsAmulet && !IsAlwaysOn;

        public bool UsesDensityModes => Kind == "gemofeternaldensity" || Kind == "voidring";

        public static bool IsActive(ItemStack stack) => stack?.Attributes?.GetBool(ActiveKey) == true;

        public static int GetMode(ItemStack stack) => stack?.Attributes?.GetInt(ModeKey) ?? 0;

        /// <summary>Whether the Ring of Arcana's effect <paramref name="index"/> is switched on (all are by default).</summary>
        public static bool ArcanaEnabled(ItemStack stack, int index) => ((stack?.Attributes?.GetInt(ArcanaOffKey) ?? 0) & (1 << index)) == 0;

        TrinketEffects Effects(IWorldAccessor world) => VintageEModSystem.Of(world.Api)?.Trinkets;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            // The Black Hole Band and Void Ring can aim at water/lava sources (right-click removes them).
            if (Kind == "blackholeband" || Kind == "voidring") LiquidSelectable = true;
            if (!(api is ICoreClientAPI capi)) return;
            if (Kind == "ringofarcana")
            {
                arcanaOn = new SkillItem[ArcanaModes.Length];
                arcanaOff = new SkillItem[ArcanaModes.Length];
                for (int i = 0; i < ArcanaModes.Length; i++)
                {
                    string name = Lang.Get("vintagee:arcana-" + ArcanaModes[i]);
                    string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
                    arcanaOn[i] = new SkillItem { Code = new AssetLocation("vintagee", "arcana-" + ArcanaModes[i] + "-on"), Name = Lang.Get("vintagee:arcana-toggle-on", name) }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, letter, true));
                    arcanaOff[i] = new SkillItem { Code = new AssetLocation("vintagee", "arcana-" + ArcanaModes[i] + "-off"), Name = Lang.Get("vintagee:arcana-toggle-off", name) }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, letter, false));
                }
            }
            else if (Kind == "swiftwolfsrendinggale")
            {
                int n = GaleModes.Length + GaleSpeeds.Length;
                galeOn = new SkillItem[n];
                galeOff = new SkillItem[n];
                for (int i = 0; i < n; i++)
                {
                    string name, letter;
                    if (i < GaleModes.Length)
                    {
                        name = Lang.Get("vintagee:gale-" + GaleModes[i]);
                        letter = i == 0 ? "F+S" : i == 1 ? "F" : "S";
                    }
                    else
                    {
                        float sp = GaleSpeeds[i - GaleModes.Length];
                        name = Lang.Get("vintagee:gale-speed", sp.ToString("0.#"), (sp * TrinketEffects.FlightCostPerSecond).ToString("0.#"));
                        letter = sp.ToString("0.#") + "x";
                    }
                    string l = letter;
                    galeOn[i] = new SkillItem { Code = new AssetLocation("vintagee", "gale" + i + "-on"), Name = name }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, l, true));
                    galeOff[i] = new SkillItem { Code = new AssetLocation("vintagee", "gale" + i + "-off"), Name = name }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, l, false, faded: true));
                }
            }
            else if (Kind == "watchofflowingtime")
            {
                modes = new SkillItem[WatchModes.Length];
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "watch-" + WatchModes[i]), Name = Lang.Get("vintagee:watch-" + WatchModes[i]) }
                        .WithLetterIcon(capi, i == 0 ? "+" : "-");
                }
            }
            else if (UsesFoodModes)
            {
                modes = new SkillItem[FoodModes.Length];
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "food-" + FoodModes[i]), Name = Lang.Get("vintagee:food-" + FoodModes[i]) }
                        .WithLetterIcon(capi, i == 0 ? "S" : "N");
                }
            }
            else if (UsesDensityModes)
            {
                // The targets, then "Filter..." which opens the filter window instead of picking a target.
                modes = new SkillItem[DensityTargets.Length + 1];
                for (int i = 0; i < DensityTargets.Length; i++)
                {
                    CollectibleObject target = Resolve(capi.World, DensityTargets[i]);
                    string name = target == null ? DensityTargets[i] : target.GetHeldItemName(new ItemStack(target));
                    modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "density" + i), Name = name }.WithLetterIcon(capi, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1));
                }
                modes[DensityTargets.Length] = new SkillItem { Code = new AssetLocation("vintagee", "densityfilter"), Name = Lang.Get("vintagee:density-filter") }.WithLetterIcon(capi, "…");
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (modes != null) foreach (SkillItem m in modes) m?.Dispose();
            if (arcanaOn != null) foreach (SkillItem m in arcanaOn) m?.Dispose();
            if (arcanaOff != null) foreach (SkillItem m in arcanaOff) m?.Dispose();
            if (galeOn != null) foreach (SkillItem m in galeOn) m?.Dispose();
            if (galeOff != null) foreach (SkillItem m in galeOff) m?.Dispose();
            base.OnUnloaded(api);
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string code)
        {
            var loc = new AssetLocation(code);
            return (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            if (galeOn != null)
            {
                // Flight/shield choice, then the speed; the picked one of each is drawn bright.
                int mode = GameMath.Clamp(GetMode(slot?.Itemstack), 0, GaleModes.Length - 1);
                int speed = GetSpeedLevel(slot?.Itemstack);
                var gl = new SkillItem[galeOn.Length];
                for (int i = 0; i < gl.Length; i++)
                {
                    bool picked = i < GaleModes.Length ? i == mode : i - GaleModes.Length == speed;
                    gl[i] = picked ? galeOn[i] : galeOff[i];
                }
                return gl;
            }
            if (arcanaOn == null) return modes;
            // One entry per effect, showing whether it is on; picking one switches it.
            var list = new SkillItem[ArcanaModes.Length];
            for (int i = 0; i < list.Length; i++) list[i] = ArcanaEnabled(slot?.Itemstack, i) ? arcanaOn[i] : arcanaOff[i];
            return list;
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => GetMode(slot?.Itemstack);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            if (Kind == "ringofarcana" && toolMode >= 0 && toolMode < ArcanaModes.Length)
            {
                int off = slot.Itemstack.Attributes.GetInt(ArcanaOffKey) ^ (1 << toolMode);
                if (off == 0) slot.Itemstack.Attributes.RemoveAttribute(ArcanaOffKey);
                else slot.Itemstack.Attributes.SetInt(ArcanaOffKey, off);
            }
            if (UsesDensityModes && toolMode >= DensityTargets.Length)
            {
                // "Filter...": the window, not a mode. The pick reaches both sides; only the client opens it.
                if (api is ICoreClientAPI capi) VintageEModSystem.Of(capi)?.OpenGemFilterDialog();
                return;
            }
            if (Kind == "swiftwolfsrendinggale" && toolMode >= GaleModes.Length)
            {
                slot.Itemstack.Attributes.SetInt(SpeedKey, GameMath.Clamp(toolMode - GaleModes.Length, 0, GaleSpeeds.Length - 1));
                slot.MarkDirty();
                return;
            }
            slot.Itemstack.Attributes.SetInt(ModeKey, toolMode);
            slot.MarkDirty();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            IWorldAccessor world = byEntity.World;
            // Placing a trinket on a pedestal is the pedestal's job.
            if (blockSel != null && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEPedestal)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent) return;
            // Gem of Eternal Density: sneak + right-click opens its filter window (the same as "Filter..." in its tool modes).
            if (Kind == "gemofeternaldensity" && byEntity.Controls.Sneak)
            {
                if (world.Side == EnumAppSide.Client) VintageEModSystem.Of(world.Api)?.OpenGemFilterDialog();
                return;
            }
            if (world.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer player) || slot.Itemstack == null) return;
            TrinketEffects effects = Effects(world);
            if (effects == null) return;

            if (IsAmulet)
            {
                if (blockSel != null) effects.PlaceLiquid(this, player, blockSel);
                return;
            }
            if (IsAlwaysOn) return;

            if (Kind == "voidring" && byEntity.Controls.Sneak)
            {
                effects.Teleport(player, TeleportRange);
                return;
            }
            if (Kind == "zeroring" && byEntity.Controls.Sneak)
            {
                effects.FreezeBurst(player, false);
                return;
            }
            if (Kind == "ringofarcana" && byEntity.Controls.Sneak)
            {
                // Its Zero Ring and Harvest Goddess bursts at once, whichever are switched on; both free, as the ring is.
                if (ArcanaEnabled(slot.Itemstack, 0)) effects.FreezeBurst(player, true);
                if (ArcanaEnabled(slot.Itemstack, 2)) effects.HarvestBurst(player, true);
                return;
            }
            if ((Kind == "blackholeband" || Kind == "voidring") && blockSel != null && effects.RemoveLiquidSource(player, blockSel.Position)) return;
            if (Kind == "harvestgoddessband" && byEntity.Controls.Sneak) { effects.HarvestBurst(player, false); return; }
            if (Kind == "harvestgoddessband" && blockSel != null && effects.PlantSeeds(player, blockSel, PlantRadius) > 0) return;

            bool on = !IsActive(slot.Itemstack);
            slot.Itemstack.Attributes.SetBool(ActiveKey, on);
            slot.MarkDirty();
            // ProjectE: switching on plays its heal sound, switching off its uncharge sound.
            PESounds.Play(world, on ? PESounds.Heal : PESounds.Uncharge, on ? "game:sounds/effect/latch" : "game:sounds/effect/woodswitch", byEntity, 8, 0.7f);
        }

        VintageEModSystem watchMod;

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            // With ProjectE's textures: its "on" look while switched on, and the Arcana/Swiftwolf mode looks.
            AssetLocation look = ProjectELook.TrinketLook(this, itemstack);
            if (look != null) ProjectELook.UseSprite(capi, look, ref renderinfo);
            // The watch glows (a pulsing tint in this computer's chosen colour) while it's on: its sound is easy to miss.
            if (Kind == "watchofflowingtime" && IsActive(itemstack))
            {
                LoadedTexture glow = (watchMod ??= VintageEModSystem.Of(capi))?.WatchGlowTexture();
                if (glow != null && glow.TextureId != 0)
                {
                    renderinfo.OverlayTexture = glow;
                    renderinfo.OverlayOpacity = 0.35f + 0.2f * (float)Math.Sin(capi.ElapsedMilliseconds / 300.0);
                }
            }
        }

        // ------------------------------------------------------------------ fire-projectile key

        public bool CanShoot(ItemSlot slot)
        {
            if (IsAmulet || Kind == "ignitionring" || Kind == "swiftwolfsrendinggale") return true;
            return Kind == "ringofarcana" && (ArcanaEnabled(slot?.Itemstack, 1) || ArcanaEnabled(slot?.Itemstack, 3));
        }

        /// <summary>What the fire-projectile key does: the Arcana shoots fire when its Ignition effect is on, else lightning.</summary>
        string ShotKind(ItemSlot slot) => Kind == "ringofarcana" ? (ArcanaEnabled(slot?.Itemstack, 1) ? "ignitionring" : "swiftwolfsrendinggale") : Kind;

        public bool Shoot(IServerPlayer player, ItemSlot slot)
        {
            if (!CanShoot(slot)) return false;
            IWorldAccessor world = player.Entity.World;
            switch (Kind)
            {
                case "evertideamulet":
                    if (EntityVintageEProjectile.Shoot(player, "water", 30) == null) return false;
                    PESounds.Play(world, PESounds.WaterMagic, "game:sounds/environment/smallsplash", player.Entity, 16, 0.8f);
                    return true;
                case "volcaniteamulet":
                    if (!Effects(world).Pay(player, LavaCost)) return false;
                    if (EntityVintageEProjectile.Shoot(player, "lava", 30) == null) return false;
                    PESounds.Play(world, PESounds.Transmute, "game:sounds/effect/extinguish", player.Entity, 16, 0.5f);
                    return true;
                case "swiftwolfsrendinggale":
                    return Effects(world).Lightning(player, false);
                case "ringofarcana" when ShotKind(slot) == "swiftwolfsrendinggale":
                    return Effects(world).Lightning(player, true);
                default: // ignition ring, ring of arcana
                    if (EntityVintageEProjectile.Shoot(player, "fire", 30, shot => shot.Damage = 3) == null) return false;
                    PESounds.Play(world, PESounds.Power, "game:sounds/effect/swoosh", player.Entity, 16, 0.8f);
                    return true;
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;
            if (IsToggleable) dsc.AppendLine(Lang.Get(IsActive(stack) ? "vintagee:trinket-on" : "vintagee:trinket-off"));
            VintageEModSystem.AppendServerScaling(world, dsc, costs: Kind != "ringofarcana" && Kind != "repairtalisman", watch: Kind == "watchofflowingtime");
            if (IsAmulet || Kind == "ignitionring" || Kind == "ringofarcana" || Kind == "swiftwolfsrendinggale") VintageEModSystem.AppendFireKey(world, dsc);
            if (Kind == "ringofarcana")
            {
                var on = new List<string>();
                for (int i = 0; i < ArcanaModes.Length; i++) if (ArcanaEnabled(stack, i)) on.Add(Lang.Get("vintagee:arcana-" + ArcanaModes[i]));
                dsc.AppendLine(on.Count == 0 ? Lang.Get("vintagee:arcana-none") : Lang.Get("vintagee:arcana-effects", string.Join(", ", on)));
            }
            else if (Kind == "swiftwolfsrendinggale")
            {
                dsc.AppendLine(Lang.Get("vintagee:trinket-mode", Lang.Get("vintagee:gale-" + GaleModes[GameMath.Clamp(GetMode(stack), 0, GaleModes.Length - 1)])));
                dsc.AppendLine(Lang.Get("vintagee:gale-speed-current", GaleSpeeds[GetSpeedLevel(stack)].ToString("0.#")));
            }
            else if (Kind == "watchofflowingtime")
            {
                dsc.AppendLine(Lang.Get("vintagee:trinket-mode", Lang.Get("vintagee:watch-" + WatchModes[GameMath.Clamp(GetMode(stack), 0, WatchModes.Length - 1)])));
            }
            else if (UsesFoodModes)
            {
                dsc.AppendLine(Lang.Get("vintagee:trinket-mode", Lang.Get("vintagee:food-" + FoodModes[GameMath.Clamp(GetMode(stack), 0, FoodModes.Length - 1)])));
            }
            if (UsesDensityModes)
            {
                CollectibleObject target = Resolve(world, DensityTargets[GameMath.Clamp(GetMode(stack), 0, DensityTargets.Length - 1)]);
                if (target != null) dsc.AppendLine(Lang.Get("vintagee:density-target", target.GetHeldItemName(new ItemStack(target)), EmcFormat.Format(StoredEmc.Get(stack))));
                string[] filter = TabletMagnet.GetFilter(stack);
                int listed = filter.Count(f => f != null);
                dsc.AppendLine(Lang.Get(GemFilter.Only(stack) ? "vintagee:density-filter-only" : listed == 0 ? "vintagee:density-filter-none" : "vintagee:density-filter-except", listed));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            var list = new List<WorldInteraction>();
            if (IsAmulet) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-place", MouseButton = EnumMouseButton.Right });
            if (IsToggleable) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-toggle", MouseButton = EnumMouseButton.Right });
            if (Kind == "blackholeband" || Kind == "voidring") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-voidliquid", MouseButton = EnumMouseButton.Right });
            if (Kind == "harvestgoddessband") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-plant", MouseButton = EnumMouseButton.Right });
            if (Kind == "voidring") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-teleport", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (Kind == "gemofeternaldensity") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-gemfilter", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (Kind == "zeroring" || Kind == "ringofarcana") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-freezeburst", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (Kind == "harvestgoddessband" || Kind == "ringofarcana") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-harvestburst", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (CanShoot(inSlot)) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-shoot-" + ShotKind(inSlot), HotKeyCode = VintageEModSystem.FireHotkey, MouseButton = EnumMouseButton.None });
            if (modes != null || arcanaOn != null || galeOn != null) list.Add(new WorldInteraction { ActionLangCode = arcanaOn != null ? "vintagee:heldhelp-arcana-toggle" : "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}
