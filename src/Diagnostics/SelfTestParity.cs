using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// 1.24.0: the ProjectE parity batch (rings and amulets, gadgets, tool actions, collector fuel, relay burning,
    /// pedestals, weather from shots). Everything that can run without a player in the world is run for real next to
    /// the spawn, in the corner from x+14, z+8 to x+31, z+31 that no other check uses.
    /// </summary>
    public partial class SelfTest
    {
        /// <summary>
        /// Clears a w × w × h box above the ground at (x+dx, z+dz) (or <paramref name="lift"/> blocks above it, in the air,
        /// for what could set the land around alight) and floors it with granite. Returns the box's first air layer corner.
        /// </summary>
        BlockPos Pad(int x, int z, int dx, int dz, int w, int h, int lift = 0)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            int gy = ba.GetTerrainMapheightAt(new BlockPos(x + dx + w / 2, 0, z + dz + w / 2)) + 1 + lift;
            Block granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            var pos = new BlockPos(0);
            for (int i = -1; i <= w; i++)
                for (int j = -1; j <= w; j++)
                {
                    pos.Set(x + dx + i, gy - 1, z + dz + j);
                    if (granite != null) ba.SetBlock(granite.Id, pos);
                    for (int k = 0; k < h; k++)
                    {
                        pos.Set(x + dx + i, gy + k, z + dz + j);
                        ba.SetBlock(0, pos);
                        ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                    }
                }
            return new BlockPos(x + dx, gy, z + dz);
        }

        Entity SpawnCreature(string pathStart, Vec3d at)
        {
            EntityProperties type = sapi.World.EntityTypes.FirstOrDefault(t => t?.Code != null && t.Code.Domain == "game" && t.Code.Path.StartsWith(pathStart, StringComparison.Ordinal));
            if (type == null) return null;
            Entity e = sapi.World.ClassRegistry.CreateEntity(type);
            e.Code = type.Code;
            e.Pos.SetPos(at);
            sapi.World.SpawnEntity(e);
            return e;
        }

        void CheckParity(int x, int y, int z)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            try { CheckFixes1241(x, y, z); }
            catch (Exception e) { Check(false, "1.24.1 fix checks threw " + e); }
            Line("ProjectE parity (1.24.0)");

            // --- Evertide Amulet: an endless water container.
            Item amulet = sapi.World.GetItem(new AssetLocation("vintagee:trinket-evertideamulet"));
            if (amulet is ItemEvertideAmulet ev)
            {
                var stack = new ItemStack(amulet);
                ItemStack water = ev.GetContent(stack);
                ItemStack taken = ev.TryTakeContent(stack, 250);
                Check(ev.AllowHeldLiquidTransfer && water?.Collectible?.Code?.Path == "waterportion" && water.StackSize == 1000 && taken?.StackSize == 250 && ev.GetCurrentLitres(stack) == 10,
                    $"the Evertide Amulet holds 10 L of water that never runs out ({water?.Collectible?.Code} x{water?.StackSize}, took {taken?.StackSize})");
                BlockPos bp = Pad(x, z, 14, 8, 2, 3);
                Block barrel = sapi.World.GetBlock(new AssetLocation("game:barrel"));
                if (barrel is BlockLiquidContainerBase lc)
                {
                    ba.SetBlock(barrel.Id, bp);
                    int moved = lc.TryPutLiquid(bp, ev.GetContent(stack), ev.CapacityLitres);
                    Check(moved > 0 && Math.Abs(lc.GetCurrentLitres(bp) - 10) < 0.01, $"the Evertide Amulet fills a barrel with a bucket of water ({moved} portions, {lc.GetCurrentLitres(bp):0.##} L)");
                }
                else Check(false, $"game:barrel is a liquid container ({barrel?.GetType().Name})");
            }
            else Check(false, $"the Evertide Amulet is ItemEvertideAmulet ({amulet?.GetType().Name})");
            Check(sapi.World.GetItem(new AssetLocation("vintagee:trinket-volcaniteamulet"))?.GetType() == typeof(ItemEmcTrinket), "the Volcanite Amulet stays a plain trinket (no lava portion to hold)");

            // --- Ring of Arcana: its shots take turns.
            Item arcana = sapi.World.GetItem(new AssetLocation("vintagee:trinket-ringofarcana"));
            if (arcana != null)
            {
                var ring = new ItemStack(arcana);
                int first = ItemEmcTrinket.NextArcanaShot(ring);
                ring.Attributes.SetInt(ItemEmcTrinket.ArcanaOffKey, 1); // Zero off
                int noZero = ItemEmcTrinket.NextArcanaShot(ring);
                ring.Attributes.SetInt(ItemEmcTrinket.ArcanaOffKey, 1 | 2 | 8); // only Harvest on: no shot
                int none = ItemEmcTrinket.NextArcanaShot(ring);
                Check(first == 0 && noZero == 1 && none == -1, $"the Ring of Arcana's shots follow its switched-on effects (all: {first}, no Zero: {noZero}, only Harvest: {none})");
            }
            Check(sapi.World.GetEntityType(new AssetLocation("vintagee:projectile-snow")) != null, "the Ring of Arcana's snowball has its entity");

            // --- Fire: a firestarter's lighting, putting fire out, the Arcana's fire wall.
            {
                BlockPos fp = Pad(x, z, 14, 12, 3, 4, lift: 40); // in the air: the fire wall reaches 30 blocks
                Block log = sapi.World.GetBlock(new AssetLocation("game:log-placed-oak-ud"));
                if (log != null)
                {
                    ba.SetBlock(log.Id, fp);
                    bool lit = FireUtil.TryLight(sapi.World, new BlockSelection { Position = fp.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) }, null, null, _ => true);
                    Block above = ba.GetBlock(fp.UpCopy());
                    Check(lit && above?.Code?.Path == "fire", $"lighting a log like a firestarter puts the game's fire on it ({lit}, {above?.Code})");
                    int out1 = FireUtil.PutOut(sapi.World, fp.AddCopy(-1, 0, -1), fp.AddCopy(1, 2, 1), _ => true);
                    Check(out1 == 1 && ba.GetBlock(fp.UpCopy()).Id == 0, $"an Ignition Ring that is off puts the fire out ({out1})");
                    // The fire wall, around a spot 2 blocks from the log: it catches.
                    int wall = mod.Trinkets?.FireWallAt(fp.ToVec3d().Add(2.5, 0.5, 0.5), true, null, null) ?? 0;
                    Check(wall >= 1 && ba.GetBlock(fp.UpCopy())?.Code?.Path == "fire", $"the Ring of Arcana's fire wall sets burnable blocks in it alight ({wall}, {ba.GetBlock(fp.UpCopy())?.Code})");
                    FireUtil.PutOut(sapi.World, fp.AddCopy(-1, 0, -1), fp.AddCopy(1, 2, 1), _ => true);
                    ba.SetBlock(0, fp);
                }
                else Check(false, "game:log-placed-oak-ud exists");
            }

            // --- Philosopher's Stone shapes.
            {
                BlockPos c = new BlockPos(0, 100, 0);
                int cube = ItemPhilosophersStone.Positions(c, BlockFacing.UP, 0, 2, BlockFacing.NORTH).Count();
                int panel = ItemPhilosophersStone.Positions(c, BlockFacing.UP, 1, 4, BlockFacing.NORTH).Count();
                var line = ItemPhilosophersStone.Positions(c, BlockFacing.UP, 2, 3, BlockFacing.EAST).ToList();
                Check(cube == 125 && panel == 81 && line.Count == 7 && line.All(p => p.Y == 100 && p.Z == 0),
                    $"Philosopher's Stone shapes: a 5-wide cube is 125 blocks ({cube}), a 9-wide panel 81 ({panel}), a 7-long line runs where you face ({line.Count})");
                BlockPos sp = Pad(x, z, 19, 8, 3, 4);
                Block granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
                Block target = mod.WorldTransmutation?.GetTarget(granite, false);
                if (granite != null && target != null)
                {
                    BlockPos mid = sp.AddCopy(1, 1, 1);
                    foreach (BlockPos p in ItemPhilosophersStone.Positions(mid, BlockFacing.UP, 0, 1, BlockFacing.NORTH)) ba.SetBlock(granite.Id, p);
                    int changed = ItemPhilosophersStone.Transmute(sapi.World, mod, null, new BlockSelection { Position = mid, Face = BlockFacing.UP }, false, 0, 1, BlockFacing.NORTH);
                    int nowTarget = ItemPhilosophersStone.Positions(mid, BlockFacing.UP, 0, 1, BlockFacing.NORTH).Count(p => ba.GetBlock(p).Id == target.Id);
                    Check(changed == 27 && nowTarget == 27, $"the Philosopher's Stone transmutes a 3x3x3 cube of granite ({changed} changed into {target.Code})");
                    foreach (BlockPos p in ItemPhilosophersStone.Positions(mid, BlockFacing.UP, 0, 1, BlockFacing.NORTH)) ba.SetBlock(0, p);
                }
                else Check(false, $"granite has a transmutation ({target?.Code})");
            }

            // --- Gadgets: lens radius, divining depths, the eye's and smite's modes.
            Check(ItemEmcGadget.LensRadius.SequenceEqual(new float[] { 4, 8, 12, 16 }), "the Hyperkinetic Lens explodes radius 4/8/12/16, as ProjectE's");
            {
                var rod = new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:gadget-diviningrodhigh")));
                int deep = ItemEmcGadget.DiviningDepth("diviningrodhigh", rod);
                rod.Attributes.SetInt(ItemEmcTrinket.ModeKey, 0);
                int shallow = ItemEmcGadget.DiviningDepth("diviningrodhigh", rod);
                Check(deep == 128 && shallow == 16 && ItemEmcGadget.DiviningDepths("diviningrodmedium").SequenceEqual(new[] { 16, 32 }),
                    $"the better divining rods can look less deep ({deep} by default, {shallow} picked)");
                var groups = new ModeGroups(ItemEmcGadget.EyeModes.Length, ItemEmcGadget.EyeMaxRadius + 1);
                var eye = new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:gadget-mercurialeye")));
                Check(ItemEmcGadget.EyeMode(eye) == ItemEmcGadget.EyeTransmutation && groups.Split(5) == (1, 0) && groups.Split(4) == (0, 4) && groups.Split(10) == (-1, -1),
                    "the Mercurial Eye starts in transmutation (as before) and its tool modes are its five modes, then its five sizes");
                var smite = new DummySlot(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:gadget-archangelssmite"))));
                smite.Itemstack.Collectible.SetToolMode(smite, null, null, 1);
                bool on = ItemEmcTrinket.IsActive(smite.Itemstack);
                smite.Itemstack.Collectible.SetToolMode(smite, null, null, 0);
                Check(on && !ItemEmcTrinket.IsActive(smite.Itemstack), "Archangel's Smite switches on and off in its tool modes");
            }

            // --- Charge key: lenses, stone, eye and matter tools step their charge.
            {
                var lens = new DummySlot(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:gadget-hyperkineticlens"))));
                var stone = new DummySlot(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:philosophersstone"))));
                Item pick = sapi.World.GetItem(new AssetLocation("vintagee:mattertool-red-pickaxe"));
                var pickSlot = new DummySlot(new ItemStack(pick));
                var lensC = lens.Itemstack?.Collectible as IChargeableItem;
                var stoneC = stone.Itemstack?.Collectible as IChargeableItem;
                var pickC = pick?.CollectibleBehaviors.OfType<IChargeableItem>().FirstOrDefault();
                lensC?.SetCharge(lens, 9);
                stoneC?.SetCharge(stone, 3);
                pickC?.SetCharge(pickSlot, 2);
                Check(lensC?.GetCharge(lens) == 3 && stoneC?.GetCharge(stone) == 3 && stoneC?.MaxCharge(stone) == 4 && pickC?.GetCharge(pickSlot) == 2 && pickC?.MaxCharge(pickSlot) == 2,
                    $"the charge key's items keep their charge in range (lens {lensC?.GetCharge(lens)}, stone {stoneC?.GetCharge(stone)}/{stoneC?.MaxCharge(stone)}, red pickaxe {pickC?.GetCharge(pickSlot)}/{pickC?.MaxCharge(pickSlot)})");
                Check(sapi.World.GetItem(new AssetLocation("vintagee:philosophersstone")) is IExtraFunctionItem
                    && sapi.World.GetItem(new AssetLocation("vintagee:mattertool-dark-sword"))?.CollectibleBehaviors.OfType<IExtraFunctionItem>().Any() == true
                    && arcana is IExtraFunctionItem, "the extra-function key reaches the stone, the swords and the rings");
            }

            // --- Tool actions: vein mining, digging, clearing logs.
            {
                Item pick = sapi.World.GetItem(new AssetLocation("vintagee:mattertool-dark-pickaxe"));
                Item shovel = sapi.World.GetItem(new AssetLocation("vintagee:mattertool-dark-shovel"));
                Block ore = sapi.World.GetBlock(new AssetLocation("game:ore-medium-nativecopper-granite"));
                Block granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
                BlockPos vp = Pad(x, z, 24, 8, 5, 5);
                if (pick != null && ore != null && granite != null)
                {
                    // Five ore blocks joined corner to corner, one apart from them, in granite.
                    BlockPos o = vp.AddCopy(1, 1, 1);
                    var vein = new[] { o, o.AddCopy(1, 0, 0), o.AddCopy(1, 1, 0), o.AddCopy(2, 2, 1), o.AddCopy(2, 2, 2) };
                    BlockPos apart = o.AddCopy(0, 3, 3);
                    foreach (BlockPos p in vein) ba.SetBlock(ore.Id, p);
                    ba.SetBlock(ore.Id, apart);
                    int mined = AreaTools.VeinMine(sapi.World, null, pick, o, BlockFacing.UP, 0);
                    Check(mined == 5 && vein.All(p => ba.GetBlock(p).Id == 0) && ba.GetBlock(apart).Id == ore.Id,
                        $"the matter pickaxe mines a whole ore vein and nothing apart from it ({mined} of 5)");
                    ba.SetBlock(0, apart);
                }
                else Check(false, "dark pickaxe, native copper ore and granite exist");
                Block soil = sapi.World.GetBlock(new AssetLocation("game:soil-medium-none"));
                if (shovel != null && soil != null)
                {
                    BlockPos d = vp.AddCopy(2, 0, 2);
                    foreach (BlockPos p in AreaToolModes.Plane(d, BlockFacing.UP, 1)) ba.SetBlock(soil.Id, p);
                    int dug0 = AreaTools.DigFlat(sapi.World, null, shovel, d, 0);
                    int dug = AreaTools.DigFlat(sapi.World, null, shovel, d, 1);
                    Check(dug0 == 0 && dug == 9, $"the matter shovel digs a flat 3x3 (none at 1x1, as ProjectE's at charge 0): {dug0}, {dug}");
                }
                else Check(false, "dark shovel and soil exist");
                Block log = sapi.World.GetBlock(new AssetLocation("game:log-placed-oak-ud"));
                BlockPos lp = Pad(x, z, 24, 16, 5, 4, lift: 40); // in the air: trees nearby would be cleared too
                if (log != null)
                {
                    var logs = new[] { lp.AddCopy(1, 0, 1), lp.AddCopy(1, 1, 1), lp.AddCopy(3, 0, 3), lp.AddCopy(4, 2, 0) };
                    foreach (BlockPos p in logs) ba.SetBlock(log.Id, p);
                    Entity marker = sapi.World.SpawnItemEntity(new ItemStack(sapi.World.GetItem(new AssetLocation("game:stick"))), lp.ToVec3d().Add(2.5, 0.2, 2.5));
                    int none = AreaTools.ClearAround(sapi.World, null, marker, AreaTools.IsLog, 0);
                    int cleared = AreaTools.ClearAround(sapi.World, null, marker, AreaTools.IsLog, 1);
                    Check(none == 0 && cleared == 4 && logs.All(p => ba.GetBlock(p).Id == 0), $"the matter axe clears the logs around (none at 1x1): {none}, {cleared} of 4");
                }
                Check(AreaTools.IsOre(ore) && AreaTools.IsShovelVein(sapi.World.GetBlock(new AssetLocation("game:gravel-granite"))) && AreaTools.IsLeaves(sapi.World.GetBlock(new AssetLocation("game:leaves-placed-oak"))),
                    "ores, gravel and leaves are recognised for the tool actions");
            }

            // --- Shears: the creature list.
            if (mod.Trinkets != null)
            {
                var before = mod.Config.ShearableCreatures;
                mod.Config.ShearableCreatures = new Dictionary<string, string> { ["game:chicken-*"] = "game:feather*2" };
                string drop = mod.Trinkets.ShearDropFor(new AssetLocation("game:chicken-rooster"));
                ItemStack parsed = drop == null ? null : mod.Trinkets.ParseDrop(drop);
                string none = mod.Trinkets.ShearDropFor(new AssetLocation("game:wolf-male"));
                mod.Config.ShearableCreatures = before;
                Check(parsed?.StackSize == 2 && none == null, $"ShearableCreatures names what shearing a creature gives ({drop} -> {parsed?.Collectible?.Code} x{parsed?.StackSize})");
            }

            // Creatures a mod made shearable (ShearLib): named, so a run with such mods shows what the shears will meet.
            var shearable = sapi.World.EntityTypes.Where(t => t?.Server?.BehaviorsAsJsonObj != null && t.Server.BehaviorsAsJsonObj.Any(b => (b?["code"].AsString("") ?? "").IndexOf("shear", StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(t => t.Code.ToString()).Take(8).ToList();
            Line("  creatures a mod made shearable: " + (shearable.Count == 0 ? "none" : string.Join(", ", shearable)));
            if (shearable.Count > 0)
            {
                // What that mod's shearing looks like from the code (its behaviour's own methods), for checking how the
                // shears reach it: they call the creature's OnInteract as a player's click would.
                try
                {
                    EntityProperties t = sapi.World.GetEntityType(new AssetLocation(shearable[0]));
                    Entity sheep = sapi.World.ClassRegistry.CreateEntity(t);
                    sheep.Code = t.Code;
                    sheep.Pos.SetPos(new Vec3d(x + 0.5, y + 60, z + 0.5));
                    sapi.World.SpawnEntity(sheep);
                    foreach (var b in sheep.SidedProperties.Behaviors.Where(TrinketEffects.IsShearBehavior))
                    {
                        var methods = b.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                            .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(pr => pr.ParameterType.Name)) + ")").Distinct();
                        Line($"  {b.GetType().FullName} ({b.PropertyName()}): {string.Join(", ", methods)}");
                    }
                    Check(TrinketEffects.HasShearBehavior(sheep), $"the shears see {sheep.Code} as shearable");
                    sheep.Die(EnumDespawnReason.Removed);
                }
                catch (Exception e) { Line("  could not look at a shearable creature: " + e.Message); }
            }

            // --- Collector fuel and relay burning.
            {
                BlockPos mp = Pad(x, z, 14, 20, 4, 2);
                var collector = Place<BEEnergyCollector>(mp.Copy(), "vintagee:energycollector-mk1");
                Item charcoal = sapi.World.GetItem(new AssetLocation("game:charcoal"));
                var chain = FuelUpgrader.Chain(mod, sapi.World);
                Line("  collector fuels by value: " + string.Join(", ", chain.Select(c => c.Code.Path + "=" + EmcFormat.Format(mod.Emc.GetValue(c)))));
                if (collector != null && charcoal != null && chain.Count >= 2)
                {
                    collector.Fuel.Input = new ItemStack(charcoal, 4);
                    double spent = collector.Fuel.Upgrade(mod, sapi.World, 1e12);
                    ItemStack top = collector.Fuel.Output;
                    Check(collector.Fuel.Input == null && top?.Collectible == chain[chain.Count - 1] && top.StackSize == 4 && spent > 0,
                        $"the collector upgrades fuel to the best fuel ({top?.Collectible?.Code} x{top?.StackSize} for {EmcFormat.Format(spent)} EMC)");
                    collector.Fuel.Output = null;
                    collector.Fuel.Lock = chain[1].Code.ToString();
                    collector.Fuel.Input = new ItemStack(charcoal, 2);
                    collector.Fuel.Upgrade(mod, sapi.World, 1e12);
                    Check(collector.Fuel.Output?.Collectible == chain[1], $"a locked collector stops at its lock ({collector.Fuel.Output?.Collectible?.Code})");
                    collector.Fuel.Output = null;
                    collector.Fuel.Input = new ItemStack(charcoal, 1);
                    collector.Fuel.Lock = null;
                    double few = collector.Fuel.Upgrade(mod, sapi.World, 0.5);
                    Check(few == 0 && collector.Fuel.Input?.StackSize == 1, "a collector without the EMC for it waits");
                    collector.Fuel.Input = null;
                }
                else Check(false, $"collector, charcoal and two fuels with values ({chain.Count})");
                var relay = Place<BEAntimatterRelay>(mp.AddCopy(2, 0, 0), "vintagee:antimatterrelay-mk1");
                Item stick = sapi.World.GetItem(new AssetLocation("game:stick"));
                if (relay != null && stick != null)
                {
                    double each = mod.Emc.GetValue(new ItemStack(stick));
                    relay.Stored = 0;
                    relay.Burn = new ItemStack(stick, 5);
                    int burned = relay.BurnSome(AntimatterBurn);
                    Check(burned == 5 && relay.Burn == null && Math.Abs(relay.Stored - 5 * each) < 1e-6, $"the relay burns what's in its burn slot into its store ({burned}, {EmcFormat.Format(relay.Stored)} EMC)");
                }
            }

            // --- Pedestals: Swiftwolf's lightning on creatures, the smite's arrows at hostile ones.
            {
                BlockPos pp = Pad(x, z, 26, 26, 4, 3, lift: 24); // in the air: lightning can start fires
                var pedestal = Place<BEPedestal>(pp.AddCopy(1, 0, 1), "vintagee:pedestal-empty");
                Entity hen = SpawnCreature("chicken-", pp.ToVec3d().Add(3.5, 0.1, 3.5));
                Entity wolf = SpawnCreature("wolf-", pp.ToVec3d().Add(0.5, 0.1, 3.5));
                if (pedestal != null && mod.Trinkets != null)
                {
                    Vec3d center = pedestal.Pos.ToVec3d().Add(0.5, 0.5, 0.5);
                    int struck = pedestal.StrikeAround(mod.Trinkets, center);
                    int arrows = pedestal.SmiteVolley(mod.Trinkets, center);
                    Check(struck >= 1, $"Swiftwolf on a pedestal calls lightning on the creatures around ({struck})");
                    Check(wolf == null || arrows == 3, $"Archangel's Smite on a pedestal fires 3 arrows at a hostile creature ({arrows})");
                    Check(BEPedestal.IsPedestalItem(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:gadget-archangelssmite")))), "a pedestal takes the Archangel's Smite");
                }
                hen?.Die(EnumDespawnReason.Removed);
                wolf?.Die(EnumDespawnReason.Removed);
            }

            // --- Weather from shots, a water shot quenching lava and a lava shot drying water: run after the furnace checks
            // (rain puts firepits out), and the report is written when they're done.
            weatherShots = done =>
            {
                try
                {
                    BlockPos wp = Pad(x, z, 14, 24, 7, 4); // nothing lifted above it: the shots go up into the sky
                    Block lava = sapi.World.GetBlock(new AssetLocation("game:lava-still-7"));
                    Block water = sapi.World.GetBlock(new AssetLocation("game:water-still-7"));
                    BlockPos lavaPos = wp.AddCopy(3, 0, 3), waterPos = wp.AddCopy(6, 0, 6);
                    if (lava == null || water == null) { Check(false, "lava and water blocks exist"); done(); return; }
                    ba.SetBlock(lava.Id, lavaPos, BlockLayersAccess.Fluid);
                    // Open sky above both shots (a mountain or tree overhead would catch them).
                    foreach (BlockPos col in new[] { wp.AddCopy(1, 0, 3), wp.AddCopy(4, 0, 6) })
                        for (int cy = col.Y; cy < ba.MapSizeY; cy++) { var cp = new BlockPos(col.X, cy, col.Z); ba.SetBlock(0, cp); ba.SetBlock(0, cp, BlockLayersAccess.Fluid); }
                    var up = new Vec3d(0, 1, 0);
                    Line($"  precipitation before the shots: {WeatherControl.Precipitation(sapi, wp.ToVec3d()):0.##}");
                    var shot = EntityVintageEProjectile.ShootFrom(sapi.World, wp.ToVec3d().Add(1.5, 0.5, 3.5), up, "water", 40);
                    sapi.Event.RegisterCallback(__ =>
                    {
                        try
                        {
                            Block now = ba.GetBlock(lavaPos);
                            Check(now?.Code?.Path == "rock-obsidian", $"a water shot turns lava it passes into obsidian ({now?.Code})");
                            Line($"  {EntityVintageEProjectile.LastEnd ?? "the water shot left no word"} (world top {ba.MapSizeY})");
                            Vec3d waterUp = wp.ToVec3d().Add(1.5, 0.5, 3.5), lavaUp = wp.ToVec3d().Add(4.5, 0.5, 6.5);
                            bool rain = WeatherControl.IsRaining(sapi, waterUp);
                            Check(shot == null || !shot.Alive, "the water shot went into the sky");
                            Check(rain, $"a water shot into the sky brings rain ({WeatherControl.Precipitation(sapi, waterUp):0.##} where it went up)");
                            ba.SetBlock(water.Id, waterPos, BlockLayersAccess.Fluid);
                            var lavaShot = EntityVintageEProjectile.ShootFrom(sapi.World, lavaUp, up, "lava", 40);
                            // Right after it passed (water around may flow back in later: that's the world, not the shot).
                            sapi.Event.RegisterCallback(____ =>
                            {
                                Block fluid = ba.GetBlock(waterPos, BlockLayersAccess.Fluid);
                                Check(fluid == null || fluid.Id == 0 || fluid.LiquidCode != "water", $"a lava shot dries up water it passes ({fluid?.Code})");
                            }, 400);
                            sapi.Event.RegisterCallback(___ =>
                            {
                                try
                                {
                                    Line($"  {EntityVintageEProjectile.LastEnd ?? "the lava shot left no word"}");
                                    bool dry = !WeatherControl.IsRaining(sapi, lavaUp);
                                    Check(dry, $"a lava shot into the sky stops the rain ({WeatherControl.Precipitation(sapi, lavaUp):0.##} where it went up)");
                                    bool storm = WeatherControl.StartThunderstorm(sapi, wp.ToVec3d());
                                    Check(storm, $"Swiftwolf's shot into the rain can bring a thunderstorm (thundering there now: {WeatherControl.IsThundering(sapi, wp.ToVec3d())})");
                                    WeatherControl.SetRain(sapi, wp.ToVec3d(), false);
                                }
                                catch (Exception e) { Check(false, "lava shot check threw " + e); }
                                done();
                            }, 7000);
                        }
                        catch (Exception e) { Check(false, "water shot check threw " + e); done(); }
                    }, 7000);
                }
                catch (Exception e) { Check(false, "weather shots threw " + e); done(); }
            };
        }

        /// <summary>The weather shots (set up by <see cref="CheckParity"/>), then <paramref name="done"/>.</summary>
        Action<Action> weatherShots;

        void AfterFurnaces(Action done)
        {
            if (weatherShots == null) { done(); return; }
            try { weatherShots(done); }
            catch (Exception e) { Check(false, "weather shots threw " + e); done(); }
        }

        const int AntimatterBurn = BEAntimatterRelay.BurnPerSecond;

        /// <summary>1.24.1: the bug-hunt fixes that can be checked without a player in the world.</summary>
        void CheckFixes1241(int x, int y, int z)
        {
            Line("Bug-hunt fixes (1.24.1)");

            // Settings players' games need: a multiplier of 0 (left out of the packet until now), the table's range
            // and the collector fuels.
            var sent = new EmcTablePacket { TrinketCostMultiplier = 0, TableUseRange = 5, CollectorFuels = new[] { "game:charcoal" } };
            EmcTablePacket got;
            using (var ms = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, sent);
                ms.Position = 0;
                got = ProtoBuf.Serializer.Deserialize<EmcTablePacket>(ms);
            }
            Check(got != null && got.TrinketCostMultiplier == 0 && got.TableUseRange == 5 && got.CollectorFuels?.Length == 1,
                $"a TrinketCostMultiplier of 0, TableUseRange and CollectorFuels reach clients ({got?.TrinketCostMultiplier}, {got?.TableUseRange}, {got?.CollectorFuels?.Length} fuel)");
            EmcTablePacket live = mod.Emc.BuildPacket();
            Check(live.TableUseRange == mod.Config.TableUseRange && live.CollectorFuels != null,
                $"the table sent to clients carries the server's TableUseRange and CollectorFuels ({live.TableUseRange}, {live.CollectorFuels?.Length} fuels)");

            // Clothes from a condenser come out new.
            CollectibleObject warm = sapi.World.Collectibles.FirstOrDefault(c => c?.Code != null && c.Attributes != null && c.Attributes["warmth"].AsFloat(0) > 0);
            if (warm != null)
            {
                ItemStack made = BEEnergyCondenser.CopyOf(mod, new ItemStack(warm));
                float condition = made.Attributes.GetFloat("condition", -1);
                Check(condition == 1, $"a condenser's copy of warm clothing ({warm.Code}) comes out at full condition ({condition})");
            }
            else Check(false, "found warm clothing for the condenser to copy");

            // The fire block is looked up in this world.
            Block fire = FireUtil.Fire(sapi.World);
            Check(fire != null && fire.Id != 0 && fire.Code?.Path == "fire", $"the game's fire block is this world's ({fire?.Code}, id {fire?.Id})");

            // A blast needs every claim in reach to allow it: a claim 6 blocks off stops a radius-11 blast nobody
            // answers for, not a radius-3 one.
            var here = new BlockPos(x + 22, y, z + 22);
            var claim = new LandClaim { OwnedByPlayerUid = "vintagee-selftest", LastKnownOwnerName = "Vintage-E self-test", ProtectionLevel = 1 };
            claim.Areas.Add(new Cuboidi(here.X + 6, here.Y - 2, here.Z - 2, here.X + 9, here.Y + 3, here.Z + 2));
            bool clear = ClaimUtil.BlastAllowed(sapi, null, here, 11);
            sapi.World.Claims.Add(claim);
            try
            {
                bool small = ClaimUtil.BlastAllowed(sapi, null, here, 3), big = ClaimUtil.BlastAllowed(sapi, null, here, 11);
                Check(!big && (small || !clear), $"a blast is stopped by a land claim anywhere in its reach (no claim: {clear}; claim 6 blocks off: radius 3 {small}, radius 11 {big})");
            }
            finally { sapi.World.Claims.Remove(claim); }
        }
    }
}
