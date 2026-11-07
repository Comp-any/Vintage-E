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

        static string Describe(ICoreClientAPI capi, VintageEModSystem mod)
        {
            var parts = new List<string>();
            parts.Add($"blocks {capi.World?.Blocks?.Count ?? 0}, items {capi.World?.Items?.Count ?? 0}");
            List<EntityProperties> types = capi.World?.EntityTypes?.Where(t => t != null).ToList() ?? new List<EntityProperties>();
            int withTex = 0, baked = 0, noClient = 0;
            var dictTypes = new HashSet<string>();
            foreach (EntityProperties t in types)
            {
                if (t.Client == null) { noClient++; continue; }
                var tex = t.Client.Textures;
                if (tex != null) { withTex++; dictTypes.Add(tex.GetType().Name); }
                if (t.Client.FirstTexture?.Baked != null) baked++;
            }
            parts.Add($"entity types {types.Count} (with textures {withTex}, baked {baked}, no client props {noClient}" + (dictTypes.Count > 0 ? ", texture tables: " + string.Join("/", dictTypes) : "") + ")");
            try
            {
                var atlas = capi.EntityTextureAtlas;
                if (atlas != null) parts.Add($"entity atlas {atlas.Size?.Width}x{atlas.Size?.Height}, {atlas.Positions?.Length ?? 0} positions");
            }
            catch (Exception e) { parts.Add("entity atlas: " + e.GetType().Name); }
            EntityProperties player = types.FirstOrDefault(t => t.Code?.Path == "player" && t.Code.Domain == "game") ?? types.FirstOrDefault(t => t.Code?.Path == "player");
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
