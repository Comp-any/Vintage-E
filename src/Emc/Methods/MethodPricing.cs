using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>What one unit of an energy unit costs in EMC, and where that number came from.</summary>
    public sealed class MethodUnitPrice
    {
        public string Code;
        public string Name;
        public string ModId;
        public double Price;
        /// <summary>setting, generator, suggestion, none or undeclared.</summary>
        public string Source;
        /// <summary>The source in words ("your setting EnergyEmc", "the steam engine burning charcoal (32 EMC) for 16000").</summary>
        public string SourceText;
        /// <summary>What each declared generator would make it cost, in words, with the one used first.</summary>
        public List<string> Generators = new List<string>();
        /// <summary>The cheapest generator's price, even when a setting won (for the doctor's suggested price).</summary>
        public double GeneratorPrice;
        public double? Suggested;
        public bool Declared;
    }

    /// <summary>The prices a mapping charges for other mods' crafting methods' extra costs.</summary>
    public sealed class MethodPrices
    {
        public readonly Dictionary<string, MethodUnitPrice> Units = new Dictionary<string, MethodUnitPrice>(StringComparer.Ordinal);
        /// <summary>EMC per °C·s of heat: what the reference fuels (charcoal, coal) are valued at per degree-second of burning.</summary>
        public double HeatRate;
        public string HeatSource = "";
        /// <summary>EMC per second of machine time (the MethodEmcPerSecond setting).</summary>
        public double SecondRate;
        public bool ChargeEnergy = true, ChargeHeat = true, ChargeWear = true;
        readonly Dictionary<string, double> configPrices;

        public MethodPrices(Dictionary<string, double> configPrices) { this.configPrices = configPrices ?? new Dictionary<string, double>(); }

        /// <summary>The price of a unit; one no document declares gets the setting's price, or 0.</summary>
        public MethodUnitPrice Unit(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            if (Units.TryGetValue(code, out MethodUnitPrice p)) return p;
            p = new MethodUnitPrice { Code = code, Name = code, Declared = false };
            if (MethodPricing.ConfigPrice(configPrices, code, out double v)) { p.Price = v; p.Source = "setting"; p.SourceText = "your setting EnergyEmc"; }
            else { p.Price = 0; p.Source = "undeclared"; p.SourceText = "no document declares this unit and EnergyEmc has no price for it"; }
            lock (Units) Units[code] = p;
            return p;
        }
    }

    public static class MethodPricing
    {
        /// <summary>
        /// Prices every declared energy unit, heat and machine time from the settings and the base values alone (never
        /// from solved values), so they are known before the mapping and can't feed back into it.
        /// </summary>
        public static MethodPrices Compute(MethodSnapshot methods, VintageEConfig config, Dictionary<CollectibleObject, double> fixedValues, CollectibleIndex index, IEnumerable<string> fuelReference)
        {
            var prices = new MethodPrices(config.EnergyEmc)
            {
                ChargeEnergy = config.ChargeMethodEnergy,
                ChargeHeat = config.ChargeMethodHeat,
                ChargeWear = config.ChargeMethodWear,
                SecondRate = Math.Max(0, config.MethodEmcPerSecond)
            };

            // Heat: the median value per °C·s of burning of the reference fuels that have a base value.
            var rates = new List<double>();
            var named = new List<string>();
            foreach (string pattern in fuelReference ?? Enumerable.Empty<string>())
            {
                foreach (CollectibleObject c in index.Match(pattern))
                {
                    double energy = BurnEnergy(c);
                    if (energy > 0 && fixedValues.TryGetValue(c, out double v) && v > 0) { rates.Add(v / energy); if (named.Count < 3) named.Add(c.Code.ToShortString()); }
                }
            }
            if (rates.Count > 0)
            {
                rates.Sort();
                prices.HeatRate = rates.Count % 2 == 1 ? rates[rates.Count / 2] : (rates[rates.Count / 2 - 1] + rates[rates.Count / 2]) / 2;
                prices.HeatSource = string.Format(CultureInfo.InvariantCulture, "the median of {0} reference fuels' value per °C·s ({1})", rates.Count, string.Join(", ", named) + (rates.Count > named.Count ? ", …" : ""));
            }
            else prices.HeatSource = "no reference fuel (fuelReference in the value files) has a base value, so heat costs nothing";

            foreach (var kv in methods?.Units ?? new Dictionary<string, MethodEnergyUnit>())
            {
                MethodEnergyUnit unit = kv.Value;
                var p = new MethodUnitPrice { Code = unit.Code, Name = unit.Name ?? unit.Code, ModId = unit.ModId, Declared = true, Suggested = unit.EmcPerUnit };
                double best = double.PositiveInfinity;
                string bestText = null;
                foreach (MethodGenerator g in unit.Generators)
                {
                    double each = double.PositiveInfinity;
                    string what = null;
                    foreach (CollectibleObject c in index.Match(g.Burns))
                    {
                        double v;
                        string how;
                        if (fixedValues.TryGetValue(c, out v) && v > 0) how = "base value";
                        else if (prices.HeatRate > 0 && BurnEnergy(c) > 0) { v = prices.HeatRate * BurnEnergy(c); how = "as fuel"; }
                        else continue;
                        if (v < each) { each = v; what = c.Code.ToShortString() + " (" + EmcFormat.Format(v) + " EMC, " + how + ")"; }
                    }
                    if (double.IsPositiveInfinity(each)) { p.Generators.Add((g.Note ?? "a generator") + ": burns " + g.Burns + ", which has no base value: not used"); continue; }
                    double price = each * g.Quantity / g.Makes;
                    string text = (g.Note ?? "a generator") + ": " + Num(g.Quantity) + " " + what + " make " + Num(g.Makes) + " → " + Num(price) + " EMC each";
                    p.Generators.Add(text);
                    if (price < best) { best = price; bestText = text; }
                }
                if (!double.IsPositiveInfinity(best)) p.GeneratorPrice = best;
                if (ConfigPrice(config.EnergyEmc, unit.Code, out double set)) { p.Price = set; p.Source = "setting"; p.SourceText = "your setting EnergyEmc"; }
                else if (!double.IsPositiveInfinity(best)) { p.Price = best; p.Source = "generator"; p.SourceText = bestText; }
                else if (unit.EmcPerUnit.HasValue) { p.Price = unit.EmcPerUnit.Value; p.Source = "suggestion"; p.SourceText = unit.ModId + "'s suggested price"; }
                else { p.Price = 0; p.Source = "none"; p.SourceText = "no price: no setting, no generator with a base value, no suggestion"; }
                prices.Units[unit.Code] = p;
            }
            return prices;
        }

        /// <summary>The setting's price for a unit (keys with or without the "game:" domain, any case).</summary>
        internal static bool ConfigPrice(Dictionary<string, double> config, string code, out double price)
        {
            price = 0;
            if (config == null) return false;
            foreach (var kv in config)
            {
                string key = (kv.Key ?? "").Trim().ToLowerInvariant();
                if (key == code || (!key.Contains(':') && "game:" + key == code))
                {
                    if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value) || kv.Value < 0) return false;
                    price = kv.Value;
                    return true;
                }
            }
            return false;
        }

        static double BurnEnergy(CollectibleObject c)
        {
            var p = c?.CombustibleProps;
            if (p == null || p.BurnDuration <= 0 || p.BurnTemperature <= 0) return 0;
            return p.BurnDuration * p.BurnTemperature;
        }

        static string Num(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
