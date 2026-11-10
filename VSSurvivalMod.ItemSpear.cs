using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public class ItemSpear : Item
{
	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		CollectibleBehaviorAnimationAuthoritative collectibleBehavior = GetCollectibleBehavior<CollectibleBehaviorAnimationAuthoritative>(withInheritance: true);
		if (collectibleBehavior == null)
		{
			api.World.Logger.Warning("Spear {0} uses ItemSpear class, but lacks required AnimationAuthoritative behavior. Taking the freedom to add this behavior, but please fix json item type.", Code);
			collectibleBehavior = new CollectibleBehaviorAnimationAuthoritative(this);
			collectibleBehavior.OnLoaded(api);
			CollectibleBehaviors = CollectibleBehaviors.Append(collectibleBehavior);
		}
	}

	public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
	{
		return null;
	}

	public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
	{
		base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handling);
		if (handling != EnumHandHandling.PreventDefault)
		{
			handling = EnumHandHandling.PreventDefault;
			byEntity.Attributes.SetInt("aiming", 1);
			byEntity.Attributes.SetInt("aimingCancel", 0);
			byEntity.StartAnimation("aim");
		}
	}

	public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
	{
		return true;
	}

	public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
	{
		byEntity.Attributes.SetInt("aiming", 0);
		byEntity.StopAnimation("aim");
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
		byEntity.StopAnimation("aim");
		if (secondsUsed < 0.35f)
		{
			return;
		}
		float damage = 1.5f;
		if (slot.Itemstack.Collectible.Attributes != null)
		{
			damage = slot.Itemstack.Collectible.Attributes["damage"].AsFloat();
		}
		(api as ICoreClientAPI)?.World.AddCameraShake(0.17f);
		ItemStack projectileStack = slot.TakeOut(1);
		slot.MarkDirty();
		IPlayer player = null;
		if (byEntity is EntityPlayer)
		{
			player = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
		}
		byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/throw"), byEntity, player, randomizePitch: false, 8f);
		EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation(Attributes["spearEntityCode"].AsString()));
		Entity entity = byEntity.World.ClassRegistry.CreateEntity(entityType);
		IProjectile obj = entity as IProjectile;
		obj.FiredBy = byEntity;
		obj.Damage = damage;
		obj.DamageTier = Attributes["damageTier"].AsInt();
		obj.ProjectileStack = projectileStack;
		obj.DropOnImpactChance = 1.1f;
		obj.DamageStackOnImpact = true;
		obj.Weight = 0.3f;
		obj.IgnoreInvFrames = Attributes["ignoreInvFrames"].AsBool();
		double speed = 0.65 * (double)byEntity.Stats.GetBlended("bowDrawingStrength");
		EntityProjectileBase.SpawnProjectile(entity, byEntity, speed, 0.75, 0.0, 0.3, -0.15, 20.0);
		byEntity.StartAnimation("throw");
		if (byEntity is EntityPlayer)
		{
			RefillSlotIfEmpty(slot, byEntity, (ItemStack itemstack) => itemstack.Collectible is ItemSpear);
		}
		float pitchModifier = (byEntity as EntityPlayer).talkUtil.pitchModifier;
		player.Entity.World.PlaySoundAt(new AssetLocation("sounds/player/strike"), player.Entity, player, pitchModifier * 0.9f + (float)api.World.Rand.NextDouble() * 0.2f, 16f, 0.35f);
	}

	public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
	{
	}

	public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
	{
		base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
		if (inSlot.Itemstack.Collectible.Attributes != null)
		{
			float num = 1.5f;
			if (inSlot.Itemstack.Collectible.Attributes != null)
			{
				num = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();
			}
			dsc.AppendLine(num + Lang.Get("piercing-damage-thrown"));
		}
	}

	public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
	{
		return new WorldInteraction[1]
		{
			new WorldInteraction
			{
				ActionLangCode = "heldhelp-throw",
				MouseButton = EnumMouseButton.Right
			}
		}.Append(base.GetHeldInteractionHelp(inSlot));
	}
}
