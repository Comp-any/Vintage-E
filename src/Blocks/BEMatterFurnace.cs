using Vintagestory.API.MathTools;
using System;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Dark/Red Matter Furnace: the game's own firepit (same window, fuel, cooking pots, crucibles, spits and
    /// ignition) built from matter, so it burns hotter, cooks faster and makes fuel last longer. Block attributes:
    /// <c>heatModifier</c> (× the fuel's burn temperature), <c>burnDurationModifier</c> (× how long fuel lasts),
    /// <c>cookingSpeed</c> (÷ cooking/smelting time once hot), <c>emcPerSecond</c> and <c>emcTemperature</c>.
    /// It can also run on EMC pushed in by collectors and relays: when it has something to heat and no fuel burning,
    /// it lights itself and burns EMC instead, at <c>emcPerSecond</c> and <c>emcTemperature</c> °C (× heatModifier).
    /// </summary>
    public class BEMatterFurnace : BlockEntityFirepit, IEmcAcceptor
    {
        public const double EmcCapacity = 1000;
        const float EmcBurnSeconds = 10;

        double emcBuffer;

        /// <summary>The button in the bottom right of the furnace's window sends this to switch the mode.</summary>
        public const int PacketToggleMode = 1350;

        /// <summary>
        /// Heating mode (the window's mode button): keeps the furnace at its top temperature, lighting itself from its
        /// fuel or its EMC whenever the fire would go out, with or without anything to heat. Normal mode is the firepit's
        /// own behaviour: it lights on EMC only when there is something to heat, and fuel burns out and goes cold.
        /// </summary>
        public bool HeatingMode { get; private set; }
        float heatModifier = 1.4f, burnDurationModifier = 3f, cookingSpeed = 4f, emcTemperature = 1300;
        double emcPerSecond = 2;

        static readonly MethodInfo ContentMesh = typeof(BlockEntityFirepit).GetMethod("getContentMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ContentsRendererField = typeof(BlockEntityFirepit).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// The firepit draws items cooked on a spit (meat...) 0.6 blocks up, where its spit is. The furnace has no spit,
        /// so they'd float: they're lowered by this much to lie on the furnace instead.
        /// </summary>
        const float SpitDrop = 0.55f;
        ModelTransform loweredTransform;
        // The firepit renderer's fields, found once (every furnace on screen asked for them four times a second).
        static Type rendererType;
        static FieldInfo rendererStack, rendererChild, rendererTransform;

        static readonly FieldInfo InventoryField = typeof(BlockEntityFirepit).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo SlotModifiedHandler = typeof(BlockEntityFirepit).GetMethod("OnSlotModified", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Whether the chute-aware inventory went in (false if the game's firepit changed and reflection failed).</summary>
        public bool ChuteInventory { get; private set; }

        /// <summary>
        /// Swaps the firepit's inventory for one with ProjectE's hopper rules (see <see cref="MatterFurnaceInventory"/>):
        /// the game's firepit takes chutes in only, by best fit, and never lets one take anything out. Done before
        /// Initialize, so everything the firepit sets up later uses the new one.
        /// </summary>
        public BEMatterFurnace()
        {
            try
            {
                if (InventoryField?.GetValue(this) is InventorySmelting old && SlotModifiedHandler != null)
                {
                    var handler = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), this, SlotModifiedHandler);
                    old.SlotModified -= handler;
                    var replacement = new MatterFurnaceInventory(null, null);
                    replacement.SlotModified += handler;
                    InventoryField.SetValue(this, replacement);
                    ChuteInventory = true;
                }
            }
            catch { ChuteInventory = false; }
        }

        public override float HeatModifier => heatModifier;
        public override float BurnDurationModifier => burnDurationModifier;
        public override float maxCookingTime() => base.maxCookingTime() / Math.Max(0.1f, cookingSpeed);

        public override string DialogTitle
        {
            get
            {
                try { return Block?.GetPlacedBlockName(Api.World, Pos) ?? base.DialogTitle; }
                catch { return base.DialogTitle; }
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            JsonObject attrs = Block?.Attributes;
            heatModifier = attrs?["heatModifier"].AsFloat(1.4f) ?? 1.4f;
            burnDurationModifier = attrs?["burnDurationModifier"].AsFloat(3f) ?? 3f;
            cookingSpeed = attrs?["cookingSpeed"].AsFloat(4f) ?? 4f;
            emcPerSecond = attrs?["emcPerSecond"].AsDouble(2) ?? 2;
            emcTemperature = attrs?["emcTemperature"].AsFloat(1300) ?? 1300;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnEmcTick, 1000);
            else
            {
                LowerSpitItem();
                // The firepit sets its renderer up again whenever a slot changes (cooking done, a new item): lower it at
                // once, in the same call (this handler runs after the firepit's own), so it never shows at spit height.
                Inventory.SlotModified += _ => LowerSpitItem();
                RegisterGameTickListener(_ => LowerSpitItem(), 250);
            }
        }

        /// <summary>Client: whenever the firepit renderer shows a spit item, lowers it onto the furnace (a lowered copy of its transform).</summary>
        void LowerSpitItem()
        {
            // The renderer's fields are public in the game's FirepitContentsRenderer; read them by name.
            object r = ContentsRendererField?.GetValue(this);
            if (r == null) return;
            Type type = r.GetType();
            if (!ReferenceEquals(type, rendererType))
            {
                rendererStack = type.GetField("ContentStack");
                rendererChild = type.GetField("contentStackRenderer");
                rendererTransform = type.GetField("Transform");
                rendererType = type;
            }
            FieldInfo stackField = rendererStack, childField = rendererChild, transformField = rendererTransform;
            if (stackField == null || childField == null || transformField == null) return;
            if (!(stackField.GetValue(r) is ItemStack stack) || stack.Class != EnumItemClass.Item || childField.GetValue(r) != null) return;
            if (!(transformField.GetValue(r) is ModelTransform current) || ReferenceEquals(current, loweredTransform)) return;
            ModelTransform t = current.Clone();
            t.Translation.Y -= SpitDrop;
            loweredTransform = t;
            transformField.SetValue(r, t);
        }

        public double AcceptEmc(double amount)
        {
            if (!EmcTransfer.Usable(amount)) return 0;
            double take = Math.Min(amount, Math.Max(0, EmcCapacity - emcBuffer));
            emcBuffer += take;
            return take;
        }

        /// <summary>Switches between normal and heating mode, saved with the furnace and sent to players.</summary>
        public void SetHeatingMode(bool on)
        {
            if (HeatingMode == on) return;
            HeatingMode = on;
            if (on) KeepHot();
            MarkDirty(false);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketToggleMode)
            {
                // Anyone who may use the furnace (claims) may switch its mode.
                if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) SetHeatingMode(!HeatingMode);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        /// <summary>Heating mode: lets the fuel light even after the furnace has gone cold (the firepit only relights while still smouldering).</summary>
        void KeepHot()
        {
            if (!HeatingMode || IsBurning) return;
            var fuel = fuelSlot.Itemstack?.Collectible.GetCombustibleProperties(Api.World, fuelSlot.Itemstack, null);
            if (fuel?.BurnTemperature > 0 && !canIgniteFuel)
            {
                canIgniteFuel = true; // the firepit's own tick lights it next
                MarkDirty(false);
            }
        }

        /// <summary>
        /// Keeps the fire going on EMC while no fuel is burning: in normal mode only while there is something to heat, in
        /// heating mode always. Heating mode also relights from fuel when the furnace went cold.
        /// </summary>
        void OnEmcTick(float dt)
        {
            KeepHot();
            double cost = emcPerSecond * EmcBurnSeconds;
            if (emcBuffer < cost) return;
            if (!HeatingMode)
            {
                if (inputStack == null) return;
                bool wantsHeat;
                try { wantsHeat = canHeatInput(); }
                catch { wantsHeat = false; }
                if (!wantsHeat) return;
            }

            if (!IsBurning)
            {
                if (fuelSlot.Itemstack?.Collectible.GetCombustibleProperties(Api.World, fuelSlot.Itemstack, null)?.BurnTemperature > 0) return; // real fuel first
                emcBuffer -= cost;
                maxFuelBurnTime = fuelBurnTime = EmcBurnSeconds;
                maxTemperature = (int)(emcTemperature * HeatModifier);
                setBlockState("lit");
                MarkDirty(true);
            }
            else if (fuelBurnTime < 2 && fuelSlot.Empty)
            {
                emcBuffer -= cost;
                fuelBurnTime += EmcBurnSeconds;
                maxFuelBurnTime = Math.Max(maxFuelBurnTime, fuelBurnTime);
                maxTemperature = Math.Max(maxTemperature, (int)(emcTemperature * HeatModifier));
                MarkDirty(false);
            }
        }

        static readonly FieldInfo ClientDialogField = typeof(BlockEntityFirepit).GetField("clientDialog", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo SetDialogValuesMethod = typeof(BlockEntityFirepit).GetMethod("SetDialogValues", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Opens Vintage-E's furnace window: the game's firepit window plus the mode button (only matter furnaces get it;
        /// the game's firepits keep theirs). Kept in the firepit's own dialog field, so it gets the firepit's updates.
        /// If this game version's firepit changed and that can't be done, the game's own window opens.
        /// </summary>
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side != EnumAppSide.Client || ClientDialogField == null || SetDialogValuesMethod == null)
                return base.OnPlayerRightClick(byPlayer, blockSel);
            toggleInventoryDialogClient(byPlayer, () =>
            {
                var tree = new SyncedTreeAttribute();
                SetDialogValuesMethod.Invoke(this, new object[] { tree });
                var dlg = new GuiDialogMatterFurnace(DialogTitle, Inventory, Pos, tree, Api as ICoreClientAPI, this);
                ClientDialogField.SetValue(this, dlg);
                return dlg;
            });
            return true;
        }

        /// <summary>Client: asks the server to switch the mode (the furnace's window button).</summary>
        public void RequestToggleMode() =>
            (Api as ICoreClientAPI)?.Network.SendBlockEntityPacket(Pos.X, Pos.Y, Pos.Z, PacketToggleMode, null);

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            // Draw whatever sits on the fire the way the firepit does, but our own furnace instead of the logs.
            try
            {
                ItemStack content = inputStack ?? outputStack;
                if (ContentMesh?.Invoke(this, new object[] { content, tesselator }) is MeshData mesh) mesher.AddMeshData(mesh);
            }
            catch (Exception e)
            {
                Api?.Logger.VerboseDebug("Vintage-E furnace content mesh: {0}", e.Message);
            }
            return false;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("vintageeEmc", emcBuffer);
            tree.SetBool("vintageeHeating", HeatingMode);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            emcBuffer = EmcTransfer.Clean(tree.GetDouble("vintageeEmc"));
            bool heating = tree.GetBool("vintageeHeating");
            bool changed = heating != HeatingMode;
            HeatingMode = heating;
            if (Api?.Side == EnumAppSide.Client)
            {
                LowerSpitItem();
                if (changed) (ClientDialogField?.GetValue(this) as GuiDialogMatterFurnace)?.UpdateModeButton();
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:furnace-info", (int)furnaceTemperature, EmcFormat.Format(emcBuffer), EmcFormat.Format(EmcCapacity)));
            dsc.AppendLine(Lang.Get(HeatingMode ? "vintagee:furnace-mode-heating-info" : "vintagee:furnace-mode-normal-info"));
        }
    }

    /// <summary>
    /// Matter furnaces placed with Vintage-E 1.4.0 (before they became firepits): turns itself into the new furnace
    /// as soon as it loads, dropping whatever was inside on top of it.
    /// </summary>
    /// <summary>
    /// The firepit's inventory (slot 0 fuel, 1 input, 2 output, 3–6 cooking pot slots) with ProjectE's matter furnace
    /// hopper rules: a chute or hopper from above puts things to smelt or cook into the input, one from the side puts
    /// fuel into the fuel slot, and one underneath takes from the output.
    /// </summary>
    public class MatterFurnaceInventory : InventorySmelting
    {
        public MatterFurnaceInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api) { }

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            ItemStack stack = fromSlot?.Itemstack;
            if (stack == null) return null;
            if (atBlockFace == BlockFacing.UP)
            {
                ItemSlot input = this[1];
                bool cookable = stack.Collectible.CombustibleProps?.SmeltedStack != null
                    || stack.Collectible is BlockSmeltingContainer || stack.Collectible is BlockCookingContainer;
                return cookable && CanTake(input, stack) ? input : null;
            }
            if (atBlockFace.IsHorizontal)
            {
                ItemSlot fuel = this[0];
                bool burns = (stack.Collectible.CombustibleProps?.BurnTemperature ?? 0) > 0;
                return burns && CanTake(fuel, stack) ? fuel : null;
            }
            return null;
        }

        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace) =>
            atBlockFace == BlockFacing.DOWN && !this[2].Empty ? this[2] : null;

        bool CanTake(ItemSlot slot, ItemStack stack)
        {
            if (slot.Empty) return true;
            return slot.Itemstack.Equals(Api?.World, stack, GlobalConstants.IgnoredStackAttributes)
                && slot.StackSize < slot.Itemstack.Collectible.MaxStackSize;
        }
    }

    public class BEMatterFurnaceLegacy : BEInventoryBase
    {
        public override string InventoryClassName => "vintageefurnace";
        protected override int Columns => 3;
        protected override InventoryBase CreateInventory() => new InventoryGeneric(3, null, null);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterDelayedCallback(_ => Convert(), 500);
        }

        void Convert()
        {
            Block current = Api.World.BlockAccessor.GetBlock(Pos);
            if (current?.Code == null || current.Code.Domain != VintageEModSystem.ModId) return;
            string type = current.Variant?["type"] ?? "dark";
            Block replacement = Api.World.GetBlock(new AssetLocation(VintageEModSystem.ModId, "furnace-" + type + "-cold"));
            if (replacement == null || replacement.Id == 0) return;
            var contents = new System.Collections.Generic.List<ItemStack>();
            foreach (ItemSlot slot in Inventory)
            {
                if (slot?.Itemstack != null) contents.Add(slot.Itemstack.Clone());
                if (slot != null) slot.Itemstack = null;
            }
            Api.World.BlockAccessor.SetBlock(replacement.Id, Pos);
            foreach (ItemStack stack in contents) Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
        }
    }
}
