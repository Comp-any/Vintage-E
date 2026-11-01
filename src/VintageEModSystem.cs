using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Entry point. Registers every class, owns the EMC table for its side, and wires up networking.
    /// One instance exists per side (server and client each get their own), so nothing here is static.
    /// </summary>
    public class VintageEModSystem : ModSystem
    {
        public const string ModId = "vintagee";
        public const string ChannelName = "vintagee";
        public const string ConfigFile = "vintagee.json";

        public ICoreAPI Api { get; private set; }
        public ICoreServerAPI Sapi { get; private set; }
        public ICoreClientAPI Capi { get; private set; }

        /// <summary>EMC value of every collectible on this side. Server computes it, client receives it.</summary>
        public EmcRegistry Emc { get; } = new EmcRegistry();

        /// <summary>Server-side settings from ModConfig/vintagee.json.</summary>
        public VintageEConfig Config { get; private set; } = new VintageEConfig();

        /// <summary>Base values, blacklist and world-transmutation rules merged from every mod's config/vintagee-emc.json asset.</summary>
        public EmcRuleSet Rules { get; private set; } = new EmcRuleSet();

        public TransmutationService Transmutation { get; private set; }

        /// <summary>Runs ring/stone/amulet effects (server only).</summary>
        public TrinketEffects Trinkets { get; private set; }

        /// <summary>Result of the most recent mapping run (server only).</summary>
        public EmcMappingReport LastReport { get; private set; }
        WorldTransmutation worldTransmutation;
        /// <summary>Server-side philosopher's stone rules, built on first use from the merged rule files.</summary>
        public WorldTransmutation WorldTransmutation => worldTransmutation ??= new WorldTransmutation(Api, Rules.WorldTransmutation);

        /// <summary>Raised on either side whenever a fresh EMC table is available.</summary>
        public event Action OnEmcTableUpdated;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;
        GuiDialogTransmutation transmutationDialog;

        /// <summary>Hotkey that fires the Hyperkinetic/Catalytic Lens charge (R by default, like ProjectE).</summary>
        public const string FireHotkey = "vintageefireprojectile";

        public override double ExecuteOrder() => 1.0; // after the survival mod has registered its recipe registries

        public override void Start(ICoreAPI api)
        {
            Api = api;

            api.RegisterItemClass("ItemPhilosophersStone", typeof(ItemPhilosophersStone));
            api.RegisterItemClass("ItemTransmutationTablet", typeof(ItemTransmutationTablet));
            api.RegisterBlockClass("BlockTransmutationTable", typeof(BlockTransmutationTable));
            api.RegisterBlockClass("BlockEmcMachine", typeof(BlockEmcMachine));
            api.RegisterBlockEntityClass("VintageEEnergyCollector", typeof(BEEnergyCollector));
            api.RegisterBlockEntityClass("VintageEEnergyCondenser", typeof(BEEnergyCondenser));
            api.RegisterCollectibleBehaviorClass("VintageEMatterTool", typeof(CollectibleBehaviorMatterTool));
            api.RegisterItemClass("ItemKleinStar", typeof(ItemKleinStar));
            api.RegisterItemClass("ItemEmcTrinket", typeof(ItemEmcTrinket));
            api.RegisterItemClass("ItemEmcGadget", typeof(ItemEmcGadget));
            api.RegisterBlockEntityClass("VintageEAlchemicalChest", typeof(BEAlchemicalChest));
            api.RegisterBlockEntityClass("VintageETransmutationTable", typeof(BETransmutationTable));
            api.RegisterBlockEntityClass("VintageEMatterFurnace", typeof(BEMatterFurnaceLegacy)); // 1.4.0 furnaces convert themselves
            api.RegisterBlockEntityClass("VintageEFurnace", typeof(BEMatterFurnace));
            api.RegisterBlockEntityClass("VintageEAntimatterRelay", typeof(BEAntimatterRelay));
            api.RegisterBlockEntityClass("VintageEInterdictionTorch", typeof(BEInterdictionTorch));
            api.RegisterBlockEntityClass("VintageENovaExplosive", typeof(BENovaExplosive));
            api.RegisterBlockEntityClass("VintageEPedestal", typeof(BEPedestal));
            api.RegisterEntityBehaviorClass(EntityBehaviorVintageE.Code, typeof(EntityBehaviorVintageE));
            api.RegisterEntity("VintageEProjectile", typeof(EntityVintageEProjectile));

            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<EmcTablePacket>()
                .RegisterMessageType<TransmuteRequestPacket>()
                .RegisterMessageType<TransmuteResultPacket>()
                .RegisterMessageType<EmcMissingPacket>()
                .RegisterMessageType<EmcSetValuePacket>()
                .RegisterMessageType<WatchSpeedPacket>()
                .RegisterMessageType<FireProjectilePacket>()
                .RegisterMessageType<EmcBlacklistPacket>()
                .RegisterMessageType<EmcContentsPacket>()
                .RegisterMessageType<EmcContentsSetPacket>()
                .RegisterMessageType<EmcConfigPacket>()
                .RegisterMessageType<EmcConfigSetPacket>()
                .RegisterMessageType<EmcRecipeListPacket>()
                .RegisterMessageType<EmcRecipeEditPacket>()
                .RegisterMessageType<EmcItemStateRequestPacket>()
                .RegisterMessageType<EmcItemStatePacket>()
                .RegisterMessageType<EmcItemEditPacket>()
                .RegisterMessageType<EmcReportPacket>()
                .RegisterMessageType<EmcDumpRequestPacket>()
                .RegisterMessageType<EmcCompatListPacket>()
                .RegisterMessageType<EmcCompatSetPacket>();
        }

        #region Server

        /// <summary>The world setting that tells players' games the server's default look.</summary>
        public const string WorldLookKey = "vintageeLook";

        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api is ICoreClientAPI capi)
            {
                ApplyClientLook(capi);
                return;
            }
            if (api.Side != EnumAppSide.Server) return;
            // Read rule files now, while patched asset data is guaranteed to be in memory.
            Rules = EmcRuleSet.Load(api, Mod.Logger, PeekConfig(api as ICoreServerAPI)?.DisabledCompat);
            // Looks are applied on each player's own computer (see ApplyClientLook). The server only tells them its default,
            // through the world's settings, which reach a player's game before its blocks and items are set up. The config
            // is only loaded properly in StartServerSide, which comes later, so peek at that one setting here.
            var sapi = api as ICoreServerAPI;
            VintageEConfig peek = PeekConfig(sapi);
            peek?.Normalize();
            ActiveLook = ProjectELook.NormalizeLook(peek?.Look ?? ProjectELook.LookDefault);
            try { sapi.World.Config?.SetString(WorldLookKey, ActiveLook); }
            catch (Exception e) { Mod.Logger.Warning("Could not tell players the default look: {0}", e.Message); }
            Mod.Logger.Notification("Look: players get {0} unless they pick their own (.vintagee).", ActiveLook);
            if (peek?.SelfTestOnStart == true)
            {
                // For the self-test: both looks applied to copies of Vintage-E's blocks, items and projectiles (the real
                // ones are what players are sent, and a dedicated server frees their textures soon after).
                foreach (string look in new[] { ProjectELook.LookOriginal, ProjectELook.LookNew })
                {
                    // Plain copies (the game's Block.Clone fails on some blocks): a look only ever replaces a definition's
                    // fields, dictionaries and arrays, never changes them in place, so the real ones stay as they were.
                    var copies = api.World.Collectibles.Where(c => c?.Code?.Domain == ModId).Select(c => ShallowCopy(c)).Where(c => c != null).ToList();
                    var types = new List<EntityProperties>();
                    foreach (EntityProperties t in api.World.EntityTypes.Where(t => t?.Code?.Domain == ModId))
                    {
                        EntityProperties copy = ShallowCopy(t);
                        if (copy == null) continue;
                        if (copy.Client != null) copy.Client = ShallowCopy(copy.Client);
                        types.Add(copy);
                    }
                    TestLooks[look] = ProjectELook.Apply(api, Mod.Logger, look, false, copies, types);
                }
            }
        }

        static T ShallowCopy<T>(T obj) where T : class
        {
            try { return (T)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(obj, null); }
            catch { return null; }
        }

        /// <summary>
        /// Player side: applies this player's look (their .vintagee choice, or the server's default) to the blocks, items
        /// and projectiles the server sent, before the game builds their textures and models. Changing it needs a rejoin.
        /// </summary>
        void ApplyClientLook(ICoreClientAPI capi)
        {
            try
            {
                ClientConfig ??= VintageEClientConfig.Load(capi);
                string serverLook = capi.World?.Config?.GetString(WorldLookKey, null);
                string mine = ClientConfig.Look;
                ActiveLook = mine == VintageEClientConfig.LookServer ? ProjectELook.NormalizeLook(serverLook ?? ProjectELook.LookDefault) : ProjectELook.NormalizeLook(mine);
                Mod.Logger.Notification("Look: {0} ({1}; the server's default is {2}).", ActiveLook,
                    mine == VintageEClientConfig.LookServer ? "the server's" : "picked in .vintagee", serverLook ?? "not sent, so New");
                if (ProjectELook.MapFor(ActiveLook) != null) LookResult = ProjectELook.Apply(capi, Mod.Logger, ActiveLook, false);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Could not apply the look: {0}", e);
            }
        }

        /// <summary>The look in use: on the server its default for players, on a player's computer the one they see.</summary>
        public string ActiveLook { get; private set; } = ProjectELook.LookNew;

        /// <summary>What the look changed at start-up on this player's computer (null for the Middle look).</summary>
        public ProjectELook.Result LookResult { get; private set; }

        /// <summary>Server self-test only: each look applied to copies of Vintage-E's blocks and items at start-up.</summary>
        public Dictionary<string, ProjectELook.Result> TestLooks { get; } = new Dictionary<string, ProjectELook.Result>();

        /// <summary>VintagestoryData/ModData/vintagee: where the CSV files, dumps, self-test and cache go (created if needed).</summary>
        public static string DataFolder(ICoreAPI api) => api.GetOrCreateDataPath(System.IO.Path.Combine("ModData", ModId));

        VintageEConfig PeekConfig(ICoreServerAPI sapi)
        {
            if (sapi == null) return null;
            try { return sapi.LoadModConfig<VintageEConfig>(ConfigFile); }
            catch { return null; } // LoadConfig reports a broken file later
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            Sapi = sapi;
            Config = LoadConfig(sapi);
            Transmutation = new TransmutationService(this, sapi);
            Trinkets = new TrinketEffects(this, sapi);

            serverChannel = sapi.Network.GetChannel(ChannelName);
            serverChannel.SetMessageHandler<TransmuteRequestPacket>(Transmutation.OnRequest);
            serverChannel.SetMessageHandler<EmcSetValuePacket>(OnSetValueRequest);
            serverChannel.SetMessageHandler<EmcBlacklistPacket>(OnBlacklistRequest);
            serverChannel.SetMessageHandler<FireProjectilePacket>(OnFireProjectile);
            serverChannel.SetMessageHandler<EmcContentsSetPacket>(OnContentsSetRequest);
            serverChannel.SetMessageHandler<EmcConfigSetPacket>(OnConfigSetRequest);
            serverChannel.SetMessageHandler<EmcRecipeEditPacket>(OnRecipeEditRequest);
            serverChannel.SetMessageHandler<EmcItemStateRequestPacket>(OnItemStateRequest);
            serverChannel.SetMessageHandler<EmcItemEditPacket>(OnItemEditRequest);
            serverChannel.SetMessageHandler<EmcDumpRequestPacket>(OnDumpRequest);
            serverChannel.SetMessageHandler<EmcCompatSetPacket>(OnCompatSetRequest);

            // Every mod has registered its recipes by the time the game starts running, so map then.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                RecalculateEmc("server start", allowCache: true);
                if (Config.SelfTestOnStart) sapi.Event.RegisterCallback(_ => new SelfTest(this, sapi).Start(), 5000);
            });
            sapi.Event.PlayerNowPlaying += player =>
            {
                if (player.Entity != null && player.Entity.GetBehavior<EntityBehaviorVintageE>() == null)
                {
                    // First in line: the game's health behaviour takes the damage off in its own turn, so fall and fire
                    // protection has to zero the damage before it gets there.
                    var bh = new EntityBehaviorVintageE(player.Entity);
                    player.Entity.SidedProperties.Behaviors.Insert(0, bh);
                    player.Entity.CacheServerBehaviors();
                }
                SendEmcTable(player);
                if (Trinkets != null && Trinkets.WatchTimeModifier != 0) serverChannel.SendPacket(new WatchSpeedPacket { Modifier = Trinkets.WatchTimeModifier }, player);
                Announce(player, LastReport, joining: true);
            };

            EmcCommands.Register(this, sapi);
            OnEmcTableUpdated += () => ItemEmcGadget.SetDiviningDurability(sapi.World, Emc);
        }

        readonly Dictionary<string, long> lastShot = new Dictionary<string, long>();

        /// <summary>The fire-projectile key: the held lens, amulet or ring shoots (at most 4 times a second).</summary>
        void OnFireProjectile(IServerPlayer player, FireProjectilePacket packet)
        {
            ItemSlot slot = player?.InventoryManager?.ActiveHotbarSlot;
            if (!(slot?.Itemstack?.Collectible is IVintageEShooter shooter) || !shooter.CanShoot(slot)) return;
            long now = Sapi.World.ElapsedMilliseconds;
            if (lastShot.TryGetValue(player.PlayerUID, out long last) && now - last < 250) return;
            lastShot[player.PlayerUID] = now;
            shooter.Shoot(player, slot);
        }

        VintageEConfig LoadConfig(ICoreServerAPI sapi)
        {
            VintageEConfig cfg = null;
            bool broken = false;
            try
            {
                cfg = sapi.LoadModConfig<VintageEConfig>(ConfigFile);
            }
            catch (Exception e)
            {
                broken = true;
                Mod.Logger.Error("Could not read ModConfig/{0}, using defaults for this session (the file is left untouched so you can fix it): {1}", ConfigFile, e.Message);
            }
            cfg ??= new VintageEConfig();
            cfg.Normalize();
            if (!broken) SaveConfig(cfg); // writes the file on first run and adds any new settings
            return cfg;
        }

        /// <summary>Re-reads ModConfig/vintagee.json (after editing it by hand) and applies it. Returns an error message, or null.</summary>
        public string ReloadConfig()
        {
            VintageEConfig cfg;
            try
            {
                cfg = Sapi.LoadModConfig<VintageEConfig>(ConfigFile);
            }
            catch (Exception e)
            {
                return "Could not read ModConfig/" + ConfigFile + ": " + e.Message;
            }
            if (cfg == null) return "ModConfig/" + ConfigFile + " is missing.";
            cfg.Normalize();
            Config = cfg;
            Trinkets?.ResetHostile();
            RecalculateEmc("config reloaded");
            return null;
        }

        public void SaveConfig(VintageEConfig cfg = null)
        {
            try { Sapi?.StoreModConfig(cfg ?? Config, ConfigFile); }
            catch (Exception e) { Mod.Logger.Warning("Could not write ModConfig/{0}: {1}", ConfigFile, e.Message); }
        }

        /// <summary>
        /// Runs the recipe-graph mapper (or, with <paramref name="allowCache"/>, reuses the saved values if nothing that
        /// affects them has changed) and pushes the result to every connected player.
        /// </summary>
        public EmcMappingReport RecalculateEmc(string reason, bool allowCache = false)
        {
            var sw = Stopwatch.StartNew();
            if (Rules.LoadedFiles.Count == 0 || Rules.DisabledKey != EmcRuleSet.DisabledKeyOf(Config.DisabledCompat))
            {
                Rules = EmcRuleSet.Load(Sapi, Mod.Logger, Config.DisabledCompat);
                worldTransmutation = null;
            }
            var mapper = new EmcMapper(Sapi, Config, Rules, Mod.Logger);
            EmcMappingReport report = null;
            string fingerprint = null;
            if (Config.CacheEmcValues)
            {
                try
                {
                    fingerprint = EmcCache.Fingerprint(Sapi, this, Mod.Logger);
                    if (allowCache && EmcCache.TryLoad(Sapi, fingerprint, Mod.Logger, out EmcCacheFile cached, out var cachedValues, out var cachedVariants))
                    {
                        report = mapper.FromValues(cachedValues);
                        report.Variants = cachedVariants;
                        EmcCache.CopyCounts(cached, report);
                        EmcCache.CopyAnalysis(cached, report, Sapi.World);
                    }
                }
                catch (Exception e)
                {
                    Mod.Logger.Warning("EMC cache check failed, remapping: {0}", e.Message);
                    fingerprint = null;
                }
            }
            if (report == null)
            {
                try
                {
                    report = mapper.Run();
                }
                catch (Exception e)
                {
                    Mod.Logger.Error("EMC mapping failed ({0}): {1}", reason, e);
                    return null;
                }
                if (Config.CacheEmcValues && fingerprint != null) EmcCache.Save(Sapi, fingerprint, report, Mod.Logger);
            }
            Emc.Replace(report.Values);
            Emc.DurabilityScales = Config.DurabilityScalesEmc;
            Emc.ProjectESounds = Config.ProjectESounds;
            Emc.World = Sapi.World;
            BuildStackData(report);
            sw.Stop();
            report.Milliseconds = sw.ElapsedMilliseconds;
            if (report.FromCache)
            {
                Mod.Logger.Notification("EMC values loaded from cache ({0}) in {1} ms: {2} valued collectibles. Nothing that affects them has changed since they were mapped ({3}); /emc recalc forces a fresh mapping.",
                    reason, report.Milliseconds, report.Values.Count, report.CachedAt);
            }
            else Mod.Logger.Notification(
                "EMC mapped ({0}) in {1} ms: {2} valued collectibles ({3} base, {4} from recipes/processing, {5} inferred backwards), {6} conversions from {7} sources.",
                reason, report.Milliseconds, report.Values.Count, report.BaseCount, report.DerivedCount, report.InferredCount,
                report.ConversionCount, report.SourceCount);
            if (report.RelativeCount > 0 || report.SiblingCount > 0 || report.FallbackCount > 0 || report.TraderCount > 0 || report.DefaultCount > 0 || report.GuessCount > 0)
            {
                Mod.Logger.Notification("EMC: items with no recipe path: {0} valued by relativeValues rules, {1} from trader prices, {2} from a variant that only looks or sits differently, {3} other mods' variants of base-game families, {4} as fuel, {5} as food, {6} guessed from other variants, {7} from defaultValues, {8} got the configured FallbackValue.",
                    report.RelativeCount, report.TraderCount, report.SiblingCount, report.OtherModCount, report.FuelCount, report.FoodCount, report.GuessCount, report.DefaultCount, report.FallbackCount);
            }
            if (report.CompatUsed.Count > 0)
            {
                Mod.Logger.Notification("EMC: compatibility values in use for {0}. /emc compat lists every compatibility file and can switch them off.",
                    string.Join(", ", report.CompatUsed.Select(kv => CompatName(kv.Key) + " (" + kv.Value + " items)")));
            }
            var data = Emc.StackData;
            Mod.Logger.Notification("EMC: {0} blocks/items hold contents or keep a material ({1} item only, {2} item + contents, {3} item price only); {4} have material-specific values.",
                data.Kinds.Count, data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.Item), data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.Full),
                data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.ItemPrice), data.Variants.Count);
            LogCoverage(report);
            LastReport = report;
            if (Config.DumpValuesOnStart)
            {
                string file = WriteValuesFile(report);
                if (file != null) Mod.Logger.Notification("EMC values written to {0}", file);
            }
            if (report.LoopFrozen > 0 || report.UnstableRemoved > 0)
            {
                Mod.Logger.Notification("EMC: {0} items sit in self-cheapening recipe loops and keep their first value ({1} of them only settled that way at the end); {2} had to be dropped.", report.LoopFrozen, report.KeptAtFirstValue, report.UnstableRemoved);
            }
            if (report.Loops.Count > 0)
            {
                Mod.Logger.Notification("EMC: {0} value loops keep items from getting a value (each is only made from the next): {1}. /emc loop lists them all.",
                    report.Loops.Count, string.Join(" | ", report.Loops.Take(5).Select(MissingAnalysis.LoopText)));
            }

            serverChannel?.BroadcastPacket(Emc.ToPacket());
            OnEmcTableUpdated?.Invoke();
            // Players joining later get the same line from PlayerNowPlaying.
            foreach (IPlayer online in Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>())
            {
                if (online is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing) Announce(sp, report);
            }
            return report;
        }

        /// <summary>Works out which items carry contents/materials and their modes (cheap; no remapping needed).</summary>
        public void BuildStackData(EmcMappingReport report = null)
        {
            report ??= LastReport;
            try
            {
                Emc.StackData = StackDataTable.Build(Sapi.World, Rules, Config, report?.Variants);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Could not work out which items keep their contents: {0}", e);
            }
            BuildNoTransmute();
        }

        /// <summary>Matches the NoTransmute setting to blocks/items.</summary>
        public void BuildNoTransmute()
        {
            var set = new HashSet<CollectibleObject>();
            if (Config?.NoTransmute != null && Config.NoTransmute.Count > 0)
            {
                var index = new CollectibleIndex(Sapi.World.Collectibles);
                foreach (string pattern in Config.NoTransmute) set.UnionWith(index.Match(pattern));
            }
            Emc.NoTransmute = set;
        }

        /// <summary>Adds or removes codes on the NoTransmute list, saves the config and tells every client.</summary>
        public void SetNoTransmute(IEnumerable<string> configKeys, bool add)
        {
            foreach (string key in configKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (add) { if (!Config.NoTransmute.Contains(key)) Config.NoTransmute.Add(key); }
                else Config.NoTransmute.RemoveAll(k => k == key);
            }
            SaveConfig();
            RefreshStackData();
        }

        /// <summary>After a change to the contents modes: rebuild them and send everyone the new table.</summary>
        public void RefreshStackData()
        {
            BuildStackData();
            serverChannel?.BroadcastPacket(Emc.ToPacket());
            OnEmcTableUpdated?.Invoke();
        }

        /// <summary>
        /// What burning one of this stack gives: its value, plus what's inside when it's burned as the plain item and
        /// ProtectContainersWithContents is off (so the contents aren't simply lost).
        /// </summary>
        public double BurnValue(ItemStack stack)
        {
            if (stack?.Collectible == null) return 0;
            double v = Emc.GetValue(stack);
            if (!Config.ProtectContainersWithContents && Emc.StackData.ModeOf(stack.Collectible) == StackDataMode.Item && StackContents.HasNestedStacks(stack))
            {
                try { v += Emc.ContentsValue(stack); }
                catch { /* contents that can't be read count for nothing */ }
            }
            return v;
        }

        /// <summary>The mod's version as loaded from modinfo.json (falls back to the assembly version).</summary>
        public string Version => Mod?.Info?.Version
            ?? typeof(VintageEModSystem).Assembly.GetName().Version?.ToString(3)
            ?? "?";

        /// <summary>One-line summary of the last mapping, e.g. "Vintage-E version 1.3.0: calculated 4321 EMC values, 12 missing."</summary>
        public string AnnouncementText(EmcMappingReport report, string languageCode = null)
        {
            string key = report.FromCache ? "vintagee:chat-announce-cached" : "vintagee:chat-announce";
            object[] args = { Version, report.Values.Count, report.Missing.Count, report.CreativeTotal, report.Loops?.Count ?? 0 };
            return string.IsNullOrEmpty(languageCode) ? Lang.Get(key, args) : Lang.GetL(languageCode, key, args);
        }

        void Announce(IServerPlayer player, EmcMappingReport report, bool joining = false)
        {
            if (player == null || report == null || !Config.AnnounceInChat) return;
            try
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, AnnouncementText(report, player.LanguageCode), EnumChatType.Notification);
                if (report.CompatUsed.Count > 0)
                {
                    string list = string.Join(", ", report.CompatUsed.Select(kv => Lang.GetL(player.LanguageCode, "vintagee:announce-compat-one", CompatName(kv.Key), kv.Value)));
                    string line = Lang.GetL(player.LanguageCode, "vintagee:announce-compat", list);
                    if (player.HasPrivilege(Privilege.controlserver)) line += " " + Lang.GetL(player.LanguageCode, "vintagee:announce-compat-admin");
                    player.SendMessage(GlobalConstants.GeneralChatGroup, line, EnumChatType.Notification);
                }
                // Admins joining are told the settings can be changed in game.
                if (joining && player.HasPrivilege(Privilege.controlserver))
                {
                    player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "vintagee:announce-configure"), EnumChatType.Notification);
                }
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not send the EMC summary to {0}: {1}", player.PlayerName, e.Message);
            }
        }

        /// <summary>Logs how much real content got a value and writes the leftovers to ModData/vintagee/emc-missing.csv.</summary>
        void LogCoverage(EmcMappingReport report)
        {
            int valued = report.CreativeTotal - report.Missing.Count;
            double pct = report.CreativeTotal == 0 ? 100 : valued * 100.0 / report.CreativeTotal;
            Mod.Logger.Notification("EMC coverage: {0} of {1} blocks/items in the creative inventory have a value ({2:0.0}%).", valued, report.CreativeTotal, pct);
            if (report.Missing.Count == 0) return;

            string top = string.Join(", ", MissingFamilies(report).Take(15).Select(f => f.Key + " (" + f.Value + ")"));
            Mod.Logger.Notification("EMC: {0} without a value, by family: {1}. Full list: {2}. Give them values in ModConfig/{3} (BaseValues) or a config/vintagee-emc.json asset.",
                report.Missing.Count, top, WriteMissingFile(report) ?? "(could not write file)", ConfigFile);
        }

        public static IEnumerable<KeyValuePair<string, int>> MissingFamilies(EmcMappingReport report)
        {
            return report.Missing
                .GroupBy(c => c.Code.Domain + ":" + c.Code.Path.Split('-')[0])
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal);
        }

        /// <summary>Writes every value (and, after a fresh mapping, what it was worked out from) to ModData/vintagee/emc-values.csv.</summary>
        public string WriteValuesFile(EmcMappingReport report)
        {
            try
            {
                string file = System.IO.Path.Combine(DataFolder(Sapi), "emc-values.csv");
                var sb = new System.Text.StringBuilder("type,code,emc,emc_per_litre,via,name\n");
                foreach (var kv in Emc.All.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    string via = report == null ? "" : report.FromCache ? "cached - run /emc recalc to see sources" : report.Origins.TryGetValue(kv.Key, out string o) ? o : "";
                    double perLitre = EmcRegistry.ItemsPerLitre(kv.Key);
                    sb.Append(kv.Key.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',')
                      .Append(kv.Key.Code).Append(',')
                      .Append(kv.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(perLitre > 0 ? (kv.Value * perLitre).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "").Append(',')
                      .Append(Csv(via)).Append(',')
                      .Append(Csv(NameOf(kv.Key))).Append('\n');
                }
                System.IO.File.WriteAllText(file, sb.ToString());

                // Everything the blacklist keeps from having a value, next to it.
                if (report?.Blacklisted != null)
                {
                    var bl = new System.Text.StringBuilder("type,code,in_creative_inventory,name\n");
                    foreach (CollectibleObject c in report.Blacklisted.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
                    {
                        bool listed = (c.CreativeInventoryTabs?.Length ?? 0) > 0 || (c.CreativeInventoryStacks?.Length ?? 0) > 0;
                        bl.Append(c.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',').Append(c.Code).Append(',')
                          .Append(listed ? "yes" : "no").Append(',').Append(Csv(NameOf(c))).Append('\n');
                    }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(DataFolder(Sapi), "emc-blacklisted.csv"), bl.ToString());
                }

                // Items that hold contents or keep a material: their kind, mode and material-specific values.
                var data = Emc.StackData;
                var ct = new System.Text.StringBuilder("type,code,kind,mode,set_by,data,emc,via,name\n");
                foreach (var kv in data.Kinds.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    CollectibleObject c = kv.Key;
                    string type = c.ItemClass == EnumItemClass.Block ? "block" : "item";
                    string head = type + "," + c.Code + "," + kv.Value.ToString().ToLowerInvariant() + "," + StackDataTable.ModeName(data.ModeOf(c)) + "," +
                                  (data.Sources.TryGetValue(c, out string src) ? src : "") + ",";
                    ct.Append(head).Append(",").Append(Emc.GetValue(c).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(",plain item,").Append(Csv(NameOf(c))).Append('\n');
                    if (!data.Variants.TryGetValue(c, out var list)) continue;
                    foreach (VariantValue v in list.OrderBy(v => v.Value))
                    {
                        ct.Append(head).Append(Csv(v.Attributes.ToJsonToken())).Append(',')
                          .Append(v.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append(Csv(v.Origin ?? "")).Append(',').Append(Csv(NameOf(c))).Append('\n');
                    }
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(DataFolder(Sapi), "emc-contents.csv"), ct.ToString());
                return file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not write emc-values.csv: {0}", e.Message);
                return null;
            }
        }

        static string NameOf(CollectibleObject c)
        {
            try { return c.GetHeldItemName(new ItemStack(c)); }
            catch { return c.Code?.ToShortString() ?? ""; }
        }

        static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        public string WriteMissingFile(EmcMappingReport report)
        {
            try
            {
                string file = System.IO.Path.Combine(DataFolder(Sapi), "emc-missing.csv");
                var sb = new System.Text.StringBuilder("type,code,why,name\n");
                foreach (CollectibleObject c in report.Missing.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
                {
                    string why = report.MissingWhy != null && report.MissingWhy.TryGetValue(c, out string w) ? w : "";
                    sb.Append(c.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',').Append(c.Code).Append(',').Append(Csv(why)).Append(',').Append(Csv(NameOf(c))).Append('\n');
                }
                System.IO.File.WriteAllText(file, sb.ToString());
                return file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not write emc-missing.csv: {0}", e.Message);
                return null;
            }
        }

        /// <summary>Sends one player the server's values again (after the server refused something their game showed a value for).</summary>
        public void ResendEmcTable(IServerPlayer player) => SendEmcTable(player);

        void SendEmcTable(IServerPlayer player)
        {
            if (Emc.Count == 0) return; // mapping not done yet; it broadcasts when finished
            serverChannel.SendPacket(Emc.ToPacket(), player);
        }

        public void SendResult(IServerPlayer player, TransmuteResultPacket result)
        {
            serverChannel.SendPacket(result, player);
        }

        /// <summary>
        /// Pins an item's EMC value (saved to ModConfig FixedValues, like /emc set) and remaps. Returns the config key
        /// used, or null if the code didn't resolve.
        /// </summary>
        public string SetFixedValue(CollectibleObject collectible, double? value, bool save = true)
        {
            if (collectible?.Code == null) return null;
            string key = ConfigKey(collectible);
            if (value.HasValue)
            {
                Config.FixedValues[key] = value.Value;
                if (value.Value > 0) Config.Blacklist.Remove(key); // a value takes it off the blacklist
            }
            else Config.FixedValues.Remove(key);
            if (save) SaveConfig();
            return key;
        }

        public static string ConfigKey(CollectibleObject c) => (c.ItemClass == EnumItemClass.Block ? "block:" : "item:") + c.Code;

        /// <summary>
        /// Puts a block/item on the server blacklist (ModConfig Blacklist) or takes it off: removed from Blacklist if
        /// it was added there, otherwise added to Allow so the built-in blacklist no longer applies. Remaps.
        /// </summary>
        public void SetBlacklisted(CollectibleObject c, bool blacklisted, bool save = true) => SetBlacklistedPattern(ConfigKey(c), blacklisted, save);

        /// <summary>Same as <see cref="SetBlacklisted"/> for a config pattern (a code, or "modid:*" for a whole mod).</summary>
        public void SetBlacklistedPattern(string key, bool blacklisted, bool save = true)
        {
            if (blacklisted)
            {
                Config.Allow.Remove(key);
                Config.FixedValues.Remove(key);
                if (!Config.Blacklist.Contains(key)) Config.Blacklist.Add(key);
            }
            else if (!Config.Blacklist.Remove(key) && !Config.Allow.Contains(key))
            {
                Config.Allow.Add(key);
            }
            if (save) SaveConfig();
        }

        void OnBlacklistRequest(IServerPlayer player, EmcBlacklistPacket packet)
        {
            string lang = player.LanguageCode;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            if (packet.List == (byte)EmcListKind.NoTransmute)
            {
                OnNoTransmuteRequest(player, packet);
                return;
            }
            if (packet.List == (byte)EmcListKind.GrayList)
            {
                OnGrayListRequest(player, packet);
                return;
            }
            string msg;
            if (!string.IsNullOrEmpty(packet.Domain))
            {
                // A whole mod. Not the base game: blacklisting it would remove every vanilla value, and taking it off
                // wholesale would give values to creative-only and technical blocks.
                if (packet.Domain == "game")
                {
                    player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:missing-nogame"), EnumChatType.CommandError);
                    return;
                }
                SetBlacklistedPattern(packet.Domain + ":*", packet.Add);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-blacklistedmod" : "vintagee:cmd-unblacklistedmod", packet.Domain);
            }
            else
            {
                List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
                if (items.Count == 0) return;
                foreach (CollectibleObject c in items) SetBlacklisted(c, packet.Add, save: false);
                SaveConfig();
                msg = items.Count > 1 ? Lang.GetL(lang, packet.Add ? "vintagee:cmd-blacklistedmany" : "vintagee:cmd-unblacklistedmany", items.Count) : null;
                if (items.Count == 1)
                {
                    CollectibleObject c = items[0];
                    msg = packet.Add ? Lang.GetL(lang, "vintagee:cmd-blacklisted", NameOf(c), c.Code) : null;
                }
            }
            EmcMappingReport report = RecalculateEmc(packet.Add ? "blacklisted from the /emc missing window" : "removed from the blacklist");
            if (msg == null)
            {
                CollectibleObject c = ResolveKeys(Sapi.World, packet.Key, packet.Keys)[0];
                double v = Emc.GetValue(c);
                msg = v > 0 ? Lang.GetL(lang, "vintagee:cmd-unblacklisted-value", NameOf(c), c.Code, EmcFormat.Format(v))
                    : Lang.GetL(lang, "vintagee:cmd-unblacklisted", NameOf(c), c.Code);
            }
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (report != null) SendMissingList(player, report, null, FromKind(packet.FromKind, packet.FromBlacklist));
        }

        /// <summary>The no-transmute window's buttons: put items on the list or take them (or a whole mod) off. Values don't change.</summary>
        void OnNoTransmuteRequest(IServerPlayer player, EmcBlacklistPacket packet)
        {
            string lang = player.LanguageCode;
            string msg;
            if (!string.IsNullOrEmpty(packet.Domain))
            {
                // Taking a whole mod off removes its "modid:*" entry and every entry of its own blocks/items.
                string prefix = packet.Domain + ":";
                var keys = Config.NoTransmute.Where(k => k == packet.Domain + ":*" || StripType(k).StartsWith(prefix, StringComparison.Ordinal)).ToList();
                if (packet.Add) keys = new List<string> { packet.Domain + ":*" };
                SetNoTransmute(keys, packet.Add);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-notransmute-addedmod" : "vintagee:cmd-notransmute-removedmod", packet.Domain);
            }
            else
            {
                List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
                if (items.Count == 0) return;
                SetNoTransmute(items.Select(ConfigKey), packet.Add);
                int still = packet.Add ? 0 : items.Count(Emc.IsNoTransmute);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-notransmute-added" : "vintagee:cmd-notransmute-removed", items.Count);
                if (still > 0) msg += " " + Lang.GetL(lang, "vintagee:cmd-notransmute-stillwildcard", still);
            }
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (LastReport != null) SendMissingList(player, LastReport, null, FromKind(packet.FromKind, packet.FromBlacklist));
        }

        /// <summary>
        /// The gray list's buttons (from the missing window: "Gray-list it"; from the gray list window: take items or a
        /// whole mod off it). The values are worked out again, since gray-listed things get no guessed value.
        /// </summary>
        void OnGrayListRequest(IServerPlayer player, EmcBlacklistPacket packet)
        {
            string lang = player.LanguageCode;
            string msg;
            if (!string.IsNullOrEmpty(packet.Domain))
            {
                string prefix = packet.Domain + ":";
                var keys = packet.Add ? new List<string> { packet.Domain + ":*" }
                    : Config.GrayList.Where(k => k == packet.Domain + ":*" || StripType(k).StartsWith(prefix, StringComparison.Ordinal)).ToList();
                SetGrayList(keys, packet.Add);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-gray-addedmod" : "vintagee:cmd-gray-removedmod", packet.Domain);
            }
            else
            {
                List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
                if (items.Count == 0) return;
                SetGrayList(items.Select(ConfigKey), packet.Add);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-gray-added" : "vintagee:cmd-gray-removed", items.Count);
            }
            EmcMappingReport report = RecalculateEmc(packet.Add ? "gray-listed from the /emc missing window" : "taken off the gray list");
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (report != null) SendMissingList(player, report, null, FromKind(packet.FromKind, packet.FromBlacklist));
        }

        /// <summary>Adds or removes codes on the GrayList setting and saves it (the caller remaps).</summary>
        public void SetGrayList(IEnumerable<string> configKeys, bool add)
        {
            foreach (string key in configKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (add) { if (!Config.GrayList.Contains(key)) Config.GrayList.Add(key); }
                else Config.GrayList.RemoveAll(k => k == key);
            }
            SaveConfig();
        }

        static string StripType(string key) => key.StartsWith("item:") ? key.Substring(5) : key.StartsWith("block:") ? key.Substring(6) : key;

        static EmcListKind FromKind(byte kind, bool fromBlacklist) => kind != 0 ? (EmcListKind)kind : fromBlacklist ? EmcListKind.Blacklist : EmcListKind.Missing;

        static List<CollectibleObject> ResolveKeys(IWorldAccessor world, string key, string[] keys)
        {
            var list = new List<CollectibleObject>();
            foreach (string k in keys != null && keys.Length > 0 ? keys : new[] { key })
            {
                CollectibleObject c = PlayerEmc.Resolve(world, k);
                if (c?.Code != null && !list.Contains(c)) list.Add(c);
            }
            return list;
        }

        void OnSetValueRequest(IServerPlayer player, EmcSetValuePacket packet)
        {
            string lang = player.LanguageCode;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            if (!packet.Clear && (double.IsNaN(packet.Value) || double.IsInfinity(packet.Value) || packet.Value < 0)) return;
            List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
            if (items.Count == 0) return;
            if (packet.Clear && items.All(c => !Config.FixedValues.ContainsKey(ConfigKey(c))))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:cmd-nopin", ConfigKey(items[0])), EnumChatType.CommandError);
                return;
            }
            foreach (CollectibleObject c in items) SetFixedValue(c, packet.Clear ? (double?)null : packet.Value, save: false);
            SaveConfig();
            EmcMappingReport report = RecalculateEmc(packet.Quiet ? "hovered-item value box" : "/emc missing window");
            string msg;
            if (items.Count == 1)
            {
                double now = Emc.GetValue(items[0]);
                msg = now > 0
                    ? Lang.GetL(lang, "vintagee:cmd-itemvalue", NameOf(items[0]), items[0].Code, EmcFormat.Format(now))
                    : Lang.GetL(lang, "vintagee:cmd-novalue", NameOf(items[0]), items[0].Code);
            }
            else msg = Lang.GetL(lang, "vintagee:cmd-setmany", items.Count, EmcFormat.Format(packet.Value));
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (report != null && !packet.Quiet) SendMissingList(player, report, null, FromKind(packet.FromKind, packet.FromBlacklist));
        }

        void OnItemStateRequest(IServerPlayer player, EmcItemStateRequestPacket packet)
        {
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            CollectibleObject c = PlayerEmc.Resolve(Sapi.World, packet?.Key);
            if (c?.Code == null) return;
            serverChannel.SendPacket(ItemState(c, packet.Key), player);
        }

        /// <summary>Saves a report's text to ModData/vintagee/<paramref name="fileName"/> (the report remembers where).</summary>
        public string SaveReport(EmcReport report, string fileName)
        {
            try
            {
                string file = System.IO.Path.Combine(DataFolder(Sapi), fileName);
                System.IO.File.WriteAllText(file, report.PlainText(Sapi.World) + "\n");
                report.File = file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Vintage-E: could not write {0}: {1}", fileName, e.Message);
                report.File = null;
            }
            return report.File;
        }

        /// <summary>Opens the report window (/emc dump, /emc loop) for a player.</summary>
        public void SendReport(IServerPlayer player, EmcReport report) => serverChannel.SendPacket(report.ToPacket(), player);

        public static string DumpFileName(CollectibleObject c) => "emc-dump-" + c.Code.ToString().Replace(':', '_').Replace('/', '_').Replace('*', '_') + ".txt";

        /// <summary>An item clicked in the report window (or its Refresh): explain that one, as a new one; or list the loops again.</summary>
        void OnDumpRequest(IServerPlayer player, EmcDumpRequestPacket packet)
        {
            if (packet == null) return;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            try
            {
                EmcReport report;
                if (packet.Loops)
                {
                    report = EmcExplain.Loops(this, Sapi, player.LanguageCode);
                    SaveReport(report, "emc-loops.txt");
                }
                else
                {
                    ItemStack stack = null;
                    if (packet.Stack != null && packet.Stack.Length > 0)
                    {
                        try
                        {
                            stack = new ItemStack(packet.Stack);
                            if (!stack.ResolveBlockOrItem(Sapi.World)) stack = null;
                        }
                        catch { stack = null; }
                    }
                    bool exact = stack?.Collectible?.Code != null;
                    if (!exact)
                    {
                        CollectibleObject c = PlayerEmc.Resolve(Sapi.World, packet.Key);
                        if (c?.Code == null) return;
                        stack = new ItemStack(c);
                    }
                    report = EmcExplain.Dump(this, Sapi, stack, player, exact, player.LanguageCode);
                    SaveReport(report, DumpFileName(stack.Collectible));
                }
                SendReport(player, report);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Vintage-E: explaining {0} failed: {1}", packet.Loops ? "the loops" : packet.Key, e);
                player.SendMessage(GlobalConstants.GeneralChatGroup, "Vintage-E: that failed (" + e.Message + "), see the server log.", EnumChatType.CommandError);
            }
        }

        internal EmcItemStatePacket ItemState(CollectibleObject c, string key)
        {
            EmcMappingReport report = LastReport;
            string cfgKey = ConfigKey(c);
            bool free = report?.Free?.Contains(c) == true;
            bool gray = report?.Gray?.Contains(c) == true;
            // What only the value files (or a wildcard) say can't be undone for this one item from the box.
            var fromFiles = new List<string>();
            if (gray && !Config.GrayList.Contains(cfgKey)) fromFiles.Add("gray");
            if (free && !Config.FreeIngredients.Contains(cfgKey)) fromFiles.Add("free");
            return new EmcItemStatePacket
            {
                Key = key,
                Value = Emc.GetValue(c),
                Pinned = Config.FixedValues.ContainsKey(cfgKey),
                Blacklisted = !free && report?.Blacklisted?.Contains(c) == true,
                Gray = gray,
                Free = free,
                FromFiles = fromFiles.ToArray()
            };
        }

        /// <summary>The hovered-item box's Save: the free ingredient, blacklist and gray list boxes, then the value (or unpin); one remap.</summary>
        void OnItemEditRequest(IServerPlayer player, EmcItemEditPacket packet)
        {
            string lang = player.LanguageCode;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            CollectibleObject c = PlayerEmc.Resolve(Sapi.World, packet?.Key);
            if (c?.Code == null) return;
            if (packet.SetValue && (double.IsNaN(packet.Value) || double.IsInfinity(packet.Value) || packet.Value < 0)) return;
            EmcItemStatePacket before = ItemState(c, packet.Key);
            string key = ConfigKey(c);
            var notes = new List<string>();

            if (packet.Free != before.Free)
            {
                if (packet.Free)
                {
                    if (!Config.FreeIngredients.Contains(key)) Config.FreeIngredients.Add(key);
                    Config.FixedValues.Remove(key);
                }
                else if (!Config.FreeIngredients.Remove(key)) notes.Add(Lang.GetL(lang, "vintagee:setvalue-freefromfiles"));
            }
            if (packet.Blacklist != before.Blacklisted) SetBlacklisted(c, packet.Blacklist, save: false);
            if (packet.Gray != before.Gray)
            {
                if (packet.Gray) { if (!Config.GrayList.Contains(key)) Config.GrayList.Add(key); }
                else if (Config.GrayList.RemoveAll(k => k == key) == 0) notes.Add(Lang.GetL(lang, "vintagee:setvalue-grayfromfiles"));
            }
            if (packet.Unpin) Config.FixedValues.Remove(key);
            else if (packet.SetValue && !packet.Blacklist && !packet.Free) SetFixedValue(c, packet.Value, save: false);
            SaveConfig();
            RecalculateEmc("hovered-item value box");

            EmcItemStatePacket after = ItemState(c, packet.Key);
            string state = after.Free ? Lang.GetL(lang, "vintagee:setvalue-isfree")
                : after.Blacklisted ? Lang.GetL(lang, "vintagee:setvalue-isblacklisted")
                : after.Value > 0 ? EmcFormat.Format(after.Value) + " EMC" : Lang.GetL(lang, "vintagee:setvalue-novalue");
            if (after.Gray) state += ", " + Lang.GetL(lang, "vintagee:setvalue-isgray");
            string msg = Lang.GetL(lang, "vintagee:setvalue-result", NameOf(c), c.Code, state);
            if (notes.Count > 0) msg += " " + string.Join(" ", notes);
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
        }

        /// <summary>A compatibility file's display name, from the rules (or its file name when it isn't known).</summary>
        public string CompatName(string source)
        {
            EmcRuleSet.CompatFile f = Rules?.Compat?.FirstOrDefault(c => c.Source == source);
            if (f != null) return f.Name;
            string file = source.Substring(source.LastIndexOf('/') + 1);
            return file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 5) : file;
        }

        /// <summary>Opens (or refreshes) the /emc compat window: every compatibility value file, whether its mod is loaded, whether it's disabled, and how many items it valued.</summary>
        public void SendCompatList(IServerPlayer player, string message = null, bool error = false)
        {
            var files = Rules?.Compat ?? new List<EmcRuleSet.CompatFile>();
            var used = LastReport?.CompatUsed ?? new SortedDictionary<string, int>(StringComparer.Ordinal);
            serverChannel.SendPacket(new EmcCompatListPacket
            {
                Keys = files.Select(f => f.Source).ToArray(),
                Names = files.Select(f => f.Name).ToArray(),
                Descriptions = files.Select(f => f.Description ?? "").ToArray(),
                Mods = files.Select(f => string.Join(", ", f.RequiredMods)).ToArray(),
                MissingMods = files.Select(f => f.MissingMod ?? "").ToArray(),
                Disabled = files.Select(f => f.Disabled).ToArray(),
                RuleCounts = files.Select(f => f.RuleCount).ToArray(),
                UsedCounts = files.Select(f => used.TryGetValue(f.Source, out int n) ? n : 0).ToArray(),
                Message = message ?? "",
                Error = error
            }, player);
        }

        /// <summary>Disables or enables a compatibility file (DisabledCompat in the server config) and remaps. Returns the message for the player.</summary>
        public string SetCompatDisabled(string key, bool disabled, string lang, out bool error)
        {
            error = true;
            EmcRuleSet.CompatFile f = Rules?.Compat?.FirstOrDefault(c => c.Source.Equals(key, StringComparison.OrdinalIgnoreCase)
                || c.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || EmcRuleSet.IsDisabled(c.Source, new[] { key }));
            if (f == null) return Lang.GetL(lang, "vintagee:compat-unknown", key);
            if (f.Disabled == disabled) { error = false; return Lang.GetL(lang, disabled ? "vintagee:compat-alreadyoff" : "vintagee:compat-alreadyon", f.Name); }
            Config.DisabledCompat.RemoveAll(d => EmcRuleSet.IsDisabled(f.Source, new[] { d }));
            if (disabled) Config.DisabledCompat.Add(f.Source.Substring(f.Source.LastIndexOf('/') + 1).Replace(".json", ""));
            SaveConfig();
            EmcMappingReport report = RecalculateEmc(disabled ? "/emc compat disable" : "/emc compat enable");
            if (report == null) return Lang.GetL(lang, "vintagee:compat-failed");
            error = false;
            int n = report.CompatUsed.TryGetValue(f.Source, out int used) ? used : 0;
            return Lang.GetL(lang, disabled ? "vintagee:compat-disabled" : "vintagee:compat-enabled", f.Name, n);
        }

        void OnCompatSetRequest(IServerPlayer player, EmcCompatSetPacket packet)
        {
            if (packet?.Key == null) return;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            string message = SetCompatDisabled(packet.Key, packet.Disabled, player.LanguageCode, out bool error);
            SendCompatList(player, message, error);
        }

        /// <summary>Opens (or refreshes) the /emc recipe window for a player.</summary>
        public void SendRecipeList(IServerPlayer player, string message = null, bool error = false)
        {
            var problems = LastReport?.CustomRecipeProblems ?? new List<string>();
            var lines = Config.CustomRecipes.ToList();
            serverChannel.SendPacket(new EmcRecipeListPacket
            {
                Lines = lines.ToArray(),
                Problems = lines.Select(l => ProblemOf(problems, l)).ToArray(),
                Values = lines.Select(l =>
                {
                    CustomRecipe r = CustomRecipe.Parse(l, out _);
                    CollectibleObject output = r == null ? null : ResolveCode(r.Output);
                    double v = output == null ? 0 : Emc.GetValue(output);
                    return v > 0 ? EmcFormat.Format(v) : "";
                }).ToArray(),
                Message = message ?? "",
                Error = error,
                FromFiles = Rules?.Recipes?.Count ?? 0
            }, player);
        }

        /// <summary>The problem the mapper had with a recipe line ("" for none): its problems read "line  (why)".</summary>
        static string ProblemOf(List<string> problems, string line)
        {
            string p = problems.FirstOrDefault(x => x.StartsWith(line + "  (", StringComparison.Ordinal));
            return p == null ? "" : p.Substring(line.Length + 3).TrimEnd(')');
        }

        /// <summary>A block or item from a recipe's code: "item:domain:path", "block:…", or "domain:path" (item first).</summary>
        public CollectibleObject ResolveCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            if (code.StartsWith("item:")) return Sapi.World.GetItem(new AssetLocation(code.Substring(5)));
            if (code.StartsWith("block:")) return Sapi.World.GetBlock(new AssetLocation(code.Substring(6)));
            var loc = new AssetLocation(code);
            return (CollectibleObject)Sapi.World.GetItem(loc) ?? Sapi.World.GetBlock(loc);
        }

        /// <summary>
        /// Adds a hand-written recipe to the server config (replacing <paramref name="replaces"/> when editing) and remaps.
        /// Returns the message for the player; a recipe the mapper can't use is taken back off.
        /// </summary>
        public string AddCustomRecipe(string line, string replaces, string lang, out bool error)
        {
            error = true;
            CustomRecipe r = CustomRecipe.Parse(line, out string why);
            if (r == null) return Lang.GetL(lang, "vintagee:recipe-bad", why);
            string clean = r.ToString();
            CollectibleObject output = ResolveCode(r.Output);
            if (output == null) return Lang.GetL(lang, "vintagee:recipe-bad", "no block or item " + r.Output);
            int at = replaces == null ? -1 : Config.CustomRecipes.IndexOf(replaces);
            if (at >= 0) Config.CustomRecipes.RemoveAt(at);
            if (Config.CustomRecipes.Contains(clean)) { error = false; return Lang.GetL(lang, "vintagee:recipe-already"); }
            if (at >= 0) Config.CustomRecipes.Insert(at, clean); else Config.CustomRecipes.Add(clean);
            SaveConfig();
            EmcMappingReport report = RecalculateEmc("/emc recipe");
            string problem = report == null ? null : ProblemOf(report.CustomRecipeProblems, clean);
            if (!string.IsNullOrEmpty(problem))
            {
                Config.CustomRecipes.Remove(clean);
                if (at >= 0) Config.CustomRecipes.Insert(at, replaces);
                SaveConfig();
                RecalculateEmc("/emc recipe (undone)");
                return Lang.GetL(lang, "vintagee:recipe-bad", problem);
            }
            error = false;
            double v = Emc.GetValue(output);
            return v > 0
                ? Lang.GetL(lang, "vintagee:recipe-added", NameOf(output), EmcFormat.Format(v))
                : Lang.GetL(lang, "vintagee:recipe-addednovalue", NameOf(output));
        }

        /// <summary>Takes a hand-written recipe off the server config and remaps.</summary>
        public string RemoveCustomRecipe(string line, string lang, out bool error)
        {
            error = !Config.CustomRecipes.Remove(line);
            if (error) return Lang.GetL(lang, "vintagee:recipe-notfound", line);
            SaveConfig();
            RecalculateEmc("/emc recipe remove");
            return Lang.GetL(lang, "vintagee:recipe-removed", line);
        }

        void OnRecipeEditRequest(IServerPlayer player, EmcRecipeEditPacket packet)
        {
            if (packet?.Line == null) return;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            bool error;
            string message = packet.Remove
                ? RemoveCustomRecipe(packet.Line, player.LanguageCode, out error)
                : AddCustomRecipe(packet.Line, packet.Replaces, player.LanguageCode, out error);
            SendRecipeList(player, message, error);
        }

        /// <summary>Opens the /emc contents window for a player.</summary>
        public void SendContentsList(IServerPlayer player)
        {
            StackDataTable table = Emc.StackData;
            // What each would be without the server config: the table built with an empty StackDataModes.
            var plainConfig = new VintageEConfig { StackDataModes = new Dictionary<string, string>() };
            StackDataTable defaults = StackDataTable.Build(Sapi.World, Rules, plainConfig, table.Variants);
            var keys = new List<string>();
            var kinds = new List<byte>();
            var modes = new List<byte>();
            var defaultModes = new List<byte>();
            var fromConfig = new List<bool>();
            foreach (var kv in table.Kinds)
            {
                string key = PlayerEmc.KeyOf(kv.Key);
                if (key == null) continue;
                keys.Add(key);
                kinds.Add((byte)kv.Value);
                modes.Add((byte)table.ModeOf(kv.Key));
                defaultModes.Add((byte)(defaults.CarriesData(kv.Key) ? defaults.ModeOf(kv.Key) : StackDataMode.Item));
                fromConfig.Add(table.Sources.TryGetValue(kv.Key, out string src) && src == "server config");
            }
            serverChannel.SendPacket(new EmcContentsPacket
            {
                Keys = keys.ToArray(),
                Kinds = kinds.ToArray(),
                Modes = modes.ToArray(),
                DefaultModes = defaultModes.ToArray(),
                FromConfig = fromConfig.ToArray(),
                Version = Version
            }, player);
        }

        /// <summary>
        /// Sets (or with <paramref name="mode"/> null, clears) the server config's contents mode for blocks/items and
        /// sends everyone the new table. Values don't change, so nothing is remapped.
        /// </summary>
        public void SetStackDataModes(IEnumerable<CollectibleObject> items, StackDataMode? mode)
        {
            foreach (CollectibleObject c in items)
            {
                string key = ConfigKey(c);
                if (mode == null) Config.StackDataModes.Remove(key);
                else Config.StackDataModes[key] = StackDataTable.ModeName(mode.Value);
            }
            SaveConfig();
            RefreshStackData();
        }

        void OnContentsSetRequest(IServerPlayer player, EmcContentsSetPacket packet)
        {
            if (packet == null || !player.HasPrivilege(Privilege.controlserver)) return;
            string lang = player.LanguageCode;
            var items = (packet.Keys ?? Array.Empty<string>()).Select(k => PlayerEmc.Resolve(Sapi.World, k)).Where(c => c != null && !(c is IEmcStorageItem)).Distinct().ToList();
            if (items.Count == 0) return;
            StackDataMode? mode = packet.Mode < 0 ? (StackDataMode?)null : (StackDataMode)Math.Clamp(packet.Mode, 0, 2);
            SetStackDataModes(items, mode);
            string modeName = mode == null ? Lang.GetL(lang, "vintagee:contents-mode-default") : Lang.GetL(lang, "vintagee:contents-mode-" + StackDataTable.ModeName(mode.Value));
            string msg = items.Count == 1
                ? Lang.GetL(lang, "vintagee:cmd-contents-set", NameOf(items[0]), modeName)
                : Lang.GetL(lang, "vintagee:cmd-contents-setmany", items.Count, modeName);
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            SendContentsList(player);
        }

        /// <summary>Opens (or refreshes) the /emc configure window for a player.</summary>
        public void SendConfig(IServerPlayer player, string message = null, bool error = false, string selected = null)
        {
            var (names, values) = ConfigureCommand.Snapshot(Config);
            serverChannel.SendPacket(new EmcConfigPacket { Names = names, Values = values, Defaults = ConfigureCommand.DefaultTexts(), Message = message ?? "", Error = error, Selected = selected }, player);
        }

        void OnConfigSetRequest(IServerPlayer player, EmcConfigSetPacket packet)
        {
            if (packet?.Name == null || !player.HasPrivilege(Privilege.controlserver)) return;
            string message;
            bool error = false;
            try
            {
                message = packet.Reset
                    ? ConfigureCommand.Reset(this, packet.Name, player.LanguageCode)
                    : ConfigureCommand.SetFromText(this, packet.Name, packet.Text ?? "", player.LanguageCode);
            }
            catch (FormatException e)
            {
                message = e.Message;
                error = true;
            }
            SendConfig(player, message, error, packet.Name == "*" ? null : packet.Name);
        }

        /// <summary>Tells every client about a change to the watch's time speed, so their clocks and skies keep up.</summary>
        public void BroadcastWatchSpeed(float modifier) => serverChannel?.BroadcastPacket(new WatchSpeedPacket { Modifier = modifier });

        /// <summary>Opens the /emc missing window (or with <paramref name="blacklist"/>, the /emc blacklist window) for a player.</summary>
        public void SendMissingList(IServerPlayer player, EmcMappingReport report, string file, EmcListKind kind = EmcListKind.Missing)
        {
            string WhyOf(CollectibleObject c) => report.MissingWhy != null && report.MissingWhy.TryGetValue(c, out string w) ? w : "";
            List<CollectibleObject> main, other;
            switch (kind)
            {
                case EmcListKind.Blacklist:
                    main = report.Blacklisted;
                    other = report.Missing; // untick "Hide missing" to mix these in
                    break;
                case EmcListKind.GrayList:
                    main = report.Gray ?? new List<CollectibleObject>();
                    other = report.Missing; // untick "Hide missing" to gray-list some
                    break;
                case EmcListKind.NoTransmute:
                    main = Emc.NoTransmute.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList();
                    // "Show valued items": everything else in the creative inventory that has a value.
                    other = report.Values.Keys.Where(c => CollectibleIndex.IsListed(c) && !Emc.NoTransmute.Contains(c)).ToList();
                    break;
                default:
                    main = report.Missing;
                    other = report.Blacklisted; // "Show blacklisted"
                    break;
            }
            main = main.Where(c => PlayerEmc.KeyOf(c) != null).ToList();
            other = other.Where(c => PlayerEmc.KeyOf(c) != null).ToList();
            serverChannel.SendPacket(new EmcMissingPacket
            {
                Keys = main.Select(PlayerEmc.KeyOf).ToArray(),
                Why = kind == EmcListKind.Missing ? main.Select(WhyOf).ToArray()
                    : kind == EmcListKind.GrayList ? main.Select(c => report.Values.TryGetValue(c, out double v)
                        ? Lang.GetL(player.LanguageCode, "vintagee:gray-valued", EmcFormat.Format(v))
                        : Lang.GetL(player.LanguageCode, "vintagee:gray-waiting")).ToArray()
                    : null,
                OtherKeys = other.Select(PlayerEmc.KeyOf).ToArray(),
                OtherWhy = kind == EmcListKind.Blacklist || kind == EmcListKind.GrayList ? other.Select(WhyOf).ToArray() : null,
                CreativeTotal = report.CreativeTotal,
                ValueCount = report.Values.Count,
                Version = Version,
                File = file,
                Blacklist = kind == EmcListKind.Blacklist,
                Kind = (byte)kind
            }, player);
        }

        #endregion

        #region Client

        public override void StartClientSide(ICoreClientAPI capi)
        {
            Capi = capi;
            clientChannel = capi.Network.GetChannel(ChannelName);
            clientChannel.SetMessageHandler<EmcTablePacket>(OnEmcTableReceived);
            clientChannel.SetMessageHandler<TransmuteResultPacket>(OnTransmuteResult);
            clientChannel.SetMessageHandler<EmcMissingPacket>(OnMissingList);
            clientChannel.SetMessageHandler<EmcContentsPacket>(OnContentsList);
            clientChannel.SetMessageHandler<EmcConfigPacket>(OnConfigList);
            clientChannel.SetMessageHandler<WatchSpeedPacket>(p =>
            {
                ClientWatchModifier = p.Modifier;
                TrinketEffects.ApplyTimeModifier(capi.World.Calendar, p.Modifier);
                watchHud?.Update();
            });

            // This computer's settings (the look, the watch's icon and glow), and the .vintagee command that opens them.
            ClientConfig ??= VintageEClientConfig.Load(capi);
            capi.ChatCommands.Create("vintagee")
                .WithDescription("Vintage-E settings for this computer (the look of its blocks and items, how the Watch of Flowing Time shows it's on)")
                .HandleWith(_ =>
                {
                    clientSettingsDialog?.TryClose();
                    clientSettingsDialog = new GuiDialogClientSettings(capi, this);
                    clientSettingsDialog.TryOpen();
                    return TextCommandResult.Success();
                });
            capi.Event.LevelFinalize += () => watchHud = new HudWatchIndicator(capi, this);

            capi.Event.RegisterGameTickListener(WindTick, 250);
            OnEmcTableUpdated += () => ItemEmcGadget.SetDiviningDurability(capi.World, Emc);

            capi.Input.RegisterHotKey(FireHotkey, Lang.Get("vintagee:hotkey-fire"), GlKeys.R, HotkeyType.CharacterControls);
            capi.Input.SetHotKeyHandler(FireHotkey, _ =>
            {
                ItemSlot slot = capi.World.Player?.InventoryManager?.ActiveHotbarSlot;
                if (!(slot?.Itemstack?.Collectible is IVintageEShooter shooter) || !shooter.CanShoot(slot)) return false;
                clientChannel?.SendPacket(new FireProjectilePacket { Hand = 0 });
                return true;
            });

            // Admins: set the EMC value of whatever is under the mouse (inventory, handbook, any window).
            capi.Input.RegisterHotKey(SetValueHotkey, Lang.Get("vintagee:hotkey-setvalue"), GlKeys.K, HotkeyType.GUIOrOtherControls, ctrlPressed: true);
            capi.Input.SetHotKeyHandler(SetValueHotkey, _ => OpenSetValueBox());
            clientChannel.SetMessageHandler<EmcRecipeListPacket>(OnRecipeList);
            clientChannel.SetMessageHandler<EmcCompatListPacket>(OnCompatList);
            clientChannel.SetMessageHandler<EmcItemStatePacket>(OnItemState);
            clientChannel.SetMessageHandler<EmcReportPacket>(OnReport);

            // Tooltips: give every collectible a tiny client-only behaviour that prints its EMC.
            capi.Event.BlockTexturesLoaded += () =>
            {
                int count = 0;
                foreach (CollectibleObject obj in capi.World.Collectibles)
                {
                    if (obj?.Code == null) continue;
                    var bh = new CollectibleBehaviorEmcTooltip(obj, this);
                    var list = new List<CollectibleBehavior>(obj.CollectibleBehaviors ?? Array.Empty<CollectibleBehavior>()) { bh };
                    obj.CollectibleBehaviors = list.ToArray();
                    count++;
                }
                Mod.Logger.VerboseDebug("Added EMC tooltips to {0} collectibles", count);
            };
        }

        ILoadedSound windSound;

        /// <summary>
        /// ProjectE plays a looping wind while you fly with Swiftwolf's Rending Gale. With ProjectE's sounds on, that plays
        /// while you're in the air on flight the ring switched on - not flight from another mod (an angel belt, say).
        /// </summary>
        void WindTick(float dt)
        {
            IClientPlayer player = Capi?.World?.Player;
            bool flying = player?.WorldData != null && player.Entity != null && PlaysRingWind(Emc.ProjectESounds, player.WorldData.FreeMove,
                player.WorldData.CurrentGameMode, player.Entity.OnGround, player.Entity.WatchedAttributes.GetBool(TrinketEffects.FlightKey));
            if (flying && windSound == null)
            {
                windSound = Capi.World.LoadSound(new SoundParams
                {
                    Location = PESounds.Location(PESounds.WindMagic),
                    ShouldLoop = true,
                    RelativePosition = true,
                    Position = new Vec3f(),
                    DisposeOnFinish = false,
                    Volume = 0.6f,
                    SoundType = EnumSoundType.Ambient
                });
                windSound?.Start();
            }
            else if (!flying && windSound != null)
            {
                windSound.Stop();
                windSound.Dispose();
                windSound = null;
            }
        }

        /// <summary>
        /// Whether the ring's wind plays: ProjectE's sounds on, flying in survival, off the ground, and the flight is the
        /// ring's (<see cref="TrinketEffects.FlightKey"/>, which the server sets only when the ring switched flight on).
        /// </summary>
        public static bool PlaysRingWind(bool projectESounds, bool freeMove, EnumGameMode mode, bool onGround, bool ringFlight) =>
            projectESounds && freeMove && ringFlight && mode == EnumGameMode.Survival && !onGround;

        /// <summary>Goes up by one every time the client gets a new EMC table (windows use it to know their values are stale).</summary>
        public int TableVersion { get; private set; }

        void OnEmcTableReceived(EmcTablePacket packet)
        {
            Emc.LoadFromPacket(packet, Capi.World);
            TableVersion++;
            OnEmcTableUpdated?.Invoke();
        }

        void OnTransmuteResult(TransmuteResultPacket packet)
        {
            if (!string.IsNullOrEmpty(packet.ErrorCode))
            {
                Capi.TriggerIngameError(this, packet.ErrorCode, Lang.Get("vintagee:error-" + packet.ErrorCode, packet.Args ?? Array.Empty<string>()));
            }
            // Learned at the table, or by the tablet's magnet while the window is shut.
            if (packet.LearnedKey != null)
            {
                ItemStack learned = PlayerEmc.ResolveStack(Capi.World, packet.LearnedKey);
                if (learned != null) Capi.ShowChatMessage(Lang.Get("vintagee:learned", learned.GetName()));
            }
            transmutationDialog?.OnServerResult(packet);
        }

        public void SendTransmuteRequest(TransmuteRequestPacket packet) => clientChannel?.SendPacket(packet);

        public const string SetValueHotkey = "vintagee-setvalue";

        /// <summary>Client: this computer's settings (ModConfig/vintagee-client.json).</summary>
        public VintageEClientConfig ClientConfig { get; private set; }
        /// <summary>Client: the watch's change to the world's time speed (0 = none), from the server.</summary>
        public float ClientWatchModifier { get; private set; }
        HudWatchIndicator watchHud;
        GuiDialogClientSettings clientSettingsDialog;
        LoadedTexture watchGlowTexture;
        string watchGlowColor;

        /// <summary>The client settings changed in their window: save them and show the change.</summary>
        public void OnClientConfigChanged()
        {
            ClientConfig?.Save(Capi);
            watchHud?.Update(true);
        }

        /// <summary>
        /// Client: a plain texture of the watch's glow colour, drawn over the watch (as the game draws rot over food)
        /// while it's on; null when the glow is off in the settings.
        /// </summary>
        public LoadedTexture WatchGlowTexture()
        {
            if (Capi == null || ClientConfig == null || !ClientConfig.WatchGlow) return null;
            if (watchGlowTexture != null && watchGlowColor == ClientConfig.WatchGlowColor) return watchGlowTexture;
            if (!VintageEClientConfig.TryParseColor(ClientConfig.WatchGlowColor, out int rgb)) rgb = 0x66CCFF;
            int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
            int pixel = (255 << 24) | (b << 16) | (g << 8) | r; // RGBA bytes in memory order
            int[] pixels = Enumerable.Repeat(pixel, 16).ToArray();
            watchGlowTexture ??= new LoadedTexture(Capi, 0, 4, 4);
            watchGlowTexture.Width = 4;
            watchGlowTexture.Height = 4;
            Capi.Render.LoadOrUpdateTextureFromRgba(pixels, false, 0, ref watchGlowTexture);
            watchGlowColor = ClientConfig.WatchGlowColor;
            return watchGlowTexture;
        }

        /// <summary>Tooltip line naming the key the fire-projectile control is bound to now (it can be changed in the game's controls).</summary>
        public static void AppendFireKey(IWorldAccessor world, System.Text.StringBuilder dsc)
        {
            if (!(world?.Api is ICoreClientAPI capi)) return;
            string key = null;
            try { key = capi.Input.GetHotKeyByCode(FireHotkey)?.CurrentMapping?.ToString(); }
            catch { /* controls not loaded yet */ }
            dsc.AppendLine(string.IsNullOrEmpty(key) ? Lang.Get("vintagee:firekey-unbound") : Lang.Get("vintagee:firekey-line", key));
        }

        GuiDialogEmcSetValue setValueDialog;

        bool OpenSetValueBox()
        {
            IClientPlayer player = Capi?.World?.Player;
            if (player == null) return false;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                Capi.TriggerIngameError(this, "noprivilege", Lang.Get("vintagee:error-noprivilege"));
                return true;
            }
            ItemStack stack = HoveredStack.Find(Capi);
            if (stack?.Collectible?.Code == null)
            {
                Capi.TriggerIngameError(this, "nothinghovered", Lang.Get("vintagee:setvalue-nothing"));
                return true;
            }
            string key = PlayerEmc.KeyOf(stack.Collectible);
            if (key == null) return true;
            // Ask the server about its lists first; the box opens when the answer comes (OnItemState).
            pendingSetValueStack = stack.Clone();
            clientChannel?.SendPacket(new EmcItemStateRequestPacket { Key = key });
            return true;
        }

        ItemStack pendingSetValueStack;

        void OnItemState(EmcItemStatePacket packet)
        {
            ItemStack stack = pendingSetValueStack;
            if (stack == null || PlayerEmc.KeyOf(stack.Collectible) != packet.Key) return;
            pendingSetValueStack = null;
            string key = packet.Key;
            setValueDialog?.TryClose();
            setValueDialog = new GuiDialogEmcSetValue(Capi, stack, packet, edit =>
            {
                edit.Key = key;
                clientChannel?.SendPacket(edit);
            }, () =>
            {
                byte[] bytes = null;
                try { bytes = stack.ToBytes(); }
                catch { /* explain a new one instead */ }
                clientChannel?.SendPacket(new EmcDumpRequestPacket { Key = key, Stack = bytes });
            });
            setValueDialog.TryOpen();
        }

        /// <summary>The report window's Set value button: the hovered-item value box, for the item it explains.</summary>
        void OpenSetValueFor(string key)
        {
            CollectibleObject c = PlayerEmc.Resolve(Capi.World, key);
            if (c?.Code == null) return;
            pendingSetValueStack = new ItemStack(c);
            clientChannel?.SendPacket(new EmcItemStateRequestPacket { Key = key });
        }

        GuiDialogEmcReport reportDialog;

        void OnReport(EmcReportPacket packet)
        {
            if (reportDialog != null && reportDialog.IsOpened())
            {
                reportDialog.Show(packet);
                return;
            }
            reportDialog = new GuiDialogEmcReport(Capi, packet,
                (key, stack) => clientChannel?.SendPacket(new EmcDumpRequestPacket { Key = key, Stack = stack }),
                () => clientChannel?.SendPacket(new EmcDumpRequestPacket { Loops = true }),
                OpenSetValueFor);
            reportDialog.TryOpen();
        }

        GuiDialogEmcRecipe recipeDialog;

        void OnRecipeList(EmcRecipeListPacket packet)
        {
            if (recipeDialog != null && recipeDialog.IsOpened())
            {
                recipeDialog.Update(packet);
                return;
            }
            recipeDialog = new GuiDialogEmcRecipe(Capi, packet, (line, replaces) => clientChannel?.SendPacket(new EmcRecipeEditPacket { Line = line, Replaces = replaces }),
                line => clientChannel?.SendPacket(new EmcRecipeEditPacket { Line = line, Remove = true }));
            recipeDialog.TryOpen();
        }

        GuiDialogEmcCompat compatDialog;

        void OnCompatList(EmcCompatListPacket packet)
        {
            if (compatDialog != null && compatDialog.IsOpened())
            {
                compatDialog.Update(packet);
                return;
            }
            compatDialog = new GuiDialogEmcCompat(Capi, packet, (key, disabled) => clientChannel?.SendPacket(new EmcCompatSetPacket { Key = key, Disabled = disabled }));
            compatDialog.TryOpen();
        }

        GuiDialogEmcMissing missingDialog;

        void OnMissingList(EmcMissingPacket packet)
        {
            EmcListKind kind = packet.Kind != 0 ? (EmcListKind)packet.Kind : packet.Blacklist ? EmcListKind.Blacklist : EmcListKind.Missing;
            byte from = (byte)kind;
            // The no-transmute and gray list windows edit their own lists; the missing and blacklist windows edit the blacklist.
            byte list = kind == EmcListKind.NoTransmute || kind == EmcListKind.GrayList ? (byte)kind : (byte)0;
            GuiDialogEmcMissing previous = missingDialog != null && missingDialog.IsOpened() && missingDialog.Kind == kind ? missingDialog : null;
            var dialog = new GuiDialogEmcMissing(Capi, packet, previous,
                (keys, value) => clientChannel?.SendPacket(new EmcSetValuePacket { Keys = keys, Value = value, FromBlacklist = packet.Blacklist, FromKind = from }),
                (keys, add) => clientChannel?.SendPacket(new EmcBlacklistPacket { Keys = keys, Add = add, FromBlacklist = packet.Blacklist, FromKind = from, List = list }),
                (domain, add) => clientChannel?.SendPacket(new EmcBlacklistPacket { Domain = domain, Add = add, FromBlacklist = packet.Blacklist, FromKind = from, List = list }),
                (keys, add) => clientChannel?.SendPacket(new EmcBlacklistPacket { Keys = keys, Add = add, FromKind = from, List = (byte)EmcListKind.GrayList }));
            missingDialog?.TryClose();
            missingDialog?.Dispose();
            missingDialog = dialog;
            missingDialog.TryOpen();
        }

        /// <summary>Opens the transmutation GUI (from a table at <paramref name="tablePos"/>, or from a tablet when null).</summary>
        GuiDialogEmcConfig configDialog;

        void OnConfigList(EmcConfigPacket packet)
        {
            if (configDialog != null && configDialog.IsOpened())
            {
                configDialog.Update(packet);
                return;
            }
            configDialog = new GuiDialogEmcConfig(Capi, packet, (name, text) => clientChannel.SendPacket(new EmcConfigSetPacket { Name = name, Text = text }),
                name => clientChannel.SendPacket(new EmcConfigSetPacket { Name = name, Reset = true }));
            configDialog.TryOpen();
        }

        GuiDialogEmcContents contentsDialog;

        void OnContentsList(EmcContentsPacket packet)
        {
            GuiDialogEmcContents previous = contentsDialog != null && contentsDialog.IsOpened() ? contentsDialog : null;
            var dialog = new GuiDialogEmcContents(Capi, packet, previous, (keys, mode) => clientChannel.SendPacket(new EmcContentsSetPacket { Keys = keys, Mode = mode }));
            previous?.TryClose();
            contentsDialog = dialog;
            dialog.TryOpen();
        }

        public void OpenTransmutationDialog(Vintagestory.API.MathTools.BlockPos tablePos)
        {
            if (Capi == null) return;
            if (transmutationDialog != null && transmutationDialog.IsOpened())
            {
                transmutationDialog.TryClose();
                return;
            }
            transmutationDialog?.Dispose();
            transmutationDialog = new GuiDialogTransmutation(Capi, this, tablePos);
            transmutationDialog.OnClosed += () =>
            {
                SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Close });
            };
            transmutationDialog.TryOpen();
        }

        #endregion

        public override void Dispose()
        {
            transmutationDialog?.Dispose();
            missingDialog?.Dispose();
            watchHud?.Dispose();
            watchGlowTexture?.Dispose();
            windSound?.Stop();
            windSound?.Dispose();
            windSound = null;
            if (Capi != null) ProjectELook.DisposeMeshes();
            try { (Sapi?.World ?? (IWorldAccessor)Capi?.World)?.Calendar?.RemoveTimeSpeedModifier(TrinketEffects.TimeModifierName); }
            catch { /* world already gone */ }
            base.Dispose();
        }
    }
}
