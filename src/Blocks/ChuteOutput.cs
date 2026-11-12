using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>What a chute, hopper or screw next to a machine does with what the machine makes.</summary>
    public enum ChuteLink
    {
        /// <summary>No chute, hopper or screw there.</summary>
        None,
        /// <summary>It pulls from the machine itself (it has a pulling opening towards the machine).</summary>
        Pulls,
        /// <summary>The machine hands its output to it: it can only lead away.</summary>
        HandedTo,
        /// <summary>It has no opening towards the machine.</summary>
        Closed,
        /// <summary>It pushes towards the machine and may be bringing things in, so it's handed nothing.</summary>
        BringsIn,
        /// <summary>A flat run of chutes that ends in nothing (air, a wall).</summary>
        NoEnd,
        /// <summary>More straight chutes in a row than the game's chutes can carry something sideways.</summary>
        TooLong,
        /// <summary>The run ends in another condenser: the two would swap copies forever.</summary>
        OtherCondenser,
        /// <summary>The piece that should carry things away has nothing to put them into (they would spill).</summary>
        OpenEnd
    }

    /// <summary>
    /// Which chutes and hoppers a machine may hand its output to. The game's chutes only take from a container through
    /// an opening that pulls (<c>pullFaces</c>): a straight chute underneath, or an elbow, T or 3-way that turns down.
    /// A straight chute lying flat never pulls and carries things both ways, and a hopper never pulls either, so next
    /// to a machine they did nothing. A machine pushing into them blindly would be wrong as often as right (the same
    /// chute may be bringing things in), so it only does where the chutes can't be anything but a way out:
    /// <list type="bullet">
    /// <item>a piece that can't push back into the machine (a hopper underneath, a cross with a side opening), if it
    /// has something to put things into;</item>
    /// <item>one or two straight chutes in a row that lead into a container (not another condenser), or into a piece
    /// that drops things down. Nothing can come back along such a run.</item>
    /// </list>
    /// Never upwards, and never where things would spill onto the ground.
    /// </summary>
    public static class ChuteOutput
    {
        /// <summary>
        /// The game's chutes move a stack at most three chutes sideways before it needs a drop
        /// (<c>MaxHorizontalTravel</c>); the machine's push counts as the first, so two straight chutes can still push on.
        /// </summary>
        public const int MaxStraightChutes = 2;

        static BlockEntityContainer ContainerAt(IBlockAccessor ba, BlockPos pos) => ba.GetBlock(pos)?.GetBlockEntity<BlockEntityContainer>(pos);

        /// <summary>A chute lying flat along <paramref name="dir"/>: no pulling opening, and it pushes exactly both ways.</summary>
        static bool IsStraight(BlockEntityItemFlow flow, BlockFacing dir) =>
            flow.PullFaces.Length == 0 && flow.PushFaces.Length == 2 && flow.PushFaces.Contains(dir) && flow.PushFaces.Contains(dir.Opposite);

        /// <summary>Whether a piece has a container (or another chute) at one of the openings it pushes out of.</summary>
        static bool HasOutlet(IBlockAccessor ba, BlockEntityItemFlow flow) =>
            flow.PushFaces.Any(face => ContainerAt(ba, flow.Pos.AddCopy(face)) != null);

        /// <summary>
        /// What the chute, hopper or screw at <paramref name="dir"/> from the machine at <paramref name="machine"/> does.
        /// For <see cref="ChuteLink.HandedTo"/>, <paramref name="first"/> is the piece to push into and
        /// <paramref name="end"/> what the run leads into (the piece itself, the piece that drops things down, or the
        /// container at its end, whose room is the caller's to check).
        /// </summary>
        public static ChuteLink Link(IBlockAccessor ba, BlockPos machine, BlockFacing dir, out BlockEntityItemFlow first, out BlockEntityContainer end)
        {
            end = null;
            first = ContainerAt(ba, machine.AddCopy(dir)) as BlockEntityItemFlow;
            if (first?.PullFaces == null || first.PushFaces == null || first.AcceptFromFaces == null) { first = null; return ChuteLink.None; }
            BlockFacing back = dir.Opposite;
            if (first.PullFaces.Contains(back)) return ChuteLink.Pulls;
            bool pushesBack = first.PushFaces.Contains(back);
            // Nothing is handed upwards, or into an opening that doesn't take things.
            if (dir == BlockFacing.UP || !first.AcceptFromFaces.Contains(back)) return pushesBack ? ChuteLink.BringsIn : ChuteLink.Closed;
            if (!pushesBack)
            {
                // It can't push back into the machine, so whatever it's given moves on.
                end = first;
                return HasOutlet(ba, first) ? ChuteLink.HandedTo : ChuteLink.OpenEnd;
            }
            // It carries things both ways. Only a straight run to a container, or to a piece that drops things, is
            // certain to lead away: a T, a 3-way or a cross on the ground may be bringing things in from elsewhere.
            if (!IsStraight(first, dir)) return ChuteLink.BringsIn;
            int straight = 1;
            BlockPos at = first.Pos.Copy();
            while (true)
            {
                at.Add(dir);
                BlockEntityContainer be = ContainerAt(ba, at);
                if (be is BlockEntityItemFlow next)
                {
                    if (next.PullFaces == null || next.PushFaces == null || next.AcceptFromFaces == null) return ChuteLink.NoEnd;
                    bool nextPushesBack = next.PushFaces.Contains(back);
                    if (!next.PullFaces.Contains(back) && !next.AcceptFromFaces.Contains(back)) return nextPushesBack ? ChuteLink.BringsIn : ChuteLink.NoEnd;
                    if (!nextPushesBack)
                    {
                        end = next;
                        return HasOutlet(ba, next) ? ChuteLink.HandedTo : ChuteLink.OpenEnd;
                    }
                    if (!IsStraight(next, dir)) return ChuteLink.BringsIn;
                    if (++straight > MaxStraightChutes) return ChuteLink.TooLong;
                    continue;
                }
                if (be is BEEnergyCondenser) return ChuteLink.OtherCondenser;
                if (be == null) return ChuteLink.NoEnd;
                end = be;
                return ChuteLink.HandedTo;
            }
        }
    }
}
