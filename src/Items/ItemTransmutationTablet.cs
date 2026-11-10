using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>A portable transmutation table: right-click in the air to open the same GUI anywhere.</summary>
    public class ItemTransmutationTablet : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (api is ICoreClientAPI)
            {
                VintageEModSystem.Of(api).OpenTransmutationDialog(null);
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-opentablet", MouseButton = EnumMouseButton.Right }
            };
        }
    }
}
