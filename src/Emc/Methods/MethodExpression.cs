using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VintageE
{
    /// <summary>
    /// The small formulas a method document may write for a number: a string starting with "=", such as
    /// "=$Load * $Seconds / 20" or "=count($^pattern, $@key) * 2". Numbers, + - * / %, brackets, $fields (in a reader's
    /// template only; <paramref name="resolve"/> gives their value) and the functions min, max, round, floor, ceil, abs
    /// and count(text, part). Never throws: an error comes back in words.
    /// </summary>
    public static class MethodExpression
    {
        /// <summary>
        /// Works <paramref name="text"/> out (without its "="). <paramref name="resolve"/> gives a $field's value (a
        /// number, text, or null when the data hasn't got it); without one, a $field is an error. A missing field sets
        /// <paramref name="missing"/> (the caller leaves that part out, as for a "$Field" that isn't there).
        /// </summary>
        public static bool TryEvaluate(string text, Func<string, object> resolve, out double value, out bool missing, out string error)
        {
            value = 0;
            var p = new Parser(text ?? "", resolve);
            try
            {
                object v = p.Expression();
                p.SkipSpace();
                if (p.Pos < p.Text.Length) throw new FormatException("unexpected \"" + p.Text.Substring(p.Pos, Math.Min(8, p.Text.Length - p.Pos)) + "\"");
                missing = p.Missing;
                if (missing) { error = null; return false; }
                value = p.ToNumber(v);
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("the result isn't a number (a division by 0?)");
                error = null;
                return true;
            }
            catch (Exception e)
            {
                // Never throws: anything that goes wrong is an error in words (a formula too deep, a number too odd).
                missing = p.Missing;
                error = missing ? null : "the formula \"=" + text + "\" doesn't work: " + (e is FormatException ? e.Message : e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        sealed class Parser
        {
            public readonly string Text;
            public int Pos;
            public bool Missing;
            int depth;
            readonly Func<string, object> resolve;

            public Parser(string text, Func<string, object> resolve) { Text = text; this.resolve = resolve; }

            public void SkipSpace() { while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos])) Pos++; }

            bool Eat(char c)
            {
                SkipSpace();
                if (Pos < Text.Length && Text[Pos] == c) { Pos++; return true; }
                return false;
            }

            public double ToNumber(object v)
            {
                if (v is double d) return d;
                if (v is string s && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && !double.IsNaN(parsed) && !double.IsInfinity(parsed)) return parsed;
                if (v is bool b) return b ? 1 : 0;
                if (Missing) return 0;
                throw new FormatException("\"" + v + "\" isn't a number");
            }

            public object Expression()
            {
                object left = Term();
                while (true)
                {
                    if (Eat('+')) left = ToNumber(left) + ToNumber(Term());
                    else if (Eat('-')) left = ToNumber(left) - ToNumber(Term());
                    else return left;
                }
            }

            object Term()
            {
                object left = Factor();
                while (true)
                {
                    if (Eat('*')) left = ToNumber(left) * ToNumber(Factor());
                    else if (Eat('/'))
                    {
                        double by = ToNumber(Factor());
                        if (by == 0 && !Missing) throw new FormatException("division by 0");
                        left = by == 0 ? 0 : ToNumber(left) / by;
                    }
                    else if (Eat('%'))
                    {
                        double by = ToNumber(Factor());
                        if (by == 0 && !Missing) throw new FormatException("% by 0");
                        left = by == 0 ? 0 : ToNumber(left) % by;
                    }
                    else return left;
                }
            }

            object Factor()
            {
                SkipSpace();
                if (Pos >= Text.Length) throw new FormatException("it ends too soon");
                char c = Text[Pos];
                if (c == '-') { Pos++; return -ToNumber(Factor()); }
                if (c == '+') { Pos++; return ToNumber(Factor()); }
                if (c == '(')
                {
                    Pos++;
                    if (++depth > 64) throw new FormatException("too many brackets inside each other");
                    object v = Expression();
                    depth--;
                    if (!Eat(')')) throw new FormatException("a \")\" is missing");
                    return v;
                }
                if (c == '"' || c == '\'')
                {
                    int end = Text.IndexOf(c, Pos + 1);
                    if (end < 0) throw new FormatException("a quote isn't closed");
                    string s = Text.Substring(Pos + 1, end - Pos - 1);
                    Pos = end + 1;
                    return s;
                }
                if (c == '$')
                {
                    // $Field, $a.b.0, $^Field (the entry around; ^^ two up), $@key, $@index. A "-" after a field is a minus,
                    // so "=$Seconds-1" is the field Seconds less 1.
                    int start = ++Pos;
                    while (Pos < Text.Length && Text[Pos] == '^') Pos++;
                    if (Pos < Text.Length && Text[Pos] == '@') Pos++;
                    while (Pos < Text.Length && (char.IsLetterOrDigit(Text[Pos]) || Text[Pos] == '_' || Text[Pos] == '.')) Pos++;
                    string field = Text.Substring(start, Pos - start).TrimEnd('.');
                    Pos = start + field.Length;
                    if (resolve == null) throw new FormatException("$" + field + ": fields only work in a reader's template");
                    object v = resolve(field);
                    if (v == null) { Missing = true; return 0.0; }
                    return v;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    int start = Pos;
                    while (Pos < Text.Length && (char.IsDigit(Text[Pos]) || Text[Pos] == '.')) Pos++;
                    if (Pos < Text.Length && (Text[Pos] == 'e' || Text[Pos] == 'E'))
                    {
                        int save = Pos++;
                        if (Pos < Text.Length && (Text[Pos] == '+' || Text[Pos] == '-')) Pos++;
                        if (Pos < Text.Length && char.IsDigit(Text[Pos])) { while (Pos < Text.Length && char.IsDigit(Text[Pos])) Pos++; }
                        else Pos = save;
                    }
                    if (!double.TryParse(Text.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new FormatException("\"" + Text.Substring(start, Pos - start) + "\" isn't a number");
                    return d;
                }
                if (char.IsLetter(c))
                {
                    int start = Pos;
                    while (Pos < Text.Length && char.IsLetter(Text[Pos])) Pos++;
                    string name = Text.Substring(start, Pos - start).ToLowerInvariant();
                    if (!Eat('(')) throw new FormatException("\"" + name + "\" isn't a function (min, max, round, floor, ceil, abs, count, default)");
                    if (name == "default")
                    {
                        // default($field, 1): the field, or 1 when the data hasn't got it.
                        bool before = Missing;
                        Missing = false;
                        object first = Expression();
                        bool gone = Missing;
                        Missing = before;
                        if (!Eat(',')) throw new FormatException("default() takes 2 values: default($field, value)");
                        object fallback = Expression();
                        if (!Eat(')')) throw new FormatException("a \")\" is missing after default(...");
                        return gone ? fallback : first;
                    }
                    var args = new List<object>();
                    if (!Eat(')'))
                    {
                        do args.Add(Expression()); while (Eat(','));
                        if (!Eat(')')) throw new FormatException("a \")\" is missing after " + name + "(...");
                    }
                    return Call(name, args);
                }
                throw new FormatException("unexpected \"" + c + "\"");
            }

            object Call(string name, List<object> args)
            {
                void Need(int min, int max)
                {
                    if (args.Count < min || args.Count > max) throw new FormatException(name + "() takes " + (min == max ? min.ToString() : min + " to " + max) + " values");
                }
                switch (name)
                {
                    case "min": Need(1, 64); return args.Select(ToNumber).Min();
                    case "max": Need(1, 64); return args.Select(ToNumber).Max();
                    case "abs": Need(1, 1); return Math.Abs(ToNumber(args[0]));
                    case "floor": Need(1, 1); return Math.Floor(ToNumber(args[0]));
                    case "ceil": Need(1, 1); return Math.Ceiling(ToNumber(args[0]));
                    case "round":
                        Need(1, 2);
                        double want = args.Count > 1 ? ToNumber(args[1]) : 0;
                        int digits = double.IsNaN(want) ? 0 : (int)Math.Max(0, Math.Min(15, want));
                        return Math.Round(ToNumber(args[0]), digits, MidpointRounding.AwayFromZero);
                    case "count":
                        {
                            // How often a part comes up in a text: count($^pattern, $@key) is how many of a grid ingredient a pattern takes.
                            Need(2, 2);
                            string text = AsText(args[0]), part = AsText(args[1]);
                            if (part.Length == 0) return 0.0;
                            int n = 0;
                            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
                            return (double)n;
                        }
                    default: throw new FormatException("\"" + name + "\" isn't a function (min, max, round, floor, ceil, abs, count, default)");
                }
            }

            static string AsText(object v) => v is double d ? d.ToString(CultureInfo.InvariantCulture) : v as string ?? (v is string[] parts ? string.Join(",", parts) : "");
        }
    }
}
