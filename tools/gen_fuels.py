#!/usr/bin/env python3
"""Project Expansion's fuel tiers for Vintage-E (1.29.4): magenta to white fuel after aeternalis, and their blocks. Run by
tools/gen_expansion.py after tools/gen_books.py (it adds to the look maps those write):

    python3 tools/gen_expansion.py

Writes (and rewrites):
  - the new states of itemtypes/alchemicalfuel.json and blocktypes/alchemicalfuelblock.json (taken out with Expansion
    off; see gen_expansion.manifest), with their burn times and forge numbers;
  - recipes/grid/expansion-fuels.json: Project Expansion's 4 of the fuel before with the philosopher's stone (kept),
    and 9 to a block and back as the other fuels;
  - their language lines (only the lines this script owns);
  - textures for the three looks and their entries in the companion look maps.

Each tier burns at aeternalis's 1,600 °C (the hottest the game's fuels go) for 4 times as long as the one before, as
alchemical coal, mobius and aeternalis do; in a forge it lasts 4 times as long again each tier.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_expansion as gx  # noqa: E402
import gen_newlook as gn  # noqa: E402
import gen_textures as gt  # noqa: E402

MOD, G = gx.MOD, gx.G
ing = gx.ing
BASE = ["alchemicalcoal", "mobiusfuel", "aeternalisfuel"]
# Project Expansion's fuels (Fuel.java): the matter colours from magenta to white, without fading and final.
NAMES = ["magenta", "pink", "purple", "violet", "blue", "cyan", "green", "lime", "yellow", "orange", "white"]
COLOURS = {t[1]: t for t in gx.TIERS}


def burn(n):
    """The n-th new tier (0 = magenta): seconds burning, forge duration multiplier."""
    return 2560 * 4 ** (n + 1), 64 * 4 ** (n + 1)


def code(name):
    return f"{name}fuel"


def n(v):
    return gx.n(v)


def types():
    d = gx.load("itemtypes/alchemicalfuel.json")
    gx.set_states(d, "type", BASE, [code(x) for x in NAMES])
    for i, name in enumerate(NAMES):
        secs, mul = burn(i)
        d["combustiblePropsByType"][f"*-{code(name)}"] = {"burnTemperature": 1600, "burnDuration": secs}
        d["attributesByType"][f"*-{code(name)}"] = {"inForge": {"durationMul": mul, "tempGainDeg": 600}}
    gx.dump("itemtypes/alchemicalfuel.json", d)
    b = gx.load("blocktypes/alchemicalfuelblock.json")
    gx.set_states(b, "type", BASE, [code(x) for x in NAMES])
    for i, name in enumerate(NAMES):
        secs, _ = burn(i)
        mk, _, core, base, glow, hue, sat = COLOURS[name]
        b["combustiblePropsByType"][f"*-{code(name)}"] = {"burnTemperature": 1600, "burnDuration": secs * 9}
        b["lightHsvByType"][f"*-{code(name)}"] = [hue, sat, min(12, 8 + i // 3)]
    gx.dump("blocktypes/alchemicalfuelblock.json", b)


def recipes():
    stone = {**ing(f"{MOD}:philosophersstone"), "consume": False}
    rs = []
    prev = f"{MOD}:alchemicalfuel-aeternalisfuel"
    for name in NAMES:
        fuel = f"{MOD}:alchemicalfuel-{code(name)}"
        block = f"{MOD}:alchemicalfuelblock-{code(name)}"
        rs.append({"ingredientPattern": "PFF,FF_", "ingredients": {"P": stone, "F": ing(prev)}, "width": 3, "height": 2, "shapeless": True,
                   "output": ing(fuel), "name": fuel})
        rs.append({"ingredientPattern": "FFF,FFF,FFF", "ingredients": {"F": ing(fuel)}, "width": 3, "height": 3, "output": ing(block, "block"), "name": block})
        rs.append({"ingredientPattern": "B", "ingredients": {"B": ing(block, "block")}, "width": 1, "height": 1,
                   "output": {**ing(fuel), "quantity": 9}, "name": fuel + "-from-block"})
        prev = fuel
    gx.dump("recipes/grid/expansion-fuels.json", rs)
    return rs


def lang_lines():
    out = {}
    prev = "aeternalis fuel"
    for i, name in enumerate(NAMES):
        secs, mul = burn(i)
        title = name.capitalize()
        text = (f"Burns at 1,600 °C for {n(secs)} seconds. In a forge it lasts {n(mul)}x as long and burns 600 °C hotter. "
                f"Made from 4 {prev} with the philosopher's stone, which you keep (Project Expansion). An Energy Collector's fuel slot "
                f"upgrades fuel up to it, paying the difference in value from its EMC.")
        out[f"item-alchemicalfuel-{code(name)}"] = f"{title} Fuel"
        out[f"itemdesc-alchemicalfuel-{code(name)}"] = text
        out[f"vintagee:item-handbooktitle-vintagee:alchemicalfuel-{code(name)}"] = "How it works"
        out[f"vintagee:item-handbooktext-vintagee:alchemicalfuel-{code(name)}"] = text
        out[f"block-alchemicalfuelblock-{code(name)}"] = f"{title} Fuel Block"
        out[f"blockdesc-alchemicalfuelblock-{code(name)}"] = f"9 {name} fuel: burns at 1,600 °C for {n(secs * 9)} seconds."
        prev = f"{name} fuel"
    return out


def textures_and_looks():
    written = []
    new_stops = {}
    for i, name in enumerate(NAMES):
        mk, _, core, base, glow, hue, sat = COLOURS[name]
        # Middle: Vintage-E's lumps and fuel block in the tier's colours, glowing more each tier as the three before do.
        # Brighter than the tier's matter, as aeternalis is pale and bright beside the darker coal and mobius.
        body = gt.mix(gt.rgba(core), gt.rgba(base), 0.35)
        written.append(gt.save(gt.lump(body, gt.rgba(glow), "fuel" + name, 1.4 + 0.1 * i), f"item/alchemicalfuel-{code(name)}"))
        written.append(gt.save(gt.fuel_block(body, gt.rgba(glow), "fb" + name), f"block/alchemicalfuelblock-{code(name)}"))
        # Original: Project Expansion's.
        written.append(gx.save_png(gx.vendor(f"fuel-{name}"), f"projecte/expansion/fuel-{name}"))
        written.append(gx.save_png(gx.vendor(f"fuelblock-{name}"), f"projecte/expansion/fuelblock-{name}"))
        # New: the game's coal-like packed lumps in the tier's muted colours (its matter's), as aeternalis is drawn.
        dark, mid, light, vein, glint = gx.new_matter_stops(name)
        new_stops[name] = [(0, dark), (0.5, mid), (0.85, light), (1, glint)]

    path = os.path.join(gx.ROOT, "config", "vintagee-projecte-look-expansion.json")
    orig = json.load(open(path))
    for name in NAMES:
        orig["textures"][f"{MOD}:item/alchemicalfuel-{code(name)}"] = f"{MOD}:projecte/expansion/fuel-{name}"
        orig["textures"][f"{MOD}:block/alchemicalfuelblock-{code(name)}"] = f"{MOD}:projecte/expansion/fuelblock-{name}"
    gx.dump("config/vintagee-projecte-look-expansion.json", orig)

    path = os.path.join(gx.ROOT, "config", "vintagee-new-look-expansion.json")
    new = json.load(open(path))
    new["overrides"] = [o for o in new["overrides"] if not o["code"].startswith((f"{MOD}:alchemicalfuel-", f"{MOD}:alchemicalfuelblock-"))]
    lump = f"{MOD}:newlook/item/lump"
    for i, name in enumerate(NAMES):
        tex = gn.save(gn.lumps(70 + i, new_stops[name]), f"block/fuel-{name}")
        new["textures"][f"{MOD}:block/alchemicalfuelblock-{code(name)}"] = tex
        new["overrides"].append(gn.item(f"alchemicalfuel-{code(name)}", lump, {"fuel": tex}, f"{name} fuel"))
        new["overrides"].append({"code": f"{MOD}:alchemicalfuelblock-{code(name)}", "label": f"{name} fuel block", "cube": True, "preview_only": True,
                                 "textures": {f: tex for f in gn.FACES}})
    gx.dump("config/vintagee-new-look-expansion.json", new)
    return written


def main():
    types()
    rs = recipes()
    entries = lang_lines()
    gx.write_lang(entries)
    tex = textures_and_looks()
    print(f"fuels: {len(NAMES)} tiers, {len(rs)} recipes, {len(entries)} language lines, {len(tex)} textures")


if __name__ == "__main__":
    main()
