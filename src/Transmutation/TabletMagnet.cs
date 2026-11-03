using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The magnet slot of the Transmutation Tablet. Put a Black Hole Band or a Void Ring in it and, while you carry the
    /// tablet anywhere in your inventory, items on the ground nearby that pass its filter are pulled in and transmuted
    /// into your EMC (and learned), as if you'd burned them at the table. The filter is a row of item slots: either
    /// "only these" (the default; an empty filter pulls nothing) or "everything except these".
    /// <para>Everything is stored on the tablet itself, so it goes with the tablet.</para>
    /// </summary>
    public static class TabletMagnet
    {
        public const string MagnetKey = "vintageeMagnet";
        public const string FilterKey = "vintageeMagnetFilter";
        public const string ExcludeKey = "vintageeMagnetExclude";
        public const int FilterSlots = 9;
        public const float Radius = TrinketEffects.BandPullRadius; // a band in the tablet reaches as far as a worn one
        /// <summary>The magnet's reach: a Void Ring pulls from further than a Black Hole Band.</summary>
        public static float RadiusOf(ItemStack magnet) => magnet?.Collectible is ItemEmcTrinket t && t.Kind == "voidring" ? TrinketEffects.VoidPullRadius : TrinketEffects.BandPullRadius;
        /// <summary>Items closer than this are transmuted; further out they're pulled in.</summary>
        public const float TakeRange = 2.5f;

        public static bool IsMagnet(ItemStack stack) =>
            stack?.Collectible is ItemEmcTrinket t && (t.Kind == "blackholeband" || t.Kind == "voidring");

        public static ItemStack GetMagnet(ItemStack tablet, IWorldAccessor world)
        {
            ItemStack magnet = tablet?.Attributes?.GetItemstack(MagnetKey);
            if (magnet != null && magnet.Collectible == null && world != null) magnet.ResolveBlockOrItem(world);
            return magnet?.Collectible == null ? null : magnet;
        }

        /// <summary>The filter as knowledge keys ("i:game:rock-granite"), one per filter slot (null = empty slot).</summary>
        public static bool IsTablet(ItemStack stack) => stack?.Collectible is ItemTransmutationTablet;

        public static string[] GetFilter(ItemStack tablet)
        {
            string[] stored = (tablet?.Attributes as TreeAttribute)?.GetStringArray(FilterKey) ?? Array.Empty<string>();
            var keys = new string[FilterSlots];
            for (int i = 0; i < FilterSlots && i < stored.Length; i++) keys[i] = string.IsNullOrEmpty(stored[i]) ? null : stored[i];
            return keys;
        }

        public static bool Excludes(ItemStack tablet) => tablet?.Attributes?.GetBool(ExcludeKey) == true;

        /// <summary>Whether the filter lets this item through.</summary>
        public static bool Passes(ItemStack tablet, CollectibleObject c)
        {
            string key = PlayerEmc.KeyOf(c);
            bool listed = key != null && GetFilter(tablet).Contains(key);
            return Excludes(tablet) ? !listed : listed;
        }

        /// <summary>The tablet the player is using: the one in their hand if it's a tablet, otherwise the first one they carry.</summary>
        public static ItemSlot FindTablet(IPlayer player)
        {
            ItemSlot hand = player?.InventoryManager?.ActiveHotbarSlot;
            if (hand?.Itemstack?.Collectible is ItemTransmutationTablet) return hand;
            return EmcWallet.CarriedSlots(player).FirstOrDefault(s => s.Itemstack?.Collectible is ItemTransmutationTablet);
        }

        /// <summary>Handles the magnet-slot and filter clicks from the tablet window (server side).</summary>
        public static void OnRequest(VintageEModSystem mod, IServerPlayer player, TransmuteRequestPacket req, Action<string, string[]> fail)
        {
            ItemSlot tabletSlot = FindTablet(player);
            if (tabletSlot == null) { fail("notablet", Array.Empty<string>()); return; }
            ItemStack tablet = tabletSlot.Itemstack;
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;

            switch (req.Action)
            {
                case TransmuteAction.MagnetSwap:
                {
                    ItemStack inside = GetMagnet(tablet, player.Entity.World);
                    ItemStack cursor = mouse?.Itemstack;
                    if (cursor != null && !IsMagnet(cursor)) { fail("notamagnet", new[] { cursor.GetName() }); return; }
                    if (cursor != null && cursor.StackSize > 1) { fail("notamagnet", new[] { cursor.GetName() }); return; }
                    if (cursor == null && inside == null) return;
                    if (cursor != null) tablet.Attributes.SetItemstack(MagnetKey, cursor.Clone());
                    else tablet.Attributes.RemoveAttribute(MagnetKey);
                    mouse.Itemstack = inside;
                    mouse.MarkDirty();
                    tabletSlot.MarkDirty();
                    break;
                }
                case TransmuteAction.MagnetFilter:
                {
                    int i = req.Mode;
                    if (i < 0 || i >= FilterSlots) return;
                    string[] keys = GetFilter(tablet);
                    string key = mouse?.Itemstack != null ? PlayerEmc.KeyOf(mouse.Itemstack.Collectible) : null;
                    if (key != null && keys.Contains(key)) return; // already in the filter
                    keys[i] = key;
                    (tablet.Attributes as TreeAttribute)?.SetStringArray(FilterKey, keys.Select(k => k ?? "").ToArray());
                    tabletSlot.MarkDirty();
                    break;
                }
                case TransmuteAction.MagnetMode:
                    tablet.Attributes.SetBool(ExcludeKey, !Excludes(tablet));
                    tabletSlot.MarkDirty();
                    break;
            }
        }

        /// <summary>Pulls in and transmutes filtered items around every player who carries a tablet with a magnet in it.</summary>
        public static void Tick(VintageEModSystem mod, ICoreServerAPI sapi, TransmutationService service)
        {
            foreach (IPlayer p in sapi.World.AllOnlinePlayers)
            {
                if (!(p is IServerPlayer player) || player.Entity == null || !player.Entity.Alive || player.ConnectionState != EnumClientState.Playing) continue;
                ItemStack tablet = null;
                foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
                {
                    if (slot.Itemstack?.Collectible is ItemTransmutationTablet && GetMagnet(slot.Itemstack, sapi.World) != null) { tablet = slot.Itemstack; break; }
                }
                if (tablet == null) continue;
                string[] filter = GetFilter(tablet);
                if (!Excludes(tablet) && filter.All(k => k == null)) continue; // "only these" with nothing chosen

                Vec3d center = player.Entity.Pos.XYZ.Add(0, 0.5, 0);
                long now = sapi.World.ElapsedMilliseconds;
                int taken = 0;
                float radius = RadiusOf(GetMagnet(tablet, sapi.World));
                foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is EntityItem && x.Alive))
                {
                    var ei = (EntityItem)e;
                    ItemStack stack = ei.Itemstack;
                    if (stack?.Collectible == null || !Passes(tablet, stack.Collectible)) continue;
                    // Something you just threw away stays put for a moment.
                    if (ei.ByPlayerUid == player.PlayerUID && now - ei.itemSpawnedMilliseconds < 3000) continue;
                    if (service.PlanBurn(player, stack, out TransmutationService.BurnPlan plan, out _) != null) continue;

                    Vec3d dir = center.SubCopy(e.Pos.XYZ);
                    double dist = dir.Length();
                    if (dist <= TakeRange)
                    {
                        string learned = service.ApplyBurn(player, stack, plan);
                        ei.Itemstack = null;
                        ei.Die(EnumDespawnReason.PickedUp);
                        taken++;
                        if (learned != null) mod.SendResult(player, new TransmuteResultPacket { LearnedKey = learned });
                        continue;
                    }
                    dir.Mul(Math.Min(0.45, 0.1 + dist * 0.05) / dist);
                    e.Pos.Motion.Set(dir.X, dir.Y + 0.04, dir.Z);
                }
                if (taken > 0) sapi.World.PlaySoundAt(new AssetLocation("game:sounds/player/collect"), player.Entity, null, true, 8, 0.4f);
            }
        }
    }
}
