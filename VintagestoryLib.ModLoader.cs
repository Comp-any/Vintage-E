using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProperVersion;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.ModDb;

namespace Vintagestory.Common;

public class ModLoader : IModLoader
{
	private readonly ICoreAPI api;

	private readonly EnumAppSide side;

	private readonly ILogger logger;

	private bool traceLog;

	private readonly ModCompilationContext compilationContext = new ModCompilationContext();

	private Dictionary<string, ModContainer> loadedMods = new Dictionary<string, ModContainer>();

	private List<ModSystem> enabledSystems = new List<ModSystem>();

	public List<string> MissingDependencies = new List<string>();

	internal Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin> contentAssetOrigins;

	internal Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin> themeAssetOrigins;

	public int TextureSize { get; set; } = 32;

	public IReadOnlyCollection<string> ModSearchPaths { get; }

	public string UnpackPath { get; } = Path.Combine(GamePaths.Cache, "unpack");

	public IEnumerable<Mod> Mods => loadedMods.Values.Where((ModContainer mod) => mod.Enabled);

	public IEnumerable<ModSystem> Systems => enabledSystems.Select((ModSystem x) => x);

	public Mod GetMod(string modID)
	{
		if (!loadedMods.TryGetValue(modID, out var value))
		{
			return null;
		}
		if (!value.Enabled)
		{
			return null;
		}
		return value;
	}

	public bool IsModEnabled(string modID)
	{
		return (GetMod(modID) as ModContainer)?.Enabled ?? false;
	}

	public ModSystem GetModSystem(string fullName)
	{
		return Systems.FirstOrDefault((ModSystem mod) => string.Equals(mod.GetType().FullName, fullName, StringComparison.InvariantCultureIgnoreCase));
	}

	public T GetModSystem<T>(bool withInheritance = true) where T : ModSystem
	{
		if (withInheritance)
		{
			return Systems.OfType<T>().FirstOrDefault();
		}
		return Systems.FirstOrDefault((ModSystem mod) => mod.GetType() == typeof(T)) as T;
	}

	public bool IsModSystemEnabled(string fullName)
	{
		return GetModSystem(fullName) != null;
	}

	public ModLoader(ILogger logger, EnumAppSide side, IEnumerable<string> modSearchPaths, bool traceLog)
		: this(null, side, logger, modSearchPaths, traceLog)
	{
	}

	public ModLoader(ICoreAPI api, IEnumerable<string> modSearchPaths, bool traceLog)
		: this(api, api.Side, api.World.Logger, modSearchPaths, traceLog)
	{
	}

	private ModLoader(ICoreAPI api, EnumAppSide side, ILogger logger, IEnumerable<string> modSearchPaths, bool traceLog)
	{
		this.api = api;
		this.side = side;
		this.logger = logger;
		this.traceLog = traceLog;
		ModSearchPaths = modSearchPaths.Select((string path) => Path.IsPathRooted(path) ? path : Path.Combine(GamePaths.Binaries, path)).ToList().AsReadOnly();
	}

	public Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin> GetContentArchives()
	{
		return contentAssetOrigins;
	}

	public Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin> GetThemeArchives()
	{
		return themeAssetOrigins;
	}

	public List<ModContainer> LoadModInfos()
	{
		List<ModContainer> list = CollectMods();
		using ModAssemblyLoader loader = new ModAssemblyLoader(ModSearchPaths, list);
		foreach (ModContainer item in list)
		{
			item.LoadModInfo(compilationContext, loader);
		}
		return list;
	}

	public List<ModContainer> LoadModInfosAndVerify(IEnumerable<string> disabledModsByIdAndVersion = null)
	{
		List<ModContainer> mods = LoadModInfos();
		return DisableAndVerify(mods, disabledModsByIdAndVersion);
	}

	public List<ModContainer> DisableAndVerify(List<ModContainer> mods, IEnumerable<string> disabledModsByIdAndVersion = null)
	{
		if (disabledModsByIdAndVersion != null && disabledModsByIdAndVersion.Count() > 0)
		{
			DisableMods(mods, disabledModsByIdAndVersion);
		}
		return verifyMods(mods);
	}

	public void LoadMods(IEnumerable<string> disabledModsByIdAndVersion = null)
	{
		List<ModContainer> mods = LoadModInfos();
		LoadMods(mods, disabledModsByIdAndVersion);
	}

	public void LoadMods(List<ModContainer> mods, IEnumerable<string> disabledModsByIdAndVersion = null)
	{
		Dictionary<string, string>? modBlockList = ModDbUtil.ModBlockList;
		if (modBlockList != null && modBlockList.Count > 0)
		{
			List<string> list = new List<string>();
			foreach (ModContainer mod in mods)
			{
				if (!mod.Error.HasValue && ModDbUtil.IsModBlocked(mod.Info.ModID, mod.Info.Version, out string reason))
				{
					list.Add($"{mod.Info.ModID}@{mod.Info.Version}: {reason}");
				}
			}
			if (list.Count > 0)
			{
				logger.Warning("The following mods where blocked from loading:");
				foreach (string item in list)
				{
					logger.Warning("  " + item);
				}
			}
			disabledModsByIdAndVersion = ((disabledModsByIdAndVersion == null) ? new List<string>() : new List<string>(disabledModsByIdAndVersion));
			((List<string>)disabledModsByIdAndVersion).AddRange(ModDbUtil.ModBlockList.Keys);
		}
		if (disabledModsByIdAndVersion != null && disabledModsByIdAndVersion.Count() > 0)
		{
			using (ModAssemblyLoader loader = new ModAssemblyLoader(ModSearchPaths, mods))
			{
				foreach (ModContainer mod2 in mods)
				{
					mod2.LoadModInfo(compilationContext, loader);
				}
			}
			int num = DisableMods(mods, disabledModsByIdAndVersion);
			logger.Notification("Found {0} mods ({1} disabled)", mods.Count, num);
		}
		else
		{
			logger.Notification("Found {0} mods (0 disabled)", mods.Count);
		}
		mods = verifyMods(mods);
		logger.Notification("Mods, sorted by dependency: {0}", string.Join(", ", mods.Select((ModContainer m) => m.Info.ModID)));
		foreach (ModContainer mod3 in mods)
		{
			if (mod3.Enabled)
			{
				mod3.Unpack(UnpackPath);
			}
		}
		ClearCacheFolder(mods);
		enabledSystems = instantiateMods(mods);
	}

	private List<ModContainer> verifyMods(List<ModContainer> mods)
	{
		CheckDuplicateModIDMods(mods);
		return CheckAndSortDependencies(mods);
	}

	private List<ModSystem> instantiateMods(List<ModContainer> mods)
	{
		List<ModSystem> list = new List<ModSystem>();
		mods = mods.OrderBy((ModContainer mod) => mod.RequiresCompilation).ToList();
		using (ModAssemblyLoader loader = new ModAssemblyLoader(ModSearchPaths, mods))
		{
			foreach (ModContainer mod in mods)
			{
				if (mod.Enabled)
				{
					mod.LoadAssembly(compilationContext, loader);
					if (mod.Status == ModStatus.Errored && mod.Error == ModError.ChangedVersion && api is ICoreServerAPI coreServerAPI && !coreServerAPI.Server.IsDedicated)
					{
						throw new RestartGameException(Lang.Get("modwarning-assemblyloaded", mod.Info.ModID));
					}
				}
			}
		}
		logger.VerboseDebug("{0} assemblies loaded", mods.Count);
		if (mods.Any((ModContainer mod) => mod.Error.HasValue && mod.RequiresCompilation))
		{
			logger.Warning("One or more source code mods failed to compile. Info to modders: In case you cannot find the problem, be aware that the game engine currently can only compile C# code until version 5.0. Any language features from C#6.0 or above will result in compile errors.");
		}
		foreach (ModContainer mod2 in mods)
		{
			if (mod2.Enabled)
			{
				logger.VerboseDebug("Instantiate mod systems for {0}", mod2.Info.ModID);
				mod2.InstantiateModSystems(side);
			}
		}
		contentAssetOrigins = new Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin>();
		themeAssetOrigins = new Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin>();
		Vintagestory.API.Datastructures.OrderedDictionary<string, int> orderedDictionary = new Vintagestory.API.Datastructures.OrderedDictionary<string, int>();
		foreach (ModContainer item in mods.Where((ModContainer mod) => mod.Enabled))
		{
			loadedMods.Add(item.Info.ModID, item);
			list.AddRange(item.Systems);
			if (item.FolderPath != null && Directory.Exists(Path.Combine(item.FolderPath, "assets")))
			{
				bool flag = item.Info.Type == EnumModType.Theme;
				Vintagestory.API.Datastructures.OrderedDictionary<string, IAssetOrigin> orderedDictionary2 = (flag ? themeAssetOrigins : contentAssetOrigins);
				FolderOrigin value = (flag ? new ThemeFolderOrigin(item.FolderPath, (api.Side == EnumAppSide.Client) ? "textures/" : null) : new FolderOrigin(item.FolderPath, (api.Side == EnumAppSide.Client) ? "textures/" : null));
				orderedDictionary2.Add(item.FileName, value);
				orderedDictionary.Add(item.FileName, item.Info.TextureSize);
			}
		}
		if (orderedDictionary.Count > 0)
		{
			TextureSize = orderedDictionary.Values.Last();
		}
		list = list.OrderBy((ModSystem system) => system.ExecuteOrder()).ToList();
		logger.Notification("Instantiated {0} mod systems from {1} enabled mods", list.Count, Mods.Count());
		return list;
	}

	private void ClearCacheFolder(IEnumerable<ModContainer> mods)
	{
		if (!Directory.Exists(UnpackPath))
		{
			return;
		}
		foreach (string item in Directory.GetDirectories(UnpackPath).Except(from mod in mods
			where !mod.Error.HasValue
			select mod.FolderPath, StringComparer.InvariantCultureIgnoreCase))
		{
			try
			{
				string[] files = Directory.GetFiles(item, "*.dll");
				for (int num = 0; num < files.Length; num++)
				{
					File.Delete(files[num]);
				}
			}
			catch
			{
				break;
			}
			try
			{
				Directory.Delete(item, recursive: true);
			}
			catch (Exception e)
			{
				logger.Error("There was an exception deleting the cached mod folder '{0}':");
				logger.Error(e);
			}
		}
	}

	private List<ModContainer> CollectMods()
	{
		List<DirectoryInfo> list = (from path in ModSearchPaths
			select new DirectoryInfo(path) into dirInfo
			group dirInfo by dirInfo.FullName.ToLowerInvariant() into @group
			select @group.First()).ToList();
		logger.Notification("Will search the following paths for mods:");
		foreach (DirectoryInfo item in list)
		{
			if (item.Exists)
			{
				logger.Notification("    {0}", GamePaths.RemoveUsername(item.FullName));
			}
			else
			{
				logger.Notification("    {0} (Not found?)", GamePaths.RemoveUsername(item.FullName));
			}
		}
		return (from fsInfo in list.Where((DirectoryInfo dirInfo) => dirInfo.Exists).SelectMany((DirectoryInfo dirInfo) => dirInfo.GetFileSystemInfos())
			where ModContainer.GetSourceType(fsInfo).HasValue
			select new ModContainer(fsInfo, logger, traceLog)).OrderBy((ModContainer mod) => mod.FileName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private int DisableMods(IEnumerable<ModContainer> mods, IEnumerable<string> disabledModsByIdAndVersion)
	{
		if (disabledModsByIdAndVersion == null)
		{
			return 0;
		}
		HashSet<string> disabledSet = new HashSet<string>(disabledModsByIdAndVersion);
		List<ModContainer> list = mods.Where((ModContainer mod) => mod?.Info == null || disabledSet.Contains(mod.Info.ModID + "@" + mod.Info.Version) || disabledSet.Contains(mod.Info.ModID ?? "")).ToList();
		foreach (ModContainer item in list)
		{
			item.Status = ModStatus.Disabled;
		}
		return Enumerable.Count(list);
	}

	private void CheckDuplicateModIDMods(IEnumerable<ModContainer> mods)
	{
		foreach (IGrouping<string, ModContainer> item in from mod in mods
			where mod.Info?.ModID != null && mod.Enabled
			group mod by mod.Info.ModID into @group
			where @group.Skip(1).Any()
			select @group)
		{
			IOrderedEnumerable<ModContainer> source = item.OrderBy((ModContainer mod) => mod.Info);
			logger.Warning("Multiple mods share the mod ID '{0}' ({1}). Will only load the highest version one - v{2}.", item.Key, string.Join(", ", item.Select((ModContainer m) => "'" + m.FileName + "'")), source.First().Info.Version);
			foreach (ModContainer item2 in source.Skip(1))
			{
				item2.SetError(ModError.Loading);
			}
		}
	}

	private List<ModContainer> CheckAndSortDependencies(IEnumerable<ModContainer> mods)
	{
		mods = mods.Where((ModContainer mod) => !mod.Error.HasValue && mod.Enabled).ToList();
		List<ModContainer> list = new List<ModContainer>();
		HashSet<ModContainer> hashSet = new HashSet<ModContainer>(mods);
		List<ModContainer> list2 = new List<ModContainer>();
		Dictionary<string, ModContainer> dictionary = mods.Where((ModContainer mod) => mod.Info?.ModID != null).ToDictionary((ModContainer mod) => mod.Info.ModID);
		do
		{
			list2.Clear();
			foreach (ModContainer item in hashSet)
			{
				bool flag = true;
				if (item.Info != null)
				{
					foreach (ModDependency dependency in item.Info.Dependencies)
					{
						if (!dictionary.TryGetValue(dependency.ModID, out var value) || !SatisfiesVersion(dependency.Version, value.Info.Version) || !value.Enabled)
						{
							item.SetError(ModError.Dependency);
						}
						else if (hashSet.Contains(value))
						{
							flag = false;
						}
					}
				}
				if (flag)
				{
					list2.Add(item);
				}
			}
			foreach (ModContainer item2 in list2)
			{
				hashSet.Remove(item2);
				list.Add(item2);
			}
		}
		while (list2.Count > 0);
		foreach (ModContainer mod in mods)
		{
			if (mod.Enabled || mod.Status == ModStatus.Disabled)
			{
				continue;
			}
			mod.Logger.Error("Could not resolve some dependencies:");
			foreach (ModDependency dependency2 in mod.Info.Dependencies)
			{
				if (!dictionary.TryGetValue(dependency2.ModID, out var value2))
				{
					mod.Logger.Error("    {0} - Missing", dependency2);
					MissingDependencies.Add(dependency2.ModID + "@" + dependency2.Version);
					if (mod.MissingDependencies == null)
					{
						mod.MissingDependencies = new List<string>();
					}
					mod.MissingDependencies.Add(dependency2.ModID + "@" + dependency2.Version);
				}
				else if (!SatisfiesVersion(dependency2.Version, value2.Info.Version))
				{
					mod.Logger.Error("    {0} - Version mismatch (has {1})", dependency2, value2.Info.Version);
					MissingDependencies.Add(dependency2.ModID + "@" + dependency2.Version);
					if (mod.MissingDependencies == null)
					{
						mod.MissingDependencies = new List<string>();
					}
					mod.MissingDependencies.Add(dependency2.ModID + "@" + dependency2.Version);
				}
				else if (value2.Error == ModError.Loading)
				{
					mod.Logger.Error("    {0} - Dependency {1} failed loading", dependency2, value2);
				}
				else if (value2.Error == ModError.Dependency)
				{
					mod.Logger.Error("    {0} - Dependency {1} has dependency errors itself", dependency2, value2);
				}
				else if (!value2.Enabled)
				{
					mod.Logger.Error("    {0} - Dependency {1} is not enabled", dependency2, value2);
				}
			}
		}
		if (hashSet.Count > 0)
		{
			logger.Warning("Possible cyclic dependencies between mods: " + string.Join(", ", hashSet));
			list.AddRange(hashSet);
		}
		return list;
	}

	private bool SatisfiesVersion(string requested, string provided)
	{
		if (string.IsNullOrEmpty(requested) || string.IsNullOrEmpty(provided) || requested == "*")
		{
			return true;
		}
		SemVer.TryParse(requested, out var result);
		SemVer.TryParse(provided, out var result2);
		return result2 >= result;
	}

	public void RunModPhase(ModRunPhase phase)
	{
		RunModPhase(ref enabledSystems, phase);
	}

	public void RunModPhase(ref List<ModSystem> enabledSystems, ModRunPhase phase)
	{
		if (phase != ModRunPhase.Normal)
		{
			foreach (ModSystem enabledSystem in enabledSystems)
			{
				if (enabledSystem != null && enabledSystem.ShouldLoad(api) && !TryRunModPhase(enabledSystem.Mod, enabledSystem, api, phase))
				{
					logger.Error("Failed to run mod phase {0} for mod {1}", phase, enabledSystem);
				}
			}
			return;
		}
		List<ModSystem> list = new List<ModSystem>();
		foreach (ModSystem enabledSystem2 in enabledSystems)
		{
			if (enabledSystem2.ShouldLoad(api))
			{
				logger.VerboseDebug("Starting system: " + enabledSystem2.GetType().Name);
				if (TryRunModPhase(enabledSystem2.Mod, enabledSystem2, api, ModRunPhase.Normal))
				{
					list.Add(enabledSystem2);
					continue;
				}
				logger.Error("Failed to start system {0}", enabledSystem2);
			}
		}
		logger.Notification("Started {0} systems on {1}:", list.Count, api.Side);
		foreach (IGrouping<Mod, ModSystem> item in from system in list
			group system by system.Mod)
		{
			logger.Notification("    Mod {0}:", item.Key);
			foreach (ModSystem item2 in item)
			{
				logger.Notification("        {0}", item2);
			}
		}
		enabledSystems = list;
	}

	private bool TryRunModPhase(Mod mod, ModSystem system, ICoreAPI api, ModRunPhase phase)
	{
		try
		{
			switch (phase)
			{
			case ModRunPhase.Pre:
				system.StartPre(api);
				break;
			case ModRunPhase.Start:
				system.Start(api);
				break;
			case ModRunPhase.AssetsLoaded:
				system.AssetsLoaded(api);
				break;
			case ModRunPhase.AssetsFinalize:
				system.AssetsFinalize(api);
				break;
			case ModRunPhase.Normal:
				if (api.Side == EnumAppSide.Client)
				{
					system.StartClientSide(api as ICoreClientAPI);
				}
				else
				{
					system.StartServerSide(api as ICoreServerAPI);
				}
				break;
			case ModRunPhase.Dispose:
				system.Dispose();
				break;
			}
			return true;
		}
		catch (FormatException ex)
		{
			throw ex;
		}
		catch (Exception e)
		{
			mod.Logger.Error("An exception was thrown when trying to start the mod:");
			mod.Logger.Error(e);
		}
		return false;
	}

	public void Dispose()
	{
		RunModPhase(ModRunPhase.Dispose);
	}
}
