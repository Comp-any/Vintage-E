using System.Collections.Generic;
using ProtoBuf;

namespace VintageE
{
    /// <summary>Server → client: the full EMC table, by numeric id (ids are identical on both sides in a session).</summary>
    [ProtoContract]
    public class EmcTablePacket
    {
        [ProtoMember(1, IsPacked = true)] public int[] ItemIds;
        [ProtoMember(2, IsPacked = true)] public double[] ItemValues;
        [ProtoMember(3, IsPacked = true)] public int[] BlockIds;
        [ProtoMember(4, IsPacked = true)] public double[] BlockValues;
        [ProtoMember(5)] public bool DurabilityScales;
        /// <summary>Blocks/items that carry contents or a material: "i:code"/"b:code" keys, with kind × 4 + mode.</summary>
        [ProtoMember(6)] public string[] DataKeys;
        [ProtoMember(7)] public byte[] DataKindModes;
        /// <summary>Material-specific values: key, the data it applies to (base64 tree attributes), value.</summary>
        [ProtoMember(8)] public string[] VariantKeys;
        [ProtoMember(9)] public string[] VariantData;
        [ProtoMember(10, IsPacked = true)] public double[] VariantValues;
        /// <summary>The server's ProjectESounds setting (for sounds the client plays itself, like Swiftwolf's wind while flying).</summary>
        [ProtoMember(11)] public bool ProjectESounds;
        /// <summary>Blocks/items on the server's NoTransmute list (valued, but can't be burned, learned or created).</summary>
        [ProtoMember(12)] public string[] NoTransmuteKeys;
        /// <summary>The server's multipliers, so tooltips can say when the numbers in them are scaled: trinket/gadget costs, machine speed, the watch's speed-up and slow-down.</summary>
        /// <remarks>
        /// IsRequired: a 0 (trinkets cost nothing) is written too. Without it a zero is left out of the packet and the
        /// 1 below stays, so until 1.24.1 players' tooltips never said that the costs were scaled to nothing.
        /// </remarks>
        [ProtoMember(13, IsRequired = true)] public double TrinketCostMultiplier = 1;
        [ProtoMember(14)] public double MachineSpeedMultiplier = 1;
        [ProtoMember(15)] public double WatchSpeedUp = 10;
        [ProtoMember(16)] public double WatchSlowDown = 3;
        /// <summary>The server's TableUseRange (1.24.1): the table's window closes where the server stops answering it.</summary>
        [ProtoMember(17)] public double TableUseRange = 8;
        /// <summary>The server's CollectorFuels (1.24.1), so a click on a collector with a fuel the server added is sent on.</summary>
        [ProtoMember(18)] public string[] CollectorFuels;
    }

    /// <summary>
    /// Server → one client: that player's own EMC and knowledge. Everything on joining (<see cref="HasEmc"/> and
    /// <see cref="HasKnown"/>), then only what changed: the balance, single keys learned or forgotten, or the whole
    /// list again after a big change.
    /// </summary>
    [ProtoContract]
    public class PlayerEmcPacket
    {
        [ProtoMember(1)] public double Emc;
        [ProtoMember(2)] public bool HasEmc;
        /// <summary>The whole knowledge list (only when <see cref="HasKnown"/>).</summary>
        [ProtoMember(3)] public string[] Known;
        [ProtoMember(4)] public bool HasKnown;
        [ProtoMember(5)] public string[] Learned;
        [ProtoMember(6)] public string[] Forgotten;
    }

    public enum TransmuteAction
    {
        None = 0,
        /// <summary>Burn what the player holds on the mouse cursor. Mode 0 = whole stack, 1 = one item.</summary>
        Burn = 1,
        /// <summary>Create a known item. Mode 0 = one onto the cursor, 1 = a stack onto the cursor, 2 = a stack into the inventory.</summary>
        Create = 2,
        /// <summary>The GUI was closed.</summary>
        Close = 3,
        /// <summary>Remove an item from the player's knowledge.</summary>
        Forget = 4,
        /// <summary>Tablet magnet slot: swap what's on the cursor (a Black Hole Band or Void Ring, or nothing) with the magnet.</summary>
        MagnetSwap = 5,
        /// <summary>Tablet magnet filter: Mode = filter slot; what's on the cursor goes in it (an empty cursor clears it).</summary>
        MagnetFilter = 6,
        /// <summary>Tablet magnet: switch between "only these" and "everything except these".</summary>
        MagnetMode = 7,
        /// <summary>Learn what's on the mouse cursor without burning it (it stays on the cursor).</summary>
        Learn = 8,
        /// <summary>Gem of Eternal Density filter: Mode = filter slot; what's on the cursor goes in it (an empty cursor clears it).</summary>
        GemFilter = 9,
        /// <summary>Gem of Eternal Density: switch between "all cheap things except these" and "only these".</summary>
        GemMode = 10,
        /// <summary>Charge slot (1.18.11): a Klein star on the cursor is filled from the player's EMC (Mode 0) or emptied into it (Mode 1); it stays on the cursor.</summary>
        Charge = 11
    }

    public enum EmcCraftAction
    {
        None = 0,
        /// <summary>The Arcane Tablet's crafting window opened.</summary>
        Open = 1,
        /// <summary>The window closed: the grid is emptied (tools made from EMC burned back).</summary>
        Close = 2,
        /// <summary>Emc Craft: fill the grid for a recipe from the player's EMC.</summary>
        Fill = 3,
        /// <summary>Server → client: what the server's grid holds (Stacks), so the window can check it shows the same.</summary>
        Contents = 4
    }

    /// <summary>Client → server: the Arcane Tablet's crafting window (1.19.0).</summary>
    [ProtoContract]
    public class EmcCraftPacket
    {
        [ProtoMember(1)] public EmcCraftAction Action;
        /// <summary>The recipe's place in the game's grid recipe list (checked against the name and output).</summary>
        [ProtoMember(2)] public int RecipeIndex = -1;
        [ProtoMember(3)] public string RecipeName;
        [ProtoMember(4)] public string OutputCode;
        /// <summary>Contents: one entry per grid cell (ItemStack.ToBytes; empty for an empty cell).</summary>
        [ProtoMember(5)] public List<byte[]> Stacks;
    }

    /// <summary>Client → server request from the transmutation GUI.</summary>
    [ProtoContract]
    public class TransmuteRequestPacket
    {
        [ProtoMember(1)] public TransmuteAction Action;
        [ProtoMember(2)] public string Key;
        [ProtoMember(3)] public int Mode;
    }

    /// <summary>Server → client outcome; ErrorCode maps to the lang key "vintagee:error-{code}".</summary>
    [ProtoContract]
    public class TransmuteResultPacket
    {
        [ProtoMember(1)] public string ErrorCode;
        [ProtoMember(2)] public string[] Args;
        [ProtoMember(3)] public string LearnedKey;
        /// <summary>The player's EMC after the action, so the window can show it before the entity's attributes sync.</summary>
        [ProtoMember(4)] public double Emc;
        [ProtoMember(5)] public bool HasEmc;
        /// <summary>Every knowledge key this action added (the exact key and, when it applies, the plain one).</summary>
        [ProtoMember(6)] public string[] LearnedKeys;
    }

    /// <summary>Server → client: the blocks/items still without an EMC value, for the /emc missing window. Keys are "i:code" / "b:code".</summary>
    [ProtoContract]
    public class EmcMissingPacket
    {
        [ProtoMember(1)] public string[] Keys;
        [ProtoMember(2)] public int CreativeTotal;
        [ProtoMember(3)] public int ValueCount;
        [ProtoMember(4)] public string Version;
        [ProtoMember(5)] public string File;
        /// <summary>True: this is the /emc blacklist window (Keys are the blacklisted blocks/items).</summary>
        [ProtoMember(6)] public bool Blacklist;
        /// <summary>Why each one has no value (same order as Keys; missing window only).</summary>
        [ProtoMember(7)] public string[] Why;
        /// <summary>
        /// The other list, mixed in on request: in the missing window everything blacklisted ("Show blacklisted"),
        /// in the blacklist window everything missing (untick "Hide missing").
        /// </summary>
        [ProtoMember(8)] public string[] OtherKeys;
        /// <summary>Why each of <see cref="OtherKeys"/> has no value (blacklist window only, same order).</summary>
        [ProtoMember(9)] public string[] OtherWhy;
        /// <summary>Which window this is (<see cref="EmcListKind"/>); 0 with Blacklist set means the blacklist window.</summary>
        [ProtoMember(10)] public byte Kind;
    }

    /// <summary>Client → server: set a block/item's EMC value from the /emc missing window (admins only). Key is "i:code" / "b:code".</summary>
    [ProtoContract]
    public class EmcSetValuePacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public double Value;
        /// <summary>Sent from the blacklist window (the reply reopens that one).</summary>
        [ProtoMember(3)] public bool FromBlacklist;
        /// <summary>Several blocks/items at once (Key is then unused).</summary>
        [ProtoMember(4)] public string[] Keys;
        /// <summary>The window it was sent from (<see cref="EmcListKind"/>), which is shown again afterwards.</summary>
        [ProtoMember(5)] public byte FromKind;
        /// <summary>Sent from the hovered-item box: no window is opened afterwards, only the chat line.</summary>
        [ProtoMember(6)] public bool Quiet;
        /// <summary>Take the pinned value off instead (Value is unused).</summary>
        [ProtoMember(7)] public bool Clear;
    }

    /// <summary>Client → server: what the hovered-item box (Ctrl+K) needs to know about one block/item (admins only).</summary>
    [ProtoContract]
    public class EmcItemStateRequestPacket
    {
        [ProtoMember(1)] public string Key;
    }

    /// <summary>Server → client: one block/item's value and lists, to open the hovered-item box with.</summary>
    [ProtoContract]
    public class EmcItemStatePacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public double Value;
        /// <summary>Its value is set by hand (FixedValues).</summary>
        [ProtoMember(3)] public bool Pinned;
        /// <summary>On the blacklist (and not a free ingredient).</summary>
        [ProtoMember(4)] public bool Blacklisted;
        [ProtoMember(5)] public bool Gray;
        [ProtoMember(6)] public bool Free;
        /// <summary>Which of those come from the value files rather than the server config ("blacklist", "gray", "free"), so they can't all be undone here.</summary>
        [ProtoMember(7)] public string[] FromFiles;
    }

    /// <summary>Server → client: opens the /emc dump or /emc loop window (see <see cref="EmcReport"/>).</summary>
    [ProtoContract]
    public class EmcReportPacket
    {
        /// <summary>"dump" or "loops".</summary>
        [ProtoMember(1)] public string Kind;
        [ProtoMember(2)] public string Title;
        /// <summary>The block/item a dump explains (PlayerEmc key).</summary>
        [ProtoMember(3)] public string Key;
        /// <summary>Where the server saved the same text.</summary>
        [ProtoMember(4)] public string File;
        /// <summary>One entry per line; blocks and items written as [[key]].</summary>
        [ProtoMember(5)] public string[] Texts;
        [ProtoMember(6)] public byte[] Indents;
        /// <summary><see cref="EmcLineStyle"/> per line.</summary>
        [ProtoMember(7)] public byte[] Styles;
        /// <summary>Which block each line is in (the search shows whole blocks; 0 is always shown).</summary>
        [ProtoMember(8)] public int[] Blocks;
        /// <summary>For a dump of an exact stack (held or hovered): that stack, so Refresh explains the same one.</summary>
        [ProtoMember(9)] public byte[] Stack;
    }

    /// <summary>Client → server: explain one block/item (an item clicked in the dump or loop window), or list the loops again (admins only).</summary>
    [ProtoContract]
    public class EmcDumpRequestPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool Loops;
        /// <summary>The exact stack to explain (ItemStack.ToBytes: its wear, contents and size count); null for a new one.</summary>
        [ProtoMember(3)] public byte[] Stack;
    }

    /// <summary>Client → server: an /emc diagnose report (the command, a Refresh, or an answer link clicked in the window). Admins only.</summary>
    [ProtoContract]
    public class EmcDiagnoseRequestPacket
    {
        /// <summary>See <see cref="EmcDiagnose"/>: "", "hand", "item", "mod", "mods", "sporadic", "mapping".</summary>
        [ProtoMember(1)] public string Action;
        /// <summary>A block/item code or a mod id, for "item" and "mod".</summary>
        [ProtoMember(2)] public string Arg;
        /// <summary>What the player holds (ItemStack.ToBytes), so "hand" and "sporadic" can look at the exact stack.</summary>
        [ProtoMember(3)] public byte[] Stack;
        /// <summary>The player's game: Vintage-E version there, its EMC table (count and fingerprint, see EmcRegistry.Fingerprint),
        /// how many of the look patches applied, and whether the held item has the value tooltip behavior.</summary>
        [ProtoMember(4)] public string ClientVersion;
        [ProtoMember(5)] public int ClientTableCount;
        [ProtoMember(6)] public long ClientTableHash;
        [ProtoMember(7)] public int ClientPatched;
        [ProtoMember(8)] public bool ClientHeldHasTooltip;
        [ProtoMember(9)] public bool HasClientInfo;
        [ProtoMember(10)] public int ClientTableVersion;
        /// <summary>Vintage-E error and warning lines from the player's client-main.log (the last few), for the server-side report.</summary>
        [ProtoMember(11)] public string[] ClientLog;
        /// <summary>Tooltips that threw in the player's game: "family: count: last error".</summary>
        [ProtoMember(12)] public string[] ClientTooltipFailures;
        /// <summary>Our hotkeys that another hotkey shares a key with ("Ctrl+H (Vintage-E: …) = Some mod: …").</summary>
        [ProtoMember(13)] public string[] ClientHotkeyConflicts;
        /// <summary>Other mods' Harmony patches on the game methods Vintage-E relies on ("Type.Method ← owner, owner").</summary>
        [ProtoMember(14)] public string[] ClientPatchOwners;
        /// <summary>The look on this computer: active|setting|server default|hook calls|hooked blocks+items|models|textures|collectibles|missing models|patched methods|patch error.</summary>
        [ProtoMember(15)] public string ClientLookInfo;
        /// <summary>Emc Craft on this computer (1.19.1): <see cref="HandbookPatches.Info"/>, then "|" tablet carried now, grid open.</summary>
        [ProtoMember(16)] public string ClientEmcCraftInfo;
        /// <summary>The block the player is looking at on their computer (the server only knows the last one they clicked).</summary>
        [ProtoMember(17)] public bool HasLookedAt;
        [ProtoMember(18)] public int LookedAtX;
        [ProtoMember(19)] public int LookedAtY;
        [ProtoMember(20)] public int LookedAtZ;
        [ProtoMember(21)] public int LookedAtDim;
    }

    /// <summary>Client → server: the hovered-item box's changes: a value (or unpin), and the blacklist, gray list and free ingredient boxes.</summary>
    [ProtoContract]
    public class EmcItemEditPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool SetValue;
        [ProtoMember(3)] public double Value;
        [ProtoMember(4)] public bool Unpin;
        [ProtoMember(5)] public bool Blacklist;
        [ProtoMember(6)] public bool Gray;
        [ProtoMember(7)] public bool Free;
    }

    /// <summary>Server → client: opens (or refreshes) the /emc recipe window with the hand-written recipes in the server config.</summary>
    [ProtoContract]
    public class EmcRecipeListPacket
    {
        /// <summary>The CustomRecipes setting, one "output = ingredients" line each.</summary>
        [ProtoMember(1)] public string[] Lines;
        /// <summary>Why each line isn't used ("" when it is), same order.</summary>
        [ProtoMember(2)] public string[] Problems;
        /// <summary>The output's EMC value now, formatted ("" for none), same order.</summary>
        [ProtoMember(3)] public string[] Values;
        [ProtoMember(4)] public string Message;
        [ProtoMember(5)] public bool Error;
        /// <summary>How many recipes come from value files (read only, not listed).</summary>
        [ProtoMember(6)] public int FromFiles;
    }

    /// <summary>Client → server: add a hand-written recipe (or with Remove, take one off) from the /emc recipe window (admins only).</summary>
    [ProtoContract]
    public class EmcRecipeEditPacket
    {
        [ProtoMember(1)] public string Line;
        [ProtoMember(2)] public bool Remove;
        /// <summary>When editing: the line this one replaces.</summary>
        [ProtoMember(3)] public string Replaces;
    }

    /// <summary>Client → server: put a block/item on the blacklist (Add) or take it off, from the missing/blacklist windows (admins only).</summary>
    [ProtoContract]
    public class EmcBlacklistPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool Add;
        [ProtoMember(3)] public bool FromBlacklist;
        /// <summary>Several blocks/items at once (Key is then unused).</summary>
        [ProtoMember(4)] public string[] Keys;
        /// <summary>A whole mod (its domain) instead of single blocks/items.</summary>
        [ProtoMember(5)] public string Domain;
        /// <summary>The list to change: <see cref="EmcListKind.NoTransmute"/> for the no-transmute list, otherwise the blacklist.</summary>
        [ProtoMember(6)] public byte List;
        /// <summary>The window it was sent from (<see cref="EmcListKind"/>), which is shown again afterwards.</summary>
        [ProtoMember(7)] public byte FromKind;
    }

    /// <summary>Server → client: every block/item that can hold contents or carry a material, for the /emc contents window.</summary>
    [ProtoContract]
    public class EmcContentsPacket
    {
        /// <summary>"i:code" / "b:code".</summary>
        [ProtoMember(1)] public string[] Keys;
        /// <summary>StackDataKind per key.</summary>
        [ProtoMember(2)] public byte[] Kinds;
        /// <summary>StackDataMode in effect per key.</summary>
        [ProtoMember(3)] public byte[] Modes;
        /// <summary>The mode it would have without the server config (built-in default or value file).</summary>
        [ProtoMember(4)] public byte[] DefaultModes;
        /// <summary>True where the server config sets the mode.</summary>
        [ProtoMember(5)] public bool[] FromConfig;
        [ProtoMember(6)] public string Version;
    }

    /// <summary>Client → server: switch blocks/items to a contents mode from the /emc contents window (admins only). Mode -1 = back to the default.</summary>
    [ProtoContract]
    public class EmcContentsSetPacket
    {
        [ProtoMember(1)] public string[] Keys;
        [ProtoMember(2)] public int Mode;
    }

    /// <summary>Server → client: every server setting and its value as text, for the /emc configure window.</summary>
    [ProtoContract]
    public class EmcConfigPacket
    {
        [ProtoMember(1)] public string[] Names;
        /// <summary>The value as editable text: true/false, a number, one list entry per line, or "key = value" per line.</summary>
        [ProtoMember(2)] public string[] Values;
        /// <summary>What happened to the last change (empty when just opening).</summary>
        [ProtoMember(3)] public string Message;
        [ProtoMember(4)] public bool Error;
        /// <summary>The setting to show selected.</summary>
        [ProtoMember(5)] public string Selected;
        /// <summary>Each setting's default, in the same form as Values (same order as Names).</summary>
        [ProtoMember(6)] public string[] Defaults;
    }

    /// <summary>Client → server: change one setting from the /emc configure window (admins only). Text is in the same form as <see cref="EmcConfigPacket.Values"/>.</summary>
    [ProtoContract]
    public class EmcConfigSetPacket
    {
        [ProtoMember(1)] public string Name;
        [ProtoMember(2)] public string Text;
        /// <summary>Put the setting back to its default instead (Name "*" = every setting).</summary>
        [ProtoMember(3)] public bool Reset;
    }

    /// <summary>Server → client: opens (or refreshes) the /emc compat window: every compatibility value file and its state.</summary>
    [ProtoContract]
    public class EmcCompatListPacket
    {
        /// <summary>Asset locations ("vintagee:config/vintagee-emc-compat/primitivesurvival.json"), the keys <see cref="EmcCompatSetPacket"/> uses.</summary>
        [ProtoMember(1)] public string[] Keys;
        [ProtoMember(2)] public string[] Names;
        [ProtoMember(3)] public string[] Descriptions;
        /// <summary>The mod ids each file needs, comma-separated.</summary>
        [ProtoMember(4)] public string[] Mods;
        /// <summary>The first required mod that isn't loaded ("" when they all are).</summary>
        [ProtoMember(5)] public string[] MissingMods;
        [ProtoMember(6)] public bool[] Disabled;
        [ProtoMember(7, IsPacked = true)] public int[] RuleCounts;
        /// <summary>How many items each file valued in the last mapping.</summary>
        [ProtoMember(8, IsPacked = true)] public int[] UsedCounts;
        [ProtoMember(9)] public string Message;
        [ProtoMember(10)] public bool Error;
    }

    /// <summary>Client → server: disable or enable a compatibility value file from the /emc compat window (admins only); the server remaps.</summary>
    [ProtoContract]
    public class EmcCompatSetPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool Disabled;
    }

    /// <summary>Server → client: the Watch of Flowing Time's current world time speed modifier (0 = none).</summary>
    [ProtoContract]
    public class WatchSpeedPacket
    {
        [ProtoMember(1)] public float Modifier;
    }

    /// <summary>Client → server: the fire-projectile key was pressed (shoot the held lens).</summary>
    [ProtoContract]
    public class FireProjectilePacket
    {
        [ProtoMember(1)] public int Hand;
    }

    /// <summary>
    /// Client → server: one of ProjectE's keys (extra, charge, uncharge, nightvision, stepassist).
    /// Server → client: what came of it, shown on screen (<see cref="Message"/>).
    /// </summary>
    [ProtoContract]
    public class KeyActionPacket
    {
        [ProtoMember(1)] public string Action;
        [ProtoMember(2)] public string Message;
    }
}
