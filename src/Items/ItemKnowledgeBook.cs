using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The Knowledge Sharing Book (1.29.3, Project Expansion's). Sneak + right-click writes the writer's knowledge into
    /// the book itself (compressed, <see cref="KeysKey"/>); another player right-clicking it learns what's in it.
    /// <list type="bullet">
    /// <item>It teaches exactly what was written, so it works whoever is online; writing it again updates it.</item>
    /// <item>It isn't used up unless the server's <see cref="VintageEConfig.KnowledgeBookConsumed"/> says so (Project
    /// Expansion always uses it up).</item>
    /// <item>Its tooltip says how many things are in it and how many of them the player looking at it would learn
    /// (the same <see cref="Plan"/> the server uses, against their game's copy of their knowledge).</item>
    /// <item>Exact versions count against the reader's MaxExactKnowledge, as when burning.</item>
    /// </list>
    /// </summary>
    public class ItemKnowledgeBook : Item
    {
        public const string OwnerKey = "vintageeBookOwner";
        public const string OwnerNameKey = "vintageeBookOwnerName";
        public const string CountKey = "vintageeBookCount";
        public const string KeysKey = "vintageeBookKeys";

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer player) || slot?.Itemstack == null) return;
            var sapi = byEntity.World.Api as ICoreServerAPI;
            string message = byEntity.Controls.Sneak ? Write(player, slot) : Read(sapi, player, slot);
            if (message != null) player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }

        /// <summary>Writes the player's knowledge into the book. Returns what to tell them.</summary>
        public static string Write(IServerPlayer player, ItemSlot slot)
        {
            string[] known = PlayerEmc.GetKnown(player);
            ItemStack book = slot.Itemstack;
            book.Attributes.SetString(OwnerKey, player.PlayerUID);
            book.Attributes.SetString(OwnerNameKey, player.PlayerName);
            book.Attributes.SetInt(CountKey, known.Length);
            book.Attributes.SetBytes(KeysKey, Pack(known));
            slot.MarkDirty();
            return Lang.GetL(player.LanguageCode, "vintagee:knowledgebook-written", known.Length);
        }

        /// <summary>Teaches the reader what's in the book (and uses it up if the server says so). Returns what to tell them.</summary>
        public static string Read(ICoreServerAPI sapi, IServerPlayer player, ItemSlot slot)
        {
            string L(string key, params object[] args) => Lang.GetL(player.LanguageCode, "vintagee:" + key, args);
            ItemStack book = slot.Itemstack;
            string owner = book.Attributes.GetString(OwnerKey);
            string ownerName = book.Attributes.GetString(OwnerNameKey) ?? "?";
            if (string.IsNullOrEmpty(owner)) return L("knowledgebook-noowner");
            if (owner == player.PlayerUID) return L("knowledgebook-self");
            string[] written = Unpack(book.Attributes.GetBytes(KeysKey));
            if (written == null) return L("knowledgebook-gone");
            var mod = VintageEModSystem.Of(sapi);
            int max = mod?.Config?.MaxExactKnowledge ?? int.MaxValue;
            List<string> learn = Plan(sapi.World, written, PlayerEmc.GetKnown(player), PlayerEmc.CountExact(player), max, out int skipped);
            int added = learn.Count == 0 ? 0 : PlayerEmc.LearnMany(player, learn);
            if (added == 0) return L("knowledgebook-nothing", ownerName) + (skipped > 0 ? " " + L("knowledgebook-limit-note", skipped, max) : "");
            bool consumed = mod?.Config?.KnowledgeBookConsumed == true;
            if (consumed)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/player/collect"), player.Entity, null, true, 8, 0.6f);
            return L("knowledgebook-learned", added, ownerName)
                + (skipped > 0 ? " " + L("knowledgebook-limit-note", skipped, max) : "")
                + " " + L(consumed ? "knowledgebook-usedup-note" : "knowledgebook-kept-note");
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

        /// <summary>The keys as the book keeps them: one per line, gzip-compressed (they repeat a lot, "i:game:" above all).</summary>
        public static byte[] Pack(string[] keys)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, true))
            {
                byte[] text = Encoding.UTF8.GetBytes(string.Join("\n", keys ?? Array.Empty<string>()));
                gz.Write(text, 0, text.Length);
            }
            return output.ToArray();
        }

        /// <summary>The book's keys, or null if there's nothing (readable) in it.</summary>
        public static string[] Unpack(byte[] data)
        {
            // Only what Pack wrote: gzip begins 1F 8B. (Damaged data may otherwise come out as empty text, not an error.)
            if (data == null || data.Length < 18 || data[0] != 0x1F || data[1] != 0x8B) return null;
            try
            {
                using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var reader = new StreamReader(input, Encoding.UTF8);
                string text = reader.ReadToEnd();
                return text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
            }
            catch { return null; }
        }

        // The tooltip is drawn every frame it's shown: what it counted is kept until the book or the player's knowledge changes.
        static byte[] shownBytes;
        static string[] shownKnown;
        static int shownLearn, shownSkipped;

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var a = inSlot?.Itemstack?.Attributes;
            string name = a?.GetString(OwnerNameKey);
            if (name == null) { dsc.AppendLine(Lang.Get("vintagee:knowledgebook-blank")); return; }
            dsc.AppendLine(Lang.Get("vintagee:knowledgebook-by", name, a.GetInt(CountKey)));
            if (world?.Side != EnumAppSide.Client) return;
            IPlayer me = (world.Api as Vintagestory.API.Client.ICoreClientAPI)?.World?.Player;
            if (me == null) return;
            var emc = VintageEModSystem.Of(world.Api)?.Emc;
            if (a.GetString(OwnerKey) == me.PlayerUID) dsc.AppendLine(Lang.Get("vintagee:knowledgebook-yours"));
            else
            {
                byte[] bytes = a.GetBytes(KeysKey);
                string[] known = PlayerEmc.GetKnown(me);
                if (!ReferenceEquals(bytes, shownBytes) || !ReferenceEquals(known, shownKnown))
                {
                    shownBytes = bytes;
                    shownKnown = known;
                    int max = emc != null && emc.MaxExactKnowledge >= 0 ? emc.MaxExactKnowledge : int.MaxValue;
                    shownLearn = Plan(world, Unpack(bytes) ?? Array.Empty<string>(), known, PlayerEmc.CountExact(me), max, out shownSkipped).Count;
                }
                if (shownLearn == 0 && shownSkipped == 0) dsc.AppendLine(Lang.Get("vintagee:knowledgebook-knowall"));
                else if (shownSkipped == 0) dsc.AppendLine(Lang.Get("vintagee:knowledgebook-wouldlearn", shownLearn));
                else dsc.AppendLine(Lang.Get("vintagee:knowledgebook-wouldlearn-limit", shownLearn, shownSkipped));
            }
            if (emc?.KnowledgeBookConsumed == true) dsc.AppendLine(Lang.Get("vintagee:knowledgebook-usedup"));
        }
    }
}
