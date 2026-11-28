using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Server half of the transmutation table/tablet: validates access and performs burn/create requests.
    /// Every request is checked here; the client GUI is just a view.
    /// </summary>
    public class TransmutationService
    {
        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly Dictionary<string, BlockPos> tableSessions = new Dictionary<string, BlockPos>();

        public TransmutationService(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
            sapi.Event.PlayerLeave += p => tableSessions.Remove(p.PlayerUID);
            sapi.Event.RegisterGameTickListener(Guard.Tick(sapi.Logger, "the Arcane Tablet's magnet", () => TabletMagnet.Tick(mod, sapi, this)), 250);
        }

        public void OpenTableSession(IServerPlayer player, BlockPos pos)
        {
            tableSessions[player.PlayerUID] = pos.Copy();
        }

        public void OnRequest(IServerPlayer player, TransmuteRequestPacket req)
        {
            if (req == null || player?.Entity == null) return;
            if (req.Action == TransmuteAction.Close)
            {
                tableSessions.Remove(player.PlayerUID);
                return;
            }
            if (req.Action == TransmuteAction.GemFilter || req.Action == TransmuteAction.GemMode)
            {
                GemFilter.OnRequest(player, req, (code, args) => Fail(player, code, args)); // needs the gem, not a table
                return;
            }
            if (!HasAccess(player))
            {
                Fail(player, "noaccess");
                return;
            }

            switch (req.Action)
            {
                case TransmuteAction.Burn: Burn(player, req.Mode == 1); break;
                case TransmuteAction.BurnSlot: BurnSlot(player, req); break;
                case TransmuteAction.Learn: LearnHeld(player); break;
                case TransmuteAction.Charge: Charge(player, req.Mode == 1); break;
                case TransmuteAction.Create: Create(player, req.Key, req.Mode); break;
                case TransmuteAction.Forget: PlayerEmc.Forget(player, req.Key); break;
                case TransmuteAction.MagnetSwap:
                case TransmuteAction.MagnetFilter:
                case TransmuteAction.MagnetMode:
                    TabletMagnet.OnRequest(mod, player, req, (code, args) => Fail(player, code, args));
                    break;
            }
        }

        bool HasAccess(IServerPlayer player)
        {
            if (tableSessions.TryGetValue(player.PlayerUID, out BlockPos pos))
            {
                Block block = sapi.World.BlockAccessor.GetBlock(pos);
                double range = mod.Config.TableUseRange;
                if (block is BlockTransmutationTable)
                {
                    if (player.Entity.Pos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) <= range * range) return true;
                    // Too far just now: the table stays open for when the player steps back. Until 1.24.1 one click
                    // from out of reach closed it on this side only, and the window answered "no access" from then
                    // on, however close the player came, until it was shut and the table clicked again.
                }
                else tableSessions.Remove(player.PlayerUID); // the table is gone
            }
            return CarriesTablet(player);
        }

        static bool CarriesTablet(IServerPlayer player)
        {
            foreach (string invClass in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.characterInvClassName })
            {
                IInventory inv = player.InventoryManager.GetOwnInventory(invClass);
                if (inv == null) continue;
                foreach (ItemSlot slot in inv)
                {
                    if (slot?.Itemstack?.Collectible is ItemTransmutationTablet) return true;
                }
            }
            return false;
        }

        /// <summary>What burning a stack gives: its EMC per item and the knowledge entry it teaches.</summary>
        public sealed class BurnPlan
        {
            public double Each;
            public string Key;
            /// <summary>A container learned with its contents also teaches the plain container.</summary>
            public bool AlsoPlain;
        }

        /// <summary>Whether this stack can be burned (null) or why not (an error code, with <paramref name="args"/> for its message).</summary>
        /// <param name="learnOnly">Only learning it, not burning it: a container's contents aren't at risk, so it isn't refused for them.</param>
        /// <param name="byHand">The player dropped it on Burn themselves: a Klein star is burned then (for the star and what it holds).</param>
        public string PlanBurn(IServerPlayer player, ItemStack stack, out BurnPlan plan, out string[] args, bool learnOnly = false, bool byHand = false)
        {
            plan = null;
            // The item's name is only looked up for a refusal's message: the magnet, hoppers and the condenser ask this
            // for every stack they consider, and nearly always the answer is yes.
            args = Array.Empty<string>();
            if (stack?.Collectible == null) { args = new[] { stack?.GetName() ?? "?" }; return "novalue"; }
            // A Klein star is only burned by hand on Burn (its value counts what it holds, so nothing is lost), never by a
            // hopper into the table or the magnet, where someone would lose a charged star without meaning to.
            if (stack.Collectible is IEmcStorageItem && !learnOnly && !byHand) { args = new[] { stack.GetName() }; return "star-noburn"; }
            StackDataTable data = mod.Emc.StackData;
            StackDataMode mode = data.ModeOf(stack.Collectible);
            bool keepsData = data.CarriesData(stack.Collectible) && mode != StackDataMode.Item;

            // Item only: what's inside would be lost, so containers that hold something are refused.
            if (!learnOnly && !keepsData && mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack)) { args = new[] { stack.GetName() }; return "hascontents"; }

            double each = mod.BurnValue(stack);
            if (each <= 0)
            {
                // The table only offers what the player's game shows a value for; if the server disagrees, say what
                // it has in the log and send the player the server's values again, in case theirs are out of date.
                double plain = mod.Emc.GetValue(stack.Collectible);
                // A notification, not a warning: a player trying to burn something worth nothing (the Tome, say) is
                // expected, and the doctor would otherwise flag every attempt. The watch keeps the refusal.
                sapi.Logger.Notification("Vintage-E: {0} can't burn {1} ({2}): the server values it at {3} each (the plain item {4}); stack attributes: {5}",
                    player?.PlayerName ?? "?", stack.GetName(), stack.Collectible.Code, each, plain, stack.Attributes?.ToJsonToken() ?? "none");
                // Once a minute per player: clicking the burn slot with a worthless item must not cost a table send each time.
                if (player != null && mod.ResendAllowed(player)) mod.ResendEmcTable(player);
                args = new[] { stack.GetName() + " (" + stack.Collectible.Code + ")" };
                return "novalue";
            }
            if (mod.Emc.IsNoTransmute(stack.Collectible)) { args = new[] { stack.GetName() }; return "notransmute"; }

            // Items that keep their contents/material are learned as exactly this version (a liquid container as a full one).
            TreeAttribute kept = keepsData ? StackDataUtil.KeptData(StackDataUtil.FilledLiquid(stack, sapi.World)) : null;
            string key = kept != null ? PlayerEmc.ExactKey(stack.Collectible, kept) : PlayerEmc.KeyOf(stack.Collectible);
            if (kept != null && !PlayerEmc.Knows(player, key) && PlayerEmc.CountExact(player) >= mod.Config.MaxExactKnowledge)
            {
                args = new[] { mod.Config.MaxExactKnowledge.ToString() };
                return "toomanyexact";
            }
            plan = new BurnPlan
            {
                Each = each,
                Key = key,
                AlsoPlain = kept != null && data.KindOf(stack.Collectible) == StackDataKind.Contents && mod.Emc.GetValue(stack.Collectible) > 0
            };
            return null;
        }

        /// <summary>Gives the EMC for a stack taken from the player (or the ground) and teaches it. Returns the key if it was new.</summary>
        public string ApplyBurn(IServerPlayer player, ItemStack taken, BurnPlan plan)
        {
            PlayerEmc.AddEmc(player, plan.Each * taken.StackSize);
            bool learned = PlayerEmc.Learn(player, plan.Key);
            if (plan.AlsoPlain) learned |= PlayerEmc.Learn(player, PlayerEmc.KeyOf(taken.Collectible));
            return learned ? plan.Key : null;
        }

        /// <summary>
        /// Learns a stack without burning it (the table's Learn slot, /emc learn hand): the same checks as burning, nothing
        /// taken. Learning by burning gives back the item's full value in EMC, so keeping it instead costs nothing either way.
        /// Returns an error code (null when it worked); <paramref name="isNew"/> is false if it was already known.
        /// </summary>
        public string Learn(IServerPlayer player, ItemStack stack, out bool isNew, out string[] args) => Learn(player, stack, out isNew, out args, out _);

        public string Learn(IServerPlayer player, ItemStack stack, out bool isNew, out string[] args, out string[] learnedKeys)
        {
            isNew = false;
            learnedKeys = Array.Empty<string>();
            string error = PlanBurn(player, stack, out BurnPlan plan, out args, learnOnly: true);
            if (error != null) return error;
            var keys = new List<string>();
            if (PlayerEmc.Learn(player, plan.Key)) keys.Add(plan.Key);
            if (plan.AlsoPlain && PlayerEmc.Learn(player, PlayerEmc.KeyOf(stack.Collectible))) keys.Add(PlayerEmc.KeyOf(stack.Collectible));
            isNew = keys.Count > 0;
            learnedKeys = keys.ToArray();
            args = new[] { stack.GetName() };
            return null;
        }

        /// <summary>A result that also carries the player's balance, so the window updates at once.</summary>
        TransmuteResultPacket Result(IServerPlayer player, string learnedKey, params string[] learnedKeys)
            => new TransmuteResultPacket { LearnedKey = learnedKey, LearnedKeys = learnedKeys, Emc = PlayerEmc.GetEmc(player), HasEmc = true };

        void LearnHeld(IServerPlayer player)
        {
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            string error = Learn(player, mouse.Itemstack, out bool isNew, out string[] args, out string[] learnedKeys);
            if (error != null)
            {
                Fail(player, error, args);
                return;
            }
            if (!isNew)
            {
                Fail(player, "alreadyknown", mouse.Itemstack.GetName());
                return;
            }
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, Result(player, learnedKeys.Length > 0 ? learnedKeys[0] : PlayerEmc.KeyOf(mouse.Itemstack.Collectible), learnedKeys));
        }

        /// <summary>
        /// The Charge slot: a Klein star dropped on it is filled from the player's EMC (left-click) or emptied into it
        /// (right-click), and stays on the cursor. Until 1.18.11 this was what Burn did with a star, so a star could
        /// never be burned.
        /// </summary>
        void Charge(IServerPlayer player, bool empty)
        {
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            if (!(mouse.Itemstack.Collectible is IEmcStorageItem)) { Fail(player, "charge-notstar", mouse.Itemstack.GetName()); return; }
            ItemStack star = mouse.Itemstack;
            if (star.StackSize != 1) { Fail(player, "star-onlyone", star.GetName()); return; }
            decimal stored = StoredEmc.Get(star);
            decimal move = empty ? -stored : Math.Min(PlayerEmc.GetEmc(player), StoredEmc.Capacity(star) - stored);
            if (move == 0) { Fail(player, empty ? "star-empty" : "star-full", star.GetName()); return; }
            PlayerEmc.AddEmcExact(player, -move);
            StoredEmc.Set(star, stored + move);
            mouse.MarkDirty();
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), player.Entity, null, true, 8, 0.6f);
            mod.SendResult(player, Result(player, null));
        }

        void Burn(IServerPlayer player, bool onlyOne)
        {
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            string error = PlanBurn(player, mouse.Itemstack, out BurnPlan plan, out string[] args, byHand: true);
            if (error != null)
            {
                Fail(player, error, args);
                return;
            }

            int count = onlyOne ? 1 : mouse.Itemstack.StackSize;
            ItemStack taken = mouse.TakeOut(count);
            if (taken == null || taken.StackSize <= 0) return;
            mouse.MarkDirty();
            Burned(player, taken, plan);
        }

        /// <summary>A stack was taken from the player to burn: the EMC, the knowledge, the sound and the answer to the window.</summary>
        void Burned(IServerPlayer player, ItemStack taken, BurnPlan plan)
        {
            string learned = ApplyBurn(player, taken, plan);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, Result(player, learned, learned == null ? Array.Empty<string>() : plan.AlsoPlain ? new[] { learned, PlayerEmc.KeyOf(taken.Collectible) } : new[] { learned }));
        }

        /// <summary>
        /// Whether a shift-click may burn what is in this slot (null), and if not why: an error code, or "" when the
        /// slot doesn't hold what the player clicked any more (nothing is done and nothing said then). The slot has
        /// to be one a shift-click burns from (<see cref="ShiftClickBurn.Eligible"/>) and the stack one it burns
        /// (<see cref="ShiftClickBurn.CanBurn"/>); after that the rules are the Burn slot's, except that a Klein
        /// star isn't burned this way.
        /// </summary>
        public string PlanBurnSlot(IServerPlayer player, string playerUid, ItemSlot slot, string code, out BurnPlan plan, out string[] args)
        {
            plan = null;
            args = Array.Empty<string>();
            ItemStack stack = slot?.Itemstack;
            if (stack?.Collectible?.Code == null || stack.StackSize <= 0 || stack.Collectible.Code.ToString() != code) return "";
            if (!ShiftClickBurn.Eligible(playerUid, slot.Inventory, slot) || !ShiftClickBurn.CanBurn(mod, stack))
            {
                args = new[] { stack.GetName() };
                return "shiftburn";
            }
            return PlanBurn(player, stack, out plan, out args);
        }

        /// <summary>A shift-click on a slot of the player's own inventory while the window is open (1.26.8): its whole stack is burned.</summary>
        void BurnSlot(IServerPlayer player, TransmuteRequestPacket req)
        {
            ItemSlot slot = ShiftClickBurn.SlotOf(player, req.SlotInv, req.Slot - 1);
            string error = PlanBurnSlot(player, player.PlayerUID, slot, req.Key, out BurnPlan plan, out string[] args);
            if (error != null)
            {
                // Not what the player saw there: their game gets the slot as it is here. Otherwise the reason.
                if (error.Length == 0) slot?.MarkDirty();
                else FailFor(player, slot.Itemstack, error, args);
                return;
            }
            ItemStack taken = slot.TakeOutWhole();
            if (taken == null || taken.StackSize <= 0) return;
            slot.MarkDirty();
            Burned(player, taken, plan);
        }

        /// <summary>
        /// What a knowledge entry makes: the plain item, or exactly the version that was learned. If an item has since
        /// been switched to "item only", its exact versions make the plain item.
        /// </summary>
        public static ItemStack Prototype(VintageEModSystem mod, IWorldAccessor world, string key)
        {
            ItemStack stack = PlayerEmc.ResolveStack(world, key);
            if (stack == null) return null;
            if (PlayerEmc.IsExact(key) && mod.Emc.StackData.ModeOf(stack.Collectible) == StackDataMode.Item) return new ItemStack(stack.Collectible, 1);
            // A clutter, tapestry or typed-container version whose type the game no longer lists (its mod was removed
            // or renamed it) would be a nameless block with no shape: not offered, not created.
            if (PlayerEmc.IsExact(key) && StackDataUtil.NeedsData(stack.Collectible, world) && !StackDataUtil.VersionExists(world, stack)) return null;
            // Clutter comes out as the game hands it to a player who picked it up (1.26.10).
            StackDataUtil.MarkCollected(stack);
            return stack;
        }

        /// <summary>
        /// Why the table doesn't offer something a player has learned, or null when it does (the doctor's page about a
        /// player counts these, 1.26.10): "gone" (its block or item isn't in the game, or something inside it isn't),
        /// "version" (a typed block's version the game no longer has), "typeless" (a typed block learned without its
        /// type), "novalue" (nothing to make it from now).
        /// </summary>
        public static string WhyNotOffered(VintageEModSystem mod, IWorldAccessor world, string key)
        {
            try
            {
                ItemStack resolved = PlayerEmc.ResolveStack(world, key);
                if (resolved == null) return "gone";
                ItemStack proto = Prototype(mod, world, key);
                if (proto == null) return "version";
                if (StackDataUtil.NeedsData(proto.Collectible, world) && StackDataUtil.KeptData(proto) == null) return "typeless";
                return mod.Emc.GetValue(proto) > 0 ? null : "novalue";
            }
            catch { return "gone"; }
        }

        /// <param name="mode">0 = one onto cursor, 1 = stack onto cursor, 2 = stack into inventory</param>
        void Create(IServerPlayer player, string key, int mode)
        {
            if (string.IsNullOrEmpty(key) || !PlayerEmc.Knows(player, key))
            {
                Fail(player, "unknown");
                return;
            }
            ItemStack proto = Prototype(mod, sapi.World, key);
            double each = proto == null ? 0 : mod.Emc.GetValue(proto);
            if (proto == null || each <= 0)
            {
                Fail(player, "novalue", proto?.GetName() ?? key);
                return;
            }
            // A clutter block or tapestry without its type would be a nameless, broken block: never make one.
            if (StackDataUtil.NeedsData(proto.Collectible, sapi.World) && StackDataUtil.KeptData(proto) == null)
            {
                Fail(player, "novalue", proto.Collectible.Code.ToString());
                return;
            }
            CollectibleObject collectible = proto.Collectible;
            if (mod.Emc.IsNoTransmute(collectible))
            {
                Fail(player, "notransmute", proto.GetName());
                return;
            }

            decimal emc = PlayerEmc.GetEmc(player), eachExact = EmcAmount.From(each);
            int maxStack = Math.Max(1, collectible.MaxStackSize);
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;

            int want;
            if (mode == 2)
            {
                want = maxStack;
            }
            else
            {
                if (!mouse.Empty && !mouse.Itemstack.Equals(sapi.World, proto, GlobalConstants.IgnoredStackAttributes))
                {
                    Fail(player, "cursorbusy");
                    return;
                }
                int room = mouse.Empty ? maxStack : maxStack - mouse.StackSize;
                want = mode == 0 ? Math.Min(1, room) : room;
                if (want <= 0) return;
            }

            int affordable = EmcAmount.HowMany(emc, eachExact);
            int amount = Math.Min(want, affordable);
            if (amount <= 0)
            {
                Fail(player, "notenoughemc", EmcFormat.Format(each), EmcFormat.Total(emc));
                return;
            }

            ItemStack made = proto.Clone();
            made.StackSize = amount;
            // Clothes made from EMC are new (the game would otherwise give them a random, worn condition).
            if (EmcRegistry.ClothingCondition(made) >= 0) made.Attributes.SetFloat("condition", 1);
            if (mode == 2)
            {
                // Whatever could not be given is dropped at the player's feet. The game's TryGiveItemstack answers true
                // as soon as any of the stack went in and leaves the rest in the stack it was handed, so the answer
                // alone isn't enough: until 1.21.1 a stack that only partly fitted lost the rest, though all of it
                // was paid for.
                player.InventoryManager.TryGiveItemstack(made, true);
                if (made.StackSize > 0) sapi.World.SpawnItemEntity(made, player.Entity.Pos.XYZ);
            }
            else
            {
                if (mouse.Empty) mouse.Itemstack = made;
                else mouse.Itemstack.StackSize += amount;
                mouse.MarkDirty();
            }

            PlayerEmc.SetEmc(player, emc - eachExact * amount);
            mod.SendResult(player, Result(player, null));
        }

        internal void Fail(IServerPlayer player, string code, params string[] args) => FailFor(player, player?.InventoryManager?.MouseItemSlot?.Itemstack, code, args);

        /// <summary>A refusal about this stack (the doctor's watch names it), which needn't be the one on the cursor.</summary>
        void FailFor(IServerPlayer player, ItemStack stack, string code, string[] args)
        {
            try { EmcDiagnose.RecordRefusal(player, code, args, stack); }
            catch { /* diagnostics never get in the way */ }
            mod.SendResult(player, new TransmuteResultPacket { ErrorCode = code, Args = args });
        }
    }
}
