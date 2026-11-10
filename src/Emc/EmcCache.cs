using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>What is written to ModData/vintagee/cache/emc-&lt;world&gt;.json.</summary>
    public class EmcCacheFile
    {
        public int Format;
        public string Fingerprint;
        public string CreatedUtc;
        public string GameVersion;
        public int BaseCount, DerivedCount, InferredCount, SiblingCount, FallbackCount, RelativeCount, TraderCount, DefaultCount;
        public int ConversionCount, SourceCount, LoopFrozen, UnstableRemoved;
        /// <summary>Knowledge-style keys ("i:game:ingot-gold", "b:game:rock-granite") → EMC.</summary>
        public SortedDictionary<string, double> Values = new SortedDictionary<string, double>(StringComparer.Ordinal);
        public int FuelCount, FoodCount, OtherModCount, GuessCount;
        /// <summary>Material-specific values: key, the stored data they apply to (base64), value.</summary>
        public List<CachedVariant> Variants = new List<CachedVariant>();
        /// <summary>Value loops and settled loops, each as knowledge-style keys in order; and why each missing item has no value.</summary>
        public List<List<string>> Loops = new List<List<string>>();
        public List<List<string>> SettledLoops = new List<List<string>>();
        public SortedDictionary<string, string> MissingWhy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Items valued by each compatibility file (asset location → count).</summary>
        public SortedDictionary<string, int> CompatUsed = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }

    public class CachedVariant
    {
        public string Key;
        public string Data;
        public double Value;
    }

    /// <summary>
    /// Saves mapped EMC values per world and reuses them on the next start when nothing that affects them has changed.
    ///
    /// "Nothing changed" is decided by a fingerprint (SHA-256) over every input the mapper reads: game version,
    /// loaded mods and their versions, this mod's code, the server config, the merged value files, every block and
    /// item code with its processing data (smelting, grinding, crushing, transitions, drops, baking/press/still
    /// attributes, emcValue), every grid recipe and every recipe/alloy list found on any mod system. Building it walks
    /// the data once but skips the expensive parts (expanding wildcard/tag ingredients and solving the graph).
    /// Anything different → full remap and a fresh cache. <c>/emc recalc</c> always remaps.
    /// </summary>
    public static class EmcCache
    {
        public const int Format = 9; // 2: barrel outputs counted in portions, liquid-container ingredients; 7: cooking pot recipes; 8: material-specific values, staged guesses; 9: loops, missing reasons, free ingredients

        public static string FilePath(ICoreServerAPI sapi)
        {
            string dir = Path.Combine(VintageEModSystem.DataFolder(sapi), "cache");
            Directory.CreateDirectory(dir);
            string world = sapi.World.SavegameIdentifier ?? "default";
            foreach (char ch in Path.GetInvalidFileNameChars()) world = world.Replace(ch, '_');
            return Path.Combine(dir, "emc-" + world + ".json");
        }

        public static string Fingerprint(ICoreServerAPI sapi, VintageEModSystem mod, ILogger logger, VintageEConfig config = null)
        {
            config ??= mod.Config;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var sb = new StringBuilder(4096);
            void Flush()
            {
                if (sb.Length == 0) return;
                hash.AppendData(Encoding.UTF8.GetBytes(sb.ToString()));
                sb.Clear();
            }

            // Code and versions
            sb.Append("fmt=").Append(Format).Append('\n');
            sb.Append("game=").Append(GameVersion.LongGameVersion).Append('\n');
            sb.Append("self=").Append(mod.Mod?.Info?.Version).Append('|').Append(typeof(EmcCache).Assembly.ManifestModule.ModuleVersionId).Append('\n');
            foreach (Mod m in sapi.ModLoader.Mods.OrderBy(m => m.Info?.ModID, StringComparer.Ordinal))
            {
                sb.Append("mod=").Append(m.Info?.ModID).Append('@').Append(m.Info?.Version).Append('\n');
            }

            // Settings and value files
            sb.Append("config=").Append(JsonConvert.SerializeObject(config)).Append('\n');
            foreach (EmcValueRule r in mod.Rules.Values) sb.Append("rule=").Append(r.Source).Append('|').Append(r.Pattern).Append(r.PerLitre ? "/L=" : "=").Append(r.Value.ToString("R")).Append('\n');
            foreach (string b in mod.Rules.Blacklist) sb.Append("bl=").Append(b).Append('\n');
            foreach (string a in mod.Rules.Allow) sb.Append("allow=").Append(a).Append('\n');
            foreach (string a in mod.Rules.FreeIngredients) sb.Append("free=").Append(a).Append('\n');
            foreach (string a in mod.Rules.GrayList) sb.Append("gray=").Append(a).Append('\n');
            foreach (string a in mod.Rules.Recipes) sb.Append("recipe=").Append(a).Append('\n');
            foreach (string a in mod.Rules.TraderCurrency) sb.Append("currency=").Append(a).Append('\n');
            foreach (EmcValueRule r in mod.Rules.Defaults) sb.Append("default=").Append(r.Source).Append('|').Append(r.Pattern).Append(r.PerLitre ? "/L=" : "=").Append(r.Value.ToString("R")).Append('\n');
            foreach (RelativeValueRule r in mod.Rules.Relative) sb.Append("rel=").Append(r.Source).Append('|').Append(r.Code).Append('=').Append(r.Of).Append('*').Append(r.Ratio.ToString("R")).Append('\n');
            sb.Append("hoursPerDay=").Append(sapi.World.Calendar?.HoursPerDay).Append('\n');
            Flush();

            // Every block/item with the per-item data the mapper reads
            foreach (CollectibleObject c in sapi.World.Collectibles)
            {
                if (c?.Code == null) continue;
                sb.Append(c.ItemClass == EnumItemClass.Block ? "b:" : "i:").Append(c.Code).Append('#').Append(c.GetType().Name);
                AppendStack(sb, "smelt", c.CombustibleProps?.SmeltedStack?.ResolvedItemstack, c.CombustibleProps?.SmeltedRatio ?? 0);
                AppendStack(sb, "grind", c.GrindingProps?.GroundStack?.ResolvedItemstack, 1);
                AppendStack(sb, "crush", c.CrushingProps?.CrushedStack?.ResolvedItemstack, c.CrushingProps?.Quantity?.avg ?? 0);
                if (c.TransitionableProps != null)
                {
                    foreach (TransitionableProperties tp in c.TransitionableProps)
                    {
                        if (tp == null) continue;
                        AppendStack(sb, "tr" + (int)tp.Type, tp.TransitionedStack?.ResolvedItemstack, tp.TransitionRatio);
                        sb.Append('@').Append(tp.FreshHours?.avg).Append('+').Append(tp.TransitionHours?.avg);
                    }
                }
                if (c is Block block && block.Drops != null)
                {
                    sb.Append("|be=").Append(block.EntityClass);
                    foreach (BlockDropItemStack drop in block.Drops) AppendStack(sb, "drop", drop?.ResolvedItemstack, drop?.Quantity?.avg ?? 0);
                }
                if (c.CollectibleBehaviors != null)
                {
                    foreach (CollectibleBehavior bh in c.CollectibleBehaviors)
                    {
                        string json = bh?.propertiesAtString;
                        if (json != null && (json.Contains("squeezedLitres") || json.Contains("processedStack") || json.Contains("remainingItem")))
                        {
                            sb.Append("|bh=").Append(bh.GetType().Name).Append(json);
                        }
                    }
                }
                if (c.Attributes != null)
                {
                    foreach (string key in FingerprintAttributes)
                    {
                        var a = c.Attributes[key];
                        if (a != null && a.Exists) sb.Append('|').Append(key).Append('=').Append(a.Token?.ToString(Formatting.None));
                    }
                }
                sb.Append('\n');
                if (sb.Length > 1 << 16) Flush();
            }
            Flush();

            // Things built in stages (sailboats): what each stage takes.
            foreach (var etype in sapi.World.EntityTypes ?? new List<Vintagestory.API.Common.Entities.EntityProperties>())
            {
                var stages = etype?.Attributes?["stages"];
                if (stages == null || !stages.Exists) continue;
                sb.Append("e=").Append(etype.Code).Append(stages.Token?.ToString(Formatting.None)).Append('\n');
            }
            Flush();

            // Recipes: the crafting grid plus every recipe list any mod system holds
            int n = 0;
            foreach (GridRecipe r in sapi.World.GridRecipes) AppendRecipe(sb, r, ref n, hash);
            foreach (ConversionCollector.DiscoveredRecipeList list in ConversionCollector.DiscoverRecipeLists(sapi, logger))
            {
                sb.Append("list=").Append(list.Label).Append('\n');
                foreach (object recipe in list.Recipes)
                {
                    if (recipe is IRecipeBase rb) AppendRecipe(sb, rb, ref n, hash);
                    else AppendObjectFields(sb, recipe);
                    if (sb.Length > 1 << 16) { hash.AppendData(Encoding.UTF8.GetBytes(sb.ToString())); sb.Clear(); }
                }
            }
            Flush();

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>The item attributes the mapper reads, which therefore go into the fingerprint.</summary>
        static readonly string[] FingerprintAttributes = { "emcValue", "bakingProperties", "juiceableProperties", "distillationProps", "waterTightContainerProps", "drop", "drops", "requiredUnits", "breaksWhenFilled", "beehivekiln", "carburizableProps" };

        static void AppendStack(StringBuilder sb, string tag, ItemStack stack, double qty)
        {
            if (stack?.Collectible == null) return;
            sb.Append('|').Append(tag).Append('=').Append(stack.Collectible.Code).Append('*').Append(stack.StackSize).Append('x').Append(qty.ToString("R"));
        }

        static void AppendRecipe(StringBuilder sb, IRecipeBase recipe, ref int n, IncrementalHash hash)
        {
            try
            {
                sb.Append("r=").Append(recipe.Enabled ? '1' : '0').Append(recipe.Name);
                ItemStack output = recipe.RecipeOutput?.ResolvedItemStack;
                if (output != null) sb.Append("->").Append(output.Collectible?.Code).Append('*').Append(ConversionCollector.RecipeOutputQuantity(recipe.RecipeOutput, output));
                if (output?.Attributes != null && output.Attributes.Count > 0)
                {
                    var data = StackDataUtil.KeptData(output);
                    if (data != null) sb.Append("{").Append(StackDataUtil.Encode(data)).Append('}');
                }
                var recipeAttrs = (recipe as RecipeBase)?.Attributes?["liquidContainerProps"];
                if (recipeAttrs != null && recipeAttrs.Exists) sb.Append("|lcp=").Append(recipeAttrs.Token?.ToString(Formatting.None));
                foreach (IRecipeIngredient ing in recipe.RecipeIngredients)
                {
                    if (ing == null) { sb.Append("|_"); continue; }
                    RecipeIngredientConsumeProperties cp = ing.ConsumeProperties;
                    sb.Append('|').Append(ing.Type).Append(':').Append(ing.Code).Append('*').Append(ing.Quantity)
                      .Append(cp.Consume ? "" : "(tool)")
                      .Append('[').Append(ing.Tags).Append(']')
                      .Append(ing.AllowedVariants == null ? "" : string.Join(",", ing.AllowedVariants))
                      .Append('/').Append(ing.SkipVariants == null ? "" : string.Join(",", ing.SkipVariants));
                    ItemStack ret = ing.ReturnedStack?.ResolvedItemstack;
                    if (ret != null) sb.Append("ret=").Append(ret.Collectible?.Code);
                    object consumeQty = ConversionCollector.OptionalMember(ing, "ConsumeQuantity");
                    if (consumeQty != null) sb.Append("cq=").Append(consumeQty);
                    if (ing.RecipeAttributes != null && ing.RecipeAttributes.Exists) sb.Append("ra=").Append(ing.RecipeAttributes.Token?.ToString(Formatting.None));
                }
                if (recipe is LayeredVoxelRecipe voxel && voxel.Voxels != null)
                {
                    int count = 0;
                    foreach (bool v in voxel.Voxels) if (v) count++;
                    sb.Append("|vox=").Append(count);
                }
            }
            catch (Exception e)
            {
                sb.Append("|err=").Append(e.GetType().Name);
            }
            sb.Append('\n');
            if (++n % 512 == 0)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(sb.ToString()));
                sb.Clear();
            }
        }

        /// <summary>Alloy-shaped recipes and other non-IRecipeBase entries: hash their public fields' resolved stacks and numbers.</summary>
        static void AppendObjectFields(StringBuilder sb, object obj)
        {
            if (obj == null) return;
            sb.Append("o=");
            foreach (var field in obj.GetType().GetFields())
            {
                object v = field.GetValue(obj);
                sb.Append(field.Name).Append('=');
                AppendValue(sb, v);
                sb.Append(';');
            }
            sb.Append('\n');
        }

        static void AppendValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: break;
                case JsonItemStack js:
                    sb.Append(js.ResolvedItemstack?.Collectible?.Code).Append('*').Append(js.ResolvedItemstack?.StackSize);
                    foreach (var f in js.GetType().GetFields().Where(f => f.DeclaringType != typeof(JsonItemStack) && (f.FieldType == typeof(float) || f.FieldType == typeof(double) || f.FieldType == typeof(int))))
                    {
                        sb.Append(',').Append(f.Name).Append(':').Append(f.GetValue(js));
                    }
                    break;
                case string s: sb.Append(s); break;
                case IEnumerable e:
                    sb.Append('[');
                    foreach (object item in e) { AppendValue(sb, item); sb.Append(','); }
                    sb.Append(']');
                    break;
                default: sb.Append(v); break;
            }
        }

        public static bool TryLoad(ICoreServerAPI sapi, string fingerprint, ILogger logger, out EmcCacheFile file, out Dictionary<CollectibleObject, double> values)
            => TryLoad(sapi, fingerprint, logger, out file, out values, out _);

        public static bool TryLoad(ICoreServerAPI sapi, string fingerprint, ILogger logger, out EmcCacheFile file, out Dictionary<CollectibleObject, double> values,
            out Dictionary<CollectibleObject, List<VariantValue>> variants)
        {
            file = null;
            values = null;
            variants = new Dictionary<CollectibleObject, List<VariantValue>>();
            string path = FilePath(sapi);
            if (!File.Exists(path)) return false;
            try
            {
                file = JsonConvert.DeserializeObject<EmcCacheFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                logger.Warning("EMC cache {0} is unreadable, remapping: {1}", path, e.Message);
                return false;
            }
            if (file == null || file.Format != Format || file.Fingerprint != fingerprint || file.Values == null) return false;

            values = new Dictionary<CollectibleObject, double>();
            foreach (var kv in file.Values)
            {
                CollectibleObject c = PlayerEmc.Resolve(sapi.World, kv.Key);
                if (c == null)
                {
                    logger.Notification("EMC cache refers to {0}, which no longer exists; remapping.", kv.Key);
                    values = null;
                    return false;
                }
                values[c] = kv.Value;
            }
            foreach (CachedVariant cv in file.Variants ?? new List<CachedVariant>())
            {
                CollectibleObject c = PlayerEmc.Resolve(sapi.World, cv.Key);
                var tree = StackDataUtil.Decode(cv.Data);
                if (c == null || tree == null) continue;
                if (!variants.TryGetValue(c, out var list)) variants[c] = list = new List<VariantValue>();
                list.Add(new VariantValue { Attributes = tree, Value = cv.Value });
            }
            return true;
        }

        public static void Save(ICoreServerAPI sapi, string fingerprint, EmcMappingReport report, ILogger logger)
        {
            try
            {
                var file = new EmcCacheFile
                {
                    Format = Format,
                    Fingerprint = fingerprint,
                    CreatedUtc = DateTime.UtcNow.ToString("u"),
                    GameVersion = GameVersion.LongGameVersion,
                    BaseCount = report.BaseCount,
                    DerivedCount = report.DerivedCount,
                    InferredCount = report.InferredCount,
                    SiblingCount = report.SiblingCount,
                    FallbackCount = report.FallbackCount,
                    RelativeCount = report.RelativeCount,
                    TraderCount = report.TraderCount,
                    DefaultCount = report.DefaultCount,
                    ConversionCount = report.ConversionCount,
                    SourceCount = report.SourceCount,
                    LoopFrozen = report.LoopFrozen,
                    UnstableRemoved = report.UnstableRemoved,
                    FuelCount = report.FuelCount,
                    FoodCount = report.FoodCount,
                    OtherModCount = report.OtherModCount,
                    GuessCount = report.GuessCount,
                    CompatUsed = new SortedDictionary<string, int>(report.CompatUsed ?? new SortedDictionary<string, int>(StringComparer.Ordinal), StringComparer.Ordinal)
                };
                foreach (var kv in report.Variants)
                {
                    string key = PlayerEmc.KeyOf(kv.Key);
                    if (key == null) continue;
                    foreach (VariantValue v in kv.Value) file.Variants.Add(new CachedVariant { Key = key, Data = StackDataUtil.Encode(v.Attributes), Value = v.Value });
                }
                foreach (var kv in report.Values)
                {
                    string key = PlayerEmc.KeyOf(kv.Key);
                    if (key != null) file.Values[key] = kv.Value;
                }
                List<List<string>> Keys(List<List<CollectibleObject>> loops) => loops.Select(l => l.Select(PlayerEmc.KeyOf).Where(k => k != null).ToList()).Where(l => l.Count > 0).ToList();
                file.Loops = Keys(report.Loops);
                file.SettledLoops = Keys(report.SettledLoops);
                foreach (var kv in report.MissingWhy)
                {
                    string key = PlayerEmc.KeyOf(kv.Key);
                    if (key != null) file.MissingWhy[key] = kv.Value;
                }
                string path = FilePath(sapi);
                string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp"; // two mappings finishing together must not share it
                File.WriteAllText(tmp, JsonConvert.SerializeObject(file, Formatting.Indented));
                File.Move(tmp, path, true);
            }
            catch (Exception e)
            {
                logger.Warning("Could not write the EMC cache: {0}", e.Message);
            }
        }

        public static void CopyCounts(EmcCacheFile from, EmcMappingReport to)
        {
            to.BaseCount = from.BaseCount;
            to.DerivedCount = from.DerivedCount;
            to.InferredCount = from.InferredCount;
            to.SiblingCount = from.SiblingCount;
            to.FallbackCount = from.FallbackCount;
            to.RelativeCount = from.RelativeCount;
            to.TraderCount = from.TraderCount;
            to.DefaultCount = from.DefaultCount;
            to.ConversionCount = from.ConversionCount;
            to.SourceCount = from.SourceCount;
            to.LoopFrozen = from.LoopFrozen;
            to.UnstableRemoved = from.UnstableRemoved;
            to.FuelCount = from.FuelCount;
            to.FoodCount = from.FoodCount;
            to.OtherModCount = from.OtherModCount;
            to.GuessCount = from.GuessCount;
            to.CachedAt = from.CreatedUtc;
            to.CompatUsed = from.CompatUsed ?? new SortedDictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Loops and missing reasons from the cache file (the recipes behind them aren't cached).</summary>
        public static void CopyAnalysis(EmcCacheFile from, EmcMappingReport to, IWorldAccessor world)
        {
            List<List<CollectibleObject>> Resolve(List<List<string>> loops) => (loops ?? new List<List<string>>())
                .Select(l => l.Select(k => PlayerEmc.Resolve(world, k)).ToList())
                .Where(l => l.Count > 0 && l.All(c => c != null)).ToList();
            to.Loops = Resolve(from.Loops);
            to.SettledLoops = Resolve(from.SettledLoops);
            to.LoopOf.Clear();
            for (int i = 0; i < to.Loops.Count; i++) foreach (CollectibleObject c in to.Loops[i]) if (!to.LoopOf.ContainsKey(c)) to.LoopOf[c] = i;
            foreach (var kv in from.MissingWhy ?? new SortedDictionary<string, string>())
            {
                CollectibleObject c = PlayerEmc.Resolve(world, kv.Key);
                if (c != null) to.MissingWhy[c] = kv.Value;
            }
        }
    }
}
