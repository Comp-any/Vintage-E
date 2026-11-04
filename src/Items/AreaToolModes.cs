using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>Shared "1x1 / 3x3 / 5x5" tool modes (picked with the tool-mode key, F by default), stored on the itemstack.</summary>
    public static class AreaToolModes
    {
        public const string AttributeKey = "vintageeArea";

        /// <summary>ProjectE's red matter "shot" modes, after the squares: 3 tall, 3 wide, 3 long (into the block).</summary>
        public static readonly string[] Shots = { "tall", "wide", "long" };

        /// <param name="shots">Add the tall/wide/long modes after the squares (red matter tools).</param>
        public static SkillItem[] Create(ICoreClientAPI capi, int maxRadius, bool shots = false)
        {
            var modes = new SkillItem[maxRadius + 1 + (shots ? Shots.Length : 0)];
            for (int r = 0; r <= maxRadius; r++)
            {
                int size = r * 2 + 1;
                int cells = size;
                modes[r] = new SkillItem
                {
                    Code = new AssetLocation("vintagee", "area" + size),
                    Name = Lang.Get("vintagee:toolmode-area", size, size)
                }.WithIcon(capi, (ctx, x, y, w, h, rgba) => DrawGrid(ctx, x, y, w, h, rgba, cells));
            }
            if (shots)
            {
                for (int i = 0; i < Shots.Length; i++)
                {
                    string shot = Shots[i];
                    modes[maxRadius + 1 + i] = new SkillItem
                    {
                        Code = new AssetLocation("vintagee", "shot" + shot),
                        Name = Lang.Get("vintagee:toolmode-" + shot)
                    }.WithIcon(capi, (ctx, x, y, w, h, rgba) => DrawGrid(ctx, x, y, w, h, rgba, 3, shot));
                }
            }
            return modes;
        }

        /// <summary>Draws a cells×cells grid; with a <paramref name="shot"/>, only its column, row or (for "long") a diagonal of 3.</summary>
        static void DrawGrid(Context ctx, int x, int y, float w, float h, double[] rgba, int cells, string shot = null)
        {
            double size = Math.Min(w, h);
            double gap = size * 0.06;
            double cell = (size - gap * (cells + 1)) / cells;
            ctx.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
            for (int i = 0; i < cells; i++)
            {
                for (int j = 0; j < cells; j++)
                {
                    bool draw = shot == null || (shot == "tall" ? i == cells / 2 : shot == "wide" ? j == cells / 2 : i == j);
                    if (draw) ctx.Rectangle(x + gap + i * (cell + gap), y + gap + j * (cell + gap), cell, cell);
                }
            }
            ctx.Fill();
        }

        /// <summary>
        /// The blocks a tool in <paramref name="mode"/> works on: a square of radius <paramref name="mode"/> in the face's
        /// plane, or - past <paramref name="maxRadius"/> - a line of 3 standing up, lying across, or going into the block.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<BlockPos> Positions(BlockPos center, BlockFacing face, int mode, int maxRadius)
        {
            if (mode <= maxRadius) { foreach (BlockPos p in Plane(center, face, Math.Max(0, mode))) yield return p; yield break; }
            string shot = Shots[Math.Min(Shots.Length - 1, mode - maxRadius - 1)];
            EnumAxis axis = face?.Axis ?? EnumAxis.Y;
            for (int k = -1; k <= 1; k++)
            {
                BlockPos p = center.Copy();
                switch (shot)
                {
                    case "tall": // up and down; on a floor or ceiling, north-south
                        if (axis == EnumAxis.Y) p.Add(0, 0, k); else p.Add(0, k, 0);
                        break;
                    case "wide": // across the face
                        if (axis == EnumAxis.X) p.Add(0, 0, k); else p.Add(k, 0, 0);
                        break;
                    default: // long: into the block, away from the player
                        if (k < 0) continue;
                        BlockFacing into = face?.Opposite ?? BlockFacing.DOWN;
                        p.Add(into.Normali.X * k, into.Normali.Y * k, into.Normali.Z * k);
                        break;
                }
                yield return p;
            }
        }

        public static int GetRadius(ItemSlot slot, int defaultRadius = 0) => slot?.Itemstack?.Attributes?.GetInt(AttributeKey, defaultRadius) ?? defaultRadius;

        public static void SetRadius(ItemSlot slot, int radius)
        {
            if (slot?.Itemstack == null) return;
            slot.Itemstack.Attributes.SetInt(AttributeKey, Math.Max(0, radius));
            slot.MarkDirty();
        }

        /// <summary>Positions in the square of the given radius around <paramref name="center"/>, in the plane facing <paramref name="face"/>.</summary>
        public static System.Collections.Generic.IEnumerable<BlockPos> Plane(BlockPos center, BlockFacing face, int radius)
        {
            for (int a = -radius; a <= radius; a++)
            {
                for (int b = -radius; b <= radius; b++)
                {
                    BlockPos p = center.Copy();
                    switch (face?.Axis ?? EnumAxis.Y)
                    {
                        case EnumAxis.X: p.Add(0, a, b); break;
                        case EnumAxis.Z: p.Add(a, b, 0); break;
                        default: p.Add(a, 0, b); break;
                    }
                    yield return p;
                }
            }
        }
    }
}
