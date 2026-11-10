using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// The ProjectESounds setting: ProjectE's eight sounds (assets/vintagee/sounds/projecte, copied by
    /// tools/import_projecte_assets.py) for the things ProjectE plays them for. With it off, Vintage-E's own sound plays;
    /// where Vintage-E has none (stones healing, pedestals, area mining), nothing plays, as before.
    /// </summary>
    public static class PESounds
    {
        /// <summary>ProjectE's sound events (its sounds.json).</summary>
        public const string Charge = "charge", Uncharge = "uncharge", Heal = "heal", Destruct = "destruct",
            Power = "power", Transmute = "transmute", WaterMagic = "watermagic", WindMagic = "windmagic";

        public static AssetLocation Location(string sound) => new AssetLocation(VintageEModSystem.ModId, "sounds/projecte/" + sound);

        /// <summary>Whether ProjectE's sounds are on (the server's setting; clients get it with the EMC table).</summary>
        public static bool On(IWorldAccessor world)
        {
            VintageEModSystem mod = VintageEModSystem.Of(world?.Api);
            if (mod == null) return false;
            return world.Side == EnumAppSide.Server ? mod.Config?.ProjectESounds == true : mod.Emc.ProjectESounds;
        }

        /// <summary>The sound to play: ProjectE's when it's on, otherwise <paramref name="ownSound"/> (null = none).</summary>
        static AssetLocation Pick(IWorldAccessor world, string sound, string ownSound, ref float volume)
        {
            if (sound != null && On(world))
            {
                volume = 1f; // ProjectE plays them all at full volume
                return Location(sound);
            }
            return ownSound == null ? null : new AssetLocation(ownSound);
        }

        /// <summary>Plays at an entity (a player using a ring, say), heard by everyone nearby.</summary>
        public static void Play(IWorldAccessor world, string sound, string ownSound, Entity at, float range = 16, float volume = 1f)
        {
            AssetLocation loc = Pick(world, sound, ownSound, ref volume);
            if (loc != null && at != null) world.PlaySoundAt(loc, at, null, true, range, volume);
        }

        /// <summary>Plays at a position (a block, the end of a tunnel).</summary>
        public static void Play(IWorldAccessor world, string sound, string ownSound, double x, double y, double z, float range = 16, float volume = 1f)
        {
            AssetLocation loc = Pick(world, sound, ownSound, ref volume);
            if (loc != null) world.PlaySoundAt(loc, x, y, z, null, true, range, volume);
        }

        /// <summary>Plays at the centre of a block.</summary>
        public static void Play(IWorldAccessor world, string sound, string ownSound, BlockPos pos, float range = 16, float volume = 1f)
        {
            if (pos != null) Play(world, sound, ownSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, range, volume);
        }
    }
}
