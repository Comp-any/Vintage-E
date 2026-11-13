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

        int revision;
        /// <summary>
        /// Goes up whenever anything a player's game is sent changes: the values, the settings that travel with them,
        /// the NoTransmute list, the contents modes. What is worked out from the table (the packet for players, a
        /// condenser's copy of its target) is kept until this moves.
        /// </summary>
        public int Revision => revision;
        /// <summary>Says the table changed. The setters here do it themselves; call it after changing <see cref="StackData"/> or <see cref="NoTransmute"/> in place.</summary>
        public void Touch() => System.Threading.Interlocked.Increment(ref revision);

        bool durabilityScales = true, projectESounds;
        double trinketCostMultiplier = 1, machineSpeedMultiplier = 1, watchSpeedUp = 10, watchSlowDown = 3, tableUseRange = 8;
        string[] collectorFuels;
        HashSet<CollectibleObject> noTransmute = new HashSet<CollectibleObject>();
        StackDataTable stackData = new StackDataTable();

        /// <summary>Whether damaged tools are worth proportionally less (mirrors the server config on clients).</summary>
        public bool DurabilityScales { get => durabilityScales; set { durabilityScales = value; Touch(); } }

        /// <summary>Whether ProjectE's sounds are on (mirrors the server config on clients).</summary>
        public bool ProjectESounds { get => projectESounds; set { projectESounds = value; Touch(); } }
        /// <summary>The server's multipliers (sent with the table), for tooltips that quote costs and speeds.</summary>
        public double TrinketCostMultiplier { get => trinketCostMultiplier; set { trinketCostMultiplier = value; Touch(); } }
        public double MachineSpeedMultiplier { get => machineSpeedMultiplier; set { machineSpeedMultiplier = value; Touch(); } }
        public double WatchSpeedUp { get => watchSpeedUp; set { watchSpeedUp = value; Touch(); } }
        public double WatchSlowDown { get => watchSlowDown; set { watchSlowDown = value; Touch(); } }
        /// <summary>How far from a Transmutation Table its window works (mirrors the server config on clients).</summary>
        public double TableUseRange { get => tableUseRange; set { tableUseRange = value; Touch(); } }
        /// <summary>The fuels Energy Collectors upgrade (mirrors the server config on clients); null until the server's table arrives.</summary>
        public string[] CollectorFuels { get => collectorFuels; set { collectorFuels = value; Touch(); } }

        /// <summary>
        /// Items that keep their EMC value (recipes still use it) but can't be burned for EMC, learned or created
        /// (the server's NoTransmute list, matched to codes; mirrored on clients).
        /// </summary>
        public HashSet<CollectibleObject> NoTransmute { get => noTransmute; set { noTransmute = value ?? new HashSet<CollectibleObject>(); Touch(); } }

        public bool IsNoTransmute(CollectibleObject c) => c != null && noTransmute.Contains(c);

        /// <summary>Whether this item can be burned, learned or created: it has a value and isn't on the NoTransmute list.</summary>
        public bool CanTransmute(CollectibleObject c) => c != null && !noTransmute.Contains(c) && GetValue(c) > 0;

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
            Touch();
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
        public StackDataTable StackData { get => stackData; set { stackData = value ?? new StackDataTable(); Touch(); } }

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
            if (mode == StackDataMode.Full)
            {
                // Something inside with no value makes the whole thing unvalued: otherwise a bucket of an unvalued
                // liquid would be worth the bucket, and making one would give the liquid away.
                if (!AllContentsValued(stack, depth)) return 0;
                total += ContentsValue(stack, depth);
            }
            return total;
        }

        /// <summary>False if anything stored inside has no EMC value (and isn't on the free list).</summary>
        public bool AllContentsValued(ItemStack stack, int depth = 0)
        {
            if (stack.Attributes?["units"] is IntAttribute && stack.Attributes.GetItemstack("output") is ItemStack metal)
            {
                if (metal.Collectible == null && (World == null || !metal.ResolveBlockOrItem(World))) return false;
                return GetValue(metal, depth + 1) > 0;
            }
            foreach (ItemStack inner in StackContents.NestedStacks(stack))
            {
                if (inner.Collectible == null && (World == null || !inner.ResolveBlockOrItem(World))) return false;
                if (inner.Collectible == null) return false;
                if (GetValue(inner, depth + 1) <= 0) return false;
            }
            return true;
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
                    if (variant != null) return variant.Value;
                    double plain = PlainValue(stack);
                    // No value for this version and none for the plain block (a tapestry pattern no trader sells):
                    // priced like its priced siblings, the middle of them.
                    if (plain <= 0 && StackData.Variants.TryGetValue(c, out var siblings) && siblings.Count > 0)
                    {
                        var sorted = siblings.Select(v => v.Value).Where(v => v > 0).OrderBy(v => v).ToList();
                        if (sorted.Count > 0) return sorted[sorted.Count / 2];
                    }
                    return plain;
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

        sealed class PacketAt
        {
            public EmcTablePacket Packet;
            public int Revision;
        }

        volatile PacketAt lastPacket;

        /// <summary>
        /// The table as players' games are sent it. Built once per change of the table (<see cref="Revision"/>): it
        /// used to be put together again - 1.5 MB of lists for a base game - for every player who joined and every
        /// resend. Nobody may change what this returns.
        /// </summary>
        public EmcTablePacket ToPacket()
        {
            int at = revision;
            PacketAt last = lastPacket;
            if (last != null && last.Revision == at) return last.Packet;
            EmcTablePacket packet = BuildPacket();
            lastPacket = new PacketAt { Packet = packet, Revision = at };
            return packet;
        }

        /// <summary>Puts the table's packet together from scratch (what <see cref="ToPacket"/> did every time before 1.21.1).</summary>
        public EmcTablePacket BuildPacket()
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
                TrinketCostMultiplier = TrinketCostMultiplier,
                MachineSpeedMultiplier = MachineSpeedMultiplier,
                WatchSpeedUp = WatchSpeedUp,
                WatchSlowDown = WatchSlowDown,
                TableUseRange = TableUseRange,
                CollectorFuels = CollectorFuels,
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
            // Ids the server sent; this game may hold fewer (a mismatch), so stay inside its lists and skip bad numbers.
            int itemCount = world.Items?.Count ?? 0, blockCount = world.Blocks?.Count ?? 0;
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                if (ids[i] < 0 || ids[i] >= itemCount || double.IsNaN(vals[i]) || double.IsInfinity(vals[i])) continue;
                Item item = world.GetItem(ids[i]);
                if (item?.Code != null) fresh[item] = vals[i];
            }
            ids = packet.BlockIds ?? Array.Empty<int>();
            vals = packet.BlockValues ?? Array.Empty<double>();
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                if (ids[i] < 0 || ids[i] >= blockCount || double.IsNaN(vals[i]) || double.IsInfinity(vals[i])) continue;
                Block block = world.GetBlock(ids[i]);
                if (block?.Code != null) fresh[block] = vals[i];
            }
            values = fresh;
            Touch();
            DurabilityScales = packet.DurabilityScales;
            ProjectESounds = packet.ProjectESounds;
            TrinketCostMultiplier = packet.TrinketCostMultiplier >= 0 ? packet.TrinketCostMultiplier : 1; // 0: trinkets are free on this server
            MachineSpeedMultiplier = packet.MachineSpeedMultiplier > 0 ? packet.MachineSpeedMultiplier : 1;
            WatchSpeedUp = packet.WatchSpeedUp > 0 ? packet.WatchSpeedUp : 10;
            WatchSlowDown = packet.WatchSlowDown > 0 ? packet.WatchSlowDown : 3;
            TableUseRange = packet.TableUseRange > 0 ? packet.TableUseRange : 8;
            CollectorFuels = packet.CollectorFuels;
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
