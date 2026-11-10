using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class ItemBow : Item
{
	private WorldInteraction[] interactions;

	private string aimAnimation;

	public override void OnLoaded(ICoreAPI api)
	{
		aimAnimation = Attributes["aimAnimation"].AsString();
		if (api.Side != EnumAppSide.Client)
		{
			return;
		}
		_ = api;
		interactions = ObjectCacheUtil.GetOrCreate(api, "bowInteractions", () =>
		{
			List<ItemStack> list = new List<ItemStack>();
			foreach (CollectibleObject collectible in api.World.Collectibles)
			{
				if (collectible.Code.PathStartsWith("arrow-"))
				{
					list.Add(new ItemStack(collectible));
				}
			}
			return new WorldInteraction[1]
			{
				new WorldInteraction
				{
					ActionLangCode = "heldhelp-chargebow",
					MouseButton = EnumMouseButton.Right,
					HotKeyCode = "dropitems",
					Itemstacks = list.ToArray()
				}
			};
		});
	}

	public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
	{
		return null;
	}

	protected ItemSlot GetNextArrow(EntityAgent byEntity)
	{
		ItemSlot slot = null;
		byEntity.WalkInventory((ItemSlot invslot) =>
		{
			if (invslot is ItemSlotCreative)
			{
				return true;
			}
			ItemStack itemstack = invslot.Itemstack;
			if (itemstack != null && itemstack.Collectible != null && itemstack.Collectible.Code.PathStartsWith("arrow-") && itemstack.StackSize > 0)
			{
				slot = invslot;
				return false;
			}
			return true;
		});
		return slot;
	}

	public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
	{
		base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
		if (handling != EnumHandHandling.PreventDefault && (!(byEntity.MountedOn?.Controls ?? byEntity.Controls).CtrlKey || (entitySel?.SelectionBoxIndex ?? (-1)) < 0 || entitySel.Entity?.GetBehavior<EntityBehaviorAttachable>() == null) && GetNextArrow(byEntity) != null)
		{
			if (byEntity.World is IClientWorldAccessor)
			{
				slot.Itemstack.TempAttributes.SetInt("renderVariant", 1);
			}
			slot.Itemstack.Attributes.SetInt("renderVariant", 1);
			byEntity.Attributes.SetInt("aiming", 1);
			byEntity.Attributes.SetInt("aimingCancel", 0);
			byEntity.AnimManager.StartAnimation(aimAnimation);
			IPlayer dualCallByPlayer = null;
			if (byEntity is EntityPlayer)
			{
				dualCallByPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
			}
			byEntity.World.PlaySoundAt(new AssetLocation("sounds/bow-draw"), byEntity, dualCallByPlayer, randomizePitch: false, 8f);
			handling = EnumHandHandling.PreventDefault;
		}
	}

	public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
	{
		int num = GameMath.Clamp((int)Math.Ceiling(secondsUsed * 4f), 0, 3);
		int num2 = slot.Itemstack.Attributes.GetInt("renderVariant");
		slot.Itemstack.TempAttributes.SetInt("renderVariant", num);
		slot.Itemstack.Attributes.SetInt("renderVariant", num);
		if (num2 != num)
		{
			(byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
		}
		return true;
	}

	public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
	{
		byEntity.Attributes.SetInt("aiming", 0);
		byEntity.AnimManager.StopAnimation(aimAnimation);
		if (byEntity.World is IClientWorldAccessor)
		{
			slot.Itemstack?.TempAttributes.RemoveAttribute("renderVariant");
		}
		slot.Itemstack?.Attributes.SetInt("renderVariant", 0);
		if (cancelReason != EnumItemUseCancelReason.Destroyed)
		{
			(byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
		}
		if (cancelReason != EnumItemUseCancelReason.ReleasedMouse)
		{
			byEntity.Attributes.SetInt("aimingCancel", 1);
		}
		return true;
	}

	public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
	{
		if (byEntity.Attributes.GetInt("aimingCancel") == 1)
		{
			return;
		}
		byEntity.Attributes.SetInt("aiming", 0);
		byEntity.AnimManager.StopAnimation(aimAnimation);
		if (byEntity.World.Side == EnumAppSide.Client)
		{
			slot.Itemstack.TempAttributes.RemoveAttribute("renderVariant");
			byEntity.AnimManager.StartAnimation("bowhit");
			return;
		}
		slot.Itemstack.Attributes.SetInt("renderVariant", 0);
		(byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
		if (secondsUsed < 0.65f)
		{
			return;
		}
		ItemSlot nextArrow = GetNextArrow(byEntity);
		if (nextArrow != null)
		{
			float num = 0f;
			if (slot.Itemstack.Collectible.Attributes != null)
			{
				num += slot.Itemstack.Collectible.Attributes["damage"].AsFloat();
			}
			if (nextArrow.Itemstack.Collectible.Attributes != null)
			{
				num += nextArrow.Itemstack.Collectible.Attributes["damage"].AsFloat();
			}
			ItemStack itemStack = nextArrow.TakeOut(1);
			nextArrow.MarkDirty();
			byEntity.World.PlaySoundAt(new AssetLocation("sounds/bow-release"), byEntity, null, randomizePitch: false, 8f);
			float num2 = 0.5f;
			if (itemStack.ItemAttributes != null)
			{
				num2 = itemStack.ItemAttributes["breakChanceOnImpact"].AsFloat(0.5f);
			}
			EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation(itemStack.ItemAttributes["arrowEntityCode"].AsString("arrow-" + itemStack.Collectible.Variant["material"])));
			Entity entity = byEntity.World.ClassRegistry.CreateEntity(entityType);
			IProjectile obj = entity as IProjectile;
			obj.FiredBy = byEntity;
			obj.Damage = num;
			obj.DamageTier = Attributes["damageTier"].AsInt();
			obj.ProjectileStack = itemStack;
			obj.DropOnImpactChance = 1f - num2;
			obj.IgnoreInvFrames = Attributes["ignoreInvFrames"].AsBool();
			obj.WeaponStack = slot.Itemstack;
			float num3 = Math.Max(0.001f, 1f - byEntity.Attributes.GetFloat("aimingAccuracy"));
			double num4 = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1.0) * (double)num3 * 0.75;
			double num5 = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1.0) * (double)num3 * 0.75;
			Vec3d vec3d = byEntity.Pos.XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0);
			Vec3d pos = (vec3d.AheadCopy(1.0, (double)byEntity.Pos.Pitch + num4, (double)byEntity.Pos.Yaw + num5) - vec3d) * byEntity.Stats.GetBlended("bowDrawingStrength");
			entity.Pos.SetPosWithDimension(byEntity.Pos.BehindCopy(0.21).XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0));
			entity.Pos.Motion.Set(pos);
			entity.World = byEntity.World;
			obj.PreInitialize();
			byEntity.World.SpawnPriorityEntity(entity);
			slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot);
			slot.MarkDirty();
			byEntity.AnimManager.StartAnimation("bowhit");
		}
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		if (inSlot.Itemstack.Collectible.Attributes != null)
		{
			float num = inSlot.Itemstack.Collectible.Attributes?["damage"].AsFloat() ?? 0f;
			if (num != 0f)
			{
				dsc.AppendLine(Lang.Get("bow-piercingdamage", num));
			}
			float num2 = inSlot.Itemstack.Collectible?.Attributes["statModifier"]["rangedWeaponsAcc"].AsFloat() ?? 0f;
			if (num2 != 0f)
			{
				dsc.AppendLine(Lang.Get("bow-accuracybonus", (num2 > 0f) ? "+" : "", (int)(100f * num2)));
			}
		}
	}

	public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
	{
		return interactions.Append(base.GetHeldInteractionHelp(inSlot));
	}
}
