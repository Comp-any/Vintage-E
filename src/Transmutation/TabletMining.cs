using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// "Burn what I mine" (1.29.3, Project Expansion's Alchemical Collection as a tablet checkbox): while a player carries a
    /// Transmutation Tablet with it ticked, what drops from the blocks they break is burned into their EMC and learned,
    /// through the tablet's filter (the magnet's: only these, or all except these; an empty filter burns everything).
    /// <para>How: the game's BreakBlock event opens a window for that player; every dropped item spawned while it's open
    /// (<c>OnEntitySpawn</c>; drops are spawned in the same call) is noted; DidBreakBlock closes it and burns them. That
    /// catches the player's own break, the game's tree felling and in-tool area mining (the matter tools' 3×3), and
    /// only that player's drops. A container's contents spill through <see cref="InventoryBase.DropAll"/>, which pauses
    /// the window (a Harmony patch), so a broken chest's contents are never burned. A window still open on the next tick
    /// (a break that threw) is shut without burning anything.</para>
    /// </summary>
    public static class TabletMining
    {
        public const string Key = "vintageeMineBurn";
        public const string HarmonyId = "vintagee.tabletmining";

        static VintageEModSystem mod;
        static ICoreServerAPI sapi;
        static IServerPlayer breaking;
        static ItemStack breakingTablet;
        static readonly List<EntityItem> caught = new List<EntityItem>();
        static int paused;

        public static bool Patched { get; private set; }
        public static string Error { get; private set; }
        /// <summary>Items burned this way since the server started (the doctor and the test read it).</summary>
        public static long Burned { get; private set; }

        public static bool On(ItemStack tablet) => tablet?.Collectible is ItemTransmutationTablet && tablet.Attributes?.GetBool(Key) == true;

        /// <summary>Whether this kind of item is burned by this tablet's filter: an empty filter burns everything.</summary>
        public static bool Wants(ItemStack tablet, CollectibleObject c, IWorldAccessor world)
        {
            if (c == null) return false;
            CollectibleObject[] filter = TabletMagnet.FilterItems(tablet, world);
            if (filter.Length == 0) return true;
            return (Array.IndexOf(filter, c) >= 0) != TabletMagnet.Excludes(tablet);
        }

        /// <summary>The tablet that burns what this player mines: the one in hand if it's ticked, else the first carried one that is.</summary>
        public static ItemStack TabletOf(IPlayer player)
        {
            ItemStack held = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (On(held)) return held;
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                if (On(slot.Itemstack)) return slot.Itemstack;
            }
            return null;
        }

        /// <summary>Who has it ticked on a tablet they carry now (the doctor's line).</summary>
        public static List<string> Users(ICoreServerAPI api) =>
            api.World.AllOnlinePlayers.Where(p => TabletOf(p) != null).Select(p => p.PlayerName).ToList();

        public static void Start(VintageEModSystem system, ICoreServerAPI api)
        {
            mod = system;
            sapi = api;
            api.Event.BreakBlock += OnBreak;
            api.Event.DidBreakBlock += OnDidBreak;
            api.Event.OnEntitySpawn += OnSpawn;
            if (Patched) return;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.DropAll));
                if (target == null) { Error = "InventoryBase.DropAll wasn't found"; return; }
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(typeof(TabletMining), nameof(Pause)),
                    finalizer: new HarmonyMethod(typeof(TabletMining), nameof(Resume)));
                Patched = true;
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                api.Logger.Warning("Vintage-E: couldn't hook the game's container spill ({0}); \"Burn what I mine\" is off for safety.", Error);
            }
        }

        /// <summary>The server stopped: let go of it (the patch stays, idle, for the next world).</summary>
        public static void Stop()
        {
            if (sapi != null)
            {
                sapi.Event.BreakBlock -= OnBreak;
                sapi.Event.DidBreakBlock -= OnDidBreak;
                sapi.Event.OnEntitySpawn -= OnSpawn;
            }
            Close();
            mod = null;
            sapi = null;
        }

        static void Pause() => paused++;

        static Exception Resume(Exception __exception)
        {
            if (paused > 0) paused--;
            return __exception;
        }

        static void OnBreak(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
        {
            Close();
            if (!Patched || byPlayer?.Entity == null || byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative) return;
            ItemStack tablet = TabletOf(byPlayer);
            if (tablet == null) return;
            breaking = byPlayer;
            breakingTablet = tablet;
            IServerPlayer opened = byPlayer;
            // A break that throws never reaches DidBreakBlock: shut the window on the next tick, burning nothing.
            sapi.Event.RegisterCallback(_ => { if (breaking == opened) Close(); }, 0);
        }

        static void OnSpawn(Entity entity)
        {
            if (breaking != null && paused == 0 && entity is EntityItem item) caught.Add(item);
        }

        static void OnDidBreak(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel)
        {
            if (breaking == null || byPlayer != breaking) { Close(); return; }
            IServerPlayer player = breaking;
            ItemStack tablet = breakingTablet;
            var items = caught.ToList();
            Close();
            try { Burn(player, tablet, items); }
            catch (Exception e) { Guard.Report(sapi.Logger, "burning what " + player.PlayerName + " mined", e); }
        }

        /// <summary>Burns the caught drops that pass the tablet's filter and can be burned; the rest stay where they fell.</summary>
        public static int Burn(IServerPlayer player, ItemStack tablet, IEnumerable<EntityItem> items)
        {
            var service = mod?.Transmutation;
            if (service == null) return 0;
            int burned = 0;
            foreach (EntityItem ei in items)
            {
                ItemStack stack = ei?.Itemstack;
                if (ei == null || !ei.Alive || stack?.Collectible == null || !Wants(tablet, stack.Collectible, sapi.World)) continue;
                if (mod.BurnValue(stack) <= 0) continue;
                if (service.PlanBurn(player, stack, out TransmutationService.BurnPlan plan, out _) != null) continue;
                string learned = service.ApplyBurn(player, stack, plan);
                burned += stack.StackSize;
                ei.Itemstack = null;
                ei.Die(EnumDespawnReason.PickedUp);
                if (learned != null) mod.SendResult(player, new TransmuteResultPacket { LearnedKey = learned });
            }
            Burned += burned;
            return burned;
        }

        static void Close()
        {
            breaking = null;
            breakingTablet = null;
            caught.Clear();
        }
    }
}
