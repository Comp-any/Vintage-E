using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>The live EMC table for one side. The server fills it from the mapper; clients get a copy over the network.</summary>
    public class EmcRegistry
    {
        Dictionary<CollectibleObject, double> values = new Dictionary<CollectibleObject, double>();

        /// <summary>Whether damaged tools are worth proportionally less (mirrors the server config on clients).</summary>
        public bool DurabilityScales { get; set; } = true;

        /// <summary>Whether ProjectE's sounds are on (mirrors the server config on clients).</summary>
        public bool ProjectESounds { get; set; }

        /// <summary>
        /// Items that keep their EMC value (recipes still use it) but can't be burned for EMC, learned or created
        /// (the server's NoTransmute list, matched to codes; mirrored on clients).
        /// </summary>
        public HashSet<CollectibleObject> NoTransmute { get; set; } = new HashSet<CollectibleObject>();

        public bool IsNoTransmute(CollectibleObject c) => c != null && NoTransmute.Contains(c);

        /// <summary>Whether this item can be burned, learned or created: it has a value and isn't on the NoTransmute list.</summary>
        public bool CanTransmute(CollectibleObject c) => c != null && !NoTransmute.Contains(c) && GetValue(c) > 0;

        public int Count => values.Count;

        /// <summary>A number that changes whenever any value does (the same on server and client for the same table), for /emc diagnose.</summary>
        public long Fingerprint()
        {
            unchecked
            {
                long h = 1469598103934665603L;
                foreach (var kv in values.Where(k => k.Key?.Code != null).OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    foreach (char ch in kv.Key.Code.ToString()) h = (h ^ ch) * 1099511628211L;
                    h = (h ^ (long)Math.Round(kv.Value * 1000)) * 1099511628211L;
                }
                return h;
            }
        }

        public IReadOnlyDictionary<CollectibleObject, double> All => values;

        public void Replace(Dictionary<CollectibleObject, double> newValues)
        {
            values = new Dictionary<CollectibleObject, double>(newValues);
        }

        /// <summary>Value of one pristine item, or 0 if it has none.</summary>
        public double GetValue(CollectibleObject collectible)
        {
            if (collectible == null) return 0;
            return values.TryGetValue(collectible, out double v) ? v : 0;
        }

        public bool HasValue(CollectibleObject collectible) => collectible != null && values.ContainsKey(collectible);

        /// <summary>
        /// For liquid portion items (water, brine, ale...): how many items make one litre (100 for vanilla liquids).
        /// 0 for anything that isn't a liquid. Values are stored per item, so a litre is worth value × this.
        /// </summary>
        public static double ItemsPerLitre(CollectibleObject c)
        {
            JsonObject props = c?.Attributes?["waterTightContainerProps"];
            if (props == null || !props.Exists) return 0;
            double perLitre = props["itemsPerLitre"].AsDouble(1);
            return perLitre > 0 ? perLitre : 1;
        }

        /// <summary>Which items carry contents/materials and the mode each uses, plus material-specific values.</summary>
        public StackDataTable StackData { get; set; } = new StackDataTable();

        /// <summary>The world, for resolving what's inside stacks and chiseled blocks' materials (set on both sides).</summary>
        public IWorldAccessor World { get; set; }

        /// <summary>
        /// Value of one item of this stack - what burning it gives and what a copy of it costs - accounting for wear and,
        /// for items that carry data, their mode: item only (the plain item), item + contents (material and contents),
        /// item price (material, not contents).
        /// </summary>
        public double GetValue(ItemStack stack) => GetValue(stack, 0);

        double GetValue(ItemStack stack, int depth)
        {
            if (stack?.Collectible == null || depth > 6) return 0;
            CollectibleObject c = stack.Collectible;
            StackDataMode mode = StackData.ModeOf(c);
            if (mode == StackDataMode.Item || !StackData.CarriesData(c)) return Worn(stack, PlainValue(stack));

            double own = ItemValue(stack);
            if (own <= 0 && StackData.KindOf(c) != StackDataKind.Contents) return 0;
            double total = Worn(stack, own);
            if (mode == StackDataMode.Full) total += ContentsValue(stack, depth);
            return total;
        }

        /// <summary>The plain item's value (Klein stars include what they hold); no wear applied.</summary>
        public double PlainValue(ItemStack stack)
        {
            double v = GetValue(stack.Collectible);
            if (stack.Collectible is IEmcStorageItem) v += StoredEmc.Get(stack); // Klein stars are worth what they hold
            return v;
        }

        /// <summary>The item itself, made of what its data says (a gold round shield, a chiseled block's voxels), without its contents.</summary>
        public double ItemValue(ItemStack stack)
        {
            CollectibleObject c = stack.Collectible;
            switch (StackData.KindOf(c))
            {
                case StackDataKind.Chiseled:
                    return StackDataUtil.ChiseledValue(World, stack, GetValue);
                case StackDataKind.Material:
                    VariantValue variant = StackData.FindVariant(World, stack);
                    return variant?.Value ?? PlainValue(stack);
                default:
                    return PlainValue(stack);
            }
        }

        /// <summary>
        /// What's inside: every stack held in the data, each valued by its own mode. Meals and pies store one serving's
        /// ingredients, so those are multiplied by the number of servings.
        /// </summary>
        public double ContentsValue(ItemStack stack, int depth = 0)
        {
            // A crucible of molten metal holds "units" of its output (100 units = one ingot).
            if (stack.Attributes?["units"] is IntAttribute units && stack.Attributes.GetItemstack("output") is ItemStack metal)
            {
                if (metal.Collectible == null && (World == null || !metal.ResolveBlockOrItem(World))) return 0;
                return GetValue(metal, depth + 1) * units.value / 100.0;
            }
            double sum = 0;
            foreach (ItemStack inner in StackContents.NestedStacks(stack))
            {
                if (inner.Collectible == null && (World == null || !inner.ResolveBlockOrItem(World))) continue;
                sum += GetValue(inner, depth + 1) * inner.StackSize;
            }
            double servings = stack.Attributes?.GetDecimal("quantityServings", 0) ?? 0;
            if (servings > 0) sum *= servings;
            return sum;
        }

        double Worn(ItemStack stack, double v)
        {
            if (v <= 0 || !DurabilityScales) return v;
            int max = stack.Collectible.GetMaxDurability(stack);
            if (max > 1)
            {
                int remaining = stack.Collectible.GetRemainingDurability(stack);
                v *= Math.Clamp(remaining / (double)max, 0, 1);
            }
            // Clothes wear by condition (0-100%) instead of durability.
            double condition = ClothingCondition(stack);
            if (condition >= 0) v *= condition;
            return v;
        }

        /// <summary>
        /// A piece of clothing's condition, 0 to 1 (the game's "condition" attribute; a new one is 1), or -1 for anything
        /// that isn't clothing. Only clothes that keep you warm have a condition.
        /// </summary>
        public static double ClothingCondition(ItemStack stack)
        {
            if (stack?.Collectible?.Attributes == null) return -1;
            if (stack.Collectible.Attributes["warmth"].AsFloat(0) == 0) return -1;
            return Math.Clamp(stack.Attributes?.GetFloat("condition", 1) ?? 1, 0, 1);
        }

        public EmcTablePacket ToPacket()
        {
            var itemIds = new List<int>();
            var itemValues = new List<double>();
            var blockIds = new List<int>();
            var blockValues = new List<double>();
            foreach (var kv in values)
            {
                if (kv.Key.ItemClass == EnumItemClass.Block) { blockIds.Add(kv.Key.Id); blockValues.Add(kv.Value); }
                else { itemIds.Add(kv.Key.Id); itemValues.Add(kv.Value); }
            }
            var dataKeys = new List<string>();
            var dataKindModes = new List<byte>();
            foreach (var kv in StackData.Kinds)
            {
                string key = PlayerEmc.KeyOf(kv.Key);
                if (key == null) continue;
                dataKeys.Add(key);
                dataKindModes.Add((byte)((int)kv.Value * 4 + (int)StackData.ModeOf(kv.Key)));
            }
            var variantKeys = new List<string>();
            var variantData = new List<string>();
            var variantValues = new List<double>();
            foreach (var kv in StackData.Variants)
            {
                string key = PlayerEmc.KeyOf(kv.Key);
                if (key == null) continue;
                foreach (VariantValue v in kv.Value)
                {
                    variantKeys.Add(key);
                    variantData.Add(StackDataUtil.Encode(v.Attributes));
                    variantValues.Add(v.Value);
                }
            }
            return new EmcTablePacket
            {
                ItemIds = itemIds.ToArray(),
                ItemValues = itemValues.ToArray(),
                BlockIds = blockIds.ToArray(),
                BlockValues = blockValues.ToArray(),
                DurabilityScales = DurabilityScales,
                ProjectESounds = ProjectESounds,
                NoTransmuteKeys = NoTransmute.Select(PlayerEmc.KeyOf).Where(k => k != null).ToArray(),
                DataKeys = dataKeys.ToArray(),
                DataKindModes = dataKindModes.ToArray(),
                VariantKeys = variantKeys.ToArray(),
                VariantData = variantData.ToArray(),
                VariantValues = variantValues.ToArray()
            };
        }

        public void LoadFromPacket(EmcTablePacket packet, IWorldAccessor world)
        {
            var fresh = new Dictionary<CollectibleObject, double>();
            int[] ids = packet.ItemIds ?? Array.Empty<int>();
            double[] vals = packet.ItemValues ?? Array.Empty<double>();
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                Item item = world.GetItem(ids[i]);
                if (item?.Code != null) fresh[item] = vals[i];
            }
            ids = packet.BlockIds ?? Array.Empty<int>();
            vals = packet.BlockValues ?? Array.Empty<double>();
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                Block block = world.GetBlock(ids[i]);
                if (block?.Code != null) fresh[block] = vals[i];
            }
            values = fresh;
            DurabilityScales = packet.DurabilityScales;
            ProjectESounds = packet.ProjectESounds;
            var noTransmute = new HashSet<CollectibleObject>();
            foreach (string key in packet.NoTransmuteKeys ?? Array.Empty<string>())
            {
                CollectibleObject c = PlayerEmc.Resolve(world, key);
                if (c != null) noTransmute.Add(c);
            }
            NoTransmute = noTransmute;
            World = world;

            var table = new StackDataTable();
            string[] keys = packet.DataKeys ?? Array.Empty<string>();
            byte[] kindModes = packet.DataKindModes ?? Array.Empty<byte>();
            for (int i = 0; i < keys.Length && i < kindModes.Length; i++)
            {
                CollectibleObject c = PlayerEmc.Resolve(world, keys[i]);
                if (c == null) continue;
                table.Kinds[c] = (StackDataKind)(kindModes[i] / 4);
                table.Modes[c] = (StackDataMode)(kindModes[i] % 4);
            }
            keys = packet.VariantKeys ?? Array.Empty<string>();
            string[] data = packet.VariantData ?? Array.Empty<string>();
            double[] vv = packet.VariantValues ?? Array.Empty<double>();
            for (int i = 0; i < keys.Length && i < data.Length && i < vv.Length; i++)
            {
                CollectibleObject c = PlayerEmc.Resolve(world, keys[i]);
                TreeAttribute tree = StackDataUtil.Decode(data[i]);
                if (c == null || tree == null) continue;
                if (!table.Variants.TryGetValue(c, out var list)) table.Variants[c] = list = new List<VariantValue>();
                list.Add(new VariantValue { Attributes = tree, Value = vv[i] });
            }
            StackData = table;
        }
    }

    public static class EmcFormat
    {
        /// <summary>1234567 → "1,234,567"; 1234.567 → "1,234.56"; 12.5 → "12.5"; 0.0312 → "0.031".</summary>
        /// <summary>A short form for very large numbers (1.23 million, 4.5 billion), for places where the full number won't fit.</summary>
        public static string Short(double v)
        {
            if (v < 1_000_000) return Format(v);
            if (v < 1_000_000_000) return (Math.Floor(v / 10_000) / 100).ToString("0.##", CultureInfo.InvariantCulture) + " " + Lang.Get("vintagee:number-million");
            if (v < 1_000_000_000_000) return (Math.Floor(v / 10_000_000) / 100).ToString("0.##", CultureInfo.InvariantCulture) + " " + Lang.Get("vintagee:number-billion");
            return (Math.Floor(v / 10_000_000_000) / 100).ToString("0.##", CultureInfo.InvariantCulture) + " " + Lang.Get("vintagee:number-trillion");
        }

        public static string Format(double v)
        {
            // Two decimals at most (1,234.56), cut off rather than rounded, so a total never shows more than there is.
            // Burning things worth fractions adds up, and the total should show it.
            if (v >= 1) return (Math.Floor(v * 100 + 1e-6) / 100).ToString(v >= 1000 ? "#,0.##" : "0.##", CultureInfo.InvariantCulture);
            return (Math.Floor(v * 1000 + 1e-6) / 1000).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Stacks that carry other stacks inside their attributes (bags, filled pots, crates...) are containers with contents.
    /// </summary>
    public static class StackContents
    {
        public static bool HasNestedStacks(ItemStack stack)
        {
            return stack?.Attributes != null && ContainsStack(stack.Attributes, 0);
        }

        /// <summary>Every stack stored inside this stack's attributes (bucket contents, bag slots, pot ingredients...).</summary>
        public static List<ItemStack> NestedStacks(ItemStack stack)
        {
            var list = new List<ItemStack>();
            if (stack?.Attributes != null) Collect(stack.Attributes, list, 0);
            return list;
        }

        static void Collect(ITreeAttribute tree, List<ItemStack> list, int depth)
        {
            if (depth > 8) return;
            foreach (var kv in tree)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) list.Add(isa.value);
                else if (kv.Value is ITreeAttribute sub) Collect(sub, list, depth + 1);
                else if (kv.Value is TreeArrayAttribute arr && arr.value != null)
                {
                    foreach (TreeAttribute t in arr.value) if (t != null) Collect(t, list, depth + 1);
                }
            }
        }

        static bool ContainsStack(ITreeAttribute tree, int depth)
        {
            if (depth > 8) return false;
            foreach (var kv in tree)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) return true;
                if (kv.Value is ITreeAttribute sub && ContainsStack(sub, depth + 1)) return true;
                if (kv.Value is TreeArrayAttribute arr && arr.value != null)
                {
                    foreach (TreeAttribute t in arr.value) if (t != null && ContainsStack(t, depth + 1)) return true;
                }
            }
            return false;
        }
    }
}
