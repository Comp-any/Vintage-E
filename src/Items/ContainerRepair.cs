using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// The Repair Talisman in ordinary containers: chests, storage vessels, crates and other mods' storage blocks.
    /// <para>Off unless the server names the containers in <see cref="VintageEConfig.RepairTalismanContainers"/>
    /// (block codes, wildcards allowed, "*" for every container): by default the talisman only works carried, in an
    /// Alchemical Chest and on a pedestal, as before. In a named container it repairs everything stored beside it the
    /// way it does in the Alchemical Chest: 1 durability a second on tools, weapons and armour, and clothing at the
    /// configured rate.</para>
    /// <para>Ordinary containers have no tick of their own for this, so every few seconds the loaded chunks are looked
    /// through for named containers holding a talisman, and those are then repaired once a second. With the list
    /// empty nothing is looked at.</para>
    /// </summary>
    public sealed class ContainerRepair
    {
        /// <summary>How often loaded chunks are searched for containers that hold a talisman.</summary>
        public const int SearchEverySeconds = 10;

        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        /// <summary>Containers that held a talisman at the last search (or since), repaired every second.</summary>
        readonly HashSet<BlockPos> tracked = new HashSet<BlockPos>();
        /// <summary>Whether each block is a container the list names, by block id (cleared when the list changes).</summary>
        readonly Dictionary<int, bool> allowedById = new Dictionary<int, bool>();
        AssetLocation[] patterns = Array.Empty<AssetLocation>();
        string patternsKey;
        int secondsToSearch;

        public ContainerRepair(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
            sapi.Event.RegisterGameTickListener(_ => Tick(), 1000);
        }

        /// <summary>How many containers are being repaired in now.</summary>
        public int TrackedCount => tracked.Count;

        /// <summary>One second's work, as the tick listener does it (the self-test calls it directly).</summary>
        public void TickOnce() => Tick();

        void Tick()
        {
            try
            {
                if (!ReadList())
                {
                    tracked.Clear();
                    return;
                }
                if (--secondsToSearch <= 0)
                {
                    secondsToSearch = SearchEverySeconds;
                    Search();
                }
                RepairTracked();
            }
            catch (Exception e) { sapi.Logger.Warning("Vintage-E: Repair Talisman in containers: {0}", e.Message); }
        }

        /// <summary>Reads the setting (again when it changed). False when it names no container.</summary>
        bool ReadList()
        {
            List<string> list = mod.Config?.RepairTalismanContainers;
            if (list == null || list.Count == 0) { patternsKey = null; return false; }
            string key = string.Join("\n", list);
            if (key == patternsKey) return patterns.Length > 0;
            patternsKey = key;
            var found = new List<AssetLocation>();
            foreach (string entry in list)
            {
                string p = entry?.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                found.Add(p == "*" ? new AssetLocation("*:*") : new AssetLocation(p));
            }
            patterns = found.ToArray();
            allowedById.Clear();
            secondsToSearch = 0; // look for talismans in the newly named containers at once
            return patterns.Length > 0;
        }

        /// <summary>Whether the setting names this block's container. The mod's own blocks have their own rules and are never included.</summary>
        public bool Allows(Block block)
        {
            if (block?.Code == null || block.Code.Domain == "vintagee") return false;
            if (allowedById.TryGetValue(block.Id, out bool known)) return known;
            bool match = false;
            foreach (AssetLocation pattern in patterns)
            {
                try
                {
                    if (pattern.Domain == "*" ? WildcardUtil.Match(pattern.Path, block.Code.Path) : WildcardUtil.Match(pattern, block.Code)) { match = true; break; }
                }
                catch (Exception e) { sapi.Logger.Warning("Vintage-E: RepairTalismanContainers entry {0} can't be used: {1}", pattern, e.Message); }
            }
            allowedById[block.Id] = match;
            return match;
        }

        /// <summary>Whether a switched-on (always-on) Repair Talisman lies in this inventory.</summary>
        public static bool HoldsTalisman(IInventory inventory)
        {
            if (inventory == null) return false;
            foreach (ItemSlot slot in inventory)
            {
                if (slot?.Itemstack?.Collectible is ItemEmcTrinket t && t.Kind == "repairtalisman") return true;
            }
            return false;
        }

        /// <summary>Every loaded chunk's block entities: named containers holding a talisman are repaired from now on.</summary>
        void Search()
        {
            foreach (IServerChunk chunk in sapi.WorldManager.AllLoadedChunks.Values)
            {
                var entities = chunk?.BlockEntities;
                if (entities == null || entities.Count == 0) continue;
                foreach (BlockEntity be in entities.Values)
                {
                    if (be?.Pos == null || !(be is IBlockEntityContainer container) || !Allows(be.Block)) continue;
                    if (HoldsTalisman(container.Inventory)) tracked.Add(be.Pos.Copy());
                }
            }
        }

        readonly List<BlockPos> gone = new List<BlockPos>();

        void RepairTracked()
        {
            if (tracked.Count == 0) return;
            gone.Clear();
            foreach (BlockPos pos in tracked)
            {
                BlockEntity be = sapi.World.BlockAccessor.GetBlockEntity(pos);
                // Broken, unloaded, no longer named, or the talisman was taken out: dropped until a search finds it again.
                if (!(be is IBlockEntityContainer container) || !Allows(be.Block) || !RepairIn(container.Inventory, mod.Config)) gone.Add(pos);
            }
            foreach (BlockPos pos in gone) tracked.Remove(pos);
        }

        /// <summary>One second of a talisman in this inventory. False (and nothing repaired) when it holds none.</summary>
        public static bool RepairIn(IInventory inventory, VintageEConfig config)
        {
            if (!HoldsTalisman(inventory)) return false;
            foreach (ItemSlot slot in inventory) TrinketEffects.RepairOnce(slot, config);
            return true;
        }

        /// <summary>The setting changed: forget what was found and search again on the next tick.</summary>
        public void Reset()
        {
            patternsKey = null;
            allowedById.Clear();
            tracked.Clear();
            secondsToSearch = 0;
        }
    }
}
