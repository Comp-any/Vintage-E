using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Check: the one-line result of a test ("✓ ...", "⚠ ..." or "• ..."). In the window the lines after it that are
    /// indented or notes are its details, hidden until the line is clicked; the file and the console show everything.
    /// </summary>
    public enum EmcLineStyle : byte { Text = 0, Heading = 1, Total = 2, Note = 3, Warn = 4, Check = 5 }

    public sealed class EmcReportLine
    {
        public int Indent;
        public EmcLineStyle Style;
        public string Text;
        /// <summary>Which block the line belongs to: the window's search shows or hides whole blocks (0 = always shown).</summary>
        public int Block;
    }

    /// <summary>
    /// What an /emc report window shows (/emc dump hand, /emc loop): lines grouped into blocks the window's search
    /// filters by. Blocks and items are written into the text as [[key]] (<see cref="PlayerEmc.KeyOf"/> keys): the window
    /// draws them as an icon and a name you can click, the file and the console as "Name (code)".
    /// </summary>
    public sealed class EmcReport
    {
        public const string DumpKind = "dump", LoopsKind = "loops", DiagnoseKind = "diagnose";

        public string Kind = DumpKind;
        public string Title = "";
        /// <summary>The block/item a dump explains.</summary>
        public string Key;
        /// <summary>Where the text was saved on the server (null if it couldn't be).</summary>
        public string File;
        /// <summary>The exact stack a dump explains (its wear and contents counted), so the window's Refresh can ask again.</summary>
        public byte[] Stack;
        public readonly List<EmcReportLine> Lines = new List<EmcReportLine>();
        int block;

        /// <summary>Starts a new block with a heading.</summary>
        public void Section(string heading)
        {
            block++;
            Add(heading, EmcLineStyle.Heading);
        }

        public void Add(string text, EmcLineStyle style = EmcLineStyle.Text, int indent = 0)
        {
            Lines.Add(new EmcReportLine { Text = text ?? "", Style = style, Indent = Math.Max(0, indent), Block = block });
        }

        public static string Ref(CollectibleObject c) => c?.Code == null ? "?" : "[[" + PlayerEmc.KeyOf(c) + "]]";

        public static readonly Regex RefToken = new Regex(@"\[\[([^\[\]]+)\]\]", RegexOptions.Compiled);

        /// <summary>A question's answer in a diagnose report: {{action|arg|label}}. The window shows the label as a link that asks the server for that report.</summary>
        public static readonly Regex ActionToken = new Regex(@"\{\{([a-z-]*)\|([^|{}]*)\|([^{}]*)\}\}", RegexOptions.Compiled);

        /// <summary>Both kinds of token, so a line can be cut into text, item refs and answer links in order.</summary>
        public static readonly Regex AnyToken = new Regex(@"\[\[([^\[\]]+)\]\]|\{\{([a-z-]*)\|([^|{}]*)\|([^{}]*)\}\}", RegexOptions.Compiled);

        /// <summary>An answer link as text: its label and the command that does the same.</summary>
        public static string PlainAction(string action, string arg, string label)
        {
            if (action.StartsWith("fix-", StringComparison.Ordinal) || action == "setvalue") return label + " (" + (action == "setvalue" ? "/emc set, holding it" : "in the window") + ")";
            return label + " (/emc diagnose" + (action.Length > 0 && action != EmcDiagnose.ActionOverview ? " " + action : "") + (arg.Length > 0 ? " " + arg : "") + ")";
        }

        /// <summary>The text with each [[key]] written out as "Name (code)". With <paramref name="labelsOnly"/>, answer links keep just their label (for a bug report).</summary>
        public static string Plain(IWorldAccessor world, string text, bool labelsOnly = false)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf("[[", StringComparison.Ordinal) < 0 && text.IndexOf("{{", StringComparison.Ordinal) < 0)) return text ?? "";
            text = ActionToken.Replace(text, m => labelsOnly ? m.Groups[3].Value : PlainAction(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
            return RefToken.Replace(text, m =>
            {
                CollectibleObject c = world == null ? null : PlayerEmc.Resolve(world, m.Groups[1].Value);
                return c?.Code == null ? m.Groups[1].Value : EmcExplain.NameOf(c) + " (" + c.Code + ")";
            });
        }

        /// <summary>The whole report as plain text, for the file and the console.</summary>
        public string PlainText(IWorldAccessor world, bool labelsOnly = false)
        {
            var sb = new StringBuilder();
            foreach (EmcReportLine line in Lines)
            {
                if (line.Style == EmcLineStyle.Heading && sb.Length > 0) sb.Append('\n');
                sb.Append(' ', line.Indent * 2);
                string text = Plain(world, line.Text, labelsOnly);
                sb.Append(line.Style == EmcLineStyle.Heading ? "== " + text + " ==" : text).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        public EmcReportPacket ToPacket() => new EmcReportPacket
        {
            Kind = Kind,
            Title = Title,
            Key = Key,
            File = File,
            Texts = Lines.Select(l => l.Text).ToArray(),
            Indents = Lines.Select(l => (byte)Math.Min(l.Indent, 20)).ToArray(),
            Styles = Lines.Select(l => (byte)l.Style).ToArray(),
            Blocks = Lines.Select(l => l.Block).ToArray(),
            Stack = Stack
        };
    }

    /// <summary>
    /// Builds the /emc dump hand and /emc loop reports: an item's value and every part of how it was worked out -
    /// what the one you hold is worth (Klein star contents, what it holds or is made of, durability, stack size), where
    /// the plain item's value comes from (a value file, the server config, a recipe with its numbers, a guess...), the
    /// other ways of making it, and for something with no value, which ingredients are missing and any value loop.
    /// </summary>
    public static class EmcExplain
    {
        public static string NameOf(CollectibleObject c)
        {
            if (c?.Code == null) return "?";
            // A block that is nothing without its type (clutter, tapestries) has no name as a plain stack: use the code.
            if (StackDataUtil.NeedsData(c)) return c.Code.ToShortString();
            try { return c.GetHeldItemName(new ItemStack(c)); }
            catch { return c.Code.ToShortString(); }
        }

        static string F(double v) => EmcFormat.Format(v);
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Pct(double f) => (f * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        sealed class Ctx
        {
            public VintageEModSystem Mod;
            public ICoreServerAPI Sapi;
            public EmcMappingReport Report;
            public string LangCode;
            public EmcReport R;
            public HashSet<CollectibleObject> Free = new HashSet<CollectibleObject>();
            /// <summary>Value loops already described in this report (each is said once).</summary>
            public HashSet<int> LoopsShown = new HashSet<int>();

            public string L(string key, params object[] args) => Vintagestory.API.Config.Lang.GetL(LangCode, "vintagee:" + key, args);

            public double V(CollectibleObject c) => c != null && Report?.Values != null && Report.Values.TryGetValue(c, out double v) ? v : 0;
        }

        static readonly Regex CodeToken = new Regex(@"(?<![\[\w:])([a-z0-9_]+:[a-z0-9_.\-/]+)", RegexOptions.Compiled);

        /// <summary>Turns block/item codes in the mapper's English notes ("grid: 2 game:plank-oak -> ...") into [[key]] references.</summary>
        static string Linkify(Ctx x, string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            return CodeToken.Replace(text, m =>
            {
                var loc = new AssetLocation(m.Groups[1].Value);
                CollectibleObject c = (CollectibleObject)x.Sapi.World.GetItem(loc) ?? x.Sapi.World.GetBlock(loc);
                return c?.Code == null ? m.Value : EmcReport.Ref(c);
            });
        }

        /// <summary>A recipe source the way a player would say it ("smithing" for RecipeRegistrySystem.SmithingRecipes); unknown ones as they are.</summary>
        static string Friendly(Ctx x, string source)
        {
            if (string.IsNullOrEmpty(source)) return "?";
            string slug = Regex.Replace(source.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            string key = "vintagee:dumpgui-src-" + slug;
            return Vintagestory.API.Config.Lang.HasTranslation(key, false, false) ? Vintagestory.API.Config.Lang.GetL(x.LangCode, key) : source;
        }

        /// <summary>A recipe in words, with its items as references: "smithing: 2 [[ingot]] -> 1 [[plate]]".</summary>
        static string Describe(Ctx x, Conversion conv)
        {
            string text = EmcMapper.Describe(conv);
            string src = conv?.Source ?? "";
            if (src.Length > 0 && text.StartsWith(src, StringComparison.Ordinal)) text = Friendly(x, src) + text.Substring(src.Length);
            return Linkify(x, text);
        }

        /// <summary>One line on where an item's value comes from (for ingredients and contents).</summary>
        static string ShortOrigin(Ctx x, CollectibleObject c)
        {
            if (!x.Report.Origins.TryGetValue(c, out string origin) || string.IsNullOrEmpty(origin)) return null;
            if (origin == "base value")
            {
                var b = BaseInfo(x, c);
                switch (b.kind)
                {
                    case "fixed": return x.L("dumpgui-short-fixed");
                    case "basevalues": return x.L("dumpgui-short-config");
                    case "attribute": return x.L("dumpgui-short-attribute");
                    case "file": case "litre": return x.L("dumpgui-short-file", b.source ?? "?");
                    default: return x.L("dump-noorigin");
                }
            }
            if (x.Report.Via.TryGetValue(c, out Conversion via) && via != null)
                return (x.Report.Inferred.Contains(c) ? x.L("dumpgui-short-back") + " " : "") + Describe(x, via);
            return Linkify(x, origin);
        }

        static Ctx NewCtx(VintageEModSystem mod, ICoreServerAPI sapi, string lang, EmcReport r, EmcMappingReport report)
        {
            var x = new Ctx { Mod = mod, Sapi = sapi, LangCode = lang, R = r, Report = report };
            if (report?.Free != null) x.Free.UnionWith(report.Free);
            return x;
        }

        /// <summary>The cache keeps values, not the recipes behind them: work everything out again (once) to explain it.</summary>
        static EmcMappingReport FullReport(VintageEModSystem mod, string why, out bool remapped)
        {
            EmcMappingReport report = mod.LastReport;
            remapped = false;
            if (report == null || report.FromCache)
            {
                // Compute only: a live remap would resend the table and announce the mapping to every player.
                try { report = mod.ComputeMapping(why, false, out _) ?? report; }
                catch (Exception e) { mod.Mod.Logger.Warning("EMC: could not work the values out for the dump: {0}", e.Message); }
                remapped = true;
            }
            return report;
        }

        // ---------------------------------------------------------------- /emc dump

        /// <summary>
        /// Everything that makes up <paramref name="stack"/>'s value. <paramref name="held"/>: it's the stack in the
        /// player's hand (its wear, contents and size count); otherwise a new one, as made.
        /// </summary>
        public static EmcReport Dump(VintageEModSystem mod, ICoreServerAPI sapi, ItemStack stack, IPlayer player, bool held, string lang)
        {
            CollectibleObject c = stack.Collectible;
            var r = new EmcReport { Kind = EmcReport.DumpKind, Key = PlayerEmc.KeyOf(c) };
            if (held)
            {
                try { r.Stack = stack.ToBytes(); }
                catch { r.Stack = null; }
            }
            EmcMappingReport report = FullReport(mod, "/emc dump", out bool remapped);
            Ctx x = NewCtx(mod, sapi, lang, r, report);
            r.Title = x.L("dumpgui-title", NameOf(c));

            double each = mod.Emc.GetValue(stack);
            int size = Math.Max(1, stack.StackSize);
            r.Add(each > 0 ? x.L("dumpgui-worth", EmcReport.Ref(c), F(each)) : x.L("dumpgui-worthnone", EmcReport.Ref(c)), EmcLineStyle.Total);
            if (held && size > 1 && each > 0) r.Add(x.L("dumpgui-stacktotal", size, F(each * size)), EmcLineStyle.Total);
            r.Add(x.L("dumpgui-code", VintageEModSystem.ConfigKey(c)), EmcLineStyle.Note);
            if (remapped) r.Add(x.L("dump-remapping"), EmcLineStyle.Note);
            if (report == null || report.FromCache)
            {
                r.Add(x.L("dump-cached"), EmcLineStyle.Warn);
                return r;
            }
            Status(x, c, stack, player);
            // Nothing to step through for something without a value (the next part says why it has none).
            if (each > 0 || mod.Emc.GetValue(c) > 0 || c is IEmcStorageItem)
            try { StackFactors(x, stack, held); }
            catch (Exception e) { r.Add("(" + e.Message + ")", EmcLineStyle.Warn, 1); sapi.Logger.Warning("Vintage-E: /emc dump, the stack part: {0}", e); }
            try { Origin(x, c); }
            catch (Exception e) { r.Add("(" + e.Message + ")", EmcLineStyle.Warn, 1); sapi.Logger.Warning("Vintage-E: /emc dump, the origin part: {0}", e); }
            try { OtherWays(x, c); }
            catch (Exception e) { r.Add("(" + e.Message + ")", EmcLineStyle.Warn, 1); sapi.Logger.Warning("Vintage-E: /emc dump, other ways: {0}", e); }
            return r;
        }

        /// <summary>The lists it's on and whether the player knows it (the top block, always shown).</summary>
        static void Status(Ctx x, CollectibleObject c, ItemStack stack, IPlayer player)
        {
            EmcReport r = x.R;
            if (x.Free.Contains(c)) r.Add(x.L("dump-free"), EmcLineStyle.Note);
            else if (x.Report.Blacklisted.Contains(c)) r.Add(x.L("dumpgui-blacklisted"), EmcLineStyle.Warn);
            if (x.Report.Gray?.Contains(c) == true) r.Add(x.L("dumpgui-gray"), EmcLineStyle.Note);
            if (x.Mod.Emc.IsNoTransmute(c)) r.Add(x.L("dump-notransmute"), EmcLineStyle.Warn);
            if (player != null && x.Mod.Emc.CanTransmute(c))
            {
                r.Add(x.L(PlayerEmc.Knows(player, EmcCommands.KnowledgeKey(x.Mod, stack)) ? "dumpgui-known" : "dumpgui-unknown"), EmcLineStyle.Note);
            }
        }

        /// <summary>From the plain item's value to this stack's: Klein star EMC, contents/material, wear, size.</summary>
        static void StackFactors(Ctx x, ItemStack stack, bool held)
        {
            EmcRegistry emc = x.Mod.Emc;
            EmcReport r = x.R;
            CollectibleObject c = stack.Collectible;
            r.Section(x.L(held ? "dumpgui-sec-held" : "dumpgui-sec-new"));
            int maxDurability = 0;
            try { maxDurability = c.GetMaxDurability(stack); } catch { /* none */ }
            bool varies = maxDurability > 1 || c is IEmcStorageItem || emc.StackData.CarriesData(c) || EmcRegistry.ClothingCondition(stack) >= 0;
            if (!held && varies) r.Add(x.L("dumpgui-newnote"), EmcLineStyle.Note);
            int linesBefore = r.Lines.Count;

            double plain = emc.GetValue(c);
            r.Add(x.L("dumpgui-base", F(plain)));
            linesBefore = r.Lines.Count;
            double own = plain;
            if (c is IEmcStorageItem)
            {
                double stored = StoredEmc.Get(stack);
                own += stored;
                r.Add(x.L("dumpgui-stored", F(stored), F(own)), EmcLineStyle.Text, 1);
            }

            StackDataTable data = emc.StackData;
            bool countsContents = false;
            double contents = 0;
            if (data.CarriesData(c))
            {
                StackDataKind kind = data.KindOf(c);
                StackDataMode mode = data.ModeOf(c);
                string source = data.Sources.TryGetValue(c, out string s) ? s : "default";
                string sourceKey = source == "server config" ? "config" : source == "value file" ? "file" : "default";
                r.Add(x.L("dumpgui-data-" + KindName(kind), x.L("contents-mode-" + StackDataTable.ModeName(mode)), x.L("dumpgui-modesource-" + sourceKey)));
                if (mode == StackDataMode.Item)
                {
                    r.Add(x.L("dumpgui-mode-item"), EmcLineStyle.Note, 1);
                    if (held)
                    {
                        double would = SafeContents(emc, stack);
                        if (would > 0) r.Add(x.L("dumpgui-wouldcontents", F(would)), EmcLineStyle.Note, 1);
                        if (kind == StackDataKind.Contents && x.Mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack))
                            r.Add(x.L("dumpgui-protected"), EmcLineStyle.Warn, 1);
                    }
                }
                else
                {
                    own = emc.ItemValue(stack);
                    if (kind == StackDataKind.Chiseled) ChiseledLines(x, stack);
                    else if (kind == StackDataKind.Material) MaterialLines(x, stack, own);
                    if (own <= 0 && kind != StackDataKind.Contents) r.Add(x.L("dumpgui-nomaterialvalue"), EmcLineStyle.Warn, 1);
                    contents = SafeContents(emc, stack);
                    if (mode == StackDataMode.Full)
                    {
                        countsContents = true;
                        ContentsLines(x, stack, contents);
                    }
                    else if (held || contents > 0) r.Add(x.L("dumpgui-mode-itemprice", F(contents)), EmcLineStyle.Note, 1);
                }
            }

            Wear(x, stack, own, countsContents && contents > 0);

            if (r.Lines.Count == linesBefore) r.Add(x.L("dumpgui-nofactors"), EmcLineStyle.Note);
            double ipl = EmcRegistry.ItemsPerLitre(c);
            double each = emc.GetValue(stack);
            if (ipl > 0) r.Add(x.L("dumpgui-liquid", N(ipl), F(each * ipl)), EmcLineStyle.Note);
            if (c.TransitionableProps?.Any(tp => tp != null && tp.Type == EnumTransitionType.Perish) == true) r.Add(x.L("dumpgui-fresh"), EmcLineStyle.Note);

            r.Add(each > 0 ? x.L("dumpgui-result", F(each)) : x.L("dumpgui-resultnone"), EmcLineStyle.Total);
            double burn = x.Mod.BurnValue(stack);
            if (burn > each + 1e-9) r.Add(x.L("dumpgui-burnwith", F(burn)), EmcLineStyle.Total);
            if (held && stack.StackSize > 1 && each > 0) r.Add(x.L("dumpgui-resultstack", stack.StackSize, F(each * stack.StackSize)), EmcLineStyle.Total);
        }

        static string KindName(StackDataKind kind) => kind == StackDataKind.Material ? "material" : kind == StackDataKind.Chiseled ? "chiseled" : "contents";

        static double SafeContents(EmcRegistry emc, ItemStack stack)
        {
            try { return emc.ContentsValue(stack); }
            catch { return 0; }
        }

        static void Wear(Ctx x, ItemStack stack, double own, bool hasContents)
        {
            int max, remaining;
            try
            {
                max = stack.Collectible.GetMaxDurability(stack);
                remaining = stack.Collectible.GetRemainingDurability(stack);
            }
            catch { return; }
            EmcReport r = x.R;
            bool scales = x.Mod.Emc.DurabilityScales;
            if (max > 1)
            {
                if (!scales) r.Add(x.L("dumpgui-wear-off", remaining, max));
                else if (remaining >= max) r.Add(x.L("dumpgui-wear-full", max));
                else
                {
                    double f = Math.Clamp(remaining / (double)max, 0, 1);
                    r.Add(x.L("dumpgui-wear", remaining, max, Pct(f), F(own), F(own * f)));
                    own *= f;
                    if (hasContents) r.Add(x.L("dumpgui-wear-contents"), EmcLineStyle.Note, 1);
                }
            }
            // Clothes wear by condition instead.
            double condition = EmcRegistry.ClothingCondition(stack);
            if (condition < 0) return;
            if (!scales) r.Add(x.L("dumpgui-cond-off", Pct(condition)));
            else if (condition >= 1) r.Add(x.L("dumpgui-cond-full"));
            else r.Add(x.L("dumpgui-cond", Pct(condition), F(own), F(own * condition)));
        }

        static void ContentsLines(Ctx x, ItemStack stack, double total)
        {
            EmcRegistry emc = x.Mod.Emc;
            EmcReport r = x.R;
            IWorldAccessor world = x.Sapi.World;
            if (stack.Attributes?["units"] is IntAttribute units && stack.Attributes.GetItemstack("output") is ItemStack metal)
            {
                if (metal.Collectible == null) metal.ResolveBlockOrItem(world);
                r.Add(x.L("dumpgui-molten", units.value, EmcReport.Ref(metal.Collectible), F(metal.Collectible == null ? 0 : emc.GetValue(metal)), F(total)), EmcLineStyle.Text, 1);
                return;
            }
            List<ItemStack> inner = StackContents.NestedStacks(stack);
            if (inner.Count == 0)
            {
                r.Add(x.L("dumpgui-empty"), EmcLineStyle.Note, 1);
                return;
            }
            int shown = 0;
            foreach (ItemStack s in inner)
            {
                if (s.Collectible == null && !s.ResolveBlockOrItem(world)) continue;
                if (++shown > 30) continue;
                double v = emc.GetValue(s);
                r.Add(v > 0
                    ? x.L("dumpgui-inside", EmcReport.Ref(s.Collectible), s.StackSize, F(v), F(v * s.StackSize))
                    : x.L("dumpgui-inside-novalue", EmcReport.Ref(s.Collectible), s.StackSize), v > 0 ? EmcLineStyle.Text : EmcLineStyle.Warn, 1);
            }
            if (shown > 30) r.Add(x.L("dump-moreways", shown - 30), EmcLineStyle.Note, 1);
            double servings = stack.Attributes?.GetDecimal("quantityServings", 0) ?? 0;
            r.Add(servings > 0 ? x.L("dumpgui-servings", N(servings), F(total)) : x.L("dumpgui-contentstotal", F(total)), EmcLineStyle.Text, 1);
        }

        static void MaterialLines(Ctx x, ItemStack stack, double own)
        {
            EmcReport r = x.R;
            StackDataTable data = x.Mod.Emc.StackData;
            VariantValue variant = data.FindVariant(x.Sapi.World, stack);
            if (variant != null)
            {
                r.Add(x.L("dumpgui-variant", AttrSummary(variant.Attributes), F(variant.Value)), EmcLineStyle.Text, 1);
                if (!string.IsNullOrEmpty(variant.Origin)) r.Add(x.L("dump-from", Linkify(x, variant.Origin)), EmcLineStyle.Note, 2);
            }
            else r.Add(x.L("dumpgui-novariant", F(own)), EmcLineStyle.Note, 1);
            if (data.Variants.TryGetValue(stack.Collectible, out List<VariantValue> all) && all.Count > 1)
            {
                r.Add(x.L("dumpgui-variants", all.Count, F(all.Min(v => v.Value)), F(all.Max(v => v.Value))), EmcLineStyle.Note, 1);
            }
        }

        static string AttrSummary(ITreeAttribute tree)
        {
            if (tree == null || tree.Count == 0) return "-";
            var parts = new List<string>();
            foreach (var kv in tree)
            {
                if (parts.Count >= 4) { parts.Add("..."); break; }
                string v = kv.Value is ItemstackAttribute isa ? isa.value?.Collectible?.Code?.ToShortString() ?? "?"
                         : kv.Value is ITreeAttribute ? "{...}"
                         : kv.Value?.ToString() ?? "";
                if (v.Length > 30) v = v.Substring(0, 29) + "…";
                parts.Add(kv.Key + ": " + v);
            }
            return string.Join(", ", parts);
        }

        static void ChiseledLines(Ctx x, ItemStack stack)
        {
            EmcReport r = x.R;
            List<KeyValuePair<Block, long>> parts = StackDataUtil.ChiseledParts(x.Sapi.World, stack);
            if (parts == null || parts.Count == 0)
            {
                r.Add(x.L("dumpgui-novoxels"), EmcLineStyle.Warn, 1);
                return;
            }
            foreach (var part in parts.OrderByDescending(p => p.Value))
            {
                double v = part.Key == null ? 0 : x.Mod.Emc.GetValue(part.Key);
                r.Add(v > 0
                    ? x.L("dumpgui-voxels", EmcReport.Ref(part.Key), part.Value, F(v), F(v * part.Value / 4096.0))
                    : x.L("dumpgui-voxels-novalue", EmcReport.Ref(part.Key), part.Value), v > 0 ? EmcLineStyle.Text : EmcLineStyle.Warn, 1);
            }
        }

        /// <summary>Where the plain item's value comes from, with the numbers.</summary>
        static void Origin(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            r.Section(x.L("dumpgui-sec-origin"));
            if (x.Free.Contains(c))
            {
                string cfg = x.Mod.Config.FreeIngredients.LastOrDefault(p => CollectibleIndex.Matches(p, c));
                string file = x.Mod.Rules.FreeIngredients.LastOrDefault(p => CollectibleIndex.Matches(p, c));
                r.Add(cfg != null ? x.L("dumpgui-free-config", cfg) : x.L("dumpgui-free-file", file ?? "?"));
                return;
            }
            if (report.Blacklisted.Contains(c))
            {
                BlacklistSource(x, c);
                return;
            }
            if (!report.Values.ContainsKey(c))
            {
                Missing(x, c, 0, 0, new HashSet<CollectibleObject> { c });
                return;
            }
            report.Origins.TryGetValue(c, out string origin);
            if (origin == "base value")
            {
                BaseSource(x, c);
                return;
            }
            if (report.Via.TryGetValue(c, out Conversion conv) && conv != null)
            {
                Recipe(x, c, conv);
                return;
            }
            r.Add(x.L("dump-from", Linkify(x, origin ?? "?")));
            string setting = SettingFor(origin);
            if (setting != null) r.Add(x.L("dumpgui-setting", setting), EmcLineStyle.Note, 1);
            if (origin != null && (origin.StartsWith("guessed", StringComparison.Ordinal) || origin.StartsWith("same as", StringComparison.Ordinal) ||
                origin.StartsWith("as fuel", StringComparison.Ordinal) || origin.StartsWith("as food", StringComparison.Ordinal) || origin.StartsWith("default value", StringComparison.Ordinal)))
            {
                r.Add(x.L("dumpgui-guessnote"), EmcLineStyle.Note, 1);
            }
        }

        static string SettingFor(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return null;
            if (origin.StartsWith("trader", StringComparison.Ordinal)) return "UseTraderPrices";
            if (origin.StartsWith("guessed", StringComparison.Ordinal) || origin.StartsWith("same as", StringComparison.Ordinal)) return "InferFromVariantSiblings";
            if (origin.StartsWith("as fuel", StringComparison.Ordinal) || origin.StartsWith("as food", StringComparison.Ordinal)) return "EstimateFuelAndFood";
            if (origin.StartsWith("FallbackValue", StringComparison.Ordinal)) return "FallbackValue";
            return null;
        }

        static void BlacklistSource(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            VintageEConfig cfg = x.Mod.Config;
            string zero = cfg.FixedValues.Where(kv => kv.Value <= 0 && CollectibleIndex.Matches(kv.Key, c)).Select(kv => kv.Key).LastOrDefault()
                       ?? cfg.BaseValues.Where(kv => kv.Value <= 0 && CollectibleIndex.Matches(kv.Key, c)).Select(kv => kv.Key).LastOrDefault();
            string listed = cfg.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
            JsonObject attr = c.Attributes?["emcValue"];
            string file = x.Mod.Rules.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
            if (zero != null) r.Add(x.L("dumpgui-bl-zero", zero), EmcLineStyle.Warn);
            else if (listed != null) r.Add(x.L("dumpgui-bl-config", listed), EmcLineStyle.Warn);
            else if (attr != null && attr.Exists && attr.AsDouble(-1) == 0) r.Add(x.L("dumpgui-bl-attribute"), EmcLineStyle.Warn);
            else if (file != null) r.Add(x.L("dumpgui-bl-file", file), EmcLineStyle.Warn);
            else r.Add(x.L("dump-blacklisted"), EmcLineStyle.Warn);
        }

        /// <summary>Which value file or setting gives a base value, in the same order the mapper applies them (later ones win).</summary>
        static (string kind, string pattern, string source, double value) BaseInfo(Ctx x, CollectibleObject c)
        {
            VintageEConfig cfg = x.Mod.Config;
            string kind = null, pattern = null, source = null;
            double value = 0;
            foreach (EmcValueRule rule in x.Mod.Rules.Values)
            {
                if (cfg.DisableBuiltInBaseValues && rule.Source != null && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                if (!CollectibleIndex.Matches(rule.Pattern, c)) continue;
                if (rule.Value > 0) { kind = rule.PerLitre ? "litre" : "file"; pattern = rule.Pattern; source = rule.Source; value = rule.Value; }
                else kind = null;
            }
            JsonObject attr = c.Attributes?["emcValue"];
            if (attr != null && attr.Exists && attr.AsDouble(-1) > 0) { kind = "attribute"; value = attr.AsDouble(0); }
            foreach (var kv in cfg.BaseValues)
            {
                if (!CollectibleIndex.Matches(kv.Key, c)) continue;
                if (kv.Value > 0) { kind = "basevalues"; pattern = kv.Key; value = kv.Value; } else kind = null;
            }
            foreach (var kv in cfg.FixedValues)
            {
                if (!CollectibleIndex.Matches(kv.Key, c)) continue;
                if (kv.Value > 0) { kind = "fixed"; pattern = kv.Key; value = kv.Value; } else kind = null;
            }
            return (kind, pattern, source, value);
        }

        static void BaseSource(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            var (kind, pattern, source, value) = BaseInfo(x, c);
            switch (kind)
            {
                case "fixed":
                    r.Add(x.L("dumpgui-base-fixed", pattern, F(value)));
                    if (pattern != VintageEModSystem.ConfigKey(c)) r.Add(x.L("dumpgui-base-wildcard"), EmcLineStyle.Note, 1);
                    break;
                case "basevalues": r.Add(x.L("dumpgui-base-config", pattern, F(value))); break;
                case "attribute": r.Add(x.L("dumpgui-base-attribute", F(value))); break;
                case "file": r.Add(x.L("dumpgui-base-file", source ?? "?", pattern, F(value))); break;
                case "litre":
                    double ipl = EmcRegistry.ItemsPerLitre(c);
                    r.Add(x.L("dumpgui-base-litre", source ?? "?", pattern, F(value), N(ipl), F(ipl > 0 ? value / ipl : value)));
                    break;
                default: r.Add(x.L("dump-noorigin")); break;
            }
            r.Add(x.L(kind == "fixed" ? "dumpgui-base-fixednote" : "dumpgui-base-note"), EmcLineStyle.Note, 1);
        }

        /// <summary>The cheapest valued item that fits an ingredient slot (a free ingredient costs nothing).</summary>
        static (CollectibleObject pick, double value, bool free) Cheapest(Ctx x, ConversionInput input)
        {
            CollectibleObject best = null;
            double bestValue = double.PositiveInfinity;
            foreach (CollectibleObject ch in input.Choices)
            {
                if (ch == null) continue;
                if (x.Free.Contains(ch)) return (ch, 0, true);
                if (x.Report.Values.TryGetValue(ch, out double v) && v < bestValue) { bestValue = v; best = ch; }
            }
            return best == null ? (null, 0, false) : (best, bestValue, false);
        }

        static string Term(Ctx x, string sign, CollectibleObject c, double qty, double value) =>
            sign + x.L("dumpgui-term", EmcReport.Ref(c), N(qty), F(value), F(value * qty));

        static string TimeBonus(Ctx x, Conversion conv, string sign)
        {
            const string prefix = "transitions (";
            string src = conv.Source ?? "";
            if (src.StartsWith(prefix, StringComparison.Ordinal))
            {
                string type = src.Substring(prefix.Length).TrimEnd(')');
                if (x.Mod.Config.TransitionEmcPerDay.TryGetValue(type, out double perDay) && perDay > 0)
                    return sign + x.L("dumpgui-timebonus", F(conv.ExtraCost), N(conv.ExtraCost / perDay), type, F(perDay));
            }
            return sign + x.L("dumpgui-extra", F(conv.ExtraCost));
        }

        /// <summary>One extra cost of a crafting method's recipe, with its price and where that came from.</summary>
        static string CostLine(Ctx x, MethodCostLine k, string sign)
        {
            switch (k.Kind)
            {
                case "energy":
                    string unit = x.Report?.MethodPrices != null && x.Report.MethodPrices.Units.TryGetValue(k.Unit ?? "", out MethodUnitPrice up) && !string.IsNullOrEmpty(up.Name) ? up.Name : k.Unit;
                    return sign + (k.Charged ? x.L("dumpgui-cost-energy", N(k.Amount), unit, N(k.Price), F(k.Emc), k.PriceSource ?? "?") : x.L("dumpgui-cost-energy-off", N(k.Amount), unit));
                case "heat":
                    return sign + (k.Charged ? x.L("dumpgui-cost-heat", N(k.Amount), N(k.Price), F(k.Emc), k.PriceSource ?? "?") : x.L("dumpgui-cost-heat-off", N(k.Amount)));
                case "time":
                    return sign + x.L("dumpgui-cost-time", N(k.Amount), N(k.Price), F(k.Emc));
                default:
                    return sign + x.L("dumpgui-cost-emc", F(k.Emc));
            }
        }

        /// <summary>The extra cost of a recipe: each of a crafting method's costs, or the time bonus. Returns what it adds.</summary>
        static double ExtraLines(Ctx x, Conversion conv, string sign, int indent)
        {
            if (conv.Costs != null && conv.Costs.Count > 0)
            {
                foreach (MethodCostLine k in conv.Costs) x.R.Add(CostLine(x, k, sign), EmcLineStyle.Text, indent);
                return conv.ExtraCost;
            }
            if (conv.ExtraCost > 0) x.R.Add(TimeBonus(x, conv, sign), EmcLineStyle.Text, indent);
            return conv.ExtraCost;
        }

        /// <summary>Which crafting method made this: the machine, the recipe, what its *s stood for.</summary>
        static void MethodHeader(Ctx x, Conversion conv)
        {
            MethodRef m = conv.Method;
            if (m?.Method == null) return;
            EmcReport r = x.R;
            r.Add(x.L("dumpgui-method-recipe", m.Recipe?.Code ?? "?", m.ModId ?? "?", m.Method.Code), EmcLineStyle.Note, 1);
            if (!string.IsNullOrEmpty(m.Method.Machine))
            {
                CollectibleObject machine = MethodParser.FindOne(x.Sapi.World, m.Method.Machine);
                r.Add(x.L("dumpgui-method-machine", machine != null ? EmcReport.Ref(machine) : m.Method.Machine), EmcLineStyle.Note, 1);
            }
            if (!string.IsNullOrEmpty(m.Method.Description)) r.Add(m.Method.Description, EmcLineStyle.Note, 2);
            if (m.Captures != null && m.Captures.Count > 0) r.Add(x.L("dumpgui-method-variant", string.Join(", ", m.Captures.Select(kv => kv.Key + " = " + kv.Value))), EmcLineStyle.Note, 1);
        }

        /// <summary>What a crafting method's recipe uses without using it up, its own note, and notes on the output asked about.</summary>
        static void MethodFooter(Ctx x, Conversion conv, CollectibleObject c)
        {
            MethodRef m = conv.Method;
            if (m == null) return;
            EmcReport r = x.R;
            if (m.OutputNotes != null && c != null && m.OutputNotes.TryGetValue(c, out string note)) r.Add(x.L("dumpgui-outnote", EmcReport.Ref(c), note), EmcLineStyle.Note, 1);
            if (m.Catalysts != null && m.Catalysts.Count > 0)
                r.Add(x.L("dumpgui-method-catalyst", string.Join(", ", m.Catalysts.Take(3).Select(EmcReport.Ref)) + (m.Catalysts.Count > 3 ? " (+" + (m.Catalysts.Count - 3) + ")" : "")), EmcLineStyle.Note, 1);
            if (!string.IsNullOrEmpty(m.Recipe?.Note)) r.Add(x.L("dumpgui-method-note", m.ModId ?? "?", m.Recipe.Note), EmcLineStyle.Note, 1);
        }

        /// <summary>An ingredient line with what the slot stands for (a tool's wear, a chance), when it says.</summary>
        static void InputNote(Ctx x, ConversionInput input, int indent)
        {
            if (!string.IsNullOrEmpty(input.Note)) x.R.Add(input.Note, EmcLineStyle.Note, indent);
        }

        /// <summary>The recipe/process behind a value, with every number: forwards, worked backwards, or shared out.</summary>
        static void Recipe(Ctx x, CollectibleObject c, Conversion conv)
        {
            EmcReport r = x.R;
            double computed;
            if (conv.JointOutputs != null && !conv.IsSimpleJoint)
            {
                computed = JointRecipe(x, c, conv);
            }
            else if (conv.JointOutputs != null)
            {
                CollectibleObject input = conv.Inputs[0].Choices[0];
                double inQ = conv.Inputs[0].Quantity;
                if (input == c)
                {
                    r.Add(x.L("dumpgui-joint-back", Friendly(x, conv.Source)));
                    double sum = 0;
                    foreach (var o in conv.JointOutputs)
                    {
                        double v = x.V(o.Key);
                        sum += v * o.Value;
                        r.Add(Term(x, "+ ", o.Key, o.Value, v), EmcLineStyle.Text, 1);
                    }
                    computed = sum / inQ;
                    r.Add(x.L("dumpgui-per", F(sum), N(inQ), F(computed)), EmcLineStyle.Total, 1);
                }
                else
                {
                    r.Add(x.L("dumpgui-joint", Friendly(x, conv.Source)));
                    double inV = x.V(input);
                    double rest = inV * inQ;
                    r.Add(Term(x, "", input, inQ, inV), EmcLineStyle.Text, 1);
                    var sharers = conv.JointOutputs.Where(o => x.Report.Via.TryGetValue(o.Key, out Conversion cv) && cv == conv).Select(o => o.Key).ToList();
                    if (!sharers.Contains(c)) sharers.Add(c);
                    foreach (var o in conv.JointOutputs)
                    {
                        if (sharers.Contains(o.Key)) continue;
                        double v = x.V(o.Key);
                        if (v <= 0) continue;
                        rest -= v * o.Value;
                        r.Add(Term(x, "- ", o.Key, o.Value, v) + " " + x.L("dumpgui-hasvalue"), EmcLineStyle.Text, 1);
                    }
                    double myQ = conv.JointOutputs.FirstOrDefault(o => o.Key == c).Value;
                    if (myQ <= 0) myQ = 1;
                    double share = rest / sharers.Count;
                    computed = share / myQ;
                    r.Add(x.L("dumpgui-share", F(rest), sharers.Count, F(share), N(myQ), F(computed)), EmcLineStyle.Total, 1);
                }
            }
            else
            {
                int slot = -1;
                if (conv.Output != c)
                {
                    for (int i = 0; i < conv.Inputs.Count; i++)
                    {
                        if (Array.IndexOf(conv.Inputs[i].Choices, c) >= 0) { slot = i; break; }
                    }
                }
                if (slot < 0)
                {
                    r.Add(x.L("dumpgui-made", Friendly(x, conv.Source)));
                    MethodHeader(x, conv);
                    double sum = 0;
                    foreach (ConversionInput input in conv.Inputs)
                    {
                        var (pick, v, free) = Cheapest(x, input);
                        if (pick == null)
                        {
                            r.Add(x.L("dumpgui-term-novalue", EmcReport.Ref(input.Choices.FirstOrDefault()), N(input.Quantity)), EmcLineStyle.Warn, 1);
                            continue;
                        }
                        if (free)
                        {
                            r.Add("+ " + x.L("dumpgui-term-free", EmcReport.Ref(pick), N(input.Quantity)), EmcLineStyle.Text, 1);
                            continue;
                        }
                        sum += v * input.Quantity;
                        string line = Term(x, "+ ", pick, input.Quantity, v);
                        if (input.Choices.Length > 1) line += " " + x.L("dumpgui-cheapestof", input.Choices.Length);
                        r.Add(line, EmcLineStyle.Text, 1);
                        InputNote(x, input, 2);
                        string po = ShortOrigin(x, pick);
                        if (po != null) r.Add(x.L("dump-from", po), EmcLineStyle.Note, 2);
                    }
                    sum += ExtraLines(x, conv, "+ ", 1);
                    if (conv.Returned != null)
                    {
                        foreach (var ret in conv.Returned)
                        {
                            double v = x.V(ret.Key);
                            if (v <= 0)
                            {
                                // A byproduct the recipe doesn't wait for: nothing taken off until it has a value.
                                if (conv.IsOptionalReturn(ret.Key)) r.Add(x.L("dumpgui-byproduct-novalue", EmcReport.Ref(ret.Key), N(ret.Value)), EmcLineStyle.Note, 1);
                                continue;
                            }
                            sum -= v * ret.Value;
                            bool byproduct = conv.Method?.Byproducts != null && conv.Method.Byproducts.Contains(ret.Key);
                            r.Add(Term(x, "- ", ret.Key, ret.Value, v) + " " + x.L(byproduct ? "dumpgui-byproduct" : "dumpgui-returned"), EmcLineStyle.Text, 1);
                            if (byproduct && conv.Method.OutputNotes != null && conv.Method.OutputNotes.TryGetValue(ret.Key, out string bn)) r.Add(bn, EmcLineStyle.Note, 2);
                        }
                    }
                    double outQ = conv.OutputQuantity > 0 ? conv.OutputQuantity : 1;
                    computed = sum / outQ;
                    r.Add(x.L("dumpgui-per", F(sum), N(outQ), F(computed)), EmcLineStyle.Total, 1);
                    MethodFooter(x, conv, c);
                }
                else
                {
                    string setting = conv.Kind == ConversionKind.Processing ? "InferValuesFromProcessing" : "InferValuesFromCrafting";
                    r.Add(x.L("dumpgui-back", Friendly(x, conv.Source), setting));
                    MethodHeader(x, conv);
                    double outV = x.V(conv.Output);
                    double total = outV * conv.OutputQuantity;
                    r.Add(Term(x, "", conv.Output, conv.OutputQuantity, outV) + " " + x.L("dumpgui-whatitmakes"), EmcLineStyle.Text, 1);
                    for (int i = 0; i < conv.Inputs.Count; i++)
                    {
                        if (i == slot) continue;
                        var (pick, v, free) = Cheapest(x, conv.Inputs[i]);
                        if (pick == null || free) continue;
                        total -= v * conv.Inputs[i].Quantity;
                        r.Add(Term(x, "- ", pick, conv.Inputs[i].Quantity, v), EmcLineStyle.Text, 1);
                    }
                    total -= ExtraLines(x, conv, "- ", 1);
                    if (conv.Returned != null)
                    {
                        foreach (var ret in conv.Returned)
                        {
                            double v = x.V(ret.Key);
                            if (v <= 0) continue;
                            total += v * ret.Value;
                            r.Add(Term(x, "+ ", ret.Key, ret.Value, v) + " " + x.L("dumpgui-returned"), EmcLineStyle.Text, 1);
                        }
                    }
                    double q = conv.Inputs[slot].Quantity > 0 ? conv.Inputs[slot].Quantity : 1;
                    computed = total / q;
                    r.Add(x.L("dumpgui-perback", F(total), N(q), F(computed)), EmcLineStyle.Total, 1);
                }
            }
            if (x.Report.Frozen.Contains(c)) r.Add(x.L("dumpgui-frozen"), EmcLineStyle.Note, 1);
            double actual = x.V(c);
            if (actual > 0 && Math.Abs(computed - actual) > Math.Max(0.01, actual * 0.01)) r.Add(x.L("dumpgui-differs", F(actual)), EmcLineStyle.Note, 1);
        }

        /// <summary>
        /// A shared-out recipe with several ingredients (another mod's machine making several things, none the main one):
        /// what it all costs, less what's valued already, shared between the rest; or, for an ingredient worked out
        /// backwards, what's left of the outputs' worth.
        /// </summary>
        static double JointRecipe(Ctx x, CollectibleObject c, Conversion conv)
        {
            EmcReport r = x.R;
            int slot = conv.Inputs.FindIndex(i => Array.IndexOf(i.Choices, c) >= 0);
            if (slot >= 0)
            {
                r.Add(x.L("dumpgui-joint-back", Friendly(x, conv.Source)));
                MethodHeader(x, conv);
                double total = 0;
                foreach (var o in conv.JointOutputs)
                {
                    double v = x.V(o.Key);
                    total += v * o.Value;
                    r.Add(Term(x, "+ ", o.Key, o.Value, v), EmcLineStyle.Text, 1);
                }
                for (int i = 0; i < conv.Inputs.Count; i++)
                {
                    if (i == slot) continue;
                    var (pick, v, free) = Cheapest(x, conv.Inputs[i]);
                    if (pick == null || free) continue;
                    total -= v * conv.Inputs[i].Quantity;
                    r.Add(Term(x, "- ", pick, conv.Inputs[i].Quantity, v), EmcLineStyle.Text, 1);
                }
                total -= ExtraLines(x, conv, "- ", 1);
                if (conv.Returned != null) foreach (var ret in conv.Returned) { double v = x.V(ret.Key); if (v <= 0) continue; total += v * ret.Value; r.Add(Term(x, "+ ", ret.Key, ret.Value, v) + " " + x.L("dumpgui-returned"), EmcLineStyle.Text, 1); }
                double q = conv.Inputs[slot].Quantity > 0 ? conv.Inputs[slot].Quantity : 1;
                double each = total / q;
                r.Add(x.L("dumpgui-perback", F(total), N(q), F(each)), EmcLineStyle.Total, 1);
                MethodFooter(x, conv, c);
                return each;
            }
            r.Add(x.L("dumpgui-joint", Friendly(x, conv.Source)));
            MethodHeader(x, conv);
            double rest = 0;
            foreach (ConversionInput input in conv.Inputs)
            {
                var (pick, v, free) = Cheapest(x, input);
                if (pick == null) { r.Add(x.L("dumpgui-term-novalue", EmcReport.Ref(input.Choices.FirstOrDefault()), N(input.Quantity)), EmcLineStyle.Warn, 1); continue; }
                if (free) { r.Add("+ " + x.L("dumpgui-term-free", EmcReport.Ref(pick), N(input.Quantity)), EmcLineStyle.Text, 1); continue; }
                rest += v * input.Quantity;
                r.Add(Term(x, "+ ", pick, input.Quantity, v), EmcLineStyle.Text, 1);
                InputNote(x, input, 2);
            }
            rest += ExtraLines(x, conv, "+ ", 1);
            if (conv.Returned != null) foreach (var ret in conv.Returned) { double v = x.V(ret.Key); if (v <= 0) continue; rest -= v * ret.Value; r.Add(Term(x, "- ", ret.Key, ret.Value, v) + " " + x.L("dumpgui-returned"), EmcLineStyle.Text, 1); }
            var sharers = conv.JointOutputs.Where(o => x.Report.Via.TryGetValue(o.Key, out Conversion cv) && cv == conv).Select(o => o.Key).ToList();
            if (!sharers.Contains(c)) sharers.Add(c);
            foreach (var o in conv.JointOutputs)
            {
                if (sharers.Contains(o.Key)) continue;
                double v = x.V(o.Key);
                if (v <= 0) continue;
                rest -= v * o.Value;
                r.Add(Term(x, "- ", o.Key, o.Value, v) + " " + x.L("dumpgui-hasvalue"), EmcLineStyle.Text, 1);
            }
            double myQ = conv.JointOutputs.FirstOrDefault(o => o.Key == c).Value;
            if (myQ <= 0) myQ = 1;
            double share, computed;
            if (conv.JointShares != null)
            {
                // Shared by the recipe's weights ("share"), not evenly.
                double Weight(CollectibleObject o) { int i = conv.JointOutputs.FindIndex(p => p.Key == o); return i >= 0 && i < conv.JointShares.Count ? conv.JointShares[i] : 1; }
                double mine = Weight(c), all = sharers.Sum(Weight);
                share = all > 0 ? rest * mine / all : 0;
                computed = share / myQ;
                r.Add(x.L("dumpgui-share-weighted", F(rest), N(mine), N(all), F(share), N(myQ), F(computed)), EmcLineStyle.Total, 1);
            }
            else
            {
                share = rest / sharers.Count;
                computed = share / myQ;
                r.Add(x.L("dumpgui-share", F(rest), sharers.Count, F(share), N(myQ), F(computed)), EmcLineStyle.Total, 1);
            }
            MethodFooter(x, conv, c);
            return computed;
        }

        /// <summary>Per-item cost of a way of making <paramref name="c"/> at today's values, or null (with what's missing).</summary>
        static double? PerItem(Ctx x, Conversion conv, CollectibleObject c, List<CollectibleObject> lacking)
        {
            if (conv.JointOutputs != null || conv.Output != c) return null;
            double sum = 0;
            foreach (ConversionInput input in conv.Inputs)
            {
                var (pick, v, _) = Cheapest(x, input);
                if (pick == null)
                {
                    CollectibleObject first = input.Choices.FirstOrDefault();
                    if (first != null) lacking.Add(first);
                    continue;
                }
                sum += v * input.Quantity;
            }
            if (lacking.Count > 0) return null;
            sum += conv.ExtraCost;
            if (conv.Returned != null) foreach (var ret in conv.Returned) sum -= x.V(ret.Key) * ret.Value;
            if (sum <= 0 || conv.OutputQuantity <= 0) return null;
            return sum / conv.OutputQuantity;
        }

        static void OtherWays(Ctx x, CollectibleObject c)
        {
            EmcReport r = x.R;
            if (!x.Report.Values.ContainsKey(c)) return; // the missing part already lists every way
            x.Report.Via.TryGetValue(c, out Conversion used);
            List<Conversion> others = x.Report.WaysToMake(c).Where(w => w != used).ToList();
            if (others.Count == 0) return;
            r.Section(x.L("dumpgui-sec-other", others.Count));
            x.Report.Origins.TryGetValue(c, out string origin);
            bool set = origin == "base value";
            r.Add(x.L(set ? "dumpgui-other-set" : "dumpgui-other-note"), EmcLineStyle.Note);
            var rows = others.Select(w =>
            {
                var lacking = new List<CollectibleObject>();
                return (way: w, cost: PerItem(x, w, c, lacking), lacking, how: Describe(x, w));
            }).GroupBy(t => t.how).Select(g => (g.First().way, g.First().cost, g.First().lacking, how: g.Key, count: g.Count()))
              .OrderBy(t => t.cost.HasValue ? 0 : 1).ThenBy(t => t.cost ?? 0).ToList();
            int n = 0;
            foreach (var row in rows)
            {
                if (++n > 12)
                {
                    r.Add(x.L("dump-moreways", rows.Count - 12), EmcLineStyle.Note, 1);
                    break;
                }
                string how = row.how + (row.count > 1 ? " " + x.L("dumpgui-times", row.count) : "");
                if (row.cost.HasValue) r.Add(x.L("dumpgui-way", how, F(row.cost.Value)), EmcLineStyle.Text, 1);
                else if (row.lacking.Count > 0) r.Add(x.L("dumpgui-way-lacks", how, string.Join(", ", row.lacking.Distinct().Take(4).Select(EmcReport.Ref))), EmcLineStyle.Note, 1);
                else r.Add(x.L("dumpgui-way-shared", how), EmcLineStyle.Note, 1);
            }
        }

        /// <summary>Why something has no value: the lists, a value loop, or each way of making it and what it's missing (two levels down).</summary>
        static void Missing(Ctx x, CollectibleObject c, int depth, int indent, HashSet<CollectibleObject> seen)
        {
            EmcReport r = x.R;
            EmcMappingReport report = x.Report;
            if (x.Free.Contains(c)) { r.Add(x.L("dump-free"), EmcLineStyle.Note, indent); return; }
            if (report.Blacklisted.Contains(c)) { r.Add(x.L("dump-blacklisted"), EmcLineStyle.Warn, indent); return; }
            if (report.LoopOf.TryGetValue(c, out int loop) && loop >= 0 && loop < report.Loops.Count && x.LoopsShown.Add(loop))
            {
                r.Add(x.L("dumpgui-loop", Chain(report.Loops[loop])), EmcLineStyle.Warn, indent);
            }
            if (!report.MissingRecipes.TryGetValue(c, out List<Conversion> recipes) || recipes.Count == 0)
            {
                r.Add(x.L("dump-norecipe"), EmcLineStyle.Warn, indent);
                if (depth == 0) r.Add(x.L("dumpgui-fixhint"), EmcLineStyle.Note, indent);
                return;
            }
            bool Valued(CollectibleObject o) => o != null && (report.Values.ContainsKey(o) || x.Free.Contains(o));
            // The same recipe often comes several times (one per variant of a tool, say): once each, with how many.
            var ways = recipes.GroupBy(w => Describe(x, w)).Select(g => (conv: g.First(), how: g.Key, count: g.Count())).ToList();
            r.Add(x.L("dump-ways", ways.Count), EmcLineStyle.Text, indent);
            int n = 0;
            foreach (var (conv, how, count) in ways)
            {
                if (++n > (depth == 0 ? 12 : 4))
                {
                    r.Add(x.L("dump-moreways", ways.Count - n + 1), EmcLineStyle.Note, indent + 1);
                    break;
                }
                r.Add(n + ". " + how + (count > 1 ? " " + x.L("dumpgui-times", count) : ""), EmcLineStyle.Text, indent + 1);
                foreach (CollectibleObject part in MissingAnalysis.Lacking(conv, Valued).Distinct())
                {
                    r.Add(x.L("dumpgui-lacks", EmcReport.Ref(part)), EmcLineStyle.Warn, indent + 2);
                    if (depth < 2 && seen.Add(part)) Missing(x, part, depth + 1, indent + 3, seen);
                    else if (report.LoopOf.TryGetValue(part, out int l) && report.LoopOf.TryGetValue(c, out int mine) && l == mine)
                        r.Add(x.L("dump-inloop"), EmcLineStyle.Note, indent + 3);
                }
            }
            if (depth == 0) r.Add(x.L("dumpgui-fixhint"), EmcLineStyle.Note, indent);
        }

        static string Chain(IList<CollectibleObject> loop)
        {
            if (loop == null || loop.Count == 0) return "";
            return string.Join(" -> ", loop.Select(EmcReport.Ref)) + " -> " + EmcReport.Ref(loop[0]);
        }

        // ---------------------------------------------------------------- /emc loop

        /// <summary>Every value loop (items that can't get a value because each is only made from the next) and settled loop.</summary>
        public static EmcReport Loops(VintageEModSystem mod, ICoreServerAPI sapi, string lang)
        {
            var r = new EmcReport { Kind = EmcReport.LoopsKind };
            EmcMappingReport report = FullReport(mod, "/emc loop", out bool remapped);
            Ctx x = NewCtx(mod, sapi, lang, r, report);
            r.Title = x.L("loopgui-title");
            if (report == null)
            {
                r.Add("EMC has not been mapped yet.", EmcLineStyle.Warn);
                return r;
            }
            if (remapped) r.Add(x.L("loopgui-remapped"), EmcLineStyle.Note);
            r.Add(report.Loops.Count == 0 ? x.L("loop-none") : x.L("loopgui-header", report.Loops.Count), EmcLineStyle.Total);
            if (report.Loops.Count > 0) r.Add(x.L("loopgui-help"), EmcLineStyle.Note);
            for (int i = 0; i < report.Loops.Count; i++)
            {
                List<CollectibleObject> loop = report.Loops[i];
                r.Section(x.L("loopgui-loop", i + 1, loop.Count));
                r.Add(Chain(loop));
                Steps(x, loop);
            }
            if (report.SettledLoops.Count > 0)
            {
                r.Section(x.L("loopgui-settledheader", report.SettledLoops.Count));
                r.Add(x.L("loopgui-settledhelp"), EmcLineStyle.Note);
                for (int i = 0; i < report.SettledLoops.Count; i++)
                {
                    List<CollectibleObject> loop = report.SettledLoops[i];
                    r.Section(x.L("loopgui-settled", i + 1, loop.Count));
                    r.Add(Chain(loop));
                    r.Add(string.Join(", ", loop.Take(12).Select(c => EmcReport.Ref(c) + " " + F(x.V(c)))) + (loop.Count > 12 ? ", ..." : ""), EmcLineStyle.Note, 1);
                    Steps(x, loop);
                }
            }
            return r;
        }

        /// <summary>For short loops, which recipe makes each item from the next one.</summary>
        static void Steps(Ctx x, List<CollectibleObject> loop)
        {
            if (loop.Count > 10) return;
            for (int k = 0; k < loop.Count; k++)
            {
                CollectibleObject a = loop[k], b = loop[(k + 1) % loop.Count];
                List<Conversion> ways = x.Report.MissingRecipes.TryGetValue(a, out List<Conversion> missing) && missing.Count > 0 ? missing : x.Report.WaysToMake(a);
                Conversion step = ways.FirstOrDefault(w => w.Inputs.Any(i => Array.IndexOf(i.Choices, b) >= 0));
                if (step == null) continue;
                x.R.Add(x.L("loopgui-step", EmcReport.Ref(a), EmcReport.Ref(b), Friendly(x, step.Source)), EmcLineStyle.Note, 1);
            }
        }
    }
}
