using System;
using System.Globalization;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// Running totals of EMC (a player's balance, a Klein star's contents, a machine's store) are kept as
    /// <see cref="decimal"/> since 1.27.0. A <c>double</c> rounds amounts with four decimals once a total passes about
    /// a trillion (its smallest step there is 0.000122) and loses a 0.09 item outright near a quadrillion; a decimal
    /// keeps base-10 numbers exact to 28 digits, so every value the mapper gives (at most four decimals, see
    /// <c>EmcMapper.Tidy</c>) stays exact up to about 7.9 × 10^24, and it holds up to about 7.9 × 10^28 in all.
    /// <para>Item values stay <c>double</c> (they are small); amounts computed from them come in through
    /// <see cref="From"/>, which never throws. Totals are added with <see cref="Add"/>, which stops at the largest
    /// decimal instead of throwing.</para>
    /// <para>Saved as text under the old key + <see cref="Suffix"/>, with the old <c>double</c> key still written
    /// beside it so an older Vintage-E reads the balance (rounded) after a downgrade. If an older version has changed
    /// the old key since, that number wins.</para>
    /// </summary>
    public static class EmcAmount
    {
        /// <summary>Added to an attribute key for the exact (text) copy.</summary>
        public const string Suffix = "Exact";

        /// <summary>A double from the value table or a rate, as a decimal: NaN is 0, infinities and anything past
        /// the decimal range are clamped to it. Rounded to 15 significant digits, as C#'s conversion does, which gives
        /// back the tidy value (102.4, not 102.40000000000000568).</summary>
        public static decimal From(double v)
        {
            if (double.IsNaN(v)) return 0m;
            if (v >= 7.9e28) return decimal.MaxValue;
            if (v <= -7.9e28) return decimal.MinValue;
            try { return (decimal)v; }
            catch (OverflowException) { return v > 0 ? decimal.MaxValue : decimal.MinValue; }
        }

        /// <summary>a + b, stopping at the largest (or smallest) decimal instead of throwing.</summary>
        public static decimal Add(decimal a, decimal b)
        {
            try { return a + b; }
            catch (OverflowException) { return b > 0 ? decimal.MaxValue : decimal.MinValue; }
        }

        /// <summary>a × b, stopping at the largest (or smallest) decimal instead of throwing.</summary>
        public static decimal Multiply(decimal a, decimal b)
        {
            try { return a * b; }
            catch (OverflowException) { return (a > 0) == (b > 0) ? decimal.MaxValue : decimal.MinValue; }
        }

        /// <summary>For places that only show or compare roughly (sorting, bars, logs).</summary>
        public static double ToDouble(decimal v) => (double)v;

        /// <summary>A stored total as read back: negative is 0.</summary>
        public static decimal Clean(decimal v) => v < 0 ? 0m : v;

        static double CleanDouble(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;

        /// <summary>
        /// The total saved under <paramref name="key"/>: the exact copy if there is one and the old double beside it
        /// still matches it, otherwise the old double (a save from before 1.27.0, or one an older version changed).
        /// </summary>
        public static decimal Read(ITreeAttribute tree, string key)
        {
            if (tree == null) return 0m;
            double legacy = CleanDouble(tree.GetDouble(key, 0));
            string text = tree.GetString(key + Suffix, null);
            if (text != null && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal exact))
            {
                exact = Clean(exact);
                // Written together, so they match exactly; a different double means an older Vintage-E changed it.
                if ((double)exact == legacy || !tree.HasAttribute(key)) return exact;
            }
            return From(legacy);
        }

        /// <summary>Saves a total: the exact text copy and the double an older version reads.</summary>
        public static void Write(ITreeAttribute tree, string key, decimal value)
        {
            if (tree == null) return;
            value = Clean(value);
            tree.SetString(key + Suffix, value.ToString(CultureInfo.InvariantCulture));
            tree.SetDouble(key, (double)value);
        }

        /// <summary>Removes both copies.</summary>
        public static void Remove(ITreeAttribute tree, string key)
        {
            if (tree == null) return;
            tree.RemoveAttribute(key);
            tree.RemoveAttribute(key + Suffix);
        }

        /// <summary>Whether either copy is there.</summary>
        public static bool Has(ITreeAttribute tree, string key) => tree != null && (tree.HasAttribute(key) || tree.HasAttribute(key + Suffix));
    }
}
