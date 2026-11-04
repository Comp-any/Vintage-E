using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Right-click a block to transmute it (stone types, wood types, sand ↔ gravel...); sneak to go the other way.
    /// The tool-mode key picks 1x1, 3x3 or 5x5. Also used as a non-consumed catalyst in crafting recipes.
    /// </summary>
    public class ItemPhilosophersStone : Item
    {
        const int MaxRadius = 2;
        SkillItem[] toolModes;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api is ICoreClientAPI capi) toolModes = AreaToolModes.Create(capi, MaxRadius);
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (toolModes != null) foreach (SkillItem m in toolModes) m?.Dispose();
            base.OnUnloaded(api);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => toolModes;
        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => AreaToolModes.GetRadius(slot);
        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode) => AreaToolModes.SetRadius(slot, toolMode);

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null && firstEvent && byEntity is EntityPlayer)
            {
                // ProjectE: right-click in the air opens a crafting grid. Here that's the game's own, in the inventory window.
                handling = EnumHandHandling.PreventDefault;
                if (api is ICoreClientAPI capi) OpenCrafting(capi);
                return;
            }
            if (blockSel == null || !firstEvent || !(byEntity is EntityPlayer ep) || ep.Player == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (api.Side != EnumAppSide.Server) return;

            IPlayer player = ep.Player;
            var mod = api.ModLoader.GetModSystem<VintageEModSystem>();
            IBlockAccessor ba = api.World.BlockAccessor;
            Block clicked = ba.GetBlock(blockSel.Position);
            Block target = mod.WorldTransmutation?.GetTarget(clicked, byEntity.Controls.Sneak);
            if (target == null)
            {
                (player as IServerPlayer)?.SendIngameError("vintagee-notransmutable", Lang.Get("vintagee:error-notransmutable"));
                return;
            }

            int radius = GameMathClamp(AreaToolModes.GetRadius(slot), 0, MaxRadius);
            int changed = 0;
            foreach (var pos in AreaToolModes.Plane(blockSel.Position, blockSel.Face, radius))
            {
                if (ba.GetBlock(pos).Id != clicked.Id) continue;
                if (!api.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                ba.SetBlock(target.Id, pos);
                changed++;
            }
            if (changed > 0)
            {
                PESounds.Play(api.World, PESounds.Transmute, "game:sounds/effect/timeswitch", blockSel.Position, 16, 0.6f);
            }
        }

        static void OpenCrafting(ICoreClientAPI capi)
        {
            try
            {
                GuiDialog inv = System.Linq.Enumerable.FirstOrDefault(capi.Gui.LoadedGuis, g => g.GetType().Name == "GuiDialogInventory");
                if (inv == null) return;
                if (!inv.IsOpened()) inv.TryOpen();
            }
            catch (System.Exception e) { capi.Logger.Warning("Vintage-E: could not open the crafting grid: {0}", e.Message); }
        }

        static int GameMathClamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-transmute", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-stone-craft", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-transmute-reverse", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-area", HotKeyCode = "toolmodeselect" }
            };
        }
    }
}
