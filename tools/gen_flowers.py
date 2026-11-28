#!/usr/bin/env python3
"""Project Expansion's owned machines for Vintage-E (1.29.2): Power Flowers and EMC Links MK1 to MK16, the Compact Sun,
the Matter Upgrader and the Final Star. Runs after tools/gen_expansion.py (it uses its tier tables):

    python3 tools/gen_flowers.py

Writes (and rewrites):
  - blocktypes/powerflower.json, emclink.json, compactsun.json; itemtypes/finalstar.json, matterupgrader.json;
  - shapes/block/powerflower.json (Project Expansion's model, MIT) and the New look's version of it;
  - recipes/grid/flowers.json (MK1-3 and the upgrader: always there) and recipes/grid/expansion-flowers.json (MK4-16,
    the Final Star and the Compact Sun: taken out with the Expansion setting off; see gen_expansion.manifest);
  - their language lines (only the lines this script owns);
  - textures for the three looks and their entries in the companion look maps (…-look-expansion.json, which
    gen_expansion.py writes: this script adds to them, so run that first).
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
# Vintage-E's MK1-3 under Project Expansion's names for them, then the Expansion tiers.
BASE = [(1, "basic", "ffd24a", 12, 4), (2, "dark", "9b5cff", 49, 5), (3, "red", "ff4a3c", 0, 6)]
ALL = BASE + [(mk, name, core, hue, sat) for mk, name, core, base, glow, hue, sat in gx.TIERS]
TITLE = {name: name.capitalize() for _, name, *_ in ALL}
# The New look's metal and glow for MK1-3 (as the collectors': brass and citrine, silver and amethyst, gold and ruby).
BASE_METAL = {"basic": "brass", "dark": "silver", "red": "gold"}
BASE_GEM = {"basic": "citrine", "dark": "amethyst", "red": "ruby"}


def collector_rate(mk):
    return gx.steps(mk, 4)


def relay_bonus(mk):
    return gx.steps(mk, 1)


def flower_rate(mk):
    """Project Expansion: as much as 18 collectors and 30 relay bonuses of its tier."""
    return 18 * collector_rate(mk) + 30 * relay_bonus(mk)


def link_limits(mk):
    """Project Expansion's limits a second: EMC 16^MK, items 2^(MK-1), liquid 2^(MK-1) buckets (10 L), 3 slots a tier."""
    if mk >= 16:
        return {"unlimited": True, "inputSlots": 48}
    return {"emcPerSecond": 16 ** mk, "itemsPerSecond": 2 ** (mk - 1), "litresPerSecond": 10 * 2 ** (mk - 1), "inputSlots": 3 * mk}


def matter_for(mk):
    """What goes into the link of this tier as Project Expansion's T: dark matter for MK2, red for MK3, then the tier's matter."""
    return {2: f"{MOD}:matter-dark", 3: f"{MOD}:matter-red"}.get(mk) or gx.matter_of(mk)


ing = gx.ing
mks = [f"mk{t[0]}" for t in ALL]


# ---------------------------------------------------------------- block and item types


def types():
    help_link = {"interactionHelp": "vintagee:blockhelp-link", "sneakInteractionHelp": "vintagee:blockhelp-link-sneak"}
    gx.dump("blocktypes/powerflower.json", {
        "code": "powerflower",
        "class": "VintageEBlockOwnedMachine",
        "entityClass": "VintageEPowerFlower",
        "variantgroups": [{"code": "tier", "states": mks}],
        "creativeinventory": {"general": ["*"], "mechanics": ["*"], "vintagee": ["*"]},
        "blockmaterial": "Metal",
        "drawtype": "json",
        "shape": {"base": "block/powerflower"},
        "textures": {"collector": {"base": "block/powerflower-{tier}-petal"}, "relay": {"base": "block/antimatterrelay-{tier}-side"}},
        "sideopaque": {"all": False},
        "sidesolid": {"all": False, "down": True},
        "emitsideao": {"all": False},
        "lightAbsorption": 0,
        "attributesByType": {f"*-mk{mk}": {"emcPerSecond": flower_rate(mk)} for mk, *_ in ALL},
        "lightHsvByType": {f"*-mk{mk}": [hue, sat, min(14, 8 + mk // 3)] for mk, name, core, hue, sat in ALL},
        "resistance": 4,
        "requiredMiningTier": 2,
        "sounds": {"place": "game:block/ingot", "break": "game:block/ingot", "hit": "game:block/ingot"},
    })
    gx.dump("blocktypes/emclink.json", {
        "code": "emclink",
        "class": "VintageEBlockOwnedMachine",
        "entityClass": "VintageEEmcLink",
        "variantgroups": [{"code": "tier", "states": mks}],
        "creativeinventory": {"general": ["*"], "mechanics": ["*"], "vintagee": ["*"]},
        "blockmaterial": "Metal",
        "drawtype": "cube",
        "textures": {"up": {"base": "block/emclink-{tier}-top"}, "down": {"base": "block/emclink-{tier}-top"},
                     **{f: {"base": "block/emclink-{tier}-side"} for f in ("north", "east", "south", "west")}},
        "attributesByType": {f"*-mk{mk}": {**link_limits(mk), **help_link} for mk, *_ in ALL},
        "lightHsvByType": {f"*-mk{mk}": [hue, sat, min(10, 4 + mk // 3)] for mk, name, core, hue, sat in ALL},
        "resistance": 5,
        "requiredMiningTier": 2,
        "sounds": {"place": "game:block/ingot", "break": "game:block/ingot", "hit": "game:block/ingot"},
    })
    gx.dump("blocktypes/compactsun.json", {
        "code": "compactsun",
        "class": "VintageEBlockCompactSun",
        "creativeinventory": {"general": ["*"], "mechanics": ["*"], "vintagee": ["*"]},
        "blockmaterial": "Metal",
        "drawtype": "cube",
        "textures": {"all": {"base": "block/compactsun"}},
        "lightHsv": [8, 3, 18],
        "behaviors": [{"name": "HeatSource", "properties": {"heatStrength": 10}}],
        "resistance": 6,
        "requiredMiningTier": 3,
        "sounds": {"place": "game:block/ingot", "break": "game:block/ingot", "hit": "game:block/ingot"},
    })
    gx.dump("itemtypes/finalstar.json", {
        "code": "finalstar",
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/finalstar"},
        "lightHsv": [8, 4, 12],
        "materialDensity": 3000,
    })
    gx.dump("itemtypes/matterupgrader.json", {
        "code": "matterupgrader",
        "class": "VintageEItemMatterUpgrader",
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/matterupgrader"},
        "materialDensity": 2000,
    })


def flower_shape():
    """Project Expansion's power flower (src/generated/.../models/block/power_flower/base.json, MIT): a relay plate with a
    flower of collector cubes. Minecraft's model elements and UVs are the game's own format."""
    src = json.load(open(os.path.join(gx.VENDOR, "powerflower-model.json")))
    els = []
    for i, e in enumerate(src["elements"]):
        faces = {f: {"texture": v["texture"], "uv": v["uv"]} for f, v in e["faces"].items()}
        if any(v["texture"] == "#collector" for v in faces.values()):
            for v in faces.values():
                v["glow"] = 40
        els.append({"name": f"part{i}", "from": e["from"], "to": e["to"], "faces": faces})
    data = {"textureWidth": 16, "textureHeight": 16,
            "textures": {"collector": f"{MOD}:block/powerflower-mk1-petal", "relay": f"{MOD}:block/antimatterrelay-mk1-side"},
            "elements": els}
    path = gx.dump("shapes/block/powerflower.json", data)
    return path, els


# ---------------------------------------------------------------- recipes


def recipes():
    base, more = [], []
    low, med, high = (ing(f"{MOD}:covalencedust-{t}") for t in ("low", "medium", "high"))
    for mk, name, *_ in ALL:
        out = base if mk <= 3 else more
        link = f"{MOD}:emclink-mk{mk}"
        # The EMC Link: Project Expansion's LMH,TCT,HML. MK1 a transmutation tablet and a condenser; then the link before
        # it with the tier's matter (dark, red, then Project Expansion's).
        t = ing(f"{MOD}:transmutationtablet") if mk == 1 else ing(matter_for(mk))
        c = ing(f"{MOD}:energycondenser-mk1", "block") if mk == 1 else ing(f"{MOD}:emclink-mk{mk - 1}", "block")
        out.append({"ingredientPattern": "LMH,TCT,HML", "ingredients": {"L": low, "M": med, "H": high, "T": t, "C": c},
                    "width": 3, "height": 3, "output": ing(link, "block"), "name": link})
        # The Power Flower: CLC,RRR,RRR, C a compressed collector (9 collectors: Vintage-E has no compressed ones), L the
        # link, R relays of the tier; or CLC,RPR,RRR with the flower before it.
        flower = f"{MOD}:powerflower-mk{mk}"
        cc = ing(f"{MOD}:energycollector-mk{mk}", "block", 9)
        r = ing(f"{MOD}:antimatterrelay-mk{mk}", "block")
        out.append({"ingredientPattern": "CLC,RRR,RRR", "ingredients": {"C": cc, "L": ing(link, "block"), "R": r},
                    "width": 3, "height": 3, "output": ing(flower, "block"), "name": flower})
        if mk > 1:
            out.append({"ingredientPattern": "CLC,RPR,RRR", "ingredients": {"C": cc, "L": ing(link, "block"), "R": r,
                                                                            "P": ing(f"{MOD}:powerflower-mk{mk - 1}", "block")},
                        "width": 3, "height": 3, "output": ing(flower, "block"), "name": flower + "-upgrade"})
    # The Matter Upgrader: LCL,PMR,LDL with MK1 links, collector, flower and relay, dark matter, and (for Minecraft's lime
    # dye) medium covalence dust.
    base.append({"ingredientPattern": "LCL,PMR,LDL", "ingredients": {
        "L": ing(f"{MOD}:emclink-mk1", "block"), "C": ing(f"{MOD}:energycollector-mk1", "block"), "P": ing(f"{MOD}:powerflower-mk1", "block"),
        "M": med, "R": ing(f"{MOD}:antimatterrelay-mk1", "block"), "D": ing(f"{MOD}:matter-dark")},
        "width": 3, "height": 3, "output": ing(f"{MOD}:matterupgrader"), "name": f"{MOD}:matterupgrader"})
    # The Final Star: 8 Final Power Flowers around (for Minecraft's dragon egg) 16 temporal gears.
    more.append({"ingredientPattern": "SSS,SES,SSS", "ingredients": {"S": ing(f"{MOD}:powerflower-mk16", "block"), "E": ing(G + "gear-temporal", quantity=16)},
                 "width": 3, "height": 3, "output": ing(f"{MOD}:finalstar"), "name": f"{MOD}:finalstar"})
    # The Compact Sun: Final Stars and shards around yellow matter, either way round.
    for i, pattern in enumerate(("HSH,SYS,HSH", "SHS,HYH,SHS")):
        more.append({"ingredientPattern": pattern, "ingredients": {"S": ing(f"{MOD}:finalstar"), "H": ing(f"{MOD}:finalstarshard"), "Y": ing(f"{MOD}:matter-yellow")},
                     "width": 3, "height": 3, "output": ing(f"{MOD}:compactsun", "block"), "name": f"{MOD}:compactsun-{i + 1}"})
    gx.dump("recipes/grid/flowers.json", base)
    gx.dump("recipes/grid/expansion-flowers.json", more)
    return base + more


# ---------------------------------------------------------------- language


def n(v):
    return gx.n(v)


def lang_lines():
    out = {}
    sun_how = "A Compact Sun right under it makes it make ten times as much."
    for mk, name, *_ in ALL:
        title = TITLE[name]
        rate = n(flower_rate(mk))
        out[f"block-powerflower-mk{mk}"] = f"Power Flower MK{mk} ({title})"
        flower = (f"Makes {rate} EMC a second, day and night, with no sunlight needed (as much as 18 Energy Collectors and 30 relay bonuses MK{mk}), "
                  "and pays it straight into the personal EMC of whoever placed it. While they're away it keeps what it makes and pays it all when "
                  f"they're back; broken, it takes what it's keeping with it. {sun_how} A Matter Upgrader turns it into the next tier where it stands.")
        out[f"blockdesc-powerflower-mk{mk}"] = flower
        out[f"vintagee:block-handbooktitle-vintagee:powerflower-mk{mk}"] = "How it works"
        out[f"vintagee:block-handbooktext-vintagee:powerflower-mk{mk}"] = flower
        lim = link_limits(mk)
        limits = ("It has no limits." if lim.get("unlimited") else
                  f"Each second it takes in up to {n(lim['emcPerSecond'])} EMC, makes and burns up to {n(lim['itemsPerSecond'])} items, "
                  f"makes up to {n(lim['litresPerSecond'])} litres, and holds what's fed to it in {lim['inputSlots']} slots.")
        out[f"block-emclink-mk{mk}"] = f"EMC Link MK{mk} ({title})"
        link = ("Ties its owner's personal EMC (whoever placed it) to automation. Right-click it with something you've learned to set what it makes "
                "(with a bucket or barrel's liquid, that liquid); sneak + right-click with an empty hand to clear it. It then makes that from your EMC: "
                "into the chutes and hoppers under or beside it that take a condenser's copies, into your hand when you right-click it, or for a liquid into the barrels next to it. "
                "Items a hopper or chute puts into it are burned into your EMC and learned, and EMC that collectors and relays push into it goes to you. "
                f"{limits} Only works while you're in the game.")
        out[f"blockdesc-emclink-mk{mk}"] = link
        out[f"vintagee:block-handbooktitle-vintagee:emclink-mk{mk}"] = "How it works"
        out[f"vintagee:block-handbooktext-vintagee:emclink-mk{mk}"] = link
    sun = ("A star you can hold: placed under a Power Flower it makes ten times as much, and on top of an Energy Collector the collector "
           "works as in full sun all day and night, ten times over. It warms like a fire and sets alight whatever stands on it. "
           "Made from Final Stars and Final Star Shards around yellow matter.")
    out["block-compactsun"] = "Compact Sun"
    out["blockdesc-compactsun"] = sun
    out["vintagee:block-handbooktitle-vintagee:compactsun"] = "How it works"
    out["vintagee:block-handbooktext-vintagee:compactsun"] = sun
    star = ("Eight Final Power Flowers around 16 temporal gears (Project Expansion uses a dragon egg). It holds no EMC: "
            "it's for showing off, and for making Compact Suns.")
    out["item-finalstar"] = "Final Star"
    out["itemdesc-finalstar"] = star
    out["vintagee:item-handbooktitle-vintagee:finalstar"] = "How it works"
    out["vintagee:item-handbooktext-vintagee:finalstar"] = star
    up = ("Right-click an Energy Collector, Anti-Matter Relay, Power Flower or EMC Link with it to turn it into the next tier where it stands, "
          "keeping what's in it. You must have learned the next tier, and you pay the difference in their EMC values. Only a flower's "
          "or link's owner can upgrade it. Not used up.")
    out["item-matterupgrader"] = "Matter Upgrader"
    out["itemdesc-matterupgrader"] = up
    out["vintagee:item-handbooktitle-vintagee:matterupgrader"] = "How it works"
    out["vintagee:item-handbooktext-vintagee:matterupgrader"] = up

    out["blockhelp-link"] = "Take what it makes, or set it"
    out["blockhelp-link-sneak"] = "Change what it makes (empty hand: clear it)"
    # Block info (BEPowerFlower, BEEmcLink, BlockOwnedMachine).
    out["owned-noowner"] = "No owner: right-click it to make it yours"
    out["owned-owner-you"] = "Yours"
    out["owned-owner"] = "Owner: {0}"
    out["owned-held"] = "Holding {0} EMC for its owner (paid to whoever places it)"
    out["flower-rate"] = "Makes {0} EMC a second"
    out["flower-rate-sun"] = "Makes {0} EMC a second (×{1}: a Compact Sun under it)"
    out["flower-waiting"] = "{0} EMC waiting for its owner to come back"
    out["link-info-notset"] = "Makes nothing yet: right-click with something you've learned"
    out["link-info-target"] = "Makes: {0}"
    out["link-info-unlimited"] = "No limits"
    out["link-info-limits"] = "A second: {0} EMC in, {1} items, {2} L; {3} input slots"
    out["link-info-buffer"] = "{0} EMC waiting for its owner"
    out["link-info-waiting"] = "{0} items waiting to be burned"
    out["link-info-output"] = "Ready to take: {0} × {1}"
    # Chat (BEEmcLink.OnInteract).
    out["link-notowner"] = "This EMC Link belongs to {0}."
    out["link-cleared"] = "This EMC Link no longer makes anything."
    out["link-nothing"] = "That's empty: there's nothing in it to make."
    out["link-novalue"] = "{0} can't be made from EMC (no value, or not transmutable on this server)."
    out["link-notlearned"] = "You haven't learned {0}: burn one first."
    out["link-set"] = "This EMC Link now makes {0}."
    out["link-notset"] = "Right-click with something you've learned to set what this EMC Link makes."
    out["link-cant"] = "Can't make {0} now: not enough EMC, or the link's limit for this second is used up."
    out["link-holdsame"] = "Empty your hand (or hold {0}) to take what it makes; sneak + right-click to change it."
    # The Matter Upgrader.
    out["upgrader-last"] = "{0} is as far as it goes."
    out["upgrader-noaccess"] = "You can't change blocks here."
    out["upgrader-notowner"] = "This belongs to {0}: only they can upgrade it."
    out["upgrader-notlearned"] = "Learn {0} first (burn one, or learn it at a table)."
    out["upgrader-cantpay"] = "Upgrading to {1} costs {0} EMC, and you don't have that much."
    out["upgrader-done"] = "Upgraded to {0} for {1} EMC."
    # /emc setowner.
    out["cmdhelp-setowner"] = "Give the Power Flower or EMC Link you're looking at to an online player."
    out["cmd-setowner-look"] = "Look at a Power Flower or EMC Link first."
    out["cmd-setowner-notowned"] = "That isn't a Power Flower or EMC Link."
    out["cmd-setowner-done"] = "{0} now belongs to {1} (it was {2}'s)."
    # The doctor's machines page.
    out["diag-mach-collector-sun"] = "A Compact Sun on top: full sun all the time, ×{0}."
    out["diag-mach-flower"] = "{0}: owner {1} (in the game: {2}), makes {3} EMC a second (×{4} sun), {5} EMC waiting."
    out["diag-mach-owned-noowner"] = "It has no owner, so it does nothing."
    out["diag-mach-owned-noowner-fix"] = "Right-click it to make it yours, or an admin can give it to someone with /emc setowner <player> while looking at it."
    out["diag-mach-flower-offline-why"] = "Its owner isn't in the game: it keeps what it makes and pays it all when they're back."
    out["diag-mach-flower-sun-how"] = "A Compact Sun right under it would make it make ×{0}."
    out["diag-mach-link"] = "{0}: owner {1} (in the game: {2}), makes {3}; {4}; {5} EMC waiting for the owner, {6} items waiting to be burned."
    out["diag-mach-link-unlimited"] = "no limits"
    out["diag-mach-link-limits"] = "a second: {0} EMC in, {1} items, {2} L, {3} input slots"
    out["diag-mach-link-offline-why"] = "Its owner isn't in the game: the link only spends and pays their EMC while they are."
    out["diag-mach-link-gone"] = "It's set to make {0}, which isn't in the game any more."
    out["diag-mach-link-notarget-why"] = "Right-click it with something its owner has learned to set what it makes; sneak + right-click with an empty hand clears it."
    out["diag-mach-link-notlearned"] = "{0} hasn't learned {1}, so the link can't make it."
    out["diag-mach-link-notlearned-why"] = "They can learn it by burning one at a table, or by putting one into the link."
    out["diag-mach-link-poor"] = "{0} has less EMC than one {1} costs ({2})."
    out["diag-mach-link-notaker"] = "Nothing takes what it makes: put a chute under it, or a chute or hopper that leads to a chest beside or under it (as for a condenser: /emc diagnose chutes), or take it by right-clicking."
    return out


# ---------------------------------------------------------------- textures and looks


def link_face(accent, top, seed):
    """The Middle look's EMC Link: a dark plate like the condenser's, with two links of a chain on its sides and a core on top."""
    img = gt.noise_fill(gt.rgba("3a3f4a"), 0.06, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((1, 1, 30, 30), outline=gt.shade(accent, 0.7))
    if top:
        d.ellipse((6, 6, 25, 25), fill=gt.shade(accent, 0.35), outline=accent)
        d.ellipse((11, 11, 20, 20), fill=accent)
        d.ellipse((14, 14, 17, 17), fill=gt.mix(accent, (255, 255, 255, 255), 0.6))
    else:
        d.rectangle((5, 10, 18, 21), outline=accent, width=2)
        d.rectangle((13, 10, 26, 21), outline=gt.mix(accent, (255, 255, 255, 255), 0.35), width=2)
    return gt.bevel(img, gt.rgba("5a6070"), gt.rgba("15181e"))


def petal(core, seed):
    """The Middle look's flower cubes: the tier's colour, lit from inside, as a collector's core is drawn."""
    img = gt.noise_fill(gt.shade(core, 0.55), 0.06, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((4, 4, 27, 27), fill=gt.shade(core, 0.8))
    d.rectangle((9, 9, 22, 22), fill=core)
    d.rectangle((13, 13, 18, 18), fill=gt.mix(core, (255, 255, 255, 255), 0.6))
    return gt.bevel(img, gt.mix(core, (255, 255, 255, 255), 0.45), gt.shade(core, 0.3))


def sun_face(seed):
    img = gt.noise_fill(gt.rgba("e8a020"), 0.08, seed)
    d = ImageDraw.Draw(img)
    for r, col in ((14, "ffc838"), (10, "ffe070"), (6, "fff6c0"), (3, "ffffff")):
        d.ellipse((16 - r, 16 - r, 15 + r, 15 + r), fill=gt.rgba(col))
    return gt.bevel(img, gt.rgba("fff0a0"), gt.rgba("a05a10"))


def textures_and_looks():
    written = []
    for mk, name, core, *_ in ALL:
        c = gt.rgba(core)
        written.append(gt.save(petal(c, f"pf{mk}"), f"block/powerflower-mk{mk}-petal"))
        written.append(gt.save(link_face(c, True, f"lt{mk}"), f"block/emclink-mk{mk}-top"))
        written.append(gt.save(link_face(c, False, f"ls{mk}"), f"block/emclink-mk{mk}-side"))
        written.append(gx.save_png(gx.vendor(f"emclink-{name}"), f"projecte/expansion/emclink-{name}"))
        if mk <= 3:
            written.append(gx.save_png(gx.vendor(f"collector-{name}"), f"projecte/expansion/collector-{name}"))
            written.append(gx.save_png(gx.vendor(f"relay-{name}"), f"projecte/expansion/relay-{name}"))
    written.append(gt.save(sun_face("sun"), "block/compactsun"))
    written.append(gt.save(gt.rod(gt.rgba("3a2a4a"), gt.rgba("9aff30"), "upgrader"), "item/matterupgrader"))
    written.append(gx.save_png(gx.vendor("sun"), "projecte/expansion/sun"))
    written.append(gx.save_png(gx.vendor("matterupgrader"), "projecte/expansion/matterupgrader"))
    # The Final Star: Project Expansion's art in every look but the New, as the other stars.
    written.append(gx.save_png(gx.vendor("finalstar"), "item/finalstar"))

    # Original look: Project Expansion's textures.
    path = os.path.join(gx.ROOT, "config", "vintagee-projecte-look-expansion.json")
    orig = json.load(open(path))
    orig["overrides"] = [o for o in orig["overrides"] if not o["code"].startswith((f"{MOD}:powerflower", f"{MOD}:emclink"))]
    for mk, name, *_ in ALL:
        pl = f"{MOD}:projecte/expansion/emclink-{name}"
        orig["textures"][f"{MOD}:block/emclink-mk{mk}-top"] = pl
        orig["textures"][f"{MOD}:block/emclink-mk{mk}-side"] = pl
        orig["overrides"].append({"code": f"{MOD}:powerflower-mk{mk}", "textures": {
            "collector": f"{MOD}:projecte/expansion/collector-{name}", "relay": f"{MOD}:projecte/expansion/relay-{name}"}})
    orig["textures"][f"{MOD}:block/compactsun"] = f"{MOD}:projecte/expansion/sun"
    orig["textures"][f"{MOD}:item/matterupgrader"] = f"{MOD}:projecte/expansion/matterupgrader"
    gx.dump("config/vintagee-projecte-look-expansion.json", orig)

    # New look: models with the game's metals, gems and the tier's matter.
    plate = lambda m: G + "block/metal/plate/" + m
    gem = lambda m: G + "block/stone/gem/" + m
    newmat = {name: f"{MOD}:newlook/material/{name}matter" for _, name, *_ in gx.TIERS}
    newmat.update({"basic": f"{MOD}:newlook/material/darkmatter", "dark": f"{MOD}:newlook/material/darkmatter", "red": f"{MOD}:newlook/material/redmatter"})

    def metal_of(name):
        return BASE_METAL.get(name) or gx.TIER_METAL[name]

    def shine_of(name):
        g = BASE_GEM.get(name) or gx.TIER_GEM.get(name)
        return gem(g) if g else newmat[name]

    # The flower: Project Expansion's model, its plate in the tier's metal and its flower in the tier's gem.
    _, els = flower_shape()
    nl = json.loads(json.dumps(els))
    for e in nl:
        for v in e["faces"].values():
            v["texture"] = "#glow" if v["texture"] == "#collector" else "#metal"
    flower = gn.save_shape("block/powerflower", gn.shape({"metal": plate("brass"), "glow": gem("citrine")}, nl))
    # The link: the condenser's model in white marble.
    condenser = f"{MOD}:newlook/block/energycondenser"
    # The sun: a gold cage round a glowing core.
    cage = [gn.box("core", (2.5, 2.5, 2.5), (13.5, 13.5, 13.5), "core", glow=255)]
    for ax in range(3):
        for a in (0, 14.5):
            for b in (0, 14.5):
                f, t = [0, 0, 0], [16, 16, 16]
                o1, o2 = [i for i in range(3) if i != ax]
                f[o1], t[o1], f[o2], t[o2] = a, a + 1.5, b, b + 1.5
                cage.append(gn.box(f"bar{ax}-{a}-{b}", tuple(f), tuple(t), "metal"))
    sun = gn.save_shape("block/compactsun", gn.shape({"metal": plate("gold"), "core": newmat["final"]}, cage))

    path = os.path.join(gx.ROOT, "config", "vintagee-new-look-expansion.json")
    new = json.load(open(path))
    mine = (f"{MOD}:powerflower", f"{MOD}:emclink", f"{MOD}:compactsun", f"{MOD}:finalstar", f"{MOD}:matterupgrader")
    new["overrides"] = [o for o in new["overrides"] if not o["code"].startswith(mine)]
    for mk, name, *_ in ALL:
        new["overrides"].append(gn.block(f"powerflower-mk{mk}", flower, {"metal": plate(metal_of(name)), "glow": shine_of(name)}, f"power flower mk{mk}",
                                         cube_before=False))
        new["overrides"].append(gn.block(f"emclink-mk{mk}", condenser, {"stone": G + "block/stone/polishedrock/whitemarble", "metal": plate(metal_of(name)),
                                                                        "matter": newmat[name], "glow": shine_of(name)}, f"emc link mk{mk}"))
    new["overrides"].append(gn.block("compactsun", sun, {}, "compact sun"))
    new["overrides"].append(gn.item("finalstar", f"{MOD}:newlook/item/star-gargantuan", {"metal": plate("gold"), "gem": gem("diamond")}, "final star"))
    new["overrides"].append(gn.item("matterupgrader", f"{MOD}:newlook/item/rod", {"wood": G + "block/wood/debarked/ebony", "metal": gem("olivine")}, "matter upgrader"))
    gx.dump("config/vintagee-new-look-expansion.json", new)
    return written


def main():
    types()
    rs = recipes()
    entries = lang_lines()
    gx.write_lang(entries)
    tex = textures_and_looks()
    print(f"{len(ALL)} tiers of flowers and links, {len(rs)} recipes, {len(entries)} language lines, {len(tex)} textures")


if __name__ == "__main__":
    main()
