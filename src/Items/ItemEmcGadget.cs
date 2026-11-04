using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// EMC-powered gadgets, used with right-click and paid for from personal EMC, then carried Klein stars.
    /// Lenses and the catalyst follow ProjectE: their charge level is picked with the tool-mode key.
    /// <list type="bullet">
    /// <item>Destruction Catalyst: breaks a 3×3 tunnel into the block you click, 1/4/8/16 deep (charge 0–3), 8 EMC a block.
    ///   Everything it breaks drops where you clicked.</item>
    /// <item>Hyperkinetic Lens: right-click (or the fire-projectile key, R) shoots an explosive charge that flies
    ///   straight and explodes where it hits: radius 4/6/8/10 for 384/768/1,536/2,304 EMC (charge 0–3).</item>
    /// <item>Catalytic Lens: right-click a block for the catalyst's tunnel, 1 then 16–64 deep (charge 0–7);
    ///   the fire-projectile key shoots the lens charge (charge 3 and up = the biggest shot).</item>
    /// <item>Mercurial Eye: sneak + right-click a block to remember it, then right-click blocks to turn them into it
    ///   (1×1 or 3×3); you pay the difference in EMC (or get it back if the old block was worth more).</item>
    /// <item>Archangel's Smite: right-click fires a homing arrow, left-click a volley of 10 (each costs what an arrow is worth).</item>
    /// <item>Tome of Knowledge: learn every item that has an EMC value.</item>
    /// <item>Divining Rods: report the EMC of the ground below the block you click (3, 16 or 64 deep).</item>
    /// </list>
    /// </summary>
    public class ItemEmcGadget : Item, IVintageEShooter
    {
        public const string EyeTargetKey = "vintageeEyeTarget";
        SkillItem[] modes;

        public string Kind => Variant?["type"] ?? "";

        VintageEModSystem Mod(IWorldAccessor world) => world.Api.ModLoader.GetModSystem<VintageEModSystem>();

        /// <summary>Lens shot explosion radius and EMC cost by charge level (ProjectE: power 4/8/12/16, 384–2,304 EMC).</summary>
        public static readonly float[] LensRadius = { 4, 6, 8, 10 };
        public static readonly double[] LensCost = { 384, 768, 1536, 2304 };
        /// <summary>Destruction Catalyst tunnel depth by charge (ProjectE: 1, then 2^(1+charge)).</summary>
        public static readonly int[] CatalystDepth = { 1, 4, 8, 16 };
        public const int CatalyticCharges = 8;
        public const double DestroyCostPerBlock = 8;
        /// <summary>Catalytic Lens tunnel depth by charge (ProjectE: 1, then 8 + 8 × charge, up to 64).</summary>
        public static int CatalyticDepth(int charge) => charge <= 0 ? 1 : 8 + 8 * Math.Min(charge, CatalyticCharges - 1);
        public static int LensCharge(int charge) => GameMath.Clamp(charge, 0, LensRadius.Length - 1);

        public bool IsLens => Kind == "hyperkineticlens" || Kind == "catalyticlens";

        public bool CanShoot(ItemSlot slot) => IsLens;

        public bool Shoot(IServerPlayer player, ItemSlot slot) => FireLens(player, slot);

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (!(api is ICoreClientAPI capi)) return;
            if (Kind == "destructioncatalyst")
            {
                modes = Enumerable.Range(0, CatalystDepth.Length).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    Name = Lang.Get("vintagee:gadget-charge-catalyst", c, CatalystDepth[c], EmcFormat.Format(9 * CatalystDepth[c] * DestroyCostPerBlock))
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "hyperkineticlens")
            {
                modes = Enumerable.Range(0, LensRadius.Length).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    Name = Lang.Get("vintagee:gadget-charge-lens", c, LensRadius[c], EmcFormat.Format(LensCost[c]))
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "catalyticlens")
            {
                modes = Enumerable.Range(0, CatalyticCharges).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    // The tunnel's cost grows with the charge; the shot stops growing at charge 3 (the Hyperkinetic Lens's biggest).
                    Name = Lang.Get("vintagee:gadget-charge-catalytic", c, CatalyticDepth(c), EmcFormat.Format(9 * CatalyticDepth(c) * DestroyCostPerBlock),
                        LensRadius[LensCharge(c)], EmcFormat.Format(LensCost[LensCharge(c)]))
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "mercurialeye")
            {
                modes = AreaToolModes.Create(capi, EyeMaxRadius); // 1x1, 3x3, 5x5, 7x7, 9x9
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (modes != null) foreach (SkillItem m in modes) m?.Dispose();
            base.OnUnloaded(api);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => modes;

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => slot?.Itemstack?.Attributes?.GetInt(ItemEmcTrinket.ModeKey) ?? 0;

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            slot.Itemstack.Attributes.SetInt(ItemEmcTrinket.ModeKey, toolMode);
            slot.MarkDirty();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer player) || slot.Itemstack == null) return;
            var mod = Mod(byEntity.World);
            if (mod == null) return;
            double mult = mod.Config.TrinketCostMultiplier;
            int mode = GetToolMode(slot, player, blockSel);

            switch (Kind)
            {
                case "destructioncatalyst":
                    if (blockSel != null) Destroy(player, blockSel, CatalystDepth[GameMath.Clamp(mode, 0, CatalystDepth.Length - 1)], DestroyCostPerBlock * mult);
                    break;
                case "hyperkineticlens":
                    FireLens(player, slot);
                    break;
                case "catalyticlens":
                    if (blockSel != null) Destroy(player, blockSel, CatalyticDepth(mode), DestroyCostPerBlock * mult);
                    break;
                case "mercurialeye":
                    if (blockSel != null) Eye(mod, player, slot, blockSel, mode);
                    break;
                case "archangelssmite":
                    FireArrows(mod, player, 1, 0);
                    break;
                case "eternalglue":
                    if (blockSel != null) Glue(mod, player, blockSel);
                    break;
                case "tomeofknowledge":
                    // Creative only: learning everything at once is a creative tool. And only what's in the creative
                    // inventory, so technical blocks (and things that are only placed states) don't flood the table.
                    if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        Tell(player, Lang.GetL(player.LanguageCode, "vintagee:tome-creativeonly"));
                        break;
                    }
                    var keys = new System.Collections.Generic.List<string>();
                    foreach (CollectibleObject c in mod.Emc.All.Keys)
                    {
                        if (!CollectibleIndex.IsListed(c) || !mod.Emc.CanTransmute(c)) continue;
                        if (StackDataUtil.NeedsData(c, byEntity.World))
                        {
                            // Clutter, tapestries, typed chests: each creative version, never the typeless block.
                            foreach (ItemStack v in StackDataUtil.CreativeVersions(c, byEntity.World))
                            {
                                if (mod.Emc.GetValue(v) > 0) keys.Add(PlayerEmc.ExactKey(c, StackDataUtil.KeptData(v)));
                            }
                            continue;
                        }
                        keys.Add(PlayerEmc.KeyOf(c));
                    }
                    int n = PlayerEmc.LearnMany(player, keys);
                    Tell(player, Lang.GetL(player.LanguageCode, "vintagee:tome-learned", n));
                    break;
                case "diviningrodlow":
                case "diviningrodmedium":
                case "diviningrodhigh":
                    if (blockSel != null)
                    {
                        Divine(mod, player, blockSel, DiviningDepth(Kind));
                        DamageItem(byEntity.World, byEntity, slot); // wears out: as many uses as 10x its EMC value (see SetDiviningDurability)
                    }
                    break;
            }
        }

        /// <summary>One portion of pitch glue's worth of repair on a clutter block: the game's own repair step, paid in EMC.</summary>
        const float GlueRepairGain = 0.2f; // the game's repairGain of a glue portion

        /// <summary>What one use costs: a litre of pitch glue at this server's value (32 EMC by default).</summary>
        public static double GlueCost(VintageEModSystem mod)
        {
            CollectibleObject glue = mod.Sapi?.World.GetItem(new AssetLocation("game:glueportion-pitch-cold")) ?? mod.Sapi?.World.GetItem(new AssetLocation("game:glueportion-pitch-hot"));
            if (glue == null) return 32;
            double each = mod.Emc.GetValue(glue);
            double perLitre = EmcRegistry.ItemsPerLitre(glue);
            return each > 0 ? each * (perLitre > 0 ? perLitre : 100) : 32;
        }

        void Glue(VintageEModSystem mod, IServerPlayer player, BlockSelection blockSel)
        {
            IWorldAccessor world = player.Entity.World;
            string lang = player.LanguageCode;
            Block block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (block == null || !block.HasBehavior<BlockBehaviorReparable>()) { Tell(player, Lang.GetL(lang, "vintagee:glue-notclutter")); return; }
            if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return;
            var bec = block.GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position);
            if (bec == null) { Tell(player, Lang.GetL(lang, "vintagee:glue-notclutter")); return; }
            if (bec.Collected || bec.repairState >= 1f) { Tell(player, Lang.GetL(lang, "vintagee:glue-repaired-already")); return; }
            if (bec.reparability <= 1) { Tell(player, Lang.GetL(lang, "vintagee:glue-cannot")); return; }
            // The game's "clutterObtainable" setting decides whether repairing means anything.
            string rule = world.Config.GetString("clutterObtainable", "ifrepaired").ToLowerInvariant();
            if (rule == "yes" || rule == "no") { Tell(player, Lang.GetL(lang, rule == "yes" ? "vintagee:glue-alwaysobtain" : "vintagee:glue-neverobtain")); return; }

            double cost = GlueCost(mod) * mod.Config.TrinketCostMultiplier;
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative && !EmcWallet.TryPay(player, cost))
            {
                Tell(player, Lang.GetL(lang, "vintagee:glue-noemc", EmcFormat.Format(cost)));
                return;
            }
            // The same step the game takes for one portion of glue.
            bec.repairState = Math.Min(1f, bec.repairState + GlueRepairGain * 5f / (bec.reparability - 1));
            bec.Blockentity.MarkDirty(true);
            world.PlaySoundAt(new AssetLocation("game:sounds/player/gluerepair"), blockSel.Position, 0, player, true, 8f);
            int pct = GameMath.Clamp((int)(bec.repairState * 100.001f), 0, 100);
            Tell(player, Lang.GetL(lang, "vintagee:glue-repaired", Lang.GetMatchingL(lang, bec.GetFullCode()), pct, EmcFormat.Format(cost)));
        }

        public static void Tell(IServerPlayer player, string msg) => player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);

        static bool Breakable(IWorldAccessor world, IServerPlayer player, BlockPos pos, Block block)
        {
            if (block == null || block.Id == 0 || block.Replaceable >= 6000) return false;
            if (block.RequiredMiningTier > 6 || block.Code.Path.StartsWith("mantle")) return false;
            return world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak);
        }

        /// <summary>
        /// ProjectE's destruction: a 3×3 area <paramref name="depth"/> blocks deep into the clicked face, paying
        /// <paramref name="costEach"/> per block broken. The drops are gathered where you clicked.
        /// </summary>
        void Destroy(IServerPlayer player, BlockSelection sel, int depth, double costEach)
        {
            IWorldAccessor world = player.Entity.World;
            IBlockAccessor ba = world.BlockAccessor;
            BlockFacing into = sel.Face.Opposite;
            int broken = 0;
            bool stop = false;
            for (int d = 0; d < depth && !stop; d++)
            {
                BlockPos layer = sel.Position.AddCopy(into, d);
                foreach (BlockPos pos in AreaToolModes.Plane(layer, sel.Face, 1))
                {
                    Block block = ba.GetBlock(pos);
                    if (!Breakable(world, player, pos, block)) continue;
                    if (!EmcWallet.TryPay(player, costEach))
                    {
                        Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(costEach), EmcFormat.Format(EmcWallet.Balance(player))));
                        stop = true;
                        break;
                    }
                    ba.BreakBlock(pos, player);
                    broken++;
                }
            }
            if (broken == 0) return;
            PESounds.Play(world, PESounds.Destruct, "game:sounds/effect/rockslide", sel.Position, 16, 0.6f);

            // Pull what dropped along the tunnel back to where the player clicked.
            Vec3f n = sel.Face.Normalf;
            Vec3d target = sel.Position.ToVec3d().Add(0.5 + n.X * 0.7, 0.5 + n.Y * 0.7, 0.5 + n.Z * 0.7);
            BlockPos far = sel.Position.AddCopy(into, depth - 1);
            Vec3d mid = new Vec3d((sel.Position.X + far.X) / 2.0 + 0.5, (sel.Position.Y + far.Y) / 2.0 + 0.5, (sel.Position.Z + far.Z) / 2.0 + 0.5);
            float reach = depth / 2f + 2;
            world.Api.Event.RegisterCallback(_ =>
            {
                foreach (Entity e in world.GetEntitiesAround(mid, reach, reach, x => x is EntityItem && x.Alive))
                {
                    e.Pos.SetPos(target);
                    e.Pos.Motion.Set(0, 0, 0);
                }
            }, 100);
        }

        /// <summary>Hyperkinetic/Catalytic Lens: pays for and fires an explosive lens charge where the player looks.</summary>
        public static bool FireLens(IServerPlayer player, ItemSlot slot)
        {
            if (!(slot?.Itemstack?.Collectible is ItemEmcGadget gadget) || !gadget.IsLens) return false;
            IWorldAccessor world = player.Entity.World;
            var mod = world.Api.ModLoader.GetModSystem<VintageEModSystem>();
            int charge = LensCharge(slot.Itemstack.Attributes.GetInt(ItemEmcTrinket.ModeKey));
            double cost = LensCost[charge] * (mod?.Config.TrinketCostMultiplier ?? 1);
            if (!EmcWallet.TryPay(player, cost))
            {
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
                return false;
            }
            if (EntityVintageEProjectile.Shoot(player, "lens", 36, shot => shot.Radius = LensRadius[charge]) == null) return false;
            PESounds.Play(world, PESounds.Power, "game:sounds/effect/swoosh", player.Entity, 24, 1f);
            return true;
        }

        void Eye(VintageEModSystem mod, IServerPlayer player, ItemSlot slot, BlockSelection sel, int mode)
        {
            IWorldAccessor world = player.Entity.World;
            IBlockAccessor ba = world.BlockAccessor;
            Block clicked = ba.GetBlock(sel.Position);
            if (player.Entity.Controls.Sneak)
            {
                if (clicked == null || clicked.Id == 0 || clicked.EntityClass != null || !mod.Emc.CanTransmute(clicked)) return;
                slot.Itemstack.Attributes.SetString(EyeTargetKey, clicked.Code.ToString());
                slot.MarkDirty();
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:eye-target", clicked.GetPlacedBlockName(world, sel.Position)));
                return;
            }
            string code = slot.Itemstack.Attributes.GetString(EyeTargetKey);
            Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
            if (target == null || target.Id == 0) { Tell(player, Lang.GetL(player.LanguageCode, "vintagee:eye-notarget")); return; }
            if (mod.Emc.IsNoTransmute(target)) { Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notransmute", target.GetHeldItemName(new ItemStack(target)))); return; }
            double targetValue = mod.Emc.GetValue(target);

            int radius = GameMath.Clamp(mode, 0, EyeMaxRadius);
            foreach (BlockPos pos in AreaToolModes.Plane(sel.Position, sel.Face, radius))
            {
                Block old = ba.GetBlock(pos);
                if (old.Id == target.Id || old.EntityClass != null || mod.Emc.IsNoTransmute(old) || !Breakable(world, player, pos, old)) continue;
                if (radius > 0 && old.Id != clicked.Id) continue; // the bigger squares only swap blocks like the one clicked
                double diff = targetValue - mod.Emc.GetValue(old);
                if (diff > 0 && !EmcWallet.TryPay(player, diff * Math.Max(0, mod.Config.TrinketCostMultiplier))) break;
                if (diff < 0) PlayerEmc.AddEmc(player, -diff);
                ba.SetBlock(target.BlockId, pos);
                ba.TriggerNeighbourBlockUpdate(pos);
            }
            PESounds.Play(world, PESounds.Power, "game:sounds/effect/swoosh", sel.Position, 12, 0.5f);
        }

        /// <summary>The Mercurial Eye's biggest square: radius 4, 9x9.</summary>
        public const int EyeMaxRadius = 4;

        /// <summary>How deep each divining rod looks (3x3 columns): 16, 32 and 128 blocks.</summary>
        public static int DiviningDepth(string kind) => kind == "diviningrodlow" ? 16 : kind == "diviningrodmedium" ? 32 : 128;

        public static bool IsDiviningRod(CollectibleObject c) => c is ItemEmcGadget g && g.Kind.StartsWith("diviningrod");

        /// <summary>
        /// Divining rods last as many uses as 10x their EMC value (a 9 EMC rod: 90 uses). Set on both sides whenever the
        /// values are worked out or arrive, since the values come from the recipes in the game.
        /// </summary>
        public static void SetDiviningDurability(IWorldAccessor world, EmcRegistry emc)
        {
            foreach (Item item in world.Items)
            {
                if (!IsDiviningRod(item)) continue;
                double v = emc?.GetValue(item) ?? 0;
                item.Durability = v > 0 ? (int)Math.Min(int.MaxValue / 2, Math.Max(1, Math.Round(v * 10))) : 100;
            }
        }

        /// <summary>Archangel's Smite arrow damage and how many a left-click volley fires (ProjectE: 10).</summary>
        public const float ArrowDamage = 3;
        public const int VolleySize = 10;

        /// <summary>EMC per smite arrow: what an arrow is worth, like ProjectE (a flint arrow here; 14 if it has no value).</summary>
        public static double ArrowCost(VintageEModSystem mod, IWorldAccessor world)
        {
            Item arrow = world.GetItem(new AssetLocation("game:arrow-flint"));
            double v = arrow == null ? 0 : mod.Emc.GetValue(arrow);
            return v > 0 ? v : 14;
        }

        /// <summary>Archangel's Smite: homing arrows that steer towards hostile creatures.</summary>
        void FireArrows(VintageEModSystem mod, IServerPlayer player, int count, float spread)
        {
            double cost = ArrowCost(mod, player.Entity.World) * mod.Config.TrinketCostMultiplier;
            int fired = 0;
            for (int i = 0; i < count; i++)
            {
                if (!EmcWallet.TryPay(player, cost))
                {
                    if (fired == 0) Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
                    break;
                }
                if (EntityVintageEProjectile.Shoot(player, "arrow", 50, shot => shot.Damage = ArrowDamage, spread) == null) break;
                fired++;
            }
            if (fired > 0) player.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/bow-release"), player.Entity, null, true, 24, 1f);
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (Kind != "archangelssmite")
            {
                base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                return;
            }
            // ProjectE: left-click fires a volley.
            handling = EnumHandHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server || !((byEntity as EntityPlayer)?.Player is IServerPlayer player)) return;
            var mod = Mod(byEntity.World);
            if (mod != null) FireArrows(mod, player, VolleySize, 0.08f);
        }

        void Divine(VintageEModSystem mod, IServerPlayer player, BlockSelection sel, int depth)
        {
            IBlockAccessor ba = player.Entity.World.BlockAccessor;
            double total = 0, best = 0;
            int count = 0;
            Block bestBlock = null;
            for (int dy = 0; dy < depth; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        BlockPos pos = sel.Position.AddCopy(dx, -dy, dz);
                        if (pos.Y < 1) continue;
                        Block block = ba.GetBlock(pos);
                        if (block == null || block.Id == 0) continue;
                        double v = mod.Emc.GetValue(block);
                        if (v <= 0) continue;
                        total += v;
                        count++;
                        if (v > best) { best = v; bestBlock = block; }
                    }
                }
            }
            string bestName = bestBlock == null ? "-" : bestBlock.GetHeldItemName(new ItemStack(bestBlock));
            Tell(player, Lang.GetL(player.LanguageCode, "vintagee:divining-result", depth, EmcFormat.Format(total), EmcFormat.Format(count == 0 ? 0 : total / count), bestName, EmcFormat.Format(best)));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (IsLens) VintageEModSystem.AppendFireKey(world, dsc);
            if (Kind == "mercurialeye")
            {
                string code = inSlot?.Itemstack?.Attributes?.GetString(EyeTargetKey);
                Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
                dsc.AppendLine(target == null ? Lang.Get("vintagee:eye-notarget") : Lang.Get("vintagee:eye-current", target.GetHeldItemName(new ItemStack(target))));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            var list = new System.Collections.Generic.List<WorldInteraction>
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-" + Kind, MouseButton = EnumMouseButton.Right }
            };
            if (Kind == "mercurialeye") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-eyepick", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (Kind == "archangelssmite") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-volley", MouseButton = EnumMouseButton.Left });
            if (IsLens) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-fire", HotKeyCode = VintageEModSystem.FireHotkey, MouseButton = EnumMouseButton.None });
            if (modes != null) list.Add(new WorldInteraction { ActionLangCode = IsLens || Kind == "destructioncatalyst" ? "vintagee:heldhelp-gadget-charge" : "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}
