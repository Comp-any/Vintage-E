using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class BlockClutter : BlockShapeFromAttributes, ISearchTextProvider
{
	public Dictionary<string, ClutterTypeProps> clutterByCode = new Dictionary<string, ClutterTypeProps>();

	private string basePath;

	public override string ClassType => "clutter";

	public override IEnumerable<IShapeTypeProps> AllTypes => clutterByCode.Values;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		api.Event.RegisterEventBusListener(onExpClang, 0.5, "expclang");
	}

	private void onExpClang(string eventName, ref EnumHandling handling, IAttribute data)
	{
		ITreeAttribute treeAttribute = data as ITreeAttribute;
		foreach (KeyValuePair<string, ClutterTypeProps> item in clutterByCode)
		{
			string text = ((Code.Domain == "game") ? "" : (Code.Domain + ":")) + ClassType + "-" + item.Key?.Replace("/", "-");
			if (!Lang.HasTranslation(text))
			{
				treeAttribute[text] = new StringAttribute("\t\"" + text + "\": \"" + Lang.GetNamePlaceHolder(new AssetLocation(item.Key)) + "\",");
			}
		}
	}

	public override void LoadTypes()
	{
		ClutterTypeProps[]? array = Attributes["types"].AsObject<ClutterTypeProps[]>();
		basePath = "shapes/" + Attributes["shapeBasePath"].AsString() + "/";
		List<JsonItemStack> list = new List<JsonItemStack>();
		ModelTransform defaults = ModelTransform.BlockDefaultGui();
		ModelTransform defaults2 = ModelTransform.BlockDefaultFp();
		ModelTransform defaults3 = ModelTransform.BlockDefaultTp();
		ModelTransform defaults4 = ModelTransform.BlockDefaultGround();
		ClutterTypeProps[] array2 = array;
		foreach (ClutterTypeProps clutterTypeProps in array2)
		{
			clutterByCode[clutterTypeProps.Code] = clutterTypeProps;
			if (clutterTypeProps.GuiTf != null)
			{
				clutterTypeProps.GuiTransform = new ModelTransform(clutterTypeProps.GuiTf, defaults);
			}
			if (clutterTypeProps.FpTf != null)
			{
				clutterTypeProps.FpTtransform = new ModelTransform(clutterTypeProps.FpTf, defaults2);
			}
			if (clutterTypeProps.TpTf != null)
			{
				clutterTypeProps.TpTransform = new ModelTransform(clutterTypeProps.TpTf, defaults3);
			}
			if (clutterTypeProps.GroundTf != null)
			{
				clutterTypeProps.GroundTransform = new ModelTransform(clutterTypeProps.GroundTf, defaults4);
			}
			if (clutterTypeProps.ShapePath == null)
			{
				clutterTypeProps.ShapePath = AssetLocation.Create(basePath + clutterTypeProps.Code + ".json", Code.Domain);
			}
			else if (clutterTypeProps.ShapePath.Path.StartsWith('/'))
			{
				clutterTypeProps.ShapePath.WithPathPrefixOnce("shapes").WithPathAppendixOnce(".json");
			}
			else
			{
				clutterTypeProps.ShapePath.WithPathPrefixOnce(basePath).WithPathAppendixOnce(".json");
			}
			if (clutterTypeProps.Drops != null)
			{
				BlockDropItemStack[] drops = clutterTypeProps.Drops;
				for (int j = 0; j < drops.Length; j++)
				{
					drops[j].Resolve(api.World, "clutter block drop", Code);
				}
			}
			JsonItemStack jsonItemStack = new JsonItemStack
			{
				Code = Code,
				Type = EnumItemClass.Block,
				Attributes = new JsonObject(JToken.Parse("{ \"type\": \"" + clutterTypeProps.Code + "\" }"))
			};
			jsonItemStack.Resolve(api.World, ClassType + " type");
			list.Add(jsonItemStack);
		}
		CreativeInventoryStacks = new CreativeTabAndStackList[1]
		{
			new CreativeTabAndStackList
			{
				Stacks = list.ToArray(),
				Tabs = new string[2] { "general", "clutter" }
			}
		};
	}

	public static string Remap(IWorldAccessor worldAccessForResolve, string type)
	{
		if (type.StartsWithFast("pipes/"))
		{
			return "pipe-veryrusted-" + type.Substring(6);
		}
		return type;
	}

	public override string GetHeldTpIdleAnimation(ItemSlot activeHotbarSlot, Entity forEntity, EnumHand hand)
	{
		string code = activeHotbarSlot.Itemstack.Attributes.GetString("type", "");
		return GetTypeProps(code, activeHotbarSlot.Itemstack, null)?.HeldIdleAnim ?? base.GetHeldTpIdleAnimation(activeHotbarSlot, forEntity, hand);
	}

	public override string GetHeldReadyAnimation(ItemSlot activeHotbarSlot, Entity forEntity, EnumHand hand)
	{
		string code = activeHotbarSlot.Itemstack.Attributes.GetString("type", "");
		return GetTypeProps(code, activeHotbarSlot.Itemstack, null)?.HeldReadyAnim ?? base.GetHeldReadyAnimation(activeHotbarSlot, forEntity, hand);
	}

	public override bool IsClimbable(BlockPos pos)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		if (bEBehavior != null && bEBehavior.Type != null && clutterByCode.TryGetValue(bEBehavior.Type, out var value))
		{
			return value.Climbable;
		}
		return Climbable;
	}

	public override IShapeTypeProps GetTypeProps(string code, ItemStack stack, BEBehaviorShapeFromAttributes be)
	{
		if (code == null)
		{
			return null;
		}
		clutterByCode.TryGetValue(code, out var value);
		return value;
	}

	public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
	{
		return new BlockDropItemStack[1]
		{
			new BlockDropItemStack(handbookStack)
		};
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		string text = baseInfo(inSlot, dsc, world, withDebugInfo);
		ICoreClientAPI obj = api as ICoreClientAPI;
		if (obj != null && obj.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			dsc.AppendLine(Lang.Get("Clutter type: {0}", text));
		}
	}

	private string baseInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		dsc.AppendLine(Lang.Get("Unusable clutter"));
		string text = inSlot.Itemstack.Attributes.GetString("type", "");
		if (text.StartsWithFast("banner-"))
		{
			string[] array = text.Split('-');
			dsc.AppendLine(Lang.Get("Pattern: {0}", Lang.Get("bannerpattern-" + array[1])));
			dsc.AppendLine(Lang.Get("Segment: {0}", Lang.Get("bannersegment-" + array[3])));
		}
		return text;
	}

	public string GetSearchText(IWorldAccessor world, ItemSlot inSlot)
	{
		StringBuilder stringBuilder = new StringBuilder();
		baseInfo(inSlot, stringBuilder, world, withDebugInfo: false);
		string text = inSlot.Itemstack.Attributes.GetString("type", "");
		stringBuilder.AppendLine(Lang.Get("Clutter type: {0}", text));
		return stringBuilder.ToString();
	}

	public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
	{
		BEBehaviorShapeFromAttributes bEBehavior = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
		return GetTypeProps(bEBehavior?.Type, null, bEBehavior)?.Drops?.Select((BlockDropItemStack drop) => drop.GetNextItemStack(dropQuantityMultiplier)).ToArray() ?? base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
	}
}
