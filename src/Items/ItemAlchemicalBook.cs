using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>A saved place in an Alchemical Book: a name and where the player stood.</summary>
    public class AlchemicalPlace
    {
        public string Name;
        public double X, Y, Z;
        public int Dim;
    }

    /// <summary>An Alchemical Book's places and where Back goes, saved as JSON (in the book, or with the player it's bound to).</summary>
    public class AlchemicalPlaces
    {
        public List<AlchemicalPlace> Places = new List<AlchemicalPlace>();
        public AlchemicalPlace Back;

        public static AlchemicalPlaces Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new AlchemicalPlaces();
            try { return JsonConvert.DeserializeObject<AlchemicalPlaces>(json) ?? new AlchemicalPlaces(); }
            catch { return new AlchemicalPlaces(); }
        }

        public string ToJson() => JsonConvert.SerializeObject(this);

        public AlchemicalPlace Find(string name) => Places.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Basic, Advanced, Master and Arcane Alchemical Books (1.29.3, Project Expansion's). Right-click opens a window of
    /// saved places: save where you stand under a name, teleport to one for EMC a block of distance (the item's
    /// <c>emcPerBlock</c>: 1,000, 500, 100, 0, times <see cref="VintageEConfig.AlchemicalBookCostMultiplier"/>; free in
    /// creative), and Back to where you last went from. The Advanced and better can be bound to their owner with
    /// sneak + right-click: their places are then kept with the owner (so all their bound books share them), and only
    /// the owner can use it. Not during a temporal storm (<see cref="VintageEConfig.AlchemicalBookInStorms"/>), and not
    /// between dimensions.
    /// </summary>
    public class ItemAlchemicalBook : Item
    {
        public const string PlacesKey = "vintageeBookPlaces";
        public const string BoundKey = "vintageeABookOwner";
        public const string BoundNameKey = "vintageeABookOwnerName";
        public const int MaxPlaces = 50;
        public const int MaxName = 32;

        public double EmcPerBlock => Attributes?["emcPerBlock"].AsDouble(1000) ?? 1000;
        public bool CanBind => Attributes?["canBind"].AsBool(false) ?? false;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent) return;
            bool sneak = byEntity.Controls.Sneak;
            if (byEntity.World.Side == EnumAppSide.Client)
            {
                if (!(sneak && CanBind)) GuiDialogAlchemicalBook.Open(byEntity.World.Api as ICoreClientAPI);
                return;
            }
            if (sneak && CanBind && (byEntity as EntityPlayer)?.Player is IServerPlayer player)
            {
                string message = ToggleBind(player, slot);
                if (message != null) player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        /// <summary>Binds the book to the player, or unbinds it if it's theirs. Returns what to tell them.</summary>
        public static string ToggleBind(IServerPlayer player, ItemSlot slot)
        {
            ItemStack book = slot.Itemstack;
            string bound = book.Attributes.GetString(BoundKey);
            if (bound != null && bound != player.PlayerUID) return Lang.GetL(player.LanguageCode, "vintagee:abook-err-notyours", book.Attributes.GetString(BoundNameKey) ?? "?");
            if (bound == null)
            {
                book.Attributes.SetString(BoundKey, player.PlayerUID);
                book.Attributes.SetString(BoundNameKey, player.PlayerName);
            }
            else
            {
                book.Attributes.RemoveAttribute(BoundKey);
                book.Attributes.RemoveAttribute(BoundNameKey);
            }
            slot.MarkDirty();
            return Lang.GetL(player.LanguageCode, bound == null ? "vintagee:abook-nowbound" : "vintagee:abook-unbound");
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var a = inSlot?.Itemstack?.Attributes;
            string bound = a?.GetString(BoundNameKey);
            if (bound != null) dsc.AppendLine(Lang.Get("vintagee:abook-bound-info", bound));
            else
            {
                int n = AlchemicalPlaces.Parse(a?.GetString(PlacesKey)).Places.Count;
                if (n > 0) dsc.AppendLine(Lang.Get("vintagee:abook-places-info", n));
            }
            double per = EmcPerBlock;
            dsc.AppendLine(per <= 0 ? Lang.Get("vintagee:abook-free") : Lang.Get("vintagee:abook-perblock", EmcFormat.Format(per)));
        }
    }

    /// <summary>The server side of the Alchemical Books' window (1.29.3).</summary>
    public static class AlchemicalBooks
    {
        /// <summary>The book in the player's hand, if they hold one.</summary>
        public static ItemSlot HeldBook(IPlayer player)
        {
            ItemSlot slot = player?.InventoryManager?.ActiveHotbarSlot;
            if (slot?.Itemstack?.Collectible is ItemAlchemicalBook) return slot;
            slot = player?.Entity?.LeftHandItemSlot;
            return slot?.Itemstack?.Collectible is ItemAlchemicalBook ? slot : null;
        }

        /// <summary>Where a book's places are kept: with the player it's bound to, or in the book.</summary>
        static ITreeAttribute Store(IServerPlayer player, ItemStack book) =>
            book.Attributes.GetString(ItemAlchemicalBook.BoundKey) != null ? player.Entity.Attributes : book.Attributes;

        public static AlchemicalPlaces Read(IServerPlayer player, ItemStack book) => AlchemicalPlaces.Parse(Store(player, book).GetString(ItemAlchemicalBook.PlacesKey));

        static void Write(IServerPlayer player, ItemSlot slot, AlchemicalPlaces places)
        {
            ITreeAttribute store = Store(player, slot.Itemstack);
            store.SetString(ItemAlchemicalBook.PlacesKey, places.ToJson());
            slot.MarkDirty();
        }

        /// <summary>EMC a block for this player with this book: the book's rate times the server's multiplier, nothing in creative.</summary>
        public static double PerBlock(VintageEModSystem mod, IPlayer player, ItemStack book)
        {
            if (player?.WorldData?.CurrentGameMode == EnumGameMode.Creative) return 0;
            double rate = (book?.Collectible as ItemAlchemicalBook)?.EmcPerBlock ?? 1000;
            return Math.Max(0, rate * (mod?.Config?.AlchemicalBookCostMultiplier ?? 1));
        }

        /// <summary>What going from one point to another costs: the distance, rounded up, times the rate.</summary>
        public static decimal Cost(double perBlock, double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
        {
            if (perBlock <= 0) return 0;
            double d = Math.Sqrt((toX - fromX) * (toX - fromX) + (toY - fromY) * (toY - fromY) + (toZ - fromZ) * (toZ - fromZ));
            return EmcAmount.From(Math.Ceiling(d * perBlock));
        }

        /// <summary>Whether a temporal storm is on now.</summary>
        public static bool StormNow(ICoreAPI api)
        {
            try { return api?.ModLoader?.GetModSystem<SystemTemporalStability>()?.StormData?.nowStormActive == true; }
            catch { return false; }
        }

        public static void OnRequest(VintageEModSystem mod, ICoreServerAPI sapi, IServerPlayer player, AlchemicalBookRequestPacket req)
        {
            string L(string key, params object[] args) => Lang.GetL(player.LanguageCode, "vintagee:" + key, args);
            ItemSlot slot = HeldBook(player);
            if (slot == null) { Send(mod, sapi, player, null, L("abook-err-nobook"), true, close: true); return; }
            ItemStack book = slot.Itemstack;
            string bound = book.Attributes.GetString(ItemAlchemicalBook.BoundKey);
            if (bound != null && bound != player.PlayerUID)
            {
                Send(mod, sapi, player, null, L("abook-err-notyours", book.Attributes.GetString(ItemAlchemicalBook.BoundNameKey) ?? "?"), true, close: true);
                return;
            }
            AlchemicalPlaces places = Read(player, book);
            string name = (req.Name ?? "").Trim();
            if (name.Length > ItemAlchemicalBook.MaxName) name = name.Substring(0, ItemAlchemicalBook.MaxName);
            EntityPos at = player.Entity.Pos;
            string message = null;
            bool error = false, close = false;

            switch (req.Action)
            {
                case AlchemicalBookAction.Save:
                    if (name.Length == 0) { message = L("abook-err-noname"); error = true; break; }
                    if (places.Find(name) != null) { message = L("abook-err-duplicate", name); error = true; break; }
                    if (places.Places.Count >= ItemAlchemicalBook.MaxPlaces) { message = L("abook-err-full", ItemAlchemicalBook.MaxPlaces); error = true; break; }
                    places.Places.Add(new AlchemicalPlace { Name = name, X = at.X, Y = at.Y, Z = at.Z, Dim = at.Dimension });
                    Write(player, slot, places);
                    message = L("abook-saved", name);
                    break;
                case AlchemicalBookAction.Delete:
                {
                    AlchemicalPlace p = places.Find(name);
                    if (p == null) { message = L("abook-err-notfound", name); error = true; break; }
                    places.Places.Remove(p);
                    Write(player, slot, places);
                    message = L("abook-deleted", p.Name);
                    break;
                }
                case AlchemicalBookAction.Go:
                case AlchemicalBookAction.Back:
                {
                    bool back = req.Action == AlchemicalBookAction.Back;
                    AlchemicalPlace p = back ? places.Back : places.Find(name);
                    if (p == null) { message = back ? L("abook-err-noback") : L("abook-err-notfound", name); error = true; break; }
                    if (p.Dim != at.Dimension) { message = L("abook-err-dimension"); error = true; break; }
                    if (!mod.Config.AlchemicalBookInStorms && StormNow(sapi)) { message = L("abook-err-storm"); error = true; break; }
                    decimal cost = Cost(PerBlock(mod, player, book), at.X, at.Y, at.Z, p.X, p.Y, p.Z);
                    if (!EmcWallet.TryPayExact(player, cost)) { message = L("abook-err-emc", EmcFormat.Total(cost), EmcFormat.Total(EmcWallet.Balance(player))); error = true; break; }
                    places.Back = back ? null : new AlchemicalPlace { Name = "", X = at.X, Y = at.Y, Z = at.Z, Dim = at.Dimension };
                    Write(player, slot, places);
                    player.Entity.TeleportToDouble(p.X, p.Y, p.Z);
                    sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player.Entity, null, true, 16, 0.6f);
                    message = back ? L("abook-wentback", EmcFormat.Total(cost)) : L("abook-went", p.Name, EmcFormat.Total(cost));
                    close = true;
                    break;
                }
            }
            Send(mod, sapi, player, slot, message, error, close);
            if (close && message != null) player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }

        /// <summary>Sends the book's places (or only a message, without a book) to the player's window.</summary>
        static void Send(VintageEModSystem mod, ICoreServerAPI sapi, IServerPlayer player, ItemSlot slot, string message, bool error, bool close = false)
        {
            var packet = new AlchemicalBookStatePacket { Message = message, MessageIsError = error, Close = close, Emc = EmcWallet.Balance(player), Storm = StormNow(sapi) };
            if (slot?.Itemstack != null)
            {
                AlchemicalPlaces places = Read(player, slot.Itemstack);
                packet.Names = places.Places.Select(p => p.Name).ToArray();
                packet.X = places.Places.Select(p => p.X).ToArray();
                packet.Y = places.Places.Select(p => p.Y).ToArray();
                packet.Z = places.Places.Select(p => p.Z).ToArray();
                packet.Dim = places.Places.Select(p => p.Dim).ToArray();
                if (places.Back != null)
                {
                    packet.HasBack = true;
                    packet.BackX = places.Back.X; packet.BackY = places.Back.Y; packet.BackZ = places.Back.Z; packet.BackDim = places.Back.Dim;
                }
                packet.PerBlock = PerBlock(mod, player, slot.Itemstack);
                packet.BoundTo = slot.Itemstack.Attributes.GetString(ItemAlchemicalBook.BoundNameKey);
            }
            mod.SendAlchemicalBookState(player, packet);
        }
    }
}
