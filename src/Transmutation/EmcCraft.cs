using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

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
    /// <summary>
    /// The game's crafting grid, except that it goes away when it's closed: the game's player grid stays among the
    /// player's inventories for good (InventoryBasePlayer.RemoveOnClose is false), which would leave this one open, and
    /// possibly saved with the player, after the window closed.
    /// </summary>
    public class EmcCraftGrid : InventoryCraftingGrid
    {
        public EmcCraftGrid(string className, string instanceID, ICoreAPI api) : base(className, instanceID, api) { }
        public EmcCraftGrid(string inventoryID, ICoreAPI api) : base(inventoryID, api) { }
        public override bool RemoveOnClose => true;
    }

    public class EmcCraft
    {
        public const string InventoryClass = "vintageeemccraft";

        /// <summary>Stack attribute on tools Emc Craft made from EMC: they are burned again when the window closes.</summary>
        public const string MadeFromEmc = "vintageeEmcMade";

        public const int GridSize = 3;

        readonly VintageEModSystem mod;
        readonly ICoreAPI api;
        readonly Dictionary<string, EmcCraftGrid> grids = new Dictionary<string, EmcCraftGrid>();

        /// <summary>
        /// Tools this player's Emc Craft made from EMC and hasn't burned yet: the id written in each one's
        /// <see cref="MadeFromEmc"/> attribute, and its code. Only these are burned on closing, so the attribute on a
        /// tool that left the grid (shift-click, the mouse, death), or copied onto a crafted item, burns nothing later.
        /// </summary>
        readonly Dictionary<string, Dictionary<string, string>> madeTools = new Dictionary<string, Dictionary<string, string>>();

        public EmcCraft(VintageEModSystem mod, ICoreAPI api)
        {
            this.mod = mod;
            this.api = api;
        }

        /// <summary>
        /// Tells the game how to rebuild the grid by its class name. It saves every player-grid inventory the player has
        /// open with their data and sends it to their game on joining, so a server that saved (or stopped) while the
        /// window was open would otherwise log "Could not load a players inventory" on the next join and drop what was
        /// in it. Through the game's world object (ServerMain / ClientMain both expose the registry); never throws.
        /// </summary>
        public static void RegisterGridClass(ICoreAPI api)
        {
            try
            {
                object world = api.World;
                var prop = world?.GetType().GetProperty("ClassRegistryInt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                (prop?.GetValue(world) as ClassRegistry)?.RegisterInventoryClass(InventoryClass, typeof(EmcCraftGrid));
                if (prop == null) api.Logger.Warning("Vintage-E: couldn't register the Emc Craft grid with the game (no class registry found); a grid saved while open won't load back.");
            }
            catch (Exception e) { api.Logger.Warning("Vintage-E: registering the Emc Craft grid: {0}", e.Message); }
        }

        /// <summary>
        /// On joining: a grid the game saved while its window was open (an autosave, or the server stopping) comes back
        /// with the player's data. Give its contents back and close it, so nothing is lost and the grid starts empty.
        /// </summary>
        public void OnPlayerJoined(IServerPlayer player)
        {
            try
            {
                if (!(player?.InventoryManager?.GetInventory(InventoryClass + "-" + player.PlayerUID) is InventoryCraftingGrid restored)) return;
                int items = 0;
                for (int i = 0; i < GridSize * GridSize; i++) if (restored[i]?.Itemstack != null) items++;
                Empty(player, restored, burnMadeTools: false);
                player.InventoryManager.CloseInventory(restored);
                if (items > 0) api.Logger.Notification("Vintage-E: gave {0} back {1} stack(s) left in their Emc Craft grid when the game was last saved", player.PlayerName, items);
            }
            catch (Exception e) { api.Logger.Warning("Vintage-E: restoring {0}'s Emc Craft grid: {1}", player?.PlayerName, e.Message); }
        }

        /// <summary>The player's grid on this side (made on first use; one per player).</summary>
        public EmcCraftGrid GridOf(IPlayer player)
        {
            if (player == null) return null;
            if (!grids.TryGetValue(player.PlayerUID, out EmcCraftGrid grid))
            {
                grid = new EmcCraftGrid(InventoryClass, player.PlayerUID, api);
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
            /// <summary>EMC taken when the grid is filled (tools made from EMC at full price).</summary>
            public double Cost;
            /// <summary>
            /// What one craft really costs in EMC: the ingredients it uses up, plus the wear on each tool (its value × the
            /// durability one craft takes ÷ its full durability), since a tool made from EMC is burned back on closing for
            /// what it's still worth, and wear on your own tool is a loss too.
            /// </summary>
            public double NetCost;
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
        /// <param name="toolsByWear">Pick each tool made from EMC by what one craft wears off it (true), or by what it costs to make (false).</param>
        public Plan MakePlan(GridRecipe recipe, ICollection<string> known, IEnumerable<ItemSlot> carried, bool toolsByWear = true)
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
                            plan.NetCost += WearCost(own.Itemstack, ing);
                            continue;
                        }
                    }

                    if (!cache.TryGetValue(ing, out ItemStack pick))
                    {
                        knownStacks ??= KnownStacks(known);
                        pick = Cheapest(ing, knownStacks, out bool anyKnown, ing.IsTool && toolsByWear);
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
                    if (ing.IsTool) stack.Attributes.SetString(MadeFromEmc, Guid.NewGuid().ToString("N"));
                    plan.Cells[cell] = stack;
                    plan.Cost += mod.Emc.GetValue(pick) * stack.StackSize;
                    plan.NetCost += ing.IsTool ? WearCost(pick, ing) : mod.Emc.GetValue(pick) * stack.StackSize;
                }
            }
            return plan;
        }

        readonly System.Runtime.CompilerServices.ConditionalWeakTable<ICollection<string>, List<ItemStack>> knownStacksCache = new System.Runtime.CompilerServices.ConditionalWeakTable<ICollection<string>, List<ItemStack>>();

        /// <summary>Every learned key as a stack that could be made (exact versions included), once per knowledge list.</summary>
        List<ItemStack> KnownStacks(ICollection<string> known) => knownStacksCache.GetValue(known, BuildKnownStacks);

        List<ItemStack> BuildKnownStacks(ICollection<string> known)
        {
            var list = new List<ItemStack>();
            foreach (string key in known)
            {
                ItemStack s = TransmutationService.Prototype(mod, api.World, key);
                if (s?.Collectible != null) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// The EMC one craft wears off a tool: its value × the durability the recipe takes ÷ its full durability (all of
        /// it if that breaks it). Nothing when the server doesn't scale values by wear (a worn tool burns for its full value).
        /// </summary>
        public double WearCost(ItemStack tool, CraftingRecipeIngredient ing)
        {
            if (tool?.Collectible == null) return 0;
            double value = mod.Emc.GetValue(tool);
            int max = tool.Collectible.GetMaxDurability(tool);
            if (max <= 0) return 0; // no durability: the recipe doesn't use it up
            int remaining = tool.Collectible.GetRemainingDurability(tool);
            int cost = Math.Max(1, ing.ToolDurabilityCost);
            if (cost >= remaining) return value; // this craft breaks it
            if (!mod.Emc.DurabilityScales) return 0;
            return value * cost / remaining;
        }

        ItemStack Cheapest(CraftingRecipeIngredient ing, List<ItemStack> knownStacks, out bool anyKnown, bool byWear = false)
        {
            anyKnown = false;
            ItemStack best = null;
            double bestValue = double.MaxValue, bestPrice = double.MaxValue;
            foreach (ItemStack s in knownStacks)
            {
                if (!ing.SatisfiesAsIngredient(s, false)) continue; // stack size isn't checked, so no copy is needed
                anyKnown = true;
                if (mod.Emc.IsNoTransmute(s.Collectible)) continue;
                double price = mod.Emc.GetValue(s);
                if (price <= 0) continue;
                double v = byWear ? WearCost(s, ing) : price;
                // Equal wear: the cheaper one to make, so less EMC is tied up while the window is open.
                if (v > bestValue || (v == bestValue && price >= bestPrice)) continue;
                best = s;
                bestValue = v;
                bestPrice = price;
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
            EmcCraftGrid grid = GridOf(player);
            if (!player.InventoryManager.HasInventory(grid)) player.InventoryManager.OpenInventory(grid);
        }

        /// <summary>Empties the grid (burning tools Emc Craft made), then closes it. Also run when the player leaves.</summary>
        public void Close(IServerPlayer player)
        {
            if (!grids.TryGetValue(player.PlayerUID, out EmcCraftGrid grid)) return;
            Empty(player, grid, burnMadeTools: true);
            if (player.InventoryManager.HasInventory(grid)) player.InventoryManager.CloseInventory(grid);
        }

        /// <summary>
        /// Takes everything out of the grid: tools made from EMC are burned for what they're still worth, the rest goes
        /// to the player (or the ground at their feet). Returns the EMC given back.
        /// </summary>
        /// <param name="ownerUid">Whose made tools to look up when there's no player object (the self-test).</param>
        public double Empty(IPlayer player, InventoryCraftingGrid grid, bool burnMadeTools, string ownerUid = null)
        {
            double back = 0;
            string uid = player?.PlayerUID ?? ownerUid ?? "";
            madeTools.TryGetValue(uid, out Dictionary<string, string> made);
            for (int i = 0; i < GridSize * GridSize; i++)
            {
                ItemSlot slot = grid[i];
                if (slot?.Itemstack == null) continue;
                ItemStack stack = slot.TakeOutWhole();
                slot.MarkDirty(); // before anything else, so a burned tool also leaves the player's view of the grid
                string id = stack.Attributes.GetString(MadeFromEmc);
                if (burnMadeTools && id != null && made != null && made.TryGetValue(id, out string code) && code == stack.Collectible?.Code?.ToString())
                {
                    made.Remove(id);
                    double v = mod.BurnValue(stack) * stack.StackSize;
                    if (v > 0) { back += v; continue; }
                }
                if (id != null) stack.Attributes.RemoveAttribute(MadeFromEmc);
                Give(player, stack);
            }
            // Made tools that left the grid are the player's now; their ids no longer burn anything.
            if (burnMadeTools) madeTools.Remove(uid);
            if (back > 0 && player != null) PlayerEmc.AddEmc(player, back);
            return back;
        }

        /// <summary>Records a tool Emc Craft made (by the id in its attribute), so closing burns it.</summary>
        public void RecordMade(string uid, ItemStack tool)
        {
            string id = tool?.Attributes.GetString(MadeFromEmc);
            if (id == null) return;
            if (!madeTools.TryGetValue(uid, out var made)) madeTools[uid] = made = new Dictionary<string, string>();
            made[id] = tool.Collectible.Code.ToString();
        }

        void Give(IPlayer player, ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;
            if (player?.InventoryManager != null && player.InventoryManager.TryGiveItemstack(stack, true) && stack.StackSize <= 0) return;
            if (stack.StackSize > 0 && player?.Entity != null) api.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
        }

        readonly Dictionary<string, string> lastFill = new Dictionary<string, string>();

        /// <summary>What happened the last time this player asked Emc Craft to fill the grid (for the doctor), or null.</summary>
        public string LastFill(IPlayer player) => player != null && lastFill.TryGetValue(player.PlayerUID, out string s) ? s : null;

        void FillOutcome(IServerPlayer player, EmcCraftPacket packet, GridRecipe clicked, string outcome)
        {
            string what = (clicked?.Output?.Code?.ToString() ?? packet.OutputCode ?? "?") + " (recipe " + (packet.RecipeName ?? "?") + ", #" + packet.RecipeIndex + ")";
            lastFill[player.PlayerUID] = DateTime.Now.ToString("HH:mm:ss") + " " + what + ": " + outcome;
            api.Logger.Notification("Vintage-E: Emc Craft: {0} asked to fill the grid for {1}: {2}", player.PlayerName, what, outcome);
        }

        void Refuse(IServerPlayer player, EmcCraftPacket packet, GridRecipe clicked, string code, params string[] args)
        {
            FillOutcome(player, packet, clicked, "refused, " + code + (args.Length > 0 ? " (" + string.Join("; ", args) + ")" : ""));
            mod.Transmutation.Fail(player, code, args);
        }

        void Fill(IServerPlayer player, EmcCraftPacket packet)
        {
            if (!CarriesArcaneTablet(player)) { Refuse(player, packet, null, "emccraft-notablet"); return; }
            GridRecipe clicked = FindRecipe(packet);
            if (clicked == null) { Refuse(player, packet, null, "emccraft-norecipe", packet.RecipeName ?? "?"); return; }

            EmcCraftGrid grid = GridOf(player);
            if (!player.InventoryManager.HasInventory(grid)) player.InventoryManager.OpenInventory(grid);
            // The grid starts empty: what was in it goes back first (tools made earlier are burned, as on closing),
            // so the plan below sees the player's own tools wherever they ended up.
            Empty(player, grid, burnMadeTools: true);

            double emc = PlayerEmc.GetEmc(player);
            string[] known = PlayerEmc.GetKnown(player);
            List<ItemSlot> carried = EmcWallet.CarriedSlots(player).ToList();
            (GridRecipe recipe, Plan plan, Plan clickedPlan, Plan cheapestUnaffordable) = Choose(player, clicked, known, carried, emc);
            if (plan == null)
            {
                if (cheapestUnaffordable != null) { Refuse(player, packet, clicked, "notenoughemc", EmcFormat.Format(cheapestUnaffordable.Cost), EmcFormat.Format(emc)); return; }
                if (clickedPlan.Missing.Count > 0) { Refuse(player, packet, clicked, "emccraft-missing", string.Join(", ", clickedPlan.Missing)); return; }
                Refuse(player, packet, clicked, "emccraft-notmakeable", string.Join(", ", clickedPlan.NotMakeable));
                return;
            }

            PlayerEmc.AddEmc(player, -plan.Cost);
            int filled = 0, ownTools = 0;
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
                    // The player's own tool: never burned, whatever an old attribute on it says.
                    stack.Attributes.RemoveAttribute(MadeFromEmc);
                    ownTools++;
                }
                else RecordMade(player.PlayerUID, stack);
                grid[cell].Itemstack = stack;
                grid[cell].MarkDirty();
                filled++;
            }
            FillOutcome(player, packet, clicked, "filled " + filled + " cell(s) (" + ownTools + " of your own tools) for " + EmcFormat.Format(plan.Cost) + " EMC"
                + (recipe != clicked ? ", using a cheaper recipe" : ""));
            api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, new TransmuteResultPacket { Emc = PlayerEmc.GetEmc(player), HasEmc = true });
        }

        /// <summary>
        /// Every recipe that makes the same thing as <paramref name="clicked"/> (and that this player may use), planned;
        /// the one with the lowest real cost per item made (<see cref="Plan.NetCost"/>) among those the player can pay for
        /// up front. Tools are first picked by wear; if that makes a recipe too dear up front, by price to make.
        /// Returns the pick (or null), the clicked recipe's own plan (for its error), and the cheapest plan that was out of reach.
        /// </summary>
        public (GridRecipe recipe, Plan plan, Plan clickedPlan, Plan cheapestUnaffordable) Choose(IPlayer player, GridRecipe clicked, ICollection<string> known, List<ItemSlot> carried, double emc)
        {
            ItemStack target = clicked.Output?.ResolvedItemStack;
            CharacterSystem chars = api.ModLoader.GetModSystem<CharacterSystem>();
            bool Usable(GridRecipe r) => r.Enabled != false && r.ResolvedIngredients != null
                && (r.RequiresTrait == null || player == null || chars == null || chars.HasTrait(player, r.RequiresTrait));
            var candidates = new List<GridRecipe>();
            if (Usable(clicked)) candidates.Add(clicked);
            if (target != null)
            {
                foreach (GridRecipe r in api.World.GridRecipes)
                {
                    if (r == clicked || !Usable(r)) continue;
                    ItemStack o = r.Output?.ResolvedItemStack;
                    if (o?.Collectible != target.Collectible || !o.Equals(api.World, target, GlobalConstants.IgnoredStackAttributes)) continue;
                    candidates.Add(r);
                }
            }
            if (candidates.Count == 0) candidates.Add(clicked); // for its plan's error message; the grid won't craft it

            GridRecipe bestRecipe = null;
            Plan best = null, clickedPlan = null, out_ = null;
            double bestPer = double.MaxValue, outPer = double.MaxValue;
            foreach (GridRecipe r in candidates)
            {
                Plan p = MakePlan(r, known, carried, toolsByWear: true);
                if (r == clicked) clickedPlan = p;
                if (p.Missing.Count > 0 || p.NotMakeable.Count > 0) continue;
                if (p.Cost > emc + 1e-9)
                {
                    Plan p2 = MakePlan(r, known, carried, toolsByWear: false);
                    if (p2.Cost <= emc + 1e-9) p = p2;
                }
                double per = p.NetCost / Math.Max(1, r.Output?.Quantity ?? 1);
                if (p.Cost > emc + 1e-9)
                {
                    if (per < outPer) { outPer = per; out_ = p; }
                    continue;
                }
                if (per < bestPer - 1e-9 || (Math.Abs(per - bestPer) <= 1e-9 && r == clicked))
                {
                    bestPer = per; best = p; bestRecipe = r;
                }
            }
            return (bestRecipe, best, clickedPlan, best == null ? out_ : null);
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
            madeTools.Remove(player.PlayerUID);
        }

        // ---------------------------------------------------------------- client

        /// <summary>
        /// The Emc Craft link's label for a handbook recipe slideshow: what one craft would cost right now, after the
        /// tool refund (the same choice the server makes), or why it can't be made.
        /// </summary>
        public string ButtonLabel(IPlayer player, GridRecipe recipe)
        {
            string name = Lang.Get("vintagee:emccraft-button");
            try
            {
                double emc = PlayerEmc.GetEmc(player);
                var (_, plan, clickedPlan, tooDear) = Choose(player, recipe, PlayerEmc.GetKnown(player), EmcWallet.CarriedSlots(player).ToList(), emc);
                if (plan != null) return Lang.Get("vintagee:emccraft-button-cost", name, EmcFormat.Format(plan.NetCost));
                if (tooDear != null) return Lang.Get("vintagee:emccraft-button-dear", name, EmcFormat.Format(tooDear.NetCost), EmcFormat.Format(tooDear.Cost));
                if (clickedPlan?.Missing.Count > 0) return Lang.Get("vintagee:emccraft-button-missing", name, clickedPlan.Missing.Count);
                return Lang.Get("vintagee:emccraft-button-cant", name);
            }
            catch { return name; }
        }

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
