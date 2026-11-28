using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
            if (!furnace) { x.Problem(x.L("diag-mach-furnace-nochute")); x.Fix(x.L("diag-mach-furnace-nochute-fix")); }
            else x.Ok(x.L("diag-mach-chutes-ok"));
            x.R.Add(Link(ActionMachines, "", x.L("diag-mach-more")), EmcLineStyle.Text, 1);
        }

        static bool FurnaceChutesWork()
        {
            try { return new BEMatterFurnace().ChuteInventory; } catch { return false; }
        }

        /// <summary>Project Expansion's tiers (1.29.0): on, off, off but not taken out, or switched since the server started.</summary>
        static void ExpansionLines(Ctx x)
        {
            if (ExpansionTiers.Error != null)
            {
                x.Problem(x.L("diag-mach-expansion-failed", ExpansionTiers.Error));
                x.Explain(x.L("diag-mach-expansion-failed-why"));
            }
            else if (!ExpansionTiers.On) x.Info(x.L("diag-mach-expansion-off", ExpansionTiers.Removed));
            else x.Info(x.L("diag-mach-expansion-on"));
            if (x.Mod.Config != null && x.Mod.Config.Expansion != ExpansionTiers.On)
                x.Warn(x.L("diag-mach-expansion-restart", x.Mod.Config.Expansion ? x.L("diag-on") : x.L("diag-off")));
        }

        /// <summary>"Burn what I mine" (1.29.3): who has it on, and whether its safety hook is in place.</summary>
        static void MiningLines(Ctx x)
        {
            if (!TabletMining.Patched)
            {
                x.Problem(x.L("diag-mach-mining-nohook", TabletMining.Error ?? "?"));
                x.Explain(x.L("diag-mach-mining-nohook-why"));
                return;
            }
            var users = TabletMining.Users(x.Sapi);
            x.Info(users.Count == 0 ? x.L("diag-mach-mining-none") : x.L("diag-mach-mining-users", string.Join(", ", users), TabletMining.Burned));
        }

        static void MachinesReport(Ctx x, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            var cfg = x.Mod.Config;

            r.Section(x.L("diag-mach-chutes-title"));
            x.Ok(x.L("diag-mach-chest-ok"));
            x.Explain(x.L("diag-mach-chest-why"));
            x.Ok(x.L("diag-mach-condenser-ok"));
            x.Explain(x.L("diag-mach-condenser-why"));
            if (FurnaceChutesWork()) { x.Ok(x.L("diag-mach-furnace-ok")); x.Explain(x.L("diag-mach-furnace-why")); }
            else { x.Problem(x.L("diag-mach-furnace-nochute")); x.Fix(x.L("diag-mach-furnace-nochute-fix")); }
            x.Info(x.L("diag-mach-table-chutes"));
            x.Explain(x.L("diag-mach-hopper-note"));
            if (Math.Abs(cfg.MachineSpeedMultiplier - 1) > 1e-9) x.Info(x.L("diag-mach-speed", cfg.MachineSpeedMultiplier));
            ExpansionLines(x);
            MiningLines(x);

            r.Section(x.L("diag-mach-looked-title"));
            LookedAt(x, client);

            if (x.Player != null)
            {
                r.Section(x.L("diag-mach-carried-title"));
                Magnet(x);
                Stars(x);
                Trinkets(x);
            }
            Back(x);
        }

        /// <summary>
        /// For the condenser being looked at: every chute, hopper or screw touching it, and whether it takes the copies
        /// out, brings things in, or does nothing and why (1.22.1: "the items don't even make it into the chute").
        /// </summary>
        static void CondenserChutes(Ctx x, BlockPos pos)
        {
            IBlockAccessor ba = x.Sapi.World.BlockAccessor;
            bool any = false, explain = false;
            foreach (BlockFacing dir in BlockFacing.ALLFACES)
            {
                ChuteLink link = ChuteOutput.Link(ba, pos, dir, out _, out _);
                if (link == ChuteLink.None) continue;
                any = true;
                BlockPos at = pos.AddCopy(dir);
                string what = ba.GetBlock(at)?.GetPlacedBlockName(x.Sapi.World, at) ?? "?";
                string side = x.L("diag-mach-side-" + dir.Code);
                string key = "diag-mach-chute-" + link.ToString().ToLowerInvariant();
                if (link == ChuteLink.Pulls || link == ChuteLink.HandedTo) x.Ok(x.L(key, what, side));
                else if (link == ChuteLink.BringsIn) { x.Info(x.L(key, what, side)); explain = true; }
                else { x.Warn(x.L(key, what, side)); explain = true; }
            }
            if (!any) { x.Info(x.L("diag-mach-chute-nothing")); explain = true; }
            if (explain) x.Explain(x.L("diag-mach-chute-how"));
        }

        /// <summary>The Vintage-E block the asking player is looking at, and why it might not be doing anything.</summary>
        static void LookedAt(Ctx x, EmcDiagnoseRequestPacket client)
        {
            // Where the player looks on their computer; the server's own selection is only the last block they clicked.
            BlockPos pos = client?.HasLookedAt == true ? new BlockPos(client.LookedAtX, client.LookedAtY, client.LookedAtZ, client.LookedAtDim)
                : client?.HasClientInfo == true ? null : x.Player?.CurrentBlockSelection?.Position;
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
                        bool compactSun = BlockCompactSun.Above(world, pos);
                        double light = compactSun ? 1 : Math.Max(GameMath.Clamp(sun / (double)maxSun, 0, 1), cfg.CollectorDarkFactor);
                        x.Info(x.L("diag-mach-collector", name, EmcFormat.Total(c.Stored), EmcFormat.Total(c.Capacity), EmcFormat.Format(c.RateNow()), (int)Math.Round(light * 100)));
                        if (compactSun) x.Ok(x.L("diag-mach-collector-sun", BlockCompactSun.Bonus(x.Sapi)));
                        else if (light <= cfg.CollectorDarkFactor + 1e-9) { x.Warn(x.L("diag-mach-collector-dark")); x.Explain(x.L("diag-mach-collector-dark-why")); }
                        Neighbours(x, pos, c.Stored >= c.Capacity);
                        StarIn(x, c.Star);
                        break;
                    }
                case BEAntimatterRelay rl:
                    x.Info(x.L("diag-mach-relay", name, EmcFormat.Total(rl.Stored), EmcFormat.Total(rl.Capacity), EmcFormat.Format(rl.TransferPerSecond * cfg.MachineSpeedMultiplier)));
                    if (rl.Stored <= 0) { x.Warn(x.L("diag-mach-relay-empty")); x.Explain(x.L("diag-mach-relay-empty-why")); }
                    Neighbours(x, pos, rl.Stored >= rl.Capacity);
                    StarIn(x, rl.Star);
                    break;
                case BEEnergyCondenser cd:
                    {
                        ItemStack target = cd.Inventory[BEEnergyCondenser.TargetSlot]?.Itemstack;
                        int filled = 0, free = 0;
                        for (int i = 1; i < cd.Inventory.Count; i++) { if (cd.Inventory[i].Empty) free++; else filled++; }
                        x.Info(x.L("diag-mach-condenser", name, EmcFormat.Total(cd.Stored), EmcFormat.Total(cd.Capacity), EmcFormat.Format(cd.IncomingPerSecond), filled, free));
                        if (target == null) { x.Warn(x.L("diag-mach-condenser-notarget")); x.Explain(x.L("diag-mach-condenser-notarget-why")); }
                        else if (target.Collectible is IEmcStorageItem) x.Info(x.L("diag-mach-condenser-star", target.GetName()));
                        else
                        {
                            double v = x.Mod.Emc.GetValue(target);
                            if (v <= 0) { x.Warn(x.L("diag-mach-condenser-novalue", target.GetName())); x.Fix(x.L("diag-mach-condenser-novalue-fix")); }
                            else if (x.Mod.Emc.IsNoTransmute(target.Collectible)) x.Warn(x.L("diag-mach-condenser-notransmute", target.GetName()));
                            else x.Ok(x.L("diag-mach-condenser-target", target.GetName(), EmcFormat.Format(v)));
                            if (v > 0 && (double)cd.Stored < v && filled == 0 && cd.IncomingPerSecond <= 0) { x.Warn(x.L("diag-mach-condenser-starved")); x.Explain(x.L("diag-mach-condenser-starved-why")); }
                        }
                        if (free == 0) { x.Warn(x.L("diag-mach-condenser-full")); x.Explain(x.L("diag-mach-condenser-full-why")); }
                        CondenserChutes(x, pos);
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
                case BEStormCollector sc:
                    {
                        x.Info(x.L("diag-mach-storm", name, EmcFormat.Total(sc.Stored), EmcFormat.Total(sc.Capacity), EmcFormat.Format(sc.RateNow()),
                            (int)Math.Round(sc.Rain * 100), sc.Thunder ? x.L("diag-yes") : x.L("diag-no"), sc.TemporalStorm ? x.L("diag-yes") : x.L("diag-no"), sc.Strikes));
                        if (!sc.UnderSky) { x.Warn(x.L("diag-mach-storm-roof")); x.Explain(x.L("diag-mach-storm-roof-why")); }
                        else if (sc.RateNow() <= 0) x.Explain(x.L("diag-mach-storm-calm"));
                        Neighbours(x, pos, sc.Stored >= sc.Capacity);
                        break;
                    }
                case BEPowerFlower fl:
                    {
                        bool online = OwnedMachine.OnlineOwner(x.Sapi, fl.OwnerUid) != null;
                        x.Info(x.L("diag-mach-flower", name, fl.OwnerName ?? x.L("diag-mach-table-noowner"), online ? x.L("diag-yes") : x.L("diag-no"), EmcFormat.Format(fl.PerSecond), fl.SunBonus, EmcFormat.Total(fl.Stored)));
                        if (fl.OwnerUid == null) { x.Warn(x.L("diag-mach-owned-noowner")); x.Fix(x.L("diag-mach-owned-noowner-fix")); }
                        else if (!online && fl.Stored > 0) x.Explain(x.L("diag-mach-flower-offline-why"));
                        if (fl.SunBonus <= 1) x.Explain(x.L("diag-mach-flower-sun-how", BlockCompactSun.Bonus(x.Sapi)));
                        break;
                    }
                case BEEmcLink ln:
                    {
                        IServerPlayer owner = OwnedMachine.OnlineOwner(x.Sapi, ln.OwnerUid);
                        ItemStack product = ln.TargetKey == null ? null : TransmutationService.Prototype(x.Mod, world, ln.TargetKey);
                        int waiting = 0;
                        for (int i = 1; i <= ln.InputSlots; i++) waiting += ln.Inventory[i].StackSize;
                        string limits = ln.Unlimited ? x.L("diag-mach-link-unlimited")
                            : x.L("diag-mach-link-limits", EmcFormat.Total(ln.EmcLimit), ln.ItemLimit, EmcFormat.Format(ln.LitreLimit), ln.InputSlots);
                        x.Info(x.L("diag-mach-link", name, ln.OwnerName ?? x.L("diag-mach-table-noowner"), owner != null ? x.L("diag-yes") : x.L("diag-no"),
                            product == null ? "-" : product.GetName(), limits, EmcFormat.Total(ln.Buffer), waiting));
                        if (ln.OwnerUid == null) { x.Warn(x.L("diag-mach-owned-noowner")); x.Fix(x.L("diag-mach-owned-noowner-fix")); }
                        else if (owner == null && (ln.Buffer > 0 || waiting > 0)) x.Explain(x.L("diag-mach-link-offline-why"));
                        if (ln.TargetKey != null && product == null) { x.Warn(x.L("diag-mach-link-gone", ln.TargetKey)); x.Fix(x.L("diag-mach-link-notarget-why")); }
                        else if (product == null) x.Explain(x.L("diag-mach-link-notarget-why"));
                        else
                        {
                            double v = x.Mod.Emc.GetValue(product);
                            if (v <= 0) x.Warn(x.L("diag-mach-condenser-novalue", product.GetName()));
                            else if (x.Mod.Emc.IsNoTransmute(product.Collectible)) x.Warn(x.L("diag-mach-condenser-notransmute", product.GetName()));
                            else if (owner != null && !PlayerEmc.Knows(owner, ln.TargetKey)) { x.Warn(x.L("diag-mach-link-notlearned", owner.PlayerName, product.GetName())); x.Explain(x.L("diag-mach-link-notlearned-why")); }
                            else if (owner != null && PlayerEmc.GetEmc(owner) < EmcAmount.From(v)) x.Warn(x.L("diag-mach-link-poor", owner.PlayerName, product.GetName(), EmcFormat.Format(v)));
                            else x.Ok(x.L("diag-mach-condenser-target", product.GetName(), EmcFormat.Format(v)));
                            if (EmcRegistry.ItemsPerLitre(product.Collectible) <= 0 && !ChuteOutput.HasTaker(world.BlockAccessor, pos)) x.Explain(x.L("diag-mach-link-notaker"));
                        }
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
                    if (!f.ChuteInventory) x.Problem(x.L("diag-mach-furnace-nochute"));
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
            x.Info(x.L("diag-mach-star-in", holder.Star.GetName(), EmcFormat.Total(StoredEmc.Get(holder.Star)), EmcFormat.Total(StoredEmc.Capacity(holder.Star))));
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
            decimal held = stars.Aggregate(0m, (sum, s) => EmcAmount.Add(sum, StoredEmc.Get(s)));
            decimal cap = stars.Aggregate(0m, (sum, s) => EmcAmount.Add(sum, StoredEmc.Capacity(s)));
            x.Info(x.L("diag-mach-stars", stars.Count, EmcFormat.Total(held), EmcFormat.Total(cap)));
            x.Explain(x.L("diag-mach-stars-why"));
        }

        static void Trinkets(Ctx x)
        {
            var on = EmcWallet.CarriedSlots(x.Player).Where(s => s.Itemstack?.Collectible is ItemEmcTrinket && ItemEmcTrinket.IsActive(s.Itemstack)).Select(s => s.Itemstack.GetName()).ToList();
            decimal balance = EmcWallet.Balance(x.Player);
            if (on.Count == 0) x.Info(x.L("diag-mach-trinkets-none"));
            else x.Info(x.L("diag-mach-trinkets", on.Count, string.Join(", ", on.Distinct()), EmcFormat.Total(balance)));
            double mul = x.Mod.Config.TrinketCostMultiplier;
            if (Math.Abs(mul - 1) > 1e-9) x.Info(x.L("diag-mach-trinket-cost", mul));
            if (on.Count > 0 && balance <= 0) { x.Warn(x.L("diag-mach-trinkets-broke")); x.Explain(x.L("diag-mach-trinkets-broke-why")); }
        }
    }
}
