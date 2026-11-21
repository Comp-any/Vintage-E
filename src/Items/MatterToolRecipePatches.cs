using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// Lets matter tools stand in for the game's tools in recipes. Many of the game's recipes name their tool by
    /// code ("saw-*", "knife-*", "hammer-*", sometimes with a tag as well), which only the game's own tools match.
    /// Recipes that name the tool by tag alone ("tool-saw") already take matter tools, since those carry the tags.
    ///
    /// Two Harmony postfixes, run only when the game's own check said no:
    /// <list type="bullet">
    /// <item><c>CraftingRecipeIngredient.SatisfiesAsIngredient</c>: a tool ingredient (not used up) whose code is in a
    /// family the matter tool belongs to ("knife-*" for the matter knife, "axe-*" for the matter axe and the katar).</item>
    /// <item><c>FastSearchCraftingRecipeIngredient.SatisfiesAsIngredient</c>: the crafting grid's quick search for
    /// candidate recipes, so those recipes are tried at all (the full check above still decides).</item>
    /// </list>
    /// Matter tools never wear out (CollectibleBehaviorMatterTool), so the recipe's durability cost does nothing.
    ///
    /// A third postfix (1.26.2), on <c>CollectibleObject.MatchesForCrafting</c>: after an ingredient is satisfied the
    /// game also asks whether a kept tool has as much durability left as the recipe takes off it. A matter tool has
    /// the game's default durability of 1 (it never loses it), so every recipe that takes more than 1 from its tool
    /// (the pulverizer's 10 from the hammer, the Archimedes screw's 20) refused it: the tool sat in the grid and no
    /// output appeared, while the game's own hammer worked. A tool that never wears always has enough.
    /// </summary>
    public static class MatterToolRecipePatches
    {
        public const string HarmonyId = "vintagee.tools";
        static bool patched;
        public static string Error { get; private set; }
        public static bool Patched => patched;
        /// <summary>Whether the durability check is hooked too (the third postfix), and why not if it isn't.</summary>
        public static bool DurabilityPatched { get; private set; }
        public static string DurabilityError { get; private set; }
        /// <summary>How many times a matter tool was let through the game's "enough durability left" check (for the tests).</summary>
        public static int DurabilityAccepted;

        /// <summary>A tool that never wears out: every matter tool carries the behaviour that takes all wear off it.</summary>
        public static bool NeverWears(CollectibleObject c)
        {
            CollectibleBehavior[] behaviors = c?.CollectibleBehaviors;
            if (behaviors == null) return false;
            for (int i = 0; i < behaviors.Length; i++) if (behaviors[i] is CollectibleBehaviorMatterTool) return true;
            return false;
        }
        /// <summary>How many times a matter tool was accepted where the game's check refused it (for the doctor and tests).</summary>
        public static int Accepted;

        static readonly string[] None = Array.Empty<string>();
        static readonly string[] Knife = { "knife" }, Saw = { "saw" }, Chisel = { "chisel" }, Scythe = { "scythe" }, Propick = { "prospectingpick" },
            Hammer = { "hammer" }, Spear = { "spear" }, Bow = { "bow" }, Axe = { "axe" }, Shears = { "shears" }, Pickaxe = { "pickaxe" },
            Shovel = { "shovel" }, Hoe = { "hoe" }, Sword = { "sword", "blade" }, Katar = { "axe", "shears", "sword", "blade" }, MorningStar = { "pickaxe", "shovel" };

        /// <summary>The game tool families (first part of their item code) a Vintage-E tool can stand in for; empty if none.</summary>
        public static string[] FamiliesOf(CollectibleObject c)
        {
            AssetLocation code = c?.Code;
            if (code == null || code.Domain != "vintagee") return None;
            string p = code.Path;
            if (p.StartsWith("matterknife-", StringComparison.Ordinal)) return Knife;
            if (p.StartsWith("mattersaw-", StringComparison.Ordinal)) return Saw;
            if (p.StartsWith("matterchisel-", StringComparison.Ordinal)) return Chisel;
            if (p.StartsWith("matterscythe-", StringComparison.Ordinal)) return Scythe;
            if (p.StartsWith("matterpropick-", StringComparison.Ordinal)) return Propick;
            if (p.StartsWith("mattersmithinghammer-", StringComparison.Ordinal)) return Hammer;
            if (p.StartsWith("matterspear-", StringComparison.Ordinal)) return Spear;
            if (p.StartsWith("matterbow-", StringComparison.Ordinal)) return Bow;
            if (p == "redmattertool-katar") return Katar;
            if (p == "redmattertool-morningstar") return MorningStar;
            if (p.StartsWith("mattertool-", StringComparison.Ordinal))
            {
                // mattertool-<dark|red>-<tool>; the sledgehammer ("hammer") is a mining tool, not the smith's hammer.
                string tool = p.Substring(p.LastIndexOf('-') + 1);
                switch (tool)
                {
                    case "axe": return Axe;
                    case "shears": return Shears;
                    case "pickaxe": return Pickaxe;
                    case "shovel": return Shovel;
                    case "hoe": return Hoe;
                    case "sword": return Sword;
                }
            }
            return None;
        }

        /// <summary>The family an ingredient code names ("knife" for "game:knife-*"), or null for a code that names none ("*").</summary>
        public static string FamilyOf(AssetLocation code)
        {
            string path = code?.Path;
            if (string.IsNullOrEmpty(path) || path[0] == '*' || path[0] == '@') return null;
            int dash = path.IndexOf('-');
            return dash > 0 ? path.Substring(0, dash) : path;
        }

        public static bool Accepts(AssetLocation ingredientCode, ItemStack input)
        {
            string[] families = FamiliesOf(input?.Collectible);
            if (families.Length == 0) return false;
            string family = FamilyOf(ingredientCode);
            return family != null && Array.IndexOf(families, family) >= 0;
        }

        public static void Patch(ILogger logger)
        {
            if (patched) return;
            try
            {
                var harmony = new Harmony(HarmonyId);
                var full = typeof(CraftingRecipeIngredient).GetMethod(nameof(CraftingRecipeIngredient.SatisfiesAsIngredient), new[] { typeof(ItemStack), typeof(bool) });
                var fast = typeof(FastSearchCraftingRecipeIngredient).GetMethod(nameof(FastSearchCraftingRecipeIngredient.SatisfiesAsIngredient), new[] { typeof(ItemStack), typeof(bool) });
                if (full == null || fast == null)
                {
                    Error = "the game's recipe ingredient check (SatisfiesAsIngredient) wasn't found";
                    logger.Warning("Vintage-E: {0}; matter tools only work in recipes that name the tool by tag.", Error);
                    return;
                }
                harmony.Patch(full, postfix: new HarmonyMethod(typeof(MatterToolRecipePatches), nameof(FullPostfix)));
                harmony.Patch(fast, postfix: new HarmonyMethod(typeof(MatterToolRecipePatches), nameof(FastPostfix)));
                patched = true;
                Error = null;
                logger.Notification("Vintage-E: matter tools: hooked the recipe ingredient checks, so they count as the game's tools in recipes.");
                try
                {
                    var crafting = typeof(CollectibleObject).GetMethod(nameof(CollectibleObject.MatchesForCrafting), new[] { typeof(ItemStack), typeof(IRecipeBase), typeof(IRecipeIngredient) });
                    if (crafting == null) DurabilityError = "the game's durability check for recipe tools (MatchesForCrafting) wasn't found";
                    else
                    {
                        harmony.Patch(crafting, postfix: new HarmonyMethod(typeof(MatterToolRecipePatches), nameof(CraftingPostfix)));
                        DurabilityPatched = true;
                        DurabilityError = null;
                    }
                }
                catch (Exception e) { DurabilityError = e.Message; }
                if (!DurabilityPatched) logger.Warning("Vintage-E: matter tools: {0}; recipes that take more than 1 durability from their tool won't accept a matter tool.", DurabilityError);
            }
            catch (Exception e)
            {
                Error = e.Message;
                logger.Warning("Vintage-E: matter tools: couldn't hook the recipe ingredient checks ({0}); they only work in recipes that name the tool by tag.", e.Message);
            }
        }

        static void FullPostfix(CraftingRecipeIngredient __instance, ItemStack inputStack, ref bool __result)
        {
            if (__result || inputStack?.Collectible == null || __instance == null) return;
            if (__instance.Consume && !__instance.IsTool) return; // only a tool the recipe keeps, never one it uses up
            if (__instance.Type != inputStack.Class) return;
            if (!Accepts(__instance.Code, inputStack)) return;
            __result = true;
            Accepted++;
        }

        /// <summary>The game's only reason to say no here is a kept tool with less durability left than the recipe takes; a tool that never wears has enough.</summary>
        static void CraftingPostfix(ItemStack inputStack, IRecipeIngredient ingredient, ref bool __result)
        {
            if (__result || inputStack?.Collectible == null || ingredient == null) return;
            if (!NeverWears(inputStack.Collectible)) return;
            __result = true;
            DurabilityAccepted++;
        }

        static void FastPostfix(FastSearchCraftingRecipeIngredient __instance, ItemStack inputStack, ref bool __result)
        {
            if (__result || inputStack?.Collectible == null || __instance == null) return;
            if (__instance.Type != inputStack.Class) return;
            if (Accepts(__instance.Code, inputStack)) __result = true;
        }
    }
}
