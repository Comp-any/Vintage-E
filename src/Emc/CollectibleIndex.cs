using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// Lookup tables over every registered block and item so that wildcard codes
    /// ("game:plank-*", "*:ingot-*") can be expanded without scanning tens of thousands of entries each time.
    /// </summary>
    public class CollectibleIndex
    {
        readonly List<CollectibleObject> all = new List<CollectibleObject>();
        readonly Dictionary<string, List<CollectibleObject>> byDomain = new Dictionary<string, List<CollectibleObject>>();
        readonly Dictionary<string, List<CollectibleObject>> byFirstSegment = new Dictionary<string, List<CollectibleObject>>();
        readonly Dictionary<string, List<CollectibleObject>> byCode = new Dictionary<string, List<CollectibleObject>>();

        public IReadOnlyList<CollectibleObject> All => all;

        public CollectibleIndex(IEnumerable<CollectibleObject> collectibles)
        {
            foreach (CollectibleObject c in collectibles)
            {
                if (c?.Code == null || c.Id == 0 && c is Block) continue; // block 0 is air
                all.Add(c);
                Add(byDomain, c.Code.Domain, c);
                Add(byCode, c.Code.ToString(), c);
                Add(byFirstSegment, c.Code.Domain + ":" + FirstSegment(c.Code.Path), c);
            }
        }

        /// <summary>Real, obtainable content: anything shown in a creative inventory tab.</summary>
        public static bool IsListed(CollectibleObject c) => (c.CreativeInventoryTabs?.Length ?? 0) > 0 || (c.CreativeInventoryStacks?.Length ?? 0) > 0;

        static void Add(Dictionary<string, List<CollectibleObject>> dict, string key, CollectibleObject c)
        {
            if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<CollectibleObject>();
            list.Add(c);
        }

        static string FirstSegment(string path)
        {
            int dash = path.IndexOf('-');
            return dash < 0 ? path : path.Substring(0, dash);
        }

        /// <summary>
        /// Every collectible whose code matches <paramref name="pattern"/>. The pattern may start with
        /// "item:" or "block:" to restrict the type, and may use '*' wildcards in domain and path.
        /// </summary>
        public IEnumerable<CollectibleObject> Match(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return Enumerable.Empty<CollectibleObject>();
            pattern = pattern.Trim();
            EnumItemClass? type = null;
            if (pattern.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { type = EnumItemClass.Item; pattern = pattern.Substring(5); }
            else if (pattern.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { type = EnumItemClass.Block; pattern = pattern.Substring(6); }

            var loc = new AssetLocation(pattern);
            // A broken @regex in a settings entry throws inside the game's matcher; it then matches nothing.
            try { return Candidates(loc).Where(c => (type == null || c.ItemClass == type) && WildcardUtil.Match(loc, c.Code)).ToList(); }
            catch (ArgumentException) { return Enumerable.Empty<CollectibleObject>(); }
        }

        /// <summary>Whether one collectible matches <paramref name="pattern"/> (same rules as <see cref="Match"/>).</summary>
        public static bool Matches(string pattern, CollectibleObject c)
        {
            if (c?.Code == null || string.IsNullOrWhiteSpace(pattern)) return false;
            pattern = pattern.Trim();
            if (pattern.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { if (c.ItemClass != EnumItemClass.Item) return false; pattern = pattern.Substring(5); }
            else if (pattern.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { if (c.ItemClass != EnumItemClass.Block) return false; pattern = pattern.Substring(6); }
            try { return WildcardUtil.Match(new AssetLocation(pattern), c.Code); }
            catch { return false; }
        }

        /// <summary>A superset of the collectibles that could match <paramref name="loc"/>, narrowed by its literal prefix.</summary>
        public IEnumerable<CollectibleObject> Candidates(AssetLocation loc)
        {
            string path = loc.Path ?? "";
            bool anyDomain = loc.Domain == "*";

            if (path.StartsWith("@")) // regex: no cheap narrowing possible
            {
                return anyDomain ? all : (byDomain.TryGetValue(loc.Domain, out var d) ? d : Empty);
            }

            int wc = path.IndexOfAny(WildcardChars);
            if (wc < 0 && !anyDomain)
            {
                return byCode.TryGetValue(loc.ToString(), out var exact) ? exact : Empty;
            }

            string prefix = wc < 0 ? path : path.Substring(0, wc);
            IEnumerable<CollectibleObject> pool;
            int dash = prefix.IndexOf('-');
            if (!anyDomain && dash > 0)
            {
                pool = byFirstSegment.TryGetValue(loc.Domain + ":" + prefix.Substring(0, dash), out var seg) ? seg : Empty;
            }
            else if (!anyDomain)
            {
                pool = byDomain.TryGetValue(loc.Domain, out var d) ? d : Empty;
            }
            else
            {
                pool = all;
            }
            if (prefix.Length == 0) return pool;
            return pool.Where(c => c.Code.Path.StartsWith(prefix, StringComparison.Ordinal));
        }

        static readonly char[] WildcardChars = { '*', '?', '{', '(' };
        static readonly List<CollectibleObject> Empty = new List<CollectibleObject>();
    }
}
