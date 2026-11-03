using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc diagnose 1.15.3, the deeper checks (/emc diagnose checks): recipes that make EMC out of nothing, blacklisted
    /// ingredients holding recipes back, containers that give away their contents, metal tiers out of order, the live
    /// values against the shipped defaults (baseline), mapping time and frequency, the table's size, tooltips that threw,
    /// skipped recipes with their reasons, compatibility files that drifted from their mods, and values defined twice.
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionChecks = "checks", ActionBaseline = "baseline", ActionProfit = "profit", ActionSkipped = "skipped";

        static readonly string[] MetalNames = { "copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "gold", "silver", "lead", "tin", "zinc", "nickel", "chromium", "titanium", "platinum", "stainlesssteel", "electrum", "brass", "cupronickel", "molybdochalkos", "leadsolder", "silversolder" };

        /// <summary>The metals in order of their ingot's value on this server: the order every tool and armour family should follow.</summary>
        static string[] MetalTiersFor(Ctx x)
        {
            var known = new List<(string metal, double v)>();
            foreach (string m in MetalNames)
            {
                CollectibleObject ingot = x.Sapi.World.GetItem(new AssetLocation("game:ingot-" + m));
                double v = x.V(ingot);
                if (v > 0) known.Add((m, v));
            }
            return known.OrderBy(k => k.v).Select(k => k.metal).ToArray();
        }

        /// <summary>Every deeper check, each in its own section, with the fixes.</summary>
        static void Checks(Ctx x, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-checks-deep"));
            r.Add(x.L("diag-checks-help"), EmcLineStyle.Note);
            Profit(x, full: false);
            BlacklistBlockers(x);
            ContainerModes(x);
            Tiers(x);
            MappingRuns(x);
            TableSize(x);
            TooltipFailures(x, client);
            SkippedRecipes(x, full: false);
            CompatDrift(x);
            Duplicates(x);
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionHealth, "", x.L("diag-q-health")));
            r.Add(Link(ActionBaseline, "", x.L("diag-q-baseline")));
            r.Add(Link(ActionProfit, "", x.L("diag-q-profit")));
            r.Add(Link(ActionSkipped, "", x.L("diag-q-skipped")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        /// <summary>One line per deeper check that found something, for the check-up. Returns how many found something.</summary>
        static int ChecksSummary(Ctx x)
        {
            EmcReport r = x.R;
            int found = 0;
            var profit = ProfitList(x);
            if (profit.Count > 0) { found++; x.Warn(x.L("diag-profit-summary", profit.Count, F(profit[0].profit))); r.Add(Link(ActionProfit, "", x.L("diag-q-profit")), EmcLineStyle.Text, 1); }
            var blockers = BlacklistBlockerList(x);
            if (blockers.Count > 0) { found++; x.Warn(x.L("diag-blblockers-summary", blockers.Count, blockers.Sum(b => b.held))); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            var modes = ContainerModeList(x);
            if (modes.Count > 0) { found++; x.Warn(x.L("diag-modes-summary", modes.Count)); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            var tiers = TierViolations(x);
            if (tiers.Count > 0) { found++; x.Warn(x.L("diag-tiers-summary", tiers.Count)); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            if (x.Report.Skipped.Count > 0) { x.Info(x.L("diag-skipped-summary", x.Report.SkippedRecipes)); r.Add(Link(ActionSkipped, "", x.L("diag-q-skipped")), EmcLineStyle.Text, 1); }
            var drift = CompatDriftList(x);
            if (drift.Any(d => d.fresh.Count > 0)) { found++; x.Warn(x.L("diag-drift-summary", drift.Count(d => d.fresh.Count > 0))); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            var dups = DuplicateList(x);
            if (dups.Count > 0) { found++; x.Warn(x.L("diag-dups-summary", dups.Count)); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            if (found == 0) { x.Ok(x.L("diag-checks-clean")); x.Explain(x.L("diag-checks-clean-why")); r.Add(Link(ActionChecks, "", x.L("diag-q-checks")), EmcLineStyle.Text, 1); }
            return found;
        }

        // ---------------------------------------------------------------- 1, 2: EMC out of nothing

        sealed class ProfitEntry { public Conversion Conv; public double profit, cost, gain; public bool processing; public int Recipes = 1; }

        /// <summary>Recipes whose output is worth more than their ingredients: craft and burn for free EMC.</summary>
        static List<ProfitEntry> ProfitList(Ctx x)
        {
            var result = new List<ProfitEntry>();
            EmcMappingReport report = x.Report;
            if (report.Conversions == null) return result;
            var free = new HashSet<CollectibleObject>(report.Free);
            foreach (Conversion conv in report.Conversions)
            {
                double gain;
                if (conv.JointOutputs != null)
                {
                    if (conv.Inputs.Count != 1) continue;
                    gain = conv.JointOutputs.Sum(o => x.V(o.Key) * o.Value);
                }
                else
                {
                    if (conv.Output == null || !report.Values.ContainsKey(conv.Output)) continue;
                    gain = x.V(conv.Output) * conv.OutputQuantity;
                }
                if (gain <= 0) continue;
                double cost = conv.ExtraCost;
                bool ok = true;
                foreach (ConversionInput input in conv.Inputs)
                {
                    double cheapest = double.MaxValue;
                    foreach (CollectibleObject ch in input.Choices)
                    {
                        if (free.Contains(ch)) { cheapest = 0; break; }
                        double v = x.V(ch);
                        if (v > 0 && v < cheapest) cheapest = v;
                    }
                    if (cheapest == double.MaxValue) { ok = false; break; }
                    cost += cheapest * input.Quantity;
                }
                if (!ok) continue;
                if (conv.Returned != null) foreach (var ret in conv.Returned) cost -= x.V(ret.Key) * ret.Value;
                double profit = gain - cost;
                // A hair of rounding is nothing; a quarter of the cost, or more than 4 EMC, is a real gain.
                if (profit > Math.Max(4, cost * 0.25)) result.Add(new ProfitEntry { Conv = conv, profit = profit, cost = cost, gain = gain, processing = conv.Kind == ConversionKind.Processing });
            }
            // One line per output: the recipe with the biggest gain stands for the others (18 fish all filleting to the same thing is one finding, not 18).
            return result.OrderByDescending(p => p.profit)
                .GroupBy(p => p.Conv.Output ?? p.Conv.JointOutputs?.FirstOrDefault().Key)
                .Select(g => { ProfitEntry first = g.First(); first.Recipes = g.Select(e => EmcMapper.Describe(e.Conv)).Distinct().Count(); return first; })
                .ToList();
        }

        static void Profit(Ctx x, bool full)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-profit"));
            r.Add(x.L("diag-profit-help"), EmcLineStyle.Note);
            var list = ProfitList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-profit-none")); return; }
            x.Warn(x.L("diag-profit-found", list.Count, list.Count(p => p.processing)));
            foreach (ProfitEntry p in list.Take(full ? 60 : 12))
            {
                CollectibleObject outC = p.Conv.Output ?? p.Conv.JointOutputs?.FirstOrDefault().Key;
                r.Add((outC != null ? EmcReport.Ref(outC) : "?") + ": " + x.L("diag-profit-line", F(p.gain), F(p.cost), F(p.profit)) + (p.processing ? " " + x.L("diag-profit-processing") : ""), EmcLineStyle.Text, 1);
                x.Note(Linkify(x, EmcMapper.Describe(p.Conv)) + (p.Recipes > 1 ? " " + x.L("diag-profit-recipes", p.Recipes - 1) : ""), 2);
                // The usual culprit: an ingredient whose value is a guess or a trader price, below what it really costs to make.
                var weak = p.Conv.Inputs.SelectMany(i => i.Choices).Where(c => x.Report.Origins.TryGetValue(c, out string o) && (o.StartsWith("trader", StringComparison.Ordinal) || o.StartsWith("guessed", StringComparison.Ordinal) || o.StartsWith("same as", StringComparison.Ordinal) || o.StartsWith("default value", StringComparison.Ordinal))).Distinct().Take(3).ToList();
                if (weak.Count > 0) x.Note(x.L("diag-profit-weak", string.Join(", ", weak.Select(w => EmcReport.Ref(w) + " " + F(x.V(w))))), 2);
                if (outC != null) r.Add(Link(ActionTrace, outC.Code.ToString(), x.L("diag-q-trace")), EmcLineStyle.Text, 2);
            }
            if (list.Count > (full ? 60 : 12)) x.Note(x.L("diag-more", list.Count - (full ? 60 : 12)));
            x.Fix(x.L("diag-profit-fix"));
            if (!full) r.Add(Link(ActionProfit, "", x.L("diag-q-profit")), EmcLineStyle.Text, 1);
        }

        // ---------------------------------------------------------------- 3: blacklisted ingredients holding recipes back

        static List<(CollectibleObject c, int held, string why)> BlacklistBlockerList(Ctx x)
        {
            EmcMappingReport report = x.Report;
            var black = new HashSet<CollectibleObject>(report.Blacklisted);
            var counts = new Dictionary<CollectibleObject, int>();
            bool Valued(CollectibleObject o) => o != null && (report.Values.ContainsKey(o) || report.Free.Contains(o));
            foreach (var kv in report.MissingRecipes)
            {
                foreach (Conversion conv in kv.Value)
                {
                    foreach (ConversionInput input in conv.Inputs)
                    {
                        if (input.Choices.Any(Valued)) continue;
                        if (!input.Choices.All(ch => black.Contains(ch))) continue;
                        foreach (CollectibleObject ch in input.Choices.Distinct()) counts[ch] = counts.TryGetValue(ch, out int n) ? n + 1 : 1;
                        break;
                    }
                }
            }
            return counts.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value, BlacklistReason(x, kv.Key))).ToList();
        }

        static string BlacklistReason(Ctx x, CollectibleObject c)
        {
            VintageEConfig cfg = x.Mod.Config;
            if (cfg.FixedValues.Any(kv => kv.Value <= 0 && CollectibleIndex.Matches(kv.Key, c))) return x.L("diag-blreason-pin0");
            string listed = cfg.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
            if (listed != null) return x.L("diag-blreason-config", listed);
            string file = x.Mod.Rules.Blacklist.LastOrDefault(p => CollectibleIndex.Matches(p, c));
            if (file != null) return x.L("diag-blreason-file", file);
            return x.L("diag-blreason-other");
        }

        static void BlacklistBlockers(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-blblockers"));
            var list = BlacklistBlockerList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-blblockers-none")); return; }
            x.Warn(x.L("diag-blblockers-found", list.Count, list.Sum(b => b.held)));
            r.Add(x.L("diag-blblockers-help"), EmcLineStyle.Note, 1);
            foreach (var (c, held, why) in list.Take(15))
            {
                r.Add(EmcReport.Ref(c) + ": " + x.L("diag-blblockers-line", held) + " " + why, EmcLineStyle.Text, 1);
                r.Add(FixLink(x, "allow", VintageEModSystem.ConfigKey(c), x.L("diag-fix-allow")) + "  " + Link("setvalue", PlayerEmc.KeyOf(c), x.L("diag-fix-setvalue")), EmcLineStyle.Text, 2);
            }
            x.Fix(x.L("diag-blblockers-fix"));
        }

        // ---------------------------------------------------------------- 4: containers giving away their contents

        static List<CollectibleObject> ContainerModeList(Ctx x)
        {
            var data = x.Mod.Emc.StackData;
            return data.Kinds.Where(kv => kv.Value == StackDataKind.Contents && data.ModeOf(kv.Key) == StackDataMode.ItemPrice).Select(kv => kv.Key).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList();
        }

        static void ContainerModes(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-modes"));
            var list = ContainerModeList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-modes-none")); return; }
            x.Warn(x.L("diag-modes-found", list.Count));
            r.Add(x.L("diag-modes-help"), EmcLineStyle.Note, 1);
            foreach (CollectibleObject c in list.Take(20)) r.Add(EmcReport.Ref(c) + "  " + FixLink(x, "contentsmode", c.Code + "=full", x.L("diag-fix-contentsmode")), EmcLineStyle.Text, 1);
            x.Fix(x.L("diag-modes-fix"));
        }

        // ---------------------------------------------------------------- 5: metal tiers out of order

        static List<(string family, string low, double lowV, string high, double highV)> TierViolations(Ctx x)
        {
            var result = new List<(string, string, double, string, double)>();
            if (x.Index == null) return result;
            string[] MetalTiers = MetalTiersFor(x);
            if (MetalTiers.Length < 2) return result;
            var byFamily = new Dictionary<string, Dictionary<string, double>>();
            foreach (var kv in x.Report.Values)
            {
                CollectibleObject c = kv.Key;
                if (c?.Code == null || c.VariantStrict == null || !c.VariantStrict.TryGetValue("metal", out string metal) || Array.IndexOf(MetalTiers, metal) < 0) continue;
                if (c.Code.Path.StartsWith("ingot-", StringComparison.Ordinal)) continue; // the ingots define the order
                // The family without the metal part, so "axe-copper" and "axe-steel" compare.
                string family = c.Code.Domain + ":" + c.Code.Path.Replace("-" + metal, "").Replace(metal + "-", "");
                if (!byFamily.TryGetValue(family, out var tiers)) byFamily[family] = tiers = new Dictionary<string, double>();
                tiers[metal] = kv.Value;
            }
            foreach (var kv in byFamily)
            {
                var present = MetalTiers.Where(kv.Value.ContainsKey).ToList();
                for (int i = 0; i + 1 < present.Count; i++)
                {
                    // A lower tier worth more than a higher one, by a margin (alloys legitimately cost a little differently).
                    if (kv.Value[present[i]] > kv.Value[present[i + 1]] * 1.15) result.Add((kv.Key, present[i], kv.Value[present[i]], present[i + 1], kv.Value[present[i + 1]]));
                }
            }
            return result.OrderByDescending(v => v.Item3 / Math.Max(v.Item5, 1e-6)).ToList();
        }

        static void Tiers(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-tiers"));
            var list = TierViolations(x);
            if (list.Count == 0) { x.Ok(x.L("diag-tiers-none")); return; }
            x.Warn(x.L("diag-tiers-found", list.Count));
            r.Add(x.L("diag-tiers-help"), EmcLineStyle.Note, 1);
            foreach (var (family, low, lowV, high, highV) in list.Take(20))
            {
                CollectibleObject lowC = x.Index.All.FirstOrDefault(c => c.Code.Domain + ":" + c.Code.Path.Replace("-" + low, "").Replace(low + "-", "") == family && c.VariantStrict != null && c.VariantStrict.TryGetValue("metal", out string m) && m == low);
                CollectibleObject highC = x.Index.All.FirstOrDefault(c => c.Code.Domain + ":" + c.Code.Path.Replace("-" + high, "").Replace(high + "-", "") == family && c.VariantStrict != null && c.VariantStrict.TryGetValue("metal", out string m) && m == high);
                r.Add((lowC != null ? EmcReport.Ref(lowC) : low) + " " + F(lowV) + " > " + (highC != null ? EmcReport.Ref(highC) : high) + " " + F(highV), EmcLineStyle.Text, 1);
                if (lowC != null && x.Report.Origins.TryGetValue(lowC, out string o)) x.Note(Linkify(x, o), 2);
            }
            x.Fix(x.L("diag-tiers-fix"));
        }

        // ---------------------------------------------------------------- 6: the live values against the shipped defaults

        static void Baseline(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-baseline"));
            r.Add(x.L("diag-baseline-help"), EmcLineStyle.Note);
            EmcMappingReport baseline;
            try
            {
                var defaults = new VintageEConfig();
                defaults.Normalize();
                EmcRuleSet rules = EmcRuleSet.Load(x.Sapi, x.Sapi.Logger, null);
                var mapper = new EmcMapper(x.Sapi, defaults, rules, x.Sapi.Logger) { ConversionFilter = BaseOnly };
                baseline = mapper.Run();
            }
            catch (Exception e)
            {
                x.Warn(x.L("diag-clean-failed", e.Message));
                Back(x);
                return;
            }
            // Then a mapping with the live settings but still without other mods, to tell settings from mods apart.
            EmcMappingReport settingsOnly = null;
            try
            {
                var mapper = new EmcMapper(x.Sapi, x.Mod.Config, x.Mod.Rules, x.Sapi.Logger) { ConversionFilter = BaseOnly };
                settingsOnly = mapper.Run();
            }
            catch { /* the first comparison still stands */ }
            EmcMappingReport live = x.Report;
            var bySettings = new List<(CollectibleObject c, double was, double now)>();
            var byMods = new List<(CollectibleObject c, double was, double now)>();
            foreach (CollectibleObject c in (x.Index?.All ?? Enumerable.Empty<CollectibleObject>()).Where(BaseDomain))
            {
                double b = baseline.Values.TryGetValue(c, out double bv) ? bv : 0;
                double l = live.Values.TryGetValue(c, out double lv) ? lv : 0;
                if (Math.Abs(b - l) < 1e-6) continue;
                double s = settingsOnly != null ? (settingsOnly.Values.TryGetValue(c, out double sv) ? sv : 0) : b;
                if (Math.Abs(s - b) > 1e-6) bySettings.Add((c, b, s));
                if (Math.Abs(l - s) > 1e-6) byMods.Add((c, s, l));
            }
            if (bySettings.Count + byMods.Count == 0) { x.Ok(x.L("diag-baseline-same")); Back(x); return; }
            r.Add(x.L("diag-baseline-summary", bySettings.Count, byMods.Count), EmcLineStyle.Total);
            if (bySettings.Count > 0)
            {
                r.Section(x.L("diag-baseline-settings", bySettings.Count));
                foreach (var g in bySettings.GroupBy(ch => ch.c.Code.Domain + ":" + ch.c.Code.Path.Split('-')[0]).OrderByDescending(g => g.Count()).Take(25))
                {
                    var first = g.First();
                    r.Add(g.Key + " (" + g.Count() + "): " + EmcReport.Ref(first.c) + " " + (first.was > 0 ? F(first.was) : x.L("diag-none")) + " → " + (first.now > 0 ? F(first.now) : x.L("diag-none")), first.now <= 0 ? EmcLineStyle.Warn : EmcLineStyle.Text, 1);
                }
                x.Note(x.L("diag-baseline-settings-why"), 1);
            }
            if (byMods.Count > 0)
            {
                r.Section(x.L("diag-baseline-mods", byMods.Count));
                foreach (var g in byMods.GroupBy(ch => Culprit(live, ch.c) ?? "?").OrderByDescending(g => g.Count()).Take(10))
                {
                    r.Add((g.Key == "?" ? x.L("diag-clean-unknown", g.Count()) : x.L("diag-clean-bymod", ModName(x, g.Key), g.Count())), EmcLineStyle.Text, 1);
                }
                r.Add(Link(ActionClean, "", x.L("diag-q-clean")), EmcLineStyle.Text, 1);
            }
            Back(x);
        }

        // ---------------------------------------------------------------- 7: mapping time and frequency

        static void MappingRuns(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-runs"));
            var runs = x.Mod.MappingHistory;
            if (runs.Count == 0) { x.Note(x.L("diag-runs-none"), 0); return; }
            var last = runs[runs.Count - 1];
            var fresh = runs.Where(m => !m.cached).ToList();
            r.Add(x.L("diag-runs-last", last.at.ToString("HH:mm:ss"), last.reason, last.ms, last.cached ? x.L("diag-runs-cached") : ""), EmcLineStyle.Text);
            if (fresh.Count >= 2)
            {
                var earlier = fresh.Take(fresh.Count - 1).Select(m => m.ms).OrderBy(v => v).ToList();
                long median = earlier[earlier.Count / 2];
                if (!last.cached && median > 0 && last.ms > median * 3 && last.ms > 10000)
                {
                    x.Warn(x.L("diag-runs-slow", last.ms, median));
                    x.Fix(x.L("diag-runs-slow-fix"));
                }
            }
            if (last.ms > 60000) { x.Warn(x.L("diag-runs-long", last.ms / 1000)); x.Fix(x.L("diag-runs-long-fix")); }
            var recent = runs.Where(m => m.at > DateTime.Now.AddMinutes(-10)).ToList();
            if (recent.Count >= 5)
            {
                x.Warn(x.L("diag-runs-many", recent.Count));
                r.Add(string.Join(", ", recent.GroupBy(m => m.reason).Select(g => g.Key + " ×" + g.Count())), EmcLineStyle.Text, 1);
                x.Fix(x.L("diag-runs-many-fix"));
            }
            else x.Ok(x.L("diag-runs-ok", runs.Count));
        }

        // ---------------------------------------------------------------- 8: the table's size on the wire

        static void TableSize(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-tablesize"));
            try
            {
                EmcTablePacket packet = x.Mod.Emc.ToPacket();
                long bytes;
                using (var ms = new MemoryStream())
                {
                    ProtoBuf.Serializer.Serialize(ms, packet);
                    bytes = ms.Length;
                }
                string size = bytes > 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : (bytes / 1024) + " KB";
                if (bytes > 12 * 1024 * 1024) { x.Warn(x.L("diag-tablesize-big", size, packet.ItemIds?.Length + packet.BlockIds?.Length ?? 0)); x.Fix(x.L("diag-tablesize-fix")); }
                else x.Ok(x.L("diag-tablesize-ok", size, (packet.ItemIds?.Length ?? 0) + (packet.BlockIds?.Length ?? 0), packet.VariantKeys?.Length ?? 0));
            }
            catch (Exception e) { x.Note(x.L("diag-tablesize-error", e.Message), 0); }
        }

        // ---------------------------------------------------------------- 9: tooltips that threw

        static void TooltipFailures(Ctx x, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-tooltips"));
            if (client == null || !client.HasClientInfo) { x.Note(x.L("diag-tooltips-noinfo"), 0); return; }
            if (client.ClientTooltipFailures == null || client.ClientTooltipFailures.Length == 0) { x.Ok(x.L("diag-tooltips-none")); return; }
            x.Warn(x.L("diag-tooltips-found", client.ClientTooltipFailures.Length));
            foreach (string f in client.ClientTooltipFailures) r.Add(Anonymise(f), EmcLineStyle.Text, 1);
            x.Fix(x.L("diag-tooltips-fix"));
        }

        // ---------------------------------------------------------------- 10: skipped recipes

        static void SkippedRecipes(Ctx x, bool full)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-skipped"));
            EmcMappingReport report = x.Report;
            if (report.SkippedRecipes == 0) { x.Ok(x.L("diag-skipped-none")); return; }
            if (report.FromCache || report.Skipped.Count == 0) { x.Note(x.L("diag-skipped-cached", report.SkippedRecipes), 0); return; }
            r.Add(x.L("diag-skipped-found", report.SkippedRecipes), EmcLineStyle.Text);
            r.Add(x.L("diag-skipped-help"), EmcLineStyle.Note, 1);
            foreach (var g in report.Skipped.GroupBy(Reason).OrderByDescending(g => g.Count()).Take(full ? 30 : 6))
            {
                r.Add(g.Key + " (" + g.Count() + ")", EmcLineStyle.Text, 1);
                foreach (string line in g.Take(full ? 15 : 3)) x.Note(Linkify(x, line), 2);
            }
            if (!full) r.Add(Link(ActionSkipped, "", x.L("diag-q-skipped")), EmcLineStyle.Text, 1);
            x.Fix(x.L("diag-skipped-fix"));
        }

        static string Reason(string skipped)
        {
            int p = skipped.LastIndexOf(" (", StringComparison.Ordinal);
            if (p < 0) return "?";
            string reason = skipped.Substring(p + 2).TrimEnd(')');
            // "the ingredient 'x' matches no block or item" → one group for every such ingredient.
            int q = reason.IndexOf('\'');
            if (q >= 0) { int q2 = reason.IndexOf('\'', q + 1); if (q2 > q) reason = reason.Substring(0, q) + "..." + reason.Substring(q2 + 1); }
            return reason;
        }

        // ---------------------------------------------------------------- 11: compatibility files that drifted

        static List<(EmcRuleSet.CompatFile file, List<string> dead, List<CollectibleObject> fresh)> CompatDriftList(Ctx x)
        {
            var result = new List<(EmcRuleSet.CompatFile, List<string>, List<CollectibleObject>)>();
            if (x.Index == null) return result;
            EmcMappingReport report = x.Report;
            foreach (var f in x.Mod.Rules.Compat.Where(f => f.Loaded))
            {
                var patterns = x.Mod.Rules.Defaults.Where(rl => rl.Source == f.Source).Select(rl => rl.Pattern)
                    .Concat(x.Mod.Rules.Values.Where(rl => rl.Source == f.Source).Select(rl => rl.Pattern))
                    .Concat(x.Mod.Rules.Relative.Where(rl => rl.Source == f.Source).Select(rl => rl.Code)).Distinct().ToList();
                var dead = patterns.Where(p => !x.Index.Match(p).Any()).ToList();
                var fresh = report.Missing.Where(c => f.RequiredMods.Contains(c.Code.Domain) && report.MissingWhy.TryGetValue(c, out string w) && w == MissingAnalysis.NoRecipe && !patterns.Any(p => CollectibleIndex.Matches(p, c))).ToList();
                result.Add((f, dead, fresh));
            }
            return result;
        }

        static void CompatDrift(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-drift"));
            var list = CompatDriftList(x);
            if (list.Count == 0) { x.Note(x.L("diag-drift-nofiles"), 0); return; }
            bool any = false;
            foreach (var (file, dead, fresh) in list)
            {
                if (dead.Count + fresh.Count == 0) { x.Ok(x.L("diag-drift-ok", file.Name)); continue; }
                string version = "?";
                try { version = x.Sapi.ModLoader.GetMod(file.RequiredMods.FirstOrDefault() ?? "")?.Info?.Version ?? "?"; } catch { }
                if (fresh.Count > 0) { any = true; x.Warn(x.L("diag-drift-file", file.Name, version, dead.Count, fresh.Count)); }
                else x.Note(x.L("diag-drift-deadonly", file.Name, version, dead.Count), 0);
                foreach (string d in dead.Take(8)) x.Note(x.L("diag-drift-dead", d), 1);
                foreach (CollectibleObject c in fresh.Take(8)) r.Add(x.L("diag-drift-fresh", EmcReport.Ref(c)), EmcLineStyle.Text, 1);
                if (fresh.Count > 0 && file.RequiredMods.Count > 0) r.Add(Link(ActionDraft, file.RequiredMods[0], x.L("diag-q-draft", fresh.Count)), EmcLineStyle.Text, 1);
            }
            if (any) x.Fix(x.L("diag-drift-fix"));
        }

        // ---------------------------------------------------------------- 12: values defined twice

        static List<string> DuplicateList(Ctx x)
        {
            var result = new List<string>();
            VintageEConfig cfg = x.Mod.Config;
            // The same pattern in two value files, with different numbers: the later file wins silently.
            foreach (var g in x.Mod.Rules.Values.GroupBy(rl => rl.Pattern).Where(g => g.Select(rl => rl.Value).Distinct().Count() > 1))
            {
                result.Add(x.L("diag-dups-files", g.Key, string.Join(", ", g.Select(rl => rl.Source + "=" + F(rl.Value)))));
            }
            // A pin and a base value for the same key.
            foreach (string k in cfg.BaseValues.Keys.Intersect(cfg.FixedValues.Keys))
            {
                if (Math.Abs(cfg.BaseValues[k] - cfg.FixedValues[k]) > 1e-9) result.Add(x.L("diag-dups-config", k, F(cfg.BaseValues[k]), F(cfg.FixedValues[k])));
            }
            // A key both blacklisted and allowed, or blacklisted and pinned above 0.
            foreach (string k in cfg.Blacklist.Intersect(cfg.Allow)) result.Add(x.L("diag-dups-blallow", k));
            foreach (string k in cfg.Blacklist.Where(k => cfg.FixedValues.TryGetValue(k, out double v) && v > 0)) result.Add(x.L("diag-dups-blpin", k));
            // One item matched by several explicit (no wildcard) rules with different values, across files and settings.
            if (x.Index != null)
            {
                var explicitRules = x.Mod.Rules.Values.Where(rl => !rl.Pattern.Contains('*')).GroupBy(rl => rl.Pattern.Replace("item:", "").Replace("block:", ""))
                    .Where(g => g.Select(rl => rl.Value).Distinct().Count() > 1 && g.Select(rl => rl.Pattern).Distinct().Count() > 1);
                foreach (var g in explicitRules.Take(20)) result.Add(x.L("diag-dups-files", g.Key, string.Join(", ", g.Select(rl => rl.Source + ":" + rl.Pattern + "=" + F(rl.Value)))));
            }
            return result;
        }

        static void Duplicates(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-dups"));
            var list = DuplicateList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-dups-none")); return; }
            x.Warn(x.L("diag-dups-found", list.Count));
            foreach (string d in list.Take(30)) r.Add(d, EmcLineStyle.Text, 1);
            x.Fix(x.L("diag-dups-fix"));
        }
    }
}
