using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Dark/red matter tools: never wear out, and (when <c>maxAreaRadius</c> &gt; 0) can mine a 3x3 or 5x5
    /// area facing the player — picked with the tool-mode key. Works on top of vanilla tool classes
    /// (ItemAxe keeps tree felling, ItemHoe keeps tilling, and so on).
    /// <code>"behaviors": [ { "name": "VintageEMatterTool", "properties": { "maxAreaRadius": 1, "defaultAreaRadius": 1 } } ]</code>
    /// defaultAreaRadius is the mode a fresh tool starts in (sledgehammers start at 3x3).
    /// <para>
    /// Right-click, as ProjectE's tools (the area mode is ProjectE's charge; the charge key steps it too): the pickaxe
    /// and Morning Star mine the whole vein of the ore clicked, the shovel and Morning Star the whole patch of gravel or
    /// clay; the shovel digs a flat square around the clicked block; the axe clears every log around you and the shears
    /// every leaf block (5 × the charge across, 10 × up and down); the shears in the air shear the creatures around you.
    /// </para>
    /// </summary>
    public class CollectibleBehaviorMatterTool : CollectibleBehavior, IChargeableItem
    {
        int maxRadius;
        int defaultRadius;
        bool shots;
        SkillItem[] toolModes;

        public CollectibleBehaviorMatterTool(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            maxRadius = properties?["maxAreaRadius"].AsInt(0) ?? 0;
            defaultRadius = System.Math.Clamp(properties?["defaultAreaRadius"].AsInt(0) ?? 0, 0, maxRadius);
            shots = properties?["shotModes"].AsBool(false) ?? false; // red matter: 3 tall / 3 wide / 3 long too
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api is ICoreClientAPI capi && maxRadius > 0) toolModes = AreaToolModes.Create(capi, maxRadius, shots);
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (toolModes != null) foreach (SkillItem m in toolModes) m?.Dispose();
            base.OnUnloaded(api);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => toolModes;

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => maxRadius > 0 ? AreaToolModes.GetRadius(slot, defaultRadius) : 0;

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (maxRadius > 0) AreaToolModes.SetRadius(slot, toolMode);
        }

        // ------------------------------------------------------------------ charge key

        public int MaxCharge(ItemSlot slot) => maxRadius;
        public int GetCharge(ItemSlot slot) => maxRadius > 0 ? System.Math.Clamp(AreaToolModes.GetRadius(slot, defaultRadius), 0, maxRadius) : 0;
        public void SetCharge(ItemSlot slot, int charge)
        {
            if (maxRadius > 0) AreaToolModes.SetRadius(slot, System.Math.Clamp(charge, 0, maxRadius));
        }

        // ------------------------------------------------------------------ right-click (ProjectE's tool actions)

        /// <summary>What the tool is, from its code: pickaxe, shovel, axe, shears, morningstar (others: null).</summary>
        string Role
        {
            get
            {
                string path = collObj.Code?.Path;
                if (path == null) return null;
                if (path.EndsWith("-morningstar")) return "morningstar";
                if (!path.StartsWith("mattertool-")) return null;
                string tool = collObj.Variant?["tool"];
                return tool == "pickaxe" || tool == "shovel" || tool == "axe" || tool == "shears" ? tool : null;
            }
        }

        /// <summary>What a right-click would do here (both sides decide the same, so the client sends it on): null for nothing.</summary>
        string ActionFor(EntityAgent byEntity, BlockSelection blockSel)
        {
            string role = Role;
            if (role == null || byEntity.Controls.CtrlKey) return null; // Ctrl + right-click puts the tool on the ground
            if (blockSel == null) return role == "shears" ? "shear" : null;
            Block block = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
            if ((role == "pickaxe" || role == "morningstar") && AreaTools.IsOre(block)) return "vein";
            if ((role == "shovel" || role == "morningstar") && AreaTools.IsShovelVein(block)) return "vein";
            if (role == "shovel") return "dig";
            if (role == "axe" && AreaTools.IsLog(block)) return "logs";
            if (role == "shears" && AreaTools.IsLeaves(block)) return "leaves";
            return null;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (!firstEvent) return;
            string action = ActionFor(byEntity, blockSel);
            if (action == null) return;
            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;
            if (byEntity.World.Side != EnumAppSide.Server || !((byEntity as EntityPlayer)?.Player is IServerPlayer player)) return;
            IWorldAccessor world = byEntity.World;
            int charge = GetCharge(slot);
            int done;
            switch (action)
            {
                case "vein": done = AreaTools.VeinMine(world, player, collObj, blockSel.Position, blockSel.Face, charge); break;
                case "dig": done = AreaTools.DigFlat(world, player, collObj, blockSel.Position, charge); break;
                case "logs": done = AreaTools.ClearAround(world, player, byEntity, AreaTools.IsLog, charge); break;
                case "leaves": done = AreaTools.ClearAround(world, player, byEntity, AreaTools.IsLeaves, charge); break;
                default:
                    done = VintageEModSystem.Of(world.Api)?.Trinkets?.Shear(player, charge) ?? 0;
                    if (done < 0) player.SendIngameError("vintagee-noshear", Lang.GetL(player.LanguageCode, "vintagee:shears-nolist"));
                    return;
            }
            // ProjectE's area actions do nothing at charge 0: say how to get a bigger one.
            if (done == 0 && charge == 0 && (action == "logs" || action == "leaves"))
                player.SendIngameError("vintagee-needcharge", Lang.GetL(player.LanguageCode, "vintagee:tool-needcharge"));
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            string role = Role;
            var list = new System.Collections.Generic.List<WorldInteraction>();
            if (role == "pickaxe" || role == "morningstar") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-veinore", MouseButton = EnumMouseButton.Right });
            if (role == "shovel" || role == "morningstar") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-veingravel", MouseButton = EnumMouseButton.Right });
            if (role == "shovel") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-dig", MouseButton = EnumMouseButton.Right });
            if (role == "axe") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-logs", MouseButton = EnumMouseButton.Right });
            if (role == "shears")
            {
                list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-leaves", MouseButton = EnumMouseButton.Right });
                list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-tool-shear", MouseButton = EnumMouseButton.Right });
            }
            if (maxRadius > 0) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-chargekey", HotKeyCode = VintageEModSystem.ChargeHotkey });
            return list.ToArray();
        }

        public override void OnDamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, ref int amount, ref EnumHandling bhHandling)
        {
            amount = 0;
            bhHandling = EnumHandling.PreventDefault; // matter does not wear
        }

        public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier, ref EnumHandling bhHandling)
        {
            // Runs before the targeted block breaks; we only add the surrounding ones and let the normal break continue.
            if (world.Side != EnumAppSide.Server || maxRadius <= 0 || blockSel == null) return true;
            int mode = System.Math.Min(AreaToolModes.GetRadius(itemslot, defaultRadius), maxRadius + (shots ? AreaToolModes.Shots.Length : 0));
            if (mode <= 0) return true;

            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null || (byEntity as EntityAgent)?.Controls.Sneak == true) return true; // sneak = single block

            IBlockAccessor ba = world.BlockAccessor;
            CollectibleObject tool = itemslot.Itemstack?.Collectible;
            if (tool == null) return true;

            int broken = 0;
            foreach (var pos in AreaToolModes.Positions(blockSel.Position, blockSel.Face, mode, maxRadius))
            {
                if (pos.Equals(blockSel.Position)) continue;
                Block block = ba.GetBlock(pos);
                if (block == null || block.Id == 0 || block.Replaceable >= 6000) continue; // air, grass, liquids
                if (block.RequiredMiningTier > tool.ToolTier) continue;
                if (tool.MiningSpeed == null || !tool.MiningSpeed.ContainsKey(block.BlockMaterial)) continue; // only what this tool is for
                if (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                ba.BreakBlock(pos, player, dropQuantityMultiplier);
                broken++;
            }
            if (broken > 0) PESounds.Play(world, PESounds.Destruct, null, byEntity, 16); // ProjectE's area mining sound
            return true;
        }
    }
}
