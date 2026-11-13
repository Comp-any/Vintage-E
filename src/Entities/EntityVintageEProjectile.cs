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
    /// <item><c>snow</c> (Ring of Arcana, Zero): a snowball that knocks back and puts out fire.</item>
    /// </list>
    /// On the way, a water shot turns lava within 3 blocks into obsidian (a source) or basalt cobblestone, and a lava
    /// shot dries up water within 3 blocks. Shot into the sky (past the top of the world), water brings rain and lava
    /// stops it, as in ProjectE.
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

        /// <summary>
        /// A shot from a block (a pedestal's arrows): from <paramref name="from"/> towards <paramref name="dir"/>,
        /// moving <paramref name="blocksPerSecond"/>. Nobody fired it, so it acts with no player's rights.
        /// </summary>
        public static EntityVintageEProjectile ShootFrom(IWorldAccessor world, Vec3d from, Vec3d dir, string kind, double blocksPerSecond, Action<EntityVintageEProjectile> setup = null)
        {
            EntityProperties type = world.GetEntityType(new AssetLocation("vintagee", "projectile-" + kind));
            if (type == null || dir == null || dir.Length() < 1e-6) return null;
            var shot = (EntityVintageEProjectile)world.ClassRegistry.CreateEntity(type);
            Vec3d d = dir.Clone().Normalize();
            double perFrame = blocksPerSecond / 60.0;
            shot.Pos.SetPos(from);
            shot.Pos.Motion.Set(d.X * perFrame, d.Y * perFrame, d.Z * perFrame);
            setup?.Invoke(shot);
            shot.FaceFlight();
            world.SpawnEntity(shot);
            return shot;
        }

        /// <summary>What the last water or lava shot did at its end (for the self-test's report).</summary>
        public static string LastEnd;

        string kind;

        /// <summary>lens, water, lava, fire or arrow: the end of the entity's code (read once, not several times a tick).</summary>
        public string Kind => kind ?? (Code == null ? "lens" : kind = Code.EndVariant() ?? "lens");

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
        bool done, ageRestored;
        /// <summary>The key the shot's age is saved under, so one that was saved with its chunk doesn't fly its full time again.</summary>
        const string AgeKey = "vintageeAge";

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
                "snow" => new byte[] { 36, 0, 6 },
                _ => new byte[] { 8, 7, 14 },
            };
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (!Alive || done || World.Side != EnumAppSide.Server) return;

            age += dt;
            // A shot that was saved with its chunk (it flew into unloaded land) carries its age across the reload,
            // instead of flying its full time again from there. The age is kept in the entity's own attributes, which
            // are saved but sent to nobody: until 1.21.1 it sat in the watched ones, so every shot in the air sent
            // every player nearby an update on every tick (a smite volley is ten of them).
            if (!ageRestored)
            {
                ageRestored = true;
                age = Math.Max(age, Math.Max(Attributes.GetFloat(AgeKey), WatchedAttributes.GetFloat(AgeKey)));
            }
            Attributes.SetFloat(AgeKey, age);
            if (age > MaxLifeSeconds)
            {
                if (Kind == "water" || Kind == "lava") LastEnd = $"{Kind} shot ran out of time at y {Pos.Y:0}";
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

            // ProjectE: water shots quench lava and lava shots boil water away on the way past (3 blocks around).
            if (Kind == "water" || Kind == "lava")
            {
                if (Kind == "water") QuenchLava(from); else DryWater(from);
                // Into the sky (above the world's top): water brings rain, lava clears it.
                if (to.Y >= World.BlockAccessor.MapSizeY - 1)
                {
                    done = true;
                    bool ok = WeatherControl.SetRain((ICoreServerAPI)Api, from, Kind == "water");
                    LastEnd = $"{Kind} shot reached the sky at y {from.Y:0} (rain {(Kind == "water" ? "on" : "off")}: {ok})";
                    Die(EnumDespawnReason.Removed);
                    return;
                }
            }

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
                if (Kind == "water" || Kind == "lava") LastEnd = $"{Kind} shot hit {World.BlockAccessor.GetBlock(blockSel.Position)?.Code} at y {blockSel.Position.Y}";
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
                TrinketEffects fx = VintageEModSystem.Of(Api)?.Trinkets;
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
                case "snow":
                    // A snowball: no harm, a push, and the fire on it is out.
                    if (target.IsOnFire) target.IsOnFire = false;
                    double speed = Math.Max(1e-6, Pos.Motion.Length());
                    target.Pos.Motion.Add(Pos.Motion.X / speed * 0.15, 0.08, Pos.Motion.Z / speed * 0.15);
                    SnowPuff(at);
                    break;
            }
        }

        void SnowPuff(Vec3d at)
        {
            World.SpawnParticles(10, ColorUtil.ToRgba(230, 240, 245, 255), at.AddCopy(-0.2, -0.2, -0.2), at.AddCopy(0.2, 0.2, 0.2),
                new Vec3f(-0.6f, 0.2f, -0.6f), new Vec3f(0.6f, 0.8f, 0.6f), 0.8f, 0.6f, 0.6f);
            World.PlaySoundAt(new AssetLocation("game:sounds/environment/smallsplash"), at.X, at.Y, at.Z, null, true, 16, 0.3f);
        }

        Block obsidian, basaltCobble;

        /// <summary>ProjectE's water shot: lava within 3 blocks becomes obsidian (a source) or cobblestone (flowing lava).</summary>
        void QuenchLava(Vec3d at)
        {
            IBlockAccessor ba = World.BlockAccessor;
            BlockPos c = ToBlockPos(at);
            BlockPos pos = c.Copy();
            bool any = false;
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        pos.Set(c.X + dx, c.Y + dy, c.Z + dz);
                        Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (fluid == null || fluid.Id == 0 || fluid.LiquidCode != "lava") continue;
                        if (!MayEdit(pos)) continue;
                        bool source = fluid.LiquidLevel >= 7 && fluid.Code?.Path?.Contains("still") == true;
                        Block to = source ? (obsidian ??= World.GetBlock(new AssetLocation("game:rock-obsidian"))) : (basaltCobble ??= World.GetBlock(new AssetLocation("game:cobblestone-basalt")));
                        BlockPos p = pos.Copy();
                        ba.SetBlock(0, p, BlockLayersAccess.Fluid);
                        if (to != null && to.Id != 0 && ba.GetBlock(p, BlockLayersAccess.Solid).Id == 0) ba.SetBlock(to.BlockId, p);
                        ba.TriggerNeighbourBlockUpdate(p);
                        any = true;
                    }
            if (any) World.PlaySoundAt(new AssetLocation("game:sounds/effect/extinguish"), at.X, at.Y, at.Z, null, true, 16, 0.6f);
        }

        /// <summary>ProjectE's lava shot: water within 3 blocks is gone.</summary>
        void DryWater(Vec3d at)
        {
            IBlockAccessor ba = World.BlockAccessor;
            BlockPos c = ToBlockPos(at);
            BlockPos pos = c.Copy();
            bool any = false;
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        pos.Set(c.X + dx, c.Y + dy, c.Z + dz);
                        Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (fluid == null || fluid.Id == 0 || fluid.LiquidCode == null || !fluid.LiquidCode.Contains("water")) continue;
                        if (!MayEdit(pos)) continue;
                        BlockPos p = pos.Copy();
                        ba.SetBlock(0, p, BlockLayersAccess.Fluid);
                        ba.TriggerNeighbourBlockUpdate(p);
                        any = true;
                    }
            if (any) World.PlaySoundAt(new AssetLocation("game:sounds/effect/extinguish"), at.X, at.Y, at.Z, null, true, 16, 0.5f);
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
                case "snow":
                    // Puts out a fire where it lands.
                    BlockPos firePos = sel.Position.AddCopy(sel.Face);
                    FireUtil.PutOut(World, firePos, firePos, MayEdit);
                    FireUtil.PutOut(World, sel.Position, sel.Position, MayEdit);
                    SnowPuff(at);
                    break;
            }
        }

        /// <summary>
        /// A fireball sets what it hits alight, as fire does in the game: things that can be lit (firepits, torches, bombs)
        /// are lit, and anything that burns gets the game's fire on the face that was hit. Land claims are respected.
        /// </summary>
        void LightFire(BlockSelection sel)
        {
            FireUtil.TryLight(World, sel, World.GetEntityById(FiredById) as EntityAgent, FiredByUid, MayEdit);
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
