using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Two ways to make Vintage-E's progression items: the crafting grid (ProjectE's recipes) and the game's own
    /// systems ("realistic": heating in a firepit or forge, the quern, the anvil, the barrel), which cost less. The
    /// server's CraftingMethods setting picks Grid, Realistic or Both; the EMC mapping then values each item by the
    /// cheapest route that's switched on.
    ///
    /// Realistic routes (assets): smelting on alchemical coal → mobius → aeternalis → dark matter → red matter and on
    /// the temporal gear → philosopher's stone (combustibleProps, patches/realistic-crafting.json); quern grinding of
    /// iron ingots and rough diamonds into covalence dust; a barrel recipe for low covalence dust; a smithing recipe
    /// for the iron band. The grid recipes they replace are named "vintagee:gridroute-…".
    ///
    /// Switched off by changing the loaded definitions on each side (the server before the mapping; the player's game
    /// when it has its items and when it has the server's recipes), so a world needs a restart to change it.
    /// </summary>
    public static class CraftingRoutes
    {
        public const string WorldKey = "vintageeCraftingMethods";
        public const string Both = "Both", Grid = "Grid", Realistic = "Realistic";
        public const string GridRoutePrefix = "gridroute-";

        /// <summary>The game's or Vintage-E's items that smelt or grind into Vintage-E items on a realistic route.</summary>
        public static readonly string[] SmeltInputs =
        {
            "vintagee:alchemicalfuel-alchemicalcoal", "vintagee:alchemicalfuel-mobiusfuel", "vintagee:alchemicalfuel-aeternalisfuel",
            "vintagee:matter-dark", "game:gear-temporal",
        };
        public static readonly string[] GrindInputs = { "game:ingot-iron", "game:gem-diamond-rough" };

        public static string Normalize(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "grid" || m == "crafting" || m == "craftinggrid") return Grid;
            if (m == "realistic" || m == "game") return Realistic;
            return Both;
        }

        static bool Ours(AssetLocation code) => code?.Domain == "vintagee";

        /// <summary>The grid recipes that have a realistic route (switched off in Realistic).</summary>
        public static IEnumerable<GridRecipe> GridRoutes(IWorldAccessor world) =>
            (world.GridRecipes ?? new List<GridRecipe>()).Where(r => r?.Name != null && Ours(r.Name) && r.Name.Path.StartsWith(GridRoutePrefix, StringComparison.Ordinal));

        public static IEnumerable<SmithingRecipe> SmithingRoutes(ICoreAPI api) =>
            (api.GetSmithingRecipes() ?? new List<SmithingRecipe>()).Where(r => Ours(r?.Output?.Code));

        public static IEnumerable<BarrelRecipe> BarrelRoutes(ICoreAPI api) =>
            (api.GetBarrelRecipes() ?? new List<BarrelRecipe>()).Where(r => Ours(r?.Output?.Code));

        /// <summary>Switch the item properties (smelting, grinding) for this mode. Returns how many were switched off.</summary>
        public static int ApplyProperties(ICoreAPI api, string mode)
        {
            if (Normalize(mode) != Grid) return 0;
            int n = 0;
            foreach (string code in SmeltInputs)
            {
                CollectibleObject c = Find(api.World, code);
                CombustibleProperties cp = c?.CombustibleProps;
                if (cp?.SmeltedStack != null && Ours(cp.SmeltedStack.Code)) { cp.SmeltedStack = null; cp.MeltingPoint = 0; n++; }
            }
            foreach (string code in GrindInputs)
            {
                CollectibleObject c = Find(api.World, code);
                if (c?.GrindingProps?.GroundStack != null && Ours(c.GrindingProps.GroundStack.Code)) { c.GrindingProps = null; n++; }
            }
            return n;
        }

        /// <summary>Switch the recipes for this mode. Returns how many were switched off.</summary>
        public static int ApplyRecipes(ICoreAPI api, string mode)
        {
            mode = Normalize(mode);
            int n = 0;
            if (mode == Realistic)
            {
                foreach (GridRecipe r in GridRoutes(api.World)) if (r.Enabled) { r.Enabled = false; n++; }
            }
            else if (mode == Grid)
            {
                foreach (SmithingRecipe r in SmithingRoutes(api)) if (r.Enabled) { r.Enabled = false; n++; }
                foreach (BarrelRecipe r in BarrelRoutes(api)) if (r.Enabled) { r.Enabled = false; n++; }
            }
            return n;
        }

        static CollectibleObject Find(IWorldAccessor world, string code)
        {
            var loc = new AssetLocation(code);
            return (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
        }
    }
}
