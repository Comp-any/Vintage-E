using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;

namespace VintageE
{
    /// <summary>
    /// Gathered on the player's side and sent with each doctor request: hotkeys of other mods (or the game) bound to
    /// the same keys as Vintage-E's, and other mods' Harmony patches on the game methods Vintage-E relies on.
    /// </summary>
    public static partial class EmcDiagnose
    {
        /// <summary>"Ctrl+H (Vintage-E: Open the EMC doctor) = Some mod's hotkey (code)" for every clash with our hotkeys.</summary>
        public static string[] HotkeyConflicts(ICoreClientAPI capi, string[] ours)
        {
            var result = new List<string>();
            try
            {
                var all = capi?.Input?.HotKeys;
                if (all == null) return result.ToArray();
                foreach (string code in ours)
                {
                    if (!all.TryGetValue(code, out HotKey mine) || mine?.CurrentMapping == null) continue;
                    KeyCombination k = mine.CurrentMapping;
                    if (k.KeyCode <= 0) continue;
                    foreach (var kv in all)
                    {
                        HotKey other = kv.Value;
                        if (other == null || kv.Key == code || other.CurrentMapping == null) continue;
                        KeyCombination o = other.CurrentMapping;
                        if (o.KeyCode != k.KeyCode || o.Ctrl != k.Ctrl || o.Alt != k.Alt || o.Shift != k.Shift) continue;
                        // The same kind of control clashes; a GUI hotkey and a movement key on one letter is how the game itself works.
                        bool sameKind = SameKind(other.KeyCombinationType, mine.KeyCombinationType);
                        if (!sameKind) continue;
                        result.Add(Describe(k) + " (" + mine.Name + ") = " + other.Name + " [" + kv.Key + "]");
                    }
                }
            }
            catch (Exception e) { result.Add("could not read the hotkeys: " + e.Message); }
            return result.ToArray();
        }

        static bool SameKind(HotkeyType a, HotkeyType b)
        {
            bool Gui(HotkeyType t) => t == HotkeyType.GUIOrOtherControls || t == HotkeyType.HelpAndOverlays || t == HotkeyType.InventoryHotkeys || t == HotkeyType.CreativeOrSpectatorTool;
            bool Play(HotkeyType t) => t == HotkeyType.CharacterControls || t == HotkeyType.MovementControls || t == HotkeyType.MouseControls || t == HotkeyType.MouseModifiers;
            return Gui(a) == Gui(b) && Play(a) == Play(b);
        }

        static string Describe(KeyCombination k)
        {
            string s = (k.Ctrl ? "Ctrl+" : "") + (k.Alt ? "Alt+" : "") + (k.Shift ? "Shift+" : "");
            try { s += Enum.IsDefined(typeof(GlKeys), k.KeyCode) ? ((GlKeys)k.KeyCode).ToString() : "key " + k.KeyCode; }
            catch { s += "key " + k.KeyCode; }
            return s;
        }

        /// <summary>Game methods whose patching by another mod would change what Vintage-E's tooltips, looks and windows do.</summary>
        static readonly string[] WatchedMethods =
        {
            "ShapeTesselatorManager.LoadBlockShapes", "ShapeTesselatorManager.LoadItemShapes",
            "CollectibleObject.GetHeldItemInfo", "CollectibleObject.GetHeldItemName", "Block.GetPlacedBlockInfo", "Block.GetPlacedBlockName",
            "GuiElementItemstackInfo", "ItemSlot.", "InventoryBase.", "GuiDialog.TryOpen", "GuiComposer.", "Entity.ReceiveDamage", "EntityPlayer."
        };

        /// <summary>"Type.Method ← owner, owner" for every watched method another mod has patched with Harmony.</summary>
        public static string[] HarmonyPatchOwners(string ourId)
        {
            var result = new List<string>();
            try
            {
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    if (m?.DeclaringType == null) continue;
                    string name = m.DeclaringType.Name + "." + m.Name;
                    if (!WatchedMethods.Any(w => w.EndsWith(".", StringComparison.Ordinal) ? name.StartsWith(w, StringComparison.Ordinal) : name == w || name.StartsWith(w + ".", StringComparison.Ordinal) || (w.IndexOf('.') < 0 && m.DeclaringType.Name == w))) continue;
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    var owners = info.Owners.Where(o => o != ourId).Distinct().OrderBy(o => o, StringComparer.Ordinal).ToList();
                    if (owners.Count == 0) continue;
                    result.Add(name + " ← " + string.Join(", ", owners));
                    if (result.Count >= 30) break;
                }
            }
            catch (Exception e) { result.Add("could not read the Harmony patches: " + e.Message); }
            return result.ToArray();
        }

        /// <summary>The client's hotkey clashes and other mods' patches, in the "Your game" check.</summary>
        static void ClientEnvironment(Ctx x, EmcDiagnoseRequestPacket client)
        {
            if (client == null || !client.HasClientInfo) return;
            ClientLook(x, client);
            ClientLookSettings(x, client);
            ClientEmcCraft(x, client);
            var keys = client.ClientHotkeyConflicts ?? Array.Empty<string>();
            if (keys.Length == 0) x.Ok(x.L("diag-hotkeys-ok"));
            else
            {
                x.Warn(x.L("diag-hotkeys-found", keys.Length));
                foreach (string k in keys) x.Note(k);
                x.Explain(x.L("diag-hotkeys-why"));
                x.Fix(x.L("diag-hotkeys-fix"));
            }
            var patches = client.ClientPatchOwners ?? Array.Empty<string>();
            if (patches.Length == 0) { x.Ok(x.L("diag-harmony-ok")); x.Explain(x.L("diag-harmony-ok-why")); }
            else
            {
                x.Info(x.L("diag-harmony-found", patches.Length));
                foreach (string p in patches) x.Note(p);
                x.Explain(x.L("diag-harmony-why"));
                x.Fix(x.L("diag-harmony-fix"));
            }
        }

        /// <summary>The look settings on the player's computer (1.19.1): LookHooks and HotLooksReload.</summary>
        static void ClientLookSettings(Ctx x, EmcDiagnoseRequestPacket client)
        {
            string[] f = (client.ClientLookInfo ?? "").Split('|');
            if (f.Length < 17) return;
            bool hooks = f[15] != "0", hot = f[16] != "0";
            string active = f[0];
            if (!hooks && !string.Equals(active, "Middle", StringComparison.OrdinalIgnoreCase))
            {
                x.Warn(x.L("diag-look-hooksoff", active));
                x.Fix(x.L("diag-look-hooksoff-fix"));
            }
            else x.Info(x.L("diag-look-settings", hooks ? x.L("diag-on") : x.L("diag-off"), hot ? x.L("diag-on") : x.L("diag-off")));
            if (!hot) x.Explain(x.L("diag-look-hotoff-why"));
        }

        /// <summary>The look on the player's computer: which one, where it came from, and whether the game's shape loading went through the hook that applies it.</summary>
        static void ClientLook(Ctx x, EmcDiagnoseRequestPacket client)
        {
            string[] f = (client.ClientLookInfo ?? "").Split('|');
            if (f.Length < 11) return;
            string active = f[0], setting = f[1], serverDefault = f[2].Length == 0 ? x.L("diag-look-nodefault") : f[2];
            int.TryParse(f[3], out int calls); int.TryParse(f[4], out int hooked); int.TryParse(f[5], out int models); int.TryParse(f[6], out int textures);
            int.TryParse(f[8], out int missing); int.TryParse(f[9], out int patched);
            string patchError = f[10];
            int rawCalls = 0, early = 0; string skip = "";
            if (f.Length >= 14) { int.TryParse(f[11], out rawCalls); skip = f[12]; int.TryParse(f[13], out early); }
            string[] trail = f.Length >= 15 && f[14].Length > 0 ? f[14].Split(';') : Array.Empty<string>();
            void Trail() { if (trail.Length > 0) { x.Note(x.L("diag-look-trail")); foreach (string t in trail) x.Note("  " + t); } }
            string serverSetting = x.Mod.Config.Look;
            if (patched < ClientLookPatches.HookCount || patchError.Length > 0)
            {
                x.Warn(x.L("diag-look-nohook", patched, patchError.Length == 0 ? "?" : patchError, ClientLookPatches.HookCount));
                x.Explain(x.L("diag-look-nohook-why"));
                x.Fix(x.L("diag-look-nohook-fix"));
                return;
            }
            if (calls == 0 && models + textures == 0)
            {
                x.Warn(x.L("diag-look-nocalls", active));
                x.Explain(rawCalls == 0 ? x.L("diag-look-nocalls-why") : x.L("diag-look-skipped-why", rawCalls, skip));
                x.Note(x.L("diag-look-early", early));
                x.Fix(x.L("diag-look-nocalls-fix"));
                Trail();
                return;
            }
            if (hooked == 0)
            {
                x.Warn(x.L("diag-look-nothingours", calls));
                x.Explain(x.L("diag-look-nothingours-why"));
                Trail();
                return;
            }
            string from = setting == "Server" ? x.L("diag-look-from-server", serverDefault, serverSetting) : x.L("diag-look-from-setting", setting);
            if (active == "Middle") { x.Info(x.L("diag-look-middle", from)); x.Explain(x.L("diag-look-middle-why")); x.Fix(x.L("diag-look-middle-fix")); }
            else if (models + textures == 0) { x.Warn(x.L("diag-look-noeffect", active, hooked)); x.Explain(x.L("diag-look-noeffect-why")); x.Fix(x.L("diag-look-noeffect-fix")); }
            else
            {
                x.Ok(x.L("diag-look-ok", active, models, textures, from));
                x.Note(x.L("diag-look-how", calls, early));
                if (missing > 0) x.Note(x.L("diag-look-missing", missing));
                Trail();
            }
        }

        /// <summary>What a version difference between client and server breaks, by how far apart they are.</summary>
        static string VersionImpact(Ctx x, string clientVersion, string serverVersion)
        {
            if (!TryParseVersion(clientVersion, out int cm, out int cn, out _) || !TryParseVersion(serverVersion, out int sm, out int sn, out _)) return x.L("diag-client-versiondiff-unknown");
            return cm != sm || cn != sn ? x.L("diag-client-versiondiff-major") : x.L("diag-client-versiondiff-patch");
        }
    }
}
