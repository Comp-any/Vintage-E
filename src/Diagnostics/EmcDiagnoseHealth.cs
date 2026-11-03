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
    /// Health checks beyond the mapping itself: the game version the mod was built for, whether the mod can write its
    /// files, what joining costs each player, the players' own data (EMC, learned items, tablet filters), values that
    /// swung since the last mapping, recipes the solver left alone though every ingredient has a value, items priced
    /// from a trader though a recipe exists, and variants that should be worth the same but aren't. The client adds
    /// hotkey clashes and other mods' Harmony patches on the methods Vintage-E relies on.
    /// </summary>
    public static partial class EmcDiagnose
    {
        // ---------------------------------------------------------------- 11: the game version

        static void GameVersionCheck(Ctx x)
        {
            string game = GameVersion.ShortGameVersion;
            string built = VintageEModSystem.BuiltForGame;
            if (!TryParseVersion(game, out int gm, out int gn, out int gp) || !TryParseVersion(built, out int bm, out int bn, out int bp))
            {
                x.Info(x.L("diag-gamever-unknown", game, built));
                return;
            }
            int cmp = gm != bm ? gm.CompareTo(bm) : gn != bn ? gn.CompareTo(bn) : gp.CompareTo(bp);
            if (cmp == 0) x.Ok(x.L("diag-gamever-same", game));
            else if (cmp < 0) { x.Warn(x.L("diag-gamever-older", game, built)); x.Explain(x.L("diag-gamever-older-why")); x.Fix(x.L("diag-gamever-older-fix")); }
            else if (gm != bm || gn != bn) { x.Warn(x.L("diag-gamever-newer", game, built)); x.Explain(x.L("diag-gamever-newer-why")); x.Fix(x.L("diag-gamever-newer-fix")); }
            else { x.Info(x.L("diag-gamever-patch", game, built)); x.Explain(x.L("diag-gamever-patch-why")); }
        }

        static bool TryParseVersion(string v, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(v)) return false;
            string[] parts = v.Split('-')[0].Split('.');
            return parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor) && (parts.Length < 3 || int.TryParse(new string(parts[2].TakeWhile(char.IsDigit).ToArray()), out patch));
        }

        // ---------------------------------------------------------------- 10: can the mod write its files?

        static void Writable(Ctx x)
        {
            var bad = new List<string>();
            foreach (string dir in new[] { VintageEModSystem.DataFolder(x.Sapi), Path.Combine(VintageEModSystem.DataFolder(x.Sapi), "cache"), x.Sapi.GetOrCreateDataPath("ModConfig") })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".vintagee-write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                }
                catch (Exception e) { bad.Add(dir + ": " + e.Message); }
            }
            if (bad.Count == 0) { x.Ok(x.L("diag-write-ok")); return; }
            x.Warn(x.L("diag-write-bad", bad.Count));
            foreach (string b in bad) x.Note(Anonymise(b));
            x.Explain(x.L("diag-write-why"));
            x.Fix(x.L("diag-write-fix"));
        }

        // ---------------------------------------------------------------- 4: what a join costs

        static void JoinCost(Ctx x)
        {
            VintageEModSystem mod = x.Mod;
            long bytes = 0;
            try
            {
                using var buffer = new MemoryStream();
                ProtoBuf.Serializer.Serialize(buffer, mod.Emc.ToPacket());
                bytes = buffer.Length;
            }
            catch (Exception e) { x.Note(x.L("diag-join-error", e.Message)); return; }
            int online = (x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>()).Length;
            string size = bytes > 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : (bytes / 1024) + " KB";
            long ms = mod.LastTableSendMs;
            bool slow = ms > 300 || bytes > 6 * 1024 * 1024;
            if (slow)
            {
                x.Warn(x.L("diag-join-slow", size, ms, online));
                x.Explain(x.L("diag-join-slow-why"));
                x.Fix(x.L("diag-join-slow-fix"));
            }
            else x.Ok(x.L("diag-join-ok", size, ms));
        }

        // ---------------------------------------------------------------- 2, 3: the players' own data

        static void PlayerData(Ctx x)
        {
            IPlayer[] players = x.Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>();
            if (players.Length == 0) { x.Info(x.L("diag-pdata-nobody")); x.Explain(x.L("diag-pdata-nobody-why")); return; }
            int cap = x.Mod.Config.MaxExactKnowledge;
            var problems = new List<string>();
            foreach (IPlayer p in players)
            {
                if (p?.Entity == null) continue;
                double emc = p.Entity.WatchedAttributes?.GetDouble(PlayerEmc.EmcKey, 0) ?? 0;
                if (double.IsNaN(emc) || double.IsInfinity(emc) || emc < 0) problems.Add(x.L("diag-pdata-emc", p.PlayerName, emc.ToString(CultureInfo.InvariantCulture)));
                string[] known = PlayerEmc.GetKnown(p);
                int dead = 0;
                string firstDead = null;
                foreach (string k in known)
                {
                    if (PlayerEmc.Resolve(x.Sapi.World, k) == null) { dead++; firstDead ??= k; }
                }
                if (dead > 0) problems.Add(x.L("diag-pdata-dead", p.PlayerName, dead, known.Length, firstDead));
                int exact = known.Count(PlayerEmc.IsExact);
                if (cap > 0 && exact > cap) problems.Add(x.L("diag-pdata-overcap", p.PlayerName, exact, cap));
                // Tablet filters naming items that no longer exist.
                int badFilters = 0;
                p.InventoryManager?.Inventories?.Values.ToList().ForEach(inv =>
                {
                    if (inv == null) return;
                    foreach (ItemSlot slot in inv)
                    {
                        ItemStack s = slot?.Itemstack;
                        if (s?.Collectible == null || !TabletMagnet.IsTablet(s)) continue;
                        foreach (string key in TabletMagnet.GetFilter(s))
                        {
                            if (key != null && PlayerEmc.Resolve(x.Sapi.World, key) == null) badFilters++;
                        }
                    }
                });
                if (badFilters > 0) problems.Add(x.L("diag-pdata-filter", p.PlayerName, badFilters));
            }
            if (problems.Count == 0) { x.Ok(x.L("diag-pdata-ok", players.Length)); x.Explain(x.L("diag-pdata-ok-why")); return; }
            x.Warn(x.L("diag-pdata-found", problems.Count, players.Length));
            foreach (string pr in problems) x.Note(pr);
            x.Explain(x.L("diag-pdata-why"));
            x.Fix(x.L("diag-pdata-fix"));
        }

        // ---------------------------------------------------------------- 6: values that swung since the last mapping

        static List<(string key, double before, double after)> SwingList(Ctx x)
        {
            var result = new List<(string key, double before, double after)>();
            VintageEModSystem mod = x.Mod;
            if (mod.PreviousValues == null || x.Report?.Values == null) return result;
            bool sameMods = mod.PreviousMods != null && mod.CurrentMods != null && mod.PreviousMods.Count == mod.CurrentMods.Count && mod.PreviousMods.All(kv => mod.CurrentMods.TryGetValue(kv.Key, out string v) && v == kv.Value);
            if (!sameMods) return result;
            foreach (var kv in x.Report.Values)
            {
                if (kv.Key?.Code == null || kv.Value <= 0) continue;
                if (!mod.PreviousValues.TryGetValue(PlayerEmc.KeyOf(kv.Key), out double before) || before <= 0) continue;
                double ratio = kv.Value / before;
                if (ratio >= 10 || ratio <= 0.1) result.Add((PlayerEmc.KeyOf(kv.Key), before, kv.Value));
            }
            return result.OrderByDescending(s => Math.Max(s.after / s.before, s.before / s.after)).ToList();
        }

        static void Swings(Ctx x, bool full)
        {
            var swings = SwingList(x);
            if (x.Mod.PreviousValues == null) { x.Info(x.L("diag-swing-none-yet")); return; }
            if (swings.Count == 0) { x.Ok(x.L("diag-swing-ok")); return; }
            x.Warn(x.L("diag-swing-found", swings.Count));
            x.Explain(x.L("diag-swing-why"));
            foreach (var (key, before, after) in swings.Take(full ? 60 : 8))
                x.R.Add("[[" + key + "]] " + F(before) + " → " + F(after), EmcLineStyle.Text, 1);
            if (swings.Count > (full ? 60 : 8)) x.Note(x.L("diag-more", swings.Count - (full ? 60 : 8)));
            x.Fix(x.L("diag-swing-fix"));
        }

        // ---------------------------------------------------------------- 8: recipes the solver left alone

        static bool InputsValued(Conversion conv, IReadOnlyDictionary<CollectibleObject, double> values)
        {
            foreach (ConversionInput input in conv.Inputs)
            {
                if (input.Choices == null || input.Choices.Length == 0) return false;
                if (!input.Choices.Any(c => c != null && values.TryGetValue(c, out double v) && v > 0)) return false;
            }
            return true;
        }

        static List<Conversion> SolverSkippedList(Ctx x)
        {
            var result = new List<Conversion>();
            EmcMappingReport report = x.Report;
            if (report?.Conversions == null || report.Values == null) return result;
            var missing = new HashSet<CollectibleObject>(report.Missing);
            if (missing.Count == 0) return result;
            var seen = new HashSet<CollectibleObject>();
            foreach (Conversion conv in report.Conversions)
            {
                if (conv.Output == null || !missing.Contains(conv.Output) || !seen.Add(conv.Output)) continue;
                if (InputsValued(conv, report.Values)) result.Add(conv);
            }
            return result;
        }

        static void SolverSkipped(Ctx x, bool full)
        {
            if (x.Report.FromCache) { x.Info(x.L("diag-solver-cache")); return; }
            var list = SolverSkippedList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-solver-ok")); return; }
            x.Warn(x.L("diag-solver-found", list.Count));
            x.Explain(x.L("diag-solver-why"));
            foreach (Conversion conv in list.Take(full ? 40 : 6))
            {
                string why = x.Report.Loops.Any(l => l.Contains(conv.Output)) ? x.L("diag-solver-loop") : x.L("diag-solver-floor");
                x.R.Add("[[" + PlayerEmc.KeyOf(conv.Output) + "]] ← " + conv.Source + ": " + string.Join(" + ", conv.Inputs) + " (" + why + ")", EmcLineStyle.Text, 1);
            }
            if (list.Count > (full ? 40 : 6)) x.Note(x.L("diag-more", list.Count - (full ? 40 : 6)));
            x.Fix(x.L("diag-solver-fix"));
        }

        // ---------------------------------------------------------------- 7: trader-priced items that have a recipe

        static List<(CollectibleObject item, Conversion conv)> TraderOverRecipeList(Ctx x)
        {
            var result = new List<(CollectibleObject, Conversion)>();
            EmcMappingReport report = x.Report;
            if (report?.Conversions == null || report.Origins == null) return result;
            var traderPriced = new HashSet<CollectibleObject>(report.Origins.Where(kv => kv.Value != null && kv.Value.StartsWith("trader", StringComparison.Ordinal)).Select(kv => kv.Key));
            if (traderPriced.Count == 0) return result;
            var seen = new HashSet<CollectibleObject>();
            foreach (Conversion conv in report.Conversions)
            {
                if (conv.Output == null || !traderPriced.Contains(conv.Output) || !seen.Add(conv.Output)) continue;
                if (InputsValued(conv, report.Values)) result.Add((conv.Output, conv));
            }
            return result;
        }

        static void TraderOverRecipe(Ctx x, bool full)
        {
            if (x.Report.FromCache) return;
            var list = TraderOverRecipeList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-traderrecipe-ok")); return; }
            x.Warn(x.L("diag-traderrecipe-found", list.Count));
            x.Explain(x.L("diag-traderrecipe-why"));
            foreach (var (item, conv) in list.Take(full ? 40 : 6))
            {
                double cost = conv.Inputs.Sum(i => i.Quantity * i.Choices.Where(c => c != null && x.Report.Values.ContainsKey(c)).Select(c => x.Report.Values[c]).DefaultIfEmpty(0).Min()) + conv.ExtraCost;
                double per = conv.OutputQuantity > 0 ? cost / conv.OutputQuantity : cost;
                x.R.Add("[[" + PlayerEmc.KeyOf(item) + "]] " + F(x.Report.Values[item]) + " (" + x.L("diag-traderrecipe-recipe", F(per)) + ") ← " + conv.Source, EmcLineStyle.Text, 1);
            }
            if (list.Count > (full ? 40 : 6)) x.Note(x.L("diag-more", list.Count - (full ? 40 : 6)));
            x.Fix(x.L("diag-traderrecipe-fix"));
        }

        // ---------------------------------------------------------------- 9: look-only variants worth different amounts

        static List<(string group, string attr, double min, double max, CollectibleObject lo, CollectibleObject hi)> VariantSpreadList(Ctx x)
        {
            var result = new List<(string, string, double, double, CollectibleObject, CollectibleObject)>();
            EmcMappingReport report = x.Report;
            if (report?.Values == null || report.Origins == null) return result;
            var copy = x.Mod.Rules.CopyAcrossGroups;
            if (copy == null || copy.Count == 0) return result;
            // Only values that came from a recipe or processing (not copied or guessed from another variant).
            bool Own(CollectibleObject c) => report.Origins.TryGetValue(c, out string o) && o != null && !o.StartsWith("same as", StringComparison.Ordinal) && !o.StartsWith("guessed", StringComparison.Ordinal) && !o.StartsWith("copied", StringComparison.Ordinal);
            foreach (string attr in copy)
            {
                var groups = new Dictionary<string, List<CollectibleObject>>();
                foreach (var kv in report.Values)
                {
                    CollectibleObject c = kv.Key;
                    if (c?.Code == null || kv.Value <= 0 || c.VariantStrict == null || !c.VariantStrict.ContainsKey(attr) || !Own(c)) continue;
                    string sig = VariantSignature(c, attr);
                    if (!groups.TryGetValue(sig, out var list)) groups[sig] = list = new List<CollectibleObject>();
                    list.Add(c);
                }
                foreach (var g in groups.Values)
                {
                    if (g.Count < 2) continue;
                    CollectibleObject lo = g.OrderBy(c => report.Values[c]).First(), hi = g.OrderByDescending(c => report.Values[c]).First();
                    double min = report.Values[lo], max = report.Values[hi];
                    if (max > min * 1.5) result.Add((lo.Code.Domain + ":" + lo.FirstCodePart(), attr, min, max, lo, hi));
                }
            }
            return result.OrderByDescending(r => r.Item4 / r.Item3).ToList();
        }

        static string VariantSignature(CollectibleObject c, string wild)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(c.ItemClass == EnumItemClass.Block ? 'b' : 'i').Append('|').Append(c.Code.Domain).Append('|').Append(c.FirstCodePart()).Append('|');
            foreach (var kv in c.VariantStrict) sb.Append(kv.Key).Append('=').Append(kv.Key == wild ? "*" : kv.Value).Append(';');
            return sb.ToString();
        }

        static void VariantSpread(Ctx x, bool full)
        {
            if (x.Report.FromCache) return;
            var list = VariantSpreadList(x);
            if (list.Count == 0) { x.Ok(x.L("diag-vspread-ok")); return; }
            x.Warn(x.L("diag-vspread-found", list.Count));
            x.Explain(x.L("diag-vspread-why"));
            foreach (var (group, attr, min, max, lo, hi) in list.Take(full ? 40 : 6))
                x.R.Add(group + " (" + attr + "): [[" + PlayerEmc.KeyOf(lo) + "]] " + F(min) + " … [[" + PlayerEmc.KeyOf(hi) + "]] " + F(max), EmcLineStyle.Text, 1);
            if (list.Count > (full ? 40 : 6)) x.Note(x.L("diag-more", list.Count - (full ? 40 : 6)));
            x.Fix(x.L("diag-vspread-fix"));
        }

        // ---------------------------------------------------------------- all of them, in the check-up and the deeper checks

        /// <summary>One line per health check, for the check-up. Returns how many found something.</summary>
        static int HealthSummary(Ctx x)
        {
            int before = x.R.Lines.Count;
            GameVersionCheck(x);
            Writable(x);
            JoinCost(x);
            PlayerData(x);
            Swings(x, false);
            SolverSkipped(x, false);
            TraderOverRecipe(x, false);
            VariantSpread(x, false);
            return x.R.Lines.Skip(before).Count(l => l.Style == EmcLineStyle.Check && l.Text.StartsWith("⚠", StringComparison.Ordinal));
        }

        /// <summary>The full health report (/emc diagnose health).</summary>
        static void Health(Ctx x)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-sec-health"));
            r.Add(x.L("diag-health-help"), EmcLineStyle.Note);
            GameVersionCheck(x);
            Writable(x);
            JoinCost(x);
            PlayerData(x);
            Swings(x, true);
            SolverSkipped(x, true);
            TraderOverRecipe(x, true);
            VariantSpread(x, true);
            r.Section(x.L("diag-sec-next"));
            r.Add(Link(ActionChecks, "", x.L("diag-q-checks")));
            r.Add(Link(ActionOverview, "", x.L("diag-q-back")));
        }

        public const string ActionHealth = "health";
    }
}
