using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// A player's personal EMC and transmutation knowledge. Stored in the player entity's watched attributes,
    /// so it is saved with the world and synced to that player's client automatically.
    /// Knowledge entries are "i:domain:code" for items and "b:domain:code" for blocks, so they survive id changes.
    /// </summary>
    public static class PlayerEmc
    {
        public const string EmcKey = "vintageeEmc";
        public const string KnownKey = "vintageeKnown";

        static SyncedTreeAttribute Attrs(IPlayer player) => player?.Entity?.WatchedAttributes;

        public static double GetEmc(IPlayer player) => Attrs(player)?.GetDouble(EmcKey, 0) ?? 0;

        public static void SetEmc(IPlayer player, double value)
        {
            var attrs = Attrs(player);
            if (attrs == null) return;
            if (double.IsNaN(value) || value < 0) value = 0;
            attrs.SetDouble(EmcKey, value);
            attrs.MarkPathDirty(EmcKey);
        }

        public static void AddEmc(IPlayer player, double amount) => SetEmc(player, GetEmc(player) + amount);

        public static string[] GetKnown(IPlayer player) => Attrs(player)?.GetStringArray(KnownKey, Array.Empty<string>()) ?? Array.Empty<string>();

        /// <summary>A lookup set per knowledge array (the array is replaced whenever knowledge changes, so the set never goes stale).</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string[], HashSet<string>> knownSets = new System.Runtime.CompilerServices.ConditionalWeakTable<string[], HashSet<string>>();

        static HashSet<string> KnownSet(string[] known) => knownSets.GetValue(known, k => new HashSet<string>(k, StringComparer.Ordinal));

        public static bool Knows(IPlayer player, string key) => key != null && KnownSet(GetKnown(player)).Contains(key);

        /// <summary>Adds the key; returns true if it was new.</summary>
        public static bool Learn(IPlayer player, string key)
        {
            var attrs = Attrs(player);
            if (attrs == null || string.IsNullOrEmpty(key)) return false;
            string[] known = GetKnown(player);
            if (KnownSet(known).Contains(key)) return false;
            var list = new List<string>(known) { key };
            attrs.SetStringArray(KnownKey, list.ToArray());
            attrs.MarkPathDirty(KnownKey);
            return true;
        }

        /// <summary>Learns many keys at once; returns how many were new.</summary>
        public static int LearnMany(IPlayer player, IEnumerable<string> keys)
        {
            var attrs = Attrs(player);
            if (attrs == null) return 0;
            var set = new HashSet<string>(GetKnown(player));
            int before = set.Count;
            foreach (string k in keys) if (!string.IsNullOrEmpty(k)) set.Add(k);
            if (set.Count == before) return 0;
            attrs.SetStringArray(KnownKey, set.ToArray());
            attrs.MarkPathDirty(KnownKey);
            return set.Count - before;
        }

        public static bool Forget(IPlayer player, string key)
        {
            var attrs = Attrs(player);
            if (attrs == null) return false;
            string[] known = GetKnown(player);
            if (!KnownSet(known).Contains(key)) return false;
            attrs.SetStringArray(KnownKey, known.Where(k => k != key).ToArray());
            attrs.MarkPathDirty(KnownKey);
            return true;
        }

        public static string KeyOf(CollectibleObject collectible)
        {
            if (collectible?.Code == null) return null;
            return (collectible.ItemClass == EnumItemClass.Block ? "b:" : "i:") + collectible.Code.ToString();
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 3 || key[1] != ':') return null;
            if (IsExact(key)) return Resolve(world, PlainKeyOf(key));
            var code = new AssetLocation(key.Substring(2));
            return key[0] == 'b' ? world.GetBlock(code) : (CollectibleObject)world.GetItem(code);
        }

        /// <summary>
        /// Exact versions (a filled bucket, a gold round shield, a chiseled block) are known as
        /// "e:" + the plain key + "|" + their stored data (base64), so each version is its own entry.
        /// </summary>
        public const string ExactPrefix = "e:";

        public static bool IsExact(string key) => key != null && key.StartsWith(ExactPrefix, StringComparison.Ordinal) && key.IndexOf('|') > 2;

        public static string ExactKey(CollectibleObject c, TreeAttribute data)
        {
            string plain = KeyOf(c);
            return plain == null || data == null ? plain : ExactPrefix + plain + "|" + StackDataUtil.Encode(data);
        }

        /// <summary>"i:game:bucket" for both "i:game:bucket" and an exact "e:i:game:bucket|...".</summary>
        public static string PlainKeyOf(string key)
        {
            if (!IsExact(key)) return key;
            int bar = key.IndexOf('|');
            return key.Substring(ExactPrefix.Length, bar - ExactPrefix.Length);
        }

        /// <summary>A single item for a knowledge key, with its stored data for exact versions (null if it no longer exists).</summary>
        public static ItemStack ResolveStack(IWorldAccessor world, string key)
        {
            CollectibleObject c = Resolve(world, key);
            if (c == null) return null;
            ItemStack stack;
            try { stack = new ItemStack(c, 1); }
            catch { return null; }
            if (IsExact(key))
            {
                TreeAttribute data = StackDataUtil.Decode(key.Substring(key.IndexOf('|') + 1));
                if (data == null) return null;
                foreach (var kv in data) stack.Attributes[kv.Key] = kv.Value.Clone();
                StackDataUtil.ResolveNested(world, stack);
            }
            return stack;
        }

        /// <summary>
        /// How many exact versions a player knows that count against MaxExactKnowledge: a filled bucket, a gold
        /// shield, a chiseled block. A block that is nothing without its data (clutter, a tapestry pattern, a fruit
        /// tree cutting) is only ever known as an exact version, so those don't count: they are the plain items of
        /// their kind, and with the Tome of Knowledge there are thousands of them.
        /// </summary>
        public static int CountExact(IPlayer player)
        {
            IWorldAccessor world = player?.Entity?.World;
            int n = 0;
            foreach (string key in GetKnown(player))
            {
                if (!IsExact(key)) continue;
                if (world != null && NeedsDataCached(Resolve(world, key), world)) continue;
                n++;
            }
            return n;
        }

        static readonly Dictionary<CollectibleObject, bool> needsData = new Dictionary<CollectibleObject, bool>();
        static bool NeedsDataCached(CollectibleObject c, IWorldAccessor world)
        {
            if (c == null) return false;
            lock (needsData)
            {
                if (!needsData.TryGetValue(c, out bool v)) needsData[c] = v = StackDataUtil.NeedsData(c, world);
                return v;
            }
        }
    }
}
