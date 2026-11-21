using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Ancient Armory is installed, but none of its items are in the game (1.25.3).
    ///
    /// The Ancient Armory Fork's download (2.1.0) is a zip whose paths use backslashes ("assets\ancientarmory\..."),
    /// which only Windows reads as folders. On a Linux or macOS server the game still lists the mod as loaded and
    /// runs its code, but finds no assets folder in it, and says nothing about that: no error, no warning, the zip is
    /// just missing from the log's "External Origins" line. Vintage-E's matter claymores are switched on by the mod
    /// being installed, so they are there, but the claymore they are made from isn't: they can't be crafted and have
    /// no EMC value, which looks like Vintage-E's fault. So Vintage-E says what it found: in the server log at start,
    /// and in chat to each player who joins.
    ///
    /// The same zip on a player's own Linux or macOS computer (1.25.4), joining a server where Ancient Armory did
    /// load: the items exist, but that player's game has none of the mod's shapes, textures or names, and not the
    /// claymore swing either (Ancient Armory patches it into the player's shape, which every game reads from its own
    /// copy of the mod). The game makes a swing's hit on the player's computer, at a frame of the animation, so
    /// without the animation a claymore doesn't hit there: Ancient Armory's and Vintage-E's alike. The server can't
    /// see any of that, so the player's own game looks (<see cref="LookHere"/>) and tells them.
    /// </summary>
    public static class AncientArmoryCheck
    {
        /// <summary>The fork first: where both are installed, it is the one a 1.22 server runs.</summary>
        public static readonly string[] ModIds = { "ancientarmoryfix", "ancientarmory" };
        public const string Domain = "ancientarmory";

        public enum Finding
        {
            NotInstalled,
            Loaded,
            /// <summary>Installed, none of its items in the game, and the reason isn't known.</summary>
            NothingLoaded,
            /// <summary>Installed, none of its items in the game, and its zip has backslash paths.</summary>
            NothingLoadedBackslashZip
        }

        public sealed class Result
        {
            /// <summary>About this computer's own copy of the mod (a player's game), not about the server's items.</summary>
            public bool Here;
            public Finding Finding = Finding.NotInstalled;
            public string ModName = "Ancient Armory", FileName = "";
            public bool Broken => Finding == Finding.NothingLoaded || Finding == Finding.NothingLoadedBackslashZip;
        }

        public static Finding Judge(bool installed, bool anyItems, bool backslashZip) =>
            !installed ? Finding.NotInstalled : anyItems ? Finding.Loaded : backslashZip ? Finding.NothingLoadedBackslashZip : Finding.NothingLoaded;

        /// <summary>
        /// The same for a player's own computer. <paramref name="assetsFolder"/>: whether the game found an assets folder
        /// in its copy of the mod (null: couldn't be told; then only a backslash zip on a computer that doesn't read
        /// backslashes as folders counts). Nothing to say when the server has none of the mod's items either: the
        /// server's own line covers that.
        /// </summary>
        public static Finding JudgeHere(bool installed, bool serverHasItems, bool? assetsFolder, bool backslashZip, bool windowsPaths)
        {
            if (!installed || !serverHasItems) return Finding.NotInstalled;
            if (assetsFolder == true) return Finding.Loaded;
            if (assetsFolder == false) return backslashZip ? Finding.NothingLoadedBackslashZip : Finding.NothingLoaded;
            return backslashZip && !windowsPaths ? Finding.NothingLoadedBackslashZip : Finding.Loaded;
        }

        /// <summary>The installed Ancient Armory, or null. <paramref name="installed"/> is true even if the game won't hand the mod over.</summary>
        static Mod Installed(ICoreAPI api, out bool installed)
        {
            installed = false;
            foreach (string id in ModIds)
            {
                if (!api.ModLoader.IsModEnabled(id)) continue;
                installed = true;
                return api.ModLoader.GetMod(id);
            }
            return null;
        }

        static bool BackslashZip(ICoreAPI api, Mod armory)
        {
            try { return armory?.SourceType == EnumModSourceType.ZIP && ZipHasBackslashPaths(armory.SourcePath); }
            catch (Exception e) { api.Logger.VerboseDebug("Vintage-E: could not look inside {0}: {1}", armory?.FileName, e.Message); return false; }
        }

        static Result Named(Result result, Mod armory)
        {
            if (!string.IsNullOrEmpty(armory?.Info?.Name)) result.ModName = armory.Info.Name;
            result.FileName = armory?.FileName ?? "";
            return result;
        }

        /// <summary>What is the case on this server. Call it once the items are loaded.</summary>
        public static Result Look(ICoreAPI api)
        {
            var result = new Result();
            Mod armory = Installed(api, out bool installed);
            if (!installed) return result;
            bool anyItems = api.World.Collectibles.Any(c => c?.Code?.Domain == Domain);
            result.Finding = Judge(true, anyItems, !anyItems && BackslashZip(api, armory));
            return Named(result, armory);
        }

        /// <summary>What is the case with this computer's own copy of the mod. For a player's game, once it has joined.</summary>
        public static Result LookHere(ICoreAPI api)
        {
            var result = new Result { Here = true };
            Mod armory = Installed(api, out bool installed);
            if (!installed) return result;
            bool serverHasItems = api.World.Collectibles.Any(c => c?.Code?.Domain == Domain);
            bool? assets = HasAssetsFolder(armory);
            bool backslash = serverHasItems && assets != true && BackslashZip(api, armory);
            result.Finding = JudgeHere(true, serverHasItems, assets, backslash, Path.DirectorySeparatorChar == '\\');
            return Named(result, armory);
        }

        /// <summary>
        /// Whether the folder the game unpacked the mod into has an "assets" folder. That is the game's own rule for
        /// reading a mod's files (ModLoader: Directory.Exists(FolderPath/assets)); a zip with backslash paths unpacks
        /// on Linux and macOS into files with backslashes in their names and no folder at all. Null when it can't be told.
        /// </summary>
        public static bool? HasAssetsFolder(Mod mod)
        {
            try
            {
                string folder = mod?.GetType().GetProperty("FolderPath")?.GetValue(mod) as string;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                return Directory.Exists(Path.Combine(folder, "assets"));
            }
            catch { return null; }
        }

        /// <summary>Whether any path inside the zip is written with a backslash. False for a file that isn't there.</summary>
        public static bool ZipHasBackslashPaths(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.IndexOf('\\') >= 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The chat line for a player, or null when there is nothing to say. Admins get the cause and what to do about
        /// it; everyone else a line saying which mod it is, so nobody takes the claymores for broken.
        /// </summary>
        public static string Message(Result result, string lang, bool admin)
        {
            if (result == null || !result.Broken) return null;
            if (result.Here)
            {
                string mine = string.IsNullOrEmpty(result.FileName) ? result.ModName : result.FileName;
                string key = result.Finding == Finding.NothingLoadedBackslashZip ? "vintagee:ancientarmory-notloaded-here-zip" : "vintagee:ancientarmory-notloaded-here";
                // A player's game speaks its own language (lang null); the self-test asks for English.
                return lang == null ? Lang.Get(key, result.ModName, mine) : Lang.GetL(lang, key, result.ModName, mine);
            }
            if (!admin) return Lang.GetL(lang, "vintagee:ancientarmory-notloaded-player", result.ModName);
            string file = string.IsNullOrEmpty(result.FileName) ? result.ModName : result.FileName;
            return Lang.GetL(lang, result.Finding == Finding.NothingLoadedBackslashZip ? "vintagee:ancientarmory-notloaded-zip" : "vintagee:ancientarmory-notloaded", result.ModName, file);
        }
    }
}
