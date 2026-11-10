using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public class EntityProjectile : EntityProjectileBase, IProjectile
{
	public override bool ApplyGravity => !Stuck;

	public override bool IsInteractable => false;

	void IProjectile.PreInitialize()
	{
		Collectible = true;
		SetInitialRotation();
	}

	public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
	{
		base.Initialize(properties, api, InChunkIndex3d);
		collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);
	}

	public override void OnGameTick(float dt)
	{
		base.OnGameTick(dt);
		if (ShouldDespawn)
		{
			return;
		}
		EntityPos pos = Pos;
		Stuck = Collided || collTester.IsColliding(World.BlockAccessor, collisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
		if (Api.Side == EnumAppSide.Server)
		{
			WatchedAttributes.SetBool("stuck", Stuck);
		}
		double impactSpeed = Math.Max(motionBeforeCollide.Length(), pos.Motion.Length());
		if (Stuck)
		{
			IsColliding(pos, impactSpeed);
			entitiesHit.Clear();
			return;
		}
		SetRotationFromMotion();
		if (!TryAttackEntity(impactSpeed))
		{
			beforeCollided = false;
			motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
		}
	}

	public override void OnCollided()
	{
		EntityPos pos = Pos;
		IsColliding(Pos, Math.Max(motionBeforeCollide.Length(), pos.Motion.Length()));
		motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
	}

	protected virtual void IsColliding(EntityPos pos, double impactSpeed)
	{
		pos.Motion.Set(0.0, 0.0, 0.0);
		if (beforeCollided || !(World is IServerWorldAccessor) || World.ElapsedMilliseconds <= msCollide + 500)
		{
			return;
		}
		if (impactSpeed >= 0.07)
		{
			World.PlaySoundAt(impactSound, this, null, randomizePitch: false);
			WatchedAttributes.MarkAllDirty();
			if (DamageStackOnImpact)
			{
				ProjectileStack.Collectible.DamageItem(World, this, new DummySlot(ProjectileStack));
				if (((ProjectileStack == null || ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack) != 0) ? 1 : 0) <= (false ? 1 : 0))
				{
					Die();
				}
			}
		}
		TryAttackEntity(impactSpeed);
		msCollide = World.ElapsedMilliseconds;
		beforeCollided = true;
	}

	public virtual void SetInitialRotation()
	{
		EntityPos pos = Pos;
		double num = pos.Motion.Length();
		if (num > 0.01)
		{
			pos.Pitch = 0f;
			pos.Yaw = (float)Math.PI + (float)Math.Atan2(pos.Motion.X / num, pos.Motion.Z / num);
			pos.Roll = 0f - (float)Math.Asin(GameMath.Clamp((0.0 - pos.Motion.Y) / num, -1.0, 1.0));
		}
	}

	public virtual void SetRotationFromMotion()
	{
		EntityPos pos = Pos;
		double num = pos.Motion.Length();
		if (num > 0.01)
		{
			pos.Pitch = 0f;
			pos.Yaw = (float)Math.PI + (float)Math.Atan2(pos.Motion.X / num, pos.Motion.Z / num) + GameMath.Cos((float)(World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f;
			pos.Roll = 0f - (float)Math.Asin(GameMath.Clamp((0.0 - pos.Motion.Y) / num, -1.0, 1.0)) + GameMath.Sin((float)(World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f;
		}
	}
}
