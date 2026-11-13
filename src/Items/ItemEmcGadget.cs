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
    ///   straight and explodes where it hits: radius 4/8/12/16 for 384/768/1,536/2,304 EMC (charge 0–3), as ProjectE.</item>
    /// <item>Catalytic Lens: right-click a block for the catalyst's tunnel, 1 then 16–64 deep (charge 0–7);
    ///   the fire-projectile key shoots the lens charge (charge 3 and up = the biggest shot).</item>
    /// <item>Mercurial Eye: sneak + right-click a block to remember it; then, by its mode, right-click to place it
    ///   (creation), to build a layer of it on the face of every like block joined to the clicked one (extension), to fill
    ///   the gaps in the clicked layer (classic extension), to turn blocks like the clicked one into it (transmutation) or
    ///   to build a 3×3 pillar of it out of the clicked face (pillar); 1×1 up to 9×9. You pay the difference in EMC (or get
    ///   it back if the old block was worth more).</item>
    /// <item>Archangel's Smite: right-click fires a homing arrow, left-click a volley of 10 (each costs what an arrow is
    ///   worth); switched on in its tool modes, it keeps firing from your inventory, five a second.</item>
    /// <item>Tome of Knowledge: learn every item that has an EMC value.</item>
    /// <item>Divining Rods: report the EMC of the ground below the block you click (16, 32 or 128 deep; the better rods
    ///   can look less deep too, picked in their tool modes).</item>
    /// </list>
    /// </summary>
    public class ItemEmcGadget : Item, IVintageEShooter, IChargeableItem
    {
        public const string EyeTargetKey = "vintageeEyeTarget";
        /// <summary>The Mercurial Eye's mode (index into <see cref="EyeModes"/>); its size is kept under the mode key.</summary>
        public const string EyeModeKey = "vintageeEyeMode";
        /// <summary>ProjectE's Mercurial Eye modes but classic transmutation. An eye made before 1.24.0 is in transmutation mode, its only one then.</summary>
        public static readonly string[] EyeModes = { "creation", "extension", "extensionclassic", "transmutation", "pillar" };
        public const int EyeTransmutation = 3;
        /// <summary>Archangel's Smite switched on: one arrow this often (seconds) from anywhere in your inventory.</summary>
        public const float SmiteAutoSeconds = 0.2f;
        SkillItem[] modes;
        ModeGroups eyeGroups, smiteGroups;

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

        VintageEModSystem Mod(IWorldAccessor world) => VintageEModSystem.Of(world.Api);

        /// <summary>Lens shot explosion radius and EMC cost by charge level (as ProjectE: power 4/8/12/16, 384–2,304 EMC).</summary>
        public static readonly float[] LensRadius = { 4, 8, 12, 16 };
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
            if (Kind == "mercurialeye") eyeGroups = new ModeGroups(EyeModes.Length, EyeMaxRadius + 1);
            if (Kind == "archangelssmite") smiteGroups = new ModeGroups(2);
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
                // Its mode, then its size (1x1 up to 9x9); the picked one of each is drawn bright.
                eyeGroups.Load(capi, "eye", (g, i) => g == 0
                    ? (Lang.Get("vintagee:eye-mode-" + EyeModes[i]), Lang.Get("vintagee:eye-mode-" + EyeModes[i]).Substring(0, 1))
                    : (Lang.Get("vintagee:toolmode-area", 2 * i + 1, 2 * i + 1), (2 * i + 1).ToString()));
            }
            else if (Kind == "archangelssmite")
            {
                smiteGroups.Load(capi, "smite", (g, i) => (Lang.Get(i == 0 ? "vintagee:smite-mode-off" : "vintagee:smite-mode-on"), i == 0 ? "0" : "1"));
            }
            else if (IsDiviningRod(this) && DiviningDepths(Kind).Length > 1)
            {
                int[] depths = DiviningDepths(Kind);
                modes = Enumerable.Range(0, depths.Length).Select(i => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "depth" + depths[i]),
                    Name = Lang.Get("vintagee:divining-depthmode", depths[i])
                }.WithLetterIcon(capi, depths[i].ToString())).ToArray();
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (modes != null) foreach (SkillItem m in modes) m?.Dispose();
            eyeGroups?.Dispose();
            smiteGroups?.Dispose();
            base.OnUnloaded(api);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            if (eyeGroups != null) return eyeGroups.Show(EyeMode(slot?.Itemstack), GameMath.Clamp(Charge(slot), 0, EyeMaxRadius));
            if (smiteGroups != null) return smiteGroups.Show(ItemEmcTrinket.IsActive(slot?.Itemstack) ? 1 : 0);
            return modes;
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
        {
            if (eyeGroups != null) return EyeMode(slot?.Itemstack);
            if (smiteGroups != null) return ItemEmcTrinket.IsActive(slot?.Itemstack) ? 1 : 0;
            return Charge(slot);
        }

        static int Charge(ItemSlot slot) => slot?.Itemstack?.Attributes?.GetInt(ItemEmcTrinket.ModeKey) ?? 0;

        /// <summary>The Mercurial Eye's mode: an eye that never had one set is in transmutation mode (all it did before 1.24.0).</summary>
        public static int EyeMode(ItemStack stack) => GameMath.Clamp(stack?.Attributes?.GetInt(EyeModeKey, EyeTransmutation) ?? EyeTransmutation, 0, EyeModes.Length - 1);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            if (eyeGroups != null)
            {
                var (g, i) = eyeGroups.Split(toolMode);
                if (g == 0) slot.Itemstack.Attributes.SetInt(EyeModeKey, i);
                else if (g == 1) slot.Itemstack.Attributes.SetInt(ItemEmcTrinket.ModeKey, i);
                slot.MarkDirty();
                return;
            }
            if (smiteGroups != null)
            {
                bool on = toolMode == 1;
                slot.Itemstack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, on);
                slot.MarkDirty();
                if (api?.Side == EnumAppSide.Server && byPlayer?.Entity != null) PESounds.Play(api.World, on ? PESounds.Heal : PESounds.Uncharge, on ? "game:sounds/effect/latch" : "game:sounds/effect/woodswitch", byPlayer?.Entity, 8, 0.7f);
                return;
            }
            slot.Itemstack.Attributes.SetInt(ItemEmcTrinket.ModeKey, toolMode);
            slot.MarkDirty();
        }

        // ------------------------------------------------------------------ charge key

        public int MaxCharge(ItemSlot slot)
        {
            switch (Kind)
            {
                case "hyperkineticlens": return LensRadius.Length - 1;
                case "catalyticlens": return CatalyticCharges - 1;
                case "destructioncatalyst": return CatalystDepth.Length - 1;
                case "mercurialeye": return EyeMaxRadius;
                default: return 0;
            }
        }

        public int GetCharge(ItemSlot slot) => GameMath.Clamp(Charge(slot), 0, MaxCharge(slot));

        public void SetCharge(ItemSlot slot, int charge)
        {
            if (slot?.Itemstack == null || MaxCharge(slot) <= 0) return;
            slot.Itemstack.Attributes.SetInt(ItemEmcTrinket.ModeKey, GameMath.Clamp(charge, 0, MaxCharge(slot)));
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
                    Eye(mod, player, slot, blockSel, Charge(slot));
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
                        Divine(mod, player, blockSel, DiviningDepth(Kind, slot.Itemstack));
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
            var mod = VintageEModSystem.Of(world.Api);
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

        /// <summary>The block the eye is set to, or null.</summary>
        public static Block EyeTarget(IWorldAccessor world, ItemStack eye)
        {
            string code = eye?.Attributes?.GetString(EyeTargetKey);
            Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
            return target == null || target.Id == 0 ? null : target;
        }

        /// <summary>
        /// The Mercurial Eye, by its mode (ProjectE's, without the classic transmutation):
        /// <list type="bullet">
        /// <item>creation: the target block against the clicked face (or in the clicked block if it's grass, snow or
        ///   another block you can build into; clicked in the air: 2 blocks ahead).</item>
        /// <item>extension: a layer on the clicked face of the clicked block and of every block like it joined to it in
        ///   that plane, up to the size's square in number. With no target set, the clicked block itself.</item>
        /// <item>classic extension: fills the gaps in the clicked block's layer, the size's square around it.</item>
        /// <item>transmutation: blocks like the clicked one in the size's square turn into the target.</item>
        /// <item>pillar: a 3×3 pillar out of the clicked face, 2 + 3 × the size's radius long, into the gaps only.</item>
        /// </list>
        /// Each block costs the target's EMC less the old block's (paid back when the old one was worth more); a block
        /// with no EMC value that is replaced drops as if broken. Blocks with a block entity are left alone.
        /// </summary>
        void Eye(VintageEModSystem mod, IServerPlayer player, ItemSlot slot, BlockSelection sel, int size)
        {
            IWorldAccessor world = player.Entity.World;
            IBlockAccessor ba = world.BlockAccessor;
            string lang = player.LanguageCode;
            Block clicked = sel == null ? null : ba.GetBlock(sel.Position);
            if (sel != null && player.Entity.Controls.Sneak)
            {
                if (clicked == null || clicked.Id == 0 || clicked.EntityClass != null || !mod.Emc.CanTransmute(clicked)) return;
                slot.Itemstack.Attributes.SetString(EyeTargetKey, clicked.Code.ToString());
                slot.MarkDirty();
                Tell(player, Lang.GetL(lang, "vintagee:eye-target", clicked.GetPlacedBlockName(world, sel.Position)));
                return;
            }
            int mode = EyeMode(slot.Itemstack);
            if (sel == null && mode != 0) return; // only creation works in the air
            Block target = EyeTarget(world, slot.Itemstack);
            // ProjectE: with no target set, extension builds out of the clicked block itself.
            if (target == null && (mode == 1 || mode == 2) && clicked != null && clicked.Id != 0 && clicked.EntityClass == null && mod.Emc.CanTransmute(clicked)) target = clicked;
            if (target == null) { Tell(player, Lang.GetL(lang, "vintagee:eye-notarget")); return; }
            if (mod.Emc.IsNoTransmute(target)) { Tell(player, Lang.GetL(lang, "vintagee:error-notransmute", target.GetHeldItemName(new ItemStack(target)))); return; }
            double targetValue = mod.Emc.GetValue(target);
            double mult = Math.Max(0, mod.Config.TrinketCostMultiplier);
            int radius = GameMath.Clamp(size, 0, EyeMaxRadius);
            bool outOfEmc = false;
            int placed = 0;

            static bool Empty(Block b) => b == null || b.Id == 0 || b.Replaceable >= 6000;

            // One block: pays the difference and swaps it in. emptyOnly: only into air, grass, snow and the like.
            bool Put(BlockPos pos, bool emptyOnly)
            {
                if (outOfEmc) return false;
                Block old = ba.GetBlock(pos);
                if (old.Id == target.Id || old.EntityClass != null) return false;
                bool empty = Empty(old);
                if (emptyOnly && !empty) return false;
                if (!empty && (mod.Emc.IsNoTransmute(old) || !Breakable(world, player, pos, old))) return false;
                if (empty && !world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return false;
                double oldValue = empty ? 0 : mod.Emc.GetValue(old);
                double diff = targetValue - oldValue;
                if (diff > 0 && !EmcWallet.TryPay(player, diff * mult))
                {
                    outOfEmc = true;
                    Tell(player, Lang.GetL(lang, "vintagee:error-notenoughemc", EmcFormat.Format(diff * mult), EmcFormat.Format(EmcWallet.Balance(player))));
                    return false;
                }
                if (diff < 0) PlayerEmc.AddEmc(player, -diff);
                if (!empty && oldValue <= 0) ba.BreakBlock(pos, player); // ProjectE drops what has no EMC value
                ba.SetBlock(target.BlockId, pos);
                ba.TriggerNeighbourBlockUpdate(pos);
                placed++;
                return true;
            }

            switch (mode)
            {
                case 0: // creation
                    if (sel == null)
                    {
                        EntityPlayer e = player.Entity;
                        Vec3d eye = e.Pos.XYZ.Add(0, e.LocalEyePos.Y, 0);
                        Put(eye.AheadCopy(2, e.Pos.Pitch, e.Pos.Yaw).AsBlockPos, true);
                    }
                    else if (Empty(clicked)) Put(sel.Position.Copy(), true);
                    else Put(sel.Position.AddCopy(sel.Face), true);
                    break;
                case 1: // extension: a flood over the like blocks in the clicked plane
                {
                    if (Empty(clicked)) break;
                    int side = 2 * radius + 1, max = side * side, tries = max * 4;
                    var queue = new Queue<BlockPos>();
                    var seen = new HashSet<BlockPos> { sel.Position.Copy() };
                    queue.Enqueue(sel.Position.Copy());
                    BlockFacing[] across = BlockFacing.ALLFACES.Where(f => f.Axis != sel.Face.Axis).ToArray();
                    for (int t = 0; t < tries && queue.Count > 0 && placed < max && !outOfEmc; t++)
                    {
                        BlockPos pos = queue.Dequeue();
                        if (ba.GetBlock(pos).Id != clicked.Id) continue;
                        if (!Put(pos.AddCopy(sel.Face), true)) continue;
                        foreach (BlockFacing f in across)
                        {
                            BlockPos next = pos.AddCopy(f);
                            if (seen.Add(next)) queue.Enqueue(next);
                        }
                    }
                    break;
                }
                case 2: // classic extension: the gaps in the clicked layer
                    foreach (BlockPos pos in AreaToolModes.Plane(sel.Position, sel.Face, radius)) Put(pos, true);
                    break;
                case 4: // pillar: 3x3, out of the clicked face
                    for (int d = 1; d <= 2 + 3 * radius && !outOfEmc; d++)
                    {
                        foreach (BlockPos pos in AreaToolModes.Plane(sel.Position.AddCopy(sel.Face, d), sel.Face, 1)) Put(pos, true);
                    }
                    break;
                default: // transmutation
                    foreach (BlockPos pos in AreaToolModes.Plane(sel.Position, sel.Face, radius))
                    {
                        if (radius > 0 && ba.GetBlock(pos).Id != clicked.Id) continue; // the bigger squares only swap blocks like the one clicked
                        Put(pos, false);
                    }
                    break;
            }
            if (placed > 0) PESounds.Play(world, PESounds.Power, "game:sounds/effect/swoosh", sel?.Position ?? player.Entity.Pos.AsBlockPos, 12, 0.5f);
        }

        /// <summary>The Mercurial Eye's biggest square: radius 4, 9x9.</summary>
        public const int EyeMaxRadius = 4;

        /// <summary>How deep each divining rod looks (3x3 columns): 16, 32 and 128 blocks.</summary>
        public static int DiviningDepth(string kind) => kind == "diviningrodlow" ? 16 : kind == "diviningrodmedium" ? 32 : 128;

        /// <summary>The depths a rod can pick from (ProjectE's better rods also do the lesser rods' depths).</summary>
        public static int[] DiviningDepths(string kind) => kind == "diviningrodlow" ? new[] { 16 } : kind == "diviningrodmedium" ? new[] { 16, 32 } : new[] { 16, 32, 128 };

        /// <summary>The depth this rod is set to look (its deepest unless another is picked in its tool modes).</summary>
        public static int DiviningDepth(string kind, ItemStack stack)
        {
            int[] depths = DiviningDepths(kind);
            int picked = stack?.Attributes?.HasAttribute(ItemEmcTrinket.ModeKey) == true ? stack.Attributes.GetInt(ItemEmcTrinket.ModeKey) : depths.Length - 1;
            return depths[GameMath.Clamp(picked, 0, depths.Length - 1)];
        }

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

        /// <summary>Archangel's Smite: homing arrows that steer towards hostile creatures. Returns how many were fired.</summary>
        public static int FireArrows(VintageEModSystem mod, IServerPlayer player, int count, float spread, bool quiet = false)
        {
            double cost = ArrowCost(mod, player.Entity.World) * mod.Config.TrinketCostMultiplier;
            int fired = 0;
            for (int i = 0; i < count; i++)
            {
                if (!EmcWallet.TryPay(player, cost))
                {
                    if (fired == 0 && !quiet) Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
                    break;
                }
                if (EntityVintageEProjectile.Shoot(player, "arrow", 50, shot => shot.Damage = ArrowDamage, spread) == null) break;
                fired++;
            }
            if (fired > 0) player.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/bow-release"), player.Entity, null, true, 24, quiet ? 0.5f : 1f);
            return fired;
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
            var kinds = new Dictionary<Block, (double value, int n)>();
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
                        kinds[block] = kinds.TryGetValue(block, out var k) ? (v, k.n + 1) : (v, 1);
                    }
                }
            }
            string bestName = bestBlock == null ? "-" : bestBlock.GetHeldItemName(new ItemStack(bestBlock));
            Tell(player, Lang.GetL(player.LanguageCode, "vintagee:divining-result", depth, EmcFormat.Format(total), EmcFormat.Format(count == 0 ? 0 : total / count), bestName, EmcFormat.Format(best)));
            // ProjectE's rods also name the best finds; how many is the server's DiviningRodListsTop (0: not at all).
            int top = mod.Config.DiviningRodListsTop;
            if (top > 0 && kinds.Count > 0)
            {
                var lines = new List<string>();
                foreach (var kv in kinds.OrderByDescending(kv => kv.Value.value).Take(top))
                {
                    lines.Add(Lang.GetL(player.LanguageCode, "vintagee:divining-top-line", kv.Key.GetHeldItemName(new ItemStack(kv.Key)), kv.Value.n, EmcFormat.Format(kv.Value.value)));
                }
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:divining-top", string.Join(", ", lines)));
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            VintageEModSystem.AppendServerScaling(world, dsc, costs: Kind != "tomeofknowledge" && !IsDiviningRod(this));
            if (IsLens) VintageEModSystem.AppendFireKey(world, dsc);
            if (Kind == "mercurialeye")
            {
                string code = inSlot?.Itemstack?.Attributes?.GetString(EyeTargetKey);
                Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
                dsc.AppendLine(target == null ? Lang.Get("vintagee:eye-notarget") : Lang.Get("vintagee:eye-current", target.GetHeldItemName(new ItemStack(target))));
                int size = 2 * GameMath.Clamp(Charge(inSlot), 0, EyeMaxRadius) + 1;
                dsc.AppendLine(Lang.Get("vintagee:eye-modeline", Lang.Get("vintagee:eye-mode-" + EyeModes[EyeMode(inSlot?.Itemstack)]), size));
            }
            if (Kind == "archangelssmite") dsc.AppendLine(Lang.Get(ItemEmcTrinket.IsActive(inSlot?.Itemstack) ? "vintagee:smite-on" : "vintagee:smite-off"));
            if (IsDiviningRod(this)) dsc.AppendLine(Lang.Get("vintagee:divining-depthline", DiviningDepth(Kind, inSlot?.Itemstack)));
            if (MaxCharge(inSlot) > 0) VintageEModSystem.AppendChargeKey(world, dsc);
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
            if (modes != null || eyeGroups != null || smiteGroups != null) list.Add(new WorldInteraction { ActionLangCode = IsLens || Kind == "destructioncatalyst" ? "vintagee:heldhelp-gadget-charge" : "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            if (MaxCharge(inSlot) > 0) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-chargekey", HotKeyCode = VintageEModSystem.ChargeHotkey, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}
