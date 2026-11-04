using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// ProjectE's projectiles, fired with the fire-projectile key or right-click. Entity type
    /// <c>vintagee:projectile-{kind}</c>; all fly in a straight line without gravity (the smite's arrows steer
    /// towards hostile creatures) and act on the first block or creature they hit:
    /// <list type="bullet">
    /// <item><c>lens</c> (Hyperkinetic/Catalytic Lens): explodes, radius <see cref="Radius"/>.</item>
    /// <item><c>water</c> (Evertide Amulet) / <c>lava</c> (Volcanite Amulet): places a source block where it lands.</item>
    /// <item><c>fire</c> (Ignition Ring, Ring of Arcana): sets the creature it hits on fire.</item>
    /// <item><c>arrow</c> (Archangel's Smite): homing arrow, <see cref="Damage"/> piercing damage.</item>
    /// </list>
    /// Shots that hit nothing vanish after <see cref="MaxLifeSeconds"/>; lens and fire shots fizzle in water.
    /// Movement and hit tests run on the server; clients interpolate the position.
    /// </summary>
    public class EntityVintageEProjectile : Entity
    {
        public const float MaxLifeSeconds = 8;

        /// <summary>
        /// Spawns a <c>vintagee:projectile-{kind}</c> from the player's eyes in the direction they look, moving
        /// <paramref name="blocksPerSecond"/>. Returns null if the entity type is missing.
        /// </summary>
        public static EntityVintageEProjectile Shoot(IServerPlayer player, string kind, double blocksPerSecond, Action<EntityVintageEProjectile> setup = null, float spread = 0)
        {
            IWorldAccessor world = player.Entity.World;
            EntityProperties type = world.GetEntityType(new AssetLocation("vintagee", "projectile-" + kind));
            if (type == null) return null;
            var shot = (EntityVintageEProjectile)world.ClassRegistry.CreateEntity(type);
            EntityPos ppos = player.Entity.Pos;
            Vec3d eye = ppos.XYZ.Add(0, player.Entity.LocalEyePos.Y, 0);
            Random rand = world.Rand;
            float pitch = ppos.Pitch + (float)(rand.NextDouble() * 2 - 1) * spread;
            float yaw = ppos.Yaw + (float)(rand.NextDouble() * 2 - 1) * spread;
            Vec3d dir = eye.AheadCopy(1, pitch, yaw).Sub(eye).Normalize();
            double perFrame = blocksPerSecond / 60.0; // motion is in blocks per 1/60 s
            shot.Pos.SetPos(eye.AddCopy(dir.X * 0.8, dir.Y * 0.8, dir.Z * 0.8));
            shot.Pos.Motion.Set(dir.X * perFrame, dir.Y * perFrame, dir.Z * perFrame);
            shot.FiredById = player.Entity.EntityId;
            shot.FiredByUid = player.PlayerUID;
            setup?.Invoke(shot);
            shot.FaceFlight();
            world.SpawnEntity(shot);
            return shot;
        }

        public string Kind => Code?.EndVariant() ?? "lens";

        /// <summary>
        /// Turns the model to point where it flies (as the game's arrows do): the shape renderer turns the model's +x
        /// by yaw + 90° and tilts it up by roll.
        /// </summary>
        public void FaceFlight()
        {
            double speed = Pos.Motion.Length();
            if (speed < 1e-6) return;
            Pos.Pitch = 0;
            Pos.Yaw = GameMath.PI + (float)Math.Atan2(Pos.Motion.X / speed, Pos.Motion.Z / speed);
            Pos.Roll = -(float)Math.Asin(GameMath.Clamp(-Pos.Motion.Y / speed, -1, 1));
        }

        /// <summary>Explosion radius in blocks (lens).</summary>
        public float Radius
        {
            get => WatchedAttributes.GetFloat("radius", 4);
            set => WatchedAttributes.SetFloat("radius", value);
        }

        /// <summary>Damage dealt to a creature that is hit (fire, arrow).</summary>
        public float Damage
        {
            get => WatchedAttributes.GetFloat("damage", 3);
            set => WatchedAttributes.SetFloat("damage", value);
        }

        /// <summary>Entity id of whoever fired it (not hit by its own shot).</summary>
        public long FiredById
        {
            get => WatchedAttributes.GetLong("firedBy");
            set => WatchedAttributes.SetLong("firedBy", value);
        }

        /// <summary>Player uid credited with what it does (land claims are checked against it).</summary>
        public string FiredByUid
        {
            get => WatchedAttributes.GetString("firedByUid");
            set => WatchedAttributes.SetString("firedByUid", value);
        }

        float age;
        bool done;

        public override bool IsInteractable => false;
        public override bool ApplyGravity => false;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            LightHsv = Kind switch
            {
                "water" => new byte[] { 36, 4, 6 },
                "lava" => new byte[] { 4, 7, 12 },
                "fire" => new byte[] { 6, 7, 12 },
                "arrow" => new byte[] { 10, 2, 8 },
                _ => new byte[] { 8, 7, 14 },
            };
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (!Alive || done || World.Side != EnumAppSide.Server) return;

            age += dt;
            // A shot that was saved with its chunk (it flew into unloaded land) carries its age across the reload,
            // instead of flying its full time again from there.
            if (age < 0.5f && WatchedAttributes.HasAttribute("vintageeAge")) age = Math.Max(age, WatchedAttributes.GetFloat("vintageeAge"));
            WatchedAttributes.SetFloat("vintageeAge", age);
            if (age > MaxLifeSeconds)
            {
                Die(EnumDespawnReason.Expire);
                return;
            }

            if (Kind == "arrow")
            {
                Steer(dt);
                FaceFlight();
            }

            Vec3d from = Pos.XYZ;
            Vec3d to = from.AddCopy(Pos.Motion.X * dt * 60, Pos.Motion.Y * dt * 60, Pos.Motion.Z * dt * 60);

            BlockSelection blockSel = null;
            EntitySelection entitySel = null;
            long firedBy = FiredById;
            float myAge = age;
            World.RayTraceForSelection(from, to, ref blockSel, ref entitySel,
                (pos, block) => block != null && block.Id != 0 && block.CollisionBoxes != null && block.CollisionBoxes.Length > 0,
                e => e.EntityId != EntityId && e.Alive && e.IsInteractable && !(e.EntityId == firedBy && myAge < 0.5f));

            Vec3d blockHit = blockSel == null ? null : blockSel.Position.ToVec3d().Add(blockSel.HitPosition);
            Vec3d entityHit = entitySel == null ? null : (entitySel.Position ?? entitySel.Entity.Pos.XYZ);
            bool entityFirst = entityHit != null && (blockHit == null || entityHit.SquareDistanceTo(from) <= blockHit.SquareDistanceTo(from));

            if (entityFirst)
            {
                done = true;
                OnHitEntity(entitySel.Entity, entityHit);
                Die(EnumDespawnReason.Removed);
                return;
            }
            if (blockHit != null)
            {
                done = true;
                OnHitBlock(blockSel, blockHit);
                Die(EnumDespawnReason.Removed);
                return;
            }

            if ((Kind == "lens" || Kind == "fire") && InWater(to))
            {
                World.PlaySoundAt(new AssetLocation("game:sounds/effect/extinguish"), to.X, to.Y, to.Z, null, true, 16, 0.7f);
                World.SpawnParticles(12, ColorUtil.ToRgba(180, 200, 200, 200), to.AddCopy(-0.3, 0, -0.3), to.AddCopy(0.3, 0.6, 0.3),
                    new Vec3f(-0.3f, 0.4f, -0.3f), new Vec3f(0.3f, 1f, 0.3f), 1.5f, -0.05f, 0.8f);
                Die(EnumDespawnReason.Removed);
                return;
            }

            Pos.SetPos(to);
        }

        bool InWater(Vec3d at)
        {
            Block fluid = World.BlockAccessor.GetBlock(ToBlockPos(at), BlockLayersAccess.Fluid);
            return fluid != null && fluid.IsLiquid() && fluid.LiquidCode == "water";
        }

        BlockPos ToBlockPos(Vec3d at) => new BlockPos((int)Math.Floor(at.X), (int)Math.Floor(at.Y), (int)Math.Floor(at.Z), Pos.Dimension);

        /// <summary>Smite arrows turn towards the nearest hostile creature within 24 blocks.</summary>
        Entity steerTarget;
        float steerSearchIn;

        void Steer(float dt)
        {
            // A volley is ten arrows; searching 24 blocks of entities every tick for each was the mod's dearest loop.
            steerSearchIn -= dt;
            if (steerSearchIn <= 0 || steerTarget == null || !steerTarget.Alive)
            {
                steerSearchIn = 0.2f;
                TrinketEffects fx = Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets;
                if (fx == null) return;
                steerTarget = World.GetNearestEntity(Pos.XYZ, 24, 24, fx.IsHostile);
            }
            Entity target = steerTarget;
            if (target == null || !target.Alive) return;
            double speed = Pos.Motion.Length();
            Vec3d want = target.Pos.XYZ.Add(0, target.SelectionBox.Y2 / 2, 0).Sub(Pos.XYZ).Normalize().Mul(speed);
            double t = Math.Min(1, dt * 6);
            Pos.Motion.Set(
                Pos.Motion.X + (want.X - Pos.Motion.X) * t,
                Pos.Motion.Y + (want.Y - Pos.Motion.Y) * t,
                Pos.Motion.Z + (want.Z - Pos.Motion.Z) * t);
        }

        IPlayer Shooter => string.IsNullOrEmpty(FiredByUid) ? null : World.PlayerByUid(FiredByUid);

        bool MayEdit(BlockPos pos)
        {
            IPlayer player = Shooter;
            return player == null || World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak);
        }

        void OnHitEntity(Entity target, Vec3d at)
        {
            Entity shooter = World.GetEntityById(FiredById);
            switch (Kind)
            {
                case "lens":
                    Explode(at);
                    break;
                case "water":
                case "lava":
                    PlaceLiquid(ToBlockPos(target.Pos.XYZ));
                    break;
                case "fire":
                    target.ReceiveDamage(new DamageSource { Source = shooter is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity, SourceEntity = this, CauseEntity = shooter, Type = EnumDamageType.Fire }, Damage);
                    if (target.Alive)
                    {
                        target.Ignite();
                        target.OnFireBeginTotalMs = World.ElapsedMilliseconds; // burns its full 12 seconds from now
                    }
                    break;
                case "arrow":
                    target.ReceiveDamage(new DamageSource { Source = shooter is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity, SourceEntity = this, CauseEntity = shooter, Type = EnumDamageType.PiercingAttack }, Damage);
                    World.PlaySoundAt(new AssetLocation("game:sounds/arrow-impact"), target, null, true, 24, 0.8f);
                    break;
            }
        }

        void OnHitBlock(BlockSelection sel, Vec3d at)
        {
            switch (Kind)
            {
                case "lens":
                    Explode(at);
                    break;
                case "water":
                case "lava":
                    PlaceLiquid(sel.Position.AddCopy(sel.Face));
                    break;
                case "fire":
                    World.SpawnParticles(10, ColorUtil.ToRgba(255, 60, 160, 255), at.AddCopy(-0.2, 0, -0.2), at.AddCopy(0.2, 0.4, 0.2),
                        new Vec3f(-0.3f, 0.3f, -0.3f), new Vec3f(0.3f, 0.9f, 0.3f), 0.6f, -0.05f, 0.5f);
                    LightFire(sel);
                    break;
            }
        }

        /// <summary>
        /// A fireball sets what it hits alight, as fire does in the game: things that can be lit (firepits, torches, bombs)
        /// are lit, and anything that burns gets the game's fire on the face that was hit. Land claims are respected.
        /// </summary>
        void LightFire(BlockSelection sel)
        {
            if (sel?.Position == null || sel.Face == null || !MayEdit(sel.Position)) return;
            IBlockAccessor ba = World.BlockAccessor;
            Block hit = ba.GetBlock(sel.Position);
            if (hit == null || hit.Id == 0) return;

            // Things with their own lighting: what a torch held to them does.
            var lighter = World.GetEntityById(FiredById) as EntityAgent;
            var ign = hit.GetInterface<Vintagestory.GameContent.IIgnitable>(World, sel.Position);
            if (ign != null && lighter != null)
            {
                try
                {
                    var state = ign.OnTryIgniteBlock(lighter, sel.Position, 10f);
                    if (state == Vintagestory.GameContent.EnumIgniteState.IgniteNow || state == Vintagestory.GameContent.EnumIgniteState.Ignitable)
                    {
                        EnumHandling handling = EnumHandling.PassThrough;
                        ign.OnTryIgniteBlockOver(lighter, sel.Position, 10f, ref handling);
                        return;
                    }
                    if (state == Vintagestory.GameContent.EnumIgniteState.NotIgnitablePreventDefault) return;
                }
                catch (Exception e) { Api.Logger.VerboseDebug("Vintage-E fireball lighting {0}: {1}", hit.Code, e.Message); }
            }

            // Anything that burns: the game's fire in the air next to the face that was hit.
            if (!(hit.CombustibleProps?.BurnDuration > 0)) return;
            BlockPos firePos = sel.Position.AddCopy(sel.Face);
            Block there = ba.GetBlock(firePos);
            if (there == null || (there.Id != 0 && there.Replaceable < 6000) || !MayEdit(firePos)) return;
            Block fire = World.GetBlock(new AssetLocation("game:fire"));
            if (fire == null || fire.Id == 0) return;
            ba.SetBlock(fire.BlockId, firePos);
            BlockEntity be = ba.GetBlockEntity(firePos);
            object burning = null;
            if (be?.Behaviors != null) foreach (var b in be.Behaviors) if (b.GetType().Name == "BEBehaviorBurning") { burning = b; break; }
            var place = burning?.GetType().GetMethod("OnFirePlaced", new[] { typeof(BlockFacing), typeof(string) });
            bool lit = false;
            try
            {
                place?.Invoke(burning, new object[] { sel.Face, FiredByUid });
                lit = burning?.GetType().GetField("IsBurning")?.GetValue(burning) is bool on && on;
            }
            catch (Exception e) { Api.Logger.VerboseDebug("Vintage-E fireball fire: {0}", e.Message); }
            if (!lit) ba.SetBlock(0, firePos); // it didn't catch: no fire block left hanging there
        }

        void Explode(Vec3d at)
        {
            BlockPos pos = ToBlockPos(at);
            if (!MayEdit(pos)) return;
            float radius = Radius;
            ((ICoreServerAPI)Api).World.CreateExplosion(pos, EnumBlastType.RockBlast, radius, radius + 2, 1f, FiredByUid);
        }

        void PlaceLiquid(BlockPos pos)
        {
            if (!MayEdit(pos)) return;
            TrinketEffects.PlaceLiquidAt(World, pos, Kind == "lava");
        }
    }
}
