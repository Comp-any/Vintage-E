using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// What the game's crafting grid makes of what is in it, asked without a grid or a player: Emc Craft asks before
    /// it fills its grid (EmcCraft.LayOut, 1.26.6), and so do the tests, whose server has no player (the game's own
    /// search, InventoryCraftingGrid.FindMatchingRecipe, read from 1.22.7, asks which player owns the grid while it
    /// matches). The steps are the game's:
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
        // The game's two matching methods aren't public. Bound once as delegates: asked through reflection each time,
        // every question made an argument list (a grid with a saw in it has hundreds of candidate recipes).
        static readonly System.Func<RecipeBase, int, int, ItemSlot[], int, int, IRecipeIngredient[], bool> AtPosition =
            Bind<System.Func<RecipeBase, int, int, ItemSlot[], int, int, IRecipeIngredient[], bool>>("MatchesAtPosition",
                typeof(int), typeof(int), typeof(ItemSlot[]), typeof(int), typeof(int), typeof(IRecipeIngredient[]));
        static readonly System.Func<RecipeBase, ItemSlot[], IWorldAccessor, IRecipeIngredient[], bool> ShapeLess =
            Bind<System.Func<RecipeBase, ItemSlot[], IWorldAccessor, IRecipeIngredient[], bool>>("MatchesShapeLess",
                typeof(ItemSlot[]), typeof(IWorldAccessor), typeof(IRecipeIngredient[]));

        static T Bind<T>(string name, params Type[] parameters) where T : Delegate
        {
            try
            {
                MethodInfo method = typeof(RecipeBase).GetMethod(name, Hidden, null, parameters, null);
                return method == null ? null : (T)method.CreateDelegate(typeof(T));
            }
            catch { return null; }
        }

        /// <summary>Whether the game's two matching methods were found (they aren't public).</summary>
        public static bool Ready => AtPosition != null && ShapeLess != null;

        /// <summary>Whether the grid can be asked on this side: the methods were found and the game's recipe index has something in it.</summary>
        public static bool Usable(IWorldAccessor world)
        {
            if (!Ready || world == null) return false;
            try { return world.FastSearchRecipesByIngredient?.Count > 0; }
            catch { return false; }
        }

        /// <summary>The recipe the game's grid would pick for these cells (row by row, <paramref name="gridWidth"/> to a row), or null.</summary>
        public static GridRecipe Find(IWorldAccessor world, ItemStack[] cells, int gridWidth = 3)
        {
            if (!Ready || world == null || cells == null) return null;
            var slots = new ItemSlot[cells.Length];
            for (int i = 0; i < cells.Length; i++) slots[i] = new DummySlot(cells[i]);

            var candidates = new List<GridRecipe>();
            var seen = new HashSet<GridRecipe>();
            int filled = 0;
            foreach (ItemStack stack in cells)
            {
                if (stack == null || stack.StackSize == 0) continue;
                filled++;
                foreach (var pair in world.FastSearchRecipesByIngredient)
                {
                    if (!pair.Key.SatisfiesAsIngredient(stack, false)) continue;
                    foreach (IRecipeBase r in pair.Value)
                    {
                        if (r is GridRecipe g && g.Enabled && seen.Add(g)) candidates.Add(g);
                    }
                }
            }
            // A shaped recipe wants a stack in each of its cells and none anywhere else: one with another number of
            // cells can't match, and needn't be asked.
            foreach (GridRecipe g in candidates) if (!g.Shapeless && Ingredients(g) == filled && Matches(g, world, slots, gridWidth)) return g;
            foreach (GridRecipe g in candidates) if (g.Shapeless && Matches(g, world, slots, gridWidth)) return g;
            return null;
        }

        static int Ingredients(GridRecipe recipe)
        {
            CraftingRecipeIngredient[] all = recipe.ResolvedIngredients;
            if (all == null) return -1;
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != null) n++;
            return n;
        }

        /// <summary>GridRecipe.Matches without its questions to the player.</summary>
        static bool Matches(GridRecipe recipe, IWorldAccessor world, ItemSlot[] slots, int gridWidth)
        {
            if (recipe.ResolvedIngredients == null) return false;
            int rows = slots.Length / gridWidth;
            if (gridWidth < recipe.Width || rows < recipe.Height) return false;
            if (recipe.Shapeless) return ShapeLess(recipe, slots, world, recipe.ResolvedIngredients);
            for (int col = 0; col <= gridWidth - recipe.Width; col++)
            {
                for (int row = 0; row <= rows - recipe.Height; row++)
                {
                    if (AtPosition(recipe, col, row, slots, gridWidth, recipe.Width, recipe.ResolvedIngredients)) return true;
                }
            }
            return false;
        }
    }
}
