using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    public static partial class EmcCommands
    {
        /// <summary>
        /// /emc setowner &lt;player&gt; (1.29.2): gives the Power Flower or EMC Link the caller is looking at to another
        /// player, as Project Expansion's /pex setowner does. What it was holding for its old owner goes to the new one.
        /// </summary>
        static TextCommandResult SetOwner(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            if (!(args[0] is IPlayer target)) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-needplayer"));
            BlockSelection sel = args.Caller.Player?.CurrentBlockSelection;
            if (sel?.Position == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-setowner-look"));
            if (!(sapi.World.BlockAccessor.GetBlockEntity(sel.Position) is IOwnedMachine owned))
                return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-setowner-notowned"));
            string was = owned.OwnerName;
            owned.SetOwner(target.PlayerUID, target.PlayerName);
            string name = sapi.World.BlockAccessor.GetBlock(sel.Position).GetPlacedBlockName(sapi.World, sel.Position);
            return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-setowner-done", name, target.PlayerName, was ?? "-"));
        }
    }
}
