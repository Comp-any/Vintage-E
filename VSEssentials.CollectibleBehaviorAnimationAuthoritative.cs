using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public class CollectibleBehaviorAnimationAuthoritative(CollectibleObject collObj) : CollectibleBehavior(collObj)
{
	public delegate void OnBeginHitEntityDelegate(EntityAgent byEntity, ref EnumHandling handling);

	protected AssetLocation strikeSound;

	public EnumHandInteract strikeSoundHandInteract = EnumHandInteract.HeldItemAttack;

	private bool onlyOnEntity;

	public event OnBeginHitEntityDelegate OnBeginHitEntity;

	public override void Initialize(JsonObject properties)
	{
		base.Initialize(properties);
		strikeSound = AssetLocation.Create(properties["strikeSound"].AsString("sounds/player/strike"), collObj.Code.Domain);
		onlyOnEntity = properties["onlyOnEntity"].AsBool();
	}

	public static float getHitDamageAtFrame(EntityAgent byEntity, string animCode)
	{
		if (byEntity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out var value))
		{
			JsonObject attributes = value.Attributes;
			if (attributes != null && attributes["damageAtFrame"].Exists)
			{
				return value.Attributes["damageAtFrame"].AsFloat(-1f) / value.AnimationSpeed;
			}
		}
		return -1f;
	}

	public static float getSoundAtFrame(EntityAgent byEntity, string animCode)
	{
		if (byEntity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out var value))
		{
			JsonObject attributes = value.Attributes;
			if (attributes != null && attributes["soundAtFrame"].Exists)
			{
				return value.Attributes["soundAtFrame"].AsFloat(-1f) / value.AnimationSpeed;
			}
		}
		return -1f;
	}

	public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity, ref EnumHandling bhHandling)
	{
		bhHandling = EnumHandling.PreventDefault;
		return collObj.HeldTpUseAnimation;
	}

	public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handHandling, ref EnumHandling handling)
	{
		if (!onlyOnEntity || entitySel != null)
		{
			StartAttack(slot, byEntity);
			handling = EnumHandling.PreventSubsequent;
			handHandling = EnumHandHandling.PreventDefault;
		}
	}

	public override bool OnHeldAttackCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handling)
	{
		handling = EnumHandling.PreventSubsequent;
		return false;
	}

	public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, ref EnumHandling handling)
	{
		handling = EnumHandling.PreventSubsequent;
		return StepAttack(slot, byEntity);
	}

	public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, ref EnumHandling handling)
	{
	}

	public void StartAttack(ItemSlot slot, EntityAgent byEntity)
	{
		string heldTpHitAnimation = collObj.GetHeldTpHitAnimation(slot, byEntity);
		byEntity.Attributes.SetInt("didattack", 0);
		byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback
		{
			Animation = heldTpHitAnimation,
			Frame = getSoundAtFrame(byEntity, heldTpHitAnimation),
			Callback = () =>
			{
				playStrikeSound(byEntity);
			}
		});
		byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback
		{
			Animation = heldTpHitAnimation,
			Frame = getHitDamageAtFrame(byEntity, heldTpHitAnimation),
			Callback = () =>
			{
				hitEntity(byEntity);
			}
		});
	}

	public bool StepAttack(ItemSlot slot, EntityAgent byEntity)
	{
		string heldTpHitAnimation = collObj.GetHeldTpHitAnimation(slot, byEntity);
		return byEntity.AnimManager.IsAnimationActive(heldTpHitAnimation);
	}

	protected virtual void playStrikeSound(EntityAgent byEntity)
	{
		IPlayer player = (byEntity as EntityPlayer).Player;
		if (player != null && byEntity.Controls.HandUse == strikeSoundHandInteract)
		{
			player.Entity.World.PlaySoundAt(strikeSound, player.Entity, player, 0.9f + (float)byEntity.World.Rand.NextDouble() * 0.2f, 16f, 0.35f);
		}
	}

	public virtual void hitEntity(EntityAgent byEntity)
	{
		EnumHandling handling = EnumHandling.PassThrough;
		OnBeginHitEntity?.Invoke(byEntity, ref handling);
		if (handling != EnumHandling.PassThrough)
		{
			return;
		}
		EntitySelection entitySelection = (byEntity as EntityPlayer)?.EntitySelection;
		long valueOrDefault = (entitySelection?.Entity?.EntityId).GetValueOrDefault();
		long valueOrDefault2 = (byEntity?.MountedOn?.Entity?.EntityId).GetValueOrDefault();
		if (byEntity.World.Side == EnumAppSide.Client)
		{
			IClientWorldAccessor clientWorldAccessor = byEntity.World as IClientWorldAccessor;
			if (byEntity.Attributes.GetInt("didattack") == 0)
			{
				if (entitySelection != null && valueOrDefault != valueOrDefault2)
				{
					clientWorldAccessor.TryAttackEntity(entitySelection);
				}
				byEntity.Attributes.SetInt("didattack", 1);
				clientWorldAccessor.AddCameraShake(0.25f);
			}
		}
		else if (byEntity.Attributes.GetInt("didattack") == 0 && entitySelection != null && valueOrDefault != valueOrDefault2)
		{
			byEntity.Attributes.SetInt("didattack", 1);
		}
	}
}
