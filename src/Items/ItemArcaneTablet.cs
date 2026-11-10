using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// The Transmutation Tablet upgraded with red matter (1.19.0): everything the tablet does (it is one, so every
    /// "is this a tablet" check counts it), plus a crafting window whose grid Emc Craft fills from your EMC.
    /// Right-click opens the tablet; sneak + right-click opens the crafting window.
    /// </summary>
    public class ItemArcaneTablet : ItemTransmutationTablet
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (firstEvent && byEntity?.Controls?.Sneak == true)
            {
                handling = EnumHandHandling.PreventDefault;
                if (api is ICoreClientAPI) VintageEModSystem.Of(api).OpenEmcCraftDialog();
                return;
            }
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-opentablet", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-openemccraft", MouseButton = EnumMouseButton.Right, HotKeyCode = "sneak" }
            };
        }
    }
}
