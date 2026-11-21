using System;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// The matter claymores swing with Ancient Armory's animations (greatswing, twohandready, readyidle), which that mod
    /// adds to the player. A swing deals its damage when its animation reaches the damage frame, so with an animation
    /// missing (Ancient Armory's files didn't load, as happens with the fork's zip on Linux servers) a claymore would
    /// do nothing on a left-click. Each missing one is swapped here, at start-up on both sides, for the game's own:
    /// the falx swing, the normal ready pose, no idle pose.
    /// </summary>
    public static class ClaymoreAnimations
    {
        public const string Swing = "greatswing", Ready = "twohandready", Idle = "readyidle";
        public const string SwingFallback = "falx", ReadyFallback = "helditemready";

        /// <summary>Swaps the claymores' missing animations. Returns how many items were changed.</summary>
        public static int Apply(ICoreAPI api, ILogger log)
        {
            var claymores = api.World.Items.Where(i => i?.Code != null && i.Code.Domain == VintageEModSystem.ModId && i.Code.Path.StartsWith("matterclaymore-", StringComparison.Ordinal)).ToArray();
            if (claymores.Length == 0) return 0;
            EntityProperties player = api.World.GetEntityType(new AssetLocation("game:player"));
            var codes = player?.Client?.Animations?.Where(a => a?.Code != null).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
            System.Func<string, bool> has = code => codes != null && codes.Contains(code);
            int changed = 0;
            foreach (CollectibleObject c in claymores) if (Fix(c, has)) changed++;
            if (changed > 0) log?.Warning("Vintage-E: Ancient Armory's claymore animations aren't on the player ({0} not found); the matter claymores use the game's sword swing instead, so they still hit.",
                string.Join(", ", new[] { Swing, Ready, Idle }.Where(a => !has(a))));
            return changed;
        }

        /// <summary>Swaps the animations <paramref name="has"/> says are missing. Returns true if anything changed.</summary>
        public static bool Fix(CollectibleObject c, System.Func<string, bool> has)
        {
            bool changed = false;
            if (c.HeldTpHitAnimation == Swing && !has(Swing)) { c.HeldTpHitAnimation = SwingFallback; changed = true; }
            if (c.HeldRightReadyAnimation == Ready && !has(Ready)) { c.HeldRightReadyAnimation = ReadyFallback; changed = true; }
            if (c.HeldRightTpIdleAnimation == Idle && !has(Idle)) { c.HeldRightTpIdleAnimation = null; changed = true; }
            return changed;
        }
    }
}
