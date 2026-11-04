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
            sapi.Event.RegisterGameTickListener(_ => TabletMagnet.Tick(mod, sapi, this), 250);
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
                case TransmuteAction.Learn: LearnHeld(player); break;
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
                if (block is BlockTransmutationTable && player.Entity.Pos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) <= range * range)
                {
                    return true;
                }
                tableSessions.Remove(player.PlayerUID);
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
        public string PlanBurn(IServerPlayer player, ItemStack stack, out BurnPlan plan, out string[] args, bool learnOnly = false)
        {
            plan = null;
            args = new[] { stack?.GetName() ?? "?" };
            if (stack?.Collectible == null) return "novalue";
            // A Klein star is never burned (what it holds would be lost): not by a hopper into the table, nor the magnet.
            if (stack.Collectible is IEmcStorageItem && !learnOnly) return "star-noburn";
            StackDataTable data = mod.Emc.StackData;
            StackDataMode mode = data.ModeOf(stack.Collectible);
            bool keepsData = data.CarriesData(stack.Collectible) && mode != StackDataMode.Item;

            // Item only: what's inside would be lost, so containers that hold something are refused.
            if (!learnOnly && !keepsData && mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack)) return "hascontents";

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
            if (mod.Emc.IsNoTransmute(stack.Collectible)) return "notransmute";

            // Items that keep their contents/material are learned as exactly this version.
            TreeAttribute kept = keepsData ? StackDataUtil.KeptData(stack) : null;
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

        void Burn(IServerPlayer player, bool onlyOne)
        {
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            // A Klein star dropped on Burn isn't burned (what it holds would be lost): left-click fills it from your
            // EMC, right-click empties it into your EMC, and it stays on the cursor (as in ProjectE).
            if (mouse.Itemstack.Collectible is IEmcStorageItem)
            {
                ItemStack star = mouse.Itemstack;
                if (star.StackSize != 1) { Fail(player, "star-onlyone", star.GetName()); return; }
                double stored = StoredEmc.Get(star);
                double move = onlyOne ? -stored : Math.Min(PlayerEmc.GetEmc(player), StoredEmc.Capacity(star) - stored);
                if (Math.Abs(move) < 1e-9) { Fail(player, onlyOne ? "star-empty" : "star-full", star.GetName()); return; }
                PlayerEmc.AddEmc(player, -move);
                StoredEmc.Set(star, stored + move);
                mouse.MarkDirty();
                sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), player.Entity, null, true, 8, 0.6f);
                mod.SendResult(player, Result(player, null));
                return;
            }
            string error = PlanBurn(player, mouse.Itemstack, out BurnPlan plan, out string[] args);
            if (error != null)
            {
                Fail(player, error, args);
                return;
            }

            int count = onlyOne ? 1 : mouse.Itemstack.StackSize;
            ItemStack taken = mouse.TakeOut(count);
            if (taken == null || taken.StackSize <= 0) return;
            mouse.MarkDirty();

            string learned = ApplyBurn(player, taken, plan);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, Result(player, learned, learned == null ? Array.Empty<string>() : plan.AlsoPlain ? new[] { learned, PlayerEmc.KeyOf(taken.Collectible) } : new[] { learned }));
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
            if (PlayerEmc.IsExact(key) && StackDataUtil.NeedsData(stack.Collectible, world) && !KnownVersion(world, stack)) return null;
            return stack;
        }

        /// <summary>True when the game's creative inventory lists this version of a typed block (or lists none at all, like cuttings).</summary>
        static bool KnownVersion(IWorldAccessor world, ItemStack stack)
        {
            TreeAttribute kept = StackDataUtil.KeptData(stack);
            if (kept == null) return true;
            bool any = false;
            foreach (ItemStack version in StackDataUtil.CreativeVersions(stack.Collectible, world))
            {
                any = true;
                if (version?.Attributes == null) continue;
                if (StackDataUtil.IsSubset(world, kept, version.Attributes)) return true;
            }
            return !any;
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

            double emc = PlayerEmc.GetEmc(player);
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

            int affordable = (int)Math.Min(int.MaxValue, Math.Floor(emc / each + 1e-9));
            int amount = Math.Min(want, affordable);
            if (amount <= 0)
            {
                Fail(player, "notenoughemc", EmcFormat.Format(each), EmcFormat.Format(emc));
                return;
            }

            ItemStack made = proto.Clone();
            made.StackSize = amount;
            // Clothes made from EMC are new (the game would otherwise give them a random, worn condition).
            if (EmcRegistry.ClothingCondition(made) >= 0) made.Attributes.SetFloat("condition", 1);
            if (mode == 2)
            {
                // Same pattern as vanilla: whatever could not be given is dropped at the player's feet.
                if (!player.InventoryManager.TryGiveItemstack(made, true))
                {
                    sapi.World.SpawnItemEntity(made, player.Entity.Pos.XYZ);
                }
            }
            else
            {
                if (mouse.Empty) mouse.Itemstack = made;
                else mouse.Itemstack.StackSize += amount;
                mouse.MarkDirty();
            }

            PlayerEmc.SetEmc(player, emc - each * amount);
            mod.SendResult(player, Result(player, null));
        }

        void Fail(IServerPlayer player, string code, params string[] args)
        {
            try { EmcDiagnose.RecordRefusal(player, code, args, player?.InventoryManager?.MouseItemSlot?.Itemstack); }
            catch { /* diagnostics never get in the way */ }
            mod.SendResult(player, new TransmuteResultPacket { ErrorCode = code, Args = args });
        }
    }
}
