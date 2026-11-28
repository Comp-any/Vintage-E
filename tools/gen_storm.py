#!/usr/bin/env python3
"""The Storm Collector for Vintage-E (1.29.4): EMC from rain, thunderstorms, lightning and temporal storms. MK1 to MK3,
always there (not one of Project Expansion's tiers). Run by tools/gen_expansion.py after tools/gen_fuels.py:

    python3 tools/gen_expansion.py

Writes (and rewrites): blocktypes/stormcollector.json, recipes/grid/storm.json, its language lines, its Middle look
textures (also used by the Original look: there's no ProjectE art for it) and the New look's model (the collector's
with a copper lightning rod on top) in the companion New look map.
"""
import json
import os
import sys

from PIL import ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_expansion as gx  # noqa: E402
import gen_newlook as gn  # noqa: E402
import gen_textures as gt  # noqa: E402

MOD, G = gx.MOD, gx.G
ing = gx.ing
# tier, the collectors' core colour, New look metal and glow (as the collectors MK1-3).
TIERS = [(1, "ffd24a", "brass", "citrine"), (2, "9b5cff", "silver", "amethyst"), (3, "ff4a3c", "gold", "ruby")]


def numbers(mk):
    rain = gx.steps(mk, 8)
    return {"emcPerSecond": rain, "thunderMultiplier": 3, "temporalPerSecond": gx.steps(mk, 40), "lightningEmc": gx.steps(mk, 4000),
            "capacity": 2500 * rain}


def types():
    gx.dump("blocktypes/stormcollector.json", {
        "code": "stormcollector",
        "class": "VintageEBlockEmcMachine",
        "entityClass": "VintageEStormCollector",
        # The game's lightning rod behaviour: it draws the strikes around it.
        "entityBehaviors": [{"name": "AttractsLightning", "properties": {"artificialElevation": 5, "elevationAttractivenessMultiplier": 2}}],
        "variantgroups": [{"code": "tier", "states": [f"mk{t[0]}" for t in TIERS]}],
        "creativeinventory": {"general": ["*"], "mechanics": ["*"], "vintagee": ["*"]},
        "blockmaterial": "Metal",
        "drawtype": "cube",
        "textures": {"up": {"base": "block/stormcollector-{tier}-top"}, "down": {"base": "block/transmutationtable-bottom"},
                     **{f: {"base": "block/stormcollector-{tier}-side"} for f in ("north", "east", "south", "west")}},
        "attributesByType": {f"*-mk{mk}": {**numbers(mk), "sneakInteractionHelp": "vintagee:blockhelp-collector-withdraw"} for mk, *_ in TIERS},
        "resistance": 4,
        "requiredMiningTier": 2,
        "sounds": {"place": "game:block/ingot", "break": "game:block/ingot", "hit": "game:block/ingot"},
    })


def recipes():
    glass = ing(G + "glass-*", "block")
    rs = [{"ingredientPattern": "GLG,GCG,GTG", "ingredients": {"G": glass, "L": ing(G + "lightningrod", "block"), "C": ing(f"{MOD}:energycollector-mk1", "block"),
                                                                "T": ing(G + "gear-temporal")},
           "width": 3, "height": 3, "output": ing(f"{MOD}:stormcollector-mk1", "block"), "name": f"{MOD}:stormcollector-mk1"}]
    for mk, matter in ((2, "dark"), (3, "red")):
        rs.append({"ingredientPattern": "GMG,GSG,GGG", "ingredients": {"G": glass, "M": ing(f"{MOD}:matter-{matter}"), "S": ing(f"{MOD}:stormcollector-mk{mk - 1}", "block")},
                   "width": 3, "height": 3, "output": ing(f"{MOD}:stormcollector-mk{mk}", "block"), "name": f"{MOD}:stormcollector-mk{mk}"})
    gx.dump("recipes/grid/storm.json", rs)
    return rs


def lang_lines():
    out = {}
    made = {1: "Made from an Energy Collector MK1 with the game's lightning rod above it, a temporal gear below and glass around it.",
            2: "Made from a Storm Collector MK1 in glass with dark matter, as collectors are.",
            3: "Made from a Storm Collector MK2 in glass with red matter, as collectors are."}
    for mk, *_ in TIERS:
        x = numbers(mk)
        text = (f"Makes EMC from the weather instead of the sun: up to {gx.n(x['emcPerSecond'])} EMC a second in full rain or snow (it must see the sky), "
                f"three times that in a thunderstorm, {gx.n(x['temporalPerSecond'])} a second more during a temporal storm wherever it stands, and "
                f"{gx.n(x['lightningEmc'])} at once when lightning strikes it. It's a lightning rod too and draws the strikes around it, so stay clear of it "
                f"in a thunderstorm. Stores {gx.n(x['capacity'])} and feeds condensers, relays, furnaces and EMC Links next to it; sneak + right-click with "
                f"an empty hand or a Klein star to take the EMC. The Matter Upgrader takes it to the next tier. {made[mk]}")
        out[f"block-stormcollector-mk{mk}"] = f"Storm Collector MK{mk}"
        out[f"blockdesc-stormcollector-mk{mk}"] = text
        out[f"vintagee:block-handbooktitle-vintagee:stormcollector-mk{mk}"] = "How it works"
        out[f"vintagee:block-handbooktext-vintagee:stormcollector-mk{mk}"] = text
    out["storm-rate"] = "Makes {0} EMC a second from the weather"
    out["diag-mach-storm"] = "{0}: {1} of {2} EMC, makes {3} a second; rain or snow {4}%, thunderstorm {5}, temporal storm {6}; struck by lightning {7} times since the server started."
    out["diag-mach-storm-roof"] = "It doesn't see the sky: no rain, snow or lightning reaches it."
    out["diag-mach-storm-roof-why"] = "Something above it stops the rain. Only a temporal storm gives it EMC there: move it into the open, or take away what's above it."
    out["diag-mach-storm-calm"] = "It's calm: it makes EMC when it rains or snows, more in a thunderstorm, during temporal storms, and when lightning strikes it."
    return out


def storm_top(core, seed):
    img = gt.noise_fill(gt.rgba("46505a"), 0.06, seed)
    d = ImageDraw.Draw(img)
    for (x0, y0, x1, y1) in ((3, 3, 17, 11), (11, 2, 28, 12), (6, 7, 24, 14)):
        d.ellipse((x0, y0, x1, y1), fill=gt.rgba("8a96a2"))
    d.polygon([(17, 10), (11, 20), (15, 20), (12, 29), (21, 16), (17, 16), (20, 10)], fill=core, outline=gt.mix(core, (255, 255, 255, 255), 0.5))
    return gt.bevel(img, gt.rgba("9aa6b2"), gt.rgba("20262c"))


def storm_side(core, seed):
    img = gt.noise_fill(gt.rgba("3e4852"), 0.06, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 31, 3), fill=gt.rgba("8a96a2"))
    d.rectangle((4, 8, 27, 27), fill=gt.rgba("2a3038"))
    for x in range(5, 30, 4):
        d.line((x, 9, x - 3, 26), fill=gt.rgba("6f8aa6"))
    d.rectangle((13, 14, 18, 21), fill=core)
    return gt.bevel(img, gt.rgba("7d8892"), gt.rgba("1a2026"))


def textures_and_looks():
    written = []
    for mk, core, *_ in TIERS:
        c = gt.rgba(core)
        written.append(gt.save(storm_top(c, f"st{mk}"), f"block/stormcollector-mk{mk}-top"))
        written.append(gt.save(storm_side(c, f"ss{mk}"), f"block/stormcollector-mk{mk}-side"))
    # New look: the collector's model with a copper lightning rod on top.
    base = json.load(open(os.path.join(gn.SHAPES, "block", "energycollector.json")))
    els = json.loads(json.dumps(base["elements"]))
    els += [gn.box("rod", (7.5, 16, 7.5), (8.5, 22.5, 8.5), "rod"), gn.box("rod-tip", (7.25, 22.5, 7.25), (8.75, 23.5, 8.75), "rod"),
            gn.box("rod-foot", (6.5, 15.5, 6.5), (9.5, 16.5, 9.5), "rod")]
    shape = gn.save_shape("block/stormcollector", gn.shape({**base["textures"], "rod": G + "block/metal/plate/copper"}, els))
    path = os.path.join(gx.ROOT, "config", "vintagee-new-look-expansion.json")
    new = json.load(open(path))
    new["overrides"] = [o for o in new["overrides"] if not o["code"].startswith(f"{MOD}:stormcollector")]
    for mk, core, metal, gem in TIERS:
        g = G + "block/stone/gem/" + gem
        new["overrides"].append(gn.block(f"stormcollector-mk{mk}", shape, {"metal": G + "block/metal/plate/" + metal, "glow": g,
                                                                          "glass": G + "block/stone/gem/aquamarine"}, f"storm collector mk{mk}"))
    gx.dump("config/vintagee-new-look-expansion.json", new)
    return written


def main():
    types()
    rs = recipes()
    entries = lang_lines()
    gx.write_lang(entries)
    tex = textures_and_looks()
    print(f"storm collector: {len(TIERS)} tiers, {len(rs)} recipes, {len(entries)} language lines, {len(tex)} textures")


if __name__ == "__main__":
    main()
