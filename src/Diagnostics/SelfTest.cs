using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Runs when the hidden <c>SelfTestOnStart</c> setting is on (the "Test on a real server" GitHub workflow turns it
    /// on): next to the world spawn it builds matter furnaces (one on charcoal, one on EMC) with raw meat in them,
    /// fires lens, water and fire projectiles at the ground, checks a few EMC values, and writes what happened to
    /// <c>ModData/vintagee/selftest.txt</c>. Never runs on normal servers.
    /// </summary>
    public partial class SelfTest
    {
        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly StringBuilder log = new StringBuilder();
        int failures;

        public SelfTest(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
        }

        void Line(string text)
        {
            log.AppendLine(text);
            sapi.Logger.Notification("[Vintage-E self-test] {0}", text); // the text may hold braces: never a format string
        }

        void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Line((ok ? "PASS " : "FAIL ") + what);
        }

        public void Start()
        {
            Line("Vintage-E " + mod.Version + " self-test");
            EntityPos spawn = sapi.World.DefaultSpawnPosition;
            int x = (int)(spawn?.X ?? sapi.World.BlockAccessor.MapSizeX / 2), z = (int)(spawn?.Z ?? sapi.World.BlockAccessor.MapSizeZ / 2);
            int cs = GlobalConstants.ChunkSize;
            sapi.WorldManager.LoadChunkColumnPriority(x / cs - 1, z / cs - 1, x / cs + 1, z / cs + 1, new ChunkLoadOptions
            {
                KeepLoaded = true,
                OnLoaded = () => sapi.Event.EnqueueMainThreadTask(() => Run(x, z), "vintagee-selftest")
            });
        }

        void Run(int x, int z)
        {
            try
            {
                CheckValues();
                CheckWorn();
                CheckGuesses();
                CheckContents();
                CheckConfigure();
                CheckProjectELook();
                Check180();
                Check190();
                Check1100();
                Check1101();
                Check1110();
                Check1211();
                CheckSpeedups();
                CheckDiagnose();
                CheckMatterTools();
                CheckCraftingRoutes();
                CheckMethods();
                IBlockAccessor ba = sapi.World.BlockAccessor;
                int y = ba.GetTerrainMapheightAt(new BlockPos(x, 0, z)) + 1;
                Line($"site {x},{y},{z}");
                benchSite = new BlockPos(x - 20, y, z - 20);

                BlockEntityFirepit fuelled = PlaceFurnace(new BlockPos(x + 2, y, z), "dark");
                BlockEntityFirepit onEmc = PlaceFurnace(new BlockPos(x + 4, y, z), "red");
                Item charcoal = sapi.World.GetItem(new AssetLocation("game:charcoal"));
                Item meat = sapi.World.GetItem(new AssetLocation("game:redmeat-raw"));
                Check(charcoal != null && meat != null, "found charcoal and raw red meat");
                if (fuelled != null && charcoal != null && meat != null)
                {
                    fuelled.fuelSlot.Itemstack = new ItemStack(charcoal, 4);
                    fuelled.inputSlot.Itemstack = new ItemStack(meat, 1);
                    fuelled.canIgniteFuel = true;
                    fuelled.MarkDirty(true);
                }
                if (onEmc is BEMatterFurnace emcFurnace && meat != null)
                {
                    emcFurnace.inputSlot.Itemstack = new ItemStack(meat, 1);
                    double took = emcFurnace.AcceptEmc(500);
                    Check(took == 500, "red furnace accepts 500 EMC");
                    emcFurnace.MarkDirty(true);
                }

                // 1.22.0: the furnace window's mode button. Heating mode keeps a furnace at its top temperature with
                // nothing to heat (on EMC, or relighting cold fuel); normal mode leaves an empty furnace alone.
                var heatEmc = PlaceFurnace(new BlockPos(x + 2, y, z + 3), "dark") as BEMatterFurnace;
                var idleEmc = PlaceFurnace(new BlockPos(x + 4, y, z + 3), "dark") as BEMatterFurnace;
                var heatFuel = PlaceFurnace(new BlockPos(x + 6, y, z + 3), "dark") as BEMatterFurnace;
                if (heatEmc != null && idleEmc != null)
                {
                    heatEmc.AcceptEmc(500);
                    idleEmc.AcceptEmc(500);
                    heatEmc.SetHeatingMode(true);
                    var saved = new TreeAttribute();
                    heatEmc.ToTreeAttributes(saved);
                    Check(saved.GetBool("vintageeHeating") && !idleEmc.HeatingMode, "a furnace's mode is saved with it (heating " + saved.GetBool("vintageeHeating") + ")");
                    var reloaded = new TreeAttribute();
                    idleEmc.ToTreeAttributes(reloaded);
                    Check(!reloaded.GetBool("vintageeHeating"), "a furnace is in normal mode until switched");
                }
                if (heatFuel != null && charcoal != null)
                {
                    heatFuel.fuelSlot.Itemstack = new ItemStack(charcoal, 4);
                    heatFuel.canIgniteFuel = false; // cold: the firepit would wait for a firestarter
                    heatFuel.SetHeatingMode(true);
                    heatFuel.MarkDirty(true);
                }
                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        Check(heatEmc != null && heatEmc.IsBurning && heatEmc.furnaceTemperature > 300,
                            $"a furnace in heating mode with nothing to heat keeps itself hot on EMC ({heatEmc?.furnaceTemperature:0} °C, burning {heatEmc?.IsBurning})");
                        Check(idleEmc != null && !idleEmc.IsBurning && idleEmc.furnaceTemperature < 100,
                            $"a furnace in normal mode with nothing to heat stays cold ({idleEmc?.furnaceTemperature:0} °C, burning {idleEmc?.IsBurning})");
                        Check(heatFuel != null && heatFuel.IsBurning && heatFuel.furnaceTemperature > 300,
                            $"a cold furnace with fuel lights itself in heating mode ({heatFuel?.furnaceTemperature:0} °C, burning {heatFuel?.IsBurning}, fuel left {heatFuel?.fuelSlot?.StackSize})");
                    }
                    catch (Exception e) { Check(false, "heating mode check threw " + e); }
                }, 30000);

                // A collector feeding a condenser next to it (1.9.0: the condenser shows and keeps what it's given).
                BEEnergyCollector collector = Place<BEEnergyCollector>(new BlockPos(x + 6, y, z), "vintagee:energycollector-mk1");
                BEEnergyCondenser condenser = Place<BEEnergyCondenser>(new BlockPos(x + 7, y, z), "vintagee:energycondenser-mk1");
                if (collector != null) collector.Stored = 500;

                // 1.21.1: a Klein star in a relay comes out when the relay is blown up. The game's explosions call the
                // block's OnBlockExploded, which tells the block entity and never the block's own OnBlockBroken.
                BEAntimatterRelay starRelay = Place<BEAntimatterRelay>(new BlockPos(x + 12, y, z), "vintagee:antimatterrelay-mk1");
                Item kleinStar = sapi.World.GetItem(new AssetLocation("vintagee:kleinstar-ein"));
                Check(kleinStar != null, "vintagee:kleinstar-ein exists");
                if (starRelay != null && kleinStar != null)
                {
                    starRelay.Star.Star = new ItemStack(kleinStar);
                    starRelay.Block.OnBlockExploded(sapi.World, starRelay.Pos, starRelay.Pos, EnumBlastType.RockBlast, null);
                    sapi.World.BulkBlockAccessor.Commit();
                    Check(starRelay.Star.Star == null && ba.GetBlock(new BlockPos(x + 12, y, z)).Id == 0, "a Klein star in a relay comes out when the relay is blown up");
                }

                // 1.11.0: a hopper on top of a transmutation table feeds it (the owner isn't online, so it waits in the table).
                BETransmutationTable table = Place<BETransmutationTable>(new BlockPos(x + 9, y, z), "vintagee:transmutationtable");
                Block hopperBlock = sapi.World.Blocks.FirstOrDefault(b => b?.Code != null && b.Code.Domain == "game" && b.Code.Path.StartsWith("hopper"));
                BlockEntityContainer hopper = hopperBlock == null ? null : Place<BlockEntityContainer>(new BlockPos(x + 9, y + 1, z), hopperBlock.Code.ToString());
                Item stickItem = sapi.World.GetItem(new AssetLocation("game:stick"));
                if (table != null) table.SetOwner("selftest-nobody", "Nobody");
                if (hopper != null && stickItem != null)
                {
                    hopper.Inventory[0].Itemstack = new ItemStack(stickItem, 6);
                    hopper.Inventory[0].MarkDirty();
                }

                // 1.18.10: chutes and hoppers see the alchemical chest, the condenser and the matter furnace (before, only
                // the table: the others weren't the game's BlockEntityContainer, which is all a chute looks for).
                var chuteChecks = new List<(string label, Func<bool> ok, Func<string> state)>();
                Item meatItem = sapi.World.GetItem(new AssetLocation("game:redmeat-raw"));
                if (hopperBlock != null && stickItem != null)
                {
                    int cz = z - 4;
                    BlockEntityContainer Hopper(BlockPos at, ItemStack with)
                    {
                        var h = Place<BlockEntityContainer>(at, hopperBlock.Code.ToString());
                        if (h != null && with != null) { h.Inventory[0].Itemstack = with; h.Inventory[0].MarkDirty(); }
                        return h;
                    }
                    // Taking out needs a chute: the game's hopper only catches dropped items and never pulls from above.
                    Block chuteBlock = sapi.World.Blocks.FirstOrDefault(b => b?.Code != null && b.Code.Domain == "game" && b.Code.Path.StartsWith("chute")
                        && (b.Attributes?["pullFaces"].AsArray<string>(null)?.Contains("up") ?? false) && (b.Attributes?["pushFaces"].AsArray<string>(null)?.Contains("down") ?? false));
                    Check(chuteBlock != null, $"a straight up-down chute exists ({chuteBlock?.Code})");
                    BlockEntityContainer Chute(BlockPos at) => chuteBlock == null ? null : Place<BlockEntityContainer>(at, chuteBlock.Code.ToString());
                    int Count(IInventory inv, Item item) => inv == null ? -1 : inv.Where(sl => sl?.Itemstack?.Collectible == item).Sum(sl => sl.StackSize);

                    // A hopper on a chest fills it; a hopper under a chest empties it.
                    var chestIn = Place<BEAlchemicalChest>(new BlockPos(x, y, cz), "vintagee:alchemicalchest");
                    var hopIn = Hopper(new BlockPos(x, y + 1, cz), new ItemStack(stickItem, 4));
                    chuteChecks.Add(("a hopper on an alchemical chest fills it", () => Count(chestIn?.Inventory, stickItem) > 0, () => $"{Count(chestIn?.Inventory, stickItem)} sticks in the chest"));
                    var chestOut = Place<BEAlchemicalChest>(new BlockPos(x + 2, y + 1, cz), "vintagee:alchemicalchest");
                    var hopOut = Chute(new BlockPos(x + 2, y, cz));
                    if (chestOut != null) { chestOut.Inventory[5].Itemstack = new ItemStack(stickItem, 4); chestOut.Inventory[5].MarkDirty(); }
                    chuteChecks.Add(("a chute under an alchemical chest takes from it", () => Count(chestOut?.Inventory, stickItem) < 4, () => $"{Count(hopOut?.Inventory, stickItem)} sticks in the chute, {Count(chestOut?.Inventory, stickItem)} left in the chest"));

                    // A hopper on a condenser fills its storage, never the target; one under it takes copies of the target.
                    var condIn = Place<BEEnergyCondenser>(new BlockPos(x + 4, y, cz), "vintagee:energycondenser-mk1");
                    Hopper(new BlockPos(x + 4, y + 1, cz), new ItemStack(stickItem, 4));
                    // No target, so what it's given is burned into EMC: either the sticks or their EMC shows they went in.
                    chuteChecks.Add(("a hopper on a condenser fills its storage, not the target", () => condIn != null && condIn.Inventory[0].Empty && (Count(condIn.Inventory, stickItem) > 0 || condIn.Stored > 0), () => $"{Count(condIn?.Inventory, stickItem)} sticks in the condenser, {condIn?.Stored} EMC, target {condIn?.Inventory[0].Itemstack?.Collectible.Code}"));
                    var condOut = Place<BEEnergyCondenser>(new BlockPos(x + 6, y + 1, cz), "vintagee:energycondenser-mk1");
                    var hopCond = Chute(new BlockPos(x + 6, y, cz));
                    if (condOut != null)
                    {
                        condOut.Inventory[0].Itemstack = new ItemStack(stickItem, 1); condOut.Inventory[0].MarkDirty();
                        condOut.Inventory[3].Itemstack = new ItemStack(stickItem, 4); condOut.Inventory[3].MarkDirty();
                    }
                    chuteChecks.Add(("a chute under a condenser takes copies of its target, never the target", () => Count(condOut?.Inventory, stickItem) < 5 && condOut?.Inventory[0].Itemstack?.Collectible == stickItem, () => $"{Count(hopCond?.Inventory, stickItem)} sticks in the chute, {Count(condOut?.Inventory, stickItem)} left in the condenser, target still {condOut?.Inventory[0].Itemstack?.Collectible.Code}"));

                    // Matter furnace: from above into the input, from below out of the output.
                    BlockEntityFirepit furnIn = PlaceFurnace(new BlockPos(x + 8, y, cz), "dark");
                    if (meatItem != null) Hopper(new BlockPos(x + 8, y + 1, cz), new ItemStack(meatItem, 2));
                    chuteChecks.Add(("a hopper on a matter furnace puts food in its input", () => meatItem == null || furnIn?.inputSlot?.Itemstack?.Collectible == meatItem || furnIn?.outputSlot?.Itemstack != null, () => $"input {furnIn?.inputSlot?.Itemstack?.Collectible.Code} x{furnIn?.inputSlot?.StackSize}, chute inventory {(furnIn as BEMatterFurnace)?.ChuteInventory}"));
                    BlockEntityFirepit furnOut = PlaceFurnace(new BlockPos(x + 10, y + 1, cz), "dark");
                    var hopFurn = Chute(new BlockPos(x + 10, y, cz));
                    if (furnOut != null) { furnOut.outputSlot.Itemstack = new ItemStack(stickItem, 3); furnOut.outputSlot.MarkDirty(); }
                    chuteChecks.Add(("a chute under a matter furnace takes its output", () => (furnOut?.outputSlot?.StackSize ?? 3) < 3, () => $"{Count(hopFurn?.Inventory, stickItem)} sticks in the chute, output {furnOut?.outputSlot?.StackSize}"));

                    // 1.22.1: "the items don't even make it into the chute". The game's chutes only take from a container
                    // through an opening that pulls; a straight chute lying flat and a hopper have none, so on a
                    // condenser's side or under it they took nothing. The condenser now hands its copies to chutes that
                    // can only lead away (ChuteOutput), and to nothing that might be bringing things in.
                    string[] Faces(Block b, string key) => b?.Attributes?[key].AsArray<string>(null) ?? new string[0];
                    bool Same(string[] have, params string[] want) => have.OrderBy(v => v).SequenceEqual(want.OrderBy(v => v));
                    Block Piece(string[] pull, string[] push, string[] accept) => sapi.World.Blocks.FirstOrDefault(b => b?.Code != null && b.Code.Domain == "game" && b.Code.Path.StartsWith("chute")
                        && Same(Faces(b, "pullFaces"), pull) && Same(Faces(b, "pushFaces"), push) && Same(Faces(b, "acceptFromFaces"), accept));
                    string[] none = new string[0], we = { "west", "east" };
                    Block flat = Piece(none, we, we);                                   // a straight chute lying west-east
                    Block dropFromWest = Piece(new[] { "west" }, new[] { "down" }, none); // an elbow: in from the west, out underneath
                    Block bringsEast = Piece(new[] { "up" }, new[] { "east" }, none);     // an elbow: in from above, out to the east
                    Block tee = Piece(new[] { "up" }, we, we);                            // a T: in from above, out west and east
                    Check(flat != null && dropFromWest != null && bringsEast != null && tee != null,
                        $"the game's chute pieces were found (flat {flat?.Code}, drop {dropFromWest?.Code}, feeder {bringsEast?.Code}, T {tee?.Code})");
                    // The game's rule this is all about: a flat straight chute has no opening that pulls.
                    Check(flat != null && Faces(flat, "pullFaces").Length == 0 && Faces(hopperBlock, "pullFaces").Length == 0,
                        "in the game, a straight chute lying flat and a hopper never pull from a container (which is why they took nothing)");
                    BlockEntityContainer Put(BlockPos at, Block b) => b == null ? null : Place<BlockEntityContainer>(at, b.Code.ToString());
                    BEEnergyCondenser Cond(BlockPos at, int copies, double emc = 0)
                    {
                        var c = Place<BEEnergyCondenser>(at, "vintagee:energycondenser-mk1");
                        if (c == null) return null;
                        c.Inventory[0].Itemstack = new ItemStack(stickItem, 1); c.Inventory[0].MarkDirty();
                        if (copies > 0) { c.Inventory[3].Itemstack = new ItemStack(stickItem, copies); c.Inventory[3].MarkDirty(); }
                        c.Stored = emc;
                        return c;
                    }
                    BEAlchemicalChest Chest(BlockPos at) => Place<BEAlchemicalChest>(at, "vintagee:alchemicalchest");
                    ChuteLink LinkOf(BEEnergyCondenser c, BlockFacing dir) => c == null ? ChuteLink.None : ChuteOutput.Link(ba, c.Pos, dir, out _, out _);
                    void Arrives(string label, BEEnergyCondenser from, BEAlchemicalChest chest) =>
                        chuteChecks.Add((label, () => Count(chest?.Inventory, stickItem) > 0, () => $"{Count(chest?.Inventory, stickItem)} sticks in the chest, {Count(from?.Inventory, stickItem)} left in the condenser"));
                    void Keeps(string label, BEEnergyCondenser c, int sticks, params BlockEntityContainer[] chutes) =>
                        chuteChecks.Add((label, () => Count(c?.Inventory, stickItem) >= sticks && chutes.All(ch => Count(ch?.Inventory, stickItem) <= 0),
                            () => $"{Count(c?.Inventory, stickItem)} sticks in the condenser (it had {sticks}), {chutes.Sum(ch => Math.Max(0, Count(ch?.Inventory, stickItem)))} in the chutes"));

                    int r1 = z - 8, r2 = z - 10, r3 = z - 12;
                    // Condenser, chute underneath, chest under that: the condenser makes the copies itself here.
                    var c1 = Cond(new BlockPos(x, y + 2, r1), 0, 5000); var ch1 = Chest(new BlockPos(x, y, r1)); Chute(new BlockPos(x, y + 1, r1));
                    Check(LinkOf(c1, BlockFacing.DOWN) == ChuteLink.Pulls, "a chute under a condenser pulls for itself: " + LinkOf(c1, BlockFacing.DOWN));
                    Arrives("condenser, chute underneath, alchemical chest below: the copies it makes reach the chest", c1, ch1);
                    // Condenser, hopper underneath, chest under that.
                    var c2 = Cond(new BlockPos(x + 2, y + 2, r1), 6); var ch2 = Chest(new BlockPos(x + 2, y, r1)); Hopper(new BlockPos(x + 2, y + 1, r1), null);
                    Check(LinkOf(c2, BlockFacing.DOWN) == ChuteLink.HandedTo, "a hopper under a condenser with a chest below is handed the copies: " + LinkOf(c2, BlockFacing.DOWN));
                    Arrives("condenser, hopper underneath, alchemical chest below: the copies reach the chest", c2, ch2);
                    // Condenser and chest side by side with one straight chute between them (the layout that did nothing).
                    var c3 = Cond(new BlockPos(x + 4, y + 1, r1), 6); var ch3 = Chest(new BlockPos(x + 6, y + 1, r1)); Put(new BlockPos(x + 5, y + 1, r1), flat);
                    Check(LinkOf(c3, BlockFacing.EAST) == ChuteLink.HandedTo, "a straight chute from a condenser's side into a chest is handed the copies: " + LinkOf(c3, BlockFacing.EAST));
                    Arrives("condenser, straight chute on its side, alchemical chest: the copies reach the chest", c3, ch3);
                    // Two straight chutes.
                    var c4 = Cond(new BlockPos(x + 8, y + 1, r1), 6); var ch4 = Chest(new BlockPos(x + 11, y + 1, r1));
                    Put(new BlockPos(x + 10, y + 1, r1), flat); Put(new BlockPos(x + 9, y + 1, r1), flat);
                    Check(LinkOf(c4, BlockFacing.EAST) == ChuteLink.HandedTo, "two straight chutes into a chest are handed the copies: " + LinkOf(c4, BlockFacing.EAST));
                    Arrives("condenser, two straight chutes, alchemical chest: the copies reach the chest", c4, ch4);
                    // A straight chute, then an elbow that drops into a chest one lower.
                    var c5 = Cond(new BlockPos(x, y + 1, r2), 6); var ch5 = Chest(new BlockPos(x + 2, y, r2));
                    Put(new BlockPos(x + 1, y + 1, r2), flat); Put(new BlockPos(x + 2, y + 1, r2), dropFromWest);
                    Check(LinkOf(c5, BlockFacing.EAST) == ChuteLink.HandedTo, "a straight chute into an elbow that drops into a chest is handed the copies: " + LinkOf(c5, BlockFacing.EAST));
                    Arrives("condenser, straight chute, elbow down, alchemical chest: the copies reach the chest", c5, ch5);

                    // What must not change. A line that brings things in (hopper, elbow, straight chute, condenser):
                    // the condenser gives it nothing, and what the hopper holds still gets in.
                    var c6 = Cond(new BlockPos(x + 6, y + 1, r2), 6); var feed = Put(new BlockPos(x + 5, y + 1, r2), flat);
                    var elbow6 = Put(new BlockPos(x + 4, y + 1, r2), bringsEast);
                    var hop6 = charcoal == null ? null : Hopper(new BlockPos(x + 4, y + 2, r2), new ItemStack(charcoal, 3));
                    Check(LinkOf(c6, BlockFacing.WEST) == ChuteLink.BringsIn, "a straight chute fed by an elbow brings things in, so it's handed nothing: " + LinkOf(c6, BlockFacing.WEST));
                    Keeps("a condenser gives no copies to a chute line that feeds it", c6, 7, feed, elbow6, hop6);
                    // The charcoal goes in and is burned; its EMC becomes more sticks (the target), so those show it arrived.
                    chuteChecks.Add(("and that line still feeds it", () => c6 != null && Count(hop6?.Inventory, charcoal) == 0 && (c6.Stored > 0 || Count(c6.Inventory, charcoal) > 0 || Count(c6.Inventory, stickItem) > 7),
                        () => $"{Count(hop6?.Inventory, charcoal)} charcoal left in the hopper, {Count(c6?.Inventory, charcoal)} in the condenser, {c6?.Stored} EMC, {Count(c6?.Inventory, stickItem)} sticks (it had 7)"));
                    // A straight chute that leads nowhere: nothing is handed over, so nothing spills.
                    var c7 = Cond(new BlockPos(x + 8, y + 1, r2), 6); var stub = Put(new BlockPos(x + 9, y + 1, r2), flat);
                    ba.SetBlock(0, new BlockPos(x + 10, y + 1, r2));
                    Check(LinkOf(c7, BlockFacing.EAST) == ChuteLink.NoEnd, "a straight chute with an open end leads nowhere, so it's handed nothing: " + LinkOf(c7, BlockFacing.EAST));
                    Keeps("a condenser spills nothing through a chute with an open end", c7, 7, stub);
                    // Two condensers joined by a straight chute, and by a T: neither gives the other anything.
                    var c8 = Cond(new BlockPos(x, y + 1, r3), 6); var c9 = Cond(new BlockPos(x + 2, y + 1, r3), 6); var between = Put(new BlockPos(x + 1, y + 1, r3), flat);
                    Check(LinkOf(c8, BlockFacing.EAST) == ChuteLink.OtherCondenser && LinkOf(c9, BlockFacing.WEST) == ChuteLink.OtherCondenser,
                        $"two condensers joined by a straight chute hand each other nothing: {LinkOf(c8, BlockFacing.EAST)}, {LinkOf(c9, BlockFacing.WEST)}");
                    Keeps("two condensers joined by a straight chute keep their copies (first)", c8, 7, between);
                    Keeps("two condensers joined by a straight chute keep their copies (second)", c9, 7, between);
                    var c10 = Cond(new BlockPos(x + 4, y + 1, r3), 6); var c11 = Cond(new BlockPos(x + 6, y + 1, r3), 6); var teeBe = Put(new BlockPos(x + 5, y + 1, r3), tee);
                    Check(LinkOf(c10, BlockFacing.EAST) == ChuteLink.BringsIn && LinkOf(c11, BlockFacing.WEST) == ChuteLink.BringsIn,
                        $"a T between two condensers may be feeding them both, so it's handed nothing: {LinkOf(c10, BlockFacing.EAST)}, {LinkOf(c11, BlockFacing.WEST)}");
                    Keeps("two condensers joined by a T keep their copies (first)", c10, 7, teeBe);
                    Keeps("two condensers joined by a T keep their copies (second)", c11, 7, teeBe);
                    // A hopper with nothing under it would spill: not handed anything.
                    var c12 = Cond(new BlockPos(x + 8, y + 3, r3), 6); var hop12 = Hopper(new BlockPos(x + 8, y + 2, r3), null);
                    ba.SetBlock(0, new BlockPos(x + 8, y + 1, r3));
                    Check(LinkOf(c12, BlockFacing.DOWN) == ChuteLink.OpenEnd, "a hopper under a condenser with nothing below it would spill, so it's handed nothing: " + LinkOf(c12, BlockFacing.DOWN));
                    Keeps("a condenser spills nothing through a hopper with nothing below it", c12, 7, hop12);
                    // The doctor names what each chute touching a condenser does: every answer and side has its text.
                    string[] untold = Enum.GetValues(typeof(ChuteLink)).Cast<ChuteLink>().Where(l => l != ChuteLink.None).Select(l => "diag-mach-chute-" + l.ToString().ToLowerInvariant())
                        .Concat(BlockFacing.ALLFACES.Select(f => "diag-mach-side-" + f.Code)).Where(k => !Lang.HasTranslation("vintagee:" + k, false, false)).ToArray(); // not Get: formatting without its values logs an error
                    Check(untold.Length == 0, "the doctor has words for everything a chute next to a condenser can be doing" + (untold.Length == 0 ? "" : ": missing " + string.Join(", ", untold)));
                }

                // Projectiles fired straight down at the ground a little way off. The lens aims at its own column's
                // ground height (hilly sites made it land outside the area that was counted).
                int lensY = ba.GetTerrainMapheightAt(new BlockPos(x, 0, z + 12)) + 1;
                // Clear the way down (trees, bushes), so the charge reaches the ground it's counted at.
                for (int dy = 0; dy < 16; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            ba.SetBlock(0, new BlockPos(x + dx, lensY + dy, z + 12 + dz));
                var lens = Fire("lens", new Vec3d(x + 0.5, lensY + 12, z + 12.5), p => p.Radius = 4);
                // Well away from the furnaces: the water flows, and firepits go out in water.
                var water = Fire("water", new Vec3d(x + 30.5, y + 8, z + 0.5), null);
                var fire = Fire("fire", new Vec3d(x + 36.5, y + 8, z + 0.5), null);
                // 1.9.2: a fireball sets what burns alight. A plank block in the open, with a fireball dropped on it.
                int woodY = ba.GetTerrainMapheightAt(new BlockPos(x + 42, 0, z)) + 1;
                BlockPos woodPos = new BlockPos(x + 42, woodY, z);
                Block planks = sapi.World.GetBlock(new AssetLocation("game:planks-oak-ud"));
                for (int dy = 0; dy < 10; dy++) ba.SetBlock(0, woodPos.UpCopy(dy));
                if (planks != null) ba.SetBlock(planks.BlockId, woodPos);
                var fireOnWood = Fire("fire", new Vec3d(x + 42.5, woodY + 8, z + 0.5), null);
                BlockPos lensTarget = new BlockPos(x, lensY - 1, z + 12);
                int solidBefore = CountSolid(lensTarget, 2);
                bool lensInWater = ba.GetBlock(lensTarget.UpCopy(), BlockLayersAccess.Fluid)?.IsLiquid() == true;

                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        Check(lens == null || !lens.Alive, "lens charge hit the ground and is gone");
                        int solidAfter = CountSolid(lensTarget, 2);
                        if (lensInWater) Line($"  lens landed in water (fizzles there, like ProjectE's): {solidBefore} -> {solidAfter} solid");
                        else Check(solidAfter < solidBefore, $"lens explosion broke blocks ({solidBefore} -> {solidAfter} solid around the impact)");
                        Check(water == null || !water.Alive, "water shot landed");
                        Block placed = sapi.World.BlockAccessor.GetBlock(new BlockPos(x + 30, ba.GetTerrainMapheightAt(new BlockPos(x + 30, 0, z)) + 1, z), BlockLayersAccess.Fluid);
                        Line("  fluid where the water shot landed: " + (placed?.Code?.ToString() ?? "none"));
                        Check(fire == null || !fire.Alive, "fireball landed");
                        Block onTop = sapi.World.BlockAccessor.GetBlock(woodPos.UpCopy());
                        // Rain and snow put the game's fire out within seconds (each run is a new world with its own
                        // weather), so with precipitation there a missing fire says nothing about the fireball.
                        float falling = 0;
                        try { falling = sapi.World.BlockAccessor.GetClimateAt(woodPos, EnumGetClimateMode.NowValues)?.Rainfall ?? 0; } catch { }
                        if (onTop?.Code?.Path != "fire" && falling > 0.04f) Line($"  fireball on planks: no fire after 4 s, but it is raining or snowing there ({falling:0.##}), which puts fire out: not judged");
                        else Check(onTop?.Code?.Path == "fire", $"fireball set the planks alight ({onTop?.Code?.ToString() ?? "nothing"} on top, precipitation {falling:0.##})");
                    }
                    catch (Exception e) { Check(false, "projectile check threw " + e); }
                }, 4000);

                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        // The rate is only what came in over the last few seconds (none at night), so it's shown, not checked.
                        Check(condenser != null && collector != null && condenser.Stored >= 400,
                            $"condenser took the collector's EMC: {EmcFormat.Format(condenser?.Stored ?? 0)} stored, {EmcFormat.Format(condenser?.IncomingPerSecond ?? 0)} a second coming in");
                        ItemStack fed = table?.Inventory[0].Itemstack;
                        Check(table != null && hopper != null && fed?.Collectible == stickItem && fed.StackSize > 0,
                            $"a hopper on the table fed it sticks to burn ({fed?.StackSize ?? 0} waiting for its owner to come online, {hopper?.Inventory[0].StackSize ?? -1} left in the hopper)");
                        foreach (var (label, ok, state) in chuteChecks) Check(ok(), $"{label} ({state()})");
                        Report("dark furnace on charcoal", fuelled);
                        Report("red furnace on EMC", onEmc);
                        CheckPedestalWeather();
                    }
                    catch (Exception e) { Check(false, "furnace check threw " + e); }
                    Finish();
                }, 40000);
            }
            catch (Exception e)
            {
                Check(false, "self-test threw " + e);
                Finish();
            }
        }

        void CheckValues()
        {
            // Primitive Survival adds fish eggs (0.2 + 0.2, worth 16 each) to what an adult fish is processed into.
            double bass = sapi.ModLoader.IsModEnabled("primitivesurvival") ? 118.4 : 112.0;
            foreach (var (code, expectMin, expectMax) in new[] { ("game:fat", 1.0, 64.0), ("game:fat-rendered", 1.0, 64.0), ("game:gem-emerald-rough", 2048.0, 2048.0), ("game:gem-diamond-rough", 8192.0, 8192.0),
                ("game:creature-fish-freshwater-bass-largemouth-adult", bass - 0.01, bass + 0.01), ("game:jonasparts-tank01", 1024.0, 1024.0),
                ("game:creature-trader-female-artisan-cold", 0.0, 0.0), ("game:locatormap-treasures", 0.0, 0.0),
                ("game:lore-paper", 0.0, 0.0), ("game:metal-parts", 1.0, 1000.0), ("game:nightvisiondevice", 4096.0, 4096.0),
                ("game:ore-fluorite", 32.0, 64.0), ("game:ore-fluorite-limestone", 1.0, 1000.0),
                // Trader price 53.66, but the grid makes 6 from saltpeter, sulfur and coal for 25.8 each: the recipe wins.
                ("game:blastingpowder", 1.0, 30.0),
                // Free-EMC fixes: a ribcage is worth its 4 bones; crystallized ore its nugget; steel follows blister steel.
                ("game:bonyremains-ribcage", 96.0, 96.0), ("game:crystalizedore-poor-quartz_nativegold-andesite", 102.0, 103.0),
                ("game:ingot-steel", 256.0, 400.0), ("game:hide-pelt-bear-brown-head", 1.0, 200.0) })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                double v = c == null ? -1 : mod.Emc.GetValue(c);
                Check(v >= expectMin && v <= expectMax, $"value of {code} = {v} (expected {expectMin}-{expectMax})");
            }
            foreach (string code in new[] { "game:altar-north", "vintagee:furnace-dark-cold", "vintagee:trinket-voidring" })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code)) ?? sapi.World.GetItem(new AssetLocation(code));
                Line($"  value of {code} = {(c == null ? "missing" : mod.Emc.GetValue(c).ToString())}");
            }
        }

        /// <summary>
        /// The worn Klein star and rings: they fit their clothing slots, their models load and hang on the player model.
        /// Also prints where they end up next to the game's own emblem and bracelet, to compare positions.
        /// </summary>
        void CheckWorn()
        {
            Shape seraph = Shape.TryGet(sapi, new AssetLocation("game:shapes/entity/humanoid/seraph.json"));
            Check(seraph != null, "player model loads");
            if (seraph == null) return;
            seraph.ResolveReferences(sapi.Logger, "seraph");
            foreach (string name in new[] { "UpperTorso", "Head", "LowerArmL", "LowerArmR" })
            {
                ShapeElement e = seraph.GetElementByName(name);
                Line($"  {name}: " + (e == null ? "missing" : $"{Describe(e)} (own box {string.Join(",", e.From)} to {string.Join(",", e.To)}, rotation {e.RotationX},{e.RotationY},{e.RotationZ})"));
            }
            // Where the hands are (rings are worn in the hand slot): any element named like a hand or finger.
            void Hands(ShapeElement e)
            {
                string n = e.Name ?? "";
                if (n.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("finger", StringComparison.OrdinalIgnoreCase) >= 0)
                    Line($"  {n} (in {e.ParentElement?.Name ?? "-"}): {Describe(e)} (own box {string.Join(",", e.From)} to {string.Join(",", e.To)})");
                if (e.Children != null) foreach (var c in e.Children) Hands(c);
            }
            foreach (ShapeElement e in seraph.Elements ?? Array.Empty<ShapeElement>()) Hands(e);
            foreach (var (code, dress, path, root) in new[] {
                ("vintagee:kleinstar-omega", EnumCharacterDressType.Emblem, (string)null, "vintageekleinstar"),
                ("vintagee:trinket-voidring", EnumCharacterDressType.Hand, null, "vintageering"),
                (null, EnumCharacterDressType.Emblem, "game:entity/humanoid/seraph/clothing/emblem/silver-star", "origin"),
                (null, EnumCharacterDressType.Arm, "game:entity/humanoid/seraph/clothing/arm/copper-bracelet", "hidelowerarm") })
            {
                string shapeBase = path;
                if (code != null)
                {
                    Item item = sapi.World.GetItem(new AssetLocation(code));
                    Check(item != null, code + " exists");
                    if (item == null) continue;
                    Check(ItemSlotCharacter.IsDressType(new ItemStack(item), dress), $"{code} fits the {dress} slot");
                    // The real slot also checks the item's storage flags (1.9.2: rings and stars lacked Outfit, so the slot refused them).
                    var charSlot = new ItemSlotCharacter(dress, new DummyInventory(sapi, 1));
                    Check(charSlot.CanHold(new DummySlot(new ItemStack(item))), $"{code} can be put in a {dress} slot");
                    shapeBase = item.Attributes?["attachableToEntity"]["attachedShape"]["base"].AsString();
                }
                Shape worn = shapeBase == null ? null : Shape.TryGet(sapi, new AssetLocation(shapeBase).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                if (code != null) Check(worn != null, $"{code} worn model {shapeBase} loads");
                if (worn == null) { Line($"  {shapeBase}: not found"); continue; }
                worn.ResolveReferences(sapi.Logger, shapeBase);
                Shape target = seraph.Clone();
                target.ResolveReferences(sapi.Logger, "seraph");
                // Step-parenting prefixes the worn model's element and texture names.
                bool added = target.StepParentShape(worn, "vintageetest-", shapeBase, "seraph", sapi.Logger, (a, b) => { });
                ShapeElement r = target.GetElementByName("vintageetest-" + root);
                if (code != null) Check(added && r?.ParentElement != null, $"{code} worn model hangs on {r?.ParentElement?.Name ?? "nothing"}");
                else Line($"  {shapeBase} hangs on {r?.ParentElement?.Name ?? "nothing"}");
                var all = new List<ShapeElement>();
                void Collect(ShapeElement e) { all.Add(e); if (e.Children != null) foreach (var c in e.Children) Collect(c); }
                if (r != null) Collect(r);
                foreach (ShapeElement e in all) if (e != r) Line($"    {e.Name}: {Describe(e)}");
            }
        }

        string OriginOf(string code)
        {
            CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code)) ?? sapi.World.GetItem(new AssetLocation(code));
            if (c is Block b && b.Id == 0) c = sapi.World.GetItem(new AssetLocation(code));
            if (c == null) return "missing";
            return mod.LastReport?.Origins != null && mod.LastReport.Origins.TryGetValue(c, out string o) ? o : (mod.Emc.HasValue(c) ? "?" : "no value");
        }

        double ValueOf(string code)
        {
            CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code));
            if (c == null || c.Id == 0) c = sapi.World.GetItem(new AssetLocation(code));
            return c == null ? -1 : mod.Emc.GetValue(c);
        }

        /// <summary>1.7.0 value methods: placement states, smarter variant guesses, staged solving, fuel/food.</summary>
        void CheckGuesses()
        {
            EmcMappingReport r = mod.LastReport;
            if (r == null) { Check(false, "values are mapped"); return; }
            Line($"  guesses: {r.SiblingCount} look/placement variants, {r.OtherModCount} other mods' variants, {r.FuelCount} fuel, {r.FoodCount} food, {r.GuessCount} other variants, {r.DefaultCount} defaults; {r.Missing.Count} missing");
            string cinnabar = OriginOf("game:ore-cinnabar-andesite");
            Line("  cinnabar ore: " + ValueOf("game:ore-cinnabar-andesite") + " (" + cinnabar + ")");
            Check(!cinnabar.Contains("quartz"), "cinnabar ore isn't priced like quartz");
            string loose = OriginOf("game:looseores-cinnabar-andesite-free");
            Line("  loose cinnabar: " + ValueOf("game:looseores-cinnabar-andesite-free") + " (" + loose + ")");
            Check(!loose.Contains("nativegold"), "loose cinnabar isn't priced like gold-bearing quartz");
            string lens = OriginOf("vintagee:gadget-hyperkineticlens");
            Line("  hyperkinetic lens: " + ValueOf("vintagee:gadget-hyperkineticlens") + " (" + lens + ")");
            Check(lens.StartsWith("grid"), "the lenses are valued from their recipes");
            string fence = OriginOf("game:woodenfence-acacia-e-free");
            Line("  fence piece: " + ValueOf("game:woodenfence-acacia-e-free") + " (" + fence + ")");
            Check(fence.Contains("placement state") && Math.Abs(ValueOf("game:woodenfence-acacia-e-free") - ValueOf("game:woodenfence-acacia-ew-free")) < 1e-6, "fence pieces are worth the fence");
            string brown = OriginOf("game:shingle-burned-brown");
            Line("  brown shingle: " + ValueOf("game:shingle-burned-brown") + " (" + brown + "), black shingle: " + ValueOf("game:shingle-burned-black"));
            Check(brown.Contains("beehive kiln") && Math.Abs(ValueOf("game:shingle-burned-brown") - ValueOf("game:shingle-burned-black")) < 1e-6, "kiln-coloured shingles cost the same clay as the others");
            Check(ValueOf("game:slantedroofing-agedthatch-east-free") > 0, "aged thatch roofing has a value");
            string boat = OriginOf("game:boat-sailed-oak");
            Line("  oak sailboat: " + ValueOf("game:boat-sailed-oak") + " (" + (boat.Length > 120 ? boat.Substring(0, 120) + "..." : boat) + ")");
            Check(boat.StartsWith("built in stages") && ValueOf("game:boat-sailed-oak") > ValueOf("game:boat-raft-oak"), "sailboats are valued from their build stages");
            double four = ValueOf("game:cheese-cheddar-4slice"), two = ValueOf("game:cheese-cheddar-2slice");
            Line($"  cheddar: 4 slices {four}, 2 slices {two} ({OriginOf("game:cheese-cheddar-2slice")})");
            Check(four > 0 && Math.Abs(two - four / 2) < 0.01, "half a cheese is worth half a cheese");
            string mold = OriginOf("game:toolmold-black-fired-axe");
            Line("  black axe mold: " + ValueOf("game:toolmold-black-fired-axe") + " (" + mold + ")");
            Check(!mold.Contains("anvil"), "the axe mold isn't priced like the anvil mold");
        }

        /// <summary>1.7.0 contents/material modes: kinds, material-specific values, exact copies, chiseled blocks, filled containers.</summary>
        void CheckContents()
        {
            StackDataTable data = mod.Emc.StackData;
            Line($"  contents: {data.Kinds.Count} carry data ({data.Kinds.Values.Count(k => k == StackDataKind.Contents)} containers, {data.Kinds.Values.Count(k => k == StackDataKind.Material)} material, {data.Kinds.Values.Count(k => k == StackDataKind.Chiseled)} chiseled); {data.Variants.Count} with material values");
            foreach (var (code, kind, mode) in new[] {
                ("game:shield-woodmetal", StackDataKind.Material, StackDataMode.Full),
                ("game:woodbucket", StackDataKind.Contents, StackDataMode.Full),
                ("game:chiseledblock", StackDataKind.Chiseled, StackDataMode.Full) })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                bool ok = c != null && data.CarriesData(c) && data.KindOf(c) == kind && data.ModeOf(c) == mode;
                Check(ok, $"{code} is {kind}, {mode} ({(c == null ? "missing" : data.CarriesData(c) ? data.KindOf(c) + ", " + data.ModeOf(c) : "not listed")})");
            }

            // Round shields: each metal its own value, and an exact copy is worth the same.
            Item shield = sapi.World.GetItem(new AssetLocation("game:shield-woodmetal"));
            var shieldValues = new List<(string, double)>();
            ItemStack goldShield = null;
            foreach (var tab in shield?.CreativeInventoryStacks ?? Array.Empty<CreativeTabAndStackList>())
            {
                foreach (JsonItemStack js in tab.Stacks ?? Array.Empty<JsonItemStack>())
                {
                    ItemStack st = js.ResolvedItemstack;
                    if (st == null) continue;
                    string metal = st.Attributes.GetString("metal");
                    if (metal == null || shieldValues.Any(v => v.Item1 == metal)) continue;
                    shieldValues.Add((metal, mod.Emc.GetValue(st)));
                    if (metal == "gold") goldShield = st;
                }
            }
            Line("  round shields by metal: " + string.Join(", ", shieldValues.Select(v => v.Item1 + " " + EmcFormat.Format(v.Item2))));
            Check(shieldValues.Count > 1 && shieldValues.Max(v => v.Item2) > shieldValues.Min(v => v.Item2), "round shields made of different metals are worth different amounts");
            if (goldShield != null)
            {
                var kept = StackDataUtil.KeptData(goldShield);
                string key = PlayerEmc.ExactKey(goldShield.Collectible, kept);
                ItemStack copy = TransmutationService.Prototype(mod, sapi.World, key);
                Check(copy != null && copy.Attributes.GetString("metal") == "gold" && Math.Abs(mod.Emc.GetValue(copy) - mod.Emc.GetValue(goldShield)) < 1e-6, "an exact copy of a gold shield is a gold shield at the same price");
            }
            // Mod changes: a typed version the game no longer lists is not offered; an exact key whose contents are gone is dead.
            Block tapestryBlock = sapi.World.Blocks.FirstOrDefault(b => b?.Code?.Path == "tapestry-north" && b.Code.Domain == "game");
            if (tapestryBlock != null)
            {
                // The check needs the game's list of versions, which a dedicated server doesn't hold for every block.
                ItemStack real = StackDataUtil.CreativeVersions(tapestryBlock, sapi.World).FirstOrDefault();
                Line("  tapestry versions known to this server: " + (real != null ? "yes" : "none (the version check is skipped here)"));
                if (real != null)
                {
                    var fake = new TreeAttribute(); fake.SetString("type", "nosuchpattern-from-a-removed-mod");
                    string badKey = PlayerEmc.ExactKey(tapestryBlock, fake);
                    Check(TransmutationService.Prototype(mod, sapi.World, badKey) == null, "a tapestry pattern the game no longer has is not offered or created");
                    Check(TransmutationService.Prototype(mod, sapi.World, PlayerEmc.ExactKey(tapestryBlock, StackDataUtil.KeptData(real))) != null, "a real tapestry pattern is still offered");
                }
            }

            // Chiseled block: a full block of granite is worth a granite block, half of one half.
            Block chiseled = sapi.World.GetBlock(new AssetLocation("game:chiseledblock"));
            Block granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            if (chiseled != null && granite != null)
            {
                ItemStack Chiseled(int y2)
                {
                    var st = new ItemStack(chiseled);
                    st.Attributes["materials"] = new IntArrayAttribute(new[] { granite.Id });
                    uint cuboid = (15u << 12) | ((uint)(y2 - 1) << 16) | (15u << 20);
                    st.Attributes["cuboids"] = new IntArrayAttribute(new[] { (int)cuboid });
                    return st;
                }
                double full = mod.Emc.GetValue(Chiseled(16)), half = mod.Emc.GetValue(Chiseled(8)), rock = mod.Emc.GetValue(granite);
                Line($"  chiseled granite: full {full}, half {half}, rock {rock}");
                Check(rock > 0 && Math.Abs(full - rock) < 1e-6 && Math.Abs(half - rock / 2) < 1e-6, "chiseled blocks are worth their voxels of material");
            }

            // A crucible holding 2 ingots' worth of molten copper (200 units).
            Block crucible = sapi.World.GetBlock(new AssetLocation("game:crucible-blue-smelted"));
            Item copper = sapi.World.GetItem(new AssetLocation("game:ingot-copper"));
            if (crucible != null && copper != null)
            {
                var st = new ItemStack(crucible);
                st.Attributes.SetItemstack("output", new ItemStack(copper));
                st.Attributes.SetInt("units", 200);
                StackDataMode before = data.ModeOf(crucible);
                data.Modes[crucible] = StackDataMode.Full;
                double full = mod.Emc.GetValue(st);
                data.Modes[crucible] = before;
                double expect = mod.Emc.GetValue(crucible) + 2 * mod.Emc.GetValue(copper);
                Line($"  crucible with 200 units of copper, item + contents: {full} (expected {expect}); kind {data.KindOf(crucible)}");
                Check(data.CarriesData(crucible) && Math.Abs(full - expect) < 1e-6, "a crucible of molten metal counts its metal units");
            }

            // A bucket of water: item + contents by default (1.18.1), so it can be learned and made; item only ignores the water.
            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item water = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            if (bucket != null && water != null)
            {
                var st = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(water, 1000));
                st.Attributes["contents"] = contents;
                StackDataMode before = data.ModeOf(bucket);
                data.Modes[bucket] = StackDataMode.Item;
                double plain = mod.Emc.GetValue(st);
                data.Modes[bucket] = StackDataMode.Full;
                double full = mod.Emc.GetValue(st);
                data.Modes[bucket] = StackDataMode.ItemPrice;
                double price = mod.Emc.GetValue(st);
                data.Modes[bucket] = before;
                Line($"  bucket of 10 L water: item only {plain}, item + contents {full}, item price {price}");
                Check(full > plain && Math.Abs(price - plain) < 1e-6, "filled buckets count their water only in item + contents mode");

                // Learned as a full bucket, whatever was in it: a 3 L bucket teaches the 10 L version, and that one is worth bucket + 10 L.
                var part = new ItemStack(bucket);
                var partContents = new TreeAttribute();
                partContents["0"] = new ItemstackAttribute(new ItemStack(water, 300));
                part.Attributes["contents"] = partContents;
                ItemStack filled = StackDataUtil.FilledLiquid(part, sapi.World);
                int litres = filled.Attributes?.GetTreeAttribute("contents")?.GetItemstack("0")?.StackSize ?? 0;
                string learned = EmcCommands.KnowledgeKey(mod, part);
                ItemStack made = PlayerEmc.ResolveStack(sapi.World, learned);
                int madeLitres = made?.Attributes?.GetTreeAttribute("contents")?.GetItemstack("0")?.StackSize ?? 0;
                Line($"  3 L bucket (capacity {StackDataUtil.CapacityLitres(bucket)} L): learned as {litres / 100.0} L ({learned.Length} chars), made as {madeLitres / 100.0} L worth {(made == null ? -1 : mod.Emc.GetValue(made))}");
                Check(litres == 1000 && madeLitres == 1000 && made != null && Math.Abs(mod.Emc.GetValue(made) - full) < 1e-6, "a part-filled bucket is learned and made as a full one");
                Check(Math.Abs(mod.Emc.GetValue(part) - (plain + 3)) < 1e-6, $"a 3 L bucket burns for the bucket plus 3 EMC ({mod.Emc.GetValue(part)} vs {plain + 3})");

                // Every liquid container with every valued liquid, part-filled and aged: what you learn from it costs at
                // least what it burned for (no profit in burn → learn → make), and comes out fresh.
                var containers = sapi.World.Collectibles.Where(c => c?.Code != null && StackDataUtil.IsLiquidContainer(c) && StackDataUtil.CapacityLitres(c) > 0).ToList();
                var liquids = sapi.World.Items.Where(i => i?.Code != null && i.Code.Path.Contains("portion") && mod.Emc.GetValue(i) > 0).ToList();
                int tried = 0, profit = 0, aged = 0;
                var worst = new List<string>();
                foreach (CollectibleObject box in containers)
                {
                    foreach (Item liq in liquids)
                    {
                        double perLitre = liq.Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsDouble(1) ?? 1;
                        int n = Math.Max(1, (int)(StackDataUtil.CapacityLitres(box) * perLitre * 0.3));
                        var inner = new ItemStack(liq, n);
                        var ts = new TreeAttribute(); ts.SetDouble("createdTotalHours", 1); ts.SetDouble("lastUpdatedTotalHours", 1); ts.SetFloat("freshHoursPercent", 10);
                        inner.Attributes["transitionstate"] = ts;
                        ItemStack holder;
                        try { holder = new ItemStack(box); } catch { continue; }
                        var cc = new TreeAttribute(); cc["0"] = new ItemstackAttribute(inner); holder.Attributes["contents"] = cc;
                        double burned = mod.Emc.GetValue(holder);
                        if (burned <= 0) continue;
                        tried++;
                        string key = EmcCommands.KnowledgeKey(mod, holder);
                        ItemStack proto = TransmutationService.Prototype(mod, sapi.World, key);
                        double cost = proto == null ? 0 : mod.Emc.GetValue(proto);
                        if (proto != null && cost + 1e-6 < burned) { profit++; if (worst.Count < 5) worst.Add($"{box.Code}+{liq.Code} burns {burned} makes for {cost}"); }
                        ItemStack madeLiquid = proto?.Attributes?.GetTreeAttribute("contents")?.GetItemstack("0");
                        if (madeLiquid?.Attributes?["transitionstate"] != null) aged++;
                    }
                }
                var noCapacity = sapi.World.Collectibles.Where(c => c?.Code != null && StackDataUtil.IsLiquidContainer(c) && StackDataUtil.CapacityLitres(c) <= 0).Select(c => c.Code.ToString()).ToList();
                Line($"  liquid containers with no capacity (learned at whatever fill they had): {noCapacity.Count} {string.Join(", ", noCapacity.Take(15))}");

                // What data the things learned as exact versions carry: anything here that changes by itself (like
                // freshness did) would make a new entry every time.
                var keyCount = new Dictionary<string, int>();
                var keyExample = new Dictionary<string, string>();
                foreach (CollectibleObject c in sapi.World.Collectibles)
                {
                    if (c?.Code == null || !data.CarriesData(c) || data.ModeOf(c) == StackDataMode.Item) continue;
                    List<ItemStack> versions;
                    try { versions = StackDataUtil.CreativeVersions(c, sapi.World).Take(4).ToList(); } catch { continue; }
                    foreach (ItemStack v in versions)
                    {
                        void Walk(ITreeAttribute t, string path)
                        {
                            foreach (var kv in t)
                            {
                                string p2 = path + kv.Key;
                                if (kv.Value is ITreeAttribute sub) Walk(sub, p2 + ".");
                                else if (kv.Value is ItemstackAttribute isa && isa.value?.Attributes != null) Walk(isa.value.Attributes, p2 + ">");
                                string norm = System.Text.RegularExpressions.Regex.Replace(p2, @"\d+", "#");
                                keyCount[norm] = keyCount.TryGetValue(norm, out int n) ? n + 1 : 1;
                                if (!keyExample.ContainsKey(norm)) keyExample[norm] = c.Code.ToString();
                            }
                        }
                        if (v?.Attributes != null) Walk(v.Attributes, "");
                    }
                }
                Line($"  attribute paths on exact-version things ({keyCount.Count}): " + string.Join("; ", keyCount.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value} ({keyExample[k.Key]})")));

                Line($"  liquid sweep: {containers.Count} containers x {liquids.Count} valued liquids, {tried} combinations valued; {profit} cheaper to make than they burn, {aged} made aged");
                foreach (string w in worst) Line("    " + w);
                Check(tried > 0 && profit == 0 && aged == 0, "no liquid container can be burned for more than it costs to make, and made liquids are fresh");

                // Liquids that spoil or turn into something worth more than they are (per portion) would be free EMC by waiting.
                var gain = new List<string>();
                foreach (Item liq in liquids)
                {
                    TransitionableProperties[] props;
                    try { props = liq.GetTransitionableProperties(sapi.World, new ItemStack(liq), null); } catch { continue; }
                    if (props == null) continue;
                    foreach (var tp in props)
                    {
                        ItemStack outStack = tp?.TransitionedStack?.ResolvedItemstack;
                        if (outStack?.Collectible == null) continue;
                        double was = mod.Emc.GetValue(liq), after = mod.Emc.GetValue(outStack) * outStack.StackSize * tp.TransitionRatio;
                        if (after > was * 1.01 + 1e-6) gain.Add($"{liq.Code} ({was}) -{tp.Type}-> {outStack.Collectible.Code} x{tp.TransitionRatio} ({after})");
                    }
                }
                Line($"  liquid transitions worth more after than before: {gain.Count}");
                foreach (string g in gain.Take(10)) Line("    " + g);

                // Freshness inside doesn't make a different thing to learn: two buckets of milk at different ages share a key.
                Item milk = sapi.World.GetItem(new AssetLocation("game:milkportion"));
                if (milk != null)
                {
                    ItemStack Milk(double hours)
                    {
                        var liquid = new ItemStack(milk, 1000);
                        var ts = new TreeAttribute();
                        ts.SetDouble("createdTotalHours", hours);
                        ts.SetDouble("lastUpdatedTotalHours", hours);
                        ts.SetFloat("freshHoursPercent", 100);
                        liquid.Attributes["transitionstate"] = ts;
                        var b = new ItemStack(bucket);
                        var cc = new TreeAttribute();
                        cc["0"] = new ItemstackAttribute(liquid);
                        b.Attributes["contents"] = cc;
                        return b;
                    }
                    string k1 = EmcCommands.KnowledgeKey(mod, Milk(10)), k2 = EmcCommands.KnowledgeKey(mod, Milk(500));
                    Check(k1 == k2 && !k1.Contains("transitionstate"), $"buckets of milk of different ages are one thing to learn ({k1.Length} chars)");
                }

                // A bucket of something with no value is worth nothing at all (else the liquid could be made for free).
                Item unvalued = sapi.World.Items.FirstOrDefault(i => i?.Code != null && i.Code.Domain == "game" && i.Code.Path.EndsWith("portion") && mod.Emc.GetValue(i) <= 0);
                if (unvalued != null)
                {
                    var odd = new ItemStack(bucket);
                    var oddContents = new TreeAttribute();
                    oddContents["0"] = new ItemstackAttribute(new ItemStack(unvalued, 1000));
                    odd.Attributes["contents"] = oddContents;
                    Check(mod.Emc.GetValue(odd) <= 0, $"a bucket of an unvalued liquid ({unvalued.Code}) is unvalued as a whole ({mod.Emc.GetValue(odd)})");
                }
                else Line("  (every base-game liquid has a value: the unvalued-contents check was skipped)");
            }
        }

        string RunCommand(string line)
        {
            string answer = null;
            var args = new TextCommandCallingArgs
            {
                Caller = new Caller { Type = EnumCallerType.Console, CallerRole = "admin", CallerPrivileges = new[] { "*" }, FromChatGroupId = GlobalConstants.ConsoleGroup }
            };
            sapi.ChatCommands.ExecuteUnparsed(line, args, r => answer = r.StatusMessage + " [" + r.Status + "]");
            return answer ?? "(no answer)";
        }

        /// <summary>
        /// 1.8.0: value rules (bone arrow, free schematic, translocator parts, creative-only Tome), the red matter pedestal,
        /// the no-transmute list, the tablet magnet's filter, value loops, dump files in ModData/vintagee, and resetting settings.
        /// </summary>
        void Check180()
        {
            EmcMappingReport report = mod.LastReport;
            CollectibleObject C(string code) => (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
            double V(string code) => mod.Emc.GetValue(C(code));

            double flint = V("game:arrow-flint"), bone = V("game:arrow-bone");
            Check(flint > 0 && Math.Abs(bone - flint) < 1e-6, $"bone arrow is worth a flint arrow: {EmcFormat.Format(bone)} / {EmcFormat.Format(flint)}");

            CollectibleObject schematic = C("game:schematic-customtranslocator");
            Check(schematic != null && report.Free.Contains(schematic) && report.Blacklisted.Contains(schematic) && V("game:schematic-customtranslocator") <= 0,
                "custom translocator schematic is a free ingredient (blacklisted, no value)");
            // 1.9.0: the illuminal divergent manifold (translocator part) is a free ingredient too, so translocators get a value.
            CollectibleObject part = C("game:tobtlocatorpart");
            var tobt = sapi.World.Collectibles.Where(c => c.Code?.Domain == "game" && c.Code.Path.StartsWith("tobtlocator-")).ToList();
            Check(part != null && report.Free.Contains(part) && tobt.Count > 0 && tobt.All(c => !report.Blacklisted.Contains(c)),
                $"translocator part is a free ingredient and {tobt.Count} rickety translocators are off the blacklist");
            foreach (CollectibleObject t in tobt.Take(1))
            {
                string why = report.MissingWhy.TryGetValue(t, out string w) ? w : null;
                Check(mod.Emc.GetValue(t) > 0, $"{t.Code} has a value: {(why == null ? EmcFormat.Format(mod.Emc.GetValue(t)) + " EMC" : why)}");
            }

            CollectibleObject tome = C("vintagee:gadget-tomeofknowledge");
            bool tomeRecipe = sapi.World.GridRecipes.Any(r => r.Output?.Code != null && r.Output.Code.ToString() == "vintagee:gadget-tomeofknowledge");
            Check(tome != null && !tomeRecipe && V("vintagee:gadget-tomeofknowledge") <= 0, "Tome of Knowledge has no recipe and no EMC value");

            CollectibleObject dmPed = C("vintagee:pedestal-empty"), rmPed = C("vintagee:redmatterpedestal-empty");
            Check(rmPed is Block rb && rb.Attributes?["pedestalRange"].AsFloat(1) == 2 && rb.Attributes?["pedestalPower"].AsFloat(1) == 2,
                "red matter pedestal: twice the range and power");
            Check(V("vintagee:redmatterpedestal-empty") > V("vintagee:pedestal-empty") && V("vintagee:pedestal-empty") > 0,
                $"red matter pedestal is worth more than the dark one: {EmcFormat.Format(V("vintagee:redmatterpedestal-empty"))} > {EmcFormat.Format(V("vintagee:pedestal-empty"))}");

            // No-transmute list: a value is kept, burning and creating are refused, and taking it off works.
            CollectibleObject granite = C("game:rock-granite");
            string key = VintageEModSystem.ConfigKey(granite);
            mod.SetNoTransmute(new[] { key }, true);
            CheckArcaneTablet();
            // 1.18.11: a Klein star is burned by hand (for the star plus what it holds), never by a hopper or the magnet.
            var kStarItem = sapi.World.GetItem(new AssetLocation("vintagee:kleinstar-ein")) as CollectibleObject;
            if (kStarItem != null)
            {
                var kStar = new ItemStack(kStarItem);
                StoredEmc.Set(kStar, 1000);
                string auto = mod.Transmutation.PlanBurn(null, kStar, out _, out _);
                string hand = mod.Transmutation.PlanBurn(null, kStar, out TransmutationService.BurnPlan kStarPlan, out _, byHand: true);
                double plain = mod.Emc.GetValue(new ItemStack(kStarItem));
                Check(auto == "star-noburn" && hand == null && kStarPlan != null && Math.Abs(kStarPlan.Each - (plain + 1000)) < 0.01,
                    $"a Klein star holding 1,000 is burned only by hand, for the star plus what it holds (automatic: {auto ?? "allowed"}, by hand: {hand ?? "allowed"}, worth {kStarPlan?.Each} = {plain} + 1,000)");
            }
            else Check(false, "vintagee:kleinstar-ein exists");
            string refused = mod.Transmutation.PlanBurn(null, new ItemStack(granite), out _, out _);
            Check(mod.Emc.IsNoTransmute(granite) && mod.Emc.GetValue(granite) > 0 && refused == "notransmute", $"no-transmute: granite keeps its value but can't be burned ({refused})");
            Check(mod.Emc.ToPacket().NoTransmuteKeys?.Contains(PlayerEmc.KeyOf(granite)) == true, "no-transmute list goes to clients");
            mod.SetNoTransmute(new[] { key }, false);
            Check(!mod.Emc.IsNoTransmute(granite) && mod.Transmutation.PlanBurn(null, new ItemStack(granite), out _, out _) == null, "no-transmute: taken off again");

            // Tablet magnet filter.
            Item tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
            var tablet = new ItemStack(tabletItem);
            Check(!TabletMagnet.Passes(tablet, granite), "magnet: an empty 'only these' filter pulls nothing");
            ((TreeAttribute)tablet.Attributes).SetStringArray(TabletMagnet.FilterKey, new[] { PlayerEmc.KeyOf(granite), "", "" });
            Check(TabletMagnet.Passes(tablet, granite) && !TabletMagnet.Passes(tablet, C("game:rock-andesite")), "magnet: 'only these' pulls just the filtered item");
            tablet.Attributes.SetBool(TabletMagnet.ExcludeKey, true);
            Check(!TabletMagnet.Passes(tablet, granite) && TabletMagnet.Passes(tablet, C("game:rock-andesite")), "magnet: 'all except these' skips the filtered item");
            Check(TabletMagnet.IsMagnet(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:trinket-blackholeband")))) && !TabletMagnet.IsMagnet(new ItemStack(granite)),
                "magnet: Black Hole Band fits, a rock doesn't");

            // Typed blocks keep their type when learned: a tapestry is learned as exactly its pattern, not as a blank tapestry.
            CollectibleObject tapestry = C("game:tapestry-north");
            if (tapestry != null)
            {
                var typed = new ItemStack(tapestry);
                typed.Attributes.SetString("type", "schematic-a");
                string err = mod.Transmutation.PlanBurn(null, typed, out var tPlan, out _, learnOnly: true);
                Check(mod.Emc.StackData.KindOf(tapestry) == StackDataKind.Material && err == null && tPlan != null && PlayerEmc.IsExact(tPlan.Key),
                    "tapestry: learned as its exact pattern (kind " + mod.Emc.StackData.KindOf(tapestry) + ", key " + (tPlan?.Key ?? err ?? "?").Substring(0, Math.Min(40, (tPlan?.Key ?? err ?? "?").Length)) + ")");
            }

            // Clutter: the plain block isn't priced from a trader's typed stack, and clutter can't be made typeless.
            CollectibleObject clutter = C("game:clutter");
            if (clutter != null && mod.LastReport?.Origins != null)
            {
                mod.LastReport.Origins.TryGetValue(clutter, out string clutterOrigin);
                Check(clutterOrigin == null || !clutterOrigin.StartsWith("trader", StringComparison.Ordinal), "clutter: the plain block isn't priced by a trader's typed stack (" + (clutterOrigin ?? "no value") + ")");
                int typed = mod.LastReport.Variants != null && mod.LastReport.Variants.TryGetValue(clutter, out var cv) ? cv.Count : 0;
                Check(StackDataUtil.NeedsData(clutter) && mod.Emc.StackData.KindOf(clutter) == StackDataKind.Material, "clutter: needs its type and keeps it (" + typed + " typed values from traders)");
            }

            // Everything that is nothing without its data, listed so the CI output shows them (cuttings, clutter, tapestries...).
            var needy = sapi.World.Collectibles.Where(c => c?.Code != null && CollectibleIndex.IsListed(c) && StackDataUtil.NeedsData(c, sapi.World)).ToList();
            var needyNotMaterial = needy.Where(c => mod.Emc.StackData.KindOf(c) != StackDataKind.Material).ToList();
            CollectibleObject cutting = C("game:fruittree-cutting");
            Check(cutting != null && StackDataUtil.NeedsData(cutting, sapi.World) && needyNotMaterial.Count == 0,
                "fruit tree cutting: " + (cutting == null ? "not found" : cutting.GetType().Name + (CollectibleIndex.IsListed(cutting) ? ", listed" : ", not in the creative inventory") + ", kind " + mod.Emc.StackData.KindOf(cutting)) + "; needs its type");
            Check(needyNotMaterial.Count == 0,
                "typed-only blocks: " + needy.Count + " need their data (" + string.Join(", ", needy.Take(12).Select(c => c.Code.ToShortString())) + (needy.Count > 12 ? ", …" : "") + "); "
                + (needyNotMaterial.Count == 0 ? "all kept as material kinds" : needyNotMaterial.Count + " not material: " + string.Join(", ", needyNotMaterial.Take(8).Select(c => c.Code.ToShortString()))));
            // A tapestry pattern no trader sells is priced like the patterns traders do sell.
            if (tapestry != null)
            {
                var odd = new ItemStack(tapestry); odd.Attributes.SetString("type", "vintagee-nosuchpattern");
                Check(mod.Emc.GetValue(odd) > 0, "tapestry: an unsold pattern is priced like its siblings (" + EmcFormat.Format(mod.Emc.GetValue(odd)) + ")");
            }

            // The everlasting glue: exists, and a use costs what a litre of pitch glue is worth.
            CollectibleObject glue = C("vintagee:gadget-eternalglue");
            double glueCost = ItemEmcGadget.GlueCost(mod);
            Check(glue != null && glueCost > 0 && glueCost < 1000, "everlasting glue: exists, a use costs " + EmcFormat.Format(glueCost) + " EMC");

            // Loops and reasons.
            Line($"  value loops: {report.Loops.Count}, settled loops: {report.SettledLoops.Count}");
            foreach (var loop in report.Loops.Take(5)) Line("    " + MissingAnalysis.LoopText(loop));
            Check(report.Missing.All(c => report.MissingWhy.ContainsKey(c)), "every missing item has a reason");

            // Files go to ModData/vintagee and the chat says where.
            string dir = VintageEModSystem.DataFolder(sapi);
            string dumpAnswer = RunCommand("/emc dump");
            Check(File.Exists(Path.Combine(dir, "emc-values.csv")) && dumpAnswer.Contains(dir), "/emc dump writes ModData/vintagee/emc-values.csv and says where: " + dumpAnswer);
            // 1.9.3: chat reads command answers as VTML; "A -> B" and "<value>" broke it, so the answer showed nothing.
            string raw = Lang.Get("vintagee:loop-header", 1) + "\n1. game:a -> game:b -> game:a  /emc set <value>";
            bool rawBroken = VtmlParser.Tokenize(sapi.Logger, raw).Any(t => !(t is VtmlTextToken));
            bool safeClean = VtmlParser.Tokenize(sapi.Logger, EmcCommands.ChatSafe(raw)).All(t => t is VtmlTextToken);
            Check(rawBroken && safeClean, $"/emc answers are escaped for the chat's markup (unescaped breaks: {rawBroken}, escaped is plain text: {safeClean})");
            string loopAnswer = RunCommand("/emc loop");
            Check(File.Exists(Path.Combine(dir, "emc-loops.txt")) && loopAnswer.Contains("emc-loops.txt"), "/emc loop writes emc-loops.txt: " + loopAnswer.Split('\n')[0]);
            Check(dir.Replace('\\', '/').Contains("ModData/vintagee"), "data folder is ModData/vintagee: " + dir);

            // Resetting settings.
            double defaultRange = new VintageEConfig().TableUseRange, adminRange = mod.Config.TableUseRange;
            RunCommand("/emc configure TableUseRange 13");
            string reset = RunCommand("/emc configure TableUseRange default");
            Check(Math.Abs(mod.Config.TableUseRange - defaultRange) < 1e-9, "/emc configure <setting> default resets it: " + reset);
            // The admin's own value, not the default, is what stays in the settings file.
            if (Math.Abs(adminRange - defaultRange) > 1e-9) RunCommand("/emc configure TableUseRange " + adminRange.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Check(ConfigureCommand.DefaultTexts().Length == ConfigureCommand.Snapshot(mod.Config).names.Length, "every setting has a default for the window");

            // Look is a drop-down in the window; typed, it only takes its choices.
            Check(ConfigureCommand.KindOf(ConfigureCommand.Field("Look")) == "choice" && ConfigureCommand.ChoicesOf("Look")?.Length == 3, "Look is a drop-down of its three choices");
            string lookBefore = mod.Config.Look;
            string bad = RunCommand("/emc configure Look banana");
            Check(mod.Config.Look == lookBefore, "/emc configure Look refuses a word that isn't a choice: " + bad);
            RunCommand("/emc configure Look middle");
            Check(mod.Config.Look == ProjectELook.LookMiddle, "/emc configure Look middle sets Middle: " + mod.Config.Look);
            ConfigureCommand.SetFromText(mod, "Look", "Original", "en");
            Check(mod.Config.Look == ProjectELook.LookOriginal, "the window's drop-down value Original is saved: " + mod.Config.Look);
            RunCommand("/emc configure Look " + lookBefore);
            Check(mod.Config.Look == lookBefore, "Look put back to " + lookBefore);
        }

        /// <summary>
        /// ProjectETextures / ProjectESounds: every ProjectE file the swap list names is there, and - when the server
        /// test turns the settings on - blocks, items and projectiles really use ProjectE's textures.
        /// </summary>
        /// <summary>1.19.0: the Arcane Tablet, Emc Craft's plan, and the game's crafting grid under Vintage-E's ID.</summary>
        void CheckArcaneTablet()
        {
            try
            {
                Item arcane = sapi.World.GetItem(new AssetLocation("vintagee:arcanetablet"));
                Item tablet = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
                Item red = sapi.World.GetItem(new AssetLocation("vintagee:matter-red"));
                Check(arcane is ItemArcaneTablet && TabletMagnet.IsTablet(new ItemStack(arcane)), "the Arcane Tablet exists and counts as a tablet");
                if (arcane == null || tablet == null || red == null) return;
                double vArcane = mod.Emc.GetValue(arcane), vTablet = mod.Emc.GetValue(tablet), vRed = mod.Emc.GetValue(red);
                Check(vArcane > 0 && Math.Abs(vArcane - (vTablet + 4 * vRed)) < 0.5, $"the Arcane Tablet is worth a tablet and four red matter ({vArcane} = {vTablet} + 4 × {vRed})");

                GridRecipe recipe = sapi.World.GridRecipes.FirstOrDefault(r => r.Output?.Code?.ToString() == "vintagee:arcanetablet");
                Check(recipe != null, "the Arcane Tablet has a crafting recipe");
                if (recipe == null) return;
                var craft = mod.EmcCrafting;
                EmcCraft.Plan none = craft.MakePlan(recipe, new string[0], null);
                Check(none.Missing.Count == 2, $"Emc Craft with nothing learned names both ingredients as missing ({string.Join(", ", none.Missing)})");
                var known = new[] { PlayerEmc.KeyOf(red), PlayerEmc.KeyOf(tablet) };
                EmcCraft.Plan plan = craft.MakePlan(recipe, known, null);
                int filled = plan.Cells.Count(c => c != null);
                Check(plan.Missing.Count == 0 && plan.NotMakeable.Count == 0 && filled == 5 && Math.Abs(plan.Cost - (vTablet + 4 * vRed)) < 0.5,
                    $"Emc Craft plans the Arcane Tablet: 5 cells for {plan.Cost} EMC");

                // The plan's layout satisfies every ingredient where the recipe wants it, and the recipe makes the tablet from it.
                // (The game's crafting grid itself asks which player owns it while matching; the test server has none.)
                bool layoutOk = true;
                for (int row = 0; row < recipe.Height; row++)
                    for (int col = 0; col < recipe.Width; col++)
                    {
                        var ing = recipe.ResolvedIngredients[row * recipe.Width + col];
                        ItemStack cellStack = plan.Cells[row * EmcCraft.GridSize + col];
                        if (ing == null ? cellStack != null : cellStack == null || !ing.SatisfiesAsIngredient(cellStack)) layoutOk = false;
                    }
                var slots = plan.Cells.Select(c => (ItemSlot)new DummySlot(c?.Clone())).ToArray();
                var outSlot = new DummySlot();
                recipe.GenerateOutputStack(slots, outSlot);
                Check(layoutOk && outSlot.Itemstack?.Collectible == arcane, $"Emc Craft lays the ingredients out as the recipe wants, and it makes {outSlot.Itemstack?.Collectible?.Code}");
                InventoryBase rebuilt = null;
                try { rebuilt = Vintagestory.Server.ServerMain.ClassRegistry?.CreateInventory(EmcCraft.InventoryClass, EmcCraft.InventoryClass + "-selftest", sapi); } catch { }
                Check(rebuilt is EmcCraftGrid, $"the game can rebuild a saved Emc Craft grid by its class name ({rebuilt?.GetType().Name ?? "no"})");
                var hb = HandbookPatches.Target();
                Check(hb != null && hb.ReturnType == typeof(Vintagestory.API.Client.RichTextComponentBase[]) && hb.GetParameters()[1].Name == "capi",
                    $"the game's handbook page builder is there for the Emc Craft link ({hb?.DeclaringType?.Name}.{hb?.Name}, parameter '{(hb == null ? "?" : hb.GetParameters()[1].Name)}')");
                var gridInv = new EmcCraftGrid(EmcCraft.InventoryClass, "selftest", sapi);
                Check(gridInv.InventoryID == EmcCraft.InventoryClass + "-selftest" && gridInv.Count == 10 && gridInv.RemoveOnClose,
                    $"the game's crafting grid takes Vintage-E's ID and goes away when closed ({gridInv.InventoryID}, {gridInv.Count} slots, removed on close {gridInv.RemoveOnClose})");

                // A recipe with a tool: the tool is made from EMC when none is carried, marked, and burned back on closing.
                GridRecipe withTool = sapi.World.GridRecipes.FirstOrDefault(r => r.Enabled != false && r.ResolvedIngredients != null && r.Output?.Code?.Domain == "game"
                    && r.ResolvedIngredients.Any(i => i != null && i.IsTool && i.ResolvedItemStack != null && mod.Emc.GetValue(i.ResolvedItemStack) > 0)
                    && r.ResolvedIngredients.All(i => i == null || i.ResolvedItemStack != null && mod.Emc.GetValue(i.ResolvedItemStack) > 0));
                if (withTool != null)
                {
                    var toolKeys = withTool.ResolvedIngredients.Where(i => i != null).Select(i => PlayerEmc.KeyOf(i.ResolvedItemStack.Collectible)).Distinct().ToArray();
                    EmcCraft.Plan tp = craft.MakePlan(withTool, toolKeys, null);
                    int toolCell = Array.FindIndex(tp.Cells, c => c != null && c.Attributes.GetString(EmcCraft.MadeFromEmc) != null);
                    Check(tp.Missing.Count == 0 && toolCell >= 0, $"Emc Craft makes the tool for {withTool.Output.Code} from EMC and marks it ({tp.Cells[Math.Max(0, toolCell)]?.Collectible?.Code})");
                    if (toolCell >= 0)
                    {
                        ItemStack tool = tp.Cells[toolCell];
                        int maxDur = tool.Collectible.GetMaxDurability(tool);
                        double wear = craft.WearCost(tool, withTool.ResolvedIngredients.First(i => i != null && i.IsTool));
                        Check(maxDur > 1 && Math.Abs(wear - mod.Emc.GetValue(tool) / maxDur) < 0.01 && tp.NetCost < tp.Cost,
                            $"Emc Craft counts only the tool's wear as the real cost ({wear:0.##} of {mod.Emc.GetValue(tool)}; {tp.NetCost:0.##} real vs {tp.Cost:0.##} up front)");
                        var (pick, pickPlan, _, _) = craft.Choose(null, withTool, toolKeys, new List<ItemSlot>(), double.MaxValue);
                        Check(pick != null && pickPlan != null && pickPlan.NetCost <= tp.NetCost + 1e-6,
                            $"Emc Craft picks the cheapest recipe for {withTool.Output.Code} ({pickPlan?.NetCost:0.##} EMC, {pick?.Name})");
                    }
                    // One item per grid: taking one out of a grid that still holds something makes the game match
                    // recipes for the grid's player, and the test server has none.
                    var g2 = new EmcCraftGrid(EmcCraft.InventoryClass, "selftest2", sapi);
                    var g3 = new EmcCraftGrid(EmcCraft.InventoryClass, "selftest3", sapi);
                    double back = 0, strayBack = -1;
                    if (toolCell >= 0)
                    {
                        // A made tool (recorded) is burned; a tool carrying the same attribute that this grid never made
                        // (one that left the grid earlier, say) isn't, and loses the attribute.
                        g2[0].Itemstack = tp.Cells[toolCell].Clone(); // no MarkDirty: matching would ask for the (absent) player
                        craft.RecordMade("selftest2", g2[0].Itemstack);
                        back = craft.Empty(null, g2, burnMadeTools: true, ownerUid: "selftest2");
                        ItemStack stray = tp.Cells[toolCell].Clone();
                        stray.Attributes.SetString(EmcCraft.MadeFromEmc, "not-made-here");
                        g3[0].Itemstack = stray;
                        strayBack = craft.Empty(null, g3, burnMadeTools: true, ownerUid: "selftest3");
                    }
                    Check(toolCell >= 0 && Math.Abs(back - mod.BurnValue(tp.Cells[toolCell])) < 0.01 && g2[0].Empty && strayBack == 0,
                        $"closing burns only the tools Emc Craft made, back into EMC ({back}); one it didn't make gives back {strayBack}");
                }
                else Check(true, "no base-game recipe with a valued tool to test Emc Craft's tools on (skipped)");
            }
            catch (Exception e) { Check(false, "Arcane Tablet checks threw: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()); }
        }

        /// <summary>1.9.0: handbook texts, nova ignition, pedestal weather, rings in the hand slot.</summary>
        /// <summary>1.21.0: realistic crafting routes next to the grid ones, and the CraftingMethods setting.</summary>
        void CheckCraftingRoutes()
        {
            string mode = CraftingRoutes.Normalize(mod.Config.CraftingMethods);
            Check(CraftingRoutes.Normalize("grid") == CraftingRoutes.Grid && CraftingRoutes.Normalize("REALISTIC") == CraftingRoutes.Realistic && CraftingRoutes.Normalize("") == CraftingRoutes.Both,
                "crafting methods: Grid / Realistic / Both are read from the setting (this server: " + mode + ")");
            var problems = new List<string>();
            foreach (string code in CraftingRoutes.SmeltInputs)
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                if (c?.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible?.Code?.Domain != "vintagee") problems.Add(code + " doesn't smelt into a Vintage-E item");
            }
            foreach (string code in CraftingRoutes.GrindInputs)
            {
                Item c = sapi.World.GetItem(new AssetLocation(code));
                if (c?.GrindingProps?.GroundStack?.ResolvedItemstack?.Collectible?.Code?.Domain != "vintagee") problems.Add(code + " doesn't grind into covalence dust");
            }
            int grid = CraftingRoutes.GridRoutes(sapi.World).Count(r => r.Enabled), smith = CraftingRoutes.SmithingRoutes(sapi).Count(), barrel = CraftingRoutes.BarrelRoutes(sapi).Count();
            if (grid != 9) problems.Add(grid + " of the 9 grid routes found");
            if (smith < 1) problems.Add("no smithing recipe for the iron band");
            if (barrel < 1) problems.Add("no barrel recipe for covalence dust");
            Check(mode != CraftingRoutes.Both || problems.Count == 0,
                $"both crafting routes are there: 5 smelting, 2 quern, {smith} smithing, {barrel} barrel, {grid} grid" + (problems.Count > 0 ? " (" + string.Join("; ", problems) + ")" : ""));

            // Switching: what Realistic and Grid would switch off (put back straight after; the mapping has already run).
            if (mode == CraftingRoutes.Both)
            {
                var gridOn = CraftingRoutes.GridRoutes(sapi.World).Where(r => r.Enabled).ToList();
                var smithOn = CraftingRoutes.SmithingRoutes(sapi).Where(r => r.Enabled).ToList();
                var barrelOn = CraftingRoutes.BarrelRoutes(sapi).Where(r => r.Enabled).ToList();
                int offRealistic = CraftingRoutes.ApplyRecipes(sapi, CraftingRoutes.Realistic);
                bool gridAllOff = CraftingRoutes.GridRoutes(sapi.World).All(r => !r.Enabled), smithStillOn = smithOn.All(r => r.Enabled);
                foreach (var r in gridOn) r.Enabled = true;
                int offGrid = CraftingRoutes.ApplyRecipes(sapi, CraftingRoutes.Grid);
                bool realisticAllOff = smithOn.All(r => !r.Enabled) && barrelOn.All(r => !r.Enabled);
                foreach (var r in smithOn) r.Enabled = true;
                foreach (var r in barrelOn) r.Enabled = true;
                Check(offRealistic == gridOn.Count && gridAllOff && smithStillOn && offGrid == smithOn.Count + barrelOn.Count && realisticAllOff,
                    $"crafting methods: Realistic switches off the {offRealistic} grid routes, Grid the {offGrid} smithing and barrel routes");
            }

            // EMC follows the cheapest route: the realistic ones cost less than the grid's.
            double V(string code) { Item it = sapi.World.GetItem(new AssetLocation(code)); return it == null ? 0 : mod.Emc.GetValue(new ItemStack(it)); }
            double aet = V("vintagee:alchemicalfuel-aeternalisfuel"), dark = V("vintagee:matter-dark"), red = V("vintagee:matter-red"), gear = V("game:gear-temporal"), stone = V("vintagee:philosophersstone");
            // No route may make more than it takes: 1 aeternalis → 4 mobius on the grid, so heating must take at least 4 back.
            double mob = V("vintagee:alchemicalfuel-mobiusfuel"), coal = V("vintagee:alchemicalfuel-alchemicalcoal");
            Check(coal > 0.5 && mob >= 3.99 * coal && aet >= 3.99 * mob, $"alchemical fuels keep their 4:1 steps (coal {coal:0.#}, mobius {mob:0.#}, aeternalis {aet:0.#}): no loop gains fuel");
            Check(mode == CraftingRoutes.Grid || (dark > 0 && dark <= 6 * aet + 0.01 && red > 0 && red <= 3 * dark + 0.01 && stone > 0 && (gear <= 0 || stone <= gear + 0.01)),
                $"EMC follows the cheaper realistic routes (aeternalis {aet:0.#}, dark matter {dark:0.#} ≤ 6×, red matter {red:0.#} ≤ 3× dark, philosopher's stone {stone:0.#} vs temporal gear {gear:0.#})");
        }

        /// <summary>1.20.0: matter versions of the game's knife, saw, chisel, scythe, prospecting pick, smithing hammer, spear and bow.</summary>
        void CheckMatterTools()
        {
            var expected = new (string code, EnumTool tool, string cls)[]
            {
                ("matterknife", EnumTool.Knife, "ItemKnife"), ("mattersaw", EnumTool.Saw, null), ("matterchisel", EnumTool.Chisel, "ItemChisel"),
                ("matterscythe", EnumTool.Scythe, "ItemMatterScythe"), ("matterpropick", EnumTool.Pickaxe, "ItemMatterPropick"),
                ("mattersmithinghammer", EnumTool.Hammer, "ItemMatterHammer"), ("matterspear", EnumTool.Spear, "ItemSpear"), ("matterbow", EnumTool.Bow, "ItemBow"),
            };
            var problems = new List<string>();
            foreach (var (code, tool, cls) in expected)
                foreach (string mat in new[] { "dark", "red" })
                {
                    Item item = sapi.World.GetItem(new AssetLocation("vintagee:" + code + "-" + mat));
                    if (item == null) { problems.Add(code + "-" + mat + " missing"); continue; }
                    if (item.Tool != tool) problems.Add($"{item.Code} is a {item.Tool}, not a {tool}");
                    if (cls != null && item.GetType().Name != cls) problems.Add($"{item.Code} is a {item.GetType().Name}, not {cls}");
                    if (mod.Emc.GetValue(new ItemStack(item)) <= 0) problems.Add($"{item.Code} has no EMC value");
                    if (!sapi.World.GridRecipes.Any(r => r.Output?.Code?.ToString() == item.Code.ToString() && r.ResolvedIngredients != null)) problems.Add($"{item.Code} has no recipe");
                    var slot = new DummySlot(new ItemStack(item));
                    item.DamageItem(sapi.World, null, slot, 5);
                    if (slot.Itemstack == null || slot.Itemstack.Attributes.HasAttribute("durability") && slot.Itemstack.Attributes.GetInt("durability") < item.GetMaxDurability(slot.Itemstack)) problems.Add($"{item.Code} wore down");
                }
            Check(problems.Count == 0, "matter knife, saw, chisel, scythe, prospecting pick, smithing hammer, spear and bow: right tool and class, valued, craftable, never wear out"
                + (problems.Count > 0 ? " (" + string.Join("; ", problems.Take(6)) + ")" : ""));
            Check(sapi.World.GetItem(new AssetLocation("vintagee:matterscythe-red")) is ItemMatterScythe sc && sc.MultiBreakQuantity > 5 && sc is ItemScythe
                  && sapi.World.GetItem(new AssetLocation("vintagee:matterpropick-red")) is ItemProspectingPick
                  && sapi.World.GetItem(new AssetLocation("vintagee:mattersmithinghammer-red")) is ItemHammer,
                "red matter upgrades: the scythe sweeps wider, the prospecting pick and smithing hammer are still the game's (so the anvil and prospecting work)");
            Check(sapi.World.GetEntityType(new AssetLocation("vintagee:matterspear-dark")) != null && sapi.World.GetEntityType(new AssetLocation("vintagee:matterspear-red")) != null,
                "thrown matter spears have their entity");

            // Recipes: the game names a tool by code ("saw-*"), by tag ("tool-saw") or both; matter tools must count either way.
            Check(MatterToolRecipePatches.Patched, "matter tools are hooked into the recipe ingredient checks" + (MatterToolRecipePatches.Error != null ? " (" + MatterToolRecipePatches.Error + ")" : ""));
            ItemStack S(string code) { Item it = sapi.World.GetItem(new AssetLocation(code)); return it == null ? null : new ItemStack(it); }
            var tools = new Dictionary<string, ItemStack>
            {
                ["saw"] = S("vintagee:mattersaw-dark"), ["hammer"] = S("vintagee:mattersmithinghammer-red"), ["knife"] = S("vintagee:matterknife-dark"),
                ["chisel"] = S("vintagee:matterchisel-dark"), ["axe"] = S("vintagee:mattertool-dark-axe"), ["shears"] = S("vintagee:redmattertool-katar"),
            };
            var vanilla = new Dictionary<string, ItemStack>
            {
                ["saw"] = S("game:saw-steel"), ["hammer"] = S("game:hammer-steel"), ["knife"] = S("game:knife-generic-steel"),
                ["chisel"] = S("game:chisel-steel"), ["axe"] = S("game:axe-felling-steel"), ["shears"] = S("game:shears-iron"),
            };
            var byCode = new Dictionary<string, string>(); var byTag = new Dictionary<string, string>(); var refused = new List<string>();
            foreach (GridRecipe r in sapi.World.GridRecipes)
            {
                if (r?.ResolvedIngredients == null || r.Output?.Code?.Domain != "game") continue;
                foreach (CraftingRecipeIngredient ing in r.ResolvedIngredients)
                {
                    if (ing == null || ing.Consume && !ing.IsTool) continue;
                    string fam = MatterToolRecipePatches.FamilyOf(ing.Code);
                    foreach (var kv in tools)
                    {
                        if (kv.Value == null) continue;
                        bool named = fam == kv.Key;
                        bool tagged = fam == null && vanilla.TryGetValue(kv.Key, out ItemStack v) && v != null && ing.SatisfiesAsIngredient(v, false);
                        if (!named && !tagged) continue;
                        bool ok = ing.SatisfiesAsIngredient(kv.Value, false);
                        if (!ok) refused.Add(kv.Value.Collectible.Code.Path + " for " + r.Output.Code.Path);
                        else if (named) byCode.TryAdd(kv.Key, r.Output.Code.Path);
                        else byTag.TryAdd(kv.Key, r.Output.Code.Path);
                    }
                }
            }
            Check(refused.Count == 0 && byCode.ContainsKey("saw") && byCode.ContainsKey("hammer"),
                $"matter tools count as the game's tools in its recipes (by code: {string.Join(", ", byCode.Select(k => k.Key + "→" + k.Value))}; by tag: {string.Join(", ", byTag.Select(k => k.Key + "→" + k.Value))})"
                + (refused.Count > 0 ? "; refused: " + string.Join(", ", refused.Distinct().Take(6)) : ""));
            // The sledgehammer mines; it isn't the smith's hammer.
            ItemStack sledge = S("vintagee:mattertool-dark-hammer");
            var hammerIng = sapi.World.GridRecipes.Where(r => r?.ResolvedIngredients != null).SelectMany(r => r.ResolvedIngredients)
                .FirstOrDefault(i => i != null && i.IsTool && MatterToolRecipePatches.FamilyOf(i.Code) == "hammer");
            Check(sledge != null && hammerIng != null && !hammerIng.SatisfiesAsIngredient(sledge, false), "the sledgehammer doesn't count as a smithing hammer in recipes");
        }

        void Check190()
        {
            Check(Lang.GetMatchingIfExists("vintagee:item-handbooktext-vintagee:trinket-voidring") != null
                  && Lang.GetMatchingIfExists("vintagee:block-handbooktext-vintagee:energycollector-mk1") != null,
                "long descriptions are in the handbook");
            string tip = Lang.Get("vintagee:itemdesc-trinket-swiftwolfsrendinggale");
            Check(tip.Length < 320 && !tip.Contains("\n"), $"tooltips stay one paragraph with the numbers in ({tip.Length} characters for Swiftwolf's)");

            Block nova = sapi.World.GetBlock(new AssetLocation("vintagee:novaexplosive-catalyst"));
            Check(nova?.EntityClass != null && typeof(Vintagestory.GameContent.IIgnitable).IsAssignableFrom(sapi.ClassRegistry.GetBlockEntity(nova.EntityClass)),
                "nova explosives are lit by torches and firestarters (ignitable)");

            // 1.9.2: divining rods wear out after 10x their EMC value in uses, and look 16/32/128 deep.
            foreach (string rod in new[] { "diviningrodlow", "diviningrodmedium", "diviningrodhigh" })
            {
                Item item = sapi.World.GetItem(new AssetLocation("vintagee:gadget-" + rod));
                double rv = item == null ? 0 : mod.Emc.GetValue(item);
                Check(item != null && rv > 0 && item.Durability == (int)Math.Max(1, Math.Round(rv * 10)),
                    $"{rod}: {item?.Durability} uses for {EmcFormat.Format(rv)} EMC, {ItemEmcGadget.DiviningDepth(rod)} deep");
            }
            Check(ItemEmcGadget.DiviningDepth("diviningrodlow") == 16 && ItemEmcGadget.DiviningDepth("diviningrodmedium") == 32 && ItemEmcGadget.DiviningDepth("diviningrodhigh") == 128,
                "divining rods look 16, 32 and 128 blocks deep");

            // 1.9.1: what the hovered-item box (Ctrl+K) is told about an item's lists.
            CollectibleObject manifold = sapi.World.GetItem(new AssetLocation("game:tobtlocatorpart"));
            EmcItemStatePacket ms = manifold == null ? null : mod.ItemState(manifold, "i:game:tobtlocatorpart");
            Check(ms != null && ms.Free && !ms.Blacklisted && ms.FromFiles.Contains("free"), "the box shows the manifold as a free ingredient from the value files");
            CollectibleObject lore = sapi.World.GetItem(new AssetLocation("game:lore-paper"));
            EmcItemStatePacket ls = lore == null ? null : mod.ItemState(lore, "i:game:lore-paper");
            Check(ls != null && ls.Blacklisted && !ls.Free && ls.Value <= 0, "lore paper is blacklisted, and the box shows it");

            foreach (var (code, slots) in new[] { ("vintagee:alchemicalbagdark-red", 48), ("vintagee:alchemicalbagred-red", 64) })
            {
                Item bag = sapi.World.GetItem(new AssetLocation(code));
                GridRecipe up = sapi.World.GridRecipes.FirstOrDefault(r => r.Output?.Code?.ToString() == code);
                Check(bag?.Attributes?["backpack"]["quantitySlots"].AsInt() == slots && up != null && up.CopyAttributesFrom == "B",
                    $"{code}: {slots} slots, upgraded from the smaller bag keeping its contents");
            }

        }

        /// <summary>
        /// 1.10.0: the /emc dump and /emc loop windows. Each recipe value's explanation adds up to the value; wear,
        /// Klein star EMC and contents show with their numbers; base values name their file or setting; the loop
        /// report and its packet hold together; /emc dump takes a code.
        /// </summary>
        void Check1100()
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null || report.FromCache) report = mod.RecalculateEmc("self-test 1.10.0");
            if (report == null) { Check(false, "the report windows need a mapping"); return; }
            string Text(EmcReport r) => string.Join("\n", r.Lines.Select(l => l.Text));
            string Differs = Lang.Get("vintagee:dumpgui-differs", "").Split('{')[0].Substring(0, 10);

            // Recipes, forwards: the numbers shown add up to the value (only items valued exactly this way, not frozen).
            var forward = report.Via.Where(kv => kv.Value.JointOutputs == null && kv.Value.Output == kv.Key && !report.Frozen.Contains(kv.Key))
                .Select(kv => kv.Key).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).Take(300).ToList();
            int off = 0;
            string firstOff = null;
            foreach (CollectibleObject c in forward)
            {
                EmcReport r = EmcExplain.Dump(mod, sapi, new ItemStack(c), null, false, "en");
                if (Text(r).Contains(Differs)) { off++; firstOff ??= c.Code.ToString(); }
            }
            Line($"  recipe explanations checked: {forward.Count}, not adding up: {off}" + (firstOff == null ? "" : " (first: " + firstOff + ")"));
            Check(forward.Count > 50 && off <= forward.Count / 20, "/emc dump's recipe numbers add up to the value");

            // Worked backwards and shared out: explained the same way.
            CollectibleObject back = report.Via.Where(kv => kv.Value.JointOutputs == null && kv.Value.Output != kv.Key && report.Inferred.Contains(kv.Key)).Select(kv => kv.Key).FirstOrDefault();
            if (back != null)
            {
                string t = Text(EmcExplain.Dump(mod, sapi, new ItemStack(back), null, false, "en"));
                Check(t.Contains("Worked backwards"), "a worked-backwards value says so: " + back.Code);
            }
            CollectibleObject shared = report.Via.Where(kv => kv.Value.JointOutputs != null && kv.Value.Inputs.Count > 0 && kv.Value.Inputs[0].Choices[0] != kv.Key).Select(kv => kv.Key).FirstOrDefault();
            if (shared != null)
            {
                string t = Text(EmcExplain.Dump(mod, sapi, new ItemStack(shared), null, false, "en"));
                Check(t.Contains("Shared out"), "a shared-out value says so: " + shared.Code);
            }

            // Base values name where they come from.
            var bases = report.Origins.Where(kv => kv.Value == "base value").Select(kv => kv.Key).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).Take(60).ToList();
            string unknown = Lang.Get("vintagee:dump-noorigin");
            int named = bases.Count(c => !Text(EmcExplain.Dump(mod, sapi, new ItemStack(c), null, false, "en")).Contains(unknown));
            Check(bases.Count > 10 && named >= bases.Count * 9 / 10, $"base values name their value file or setting ({named} of {bases.Count})");

            // A worn tool: the durability and what it takes off.
            CollectibleObject tool = report.Values.Keys.Where(c => c is Item && c.Durability > 10 && c.Code.Domain == "game").OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (tool != null)
            {
                var st = new ItemStack(tool);
                int max = tool.GetMaxDurability(st);
                st.Attributes.SetInt("durability", max / 2);
                EmcReport r = EmcExplain.Dump(mod, sapi, st, null, true, "en");
                string t = Text(r);
                bool scales = mod.Emc.DurabilityScales;
                Check(t.Contains($"Durability {max / 2}/{max}") && (!scales || t.Contains(EmcFormat.Format(mod.Emc.GetValue(st)) + " EMC each")),
                    $"a half-worn {tool.Code} explains its durability ({max / 2}/{max}) and comes to {EmcFormat.Format(mod.Emc.GetValue(st))}");
            }

            // A Klein star with EMC in it, and a bucket of water counted with its contents.
            Item star = sapi.World.GetItem(new AssetLocation("vintagee:kleinstar-ein"));
            if (star != null)
            {
                var st = new ItemStack(star);
                StoredEmc.Set(st, 1000);
                string t = Text(EmcExplain.Dump(mod, sapi, st, null, true, "en"));
                Check(t.Contains(EmcFormat.Format(1000) + " EMC stored in it"), "a Klein star's stored EMC is listed");
            }
            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item water = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            if (bucket != null && water != null)
            {
                var st = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(water, 1000));
                st.Attributes["contents"] = contents;
                StackDataTable data = mod.Emc.StackData;
                StackDataMode before = data.ModeOf(bucket);
                data.Modes[bucket] = StackDataMode.Full;
                string t;
                try { t = Text(EmcExplain.Dump(mod, sapi, st, null, true, "en")); }
                finally { data.Modes[bucket] = before; }
                Check(t.Contains("[[" + PlayerEmc.KeyOf(water) + "]] x1000"), "a filled bucket in item + contents mode lists its water");
            }

            // Something without a value says why; references resolve; the plain text has no [[markers]].
            CollectibleObject missing = report.Missing.FirstOrDefault(c => report.MissingRecipes.ContainsKey(c));
            if (missing != null)
            {
                EmcReport r = EmcExplain.Dump(mod, sapi, new ItemStack(missing), null, false, "en");
                Check(Text(r).Contains("way(s) to make it"), "an item with no value lists its ways and what each lacks: " + missing.Code);
            }
            EmcReport any = EmcExplain.Dump(mod, sapi, new ItemStack(forward.FirstOrDefault() ?? (CollectibleObject)sapi.World.GetItem(new AssetLocation("game:stick"))), null, false, "en");
            var keys = any.Lines.SelectMany(l => EmcReport.RefToken.Matches(l.Text).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value)).ToList();
            string plainText = any.PlainText(sapi.World);
            Check(keys.Count > 0 && keys.All(k => PlayerEmc.Resolve(sapi.World, k) != null) && !plainText.Contains("[["), $"the dump's {keys.Count} item references resolve, and the file text spells them out");

            // The loops window, and its packet.
            EmcReport loops = EmcExplain.Loops(mod, sapi, "en");
            EmcReportPacket p = loops.ToPacket();
            int headings = loops.Lines.Count(l => l.Style == EmcLineStyle.Heading);
            int expectHeadings = report.Loops.Count + report.SettledLoops.Count + (report.SettledLoops.Count > 0 ? 1 : 0);
            Check(headings == expectHeadings && p.Texts.Length == loops.Lines.Count && p.Blocks.Length == p.Texts.Length && p.Styles.Length == p.Texts.Length,
                $"the loop window has a section per loop ({headings}) and its packet holds every line ({p.Texts.Length})");

            // What the window says, for a few kinds of value (to read in the log).
            foreach (CollectibleObject show in new[] { forward.FirstOrDefault(c => report.Via[c].Inputs.Count > 1), back, shared, bases.FirstOrDefault(), missing })
            {
                if (show == null) continue;
                foreach (string l in EmcExplain.Dump(mod, sapi, new ItemStack(show), null, false, "en").PlainText(sapi.World).Split('\n').Take(40)) Line("  | " + l);
            }
            if (tool != null)
            {
                var worn = new ItemStack(tool);
                worn.Attributes.SetInt("durability", tool.GetMaxDurability(worn) / 2);
                foreach (string l in EmcExplain.Dump(mod, sapi, worn, null, true, "en").PlainText(sapi.World).Split('\n').Take(25)) Line("  | " + l);
            }
            foreach (string l in loops.PlainText(sapi.World).Split('\n').Take(25)) Line("  | " + l);

            // /emc dump <code> from the console.
            string byCode = RunCommand("/emc dump game:stick");
            Check(byCode.Contains("emc-dump-game_stick.txt") && byCode.Contains("Where the plain item's value comes from"), "/emc dump game:stick explains the stick and writes its file");
            string bad = RunCommand("/emc dump game:no-such-thing-at-all");
            Check(bad.Contains("No block or item is called"), "/emc dump with an unknown code says so");
        }

        /// <summary>
        /// 1.11.0: clothes are worth their condition; the philosopher's stone stays in every recipe; containers burned as the
        /// plain item give their contents' EMC too when ProtectContainersWithContents is off; EMC numbers keep two decimals;
        /// thatch has a value; the table only takes what it can burn from hoppers.
        /// </summary>
        /// <summary>
        /// 1.15.0: /emc diagnose builds every kind of report without throwing, says the right thing about a valued item,
        /// a missing one and a mod, and writes each text to ModData/vintagee/diagnose-<action>.txt for reading.
        /// </summary>
        void CheckDiagnose()
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) { Check(false, "diagnose: EMC not mapped"); return; }
            // Sorted by code: the set has no order, and a value-loop item (platinum bits) has no "Next steps" section, so an unlucky pick failed the check.
            CollectibleObject missing = report.Missing.Where(c => c.Code.Domain == "game").OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).FirstOrDefault() ?? report.Missing.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).FirstOrDefault();
            var cases = new List<(string action, string arg)>
            {
                ("", ""), ("mapping", ""), ("mods", ""), ("mod", "game"), ("mod", "nosuchmod"), ("sporadic", ""),
                ("item", "game:ingot-copper"), ("item", "game:creature-trader-female-artisan-cold"), ("item", "game:nosuchitem"),
                ("log", ""), ("patches", ""), ("outliers", ""), ("clean", ""), ("players", ""), ("player", "nobody"), ("bugreport", ""), ("changes", ""),
                ("symptom", ""), ("symptom", "tooltipnoburn"), ("symptom", "aftermod"), ("symptom", "nothing"), ("symptom", "onlyme"), ("client", ""),
                ("watch", ""), ("watch", "60"), ("draftcompat", "game"), ("draftcompat", "nosuchmod"), ("emccraft", ""), ("arcane", ""), ("machines", ""),
                ("trace", "game:ingot-tinbronze"), ("trace", "game:ingot-copper"), ("trace", "hand"), ("trace", "game:hide-pelt-bear-brown-head"), ("trace", "game:hide-pelt-bear-brown-complete"), ("short", ""),
                ("checks", ""), ("profit", ""), ("skipped", ""), ("baseline", ""), ("novalue", ""), ("troubleshoot", ""), ("startup", ""), ("health", ""),
            };
            foreach (var m in report.Missing.Select(c => c.Code.Domain).Distinct().Where(d => d != "game").Take(1)) cases.Add(("draftcompat", m));
            EmcDiagnose.RecordRefusal(null, "novalue", new[] { "test" }, new ItemStack(sapi.World.GetItem(new AssetLocation("game:ingot-copper"))));
            EmcDiagnose.RecordRefusal(null, "unknown", null, null);
            if (missing != null) cases.Add(("item", missing.Code.ToString()));
            foreach (var m in report.Missing.Select(c => c.Code.Domain).Distinct().Where(d => d != "game").Take(2)) cases.Add(("mod", m));
            string dir = VintageEModSystem.DataFolder(sapi);
            foreach (var (action, arg) in cases)
            {
                try
                {
                    EmcReport r = EmcDiagnose.Run(mod, sapi, null, "en", action, arg, null);
                    string text = r.PlainText(sapi.World);
                    Check(r.Lines.Count >= 3 && !text.Contains("vintagee:diag-"), $"diagnose {action} {arg}: {r.Lines.Count} lines, every lang key found");
                    if (action == "item" && arg == "game:ingot-copper") Check(text.Contains("It has a value: 128"), "diagnose: copper ingot has a value of 128 (" + Line1(text, "value") + ")");
                    if (action == "item" && arg.Contains("trader")) Check(text.Contains("blacklisted"), "diagnose: the trader is reported as blacklisted");
                    if (action == "item" && arg == "game:nosuchitem") Check(text.Contains("No block or item has the code"), "diagnose: an unknown code is reported");
                    if (action == "mod" && arg == "nosuchmod") Check(text.Contains("is neither a block/item code nor a loaded mod id") || text.Contains("No mod with the id"), "diagnose: an unknown mod id is reported");
                    if (action == "mod" && arg == "game") Check(text.Contains("of its") && text.Contains("have a value"), "diagnose: the base game's coverage is reported");
                    if (missing != null && action == "item" && arg == missing.Code.ToString()) Check(text.Contains("It has no value."), "diagnose: " + missing.Code + " is reported as having no value");
                    if (action == "trace" && arg == "game:ingot-tinbronze") Check(text.Contains("Copper ingot") && text.Contains("raw materials"), "diagnose: the trace of tin bronze reaches copper (" + Line1(text, "raw materials") + ")");
                    if (action == "checks") Check(text.Contains("Metal tiers") && text.Contains("Skipped recipes") && text.Contains("Values defined twice"), "diagnose: the deeper checks all ran");
                    if (action == "baseline") Check(text.Contains("fresh install") || text.Contains("Your settings change"), "diagnose: the baseline compared (" + Line1(text, "settings") + ")");
                    if (action == "short") Check(text.Contains("In short"), "diagnose: the short form");
                    if (action == "health") Check(text.Contains("Game version") && text.Contains("Join") && text.Contains("Players") && text.Contains("Solver"),
                        "diagnose: the health checks all ran (" + Line1(text, "Game version") + ")");
                    if (action == "startup") Check(text.Contains("Other mods: nothing went wrong") || text.Contains("don't exist") || text.Contains("from other mods") || text.Contains("skipped by the game"),
                        "diagnose: the start-up of other mods was read from the log (" + Line1(text, "mods") + ")");
                    if (action == "watch" && arg == "") Check(text.Contains("2 refusals") && text.Contains("Copper ingot"), "diagnose: the watch lists the recorded refusals (" + Line1(text, "refusals") + ")");
                    if (action == "draftcompat" && arg == "game") Check(text.Contains("nothing to draft"), "diagnose: no draft for the base game");
                    if (action == "clean") Check(text.Contains("base-game value"), "diagnose: the clean mapping compared base-game values (" + Line1(text, "base-game value") + ")");
                    if (action == "patches") Check(text.Contains("patch"), "diagnose: other mods' patches were scanned (" + Line1(text, "patch") + ")");
                    if (action == "bugreport") Check(text.Contains("Mods (") && text.Contains("settings that differ from the defaults") && !text.Split('\n').Any(l => l.TrimStart().StartsWith("\u2192 ")) && text.Split('\n').Length < 400, "diagnose: the bug report has the mod list and the changed settings, no advice, and stays short (" + text.Split('\n').Length + " lines)");
                    if (action == "overview" || action == "") Check(r.Lines.Count(l => l.Style == EmcLineStyle.Check) >= 8, "diagnose: the overview is a list of checks (" + r.Lines.Count(l => l.Style == EmcLineStyle.Check) + ")");
                    if (action == "novalue" || action == "troubleshoot") Check(text.Contains("Step 7") && text.Contains("Step 1") && !text.Contains("vintagee:diag-novalue"), "diagnose: the no-value walkthrough has its seven steps");
                    if (action == "machines") Check(text.Contains("Matter furnaces: from above") && text.Contains("Alchemical Chest") && !text.Contains("vintagee:diag-mach"),
                        "diagnose: the machine report says the furnaces' hopper rules are in place (" + Line1(text, "Matter furnaces") + ")");
                    if (action == "emccraft" || action == "arcane") Check(text.Contains("Arcane Tablet is loaded") && text.Contains("load a crafting grid saved") && !text.Contains("vintagee:diag-emccraft"),
                        "diagnose: the Emc Craft report checks the item and the saved-grid class (" + Line1(text, "Arcane Tablet") + ")");
                    if (action == "symptom" && arg == "") Check(text.Split('\n').Count(l => l.Contains("/emc diagnose symptom ")) >= 10, "diagnose: ten symptoms are offered");
                    if (missing != null && action == "item" && arg == missing.Code.ToString()) Check(text.Contains("Next steps") && text.Contains("mods.vintagestory.at"), "diagnose: next steps point to the Mod DB page");
                    File.WriteAllText(Path.Combine(dir, "diagnose-" + (action.Length == 0 ? "overview" : action) + (arg.Length > 0 ? "-" + arg.Replace(':', '_') : "") + ".txt"), text);
                }
                catch (Exception e) { Check(false, $"diagnose {action} {arg} threw {e}"); }
            }
        }

        static string Line1(string text, string word)
        {
            foreach (string line in text.Split('\n')) if (line.Contains(word)) return line.Trim();
            return "";
        }

        void Check1110()
        {
            CollectibleObject coat = sapi.World.Collectibles.Where(c => c?.Code != null && c.Attributes?["warmth"].AsFloat(0) > 0 && mod.Emc.GetValue(c) > 0)
                .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (coat != null)
            {
                var st = new ItemStack(coat);
                double fresh = mod.Emc.GetValue(st);
                st.Attributes.SetFloat("condition", 0.25f);
                double worn = mod.Emc.GetValue(st);
                Check(Math.Abs(worn - fresh * 0.25) < 1e-6 * Math.Max(1, fresh), $"{coat.Code} at 25% condition is worth a quarter ({EmcFormat.Format(worn)} of {EmcFormat.Format(fresh)})");
            }
            else Check(false, "found clothing with warmth and a value");

            // Once loaded, a grid recipe keeps its ingredients in ResolvedIngredients (Ingredients is emptied).
            var stoneUses = sapi.World.GridRecipes.SelectMany(r => (r.ResolvedIngredients ?? new CraftingRecipeIngredient[0]).Concat(r.Ingredients?.Values ?? Enumerable.Empty<CraftingRecipeIngredient>())
                    .Where(i => i != null).Select(i => (r, i)))
                .Where(t => t.i.Code?.ToString() == "vintagee:philosophersstone" || t.i.ResolvedItemStack?.Collectible?.Code?.ToString() == "vintagee:philosophersstone").ToList();
            Check(stoneUses.Count > 5 && stoneUses.All(t => !t.i.Consume) && stoneUses.Any(t => t.r.Output?.Code?.ToString() == "vintagee:transmutationtable"),
                $"the philosopher's stone stays in all {stoneUses.Count} recipes that use it, the table's included");

            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item waterItem = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            if (bucket != null && waterItem != null)
            {
                var st = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(waterItem, 1000));
                st.Attributes["contents"] = contents;
                bool before = mod.Config.ProtectContainersWithContents;
                StackDataMode modeBefore = mod.Emc.StackData.ModeOf(bucket);
                mod.Emc.StackData.Modes[bucket] = StackDataMode.Item; // the protection is about item-only containers
                double plain = mod.Emc.GetValue(st), inside = mod.Emc.ContentsValue(st);
                mod.Config.ProtectContainersWithContents = true;
                double guarded = mod.BurnValue(st);
                mod.Config.ProtectContainersWithContents = false;
                double open = mod.BurnValue(st);
                mod.Config.ProtectContainersWithContents = before;
                mod.Emc.StackData.Modes[bucket] = modeBefore;
                Check(Math.Abs(guarded - plain) < 1e-6 && Math.Abs(open - (plain + inside)) < 1e-6 && inside > 0,
                    $"a bucket of water burns for {EmcFormat.Format(guarded)} with containers protected, {EmcFormat.Format(open)} (with the water) without");
            }

            Check(EmcFormat.Format(1234.567) == "1,234.56" && EmcFormat.Format(99.999) == "99.99" && EmcFormat.Format(1.15) == "1.15" && EmcFormat.Format(20480) == "20,480",
                $"EMC numbers keep two decimals, cut off: {EmcFormat.Format(1234.567)}, {EmcFormat.Format(99.999)}, {EmcFormat.Format(1.15)}, {EmcFormat.Format(20480)}");

            Item thatch = sapi.World.GetItem(new AssetLocation("game:thatch"));
            Check(thatch == null || mod.Emc.GetValue(thatch) > 0, "thatch has a value: " + (thatch == null ? "no thatch item" : EmcFormat.Format(mod.Emc.GetValue(thatch))));

            Item darkMatter = sapi.World.GetItem(new AssetLocation("vintagee:matter-dark"));
            string burnErr = darkMatter == null ? "missing" : mod.Transmutation.PlanBurn(null, new ItemStack(darkMatter, 3), out TransmutationService.BurnPlan dmPlan, out _);
            Check(burnErr == null, "dark matter can be burned and learned: " + (burnErr ?? "yes"));
        }

        /// <summary>1.21.1: fixes from the bug check of 1.21.0.</summary>
        /// <summary>The 1.21.1 speed-ups: each has to give the same answers as the slow way it replaces.</summary>
        void CheckSpeedups()
        {
            // Players' EMC and knowledge moved from the watched attributes to the entity's own, sent to their owner alone.
            var watched = new TreeAttribute();
            var own = new TreeAttribute();
            watched.SetDouble(PlayerEmc.EmcKey, 50);
            watched.SetStringArray(PlayerEmc.KnownKey, new[] { "i:game:stick", "b:game:rock-granite" });
            bool moved = PlayerEmc.MigrateTree(watched, own);
            string[] ownKnown = own.GetStringArray(PlayerEmc.KnownKey) ?? Array.Empty<string>();
            Check(moved && own.GetDouble(PlayerEmc.EmcKey) == 50 && ownKnown.SequenceEqual(new[] { "i:game:stick", "b:game:rock-granite" })
                && !watched.HasAttribute(PlayerEmc.EmcKey) && !watched.HasAttribute(PlayerEmc.KnownKey),
                $"a save from before 1.21.1 keeps its EMC and knowledge when they move out of the synced attributes ({own.GetDouble(PlayerEmc.EmcKey)} EMC, {ownKnown.Length} known)");
            Check(!PlayerEmc.MigrateTree(watched, own) && own.GetDouble(PlayerEmc.EmcKey) == 50 && (own.GetStringArray(PlayerEmc.KnownKey)?.Length ?? 0) == 2,
                "moving them a second time changes nothing");
            // A world that went back to an older version for a while has both: nothing of either is lost.
            watched.SetDouble(PlayerEmc.EmcKey, 5);
            watched.SetStringArray(PlayerEmc.KnownKey, new[] { "i:game:stick", "i:game:flint" });
            PlayerEmc.MigrateTree(watched, own);
            ownKnown = own.GetStringArray(PlayerEmc.KnownKey) ?? Array.Empty<string>();
            Check(own.GetDouble(PlayerEmc.EmcKey) == 55 && ownKnown.SequenceEqual(new[] { "i:game:stick", "b:game:rock-granite", "i:game:flint" }),
                $"EMC and knowledge saved by an older version in between are added, not dropped ({own.GetDouble(PlayerEmc.EmcKey)} EMC, {string.Join(" ", ownKnown)})");
            var brokenOld = new TreeAttribute();
            brokenOld.SetDouble(PlayerEmc.EmcKey, double.NaN);
            var fresh = new TreeAttribute();
            PlayerEmc.MigrateTree(brokenOld, fresh);
            Check(fresh.GetDouble(PlayerEmc.EmcKey, -1) == 0, "a broken old balance (not a number) moves over as 0");

            // What a player's game is sent: single keys while that is enough, the whole list otherwise.
            var changes = new PlayerEmc.PendingChanges { Emc = true };
            changes.Note("i:game:flint", true);
            changes.Note("i:game:stick", false);
            PlayerEmcPacket sent = changes.Take(12.5, new[] { "b:game:rock-granite", "i:game:flint" });
            Check(sent.HasEmc && sent.Emc == 12.5 && !sent.HasKnown && sent.Known == null && sent.Learned?.Length == 1 && sent.Learned[0] == "i:game:flint"
                && sent.Forgotten?.Length == 1 && sent.Forgotten[0] == "i:game:stick" && !changes.Any,
                "a player's game is sent their new balance and just the keys learned and forgotten");
            changes.Note("i:game:flint", true);
            changes.Note("i:game:flint", false);
            sent = changes.Take(0, new[] { "b:game:rock-granite" });
            Check(!sent.HasEmc && sent.HasKnown && sent.Known.Length == 1 && sent.Learned == null && sent.Forgotten == null,
                "a key learned and forgotten in the same moment sends the whole list instead (so the order can't drift)");

            // The game's side: the same array while nothing changes (windows redraw their list when it is a new one).
            string[] mine = { "i:game:stick", "b:game:rock-granite" };
            string[] after = PlayerEmc.ApplyKnown(mine, new PlayerEmcPacket { HasEmc = true, Emc = 3 });
            bool sameArray = ReferenceEquals(after, mine);
            after = PlayerEmc.ApplyKnown(mine, new PlayerEmcPacket { Learned = new[] { "i:game:flint", "i:game:stick" }, Forgotten = new[] { "b:game:rock-granite" } });
            bool delta = !ReferenceEquals(after, mine) && after.SequenceEqual(new[] { "i:game:stick", "i:game:flint" }) && mine.Length == 2;
            string[] whole = { "i:game:clay-blue" };
            Check(sameArray && delta && ReferenceEquals(PlayerEmc.ApplyKnown(mine, new PlayerEmcPacket { HasKnown = true, Known = whole }), whole)
                && ReferenceEquals(PlayerEmc.ApplyKnown(mine, new PlayerEmcPacket { Learned = new[] { "i:game:stick" } }), mine),
                $"a game applies what it is sent: balance only keeps the list, keys are added and taken out ({string.Join(" ", after)}), a whole list replaces it");
            Check(PlayerEmc.GetEmc(null) == 0 && PlayerEmc.GetKnown(null).Length == 0 && !PlayerEmc.Learn(null, "i:game:stick") && PlayerEmc.TakeChanges(null) == null,
                "no player: no EMC, no knowledge, nothing to send");

            // The table's packet is kept until the table changes.
            EmcTablePacket first = mod.Emc.ToPacket();
            bool kept = ReferenceEquals(first, mod.Emc.ToPacket());
            int revision = mod.Emc.Revision;
            double costBefore = mod.Config.TrinketCostMultiplier;
            mod.Emc.TrinketCostMultiplier = costBefore + 1;
            EmcTablePacket second = mod.Emc.ToPacket();
            mod.Emc.TrinketCostMultiplier = costBefore;
            EmcTablePacket third = mod.Emc.ToPacket(), built = mod.Emc.BuildPacket();
            Check(kept && mod.Emc.Revision > revision && !ReferenceEquals(first, second) && Math.Abs(second.TrinketCostMultiplier - costBefore - 1) < 1e-9
                && Math.Abs(third.TrinketCostMultiplier - costBefore) < 1e-9 && third.ItemIds.SequenceEqual(built.ItemIds) && third.ItemValues.SequenceEqual(built.ItemValues)
                && third.BlockIds.SequenceEqual(built.BlockIds) && third.BlockValues.SequenceEqual(built.BlockValues) && (third.DataKeys ?? new string[0]).SequenceEqual(built.DataKeys ?? new string[0]),
                "the table sent to players is kept while nothing changes, made again as soon as something does, and is the same as one built from scratch");

            // The magnet's and gem's filter by items gives the answers the filter by keys gives.
            Item tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
            CollectibleObject graniteRock = sapi.World.GetBlock(new AssetLocation("game:rock-granite")), andesiteRock = sapi.World.GetBlock(new AssetLocation("game:rock-andesite"));
            CollectibleObject stickItem = sapi.World.GetItem(new AssetLocation("game:stick"));
            if (tabletItem != null && graniteRock != null && andesiteRock != null && stickItem != null)
            {
                var filterTablet = new ItemStack(tabletItem);
                bool agree = TabletMagnet.Passes(filterTablet, graniteRock) == TabletMagnet.Passes(filterTablet, graniteRock, sapi.World);
                ((TreeAttribute)filterTablet.Attributes).SetStringArray(TabletMagnet.FilterKey, new[] { PlayerEmc.KeyOf(graniteRock), "", "i:game:nosuchitem-gone", PlayerEmc.KeyOf(stickItem) });
                foreach (bool exclude in new[] { false, true })
                {
                    filterTablet.Attributes.SetBool(TabletMagnet.ExcludeKey, exclude);
                    filterTablet.Attributes.SetBool(GemFilter.OnlyKey, exclude);
                    foreach (CollectibleObject c in new[] { graniteRock, andesiteRock, stickItem })
                    {
                        agree &= TabletMagnet.Passes(filterTablet, c) == TabletMagnet.Passes(filterTablet, c, sapi.World);
                        agree &= GemFilter.Passes(filterTablet, c) == GemFilter.Passes(filterTablet, c, sapi.World);
                    }
                }
                // A changed filter is a new stored list: the items found for the old one must not answer for it.
                ((TreeAttribute)filterTablet.Attributes).SetStringArray(TabletMagnet.FilterKey, new[] { PlayerEmc.KeyOf(andesiteRock) });
                filterTablet.Attributes.SetBool(TabletMagnet.ExcludeKey, false);
                agree &= TabletMagnet.Passes(filterTablet, andesiteRock, sapi.World) && !TabletMagnet.Passes(filterTablet, graniteRock, sapi.World);
                Check(agree && TabletMagnet.FilterItems(filterTablet, sapi.World).Length == 1, "the magnet's and gem's filter by items gives the same answers as by keys, and follows a changed filter");
            }

            // Hostile creatures by kind: the remembered answer is the list's answer, and a changed list is asked again.
            EntityProperties wolfType = sapi.World.EntityTypes.FirstOrDefault(t => t?.Code != null && t.Code.Domain == "game" && t.Code.Path.StartsWith("wolf-", StringComparison.Ordinal));
            EntityProperties henType = sapi.World.EntityTypes.FirstOrDefault(t => t?.Code != null && t.Code.Domain == "game" && t.Code.Path.StartsWith("chicken-", StringComparison.Ordinal));
            if (wolfType != null && henType != null && mod.Trinkets != null)
            {
                Entity wolf = sapi.World.ClassRegistry.CreateEntity(wolfType), hen = sapi.World.ClassRegistry.CreateEntity(henType);
                wolf.Code = wolfType.Code;
                hen.Code = henType.Code;
                bool wolfListed = mod.Trinkets.MatchesHostileList(wolf.Code, out _), henListed = mod.Trinkets.MatchesHostileList(hen.Code, out _);
                bool sameAnswers = mod.Trinkets.IsHostile(wolf) == wolfListed && mod.Trinkets.IsHostile(hen) == henListed && mod.Trinkets.IsHostile(wolf) == wolfListed;
                var listBefore = new List<string>(mod.Config.HostileCreatures);
                mod.Config.HostileCreatures = new List<string> { "game:chicken-*" };
                mod.Trinkets.ResetHostile();
                bool followsList = mod.Trinkets.IsHostile(hen) && !mod.Trinkets.IsHostile(wolf);
                mod.Config.HostileCreatures = listBefore;
                mod.Trinkets.ResetHostile();
                Check(sameAnswers && followsList && mod.Trinkets.IsHostile(wolf) == wolfListed && mod.Trinkets.IsHostile(hen) == henListed,
                    $"hostile creatures are remembered by kind and asked again when the list changes (wolf {wolfListed}, chicken {henListed})");
            }

            // Emc Craft: remembering which learned items fit an ingredient gives the plans the full search gives.
            string[] knownAll = mod.Emc.All.Keys.Where(c => c?.Code != null && CollectibleIndex.IsListed(c) && mod.Emc.CanTransmute(c)).Take(1500).Select(PlayerEmc.KeyOf).ToArray();
            List<GridRecipe> recipes = sapi.World.GridRecipes.Where(r => r != null && r.Enabled && r.ResolvedIngredients != null && r.Output?.ResolvedItemStack != null).ToList();
            int every = Math.Max(1, recipes.Count / 60), differing = 0, compared = 0;
            try
            {
                for (int i = 0; i < recipes.Count; i += every)
                {
                    mod.EmcCrafting.RememberMatches = false;
                    EmcCraft.Plan slow = mod.EmcCrafting.MakePlan(recipes[i], knownAll, null);
                    mod.EmcCrafting.RememberMatches = true;
                    EmcCraft.Plan quick = mod.EmcCrafting.MakePlan(recipes[i], knownAll, null), again = mod.EmcCrafting.MakePlan(recipes[i], knownAll, null);
                    compared++;
                    if (!SamePlan(slow, quick) || !SamePlan(slow, again)) differing++;
                }
            }
            finally { mod.EmcCrafting.RememberMatches = true; }
            Check(compared > 0 && differing == 0, $"Emc Craft plans the same fill whether it remembers which learned items fit an ingredient or searches them all ({compared} recipes, {differing} differ)");

            // A trinket's effects into the tick's buffer are the ones the list gives.
            bool sameEffects = true;
            var effectBuffer = new string[4];
            foreach (string type in new[] { "ringofarcana", "voidring", "zeroring", "repairtalisman" })
            {
                if (!(sapi.World.GetItem(new AssetLocation("vintagee:trinket-" + type)) is ItemEmcTrinket trinket)) continue;
                var trinketStack = new ItemStack(trinket);
                int n = TrinketEffects.EffectsInto(trinket, trinketStack, effectBuffer);
                sameEffects &= TrinketEffects.EffectsOf(trinket, trinketStack).SequenceEqual(effectBuffer.Take(n)) && trinket.Kind == type;
            }
            Check(sameEffects, "a trinket's effects in the tick are the ones its list gives, and its kind is its type");
        }

        static bool SamePlan(EmcCraft.Plan a, EmcCraft.Plan b)
        {
            if (a == null || b == null) return a == b;
            if (Math.Abs(a.Cost - b.Cost) > 1e-9 || Math.Abs(a.NetCost - b.NetCost) > 1e-9 || !a.Missing.SequenceEqual(b.Missing) || !a.NotMakeable.SequenceEqual(b.NotMakeable)) return false;
            for (int i = 0; i < a.Cells.Length; i++)
            {
                if (a.Cells[i]?.Collectible != b.Cells[i]?.Collectible || (a.Cells[i]?.StackSize ?? 0) != (b.Cells[i]?.StackSize ?? 0)) return false;
            }
            return true;
        }

        void Check1211()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // Settings a hand edit can break: NaN and infinities go back to something usable.
            var broken = new VintageEConfig
            {
                CollectorDarkFactor = double.NaN, FallbackValue = double.NegativeInfinity, TableUseRange = double.NaN,
                RepairTalismanClothingPercentPerMinute = double.NaN, RepairTalismanClothingMinCondition = double.NaN, RepairTalismanClothingMaxCondition = double.PositiveInfinity
            };
            broken.Normalize();
            Check(broken.CollectorDarkFactor == 0 && broken.FallbackValue == 0 && broken.TableUseRange == 8 && broken.RepairTalismanClothingPercentPerMinute == 1
                && broken.RepairTalismanClothingMinCondition == 0 && broken.RepairTalismanClothingMaxCondition == 100,
                $"NaN and infinite settings go back to usable values (dark factor {broken.CollectorDarkFactor}, fallback {broken.FallbackValue}, table range {broken.TableUseRange}, clothing {broken.RepairTalismanClothingPercentPerMinute}/{broken.RepairTalismanClothingMinCondition}/{broken.RepairTalismanClothingMaxCondition})");

            // Machines neither take a NaN nor keep one from a save.
            double relayTook = new BEAntimatterRelay().AcceptEmc(double.NaN), condenserTook = new BEEnergyCondenser().AcceptEmc(double.PositiveInfinity);
            Check(relayTook == 0 && condenserTook == 0 && EmcTransfer.Clean(double.NaN) == 0 && EmcTransfer.Clean(-5) == 0 && EmcTransfer.Clean(12.5) == 12.5,
                $"machines refuse EMC that isn't a number (relay took {relayTook}, condenser {condenserTook}) and read a broken store back as 0");

            // /emc configure: the multipliers tooltips quote reach players' games at once (they travel with the table).
            double cost = mod.Config.TrinketCostMultiplier, speed = mod.Config.MachineSpeedMultiplier, slow = mod.Config.WatchSlowDown;
            ConfigureCommand.SetFromText(mod, "TrinketCostMultiplier", "2.5", "en");
            ConfigureCommand.SetFromText(mod, "MachineSpeedMultiplier", "3", "en");
            ConfigureCommand.SetFromText(mod, "WatchSlowDown", "4", "en");
            EmcTablePacket sent = mod.Emc.ToPacket();
            bool mirrored = Math.Abs(sent.TrinketCostMultiplier - 2.5) < 1e-9 && Math.Abs(sent.MachineSpeedMultiplier - 3) < 1e-9 && Math.Abs(sent.WatchSlowDown - 4) < 1e-9;
            ConfigureCommand.SetFromText(mod, "TrinketCostMultiplier", cost.ToString(inv), "en");
            ConfigureCommand.SetFromText(mod, "MachineSpeedMultiplier", speed.ToString(inv), "en");
            ConfigureCommand.SetFromText(mod, "WatchSlowDown", slow.ToString(inv), "en");
            sent = mod.Emc.ToPacket();
            Check(mirrored && Math.Abs(sent.TrinketCostMultiplier - cost) < 1e-9 && Math.Abs(sent.MachineSpeedMultiplier - speed) < 1e-9 && Math.Abs(sent.WatchSlowDown - slow) < 1e-9,
                $"/emc configure sends changed cost, machine speed and watch settings to players with the table (changed: {mirrored})");

            // CraftingMethods is one of three words; anything else is refused instead of quietly becoming Both.
            string methods = mod.Config.CraftingMethods;
            string refusedWord = RunCommand("/emc configure CraftingMethods banana");
            string restart = RunCommand("/emc configure CraftingMethods " + methods);
            Check(ConfigureCommand.KindOf(ConfigureCommand.Field("CraftingMethods")) == "choice" && refusedWord.Contains("Error") && mod.Config.CraftingMethods == methods && restart.Contains("next time the server starts"),
                $"CraftingMethods takes only its three words and says it needs a restart ({refusedWord.Replace("\n", " | ")} / {restart.Replace("\n", " | ")})");
        }

        /// <summary>1.10.1: the ring's wind only plays on the ring's own flight, not another mod's (an angel belt).</summary>
        void Check1101()
        {
            bool ring = VintageEModSystem.PlaysRingWind(true, true, EnumGameMode.Survival, false, true);
            bool belt = VintageEModSystem.PlaysRingWind(true, true, EnumGameMode.Survival, false, false);
            bool soundsOff = VintageEModSystem.PlaysRingWind(false, true, EnumGameMode.Survival, false, true);
            bool landed = VintageEModSystem.PlaysRingWind(true, true, EnumGameMode.Survival, true, true);
            Check(ring && !belt && !soundsOff && !landed, $"the ring's wind plays on ring flight ({ring}), not on another mod's flight ({belt}), with sounds off ({soundsOff}) or on the ground ({landed})");
        }

        /// <summary>
        /// Evertide Amulet on a pedestal: rain, then the game's weather back. Run last, so the rain can't put out the
        /// furnaces being tested.
        /// </summary>
        void CheckPedestalWeather()
        {
            TrinketEffects fx = mod.Trinkets;
            ModSystem weather = sapi.ModLoader.Systems.FirstOrDefault(m => m.GetType().Name == "WeatherSystemServer");
            var prop = weather?.GetType().GetProperty("OverridePrecipitation");
            if (fx != null && prop != null)
            {
                object before = prop.GetValue(weather);
                var at = new BlockPos(0, 1, 0);
                fx.SetPedestalWeather(at, "evertideamulet");
                object raining = prop.GetValue(weather);
                fx.SetPedestalWeather(at, null);
                object after = prop.GetValue(weather);
                Check(raining is float f && f >= 1 && after == null, $"Evertide Amulet on a pedestal sets rain ({raining ?? "null"}) and taking it off gives the weather back ({after ?? "null"}, was {before ?? "null"})");
            }
            else Check(false, "found the game's weather system");
        }

        void CheckProjectELook()
        {
            Line("Look: players get " + mod.ActiveLook + " unless they pick their own; ProjectE sounds " + (mod.Config.ProjectESounds ? "on" : "off"));
            Check(sapi.World.Config.GetString(VintageEModSystem.WorldLookKey, null) == ProjectELook.NormalizeLook(mod.Config.Look),
                "players are told the server's default look through the world settings: " + sapi.World.Config.GetString(VintageEModSystem.WorldLookKey, null));
            Check(sapi.World.GetItem(new AssetLocation("vintagee:trinket-zeroring"))?.Shape == null && mod.LookResult == null,
                "the server sends Vintage-E's own blocks and items (each player's game applies their look)");
            foreach (string s in new[] { PESounds.Charge, PESounds.Uncharge, PESounds.Heal, PESounds.Destruct, PESounds.Power, PESounds.Transmute, PESounds.WaterMagic, PESounds.WindMagic })
            {
                Check(sapi.Assets.Exists(PESounds.Location(s).WithPathAppendixOnce(".ogg")), "ProjectE sound " + s + " is there");
            }
            Check(PESounds.On(sapi.World) == mod.Config.ProjectESounds, "ProjectE sounds follow the setting");

            bool Tex(string path) => sapi.Assets.Exists(new AssetLocation(path).WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            Check(ProjectELook.NormalizeLook("original") == "Original" && ProjectELook.NormalizeLook("NEW") == "New" && ProjectELook.NormalizeLook("middle") == "Middle" && ProjectELook.NormalizeLook("auto") == "New" && new VintageEConfig().Look == "New",
                "the Look setting accepts its three looks, and New is the default");

            // Both looks' maps, whichever is in use: every texture and model they name is there (the game's own textures
            // can't be seen by a dedicated server; the server test checks those against the game client).
            foreach (string other in new[] { ProjectELook.LookOriginal, ProjectELook.LookNew })
            {
                ProjectELook.Result dry = ProjectELook.Apply(sapi, sapi.Logger, other, true);
                Check(dry.MissingFiles.Count == 0, $"{other} look: every texture it names is there" + (dry.MissingFiles.Count == 0 ? "" : ": missing " + string.Join(", ", dry.MissingFiles)));
                Check(dry.MissingShapes.Count == 0, $"{other} look: every model it names is there" + (dry.MissingShapes.Count == 0 ? "" : ": missing " + string.Join(", ", dry.MissingShapes)));
                if (other == ProjectELook.LookNew) Check(dry.Models >= 50, $"New look: {dry.Models} blocks and items get a model");
            }

            // Players' games apply their look as the game loads shapes: the methods patched for that are there, with the
            // parameter names the patch binds to (the same game library runs the server).
            Type stm = HarmonyLib.AccessTools.TypeByName("Vintagestory.Client.NoObf.ShapeTesselatorManager");
            foreach (var (name, param) in new[] { ("LoadBlockShapes", "blocks"), ("LoadItemShapes", "items") })
            {
                var m = stm == null ? null : HarmonyLib.AccessTools.GetDeclaredMethods(stm).FirstOrDefault(x => x.Name == name);
                Check(m != null && m.GetParameters().Any(pi => pi.Name == param), $"the game's {name}({param}) is there for the look to hook into");
            }
            Check(stm != null && HarmonyLib.AccessTools.Field(stm, "game") != null, "the shape loader tells a player's game from a server (its game field)");

            CheckNewLook();

            // Original, as a player's game applies it (here to copies made at start-up).
            ProjectELook.Result r = mod.TestLooks.TryGetValue(ProjectELook.LookOriginal, out var ro) ? ro : null;
            Check(r != null && r.Textures > 60, $"Original look: ProjectE textures swapped ({r?.Textures ?? 0} textures, {r?.Collectibles ?? 0} blocks/items, {r?.Entities ?? 0} entity types)");
            if (r == null) return;
            Check(r.MissingFiles.Count == 0, "every ProjectE texture in the swap list is there" + (r.MissingFiles.Count == 0 ? "" : ": missing " + string.Join(", ", r.MissingFiles)));
            // A dedicated server frees block/item textures once they're sent to players, so check what the swap recorded.
            string Got(string key) => r.Swapped.TryGetValue(key, out string v) ? v : null;
            void Expect(string key, string want, string what) => Check(Got(key) == want, what + ": " + (Got(key) ?? "unchanged"));

            Expect("vintagee:trinket-zeroring#all", "vintagee:projecte/item/rings/zero_off", "Zero Ring uses ProjectE's texture");
            Expect("vintagee:energycollector-mk2#up", "vintagee:projecte/block/collectors/top_2", "collector MK2 top is ProjectE's");
            Expect("vintagee:energycollector-mk2#north", "vintagee:projecte/block/collectors/front", "collector front is ProjectE's");
            Expect("vintagee:energycollector-mk2#east", "vintagee:projecte/block/collectors/other", "collector sides are ProjectE's plain face");
            Expect("vintagee:energycollector-mk2#down", "vintagee:projecte/block/collectors/other", "collector underside is ProjectE's plain face");
            Expect("vintagee:energycondenser-mk2#down", "vintagee:projecte/derived/condenser_mk2-bottom", "condenser MK2 underside is cut from ProjectE's chest texture");
            Expect("vintagee:energycondenser-mk1#up", "vintagee:projecte/derived/condenser_mk1-top", "condenser MK1 top is cut from ProjectE's chest texture");
            Expect("vintagee:transmutationtable#top", "vintagee:projecte/block/transmutation_stone/top", "transmutation table top is ProjectE's");
            Expect("vintagee:interdictiontorch#glow", "vintagee:projecte/derived/interdiction_torch-glow", "interdiction torch flame is ProjectE's");
            Expect("vintagee:alchemicalbag-red#sprite", "vintagee:projecte/item/alchemy_bags/red", "red alchemical bag is ProjectE's flat bag");
            Item bag = r.Objects?.GetValueOrDefault("vintagee:alchemicalbag-red") as Item;
            Check(bag != null && bag.Shape == null, "red alchemical bag has no sack model");
            string pickHead = r.Swapped.Where(kv => kv.Key.StartsWith("vintagee:") && kv.Key.Contains("dark") && kv.Key.Contains("pickaxe#")).Select(kv => kv.Value).FirstOrDefault();
            Check(pickHead == "vintagee:projecte/block/dark_matter_block", "dark matter pickaxe head is ProjectE's dark matter: " + pickHead);
            Expect("vintagee:projectile-fire#orb", "vintagee:projecte/entity/fireball", "fireball uses ProjectE's sprite");
            Expect("vintagee:projectile-fire#shape", "vintagee:entity/projectile-sprite", "fireball is drawn whole");
            Check(Got("vintagee:projectile-arrow#shape") == null && Got("vintagee:projectile-arrow#orb") == null, "smite arrows (no ProjectE texture) keep their own look");
            Check(Got("vintagee:kleinstar-omega#all") == null, "Klein Stars (already ProjectE's art) are left alone");

            // The looks rings switch to while rendering (worked out on the client from the texture it received).
            AssetLocation Look(Item item, int mode, bool on)
            {
                var stack = new ItemStack(item);
                stack.Attributes.SetInt(ItemEmcTrinket.ModeKey, mode);
                if (on) stack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, true);
                string tex = Got(item?.Code + "#all");
                return tex == null ? null : ProjectELook.TrinketLook(item as ItemEmcTrinket, stack, new AssetLocation(tex));
            }
            Item zero = sapi.World.GetItem(new AssetLocation("vintagee:trinket-zeroring"));
            Item arcana = sapi.World.GetItem(new AssetLocation("vintagee:trinket-ringofarcana"));
            Item gale = sapi.World.GetItem(new AssetLocation("vintagee:trinket-swiftwolfsrendinggale"));
            Check(Look(zero, 0, false) == null, "Zero Ring switched off keeps its texture");
            AssetLocation look = Look(zero, 0, true);
            Check(look?.ToString() == "vintagee:projecte/item/rings/zero_on" && Tex(look.ToString()), "Zero Ring switched on lights up: " + look);
            look = Look(arcana, 2, true);
            Check(look?.ToString() == "vintagee:projecte/item/rings/arcana_2_on" && Tex(look.ToString()), "Ring of Arcana shows its mode: " + look);
            look = Look(gale, 2, true); // shield only
            Check(look?.ToString() == "vintagee:projecte/item/rings/swrg_on3" && Tex(look.ToString()), "Swiftwolf's shield mode shows ProjectE's shield look: " + look);
            int lit = 0;
            foreach (Item t in sapi.World.Collectibles.OfType<ItemEmcTrinket>())
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    look = Look(t, mode, true);
                    if (look == null) continue;
                    lit++;
                    if (!Tex(look.ToString())) Check(false, $"{t.Code} switched-on texture {look} is there");
                }
            }
            Check(lit > 20, $"switched-on looks found for the rings and stones ({lit})");
        }

        /// <summary>Look = New: blocks and items really got their models, textures and transforms.</summary>
        void CheckNewLook()
        {
            ProjectELook.Result r = mod.TestLooks.TryGetValue(ProjectELook.LookNew, out var rn) ? rn : null;
            Check(r != null && r.Models >= 50, $"New look applied (to copies) ({r?.Models ?? 0} models, {r?.Textures ?? 0} textures)");
            if (r == null) return;
            Check(r.Problems.Count == 0, "New look: nothing failed to apply" + (r.Problems.Count == 0 ? "" : ": " + string.Join("; ", r.Problems.Take(5))));
            string Got(string key) => r.Swapped.TryGetValue(key, out string v) ? v : null;
            void Expect(string key, string want, string what) => Check(Got(key) == want, what + ": " + (Got(key) ?? "unchanged"));

            CollectibleObject Obj(string code) => r.Objects?.GetValueOrDefault(code);
            Item zero = Obj("vintagee:trinket-zeroring") as Item;
            Check(zero?.Shape?.Base?.ToString() == "vintagee:newlook/item/ring", "Zero Ring has the ring model: " + zero?.Shape?.Base);
            Expect("vintagee:trinket-zeroring#gem", "game:block/stone/gem/aquamarine", "Zero Ring's gem is the game's aquamarine");
            Expect("vintagee:trinket-zeroring#metal", "game:block/metal/plate/silver", "Zero Ring's band is the game's silver");
            Check(Got("vintagee:trinket-zeroring#keptall") == "yes", "Zero Ring keeps its own texture for wearing");
            Check(Got("vintagee:trinket-zeroring#guirotx") == "150", "Zero Ring is turned over like the game's gems in the inventory: " + Got("vintagee:trinket-zeroring#guirotx"));
            Item star = Obj("vintagee:kleinstar-omega") as Item;
            Check(star?.Shape?.Base?.ToString() == "vintagee:newlook/item/star", "Klein Star Omega has the star model");
            Block collector = Obj("vintagee:energycollector-mk3") as Block;
            Check(collector?.DrawType == Vintagestory.API.Client.EnumDrawType.JSON && collector.Shape?.Base?.ToString() == "vintagee:newlook/block/energycollector",
                "collector MK3 is drawn from its model: " + collector?.DrawType + " " + collector?.Shape?.Base);
            Expect("vintagee:energycollector-mk3#glow", "game:block/stone/gem/ruby", "collector MK3's crystal is a ruby");
            Block table = Obj("vintagee:transmutationtable") as Block;
            Check(table?.Shape?.Base?.ToString() == "vintagee:newlook/block/transmutationtable", "transmutation table has the new model");
            Block furnace = Obj("vintagee:furnace-red-lit") as Block;
            Check(furnace?.Shape?.Base?.ToString() == "vintagee:newlook/block/furnace-lit", "lit red matter furnace has the lit fire pit model");
            Expect("vintagee:furnace-red-lit#body", "vintagee:newlook/material/redmatter", "red matter furnace burns red matter");
            Block mblock = Obj("vintagee:matterblock-dark") as Block;
            Check(mblock?.DrawType == Vintagestory.API.Client.EnumDrawType.Cube, "matter blocks stay cubes");
            string pickHead = r.Swapped.Where(kv => kv.Key.StartsWith("vintagee:") && kv.Key.Contains("dark") && kv.Key.Contains("pickaxe#")).Select(kv => kv.Value).FirstOrDefault();
            Check(pickHead == "vintagee:newlook/material/darkmatter", "dark matter pickaxe head uses the new dark matter: " + pickHead);
            Check(Got("vintagee:alchemicalbag-red#linen") == "game:block/cloth/linen/red", "red alchemical bag is the game's red linen: " + Got("vintagee:alchemicalbag-red#linen"));
            Check(Got("vintagee:projectile-fire#shape") == null, "projectiles keep Vintage-E's look");
            // Outlines fit the models; collision is the same in every look.
            Block torch = Obj("vintagee:interdictiontorch") as Block;
            Block realTorch = sapi.World.GetBlock(new AssetLocation("vintagee:interdictiontorch"));
            Check(torch?.SelectionBoxes?.Length == 1 && Math.Abs(torch.SelectionBoxes[0].Y2 - 14.75f / 16) < 0.01f && Math.Abs(torch.SelectionBoxes[0].X1 - 4.5f / 16) < 0.01f,
                "the interdiction torch's outline fits its model: " + torch?.SelectionBoxes?.FirstOrDefault());
            Check(torch?.CollisionBoxes?.FirstOrDefault()?.Y2 == realTorch?.CollisionBoxes?.FirstOrDefault()?.Y2, "the torch's collision box is unchanged");
            Block chest = Obj("vintagee:alchemicalchest") as Block;
            Check(chest?.SelectionBoxes?.FirstOrDefault()?.Y2 < 0.9f, "the alchemical chest's outline fits its lid: " + chest?.SelectionBoxes?.FirstOrDefault());
        }

        /// <summary>/emc configure: a number, a list entry and a table entry can be changed, then put back.</summary>
        void CheckConfigure()
        {
            VintageEConfig cfg = mod.Config;
            double range = cfg.TableUseRange;
            string answer = RunCommand("/emc configure tableuserange 9.5");
            Line("  /emc configure tableuserange 9.5 -> " + answer.Replace("\n", " | "));
            Check(Math.Abs(mod.Config.TableUseRange - 9.5) < 1e-9, "/emc configure changes a number setting");
            RunCommand("/emc configure TableUseRange " + range.ToString(System.Globalization.CultureInfo.InvariantCulture));

            RunCommand("/emc configure HostileCreatures add game:fox-*");
            bool added = mod.Config.HostileCreatures.Contains("game:fox-*");
            RunCommand("/emc configure HostileCreatures remove game:fox-*");
            Check(added && !mod.Config.HostileCreatures.Contains("game:fox-*"), "/emc configure adds and removes list entries");

            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            RunCommand("/emc configure StackDataModes set block:game:woodbucket item");
            bool item = bucket != null && mod.Emc.StackData.ModeOf(bucket) == StackDataMode.Item;
            RunCommand("/emc configure StackDataModes remove block:game:woodbucket");
            Check(item && mod.Emc.StackData.ModeOf(bucket) == StackDataMode.Full, "/emc configure sets a contents mode and puts it back (liquid containers default to full)");

            string bad = RunCommand("/emc configure WatchSpeedUp lots");
            Check(bad.Contains("Error"), "/emc configure refuses a bad value (" + bad + ")");
            string list = RunCommand("/emc configure list");
            Check(list.Contains("RepairTalismanClothingPercentPerMinute"), "/emc configure list shows the settings");

            // The settings window sends whole values as text.
            var (names, values) = ConfigureCommand.Snapshot(cfg);
            string hostile = values[Array.IndexOf(names, "HostileCreatures")];
            ConfigureCommand.SetFromText(mod, "HostileCreatures", hostile + "\ngame:fox-*", "en");
            bool viaWindow = mod.Config.HostileCreatures.Contains("game:fox-*");
            ConfigureCommand.SetFromText(mod, "HostileCreatures", hostile, "en");
            Check(viaWindow && !mod.Config.HostileCreatures.Contains("game:fox-*"), "the settings window replaces a list");
            bool refused = false;
            try { ConfigureCommand.SetFromText(mod, "TransitionEmcPerDay", "dry = lots", "en"); }
            catch (FormatException) { refused = true; }
            Check(refused && mod.Config.TransitionEmcPerDay.TryGetValue("dry", out double dry) && dry == 64, "the settings window refuses a bad table line and keeps the old value");
            double adminSpeed = mod.Config.WatchSpeedUp;
            ConfigureCommand.SetFromText(mod, "WatchSpeedUp", "12", "en");
            bool number = Math.Abs(mod.Config.WatchSpeedUp - 12) < 1e-9;
            ConfigureCommand.SetFromText(mod, "WatchSpeedUp", adminSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture), "en");
            Check(number, "the settings window changes a number");
        }

        /// <summary>An element's box in model space at rest, in voxels.</summary>
        static string Describe(ShapeElement e)
        {
            var chain = new List<ShapeElement>();
            for (ShapeElement p = e; p != null; p = p.ParentElement) chain.Insert(0, p);
            float[] m = Mat4f.Create();
            foreach (ShapeElement p in chain) m = Mat4f.Mul(Mat4f.Create(), m, p.GetLocalTransformMatrix(0, Mat4f.Create()));
            float[] a = Mat4f.MulWithVec4(m, 0, 0, 0, 1);
            float[] b = Mat4f.MulWithVec4(m, (float)((e.To[0] - e.From[0]) / 16), (float)((e.To[1] - e.From[1]) / 16), (float)((e.To[2] - e.From[2]) / 16), 1);
            string V(float[] v) => $"[{v[0] * 16:0.##}, {v[1] * 16:0.##}, {v[2] * 16:0.##}]";
            return $"{V(a)} to {V(b)}";
        }

        T Place<T>(BlockPos pos, string code) where T : BlockEntity
        {
            Block block = sapi.World.GetBlock(new AssetLocation(code));
            Check(block != null && block.Id != 0, code + " exists");
            if (block == null || block.Id == 0) return null;
            sapi.World.BlockAccessor.SetBlock(block.Id, pos);
            var be = sapi.World.BlockAccessor.GetBlockEntity(pos) as T;
            Check(be != null, $"{code} has its block entity");
            return be;
        }

        BlockEntityFirepit PlaceFurnace(BlockPos pos, string type)
        {
            Block block = sapi.World.GetBlock(new AssetLocation("vintagee", "furnace-" + type + "-cold"));
            Check(block != null && block.Id != 0, $"furnace-{type}-cold exists");
            if (block == null || block.Id == 0) return null;
            sapi.World.BlockAccessor.SetBlock(block.Id, pos);
            // A roof, so rain can't put it out (firepits go out in the rain).
            Block roof = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            if (roof != null) sapi.World.BlockAccessor.SetBlock(roof.Id, pos.UpCopy(3));
            var be = sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFirepit;
            Check(be is BEMatterFurnace, $"{type} furnace has its block entity ({be?.GetType().Name ?? "none"})");
            return be;
        }

        void Report(string label, BlockEntityFirepit be)
        {
            if (be == null) { Check(false, label + ": no furnace"); return; }
            Block now = sapi.World.BlockAccessor.GetBlock(be.Pos);
            ItemStack output = be.outputSlot?.Itemstack;
            ItemStack input = be.inputSlot?.Itemstack;
            Line($"  {label}: block {now?.Code}, {be.furnaceTemperature:0} °C, max {be.maxTemperature}, burning {be.IsBurning}, input {input?.Collectible.Code} x{input?.StackSize}, output {output?.Collectible.Code} x{output?.StackSize}");
            Check(output?.Collectible?.Code?.Path?.Contains("cooked") == true, label + " cooked the meat");
        }

        EntityVintageEProjectile Fire(string kind, Vec3d from, Action<EntityVintageEProjectile> setup)
        {
            EntityProperties type = sapi.World.GetEntityType(new AssetLocation("vintagee", "projectile-" + kind));
            Check(type != null, $"entity type vintagee:projectile-{kind} exists");
            if (type == null) return null;
            var shot = sapi.World.ClassRegistry.CreateEntity(type) as EntityVintageEProjectile;
            Check(shot != null, $"projectile-{kind} uses the projectile class");
            if (shot == null) return null;
            shot.Pos.SetPos(from);
            shot.Pos.Motion.Set(0, -0.5, 0);
            setup?.Invoke(shot);
            sapi.World.SpawnEntity(shot);
            return shot;
        }

        int CountSolid(BlockPos center, int r)
        {
            int n = 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        if (sapi.World.BlockAccessor.GetBlock(center.AddCopy(dx, dy, dz))?.Id > 0) n++;
            return n;
        }

        /// <summary>Where the benchmarks run their area scans: away from the blocks the checks place.</summary>
        BlockPos benchSite;

        void Finish()
        {
            // Timings of the mod's hot paths (lines starting BENCH). Last of all: they hold the game thread for
            // seconds, and shots still in the air when that happened timed out instead of landing.
            if (benchSite != null)
            {
                try { Benchmarks.Run(mod, sapi, benchSite, Line); }
                catch (Exception e) { Line("BENCH failed: " + e); }
            }
            Line(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            try
            {
                string dir = VintageEModSystem.DataFolder(sapi);
                File.WriteAllText(Path.Combine(dir, "selftest.txt"), log.ToString());
            }
            catch (Exception e) { sapi.Logger.Error("Vintage-E self-test: could not write the report: {0}", e); }
        }
    }
}
