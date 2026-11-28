using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// 1.28.0: the commands Project Expansion has that Vintage-E didn't (its /pex emc get, knowledge
    /// learn/unlearn/test/clear and dumpfuelmap), as /emc balance, learn, unlearn, knows, knowledge, forgetall and
    /// fuels. Anyone can use them on themselves; another player, or a block or item named by code for learn, needs
    /// the controlserver privilege (as /emc add does). The console counts as an admin and has to name a player.
    /// </summary>
    public static partial class EmcCommands
    {
        static bool IsAdmin(TextCommandCallingArgs args) => args.Caller.Player == null || args.Caller.HasPrivilege(Privilege.controlserver);

        static bool IsSelf(TextCommandCallingArgs args, IPlayer target) => args.Caller.Player != null && target?.PlayerUID == args.Caller.Player.PlayerUID;

        static bool IsHand(string what) => string.IsNullOrWhiteSpace(what) || what.Equals("hand", StringComparison.OrdinalIgnoreCase);

        /// <summary>The player a command is about: the one named, else the caller. Null, or why not.</summary>
        static string Who(TextCommandCallingArgs args, object named, out IPlayer target)
        {
            string lang = args.LanguageCode;
            target = named as IPlayer ?? args.Caller.Player;
            if (target == null) return Lang.GetL(lang, "vintagee:cmd-emc-needplayer");
            if (!IsSelf(args, target) && !IsAdmin(args)) return Lang.GetL(lang, "vintagee:cmd-know-othersadmin");
            return null;
        }

        /// <summary>What a command is about: the held item ("hand" or nothing), or a block or item code. Null, or why not.</summary>
        static string Thing(ICoreServerAPI sapi, TextCommandCallingArgs args, string what, out ItemStack stack)
        {
            string lang = args.LanguageCode;
            stack = null;
            if (IsHand(what))
            {
                if (args.Caller.Player == null) return Lang.GetL(lang, "vintagee:cmd-know-needcode");
                stack = args.Caller.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                return stack == null ? Lang.GetL(lang, "vintagee:cmd-holdsomething") : null;
            }
            CollectibleObject c = FindCollectible(sapi, what);
            if (c == null) return Lang.GetL(lang, "vintagee:cmd-know-unknowncode", what);
            stack = new ItemStack(c);
            return null;
        }

        /// <summary>/emc balance [player]: a player's EMC and how much they have learned (Project Expansion's emc get).</summary>
        static TextCommandResult Balance(TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (Who(args, args[0], out IPlayer target) is string why) return TextCommandResult.Error(why);
            string emc = EmcFormat.Total(PlayerEmc.GetEmc(target));
            int known = PlayerEmc.GetKnown(target).Length;
            return TextCommandResult.Success(IsSelf(args, target)
                ? Lang.GetL(lang, "vintagee:cmd-showbalance-self", emc, known)
                : Lang.GetL(lang, "vintagee:cmd-showbalance", target.PlayerName, emc, known));
        }

        /// <summary>
        /// /emc learn [hand|&lt;code&gt;] [player]: learns a thing without burning it. A player learning what they hold gets the
        /// table's own checks (it needs a value and mustn't be on the no-transmute list); admins can teach anything, by
        /// code too, to anyone online (Project Expansion's knowledge learn).
        /// </summary>
        static TextCommandResult LearnCommand(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            string what = args[0] as string;
            bool admin = IsAdmin(args);
            if (!IsHand(what) && !admin) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-learn-codeadmin"));
            if (Who(args, args[1], out IPlayer target) is string who) return TextCommandResult.Error(who);
            if (Thing(sapi, args, what, out ItemStack stack) is string why) return TextCommandResult.Error(why);
            if (!admin)
            {
                // Only ever a player on themselves with what they hold (checked above): the table's Learn slot.
                if (mod.Transmutation == null || !(target is IServerPlayer sp)) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-notready"));
                string error = mod.Transmutation.Learn(sp, stack, out bool isNew, out string[] errArgs);
                if (error != null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:error-" + error, errArgs ?? new string[0]));
                return TextCommandResult.Success(Lang.GetL(lang, isNew ? "vintagee:learned" : "vintagee:error-alreadyknown", stack.GetName()));
            }
            bool learned = PlayerEmc.Learn(target, KnowledgeKey(mod, stack));
            if (IsSelf(args, target)) return TextCommandResult.Success(Lang.GetL(lang, learned ? "vintagee:learned" : "vintagee:error-alreadyknown", stack.GetName()));
            if (learned) Tell(target, "vintagee:cmd-learn-told", stack, args);
            return TextCommandResult.Success(Lang.GetL(lang, learned ? "vintagee:cmd-learn-other" : "vintagee:cmd-learn-otherknew", stack.GetName(), target.PlayerName));
        }

        /// <summary>/emc unlearn [hand|&lt;code&gt;] [player]: takes one thing out of a player's knowledge, as the table's
        /// Forget does (Project Expansion's knowledge unlearn). Players can do it to themselves.</summary>
        static TextCommandResult Unlearn(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (Who(args, args[1], out IPlayer target) is string who) return TextCommandResult.Error(who);
            if (Thing(sapi, args, args[0] as string, out ItemStack stack) is string why) return TextCommandResult.Error(why);
            bool forgot = PlayerEmc.Forget(target, KnowledgeKey(mod, stack));
            if (IsSelf(args, target))
                return TextCommandResult.Success(Lang.GetL(lang, forgot ? "vintagee:cmd-unlearn-self" : "vintagee:cmd-unlearn-self-notknown", stack.GetName()));
            if (forgot) Tell(target, "vintagee:cmd-unlearn-told", stack, args);
            return TextCommandResult.Success(Lang.GetL(lang, forgot ? "vintagee:cmd-unlearn-other" : "vintagee:cmd-unlearn-other-notknown", stack.GetName(), target.PlayerName));
        }

        /// <summary>/emc knows [hand|&lt;code&gt;] [player]: whether a player has learned a thing (Project Expansion's knowledge test).</summary>
        static TextCommandResult KnowsCommand(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (Who(args, args[1], out IPlayer target) is string who) return TextCommandResult.Error(who);
            if (Thing(sapi, args, args[0] as string, out ItemStack stack) is string why) return TextCommandResult.Error(why);
            bool knows = PlayerEmc.Knows(target, KnowledgeKey(mod, stack));
            return TextCommandResult.Success(IsSelf(args, target)
                ? Lang.GetL(lang, knows ? "vintagee:cmd-knows-self-yes" : "vintagee:cmd-knows-self-no", stack.GetName())
                : Lang.GetL(lang, knows ? "vintagee:cmd-knows-yes" : "vintagee:cmd-knows-no", target.PlayerName, stack.GetName()));
        }

        /// <summary>/emc knowledge [player]: how much a player has learned, and how many of those are exact versions.</summary>
        static TextCommandResult Knowledge(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (Who(args, args[0], out IPlayer target) is string who) return TextCommandResult.Error(who);
            int known = PlayerEmc.GetKnown(target).Length, exact = PlayerEmc.CountExact(target);
            return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-knowledge", target.PlayerName, known, exact, mod.Config.MaxExactKnowledge));
        }

        /// <summary>Who asked /emc forgetall about whom, and when: the second ask within <see cref="ForgetAllSeconds"/> does it.</summary>
        static readonly Dictionary<string, (string target, long at)> forgetAllAsked = new Dictionary<string, (string, long)>();
        public const int ForgetAllSeconds = 30;

        /// <summary>True when <paramref name="caller"/> asked to forget all of <paramref name="target"/>'s knowledge before,
        /// less than <see cref="ForgetAllSeconds"/> ago (the ask is then used up); otherwise notes this ask and returns false.</summary>
        internal static bool ConfirmForgetAll(string caller, string target, long nowMs)
        {
            lock (forgetAllAsked)
            {
                if (forgetAllAsked.TryGetValue(caller, out var asked) && asked.target == target && nowMs - asked.at >= 0 && nowMs - asked.at <= ForgetAllSeconds * 1000L)
                {
                    forgetAllAsked.Remove(caller);
                    return true;
                }
                forgetAllAsked[caller] = (target, nowMs);
                return false;
            }
        }

        /// <summary>/emc forgetall &lt;player&gt; (admins): forgets everything a player has learned (Project Expansion's
        /// knowledge clear). It can't be undone, so it asks for the same command a second time first.</summary>
        static TextCommandResult ForgetAll(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (!(args[0] is IPlayer target)) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-needplayer"));
            int count = PlayerEmc.GetKnown(target).Length;
            if (count == 0) return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-forgetall-none", target.PlayerName));
            if (!ConfirmForgetAll(args.Caller.Player?.PlayerUID ?? "console", target.PlayerUID, sapi.World.ElapsedMilliseconds))
                return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-forgetall-confirm", target.PlayerName, count, ForgetAllSeconds));
            PlayerEmc.SetKnown(target, Array.Empty<string>());
            if (!IsSelf(args, target) && target is IServerPlayer sp)
                sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, "vintagee:cmd-forgetall-told", args.Caller.Player?.PlayerName ?? "Server", count), EnumChatType.Notification);
            return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-forgetall-done", target.PlayerName, count));
        }

        /// <summary>/emc fuels: the fuels an Energy Collector's fuel slot upgrades, cheapest first, with their values
        /// (Project Expansion's dumpfuelmap).</summary>
        static TextCommandResult Fuels(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            List<CollectibleObject> chain = FuelUpgrader.Chain(mod, sapi.World);
            if (chain.Count == 0) return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-fuels-none"));
            var sb = new StringBuilder(Lang.GetL(lang, "vintagee:cmd-fuels-title"));
            double before = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                double value = mod.Emc.GetValue(chain[i]);
                string name = new ItemStack(chain[i]).GetName();
                sb.Append('\n').Append(i == 0
                    ? Lang.GetL(lang, "vintagee:cmd-fuels-first", name, EmcFormat.Format(value))
                    : Lang.GetL(lang, "vintagee:cmd-fuels-line", name, EmcFormat.Format(value), EmcFormat.Format(value - before)));
                before = value;
            }
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>Tells a player an admin changed their knowledge (they may wonder where an entry went).</summary>
        static void Tell(IPlayer target, string key, ItemStack stack, TextCommandCallingArgs args)
        {
            if (!(target is IServerPlayer sp)) return;
            sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, key, args.Caller.Player?.PlayerName ?? "Server", stack.GetName()), EnumChatType.Notification);
        }
    }
}
