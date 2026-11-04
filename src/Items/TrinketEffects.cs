using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// What the rings, stones, amulets, talisman and watch do, run on the server twice a second for every player
    /// carrying an active one (and by pedestals for a fixed spot). Costs are paid with <see cref="EmcWallet"/>;
    /// the Ring of Arcana is free, as in ProjectE.
    /// </summary>
    public class TrinketEffects
    {
        /// <summary>The walk speed bonus older versions gave Swiftwolf's Rending Gale; removed from players on sight.</summary>
        public const string GaleStatCode = "vintageegale";
        /// <summary>On the player entity: flight was switched on by a ring (so only we switch it off again).</summary>
        public const string FlightKey = "vintageeFlight";
        /// <summary>On the player entity: their move speed before Swiftwolf's Rending Gale sped them up (restored afterwards).</summary>
        public const string SpeedBaseKey = "vintageeSpeedBase";
        const float Interval = 0.5f;

        // ProjectE: 0.32 EMC a tick each for flying and for the shield.
        public const double FlightCostPerSecond = 6.4, ShieldCostPerSecond = 6.4;
        /// <summary>Swiftwolf's shield: how far it keeps hostile creatures (1.9.2: 8 blocks, was 5) and how often it pushes (every 0.1 s).</summary>
        public const float ShieldRadius = 8;
        /// <summary>How far the Black Hole Band pulls items from; the Void Ring, the better ring, reaches further.</summary>
        public const float BandPullRadius = 7, VoidPullRadius = 12;
        const int ShieldTickMs = 100;
        /// <summary>Players whose shield is up and paid for, with their entity: pushed for by <see cref="ShieldTick"/>.</summary>
        readonly Dictionary<string, Entity> shielded = new Dictionary<string, Entity>();
        /// <summary>Seconds after ring flight ends during which falling doesn't hurt.</summary>
        public const double FlightGraceSeconds = 15;
        /// <summary>Swiftwolf's lightning shot (ProjectE: 832 EMC).</summary>
        public const double LightningCost = 832;
        /// <summary>Zero Ring burst (sneak + right-click): a wide freeze with snow.</summary>
        public const double FreezeBurstCost = 32;
        /// <summary>Mind Stone: EMC a second while it holds your body temperature at normal.</summary>
        public const double TemperCostPerSecond = 1;
        /// <summary>Dark Matter Sword / Red Matter Katar area attack: EMC per creature hit, and how far it reaches.</summary>
        public const double SlayCostEach = 8;
        public const float SlayRadius = 6;

        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly Dictionary<string, Dictionary<string, float>> timers = new Dictionary<string, Dictionary<string, float>>();
        readonly Dictionary<string, long> flightEndedMs = new Dictionary<string, long>();
        readonly Dictionary<string, long> lastTeleportMs = new Dictionary<string, long>();
        readonly HashSet<string> statsCleaned = new HashSet<string>();
        readonly Random rand = new Random();
        AssetLocation[] hostile;

        /// <summary>Forget the cached hostile creature list (after HostileCreatures changes).</summary>
        public void ResetHostile() => hostile = null;

        /// <summary>Name of this mod's time speed modifier on the world calendar (the Watch of Flowing Time).</summary>
        public const string TimeModifierName = "vintageewatch";
        /// <summary>The calendar modifier currently applied (0 = none). Mirrored to clients.</summary>
        public float WatchTimeModifier { get; private set; }
        int watchFaster, watchSlower; // players with an active watch this tick, by mode

        public TrinketEffects(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
            sapi.Event.RegisterGameTickListener(OnTick, (int)(Interval * 1000));
            sapi.Event.RegisterGameTickListener(ShieldTick, ShieldTickMs);
            sapi.Event.PlayerDisconnect += p =>
            {
                timers.Remove(p.PlayerUID);
                shielded.Remove(p.PlayerUID);
                statsCleaned.Remove(p.PlayerUID);
                flightEndedMs.Remove(p.PlayerUID);
                lastTeleportMs.Remove(p.PlayerUID);
            };
        }

        double Cost(double baseCost) => baseCost * mod.Config.TrinketCostMultiplier;

        /// <summary>
        /// Whether an effect may change this block: the player's own claim rules, or, for a pedestal (no player),
        /// nothing inside anyone's claim at all. A pedestal just outside a claim used to harvest the crops and freeze
        /// the ponds inside it.
        /// </summary>
        bool MayChange(IPlayer byPlayer, BlockPos pos)
        {
            try
            {
                if (byPlayer != null) return sapi.World.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak);
                LandClaim[] claims = sapi.World.Claims.Get(pos);
                return claims == null || claims.Length == 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ pedestal weather

        /// <summary>Loaded pedestals holding an Evertide or Volcanite Amulet, and which.</summary>
        readonly Dictionary<BlockPos, string> weatherPedestals = new Dictionary<BlockPos, string>();
        /// <summary>Savegame key: the game's precipitation override was set by a pedestal (so only we reset it).</summary>
        const string WeatherOwnedKey = "vintageePedestalWeather";

        /// <summary>
        /// A pedestal's amulet changed (null: none, or the pedestal was removed/unloaded). An Evertide Amulet on any
        /// loaded pedestal brings rain; otherwise a Volcanite Amulet stops it; with neither, the weather is the game's again.
        /// </summary>
        /// <summary>
        /// At server start nothing of ours is running yet, so anything left behind by a crash is cleared: the watch's
        /// time-speed modifier (its first tick only removes it on a change) and a pedestal's rain override, which
        /// the pedestal re-asserts itself once its chunk loads.
        /// </summary>
        public void ReconcileAfterStart()
        {
            try { sapi.World.Calendar?.RemoveTimeSpeedModifier(TimeModifierName); } catch { }
            try
            {
                if (sapi.WorldManager.SaveGame.GetData<bool>(WeatherOwnedKey, false) && weatherPedestals.Count == 0 && SetPrecipitationOverride(null))
                    sapi.WorldManager.SaveGame.StoreData(WeatherOwnedKey, false);
            }
            catch { }
        }

        public void SetPedestalWeather(BlockPos pos, string kind)
        {
            if (pos == null) return;
            if (kind == "evertideamulet" || kind == "volcaniteamulet") weatherPedestals[pos.Copy()] = kind;
            else if (!weatherPedestals.Remove(pos)) return;
            float? want = weatherPedestals.ContainsValue("evertideamulet") ? 1f : weatherPedestals.ContainsValue("volcaniteamulet") ? 0f : (float?)null;
            bool owned = sapi.WorldManager.SaveGame.GetData<bool>(WeatherOwnedKey, false);
            if (want == null && !owned) return;
            if (SetPrecipitationOverride(want)) sapi.WorldManager.SaveGame.StoreData(WeatherOwnedKey, want != null);
        }

        /// <summary>
        /// Sets the game's precipitation override (what /weather setprecip does; null = automatic) and tells the clients.
        /// The command itself can't be used: it needs a player to run it.
        /// </summary>
        bool SetPrecipitationOverride(float? level)
        {
            try
            {
                ModSystem weather = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().Name == "WeatherSystemServer");
                var prop = weather?.GetType().GetProperty("OverridePrecipitation");
                if (prop == null) return false;
                if (Equals(prop.GetValue(weather), level)) return true;
                prop.SetValue(weather, level);
                weather.GetType().GetMethod("broadCastConfigUpdate", Type.EmptyTypes)?.Invoke(weather, null);
                return true;
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("Vintage-E: could not change the weather for a pedestal: {0}", e.Message);
                return false;
            }
        }

        /// <summary>Pays <paramref name="baseCost"/> × the cost multiplier, or tells the player they can't.</summary>
        public bool Pay(IServerPlayer player, double baseCost)
        {
            double cost = Cost(baseCost);
            if (EmcWallet.TryPay(player, cost)) return true;
            ItemEmcGadget.Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
            return false;
        }

        public bool IsHostile(Entity e)
        {
            if (e == null || !e.Alive || e is EntityPlayer || !(e is EntityAgent) || e.Code == null) return false;
            hostile ??= mod.Config.HostileCreatures.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => new AssetLocation(p)).ToArray();
            foreach (AssetLocation pattern in hostile)
            {
                // A bad pattern (an unfinished @regex in the settings) must not take a tick listener down.
                try { if (WildcardUtil.Match(pattern, e.Code)) return true; }
                catch (Exception ex) { mod.Mod.Logger.Warning("Vintage-E: HostileCreatures entry {0} can't be used: {1}", pattern, ex.Message); hostile = hostile.Where(h => h != pattern).ToArray(); return false; }
            }
            return false;
        }

        /// <summary>True once per <paramref name="seconds"/> for this player and key.</summary>
        bool Every(string uid, string key, float seconds)
        {
            if (!timers.TryGetValue(uid, out var t)) timers[uid] = t = new Dictionary<string, float>();
            t.TryGetValue(key, out float acc);
            acc += Interval;
            if (acc + 1e-4 < seconds) { t[key] = acc; return false; }
            t[key] = 0;
            return true;
        }

        void OnTick(float dt)
        {
            watchFaster = watchSlower = 0;
            foreach (IPlayer p in sapi.World.AllOnlinePlayers)
            {
                if (!(p is IServerPlayer player) || player.ConnectionState != EnumClientState.Playing || player.Entity == null || !player.Entity.Alive) continue;
                try { TickPlayer(player); }
                catch (Exception e) { sapi.Logger.Warning("Vintage-E trinkets for {0}: {1}", player.PlayerName, e.Message); }
            }
            UpdateWorldTime();
        }

        /// <summary>
        /// The Watch of Flowing Time changes how fast time passes for the whole world (like sleeping does), as long as
        /// someone has one switched on and can pay for it. Speeding up wins over slowing down.
        /// </summary>
        void UpdateWorldTime()
        {
            float want = 0;
            if (mod.Config.WatchChangesWorldTime)
            {
                // The game's base is 60 "time speed"; the modifier is added to it.
                if (watchFaster > 0) want = 60f * Math.Max(0, (float)mod.Config.WatchSpeedUp - 1);
                else if (watchSlower > 0) want = -60f * (1 - 1 / Math.Max(1, (float)mod.Config.WatchSlowDown));
            }
            if (Math.Abs(want - WatchTimeModifier) < 0.01f) return;
            WatchTimeModifier = want;
            ApplyTimeModifier(sapi.World.Calendar, want);
            mod.BroadcastWatchSpeed(want);
        }

        public static void ApplyTimeModifier(IGameCalendar calendar, float value)
        {
            if (calendar == null) return;
            if (Math.Abs(value) < 0.01f) calendar.RemoveTimeSpeedModifier(TimeModifierName);
            else calendar.SetTimeSpeedModifier(TimeModifierName, value);
        }

        public static IEnumerable<(ItemSlot slot, ItemEmcTrinket trinket)> CarriedTrinkets(IPlayer player)
        {
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is ItemEmcTrinket t) yield return (slot, t);
            }
        }

        /// <summary>Trinkets that are working: switched-on ones and the always-on talisman.</summary>
        public static IEnumerable<(ItemSlot slot, ItemEmcTrinket trinket)> ActiveTrinkets(IPlayer player)
        {
            foreach (var (slot, t) in CarriedTrinkets(player))
            {
                if (t.IsAlwaysOn || (t.IsToggleable && ItemEmcTrinket.IsActive(slot.Itemstack))) yield return (slot, t);
            }
        }

        /// <summary>The ring each Ring of Arcana effect copies, in <see cref="ItemEmcTrinket.ArcanaModes"/> order.</summary>
        static readonly string[] ArcanaRings = { "zeroring", "ignitionring", "harvestgoddessband", "swiftwolfsrendinggale" };

        /// <summary>
        /// The effects of an active trinket: its own kind; for the Ring of Arcana every one of its four ring effects
        /// that is switched on; for the Void Ring the Gem of Eternal Density and the Black Hole Band.
        /// </summary>
        public static IEnumerable<string> EffectsOf(ItemEmcTrinket t, ItemStack stack)
        {
            if (t.Kind == "ringofarcana")
            {
                for (int i = 0; i < ArcanaRings.Length; i++)
                {
                    if (ItemEmcTrinket.ArcanaEnabled(stack, i)) yield return ArcanaRings[i];
                }
                yield break;
            }
            if (t.Kind == "voidring")
            {
                yield return "gemofeternaldensity";
                yield return "voidpull"; // the band's pull, with the ring's longer reach
                yield break;
            }
            yield return t.Kind;
        }

        /// <summary>A switched-on Swiftwolf's Rending Gale whose mode includes flight (shield-only mode never pays for flight, so it gives no fall protection).</summary>
        public static bool HasGaleFlight(IPlayer player)
        {
            foreach (var (slot, t) in ActiveTrinkets(player))
            {
                if (!EffectsOf(t, slot.Itemstack).Contains("swiftwolfsrendinggale")) continue;
                bool free = t.Kind == "ringofarcana"; // the Arcana's flight is free and always on
                int mode = free ? 0 : GameMath.Clamp(ItemEmcTrinket.GetMode(slot.Itemstack), 0, ItemEmcTrinket.GaleModes.Length - 1);
                if (mode != 2) return true;
            }
            return false;
        }

        public static bool HasActiveEffect(IPlayer player, string effect)
        {
            foreach (var (slot, t) in ActiveTrinkets(player))
            {
                if (EffectsOf(t, slot.Itemstack).Contains(effect)) return true;
            }
            return false;
        }

        /// <summary>ProjectE's fire protection: carrying an Ignition Ring, Volcanite Amulet or Ring of Arcana (on or off).</summary>
        public static bool IsFireProof(IPlayer player)
        {
            foreach (var (_, t) in CarriedTrinkets(player))
            {
                if (t.Kind == "ignitionring" || t.Kind == "volcaniteamulet" || t.Kind == "ringofarcana") return true;
            }
            return false;
        }

        /// <summary>Flying on a ring now, or stopped within the last <see cref="FlightGraceSeconds"/> (so the landing doesn't hurt).</summary>
        public bool FlightProtects(IPlayer player)
        {
            if (player?.Entity?.WatchedAttributes.GetBool(FlightKey) == true) return true;
            return flightEndedMs.TryGetValue(player?.PlayerUID ?? "", out long ms) && sapi.World.ElapsedMilliseconds - ms < FlightGraceSeconds * 1000;
        }

        void TickPlayer(IServerPlayer player)
        {
            string uid = player.PlayerUID;
            EntityPlayer entity = player.Entity;
            if (statsCleaned.Add(uid)) entity.Stats?.Remove("walkspeed", GaleStatCode);

            bool wantFlight = false, wantShield = false, flightFree = false, shieldFree = false;
            float speed = 1;
            var paidGaleSlots = new List<ItemSlot>();
            var done = new HashSet<string>(); // the same effect twice doesn't stack

            foreach (var (slot, t) in ActiveTrinkets(player).ToList())
            {
                bool free = t.Kind == "ringofarcana"; // ProjectE's Arcana costs nothing
                foreach (string effect in EffectsOf(t, slot.Itemstack))
                {
                    if (effect == "swiftwolfsrendinggale")
                    {
                        int mode = free ? 0 : GameMath.Clamp(ItemEmcTrinket.GetMode(slot.Itemstack), 0, ItemEmcTrinket.GaleModes.Length - 1);
                        bool flight = mode != 2, shield = mode != 1;
                        wantFlight |= flight;
                        wantShield |= shield;
                        if (free) { flightFree |= flight; shieldFree |= shield; }
                        else
                        {
                            paidGaleSlots.Add(slot);
                            // The speed is for flying (and running) with it: shield-only mode leaves your speed alone.
                            if (flight) speed = Math.Max(speed, ItemEmcTrinket.GaleSpeeds[ItemEmcTrinket.GetSpeedLevel(slot.Itemstack)]);
                        }
                        continue;
                    }
                    string key = effect + (effect == "gemofeternaldensity" ? slot.GetHashCode().ToString() : "");
                    if (!done.Add(key)) continue;
                    IServerPlayer payer = free ? null : player;
                    switch (effect)
                    {
                        case "blackholeband":
                            Attract(entity.Pos.XYZ.Add(0, 0.5, 0), BandPullRadius);
                            break;
                        case "voidpull":
                            Attract(entity.Pos.XYZ.Add(0, 0.5, 0), VoidPullRadius);
                            break;
                        case "harvestgoddessband":
                            if (Every(uid, effect, 1))
                            {
                                Grow(entity.Pos.AsBlockPos, 5, 6, payer, payer == null ? 0 : Cost(4));
                                Harvest(entity.Pos.AsBlockPos, 5, player);
                            }
                            break;
                        case "watchofflowingtime":
                            bool faster = ItemEmcTrinket.GetMode(slot.Itemstack) == 0;
                            if (!EmcWallet.TryPay(player, Cost((faster ? 8 : 4) * Interval))) break;
                            if (faster)
                            {
                                watchFaster++;
                                if (Every(uid, effect, 1)) Grow(entity.Pos.AsBlockPos, 6, 4, null, 0);
                            }
                            else
                            {
                                watchSlower++;
                                Slow(entity.Pos.XYZ, 10);
                            }
                            break;
                        case "ignitionring":
                            if (Every(uid, effect, 1)) Burn(entity.Pos.XYZ, 5, entity, payer, payer == null ? 0 : Cost(4));
                            break;
                        case "zeroring":
                            Freeze(entity.Pos.AsBlockPos, 3, player);
                            if (Every(uid, "snow", 1)) LaySnow(entity.Pos.AsBlockPos, 3, 3, player);
                            break;
                        case "mindstone":
                            Temper(player);
                            break;
                        case "bodystone":
                            if (Every(uid, effect, 2)) Feed(player, ItemEmcTrinket.FeedsNutrition(slot.Itemstack));
                            break;
                        case "soulstone":
                            if (Every(uid, effect, 1)) Heal(player);
                            break;
                        case "lifestone":
                            if (Every(uid, "life-feed", 2)) Feed(player, ItemEmcTrinket.FeedsNutrition(slot.Itemstack));
                            if (Every(uid, "life-heal", 1)) Heal(player);
                            break;
                        case "repairtalisman":
                            if (Every(uid, effect, 1)) Repair(player);
                            break;
                        case "gemofeternaldensity":
                            if (Every(uid, key, 2)) Condense(player, slot); // per slot: two gems used to share one timer
                            break;
                    }
                }
            }

            // Gem helmet: you never run out of air (ProjectE's gives water breathing).
            if (EntityBehaviorVintageE.ArmorPieces(player, "gem", out _) > 0 && WearsGemHead(player))
            {
                ITreeAttribute oxygen = entity.WatchedAttributes.GetTreeAttribute("oxygen");
                if (oxygen != null)
                {
                    float max = oxygen.GetFloat("maxoxygen", 0), cur = oxygen.GetFloat("currentoxygen", 0);
                    if (max > 0 && cur < max) { oxygen.SetFloat("currentoxygen", max); entity.WatchedAttributes.MarkPathDirty("oxygen"); }
                }
            }

            // ProjectE: a Harvest Goddess Band that is switched off still makes nearby crops grow, slowly and for free.
            if (!done.Contains("harvestgoddessband") && CarriedTrinkets(player).Any(c => c.trinket.Kind == "harvestgoddessband") && Every(uid, "harvest-passive", 2))
            {
                Grow(entity.Pos.AsBlockPos, 5, 3, null, 0);
            }

            // Swiftwolf's Rending Gale: shield and flight, paid for per second (the ring switches off when you can't pay).
            bool outOfEmc = false;
            bool shieldUp = false;
            if (wantShield)
            {
                if (shieldFree || EmcWallet.TryPay(player, Cost(ShieldCostPerSecond * Interval))) shieldUp = true;
                else outOfEmc = true;
            }
            if (shieldUp) shielded[uid] = entity; // pushed every 0.1 s by ShieldTick
            else shielded.Remove(uid);
            // Moving costs more the faster you go: flying 6.4 EMC/s × speed, running 6.4 EMC/s × (speed - 1).
            // Only real flight is paid: a jump, a fall or a ladder with the ring on used to cost 6.4 EMC/s too.
            bool airborne = !entity.OnGround && !entity.Swimming;
            bool flying = wantFlight && airborne && player.WorldData.FreeMove && entity.Controls.IsFlying;
            double moveCost = flying && !flightFree ? FlightCostPerSecond * speed
                : !flying && speed > 1 && entity.Controls.TriesToMove ? FlightCostPerSecond * (speed - 1) : 0;
            if (moveCost > 0 && !EmcWallet.TryPay(player, Cost(moveCost * Interval)))
            {
                outOfEmc = true;
            }
            if (outOfEmc && !flightFree && !shieldFree)
            {
                foreach (ItemSlot s in paidGaleSlots)
                {
                    s.Itemstack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, false);
                    s.MarkDirty();
                }
                ItemEmcGadget.Tell(player, Lang.GetL(player.LanguageCode, "vintagee:gale-outofemc"));
                wantFlight = false;
                speed = 1;
            }
            SetFlight(player, wantFlight);
            // Speed comes with the paid ring: while flying for free on the Ring of Arcana, nothing is paid, so no speed.
            bool speedPaid = paidGaleSlots.Count > 0 && !outOfEmc && !(flying && flightFree);
            SetSpeed(player, speedPaid ? speed : 1);
        }

        static bool WearsGemHead(IPlayer player)
        {
            IInventory gear = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear == null) return false;
            foreach (ItemSlot slot in gear)
            {
                AssetLocation code = slot?.Itemstack?.Collectible?.Code;
                if (code != null && code.Domain == VintageEModSystem.ModId && code.Path == "matterarmor-gem-head") return true;
            }
            return false;
        }

        /// <summary>
        /// Swiftwolf's speed: multiplies the player's move speed (running and flying) by <paramref name="factor"/>,
        /// remembering what it was so it can be put back. Creative and spectator players are left alone.
        /// </summary>
        void SetSpeed(IServerPlayer player, float factor)
        {
            IWorldPlayerData wd = player.WorldData;
            if (wd == null || player.Entity == null) return;
            if (wd.CurrentGameMode == EnumGameMode.Creative || wd.CurrentGameMode == EnumGameMode.Spectator) return;
            SyncedTreeAttribute attrs = player.Entity.WatchedAttributes;
            bool ours = attrs.HasAttribute(SpeedBaseKey);
            if (factor > 1.001f)
            {
                float baseMul = ours ? attrs.GetFloat(SpeedBaseKey, 1) : wd.MoveSpeedMultiplier;
                if (!ours) attrs.SetFloat(SpeedBaseKey, baseMul);
                float want = baseMul * factor;
                if (Math.Abs(wd.MoveSpeedMultiplier - want) > 0.001f)
                {
                    wd.MoveSpeedMultiplier = want;
                    player.BroadcastPlayerData();
                }
            }
            else if (ours)
            {
                wd.MoveSpeedMultiplier = attrs.GetFloat(SpeedBaseKey, 1);
                attrs.RemoveAttribute(SpeedBaseKey);
                player.BroadcastPlayerData();
            }
        }

        /// <summary>Switches flying (the game's free move) on or off for a survival player. Only undoes flight it switched on.</summary>
        void SetFlight(IServerPlayer player, bool on)
        {
            IWorldPlayerData wd = player.WorldData;
            if (wd == null || player.Entity == null) return;
            if (wd.CurrentGameMode == EnumGameMode.Creative || wd.CurrentGameMode == EnumGameMode.Spectator) return;
            SyncedTreeAttribute attrs = player.Entity.WatchedAttributes;
            bool ours = attrs.GetBool(FlightKey);
            if (on)
            {
                if (!wd.FreeMove)
                {
                    wd.FreeMove = true;
                    attrs.SetBool(FlightKey, true);
                    player.BroadcastPlayerData();
                }
            }
            else if (ours)
            {
                attrs.RemoveAttribute(FlightKey);
                if (wd.FreeMove)
                {
                    wd.FreeMove = false;
                    player.BroadcastPlayerData();
                }
                flightEndedMs[player.PlayerUID] = sapi.World.ElapsedMilliseconds;
            }
        }

        // ------------------------------------------------------------------ effects (also used by pedestals)

        /// <summary>Pulls loose items towards <paramref name="center"/>.</summary>
        public void Attract(Vec3d center, float radius, bool outsideClaimsOnly = false)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is EntityItem && x.Alive))
            {
                // A pedestal (no player) leaves what lies inside someone's claim alone.
                if (outsideClaimsOnly && !MayChange(null, e.Pos.AsBlockPos)) continue;
                Vec3d dir = center.SubCopy(e.Pos.XYZ);
                double dist = dir.Length();
                if (dist < 0.75) continue;
                dir.Mul(Math.Min(0.35, 0.08 + dist * 0.04) / dist);
                e.Pos.Motion.Set(dir.X, dir.Y + 0.04, dir.Z);
            }
        }

        /// <summary>Advances random crops around <paramref name="center"/> by one growth stage.</summary>
        public int Grow(BlockPos center, int radius, int attempts, IServerPlayer payer, double costEach)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            int grown = 0;
            for (int i = 0; i < attempts; i++)
            {
                BlockPos pos = center.AddCopy(rand.Next(-radius, radius + 1), rand.Next(-2, 3), rand.Next(-radius, radius + 1));
                Block block = ba.GetBlock(pos);
                Block next = NextCropStage(block);
                if (next == null) continue;
                if (payer != null && !EmcWallet.TryPay(payer, costEach)) break;
                ba.ExchangeBlock(next.BlockId, pos);
                grown++;
            }
            return grown;
        }

        Block NextCropStage(Block block)
        {
            if (block?.Code == null || !block.Code.Path.StartsWith("crop-", StringComparison.Ordinal)) return null;
            string path = block.Code.Path;
            int dash = path.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(path.Substring(dash + 1), out int stage)) return null;
            Block next = sapi.World.GetBlock(new AssetLocation(block.Code.Domain, path.Substring(0, dash + 1) + (stage + 1)));
            return next == null || next.Id == 0 ? null : next;
        }

        static bool IsRipeCrop(Block block)
        {
            if (block?.CropProps == null || block.Code == null) return false;
            string path = block.Code.Path;
            int dash = path.LastIndexOf('-');
            return dash >= 0 && int.TryParse(path.Substring(dash + 1), out int stage) && stage >= block.CropProps.GrowthStages;
        }

        /// <summary>ProjectE's Harvest Goddess Band also harvests fully grown crops around you.</summary>
        public int Harvest(BlockPos center, int radius, IServerPlayer byPlayer)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            int harvested = 0;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        if (!IsRipeCrop(ba.GetBlock(pos))) continue;
                        if (!MayChange(byPlayer, pos)) continue;
                        ba.BreakBlock(pos, byPlayer);
                        harvested++;
                    }
                }
            }
            return harvested;
        }

        /// <summary>
        /// Harvest Goddess Band right-click: plants the seeds the player carries on empty farmland within
        /// <paramref name="radius"/> blocks of the clicked farmland. Returns how many were planted (0 = not farmland).
        /// </summary>
        public int PlantSeeds(IServerPlayer player, BlockSelection sel, int radius)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            if (!(ba.GetBlockEntity(sel.Position) is BlockEntityFarmland)) return 0;
            var seeds = new List<(ItemSlot slot, Block crop)>();
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                ItemStack stack = slot.Itemstack;
                if (stack?.Class != EnumItemClass.Item || !stack.Collectible.Code.Path.StartsWith("seeds-", StringComparison.Ordinal)) continue;
                Block crop = sapi.World.GetBlock(stack.Collectible.CodeWithPath("crop-" + stack.Collectible.LastCodePart() + "-1"));
                if (crop?.CropProps != null) seeds.Add((slot, crop));
            }
            if (seeds.Count == 0) return 0;

            int planted = 0;
            for (int dx = -radius; dx <= radius && seeds.Count > 0; dx++)
            {
                for (int dz = -radius; dz <= radius && seeds.Count > 0; dz++)
                {
                    BlockPos pos = sel.Position.AddCopy(dx, 0, dz);
                    if (!(ba.GetBlockEntity(pos) is BlockEntityFarmland farmland) || !farmland.CanPlant()) continue;
                    if (!sapi.World.Claims.TryAccess(player, pos.UpCopy(), EnumBlockAccessFlags.BuildOrBreak)) continue;
                    var (slot, crop) = seeds[0];
                    if (!farmland.TryPlant(crop, slot, player.Entity, new BlockSelection { Position = pos, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) })) continue;
                    slot.TakeOut(1);
                    slot.MarkDirty();
                    planted++;
                    if (slot.Empty) seeds.RemoveAt(0);
                }
            }
            if (planted > 0) sapi.World.PlaySoundAt(new AssetLocation("game:sounds/block/plant"), sel.Position.X + 0.5, sel.Position.Y + 1, sel.Position.Z + 0.5, null, true, 12, 0.8f);
            return planted;
        }

        /// <summary>Sets nearby hostile creatures on fire. <paramref name="payer"/> null = free (pedestal, Ring of Arcana).</summary>
        public int Burn(Vec3d center, float radius, Entity source, IServerPlayer payer, double costEach, float damage = 3f)
        {
            int hit = 0;
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius / 2, IsHostile))
            {
                if (payer != null && !EmcWallet.TryPay(payer, costEach)) break;
                e.ReceiveDamage(new DamageSource
                {
                    Source = source != null ? EnumDamageSource.Player : EnumDamageSource.Block,
                    SourceEntity = source,
                    CauseEntity = source,
                    Type = EnumDamageType.Fire
                }, damage);
                e.Ignite();
                hit++;
            }
            return hit;
        }

        /// <summary>Turns still water around and below <paramref name="center"/> into ice.</summary>
        public int Freeze(BlockPos center, int radius, IServerPlayer byPlayer)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block ice = sapi.World.GetBlock(new AssetLocation("game:lakeice"));
            if (ice == null || ice.Id == 0) return 0;
            int frozen = 0;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    for (int dy = -2; dy <= 0; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (fluid?.Code == null || !fluid.Code.Path.StartsWith("water-still", StringComparison.Ordinal)) continue;
                        if (ba.GetBlock(pos, BlockLayersAccess.Solid)?.Id > 0) continue;
                        if (!MayChange(byPlayer, pos)) continue;
                        if (!ice.ForFluidsLayer) ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                        ba.SetBlock(ice.BlockId, pos);
                        frozen++;
                    }
                }
            }
            return frozen;
        }

        /// <summary>Puts out burning creatures around <paramref name="center"/> (Zero Ring on a pedestal).</summary>
        public void Extinguish(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x.IsOnFire))
            {
                e.IsOnFire = false;
            }
        }

        /// <summary>Damps the movement of hostile creatures around <paramref name="center"/> (watch in slow mode).</summary>
        public void Slow(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius / 2, IsHostile))
            {
                e.Pos.Motion.Mul(0.4, 1, 0.4);
            }
        }

        /// <summary>Swiftwolf's shield, ten times a second, so creatures can't walk in between pushes.</summary>
        void ShieldTick(float dt)
        {
            if (shielded.Count == 0) return;
            foreach (var kv in shielded.ToList())
            {
                Entity owner = kv.Value;
                if (owner == null || !owner.Alive) { shielded.Remove(kv.Key); continue; }
                try
                {
                    Repel(owner.Pos.XYZ, ShieldRadius);
                    RepelProjectiles(owner, ShieldRadius);
                }
                catch (Exception e) { sapi.Logger.Warning("Vintage-E shield for {0}: {1}", kv.Key, e.Message); }
            }
        }

        /// <summary>
        /// Pushes hostile creatures away from <paramref name="center"/> (interdiction torch, Swiftwolf's shield): the nearer
        /// they are, the harder, and anything heading inwards has that cancelled, so they can't walk up to you between pushes.
        /// </summary>
        public void Repel(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, IsHostile))
            {
                Vec3d dir = e.Pos.XYZ.SubCopy(center);
                dir.Y = 0;
                double dist = Math.Max(0.2, dir.Length());
                dir.Mul(1 / dist);
                double push = 0.15 + 0.45 * (1 - Math.Min(1, dist / radius));
                double inward = e.Pos.Motion.X * dir.X + e.Pos.Motion.Z * dir.Z; // < 0: coming closer
                double keep = inward < 0 ? -inward : 0;
                e.Pos.Motion.Add(dir.X * (push + keep), e.OnGround ? 0.05 : 0, dir.Z * (push + keep));
            }
        }

        /// <summary>Swiftwolf's shield: turns arrows and other projectiles that someone else fired away from <paramref name="owner"/>.</summary>
        public void RepelProjectiles(Entity owner, float radius) => RepelProjectiles(owner.Pos.XYZ.Add(0, 1, 0), radius, owner);

        /// <summary>Turns projectiles within <paramref name="radius"/> of <paramref name="center"/> away from it; <paramref name="owner"/>'s own fly on (null: a player's are left alone - the interdiction torch).</summary>
        public void RepelProjectiles(Vec3d center, float radius, Entity owner)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is IProjectile))
            {
                Entity by = ((IProjectile)e).FiredBy;
                if (owner != null ? by?.EntityId == owner.EntityId : by is EntityPlayer) continue;
                Vec3d away = e.Pos.XYZ.SubCopy(center);
                double dist = away.Length();
                double speed = Math.Max(0.2, e.Pos.Motion.Length());
                if (dist < 0.01) continue;
                away.Mul(speed / dist);
                e.Pos.Motion.Set(away.X, away.Y, away.Z);
            }
        }

        /// <summary>EMC per point of saturation: plain feeding, and the nutrition mode (which also fills the food groups).</summary>
        public const double FeedCost = 0.5, NutritionFeedCost = 1;

        void Feed(IServerPlayer player, bool nutrition)
        {
            ITreeAttribute hunger = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hunger == null) return;
            float cur = hunger.GetFloat("currentsaturation"), max = hunger.GetFloat("maxsaturation");
            if (max <= 0 || cur > max - 100) return;
            float add = Math.Min(200, max - cur);
            if (!EmcWallet.TryPay(player, Cost(add * (nutrition ? NutritionFeedCost : FeedCost)))) return;
            GiveSaturation(player.Entity, add, nutrition);
            PESounds.Play(sapi.World, PESounds.Heal, null, player.Entity, 8); // ProjectE's stones play it each time they feed or heal
        }

        static readonly (EnumFoodCategory cat, string level)[] FoodGroups =
        {
            (EnumFoodCategory.Fruit, "fruitLevel"), (EnumFoodCategory.Vegetable, "vegetableLevel"), (EnumFoodCategory.Grain, "grainLevel"),
            (EnumFoodCategory.Protein, "proteinLevel"), (EnumFoodCategory.Dairy, "dairyLevel")
        };

        /// <summary>
        /// Feeds <paramref name="amount"/> saturation. With <paramref name="nutrition"/> it's eaten as food through the
        /// game (so its hunger behaviour adds nutrition too), in small portions each going to the emptiest food group.
        /// </summary>
        public static void GiveSaturation(EntityAgent entity, float amount, bool nutrition)
        {
            if (amount <= 0) return;
            if (!nutrition)
            {
                ITreeAttribute hunger = entity.WatchedAttributes.GetTreeAttribute("hunger");
                if (hunger == null) return;
                float cur = hunger.GetFloat("currentsaturation"), max = hunger.GetFloat("maxsaturation");
                hunger.SetFloat("currentsaturation", Math.Min(max, cur + amount));
                entity.WatchedAttributes.MarkPathDirty("hunger");
                return;
            }
            const float portion = 25;
            for (float left = amount; left > 0.01f; left -= portion)
            {
                ITreeAttribute hunger = entity.WatchedAttributes.GetTreeAttribute("hunger");
                var group = FoodGroups.OrderBy(g => hunger?.GetFloat(g.level) ?? 0).First();
                entity.ReceiveSaturation(Math.Min(portion, left), group.cat);
            }
        }

        void Heal(IServerPlayer player)
        {
            ITreeAttribute health = player.Entity.WatchedAttributes.GetTreeAttribute("health");
            if (health == null) return;
            float cur = health.GetFloat("currenthealth"), max = health.GetFloat("maxhealth");
            if (max <= 0 || cur >= max) return;
            if (!EmcWallet.TryPay(player, Cost(64))) return;
            player.Entity.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, 1f);
            PESounds.Play(sapi.World, PESounds.Heal, null, player.Entity, 8);
        }

        void Repair(IServerPlayer player)
        {
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player)) RepairOnce(slot, mod.Config);
        }

        /// <summary>
        /// One second of the Repair Talisman on one item: +1 durability for tools, weapons and armour, and - if the
        /// server allows it - clothing condition back up by the configured percent per minute, between the configured
        /// minimum and maximum condition. Called once a second for carried items and by pedestals.
        /// </summary>
        public static void RepairOnce(ItemSlot slot, VintageEConfig config)
        {
            ItemStack stack = slot?.Itemstack;
            if (stack == null) return;
            CollectibleObject c = stack.Collectible;
            bool changed = false;
            int max = c.GetMaxDurability(stack);
            if (max > 0)
            {
                int remaining = c.GetRemainingDurability(stack);
                if (remaining > 0 && remaining < max)
                {
                    c.SetDurability(stack, remaining + 1);
                    changed = true;
                }
            }
            if (config != null && config.RepairTalismanRepairsClothing && config.RepairTalismanClothingPercentPerMinute > 0 && stack.Attributes.HasAttribute("condition"))
            {
                float condition = stack.Attributes.GetFloat("condition", 1);
                float low = (float)(config.RepairTalismanClothingMinCondition / 100), high = (float)(config.RepairTalismanClothingMaxCondition / 100);
                if (condition >= low && condition < high)
                {
                    float step = (float)(config.RepairTalismanClothingPercentPerMinute / 100 / 60);
                    stack.Attributes.SetFloat("condition", Math.Min(high, condition + step));
                    changed = true;
                }
            }
            if (changed) slot.MarkDirty();
        }

        /// <summary>
        /// Gem of Eternal Density / Void Ring: burns cheap junk you carry (anything worth 8 EMC or less — stone,
        /// soil, sand, gravel, sticks...) and turns it into the selected target (iron, gold, diamond, dark or red matter).
        /// </summary>
        void Condense(IServerPlayer player, ItemSlot gemSlot)
        {
            int mode = GameMath.Clamp(ItemEmcTrinket.GetMode(gemSlot.Itemstack), 0, ItemEmcTrinket.DensityTargets.Length - 1);
            CollectibleObject target = ItemEmcTrinket.Resolve(sapi.World, ItemEmcTrinket.DensityTargets[mode]);
            double targetValue = target == null || mod.Emc.IsNoTransmute(target) ? 0 : mod.Emc.GetValue(target);
            if (targetValue <= 0) return;
            double maxInput = gemSlot.Itemstack.Collectible.Attributes?["maxInputValue"].AsDouble(8) ?? 8;

            double stored = StoredEmc.Get(gemSlot.Itemstack);
            var inv = player.InventoryManager;
            foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory own = inv.GetOwnInventory(name);
                if (own == null) continue;
                foreach (ItemSlot slot in own)
                {
                    ItemStack stack = slot?.Itemstack;
                    if (stack == null || slot == gemSlot || slot is ItemSlotBackpack) continue;
                    CollectibleObject c = stack.Collectible;
                    if (c == target || c is ItemEmcTrinket || c is IEmcStorageItem || c.GetMaxDurability(stack) > 0) continue;
                    if (StackContents.HasNestedStacks(stack)) continue;
                    if (!GemFilter.Passes(gemSlot.Itemstack, c)) continue;
                    double value = mod.Emc.IsNoTransmute(c) ? 0 : mod.Emc.GetValue(stack);
                    if (value <= 0 || value > maxInput) continue;
                    stored += value * stack.StackSize;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }

            int made = 0;
            while (stored >= targetValue && made < 64)
            {
                stored -= targetValue;
                var stack = new ItemStack(target);
                if (!inv.TryGiveItemstack(stack, true)) sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
                made++;
            }
            StoredEmc.Set(gemSlot.Itemstack, stored);
            gemSlot.MarkDirty();
        }

        /// <summary>
        /// Zero Ring: lays snow on bare ground around <paramref name="center"/> (ProjectE's ring does too), a few random
        /// spots at a time so it drifts in rather than blankets everything at once.
        /// </summary>
        public int LaySnow(BlockPos center, int radius, int attempts, IServerPlayer byPlayer)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block snow = sapi.World.GetBlock(new AssetLocation("game:snowlayer-1"));
            if (snow == null || snow.Id == 0) return 0;
            int laid = 0;
            for (int i = 0; i < attempts; i++)
            {
                BlockPos pos = center.AddCopy(rand.Next(-radius, radius + 1), rand.Next(-1, 2), rand.Next(-radius, radius + 1));
                if (ba.GetBlock(pos).Id != 0 || ba.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) continue;
                Block below = ba.GetBlock(pos.DownCopy());
                if (below == null || below.Id == 0 || !below.SideSolid[BlockFacing.UP.Index]) continue;
                if (!MayChange(byPlayer, pos)) continue;
                ba.SetBlock(snow.BlockId, pos);
                laid++;
            }
            return laid;
        }

        /// <summary>Zero Ring sneak + right-click: freezes water within 8 blocks and lays snow within 6, for <see cref="FreezeBurstCost"/>.</summary>
        public void FreezeBurst(IServerPlayer player, bool free)
        {
            if (!free && !Pay(player, FreezeBurstCost)) return;
            BlockPos at = player.Entity.Pos.AsBlockPos;
            Freeze(at, 8, player);
            LaySnow(at, 6, 60, player);
            Extinguish(player.Entity.Pos.XYZ, 8);
            PESounds.Play(sapi.World, PESounds.WaterMagic, "game:sounds/effect/extinguish", player.Entity, 16, 0.8f);
        }

        /// <summary>Harvest Goddess Band sneak + right-click: harvests every ripe crop within 8 blocks and grows the rest a stage, 4 EMC a stage.</summary>
        public void HarvestBurst(IServerPlayer player, bool free)
        {
            BlockPos at = player.Entity.Pos.AsBlockPos;
            int harvested = Harvest(at, 8, player);
            int grown = Grow(at, 8, 64, free ? null : player, free ? 0 : Cost(4));
            if (harvested + grown > 0) sapi.World.PlaySoundAt(new AssetLocation("game:sounds/block/plant"), player.Entity, null, true, 16, 0.8f);
        }

        /// <summary>Mind Stone: keeps the player's body temperature at normal (37 °C), for <see cref="TemperCostPerSecond"/> while it has to.</summary>
        void Temper(IServerPlayer player)
        {
            ITreeAttribute temp = player.Entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
            if (temp == null) return;
            const float normal = 37f;
            float cur = temp.GetFloat("bodytemp", normal);
            if (Math.Abs(cur - normal) < 0.05f) return;
            if (!EmcWallet.TryPay(player, Cost(TemperCostPerSecond * Interval))) return;
            temp.SetFloat("bodytemp", normal);
            player.Entity.WatchedAttributes.MarkPathDirty("bodyTemp");
        }

        /// <summary>
        /// Swiftwolf's Rending Gale fire-projectile key: a lightning strike where the player looks (up to 64 blocks),
        /// through the game's weather system; without it, the creatures there are shocked directly.
        /// </summary>
        public bool Lightning(IServerPlayer player, bool free)
        {
            EntityPlayer e = player.Entity;
            Vec3d eye = e.Pos.XYZ.Add(0, e.LocalEyePos.Y, 0);
            BlockSelection blockSel = null;
            EntitySelection entitySel = null;
            sapi.World.RayTraceForSelection(eye, e.Pos.Pitch, e.Pos.Yaw, 64, ref blockSel, ref entitySel, x => x != e, null);
            Vec3d target = entitySel?.Entity?.Pos.XYZ ?? (blockSel != null ? blockSel.Position.ToVec3d().Add(0.5, 1, 0.5) : null);
            if (target == null) return false;
            if (!sapi.World.Claims.TryAccess(player, target.AsBlockPos, EnumBlockAccessFlags.Use)) return false;
            if (!free && !Pay(player, LightningCost)) return false;
            bool flashed = false;
            try
            {
                ModSystem weather = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().Name == "WeatherSystemServer");
                var m = weather?.GetType().GetMethod("SpawnLightningFlash", new[] { typeof(Vec3d) });
                if (m != null) { m.Invoke(weather, new object[] { target }); flashed = true; }
            }
            catch (Exception ex) { sapi.Logger.Warning("Vintage-E: lightning: {0}", ex.Message); }
            if (!flashed)
            {
                foreach (Entity hit in sapi.World.GetEntitiesAround(target, 3, 3, x => x != e && x is EntityAgent && x.Alive))
                {
                    hit.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, SourceEntity = e, CauseEntity = e, Type = EnumDamageType.Electricity }, 12f);
                    hit.Ignite();
                }
                sapi.World.PlaySoundAt(new AssetLocation("game:sounds/weather/lightning-near"), target.X, target.Y, target.Z, null, true, 48, 1f);
            }
            return true;
        }

        /// <summary>
        /// Dark Matter Sword / Red Matter Katar right-click: hits every hostile creature (or, in "all" mode, every creature
        /// but players and the tamed) within <see cref="SlayRadius"/>, for <see cref="SlayCostEach"/> each.
        /// </summary>
        public int Slay(IServerPlayer player, float damage, bool all)
        {
            EntityPlayer e = player.Entity;
            int hit = 0;
            foreach (Entity target in sapi.World.GetEntitiesAround(e.Pos.XYZ, SlayRadius, SlayRadius / 2, x => all ? x is EntityAgent && !(x is EntityPlayer) && x.Alive && x.WatchedAttributes.GetString("ownerUid") == null : IsHostile(x)))
            {
                if (!EmcWallet.TryPay(player, Cost(SlayCostEach))) break;
                target.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, SourceEntity = e, CauseEntity = e, Type = EnumDamageType.SlashingAttack, KnockbackStrength = 1 }, damage);
                hit++;
            }
            if (hit > 0) PESounds.Play(sapi.World, PESounds.Power, "game:sounds/player/strike", e, 16, 0.8f);
            return hit;
        }

        /// <summary>Black Hole Band / Void Ring right-click: removes a water or lava source block. Returns false if there is none.</summary>
        public bool RemoveLiquidSource(IServerPlayer player, BlockPos pos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
            if (fluid == null || !fluid.IsLiquid() || fluid.LiquidLevel < 7) return false;
            if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return true;
            ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
            ba.TriggerNeighbourBlockUpdate(pos);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/water-pour"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 12, 0.6f);
            return true;
        }

        /// <summary>Void Ring: teleports the player to the block they look at (or <paramref name="range"/>/2 ahead), at most twice a second.</summary>
        public void Teleport(IServerPlayer player, float range)
        {
            long now = sapi.World.ElapsedMilliseconds;
            if (lastTeleportMs.TryGetValue(player.PlayerUID, out long last) && now - last < 500) return;
            EntityPlayer e = player.Entity;
            Vec3d eye = e.Pos.XYZ.Add(0, e.LocalEyePos.Y, 0);
            BlockSelection blockSel = null;
            EntitySelection entitySel = null;
            sapi.World.RayTraceForSelection(eye, e.Pos.Pitch, e.Pos.Yaw, range, ref blockSel, ref entitySel);
            Vec3d target;
            if (blockSel != null)
            {
                BlockPos stand = blockSel.Position.AddCopy(blockSel.Face);
                target = new Vec3d(stand.X + 0.5, stand.Y, stand.Z + 0.5);
            }
            else
            {
                target = eye.AheadCopy(range / 2, e.Pos.Pitch, e.Pos.Yaw);
            }
            if (!sapi.World.Claims.TryAccess(player, target.AsBlockPos, EnumBlockAccessFlags.Use)) return;
            lastTeleportMs[player.PlayerUID] = now;
            e.TeleportToDouble(target.X, target.Y, target.Z);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), e, null, true, 16, 0.6f);
        }

        /// <summary>Places a water or lava source block at <paramref name="pos"/> if nothing solid is in the way. Returns true if placed.</summary>
        public static bool PlaceLiquidAt(IWorldAccessor world, BlockPos pos, bool lava)
        {
            IBlockAccessor ba = world.BlockAccessor;
            Block solid = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (solid != null && solid.Id != 0 && solid.Replaceable < 6000) return false;
            Block liquid = world.GetBlock(new AssetLocation(lava ? "game:lava-still-7" : "game:water-still-7"));
            if (liquid == null || liquid.Id == 0) return false;
            if (solid != null && solid.Id != 0) ba.SetBlock(0, pos, BlockLayersAccess.Solid);
            ba.SetBlock(liquid.BlockId, pos);
            ba.TriggerNeighbourBlockUpdate(pos);
            // ProjectE: the Volcanite Amulet's lava plays its transmute sound, the Evertide Amulet's water its water magic.
            PESounds.Play(world, lava ? PESounds.Transmute : PESounds.WaterMagic, lava ? "game:sounds/effect/extinguish" : "game:sounds/environment/waterfall", pos, 12, 0.6f);
            return true;
        }

        /// <summary>Evertide (water, free) and Volcanite (lava, 32 EMC) amulets: places a source block where you click.</summary>
        public void PlaceLiquid(ItemEmcTrinket amulet, IServerPlayer player, BlockSelection blockSel)
        {
            bool lava = amulet.Kind == "volcaniteamulet";
            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return;
            Block solid = sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            if (solid != null && solid.Id != 0 && solid.Replaceable < 6000) return;
            if (lava && !Pay(player, ItemEmcTrinket.LavaCost)) return;
            PlaceLiquidAt(sapi.World, pos, lava);
        }
    }

    /// <summary>
    /// The Gem of Eternal Density's (and Void Ring's) filter: a row of items on the gem, either "eat everything cheap
    /// except these" (the default, with nothing listed: everything cheap) or "eat only these". Stored on the gem with
    /// the tablet magnet's keys, so the same slots and clicks serve both.
    /// </summary>
    public static class GemFilter
    {
        public const string OnlyKey = "vintageeGemOnly";

        public static bool Only(ItemStack gem) => gem?.Attributes?.GetBool(OnlyKey) == true;

        public static bool Passes(ItemStack gem, CollectibleObject c)
        {
            string key = PlayerEmc.KeyOf(c);
            bool listed = key != null && TabletMagnet.GetFilter(gem).Contains(key);
            return Only(gem) ? listed : !listed;
        }

        /// <summary>The gem the player is using: the one in hand if it's a gem or Void Ring, otherwise the first carried.</summary>
        public static ItemSlot Find(IPlayer player)
        {
            ItemSlot hand = player?.InventoryManager?.ActiveHotbarSlot;
            if ((hand?.Itemstack?.Collectible as ItemEmcTrinket)?.UsesDensityModes == true) return hand;
            return EmcWallet.CarriedSlots(player).FirstOrDefault(s => (s.Itemstack?.Collectible as ItemEmcTrinket)?.UsesDensityModes == true);
        }

        /// <summary>Handles the filter window's clicks (server side).</summary>
        public static void OnRequest(IServerPlayer player, TransmuteRequestPacket req, Action<string, string[]> fail)
        {
            ItemSlot gemSlot = Find(player);
            if (gemSlot == null) { fail("nogem", Array.Empty<string>()); return; }
            ItemStack gem = gemSlot.Itemstack;
            if (req.Action == TransmuteAction.GemMode)
            {
                gem.Attributes.SetBool(OnlyKey, !Only(gem));
                gemSlot.MarkDirty();
                return;
            }
            int i = req.Mode;
            if (i < 0 || i >= TabletMagnet.FilterSlots) return;
            string[] keys = TabletMagnet.GetFilter(gem);
            ItemStack cursor = player.InventoryManager.MouseItemSlot?.Itemstack;
            string key = cursor != null ? PlayerEmc.KeyOf(cursor.Collectible) : null;
            if (key != null && keys.Contains(key)) return;
            keys[i] = key;
            (gem.Attributes as TreeAttribute)?.SetStringArray(TabletMagnet.FilterKey, keys.Select(k => k ?? "").ToArray());
            gemSlot.MarkDirty();
        }
    }
}
