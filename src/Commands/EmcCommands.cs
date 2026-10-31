using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// /emc                 – EMC of the held item and your personal EMC
    /// /emc set &lt;value&gt;   – (admin) pin the held item's value (0 = no EMC), saved to config, remaps
    /// /emc clear           – (admin) remove that pin, remaps
    /// /emc recalc          – (admin) rerun the mapper
    /// /emc dump            – (admin) write every value to ModData/vintagee/emc-values.csv
    /// /emc missing         – (admin) list blocks/items without a value (also written to ModData/vintagee/emc-missing.csv)
    /// /emc add &lt;amount&gt; [player]    – (admin) add personal EMC to a player (yourself by default)
    /// /emc remove &lt;amount&gt; [player] – (admin) take personal EMC from a player
    /// /emc give &lt;amount&gt;  – (admin) old name for /emc add on yourself
    /// /emc learn           – (admin) learn the held item without burning it
    /// </summary>
    public static class EmcCommands
    {
        public static void Register(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            var parsers = sapi.ChatCommands.Parsers;
            sapi.ChatCommands.Create("emc")
                .WithDescription("Vintage-E: show and manage EMC values")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(Safe(args => Info(mod, args)))
                .BeginSubCommand("help")
                    .WithDescription("List every Vintage-E command")
                    .HandleWith(Safe(args => Help(args)))
                .EndSubCommand()
                .BeginSubCommand("set")
                    .RequiresPlayer()
                    .WithDescription("Pin the held item's EMC value (0 removes it). Saved to ModConfig/vintagee.json.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("value"))
                    .HandleWith(Safe(args => Set(mod, args, (double)args[0])))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .RequiresPlayer()
                    .WithDescription("Remove the pinned value for the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(Safe(args => Set(mod, args, null)))
                .EndSubCommand()
                .BeginSubCommand("recalc")
                    .WithDescription("Recalculate all EMC values from recipes")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(Safe(args => Recalc(mod)))
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Write all EMC values to a CSV file; with 'hand' (or a block/item code) open a window explaining everything that makes up its value")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("what"))
                    .HandleWith(Safe(args => DumpCommand(mod, sapi, args, args[0] as string)))
                .EndSubCommand()
                .BeginSubCommand("loop")
                    .WithAlias("loops")
                    .WithDescription("Open a window of the value loops the mapper found: unvalued items that are each only made from the next")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(Safe(args => Loops(mod, sapi, args)))
                .EndSubCommand()
                .BeginSubCommand("missing")
                    .WithDescription("List blocks/items that ended up without an EMC value")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(Safe(args => Missing(mod, args)))
                .EndSubCommand()
                .BeginSubCommand("blacklist")
                    .WithDescription("Open a window listing everything on the EMC blacklist")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(Safe(args => Blacklist(mod, args)))
                .EndSubCommand()
                .BeginSubCommand("notransmute")
                    .WithDescription("Open a window of everything that keeps its EMC value but can't be burned, learned or created; or add/remove the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "add", "remove"))
                    .HandleWith(Safe(args => NoTransmute(mod, args, args[0] as string)))
                .EndSubCommand()
                .BeginSubCommand("graylist")
                    .WithAlias("greylist")
                    .WithDescription("Open the gray list (left out of /emc missing, valued only by recipes); or add/remove the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "add", "remove"))
                    .HandleWith(Safe(args => GrayList(mod, args, args[0] as string)))
                .EndSubCommand()
                .BeginSubCommand("recipe")
                    .WithAlias("recipes")
                    .WithDescription("Open the window of hand-written recipes (for things the mapper can't see being made); or add/remove one: output = 4 game:plank-oak + game:stick")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "add", "remove", "list"), parsers.OptionalAll("recipe"))
                    .HandleWith(Safe(args => Recipe(mod, args, args[0] as string, args[1] as string)))
                .EndSubCommand()
                .BeginSubCommand("contents")
                    .WithDescription("Open a window of everything that holds things or keeps its material, and how each is transmuted; or set the held item: item, full, itemprice or default")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("mode", "item", "full", "itemprice", "default"))
                    .HandleWith(Safe(args => Contents(mod, args, args[0] as string)))
                .EndSubCommand()
                .BeginSubCommand("compat")
                    .WithAlias("compatibility")
                    .WithDescription("Open a window of the compatibility value files (last-resort values for other mods' raw materials): which are active, and switch them off or on; or: /emc compat disable|enable <name>")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "disable", "enable", "off", "on", "list"), parsers.OptionalWord("name"))
                    .HandleWith(Safe(args => Compat(mod, args, args[0] as string, args[1] as string)))
                .EndSubCommand()
                .BeginSubCommand("configure")
                    .WithAlias("config")
                    .WithDescription("Show or change the server settings (ModConfig/vintagee.json) in game")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalAll("value"))
                    .HandleWith(Safe(args => ConfigureCommand.Handle(mod, args)))
                .EndSubCommand()
                .BeginSubCommand("add")
                    .WithDescription("Add EMC to a player's personal EMC (yours if no player is named)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(Safe(args => ChangeBalance(args, +1)))
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithDescription("Take EMC from a player's personal EMC (yours if no player is named)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(Safe(args => ChangeBalance(args, -1)))
                .EndSubCommand()
                .BeginSubCommand("give")
                    .WithDescription("Same as /emc add")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(Safe(args => ChangeBalance(args, +1)))
                .EndSubCommand()
                .BeginSubCommand("learn")
                    .RequiresPlayer()
                    .WithDescription("Learn the held item without burning it (/emc learn or /emc learn hand)")
                    .RequiresPrivilege(Privilege.chat)
                    .WithArgs(parsers.OptionalWordRange("what", "hand"))
                    .HandleWith(Safe(args => LearnHeld(mod, args)))
                .EndSubCommand();
        }

        /// <summary>
        /// /emc learn [hand]: learns the held item without burning it. Players get the same checks as the table's Learn slot
        /// (it needs a value and mustn't be on the no-transmute list); admins can learn anything they hold.
        /// </summary>
        static TextCommandResult LearnHeld(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            IPlayer player = args.Caller.Player;
            ItemStack held = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
            bool admin = args.Caller.HasPrivilege(Privilege.controlserver);
            if (!admin && player is IServerPlayer sp && mod.Transmutation != null)
            {
                string error = mod.Transmutation.Learn(sp, held, out bool isNew, out string[] errArgs);
                if (error != null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:error-" + error, errArgs ?? new string[0]));
                return TextCommandResult.Success(Lang.GetL(lang, isNew ? "vintagee:learned" : "vintagee:error-alreadyknown", held.GetName()));
            }
            bool learned = PlayerEmc.Learn(player, KnowledgeKey(mod, held));
            return TextCommandResult.Success(Lang.GetL(lang, learned ? "vintagee:learned" : "vintagee:error-alreadyknown", held.GetName()));
        }

        /// <summary>(usage, lang key of the description, admin only)</summary>
        static readonly (string usage, string key, bool admin)[] HelpLines =
        {
            ("/emc", "cmdhelp-emc", false),
            ("/emc help", "cmdhelp-help", false),
            ("/emc missing", "cmdhelp-missing", true),
            ("/emc blacklist", "cmdhelp-blacklist", true),
            ("/emc contents", "cmdhelp-contents", true),
            ("/emc contents <item|full|itemprice|default>", "cmdhelp-contentsset", true),
            ("/emc configure [setting] [value]", "cmdhelp-configure", true),
            ("/emc set <value>", "cmdhelp-set", true),
            ("/emc clear", "cmdhelp-clear", true),
            ("/emc recalc", "cmdhelp-recalc", true),
            ("/emc dump", "cmdhelp-dump", true),
            ("/emc dump hand|<code>", "cmdhelp-dumphand", true),
            ("/emc loop", "cmdhelp-loop", true),
            ("/emc notransmute", "cmdhelp-notransmute", true),
            ("/emc notransmute add|remove", "cmdhelp-notransmuteset", true),
            ("/emc graylist", "cmdhelp-graylist", true),
            ("/emc graylist add|remove", "cmdhelp-graylistset", true),
            ("/emc recipe", "cmdhelp-recipe", true),
            ("/emc recipe add|remove <output = ingredients>", "cmdhelp-recipeset", true),
            ("/emc compat", "cmdhelp-compat", true),
            ("/emc compat disable|enable <name>", "cmdhelp-compatset", true),
            ("/emc add <amount> [player]", "cmdhelp-add", true),
            ("/emc remove <amount> [player]", "cmdhelp-remove", true),
            ("/emc learn [hand]", "cmdhelp-learn", false),
        };

        static TextCommandResult Help(TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            bool isAdmin = args.Caller.Player == null || args.Caller.HasPrivilege(Privilege.controlserver);
            var sb = new StringBuilder(Lang.GetL(lang, "vintagee:cmdhelp-title")).Append('\n');
            foreach (var (usage, key, admin) in HelpLines)
            {
                sb.Append("<strong>").Append(usage).Append("</strong>");
                if (admin) sb.Append(' ').Append(Lang.GetL(lang, "vintagee:cmdhelp-admin"));
                sb.Append(" - ").Append(Lang.GetL(lang, "vintagee:" + key)).Append('\n');
            }
            if (!isAdmin) sb.Append(Lang.GetL(lang, "vintagee:cmdhelp-notadmin"));
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        /// <summary>/emc add and /emc remove: changes a player's personal EMC (never below 0).</summary>
        static TextCommandResult ChangeBalance(TextCommandCallingArgs args, int sign)
        {
            string lang = args.LanguageCode;
            double amount = (double)args[0];
            IPlayer target = args[1] as IPlayer ?? args.Caller.Player;
            if (target == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-needplayer"));
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-positive"));

            double before = PlayerEmc.GetEmc(target);
            double after = Math.Max(0, before + sign * amount);
            PlayerEmc.SetEmc(target, after);
            double changed = Math.Abs(after - before);

            if (target != args.Caller.Player && target is IServerPlayer sp)
            {
                string who = args.Caller.Player?.PlayerName ?? "Server";
                sp.SendMessage(GlobalConstants.GeneralChatGroup,
                    Lang.GetL(sp.LanguageCode, sign > 0 ? "vintagee:cmd-emc-gotadded" : "vintagee:cmd-emc-gotremoved", who, EmcFormat.Format(changed), EmcFormat.Format(after)),
                    EnumChatType.Notification);
            }
            return TextCommandResult.Success(Lang.GetL(lang, sign > 0 ? "vintagee:cmd-emc-added" : "vintagee:cmd-emc-removed",
                EmcFormat.Format(changed), target.PlayerName, EmcFormat.Format(after)));
        }

        static TextCommandResult Info(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            if (player == null) return TextCommandResult.Error("Run this in game, or use /emc recalc or /emc dump from the console.");
            var sb = new StringBuilder();
            sb.AppendLine(Lang.Get("vintagee:cmd-balance", EmcFormat.Format(PlayerEmc.GetEmc(player))));
            ItemStack held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held != null)
            {
                double v = mod.Emc.GetValue(held);
                sb.Append(v > 0
                    ? Lang.Get("vintagee:cmd-itemvalue", held.GetName(), held.Collectible.Code, EmcFormat.Format(v))
                    : Lang.Get("vintagee:cmd-novalue", held.GetName(), held.Collectible.Code));
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        static TextCommandResult Set(VintageEModSystem mod, TextCommandCallingArgs args, double? value)
        {
            ItemStack held = args.Caller.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.Get("vintagee:cmd-holdsomething"));
            string key = (held.Class == EnumItemClass.Block ? "block:" : "item:") + held.Collectible.Code;
            if (value.HasValue)
            {
                if (value.Value < 0) return TextCommandResult.Error("Value must be 0 or more");
            }
            else if (!mod.Config.FixedValues.ContainsKey(key))
            {
                return TextCommandResult.Error(Lang.Get("vintagee:cmd-nopin", key));
            }
            mod.SetFixedValue(held.Collectible, value);
            var report = mod.RecalculateEmc("/emc " + (value.HasValue ? "set" : "clear"));
            if (report == null) return TextCommandResult.Error("Mapping failed, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-itemvalue", held.GetName(), held.Collectible.Code, EmcFormat.Format(mod.Emc.GetValue(held.Collectible))));
        }

        static TextCommandResult Recalc(VintageEModSystem mod)
        {
            var report = mod.RecalculateEmc("/emc recalc");
            if (report == null) return TextCommandResult.Error("Mapping failed, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-recalc", report.Values.Count, report.Milliseconds, report.ConversionCount));
        }

        static TextCommandResult Missing(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            string file = report.Missing.Count == 0 ? null : mod.WriteMissingFile(report);
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, file); // the window shows everything, so nothing goes to chat
                return TextCommandResult.Success();
            }
            if (report.Missing.Count == 0)
            {
                return TextCommandResult.Success(Lang.Get("vintagee:cmd-missing-none", report.CreativeTotal));
            }
            string top = string.Join(", ", VintageEModSystem.MissingFamilies(report).Take(12).Select(f => f.Key + " (" + f.Value + ")"));
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-missing", report.Missing.Count, report.CreativeTotal, top, file ?? "-"));
        }

        /// <summary>/emc notransmute: the window; /emc notransmute add|remove: the held item.</summary>
        static TextCommandResult NoTransmute(VintageEModSystem mod, TextCommandCallingArgs args, string action)
        {
            string lang = args.LanguageCode;
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (action == "add" || action == "remove")
            {
                ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
                bool add = action == "add";
                mod.SetNoTransmute(new[] { VintageEModSystem.ConfigKey(held.Collectible) }, add);
                string msg = Lang.GetL(lang, add ? "vintagee:cmd-notransmute-held-added" : "vintagee:cmd-notransmute-held-removed", held.GetName(), held.Collectible.Code);
                if (!add && mod.Emc.IsNoTransmute(held.Collectible)) msg += " " + Lang.GetL(lang, "vintagee:cmd-notransmute-stillwildcard", 1);
                return TextCommandResult.Success(msg);
            }
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, null, EmcListKind.NoTransmute);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success(mod.Emc.NoTransmute.Count + " can't be transmuted: " + string.Join(", ", mod.Emc.NoTransmute.Take(40).Select(c => c.Code.ToString())));
        }

        /// <summary>/emc graylist: the window; /emc graylist add|remove: the held item.</summary>
        static TextCommandResult GrayList(VintageEModSystem mod, TextCommandCallingArgs args, string action)
        {
            string lang = args.LanguageCode;
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (action == "add" || action == "remove")
            {
                ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
                bool add = action == "add";
                mod.SetGrayList(new[] { VintageEModSystem.ConfigKey(held.Collectible) }, add);
                mod.RecalculateEmc(add ? "/emc graylist add" : "/emc graylist remove");
                return TextCommandResult.Success(Lang.GetL(lang, add ? "vintagee:cmd-gray-held-added" : "vintagee:cmd-gray-held-removed", held.GetName(), held.Collectible.Code));
            }
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, mod.LastReport ?? report, null, EmcListKind.GrayList);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success((report.Gray?.Count ?? 0) + " gray-listed: " + string.Join(", ", (report.Gray ?? new List<CollectibleObject>()).Take(40).Select(c => c.Code.ToString())));
        }

        static TextCommandResult Recipe(VintageEModSystem mod, TextCommandCallingArgs args, string action, string line)
        {
            string lang = args.LanguageCode;
            if (action == "add" || action == "remove")
            {
                if (string.IsNullOrWhiteSpace(line)) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:recipe-needline"));
                bool error;
                string msg = action == "add"
                    ? mod.AddCustomRecipe(line.Trim(), null, lang, out error)
                    : mod.RemoveCustomRecipe(CustomRecipe.Parse(line, out _)?.ToString() ?? line.Trim(), lang, out error);
                return error ? TextCommandResult.Error(msg) : TextCommandResult.Success(msg);
            }
            if (action != "list" && args.Caller.Player is IServerPlayer player)
            {
                mod.SendRecipeList(player);
                return TextCommandResult.Success();
            }
            var lines = mod.Config.CustomRecipes;
            if (lines.Count == 0) return TextCommandResult.Success(Lang.GetL(lang, "vintagee:recipe-none"));
            return TextCommandResult.Success(string.Join("\n", lines.Select((l, i) => (i + 1) + ". " + l)));
        }

        /// <summary>/emc compat: the window; /emc compat disable|enable &lt;name&gt;: switch one file (and remap); /emc compat list: text.</summary>
        static TextCommandResult Compat(VintageEModSystem mod, TextCommandCallingArgs args, string action, string name)
        {
            string lang = args.LanguageCode;
            if (action == "disable" || action == "enable" || action == "off" || action == "on")
            {
                if (string.IsNullOrWhiteSpace(name)) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:compat-needname"));
                string msg = mod.SetCompatDisabled(name.Trim(), action == "disable" || action == "off", lang, out bool error);
                if (args.Caller.Player is IServerPlayer sp && !error) mod.SendCompatList(sp, msg, false);
                return error ? TextCommandResult.Error(msg) : TextCommandResult.Success(msg);
            }
            if (action != "list" && args.Caller.Player is IServerPlayer player)
            {
                mod.SendCompatList(player);
                return TextCommandResult.Success();
            }
            var files = mod.Rules?.Compat ?? new List<EmcRuleSet.CompatFile>();
            if (files.Count == 0) return TextCommandResult.Success(Lang.GetL(lang, "vintagee:compat-none"));
            var used = mod.LastReport?.CompatUsed;
            return TextCommandResult.Success(string.Join("\n", files.Select(f =>
            {
                int n = used != null && used.TryGetValue(f.Source, out int u) ? u : 0;
                string state = f.Disabled ? Lang.GetL(lang, "vintagee:compat-state-disabled")
                    : f.MissingMod != null ? Lang.GetL(lang, "vintagee:compat-state-notloaded", f.MissingMod)
                    : Lang.GetL(lang, "vintagee:compat-state-active", n);
                return f.Name + " (" + string.Join(", ", f.RequiredMods) + "): " + state;
            })));
        }

        static TextCommandResult Blacklist(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, null, EmcListKind.Blacklist);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success(report.Blacklisted.Count + " blacklisted: " + string.Join(", ", report.Blacklisted.Take(40).Select(c => c.Code.ToString())));
        }

        /// <summary>The knowledge entry a held item teaches: the plain item, or exactly this version if it keeps its contents/material.</summary>
        internal static string KnowledgeKey(VintageEModSystem mod, ItemStack stack)
        {
            StackDataTable data = mod.Emc.StackData;
            if (data.CarriesData(stack.Collectible) && data.ModeOf(stack.Collectible) != StackDataMode.Item)
            {
                var kept = StackDataUtil.KeptData(stack);
                if (kept != null) return PlayerEmc.ExactKey(stack.Collectible, kept);
            }
            return PlayerEmc.KeyOf(stack.Collectible);
        }

        /// <summary>/emc contents: the window, or with a mode, set the held item's mode.</summary>
        static TextCommandResult Contents(VintageEModSystem mod, TextCommandCallingArgs args, string modeText)
        {
            string lang = args.LanguageCode;
            if (string.IsNullOrEmpty(modeText))
            {
                if (args.Caller.Player is IServerPlayer sp)
                {
                    mod.SendContentsList(sp);
                    return TextCommandResult.Success();
                }
                var table = mod.Emc.StackData;
                return TextCommandResult.Success(string.Join(", ", new[] { StackDataMode.Item, StackDataMode.Full, StackDataMode.ItemPrice }
                    .Select(m => StackDataTable.ModeName(m) + ": " + table.Kinds.Keys.Count(c => table.ModeOf(c) == m))));
            }
            ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
            if (held.Collectible is IEmcStorageItem) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-contents-kleinstar"));
            StackDataMode? mode = null;
            if (modeText != "default")
            {
                StackDataTable.TryParseMode(modeText, out StackDataMode m);
                mode = m;
            }
            mod.SetStackDataModes(new[] { held.Collectible }, mode);
            string modeName = mode == null ? Lang.GetL(lang, "vintagee:contents-mode-default") : Lang.GetL(lang, "vintagee:contents-mode-" + StackDataTable.ModeName(mode.Value));
            return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-contents-set", held.GetName(), modeName));
        }

        /// <summary>
        /// /emc dump: every value to a CSV file. /emc dump hand: the held stack (its wear, contents and size count);
        /// /emc dump &lt;code&gt;: a new one of that block/item. Either opens a window explaining every part of its value
        /// (the console gets the text), also written to ModData/vintagee/emc-dump-&lt;code&gt;.txt.
        /// </summary>
        static TextCommandResult DumpCommand(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args, string what)
        {
            string lang = args.LanguageCode;
            if (string.IsNullOrWhiteSpace(what)) return Dump(mod, sapi);
            ItemStack stack;
            bool held = what.Equals("hand", StringComparison.OrdinalIgnoreCase);
            if (held)
            {
                stack = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (stack == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
            }
            else
            {
                CollectibleObject c = FindCollectible(sapi, what);
                if (c == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:dump-unknowncode", what));
                stack = new ItemStack(c);
            }
            EmcReport report = EmcExplain.Dump(mod, sapi, stack, args.Caller.Player, held, lang);
            string file = mod.SaveReport(report, VintageEModSystem.DumpFileName(stack.Collectible));
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendReport(player, report); // the window has all of it, so nothing goes to chat
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success(report.PlainText(sapi.World) + "\n" + FileLine(lang, file));
        }

        static string FileLine(string lang, string file) => file != null ? Lang.GetL(lang, "vintagee:dump-file", file) : Lang.GetL(lang, "vintagee:dump-nofile");

        /// <summary>A block/item from what an admin typed: "game:stick", "stick" (base game), "item:game:stick", "block:game:log-*"'s first match...</summary>
        internal static CollectibleObject FindCollectible(ICoreServerAPI sapi, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            bool? block = null;
            if (text.StartsWith("item:", StringComparison.OrdinalIgnoreCase)) { block = false; text = text.Substring(5); }
            else if (text.StartsWith("block:", StringComparison.OrdinalIgnoreCase)) { block = true; text = text.Substring(6); }
            else if (text.Length > 2 && text[1] == ':' && (text[0] == 'i' || text[0] == 'b')) { block = text[0] == 'b'; text = text.Substring(2); }
            AssetLocation loc;
            try { loc = new AssetLocation(text); }
            catch { return null; }
            if (loc.Path.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                var match = sapi.World.Collectibles.FirstOrDefault(c => c?.Code != null && (block == null || (c.ItemClass == EnumItemClass.Block) == block) && WildcardUtil.Match(loc, c.Code));
                return match;
            }
            if (block != true && sapi.World.GetItem(loc) is Item item) return item;
            if (block != false && sapi.World.GetBlock(loc) is Block b && b.Code != null) return b;
            return null;
        }

        static TextCommandResult Dump(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            string file = mod.WriteValuesFile(mod.LastReport);
            if (file == null) return TextCommandResult.Error("Could not write the file, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-dumped", mod.Emc.Count, file));
        }

        /// <summary>/emc loop: a window of every value loop (and settled loop) the mapper found, also written to ModData/vintagee/emc-loops.txt.</summary>
        static TextCommandResult Loops(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            try
            {
                EmcReport report = EmcExplain.Loops(mod, sapi, lang);
                string file = mod.SaveReport(report, "emc-loops.txt");
                if (args.Caller.Player is IServerPlayer player)
                {
                    mod.SendReport(player, report);
                    return TextCommandResult.Success();
                }
                return TextCommandResult.Success(report.PlainText(sapi.World) + "\n" + FileLine(lang, file));
            }
            catch (Exception e)
            {
                sapi.Logger.Error("Vintage-E: /emc loop failed: {0}", e);
                return TextCommandResult.Error("/emc loop failed: " + e.Message + " (the server log has the details)");
            }
        }

        /// <summary>
        /// Chat reads messages as VTML, where a stray &lt; or &gt; ("A -> B", "&lt;value&gt;") breaks the markup and the
        /// whole message shows nothing. Escapes them, keeping the bold tags these messages use on purpose.
        /// </summary>
        public static string ChatSafe(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf('<') < 0 && text.IndexOf('>') < 0)) return text;
            return text.Replace("<strong>", "\u0001").Replace("</strong>", "\u0002")
                       .Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\u0001", "<strong>").Replace("\u0002", "</strong>");
        }

        /// <summary>Every /emc command's answer goes through <see cref="ChatSafe"/> when a player asked (the console shows text as it is).</summary>
        static OnCommandDelegate Safe(OnCommandDelegate handler) => args =>
        {
            TextCommandResult r = handler(args);
            if (r?.StatusMessage != null && args.Caller.Player != null) r.StatusMessage = ChatSafe(r.StatusMessage);
            return r;
        };
    }
}
