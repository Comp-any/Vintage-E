using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace VintageE
{
    /// <summary>
    /// The Arcane Tablet's crafting window (1.19.0): a 3x3 grid of the player's own, filled from EMC by "Emc Craft".
    ///
    /// The grid is the game's own InventoryCraftingGrid under another ID ("vintageeemccraft-&lt;player uid&gt;"),
    /// opened on both sides like a chest's inventory, so it crafts exactly as the inventory's grid does (tool wear,
    /// returned stacks, shift-click). It is not saved: it opens empty, and closing it (or leaving the game) empties it.
    /// Closing burns tools that Emc Craft made from EMC back into EMC (their remaining value, after wear) and puts
    /// everything else in the player's inventory, or at their feet when it's full.
    /// </summary>
    public class EmcCraft
    {
        public const string InventoryClass = "vintageeemccraft";

        /// <summary>Stack attribute on tools Emc Craft made from EMC: they are burned again when the window closes.</summary>
        public const string MadeFromEmc = "vintageeEmcMade";

        public const int GridSize = 3;

        readonly VintageEModSystem mod;
        readonly ICoreAPI api;
        readonly Dictionary<string, InventoryCraftingGrid> grids = new Dictionary<string, InventoryCraftingGrid>();

        public EmcCraft(VintageEModSystem mod, ICoreAPI api)
        {
            this.mod = mod;
            this.api = api;
        }

        /// <summary>The player's grid on this side (made on first use; one per player).</summary>
        public InventoryCraftingGrid GridOf(IPlayer player)
        {
            if (player == null) return null;
            if (!grids.TryGetValue(player.PlayerUID, out InventoryCraftingGrid grid))
            {
                grid = new InventoryCraftingGrid(InventoryClass, player.PlayerUID, api);
                grids[player.PlayerUID] = grid;
            }
            return grid;
        }

        public static bool CarriesArcaneTablet(IPlayer player) =>
            player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemArcaneTablet
            || EmcWallet.CarriedSlots(player).Any(s => s.Itemstack?.Collectible is ItemArcaneTablet);

        // ---------------------------------------------------------------- planning (server, also used by the self-test)

        /// <summary>What filling the grid for a recipe would take: a stack per cell, what it costs, what's missing.</summary>
        public class Plan
        {
            public ItemStack[] Cells = new ItemStack[GridSize * GridSize];
            /// <summary>Cells filled with a tool from the player's inventory: the slot it comes out of.</summary>
            public ItemSlot[] FromInventory = new ItemSlot[GridSize * GridSize];
            public double Cost;
            /// <summary>Ingredients nothing known can stand in for (their names).</summary>
            public List<string> Missing = new List<string>();
            /// <summary>Ingredients that are known but can't be made from EMC (no value, no-transmute).</summary>
            public List<string> NotMakeable = new List<string>();
        }

        /// <summary>
        /// Works out a fill for <paramref name="recipe"/>: each ingredient becomes the cheapest learned stack that
        /// satisfies it (any planks, any log...), in the recipe's layout. A tool ingredient is taken from
        /// <paramref name="carried"/> when one there fits, and made from EMC otherwise.
        /// </summary>
        public Plan MakePlan(GridRecipe recipe, ICollection<string> known, IEnumerable<ItemSlot> carried)
        {
            var plan = new Plan();
            if (recipe?.ResolvedIngredients == null) { plan.Missing.Add("?"); return plan; }
            var usedSlots = new HashSet<ItemSlot>();
            List<ItemSlot> carriedList = carried?.Where(s => s?.Itemstack != null).ToList() ?? new List<ItemSlot>();
            var cache = new Dictionary<CraftingRecipeIngredient, ItemStack>();
            List<ItemStack> knownStacks = null;

            for (int row = 0; row < recipe.Height; row++)
            {
                for (int col = 0; col < recipe.Width; col++)
                {
                    CraftingRecipeIngredient ing = recipe.ResolvedIngredients[row * recipe.Width + col];
                    if (ing == null) continue;
                    int cell = row * GridSize + col;

                    if (ing.IsTool)
                    {
                        ItemSlot own = carriedList.FirstOrDefault(s => !usedSlots.Contains(s) && ing.SatisfiesAsIngredient(s.Itemstack, false));
                        if (own != null)
                        {
                            usedSlots.Add(own);
                            plan.FromInventory[cell] = own;
                            plan.Cells[cell] = own.Itemstack.GetEmptyClone();
                            plan.Cells[cell].StackSize = 1;
                            continue;
                        }
                    }

                    if (!cache.TryGetValue(ing, out ItemStack pick))
                    {
                        knownStacks ??= KnownStacks(known);
                        pick = Cheapest(ing, knownStacks, out bool anyKnown);
                        cache[ing] = pick;
                        if (pick == null)
                        {
                            string name = IngredientName(ing);
                            if (anyKnown) { if (!plan.NotMakeable.Contains(name)) plan.NotMakeable.Add(name); }
                            else if (!plan.Missing.Contains(name)) plan.Missing.Add(name);
                            continue;
                        }
                    }
                    if (pick == null) continue;

                    ItemStack stack = pick.Clone();
                    stack.StackSize = Math.Max(1, ing.Quantity);
                    if (ing.IsTool) stack.Attributes.SetBool(MadeFromEmc, true);
                    plan.Cells[cell] = stack;
                    plan.Cost += mod.Emc.GetValue(pick) * stack.StackSize;
                }
            }
            return plan;
        }

        /// <summary>Every learned key as a stack that could be made (exact versions included), once.</summary>
        List<ItemStack> KnownStacks(ICollection<string> known)
        {
            var list = new List<ItemStack>();
            foreach (string key in known)
            {
                ItemStack s = TransmutationService.Prototype(mod, api.World, key);
                if (s?.Collectible != null) list.Add(s);
            }
            return list;
        }

        ItemStack Cheapest(CraftingRecipeIngredient ing, List<ItemStack> knownStacks, out bool anyKnown)
        {
            anyKnown = false;
            ItemStack best = null;
            double bestValue = double.MaxValue;
            foreach (ItemStack s in knownStacks)
            {
                ItemStack probe = s.Clone();
                probe.StackSize = Math.Max(1, ing.Quantity);
                if (!ing.SatisfiesAsIngredient(probe, false)) continue;
                anyKnown = true;
                if (mod.Emc.IsNoTransmute(s.Collectible)) continue;
                double v = mod.Emc.GetValue(s);
                if (v <= 0 || v >= bestValue) continue;
                best = s;
                bestValue = v;
            }
            return best;
        }

        string IngredientName(CraftingRecipeIngredient ing)
        {
            if (ing.ResolvedItemStack != null) return ing.ResolvedItemStack.GetName();
            return ing.Name ?? ing.Code?.ToString() ?? "?";
        }

        // ---------------------------------------------------------------- server

        public void OnPacket(IServerPlayer player, EmcCraftPacket packet)
        {
            if (player?.Entity == null || packet == null) return;
            switch (packet.Action)
            {
                case EmcCraftAction.Open: Open(player); break;
                case EmcCraftAction.Close: Close(player); break;
                case EmcCraftAction.Fill: Fill(player, packet); break;
            }
        }

        void Open(IServerPlayer player)
        {
            if (!CarriesArcaneTablet(player)) { mod.Transmutation.Fail(player, "emccraft-notablet"); return; }
            InventoryCraftingGrid grid = GridOf(player);
            if (!player.InventoryManager.HasInventory(grid)) player.InventoryManager.OpenInventory(grid);
        }

        /// <summary>Empties the grid (burning tools Emc Craft made), then closes it. Also run when the player leaves.</summary>
        public void Close(IServerPlayer player)
        {
            if (!grids.TryGetValue(player.PlayerUID, out InventoryCraftingGrid grid)) return;
            Empty(player, grid, burnMadeTools: true);
            if (player.InventoryManager.HasInventory(grid)) player.InventoryManager.CloseInventory(grid);
        }

        /// <summary>
        /// Takes everything out of the grid: tools made from EMC are burned for what they're still worth, the rest goes
        /// to the player (or the ground at their feet). Returns the EMC given back.
        /// </summary>
        public double Empty(IPlayer player, InventoryCraftingGrid grid, bool burnMadeTools)
        {
            double back = 0;
            for (int i = 0; i < GridSize * GridSize; i++)
            {
                ItemSlot slot = grid[i];
                if (slot?.Itemstack == null) continue;
                ItemStack stack = slot.TakeOutWhole();
                if (burnMadeTools && stack.Attributes.GetBool(MadeFromEmc))
                {
                    double v = mod.BurnValue(stack) * stack.StackSize;
                    if (v > 0) { back += v; continue; }
                    stack.Attributes.RemoveAttribute(MadeFromEmc);
                }
                Give(player, stack);
                slot.MarkDirty();
            }
            if (back > 0 && player != null) PlayerEmc.AddEmc(player, back);
            return back;
        }

        void Give(IPlayer player, ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;
            if (player?.InventoryManager != null && player.InventoryManager.TryGiveItemstack(stack, true) && stack.StackSize <= 0) return;
            if (stack.StackSize > 0 && player?.Entity != null) api.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
        }

        void Fill(IServerPlayer player, EmcCraftPacket packet)
        {
            if (!CarriesArcaneTablet(player)) { mod.Transmutation.Fail(player, "emccraft-notablet"); return; }
            GridRecipe recipe = FindRecipe(packet);
            if (recipe == null) { mod.Transmutation.Fail(player, "emccraft-norecipe", packet.RecipeName ?? "?"); return; }

            Plan plan = MakePlan(recipe, PlayerEmc.GetKnown(player), EmcWallet.CarriedSlots(player));
            if (plan.Missing.Count > 0) { mod.Transmutation.Fail(player, "emccraft-missing", string.Join(", ", plan.Missing)); return; }
            if (plan.NotMakeable.Count > 0) { mod.Transmutation.Fail(player, "emccraft-notmakeable", string.Join(", ", plan.NotMakeable)); return; }
            double emc = PlayerEmc.GetEmc(player);
            if (plan.Cost > emc + 1e-9) { mod.Transmutation.Fail(player, "notenoughemc", EmcFormat.Format(plan.Cost), EmcFormat.Format(emc)); return; }

            InventoryCraftingGrid grid = GridOf(player);
            if (!player.InventoryManager.HasInventory(grid)) player.InventoryManager.OpenInventory(grid);
            // Whatever was in the grid goes back first (tools made earlier are burned, as on closing).
            Empty(player, grid, burnMadeTools: true);

            PlayerEmc.AddEmc(player, -plan.Cost);
            for (int cell = 0; cell < plan.Cells.Length; cell++)
            {
                ItemStack stack = plan.Cells[cell];
                if (stack == null) continue;
                ItemSlot from = plan.FromInventory[cell];
                if (from != null)
                {
                    stack = from.TakeOut(1);
                    from.MarkDirty();
                    if (stack == null) continue;
                }
                grid[cell].Itemstack = stack;
                grid[cell].MarkDirty();
            }
            api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, new TransmuteResultPacket { Emc = PlayerEmc.GetEmc(player), HasEmc = true });
        }

        /// <summary>The recipe the client asked for: by its place in the list when that still matches, else by name and output.</summary>
        GridRecipe FindRecipe(EmcCraftPacket packet)
        {
            List<GridRecipe> all = api.World.GridRecipes;
            if (all == null) return null;
            bool Same(GridRecipe r) => r != null && (r.Name?.ToString() ?? "") == (packet.RecipeName ?? "")
                && (r.Output?.Code?.ToString() ?? "") == (packet.OutputCode ?? "");
            if (packet.RecipeIndex >= 0 && packet.RecipeIndex < all.Count && Same(all[packet.RecipeIndex])) return all[packet.RecipeIndex];
            return all.FirstOrDefault(Same);
        }

        public void OnPlayerLeft(IServerPlayer player)
        {
            try { Close(player); } catch (Exception e) { api.Logger.Warning("Vintage-E: emptying {0}'s Emc Craft grid: {1}", player?.PlayerName, e.Message); }
            grids.Remove(player.PlayerUID);
        }

        // ---------------------------------------------------------------- client

        /// <summary>What the client sends for a recipe shown in the handbook.</summary>
        public static EmcCraftPacket FillRequest(IWorldAccessor world, GridRecipe recipe)
        {
            int index = world.GridRecipes?.IndexOf(recipe) ?? -1;
            return new EmcCraftPacket
            {
                Action = EmcCraftAction.Fill,
                RecipeIndex = index,
                RecipeName = recipe?.Name?.ToString(),
                OutputCode = recipe?.Output?.Code?.ToString()
            };
        }
    }
}
