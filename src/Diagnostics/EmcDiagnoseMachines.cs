using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// The doctor's machine checks (1.19.1): chutes and hoppers with Vintage-E's blocks, the machine the player is
    /// looking at (collector, relay, condenser, table, pedestal, chest, furnace), the tablet's magnet, Klein stars and
    /// trinkets carried. /emc diagnose machines (aliases machine, block, magnet, stars, trinkets).
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionMachines = "machines";

        /// <summary>The short version for the check-up: only what's wrong with chutes and the looked-at machine, and a link.</summary>
        static void MachinesShort(Ctx x)
        {
            bool furnace = FurnaceChutesWork();
            if (!furnace) { x.Warn(x.L("diag-mach-furnace-nochute")); x.Fix(x.L("diag-mach-furnace-nochute-fix")); }
            else x.Ok(x.L("diag-mach-chutes-ok"));
            x.R.Add(Link(ActionMachines, "", x.L("diag-mach-more")), EmcLineStyle.Text, 1);
        }

        static bool FurnaceChutesWork()
        {
            try { return new BEMatterFurnace().ChuteInventory; } catch { return false; }
        }

        static void MachinesReport(Ctx x)
        {
            EmcReport r = x.R;
            var cfg = x.Mod.Config;

            r.Section(x.L("diag-mach-chutes-title"));
            x.Ok(x.L("diag-mach-chest-ok"));
            x.Explain(x.L("diag-mach-chest-why"));
            x.Ok(x.L("diag-mach-condenser-ok"));
            x.Explain(x.L("diag-mach-condenser-why"));
            if (FurnaceChutesWork()) { x.Ok(x.L("diag-mach-furnace-ok")); x.Explain(x.L("diag-mach-furnace-why")); }
            else { x.Warn(x.L("diag-mach-furnace-nochute")); x.Fix(x.L("diag-mach-furnace-nochute-fix")); }
            x.Info(x.L("diag-mach-table-chutes"));
            x.Explain(x.L("diag-mach-hopper-note"));
            if (Math.Abs(cfg.MachineSpeedMultiplier - 1) > 1e-9) x.Info(x.L("diag-mach-speed", cfg.MachineSpeedMultiplier));

            r.Section(x.L("diag-mach-looked-title"));
            LookedAt(x);

            if (x.Player != null)
            {
                r.Section(x.L("diag-mach-carried-title"));
                Magnet(x);
                Stars(x);
                Trinkets(x);
            }
            Back(x);
        }

        /// <summary>The Vintage-E block the asking player is looking at, and why it might not be doing anything.</summary>
        static void LookedAt(Ctx x)
        {
            BlockPos pos = x.Player?.CurrentBlockSelection?.Position;
            if (pos == null) { x.Info(x.L("diag-mach-looked-none")); return; }
            var world = x.Sapi.World;
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            Block block = world.BlockAccessor.GetBlock(pos);
            string name = block?.GetPlacedBlockName(world, pos) ?? "?";
            var cfg = x.Mod.Config;
            switch (be)
            {
                case BEEnergyCollector c:
                    {
                        int sun = world.BlockAccessor.GetLightLevel(pos.UpCopy(), EnumLightLevelType.TimeOfDaySunLight);
                        int maxSun = Math.Max(1, world.SunBrightness);
                        double light = Math.Max(GameMath.Clamp(sun / (double)maxSun, 0, 1), cfg.CollectorDarkFactor);
                        x.Info(x.L("diag-mach-collector", name, EmcFormat.Format(c.Stored), EmcFormat.Format(c.Capacity), EmcFormat.Format(c.RatePerSecond * cfg.MachineSpeedMultiplier * light), (int)Math.Round(light * 100)));
                        if (light <= cfg.CollectorDarkFactor + 1e-9) { x.Warn(x.L("diag-mach-collector-dark")); x.Explain(x.L("diag-mach-collector-dark-why")); }
                        Neighbours(x, pos, c.Stored >= c.Capacity - 1e-6);
                        StarIn(x, c.Star);
                        break;
                    }
                case BEAntimatterRelay rl:
                    x.Info(x.L("diag-mach-relay", name, EmcFormat.Format(rl.Stored), EmcFormat.Format(rl.Capacity), EmcFormat.Format(rl.TransferPerSecond * cfg.MachineSpeedMultiplier)));
                    if (rl.Stored <= 0) { x.Warn(x.L("diag-mach-relay-empty")); x.Explain(x.L("diag-mach-relay-empty-why")); }
                    Neighbours(x, pos, rl.Stored >= rl.Capacity - 1e-6);
                    StarIn(x, rl.Star);
                    break;
                case BEEnergyCondenser cd:
                    {
                        ItemStack target = cd.Inventory[BEEnergyCondenser.TargetSlot]?.Itemstack;
                        int filled = 0, free = 0;
                        for (int i = 1; i < cd.Inventory.Count; i++) { if (cd.Inventory[i].Empty) free++; else filled++; }
                        x.Info(x.L("diag-mach-condenser", name, EmcFormat.Format(cd.Stored), EmcFormat.Format(cd.Capacity), EmcFormat.Format(cd.IncomingPerSecond), filled, free));
                        if (target == null) { x.Warn(x.L("diag-mach-condenser-notarget")); x.Explain(x.L("diag-mach-condenser-notarget-why")); }
                        else if (target.Collectible is IEmcStorageItem) x.Info(x.L("diag-mach-condenser-star", target.GetName()));
                        else
                        {
                            double v = x.Mod.Emc.GetValue(target);
                            if (v <= 0) { x.Warn(x.L("diag-mach-condenser-novalue", target.GetName())); x.Fix(x.L("diag-mach-condenser-novalue-fix")); }
                            else if (x.Mod.Emc.IsNoTransmute(target.Collectible)) x.Warn(x.L("diag-mach-condenser-notransmute", target.GetName()));
                            else x.Ok(x.L("diag-mach-condenser-target", target.GetName(), EmcFormat.Format(v)));
                            if (v > 0 && cd.Stored < v && filled == 0 && cd.IncomingPerSecond <= 0) { x.Warn(x.L("diag-mach-condenser-starved")); x.Explain(x.L("diag-mach-condenser-starved-why")); }
                        }
                        if (free == 0) { x.Warn(x.L("diag-mach-condenser-full")); x.Explain(x.L("diag-mach-condenser-full-why")); }
                        break;
                    }
                case BETransmutationTable t:
                    {
                        bool online = t.OwnerUid != null && world.AllOnlinePlayers.Any(p => p.PlayerUID == t.OwnerUid);
                        ItemStack waiting = t.Inventory[0]?.Itemstack;
                        x.Info(x.L("diag-mach-table", name, t.OwnerName ?? x.L("diag-mach-table-noowner"), online ? x.L("diag-yes") : x.L("diag-no"), waiting == null ? "-" : waiting.StackSize + " " + waiting.GetName()));
                        if (t.OwnerUid == null) x.Explain(x.L("diag-mach-table-noowner-why"));
                        else if (!online && waiting != null) x.Explain(x.L("diag-mach-table-offline-why"));
                        break;
                    }
                case BEPedestal pd:
                    {
                        ItemStack held = pd.Held;
                        if (held == null) { x.Info(x.L("diag-mach-pedestal-empty", name)); x.Explain(x.L("diag-mach-pedestal-empty-why")); break; }
                        int effects = held.Collectible is ItemEmcTrinket tr ? TrinketEffects.EffectsOf(tr, held).Count() : 0;
                        x.Info(x.L("diag-mach-pedestal", name, held.GetName(), effects, pd.RangeMultiplier, pd.PowerMultiplier));
                        if (effects == 0) { x.Warn(x.L("diag-mach-pedestal-off")); x.Explain(x.L("diag-mach-pedestal-off-why")); }
                        x.Explain(x.L("diag-mach-pedestal-claims"));
                        break;
                    }
                case BEAlchemicalChest ch:
                    {
                        int items = ch.Inventory.Count(s => !s.Empty);
                        int trinkets = ch.Inventory.Count(s => s.Itemstack?.Collectible is ItemEmcTrinket && ItemEmcTrinket.IsActive(s.Itemstack));
                        x.Info(x.L("diag-mach-chest", name, items, ch.Inventory.Count, trinkets));
                        if (trinkets > 0) x.Explain(x.L("diag-mach-chest-trinkets"));
                        break;
                    }
                case BEMatterFurnace f:
                    x.Info(x.L("diag-mach-furnace", name, f.fuelSlot?.Itemstack?.GetName() ?? "-", f.inputSlot?.Itemstack?.GetName() ?? "-", f.outputSlot?.Itemstack?.GetName() ?? "-"));
                    if (!f.ChuteInventory) x.Warn(x.L("diag-mach-furnace-nochute"));
                    break;
                default:
                    x.Info(x.L("diag-mach-looked-other", name));
                    break;
            }
        }

        /// <summary>What a collector or relay can pass its EMC to.</summary>
        static void Neighbours(Ctx x, BlockPos pos, bool full)
        {
            int acceptors = 0;
            foreach (BlockFacing face in BlockFacing.ALLFACES)
                if (x.Sapi.World.BlockAccessor.GetBlockEntity(pos.AddCopy(face)) is IEmcAcceptor) acceptors++;
            if (acceptors > 0) x.Ok(x.L("diag-mach-neighbours", acceptors));
            else { x.Info(x.L("diag-mach-neighbours-none")); x.Explain(x.L("diag-mach-neighbours-none-why")); }
            if (full && acceptors == 0) x.Warn(x.L("diag-mach-full"));
        }

        static void StarIn(Ctx x, StarHolder holder)
        {
            if (holder?.Star == null) return;
            x.Info(x.L("diag-mach-star-in", holder.Star.GetName(), EmcFormat.Format(StoredEmc.Get(holder.Star)), EmcFormat.Format(StoredEmc.Capacity(holder.Star))));
        }

        /// <summary>The tablet the magnet would use (the same one the window edits) and what it would do.</summary>
        static void Magnet(Ctx x)
        {
            ItemSlot slot = TabletMagnet.FindTablet(x.Player);
            ItemStack tablet = slot?.Itemstack;
            if (tablet == null) { x.Info(x.L("diag-mach-magnet-notablet")); return; }
            ItemStack magnet = TabletMagnet.GetMagnet(tablet, x.Sapi.World);
            if (magnet == null) { x.Info(x.L("diag-mach-magnet-none", tablet.GetName())); x.Explain(x.L("diag-mach-magnet-none-why")); return; }
            string[] filter = TabletMagnet.GetFilter(tablet);
            int chosen = filter.Count(k => k != null);
            bool except = TabletMagnet.Excludes(tablet);
            x.Info(x.L("diag-mach-magnet", magnet.GetName(), TabletMagnet.RadiusOf(magnet), except ? x.L("diag-mach-magnet-except", chosen) : x.L("diag-mach-magnet-only", chosen)));
            if (!except && chosen == 0) { x.Warn(x.L("diag-mach-magnet-nothing")); x.Fix(x.L("diag-mach-magnet-nothing-fix")); }
            x.Explain(x.L("diag-mach-magnet-rules"));
        }

        static void Stars(Ctx x)
        {
            var stars = EmcWallet.CarriedSlots(x.Player).Where(s => s.Itemstack?.Collectible is IEmcStorageItem).Select(s => s.Itemstack).ToList();
            if (stars.Count == 0) { x.Info(x.L("diag-mach-stars-none")); return; }
            double held = stars.Sum(s => StoredEmc.Get(s)), cap = stars.Sum(s => StoredEmc.Capacity(s));
            x.Info(x.L("diag-mach-stars", stars.Count, EmcFormat.Format(held), EmcFormat.Format(cap)));
            x.Explain(x.L("diag-mach-stars-why"));
        }

        static void Trinkets(Ctx x)
        {
            var on = EmcWallet.CarriedSlots(x.Player).Where(s => s.Itemstack?.Collectible is ItemEmcTrinket && ItemEmcTrinket.IsActive(s.Itemstack)).Select(s => s.Itemstack.GetName()).ToList();
            double balance = EmcWallet.Balance(x.Player);
            if (on.Count == 0) x.Info(x.L("diag-mach-trinkets-none"));
            else x.Info(x.L("diag-mach-trinkets", on.Count, string.Join(", ", on.Distinct()), EmcFormat.Format(balance)));
            double mul = x.Mod.Config.TrinketCostMultiplier;
            if (Math.Abs(mul - 1) > 1e-9) x.Info(x.L("diag-mach-trinket-cost", mul));
            if (on.Count > 0 && balance <= 0) { x.Warn(x.L("diag-mach-trinkets-broke")); x.Explain(x.L("diag-mach-trinkets-broke-why")); }
        }
    }
}
