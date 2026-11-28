using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// Settings for this computer only (ModConfig/vintagee-client.json), changed in the window the <c>.vintagee</c>
    /// command opens: the look of Vintage-E's blocks and items, and how the Watch of Flowing Time shows that it's on.
    /// </summary>
    public class VintageEClientConfig
    {
        public const string FileName = "vintagee-client.json";

        /// <summary>Look = Server: whatever the server's Look setting says.</summary>
        public const string LookServer = "Server";
        public static readonly string[] Looks = { LookServer, ProjectELook.LookNew, ProjectELook.LookMiddle, ProjectELook.LookOriginal };

        /// <summary>How Vintage-E's blocks and items look on this computer: Server (the server's default), New, Middle or Original. Applied when joining a world.</summary>
        public string Look = LookServer;

        /// <summary>
        /// On (default since 1.18.9): a look picked in .vintagee shows at once, by putting the blocks and items back,
        /// applying the look and having the game reload its shapes (and, if the look didn't apply while loading, doing
        /// that once after the world is in). Off: the pick shows the next time you join a world, and nothing reloads the
        /// game's shapes while you play. (1.18.6–1.18.8 had it off by default while a join crash was tracked down; that
        /// crash turned out to be unrelated, see 1.18.8.)
        /// </summary>
        public bool HotLooksReload = true;

        /// <summary>Which defaults this file was written with; lets a changed default reach files saved before it changed.</summary>
        public int ConfigVersion = 0;

        /// <summary>The current <see cref="ConfigVersion"/>. 2: HotLooksReload defaults to on again.</summary>
        public const int CurrentConfigVersion = 2;

        /// <summary>
        /// On (default): Vintage-E hooks the game's block and item shape loaders so the New and Original looks can be
        /// applied while a world loads. Off: no hooks are installed on this computer at all (no Harmony patches on the
        /// game's loading), and the blocks and items keep the Middle look. For isolating load problems.
        /// </summary>
        public bool LookHooks = true;

        /// <summary>
        /// While the Transmutation Table's or Tablet's window is open, a shift-click on an item in your hotbar or
        /// backpack burns the whole stack, if it can burn (1.26.8). Off: a shift-click is always the game's own.
        /// </summary>
        public bool ShiftClickBurns = true;

        /// <summary>Show an icon on screen while the watch is on (or time runs faster or slower because of one).</summary>
        public bool WatchIcon = true;
        /// <summary>The icon's size in GUI pixels (16-128).</summary>
        public int WatchIconSize = 48;
        /// <summary>Where on screen: TopLeft, TopCenter, TopRight, MiddleLeft, MiddleRight, BottomLeft, BottomCenter or BottomRight.</summary>
        public string WatchIconPosition = "TopRight";
        /// <summary>Moves the icon from there, in GUI pixels (right and down are positive).</summary>
        public int WatchIconOffsetX = -20;
        public int WatchIconOffsetY = 80;
        /// <summary>The watch item glows (a pulsing tint) while it's on.</summary>
        public bool WatchGlow = true;
        /// <summary>The glow's colour, #RRGGBB.</summary>
        public string WatchGlowColor = "#66CCFF";

        /// <summary>
        /// Your EMC on screen, with how much it changed a second over the last ten seconds (1.28.0; Project Expansion's
        /// EMC Display). Hidden while you have none.
        /// </summary>
        public bool EmcHud = true;
        /// <summary>Where on screen: one of <see cref="Positions"/>.</summary>
        public string EmcHudPosition = "TopLeft";
        /// <summary>Moves it from there, in GUI pixels (right and down are positive).</summary>
        public int EmcHudOffsetX = 10;
        public int EmcHudOffsetY = 10;
        /// <summary>A line under the EMC value of every block and item saying whether you've learned it for transmutation (1.28.0).</summary>
        public bool LearnedTooltip = true;

        public static readonly string[] Positions = { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight" };

        public static VintageEClientConfig Load(ICoreClientAPI capi)
        {
            VintageEClientConfig cfg = null;
            try { cfg = capi.LoadModConfig<VintageEClientConfig>(FileName); }
            catch (Exception e) { capi.Logger.Warning("Vintage-E: {0} could not be read ({1}); using the defaults.", FileName, e.Message); }
            cfg ??= new VintageEClientConfig();
            cfg.Normalize();
            try { capi.StoreModConfig(cfg, FileName); }
            catch { /* read-only config folder: keep going with what we have */ }
            return cfg;
        }

        public void Save(ICoreClientAPI capi)
        {
            Normalize();
            try { capi.StoreModConfig(this, FileName); }
            catch (Exception e) { capi.Logger.Warning("Vintage-E: could not save {0}: {1}", FileName, e.Message); }
        }

        public void Normalize()
        {
            // Files from 1.18.6–1.18.8 saved HotLooksReload = false only because that was the default then; turn it
            // back on once. Anyone who turns it off after this keeps it off.
            if (ConfigVersion < 2) HotLooksReload = true;
            ConfigVersion = CurrentConfigVersion;
            int li = Array.FindIndex(Looks, l => l.Equals((Look ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            Look = li >= 0 ? Looks[li] : LookServer;
            WatchIconSize = GameMath.Clamp(WatchIconSize, 16, 128);
            WatchIconOffsetX = GameMath.Clamp(WatchIconOffsetX, -2000, 2000);
            WatchIconOffsetY = GameMath.Clamp(WatchIconOffsetY, -2000, 2000);
            WatchIconPosition = NormalizePosition(WatchIconPosition, "TopRight");
            EmcHudPosition = NormalizePosition(EmcHudPosition, "TopLeft");
            EmcHudOffsetX = GameMath.Clamp(EmcHudOffsetX, -2000, 2000);
            EmcHudOffsetY = GameMath.Clamp(EmcHudOffsetY, -2000, 2000);
            if (!TryParseColor(WatchGlowColor, out _)) WatchGlowColor = "#66CCFF";
            WatchGlowColor = WatchGlowColor.Trim().ToUpperInvariant();
            if (!WatchGlowColor.StartsWith("#")) WatchGlowColor = "#" + WatchGlowColor;
        }

        /// <summary>One of <see cref="Positions"/> as written there (any case), or <paramref name="fallback"/>.</summary>
        public static string NormalizePosition(string position, string fallback)
        {
            int i = Array.FindIndex(Positions, p => p.Equals((position ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? Positions[i] : fallback;
        }

        /// <summary>The watch icon's place on screen.</summary>
        public EnumDialogArea Area => AreaOf(WatchIconPosition);

        /// <summary>The EMC display's place on screen.</summary>
        public EnumDialogArea EmcHudArea => AreaOf(EmcHudPosition);

        public static EnumDialogArea AreaOf(string position) => position switch
        {
            "TopLeft" => EnumDialogArea.LeftTop,
            "TopCenter" => EnumDialogArea.CenterTop,
            "MiddleLeft" => EnumDialogArea.LeftMiddle,
            "MiddleRight" => EnumDialogArea.RightMiddle,
            "BottomLeft" => EnumDialogArea.LeftBottom,
            "BottomCenter" => EnumDialogArea.CenterBottom,
            "BottomRight" => EnumDialogArea.RightBottom,
            _ => EnumDialogArea.RightTop
        };

        /// <summary>"#RRGGBB" or "RRGGBB" to 0xRRGGBB.</summary>
        public static bool TryParseColor(string text, out int rgb)
        {
            rgb = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().TrimStart('#');
            return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
        }
    }
}
