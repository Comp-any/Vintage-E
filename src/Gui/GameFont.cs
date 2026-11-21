using System.Text;

namespace VintageE
{
    /// <summary>
    /// What the game can draw. It draws text with one font and has no second font to fall back on, so a character
    /// that font lacks shows as an empty box. For ordinary text the font is the computer's own default sans-serif
    /// (the client setting defaultFontName, "sans-serif" unless changed), so it differs from one computer to the
    /// next: Windows' has arrows, many a Linux one has none, and none can be counted on for ticks, warning signs,
    /// hammers or small triangles. Letters, digits and ordinary punctuation are safe everywhere; the list is
    /// tools/game-font-chars.txt, and tools/validate_assets.py holds the lang file and the windows' own texts to it.
    ///
    /// The marks of a report (✓ ⚠ ✖ • →) and the arrows in its lines stay what they are in the report itself: in its
    /// file, in the console, in a bug report, and in what is handed to Vintage Doctor. Whatever is handed to the game
    /// to draw (a window, a button, chat) goes through <see cref="Drawable"/> first, or is drawn as a shape by the
    /// window. The same class is in Vintage Doctor (src/Core/GameFont.cs); keep the two alike.
    /// </summary>
    public static class GameFont
    {
        /// <summary>What ✓, ⚠ and ✖ become where the game draws them as text.</summary>
        public const string Ok = "[ok]", Warn = "[!]", Problem = "[x]";

        /// <summary>
        /// The text with every symbol the game's font may lack written in ordinary characters: arrows as -&gt;,
        /// ticks and warning signs as [ok] [!] [x], triangles as &gt; and v, and pictures left out. Letters of any
        /// language and ordinary punctuation are left as they are.
        /// </summary>
        public static string Drawable(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            int first = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (Special(text[i])) { first = i; break; }
            }
            if (first < 0) return text;

            var sb = new StringBuilder(text.Length + 8);
            sb.Append(text, 0, first);
            for (int i = first; i < text.Length; i++)
            {
                char c = text[i];
                if (!Special(c)) { sb.Append(c); continue; }
                // An emoji is two chars: both go.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                string to = Instead(c);
                if (to.Length > 0) { sb.Append(to); continue; }
                // Nothing in its place. The space after it goes too where the symbol began a line or followed a
                // space ("⚒ Do it" is "Do it", not " Do it").
                bool atStart = sb.Length == 0 || sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\n';
                if (atStart && i + 1 < text.Length && text[i + 1] == ' ') i++;
            }
            return sb.ToString();
        }

        /// <summary>A character that may need something else in its place. Everything below U+2000 is ordinary text.</summary>
        static bool Special(char c)
        {
            if (c < '\u2000') return false;
            if (c <= '\u200F') return true;                              // spaces of odd widths, invisible marks
            if (c >= '\u2010' && c <= '\u2012') return true;             // hyphens the font may lack
            if (c == '\u2015') return true;
            if (c >= '\u2028' && c <= '\u202F') return true;             // line separators, direction marks, a narrow space
            if (c == '\u2032' || c == '\u2033') return true;             // primes
            if (c >= '\u205F' && c <= '\u206F') return true;
            if (c >= '\u2190' && c <= '\u21FF') return true;             // arrows
            if (c == '\u2212' || c == '\u2248' || c == '\u2260' || c == '\u2264' || c == '\u2265') return true;   // − ≈ ≠ ≤ ≥
            if (c >= '\u2300' && c <= '\u23FF') return true;             // technical signs (keyboard keys, clocks)
            if (c >= '\u2460' && c <= '\u24FF') return true;             // circled numbers and letters
            if (c >= '\u2500' && c <= '\u27BF') return true;             // lines, blocks, shapes, symbols, dingbats
            if (c >= '\u2900' && c <= '\u297F') return true;             // more arrows
            if (c >= '\u2B00' && c <= '\u2BFF') return true;             // more symbols and arrows
            if (c >= '\uFE00' && c <= '\uFE0F') return true;             // "show the one before as a picture"
            if (c == '\uFEFF') return true;
            if (c >= '\uD83C' && c <= '\uD83E') return true;             // the first half of an emoji (U+1F000 to U+1FBFF)
            return false;
        }

        static string Instead(char c)
        {
            switch (c)
            {
                // The report's own marks, and their look-alikes.
                case '\u2713': case '\u2714': case '\u2705': case '\u2611': return Ok;
                case '\u26A0': return Warn;
                case '\u2715': case '\u2716': case '\u2717': case '\u2718': case '\u274C': case '\u274E': case '\u2612': return Problem;
                case '\u2610': return "[ ]";
                case '\u2757': case '\u2755': return "!";
                case '\u2753': case '\u2754': return "?";
                // Arrows.
                case '\u2190': case '\u2B05': return "<-";
                case '\u2191': case '\u2B06': return "^";
                case '\u2193': case '\u2B07': return "v";
                case '\u2194': return "<->";
                case '\u21D0': return "<=";
                case '\u21D4': return "<=>";
                case '\u21D2': return "=>";
                case '\u276E': return "<";
                case '\u276F': return ">";
                // Signs with an ordinary way of writing them.
                case '\u2212': case '\u2010': case '\u2011': case '\u2012': return "-";
                case '\u2015': return "\u2014";
                case '\u2248': return "~";
                case '\u2260': return "!=";
                case '\u2264': return "<=";
                case '\u2265': return ">=";
                case '\u2032': return "'";
                case '\u2033': return "\"";
                case '\u2028': case '\u2029': return "\n";
                case '\u2605': case '\u2606': case '\u2B50': return "*";
            }
            if (c <= '\u200A' || c == '\u202F' || c == '\u205F') return " ";             // any space is a space
            if (c < '\u2190') return "";                                                 // invisible marks
            if (c <= '\u21FF' || (c >= '\u2900' && c <= '\u297F')) return "->";          // any other arrow
            if (c >= '\u2794' && c <= '\u27BE') return "->";                             // the dingbat arrows
            if (c >= '\u2500' && c <= '\u257F') return c == '\u2502' || c == '\u2503' ? "|" : c <= '\u2501' ? "-" : "+";   // box lines
            if (c >= '\u2580' && c <= '\u259F') return "#";
            if (c >= '\u25B2' && c <= '\u25B5') return "^";                              // triangles: up,
            if (c >= '\u25B6' && c <= '\u25BB') return ">";                              // right,
            if (c >= '\u25BC' && c <= '\u25BF') return "v";                              // down,
            if (c >= '\u25C0' && c <= '\u25C5') return "<";                              // left
            if (c >= '\u25A0' && c <= '\u25FF') return "\u2022";                         // squares, circles, diamonds: a bullet
            return "";                                                                   // pictures: tools, weather, emoji
        }
    }
}
