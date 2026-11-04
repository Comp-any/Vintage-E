using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VintageE
{
    /// <summary>
    /// A recipe written by hand (the CustomRecipes setting, a value file's "recipes", the /emc recipe window), for things
    /// the mapper can't see being made: "output = ingredients", e.g.
    /// <c>item:shipwright:longship-oak = 40 game:plank-oak + 8 game:rope</c>. A number before a code is how many
    /// (default 1); ingredients may be wildcards, and then the cheapest match counts.
    /// </summary>
    public sealed class CustomRecipe
    {
        public string Output;
        public double OutputQuantity = 1;
        public List<(string Code, double Quantity)> Ingredients = new List<(string, double)>();

        /// <summary>Reads one line; null (with <paramref name="error"/>) when it isn't a recipe.</summary>
        public static CustomRecipe Parse(string line, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(line)) { error = "empty"; return null; }
            int eq = line.IndexOf('=');
            if (eq <= 0) { error = "no '=' between the output and the ingredients"; return null; }
            var r = new CustomRecipe();
            if (!ReadTerm(line.Substring(0, eq), out r.Output, out r.OutputQuantity, out error)) return null;
            if (r.Output.Contains('*')) { error = "the output can't be a wildcard"; return null; }
            foreach (string part in line.Substring(eq + 1).Split('+'))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                if (!ReadTerm(part, out string code, out double qty, out error)) return null;
                r.Ingredients.Add((code, qty));
            }
            if (r.Ingredients.Count == 0) { error = "no ingredients"; return null; }
            return r;
        }

        /// <summary>"40 game:plank-oak", "40x game:plank-oak" or "game:plank-oak".</summary>
        static bool ReadTerm(string text, out string code, out double qty, out string error)
        {
            code = null;
            qty = 1;
            error = null;
            string[] words = text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { error = "an empty part"; return false; }
            if (words.Length > 2) { error = "too many words in \"" + text.Trim() + "\""; return false; }
            if (words.Length == 2)
            {
                string n = words[0].TrimEnd('x', 'X', '*');
                if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out qty) || !(qty > 0) || double.IsInfinity(qty))
                {
                    error = "\"" + words[0] + "\" isn't a quantity";
                    return false;
                }
                code = words[1];
            }
            else code = words[0];
            if (code.Length < 2) { error = "\"" + code + "\" isn't a code"; return false; }
            return true;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Term(Output, OutputQuantity)).Append(" = ");
            sb.Append(string.Join(" + ", Ingredients.Select(i => Term(i.Code, i.Quantity))));
            return sb.ToString();
        }

        static string Term(string code, double qty) =>
            Math.Abs(qty - 1) < 1e-9 ? code : qty.ToString("0.###", CultureInfo.InvariantCulture) + " " + code;
    }
}
