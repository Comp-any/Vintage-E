using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The Knowledge Sharing Book (1.29.3, Project Expansion's). Sneak + right-click writes the writer's knowledge into it;
    /// another player right-clicking it learns everything the writer knows and the book is used up. Project Expansion
    /// reads the writer's knowledge then and fails when they're offline; here writing also keeps a copy in the world's
    /// save data (one per writer, the latest), so the book works while the writer is away. When they're in the game,
    /// what they know now is used. Exact versions count against the reader's MaxExactKnowledge, as when burning.
    /// </summary>
    public class ItemKnowledgeBook : Item
    {
        public const string OwnerKey = "vintageeBookOwner";
        public const string OwnerNameKey = "vintageeBookOwnerName";
        public const string CountKey = "vintageeBookCount";

        /// <summary>Where a writer's knowledge is kept in the world's save data.</summary>
        public static string SaveKey(string uid) => "vintagee-knowledgebook-" + uid;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer player) || slot?.Itemstack == null) return;
            var sapi = byEntity.World.Api as ICoreServerAPI;
            string message = byEntity.Controls.Sneak ? Write(sapi, player, slot) : Read(sapi, player, slot);
            if (message != null) player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }

        /// <summary>Writes the player's knowledge into the book (and the world's copy of it). Returns what to tell them.</summary>
        public static string Write(ICoreServerAPI sapi, IServerPlayer player, ItemSlot slot)
        {
            string[] known = PlayerEmc.GetKnown(player);
            Store(sapi, player.PlayerUID, known);
            ItemStack book = slot.Itemstack;
            book.Attributes.SetString(OwnerKey, player.PlayerUID);
            book.Attributes.SetString(OwnerNameKey, player.PlayerName);
            book.Attributes.SetInt(CountKey, known.Length);
            slot.MarkDirty();
            return Lang.GetL(player.LanguageCode, "vintagee:knowledgebook-written", known.Length);
        }

        /// <summary>Teaches the reader what the book's writer knows and uses the book up. Returns what to tell them.</summary>
        public static string Read(ICoreServerAPI sapi, IServerPlayer player, ItemSlot slot)
        {
            string L(string key, params object[] args) => Lang.GetL(player.LanguageCode, "vintagee:" + key, args);
            ItemStack book = slot.Itemstack;
            string owner = book.Attributes.GetString(OwnerKey);
            string ownerName = book.Attributes.GetString(OwnerNameKey) ?? "?";
            if (string.IsNullOrEmpty(owner)) return L("knowledgebook-noowner");
            if (owner == player.PlayerUID) return L("knowledgebook-self");
            IServerPlayer online = OwnedMachine.OnlineOwner(sapi, owner);
            string[] written = online != null ? PlayerEmc.GetKnown(online) : Load(sapi, owner);
            if (written == null) return L("knowledgebook-gone", ownerName);
            var mod = VintageEModSystem.Of(sapi);
            int max = mod?.Config?.MaxExactKnowledge ?? int.MaxValue;
            List<string> learn = Plan(sapi.World, written, PlayerEmc.GetKnown(player), PlayerEmc.CountExact(player), max, out int skipped);
            int added = learn.Count == 0 ? 0 : PlayerEmc.LearnMany(player, learn);
            if (added == 0) return L("knowledgebook-nothing", ownerName);
            slot.TakeOut(1);
            slot.MarkDirty();
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/player/collect"), player.Entity, null, true, 8, 0.6f);
            return skipped > 0 ? L("knowledgebook-learned-limit", added, ownerName, skipped, max) : L("knowledgebook-learned", added, ownerName);
        }

        /// <summary>
        /// What a reader learns from a book: what it holds that they don't know and that's still in the game, leaving out
        /// exact versions past the limit (<paramref name="skippedExact"/>). In the book's order, so the first are kept.
        /// </summary>
        public static List<string> Plan(IWorldAccessor world, string[] written, string[] known, int exactNow, int maxExact, out int skippedExact)
        {
            skippedExact = 0;
            var have = new HashSet<string>(known ?? Array.Empty<string>(), StringComparer.Ordinal);
            var learn = new List<string>();
            int exact = exactNow;
            foreach (string key in written ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(key) || have.Contains(key)) continue;
                if (world != null && PlayerEmc.Resolve(world, key) == null) continue; // gone with its mod
                if (PlayerEmc.CountsAsExact(world, key))
                {
                    if (exact >= maxExact) { skippedExact++; continue; }
                    exact++;
                }
                have.Add(key);
                learn.Add(key);
            }
            return learn;
        }

        public static void Store(ICoreServerAPI sapi, string uid, string[] keys) =>
            sapi.WorldManager.SaveGame.StoreData(SaveKey(uid), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(keys ?? Array.Empty<string>())));

        /// <summary>The writer's knowledge as last written into a book, or null if there is none.</summary>
        public static string[] Load(ICoreServerAPI sapi, string uid)
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey(uid));
            if (data == null || data.Length == 0) return null;
            try { return JsonConvert.DeserializeObject<string[]>(Encoding.UTF8.GetString(data)); }
            catch { return null; }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var a = inSlot?.Itemstack?.Attributes;
            string name = a?.GetString(OwnerNameKey);
            dsc.AppendLine(name == null ? Lang.Get("vintagee:knowledgebook-blank") : Lang.Get("vintagee:knowledgebook-by", name, a.GetInt(CountKey)));
        }
    }
}
