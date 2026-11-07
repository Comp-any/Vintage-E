using Vintagestory.API.MathTools;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Shared plumbing for this mod's container blocks (alchemical chest, matter furnaces): the inventory, opening
    /// it with the game's standard block-inventory window, claims checks, saving, id remapping and dropping the
    /// contents when broken. Works with hoppers and chutes through <see cref="IBlockEntityContainer"/>.
    /// </summary>
    public abstract class BEInventoryBase : BlockEntityContainer, IEmcInteractable
    {
        protected InventoryBase inventory;
        GuiDialogBlockEntityInventory dialog;

        /// <summary>
        /// The game's BlockEntityContainer, not just its IBlockEntityContainer interface: chutes and hoppers only look
        /// for the class (BlockEntityItemFlow asks for GetBlockEntity&lt;BlockEntityContainer&gt;), so before 1.18.10
        /// they couldn't see these blocks at all. Saving is unchanged: the base class keeps the inventory under the
        /// same "inventory" key.
        /// </summary>
        public override InventoryBase Inventory => inventory ??= CreateInventory();

        /// <summary>Slot columns in the window.</summary>
        protected virtual int Columns => 4;

        protected abstract InventoryBase CreateInventory();

        public override void Initialize(ICoreAPI api)
        {
            inventory ??= CreateInventory();
            base.Initialize(api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnSlotModified;
        }

        protected virtual void OnSlotModified(int slotId)
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                MarkDirty(false);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
            }
        }

        public override void CheckInventoryClearedMidTick() { }

        public override void DropContents(Vintagestory.API.MathTools.Vec3d atPos)
        {
            inventory?.DropAll(atPos);
        }

        public virtual bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
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
                dialog = new GuiDialogBlockEntityInventory(title, inventory, Pos, Columns, capi);
                dialog.OnClosed += () =>
                {
                    dialog?.Dispose();
                    dialog = null;
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Close);
                    capi.World.Player.InventoryManager.CloseInventory(inventory);
                };
                dialog.TryOpen();
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
                if (allowed) player.InventoryManager?.OpenInventory(inventory);
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

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            inventory ??= CreateInventory();
            base.FromTreeAttributes(tree, worldAccessForResolve);
            if (Api != null) inventory.AfterBlocksLoaded(Api.World);
        }
    }

    /// <summary>
    /// Alchemical Chest: 104 slots (8 rows of 13) for all your transmutation materials. As in ProjectE, some trinkets
    /// work from inside it, once a second: a Black Hole Band or Void Ring pulls loose items nearby into the chest, a
    /// Repair Talisman repairs everything in it, and a Gem of Eternal Density condenses the cheap things in it into its
    /// target (following the gem's filter).
    /// </summary>
    public class BEAlchemicalChest : BEInventoryBase
    {
        public override string InventoryClassName => "vintageechest";
        protected override int Columns => 13;

        protected override InventoryBase CreateInventory()
        {
            int slots = Block?.Attributes?["quantitySlots"].AsInt(104) ?? 104;
            var inv = new InventoryGeneric(slots, null, null, null);
            // Like the game's chests: a chute or hopper underneath takes from the first filled slot; any side fills it.
            inv.OnGetAutoPullFromSlot = face => face == BlockFacing.DOWN ? inv.FirstOrDefault(slot => !slot.Empty) : null;
            return inv;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 1000);
        }

        void OnServerTick(float dt)
        {
            try { ChestTick(); }
            catch (System.Exception e) { Api.Logger.Warning("Vintage-E alchemical chest at {0}: {1}", Pos, e.Message); }
        }

        void ChestTick()
        {
            var mod = Api.ModLoader.GetModSystem<VintageEModSystem>();
            TrinketEffects fx = mod?.Trinkets;
            if (fx == null || inventory == null) return;
            bool pull = false, repair = false;
            float pullRadius = 0;
            ItemSlot gem = null;
            foreach (ItemSlot slot in inventory)
            {
                if (!(slot?.Itemstack?.Collectible is ItemEmcTrinket t)) continue;
                if (t.IsToggleable && !ItemEmcTrinket.IsActive(slot.Itemstack)) continue; // switched off: does nothing, as when carried
                switch (t.Kind)
                {
                    case "blackholeband": pull = true; pullRadius = System.Math.Max(pullRadius, TrinketEffects.BandPullRadius); break;
                    case "voidring": pull = true; pullRadius = System.Math.Max(pullRadius, TrinketEffects.VoidPullRadius); gem ??= slot; break;
                    case "gemofeternaldensity": gem ??= slot; break;
                    case "repairtalisman": repair = true; break;
                }
            }
            Vintagestory.API.MathTools.Vec3d center = Pos.ToVec3d().Add(0.5, 0.6, 0.5);
            if (pull)
            {
                fx.Attract(center, pullRadius, outsideClaimsOnly: true);
                Collect(center);
            }
            if (repair)
            {
                foreach (ItemSlot slot in inventory) TrinketEffects.RepairOnce(slot, mod.Config);
            }
            if (gem != null) Condense(mod, gem);
        }

        /// <summary>Items that reached the chest go in.</summary>
        void Collect(Vintagestory.API.MathTools.Vec3d center)
        {
            foreach (Vintagestory.API.Common.Entities.Entity e in Api.World.GetEntitiesAround(center, 1.5f, 1.5f, x => x is EntityItem && x.Alive))
            {
                var ei = (EntityItem)e;
                if (ei.Itemstack == null) continue;
                if (ei.ByPlayerUid != null && Api.World.ElapsedMilliseconds - ei.itemSpawnedMilliseconds < 3000) continue; // just dropped: let them pick it up
                var source = new DummySlot(ei.Itemstack);
                foreach (ItemSlot slot in inventory)
                {
                    if (source.Empty) break;
                    if (slot.Itemstack?.Collectible is ItemEmcTrinket) continue;
                    source.TryPutInto(Api.World, slot, source.StackSize);
                }
                if (source.Empty) e.Die(EnumDespawnReason.PickedUp);
                else ei.Itemstack = source.Itemstack;
            }
        }

        /// <summary>The Gem of Eternal Density on the chest's contents: cheap things become its target, which goes back in the chest.</summary>
        void Condense(VintageEModSystem mod, ItemSlot gemSlot)
        {
            int mode = Vintagestory.API.MathTools.GameMath.Clamp(ItemEmcTrinket.GetMode(gemSlot.Itemstack), 0, ItemEmcTrinket.DensityTargets.Length - 1);
            CollectibleObject target = ItemEmcTrinket.Resolve(Api.World, ItemEmcTrinket.DensityTargets[mode]);
            double targetValue = target == null || mod.Emc.IsNoTransmute(target) ? 0 : mod.Emc.GetValue(target);
            if (targetValue <= 0) return;
            double maxInput = gemSlot.Itemstack.Collectible.Attributes?["maxInputValue"].AsDouble(8) ?? 8;
            double stored = StoredEmc.Get(gemSlot.Itemstack);
            bool changed = false;
            foreach (ItemSlot slot in inventory)
            {
                ItemStack stack = slot?.Itemstack;
                if (stack == null || slot == gemSlot) continue;
                CollectibleObject c = stack.Collectible;
                if (c == target || c is ItemEmcTrinket || c is IEmcStorageItem || c.GetMaxDurability(stack) > 0) continue;
                if (StackContents.HasNestedStacks(stack)) continue;
                if (!GemFilter.Passes(gemSlot.Itemstack, c)) continue;
                double value = mod.Emc.IsNoTransmute(c) ? 0 : mod.Emc.GetValue(stack);
                if (value <= 0 || value > maxInput) continue;
                stored += value * stack.StackSize;
                slot.Itemstack = null;
                slot.MarkDirty();
                changed = true;
            }
            int made = 0;
            while (stored >= targetValue && made < 64)
            {
                var source = new DummySlot(new ItemStack(target));
                foreach (ItemSlot slot in inventory)
                {
                    if (source.Empty) break;
                    if (slot == gemSlot || slot.Itemstack?.Collectible is ItemEmcTrinket) continue;
                    source.TryPutInto(Api.World, slot, 1);
                }
                if (!source.Empty) break; // the chest is full: the EMC stays on the gem
                stored -= targetValue;
                made++;
                changed = true;
            }
            if (System.Math.Abs(stored - StoredEmc.Get(gemSlot.Itemstack)) > 1e-9) { StoredEmc.Set(gemSlot.Itemstack, stored); gemSlot.MarkDirty(); changed = true; }
            if (changed) MarkDirty(false);
        }
    }
}
