using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.GameContent;

public class ItemProspectingPick : Item
{
	private ProPickWorkSpace ppws;

	private SkillItem[] toolModes;

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		ICoreClientAPI capi = api as ICoreClientAPI;
		toolModes = ObjectCacheUtil.GetOrCreate(api, "proPickToolModes", () =>
		{
			SkillItem[] array = ((api.World.Config.GetString("propickNodeSearchRadius").ToInt() > 0) ? new SkillItem[2]
			{
				new SkillItem
				{
					Code = new AssetLocation("density"),
					Name = Lang.Get("Density Search Mode (Long range, chance based search)")
				},
				new SkillItem
				{
					Code = new AssetLocation("node"),
					Name = Lang.Get("Node Search Mode (Short range, exact search)")
				}
			} : new SkillItem[1]
			{
				new SkillItem
				{
					Code = new AssetLocation("density"),
					Name = Lang.Get("Density Search Mode (Long range, chance based search)")
				}
			});
			if (capi != null)
			{
				array[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/heatmap.svg"), 48, 48, 5, -1));
				array[0].TexturePremultipliedAlpha = false;
				if (array.Length > 1)
				{
					array[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/rocks.svg"), 48, 48, 5, -1));
					array[1].TexturePremultipliedAlpha = false;
				}
			}
			return array;
		});
		if (api.Side == EnumAppSide.Server)
		{
			ppws = ObjectCacheUtil.GetOrCreate(api, "propickworkspace", () =>
			{
				ProPickWorkSpace proPickWorkSpace = new ProPickWorkSpace();
				proPickWorkSpace.OnLoaded(api);
				return proPickWorkSpace;
			});
		}
	}

	public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
	{
		float num = base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
		if (GetToolMode(itemslot, player, blockSel) == 1)
		{
			num = (num + remainingResistance) / 2f;
		}
		return num;
	}

	public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1f)
	{
		int toolMode = GetToolMode(itemslot, (byEntity as EntityPlayer).Player, blockSel);
		int num = api.World.Config.GetString("propickNodeSearchRadius").ToInt();
		int amount = 1;
		if (toolMode == 1 && num > 0)
		{
			ProbeBlockNodeMode(world, byEntity, itemslot, blockSel, num);
			amount = 2;
		}
		else
		{
			ProbeBlockDensityMode(world, byEntity, itemslot, blockSel);
		}
		EnumItemDamageSource[] damagedBy = GetDamagedBy(itemslot);
		if (damagedBy != null && damagedBy.Contains(EnumItemDamageSource.BlockBreaking))
		{
			DamageItem(world, byEntity, itemslot, amount);
		}
		return true;
	}

	public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
	{
		return toolModes;
	}

	public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
	{
		return Math.Min(toolModes.Length - 1, slot.Itemstack.Attributes.GetInt("toolMode"));
	}

	public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
	{
		slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
	}

	protected virtual void ProbeBlockNodeMode(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, int radius)
	{
		IPlayer player = null;
		if (byEntity is EntityPlayer)
		{
			player = world.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
		}
		Block block = world.BlockAccessor.GetBlock(blockSel.Position);
		float dropQuantityMultiplier = 1f;
		if (block.BlockMaterial == EnumBlockMaterial.Ore || block.BlockMaterial == EnumBlockMaterial.Stone)
		{
			dropQuantityMultiplier = 0f;
		}
		block.OnBlockBroken(world, blockSel.Position, player, dropQuantityMultiplier);
		if (!isPropickable(block) || !(player is IServerPlayer serverPlayer))
		{
			return;
		}
		BlockPos blockPos = blockSel.Position.Copy();
		Dictionary<string, int> quantityFound = new Dictionary<string, int>();
		api.World.BlockAccessor.WalkBlocks(blockPos.AddCopy(radius, radius, radius), blockPos.AddCopy(-radius, -radius, -radius), (Block nblock, int x, int y, int z) =>
		{
			if (nblock.BlockMaterial == EnumBlockMaterial.Ore && nblock.Variant.ContainsKey("type"))
			{
				string key = "ore-" + nblock.Variant["type"];
				quantityFound.TryGetValue(key, out var value);
				quantityFound[key] = value + 1;
			}
		});
		List<KeyValuePair<string, int>> list = quantityFound.OrderByDescending((KeyValuePair<string, int> val) => val.Value).ToList();
		if (list.Count == 0)
		{
			serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(serverPlayer.LanguageCode, "No ore node nearby"), EnumChatType.Notification);
			return;
		}
		serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(serverPlayer.LanguageCode, "Found the following ore nodes"), EnumChatType.Notification);
		foreach (KeyValuePair<string, int> item in list)
		{
			string l = Lang.GetL(serverPlayer.LanguageCode, item.Key);
			string l2 = Lang.GetL(serverPlayer.LanguageCode, resultTextByQuantity(item.Value), Lang.Get(item.Key));
			serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(serverPlayer.LanguageCode, l2, l), EnumChatType.Notification);
		}
	}

	private bool isPropickable(Block block)
	{
		if (block == null)
		{
			return false;
		}
		return block.Attributes?["propickable"].AsBool() == true;
	}

	protected virtual string resultTextByQuantity(int value)
	{
		if (value < 10)
		{
			return "propick-nodesearch-traceamount";
		}
		if (value < 20)
		{
			return "propick-nodesearch-smallamount";
		}
		if (value < 40)
		{
			return "propick-nodesearch-mediumamount";
		}
		if (value < 80)
		{
			return "propick-nodesearch-largeamount";
		}
		if (value < 160)
		{
			return "propick-nodesearch-verylargeamount";
		}
		return "propick-nodesearch-hugeamount";
	}

	protected virtual void ProbeBlockDensityMode(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel)
	{
		IPlayer player = null;
		if (byEntity is EntityPlayer)
		{
			player = world.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
		}
		Block block = world.BlockAccessor.GetBlock(blockSel.Position);
		float dropQuantityMultiplier = 1f;
		if (block.BlockMaterial == EnumBlockMaterial.Ore || block.BlockMaterial == EnumBlockMaterial.Stone)
		{
			dropQuantityMultiplier = 0f;
		}
		block.OnBlockBroken(world, blockSel.Position, player, dropQuantityMultiplier);
		if (!isPropickable(block) || !(player is IServerPlayer serverPlayer))
		{
			return;
		}
		if (serverPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
		{
			PrintProbeResults(world, serverPlayer, itemslot, blockSel.Position);
			return;
		}
		if (!(itemslot.Itemstack.Attributes["probePositions"] is IntArrayAttribute { value: not null } intArrayAttribute) || intArrayAttribute.value.Length == 0)
		{
			IntArrayAttribute intArrayAttribute2 = (IntArrayAttribute)(itemslot.Itemstack.Attributes["probePositions"] = new IntArrayAttribute());
			intArrayAttribute2.AddInt(blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z);
			serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(serverPlayer.LanguageCode, "Ok, need 2 more samples"), EnumChatType.Notification);
			return;
		}
		float num = 2f;
		intArrayAttribute.AddInt(blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z);
		int[] value = intArrayAttribute.value;
		for (int i = 0; i < value.Length; i += 3)
		{
			int num2 = value[i];
			int num3 = value[i + 1];
			int num4 = value[i + 2];
			float num5 = 99f;
			for (int j = i + 3; j < value.Length; j += 3)
			{
				int num6 = num2 - value[j];
				int num7 = num3 - value[j + 1];
				int num8 = num4 - value[j + 2];
				num5 = Math.Min(num5, GameMath.Sqrt(num6 * num6 + num7 * num7 + num8 * num8));
			}
			if (i + 3 < value.Length)
			{
				num -= GameMath.Clamp(num5 * num5, 3f, 16f) / 16f;
				if (num5 > 20f)
				{
					serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(serverPlayer.LanguageCode, "Sample too far away from initial reading. Sampling around this point now, need 2 more samples."), EnumChatType.Notification);
					intArrayAttribute.value = new int[3]
					{
						blockSel.Position.X,
						blockSel.Position.Y,
						blockSel.Position.Z
					};
					return;
				}
			}
		}
		if (num > 0f)
		{
			int num9 = (int)Math.Ceiling(num);
			serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, (num9 > 1) ? Lang.GetL(serverPlayer.LanguageCode, "propick-xsamples", num9) : Lang.GetL(serverPlayer.LanguageCode, "propick-1sample"), EnumChatType.Notification);
		}
		else
		{
			int x = value[0];
			int y = value[1];
			int z = value[2];
			PrintProbeResults(world, serverPlayer, itemslot, new BlockPos(x, y, z));
			intArrayAttribute.value = Array.Empty<int>();
		}
	}

	protected virtual void PrintProbeResults(IWorldAccessor world, IServerPlayer splr, ItemSlot itemslot, BlockPos pos)
	{
		PropickReading propickReading = GenProbeResults(world, pos);
		string message = propickReading.ToHumanReadable(splr.LanguageCode, ppws.pageCodes);
		splr.SendMessage(GlobalConstants.InfoLogChatGroup, message, EnumChatType.Notification);
		world.Api.ModLoader.GetModSystem<ModSystemOreMap>()?.DidProbe(propickReading, splr);
	}

	protected virtual PropickReading GenProbeResults(IWorldAccessor world, BlockPos pos)
	{
		if (api.ModLoader.GetModSystem<GenDeposits>()?.Deposits == null)
		{
			return null;
		}
		int regionSize = world.BlockAccessor.RegionSize;
		IMapRegion mapRegion = world.BlockAccessor.GetMapRegion(pos.X / regionSize, pos.Z / regionSize);
		int num = pos.X % regionSize;
		int num2 = pos.Z % regionSize;
		pos = pos.Copy();
		pos.Y = world.BlockAccessor.GetTerrainMapheightAt(pos);
		int[] rockColumn = ppws.GetRockColumn(pos.X, pos.Z);
		PropickReading propickReading = new PropickReading
		{
			Position = new Vec3d(pos.X, pos.Y, pos.Z)
		};
		foreach (KeyValuePair<string, IntDataMap2D> oreMap in mapRegion.OreMaps)
		{
			IntDataMap2D value = oreMap.Value;
			int innerSize = value.InnerSize;
			float x = (float)num / (float)regionSize * (float)innerSize;
			float z = (float)num2 / (float)regionSize * (float)innerSize;
			int unpaddedColorLerped = value.GetUnpaddedColorLerped(x, z);
			if (ppws.depositsByCode.ContainsKey(oreMap.Key))
			{
				ppws.depositsByCode[oreMap.Key].GetPropickReading(pos, unpaddedColorLerped, rockColumn, out var ppt, out var totalFactor);
				if (totalFactor > 0.0)
				{
					OreReading oreReading = new OreReading();
					oreReading.TotalFactor = totalFactor;
					oreReading.PartsPerThousand = ppt;
					propickReading.OreReadings[oreMap.Key] = oreReading;
				}
			}
		}
		return propickReading;
	}

	public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
	{
		base.OnHeldIdle(slot, byEntity);
	}

	public override void OnUnloaded(ICoreAPI api)
	{
		base.OnUnloaded(api);
		if (api is ICoreServerAPI coreServerAPI)
		{
			ppws?.Dispose(api);
			coreServerAPI.ObjectCache.Remove("propickworkspace");
		}
		int num = 0;
		while (toolModes != null && num < toolModes.Length)
		{
			toolModes[num]?.Dispose();
			num++;
		}
	}

	public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
	{
		return new WorldInteraction[1]
		{
			new WorldInteraction
			{
				ActionLangCode = "Change tool mode",
				HotKeyCodes = new string[1] { "toolmodeselect" },
				MouseButton = EnumMouseButton.None
			}
		};
	}
}
