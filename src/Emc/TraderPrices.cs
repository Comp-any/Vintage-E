using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Prices from every trader's buy and sell lists (any mod's traders: entity attributes "tradeProps" or
    /// "tradePropsFile"). Used for things that have no recipe path at all (clothes, loot armour, decorations...):
    /// the EMC worth of one unit of trader currency is measured on items that already have a value (median ratio),
    /// and unvalued traded items get price × that rate.
    /// </summary>
    public class TraderPrices
    {
        /// <summary>Average price per single item, in trader currency (rusty gears in vanilla). Selling and buying prices averaged.</summary>
        public Dictionary<CollectibleObject, double> PricePerItem { get; } = new Dictionary<CollectibleObject, double>();
        /// <summary>
        /// Prices of traded stacks that carry data (a clutter block of one type, a typed chest): the price is that
        /// version's, not the plain block's, so it becomes a material-specific value instead of pricing every version.
        /// </summary>
        public List<(CollectibleObject item, TreeAttribute data, double pricePerItem)> TypedPrices { get; } = new List<(CollectibleObject, TreeAttribute, double)>();
        public int TraderCount { get; private set; }

        readonly ICoreServerAPI sapi;
        readonly ILogger logger;

        public TraderPrices(ICoreServerAPI sapi, ILogger logger)
        {
            this.sapi = sapi;
            this.logger = logger;
        }

        public void Collect()
        {
            var sums = new Dictionary<CollectibleObject, (double sum, int n)>();
            foreach (EntityProperties type in sapi.World.EntityTypes ?? new List<EntityProperties>())
            {
                JObject trade = null;
                try
                {
                    string file = type.Attributes?["tradePropsFile"].AsString(null);
                    if (file != null)
                    {
                        var loc = AssetLocation.Create(file, type.Code.Domain).WithPathAppendixOnce(".json");
                        IAsset asset = sapi.Assets.TryGet(loc);
                        if (asset != null) trade = JObject.Parse(asset.ToText());
                    }
                    else if (type.Attributes?["tradeProps"].Exists == true)
                    {
                        trade = type.Attributes["tradeProps"].AsObject<JObject>(null);
                    }
                }
                catch (Exception e)
                {
                    logger.VerboseDebug("EMC: could not read trade list of {0}: {1}", type.Code, e.Message);
                }
                if (trade == null) continue;
                TraderCount++;
                foreach (string listName in new[] { "selling", "buying" })
                {
                    JToken list = Get(Get(trade, listName), "list");
                    if (!(list is JArray arr)) continue;
                    foreach (JToken entry in arr)
                    {
                        try { Add(entry, type.Code.Domain, sums); }
                        catch { /* odd entry: skip */ }
                    }
                }
            }
            foreach (var kv in sums) PricePerItem[kv.Key] = kv.Value.sum / kv.Value.n;
        }

        void Add(JToken entry, string domain, Dictionary<CollectibleObject, (double sum, int n)> sums)
        {
            string code = (string)Get(entry, "code");
            if (string.IsNullOrEmpty(code) || code.Contains('*')) return;
            bool block = string.Equals((string)Get(entry, "type"), "block", StringComparison.OrdinalIgnoreCase);
            var loc = AssetLocation.Create(code, domain);
            CollectibleObject c = block ? sapi.World.GetBlock(loc) : (CollectibleObject)sapi.World.GetItem(loc);
            if (c == null || c.Id == 0 && block) return;

            JToken priceTok = Get(entry, "price");
            double price = priceTok == null ? 0 : priceTok.Type == JTokenType.Object ? ((double?)Get(priceTok, "avg") ?? 0) : (double)priceTok;
            int size = (int?)Get(entry, "stacksize") ?? (int?)Get(entry, "stackSize") ?? (int?)Get(entry, "quantity") ?? 1;
            if (price <= 0 || size <= 0) return;
            double each = price / size;
            // A traded stack with data on it (clutter "type", say) is one version of the block, priced on its own.
            TreeAttribute data = null;
            try
            {
                if (Get(entry, "attributes") is JObject attrs && attrs.Count > 0 && new JsonObject(attrs).ToAttribute() is TreeAttribute tree)
                {
                    data = StackDataUtil.SortedCopy(tree, top: true);
                    if (data.Count == 0) data = null;
                }
            }
            catch { data = null; }
            if (data != null)
            {
                TypedPrices.Add((c, data, each));
                return;
            }
            sums[c] = sums.TryGetValue(c, out var prev) ? (prev.sum + each, prev.n + 1) : (each, 1);
        }

        static JToken Get(JToken obj, string key)
        {
            if (!(obj is JObject o)) return null;
            foreach (JProperty p in o.Properties())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
            }
            return null;
        }

        /// <summary>
        /// EMC per unit of currency, measured on traded items that already have a value: the median of value ÷ price,
        /// so a few odd prices can't skew it. 0 if there aren't enough items to measure.
        /// </summary>
        public double MeasureRate(Dictionary<CollectibleObject, double> values, out int samples)
        {
            var ratios = new List<double>();
            foreach (var kv in PricePerItem)
            {
                if (values.TryGetValue(kv.Key, out double v) && v > 0 && kv.Value > 0) ratios.Add(v / kv.Value);
            }
            samples = ratios.Count;
            if (ratios.Count < 5) return 0;
            ratios.Sort();
            int mid = ratios.Count / 2;
            return ratios.Count % 2 == 1 ? ratios[mid] : (ratios[mid - 1] + ratios[mid]) / 2;
        }
    }
}
