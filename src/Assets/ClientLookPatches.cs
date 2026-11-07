using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VintageE
{
    /// <summary>
    /// Applies the player's look at the moment the game loads shapes for the blocks, items and entity types it got from
    /// the server (the game's ShapeTesselatorManager.LoadBlockShapes / LoadItemShapes, which run on
    /// background threads as soon as those arrive). Mod systems' AssetsFinalize comes too late on the player's side: a
    /// model swapped in after that is never loaded, so the block or item draws nothing (only its outline shows). Each
    /// kind is changed only by its own loader, so the threads never touch the same definitions.
    /// </summary>
    public static class ClientLookPatches
    {
        public const string HarmonyId = "vintagee.clientlook";
        static Harmony harmony;
        /// <summary>How many of the three loader methods were patched (0 before Patch runs), for /emc diagnose.</summary>
        public static int PatchedCount { get; private set; }

        /// <summary>How many loaders get patched: blocks and items. Entity types are never touched on a player's game, so the entity loader is left alone (1.18.5).</summary>
        public const int HookCount = 2;

        /// <summary>The mod system on this player's side (null on a dedicated server).</summary>
        internal static VintageEModSystem Mod;

        /// <summary>Patches the game's shape loading. False (and logged) if this game version has no such methods.</summary>
        /// <summary>What went wrong patching, for the doctor (null when all three applied).</summary>
        public static string PatchError;

        /// <summary>The game's own API object, whose assembly is the one running copy of the game's client code.</summary>
        internal static object Capi;

        public static bool Patch(ILogger logger)
        {
            // Checked every time a client starts, not just once: a patch can have been removed since (an earlier
            // instance of this mod unpatching on its way out, say), and the count must say what is really in place.
            try
            {
                // The type from the assembly the game is actually running, not whichever loaded assembly answers to the
                // name first: a second copy of the game's library (loaded for a mod) has the same type, and a patch on
                // that copy is "in place" yet never called.
                Type manager = Capi?.GetType().Assembly.GetType("Vintagestory.Client.NoObf.ShapeTesselatorManager") ?? AccessTools.TypeByName("Vintagestory.Client.NoObf.ShapeTesselatorManager");
                if (manager == null) { PatchError = "ShapeTesselatorManager not found"; logger.Warning("Look: the game's shape loader wasn't found; using the Middle look."); return false; }
                logger.Notification("Look: hooking the shape loader in {0} ({1}).", manager.Assembly.FullName, manager.Assembly.Location);
                var h = harmony ?? new Harmony(HarmonyId);
                PatchedCount = 0;
                PatchError = null;
                bool all = true;
                all &= PatchOne(h, manager, "LoadBlockShapes", nameof(BlocksPrefix), logger);
                all &= PatchOne(h, manager, "LoadItemShapes", nameof(ItemsPrefix), logger);
                harmony = h;
                logger.Notification("Look: {0} of {1} shape-loader hooks in place{1}.", PatchedCount, HookCount, PatchError == null ? "" : " (" + PatchError + ")");
                return all;
            }
            catch (Exception e)
            {
                PatchError = e.GetType().Name + ": " + e.Message;
                logger.Error("Look: could not patch the game's shape loading: {0}", e);
                return false;
            }
        }

        static bool PatchOne(Harmony h, Type type, string method, string prefix, ILogger logger)
        {
            var target = AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == method);
            if (target == null) { PatchError = (PatchError == null ? "" : PatchError + "; ") + "no " + method; logger.Warning("Look: the game has no {0}; that kind keeps the Middle look.", method); return false; }
            // Already there from an earlier start? Then don't stack a second copy.
            Patches info = Harmony.GetPatchInfo(target);
            bool present = info?.Prefixes?.Any(p => p.owner == HarmonyId) ?? false;
            if (!present) h.Patch(target, prefix: new HarmonyMethod(typeof(ClientLookPatches), prefix));
            info = Harmony.GetPatchInfo(target);
            present = info?.Prefixes?.Any(p => p.owner == HarmonyId) ?? false;
            if (!present) { PatchError = (PatchError == null ? "" : PatchError + "; ") + method + " not patched"; return false; }
            PatchedCount++;
            return true;
        }

        /// <summary>
        /// Forgets this client's mod instance. The patches stay for the life of the game: they do nothing without an
        /// instance, and removing them here used to take them away from the next world's instance when the game
        /// disposed this one late.
        /// </summary>
        public static void Forget(object instance)
        {
            if (ReferenceEquals(Mod, instance)) { Mod = null; Capi = null; }
        }

        /// <summary>Removes the patches outright (not used at run time; kept for tests).</summary>
        public static void Unpatch()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            Mod = null;
        }

        /// <summary>How often each hook fired, and why it did nothing when it did (for the doctor).</summary>
        public static int RawCalls;
        public static string LastSkipReason;

        /// <summary>Only the player's own game (a dedicated or single-player server has no "game" there).</summary>
        static bool OnPlayersSide(object instance, string which)
        {
            RawCalls++;
            if (Mod == null) { LastSkipReason = which + ": no client mod instance"; return false; }
            object game = null;
            try { game = Traverse.Create(instance).Field("game").GetValue(); }
            catch (Exception e) { LastSkipReason = which + ": " + e.GetType().Name; return false; }
            if (game == null) { LastSkipReason = which + ": server-side loader"; return false; }
            return true;
        }

        static void BlocksPrefix(object __instance, IList<Block> blocks)
        {
            VintageEModSystem mod = Mod; // a local: Forget can null the static from the main thread meanwhile
            if (mod != null && OnPlayersSide(__instance, "blocks")) mod.ApplyClientLookTo("blocks", blocks?.Cast<CollectibleObject>(), new List<EntityProperties>());
        }

        static void ItemsPrefix(object __instance, IList<Item> items)
        {
            VintageEModSystem mod = Mod;
            if (mod != null && OnPlayersSide(__instance, "items")) mod.ApplyClientLookTo("items", items?.Cast<CollectibleObject>(), new List<EntityProperties>());
        }
    }
}
