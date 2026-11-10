using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Put an item in the target slot and the condenser makes copies of it: it burns everything else
    /// in its storage into EMC (and takes EMC from neighbouring collectors), then spends that EMC on
    /// copies of the target. Works with hoppers and chutes like any container.
    /// Rates come from block attributes: <c>itemsPerSecond</c>, <c>outputsPerSecond</c>, <c>capacity</c>, <c>storageSlots</c>.
    /// </summary>
    public class BEEnergyCondenser : BlockEntityContainer, IEmcAcceptor, IEmcInteractable
    {
        public const int TargetSlot = 0;

        CondenserInventory inventory;
        GuiDialogCondenser dialog;

        public double Stored;
        double capacity = 1000000;
        public double Capacity => capacity;
        double itemsPerSecond = 4;
        double outputsPerSecond = 1;
        double burnBudget, makeBudget;
        // EMC pushed in by collectors and relays: counted over a few seconds to show the rate, and synced to clients
        // at most once a second (the window and block info would otherwise show a stale amount).
        double acceptedInWindow, syncedStored;
        float windowSeconds;
        long lastSyncMs;
        public double IncomingPerSecond;

        /// <summary>The game's BlockEntityContainer (see BEInventoryBase.Inventory): chutes and hoppers only see that class.</summary>
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "vintageecondenser";

        public BEEnergyCondenser()
        {
            inventory = new CondenserInventory(41);
        }

        public override void Initialize(ICoreAPI api)
        {
            JsonObject attrs = Block?.Attributes ?? api.World.BlockAccessor.GetBlock(Pos)?.Attributes;
            int storage = attrs?["storageSlots"].AsInt(40) ?? 40;
            if (storage + 1 != inventory.Count)
            {
                var resized = new CondenserInventory(storage + 1);
                for (int i = 0; i < Math.Min(resized.Count, inventory.Count); i++) resized[i].Itemstack = inventory[i].Itemstack;
                // Fewer slots than before (an update or a patch): what was in the lost slots is dropped on the ground, not lost.
                if (api.Side == EnumAppSide.Server)
                    for (int i = resized.Count; i < inventory.Count; i++)
                        if (inventory[i].Itemstack != null) api.World.SpawnItemEntity(inventory[i].Itemstack, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
                inventory = resized;
            }
            capacity = attrs?["capacity"].AsDouble(1000000) ?? 1000000;
            itemsPerSecond = attrs?["itemsPerSecond"].AsDouble(4) ?? 4;
            outputsPerSecond = attrs?["outputsPerSecond"].AsDouble(1) ?? 1;

            // The base class initializes the inventory (and its perish handling), so it runs after any resize above.
            base.Initialize(api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnSlotModified;

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, 500);
            }
        }

        VintageEModSystem Mod => VintageEModSystem.Of(Api);

        void OnSlotModified(int slotId)
        {
            if (slotId == TargetSlot) copyOfTarget = null; // the target changed: its copy is made again
            if (Api?.Side == EnumAppSide.Server)
            {
                MarkDirty(false);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
            }
        }

        public double TargetValue
        {
            get
            {
                ItemStack target = inventory[TargetSlot].Itemstack;
                VintageEModSystem mod = Mod;
                if (target == null || mod == null) return 0;
                TargetCopy(mod, target, out _, out double value);
                return value;
            }
        }

        // The copy the condenser makes of its target and what one costs, kept while the target slot and the EMC
        // table stay as they are: both used to be worked out again on every tick of every condenser (twice a second)
        // and for every frame its block info was on screen.
        ItemStack copyOfTarget, cachedCopy;
        double cachedCopyValue;
        int cachedCopyRevision = -1, cachedCopyAge;

        void TargetCopy(VintageEModSystem mod, ItemStack target, out ItemStack copy, out double value)
        {
            int revision = mod.Emc.Revision;
            if (cachedCopy == null || !ReferenceEquals(copyOfTarget, target) || cachedCopyRevision != revision)
            {
                cachedCopy = CopyOf(mod, target);
                cachedCopyValue = mod.Emc.GetValue(cachedCopy);
                copyOfTarget = target;
                cachedCopyRevision = revision;
                cachedCopyAge = 0;
            }
            copy = cachedCopy;
            value = cachedCopyValue;
        }

        /// <summary>
        /// What the condenser makes from its target: the plain item, or - for items that keep their contents or
        /// material - exactly that version (fresh and undamaged).
        /// </summary>
        static ItemStack CopyOf(VintageEModSystem mod, ItemStack target)
        {
            var copy = new ItemStack(target.Collectible, 1);
            if (mod.Emc.StackData.CarriesData(target.Collectible) && mod.Emc.StackData.ModeOf(target.Collectible) != StackDataMode.Item)
            {
                var data = StackDataUtil.KeptData(target);
                if (data != null) foreach (var kv in data) copy.Attributes[kv.Key] = kv.Value.Clone();
            }
            return copy;
        }

        void OnServerTick(float dt)
        {
            var mod = Mod;
            if (mod == null) return;
            double speed = mod.Config.MachineSpeedMultiplier;
            ItemStack target = inventory[TargetSlot].Itemstack;
            bool changed = false;
            // A Klein star as the target is charged from the stored EMC, never copied (as in ProjectE).
            bool chargingStar = target?.Collectible is IEmcStorageItem;
            if (chargingStar && Stored >= 1)
            {
                double move = Math.Min(Math.Floor(Stored), StoredEmc.Capacity(target) - StoredEmc.Get(target));
                if (move > 0)
                {
                    StoredEmc.Set(target, StoredEmc.Get(target) + move);
                    Stored -= move;
                    inventory[TargetSlot].MarkDirty();
                    changed = true;
                }
            }
            ItemStack copy = null;
            double copyValue = 0;
            if (target != null && !chargingStar)
            {
                // Looked at afresh every ten seconds whatever happens, in case something changed the target where it lies.
                if (++cachedCopyAge >= 20) copyOfTarget = null;
                TargetCopy(mod, target, out copy, out copyValue);
            }
            // Things on the NoTransmute list are never made from EMC (nor burned, below).
            double targetValue = copy == null || mod.Emc.IsNoTransmute(copy.Collectible) ? 0 : copyValue;

            // 1. Burn storage (anything that isn't the target) into EMC, while there's room for it.
            burnBudget = Math.Min(burnBudget + itemsPerSecond * speed * dt, itemsPerSecond * speed * 2 + 1);
            for (int i = 1; i < inventory.Count && burnBudget >= 1; i++)
            {
                ItemSlot slot = inventory[i];
                ItemStack stack = slot.Itemstack;
                if (stack == null) continue;
                if (target != null && stack.Collectible == target.Collectible) continue;
                // A Klein star is never burned: what it holds would be lost. Put it in the target slot to charge it.
                if (stack.Collectible is IEmcStorageItem) continue;
                if (mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack) && mod.Emc.StackData.ModeOf(stack.Collectible) == StackDataMode.Item) continue;
                double value = mod.Emc.IsNoTransmute(stack.Collectible) ? 0 : mod.BurnValue(stack);
                if (value <= 0 || Stored + value > capacity) continue;

                int n = (int)Math.Min(Math.Floor(burnBudget), Math.Min(stack.StackSize, Math.Floor((capacity - Stored) / value)));
                if (n <= 0) continue;
                slot.TakeOut(n);
                slot.MarkDirty();
                Stored += value * n;
                burnBudget -= n;
                changed = true;
            }

            // 2. Spend EMC on copies of the target.
            if (targetValue > 0)
            {
                makeBudget = Math.Min(makeBudget + outputsPerSecond * speed * dt, outputsPerSecond * speed * 2 + 1);
                while (makeBudget >= 1 && Stored >= targetValue)
                {
                    if (!Insert(copy.Clone())) break;
                    Stored -= targetValue;
                    makeBudget -= 1;
                    changed = true;
                }
            }
            else
            {
                makeBudget = 0;
            }

            windowSeconds += dt;
            if (windowSeconds >= 3)
            {
                double rate = acceptedInWindow / windowSeconds;
                if (Math.Abs(rate - IncomingPerSecond) > 1e-6) changed = true;
                IncomingPerSecond = rate;
                acceptedInWindow = 0;
                windowSeconds = 0;
            }
            long now = Api.World.ElapsedMilliseconds;
            if (changed || (Math.Abs(Stored - syncedStored) > 1e-6 && now - lastSyncMs >= 1000))
            {
                syncedStored = Stored;
                lastSyncMs = now;
                MarkDirty(false);
            }
        }

        bool Insert(ItemStack stack)
        {
            // Prefer topping up an existing stack, then the first empty slot.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < inventory.Count; i++)
                {
                    ItemSlot slot = inventory[i];
                    if (pass == 0)
                    {
                        if (slot.Itemstack == null || slot.Itemstack.Collectible != stack.Collectible) continue;
                        if (!slot.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes)) continue;
                        if (slot.StackSize + stack.StackSize > slot.Itemstack.Collectible.MaxStackSize) continue;
                        slot.Itemstack.StackSize += stack.StackSize;
                        slot.MarkDirty();
                        return true;
                    }
                    if (slot.Itemstack == null)
                    {
                        slot.Itemstack = stack;
                        slot.MarkDirty();
                        return true;
                    }
                }
            }
            return false;
        }

        public double AcceptEmc(double amount)
        {
            if (!EmcTransfer.Usable(amount)) return 0;
            double take = Math.Min(amount, capacity - Stored);
            if (take <= 0) return 0;
            Stored += take;
            acceptedInWindow += take;
            return take;
        }

        #region GUI and networking (same protocol as vanilla containers)

        public override void DropContents(Vec3d atPos)
        {
            inventory.DropAll(atPos);
        }

        public override void CheckInventoryClearedMidTick() { }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client) ToggleDialog(byPlayer);
            return true;
        }

        void ToggleDialog(IPlayer byPlayer)
        {
            var capi = (ICoreClientAPI)Api;
            if (dialog == null)
            {
                string title = Block.GetPlacedBlockName(Api.World, Pos);
                dialog = new GuiDialogCondenser(title, inventory, Pos, capi, this);
                // Already open in another window (the game refuses a second one): don't keep a dead dialog that
                // would swallow every later click.
                if (dialog.IsDuplicate) { dialog.Dispose(); dialog = null; return; }
                dialog.OnClosed += () => { dialog?.Dispose(); dialog = null; };
                if (!dialog.TryOpen()) { dialog.Dispose(); dialog = null; return; }
                capi.Network.SendPacketClient(inventory.Open(byPlayer));
                capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Open);
            }
            else
            {
                dialog.TryClose();
            }
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }
            bool allowed = Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use);
            if (packetid == (int)EnumBlockEntityPacketId.Open)
            {
                if (allowed)
                {
                    player.InventoryManager?.OpenInventory(inventory);
                }
                return;
            }
            if (packetid < 1000)
            {
                if (!allowed)
                {
                    inventory.InvNetworkUtil.SendInventoryRollback((IServerPlayer)player, packetid, data);
                    return;
                }
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                (Api as ICoreClientAPI)?.World.Player.InventoryManager.CloseInventory(inventory);
                dialog?.TryClose();
            }
        }

        #endregion

        #region Lifecycle and persistence

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            dialog?.TryClose();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            dialog?.TryClose();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree); // the inventory, under "inventory" as before
            tree.SetDouble("emc", Stored);
            tree.SetDouble("incoming", IncomingPerSecond);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            if (Api != null) inventory.AfterBlocksLoaded(Api.World);
            Stored = EmcTransfer.Clean(tree.GetDouble("emc"));
            IncomingPerSecond = EmcTransfer.Clean(tree.GetDouble("incoming"));
            dialog?.UpdateInfo();
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            // Not the container's own info (a "stored food perish speed" line); just what block behaviors add.
            foreach (var behavior in Behaviors) behavior.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:condenser-stored", EmcFormat.Format(Stored)));
            if (IncomingPerSecond > 0) dsc.AppendLine(Lang.Get("vintagee:condenser-incoming", EmcFormat.Format(IncomingPerSecond)));
            ItemStack target = inventory[TargetSlot].Itemstack;
            if (target?.Collectible is IEmcStorageItem)
            {
                dsc.AppendLine(Lang.Get("vintagee:condenser-charging", target.GetName(), EmcFormat.Format(StoredEmc.Get(target)), EmcFormat.Format(StoredEmc.Capacity(target))));
            }
            else if (target != null)
            {
                dsc.AppendLine(Lang.Get("vintagee:condenser-target", target.GetName(), EmcFormat.Format(TargetValue)));
            }
        }

        #endregion
    }

    /// <summary>
    /// Slot 0 is the target. Hoppers/chutes and shift-clicks only ever fill storage, and automation only
    /// pulls finished copies of the target back out, so inputs are never extracted before they are burned.
    /// </summary>
    public class CondenserInventory : InventoryGeneric
    {
        public CondenserInventory(int slots) : base(slots, null, null, null, (id, inv) => new ItemSlotSurvival(inv)) { }

        public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
        {
            if (targetSlot == this[BEEnergyCondenser.TargetSlot]) return 0;
            return base.GetSuitability(sourceSlot, targetSlot, isMerge);
        }

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            ItemStack incoming = fromSlot?.Itemstack;
            if (incoming == null) return null;
            ItemSlot firstEmpty = null;
            for (int i = 1; i < Count; i++)
            {
                ItemSlot slot = this[i];
                if (slot.Empty) { firstEmpty ??= slot; continue; }
                if (slot.Itemstack.Collectible == incoming.Collectible && slot.StackSize < slot.Itemstack.Collectible.MaxStackSize
                    && slot.Itemstack.Equals(Api.World, incoming, GlobalConstants.IgnoredStackAttributes))
                {
                    return slot;
                }
            }
            return firstEmpty;
        }

        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
        {
            ItemStack target = this[BEEnergyCondenser.TargetSlot].Itemstack;
            if (target == null) return null;
            for (int i = 1; i < Count; i++)
            {
                if (this[i].Itemstack?.Collectible == target.Collectible) return this[i];
            }
            return null;
        }
    }
}
