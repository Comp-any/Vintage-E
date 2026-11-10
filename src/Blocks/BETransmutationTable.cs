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
    /// The transmutation table's block entity: a hopper or chute pointing into the table feeds it items, which it burns
    /// into its owner's personal EMC (and teaches them), a few at a time, the same as burning them in the window. The
    /// owner is whoever placed the table (or, for a table placed before 1.11, the first to open it). Only things that
    /// can be burned go in; while the owner is offline, the one input slot fills up and the hopper waits.
    /// </summary>
    public class BETransmutationTable : BlockEntityContainer
    {
        /// <summary>How many items it burns every half second (16 a second at most).</summary>
        public const int BurnPerTick = 8;
        const int TickMs = 500;

        readonly TableInputInventory inventory;
        public string OwnerUid { get; private set; }
        public string OwnerName { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "vintagee-tableinput";

        public BETransmutationTable()
        {
            inventory = new TableInputInventory(this);
        }

        VintageEModSystem Mod => VintageEModSystem.Of(Api);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(BurnTick, TickMs);
        }

        public void SetOwner(IPlayer player)
        {
            if (player != null) SetOwner(player.PlayerUID, player.PlayerName);
        }

        internal void SetOwner(string uid, string name)
        {
            OwnerUid = uid;
            OwnerName = name;
            MarkDirty(true);
        }

        /// <summary>Whether a hopper (or anything else) may put this stack in: the table has an owner and it can be burned.</summary>
        public bool Accepts(ItemStack stack)
        {
            if (stack?.Collectible == null || OwnerUid == null || Api?.Side != EnumAppSide.Server) return false;
            VintageEModSystem mod = Mod;
            if (mod == null || mod.Emc.IsNoTransmute(stack.Collectible)) return false;
            if (stack.Collectible is IEmcStorageItem) return false; // a Klein star would lose what it holds
            StackDataTable data = mod.Emc.StackData;
            bool keepsData = data.CarriesData(stack.Collectible) && data.ModeOf(stack.Collectible) != StackDataMode.Item;
            if (!keepsData && mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack)) return false;
            return mod.BurnValue(stack) > 0;
        }

        void BurnTick(float dt)
        {
            ItemSlot slot = inventory[0];
            if (slot.Empty || OwnerUid == null) return;
            VintageEModSystem mod = Mod;
            if (mod?.Transmutation == null || !(Api is ICoreServerAPI sapi)) return;
            if (!(sapi.World.PlayerByUid(OwnerUid) is IServerPlayer owner) || owner.Entity == null || owner.ConnectionState != EnumClientState.Playing) return;

            string error = mod.Transmutation.PlanBurn(owner, slot.Itemstack, out TransmutationService.BurnPlan plan, out _);
            if (error != null)
            {
                // Its value went away (or the list changed) since it went in: put it back out on top of the table.
                ItemStack back = slot.TakeOutWhole();
                slot.MarkDirty();
                if (back != null) sapi.World.SpawnItemEntity(back, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                return;
            }
            ItemStack taken = slot.TakeOut(Math.Min(BurnPerTick, slot.StackSize));
            slot.MarkDirty();
            if (taken == null || taken.StackSize <= 0) return;
            string learned = mod.Transmutation.ApplyBurn(owner, taken, plan);
            if (learned != null) mod.SendResult(owner, new TransmuteResultPacket { LearnedKey = learned });
            MarkDirty(false);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (OwnerUid != null) tree.SetString("ownerUid", OwnerUid);
            if (OwnerName != null) tree.SetString("ownerName", OwnerName);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            OwnerUid = tree.GetString("ownerUid");
            OwnerName = tree.GetString("ownerName");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            // Not the container's "stored food perishes" line: nothing waits here long.
            dsc.AppendLine(OwnerName == null
                ? Lang.Get("vintagee:table-noowner")
                : Lang.Get(forPlayer?.PlayerUID == OwnerUid ? "vintagee:table-owner-you" : "vintagee:table-owner", OwnerName));
            ItemStack waiting = inventory[0].Itemstack;
            if (waiting != null) dsc.AppendLine(Lang.Get("vintagee:table-waiting", waiting.StackSize, waiting.GetName()));
        }
    }

    /// <summary>The table's one input slot: only takes what the table can burn.</summary>
    public class TableInputInventory : InventoryBase
    {
        readonly ItemSlot[] slots;
        readonly BETransmutationTable table;

        public TableInputInventory(BETransmutationTable table) : base("vintagee-tableinput", "vintagee-tableinput-new", null)
        {
            this.table = table;
            slots = new ItemSlot[] { new TableInputSlot(this, table) };
        }

        public override int Count => slots.Length;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= slots.Length ? null : slots[slotId];
            set { if (slotId >= 0 && slotId < slots.Length) slots[slotId] = value; }
        }

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot) =>
            fromSlot?.Itemstack != null && table.Accepts(fromSlot.Itemstack) ? slots[0] : null;

        /// <summary>Nothing comes back out of the table (a hopper under it gets nothing).</summary>
        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace) => null;

        public override void FromTreeAttributes(ITreeAttribute tree) => SlotsFromTreeAttributes(tree, slots);

        public override void ToTreeAttributes(ITreeAttribute tree) => SlotsToTreeAttributes(slots, tree);
    }

    public class TableInputSlot : ItemSlot
    {
        readonly BETransmutationTable table;

        public TableInputSlot(InventoryBase inventory, BETransmutationTable table) : base(inventory)
        {
            this.table = table;
        }

        public override bool CanHold(ItemSlot sourceSlot) => sourceSlot?.Itemstack != null && table.Accepts(sourceSlot.Itemstack) && base.CanHold(sourceSlot);
    }
}
