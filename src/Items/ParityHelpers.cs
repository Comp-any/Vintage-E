using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>What the extra-function key (G by default; ProjectE's C) does with the held item. Server side.</summary>
    public interface IExtraFunctionItem
    {
        /// <summary>Returns true if something happened.</summary>
        bool DoExtraFunction(IServerPlayer player, ItemSlot slot);
    }

    /// <summary>Items with a charge level the charge key (V, sneak + V to lower it; ProjectE's V) steps through.</summary>
    public interface IChargeableItem
    {
        int GetCharge(ItemSlot slot);
        int MaxCharge(ItemSlot slot);
        void SetCharge(ItemSlot slot, int charge);
    }

    /// <summary>
    /// Tool modes made of groups, each with one choice picked (Philosopher's Stone: shape, then size; Mercurial Eye:
    /// mode, then size). The picked entry of each group is drawn bright, the others faded, as Swiftwolf's modes are.
    /// </summary>
    public sealed class ModeGroups
    {
        public readonly int[] Sizes;
        SkillItem[] on, off;

        public ModeGroups(params int[] sizes) { Sizes = sizes; }

        public int Count => Sizes.Sum();

        /// <summary>Which group a tool-mode number falls in, and the choice within it ((-1, -1) when out of range).</summary>
        public (int group, int index) Split(int mode)
        {
            for (int g = 0; g < Sizes.Length; g++)
            {
                if (mode >= 0 && mode < Sizes[g]) return (g, mode);
                mode -= Sizes[g];
            }
            return (-1, -1);
        }

        /// <summary>Makes the icons (client side). <paramref name="describe"/> gives a name and a short label for group g, choice i.</summary>
        public void Load(ICoreClientAPI capi, string codePrefix, Func<int, int, (string name, string letter)> describe)
        {
            on = new SkillItem[Count];
            off = new SkillItem[Count];
            int n = 0;
            for (int g = 0; g < Sizes.Length; g++)
            {
                for (int i = 0; i < Sizes[g]; i++, n++)
                {
                    var (name, letter) = describe(g, i);
                    string l = letter;
                    on[n] = new SkillItem { Code = new AssetLocation("vintagee", codePrefix + n + "-on"), Name = name }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, l, true));
                    off[n] = new SkillItem { Code = new AssetLocation("vintagee", codePrefix + n + "-off"), Name = name }
                        .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, l, false, faded: true));
                }
            }
        }

        /// <summary>The entries to show, with <paramref name="picked"/>[g] bright in group g.</summary>
        public SkillItem[] Show(params int[] picked)
        {
            if (on == null) return null;
            var list = new SkillItem[on.Length];
            int n = 0;
            for (int g = 0; g < Sizes.Length; g++)
            {
                for (int i = 0; i < Sizes[g]; i++, n++) list[n] = g < picked.Length && picked[g] == i ? on[n] : off[n];
            }
            return list;
        }

        public void Dispose()
        {
            if (on != null) foreach (SkillItem s in on) s?.Dispose();
            if (off != null) foreach (SkillItem s in off) s?.Dispose();
            on = off = null;
        }
    }

    /// <summary>Lighting and putting out fire the way the game's firestarter and fire do.</summary>
    public static class FireUtil
    {
        static Block fireBlock;

        public static Block Fire(IWorldAccessor world)
        {
            if (fireBlock == null || fireBlock.Id == 0) fireBlock = world.GetBlock(new AssetLocation("game:fire"));
            return fireBlock;
        }

        /// <summary>
        /// Lights the clicked block as a firestarter would: things with their own lighting (firepits, torches, bombs)
        /// are lit, and anything that burns gets the game's fire on the clicked face. Returns true if it took.
        /// </summary>
        public static bool TryLight(IWorldAccessor world, BlockSelection sel, EntityAgent lighter, string byUid, Func<BlockPos, bool> mayEdit)
        {
            if (sel?.Position == null || sel.Face == null || !mayEdit(sel.Position)) return false;
            IBlockAccessor ba = world.BlockAccessor;
            Block hit = ba.GetBlock(sel.Position);
            if (hit == null || hit.Id == 0) return false;

            IIgnitable ign = hit.GetInterface<IIgnitable>(world, sel.Position);
            if (ign != null && lighter != null)
            {
                try
                {
                    EnumIgniteState state = ign.OnTryIgniteBlock(lighter, sel.Position, 10f);
                    if (state == EnumIgniteState.IgniteNow || state == EnumIgniteState.Ignitable)
                    {
                        EnumHandling handling = EnumHandling.PassThrough;
                        ign.OnTryIgniteBlockOver(lighter, sel.Position, 10f, ref handling);
                        return true;
                    }
                    if (state == EnumIgniteState.NotIgnitablePreventDefault) return false;
                }
                catch (Exception e) { world.Logger.VerboseDebug("Vintage-E lighting {0}: {1}", hit.Code, e.Message); }
            }

            if (!(hit.CombustibleProps?.BurnDuration > 0)) return false;
            BlockPos firePos = sel.Position.AddCopy(sel.Face);
            Block there = ba.GetBlock(firePos);
            if (there == null || (there.Id != 0 && there.Replaceable < 6000) || !mayEdit(firePos)) return false;
            Block fire = Fire(world);
            if (fire == null || fire.Id == 0) return false;
            ba.SetBlock(fire.BlockId, firePos);
            BlockEntity be = ba.GetBlockEntity(firePos);
            object burning = null;
            if (be?.Behaviors != null) foreach (var b in be.Behaviors) if (b.GetType().Name == "BEBehaviorBurning") { burning = b; break; }
            var place = burning?.GetType().GetMethod("OnFirePlaced", new[] { typeof(BlockFacing), typeof(string) });
            bool lit = false;
            try
            {
                place?.Invoke(burning, new object[] { sel.Face, byUid });
                lit = burning?.GetType().GetField("IsBurning")?.GetValue(burning) is bool b2 && b2;
            }
            catch (Exception e) { world.Logger.VerboseDebug("Vintage-E fire: {0}", e.Message); }
            if (!lit) ba.SetBlock(0, firePos); // it didn't catch: no fire block left hanging there
            return lit;
        }

        /// <summary>Removes the game's fire blocks between <paramref name="min"/> and <paramref name="max"/> (inclusive). Returns how many.</summary>
        public static int PutOut(IWorldAccessor world, BlockPos min, BlockPos max, Func<BlockPos, bool> mayEdit)
        {
            Block fire = Fire(world);
            if (fire == null || fire.Id == 0) return 0;
            IBlockAccessor ba = world.BlockAccessor;
            int n = 0;
            var pos = new BlockPos(min.dimension);
            for (int x = min.X; x <= max.X; x++)
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        pos.Set(x, y, z);
                        if (ba.GetBlock(pos).Id != fire.Id || !mayEdit(pos)) continue;
                        ba.SetBlock(0, pos);
                        n++;
                    }
            if (n > 0) world.PlaySoundAt(new AssetLocation("game:sounds/effect/extinguish"), min.X + (max.X - min.X) / 2.0, min.Y + 1, min.Z + (max.Z - min.Z) / 2.0, null, true, 12, 0.5f);
            return n;
        }
    }

    /// <summary>
    /// The game's weather, for ProjectE's shots into the sky: rain is moved along in time (as /weather stoprain does)
    /// until it rains or stops where the shot went up, and a thunderstorm is the game's weather event with the most
    /// lightning. Everything is skipped while a pedestal's amulet holds the weather.
    /// </summary>
    public static class WeatherControl
    {
        /// <summary>Precipitation above this counts as rain (the game's /weather stoprain uses the same line).</summary>
        public const float RainLevel = 0.04f;

        static WeatherSystemServer Ws(ICoreServerAPI sapi)
        {
            try { return sapi?.ModLoader.GetModSystem<WeatherSystemServer>(); }
            catch { return null; }
        }

        public static float Precipitation(ICoreServerAPI sapi, Vec3d at)
        {
            WeatherSystemServer ws = Ws(sapi);
            if (ws == null || at == null) return 0;
            try { return ws.GetPrecipitation(at); }
            catch { return 0; }
        }

        public static bool IsRaining(ICoreServerAPI sapi, Vec3d at) => Precipitation(sapi, at) > RainLevel;

        /// <summary>Makes it rain (or stop) at <paramref name="at"/> for a while. Returns true if the weather is now as asked.</summary>
        public static bool SetRain(ICoreServerAPI sapi, Vec3d at, bool rain)
        {
            WeatherSystemServer ws = Ws(sapi);
            if (ws == null || at == null || ws.OverridePrecipitation != null) return false;
            try
            {
                if (IsRaining(sapi, at) == rain) return true;
                double now = sapi.World.Calendar.TotalDays;
                double step = 1.0 / Math.Max(1, sapi.World.Calendar.HoursPerDay);
                float want = rain ? 0.1f : RainLevel;
                bool Wet(double d) => ws.GetPrecipitation(at.X, at.Y, at.Z, now + d) > want;
                for (double d = step; d < 30; d += step)
                {
                    bool ok = rain ? Wet(d) && Wet(d + step) : !Wet(d) && !Wet(d + step);
                    if (!ok) continue;
                    ws.RainCloudDaysOffset += d + step / 2;
                    ws.broadCastConfigUpdate();
                    return true;
                }
            }
            catch (Exception e) { sapi.Logger.Warning("Vintage-E: could not change the rain: {0}", e.Message); }
            return false;
        }

        static WeatherSimulationRegion RegionAt(WeatherSystemServer ws, ICoreServerAPI sapi, Vec3d at)
        {
            int rs = sapi.World.BlockAccessor.RegionSize;
            long index = ws.MapRegionIndex2D((int)at.X / rs, (int)at.Z / rs);
            return ws.weatherSimByMapRegion.TryGetValue(index, out WeatherSimulationRegion sim) ? sim : null;
        }

        /// <summary>A thunderstorm where <paramref name="at"/> is: rain, and a weather event that brings lightning.</summary>
        public static bool IsThundering(ICoreServerAPI sapi, Vec3d at)
        {
            WeatherSystemServer ws = Ws(sapi);
            if (ws == null || at == null) return false;
            try
            {
                if (!IsRaining(sapi, at)) return false;
                return RegionAt(ws, sapi, at)?.CurWeatherEvent?.config?.Lightning?.LightningRate > 0;
            }
            catch { return false; }
        }

        /// <summary>Rain, and the game's weather event with the most lightning in every loaded region. Returns true if it took.</summary>
        public static bool StartThunderstorm(ICoreServerAPI sapi, Vec3d at)
        {
            WeatherSystemServer ws = Ws(sapi);
            if (ws == null) return false;
            try
            {
                SetRain(sapi, at, true);
                WeatherEventConfig storm = ws.WeatherEventConfigs?
                    .Where(c => c?.Code != null && c.Lightning != null && c.Lightning.LightningRate > 0)
                    .OrderByDescending(c => c.Lightning.LightningRate).FirstOrDefault();
                if (storm == null) return false;
                bool any = false;
                foreach (WeatherSimulationRegion sim in ws.weatherSimByMapRegion.Values.ToArray())
                {
                    try { any |= sim.SetWeatherEvent(storm.Code, true); } catch { }
                }
                return any;
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("Vintage-E: could not start a thunderstorm: {0}", e.Message);
                return false;
            }
        }

        /// <summary>The game's lightning strike at <paramref name="at"/> (it hurts what's there and can start fires).</summary>
        public static bool Strike(ICoreServerAPI sapi, Vec3d at)
        {
            WeatherSystemServer ws = Ws(sapi);
            if (ws == null || at == null) return false;
            try { ws.SpawnLightningFlash(at); return true; }
            catch (Exception e) { sapi.Logger.Warning("Vintage-E: lightning: {0}", e.Message); return false; }
        }
    }

    /// <summary>ProjectE's right-click tool actions: vein mining, clearing logs and leaves, digging an area, shearing.</summary>
    public static class AreaTools
    {
        /// <summary>ProjectE: at most 250 blocks in one vein.</summary>
        public const int MaxVein = 250;
        /// <summary>Blocks one area action looks at, at most (a red tool's log clearing is 21 × 41 × 21).</summary>
        const int MaxScan = 40000;

        /// <summary>The tool can mine this block and it drops for it: its material is one of the tool's, and its tier is enough.</summary>
        public static bool CanMine(CollectibleObject tool, Block block)
        {
            if (tool == null || block == null || block.Id == 0 || block.Replaceable >= 6000) return false;
            if (block.RequiredMiningTier > tool.ToolTier) return false;
            if (block.Code == null || block.Code.Path.StartsWith("mantle")) return false;
            return tool.MiningSpeed != null && tool.MiningSpeed.ContainsKey(block.BlockMaterial);
        }

        public static bool IsOre(Block b) => b?.Code != null && (b is BlockOre || b.Code.Path.StartsWith("ore-", StringComparison.Ordinal));

        /// <summary>ProjectE's shovel veins: gravel and clay.</summary>
        public static bool IsShovelVein(Block b)
        {
            string p = b?.Code?.Path;
            return p != null && (p.StartsWith("gravel-", StringComparison.Ordinal) || p.StartsWith("rawclay-", StringComparison.Ordinal));
        }

        public static bool IsLog(Block b)
        {
            string p = b?.Code?.Path;
            return p != null && (p.StartsWith("log-", StringComparison.Ordinal) || p.StartsWith("logsection-", StringComparison.Ordinal));
        }

        public static bool IsLeaves(Block b) => b?.Code?.Path?.StartsWith("leaves", StringComparison.Ordinal) == true;

        /// <summary>An ore's kind without its grade and rock ("ore-rich-nativecopper-granite" → "nativecopper"), so a vein runs on through other rock.</summary>
        static string OreKind(Block b)
        {
            if (!IsOre(b)) return null;
            string[] parts = b.Code.Path.Split('-');
            return parts.Length >= 3 ? b.Code.Domain + ":" + parts[2] : b.Code.ToString();
        }

        static bool SameVein(Block start, Block other)
        {
            if (other == null || other.Id == 0) return false;
            if (other.Id == start.Id) return true;
            string k = OreKind(start);
            return k != null && k == OreKind(other);
        }

        static bool MayBreak(IWorldAccessor world, IPlayer player, BlockPos pos)
        {
            try
            {
                if (player != null) return world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak);
                LandClaim[] claims = world.Claims.Get(pos);
                return claims == null || claims.Length == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// ProjectE's vein mining: every block of the clicked kind in a box around it (the clicked block alone at charge 0;
        /// <paramref name="charge"/> across and twice that deep into the face), then everything of the kind touching those,
        /// up to 250 blocks. What breaks drops where the player clicked. Returns how many broke.
        /// </summary>
        public static int VeinMine(IWorldAccessor world, IPlayer player, CollectibleObject tool, BlockPos start, BlockFacing face, int charge)
        {
            IBlockAccessor ba = world.BlockAccessor;
            Block first = ba.GetBlock(start);
            if (!CanMine(tool, first)) return 0;
            face ??= BlockFacing.UP;
            var seen = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();
            BlockFacing into = face.Opposite;
            for (int d = 0; d <= 2 * charge; d++)
            {
                BlockPos layer = start.AddCopy(into, d);
                foreach (BlockPos p in AreaToolModes.Plane(layer, face, charge))
                {
                    if (!seen.Add(p)) continue;
                    Block b = ba.GetBlock(p);
                    if (SameVein(first, b) && CanMine(tool, b) && MayBreak(world, player, p)) queue.Enqueue(p);
                }
            }
            var broken = new List<BlockPos>();
            while (queue.Count > 0 && broken.Count < MaxVein)
            {
                BlockPos pos = queue.Dequeue();
                Block b = ba.GetBlock(pos);
                if (!SameVein(first, b)) continue;
                ba.BreakBlock(pos, player);
                broken.Add(pos);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            BlockPos next = pos.AddCopy(dx, dy, dz);
                            if (!seen.Add(next)) continue;
                            Block nb = ba.GetBlock(next);
                            if (SameVein(first, nb) && CanMine(tool, nb) && MayBreak(world, player, next)) queue.Enqueue(next);
                        }
            }
            if (broken.Count > 0)
            {
                Vec3f n = face.Normalf;
                GatherDrops(world, broken, start.ToVec3d().Add(0.5 + n.X * 0.7, 0.5 + n.Y * 0.7, 0.5 + n.Z * 0.7));
                PESounds.Play(world, PESounds.Destruct, "game:sounds/effect/rockslide", start, 16, 0.6f);
            }
            return broken.Count;
        }

        /// <summary>
        /// ProjectE's clearing (axe on logs, shears on leaves): every matching block within 5 × <paramref name="charge"/>
        /// blocks across and 10 × up and down of the player. Nothing at charge 0. What breaks drops at the player.
        /// </summary>
        public static int ClearAround(IWorldAccessor world, IPlayer player, Entity at, Func<Block, bool> match, int charge)
        {
            if (charge <= 0 || at == null) return 0;
            int h = 5 * charge, v = 10 * charge;
            BlockPos c = at.Pos.AsBlockPos;
            IBlockAccessor ba = world.BlockAccessor;
            var broken = new List<BlockPos>();
            var pos = new BlockPos(c.dimension);
            int scanned = 0;
            for (int x = c.X - h; x <= c.X + h; x++)
                for (int z = c.Z - h; z <= c.Z + h; z++)
                    for (int y = c.Y - v; y <= c.Y + v + 1; y++)
                    {
                        if (++scanned > MaxScan * 2) goto done;
                        pos.Set(x, y, z);
                        Block b = ba.GetBlock(pos);
                        if (b == null || b.Id == 0 || !match(b)) continue;
                        BlockPos p = pos.Copy();
                        if (!MayBreak(world, player, p)) continue;
                        ba.BreakBlock(p, player);
                        broken.Add(p);
                    }
            done:
            if (broken.Count > 0) GatherDrops(world, broken, at.Pos.XYZ.Add(0, 0.5, 0));
            return broken.Count;
        }

        /// <summary>
        /// ProjectE's shovel and hammer digging: everything the tool can mine in a flat square of radius <paramref name="charge"/>
        /// around the clicked block (on its level). Nothing at charge 0. What breaks drops where the player clicked.
        /// </summary>
        public static int DigFlat(IWorldAccessor world, IPlayer player, CollectibleObject tool, BlockPos center, int charge)
        {
            if (charge <= 0) return 0;
            IBlockAccessor ba = world.BlockAccessor;
            var broken = new List<BlockPos>();
            foreach (BlockPos p in AreaToolModes.Plane(center, BlockFacing.UP, charge))
            {
                Block b = ba.GetBlock(p);
                if (!CanMine(tool, b) || !MayBreak(world, player, p)) continue;
                ba.BreakBlock(p, player);
                broken.Add(p);
            }
            if (broken.Count > 0)
            {
                GatherDrops(world, broken, center.ToVec3d().Add(0.5, 1.2, 0.5));
                PESounds.Play(world, PESounds.Destruct, null, center, 16);
            }
            return broken.Count;
        }

        /// <summary>A moment after blocks broke, moves what they dropped to one spot (ProjectE drops it all together).</summary>
        public static void GatherDrops(IWorldAccessor world, List<BlockPos> broken, Vec3d to)
        {
            if (broken.Count == 0 || world.Side != EnumAppSide.Server) return;
            int minX = broken.Min(p => p.X), maxX = broken.Max(p => p.X), minY = broken.Min(p => p.Y), maxY = broken.Max(p => p.Y), minZ = broken.Min(p => p.Z), maxZ = broken.Max(p => p.Z);
            var mid = new Vec3d((minX + maxX) / 2.0 + 0.5, (minY + maxY) / 2.0 + 0.5, (minZ + maxZ) / 2.0 + 0.5);
            float hr = Math.Max(maxX - minX, maxZ - minZ) / 2f + 2, vr = (maxY - minY) / 2f + 2;
            world.Api.Event.RegisterCallback(_ =>
            {
                foreach (Entity e in world.GetEntitiesAround(mid, hr, vr, x => x is EntityItem && x.Alive))
                {
                    e.Pos.SetPos(to);
                    e.Pos.Motion.Set(0, 0, 0);
                }
            }, 100);
        }

        /// <summary>Shears' charge for shearing creatures: ProjectE reaches 2^(2 + charge) blocks.</summary>
        public static float ShearRadius(int charge) => (float)Math.Pow(2, 2 + Math.Max(0, charge));
    }
}
