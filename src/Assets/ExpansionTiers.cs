using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// The server setting <see cref="VintageEConfig.Expansion"/> (1.29.0): with it off, Project Expansion's tiers don't
    /// exist on the server at all. The game reads block and item types at execute order 0.2, after its JSON patches
    /// (0.05); this runs in between and takes them out of the asset files as they stand in memory, as listed in
    /// config/vintagee-expansion.json (written by tools/gen_expansion.py): their states out of the collector, relay,
    /// matter and matter block types, the star and shard types switched off ("enabled": false, which the game skips),
    /// and their recipe file emptied. Players' games get blocks and items from the server, so nothing is needed there.
    /// The main mod system's config only loads at StartServerSide, later, so this one setting is read from the file here.
    /// </summary>
    public class ExpansionTiers : ModSystem
    {
        /// <summary>The world setting players' games and the doctor can read: "true" while the tiers are on.</summary>
        public const string WorldKey = "vintageeExpansion";
        public const string ManifestFile = "config/vintagee-expansion.json";

        /// <summary>What happened at start-up: whether the tiers are on, how many entries were taken out, and any error.</summary>
        public static bool On { get; private set; } = true;
        public static int Removed { get; private set; }
        public static string Error { get; private set; }

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        /// <summary>After the game's JSON patches (0.05), before it reads block and item types (0.2).</summary>
        public override double ExecuteOrder() => 0.1;

        public override void AssetsLoaded(ICoreAPI api)
        {
            bool on = true;
            try { on = (api as ICoreServerAPI)?.LoadModConfig<VintageEConfig>(VintageEModSystem.ConfigFile)?.Expansion ?? true; }
            catch { /* a broken settings file is reported by the main mod system; the tiers stay */ }
            On = on;
            Removed = 0;
            Error = null;
            try { api.World.Config?.SetBool(WorldKey, on); }
            catch { /* only for the doctor and players' games */ }
            if (on)
            {
                Mod.Logger.Notification("Expansion: Project Expansion's tiers are on.");
                return;
            }
            try
            {
                IAsset manifest = api.Assets.TryGet(new AssetLocation(VintageEModSystem.ModId, ManifestFile));
                if (manifest == null) throw new InvalidOperationException(ManifestFile + " is missing");
                Removed = Strip(JObject.Parse(manifest.ToText()),
                    path => api.Assets.TryGet(new AssetLocation(VintageEModSystem.ModId, path))?.ToText(),
                    (path, text) => api.Assets.TryGet(new AssetLocation(VintageEModSystem.ModId, path)).Data = Encoding.UTF8.GetBytes(text));
                Mod.Logger.Notification("Expansion is off: Project Expansion's tiers are taken out before the game reads them ({0} entries).", Removed);
            }
            catch (Exception e)
            {
                Error = e.Message;
                Mod.Logger.Error("Expansion is off, but Project Expansion's tiers could not be taken out: {0}", e);
            }
        }

        /// <summary>
        /// Takes the tiers out, file by file: <paramref name="read"/> gives a file's text (null when it isn't there),
        /// <paramref name="write"/> stores the changed text. Returns how many states, types and recipes were taken out.
        /// </summary>
        public static int Strip(JObject manifest, System.Func<string, string> read, System.Action<string, string> write)
        {
            int removed = 0;
            if (manifest["states"] is JObject states)
            {
                foreach (JProperty p in states.Properties())
                {
                    string text = read(p.Name);
                    if (text == null) continue;
                    JObject type = JObject.Parse(text);
                    var drop = new HashSet<string>((p.Value["states"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                    removed += StripStates(type, (string)p.Value["group"], drop);
                    write(p.Name, type.ToString());
                }
            }
            foreach (string path in (manifest["files"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
            {
                string text = read(path);
                if (text == null) continue;
                JObject type = JObject.Parse(text);
                type["enabled"] = false;
                write(path, type.ToString());
                removed++;
            }
            foreach (string path in (manifest["recipes"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
            {
                string text = read(path);
                if (text == null) continue;
                removed += (JToken.Parse(text) as JArray)?.Count ?? 0;
                write(path, "[]");
            }
            return removed;
        }

        /// <summary>Takes the named states out of one variant group of a block or item type; returns how many.</summary>
        public static int StripStates(JObject type, string group, ISet<string> drop)
        {
            if (!(type["variantgroups"] is JArray groups)) return 0;
            int removed = 0;
            foreach (JObject g in groups.OfType<JObject>())
            {
                if ((string)g["code"] != group || !(g["states"] is JArray st)) continue;
                var keep = new JArray(st.Where(s => !drop.Contains((string)s)));
                removed += st.Count - keep.Count;
                g["states"] = keep;
            }
            return removed;
        }
    }
}
