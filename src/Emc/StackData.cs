using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>What a block/item does with the data stored on it (what it holds, what it's made of).</summary>
    public enum StackDataMode
    {
        /// <summary>"Item only": the stored data is ignored. It burns as the plain item (containers that hold something are
        /// protected from burning), and you learn and create the plain item.</summary>
        Item = 0,
        /// <summary>"Item + contents": what it holds or is made of counts. It's worth the item plus its contents/material,
        /// and you learn and create exactly that version.</summary>
        Full = 1,
        /// <summary>"Item + contents, item price": you learn and create exactly that version, but it costs (and burns for)
        /// only the item itself - what it's made of counts, what it holds doesn't.</summary>
        ItemPrice = 2
    }

    /// <summary>Why a block/item can carry data.</summary>
    public enum StackDataKind
    {
        /// <summary>Holds other things: buckets, jugs, pots, bowls, crocks, bags.</summary>
        Contents = 0,
        /// <summary>What it is lives on the stack, not in its code: round shields (metal, wood), lanterns, typed chests, clutter.</summary>
        Material = 1,
        /// <summary>Chiseled blocks: made of so many voxels of each material.</summary>
        Chiseled = 2
    }

    /// <summary>A material-specific value: stacks whose data contains <see cref="Attributes"/> are worth <see cref="Value"/>.</summary>
    public sealed class VariantValue
    {
        public ITreeAttribute Attributes;
        public double Value;
        public string Origin;
    }

    /// <summary>
    /// Which blocks/items carry data, what kind, and the mode each one uses. Built on the server (kinds are detected,
    /// modes come from built-in defaults, value files and the server config) and sent to clients with the EMC table.
    /// </summary>
    public class StackDataTable
    {
        public Dictionary<CollectibleObject, StackDataKind> Kinds = new Dictionary<CollectibleObject, StackDataKind>();
        public Dictionary<CollectibleObject, StackDataMode> Modes = new Dictionary<CollectibleObject, StackDataMode>();
        /// <summary>Where each mode came from (server only): "default", "value file", "server config".</summary>
        public Dictionary<CollectibleObject, string> Sources = new Dictionary<CollectibleObject, string>();
        /// <summary>Material-specific values, from recipes that make the item with data on it (a gold round shield...).</summary>
        public Dictionary<CollectibleObject, List<VariantValue>> Variants = new Dictionary<CollectibleObject, List<VariantValue>>();

        /// <summary>
        /// Server: detects every block/item that carries data, gives it its built-in mode (containers: item only;
        /// material items and chiseled blocks: item + contents), then applies the value files' "stackData" and the
        /// server config's StackDataModes, in that order.
        /// </summary>
        public static StackDataTable Build(IWorldAccessor world, EmcRuleSet rules, VintageEConfig config, Dictionary<CollectibleObject, List<VariantValue>> variants)
        {
            var table = new StackDataTable { Variants = variants ?? new Dictionary<CollectibleObject, List<VariantValue>>() };
            var index = new CollectibleIndex(world.Collectibles);
            foreach (CollectibleObject c in index.All)
            {
                StackDataKind? kind;
                try { kind = StackDataUtil.Detect(c, world); }
                catch { kind = null; }
                if (kind == null && table.Variants.ContainsKey(c)) kind = StackDataKind.Material;
                if (kind == null) continue;
                table.Kinds[c] = kind.Value;
                table.Modes[c] = DefaultMode(kind.Value);
                table.Sources[c] = "default";
            }
            void Apply(IEnumerable<KeyValuePair<string, string>> entries, string source)
            {
                foreach (var kv in entries)
                {
                    if (!TryParseMode(kv.Value, out StackDataMode mode)) continue;
                    foreach (CollectibleObject c in index.Match(kv.Key))
                    {
                        if (c is IEmcStorageItem) continue;
                        if (!table.Kinds.ContainsKey(c)) table.Kinds[c] = table.Variants.ContainsKey(c) ? StackDataKind.Material : StackDataKind.Contents;
                        table.Modes[c] = mode;
                        table.Sources[c] = source;
                    }
                }
            }
            Apply(rules.StackData, "value file");
            Apply(config.StackDataModes, "server config");
            return table;
        }

        public bool CarriesData(CollectibleObject c) => c != null && Kinds.ContainsKey(c);

        public StackDataMode ModeOf(CollectibleObject c) => c != null && Modes.TryGetValue(c, out StackDataMode m) ? m : StackDataMode.Item;

        public StackDataKind KindOf(CollectibleObject c) => c != null && Kinds.TryGetValue(c, out StackDataKind k) ? k : StackDataKind.Contents;

        public static StackDataMode DefaultMode(StackDataKind kind) => kind == StackDataKind.Contents ? StackDataMode.Item : StackDataMode.Full;

        public static string ModeName(StackDataMode mode) => mode switch
        {
            StackDataMode.Full => "full",
            StackDataMode.ItemPrice => "itemprice",
            _ => "item"
        };

        public static bool TryParseMode(string text, out StackDataMode mode)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "item": case "itemonly": case "plain": mode = StackDataMode.Item; return true;
                case "full": case "contents": case "item+contents": case "withcontents": mode = StackDataMode.Full; return true;
                case "itemprice": case "item-price": case "free": case "freecontents": mode = StackDataMode.ItemPrice; return true;
                default: mode = StackDataMode.Item; return false;
            }
        }

        /// <summary>The most specific material value whose data is contained in the stack's data (null if none).</summary>
        public VariantValue FindVariant(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Collectible == null || stack.Attributes == null || !Variants.TryGetValue(stack.Collectible, out List<VariantValue> list)) return null;
            VariantValue best = null;
            int bestKeys = -1;
            foreach (VariantValue v in list)
            {
                if (!StackDataUtil.IsSubset(world, v.Attributes, stack.Attributes)) continue;
                int keys = v.Attributes.Count;
                if (keys > bestKeys || (keys == bestKeys && v.Value < best.Value))
                {
                    best = v;
                    bestKeys = keys;
                }
            }
            return best;
        }
    }

    public static class StackDataUtil
    {
        /// <summary>Data that never makes two stacks different things: heat, wear, freshness, tool modes, and this mod's own switches.</summary>
        public static readonly HashSet<string> VolatileKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "temperature", "toolMode", "renderVariant", "transitionstate", "durability", "condition", "vintageeActive", "vintageeMode", "vintageeSpeed",
            "vintageeArcana", "vintageeStored", "vintageeSpeedBase"
        };

        /// <summary>A sorted copy of the stack's data without the volatile parts; null when nothing is left.</summary>
        public static TreeAttribute KeptData(ItemStack stack)
        {
            if (stack?.Attributes == null || stack.Attributes.Count == 0) return null;
            TreeAttribute copy = SortedCopy(stack.Attributes, top: true);
            return copy.Count == 0 ? null : copy;
        }

        public static TreeAttribute SortedCopy(ITreeAttribute tree, bool top)
        {
            var result = new TreeAttribute();
            foreach (var kv in tree.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (top && VolatileKeys.Contains(kv.Key)) continue;
                result[kv.Key] = kv.Value is ITreeAttribute sub ? SortedCopy(sub, false) : kv.Value.Clone();
            }
            return result;
        }

        /// <summary>True if every entry of <paramref name="part"/> is also in <paramref name="whole"/> with the same value.</summary>
        public static bool IsSubset(IWorldAccessor world, ITreeAttribute part, ITreeAttribute whole)
        {
            if (part == null) return true;
            if (whole == null) return part.Count == 0;
            foreach (var kv in part)
            {
                IAttribute other = whole[kv.Key];
                if (other == null) return false;
                if (kv.Value is ITreeAttribute subPart)
                {
                    if (!(other is ITreeAttribute subWhole) || !IsSubset(world, subPart, subWhole)) return false;
                }
                else
                {
                    try
                    {
                        if (!kv.Value.Equals(world, other)) return false;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static string Encode(ITreeAttribute tree) => Convert.ToBase64String(((TreeAttribute)tree).ToBytes());

        public static TreeAttribute Decode(string base64)
        {
            try { return TreeAttribute.CreateFromBytes(Convert.FromBase64String(base64)); }
            catch { return null; }
        }

        /// <summary>Resolves the stacks held inside a stack's data (they only know their ids after loading).</summary>
        public static void ResolveNested(IWorldAccessor world, ItemStack stack, int depth = 0)
        {
            if (world == null || depth > 6) return;
            foreach (ItemStack inner in StackContents.NestedStacks(stack))
            {
                if (inner.Collectible == null) inner.ResolveBlockOrItem(world);
                ResolveNested(world, inner, depth + 1);
            }
        }

        /// <summary>
        /// Kind detection: chiseled blocks by class; containers by class or bag behaviour; material items when their own
        /// creative inventory lists stacks that differ only by data (that's where the game keeps metal/wood/type for them).
        /// </summary>
        /// <summary>
        /// Blocks that are nothing without their data: clutter, tapestries and typed containers take their whole
        /// identity from the "type" on the stack, so a plain stack of them is a broken thing with no name.
        /// </summary>
        public static bool NeedsData(CollectibleObject c)
            => c != null && (HasClass(c, "FromAttributes") || HasClass(c, "BlockTapestry") || HasClass(c, "BlockGenericTypedContainer"));

        /// <summary>Every version of a block that needs data, from its creative stacks (resolving them if the server hasn't).</summary>
        public static IEnumerable<ItemStack> CreativeVersions(CollectibleObject c, IWorldAccessor world)
        {
            if (c?.CreativeInventoryStacks == null) yield break;
            foreach (var tab in c.CreativeInventoryStacks)
            {
                foreach (JsonItemStack js in tab?.Stacks ?? Array.Empty<JsonItemStack>())
                {
                    if (js == null) continue;
                    ItemStack s = js.ResolvedItemstack;
                    if (s == null && world != null)
                    {
                        try { if (js.Resolve(world, "Vintage-E stack data", false)) s = js.ResolvedItemstack; } catch { s = null; }
                    }
                    if (s?.Collectible == c && KeptData(s) != null) yield return s;
                }
            }
        }

        public static StackDataKind? Detect(CollectibleObject c, IWorldAccessor world = null)
        {
            if (c == null || c is IEmcStorageItem) return null;
            if (HasClass(c, "BlockMicroBlock") || HasClass(c, "BlockChisel")) return StackDataKind.Chiseled;
            if (HasClass(c, "BlockLiquidContainerBase") || HasClass(c, "BlockCookedContainerBase") || HasClass(c, "BlockMeal") ||
                HasClass(c, "BlockCrock") || HasClass(c, "BlockPie") || HasClass(c, "BlockBarrel") || HasClass(c, "BlockSmeltedContainer"))
            {
                return StackDataKind.Contents;
            }
            if (c.Attributes?["backpack"]?.Exists == true || (c.CollectibleBehaviors?.Any(b => b.GetType().Name.Contains("HeldBag")) ?? false))
            {
                return StackDataKind.Contents;
            }
            // Blocks whose kind lives on the stack (tapestries, typed chests, lanterns...): their creative stacks carry the data.
            if (HasClass(c, "FromAttributes") || HasClass(c, "BlockLantern") || HasClass(c, "BlockGenericTypedContainer") || HasClass(c, "BlockTapestry")) return StackDataKind.Material;
            if (c.CreativeInventoryStacks != null)
            {
                foreach (var tab in c.CreativeInventoryStacks)
                {
                    foreach (JsonItemStack js in tab?.Stacks ?? Array.Empty<JsonItemStack>())
                    {
                        if (js == null) continue;
                        ItemStack s = js.ResolvedItemstack;
                        // On the server the creative stacks aren't always resolved yet; resolve them here, or every
                        // typed block looks plain and gets learned (and created) without its type.
                        if (s == null && world != null)
                        {
                            try { if (js.Resolve(world, "Vintage-E stack data", false)) s = js.ResolvedItemstack; } catch { s = null; }
                        }
                        if (s?.Collectible == c && KeptData(s) != null && !StackContents.HasNestedStacks(s)) return StackDataKind.Material;
                    }
                }
            }
            return null;
        }

        static bool HasClass(CollectibleObject c, string namePart)
        {
            for (Type t = c.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.Name.Contains(namePart)) return true;
            }
            return false;
        }

        /// <summary>Chiseled blocks: so many voxels (of 16x16x16) of each material block.</summary>
        public static double ChiseledValue(IWorldAccessor world, ItemStack stack, System.Func<CollectibleObject, double> valueOf)
        {
            List<KeyValuePair<Block, long>> parts = ChiseledParts(world, stack);
            if (parts == null) return 0;
            double total = 0;
            foreach (var part in parts)
            {
                double v = part.Key == null ? 0 : valueOf(part.Key);
                if (v <= 0) return 0; // a material without a value: the whole block has none
                total += v * part.Value / 4096.0;
            }
            return total;
        }

        /// <summary>A chiseled block's materials and how many voxels (of 4096) of each it has; null if it has no voxel data.</summary>
        public static List<KeyValuePair<Block, long>> ChiseledParts(IWorldAccessor world, ItemStack stack)
        {
            if (world == null || stack?.Attributes == null) return null;
            int[] ids = null;
            if (stack.Attributes["materials"] is IntArrayAttribute intIds) ids = intIds.value;
            else if (stack.Attributes["materials"] is StringArrayAttribute codes && codes.value != null)
            {
                ids = codes.value.Select(code => world.GetBlock(new AssetLocation(code))?.Id ?? 0).ToArray();
            }
            uint[] cuboids = (stack.Attributes["cuboids"] as IntArrayAttribute)?.AsUint;
            if (ids == null || ids.Length == 0 || cuboids == null) return null;

            var voxels = new long[ids.Length];
            foreach (uint val in cuboids)
            {
                int x1 = (int)(val & 15), y1 = (int)((val >> 4) & 15), z1 = (int)((val >> 8) & 15);
                int x2 = (int)((val >> 12) & 15) + 1, y2 = (int)((val >> 16) & 15) + 1, z2 = (int)((val >> 20) & 15) + 1;
                int mat = (int)((val >> 24) & 0xFF);
                if (mat >= ids.Length || x2 <= x1 || y2 <= y1 || z2 <= z1) continue;
                voxels[mat] += (long)(x2 - x1) * (y2 - y1) * (z2 - z1);
            }
            var parts = new List<KeyValuePair<Block, long>>();
            for (int i = 0; i < ids.Length; i++)
            {
                if (voxels[i] == 0) continue;
                parts.Add(new KeyValuePair<Block, long>(world.GetBlock(ids[i]), voxels[i]));
            }
            return parts;
        }
    }
}
