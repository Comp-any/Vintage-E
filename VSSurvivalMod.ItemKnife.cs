using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public class ItemKnife : Item
{
	public static SimpleParticleProperties particlesStab;

	public string knifeHitBlockAnimation;

	public string knifeHitEntityAnimation;

	static ItemKnife()
	{
		particlesStab = new SimpleParticleProperties(1f, 1f, ColorUtil.ToRgba(50, 220, 220, 220), new Vec3d(), new Vec3d(), new Vec3f(-0.1f, -0.1f, -0.1f), new Vec3f(0.1f, 0.1f, 0.1f), 1.5f, 0f, 0.5f, 0.75f);
		particlesStab.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -0.6f);
		particlesStab.AddPos.Set(0.10000000149011612, 0.10000000149011612, 0.10000000149011612);
		particlesStab.addLifeLength = 0.5f;
		particlesStab.RandomVelocityChange = true;
		particlesStab.MinQuantity = 200f;
		particlesStab.AddQuantity = 50f;
		particlesStab.MinSize = 0.2f;
		particlesStab.ParticleModel = EnumParticleModel.Quad;
		particlesStab.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -150f);
	}

	public virtual float GetKnifeHarvestingSpeed(ItemSlot slot)
	{
		return 1f / ((GetMiningSpeeds(slot)[EnumBlockMaterial.Plant] - 1f) * 0.5f + 1f);
	}

	public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
	{
		if ((byEntity as EntityPlayer)?.EntitySelection != null)
		{
			return knifeHitEntityAnimation;
		}
		if ((byEntity as EntityPlayer)?.BlockSelection != null)
		{
			if (byEntity.AnimManager.IsAnimationActive("knifestab"))
			{
				return "idle";
			}
			return knifeHitBlockAnimation;
		}
		return base.GetHeldTpHitAnimation(slot, byEntity);
	}

	public override void OnLoaded(ICoreAPI api)
	{
		base.OnLoaded(api);
		knifeHitBlockAnimation = Attributes["knifeHitBlockAnimation"].AsString(HeldTpHitAnimation);
		knifeHitEntityAnimation = Attributes["knifeHitEntityAnimation"].AsString(HeldTpHitAnimation);
	}

	public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
	{
		base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
		if (handling == EnumHandHandling.PreventDefault)
		{
			return;
		}
		byEntity.Attributes.SetBool("isInsertGear", value: false);
		if (byEntity.LeftHandItemSlot?.Itemstack?.Collectible is ItemTemporalGear && byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>() != null)
		{
			byEntity.StartAnimation("insertgear");
			byEntity.Attributes.SetBool("isInsertGear", value: true);
			byEntity.Attributes.SetBool("stabPlayed", value: false);
			byEntity.Attributes.SetBool("didHurt", value: false);
			ITreeAttribute treeAttribute = byEntity.WatchedAttributes.GetTreeAttribute("health");
			if (treeAttribute != null && treeAttribute.GetFloat("currenthealth") <= 2f)
			{
				if (api.Side == EnumAppSide.Client)
				{
					(api as ICoreClientAPI).TriggerIngameError(this, "toodangerous", Lang.Get("Cannot apply without dying"));
				}
			}
			else
			{
				handling = EnumHandHandling.PreventDefault;
			}
			return;
		}
		if (byEntity.Controls.ShiftKey)
		{
			IHarvestable iHarvestable = getIHarvestable(byEntity, blockSel, entitySel, out var pos);
			if (iHarvestable != null && iHarvestable.IsHarvestable(slot, byEntity))
			{
				byEntity.World.PlaySoundAt(iHarvestable.HarvestableSound, pos.X, pos.Y, pos.Z, (byEntity as EntityPlayer)?.Player, randomizePitch: false, 12f);
				handling = EnumHandHandling.PreventDefault;
				return;
			}
		}
		handling = EnumHandHandling.NotHandled;
	}

	public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
	{
		if (byEntity.Attributes.GetBool("isInsertGear"))
		{
			if (!(byEntity.LeftHandItemSlot?.Itemstack?.Collectible is ItemTemporalGear))
			{
				return false;
			}
			if (byEntity.World.Side == EnumAppSide.Client)
			{
				if (secondsUsed > 1.1f && !byEntity.Attributes.GetBool("stabPlayed"))
				{
					byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/stab"), byEntity, (byEntity as EntityPlayer)?.Player, randomizePitch: false, 12f, 0.3f);
					byEntity.Attributes.SetBool("stabPlayed", value: true);
				}
				return true;
			}
			if (secondsUsed > 1.1f && !byEntity.Attributes.GetBool("didHurt"))
			{
				byEntity.ReceiveDamage(new DamageSource
				{
					DamageTier = 0,
					Source = EnumDamageSource.Internal,
					Type = EnumDamageType.Injury
				}, 2f);
				slot?.Itemstack?.Collectible.DamageItem(byEntity.World, byEntity, slot);
				byEntity.Attributes.SetBool("didHurt", value: true);
			}
			if (secondsUsed >= 1.95f && !byEntity.Attributes.GetBool("stabPlayed"))
			{
				byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>().AddStability(0.3);
				byEntity.LeftHandItemSlot.TakeOut(1);
				byEntity.LeftHandItemSlot.MarkDirty();
				((byEntity as EntityPlayer)?.Player as IServerPlayer)?.BroadcastPlayerData();
				int h = 110 + api.World.Rand.Next(15);
				int v = 100 + api.World.Rand.Next(50);
				particlesStab.MinPos = byEntity.Pos.XYZ.Add(byEntity.SelectionBox.X1, 0.0, byEntity.SelectionBox.Z1);
				particlesStab.AddPos = new Vec3d(byEntity.SelectionBox.XSize, byEntity.SelectionBox.Y2, byEntity.SelectionBox.ZSize);
				particlesStab.Color = ColorUtil.ReverseColorBytes(ColorUtil.HsvToRgba(h, 180, v, 150));
				api.World.SpawnParticles(particlesStab);
				byEntity.Attributes.SetBool("stabPlayed", value: true);
			}
			return secondsUsed < 2f;
		}
		if (api.Side == EnumAppSide.Server)
		{
			return true;
		}
		IHarvestable iHarvestable = getIHarvestable(byEntity, blockSel, entitySel, out var _);
		if (iHarvestable != null && iHarvestable.IsHarvestable(slot, byEntity))
		{
			if (byEntity.World.Side == EnumAppSide.Client)
			{
				slot.Itemstack.TempAttributes.SetString("harvestanimation", iHarvestable.HarvestAnimation);
				byEntity.StartAnimation(iHarvestable.HarvestAnimation);
			}
			return secondsUsed < GetKnifeHarvestingSpeed(slot) * iHarvestable.GetHarvestDuration(slot, byEntity) + 0.15f;
		}
		return false;
	}

	public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
	{
		byEntity.StopAnimation("insertgear");
		byEntity.StopAnimation(slot.Itemstack.TempAttributes.GetString("harvestanimation"));
		if (!(byEntity.LeftHandItemSlot?.Itemstack?.Collectible is ItemTemporalGear))
		{
			IHarvestable iHarvestable = getIHarvestable(byEntity, blockSel, entitySel, out var _);
			if (iHarvestable != null && iHarvestable.IsHarvestable(slot, byEntity) && secondsUsed >= GetKnifeHarvestingSpeed(slot) * iHarvestable.GetHarvestDuration(slot, byEntity) - 0.1f)
			{
				iHarvestable.SetHarvested((byEntity as EntityPlayer)?.Player);
				slot?.Itemstack?.Collectible.DamageItem(byEntity.World, byEntity, slot, 3);
			}
		}
	}

	public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
	{
		byEntity.StopAnimation(slot.Itemstack.TempAttributes.GetString("harvestanimation"));
		byEntity.StopAnimation("insertgear");
		return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);
	}

	public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
	{
		if (blockSel == null)
		{
			base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
			handling = EnumHandHandling.PreventDefault;
		}
	}

	protected static IHarvestable getIHarvestable(EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, out Vec3d pos)
	{
		IHarvestable harvestable = entitySel?.Entity.GetInterface<IHarvestable>();
		if (harvestable != null)
		{
			pos = entitySel.Position;
			return harvestable;
		}
		pos = null;
		if (blockSel != null)
		{
			harvestable = byEntity.World.BlockAccessor.GetBlock(blockSel.Position)?.GetInterface<IHarvestable>(byEntity.World, blockSel.Position);
			pos = blockSel.Position.ToVec3d().Add(0.5, 0.0, 0.5);
		}
		return harvestable;
	}
}
