using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Vintagestory.Common;

public class ChatCommandImpl : IChatCommand
{
	private ChatCommandApi _cmdapi;

	private ChatCommandImpl _parent;

	protected bool ignoreAdditonalArguments;

	protected string name;

	protected string[] examples;

	protected List<string> aliases;

	protected List<string> rootAliases;

	protected string privilege;

	protected string description;

	protected string additionalInformation;

	protected OnCommandDelegate handler;

	protected Dictionary<string, IChatCommand> subCommands = new Dictionary<string, IChatCommand>(StringComparer.OrdinalIgnoreCase);

	private ICommandArgumentParser[] _parsers = Array.Empty<ICommandArgumentParser>();

	public bool Incomplete
	{
		get
		{
			if (name != null)
			{
				return GetPrivilege() == null;
			}
			return true;
		}
	}

	public List<string> Aliases => aliases;

	public List<string> RootAliases => rootAliases;

	public string CommandPrefix => _cmdapi.CommandPrefix;

	public string Name => name;

	public string Description => description;

	public string AdditionalInformation => additionalInformation;

	public string[] Examples => examples;

	public string FullName
	{
		get
		{
			if (_parent != null)
			{
				return _parent.name + " " + name;
			}
			return _cmdapi.CommandPrefix + name;
		}
	}

	public IChatCommand this[string name] => subCommands[name];

	public bool AnyPrivilegeSet => !string.IsNullOrEmpty(GetPrivilege());

	public string CallSyntax
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (_parent == null)
			{
				stringBuilder.Append(_cmdapi.CommandPrefix);
			}
			else
			{
				stringBuilder.Append(_parent.CallSyntax);
			}
			stringBuilder.Append(Name);
			stringBuilder.Append(" ");
			AddParameterSyntax(stringBuilder, "");
			return stringBuilder.ToString();
		}
	}

	public string CallSyntaxUnformatted
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (_parent == null)
			{
				stringBuilder.Append(_cmdapi.CommandPrefix);
			}
			else
			{
				stringBuilder.Append(_parent.CallSyntaxUnformatted);
			}
			stringBuilder.Append(Name);
			stringBuilder.Append(" ");
			AddParameterSyntaxUnformatted(stringBuilder, "");
			return stringBuilder.ToString();
		}
	}

	public IEnumerable<IChatCommand> Subcommands => subCommands.Values;

	public Dictionary<string, IChatCommand> AllSubcommands => subCommands;

	private event CommandPreconditionDelegate _precond;

	public string GetPrivilege()
	{
		string text = privilege;
		if (text == null)
		{
			ChatCommandImpl parent = _parent;
			if (parent == null)
			{
				return null;
			}
			text = parent.GetPrivilege();
		}
		return text;
	}

	public string GetFullName(string alias, bool isRootAlias = false)
	{
		if ((_parent == null) | isRootAlias)
		{
			return _cmdapi.CommandPrefix + alias;
		}
		if (alias != name)
		{
			return _cmdapi.CommandPrefix + alias;
		}
		return _parent.name + " " + alias;
	}

	public ChatCommandImpl(ChatCommandApi cmdapi, string name = null, ChatCommandImpl parent = null)
	{
		_cmdapi = cmdapi;
		this.name = name;
		_parent = parent;
	}

	public IChatCommand EndSubCommand()
	{
		if (_parent == null)
		{
			throw new InvalidOperationException("Not inside a subcommand");
		}
		return _parent;
	}

	public IChatCommand HandleWith(OnCommandDelegate handler)
	{
		this.handler = handler;
		return this;
	}

	public IChatCommand RequiresPrivilege(string privilege)
	{
		this.privilege = privilege;
		return this;
	}

	public IChatCommand WithDescription(string description)
	{
		this.description = description;
		return this;
	}

	public IChatCommand WithAdditionalInformation(string text)
	{
		additionalInformation = text;
		return this;
	}

	public IChatCommand WithName(string commandName)
	{
		if (_parent != null)
		{
			throw new InvalidOperationException("This method is not available for subcommands");
		}
		if (_cmdapi.ichatCommands.ContainsKey(commandName))
		{
			throw new InvalidOperationException("Command with such name already exists");
		}
		name = commandName;
		_cmdapi.ichatCommands[commandName] = this;
		return this;
	}

	public IChatCommand WithRootAlias(string commandName)
	{
		string text = commandName.ToLowerInvariant();
		if (rootAliases == null)
		{
			rootAliases = new List<string>();
		}
		rootAliases.Add(text);
		return _cmdapi.ichatCommands[text] = this;
	}

	public IChatCommand BeginSub(string name)
	{
		return BeginSubCommand(name);
	}

	public IChatCommand EndSub()
	{
		return EndSubCommand();
	}

	public IChatCommand BeginSubCommand(string name)
	{
		name = name.ToLowerInvariant();
		if (subCommands.TryGetValue(name, out var value))
		{
			return value;
		}
		return subCommands[name] = new ChatCommandImpl(_cmdapi, name, this);
	}

	public IChatCommand BeginSubCommands(params string[] names)
	{
		names[0] = names[0].ToLowerInvariant();
		IChatCommand chatCommand2;
		if (!subCommands.ContainsKey(names[0]))
		{
			IChatCommand chatCommand = new ChatCommandImpl(_cmdapi, names[0], this);
			chatCommand2 = chatCommand;
		}
		else
		{
			chatCommand2 = subCommands[names[0]];
		}
		ChatCommandImpl chatCommandImpl = chatCommand2 as ChatCommandImpl;
		ChatCommandImpl chatCommandImpl2 = chatCommandImpl;
		if (chatCommandImpl2.aliases == null)
		{
			chatCommandImpl2.aliases = new List<string>();
		}
		string[] array = names;
		foreach (string text in array)
		{
			subCommands[text.ToLowerInvariant()] = chatCommandImpl;
		}
		array = names[1..];
		foreach (string text2 in array)
		{
			chatCommandImpl.Aliases.Add(text2.ToLowerInvariant());
		}
		return chatCommandImpl;
	}

	public IChatCommand WithSubCommand(string name, string desc, OnCommandDelegate handler, params ICommandArgumentParser[] parsers)
	{
		BeginSubCommand(name).WithName(name).WithDescription(desc).WithArgs(parsers)
			.HandleWith(handler)
			.EndSubCommand();
		return this;
	}

	public IChatCommand WithArgs(params ICommandArgumentParser[] parsers)
	{
		_parsers = parsers;
		return this;
	}

	public void Execute(TextCommandCallingArgs callargs, Action<TextCommandResult> onCommandComplete = null)
	{
		callargs.Command = this;
		if (_precond != null)
		{
			Delegate[] invocationList = _precond.GetInvocationList();
			for (int i = 0; i < invocationList.Length; i++)
			{
				TextCommandResult textCommandResult = ((CommandPreconditionDelegate)invocationList[i])(callargs);
				if (textCommandResult.Status == EnumCommandStatus.Error)
				{
					if (onCommandComplete != null)
					{
						onCommandComplete(textCommandResult);
					}
					return;
				}
			}
		}
		Dictionary<int, AsyncParseResults> asyncParseResults = null;
		int deferredCount = 0;
		bool allParsed = false;
		ICommandArgumentParser commandArgumentParser = null;
		for (int j = 0; j < _parsers.Length; j++)
		{
			int index = j;
			ICommandArgumentParser commandArgumentParser2 = _parsers[j];
			commandArgumentParser2.PreProcess(callargs);
			if (commandArgumentParser2.IsMissing)
			{
				if (commandArgumentParser2.IsMandatoryArg)
				{
					onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("command-missingarg", j + 1, Lang.Get(commandArgumentParser2.ArgumentName)), "missingarg"));
					return;
				}
				continue;
			}
			EnumParseResult enumParseResult = commandArgumentParser2.TryProcess(callargs, (AsyncParseResults data) =>
			{
				int num = deferredCount;
				deferredCount = num - 1;
				if (asyncParseResults == null)
				{
					asyncParseResults = new Dictionary<int, AsyncParseResults>();
				}
				asyncParseResults[index] = data;
				if ((deferredCount == 0) & allParsed)
				{
					CallHandler(callargs, onCommandComplete, asyncParseResults);
				}
			});
			if (enumParseResult == EnumParseResult.Good)
			{
				continue;
			}
			if (enumParseResult == EnumParseResult.Deferred)
			{
				int i = deferredCount;
				deferredCount = i + 1;
			}
			if (commandArgumentParser != null)
			{
				onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("command-argumenterror1", commandArgumentParser.ArgumentName, Lang.Get(commandArgumentParser.LastErrorMessage ?? "unknown error")), "wrongarg"));
			}
			switch (enumParseResult)
			{
			case EnumParseResult.Bad:
				onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("command-argumenterror2", j + 1, commandArgumentParser2.ArgumentName, Lang.Get(commandArgumentParser2.LastErrorMessage ?? "unknown error")), "wrongarg"));
				return;
			case EnumParseResult.DependsOnSubsequent:
				if (commandArgumentParser != null)
				{
					return;
				}
				commandArgumentParser = commandArgumentParser2;
				break;
			}
		}
		callargs.Parsers.AddRange(_parsers);
		allParsed = true;
		if (deferredCount == 0)
		{
			CallHandler(callargs, onCommandComplete, asyncParseResults);
		}
		else
		{
			onCommandComplete?.Invoke(TextCommandResult.Deferred);
		}
	}

	private void CallHandler(TextCommandCallingArgs callargs, Action<TextCommandResult> onCommandComplete = null, Dictionary<int, AsyncParseResults> asyncParseResults = null)
	{
		if (asyncParseResults != null)
		{
			foreach (KeyValuePair<int, AsyncParseResults> asyncParseResult in asyncParseResults)
			{
				int key = asyncParseResult.Key;
				if (asyncParseResult.Value.Status == EnumParseResultStatus.Error)
				{
					onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("Error in argument {0} ({1}): {2}", key + 1, Lang.Get(_parsers[key].ArgumentName), Lang.Get(_parsers[key].LastErrorMessage)), "wrongarg"));
					return;
				}
				callargs.Parsers[key].SetValue(asyncParseResult.Value.Data);
			}
		}
		string text = callargs.RawArgs.PeekWord()?.ToLowerInvariant();
		if (text != null && subCommands.ContainsKey(text))
		{
			callargs.SubCmdCode = callargs.RawArgs.PopWord();
			subCommands[text].Execute(callargs, onCommandComplete);
		}
		else if (!callargs.Caller.HasPrivilege(GetPrivilege()))
		{
			onCommandComplete?.Invoke(new TextCommandResult
			{
				Status = EnumCommandStatus.Error,
				ErrorCode = "noprivilege",
				StatusMessage = Lang.Get("Sorry, you don't have the privilege to use this command")
			});
		}
		else if (handler == null)
		{
			if (subCommands.Count > 0)
			{
				List<string> list = new List<string>();
				foreach (string key2 in subCommands.Keys)
				{
					list.Add(string.Format("<a href=\"chattype://{0}\">{1}</a>", callargs.Command.FullName + " " + key2, key2));
				}
				onCommandComplete?.Invoke(TextCommandResult.Error("Choose a subcommand: " + string.Join(", ", list), "selectsubcommand"));
			}
			else
			{
				onCommandComplete?.Invoke(TextCommandResult.Error("Insufficently set up command - no handlers or subcommands set up", "incompletecommandsetup"));
			}
		}
		else if (callargs.RawArgs.Length > 0 && callargs.ArgCount >= 0 && !ignoreAdditonalArguments)
		{
			if (_parent == null)
			{
				if (subCommands.Count > 0)
				{
					onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("Command {0}, unrecognised subcommand: {1}", _cmdapi.CommandPrefix + name, text), "wrongargcount"));
				}
				else
				{
					onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("Command {0}, too many arguments", _cmdapi.CommandPrefix + name), "wrongargcount"));
				}
			}
			else
			{
				onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("Subcommand {0}, too many arguments", name), "wrongargcount"));
			}
		}
		else
		{
			TextCommandResult obj = handler(callargs);
			onCommandComplete?.Invoke(obj);
		}
	}

	public IChatCommand WithPreCondition(CommandPreconditionDelegate precond)
	{
		_precond += precond;
		return this;
	}

	public IChatCommand WithAlias(params string[] names)
	{
		if (aliases == null)
		{
			aliases = new List<string>();
		}
		for (int i = 0; i < names.Length; i++)
		{
			string text = names[i].ToLowerInvariant();
			if (_parent == null)
			{
				_cmdapi.ichatCommands[text] = this;
			}
			else
			{
				_parent.subCommands[text] = this;
			}
			aliases.Add(text);
		}
		return this;
	}

	public IChatCommand GroupWith(params string[] name)
	{
		WithAlias(name);
		return this;
	}

	public IChatCommand WithExamples(params string[] examples)
	{
		this.examples = examples;
		return this;
	}

	public IChatCommand RequiresPlayer()
	{
		_precond += (TextCommandCallingArgs args) => (args.Caller.Player == null) ? TextCommandResult.Error("Caller must be player") : TextCommandResult.Success();
		return this;
	}

	public void Validate()
	{
		if (_parent != null)
		{
			throw new Exception("Validate not called from the root command, likely missing EndSub()");
		}
		ValidateRecursive();
	}

	private void ValidateRecursive()
	{
		if (string.IsNullOrEmpty(description))
		{
			throw new Exception("Command " + CallSyntax + ": Description not set");
		}
		if (string.IsNullOrEmpty(name))
		{
			throw new Exception("Command " + CallSyntax + ": Name not set");
		}
		if (!AnyPrivilegeSet)
		{
			throw new Exception("Command " + CallSyntax + ": Privilege not set for subcommand or any parent command");
		}
		if (subCommands.Count == 0 && handler == null)
		{
			throw new Exception("Command " + CallSyntax + ": No handler or subcommands defined");
		}
		foreach (KeyValuePair<string, IChatCommand> subCommand in subCommands)
		{
			(subCommand.Value as ChatCommandImpl).ValidateRecursive();
		}
	}

	public bool IsAvailableTo(Caller caller)
	{
		return caller.HasPrivilege(GetPrivilege());
	}

	public IChatCommand IgnoreAdditionalArgs()
	{
		ignoreAdditonalArguments = true;
		return this;
	}

	public string GetFullSyntaxHandbook(Caller caller, string indent = "", bool isRootAlias = false)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Dictionary<string, IChatCommand> ordered = IChatCommandApi.GetOrdered(AllSubcommands);
		if (handler != null && (isRootAlias || _parent == null))
		{
			if (RootAliases != null)
			{
				foreach (string rootAlias in RootAliases)
				{
					stringBuilder.AppendLine(indent + $"<a href=\"chattype://{GetCallSyntaxUnformatted(rootAlias, isRootAlias: true)}\">{GetCallSyntax(rootAlias, isRootAlias: true)}</a>");
				}
			}
			stringBuilder.AppendLine(indent + $"<a href=\"chattype://{CallSyntaxUnformatted}\">{CallSyntax}</a>");
		}
		if (Description != null)
		{
			AddVerticalSpace(stringBuilder);
			stringBuilder.AppendLine(indent + Description);
		}
		if (AdditionalInformation != null)
		{
			AddVerticalSpace(stringBuilder);
			stringBuilder.AppendLine(indent + AdditionalInformation);
		}
		AddSyntaxExplanation(stringBuilder, indent);
		if (Examples != null && Examples.Length != 0)
		{
			AddVerticalSpace(stringBuilder);
			stringBuilder.AppendLine(indent + ((Examples.Length > 1) ? "Examples:" : "Example:"));
			string[] array = Examples;
			foreach (string text in array)
			{
				stringBuilder.AppendLine(indent + text);
			}
		}
		if (ordered.Count > 0 && !isRootAlias)
		{
			AddVerticalSpace(stringBuilder);
			WriteCommandsListHandbook(stringBuilder, ordered, caller, indent);
		}
		AddVerticalSpace(stringBuilder);
		return stringBuilder.ToString();
	}

	public string GetFullSyntaxConsole(Caller caller)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Dictionary<string, IChatCommand> allSubcommands = AllSubcommands;
		if (allSubcommands.Count > 0)
		{
			stringBuilder.AppendLine("Available subcommands:");
			WriteCommandsList(stringBuilder, allSubcommands, caller, isSubCommand: true);
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("Type <code>/help " + CallSyntax.Substring(1) + " &lt;<i>subcommand_name</i>&gt;</code> for help on a specific subcommand");
		}
		else
		{
			stringBuilder.AppendLine();
			if (Description != null)
			{
				stringBuilder.AppendLine(Description);
			}
			if (AdditionalInformation != null)
			{
				stringBuilder.AppendLine(AdditionalInformation);
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("Usage: <code>");
			stringBuilder.Append(CallSyntax);
			stringBuilder.Append("</code>");
			AddSyntaxExplanation(stringBuilder, "");
			if (Examples != null && Examples.Length != 0)
			{
				stringBuilder.AppendLine((Examples.Length > 1) ? "Examples:" : "Example:");
				string[] array = Examples;
				foreach (string value in array)
				{
					stringBuilder.AppendLine(value);
				}
			}
		}
		return stringBuilder.ToString();
	}

	public static void WriteCommandsListHandbook(StringBuilder text, Dictionary<string, IChatCommand> commands, Caller caller, string indent = "")
	{
		text.AppendLine();
		foreach (ChatCommandImpl item in commands.Values.Distinct(ChatCommandComparer.Comparer).Cast<ChatCommandImpl>())
		{
			if (caller != null && !item.IsAvailableTo(caller))
			{
				continue;
			}
			if (item.AllSubcommands.Count > 0 && item.handler == null)
			{
				if (item.RootAliases != null)
				{
					foreach (string rootAlias in item.RootAliases)
					{
						text.AppendLine(indent + "<strong>" + item.GetCallSyntax(rootAlias, isRootAlias: true) + "</strong>");
					}
				}
				if (item.Aliases != null)
				{
					foreach (string alias in item.Aliases)
					{
						text.AppendLine(indent + "<strong>" + item.GetCallSyntax(alias) + "</strong>");
					}
				}
				text.AppendLine(indent + "<strong>" + item.CallSyntax + "</strong> ");
			}
			else
			{
				if (item.RootAliases != null)
				{
					foreach (string rootAlias2 in item.RootAliases)
					{
						text.AppendLine(indent + $"<a href=\"chattype://{item.GetCallSyntaxUnformatted(rootAlias2, isRootAlias: true)}\">{item.GetCallSyntax(rootAlias2, isRootAlias: true).TrimEnd()}</a>");
					}
				}
				if (item.Aliases != null)
				{
					foreach (string alias2 in item.Aliases)
					{
						text.AppendLine(indent + $"<a href=\"chattype://{item.GetCallSyntaxUnformatted(alias2)}\">{item.GetCallSyntax(alias2).TrimEnd()}</a>");
					}
				}
				text.AppendLine(indent + $"<a href=\"chattype://{item.CallSyntaxUnformatted}\">{item.CallSyntax}</a>");
			}
			text.Append(item.GetFullSyntaxHandbook(caller, indent + "   "));
		}
	}

	public static void WriteCommandsList(StringBuilder text, Dictionary<string, IChatCommand> commands, Caller caller, bool isSubCommand = false)
	{
		foreach (KeyValuePair<string, IChatCommand> command in commands)
		{
			IChatCommand value = command.Value;
			if (caller != null && !value.IsAvailableTo(caller))
			{
				continue;
			}
			string text2 = value.Description;
			if (text2 == null)
			{
				text2 = " ";
			}
			else
			{
				int num = text2.IndexOf('\n');
				if (num >= 0)
				{
					text2 = text2.Substring(0, num);
				}
				text2 = Lang.Get(text2);
			}
			text.AppendLine("<code>" + value.GetCallSyntax(command.Key, !isSubCommand).TrimEnd() + "</code> :  " + text2);
		}
	}

	public string GetCallSyntax(string name, bool isRootAlias = false)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (isRootAlias)
		{
			stringBuilder.Append(_cmdapi.CommandPrefix);
		}
		else
		{
			stringBuilder.Append((_parent == null) ? _cmdapi.CommandPrefix : _parent.CallSyntax);
		}
		stringBuilder.Append(name);
		stringBuilder.Append(" ");
		AddParameterSyntax(stringBuilder, "");
		return stringBuilder.ToString();
	}

	public string GetCallSyntaxUnformatted(string name, bool isRootAlias = false)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (isRootAlias)
		{
			stringBuilder.Append(_cmdapi.CommandPrefix);
		}
		else
		{
			stringBuilder.Append((_parent == null) ? _cmdapi.CommandPrefix : _parent.CallSyntaxUnformatted);
		}
		stringBuilder.Append(name);
		stringBuilder.Append(" ");
		AddParameterSyntaxUnformatted(stringBuilder, "");
		return stringBuilder.ToString();
	}

	public void AddParameterSyntax(StringBuilder sb, string indent)
	{
		ICommandArgumentParser[] parsers = _parsers;
		for (int i = 0; i < parsers.Length; i++)
		{
			ArgumentParserBase argumentParserBase = (ArgumentParserBase)parsers[i];
			sb.Append(argumentParserBase.GetSyntax());
			sb.Append(" ");
		}
	}

	public void AddParameterSyntaxUnformatted(StringBuilder sb, string indent)
	{
		ICommandArgumentParser[] parsers = _parsers;
		for (int i = 0; i < parsers.Length; i++)
		{
			ArgumentParserBase argumentParserBase = (ArgumentParserBase)parsers[i];
			sb.Append(argumentParserBase.GetSyntaxUnformatted());
			sb.Append(" ");
		}
	}

	public void AddSyntaxExplanation(StringBuilder sb, string indent)
	{
		if (_parsers.Length == 0)
		{
			return;
		}
		bool flag = true;
		sb.Append("<font scale=\"80%\">");
		ICommandArgumentParser[] parsers = _parsers;
		for (int i = 0; i < parsers.Length; i++)
		{
			string syntaxWithExplanation = ((ArgumentParserBase)parsers[i]).GetSyntaxWithExplanation(indent);
			if (syntaxWithExplanation != null)
			{
				if (flag)
				{
					sb.AppendLine();
					flag = false;
				}
				sb.AppendLine(syntaxWithExplanation);
			}
		}
		sb.Append("</font>");
	}

	private void AddVerticalSpace(StringBuilder text)
	{
		if (text.Length != 0)
		{
			text.Append("\n");
		}
	}
}
