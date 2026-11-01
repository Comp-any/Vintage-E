using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc configure - read and change ModConfig/vintagee.json in game:
    /// <list type="bullet">
    /// <item>/emc configure - every setting and its value</item>
    /// <item>/emc configure &lt;setting&gt; - its value and what it does</item>
    /// <item>/emc configure &lt;setting&gt; &lt;value&gt; - change a true/false or number setting</item>
    /// <item>/emc configure &lt;list&gt; add|remove &lt;entry&gt; / clear</item>
    /// <item>/emc configure &lt;table&gt; set &lt;key&gt; &lt;value&gt; / remove &lt;key&gt; / clear</item>
    /// <item>/emc configure &lt;setting&gt; default - put it back to its default; /emc configure resetall - every setting</item>
    /// <item>/emc configure reload - re-read the file after editing it by hand</item>
    /// </list>
    /// Changes are saved straight away and applied: settings that change EMC values remap them, the rest apply at once
    /// (SelfTestOnStart only matters at start-up).
    /// </summary>
    public static class ConfigureCommand
    {
        /// <summary>Settings whose change means the EMC values must be worked out again.</summary>
        static readonly HashSet<string> Remaps = new HashSet<string>(StringComparer.Ordinal)
        {
            "BaseValues", "FixedValues", "Blacklist", "Allow", "DisableBuiltInBaseValues", "InferValuesFromProcessing", "InferValuesFromCrafting",
            "InferFromVariantSiblings", "EstimateFuelAndFood", "UseTraderPrices", "FallbackValue", "TransitionEmcPerDay", "MaxMappingPasses", "FreeIngredients",
            "GrayList", "CustomRecipes", "DisabledCompat"
        };

        static IEnumerable<FieldInfo> Settings => typeof(VintageEConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static TextCommandResult Handle(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            string name = args[0] as string;
            string rest = (args[1] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                // Players get the settings window; the server console gets the list.
                if (args.Caller.Player is IServerPlayer sp)
                {
                    mod.SendConfig(sp);
                    return TextCommandResult.Success();
                }
                return TextCommandResult.Success(List(mod.Config, lang));
            }
            if (name.Equals("list", StringComparison.OrdinalIgnoreCase) && rest.Length == 0) return TextCommandResult.Success(List(mod.Config, lang));
            if (name.Equals("resetall", StringComparison.OrdinalIgnoreCase))
            {
                // Typed on purpose, so no second step; the window asks first.
                return TextCommandResult.Success(Reset(mod, "*", lang));
            }
            if (name.Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                string error = mod.ReloadConfig();
                return error == null ? TextCommandResult.Success(Lang.GetL(lang, "vintagee:cfg-reloaded")) : TextCommandResult.Error(error);
            }

            FieldInfo field = Find(name, out string problem);
            if (field == null) return TextCommandResult.Error(problem ?? Lang.GetL(lang, "vintagee:cfg-unknown", name));
            object value = field.GetValue(mod.Config);
            if (rest.Length == 0) return TextCommandResult.Success(Describe(field, value, lang));
            if (rest.Equals("default", StringComparison.OrdinalIgnoreCase) || rest.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                return TextCommandResult.Success(Reset(mod, field.Name, lang));
            }

            string result;
            try
            {
                result = Change(mod.Config, field, value, rest, lang);
            }
            catch (FormatException e)
            {
                return TextCommandResult.Error(e.Message);
            }
            mod.Config.Normalize();
            mod.SaveConfig();
            string applied = Apply(mod, field.Name, lang);
            return TextCommandResult.Success(result + " " + applied + "\n" + Lang.GetL(lang, "vintagee:cfg-now", field.Name, Show(field.GetValue(mod.Config))));
        }

        /// <summary>
        /// Puts one setting back to its default, or every setting when <paramref name="name"/> is "*". Saves, applies
        /// (resetting everything remaps the values) and returns what happened.
        /// </summary>
        public static string Reset(VintageEModSystem mod, string name, string lang)
        {
            var defaults = new VintageEConfig();
            defaults.Normalize();
            if (name == "*")
            {
                foreach (FieldInfo f in Settings) f.SetValue(mod.Config, f.GetValue(defaults));
                mod.Config.Normalize();
                mod.SaveConfig();
                mod.Trinkets?.ResetHostile();
                mod.RecalculateEmc("/emc configure: every setting reset"); // also applies the table, sounds and no-transmute settings
                return Lang.GetL(lang, "vintagee:cfg-resetall-done");
            }
            FieldInfo field = Field(name) ?? throw new FormatException(Lang.GetL(lang, "vintagee:cfg-unknown", name));
            field.SetValue(mod.Config, field.GetValue(defaults));
            mod.Config.Normalize();
            mod.SaveConfig();
            return Lang.GetL(lang, "vintagee:cfg-reset-done", field.Name, Show(field.GetValue(mod.Config))) + " " + Apply(mod, field.Name, lang);
        }

        /// <summary>Every setting's default as editable text (for the window).</summary>
        public static string[] DefaultTexts()
        {
            var defaults = new VintageEConfig();
            defaults.Normalize();
            return Settings.Select(f => ToText(f.GetValue(defaults))).ToArray();
        }

        /// <summary>Every setting's name and its value as editable text (for the window).</summary>
        public static (string[] names, string[] values) Snapshot(VintageEConfig config)
        {
            var names = new List<string>();
            var values = new List<string>();
            foreach (FieldInfo f in Settings)
            {
                names.Add(f.Name);
                values.Add(ToText(f.GetValue(config)));
            }
            return (names.ToArray(), values.ToArray());
        }

        /// <summary>A value as the window edits it: true/false, a number, one list entry per line, "key = value" per line.</summary>
        public static string ToText(object value)
        {
            switch (value)
            {
                case IDictionary dict:
                    var lines = new List<string>();
                    foreach (DictionaryEntry e in dict) lines.Add(e.Key + " = " + Show(e.Value));
                    return string.Join("\n", lines);
                case IList list:
                    return string.Join("\n", list.Cast<object>().Select(Show));
                default:
                    return Show(value);
            }
        }

        /// <summary>Settings that take one of a few words: shown as a drop-down in the window, checked when typed.</summary>
        static readonly Dictionary<string, string[]> choices = new Dictionary<string, string[]>
        {
            ["Look"] = new[] { ProjectELook.LookAuto, ProjectELook.LookNew, ProjectELook.LookMiddle, ProjectELook.LookOriginal },
        };

        /// <summary>The words a setting can be set to, or null if it isn't one of those settings.</summary>
        public static string[] ChoicesOf(string name) => name != null && choices.TryGetValue(name, out string[] c) ? c : null;

        /// <summary>One of a choice setting's words, written properly (any case, and the Look setting's other names), or a FormatException.</summary>
        static string Choice(FieldInfo field, string text, string lang)
        {
            string[] allowed = ChoicesOf(field.Name);
            string t = (text ?? "").Trim();
            string match = allowed.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));
            if (match == null && field.Name == "Look")
            {
                string normal = ProjectELook.NormalizeLook(t);
                if (normal != ProjectELook.LookAuto || t.Length == 0 || t.Equals("default", StringComparison.OrdinalIgnoreCase)) match = normal;
            }
            return match ?? throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badchoice", t, string.Join(", ", allowed)));
        }

        /// <summary>The kind of editor a setting needs: "bool", "number", "text", "choice", "list" or "table".</summary>
        public static string KindOf(FieldInfo f)
        {
            if (ChoicesOf(f.Name) != null) return "choice";
            Type t = f.FieldType;
            if (t == typeof(bool)) return "bool";
            if (t == typeof(double) || t == typeof(int) || t == typeof(float)) return "number";
            if (typeof(IDictionary).IsAssignableFrom(t)) return "table";
            if (typeof(IList).IsAssignableFrom(t)) return "list";
            return "text";
        }

        public static FieldInfo Field(string name) => Settings.FirstOrDefault(f => f.Name == name);

        /// <summary>
        /// Replaces a setting with the window's text (the whole list/table at once), checking every line first. Saves,
        /// applies, and returns what happened; throws FormatException with a readable message if something is wrong.
        /// </summary>
        public static string SetFromText(VintageEModSystem mod, string name, string text, string lang)
        {
            FieldInfo field = Field(name) ?? throw new FormatException(Lang.GetL(lang, "vintagee:cfg-unknown", name));
            object value = field.GetValue(mod.Config);
            string[] lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (value is IDictionary dict)
            {
                Type valueType = field.FieldType.GetGenericArguments()[1];
                var parsed = new List<KeyValuePair<string, object>>();
                foreach (string line in lines)
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badline", line));
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (key.Length == 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badline", line));
                    if (field.Name == "StackDataModes" && !StackDataTable.TryParseMode(val, out _)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badmode", val));
                    parsed.Add(new KeyValuePair<string, object>(key, Parse(val, valueType, lang)));
                }
                dict.Clear();
                foreach (var kv in parsed) dict[kv.Key] = kv.Value;
            }
            else if (value is IList list)
            {
                list.Clear();
                foreach (string line in lines.Distinct()) list.Add(line);
            }
            else
            {
                field.SetValue(mod.Config, ChoicesOf(field.Name) != null ? Choice(field, text, lang) : Parse(text, field.FieldType, lang));
            }
            mod.Config.Normalize();
            mod.SaveConfig();
            string applied = Apply(mod, field.Name, lang);
            return Lang.GetL(lang, "vintagee:cfg-saved", field.Name) + " " + applied;
        }

        /// <summary>The setting with this name (any case), or the only one starting with it.</summary>
        static FieldInfo Find(string name, out string problem)
        {
            problem = null;
            FieldInfo exact = Settings.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var matches = Settings.Where(f => f.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) problem = Lang.Get("vintagee:cfg-ambiguous", name, string.Join(", ", matches.Select(f => f.Name)));
            return null;
        }

        static string List(VintageEConfig config, string lang)
        {
            var sb = new StringBuilder(Lang.GetL(lang, "vintagee:cfg-title")).Append('\n');
            foreach (FieldInfo f in Settings)
            {
                sb.Append("<strong>").Append(f.Name).Append("</strong> = ").Append(Short(f.GetValue(config))).Append('\n');
            }
            sb.Append(Lang.GetL(lang, "vintagee:cfg-howto"));
            return sb.ToString();
        }

        static string Describe(FieldInfo f, object value, string lang)
        {
            string about = f.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            string how = value is IDictionary ? Lang.GetL(lang, "vintagee:cfg-howto-table", f.Name)
                : value is IList ? Lang.GetL(lang, "vintagee:cfg-howto-list", f.Name)
                : value is bool ? Lang.GetL(lang, "vintagee:cfg-howto-bool", f.Name)
                : Lang.GetL(lang, "vintagee:cfg-howto-value", f.Name);
            return "<strong>" + f.Name + "</strong> = " + Show(value) + "\n" + about + "\n" + how;
        }

        static string Short(object value)
        {
            if (value is IDictionary d) return d.Count == 0 ? "{}" : "{" + d.Count + " entries}";
            if (value is IList l) return l.Count == 0 ? "[]" : "[" + l.Count + " entries]";
            return Show(value);
        }

        static string Show(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case double d: return d.ToString("0.##########", CultureInfo.InvariantCulture);
                case float fl: return fl.ToString("0.####", CultureInfo.InvariantCulture);
                case IDictionary dict:
                    if (dict.Count == 0) return "{}";
                    var parts = new List<string>();
                    foreach (DictionaryEntry e in dict) parts.Add(e.Key + ": " + Show(e.Value));
                    return "{ " + string.Join(", ", parts.Take(60)) + (parts.Count > 60 ? ", ..." : "") + " }";
                case IList list:
                    if (list.Count == 0) return "[]";
                    return "[ " + string.Join(", ", list.Cast<object>().Take(60).Select(Show)) + (list.Count > 60 ? ", ..." : "") + " ]";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        static string Change(VintageEConfig config, FieldInfo field, object value, string rest, string lang)
        {
            string[] words = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = words[0].ToLowerInvariant();
            string arg = words.Length > 1 ? words[1].Trim() : "";

            if (value is IDictionary dict)
            {
                Type valueType = field.FieldType.GetGenericArguments()[1];
                switch (verb)
                {
                    case "clear":
                        dict.Clear();
                        return Lang.GetL(lang, "vintagee:cfg-cleared", field.Name);
                    case "remove":
                        if (arg.Length == 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                        if (!dict.Contains(arg)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-notthere", arg, field.Name));
                        dict.Remove(arg);
                        return Lang.GetL(lang, "vintagee:cfg-removed", arg, field.Name);
                    case "set":
                        string[] kv = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (kv.Length < 2) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                        if (field.Name == "StackDataModes" && !StackDataTable.TryParseMode(kv[1], out _)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badmode", kv[1]));
                        dict[kv[0]] = Parse(kv[1], valueType, lang);
                        return Lang.GetL(lang, "vintagee:cfg-set", kv[0], field.Name, kv[1]);
                    default:
                        throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                }
            }
            if (value is IList list)
            {
                switch (verb)
                {
                    case "clear":
                        list.Clear();
                        return Lang.GetL(lang, "vintagee:cfg-cleared", field.Name);
                    case "add":
                        if (arg.Length == 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-list", field.Name));
                        if (!list.Contains(arg)) list.Add(arg);
                        return Lang.GetL(lang, "vintagee:cfg-added", arg, field.Name);
                    case "remove":
                        if (!list.Contains(arg)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-notthere", arg, field.Name));
                        list.Remove(arg);
                        return Lang.GetL(lang, "vintagee:cfg-removed", arg, field.Name);
                    default:
                        throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-list", field.Name));
                }
            }
            object parsed = ChoicesOf(field.Name) != null ? Choice(field, rest, lang) : Parse(rest, field.FieldType, lang);
            field.SetValue(config, parsed);
            return Lang.GetL(lang, "vintagee:cfg-changed", field.Name, Show(parsed));
        }

        static object Parse(string text, Type type, string lang)
        {
            text = text.Trim();
            if (type == typeof(bool))
            {
                switch (text.ToLowerInvariant())
                {
                    case "true": case "on": case "yes": case "1": return true;
                    case "false": case "off": case "no": case "0": return false;
                }
            }
            else if (type == typeof(double) && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            else if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            else if (type == typeof(string)) return text;
            throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badvalue", text, type == typeof(bool) ? "true/false" : type == typeof(int) ? "a whole number" : "a number"));
        }

        /// <summary>Makes the change take effect and says how.</summary>
        static string Apply(VintageEModSystem mod, string setting, string lang)
        {
            if (Remaps.Contains(setting))
            {
                mod.RecalculateEmc("/emc configure " + setting);
                return Lang.GetL(lang, "vintagee:cfg-remapped");
            }
            switch (setting)
            {
                case "StackDataModes":
                    mod.RefreshStackData();
                    break;
                case "DurabilityScalesEmc":
                    mod.Emc.DurabilityScales = mod.Config.DurabilityScalesEmc;
                    mod.RefreshStackData();
                    break;
                case "HostileCreatures":
                    mod.Trinkets?.ResetHostile();
                    break;
                case "NoTransmute":
                    mod.RefreshStackData(); // matches the list again and tells every client
                    break;
                case "ProjectESounds":
                    mod.Emc.ProjectESounds = mod.Config.ProjectESounds;
                    mod.RefreshStackData(); // sends the table, and with it the setting, to everyone
                    break;
                case "SelfTestOnStart":
                case "Look":
                    return Lang.GetL(lang, "vintagee:cfg-restart");
            }
            return Lang.GetL(lang, "vintagee:cfg-applied");
        }
    }
}
