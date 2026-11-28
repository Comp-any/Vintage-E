using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The Matter Upgrader (1.29.2, Project Expansion's): right-click a collector, relay, Power Flower or EMC Link with it
    /// to turn it into the next tier where it stands. You must have learned the next tier, and you pay the difference in
    /// their values from your EMC (and Klein stars). What the machine holds, its owner and its EMC stay; the new tier's
    /// rates and limits apply at once (<see cref="BlockEntity.OnExchanged"/>). Only the owner of a flower or link may.
    /// </summary>
    public class ItemMatterUpgrader : Item
    {
        static readonly string[] Families = { "energycollector-", "antimatterrelay-", "powerflower-", "emclink-" };

        /// <summary>The next tier of a machine block, or null if it's the last, not one this upgrades, or not in the game (the Expansion tiers off).</summary>
        /// <summary>Whether this is a collector, relay, Power Flower or EMC Link (any tier).</summary>
        public static bool Upgradable(Block block)
        {
            if (block?.Code == null || block.Code.Domain != "vintagee" || OwnedMachine.Tier(block) <= 0) return false;
            foreach (string f in Families) if (block.Code.Path.StartsWith(f, StringComparison.Ordinal)) return true;
            return false;
        }

        public static Block NextBlock(IWorldAccessor world, Block block)
        {
            if (!Upgradable(block)) return null;
            string path = block.Code.Path;
            int mk = OwnedMachine.Tier(block);
            if (mk >= 16) return null;
            string next = path.Substring(0, path.LastIndexOf("-mk", StringComparison.Ordinal)) + "-mk" + (mk + 1);
            Block b = world.GetBlock(new AssetLocation("vintagee", next));
            return b == null || b.IsMissing || b.BlockId == 0 ? null : b;
        }

        /// <summary>What upgrading from one block to the next costs: the difference in their EMC values, never below nothing.</summary>
        public static decimal Cost(VintageEModSystem mod, Block from, Block to)
        {
            if (mod?.Emc == null || from == null || to == null) return 0;
            decimal a = EmcAmount.From(mod.Emc.GetValue(from)), b = EmcAmount.From(mod.Emc.GetValue(to));
            return b > a ? b - a : 0;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null) { base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling); return; }
            Block block = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
            Block next = NextBlock(byEntity.World, block);
            if (!Upgradable(block)) { base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling); return; }
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer sp)) return;
            string why = Upgrade(sp, blockSel.Position, block, next);
            sp.SendMessage(GlobalConstants.GeneralChatGroup, why, EnumChatType.Notification);
        }

        /// <summary>Upgrades the machine at <paramref name="pos"/> for <paramref name="player"/>, and says what happened.</summary>
        public static string Upgrade(IServerPlayer player, Vintagestory.API.MathTools.BlockPos pos, Block block, Block next)
        {
            string L(string key, params object[] args) => Lang.GetL(player.LanguageCode, "vintagee:" + key, args);
            var world = player.Entity.World;
            var mod = VintageEModSystem.Of(world.Api);
            if (next == null) return L("upgrader-last", block.GetPlacedBlockName(world, pos));
            if (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return L("upgrader-noaccess");
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is IOwnedMachine owned && owned.OwnerUid != null && owned.OwnerUid != player.PlayerUID && !player.HasPrivilege(Privilege.controlserver))
                return L("upgrader-notowner", owned.OwnerName);
            bool creative = player.WorldData.CurrentGameMode == EnumGameMode.Creative;
            string nextName = new ItemStack(next).GetName();
            if (!creative && !PlayerEmc.Knows(player, PlayerEmc.KeyOf(next))) return L("upgrader-notlearned", nextName);
            decimal cost = creative ? 0 : Cost(mod, block, next);
            if (!EmcWallet.TryPayExact(player, cost)) return L("upgrader-cantpay", EmcFormat.Total(cost), nextName);
            world.BlockAccessor.ExchangeBlock(next.BlockId, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            return L("upgrader-done", nextName, EmcFormat.Total(cost));
        }
    }
}
