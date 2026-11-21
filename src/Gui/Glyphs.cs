using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Cairo;
using Vintagestory.API.Client;

namespace VintageE
{
    /// <summary>
    /// Symbols the game's font may not have (1.26.3). Vintage-E's windows use a few characters from outside the
    /// alphabet: arrows (→ ←), the marks ✓ ⚠ ✖, the small triangles ▸ ▾ on things that open, ⚒ on a fix. On Windows
    /// the system fills in a character the game's font lacks from another font; on Linux and macOS nothing does, and
    /// each one is drawn as an empty box. So before a window draws text, every such character the font can't draw is
    /// swapped for a plain one that says the same ("->", "OK", "!").
    ///
    /// Whether the font has a character is found by drawing it: a character no font has is drawn as that font's
    /// "missing" box, so a character whose picture is the same as that one's is missing too. Tried once per character.
    /// Only for a player's game (it draws); never called on a server.
    /// </summary>
    public static class Glyphs
    {
        /// <summary>The characters looked after, each with what stands in for it.</summary>
        static readonly Dictionary<char, string> Plain = new Dictionary<char, string>
        {
            ['→'] = "->", ['←'] = "<-", ['↔'] = "<->", ['↑'] = "^", ['↓'] = "v",
            ['✓'] = "OK", ['⚠'] = "!", ['✖'] = "x", ['⚒'] = "»",
            ['▸'] = "»", ['▾'] = "-", ['○'] = "o", ['●'] = "*", ['•'] = "-",
            ['≤'] = "<=", ['≥'] = ">=", ['≠'] = "!=", ['≈'] = "~", ['…'] = "...", ['−'] = "-",
        };

        static readonly Dictionary<char, bool> known = new Dictionary<char, bool>();
        static byte[] missingPicture;
        static bool testFailed;

        /// <summary>Whether the game's text font can draw this character on this computer. True when it can't be told.</summary>
        public static bool Has(char c)
        {
            if (c < 0x2000) return true; // the alphabets and Latin punctuation: the font has the game's languages
            lock (known)
            {
                if (known.TryGetValue(c, out bool has)) return has;
                has = testFailed || Test(c);
                known[c] = has;
                return has;
            }
        }

        static bool Test(char c)
        {
            try
            {
                // U+0378 is not a character at all, so every font draws its "missing" box (or nothing) for it.
                missingPicture ??= Draw("͸");
                byte[] picture = Draw(c.ToString());
                if (picture.Length != missingPicture.Length) return true;
                for (int i = 0; i < picture.Length; i++) if (picture[i] != missingPicture[i]) return true;
                return false;
            }
            catch
            {
                testFailed = true; // no drawing here: leave every character as it is, as before 1.26.3
                return true;
            }
        }

        static byte[] Draw(string text)
        {
            using var surface = new ImageSurface(Format.Argb32, 64, 64);
            using var ctx = new Context(surface);
            // The font the windows' text is drawn with, set up the way the game sets it up.
            CairoFont.WhiteSmallText().SetupContext(ctx);
            ctx.MoveTo(12, 44);
            ctx.ShowText(text);
            surface.Flush();
            byte[] data = new byte[surface.Stride * surface.Height];
            Marshal.Copy(surface.DataPtr, data, 0, data.Length);
            return data;
        }

        /// <summary>The text with every looked-after character this computer's font can't draw swapped for its plain stand-in.</summary>
        public static string Safe(string text) => Safe(text, Has);

        /// <summary>The same with the "can it be drawn?" question passed in (the self-test has no font).</summary>
        public static string Safe(string text, System.Func<char, bool> has)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 0x2000 && Plain.TryGetValue(c, out string plain) && !has(c))
                {
                    sb ??= new StringBuilder(text, 0, i, text.Length + 8);
                    sb.Append(plain);
                }
                else sb?.Append(c);
            }
            return sb?.ToString() ?? text;
        }

        /// <summary>A mark in front of something: the symbol where the font has it, its stand-in where not.</summary>
        public static string Mark(char symbol) => Has(symbol) || !Plain.TryGetValue(symbol, out string plain) ? symbol.ToString() : plain;

        /// <summary>In front of a line that says what to do.</summary>
        public static string FixArrow => Has('→') ? "→ " : "» ";
        /// <summary>In front of something that opens when clicked, closed and open.</summary>
        public static string Closed => Has('▸') ? "▸ " : "+ ";
        public static string Open => Has('▾') ? "▾ " : "- ";
        /// <summary>In front of a link that asks a question or opens a report, and of one that changes something.</summary>
        public static string Ask => Mark('▸') + " ";
        public static string Fix => Mark('⚒') + " ";

        /// <summary>A small triangle, drawn: pointing right on something closed, down on something open. No font needed.</summary>
        public static void DrawExpander(Context ctx, double width, double height, bool open, double[] rgba)
        {
            double s = Math.Min(width, height) * 0.46, cx = width / 2, cy = height / 2;
            ctx.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
            ctx.NewPath();
            if (open)
            {
                ctx.MoveTo(cx - s / 2, cy - s / 3);
                ctx.LineTo(cx + s / 2, cy - s / 3);
                ctx.LineTo(cx, cy + s / 2);
            }
            else
            {
                ctx.MoveTo(cx - s / 3, cy - s / 2);
                ctx.LineTo(cx + s / 2, cy);
                ctx.LineTo(cx - s / 3, cy + s / 2);
            }
            ctx.ClosePath();
            ctx.Fill();
        }
    }
}
