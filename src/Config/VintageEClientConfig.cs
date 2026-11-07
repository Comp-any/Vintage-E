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
        /// Off (default): a look picked in .vintagee shows the next time you join a world, and nothing reloads the game's
        /// shapes while you play. On: the pick shows at once, by putting the blocks and items back, applying the look
        /// and having the game reload its shapes (and, if the look didn't apply while loading, doing that once after
        /// the world is in). That path reaches into the game's internals and can race its loading threads.
        /// </summary>
        public bool HotLooksReload = false;

        /// <summary>
        /// On (default): Vintage-E hooks the game's block and item shape loaders so the New and Original looks can be
        /// applied while a world loads. Off: no hooks are installed on this computer at all (no Harmony patches on the
        /// game's loading), and the blocks and items keep the Middle look. For isolating load problems.
        /// </summary>
        public bool LookHooks = true;

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
            int li = Array.FindIndex(Looks, l => l.Equals((Look ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            Look = li >= 0 ? Looks[li] : LookServer;
            WatchIconSize = GameMath.Clamp(WatchIconSize, 16, 128);
            WatchIconOffsetX = GameMath.Clamp(WatchIconOffsetX, -2000, 2000);
            WatchIconOffsetY = GameMath.Clamp(WatchIconOffsetY, -2000, 2000);
            if (Array.FindIndex(Positions, p => p.Equals(WatchIconPosition ?? "", StringComparison.OrdinalIgnoreCase)) is int i && i >= 0) WatchIconPosition = Positions[i];
            else WatchIconPosition = "TopRight";
            if (!TryParseColor(WatchGlowColor, out _)) WatchGlowColor = "#66CCFF";
            WatchGlowColor = WatchGlowColor.Trim().ToUpperInvariant();
            if (!WatchGlowColor.StartsWith("#")) WatchGlowColor = "#" + WatchGlowColor;
        }

        public EnumDialogArea Area => WatchIconPosition switch
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
