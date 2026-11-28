#!/usr/bin/env python3
"""Project Expansion's tiers for Vintage-E (1.29.0): MK4 to MK16 collectors and relays, the matter that upgrades
them (magenta to fading, and the Final Star Shard), matter blocks, and the Magnum, Colossal and Gargantuan stars.

    python3 tools/gen_expansion.py

Writes (and rewrites, so it can be run again after changing the tables below):
  - the new tiers' states and attributes in blocktypes/energycollector.json, antimatterrelay.json, matterblock.json and
    itemtypes/matter.json; itemtypes/star.json and finalstarshard.json; the worn star shapes;
  - recipes/grid/expansion.json; the language lines (assets/vintagee/lang/en.json, only the lines it owns);
  - the companion look maps config/vintagee-projecte-look-expansion.json and vintagee-new-look-expansion.json, read
    alongside the generated maps (ProjectELook.LoadMap);
  - config/vintagee-expansion.json: what the Expansion setting takes out when it's off (ExpansionTiers.cs);
  - textures: the default (Middle) look drawn with tools/gen_textures.py's own code in each tier's colours, the New
    look's matter with tools/gen_newlook.py's in muted colours (its collectors and relays glow with the game's gems,
    as MK1-3 do), and the Original look and every star from Project Expansion's art
    (MIT, tools/vendor/projectexpansion; see THIRD-PARTY-NOTICES.md).

The numbers continue Vintage-E's own MK1-3 (x3, then x10/3, so every two tiers is x10), not Project Expansion's x6.
"""
import json
import os
import re
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_newlook as gn  # noqa: E402
import gen_textures as gt  # noqa: E402

ROOT = os.path.join(HERE, "..", "assets", "vintagee")
VENDOR = os.path.join(HERE, "vendor", "projectexpansion")
MOD = "vintagee"

# mk, matter, the Middle look's core colour, matter base, matter glow, light hue (0-63), light saturation (0-7)
TIERS = [
    (4, "magenta", "ff40ff", "4a0a4a", "ff70ff", 52, 5),
    (5, "pink", "ff8ac8", "5a1e3e", "ffb8dc", 58, 4),
    (6, "purple", "a040e0", "2e0c46", "c47aff", 49, 5),
    (7, "violet", "7a3aff", "1e0e54", "a98cff", 46, 5),
    (8, "blue", "3a5aff", "0a1450", "7c96ff", 40, 6),
    (9, "cyan", "30d8e8", "06424a", "8af6ff", 32, 5),
    (10, "green", "30c040", "08420e", "7cff8a", 21, 5),
    (11, "lime", "9aff30", "2e4e06", "d4ff8a", 16, 5),
    (12, "yellow", "ffe030", "4e400a", "fff28a", 10, 5),
    (13, "orange", "ff9020", "4e2206", "ffc47a", 5, 6),
    (14, "white", "f4f4f4", "8a8a92", "ffffff", 0, 0),
    (15, "fading", "8a8a9a", "141418", "cfcfe6", 0, 0),
    (16, "final", "ffd700", "3a2806", "fff2a8", 8, 4),
]
MATTERS = [t for t in TIERS if t[1] != "final"]  # Final has the Final Star Shard as its matter, and no block
STAR_SIZES = ["magnum", "colossal", "gargantuan"]
STAR_TIERS = ["ein", "zwei", "drei", "vier", "sphere", "omega"]
MAGNUM_EIN = 204_800_000  # Klein Star Omega (51,200,000) x 4

# The New look: frame metal by Project Expansion's rarity bands, star metal by size, star gem by tier (as the Klein stars).
TIER_METAL = {**{m: "platinum" for m in ("magenta", "pink", "purple", "violet", "blue")},
              **{m: "electrum" for m in ("cyan", "green", "lime", "yellow")},
              **{m: "titanium" for m in ("orange", "white", "fading")}, "final": "gold"}
# The New look glows with the game's own gems, as MK1-3 do (citrine, amethyst, ruby), wherever the game has one in the
# tier's colour; magenta, pink and fading have none and glow with their matter.
TIER_GEM = {"purple": "amethyst", "violet": "onyx", "blue": "sapphire", "cyan": "aquamarine", "green": "emerald", "lime": "olivine",
            "yellow": "citrine", "orange": "topaz", "white": "quartz", "final": "diamond"}
# The New look's matter, in Vintage Story's muted colours: hue (degrees) and saturation, drawn dark with light veins
# like the New look's dark and red matter (gen_newlook.py); white is light, fading nearly colourless.
NEW_MATTER = {"magenta": (305, 0.5), "pink": (330, 0.38), "purple": (282, 0.5), "violet": (262, 0.5), "blue": (222, 0.5),
              "cyan": (188, 0.5), "green": (128, 0.45), "lime": (88, 0.45), "yellow": (50, 0.5), "orange": (28, 0.55),
              "white": (40, 0.05), "fading": (250, 0.08), "final": (44, 0.55)}
STAR_METAL = {"magnum": "silver", "colossal": "gold", "gargantuan": "electrum"}
# The New look tells the sizes apart as Project Expansion's sprites do, by rings around the Klein star's model: one ring
# for Magnum, two for Colossal, three (an armillary setting) for Gargantuan, in the size's metal.
STAR_RINGS = {"magnum": 1, "colossal": 2, "gargantuan": 3}
STAR_GEM = {"ein": "quartz", "zwei": "citrine", "drei": "topaz", "vier": "emerald", "sphere": "sapphire", "omega": "amethyst"}
G = "game:"


def steps(mk, base):
    """Vintage-E's curve: base, x3, x10 (of base), x30, x100... (MK1 4, MK2 12, MK3 40, MK4 120 for a collector)."""
    return base * 10 ** ((mk - 1) // 2) * (3 if (mk - 1) % 2 else 1)


def collector(mk):
    rate = steps(mk, 4)
    return {"emcPerSecond": rate, "capacity": 1500 * rate}  # MK3 keeps 1,500 seconds of its output


def relay(mk):
    transfer = steps(mk, 64)
    return {"capacity": 15625 * transfer, "bonusPerSecond": steps(mk, 1), "transferPerSecond": transfer}  # MK3: 15,625 s


def star_capacity(size, tier):
    return MAGNUM_EIN * 4 ** (STAR_SIZES.index(size) * 6 + STAR_TIERS.index(tier))


def matter_of(mk):
    """The item that upgrades to this tier: its matter, or for Final the Final Star Shard."""
    name = next(t[1] for t in TIERS if t[0] == mk)
    return f"{MOD}:finalstarshard" if name == "final" else f"{MOD}:matter-{name}"


def n(v):
    return f"{v:,}"


def rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mixc(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def new_matter_stops(name):
    """Five colours for gen_newlook.matter_texture (dark, mid, light, vein, glint), as its dark matter's: about
    11%, 23%, 36%, 63% and 90% bright, half saturated; white matter light, the Final tier's brighter, like red matter."""
    import colorsys
    hue, sat = NEW_MATTER[name]
    values = {"white": (0.42, 0.56, 0.68, 0.84, 0.98), "final": (0.15, 0.30, 0.45, 0.75, 0.97)}.get(name, (0.11, 0.23, 0.36, 0.63, 0.90))
    sats = (sat, sat, sat * 0.95, sat * 1.0, sat * 0.6)
    return tuple(tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(hue / 360, s, v)) for s, v in zip(sats, values))


# ---------------------------------------------------------------- files


def load(rel):
    return json.load(open(os.path.join(ROOT, rel)))


def dump(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    return path


def set_states(data, group, base_states, extra):
    vg = next(g for g in data["variantgroups"] if g["code"] == group)
    vg["states"] = [s for s in vg["states"] if s in base_states] + extra


def frame0(img):
    """Project Expansion's animated textures (16 wide, frames stacked): the first frame."""
    img = img.convert("RGBA")
    return img.crop((0, 0, img.width, img.width)) if img.height > img.width else img


def vendor(name):
    return frame0(Image.open(os.path.join(VENDOR, name + ".png")))


def save_png(img, rel):
    path = os.path.join(ROOT, "textures", rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


def blocks_and_items():
    mks = [f"mk{t[0]}" for t in TIERS]
    # Collectors.
    d = load("blocktypes/energycollector.json")
    set_states(d, "tier", ["mk1", "mk2", "mk3"], mks)
    for mk, name, core, base, glow, hue, sat in TIERS:
        d["attributesByType"][f"*-mk{mk}"] = {**collector(mk), "interactionHelp": "vintagee:blockhelp-collector",
                                              "sneakInteractionHelp": "vintagee:blockhelp-collector-withdraw"}
        d["lightHsvByType"][f"*-mk{mk}"] = [hue, sat, min(14, 10 + (mk - 3) // 3)]
    dump("blocktypes/energycollector.json", d)
    # Relays.
    d = load("blocktypes/antimatterrelay.json")
    set_states(d, "tier", ["mk1", "mk2", "mk3"], mks)
    for mk, name, core, base, glow, hue, sat in TIERS:
        d["attributesByType"][f"*-mk{mk}"] = {**relay(mk), "sneakInteractionHelp": "vintagee:blockhelp-relay-withdraw"}
        d["lightHsvByType"][f"*-mk{mk}"] = [hue, sat, min(12, 8 + (mk - 3) // 3)]
    dump("blocktypes/antimatterrelay.json", d)
    # Matter and matter blocks.
    names = [t[1] for t in MATTERS]
    d = load("itemtypes/matter.json")
    set_states(d, "type", ["dark", "red"], names)
    for mk, name, core, base, glow, hue, sat in MATTERS:
        d["lightHsvByType"][f"*-{name}"] = [hue, sat, 5 + (mk - 3) // 4]
    dump("itemtypes/matter.json", d)
    d = load("blocktypes/matterblock.json")
    set_states(d, "type", ["dark", "red"], names)
    for mk, name, core, base, glow, hue, sat in MATTERS:
        d["lightHsvByType"][f"*-{name}"] = [hue, sat, 6 + (mk - 3) // 4]
    dump("blocktypes/matterblock.json", d)
    # Stars: Klein stars' class and data, three more sizes.
    attrs = {}
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            code = f"star-{size}-{tier}"
            attrs[f"*-{size}-{tier}"] = {
                "capacity": star_capacity(size, tier),
                "clothescategory": "emblem",
                "attachableToEntity": {"categoryCode": "emblem", "attachedShape": {"base": f"{MOD}:entity/worn/{code}"},
                                       "texturePrefixCode": f"{MOD}-{code}"},
            }
    dump("itemtypes/star.json", {
        "code": "star",
        "class": "VintageEItemKleinStar",
        "variantgroups": [{"code": "size", "states": STAR_SIZES}, {"code": "tier", "states": STAR_TIERS}],
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/star-{size}-{tier}"},
        "attributesByType": attrs,
        "storageFlags": 129,
        "materialDensity": 3000,
    })
    dump("itemtypes/finalstarshard.json", {
        "code": "finalstarshard",
        "maxstacksize": 64,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/finalstarshard"},
        "lightHsv": [8, 4, 8],
        "materialDensity": 3000,
    })
    # Worn stars: the Klein star's model with this star's texture.
    worn = load("shapes/entity/worn/kleinstar-omega.json")
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            w = json.loads(json.dumps(worn))
            w["textures"] = {"star": f"{MOD}:item/star-{size}-{tier}"}
            dump(f"shapes/entity/worn/star-{size}-{tier}.json", w)


def ing(code, kind="item", quantity=None):
    i = {"type": kind, "code": code}
    if quantity:
        i["quantity"] = quantity
    return i


def recipes():
    rs = []
    prev = f"{MOD}:matter-red"
    for mk, name, *_ in MATTERS:
        # As red matter: 3 of the matter before it between 6 aeternalis fuel.
        rs.append({"ingredientPattern": "AAA,PPP,AAA", "ingredients": {"A": ing(f"{MOD}:alchemicalfuel-aeternalisfuel"), "P": ing(prev)},
                   "width": 3, "height": 3, "output": ing(f"{MOD}:matter-{name}"), "name": f"{MOD}:matter-{name}"})
        rs.append({"ingredientPattern": "PP,PP", "ingredients": {"P": ing(f"{MOD}:matter-{name}")}, "width": 2, "height": 2,
                   "output": ing(f"{MOD}:matterblock-{name}", "block"), "name": f"{MOD}:matterblock-{name}"})
        rs.append({"ingredientPattern": "B", "ingredients": {"B": ing(f"{MOD}:matterblock-{name}", "block")}, "width": 1, "height": 1,
                   "output": {**ing(f"{MOD}:matter-{name}"), "quantity": 4}, "name": f"{MOD}:matterblock-{name}-uncraft"})
        prev = f"{MOD}:matter-{name}"
    # The Final tier's matter. Project Expansion: 8 Colossal Star Omegas around a nether star; Vintage Story has no
    # nether star, so a stack of temporal gears.
    rs.append({"ingredientPattern": "SSS,SGS,SSS", "ingredients": {"S": ing(f"{MOD}:star-colossal-omega"), "G": ing(G + "gear-temporal", quantity=8)},
               "width": 3, "height": 3, "output": ing(f"{MOD}:finalstarshard"), "name": f"{MOD}:finalstarshard"})
    for mk, *_ in TIERS:
        m = matter_of(mk)
        # As MK2 and MK3: the collector before it in glass, with one matter; the relay before it with 4 matter and 4 obsidian.
        rs.append({"ingredientPattern": "GMG,GCG,GGG", "ingredients": {"G": ing(G + "glass-*", "block"), "M": ing(m), "C": ing(f"{MOD}:energycollector-mk{mk - 1}", "block")},
                   "width": 3, "height": 3, "output": ing(f"{MOD}:energycollector-mk{mk}", "block"), "name": f"{MOD}:collector-mk{mk}"})
        rs.append({"ingredientPattern": "OMO,MXM,OMO", "ingredients": {"O": ing(G + "rock-obsidian", "block"), "M": ing(m), "X": ing(f"{MOD}:antimatterrelay-mk{mk - 1}", "block")},
                   "width": 3, "height": 3, "output": ing(f"{MOD}:antimatterrelay-mk{mk}", "block"), "name": f"{MOD}:antimatterrelay-mk{mk}"})
    prev = f"{MOD}:kleinstar-omega"
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            code = f"{MOD}:star-{size}-{tier}"
            # Four of the star before it (they keep their EMC: ItemKleinStar.OnCreatedByCrafting).
            rs.append({"ingredientPattern": "SS,SS", "ingredients": {"S": ing(prev)}, "width": 2, "height": 2, "output": ing(code),
                       "name": code, "shapeless": True})
            prev = code
    dump("recipes/grid/expansion.json", rs)
    return rs


# ---------------------------------------------------------------- language


def lang_lines():
    d = json.load(open(os.path.join(ROOT, "lang", "en.json"), encoding="utf-8"))
    c3, r3 = d["blockdesc-energycollector-mk3"], d["blockdesc-antimatterrelay-mk3"]
    hc3 = d["vintagee:block-handbooktext-vintagee:energycollector-mk3"]
    hr3 = d["vintagee:block-handbooktext-vintagee:antimatterrelay-mk3"]
    hs = d["vintagee:item-handbooktext-vintagee:kleinstar-omega"]
    ds = d["itemdesc-kleinstar-omega"]
    out = {}

    def coll(text, mk):
        c = collector(mk)
        return text.replace("up to 40 EMC", "up to " + n(c["emcPerSecond"]) + " EMC").replace("stores 60,000", "stores " + n(c["capacity"]))

    def rel(text, mk):
        r = relay(mk)
        return (text.replace("10,000,000 EMC", n(r["capacity"]) + " EMC").replace("adds 10 EMC", "adds " + n(r["bonusPerSecond"]) + " EMC")
                .replace("up to 640 EMC", "up to " + n(r["transferPerSecond"]) + " EMC"))

    prev = "red matter"
    for mk, name, *_ in TIERS:
        title = name.capitalize()
        out[f"block-energycollector-mk{mk}"] = f"Energy Collector MK{mk} ({title})"
        out[f"blockdesc-energycollector-mk{mk}"] = coll(c3, mk)
        out[f"vintagee:block-handbooktitle-vintagee:energycollector-mk{mk}"] = "How it works"
        out[f"vintagee:block-handbooktext-vintagee:energycollector-mk{mk}"] = coll(hc3, mk)
        out[f"block-antimatterrelay-mk{mk}"] = f"Anti-Matter Relay MK{mk} ({title})"
        out[f"blockdesc-antimatterrelay-mk{mk}"] = rel(r3, mk)
        out[f"vintagee:block-handbooktitle-vintagee:antimatterrelay-mk{mk}"] = "How it works"
        out[f"vintagee:block-handbooktext-vintagee:antimatterrelay-mk{mk}"] = rel(hr3, mk)
        if name == "final":
            continue
        made = f"3 {prev} between 6 aeternalis fuel"
        up = f"It upgrades an Energy Collector or Anti-Matter Relay MK{mk - 1} to MK{mk}"
        text = f"{title} matter, from Project Expansion: {made}. {up}; 4 make a {name} matter block."
        out[f"item-matter-{name}"] = f"{title} Matter"
        out[f"itemdesc-matter-{name}"] = text
        out[f"vintagee:item-handbooktitle-vintagee:matter-{name}"] = "How it works"
        out[f"vintagee:item-handbooktext-vintagee:matter-{name}"] = text
        out[f"block-matterblock-{name}"] = f"{title} Matter Block"
        prev = f"{name} matter"
    out["item-finalstarshard"] = "Final Star Shard"
    shard = ("The Final tier's matter: 8 Colossal Star Omegas around 8 temporal gears (Project Expansion uses a nether star). "
             "It upgrades an Energy Collector or Anti-Matter Relay MK15 (Fading) to MK16 (Final). The stars are used up with what they hold.")
    out["itemdesc-finalstarshard"] = shard
    out["vintagee:item-handbooktitle-vintagee:finalstarshard"] = "How it works"
    out["vintagee:item-handbooktext-vintagee:finalstarshard"] = shard
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            code = f"star-{size}-{tier}"
            cap = n(star_capacity(size, tier))
            out[f"item-{code}"] = f"{size.capitalize()} Star {tier.capitalize()}"
            text = hs.replace("51,200,000", cap)
            if size == "gargantuan" and tier == "omega":
                text = text.replace("Four stars craft into the next size and keep their EMC.", "The largest star there is.")
            elif size == "colossal" and tier == "omega":
                text = text.replace("keep their EMC.", "keep their EMC; eight make a Final Star Shard.")
            out[f"itemdesc-{code}"] = ds.replace("51,200,000", cap)
            out[f"vintagee:item-handbooktitle-vintagee:{code}"] = "How it works"
            out[f"vintagee:item-handbooktext-vintagee:{code}"] = text
    # The doctor's machines page (EmcDiagnoseMachines.ExpansionLines).
    out["diag-mach-expansion-on"] = ("Project Expansion's tiers are on: Energy Collectors and Anti-Matter Relays up to MK16, matter up to fading, "
                                     "the Final Star Shard and the Magnum, Colossal and Gargantuan stars (Expansion in /emc configure).")
    out["diag-mach-expansion-off"] = ("Project Expansion's tiers are off (Expansion in /emc configure): {0} of their entries were taken out at start-up, "
                                      "so none of them exist on this server.")
    out["diag-mach-expansion-failed"] = "Expansion is off, but Project Expansion's tiers could not be taken out at start-up: {0}"
    out["diag-mach-expansion-failed-why"] = ("They are still in the game. Check that Vintage-E's files are complete (config/vintagee-expansion.json in its zip) "
                                             "and restart; if it happens again, copy this and report it to Vintage-E's author.")
    out["diag-mach-expansion-restart"] = "Expansion was set to {0} after the server started; it takes effect after a restart."
    return out


def write_lang(entries):
    """Only the lines this script owns change: the file mixes escaped and plain characters, so it isn't re-serialised."""
    path = os.path.join(ROOT, "lang", "en.json")
    lines = open(path, encoding="utf-8").read().rstrip().split("\n")
    assert lines[0].strip() == "{" and lines[-1].strip() == "}"
    owned = set(entries)
    keep = []
    for line in lines[1:-1]:
        m = re.match(r'\s*"((?:[^"\\]|\\.)*)"\s*:', line)
        if m and json.loads('"' + m.group(1) + '"') in owned:
            continue
        keep.append(line.rstrip().rstrip(","))
    keep += ["  " + json.dumps(k, ensure_ascii=False) + ": " + json.dumps(v, ensure_ascii=False) for k, v in entries.items()]
    text = "{\n" + ",\n".join(keep) + "\n}\n"
    json.loads(text)
    open(path, "w", encoding="utf-8").write(text)


# ---------------------------------------------------------------- textures and looks


def textures_and_looks():
    written = []
    newmat = {}
    for mk, name, core, base, glow, hue, sat in TIERS:
        c, b, g = gt.rgba(core), gt.rgba(base), gt.rgba(glow)
        # Middle: Vintage-E's own drawing code, this tier's colour.
        written.append(gt.save(gt.collector_top(c, f"ct{mk}"), f"block/energycollector-mk{mk}-top"))
        written.append(gt.save(gt.collector_side(c, f"cs{mk}"), f"block/energycollector-mk{mk}-side"))
        written.append(gt.save(gt.relay_face(c, True, f"rt{mk}"), f"block/antimatterrelay-mk{mk}-top"))
        written.append(gt.save(gt.relay_face(c, False, f"rs{mk}"), f"block/antimatterrelay-mk{mk}-side"))
        # Original: Project Expansion's (one texture on every face, as there).
        written.append(save_png(vendor(f"collector-{name}"), f"projecte/expansion/collector-{name}"))
        written.append(save_png(vendor(f"relay-{name}"), f"projecte/expansion/relay-{name}"))
        # New: a matter material in this tier's colours, drawn as gen_newlook.py draws dark and red matter.
        stops = new_matter_stops(name)
        newmat[name] = gn.save(gn.matter_texture(100 + mk * 2, *stops), f"material/{name}matter")
        if name == "final":
            continue
        written.append(gt.save(gt.sphere(b, g, f"m{name}"), f"item/{name}matter"))
        written.append(gt.save(gt.matter_block(b, g, f"mb{name}"), f"block/matterblock-{name}"))
        written.append(save_png(vendor(f"matter-{name}"), f"projecte/expansion/matter-{name}"))
        written.append(save_png(vendor(f"matterblock-{name}"), f"projecte/expansion/matterblock-{name}"))
        gn.save(gn.bevel(gn.matter_texture(101 + mk * 2, *stops)), f"block/matterblock-{name}")
    # Stars and the shard: Project Expansion's art in every look, as the Klein stars use ProjectE's.
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            written.append(save_png(vendor(f"star-{size}-{tier}"), f"item/star-{size}-{tier}"))
    written.append(save_png(vendor("finalstarshard"), "item/finalstarshard"))

    # The matter textures need the matter items' own texture names: the item type names them item/{type}matter.
    d = load("itemtypes/matter.json")
    assert d["texture"]["base"] == "item/{type}matter"

    # Original look: Project Expansion's textures for the new tiers.
    orig_tex, orig_ov = {}, []
    for mk, name, *_ in TIERS:
        pc, pr = f"{MOD}:projecte/expansion/collector-{name}", f"{MOD}:projecte/expansion/relay-{name}"
        orig_tex[f"{MOD}:block/energycollector-mk{mk}-top"] = pc
        orig_tex[f"{MOD}:block/energycollector-mk{mk}-side"] = pc
        orig_tex[f"{MOD}:block/antimatterrelay-mk{mk}-top"] = pr
        orig_tex[f"{MOD}:block/antimatterrelay-mk{mk}-side"] = pr
        orig_ov.append({"code": f"{MOD}:energycollector-mk{mk}", "textures": {"down": pc}})
        orig_ov.append({"code": f"{MOD}:antimatterrelay-mk{mk}", "textures": {"down": pr}})
        if name != "final":
            orig_tex[f"{MOD}:item/{name}matter"] = f"{MOD}:projecte/expansion/matter-{name}"
            orig_tex[f"{MOD}:block/matterblock-{name}"] = f"{MOD}:projecte/expansion/matterblock-{name}"
    dump("config/vintagee-projecte-look-expansion.json", {
        "//": "Written by tools/gen_expansion.py. Read with vintagee-projecte-look.json (the Original look): Project Expansion's textures "
              "(MIT, see THIRD-PARTY-NOTICES.md) for its tiers. Stars use the same art in every look, so they aren't listed.",
        "textures": orig_tex, "overrides": orig_ov})

    # New look: the same models as MK1-3, with the game's metals and this tier's matter.
    new_tex, new_ov = {}, []
    coll_shape, relay_shape = f"{MOD}:newlook/block/energycollector", f"{MOD}:newlook/block/antimatterrelay"
    orb, star = f"{MOD}:newlook/item/orb", f"{MOD}:newlook/item/star"
    plate = lambda m: G + "block/metal/plate/" + m
    gem = lambda m: G + "block/stone/gem/" + m
    for mk, name, *_ in TIERS:
        mat = newmat[name]
        shine = gem(TIER_GEM[name]) if name in TIER_GEM else mat
        new_ov.append(gn.block(f"energycollector-mk{mk}", coll_shape, {"wood": G + "block/wood/planks/ebony1", "frame": G + "block/wood/debarked/ebony",
                                                                     "metal": plate(TIER_METAL[name]), "glow": shine, "glass": shine}, f"collector mk{mk}"))
        new_ov.append(gn.block(f"antimatterrelay-mk{mk}", relay_shape, {"stone": G + "block/stone/polishedrock/granite", "matter": mat,
                                                                      "metal": plate(TIER_METAL[name]), "glow": shine}, f"relay mk{mk}"))
        if name == "final":
            continue
        new_tex[f"{MOD}:block/matterblock-{name}"] = f"{MOD}:newlook/block/matterblock-{name}"
        new_ov.append(gn.item(f"matter-{name}", orb, {"matter": mat}, f"{name} matter"))
    ringed = star_shapes()
    for size in STAR_SIZES:
        for tier in STAR_TIERS:
            new_ov.append(gn.item(f"star-{size}-{tier}", ringed[size], {"metal": plate(STAR_METAL[size]), "gem": gem(STAR_GEM[tier])}, f"{size} star {tier}"))
    new_ov.append(gn.item("finalstarshard", star, {"metal": plate("gold"), "gem": gem("diamond")}, "final star shard"))
    dump("config/vintagee-new-look-expansion.json", {
        "//": "Written by tools/gen_expansion.py. Read with vintagee-new-look.json (the New look): the Project Expansion tiers' models, "
              "the same as MK1-3's, with the game's metals and gems and each tier's matter in Vintage Story's muted colours.",
        "textures": new_tex, "overrides": new_ov})
    return written


def star_shapes():
    """The New look's bigger stars: the Klein star's model (newlook/item/star) with metal rings around it."""
    base = json.load(open(os.path.join(gn.SHAPES, "item", "star.json")))
    lo, hi, t = 3.4, 12.6, 0.7  # half-size 4.6 around the core, between it and the spike tips
    cx, cy, cz = 8.0, 7.0, 8.0

    def square(name, axis):
        # Four bars of a square ring around the star's centre, in the plane across `axis`.
        a0, a1 = (cy - 4.6, cy + 4.6)
        thin = (lambda c: (c - t / 2, c + t / 2))
        bars = []
        if axis == "y":  # lying flat
            y0, y1 = thin(cy)
            bars = [((lo, y0, lo), (hi, y1, lo + t)), ((lo, y0, hi - t), (hi, y1, hi)),
                    ((lo, y0, lo + t), (lo + t, y1, hi - t)), ((hi - t, y0, lo + t), (hi, y1, hi - t))]
        elif axis == "z":  # upright, facing north
            z0, z1 = thin(cz)
            bars = [((lo, a0, z0), (hi, a0 + t, z1)), ((lo, a1 - t, z0), (hi, a1, z1)),
                    ((lo, a0 + t, z0), (lo + t, a1 - t, z1)), ((hi - t, a0 + t, z0), (hi, a1 - t, z1))]
        else:  # upright, facing east
            x0, x1 = thin(cx)
            bars = [((x0, a0, lo), (x1, a0 + t, hi)), ((x0, a1 - t, lo), (x1, a1, hi)),
                    ((x0, a0 + t, lo), (x1, a1 - t, lo + t)), ((x0, a0 + t, hi - t), (x1, a1 - t, hi))]
        return [gn.box(f"{name}-{i}", f, to, "metal") for i, (f, to) in enumerate(bars)]

    out = {}
    for size, rings in STAR_RINGS.items():
        els = json.loads(json.dumps(base["elements"]))
        for axis in ("y", "z", "x")[:rings]:
            els += square("ring-" + axis, axis)
        out[size] = gn.save_shape(f"item/star-{size}", gn.shape(dict(base["textures"]), els))
    return out


def manifest():
    dump("config/vintagee-expansion.json", {
        "//": "Written by tools/gen_expansion.py. What the server setting Expansion takes out when it's off (ExpansionTiers.cs): "
              "these states of these block and item types, these whole files, and these recipe files.",
        "states": {
            "blocktypes/energycollector.json": {"group": "tier", "states": [f"mk{t[0]}" for t in TIERS]},
            "blocktypes/antimatterrelay.json": {"group": "tier", "states": [f"mk{t[0]}" for t in TIERS]},
            "blocktypes/matterblock.json": {"group": "type", "states": [t[1] for t in MATTERS]},
            "itemtypes/matter.json": {"group": "type", "states": [t[1] for t in MATTERS]},
        },
        "files": ["itemtypes/star.json", "itemtypes/finalstarshard.json"],
        "recipes": ["recipes/grid/expansion.json"],
        "tiers": [{"mk": t[0], "matter": t[1]} for t in TIERS],
    })


def main():
    blocks_and_items()
    rs = recipes()
    entries = lang_lines()
    write_lang(entries)
    tex = textures_and_looks()
    manifest()
    print(f"{len(TIERS)} tiers, {len(MATTERS)} matters, {len(STAR_SIZES) * len(STAR_TIERS)} stars, {len(rs)} recipes, "
          f"{len(entries)} language lines, {len(tex)} textures (plus the New look's matter)")


if __name__ == "__main__":
    main()
