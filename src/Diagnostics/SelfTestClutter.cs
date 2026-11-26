using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// 1.26.10: clutter a player picked up. The game marks it "collected" (BlockBehaviorReparable.GetDrops), which no
    /// clutter in the creative inventory has, and Vintage-E took the mark for part of what the clutter is: every
    /// picked-up clutter block was a version "the game no longer lists", so it burned, was learned, and was never
    /// offered. A player reported 100 to 150 of them gone that way. The checks here use a stack as the game drops it,
    /// not the creative inventory's (which is all the checks before had, and why they passed).
    /// </summary>
    public partial class SelfTest
    {
        void CheckCollectedClutter()
        {
            Line("Clutter that was picked up (1.26.10)");
            Block clutter = sapi.World.GetBlock(new AssetLocation("game:clutter"));
            CollectibleObject granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            ItemStack listed = clutter == null ? null : StackDataUtil.CreativeVersions(clutter, sapi.World).FirstOrDefault();
            string type = listed?.Attributes?.GetString("type");
            if (type == null || granite == null) { Check(false, "the game lists clutter types for the picked-up clutter checks"); return; }

            // As the game drops it when it is broken: its type, and the mark.
            var picked = new ItemStack(clutter);
            picked.Attributes.SetString("type", type);
            picked.Attributes.SetBool(StackDataUtil.CollectedKey, true);
            var creative = new ItemStack(clutter);
            creative.Attributes.SetString("type", type);

            string refused = mod.Transmutation.PlanBurn(null, picked, out TransmutationService.BurnPlan plan, out _);
            string pickedKey = plan?.Key, creativeKey = EmcCommands.KnowledgeKey(mod, creative);
            Check(refused == null && pickedKey != null && pickedKey == creativeKey && EmcCommands.KnowledgeKey(mod, picked) == creativeKey,
                $"picked-up clutter burns and is learned as the same thing as that clutter from the creative inventory ({type}: {refused ?? "burns"}, {(pickedKey == creativeKey ? "one entry" : "two entries")})");
            ItemStack made = pickedKey == null ? null : TransmutationService.Prototype(mod, sapi.World, pickedKey);
            string why = pickedKey == null ? "?" : TransmutationService.WhyNotOffered(mod, sapi.World, pickedKey);
            Check(made != null && why == null && made.Attributes.GetString("type") == type && made.Attributes.GetBool(StackDataUtil.CollectedKey)
                  && mod.Emc.GetValue(picked) > 0 && Math.Abs(mod.Emc.GetValue(made) - mod.Emc.GetValue(picked)) < 1e-9,
                $"learned clutter is offered, and made with the game's picked-up mark at the price it burned for (offered: {why ?? "yes"}, {mod.Emc.GetValue(picked)} EMC)");
            var plain = new ItemStack(granite);
            StackDataUtil.MarkCollected(plain);
            Check(!plain.Attributes.HasAttribute(StackDataUtil.CollectedKey) && TransmutationService.Prototype(mod, sapi.World, PlayerEmc.KeyOf(granite))?.Attributes?.HasAttribute(StackDataUtil.CollectedKey) == false,
                "nothing but a block the game repairs gets that mark");

            // What players learned before this version: the mark was part of the version, and nothing showed it.
            var before = new TreeAttribute();
            before.SetBool(StackDataUtil.CollectedKey, true);
            before.SetString("type", type);
            string oldKey = PlayerEmc.ExactKey(clutter, before), copper = "i:game:ingot-copper";
            string[] tidied = PlayerEmc.Tidied(new[] { copper, oldKey, pickedKey, oldKey }, sapi.World, out bool changed);
            Check(oldKey != pickedKey && TransmutationService.Prototype(mod, sapi.World, oldKey) != null && TransmutationService.WhyNotOffered(mod, sapi.World, oldKey) == null,
                "clutter learned before 1.26.10, with the mark in its version, is offered again as it stands");
            Check(changed && tidied.Length == 2 && tidied[0] == copper && tidied[1] == pickedKey,
                $"and when its player next joins it becomes the one entry for that clutter ({tidied.Length} entries left of 4)");
            string[] again = PlayerEmc.Tidied(tidied, sapi.World, out bool changedAgain);
            Check(!changedAgain && again.SequenceEqual(tidied), "knowledge that is already as it would be learned today is left alone");

            // What the version check is for still holds: a type the game doesn't have, or none at all.
            var gone = new TreeAttribute();
            gone.SetString("type", "nosuchclutter-from-a-removed-mod");
            var goneMarked = new TreeAttribute();
            goneMarked.SetBool(StackDataUtil.CollectedKey, true);
            goneMarked.SetString("type", "nosuchclutter-from-a-removed-mod");
            var onlyTexture = new TreeAttribute();
            onlyTexture.SetString("overrideTextureCode", "sometexture");
            string goneKey = PlayerEmc.ExactKey(clutter, gone);
            Check(TransmutationService.Prototype(mod, sapi.World, goneKey) == null && TransmutationService.WhyNotOffered(mod, sapi.World, goneKey) == "version"
                  && TransmutationService.Prototype(mod, sapi.World, PlayerEmc.ExactKey(clutter, goneMarked)) == null
                  && TransmutationService.Prototype(mod, sapi.World, PlayerEmc.ExactKey(clutter, onlyTexture)) == null,
                "a clutter type the game doesn't have is still not offered, with the mark or without, and neither is clutter with no type");
            var textured = new TreeAttribute();
            textured.SetString("overrideTextureCode", "sometexture");
            textured.SetString("type", type);
            ItemStack texturedMade = TransmutationService.Prototype(mod, sapi.World, PlayerEmc.ExactKey(clutter, textured));
            Check(texturedMade?.Attributes.GetString("overrideTextureCode") == "sometexture" && texturedMade.Attributes.GetString("type") == type,
                "a clutter version carrying something the game's list never mentions (a texture chosen for it) is offered as it is");
            Check(TransmutationService.WhyNotOffered(mod, sapi.World, PlayerEmc.KeyOf(clutter)) == "typeless" && TransmutationService.WhyNotOffered(mod, sapi.World, "i:game:nosuchitem-from-a-removed-mod") == "gone"
                  && TransmutationService.WhyNotOffered(mod, sapi.World, PlayerEmc.KeyOf(granite)) == null,
                "the doctor can say why a learned thing isn't offered: no type, gone with its mod, or nothing wrong");

            // The doctor's page about a player (which needs its player online, so the finding is asked for by itself).
            EmcReport finding = EmcDiagnose.NotOfferedFor(mod, sapi, "en", new[] { PlayerEmc.KeyOf(granite), pickedKey, oldKey, goneKey, PlayerEmc.KeyOf(clutter), "i:game:nosuchitem-from-a-removed-mod" });
            string said = string.Join("\n", finding.Lines.Select(l => l.Text));
            bool warns = finding.Lines.Count > 0 && finding.Lines[0].Style == EmcLineStyle.Check && finding.Lines[0].Text.StartsWith("⚠", StringComparison.Ordinal);
            int copyButtons = EmcDiagnose.AddCopyButtons(mod, sapi, "en", finding);
            var handed = new TreeAttribute();
            DoctorBridge.Write(finding, handed);
            bool doctors = LinksAreTheDoctors(Texts(handed), out string badLink);
            Check(warns && said.Contains("3 of the 6") && said.Contains("nosuchclutter-from-a-removed-mod") && said.Contains("b:game:clutter") && said.Contains("i:game:nosuchitem-from-a-removed-mod")
                  && said.IndexOfAny(new[] { '{', '}', '|' }) < 0 && copyButtons == 1 && doctors,
                $"the doctor's page about a player warns of learned things the table doesn't offer, each kind with its examples, under one copy button ({finding.Lines.Count} lines{(badLink != null ? ", not for Vintage Doctor: " + badLink : "")}) [{said.Replace("\n", " / ")}]");
            Check(EmcDiagnose.NotOfferedFor(mod, sapi, "en", new[] { PlayerEmc.KeyOf(granite), pickedKey, oldKey }).Lines.Count == 0, "a player whose learned things are all offered gets no such finding");

            // The same fault anywhere else. Until 1.26.9 every block that needs its data had to fit inside a version the
            // creative inventory lists, which shows a selection: whatever a recipe makes outright has to be offered.
            // (A recipe that copies its data from an ingredient makes whatever went in; its own output says too little.)
            int outputs = 0, hiddenBefore = 0;
            var hidden = new List<string>();
            var hiddenBeforeBy = new Dictionary<string, int>(StringComparer.Ordinal);
            var needs = new Dictionary<CollectibleObject, bool>();
            foreach (GridRecipe recipe in sapi.World.GridRecipes)
            {
                ItemStack output = recipe?.Output?.ResolvedItemStack;
                if (output?.Collectible == null || !recipe.Enabled || recipe.CopyAttributesFrom != null) continue;
                if (!needs.TryGetValue(output.Collectible, out bool typed)) needs[output.Collectible] = typed = StackDataUtil.NeedsData(output.Collectible, sapi.World);
                TreeAttribute kept = typed ? StackDataUtil.KeptData(output) : null;
                if (kept == null) continue;
                outputs++;
                bool any = false, fitted = false;
                foreach (ItemStack version in StackDataUtil.CreativeVersions(output.Collectible, sapi.World))
                {
                    any = true;
                    if (StackDataUtil.IsSubset(sapi.World, kept, version.Attributes)) { fitted = true; break; }
                }
                if (any && !fitted)
                {
                    hiddenBefore++;
                    string code = output.Collectible.Code.ToString();
                    hiddenBeforeBy[code] = hiddenBeforeBy.TryGetValue(code, out int n) ? n + 1 : 1;
                }
                if (TransmutationService.WhyNotOffered(mod, sapi.World, PlayerEmc.ExactKey(output.Collectible, kept)) == "version" && hidden.Count < 12) hidden.Add(output.Collectible.Code + " " + kept.ToJsonToken());
            }
            Line($"  typed blocks made outright by grid recipes: {outputs}; not offered until 1.26.9: {hiddenBefore} ("
                + string.Join(", ", hiddenBeforeBy.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(12).Select(kv => kv.Key + " x" + kv.Value)) + ")");
            Check(hidden.Count == 0 && outputs > 0, $"every typed block a grid recipe makes outright is a version the table offers ({outputs} such outputs; {hiddenBefore} of them were hidden until 1.26.9)"
                + (hidden.Count > 0 ? ": not " + string.Join("; ", hidden) : ""));

            // And the versions are still told apart where it matters: a shield or a lantern is offered whatever it is made of.
            CollectibleObject shield = sapi.World.GetItem(new AssetLocation("game:shield-woodmetal"));
            Check(shield != null && !StackDataUtil.TypeDefined(shield) && StackDataUtil.TypeDefined(clutter) && StackDataUtil.NeedsData(shield, sapi.World),
                "only a block whose type defines it (clutter, a tapestry, a typed chest) is held to the game's list of types; a shield needs its materials and is never held to it");
        }
    }
}
