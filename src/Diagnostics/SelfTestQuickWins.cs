using System;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// 1.28.0, the plan's quick wins from Project Expansion: the EMC display, the learned line on tooltips, and the
    /// commands it had that Vintage-E didn't. The display and the tooltip are drawn in a player's game, which the test
    /// doesn't have: what it checks is the logic they run on (the rate, the texts, the knowledge key, the settings), and
    /// the commands as the console runs them.
    /// </summary>
    public partial class SelfTest
    {
        void CheckQuickWins()
        {
            Line("Quick wins: EMC display, learned line, commands (1.28.0)");

            // The rate: from the balance over the last ten seconds.
            var r = new EmcRate();
            double none = r.PerSecond;
            r.Add(0, 100m);
            r.Add(0.5, 150m);
            double tooSoon = r.PerSecond;
            Check(none == 0 && tooSoon == 0, "no rate before there is a second of balances to go by");
            r.Clear();
            for (int t = 0; t <= 4; t++) r.Add(t, 10m * t);
            double steady = r.PerSecond;
            Check(Math.Abs(steady - 10) < 1e-9, $"a balance growing 10 a second shows 10 EMC/s ({steady})");
            for (int t = 5; t <= 30; t++) r.Add(t, 40m);
            double stopped = r.PerSecond;
            Check(stopped == 0, $"ten seconds after it stops changing, the rate is gone ({stopped})");
            r.Clear();
            r.Add(0, 1000m);
            r.Add(2, 0m);
            double spent = r.PerSecond;
            Check(Math.Abs(spent + 500) < 1e-9, $"spending shows as a falling rate ({spent})");
            r.Add(1, 5m); // the clock went back: a new world
            Check(r.PerSecond == 0, "a clock that goes back starts the rate again (no rate across worlds)");
            r.Clear();
            r.Add(0, 0m);
            r.Add(5, decimal.MaxValue);
            double huge = r.PerSecond;
            Check(!double.IsNaN(huge) && !double.IsInfinity(huge) && huge > 0, "a balance going from nothing to the largest number gives a rate, not an error");

            // What it shows.
            Check(EmcRate.RateText(12.5) == Lang.Get("vintagee:emchud-rate", "+12.5") && EmcRate.RateText(-300) == Lang.Get("vintagee:emchud-rate", "-300"),
                $"the rate reads {EmcRate.RateText(12.5)} and {EmcRate.RateText(-300)}");
            Check(EmcRate.RateText(0) == null && EmcRate.RateText(0.0004) == null && EmcRate.RateText(double.NaN) == null && EmcRate.RateText(double.PositiveInfinity) == null,
                "no rate line while it would show as 0, or for a broken number");
            Check(EmcRate.RateText(2.5e9) == Lang.Get("vintagee:emchud-rate", "+" + EmcFormat.Short(2.5e9)), $"a big rate is short ({EmcRate.RateText(2.5e9)})");
            Check(EmcRate.BalanceText(1_234_567.891m) == "1,234,567.89" && EmcRate.BalanceText(3_518_437_208_883_200_000m) == EmcFormat.ShortTotal(3_518_437_208_883_200_000m),
                $"the balance is in full up to a trillion, short past it ({EmcRate.BalanceText(1_234_567.891m)}, {EmcRate.BalanceText(3_518_437_208_883_200_000m)})");

            // Its settings: in the client file, with defaults for files from before.
            VintageEClientConfig old = null;
            try { old = JsonConvert.DeserializeObject<VintageEClientConfig>("{ \"WatchIcon\": false, \"WatchIconPosition\": \"BottomLeft\" }"); }
            catch (Exception e) { Line("  reading an old client settings file threw " + e.Message); }
            old?.Normalize();
            Check(old != null && old.EmcHud && old.LearnedTooltip && old.EmcHudPosition == "TopLeft" && !old.WatchIcon && old.WatchIconPosition == "BottomLeft",
                "a client settings file from before 1.28.0 gets the display and the learned line switched on, and keeps its own settings");
            var odd = new VintageEClientConfig { EmcHudPosition = "bottomright", EmcHudOffsetX = 99999, WatchIconPosition = "nowhere" };
            odd.Normalize();
            Check(odd.EmcHudPosition == "BottomRight" && odd.EmcHudOffsetX == 2000 && odd.WatchIconPosition == "TopRight"
                && odd.EmcHudArea == EnumDialogArea.RightBottom && new VintageEClientConfig().EmcHudArea == EnumDialogArea.LeftTop,
                "the display's place is read in any case, kept on screen, and an unknown place falls back");

            // The learned line asks with the key a burn adds.
            CollectibleObject stick = sapi.World.GetItem(new AssetLocation("game:stick"));
            Block clutter = sapi.World.GetBlock(new AssetLocation("game:clutter"));
            Block lantern = sapi.World.GetBlock(new AssetLocation("game:lantern-small-up"));
            var stacks = new System.Collections.Generic.List<ItemStack>();
            if (stick != null) stacks.Add(new ItemStack(stick, 5));
            foreach (Block typed in new[] { clutter, lantern })
            {
                if (typed == null) continue;
                foreach (ItemStack v in StackDataUtil.CreativeVersions(typed, sapi.World).Take(10))
                {
                    ItemStack picked = v.Clone();
                    StackDataUtil.MarkCollected(picked); // as the game drops reparable clutter; nothing for a lantern
                    stacks.Add(picked);
                }
            }
            int same = 0, burned = 0, lanterns = 0, clutters = 0;
            string first = null;
            foreach (ItemStack s in stacks)
            {
                if (mod.Transmutation == null) break;
                if (mod.Transmutation.PlanBurn(null, s, out TransmutationService.BurnPlan plan, out _, learnOnly: true) != null || plan?.Key == null) continue;
                burned++;
                if (s.Collectible == lantern) lanterns++;
                if (s.Collectible == clutter) clutters++;
                if (plan.Key == EmcCommands.KnowledgeKey(mod, s)) same++;
                else first ??= s.Collectible.Code + ": burn " + plan.Key + ", tooltip " + EmcCommands.KnowledgeKey(mod, s);
            }
            Check(lanterns > 0 && clutters > 0 && same == burned,
                $"the learned line asks about the same entry a burn adds ({same} of {burned}: a stick, {clutters} clutter types, {lanterns} lantern versions{(first == null ? "" : "; " + first)})");

            // The commands, as the console runs them.
            string fuels = RunCommand("/emc fuels");
            var chain = FuelUpgrader.Chain(mod, sapi.World);
            Check(chain.Count >= 2 && chain.All(c => fuels.Contains(new ItemStack(c).GetName())) && fuels.EndsWith("[Success]", StringComparison.Ordinal),
                $"/emc fuels lists the collector's fuels ({chain.Count}): {fuels.Replace('\n', ' ')}");
            string needPlayer = Lang.Get("vintagee:cmd-emc-needplayer");
            string[] needOne = { "/emc balance", "/emc knowledge", "/emc learn game:stick", "/emc unlearn game:stick", "/emc knows game:stick" };
            var answers = needOne.Select(c => (c, a: RunCommand(c))).ToList();
            var wrong = answers.Where(x => !x.a.Contains(needPlayer)).ToList();
            Check(wrong.Count == 0, "from the console, the knowledge and balance commands ask for a player" + (wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong.Select(x => x.c + " -> " + x.a))));
            string forgetNobody = RunCommand("/emc forgetall");
            Check(!forgetNobody.EndsWith("[Success]", StringComparison.Ordinal), $"/emc forgetall needs a player named ({forgetNobody})");
            string help = RunCommand("/emc help");
            string[] listed = { "/emc balance", "/emc unlearn", "/emc knows", "/emc knowledge", "/emc forgetall", "/emc fuels", "/emc learn [hand|&lt;code&gt;] [player]" };
            var missingHelp = listed.Where(l => !help.Contains(l) && !help.Contains(l.Replace("&lt;", "<").Replace("&gt;", ">"))).ToList();
            Check(missingHelp.Count == 0, "/emc help lists the new commands" + (missingHelp.Count == 0 ? "" : " (missing " + string.Join(", ", missingHelp) + ")"));

            // /emc forgetall asks a second time first.
            string a1 = "selftest-" + Guid.NewGuid().ToString("N");
            bool firstAsk = EmcCommands.ConfirmForgetAll(a1, "p1", 1000);
            bool secondAsk = EmcCommands.ConfirmForgetAll(a1, "p1", 6000);
            bool usedUp = EmcCommands.ConfirmForgetAll(a1, "p1", 7000);
            bool late = EmcCommands.ConfirmForgetAll(a1, "p1", 7000 + EmcCommands.ForgetAllSeconds * 1000L + 1);
            bool otherPlayer = EmcCommands.ConfirmForgetAll(a1, "p2", 7000 + EmcCommands.ForgetAllSeconds * 1000L + 2);
            bool otherThen = EmcCommands.ConfirmForgetAll(a1, "p2", 7000 + EmcCommands.ForgetAllSeconds * 1000L + 500);
            Check(!firstAsk && secondAsk && !usedUp && !late && !otherPlayer && otherThen,
                "/emc forgetall only clears on the same command again within 30 seconds, about the same player, once");

            // Every new line of text is there.
            string[] keys = { "emchud-balance", "emchud-rate", "tooltip-learned", "tooltip-notlearned", "clientcfg-emc", "clientcfg-emchud", "clientcfg-emchud-note",
                "clientcfg-learnedtip", "cmd-know-othersadmin", "cmd-know-needcode", "cmd-know-unknowncode", "cmd-learn-codeadmin", "cmd-learn-other",
                "cmd-learn-otherknew", "cmd-learn-told", "cmd-unlearn-self", "cmd-unlearn-self-notknown", "cmd-unlearn-other", "cmd-unlearn-other-notknown",
                "cmd-unlearn-told", "cmd-knows-self-yes", "cmd-knows-self-no", "cmd-knows-yes", "cmd-knows-no", "cmd-showbalance-self", "cmd-showbalance",
                "cmd-knowledge", "cmd-forgetall-none", "cmd-forgetall-confirm", "cmd-forgetall-done", "cmd-forgetall-told", "cmd-fuels-none", "cmd-fuels-title",
                "cmd-fuels-first", "cmd-fuels-line", "cmdhelp-unlearn", "cmdhelp-knows", "cmdhelp-balance", "cmdhelp-knowledge", "cmdhelp-forgetall", "cmdhelp-fuels" };
            var noText = keys.Where(k => Lang.GetMatchingIfExists("vintagee:" + k) == null).ToList();
            Check(noText.Count == 0, $"every new line of text is in the language file ({keys.Length}{(noText.Count == 0 ? "" : "; missing " + string.Join(", ", noText))})");
        }
    }
}
