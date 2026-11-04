using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// Dark/red matter tools: never wear out, and (when <c>maxAreaRadius</c> &gt; 0) can mine a 3x3 or 5x5
    /// area facing the player — picked with the tool-mode key. Works on top of vanilla tool classes
    /// (ItemAxe keeps tree felling, ItemHoe keeps tilling, and so on).
    /// <code>"behaviors": [ { "name": "VintageEMatterTool", "properties": { "maxAreaRadius": 1, "defaultAreaRadius": 1 } } ]</code>
    /// defaultAreaRadius is the mode a fresh tool starts in (sledgehammers start at 3x3).
    /// </summary>
    public class CollectibleBehaviorMatterTool : CollectibleBehavior
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
