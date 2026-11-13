using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Right-click a block to transmute it (stone types, wood types, sand ↔ gravel...); sneak to go the other way.
    /// As ProjectE's: a cube, a panel (the clicked face's plane) or a line (along where you face), 1 to 9 wide; the
    /// shape and size are picked in the tool modes, and the charge key (V, sneak + V) changes the size. Only blocks
    /// like the clicked one change. Also used as a non-consumed catalyst in crafting recipes.
    /// </summary>
    public class ItemPhilosophersStone : Item, IChargeableItem, IExtraFunctionItem
    {
        public const int MaxRadius = 4;
        public const string ShapeKey = "vintageePhiloShape";
        public static readonly string[] Shapes = { "cube", "panel", "line" };
        /// <summary>A stone that never had a shape picked works on a panel, as all stones did before 1.24.0.</summary>
        public const int DefaultShape = 1;

        readonly ModeGroups groups = new ModeGroups(Shapes.Length, MaxRadius + 1);

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api is ICoreClientAPI capi)
            {
                groups.Load(capi, "philo", (g, i) => g == 0
                    ? (Lang.Get("vintagee:philo-shape-" + Shapes[i]), Lang.Get("vintagee:philo-shape-" + Shapes[i]).Substring(0, 1))
                    : (Lang.Get("vintagee:toolmode-area", 2 * i + 1, 2 * i + 1), (2 * i + 1).ToString()));
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            groups.Dispose();
            base.OnUnloaded(api);
        }

        public static int ShapeOf(ItemStack stack) => GameMathClamp(stack?.Attributes?.GetInt(ShapeKey, DefaultShape) ?? DefaultShape, 0, Shapes.Length - 1);

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => groups.Show(ShapeOf(slot?.Itemstack), GetCharge(slot));
        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => ShapeOf(slot?.Itemstack);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            var (g, i) = groups.Split(toolMode);
            if (g == 0) { slot.Itemstack.Attributes.SetInt(ShapeKey, i); slot.MarkDirty(); }
            else if (g == 1) AreaToolModes.SetRadius(slot, i);
        }

        public int GetCharge(ItemSlot slot) => GameMathClamp(AreaToolModes.GetRadius(slot), 0, MaxRadius);
        public int MaxCharge(ItemSlot slot) => MaxRadius;
        public void SetCharge(ItemSlot slot, int charge) => AreaToolModes.SetRadius(slot, GameMathClamp(charge, 0, MaxRadius));

        /// <summary>ProjectE's extra-function key opens the stone's crafting grid; that's done on the client (see VintageEModSystem).</summary>
        public bool DoExtraFunction(IServerPlayer player, ItemSlot slot) => false;

        /// <summary>
        /// The blocks a shape covers: ProjectE's cube (all around), panel (the clicked face's plane) or line (along the
        /// horizontal direction the player faces, through the clicked block).
        /// </summary>
        public static IEnumerable<BlockPos> Positions(BlockPos center, BlockFacing face, int shape, int radius, BlockFacing facing)
        {
            switch (shape)
            {
                case 0:
                    for (int dx = -radius; dx <= radius; dx++)
                        for (int dy = -radius; dy <= radius; dy++)
                            for (int dz = -radius; dz <= radius; dz++)
                                yield return center.AddCopy(dx, dy, dz);
                    break;
                case 2:
                    bool alongX = (facing ?? BlockFacing.NORTH).Axis == EnumAxis.X;
                    for (int d = -radius; d <= radius; d++) yield return alongX ? center.AddCopy(d, 0, 0) : center.AddCopy(0, 0, d);
                    break;
                default:
                    foreach (BlockPos p in AreaToolModes.Plane(center, face, radius)) yield return p;
                    break;
            }
        }

        /// <summary>The horizontal direction the player looks.</summary>
        public static BlockFacing Facing(EntityAgent byEntity)
        {
            if (byEntity == null) return BlockFacing.NORTH;
            Vec3d eye = byEntity.Pos.XYZ;
            Vec3d look = eye.AheadCopy(1, byEntity.Pos.Pitch, byEntity.Pos.Yaw).Sub(eye);
            if (System.Math.Abs(look.X) > System.Math.Abs(look.Z)) return look.X > 0 ? BlockFacing.EAST : BlockFacing.WEST;
            return look.Z > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null && firstEvent && byEntity is EntityPlayer)
            {
                // ProjectE: right-click in the air opens a crafting grid. Here that's the game's own, in the inventory window.
                handling = EnumHandHandling.PreventDefault;
                if (api is ICoreClientAPI capi) OpenCrafting(capi);
                return;
            }
            if (blockSel == null || !firstEvent || !(byEntity is EntityPlayer ep) || ep.Player == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (api.Side != EnumAppSide.Server) return;

            IPlayer player = ep.Player;
            var mod = VintageEModSystem.Of(api);
            int changed = Transmute(api.World, mod, player, blockSel, byEntity.Controls.Sneak, ShapeOf(slot.Itemstack), GetCharge(slot), Facing(byEntity));
            if (changed < 0)
            {
                (player as IServerPlayer)?.SendIngameError("vintagee-notransmutable", Lang.Get("vintagee:error-notransmutable"));
                return;
            }
            if (changed > 0)
            {
                PESounds.Play(api.World, PESounds.Transmute, "game:sounds/effect/timeswitch", blockSel.Position, 16, 0.6f);
            }
        }

        /// <summary>Transmutes the blocks like the clicked one in the shape. Returns how many changed, or -1 if the clicked block has no transmutation.</summary>
        public static int Transmute(IWorldAccessor world, VintageEModSystem mod, IPlayer player, BlockSelection sel, bool reverse, int shape, int radius, BlockFacing facing)
        {
            IBlockAccessor ba = world.BlockAccessor;
            Block clicked = ba.GetBlock(sel.Position);
            Block target = mod?.WorldTransmutation?.GetTarget(clicked, reverse);
            if (target == null) return -1;
            int changed = 0;
            foreach (BlockPos pos in Positions(sel.Position, sel.Face, shape, GameMathClamp(radius, 0, MaxRadius), facing))
            {
                if (ba.GetBlock(pos).Id != clicked.Id) continue;
                if (player != null && !world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                ba.SetBlock(target.Id, pos);
                changed++;
            }
            return changed;
        }

        public static void OpenCrafting(ICoreClientAPI capi)
        {
            try
            {
                GuiDialog inv = System.Linq.Enumerable.FirstOrDefault(capi.Gui.LoadedGuis, g => g.GetType().Name == "GuiDialogInventory");
                if (inv == null) return;
                if (!inv.IsOpened()) inv.TryOpen();
            }
            catch (System.Exception e) { capi.Logger.Warning("Vintage-E: could not open the crafting grid: {0}", e.Message); }
        }

        static int GameMathClamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            int size = 2 * GetCharge(inSlot) + 1;
            dsc.AppendLine(Lang.Get("vintagee:philo-modeline", Lang.Get("vintagee:philo-shape-" + Shapes[ShapeOf(inSlot?.Itemstack)]), size));
            VintageEModSystem.AppendChargeKey(world, dsc);
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-transmute", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-stone-craft", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-transmute-reverse", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-area", HotKeyCode = "toolmodeselect" },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-chargekey", HotKeyCode = VintageEModSystem.ChargeHotkey },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-stone-craft", HotKeyCode = VintageEModSystem.ExtraHotkey }
            };
        }
    }
}
