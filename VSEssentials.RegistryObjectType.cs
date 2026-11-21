using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.ServerMods.NoObf;

[DocumentAsJson]
[JsonObject(MemberSerialization.OptIn)]
public abstract class RegistryObjectType
{
	internal volatile int parseStarted;

	[DocumentAsJson("Optional", "True", false)]
	public bool Enabled = true;

	public JObject jsonObject;

	[DocumentAsJson("Required", "", false)]
	public AssetLocation Code;

	[DocumentAsJson("Optional", "None", false)]
	public RegistryObjectVariantGroup[] VariantGroups;

	public Vintagestory.API.Datastructures.OrderedDictionary<string, string> Variant = new Vintagestory.API.Datastructures.OrderedDictionary<string, string>();

	[JsonProperty]
	[DocumentAsJson("Unused", "None", false)]
	public WorldInteraction[] Interactions;

	[JsonProperty]
	[DocumentAsJson("Optional", "None", false)]
	public AssetLocation[] SkipVariants;

	[JsonProperty]
	[DocumentAsJson("Optional", "None", false)]
	public AssetLocation[] AllowedVariants;

	public HashSet<AssetLocation> AllowedVariantsQuickLookup = new HashSet<AssetLocation>();

	[JsonProperty]
	[DocumentAsJson("Optional", "None", false)]
	public string Class;

	public bool WildCardMatch(AssetLocation[] wildcards)
	{
		foreach (AssetLocation wildCard in wildcards)
		{
			if (WildCardMatch(wildCard))
			{
				return true;
			}
		}
		return false;
	}

	public bool WildCardMatch(AssetLocation wildCard)
	{
		if (wildCard == Code)
		{
			return true;
		}
		if (Code == null || wildCard.Domain != Code.Domain)
		{
			return false;
		}
		string text = Regex.Escape(wildCard.Path).Replace("\\*", "(.*)");
		return Regex.IsMatch(Code.Path, "^" + text + "$", RegexOptions.IgnoreCase);
	}

	public static bool WildCardMatches(string blockCode, List<string> wildCards, out string matchingWildcard)
	{
		foreach (string wildCard in wildCards)
		{
			if (WildcardUtil.Match(wildCard, blockCode))
			{
				matchingWildcard = wildCard;
				return true;
			}
		}
		matchingWildcard = null;
		return false;
	}

	public static bool WildCardMatch(AssetLocation wildCard, AssetLocation blockCode)
	{
		if (wildCard == blockCode)
		{
			return true;
		}
		string text = Regex.Escape(wildCard.Path).Replace("\\*", "(.*)");
		return Regex.IsMatch(blockCode.Path, "^" + text + "$", RegexOptions.IgnoreCase);
	}

	public static bool WildCardMatches(AssetLocation blockCode, List<AssetLocation> wildCards, out AssetLocation matchingWildcard)
	{
		foreach (AssetLocation wildCard in wildCards)
		{
			if (WildCardMatch(wildCard, blockCode))
			{
				matchingWildcard = wildCard;
				return true;
			}
		}
		matchingWildcard = null;
		return false;
	}

	internal virtual void CreateBasetype(ICoreAPI api, string filepathForLogging, string entryDomain, JObject entityTypeObject)
	{
		loadInherits(api, ref entityTypeObject, entryDomain, filepathForLogging);
		AssetLocation code;
		try
		{
			code = entityTypeObject.GetValue("code", StringComparison.InvariantCultureIgnoreCase).ToObject<AssetLocation>(entryDomain);
		}
		catch (Exception innerException)
		{
			throw new Exception("Asset has no valid code property. Will ignore. Exception thrown:-", innerException);
		}
		Code = code;
		if (entityTypeObject.TryGetValue("variantgroups", StringComparison.InvariantCultureIgnoreCase, out JToken value))
		{
			VariantGroups = value.ToObject<RegistryObjectVariantGroup[]>();
			entityTypeObject.Remove(value.Path);
		}
		if (entityTypeObject.TryGetValue("skipVariants", StringComparison.InvariantCultureIgnoreCase, out value))
		{
			SkipVariants = value.ToObject<AssetLocation[]>(entryDomain);
			entityTypeObject.Remove(value.Path);
		}
		if (entityTypeObject.TryGetValue("allowedVariants", StringComparison.InvariantCultureIgnoreCase, out value))
		{
			AllowedVariants = value.ToObject<AssetLocation[]>(entryDomain);
			entityTypeObject.Remove(value.Path);
		}
		if (entityTypeObject.TryGetValue("enabled", StringComparison.InvariantCultureIgnoreCase, out value))
		{
			Enabled = value.ToObject<bool>();
			entityTypeObject.Remove(value.Path);
		}
		else
		{
			Enabled = true;
		}
		jsonObject = entityTypeObject;
	}

	private void loadInherits(ICoreAPI api, ref JObject entityTypeObject, string entryDomain, string parentFileNameForLogging)
	{
		if (!entityTypeObject.TryGetValue("inheritFrom", StringComparison.InvariantCultureIgnoreCase, out JToken value))
		{
			return;
		}
		AssetLocation assetLocation = value.ToObject<AssetLocation>(entryDomain).WithPathAppendixOnce(".json");
		IAsset asset = api.Assets.TryGet(assetLocation);
		if (asset != null)
		{
			try
			{
				JObject entityTypeObject2 = JObject.Parse(asset.ToText());
				loadInherits(api, ref entityTypeObject2, entryDomain, assetLocation.ToShortString());
				entityTypeObject2.Merge(entityTypeObject, new JsonMergeSettings
				{
					MergeArrayHandling = MergeArrayHandling.Replace,
					PropertyNameComparison = StringComparison.InvariantCultureIgnoreCase
				});
				entityTypeObject = entityTypeObject2;
				entityTypeObject.Remove("inheritFrom");
				return;
			}
			catch (Exception ex)
			{
				api.Logger.Error(Lang.Get("File {0} wants to inherit from {1}, but this is not valid json. Exception: {2}.", parentFileNameForLogging, assetLocation, ex));
				return;
			}
		}
		api.Logger.Error(Lang.Get("File {0} wants to inherit from {1}, but this file does not exist. Will ignore.", parentFileNameForLogging, assetLocation));
	}

	internal virtual RegistryObjectType CreateAndPopulate(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, Vintagestory.API.Datastructures.OrderedDictionary<string, string> variant)
	{
		return this;
	}

	protected T CreateResolvedType<T>(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, Vintagestory.API.Datastructures.OrderedDictionary<string, string> variant) where T : RegistryObjectType, new()
	{
		T val = new T
		{
			Code = Code,
			VariantGroups = VariantGroups,
			Enabled = Enabled,
			jsonObject = jobject,
			Variant = variant
		};
		try
		{
			solveByType(jobject, fullcode.Path, variant);
		}
		catch (Exception e)
		{
			api.Server.Logger.Error("Exception thrown while trying to resolve *byType properties of type {0}, variant {1}. Will ignore most of the attributes. Exception thrown:", Code, fullcode);
			api.Server.Logger.Error(e);
		}
		try
		{
			JsonUtil.PopulateObject(val, jobject, deserializer);
		}
		catch (Exception e2)
		{
			api.Server.Logger.Error("Exception thrown while trying to parse json data of the type with code {0}, variant {1}. Will ignore most of the attributes. Exception:", Code, fullcode);
			api.Server.Logger.Error(e2);
		}
		val.Code = fullcode;
		val.jsonObject = null;
		return val;
	}

	protected static void solveByType(JToken json, string codePath, Vintagestory.API.Datastructures.OrderedDictionary<string, string> searchReplace)
	{
		if (json is JObject jObject)
		{
			List<string> list = null;
			Dictionary<string, JToken> dictionary = null;
			foreach (KeyValuePair<string, JToken> item in jObject)
			{
				if (!item.Key.EndsWith("byType", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string key = item.Key.Substring(0, item.Key.Length - "byType".Length);
				foreach (KeyValuePair<string, JToken> item2 in (item.Value as JObject) ?? throw new FormatException("Invalid value at key: " + item.Key))
				{
					if (WildcardUtil.Match(item2.Key, codePath))
					{
						JToken value = item2.Value;
						if (dictionary == null)
						{
							dictionary = new Dictionary<string, JToken>();
						}
						dictionary.Add(key, value);
						break;
					}
				}
				if (list == null)
				{
					list = new List<string>();
				}
				list.Add(item.Key);
			}
			if (list != null)
			{
				foreach (string item3 in list)
				{
					jObject.Remove(item3);
				}
				if (dictionary != null)
				{
					foreach (KeyValuePair<string, JToken> item4 in dictionary)
					{
						if (jObject[item4.Key] is JObject jObject2)
						{
							jObject2.Merge(item4.Value);
						}
						else
						{
							jObject[item4.Key] = item4.Value;
						}
					}
				}
			}
			{
				foreach (KeyValuePair<string, JToken> item5 in jObject)
				{
					solveByType(item5.Value, codePath, searchReplace);
				}
				return;
			}
		}
		if (json.Type == JTokenType.String)
		{
			string text = (string)(json as JValue).Value;
			if (text.Contains("{"))
			{
				(json as JValue).Value = RegistryObject.FillPlaceHolder(text, searchReplace);
			}
		}
		else
		{
			if (!(json is JArray jArray))
			{
				return;
			}
			foreach (JToken item6 in jArray)
			{
				solveByType(item6, codePath, searchReplace);
			}
		}
	}
}
