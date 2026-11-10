using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The doctor's "Emc Craft" check (1.19.1): whether the Arcane Tablet, its crafting window and the handbook's Emc
    /// Craft link work, on the server and on the player's computer. A short check in "Your game"; the full report is
    /// /emc diagnose emccraft (aliases arcane, arcanetablet, handbook).
    /// </summary>
    public static partial class EmcDiagnose
    {
        public const string ActionEmcCraft = "emccraft";

        /// <summary>A doctor window's title with the running version, e.g. "EMC doctor (Vintage-E 1.19.1)".</summary>
        public static string TitleWithVersion(ICoreClientAPI capi, string title)
        {
            string v = VintageEModSystem.Of(capi)?.Version;
            return string.IsNullOrEmpty(v) ? title : Lang.Get("vintagee:diag-title-version", title, v);
        }

        /// <summary>
        /// The player's computer, as sent with the request: patched|error|calls|pages with recipes|links|last page|
        /// last slideshows|last tablet|last error|tablet now|grid open.
        /// </summary>
        class CraftClient
        {
            public bool Known, Patched, LastTablet, TabletNow, GridOpen;
            /// <summary>Items in the open window's grid on the player's computer (-1 unknown), and whether the game holds that very grid ("1", "0", "-").</summary>
            public int WindowItems = -1;
            public string WindowHeld = "-";
            public string Error = "", LastPage = "", LastError = "";
            public int Calls, Pages, Links, LastSlideshows;

            public static CraftClient Read(EmcDiagnoseRequestPacket client)
            {
                var c = new CraftClient();
                string[] f = (client?.ClientEmcCraftInfo ?? "").Split('|');
                if (f.Length < 11) return c;
                c.Known = true;
                c.Patched = f[0] == "1"; c.Error = f[1];
                int.TryParse(f[2], out c.Calls); int.TryParse(f[3], out c.Pages); int.TryParse(f[4], out c.Links);
                c.LastPage = f[5]; int.TryParse(f[6], out c.LastSlideshows); c.LastTablet = f[7] == "1"; c.LastError = f[8];
                c.TabletNow = f[9] == "1"; c.GridOpen = f[10] == "1";
                if (f.Length >= 13) { if (!int.TryParse(f[11], out c.WindowItems)) c.WindowItems = -1; c.WindowHeld = f[12]; }
                return c;
            }
        }

        /// <summary>One line in "Your game": does the handbook link work here, and if not, the most likely reason.</summary>
        static void ClientEmcCraft(Ctx x, EmcDiagnoseRequestPacket client)
        {
            if (client == null || !client.HasClientInfo) return;
            CraftClient c = CraftClient.Read(client);
            if (!c.Known) { x.Info(x.L("diag-emccraft-oldclient")); x.Fix(x.L("diag-emccraft-oldclient-fix")); return; }
            if (!c.Patched) { x.Problem(x.L("diag-emccraft-nohook", c.Error.Length == 0 ? "?" : c.Error)); x.Fix(x.L("diag-emccraft-nohook-fix")); }
            else if (c.Links > 0) x.Ok(x.L("diag-emccraft-ok", c.Links, c.Pages));
            else if (!c.TabletNow) { x.Warn(x.L("diag-emccraft-notablet")); x.Explain(x.L("diag-emccraft-notablet-why")); }
            else if (c.Calls == 0) { x.Info(x.L("diag-emccraft-nopages")); x.Explain(x.L("diag-emccraft-nopages-why")); }
            else if (c.Pages == 0) { x.Info(x.L("diag-emccraft-norecipes", c.Calls)); x.Explain(x.L("diag-emccraft-norecipes-why")); }
            else { x.Warn(x.L("diag-emccraft-nolinks", c.Pages, c.LastPage, c.LastTablet ? x.L("diag-yes") : x.L("diag-no"))); x.Explain(x.L("diag-emccraft-nolinks-why")); }
            if (c.LastError.Length > 0) x.Note(x.L("diag-emccraft-lasterror", c.LastError));
            x.R.Add(Link(ActionEmcCraft, "", x.L("diag-emccraft-more")), EmcLineStyle.Text, 1);
        }

        /// <summary>/emc diagnose emccraft: everything Emc Craft depends on, server and player side.</summary>
        static void EmcCraftReport(Ctx x, EmcDiagnoseRequestPacket client)
        {
            EmcReport r = x.R;
            r.Section(x.L("diag-emccraft-title"));
            var world = x.Sapi.World;

            // The item and its recipe.
            Item arcane = world.GetItem(new AssetLocation("vintagee:arcanetablet"));
            if (arcane is ItemArcaneTablet) x.Ok(x.L("diag-emccraft-item-ok", EmcFormat.Format(x.Mod.Emc.GetValue(arcane))));
            else { x.Problem(x.L("diag-emccraft-item-missing")); x.Fix(x.L("diag-emccraft-item-missing-fix")); }
            GridRecipe recipe = world.GridRecipes?.FirstOrDefault(g => g.Output?.Code?.ToString() == "vintagee:arcanetablet");
            if (recipe != null && recipe.Enabled != false) x.Ok(x.L("diag-emccraft-recipe-ok"));
            else { x.Problem(x.L("diag-emccraft-recipe-missing")); x.Explain(x.L("diag-emccraft-recipe-missing-why")); }

            // The grid class, so a grid saved while open loads back.
            InventoryBase rebuilt = null;
            try { rebuilt = Vintagestory.Server.ServerMain.ClassRegistry?.CreateInventory(EmcCraft.InventoryClass, EmcCraft.InventoryClass + "-doctor", x.Sapi); } catch { }
            if (rebuilt is EmcCraftGrid) x.Ok(x.L("diag-emccraft-class-ok"));
            else { x.Problem(x.L("diag-emccraft-class-missing")); x.Explain(x.L("diag-emccraft-class-missing-why")); }

            // The asking player, as the server sees them.
            if (x.Player != null)
            {
                bool carries = EmcCraft.CarriesArcaneTablet(x.Player);
                if (carries) x.Ok(x.L("diag-emccraft-player-carries", x.Player.PlayerName));
                else { x.Warn(x.L("diag-emccraft-player-nocarry", x.Player.PlayerName)); x.Explain(x.L("diag-emccraft-player-nocarry-why")); }
                var grid = x.Player.InventoryManager?.GetInventory(EmcCraft.InventoryClass + "-" + x.Player.PlayerUID);
                int items = 0;
                if (grid != null) for (int i = 0; i < EmcCraft.GridSize * EmcCraft.GridSize; i++) if (grid[i]?.Itemstack != null) items++;
                x.Info(grid == null ? x.L("diag-emccraft-grid-closed") : x.L("diag-emccraft-grid-open", items));
                x.Info(x.L("diag-emccraft-known", PlayerEmc.GetKnown(x.Player).Length, EmcFormat.Format(PlayerEmc.GetEmc(x.Player))));
                string last = x.Mod.EmcCrafting?.LastFill(x.Player);
                x.Info(last == null ? x.L("diag-emccraft-lastfill-none") : x.L("diag-emccraft-lastfill", last));
            }

            // The player's computer: the handbook hook and what it has seen.
            r.Section(x.L("diag-emccraft-client-title"));
            if (client == null || !client.HasClientInfo) { x.Info(x.L("diag-emccraft-noclient")); Back(x); return; }
            CraftClient c = CraftClient.Read(client);
            if (!c.Known) { x.Info(x.L("diag-emccraft-oldclient")); x.Fix(x.L("diag-emccraft-oldclient-fix")); Back(x); return; }
            if (client.ClientVersion != null && client.ClientVersion != x.Mod.Version) { x.Problem(x.L("diag-emccraft-version", client.ClientVersion, x.Mod.Version)); x.Fix(x.L("diag-emccraft-version-fix")); }
            if (c.Patched) x.Ok(x.L("diag-emccraft-hook-ok"));
            else { x.Problem(x.L("diag-emccraft-nohook", c.Error.Length == 0 ? "?" : c.Error)); x.Fix(x.L("diag-emccraft-nohook-fix")); }
            if (c.TabletNow) x.Ok(x.L("diag-emccraft-client-carries"));
            else { x.Warn(x.L("diag-emccraft-notablet")); x.Explain(x.L("diag-emccraft-notablet-why")); }
            x.Info(x.L("diag-emccraft-pages", c.Calls, c.Pages, c.Links));
            if (c.LastPage.Length > 0) x.Note(x.L("diag-emccraft-lastpage", c.LastPage, c.LastSlideshows, c.LastTablet ? x.L("diag-yes") : x.L("diag-no")));
            if (c.Patched && c.TabletNow && c.Pages > 0 && c.Links == 0) { x.Warn(x.L("diag-emccraft-nolinks", c.Pages, c.LastPage, c.LastTablet ? x.L("diag-yes") : x.L("diag-no"))); x.Explain(x.L("diag-emccraft-nolinks-why")); }
            if (c.LastError.Length > 0) x.Problem(x.L("diag-emccraft-lasterror", c.LastError));
            x.Info(c.GridOpen ? x.L("diag-emccraft-window-open") : x.L("diag-emccraft-window-closed"));
            if (c.GridOpen && x.Player != null)
            {
                var serverGrid = x.Player.InventoryManager?.GetInventory(EmcCraft.InventoryClass + "-" + x.Player.PlayerUID);
                int serverItems = 0;
                if (serverGrid != null) for (int i = 0; i < EmcCraft.GridSize * EmcCraft.GridSize; i++) if (serverGrid[i]?.Itemstack != null) serverItems++;
                if (c.WindowHeld == "0") { x.Problem(x.L("diag-emccraft-twogrids")); x.Fix(x.L("diag-emccraft-twogrids-fix")); }
                else if (c.WindowItems >= 0 && serverItems != c.WindowItems) { x.Problem(x.L("diag-emccraft-nosync", serverItems, c.WindowItems)); x.Fix(x.L("diag-emccraft-twogrids-fix")); }
                else if (c.WindowItems >= 0) x.Ok(x.L("diag-emccraft-sync-ok", serverItems));
            }
            x.Note(x.L("diag-emccraft-howto"));
            Back(x);
        }
    }
}
