using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// What the game's crafting grid makes of what is in it, asked without a player. The game's own search
    /// (InventoryCraftingGrid.FindMatchingRecipe, read from 1.22.7) asks which player owns the grid while it matches,
    /// and the test server has none. The steps are the game's:
    /// <list type="number">
    /// <item>the candidates: every recipe with an ingredient that one of the stacks in the grid satisfies (the quick search);</item>
    /// <item>the shaped recipes among them, in order: the first that matches wins;</item>
    /// <item>only then the shapeless ones.</item>
    /// </list>
    /// Left out: the "matches recipe" events other mods can refuse a recipe with, and traits.
    ///
    /// Worth knowing (1.22): a shapeless recipe first adds up equal stacks, so "saw + log" (boards) also matches a saw
    /// and two logs in two slots. A shaped recipe for those three things (the long bow stave: saw above two oak, maple
    /// or walnut logs, in one column) only wins when they lie exactly as it says; any other way round makes boards.
    /// </summary>
    public static class GridProbe
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly MethodInfo AtPosition = typeof(RecipeBase).GetMethod("MatchesAtPosition", Hidden, null,
            new[] { typeof(int), typeof(int), typeof(ItemSlot[]), typeof(int), typeof(int), typeof(IRecipeIngredient[]) }, null);
        static readonly MethodInfo ShapeLess = typeof(RecipeBase).GetMethod("MatchesShapeLess", Hidden, null,
            new[] { typeof(ItemSlot[]), typeof(IWorldAccessor), typeof(IRecipeIngredient[]) }, null);

        /// <summary>Whether the game's two matching methods were found (they aren't public).</summary>
        public static bool Ready => AtPosition != null && ShapeLess != null;

        /// <summary>The recipe the game's grid would pick for these cells (row by row, <paramref name="gridWidth"/> to a row), or null.</summary>
        public static GridRecipe Find(IWorldAccessor world, ItemStack[] cells, int gridWidth = 3)
        {
            if (!Ready || world == null || cells == null) return null;
            var slots = new ItemSlot[cells.Length];
            for (int i = 0; i < cells.Length; i++) slots[i] = new DummySlot(cells[i]);

            var candidates = new List<GridRecipe>();
            var seen = new HashSet<GridRecipe>();
            foreach (ItemStack stack in cells)
            {
                if (stack == null || stack.StackSize == 0) continue;
                foreach (var pair in world.FastSearchRecipesByIngredient)
                {
                    if (!pair.Key.SatisfiesAsIngredient(stack, false)) continue;
                    foreach (IRecipeBase r in pair.Value)
                    {
                        if (r is GridRecipe g && g.Enabled && seen.Add(g)) candidates.Add(g);
                    }
                }
            }
            foreach (GridRecipe g in candidates) if (!g.Shapeless && Matches(g, world, slots, gridWidth)) return g;
            foreach (GridRecipe g in candidates) if (g.Shapeless && Matches(g, world, slots, gridWidth)) return g;
            return null;
        }

        /// <summary>GridRecipe.Matches without its questions to the player.</summary>
        static bool Matches(GridRecipe recipe, IWorldAccessor world, ItemSlot[] slots, int gridWidth)
        {
            if (recipe.ResolvedIngredients == null) return false;
            int rows = slots.Length / gridWidth;
            if (gridWidth < recipe.Width || rows < recipe.Height) return false;
            if (recipe.Shapeless) return (bool)ShapeLess.Invoke(recipe, new object[] { slots, world, recipe.ResolvedIngredients });
            for (int col = 0; col <= gridWidth - recipe.Width; col++)
            {
                for (int row = 0; row <= rows - recipe.Height; row++)
                {
                    if ((bool)AtPosition.Invoke(recipe, new object[] { col, row, slots, gridWidth, recipe.Width, recipe.ResolvedIngredients })) return true;
                }
            }
            return false;
        }
    }
}
