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
            public Finding Finding = Finding.NotInstalled;
            public string ModName = "Ancient Armory", FileName = "";
            public bool Broken => Finding == Finding.NothingLoaded || Finding == Finding.NothingLoadedBackslashZip;
        }

        public static Finding Judge(bool installed, bool anyItems, bool backslashZip) =>
            !installed ? Finding.NotInstalled : anyItems ? Finding.Loaded : backslashZip ? Finding.NothingLoadedBackslashZip : Finding.NothingLoaded;

        /// <summary>What is the case on this server. Call it once the items are loaded.</summary>
        public static Result Look(ICoreAPI api)
        {
            var result = new Result();
            Mod armory = null;
            foreach (string id in ModIds)
            {
                if (!api.ModLoader.IsModEnabled(id)) continue;
                armory = api.ModLoader.GetMod(id);
                break;
            }
            bool installed = ModIds.Any(id => api.ModLoader.IsModEnabled(id));
            if (!installed) return result;
            bool anyItems = api.World.Collectibles.Any(c => c?.Code?.Domain == Domain);
            bool backslash = false;
            if (!anyItems)
            {
                try { backslash = armory?.SourceType == EnumModSourceType.ZIP && ZipHasBackslashPaths(armory.SourcePath); }
                catch (Exception e) { api.Logger.VerboseDebug("Vintage-E: could not look inside {0}: {1}", armory?.FileName, e.Message); }
            }
            result.Finding = Judge(true, anyItems, backslash);
            if (!string.IsNullOrEmpty(armory?.Info?.Name)) result.ModName = armory.Info.Name;
            result.FileName = armory?.FileName ?? "";
            return result;
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
            if (!admin) return Lang.GetL(lang, "vintagee:ancientarmory-notloaded-player", result.ModName);
            string file = string.IsNullOrEmpty(result.FileName) ? result.ModName : result.FileName;
            return Lang.GetL(lang, result.Finding == Finding.NothingLoadedBackslashZip ? "vintagee:ancientarmory-notloaded-zip" : "vintagee:ancientarmory-notloaded", result.ModName, file);
        }
    }
}
