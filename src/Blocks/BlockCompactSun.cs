using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// The Compact Sun (1.29.2, Project Expansion's): the power of the sun in a block. A Power Flower standing on it, or
    /// an Energy Collector under it, works <see cref="VintageEConfig.CompactSunBonus"/> times as fast (a collector as if
    /// in full sun: the sun is right above it). It gives off light and heat (the game's HeatSource behaviour, in its
    /// JSON), and whoever stands on it catches fire, as in Project Expansion where it burns players without boots.
    /// </summary>
    public class BlockCompactSun : Block
    {
        public static bool Below(IWorldAccessor world, BlockPos pos) => pos != null && world?.BlockAccessor.GetBlock(pos.DownCopy()) is BlockCompactSun;
        public static bool Above(IWorldAccessor world, BlockPos pos) => pos != null && world?.BlockAccessor.GetBlock(pos.UpCopy()) is BlockCompactSun;

        /// <summary>The multiplier, at least 1 (the setting, or 10 without one).</summary>
        public static int Bonus(ICoreAPI api) => Math.Max(1, VintageEModSystem.Of(api)?.Config?.CompactSunBonus ?? 10);

        public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed, bool isImpact)
        {
            base.OnEntityCollide(world, entity, pos, facing, collideSpeed, isImpact);
            if (world.Side == EnumAppSide.Server && facing == BlockFacing.UP && entity is EntityAgent && entity.Alive && !entity.IsOnFire) entity.Ignite();
        }
    }
}
