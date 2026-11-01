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
    /// the server (the game's ShapeTesselatorManager.LoadBlockShapes / LoadItemShapes / LoadEntityShapes, which run on
    /// background threads as soon as those arrive). Mod systems' AssetsFinalize comes too late on the player's side: a
    /// model swapped in after that is never loaded, so the block or item draws nothing (only its outline shows). Each
    /// kind is changed only by its own loader, so the threads never touch the same definitions.
    /// </summary>
    public static class ClientLookPatches
    {
        const string HarmonyId = "vintagee.clientlook";
        static Harmony harmony;

        /// <summary>The mod system on this player's side (null on a dedicated server).</summary>
        internal static VintageEModSystem Mod;

        /// <summary>Patches the game's shape loading. False (and logged) if this game version has no such methods.</summary>
        public static bool Patch(ILogger logger)
        {
            if (harmony != null) return true;
            try
            {
                Type manager = AccessTools.TypeByName("Vintagestory.Client.NoObf.ShapeTesselatorManager");
                if (manager == null) { logger.Warning("Look: the game's shape loader wasn't found; using the Middle look."); return false; }
                var h = new Harmony(HarmonyId);
                bool all = true;
                all &= PatchOne(h, manager, "LoadBlockShapes", nameof(BlocksPrefix), logger);
                all &= PatchOne(h, manager, "LoadItemShapes", nameof(ItemsPrefix), logger);
                all &= PatchOne(h, manager, "LoadEntityShapes", nameof(EntitiesPrefix), logger);
                harmony = h;
                return all;
            }
            catch (Exception e)
            {
                logger.Error("Look: could not patch the game's shape loading: {0}", e);
                return false;
            }
        }

        static bool PatchOne(Harmony h, Type type, string method, string prefix, ILogger logger)
        {
            var target = AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == method);
            if (target == null) { logger.Warning("Look: the game has no {0}; that kind keeps the Middle look.", method); return false; }
            h.Patch(target, prefix: new HarmonyMethod(typeof(ClientLookPatches), prefix));
            return true;
        }

        public static void Unpatch()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            Mod = null;
        }

        /// <summary>Only the player's own game (a dedicated or single-player server has no "game" there).</summary>
        static bool OnPlayersSide(object instance) => Mod != null && Traverse.Create(instance).Field("game").GetValue() != null;

        static void BlocksPrefix(object __instance, IList<Block> blocks)
        {
            if (OnPlayersSide(__instance)) Mod.ApplyClientLookTo("blocks", blocks?.Cast<CollectibleObject>(), new List<EntityProperties>());
        }

        static void ItemsPrefix(object __instance, IList<Item> items)
        {
            if (OnPlayersSide(__instance)) Mod.ApplyClientLookTo("items", items?.Cast<CollectibleObject>(), new List<EntityProperties>());
        }

        static void EntitiesPrefix(object __instance, IEnumerable<EntityProperties> entities)
        {
            if (OnPlayersSide(__instance)) Mod.ApplyClientLookTo("entities", Array.Empty<CollectibleObject>(), entities?.ToList());
        }
    }
}
