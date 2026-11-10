using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// A player's personal EMC and transmutation knowledge.
    /// <para>The server keeps both in the player entity's own attributes (<c>Entity.Attributes</c>), which are saved
    /// with the world but sent to nobody, and sends them to their owner alone over the mod's channel: everything at
    /// once on joining, then only what changed (<see cref="PlayerEmcPacket"/>). Until 1.21.1 they sat in the watched
    /// attributes, which the game sends to every player near: each change of EMC - twice a second for anyone with a
    /// trinket running - went to everybody around, and each new piece of knowledge sent the whole list again, to
    /// everybody around. Anyone's EMC and knowledge could also be read by any other player's game.</para>
    /// <para>What an older version saved in the watched attributes is moved over the first time a player is looked at
    /// (<see cref="MigrateTree"/>); nothing is lost. A game client only knows its own player's numbers.</para>
    /// Knowledge entries are "i:domain:code" for items and "b:domain:code" for blocks, so they survive id changes.
    /// </summary>
    public static class PlayerEmc
    {
        public const string EmcKey = "vintageeEmc";
        public const string KnownKey = "vintageeKnown";

        // ------------------------------------------------------------------ the server's copy

        static bool OnServer(IPlayer player) => player is Vintagestory.API.Server.IServerPlayer;

        /// <summary>The server's store for this player (older saves moved over first), or null without an entity.</summary>
        static TreeAttribute ServerTree(IPlayer player)
        {
            Vintagestory.API.Common.Entities.Entity entity = player?.Entity;
            if (entity == null) return null;
            SyncedTreeAttribute watched = entity.WatchedAttributes;
            if (watched != null && (watched.HasAttribute(EmcKey) || watched.HasAttribute(KnownKey)) && MigrateTree(watched, entity.Attributes))
            {
                // Its owner's game gets the whole of it again, in case it was already playing.
                lock (pending) Changes(player).AllKnown = Changes(player).Emc = true;
            }
            return entity.Attributes;
        }

        /// <summary>
        /// Moves EMC and knowledge out of the watched attributes (where versions before 1.21.1 kept them) into the
        /// entity's own. Safe to run any number of times. If both hold something - the world went back to an older
        /// version for a while, which started from nothing there - the two are added together: the EMC summed, the
        /// knowledge joined. Returns true if anything was moved.
        /// </summary>
        public static bool MigrateTree(TreeAttribute watched, TreeAttribute own)
        {
            if (watched == null || own == null || ReferenceEquals(watched, own)) return false;
            bool moved = false;
            if (watched.HasAttribute(EmcKey))
            {
                double old = Sane(watched.GetDouble(EmcKey, 0)), now = Sane(own.GetDouble(EmcKey, 0));
                own.SetDouble(EmcKey, now + old);
                watched.RemoveAttribute(EmcKey);
                moved = true;
            }
            if (watched.HasAttribute(KnownKey))
            {
                string[] old = watched.GetStringArray(KnownKey) ?? Array.Empty<string>();
                string[] now = own.GetStringArray(KnownKey);
                if (now == null || now.Length == 0) own[KnownKey] = new StringArrayAttribute(old);
                else if (old.Length > 0)
                {
                    var seen = new HashSet<string>(now, StringComparer.Ordinal);
                    var joined = new List<string>(now);
                    foreach (string key in old) if (!string.IsNullOrEmpty(key) && seen.Add(key)) joined.Add(key);
                    if (joined.Count != now.Length) own[KnownKey] = new StringArrayAttribute(joined.ToArray());
                }
                watched.RemoveAttribute(KnownKey);
                moved = true;
            }
            return moved;
        }

        static double Sane(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;

        // ------------------------------------------------------------------ what a game client knows (its own player)

        static double clientEmc;
        static string[] clientKnown = Array.Empty<string>();
        /// <summary>Whose numbers the client holds (null: not told yet).</summary>
        static string clientUid;

        static bool IsClientsOwn(IPlayer player) => player != null && (clientUid == null || player.PlayerUID == clientUid);

        /// <summary>Client: what the server sent about this game's own player. Returns true if the knowledge list changed.</summary>
        public static bool ApplyOnClient(PlayerEmcPacket packet, string playerUid)
        {
            if (packet == null) return false;
            clientUid = playerUid;
            if (packet.HasEmc) clientEmc = packet.Emc;
            string[] known = ApplyKnown(clientKnown, packet);
            bool changed = !ReferenceEquals(known, clientKnown);
            clientKnown = known;
            return changed;
        }

        /// <summary>
        /// A knowledge list after a packet: the whole list if it carries one, then what was forgotten taken out and
        /// what was learned added at the end (as the server does). The same array comes back when nothing changes, and
        /// a new one whenever anything does: windows tell by the array that they have to draw the list again.
        /// </summary>
        public static string[] ApplyKnown(string[] current, PlayerEmcPacket packet)
        {
            current ??= Array.Empty<string>();
            if (packet == null) return current;
            string[] result = packet.HasKnown ? (packet.Known ?? Array.Empty<string>()) : current;
            bool forgets = packet.Forgotten != null && packet.Forgotten.Length > 0, learns = packet.Learned != null && packet.Learned.Length > 0;
            if (!forgets && !learns) return result;
            var list = new List<string>(result);
            bool changed = false;
            if (forgets)
            {
                var gone = new HashSet<string>(packet.Forgotten, StringComparer.Ordinal);
                changed |= list.RemoveAll(k => gone.Contains(k)) > 0;
            }
            if (learns)
            {
                var have = new HashSet<string>(list, StringComparer.Ordinal);
                foreach (string key in packet.Learned)
                {
                    if (!string.IsNullOrEmpty(key) && have.Add(key)) { list.Add(key); changed = true; }
                }
            }
            return changed ? list.ToArray() : result;
        }

        // ------------------------------------------------------------------ changes waiting to go to their owners

        /// <summary>
        /// What changed for one player since their game was last told. Single keys are sent as they are; anything
        /// bigger (the Tome of Knowledge, a tidy-up, a key learned and forgotten in the same moment) sends the list whole.
        /// </summary>
        public sealed class PendingChanges
        {
            public bool Emc, AllKnown;
            /// <summary>Key by key: true = now known, false = now forgotten.</summary>
            public readonly Dictionary<string, bool> Keys = new Dictionary<string, bool>(StringComparer.Ordinal);

            public bool Any => Emc || AllKnown || Keys.Count > 0;

            public void Note(string key, bool known)
            {
                if (AllKnown || string.IsNullOrEmpty(key)) return;
                // The other way round a moment ago: the order of the list is the server's business, so send it whole.
                if (Keys.TryGetValue(key, out bool was) && was != known) { AllKnown = true; Keys.Clear(); return; }
                Keys[key] = known;
                if (Keys.Count > 64) { AllKnown = true; Keys.Clear(); }
            }

            /// <summary>The packet for the owner, from the server's numbers as they stand, and a clean slate.</summary>
            public PlayerEmcPacket Take(double emc, string[] known)
            {
                var packet = new PlayerEmcPacket();
                if (Emc) { packet.HasEmc = true; packet.Emc = emc; }
                if (AllKnown) { packet.HasKnown = true; packet.Known = known ?? Array.Empty<string>(); }
                else if (Keys.Count > 0)
                {
                    var learned = new List<string>();
                    var forgotten = new List<string>();
                    foreach (var kv in Keys) (kv.Value ? learned : forgotten).Add(kv.Key);
                    if (learned.Count > 0) packet.Learned = learned.ToArray();
                    if (forgotten.Count > 0) packet.Forgotten = forgotten.ToArray();
                }
                Emc = AllKnown = false;
                Keys.Clear();
                return packet;
            }
        }

        /// <summary>By player UID. Only the server writes here.</summary>
        static readonly Dictionary<string, PendingChanges> pending = new Dictionary<string, PendingChanges>();

        /// <summary>Call with <see cref="pending"/> locked.</summary>
        static PendingChanges Changes(IPlayer player)
        {
            string uid = player.PlayerUID ?? "";
            if (!pending.TryGetValue(uid, out PendingChanges changes)) pending[uid] = changes = new PendingChanges();
            return changes;
        }

        /// <summary>Server: whether anything is waiting to be sent to anyone.</summary>
        public static bool AnyPending { get { lock (pending) return pending.Count > 0; } }

        /// <summary>
        /// Server: the packet with what changed for this player since the last one, or null if nothing did.
        /// <paramref name="everything"/>: the whole state regardless (on joining).
        /// </summary>
        public static PlayerEmcPacket TakeChanges(IPlayer player, bool everything = false)
        {
            if (player == null || !OnServer(player)) return null;
            lock (pending)
            {
                string uid = player.PlayerUID ?? "";
                pending.TryGetValue(uid, out PendingChanges changes);
                if (everything)
                {
                    changes ??= new PendingChanges();
                    changes.Emc = changes.AllKnown = true;
                }
                if (changes == null) return null;
                pending.Remove(uid);
                if (!changes.Any || player.Entity == null) return null;
                return changes.Take(GetEmc(player), GetKnown(player));
            }
        }

        /// <summary>Server: the UIDs with changes waiting (a copy).</summary>
        public static List<string> PendingPlayers()
        {
            lock (pending) return new List<string>(pending.Keys);
        }

        /// <summary>Server: a player left; nothing is owed to their game any more.</summary>
        public static void ForgetPending(string uid)
        {
            lock (pending) pending.Remove(uid ?? "");
        }

        // ------------------------------------------------------------------ reading and changing

        public static double GetEmc(IPlayer player)
        {
            if (!OnServer(player)) return IsClientsOwn(player) ? clientEmc : 0;
            return ServerTree(player)?.GetDouble(EmcKey, 0) ?? 0;
        }

        public static void SetEmc(IPlayer player, double value)
        {
            if (!OnServer(player)) return; // a game client is told its numbers; it never sets them
            TreeAttribute tree = ServerTree(player);
            if (tree == null) return;
            if (double.IsNaN(value) || value < 0) value = 0;
            tree.SetDouble(EmcKey, value);
            lock (pending) Changes(player).Emc = true;
        }

        public static void AddEmc(IPlayer player, double amount) => SetEmc(player, GetEmc(player) + amount);

        public static string[] GetKnown(IPlayer player)
        {
            if (!OnServer(player)) return IsClientsOwn(player) ? clientKnown : Array.Empty<string>();
            return ServerTree(player)?.GetStringArray(KnownKey) ?? Array.Empty<string>();
        }

        /// <summary>A lookup set per knowledge array (the array is replaced whenever knowledge changes, so the set never goes stale).</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string[], HashSet<string>> knownSets = new System.Runtime.CompilerServices.ConditionalWeakTable<string[], HashSet<string>>();

        static HashSet<string> KnownSet(string[] known) => knownSets.GetValue(known, k => new HashSet<string>(k, StringComparer.Ordinal));

        public static bool Knows(IPlayer player, string key) => key != null && KnownSet(GetKnown(player)).Contains(key);

        /// <summary>Server: stores a new knowledge list (always a new array: the lookup sets and windows go by the array).</summary>
        static void StoreKnown(TreeAttribute tree, string[] keys) => tree[KnownKey] = new StringArrayAttribute(keys ?? Array.Empty<string>());

        /// <summary>Adds the key; returns true if it was new.</summary>
        public static bool Learn(IPlayer player, string key)
        {
            if (!OnServer(player) || string.IsNullOrEmpty(key)) return false;
            TreeAttribute tree = ServerTree(player);
            if (tree == null) return false;
            string[] known = GetKnown(player);
            if (KnownSet(known).Contains(key)) return false;
            var list = new string[known.Length + 1];
            Array.Copy(known, list, known.Length);
            list[known.Length] = key;
            StoreKnown(tree, list);
            lock (pending) Changes(player).Note(key, true);
            return true;
        }

        /// <summary>Learns many keys at once; returns how many were new.</summary>
        public static int LearnMany(IPlayer player, IEnumerable<string> keys)
        {
            if (!OnServer(player)) return 0;
            TreeAttribute tree = ServerTree(player);
            if (tree == null) return 0;
            var set = new HashSet<string>(GetKnown(player));
            int before = set.Count;
            foreach (string k in keys) if (!string.IsNullOrEmpty(k)) set.Add(k);
            if (set.Count == before) return 0;
            StoreKnown(tree, set.ToArray());
            lock (pending) Changes(player).AllKnown = true;
            return set.Count - before;
        }

        /// <summary>Replaces a player's whole knowledge list (the doctor's prune of entries that no longer exist).</summary>
        public static void SetKnown(IPlayer player, string[] keys)
        {
            if (!OnServer(player)) return;
            TreeAttribute tree = ServerTree(player);
            if (tree == null) return;
            StoreKnown(tree, keys == null ? Array.Empty<string>() : (string[])keys.Clone());
            lock (pending) Changes(player).AllKnown = true;
        }

        public static bool Forget(IPlayer player, string key)
        {
            if (!OnServer(player) || key == null) return false;
            TreeAttribute tree = ServerTree(player);
            if (tree == null) return false;
            string[] known = GetKnown(player);
            if (!KnownSet(known).Contains(key)) return false;
            StoreKnown(tree, known.Where(k => k != key).ToArray());
            lock (pending) Changes(player).Note(key, false);
            return true;
        }

        public static string KeyOf(CollectibleObject collectible)
        {
            if (collectible?.Code == null) return null;
            return (collectible.ItemClass == EnumItemClass.Block ? "b:" : "i:") + collectible.Code.ToString();
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 3 || key[1] != ':') return null;
            if (IsExact(key)) return Resolve(world, PlainKeyOf(key));
            var code = new AssetLocation(key.Substring(2));
            return key[0] == 'b' ? world.GetBlock(code) : (CollectibleObject)world.GetItem(code);
        }

        /// <summary>
        /// Exact versions (a filled bucket, a gold round shield, a chiseled block) are known as
        /// "e:" + the plain key + "|" + their stored data (base64), so each version is its own entry.
        /// </summary>
        public const string ExactPrefix = "e:";

        public static bool IsExact(string key) => key != null && key.StartsWith(ExactPrefix, StringComparison.Ordinal) && key.IndexOf('|') > 2;

        public static string ExactKey(CollectibleObject c, TreeAttribute data)
        {
            string plain = KeyOf(c);
            return plain == null || data == null ? plain : ExactPrefix + plain + "|" + StackDataUtil.Encode(data);
        }

        /// <summary>"i:game:bucket" for both "i:game:bucket" and an exact "e:i:game:bucket|...".</summary>
        public static string PlainKeyOf(string key)
        {
            if (!IsExact(key)) return key;
            int bar = key.IndexOf('|');
            return key.Substring(ExactPrefix.Length, bar - ExactPrefix.Length);
        }

        /// <summary>A single item for a knowledge key, with its stored data for exact versions (null if it no longer exists).</summary>
        public static ItemStack ResolveStack(IWorldAccessor world, string key)
        {
            CollectibleObject c = Resolve(world, key);
            if (c == null) return null;
            ItemStack stack;
            try { stack = new ItemStack(c, 1); }
            catch { return null; }
            if (IsExact(key))
            {
                TreeAttribute data = StackDataUtil.Decode(key.Substring(key.IndexOf('|') + 1));
                if (data == null) return null;
                foreach (var kv in data) stack.Attributes[kv.Key] = kv.Value.Clone();
                StackDataUtil.ResolveNested(world, stack);
                // Something inside it (a modded liquid in a bucket, a modded rock in a chiseled block) is gone with
                // its mod: better no entry than a container of nothing, or one worth the empty container.
                if (!StackDataUtil.NestedResolved(stack)) return null;
            }
            return stack;
        }

        /// <summary>
        /// Re-keys a player's exact knowledge the way it would be learned today and drops the duplicates: entries
        /// learned before 1.18.3 kept the liquid's freshness (one entry per bucket of milk), and liquid containers
        /// learned on 1.18.0 kept their fill level. Returns how many entries went.
        /// </summary>
        public static int Tidy(IPlayer player, IWorldAccessor world)
        {
            string[] known = GetKnown(player);
            if (known.Length == 0) return 0;
            var kept = new List<string>(known.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;
            foreach (string key in known)
            {
                string now = key;
                if (IsExact(key))
                {
                    try
                    {
                        ItemStack stack = ResolveStack(world, key);
                        if (stack != null)
                        {
                            TreeAttribute data = StackDataUtil.KeptData(StackDataUtil.FilledLiquid(stack, world));
                            now = data == null ? KeyOf(stack.Collectible) : ExactKey(stack.Collectible, data);
                        }
                    }
                    catch { now = key; }
                }
                if (now != key) changed = true;
                if (seen.Add(now)) kept.Add(now); else changed = true;
            }
            if (!changed) return 0;
            SetKnown(player, kept.ToArray());
            return known.Length - kept.Count;
        }

        /// <summary>
        /// How many exact versions a player knows that count against MaxExactKnowledge: a filled bucket, a gold
        /// shield, a chiseled block. A block that is nothing without its data (clutter, a tapestry pattern, a fruit
        /// tree cutting) is only ever known as an exact version, so those don't count: they are the plain items of
        /// their kind, and with the Tome of Knowledge there are thousands of them.
        /// </summary>
        public static int CountExact(IPlayer player)
        {
            IWorldAccessor world = player?.Entity?.World;
            int n = 0;
            foreach (string key in GetKnown(player))
            {
                if (!IsExact(key)) continue;
                if (world != null)
                {
                    CollectibleObject c = Resolve(world, key);
                    // Gone with its mod: it sits in the list harmlessly and counts for nothing (the doctor says so).
                    if (c == null || NeedsDataCached(c, world)) continue;
                }
                n++;
            }
            return n;
        }

        static readonly Dictionary<CollectibleObject, bool> needsData = new Dictionary<CollectibleObject, bool>();
        /// <summary>Forgets the typed-block answers (they hold a world's collectibles; called when a world ends).</summary>
        public static void ForgetWorld()
        {
            lock (needsData) needsData.Clear();
            lock (pending) pending.Clear();
        }

        /// <summary>A game client left its world: its player's numbers go with it.</summary>
        public static void ForgetClient()
        {
            clientEmc = 0;
            clientKnown = Array.Empty<string>();
            clientUid = null;
        }
        static bool NeedsDataCached(CollectibleObject c, IWorldAccessor world)
        {
            if (c == null) return false;
            lock (needsData)
            {
                if (!needsData.TryGetValue(c, out bool v)) needsData[c] = v = StackDataUtil.NeedsData(c, world);
                return v;
            }
        }
    }
}
