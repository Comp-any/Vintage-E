using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// An EMC Link (1.29.2, Project Expansion's): a block tied to its owner's personal EMC, for automation.
    /// <list type="bullet">
    /// <item>Set to an item its owner has learned (right-click with it), it makes that item from the owner's EMC: into its
    /// output slot for the chutes and hoppers that take from it (as from a condenser), into the owner's hand on a right-click, or, for a
    /// liquid, into barrels and other liquid containers next to it.</item>
    /// <item>Items a hopper or chute puts into it are burned into the owner's EMC and learned, as the table does.</item>
    /// <item>EMC that collectors and relays next to it push in goes to the owner.</item>
    /// </list>
    /// All within its tier's limits a second (<c>emcPerSecond</c>, <c>itemsPerSecond</c>, <c>litresPerSecond</c>,
    /// <c>inputSlots</c> in its JSON; Final has none), times <see cref="VintageEConfig.EmcLinkLimitMultiplier"/>. The owner's
    /// EMC can only be paid or spent while they're in the game; until then items and EMC wait in the link.
    /// </summary>
    public class BEEmcLink : BlockEntityContainer, IOwnedMachine, IEmcAcceptor, IEmcInteractable
    {
        public const int MaxInputSlots = 48;
        readonly EmcLinkInventory inventory;
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "vintagee-emclink";

        public string OwnerUid { get; private set; }
        public string OwnerName { get; private set; }
        /// <summary>The knowledge key of what it makes, or null.</summary>
        public string TargetKey { get; private set; }
        /// <summary>EMC taken in from machines, waiting to go to the owner.</summary>
        public decimal Buffer;
        public decimal Held { get => Buffer; set => Buffer = value < 0 ? 0 : value; }

        double emcPerSecond = 16, itemsPerSecond = 1, litresPerSecond = 10;
        int inputSlots = 3;
        bool unlimited;
        decimal emcLeft;
        int exportLeft, importLeft;
        double litresLeft;

        public BEEmcLink()
        {
            inventory = new EmcLinkInventory(this);
        }

        VintageEModSystem Mod => VintageEModSystem.Of(Api);
        // Players' games have the default settings, so they show the multiplier the server last sent.
        double shownMultiplier = 1;
        double Multiplier => Api?.Side == EnumAppSide.Server ? Math.Max(0, Mod?.Config?.EmcLinkLimitMultiplier ?? 1) : shownMultiplier;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ReadTier();
            ResetLimits();
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 1000);
        }

        /// <summary>The Matter Upgrader swaps the block in place: what's in it stays, the new tier's limits apply.</summary>
        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            ReadTier();
            ResetLimits();
        }

        void ReadTier()
        {
            JsonObject a = (Block ?? Api?.World.BlockAccessor.GetBlock(Pos))?.Attributes;
            unlimited = a?["unlimited"].AsBool(false) ?? false;
            emcPerSecond = a?["emcPerSecond"].AsDouble(16) ?? 16;
            itemsPerSecond = a?["itemsPerSecond"].AsDouble(1) ?? 1;
            litresPerSecond = a?["litresPerSecond"].AsDouble(10) ?? 10;
            inputSlots = GameMath.Clamp(a?["inputSlots"].AsInt(3) ?? 3, 1, MaxInputSlots);
        }

        public bool Unlimited => unlimited;
        public decimal EmcLimit => unlimited ? decimal.MaxValue : EmcTransfer.PerTick(emcPerSecond * Multiplier);
        public int ItemLimit => unlimited ? int.MaxValue : (int)Math.Max(1, Math.Min(int.MaxValue, Math.Floor(itemsPerSecond * Multiplier)));
        public double LitreLimit => unlimited ? double.MaxValue : litresPerSecond * Multiplier;
        public int InputSlots => inputSlots;

        void ResetLimits()
        {
            emcLeft = EmcLimit;
            exportLeft = ItemLimit;
            importLeft = ItemLimit;
            litresLeft = LitreLimit;
        }

        public void SetOwner(string uid, string name)
        {
            OwnerUid = uid;
            OwnerName = name;
            MarkDirty(true);
        }

        /// <summary>Sets what it makes (a knowledge key), or clears it with null.</summary>
        public void SetTarget(string key)
        {
            TargetKey = key;
            MarkDirty(true);
        }

        /// <summary>EMC pushed in by collectors and relays: up to its limit a second, and while it's waiting for the owner, up to a second's worth.</summary>
        public decimal AcceptEmc(decimal amount)
        {
            if (!EmcTransfer.Usable(amount) || OwnerUid == null) return 0;
            decimal take = unlimited ? amount : Math.Min(amount, Math.Min(emcLeft, EmcLimit - Buffer));
            if (take <= 0) return 0;
            Buffer = EmcAmount.Add(Buffer, take);
            if (!unlimited) emcLeft -= take;
            MarkDirty(false);
            return take;
        }

        void OnServerTick(float dt)
        {
            ResetLimits();
            Work();
        }

        /// <summary>One second's work for an owner who's in the game: pays the EMC taken in, burns what was put in, makes the target.</summary>
        public void Work()
        {
            IServerPlayer owner = OwnedMachine.OnlineOwner(Api, OwnerUid);
            if (owner == null || Mod == null) return;
            bool changed = false;
            if (Buffer > 0)
            {
                PlayerEmc.AddEmcExact(owner, Buffer);
                Buffer = 0;
                changed = true;
            }
            changed |= Import(owner);
            changed |= ExportLiquid(owner);
            if (ChuteOutput.HasTaker(Api.World.BlockAccessor, Pos)) changed |= FillOutput(owner, exportLeft);
            changed |= ChuteOutput.HandOut(Api, Pos, () => inventory[0].Empty ? null : inventory[0], 2);
            if (changed) MarkDirty(false);
        }

        /// <summary>Burns what's in the input slots into the owner's EMC (and teaches it), up to the item limit.</summary>
        bool Import(IServerPlayer owner)
        {
            bool any = false;
            var mod = Mod;
            if (mod?.Transmutation == null) return false;
            for (int i = 1; i <= inputSlots && importLeft > 0; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                string error = mod.Transmutation.PlanBurn(owner, slot.Itemstack, out TransmutationService.BurnPlan plan, out _);
                if (error != null)
                {
                    // It can't be burned any more (its value went, or the lists changed): out it goes, on top.
                    ItemStack back = slot.TakeOutWhole();
                    slot.MarkDirty();
                    if (back != null) Api.World.SpawnItemEntity(back, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
                    any = true;
                    continue;
                }
                ItemStack taken = slot.TakeOut(Math.Min(importLeft, slot.StackSize));
                slot.MarkDirty();
                if (taken == null || taken.StackSize <= 0) continue;
                importLeft -= taken.StackSize;
                string learned = mod.Transmutation.ApplyBurn(owner, taken, plan);
                if (learned != null) mod.SendResult(owner, new TransmuteResultPacket { LearnedKey = learned });
                any = true;
            }
            return any;
        }

        /// <summary>What it makes, as a stack of one, and its value each; null if it can't make it for this owner now.</summary>
        ItemStack Product(IServerPlayer owner, out decimal each)
        {
            each = 0;
            var mod = Mod;
            if (TargetKey == null || mod == null) return null;
            ItemStack proto = TransmutationService.Prototype(mod, Api.World, TargetKey);
            if (proto == null || mod.Emc.IsNoTransmute(proto.Collectible) || !PlayerEmc.Knows(owner, TargetKey)) return null;
            each = EmcAmount.From(mod.Emc.GetValue(proto));
            return each > 0 ? proto : null;
        }

        static bool IsLiquid(ItemStack stack) => EmcRegistry.ItemsPerLitre(stack?.Collectible) > 0;
        static float PerLitre(ItemStack stack) => (float)Math.Max(1, EmcRegistry.ItemsPerLitre(stack?.Collectible));

        /// <summary>Buys up to <paramref name="most"/> of the target into the output slot (one stack at most). Returns whether any was made.</summary>
        bool FillOutput(IServerPlayer owner, int most)
        {
            ItemStack proto = Product(owner, out decimal each);
            if (proto == null || IsLiquid(proto)) return false;
            ItemSlot output = inventory[0];
            if (!output.Empty && !output.Itemstack.Equals(Api.World, proto, GlobalConstants.IgnoredStackAttributes)) return false;
            int room = proto.Collectible.MaxStackSize - output.StackSize;
            int n = Math.Min(Math.Min(room, most), Math.Min(exportLeft, EmcAmount.HowMany(PlayerEmc.GetEmc(owner), each)));
            if (n <= 0) return false;
            PlayerEmc.SetEmc(owner, PlayerEmc.GetEmc(owner) - each * n);
            if (output.Empty)
            {
                ItemStack made = proto.Clone();
                made.StackSize = n;
                output.Itemstack = made;
            }
            else output.Itemstack.StackSize += n;
            output.MarkDirty();
            exportLeft -= n;
            return true;
        }

        /// <summary>A liquid target: fills the barrels and other liquid containers next to it, up to the litre limit.</summary>
        bool ExportLiquid(IServerPlayer owner)
        {
            ItemStack proto = Product(owner, out decimal each);
            if (proto == null || !IsLiquid(proto) || litresLeft <= 0) return false;
            float perLitre = PerLitre(proto);
            bool any = false;
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (litresLeft <= 0) break;
                BlockPos at = Pos.AddCopy(face);
                if (!(Api.World.BlockAccessor.GetBlock(at) is BlockLiquidContainerBase sink)) continue;
                int portions = (int)Math.Min(int.MaxValue, Math.Floor(litresLeft * perLitre));
                portions = Math.Min(portions, EmcAmount.HowMany(PlayerEmc.GetEmc(owner), each));
                if (portions <= 0) break;
                ItemStack liquid = proto.Clone();
                liquid.StackSize = portions;
                int moved = sink.TryPutLiquid(at, liquid, portions / perLitre);
                if (moved <= 0) continue;
                PlayerEmc.SetEmc(owner, PlayerEmc.GetEmc(owner) - each * moved);
                litresLeft -= moved / perLitre;
                any = true;
            }
            return any;
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client) return true;
            var mod = Mod;
            if (!(byPlayer is IServerPlayer sp) || mod == null) return true;
            void Say(string key, params object[] args) => sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, "vintagee:" + key, args), EnumChatType.Notification);
            if (OwnerUid != null && sp.PlayerUID != OwnerUid && !sp.HasPrivilege(Privilege.controlserver)) { Say("link-notowner", OwnerName); return true; }
            if (OwnerUid == null) SetOwner(sp.PlayerUID, sp.PlayerName); // a link placed by the world or a command

            ItemSlot hand = sp.InventoryManager.ActiveHotbarSlot;
            bool sneak = sp.Entity.Controls.Sneak;
            if (hand?.Empty != false && sneak)
            {
                SetTarget(null);
                Say("link-cleared");
                return true;
            }
            ItemStack held = hand?.Itemstack;
            ItemStack product = TargetKey == null ? null : TransmutationService.Prototype(mod, Api.World, TargetKey);

            // Set (or, sneaking, change) what it makes: the held item, or the liquid in a held container.
            if (held != null && (TargetKey == null || sneak))
            {
                ItemStack want = held.Collectible is BlockLiquidContainerBase lc ? lc.GetContent(held) : held;
                if (want == null) { Say("link-nothing"); return true; }
                string key = EmcCommands.KnowledgeKey(mod, want);
                if (mod.Emc.GetValue(want) <= 0 || mod.Emc.IsNoTransmute(want.Collectible)) { Say("link-novalue", want.GetName()); return true; }
                if (!PlayerEmc.Knows(sp, key)) { Say("link-notlearned", want.GetName()); return true; }
                SetTarget(key);
                Say("link-set", want.GetName());
                return true;
            }
            if (product == null) { Say("link-notset"); return true; }

            // A liquid: into the empty (or same-liquid) container in hand.
            if (held != null && held.Collectible is BlockLiquidContainerBase box && IsLiquid(product))
            {
                ItemStack proto = Product(sp, out decimal each);
                if (proto == null) { Say("link-cant", product.GetName()); return true; }
                float perLitre = PerLitre(proto);
                float room = box.CapacityLitres - box.GetCurrentLitres(held);
                int portions = Math.Min((int)Math.Floor(Math.Min(room, litresLeft) * perLitre), EmcAmount.HowMany(PlayerEmc.GetEmc(sp), each));
                if (portions <= 0) { Say("link-cant", proto.GetName()); return true; }
                ItemStack liquid = proto.Clone();
                liquid.StackSize = portions;
                ItemStack one = held.StackSize > 1 ? hand.TakeOut(1) : held;
                int moved = box.TryPutLiquid(one, liquid, portions / perLitre);
                if (moved > 0)
                {
                    PlayerEmc.SetEmc(sp, PlayerEmc.GetEmc(sp) - each * moved);
                    litresLeft -= moved / perLitre;
                }
                if (!ReferenceEquals(one, held) && !sp.InventoryManager.TryGiveItemstack(one, true)) Api.World.SpawnItemEntity(one, sp.Entity.Pos.XYZ);
                hand.MarkDirty();
                return true;
            }

            // Otherwise a stack of what it makes, into the hand (empty, or holding the same thing): first what's waiting
            // in its output slot, then paid from the owner's EMC.
            if (held != null && !held.Equals(Api.World, product, GlobalConstants.IgnoredStackAttributes)) { Say("link-holdsame", product.GetName()); return true; }
            int max = product.Collectible.MaxStackSize - (held?.StackSize ?? 0);
            ItemSlot output = inventory[0];
            int fromOutput = output.Empty ? 0 : Math.Min(max, output.StackSize);
            if (fromOutput < max) FillOutput(sp, max - fromOutput);
            int give = Math.Min(max, output.StackSize);
            if (give <= 0) { Say("link-cant", product.GetName()); return true; }
            ItemStack got = output.TakeOut(give);
            output.MarkDirty();
            if (held == null) hand.Itemstack = got;
            else hand.Itemstack.StackSize += got.StackSize;
            hand.MarkDirty();
            MarkDirty(false);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (OwnerUid != null) tree.SetString("ownerUid", OwnerUid);
            if (OwnerName != null) tree.SetString("ownerName", OwnerName);
            if (TargetKey != null) tree.SetString("target", TargetKey); else tree.RemoveAttribute("target");
            EmcAmount.Write(tree, "emc", Buffer);
            tree.SetDouble("limitMultiplier", Multiplier);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            OwnerUid = tree.GetString("ownerUid");
            OwnerName = tree.GetString("ownerName");
            TargetKey = tree.GetString("target");
            Buffer = EmcAmount.Read(tree, "emc");
            shownMultiplier = tree.GetDouble("limitMultiplier", 1);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            dsc.AppendLine(OwnerName == null ? Lang.Get("vintagee:owned-noowner")
                : Lang.Get(forPlayer?.PlayerUID == OwnerUid ? "vintagee:owned-owner-you" : "vintagee:owned-owner", OwnerName));
            ItemStack product = TargetKey == null || Api == null ? null : TransmutationService.Prototype(VintageEModSystem.Of(Api), Api.World, TargetKey);
            dsc.AppendLine(product == null ? Lang.Get("vintagee:link-info-notset") : Lang.Get("vintagee:link-info-target", product.GetName()));
            dsc.AppendLine(unlimited ? Lang.Get("vintagee:link-info-unlimited")
                : Lang.Get("vintagee:link-info-limits", EmcFormat.Total(EmcLimit), ItemLimit, EmcFormat.Format(LitreLimit), inputSlots));
            if (Buffer > 0) dsc.AppendLine(Lang.Get("vintagee:link-info-buffer", EmcFormat.Total(Buffer)));
            int waiting = 0;
            for (int i = 1; i <= inputSlots; i++) waiting += inventory[i].StackSize;
            if (waiting > 0) dsc.AppendLine(Lang.Get("vintagee:link-info-waiting", waiting));
            if (!inventory[0].Empty) dsc.AppendLine(Lang.Get("vintagee:link-info-output", inventory[0].StackSize, inventory[0].Itemstack.GetName()));
        }

        /// <summary>Whether a hopper or chute may put this stack in: it has an owner and it can be burned.</summary>
        public bool Accepts(ItemStack stack) => OwnerUid != null && Api?.Side == EnumAppSide.Server && BETransmutationTable.Burnable(Mod, stack);
    }

    /// <summary>An EMC Link's slots: 0 is what it made (taken out from below or by chutes), 1 to 48 what it's fed (as many as its tier allows).</summary>
    public class EmcLinkInventory : InventoryBase
    {
        readonly ItemSlot[] slots;
        readonly BEEmcLink link;

        public EmcLinkInventory(BEEmcLink link) : base("vintagee-emclink", "vintagee-emclink-new", null)
        {
            this.link = link;
            slots = new ItemSlot[BEEmcLink.MaxInputSlots + 1];
            slots[0] = new EmcLinkOutputSlot(this);
            for (int i = 1; i < slots.Length; i++) slots[i] = new EmcLinkInputSlot(this, link, i);
        }

        public override int Count => slots.Length;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= slots.Length ? null : slots[slotId];
            set { if (slotId >= 0 && slotId < slots.Length) slots[slotId] = value; }
        }

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            if (fromSlot?.Itemstack == null || !link.Accepts(fromSlot.Itemstack)) return null;
            ItemSlot empty = null;
            for (int i = 1; i <= link.InputSlots; i++)
            {
                ItemSlot s = slots[i];
                if (s.Empty) { empty ??= s; continue; }
                if (s.Itemstack.Equals(Api.World, fromSlot.Itemstack, GlobalConstants.IgnoredStackAttributes) && s.StackSize < s.Itemstack.Collectible.MaxStackSize) return s;
            }
            return empty;
        }

        /// <summary>Only what it made comes back out.</summary>
        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace) => slots[0].Empty ? null : slots[0];

        public override void FromTreeAttributes(ITreeAttribute tree) => SlotsFromTreeAttributes(tree, slots);

        public override void ToTreeAttributes(ITreeAttribute tree) => SlotsToTreeAttributes(slots, tree);
    }

    /// <summary>Nothing is put into the output slot; it's only taken from.</summary>
    public class EmcLinkOutputSlot : ItemSlot
    {
        public EmcLinkOutputSlot(InventoryBase inventory) : base(inventory) { }
        public override bool CanHold(ItemSlot sourceSlot) => false;
    }

    public class EmcLinkInputSlot : ItemSlot
    {
        readonly BEEmcLink link;
        readonly int index;

        public EmcLinkInputSlot(InventoryBase inventory, BEEmcLink link, int index) : base(inventory)
        {
            this.link = link;
            this.index = index;
        }

        public override bool CanHold(ItemSlot sourceSlot) => index <= link.InputSlots && sourceSlot?.Itemstack != null && link.Accepts(sourceSlot.Itemstack) && base.CanHold(sourceSlot);
    }
}
