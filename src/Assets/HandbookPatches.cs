using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// The Emc Craft link on handbook pages (1.19.0): a Harmony postfix on the game's page builder,
    /// CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo, adds it after the page is built.
    ///
    /// Not the game's ICustomHandbookPageContent: the game asks each item for one provider and takes the first, so on
    /// items whose class or another behavior already provides one (the game's fruit tree branches; Rustbound Magic's
    /// items in the 100-mod test) Vintage-E's was never asked. The builder runs on the main thread when a page opens,
    /// long after loading, and the postfix only adds text components to what it returns, so none of the shape
    /// loaders' timing and thread problems apply. Patched once per game process; if a game version renames the
    /// method, the patch isn't made, the log says so, and pages just have no link.
    /// </summary>
    public static class HandbookPatches
    {
        public const string HarmonyId = "vintagee.handbook";
        static bool patched;

        /// <summary>The mod system on the player's side, for the postfix.</summary>
        internal static VintageEModSystem Mod;

        /// <summary>Why the patch wasn't made (null when it was, or before trying).</summary>
        public static string Error { get; private set; }
        public static bool Patched => patched;

        public static System.Reflection.MethodInfo Target() => typeof(CollectibleBehaviorHandbookTextAndExtraInfo).GetMethod("GetHandbookInfo",
            new[] { typeof(ItemSlot), typeof(ICoreClientAPI), typeof(ItemStack[]), typeof(ActionConsumable<string>) });

        public static void Patch(VintageEModSystem mod, ILogger logger)
        {
            Mod = mod;
            if (patched) return;
            try
            {
                var target = Target();
                if (target == null || target.ReturnType != typeof(RichTextComponentBase[]))
                {
                    Error = "the game's handbook page builder (GetHandbookInfo) wasn't found";
                    logger.Warning("Vintage-E: {0}; handbook pages won't have the Emc Craft link.", Error);
                    return;
                }
                new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(typeof(HandbookPatches), nameof(Postfix)));
                patched = true;
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                logger.Warning("Vintage-E: hooking the handbook page builder: {0}; handbook pages won't have the Emc Craft link.", Error);
            }
        }

        static void Postfix(ICoreClientAPI capi, ref RichTextComponentBase[] __result)
        {
            try
            {
                if (__result == null || Mod == null || capi == null) return;
                var list = new List<RichTextComponentBase>(__result);
                if (CollectibleBehaviorEmcTooltip.AddEmcCraftLinks(Mod, list, capi)) __result = list.ToArray();
            }
            catch { /* a handbook page never breaks over this */ }
        }
    }
}
