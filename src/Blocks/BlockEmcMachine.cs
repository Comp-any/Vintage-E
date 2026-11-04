using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>Anything that can take EMC pushed into it (condensers accept power from collectors).</summary>
    public interface IEmcAcceptor
    {
        /// <summary>Offers <paramref name="amount"/> EMC; returns how much was taken.</summary>
        double AcceptEmc(double amount);
    }

    public interface IEmcInteractable
    {
        bool OnInteract(IPlayer byPlayer, BlockSelection blockSel);
    }

    /// <summary>Block class for collectors and condensers: forwards right-clicks to the block entity.</summary>
    public class BlockEmcMachine : Block
    {
        public override void OnBlockBroken(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // A Klein star inside a collector or relay comes out with it.
            if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(pos) is IStarHolder holder) holder.Star.Drop(world, pos);
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is IEmcInteractable machine)
            {
                return machine.OnInteract(byPlayer, blockSel);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var help = new System.Collections.Generic.List<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? System.Array.Empty<WorldInteraction>());
            string code = Attributes?["interactionHelp"].AsString(null);
            if (code != null) help.Add(new WorldInteraction { ActionLangCode = code, MouseButton = EnumMouseButton.Right });
            string sneakCode = Attributes?["sneakInteractionHelp"].AsString(null);
            if (sneakCode != null)
            {
                help.Add(new WorldInteraction { ActionLangCode = sneakCode, HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            }
            // Lit like the game's bombs: hold right-click with a torch or firestarter.
            string igniteCode = Attributes?["igniteInteractionHelp"].AsString(null);
            if (igniteCode != null && !(world.BlockAccessor.GetBlockEntity(selection.Position) is BENovaExplosive nova && nova.IsLit))
            {
                help.Add(new WorldInteraction { ActionLangCode = igniteCode, MouseButton = EnumMouseButton.Right, Itemstacks = IgniteStacks(world) });
            }
            return help.ToArray();
        }

        /// <summary>Everything that lights things: blocks with the game's CanIgnite behaviour (torches) and its firestarters.</summary>
        ItemStack[] IgniteStacks(IWorldAccessor world) => Vintagestory.API.Util.ObjectCacheUtil.GetOrCreate(api, "vintageeIgniteStacks", () =>
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(world.Collectibles, c => c?.Code != null &&
                    ((c is Block b && b.BlockBehaviors != null && System.Array.Exists(b.BlockBehaviors, bh => bh.GetType().Name == "BlockBehaviorCanIgnite"))
                     || c.GetType().Name == "ItemFirestarter")),
                c => new ItemStack(c))));
    }
}
