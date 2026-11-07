using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VintageE
{
    /// <summary>
    /// Client: one log line per load stage saying what the game holds and whether the entity textures look right,
    /// so a crash while joining (a player model with no texture, say) can be read from client-main.log even though
    /// the doctor never gets to open. Written at Start, AssetsLoaded, StartClientSide, BlockTexturesLoaded,
    /// LevelFinalize and PlayerJoin. Never throws.
    /// </summary>
    public static class ClientCheck
    {
        public static void Log(VintageEModSystem mod, string stage)
        {
            ICoreClientAPI capi = mod?.Capi;
            if (capi == null) return;
            try
            {
                string line = Describe(capi, mod);
                mod.Mod.Logger.Notification("Client check at {0} (thread {1}): {2}", stage, System.Threading.Thread.CurrentThread.Name ?? "?", line);
            }
            catch (Exception e)
            {
                try { mod.Mod.Logger.Warning("Client check at {0} failed: {1}", stage, e.Message); } catch { }
            }
        }

        /// <summary>The client's entity types without touching World.EntityTypes (see Describe); empty if the field can't be read.</summary>
        static List<EntityProperties> EntityTypesSafe(ICoreClientAPI capi)
        {
            try
            {
                object world = capi.World;
                var field = world?.GetType().GetField("EntityClassesByCode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(world) is System.Collections.IDictionary dict)
                    return dict.Values.Cast<object>().OfType<EntityProperties>().ToList();
            }
            catch { }
            return new List<EntityProperties>();
        }

        static string Describe(ICoreClientAPI capi, VintageEModSystem mod)
        {
            var parts = new List<string>();
            parts.Add($"blocks {capi.World?.Blocks?.Count ?? 0}, items {capi.World?.Items?.Count ?? 0}");
            // Never World.EntityTypes here: on the client that property caches its list on the first read, for good
            // (ClientMain.entityTypesCached). The dictionary behind it is read by reflection instead; no side effects.
            List<EntityProperties> types = EntityTypesSafe(capi);
            int withTex = 0, baked = 0, noClient = 0;
            var dictTypes = new HashSet<string>();
            foreach (EntityProperties t in types)
            {
                if (t.Client == null) { noClient++; continue; }
                var tex = t.Client.Textures;
                if (tex != null) { withTex++; dictTypes.Add(tex.GetType().Name); }
                if (t.Client.FirstTexture?.Baked != null) baked++;
            }
            parts.Add($"entity types {types.Count} in the game's dictionary (with textures {withTex}, baked {baked}, no client props {noClient}" + (dictTypes.Count > 0 ? ", texture tables: " + string.Join("/", dictTypes) : "") + ")");
            try
            {
                var atlas = capi.EntityTextureAtlas;
                if (atlas != null) parts.Add($"entity atlas {atlas.Size?.Width}x{atlas.Size?.Height}, {atlas.Positions?.Length ?? 0} positions");
            }
            catch (Exception e) { parts.Add("entity atlas: " + e.GetType().Name); }
            EntityProperties player = null;
            try { player = capi.World?.GetEntityType(new AssetLocation("game", "player")); } catch { }
            player ??= types.FirstOrDefault(t => t.Code?.Path == "player");
            if (player == null) parts.Add("player type: not held");
            else
            {
                var c = player.Client;
                string first = c?.FirstTexture == null ? "none" : (c.FirstTexture.Base?.ToString() ?? "?") + (c.FirstTexture.Baked == null ? " (unbaked)" : " (baked " + c.FirstTexture.Baked.BakedName + ", sub id " + c.FirstTexture.Baked.TextureSubId + ")");
                string beh = c?.BehaviorsAsJsonObj == null ? "?" : string.Join(",", c.BehaviorsAsJsonObj.Select(b => b?["code"]?.AsString() ?? "?"));
                parts.Add($"player type: textures {c?.Textures?.Count ?? -1}, first {first}, renderer {c?.RendererName ?? "?"}, shape {c?.Shape?.Base?.ToString() ?? "?"}, behaviors [{beh}]");
            }
            Entity me = capi.World?.Player?.Entity;
            if (me != null)
            {
                string mine;
                try { mine = me.Properties?.Client?.FirstTexture == null ? "none" : (me.Properties.Client.FirstTexture.Baked == null ? "unbaked" : "baked"); } catch (Exception e) { mine = e.GetType().Name; }
                parts.Add($"own entity: first texture {mine}, renderer {me.Properties?.Client?.Renderer?.GetType().Name ?? "none"}");
            }
            parts.Add("look " + (mod.ActiveLook ?? "not yet") + ", hooks " + ClientLookPatches.PatchedCount + "/" + ClientLookPatches.HookCount + (ClientLookPatches.PatchError == null ? "" : " (" + ClientLookPatches.PatchError + ")") + ", hook calls " + ClientLookPatches.RawCalls);
            return string.Join("; ", parts);
        }
    }
}
