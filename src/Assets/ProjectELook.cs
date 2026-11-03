using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// The Look setting. "New" (the default) gives blocks and items models in Vintage Story's own style, built mostly
    /// from the game's materials (config/vintagee-new-look.json, written by tools/gen_newlook.py). "Middle" is
    /// Vintage-E's own flat textures. "Original" swaps them for ProjectE's originals
    /// (assets/vintagee/textures/projecte, copied by tools/import_projecte_assets.py), following
    /// config/vintagee-projecte-look.json.
    /// <para>The swap is done on the server while assets are finalised. Clients get block, item and entity definitions
    /// (textures, shapes, transforms) from the server, so players see ProjectE's look without setting anything, and the
    /// client can tell it's on because the item textures it received live under <see cref="PathPrefix"/>.</para>
    /// <para>On the client, rings, stones and the Gem of Eternal Density switch to ProjectE's "on" texture while active,
    /// and the Ring of Arcana and Swiftwolf's Rending Gale show their mode, like ProjectE's item models.</para>
    /// </summary>
    public static class ProjectELook
    {
        public const string MapFile = "config/vintagee-projecte-look.json";
        public const string NewMapFile = "config/vintagee-new-look.json";

        /// <summary>The Look setting's three choices.</summary>
        public const string LookMiddle = "Middle", LookOriginal = "Original", LookNew = "New";

        /// <summary>The look every world gets unless the Look setting says otherwise.</summary>
        public const string LookDefault = LookNew;

        /// <summary>The Look setting's value written properly (New, the default, for empty, "auto", "default" or anything it doesn't know).</summary>
        public static string NormalizeLook(string look)
        {
            switch ((look ?? "").Trim().ToLowerInvariant())
            {
                case "original": case "projecte": case "old": return LookOriginal;
                case "middle": case "flat": case "classic": case "vintagee": return LookMiddle;
                default: return LookNew;
            }
        }

        /// <summary>The look map for a Look value, or null for the default look.</summary>
        public static string MapFor(string look)
        {
            switch (NormalizeLook(look))
            {
                case LookOriginal: return MapFile;
                case LookNew: return NewMapFile;
                default: return null;
            }
        }

        /// <summary>ProjectE's textures live under textures/projecte/ in this mod.</summary>
        public const string PathPrefix = "projecte/";

        sealed class Override
        {
            public AssetLocation Code;
            public Dictionary<string, AssetLocation> Textures = new Dictionary<string, AssetLocation>();
            public AssetLocation Shape;
            public AssetLocation Sprite;
            /// <summary>New look: the block's draw type ("JSON" when a cube gets a model).</summary>
            public string DrawType;
            /// <summary>New look: false when a model no longer fills the whole block, so neighbours draw the faces next to it.</summary>
            public bool? SideOpaque;
            /// <summary>New look: the block's outline, fitted to its model (collision stays the same in every look).</summary>
            public Cuboidf SelectionBox;
            /// <summary>New look: other block/item fields to set by name (LightAbsorption, say).</summary>
            public JObject Set;
            /// <summary>New look: how an item with a model is held and shown (guiTransform, fpHandTransform, tpHandTransform, groundTransform).</summary>
            public Dictionary<string, JToken> Transforms = new Dictionary<string, JToken>();
        }

        /// <summary>What <see cref="Apply"/> changed, for the log and the self-test.</summary>
        public class Result
        {
            /// <summary>Which look this is ("Original" or "New").</summary>
            public string Look = LookOriginal;
            public int Collectibles;
            public int Entities;
            public int Textures;
            /// <summary>Blocks and items given a new model.</summary>
            public int Models;
            /// <summary>Models named in the map that aren't there (the block/item keeps its own).</summary>
            public List<string> MissingShapes = new List<string>();
            /// <summary>The copies a look was applied to, by code (the self-test's; null for the real ones).</summary>
            public Dictionary<string, CollectibleObject> Objects;
            /// <summary>Things that could not be changed (a field this game version doesn't have, say).</summary>
            public List<string> Problems = new List<string>();
            public List<string> MissingFiles = new List<string>();
            /// <summary>
            /// "code#texture code" → the ProjectE texture it now uses ("#shape" for a swapped shape, "#sprite" for a flat
            /// item). Kept because a dedicated server frees block/item textures once they're sent to players.
            /// </summary>
            public Dictionary<string, string> Swapped = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>Swaps textures on every Vintage-E block, item and entity type for ProjectE's. Server side, from AssetsFinalize.</summary>
        public static Result Apply(ICoreAPI api, ILogger logger) => Apply(api, logger, LookOriginal, false);

        /// <summary>
        /// Applies a look (<see cref="LookOriginal"/> or <see cref="LookNew"/>) to every Vintage-E block, item and entity
        /// type. Server side, from AssetsFinalize: players get the changed definitions from the server. With
        /// <paramref name="dryRun"/> nothing is changed; the result only says what would be and what's missing (the self-test).
        /// </summary>
        public static Result Apply(ICoreAPI api, ILogger logger, string look, bool dryRun,
            IEnumerable<CollectibleObject> collectibles = null, IEnumerable<EntityProperties> entityTypes = null)
        {
            look = NormalizeLook(look);
            var result = new Result { Look = look };
            string mapFile = MapFor(look);
            if (mapFile == null) return result;
            IAsset asset = api.Assets.TryGet(new AssetLocation(VintageEModSystem.ModId, mapFile));
            if (asset == null)
            {
                logger.Warning("Look is {0} but {1} is missing; keeping Vintage-E's textures.", look, mapFile);
                return result;
            }

            JObject root;
            try { root = JObject.Parse(asset.ToText()); }
            catch (Exception e)
            {
                logger.Error("Could not read {0}: {1}; keeping Vintage-E's textures.", mapFile, e.Message);
                return result;
            }

            var files = new Dictionary<string, AssetLocation>(StringComparer.Ordinal);
            if (root["textures"] is JObject textures)
            {
                foreach (JProperty p in textures.Properties())
                {
                    if (p.Name.StartsWith("//") || p.Value.Type != JTokenType.String) continue;
                    files[new AssetLocation(p.Name).ToString()] = new AssetLocation((string)p.Value);
                }
            }
            var overrides = new List<Override>();
            if (root["overrides"] is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    string code = (string)t["code"];
                    if (string.IsNullOrEmpty(code) || t["preview_only"]?.Type == JTokenType.Boolean && (bool)t["preview_only"]) continue;
                    var o = new Override { Code = new AssetLocation(code) };
                    if (t["textures"] is JObject ot)
                    {
                        foreach (JProperty p in ot.Properties()) o.Textures[p.Name] = new AssetLocation((string)p.Value);
                    }
                    if ((string)t["shape"] is string shape) o.Shape = new AssetLocation(shape);
                    if ((string)t["sprite"] is string sprite) o.Sprite = new AssetLocation(sprite);
                    o.DrawType = (string)t["drawtype"];
                    if (t["sideopaque"]?.Type == JTokenType.Boolean) o.SideOpaque = (bool)t["sideopaque"];
                    o.Set = t["set"] as JObject;
                    if (t["selectionbox"] is JObject sb)
                    {
                        float F(string k) => (float?)sb[k] ?? 0;
                        o.SelectionBox = new Cuboidf(F("x1"), F("y1"), F("z1"), F("x2"), F("y2"), F("z2"));
                    }
                    foreach (string tf in new[] { "guiTransform", "fpHandTransform", "tpHandTransform", "tpOffHandTransform", "groundTransform" })
                    {
                        if (t[tf] is JObject j) o.Transforms[tf] = j;
                    }
                    overrides.Add(o);
                }
            }

            // Every ProjectE texture named must be there, or the block would show the game's "unknown" texture.
            // The game's own textures (the New look's planks, metals, gems...) only ship with the game client, so a
            // dedicated server can't see them: those are trusted here and checked by the server test against the client.
            var missing = new HashSet<string>();
            bool Exists(AssetLocation tex)
            {
                if (tex.Domain == "game" && api.Side == EnumAppSide.Server && !api.Assets.Exists(new AssetLocation("game:textures/block/transparent.png"))) return true;
                if (api.Assets.Exists(tex.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"))) return true;
                missing.Add(tex.ToString());
                return false;
            }
            bool ShapeExists(AssetLocation shape)
            {
                if (api.Assets.Exists(shape.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"))) return true;
                if (!result.MissingShapes.Contains(shape.ToString())) result.MissingShapes.Add(shape.ToString());
                return false;
            }

            if (collectibles != null) result.Objects = collectibles.Where(c => c?.Code != null).GroupBy(c => c.Code.ToString()).ToDictionary(g => g.Key, g => g.First());
            foreach (CollectibleObject c in collectibles ?? api.World.Collectibles)
            {
                if (c?.Code == null || c.Code.Domain != VintageEModSystem.ModId) continue;
                Override[] mine = overrides.Where(o => WildcardUtil.Match(o.Code, c.Code)).ToArray();
                int before = result.Textures;

                Override model = mine.LastOrDefault(o => o.Shape != null);
                if (model != null && ShapeExists(model.Shape))
                {
                    // New look: a model of its own, its textures added to the ones it has (worn rings and Klein Stars
                    // keep using the old ones), and for items how they're held.
                    result.Models++;
                    result.Swapped[c.Code + "#shape"] = model.Shape.ToString();
                    // The game casts these back to its own types when it collects textures: blocks need a
                    // TextureDictionary (and a non-null inventory one), items a Dictionary, entity types a FastSmallDictionary.
                    IDictionary<string, CompositeTexture> modelTextures = c is Block bl0 ? BlockTextures(bl0.Textures) : new Dictionary<string, CompositeTexture>(((Item)c).Textures ?? new Dictionary<string, CompositeTexture>());
                    foreach (var kv in model.Textures)
                    {
                        if (!Exists(kv.Value)) continue;
                        modelTextures[kv.Key] = new CompositeTexture(kv.Value);
                        result.Textures++;
                        result.Swapped[c.Code + "#" + kv.Key] = kv.Value.ToString();
                    }
                    if (!dryRun) ApplyModel(c, model, modelTextures, result);
                    result.Collectibles++;
                    continue;
                }

                if (dryRun)
                {
                    // Only count what would change.
                    if (c is Item di && di.Textures != null) Swap(di.Code.ToString(), new Dictionary<string, CompositeTexture>(di.Textures), mine, files, Exists, result);
                    else if (c is Block db && db.Textures != null) Swap(db.Code.ToString(), BlockTextures(db.Textures), mine, files, Exists, result);
                    if (result.Textures > before) result.Collectibles++;
                    continue;
                }

                if (c is Item item)
                {
                    Override sprite = mine.LastOrDefault(o => o.Sprite != null);
                    if (sprite != null && Exists(sprite.Sprite))
                    {
                        // Drawn as a flat item from one texture, the way ProjectE draws it, with the game's usual item placement.
                        item.Shape = null;
                        item.Textures = new Dictionary<string, CompositeTexture> { ["all"] = new CompositeTexture(sprite.Sprite) };
                        item.GuiTransform = ModelTransform.ItemDefaultGui();
                        item.FpHandTransform = ModelTransform.ItemDefaultFp();
                        item.TpHandTransform = ModelTransform.ItemDefaultTp();
                        item.TpOffHandTransform = ModelTransform.ItemDefaultTp();
                        item.GroundTransform = ModelTransform.ItemDefaultGround();
                        result.Textures++;
                        result.Swapped[item.Code + "#sprite"] = sprite.Sprite.ToString();
                        result.Swapped[item.Code + "#all"] = sprite.Sprite.ToString();
                    }
                    else if (item.Textures != null)
                    {
                        var copy = new Dictionary<string, CompositeTexture>(item.Textures);
                        Swap(item.Code.ToString(), copy, mine, files, Exists, result);
                        item.Textures = copy;
                    }
                }
                else if (c is Block block)
                {
                    if (block.Textures != null)
                    {
                        var copy = BlockTextures(block.Textures);
                        Swap(block.Code.ToString(), copy, mine, files, Exists, result);
                        block.Textures = copy;
                    }
                    if (block.TexturesInventory != null)
                    {
                        var copy = BlockTextures(block.TexturesInventory);
                        Swap(block.Code + "#inventory", copy, mine, files, Exists, result);
                        block.TexturesInventory = copy;
                    }
                }
                if (result.Textures > before) result.Collectibles++;
            }

            foreach (EntityProperties type in entityTypes ?? api.World.EntityTypes)
            {
                if (dryRun) break;
                if (type?.Code == null || type.Code.Domain != VintageEModSystem.ModId || type.Client == null) continue;
                Override[] mine = overrides.Where(o => WildcardUtil.Match(o.Code, type.Code)).ToArray();
                int before = result.Textures;
                if (type.Client.Textures != null)
                {
                    var copy = new FastSmallDictionary<string, CompositeTexture>(type.Client.Textures);
                    Swap(type.Code.ToString(), copy, mine, files, Exists, result);
                    type.Client.Textures = copy;
                }
                AssetLocation shape = mine.LastOrDefault(o => o.Shape != null)?.Shape;
                if (shape != null && result.Textures > before && type.Client.Shape != null)
                {
                    // Only once its texture is ProjectE's: these shapes show the whole 16×16 sprite.
                    CompositeShape cs = type.Client.Shape.Clone();
                    cs.Base = shape;
                    type.Client.Shape = cs;
                    result.Swapped[type.Code + "#shape"] = shape.ToString();
                }
                if (result.Textures > before) result.Entities++;
            }

            result.MissingFiles = missing.OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (dryRun) return result;
            foreach (string m in result.MissingFiles) logger.Warning("{0} look: texture {1} is missing; kept Vintage-E's texture there.", look, m);
            foreach (string m in result.MissingShapes) logger.Warning("{0} look: model {1} is missing; kept Vintage-E's model there.", look, m);
            foreach (string m in result.Problems) logger.Warning("{0} look: {1}", look, m);
            logger.Notification("{0} look on: {1} new models, swapped {2} textures on {3} blocks/items and {4} entity types.", look, result.Models, result.Textures, result.Collectibles, result.Entities);
            return result;
        }

        /// <summary>Gives a block or item the New look's model, textures, draw type and (items) transforms.</summary>
        /// <summary>A copy of a block's textures as the game's own TextureDictionary (what its texture collection casts to).</summary>
        internal static TextureDictionary BlockTextures(IDictionary<string, CompositeTexture> from)
        {
            var d = new TextureDictionary(from?.Count ?? 0);
            if (from != null) foreach (var kv in from) d[kv.Key] = kv.Value;
            return d;
        }

        static void ApplyModel(CollectibleObject c, Override model, IDictionary<string, CompositeTexture> textures, Result result)
        {
            var shape = new CompositeShape { Base = model.Shape };
            if (c is Block block)
            {
                if (block.Shape != null)
                {
                    // Keep the turn a block has (its rotateY), only the model changes.
                    shape = block.Shape.Clone();
                    shape.Base = model.Shape;
                    shape.Alternates = null;
                    shape.Overlays = null;
                }
                block.Shape = shape;
                block.ShapeInventory = null;
                result.Swapped[c.Code + "#drawtype"] = model.DrawType ?? "";
                block.Textures = textures;
                // Never null: the game's texture collection casts and uses it without a check.
                block.TexturesInventory = BlockTextures(textures);
                if (model.DrawType != null) SetField(block, "DrawType", model.DrawType, result);
                if (model.SideOpaque.HasValue) SetSides(block, "SideOpaque", model.SideOpaque.Value, result);
                if (model.SelectionBox != null)
                {
                    block.SelectionBoxes = new[] { model.SelectionBox.Clone() };
                    result.Swapped[c.Code + "#selection"] = model.SelectionBox.Y2.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (c is Item item)
            {
                item.Shape = shape;
                item.Textures = textures as Dictionary<string, CompositeTexture> ?? new Dictionary<string, CompositeTexture>(textures);
                // A dedicated server frees item textures and transforms once they're sent, so note what was set.
                if (textures.ContainsKey("all")) result.Swapped[c.Code + "#keptall"] = "yes";
                foreach (var kv in model.Transforms)
                {
                    try
                    {
                        ModelTransform mt = kv.Value.ToObject<ModelTransform>();
                        if (mt == null) continue;
                        // Fills in what the map leaves out (origin, scale...), as the game does for an item's own json.
                        mt.GetType().GetMethod("EnsureDefaultValues", Type.EmptyTypes)?.Invoke(mt, null);
                        switch (kv.Key)
                        {
                            case "guiTransform": item.GuiTransform = mt; result.Swapped[c.Code + "#guirotx"] = mt.Rotation.X.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                            case "fpHandTransform": item.FpHandTransform = mt; break;
                            case "tpHandTransform": item.TpHandTransform = mt; break;
                            case "tpOffHandTransform": item.TpOffHandTransform = mt; break;
                            case "groundTransform": item.GroundTransform = mt; break;
                        }
                    }
                    catch (Exception e)
                    {
                        result.Problems.Add(c.Code + " " + kv.Key + ": " + e.Message);
                    }
                }
            }
            if (model.Set != null)
            {
                foreach (JProperty p in model.Set.Properties()) SetField(c, p.Name, p.Value, result);
            }
        }

        /// <summary>Sets a field or property by name, converting the value (enums by name). Logged, not thrown, if it fails.</summary>
        static void SetField(object target, string name, object value, Result result)
        {
            try
            {
                Type t = target.GetType();
                var field = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var prop = field == null ? t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) : null;
                Type type = field?.FieldType ?? prop?.PropertyType;
                if (type == null || prop != null && !prop.CanWrite) { result.Problems.Add("no settable " + name + " on " + t.Name); return; }
                object raw = value is JToken jt ? jt.ToObject<object>() : value;
                object v = type.IsEnum ? Enum.Parse(type, raw.ToString(), true) : Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture);
                if (field != null) field.SetValue(target, v); else prop.SetValue(target, v);
            }
            catch (Exception e)
            {
                result.Problems.Add("could not set " + name + ": " + e.Message);
            }
        }

        /// <summary>Sets all six of a block's per-side flags (SideOpaque...), whichever type this game version keeps them in.</summary>
        static void SetSides(Block block, string name, bool value, Result result)
        {
            try
            {
                Type t = block.GetType();
                var field = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var prop = field == null ? t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) : null;
                Type type = field?.FieldType ?? prop?.PropertyType;
                if (type == null) { result.Problems.Add("no " + name + " on blocks"); return; }
                object v;
                var flags = Enumerable.Repeat(value, 6).ToArray();
                if (type == typeof(bool[])) v = flags;
                else if (type.GetConstructor(new[] { typeof(bool[]) }) is System.Reflection.ConstructorInfo ca) v = ca.Invoke(new object[] { flags });
                else if (type.GetConstructor(new[] { typeof(int) }) is System.Reflection.ConstructorInfo ci) v = ci.Invoke(new object[] { value ? 0x3F : 0 });
                else { result.Problems.Add(name + " has a type this can't set: " + type.Name); return; }
                if (field != null) field.SetValue(block, v); else if (prop.CanWrite) prop.SetValue(block, v);
            }
            catch (Exception e)
            {
                result.Problems.Add("could not set " + name + ": " + e.Message);
            }
        }

        static void Swap(string owner, IDictionary<string, CompositeTexture> dict, Override[] mine, Dictionary<string, AssetLocation> files, System.Func<AssetLocation, bool> exists, Result result)
        {
            foreach (string key in dict.Keys.ToList())
            {
                CompositeTexture ct = dict[key];
                if (ct?.Base == null) continue;
                AssetLocation want = null;
                // Single faces / texture codes first (a collector's front), then the file-for-file swaps.
                foreach (Override o in mine) if (o.Textures.TryGetValue(key, out AssetLocation f)) want = f;
                if (want == null) files.TryGetValue(ct.Base.ToString(), out want);
                if (want == null || !exists(want)) continue;
                CompositeTexture swapped = ct.Clone();
                swapped.Base = want;
                dict[key] = swapped;
                result.Textures++;
                result.Swapped[owner + "#" + key] = want.ToString();
            }
        }

        /// <summary>True when this item's textures came from ProjectE (the server has ProjectETextures on).</summary>
        public static bool IsProjectE(Item item)
        {
            return item?.Textures != null && item.Textures.TryGetValue("all", out CompositeTexture ct) && ct?.Base?.Path?.StartsWith(PathPrefix) == true;
        }

        /// <summary>
        /// ProjectE's texture for this ring/stone in its current state, or null to keep the item's own: the "on" texture
        /// while it's switched on, the Ring of Arcana's current mode, Swiftwolf's flight/shield mode (like ProjectE's models).
        /// </summary>
        public static AssetLocation TrinketLook(ItemEmcTrinket trinket, ItemStack stack)
        {
            return IsProjectE(trinket) ? TrinketLook(trinket, stack, trinket.Textures["all"].Base) : null;
        }

        /// <summary>Same, given the item's ProjectE texture (the self-test passes it, as the server no longer has the item's textures).</summary>
        public static AssetLocation TrinketLook(ItemEmcTrinket trinket, ItemStack stack, AssetLocation baseTex)
        {
            if (trinket == null || baseTex?.Path?.StartsWith(PathPrefix) != true) return null;
            string path = baseTex.Path;
            bool on = ItemEmcTrinket.IsActive(stack);
            switch (trinket.Kind)
            {
                case "ringofarcana":
                    path = PathPrefix + "item/rings/arcana_" + GameMath.Clamp(ItemEmcTrinket.GetMode(stack), 0, 3) + (on ? "_on" : "");
                    break;
                case "swiftwolfsrendinggale":
                    // ProjectE: flight = on1, flight + shield = on2, shield = on3. Vintage-E's modes: both, flight, shield.
                    if (on) path = PathPrefix + "item/rings/swrg_on" + new[] { 2, 1, 3 }[GameMath.Clamp(ItemEmcTrinket.GetMode(stack), 0, 2)];
                    break;
                default:
                    if (on && path.EndsWith("_off")) path = path.Substring(0, path.Length - 4) + "_on";
                    break;
            }
            return path == baseTex.Path ? null : new AssetLocation(baseTex.Domain, path);
        }

        static readonly Dictionary<string, MultiTextureMeshRef> spriteMeshes = new Dictionary<string, MultiTextureMeshRef>();

        /// <summary>Draws the item as a flat sprite of <paramref name="texture"/> instead of its usual mesh (client side).</summary>
        public static void UseSprite(ICoreClientAPI capi, AssetLocation texture, ref ItemRenderInfo renderinfo)
        {
            if (texture == null) return;
            string key = texture.ToString();
            if (!spriteMeshes.TryGetValue(key, out MultiTextureMeshRef mesh))
            {
                mesh = null;
                try
                {
                    if (capi.Assets.Exists(texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
                    {
                        var ct = new CompositeTexture(texture);
                        ct.Bake(capi.Assets);
                        capi.ItemTextureAtlas.GetOrInsertTexture(ct, out _, out TextureAtlasPosition pos);
                        if (pos != null) mesh = capi.Render.UploadMultiTextureMesh(capi.Tesselator.VoxelizeTexture(ct, capi.ItemTextureAtlas.Size, pos));
                    }
                }
                catch (Exception e)
                {
                    capi.Logger.Warning("Vintage-E: could not build the ProjectE look {0}: {1}", key, e.Message);
                }
                spriteMeshes[key] = mesh;
            }
            if (mesh != null) renderinfo.ModelRef = mesh;
        }

        public static void DisposeMeshes()
        {
            foreach (MultiTextureMeshRef m in spriteMeshes.Values) m?.Dispose();
            spriteMeshes.Clear();
        }
    }
}
