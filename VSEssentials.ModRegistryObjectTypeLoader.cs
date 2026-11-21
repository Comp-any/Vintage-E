using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.ServerMods.NoObf;

public class ModRegistryObjectTypeLoader : ModSystem
{
	public Dictionary<AssetLocation, StandardWorldProperty> worldProperties;

	public Dictionary<AssetLocation, VariantEntry[]> worldPropertiesVariants;

	private Dictionary<AssetLocation, RegistryObjectType> blockTypes;

	private Dictionary<AssetLocation, RegistryObjectType> itemTypes;

	private Dictionary<AssetLocation, RegistryObjectType> entityTypes;

	private List<RegistryObjectType>[] itemVariants;

	private List<RegistryObjectType>[] blockVariants;

	private List<RegistryObjectType>[] entityVariants;

	private ICoreServerAPI api;

	public override bool ShouldLoad(EnumAppSide side)
	{
		return side == EnumAppSide.Server;
	}

	public override double ExecuteOrder()
	{
		return 0.2;
	}

	public override void AssetsLoaded(ICoreAPI coreApi)
	{
		if (!(coreApi is ICoreServerAPI coreServerAPI))
		{
			return;
		}
		api = coreServerAPI;
		coreServerAPI.Logger.VerboseDebug("Starting to preload tags");
		PreloadTags();
		coreServerAPI.Logger.VerboseDebug("Starting to gather blocktypes, itemtypes and entities");
		LoadWorldProperties();
		int max = (coreServerAPI.Server.IsDedicated ? 3 : 8);
		int threadsCount = GameMath.Clamp(Environment.ProcessorCount / 2 - 2, 1, max);
		if (coreServerAPI.Server.ReducedServerThreads)
		{
			threadsCount = 1;
		}
		itemTypes = new Dictionary<AssetLocation, RegistryObjectType>();
		blockTypes = new Dictionary<AssetLocation, RegistryObjectType>();
		entityTypes = new Dictionary<AssetLocation, RegistryObjectType>();
		foreach (KeyValuePair<AssetLocation, JObject> item in coreServerAPI.Assets.GetMany<JObject>(coreServerAPI.Server.Logger, "itemtypes/"))
		{
			if (item.Key.Path.EndsWithOrdinal(".json"))
			{
				try
				{
					ItemType itemType = new ItemType();
					itemType.CreateBasetype(coreServerAPI, item.Key.Path, item.Key.Domain, item.Value);
					itemTypes.Add(item.Key, itemType);
				}
				catch (Exception e)
				{
					coreServerAPI.World.Logger.Error("Item type {0} could not be loaded. Will ignore. Exception thrown:", item.Key);
					coreServerAPI.World.Logger.Error(e);
				}
			}
		}
		itemVariants = new List<RegistryObjectType>[itemTypes.Count];
		coreServerAPI.Logger.VerboseDebug("Starting parsing ItemTypes in " + threadsCount + " threads");
		PrepareForLoading(threadsCount);
		foreach (KeyValuePair<AssetLocation, JObject> item2 in coreServerAPI.Assets.GetMany<JObject>(coreServerAPI.Server.Logger, "entities/"))
		{
			if (item2.Key.Path.EndsWithOrdinal(".json"))
			{
				try
				{
					EntityType entityType = new EntityType();
					entityType.CreateBasetype(coreServerAPI, item2.Key.Path, item2.Key.Domain, item2.Value);
					entityTypes.Add(item2.Key, entityType);
				}
				catch (Exception e2)
				{
					coreServerAPI.World.Logger.Error("Entity type {0} could not be loaded. Will ignore. Exception thrown:", item2.Key);
					coreServerAPI.World.Logger.Error(e2);
				}
			}
		}
		entityVariants = new List<RegistryObjectType>[entityTypes.Count];
		foreach (KeyValuePair<AssetLocation, JObject> item3 in coreServerAPI.Assets.GetMany<JObject>(coreServerAPI.Server.Logger, "blocktypes/"))
		{
			if (item3.Key.Path.EndsWithOrdinal(".json"))
			{
				try
				{
					BlockType blockType = new BlockType();
					blockType.CreateBasetype(coreServerAPI, item3.Key.Path, item3.Key.Domain, item3.Value);
					blockTypes.Add(item3.Key, blockType);
				}
				catch (Exception e3)
				{
					coreServerAPI.World.Logger.Error("Block type {0} could not be loaded. Will ignore. Exception thrown:", item3.Key);
					coreServerAPI.World.Logger.Error(e3);
				}
			}
		}
		blockVariants = new List<RegistryObjectType>[blockTypes.Count];
		TyronThreadPool.QueueTask((Func<Task>)GatherAllTypes_Async, "gatheralltypes");
		coreServerAPI.Logger.StoryEvent(Lang.Get("It remembers..."));
		coreServerAPI.Logger.VerboseDebug("Gathered all types, starting to load items");
		LoadItems(itemVariants);
		coreServerAPI.Logger.VerboseDebug("Parsed and loaded items");
		coreServerAPI.Logger.StoryEvent(Lang.Get("...all that came before"));
		LoadBlocks(blockVariants);
		coreServerAPI.Logger.VerboseDebug("Parsed and loaded blocks");
		LoadEntities(entityVariants);
		coreServerAPI.Logger.VerboseDebug("Parsed and loaded entities");
		coreServerAPI.Server.LogNotification("BlockLoader: Entities, Blocks and Items loaded");
		FreeRam();
		coreServerAPI.TriggerOnAssetsFirstLoaded();
	}

	private void LoadWorldProperties()
	{
		worldProperties = new Dictionary<AssetLocation, StandardWorldProperty>();
		foreach (KeyValuePair<AssetLocation, StandardWorldProperty> item in api.Assets.GetMany<StandardWorldProperty>(api.Server.Logger, "worldproperties/"))
		{
			AssetLocation assetLocation = item.Key.Clone();
			assetLocation.Path = assetLocation.Path.Replace("worldproperties/", "");
			assetLocation.RemoveEnding();
			item.Value.Code.Domain = item.Key.Domain;
			worldProperties.Add(assetLocation, item.Value);
		}
		worldPropertiesVariants = new Dictionary<AssetLocation, VariantEntry[]>();
		foreach (KeyValuePair<AssetLocation, StandardWorldProperty> worldProperty in worldProperties)
		{
			if (worldProperty.Value == null)
			{
				continue;
			}
			WorldPropertyVariant[] variants = worldProperty.Value.Variants;
			if (variants == null)
			{
				continue;
			}
			if (worldProperty.Value.Code == null)
			{
				api.Server.LogError("Error in worldproperties {0}, code is null, so I won't load it", worldProperty.Key);
				continue;
			}
			worldPropertiesVariants[worldProperty.Value.Code] = new VariantEntry[variants.Length];
			for (int i = 0; i < variants.Length; i++)
			{
				if (variants[i].Code == null)
				{
					api.Server.LogError("Error in worldproperties {0}, variant {1}, code is null, so I won't load it", worldProperty.Key, i);
					worldPropertiesVariants[worldProperty.Value.Code] = worldPropertiesVariants[worldProperty.Value.Code].RemoveAt(i);
				}
				else
				{
					worldPropertiesVariants[worldProperty.Value.Code][i] = new VariantEntry
					{
						Code = variants[i].Code.Path
					};
				}
			}
		}
	}

	private void LoadEntities(List<RegistryObjectType>[] variantLists)
	{
		LoadFromVariants(variantLists, "entities", (List<RegistryObjectType> variants) =>
		{
			foreach (EntityType variant in variants)
			{
				api.RegisterEntityClass(variant.Class, variant.CreateProperties(api));
			}
		});
	}

	private void LoadItems(List<RegistryObjectType>[] variantLists)
	{
		LoadFromVariants(variantLists, "item", (List<RegistryObjectType> variants) =>
		{
			foreach (ItemType variant in variants)
			{
				Item item = variant.CreateItem(api);
				try
				{
					api.RegisterItem(item);
				}
				catch (Exception e)
				{
					api.Server.Logger.Error("Failed registering item {0}:", item.Code);
					api.Server.Logger.Error(e);
				}
			}
		});
	}

	private void LoadBlocks(List<RegistryObjectType>[] variantLists)
	{
		LoadFromVariants(variantLists, "block", (List<RegistryObjectType> variants) =>
		{
			foreach (BlockType variant in variants)
			{
				Block block = variant.CreateBlock(api);
				try
				{
					api.RegisterBlock(block);
				}
				catch (Exception e)
				{
					api.Server.Logger.Error("Failed registering block {0}", block.Code);
					api.Server.Logger.Error(e);
				}
			}
		});
	}

	private void PreloadTags()
	{
		foreach (var (assetLocation2, dictionary2) in api.Assets.GetMany<Dictionary<string, string[]>>(api.Logger, "config/preloaded-tags"))
		{
			foreach (var (text2, array2) in dictionary2)
			{
				if (!(text2 == "entities"))
				{
					if (!(text2 == "collectibles"))
					{
						api.Logger.Notification($"Unknown registry '{text2}' to register preloaded tags for (domain: '{assetLocation2.Domain}').");
						continue;
					}
					TagRegistryError tagRegistryError = api.CollectibleTagRegistry.TryRegister(array2.AsSpan());
					if (tagRegistryError != TagRegistryError.None)
					{
						api.CollectibleTagRegistry.LogIssue("CollectibleTag Preregister", tagRegistryError, array2.AsSpan());
						continue;
					}
				}
				else
				{
					TagRegistryError tagRegistryError2 = api.EntityTagRegistry.TryRegister(array2.AsSpan());
					if (tagRegistryError2 != TagRegistryError.None)
					{
						api.EntityTagRegistry.LogIssue("EntityTag Preregister", tagRegistryError2, array2.AsSpan());
						continue;
					}
				}
				if (array2.Length != 0)
				{
					api.Logger.VerboseDebug($"Preloaded {array2.Length} tags for registry '{text2}' from domain '{assetLocation2.Domain}': {string.Join(", ", array2)}");
				}
				else
				{
					api.Logger.Notification($"Preloaded {array2.Length} tags for registry '{text2}' from domain '{assetLocation2.Domain}'");
				}
			}
		}
	}

	private void PrepareForLoading(int threadsCount)
	{
		for (int i = 0; i < threadsCount; i++)
		{
			TyronThreadPool.QueueTask((Func<Task>)GatherAllTypes_Async, "gatheralltypes" + i);
		}
	}

	private async Task GatherAllTypes_Async()
	{
		GatherTypes_Async(itemVariants, itemTypes);
		int timeOut = 1000;
		bool logged = false;
		while (blockVariants == null)
		{
			int num = timeOut - 1;
			timeOut = num;
			if (num == 0)
			{
				return;
			}
			if (!logged)
			{
				api.Logger.VerboseDebug("Waiting for BlockTypes to be gathered");
				logged = true;
			}
			await Task.Delay(10);
		}
		if (logged)
		{
			api.Logger.VerboseDebug("BlockTypes now all gathered");
		}
		GatherTypes_Async(blockVariants, blockTypes);
		timeOut = 1000;
		logged = false;
		while (entityVariants == null)
		{
			int num = timeOut - 1;
			timeOut = num;
			if (num == 0)
			{
				return;
			}
			if (!logged)
			{
				api.Logger.VerboseDebug("Waiting for entityTypes to be gathered");
				logged = true;
			}
			await Task.Delay(10);
		}
		if (logged)
		{
			api.Logger.VerboseDebug("EntityTypes now all gathered");
		}
		GatherTypes_Async(entityVariants, entityTypes);
	}

	private void GatherTypes_Async(List<RegistryObjectType>[] resolvedTypeLists, Dictionary<AssetLocation, RegistryObjectType> baseTypes)
	{
		int num = 0;
		foreach (RegistryObjectType value in baseTypes.Values)
		{
			if (AsyncHelper.CanProceedOnThisThread(ref value.parseStarted))
			{
				List<RegistryObjectType> list = new List<RegistryObjectType>();
				try
				{
					if (value.Enabled)
					{
						GatherVariantsAndPopulate(value, list);
					}
				}
				finally
				{
					resolvedTypeLists[num] = list;
				}
			}
			num++;
		}
	}

	private void GatherVariantsAndPopulate(RegistryObjectType baseType, List<RegistryObjectType> typesResolved)
	{
		List<ResolvedVariant> list = null;
		if (baseType.VariantGroups != null && baseType.VariantGroups.Length != 0)
		{
			try
			{
				list = GatherVariants(baseType.Code, baseType.VariantGroups, baseType.Code, baseType.AllowedVariants, baseType.SkipVariants);
			}
			catch (Exception e)
			{
				api.Server.Logger.Error("Exception thrown while trying to gather all variants of the block/item/entity type with code {0}. May lead to the whole type being ignored. Exception:", baseType.Code);
				api.Server.Logger.Error(e);
				return;
			}
		}
		JsonSerializer deserializer = JsonUtil.CreateSerializerForDomain(baseType.Code.Domain);
		if (list == null || list.Count == 0)
		{
			RegistryObjectType item = baseType.CreateAndPopulate(api, baseType.Code.Clone(), baseType.jsonObject, deserializer, new Vintagestory.API.Datastructures.OrderedDictionary<string, string>());
			typesResolved.Add(item);
		}
		else
		{
			int num = 1;
			foreach (ResolvedVariant item3 in list)
			{
				JObject jobject = ((num++ == list.Count) ? baseType.jsonObject : (baseType.jsonObject.DeepClone() as JObject));
				RegistryObjectType item2 = baseType.CreateAndPopulate(api, item3.Code, jobject, deserializer, item3.CodeParts);
				typesResolved.Add(item2);
			}
		}
		baseType.jsonObject = null;
	}

	private void LoadFromVariants(List<RegistryObjectType>[] variantLists, string typeForLog, Action<List<RegistryObjectType>> register)
	{
		int num = 0;
		for (int i = 0; i < variantLists.Length; i++)
		{
			List<RegistryObjectType> list;
			for (list = variantLists[i]; list == null; list = variantLists[i])
			{
				Thread.Sleep(10);
			}
			num += list.Count;
			register(list);
		}
		api.Server.LogNotification("Loaded " + num + " unique " + typeForLog + "s");
	}

	public StandardWorldProperty GetWorldPropertyByCode(AssetLocation code)
	{
		worldProperties.TryGetValue(code, out var value);
		return value;
	}

	private List<ResolvedVariant> GatherVariants(AssetLocation baseCode, RegistryObjectVariantGroup[] variantgroups, AssetLocation location, AssetLocation[] allowedVariants, AssetLocation[] skipVariants)
	{
		List<ResolvedVariant> list = new List<ResolvedVariant>();
		Vintagestory.API.Datastructures.OrderedDictionary<string, VariantEntry[]> orderedDictionary = new Vintagestory.API.Datastructures.OrderedDictionary<string, VariantEntry[]>();
		for (int i = 0; i < variantgroups.Length; i++)
		{
			if (variantgroups[i].LoadFromProperties != null)
			{
				CollectFromWorldProperties(variantgroups[i], variantgroups, orderedDictionary, list, location);
			}
			if (variantgroups[i].LoadFromPropertiesCombine != null)
			{
				CollectFromWorldPropertiesCombine(variantgroups[i].LoadFromPropertiesCombine, variantgroups[i], variantgroups, orderedDictionary, list, location);
			}
			if (variantgroups[i].States != null)
			{
				CollectFromStateList(variantgroups[i], variantgroups, orderedDictionary, list, location);
			}
		}
		VariantEntry[,] array = MultiplyProperties(orderedDictionary.Values.ToArray());
		for (int j = 0; j < array.GetLength(0); j++)
		{
			ResolvedVariant resolvedVariant = new ResolvedVariant();
			for (int k = 0; k < array.GetLength(1); k++)
			{
				VariantEntry variantEntry = array[j, k];
				if (variantEntry.Codes != null)
				{
					for (int l = 0; l < variantEntry.Codes.Count; l++)
					{
						resolvedVariant.AddCodePart(variantEntry.Types[l], variantEntry.Codes[l]);
					}
				}
				else
				{
					resolvedVariant.AddCodePart(orderedDictionary.GetKeyAtIndex(k), variantEntry.Code);
				}
			}
			list.Add(resolvedVariant);
		}
		foreach (ResolvedVariant item in list)
		{
			item.ResolveCode(baseCode);
		}
		if (skipVariants != null)
		{
			List<ResolvedVariant> list2 = new List<ResolvedVariant>();
			HashSet<AssetLocation> hashSet = new HashSet<AssetLocation>();
			List<AssetLocation> list3 = new List<AssetLocation>();
			AssetLocation[] array2 = skipVariants;
			foreach (AssetLocation assetLocation in array2)
			{
				if (assetLocation.IsWildCard)
				{
					list3.Add(assetLocation);
				}
				else
				{
					hashSet.Add(assetLocation);
				}
			}
			foreach (ResolvedVariant var in list)
			{
				if (!hashSet.Contains(var.Code) && !(list3.FirstOrDefault((AssetLocation v) => WildcardUtil.Match(v, var.Code)) != null))
				{
					list2.Add(var);
				}
			}
			list = list2;
		}
		if (allowedVariants != null)
		{
			List<ResolvedVariant> list4 = new List<ResolvedVariant>();
			HashSet<AssetLocation> hashSet2 = new HashSet<AssetLocation>();
			List<AssetLocation> list5 = new List<AssetLocation>();
			AssetLocation[] array2 = allowedVariants;
			foreach (AssetLocation assetLocation2 in array2)
			{
				if (assetLocation2.IsWildCard)
				{
					list5.Add(assetLocation2);
				}
				else
				{
					hashSet2.Add(assetLocation2);
				}
			}
			foreach (ResolvedVariant var2 in list)
			{
				if (hashSet2.Contains(var2.Code) || list5.FirstOrDefault((AssetLocation v) => WildcardUtil.Match(v, var2.Code)) != null)
				{
					list4.Add(var2);
				}
			}
			list = list4;
		}
		return list;
	}

	private void CollectFromStateList(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, Vintagestory.API.Datastructures.OrderedDictionary<string, VariantEntry[]> variantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation filename)
	{
		if (variantGroup.Code == null)
		{
			api.Server.LogError("Error in itemtype {0}, a variantgroup using a state list must have a code. Ignoring.", filename);
			return;
		}
		string[] states = variantGroup.States;
		string code = variantGroup.Code;
		if (variantGroup.Combine == EnumCombination.Add)
		{
			for (int i = 0; i < states.Length; i++)
			{
				ResolvedVariant resolvedVariant = new ResolvedVariant();
				resolvedVariant.AddCodePart(code, states[i]);
				blockvariantsFinal.Add(resolvedVariant);
			}
		}
		if (variantGroup.Combine != EnumCombination.Multiply)
		{
			return;
		}
		List<VariantEntry> list = new List<VariantEntry>();
		for (int j = 0; j < states.Length; j++)
		{
			list.Add(new VariantEntry
			{
				Code = states[j]
			});
		}
		foreach (RegistryObjectVariantGroup registryObjectVariantGroup in variantgroups)
		{
			if (registryObjectVariantGroup.Combine != EnumCombination.SelectiveMultiply || !(registryObjectVariantGroup.OnVariant == variantGroup.Code))
			{
				continue;
			}
			for (int l = 0; l < list.Count; l++)
			{
				VariantEntry variantEntry = list[l];
				if (!(registryObjectVariantGroup.Code != variantEntry.Code))
				{
					list.RemoveAt(l);
					for (int m = 0; m < registryObjectVariantGroup.States.Length; m++)
					{
						List<string> list2 = variantEntry.Codes ?? new List<string> { variantEntry.Code };
						List<string> list3 = variantEntry.Types ?? new List<string> { variantGroup.Code };
						string text = registryObjectVariantGroup.States[m];
						list2.Add(text);
						list3.Add(registryObjectVariantGroup.Code);
						list.Insert(l, new VariantEntry
						{
							Code = ((text.Length == 0) ? variantEntry.Code : (variantEntry.Code + "-" + text)),
							Codes = list2,
							Types = list3
						});
					}
				}
			}
		}
		if (variantsMul.ContainsKey(code))
		{
			list.AddRange(variantsMul[code]);
			variantsMul[code] = list.ToArray();
		}
		else
		{
			variantsMul.Add(code, list.ToArray());
		}
	}

	private void CollectFromWorldProperties(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, Vintagestory.API.Datastructures.OrderedDictionary<string, VariantEntry[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
	{
		CollectFromWorldPropertiesCombine(new AssetLocation[1] { variantGroup.LoadFromProperties }, variantGroup, variantgroups, blockvariantsMul, blockvariantsFinal, location);
	}

	private void CollectFromWorldPropertiesCombine(AssetLocation[] propList, RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, Vintagestory.API.Datastructures.OrderedDictionary<string, VariantEntry[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
	{
		if (propList.Length > 1 && variantGroup.Code == null)
		{
			api.Server.LogError("Error in item or block {0}, defined a variantgroup with loadFromPropertiesCombine (first element: {1}), but did not explicitly declare a code for this variant group, hence I do not know which code to use. Ignoring.", location, propList[0]);
			return;
		}
		foreach (AssetLocation code in propList)
		{
			StandardWorldProperty worldPropertyByCode = GetWorldPropertyByCode(code);
			if (worldPropertyByCode == null)
			{
				api.Server.LogError("Error in item or block {0}, worldproperty {1} does not exist (or is empty). Ignoring.", location, variantGroup.LoadFromProperties);
				break;
			}
			string key = ((variantGroup.Code == null) ? worldPropertyByCode.Code.Path : variantGroup.Code);
			if (variantGroup.Combine == EnumCombination.Add)
			{
				WorldPropertyVariant[] variants = worldPropertyByCode.Variants;
				foreach (WorldPropertyVariant worldPropertyVariant in variants)
				{
					ResolvedVariant resolvedVariant = new ResolvedVariant();
					resolvedVariant.AddCodePart(key, worldPropertyVariant.Code.Path);
					blockvariantsFinal.Add(resolvedVariant);
				}
			}
			if (variantGroup.Combine == EnumCombination.Multiply)
			{
				if (blockvariantsMul.TryGetValue(key, out var value))
				{
					blockvariantsMul[key] = value.Append(worldPropertiesVariants[worldPropertyByCode.Code]);
				}
				else
				{
					blockvariantsMul.Add(key, worldPropertiesVariants[worldPropertyByCode.Code]);
				}
			}
		}
	}

	private VariantEntry[,] MultiplyProperties(VariantEntry[][] variants)
	{
		int num = 1;
		for (int i = 0; i < variants.Length; i++)
		{
			num *= variants[i].Length;
		}
		VariantEntry[,] array = new VariantEntry[num, variants.Length];
		for (int j = 0; j < num; j++)
		{
			int num2 = j;
			for (int k = 0; k < variants.Length; k++)
			{
				int num3 = variants[k].Length;
				VariantEntry variantEntry = variants[k][num2 % num3];
				array[j, k] = new VariantEntry
				{
					Code = variantEntry.Code,
					Codes = variantEntry.Codes,
					Types = variantEntry.Types
				};
				num2 /= num3;
			}
		}
		return array;
	}

	private void FreeRam()
	{
		blockTypes = null;
		blockVariants = null;
		itemTypes = null;
		itemVariants = null;
		entityTypes = null;
		entityVariants = null;
		worldProperties = null;
		worldPropertiesVariants = null;
	}
}
