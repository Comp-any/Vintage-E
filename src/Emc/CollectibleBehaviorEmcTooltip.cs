using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Client-only behaviour attached to every block and item at load: adds "EMC: x" to the tooltip.
    /// Liquids are shown per litre (the game stores them as 100 small portions per litre, which made a litre of
    /// water look like 0.01), and containers show what their contents are worth.
    /// </summary>
    public class CollectibleBehaviorEmcTooltip : CollectibleBehavior
    {
        readonly VintageEModSystem mod;

        public CollectibleBehaviorEmcTooltip(CollectibleObject collObj, VintageEModSystem mod) : base(collObj)
        {
            this.mod = mod;
        }

        public override bool ClientSideOptional => true;

        /// <summary>Tooltips that threw, by item family, for /emc diagnose (the exception itself is swallowed so the tooltip still shows).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, (int count, string last)> Failures = new System.Collections.Generic.Dictionary<string, (int, string)>();

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            try { Info(inSlot, dsc); }
            catch (Exception e)
            {
                string family = inSlot?.Itemstack?.Collectible?.Code == null ? "?" : inSlot.Itemstack.Collectible.Code.Domain + ":" + inSlot.Itemstack.Collectible.Code.Path.Split('-')[0];
                lock (Failures)
                {
                    Failures.TryGetValue(family, out var f);
                    Failures[family] = (f.count + 1, e.GetType().Name + ": " + e.Message);
                }
            }
        }

        void Info(ItemSlot inSlot, StringBuilder dsc)
        {
            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;
            double value = mod.Emc.GetValue(stack);
            if (value > 0)
            {
                double perLitre = EmcRegistry.ItemsPerLitre(stack.Collectible);
                if (perLitre > 1)
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litre", EmcFormat.Format(value * perLitre)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litres", EmcFormat.Format(stack.StackSize / perLitre), EmcFormat.Format(value * stack.StackSize)));
                    }
                }
                else
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc", EmcFormat.Format(value)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-stack", EmcFormat.Format(value * stack.StackSize)));
                    }
                }
                if (mod.Emc.IsNoTransmute(stack.Collectible)) dsc.AppendLine(Lang.Get("vintagee:tooltip-notransmute"));
            }

            StackDataTable data = mod.Emc.StackData;
            StackDataMode mode = data.ModeOf(stack.Collectible);
            bool carries = data.CarriesData(stack.Collectible);
            bool hasContents = StackContents.HasNestedStacks(stack);
            // Item only / item price: the contents aren't in the value above, so say what they're worth.
            if (hasContents && (!carries || mode != StackDataMode.Full))
            {
                double contents = mod.Emc.ContentsValue(stack);
                if (contents > 0) dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-contents", EmcFormat.Format(contents)));
            }
            if (carries && StackDataUtil.KeptData(stack) != null)
            {
                string line = mode switch
                {
                    StackDataMode.Full => hasContents ? "vintagee:tooltip-data-full-contents" : "vintagee:tooltip-data-full",
                    StackDataMode.ItemPrice => hasContents ? "vintagee:tooltip-data-itemprice-contents" : "vintagee:tooltip-data-full",
                    _ => "vintagee:tooltip-data-item"
                };
                dsc.AppendLine(Lang.Get(line));
            }
        }
    
        // ------------------------------------------------------------ Arcane Tablet in the handbook (1.19.0)

        /// <summary>
        /// With an Arcane Tablet carried, an "Emc Craft" link under every crafting-grid recipe slideshow on a handbook
        /// page: it opens the tablet's crafting window filled for the recipe the slideshow is showing at that moment.
        /// Called from <see cref="HandbookPatches"/> after the game builds the page. True if anything was added.
        /// </summary>
        public static bool AddEmcCraftLinks(VintageEModSystem mod, List<RichTextComponentBase> components, ICoreClientAPI capi)
        {
            bool added = false;
            try
            {
                if (components == null || capi?.World?.Player == null || !EmcCraft.CarriesArcaneTablet(capi.World.Player)) return false;
                for (int i = components.Count - 1; i >= 0; i--)
                {
                    if (!(components[i] is SlideshowGridRecipeTextComponent show)) continue;
                    GridRecipe first = show.GridRecipesAndUnnamedIngredients?.FirstOrDefault()?.Recipe;
                    string label = first == null ? Lang.Get("vintagee:emccraft-button") : mod.ClientEmcCraft?.ButtonLabel(capi.World.Player, first) ?? Lang.Get("vintagee:emccraft-button");
                    var link = new LinkTextComponent(capi, label, CairoFont.WhiteSmallText(), _ =>
                    {
                        GridRecipe recipe = show.CurrentVisibleRecipe?.Recipe;
                        if (recipe != null) mod.EmcCraftFill(recipe);
                    });
                    components.Insert(i + 1, link);
                    components.Insert(i + 2, new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
                    added = true;
                }
            }
            catch (Exception e) { capi?.Logger.Warning("Vintage-E: adding Emc Craft to a handbook page: {0}", e.Message); }
            return added;
        }

        static readonly ConditionalWeakTable<string[], Dictionary<string, bool>> knownIngredients = new ConditionalWeakTable<string[], Dictionary<string, bool>>();

        /// <summary>
        /// A key for what an ingredient accepts. Not the ingredient object: the handbook copies some recipes each time
        /// it builds a page, so the same ingredient comes back as a new object and would miss the cache every time.
        /// </summary>
        static string IngredientKey(CraftingRecipeIngredient ing) =>
            ing.Type + "|" + ing.Code + "|" + ing.Name + "|" + (ing.AllowedVariants == null ? "" : string.Join(",", ing.AllowedVariants))
            + "|" + (ing.SkipVariants == null ? "" : string.Join(",", ing.SkipVariants)) + "|" + TagsKey(ing) + "|" + ing.ResolvedItemStack?.Attributes?.ToJsonToken();

        static string TagsKey(CraftingRecipeIngredient ing)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                using var w = new System.IO.BinaryWriter(ms);
                var tags = ing.Tags;
                Vintagestory.API.Datastructures.ComplexTagConditionExtensions.ToBytes(in tags, w);
                w.Flush();
                return Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
        }
        static long tabletCheckedAt = -10000;
        static bool tabletCarried;

        /// <summary>
        /// With an Arcane Tablet carried, a red square behind each ingredient of a crafting-grid recipe that nothing you've
        /// learned can stand in for (for "any planks" and the like, any learned kind counts).
        /// </summary>
        public override void OnHandbookRecipeRender(ICoreClientAPI capi, IRecipeBase recipe, ItemSlot slot, double x, double y, double z, double size, ref EnumHandling handling)
        {
            try
            {
                if (!(recipe is GridRecipe grid) || grid.ResolvedIngredients == null || slot?.BackgroundIcon == null) return;
                if (!int.TryParse(slot.BackgroundIcon, out int index) || index < 0 || index >= grid.ResolvedIngredients.Length) return;
                CraftingRecipeIngredient ing = grid.ResolvedIngredients[index];
                if (ing == null || ing.IsTool) return;
                IPlayer player = capi.World.Player;
                long now = capi.World.ElapsedMilliseconds;
                if (now - tabletCheckedAt > 500) { tabletCarried = EmcCraft.CarriesArcaneTablet(player); tabletCheckedAt = now; }
                if (!tabletCarried) return;

                string[] known = PlayerEmc.GetKnown(player);
                var cache = knownIngredients.GetValue(known, _ => new Dictionary<string, bool>());
                string ingKey = IngredientKey(ing);
                if (!cache.TryGetValue(ingKey, out bool learned))
                {
                    learned = false;
                    foreach (string key in known)
                    {
                        ItemStack stack = PlayerEmc.ResolveStack(capi.World, key);
                        if (stack != null && ing.SatisfiesAsIngredient(stack, false)) { learned = true; break; }
                    }
                    if (cache.Count > 4000) cache.Clear();
                    cache[ingKey] = learned;
                }
                if (learned) return;
                float half = (float)(size / 2);
                capi.Render.RenderRectangle((float)x - half, (float)y - half, (float)z - 1, half * 2, half * 2, ColorUtil.ToRgba(110, 220, 30, 30));
            }
            catch { /* a handbook drawing never throws over this */ }
        }
    }
}
