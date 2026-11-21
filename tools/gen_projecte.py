#!/usr/bin/env python3
"""Writes the JSON assets for the ProjectE-style items, blocks and recipes added in 1.4.0:
covalence dust, Klein stars, the iron band, rings/stones/amulets/talisman/watch ("trinket"), gadgets,
alchemical bags, matter armour, red matter katar/morning star, fuel blocks, the alchemical chest, matter
furnaces, anti-matter relays, the interdiction torch, nova explosives and the pedestal.
Also writes their models and English names. Run from anywhere: python3 tools/gen_projecte.py
"""
import json
import os
from collections import OrderedDict as O

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee")


def write(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def xyz(x=0, y=0, z=0):
    return O([("x", x), ("y", y), ("z", z)])


def xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=None, scale=1.0, rotate=None):
    d = O()
    if rotate is not None:
        d["rotate"] = rotate
    d["translation"] = xyz(*trans)
    d["rotation"] = xyz(*rot)
    if origin is not None:
        d["origin"] = xyz(*origin)
    d["scale"] = scale
    return d


CREATIVE = O([("general", ["*"]), ("vintagee", ["*"])])

DUSTS = ["low", "medium", "high"]
STARS = ["ein", "zwei", "drei", "vier", "sphere", "omega"]
STAR_CAP = {"ein": 50000, "zwei": 200000, "drei": 800000, "vier": 3200000, "sphere": 12800000, "omega": 51200000}
TRINKETS = ["blackholeband", "swiftwolfsrendinggale", "harvestgoddessband", "ignitionring", "zeroring", "ringofarcana",
            "bodystone", "soulstone", "lifestone", "repairtalisman", "gemofeternaldensity", "evertideamulet",
            "volcaniteamulet", "watchofflowingtime", "voidring"]
RINGS = ["blackholeband", "swiftwolfsrendinggale", "harvestgoddessband", "ignitionring", "zeroring", "ringofarcana", "voidring"]
GADGETS = ["destructioncatalyst", "hyperkineticlens", "catalyticlens", "mercurialeye", "archangelssmite",
           "tomeofknowledge", "diviningrodlow", "diviningrodmedium", "diviningrodhigh"]
BAGS = ["white", "plain", "gray", "black", "brown", "red", "orange", "yellow", "green", "blue", "purple", "pink"]
# (item code, slots, top texture): the Alchemical Bag and its dark/red matter upgrades.
BAG_TIERS = [("alchemicalbag", 32, "item/bag/alchemicalbag-top"), ("alchemicalbagdark", 48, "item/tool/material/darkmatter"),
             ("alchemicalbagred", 64, "item/tool/material/redmatter")]
ARMOR_MATS = ["dark", "red", "gem"]
PARTS = ["head", "body", "legs"]
FUELS = ["alchemicalcoal", "mobiusfuel", "aeternalisfuel"]


def worn(category, shape):
    """Attributes that let an item go in a character (clothing) slot and show on the player there. No Wearable
    behaviour, so right-click keeps doing what the item does."""
    return O([
        ("clothescategory", category),
        ("attachableToEntity", O([("categoryCode", category), ("attachedShape", O([("base", f"vintagee:entity/worn/{shape}")])),
                                  ("texturePrefixCode", f"vintagee-{shape}")])),
    ])


def items():
    write("itemtypes/covalencedust.json", O([
        ("code", "covalencedust"),
        ("variantgroups", [O([("code", "tier"), ("states", DUSTS)])]),
        ("maxstacksize", 64),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/covalencedust-{tier}")])),
        ("materialDensity", 400),
    ]))
    write("itemtypes/kleinstar.json", O([
        ("code", "kleinstar"),
        ("class", "VintageEItemKleinStar"),
        ("variantgroups", [O([("code", "tier"), ("states", STARS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/kleinstar-{tier}")])),
        # Worn in the emblem slot (and still counted as carried EMC there): that needs the Outfit storage flag (128) as well as General (1).
        ("attributesByType", O((f"*-{s}", O([("capacity", STAR_CAP[s])]) | worn("emblem", f"kleinstar-{s}")) for s in STARS)),
        ("storageFlags", 129),
        ("materialDensity", 3000),
    ]))
    write("itemtypes/ironband.json", O([
        ("code", "ironband"),
        ("maxstacksize", 16),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/ironband")])),
        ("materialDensity", 7800),
    ]))
    write("itemtypes/trinket.json", O([
        ("code", "trinket"),
        ("class", "VintageEItemEmcTrinket"),
        ("variantgroups", [O([("code", "type"), ("states", TRINKETS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("textureByType", O([("*", O([("base", "item/trinket/{type}")]))])),
        # Rings can be worn in the hand slot (with gloves and other mods' rings), where they keep working.
        ("attributesByType", O([(f"*-{r}", (O([("maxInputValue", 8)]) if r == "voidring" else O()) | worn("hand", f"ring-{r}")) for r in RINGS]
                               + [("*-gemofeternaldensity", O([("maxInputValue", 8)]))])),
        # The Black Hole Band and Void Ring can target water/lava sources (right-click removes them).
        ("liquidSelectableByType", O([("*-blackholeband", True), ("*-voidring", True), ("*", False)])),
        # Clothing slots only take items with the Outfit storage flag (128); rings also keep General (1) for every other slot.
        ("storageFlagsByType", O([(f"*-{r}", 129) for r in RINGS] + [("*", 1)])),
        ("materialDensity", 5000),
    ]))
    write("itemtypes/gadget.json", O([
        ("code", "gadget"),
        ("class", "VintageEItemEmcGadget"),
        ("variantgroups", [O([("code", "type"), ("states", GADGETS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/gadget/{type}")])),
        ("materialDensity", 5000),
    ]))
    # Alchemical bags use the game's sack model and bag behaviours (worn in a backpack slot), with 32 slots.
    # The bag, and (1.9.0) its dark and red matter upgrades: same colours, more slots, a matter top.
    for code, slots, top in BAG_TIERS:
        write(f"itemtypes/{code}.json", O([
            ("code", code),
            ("variantgroups", [O([("code", "color"), ("states", BAGS)])]),
            ("maxstacksize", 1),
            ("shape", O([("base", "game:item/bag/linensack")])),
            ("textures", O([("linen", O([("base", "item/bag/alchemicalbag-{color}")])), ("top", O([("base", top)]))])),
            ("behaviors", [
                O([("name", "GroundStorable"), ("properties", O([("layout", "SingleCenter"), ("collisionBox", O([("x1", 0.25), ("y1", 0), ("z1", 0.25), ("x2", 0.75), ("y2", 0.375), ("z2", 0.75)]))]))]),
                O([("name", "HeldBag")]),
                O([("name", "GroundStoredHeldBag"), ("properties", O([("dialogTitleLangCode", "vintagee:alchemicalbagcontents")]))]),
            ]),
            ("attributes", O([("backpack", O([("quantitySlots", slots)]))])),
            ("storageFlags", 2),
            ("creativeinventory", CREATIVE),
            ("guiTransform", xf(rot=(-25, 23, 180), origin=(0.5299, 0.23, 0.5), scale=2.7, rotate=True)),
            ("tpHandTransform", xf(trans=(-0.7, -0.5, -0.6), rot=(-4, 47, -17), scale=0.81)),
            ("groundTransform", xf(rot=(180, 33, -180), origin=(0.5, 0, 0.5), scale=2.5)),
        ]))
    armor()


def armor():
    material_tex = {"dark": "vintagee:item/tool/material/darkmatter", "red": "vintagee:item/tool/material/redmatter", "gem": "vintagee:item/tool/material/gem"}
    shape = {"head": "game:entity/humanoid/seraph/armor/plate/head-steel", "body": "game:entity/humanoid/seraph/armor/plate/body", "legs": "game:entity/humanoid/seraph/armor/plate/legs"}
    prot = {
        "dark": O([("relativeProtection", 0.98), ("flatDamageReduction", 2.4), ("highDamageTierResistant", True), ("protectionTier", 5)]),
        "red": O([("relativeProtection", 0.985), ("flatDamageReduction", 3.0), ("highDamageTierResistant", True), ("protectionTier", 6)]),
        "gem": O([("relativeProtection", 0.99), ("flatDamageReduction", 3.6), ("highDamageTierResistant", True), ("protectionTier", 7)]),
    }
    stats = {"dark": O(), "red": O([("walkSpeed", 0.03)]), "gem": O([("walkSpeed", 0.06), ("healingeffectivness", 0.15)])}
    hide_head = ["hidehat", "hoodshoulder", "hoodupperover", "Hair tile upper part", "ponytailhigh", "ponytaillow", "Hair",
                 "hair-uncovered", "hidefacialhair", "sidehigh", "sidelow", "bangs"]
    attrs = O()
    for m in ARMOR_MATS:
        for p in PARTS:
            ate = O([("texturePrefixCode", f"matterarmor-{p}-{m}"), ("categoryCode", "armor" + p)])
            if p == "head":
                ate["disableElements"] = hide_head
            elif p == "legs":
                ate["disableElements"] = ["shoeL", "shoeR"]
            a = O([
                ("clothescategory", "armor" + p),
                ("attachableToEntity", ate),
                ("protectionModifiers", prot[m]),
                ("statModifiers", stats[m]),
                ("defaultProtLoss", O([("perTierRelativeProtectionLoss", [0.03, 0.15])])),
            ])
            attrs[f"*-{m}-{p}"] = a  # no footStepSound: matter and gem armour are silent
    write("itemtypes/matterarmor.json", O([
        ("code", "matterarmor"),
        ("variantgroups", [O([("code", "material"), ("states", ARMOR_MATS)]), O([("code", "bodypart"), ("states", PARTS)])]),
        ("maxstacksize", 1),
        ("storageFlags", 128),
        ("heldTpIdleAnimation", "holdunderarm"),
        ("behaviors", [O([("name", "Wearable")]), O([("name", "VintageEMatterTool"), ("properties", O([("maxAreaRadius", 0)]))])]),
        ("durability", 10000),
        ("shapeByType", O((f"*-{p}", O([("base", shape[p])])) for p in PARTS)),
        ("texturesByType", O((f"*-{m}-*", O([("seraph", O([("base", "game:block/transparent")])), ("material", O([("base", material_tex[m])]))])) for m in ARMOR_MATS)),
        ("creativeinventory", O([("general", ["*"]), ("clothing", ["*"]), ("vintagee", ["*"])])),
        ("attributesByType", attrs),
        ("guiTransformByType", O([
            ("*-head", xf(rot=(-26, -46, -180), origin=(0.6, 1.72, 0.5), scale=2)),
            ("*-body", xf(trans=(3, 0, 0), rot=(-26, -46, -180), origin=(0.5, 1.11, 0.5), scale=1.37)),
            ("*-legs", xf(trans=(1, 0, 0), rot=(-26, -46, -180), origin=(0.5, 0.6, 0.5), scale=1.25)),
        ])),
        ("tpHandTransformByType", O([
            ("*-head", xf(trans=(-0.82, -2.2, -0.5), rot=(18, 169, 30), origin=(0.5, 1.7, 0.5), scale=0.64)),
            ("*-body", xf(trans=(-1, -1.9, -0.5), rot=(97, -168, 94), origin=(0.5, 1.5, 0.5), scale=0.67)),
            ("*-legs", xf(trans=(-0.7, -0.6, -0.4), rot=(95, -20, -91), origin=(0.5, 0.7, 0.5), scale=0.74)),
        ])),
        ("groundTransformByType", O([
            ("*-head", xf(origin=(0.34, 1.71, 0.5), scale=3.1)),
            ("*-body", xf(rot=(0, 0, 90), origin=(0.34, 1.12, 0.5), scale=3.1)),
            ("*-legs", xf(rot=(0, 0, 90), origin=(0.34, 1.12, 0.5), scale=3.1)),
        ])),
        ("materialDensity", 9000),
    ]))


def cube(top, side, bottom=None):
    bottom = bottom or top
    return O([("up", O([("base", top)])), ("down", O([("base", bottom)])),
              ("north", O([("base", side)])), ("east", O([("base", side)])), ("south", O([("base", side)])), ("west", O([("base", side)]))])


SOUNDS_METAL = O([("place", "game:block/ingot"), ("break", "game:block/ingot"), ("hit", "game:block/ingot")])

# Interdiction Torch brightness (a vanilla torch is 14) and the radius it keeps hostile creatures out of.
TORCH_LIGHT = 20
TORCH_RADIUS = 8

# Matter furnace numbers (also used in the descriptions): heat × fuel temperature, × fuel burn time, × cooking speed, EMC/s.
FURNACE = {"dark": {"heat": 1.4, "burn": 3, "speed": 4, "emc": 2}, "red": {"heat": 2.0, "burn": 4, "speed": 12, "emc": 4}}

# The vanilla firepit's particles (embers, smoke, flames when lit; a wisp of smoke when extinct).
FIREPIT_PARTICLES = """{
 "lit": [
  {"hsvaColor": [{"avg": 30, "var": 20}, {"avg": 255, "var": 50}, {"avg": 255, "var": 50}, {"avg": 255, "var": 0}],
   "gravityEffect": {"dist": "invexp", "avg": -0.1, "var": 0.4}, "posOffset": [{"avg": 0, "var": 0.2}, {"avg": -0.3, "var": 0}, {"avg": 0, "var": 0.2}],
   "velocity": [{"avg": 0, "var": 0.15}, {"avg": 0.5, "var": 0.1}, {"avg": 0, "var": 0.15}], "quantity": {"avg": 0.015}, "size": {"avg": 0.4, "var": 0},
   "sizeEvolve": {"transform": "quadratic", "factor": -0.7}, "lifeLength": {"avg": 1.5}, "vertexFlags": 128, "windAffectednes": 0.2},
  {"hsvaColor": [{"avg": 0, "var": 0}, {"avg": 0, "var": 0}, {"avg": 70, "var": 30}, {"avg": 220, "var": 50}], "opacityEvolve": {"transform": "linear", "factor": -255},
   "gravityEffect": {"avg": 0, "var": 0}, "posOffset": [{"avg": 0, "var": 0.2}, {"avg": -0.3, "var": 0}, {"avg": 0, "var": 0.2}],
   "velocity": [{"avg": 0, "var": 0.025}, {"avg": 0.2, "var": 0.05}, {"avg": 0, "var": 0.025}], "quantity": {"avg": 0.08}, "size": {"avg": 0.3, "var": 0.05},
   "sizeEvolve": {"transform": "linear", "factor": 1.5}, "particleModel": "Quad", "lifeLength": {"avg": 18}, "selfPropelled": true, "windAffectednes": 0.7},
  {"hsvaColor": [{"avg": 20, "var": 20}, {"avg": 255, "var": 50}, {"avg": 255, "var": 50}, {"avg": 255, "var": 0}], "opacityEvolve": {"transform": "quadratic", "factor": -16},
   "gravityEffect": {"avg": 0, "var": 0}, "posOffset": [{"avg": 0, "var": 0.15}, {"avg": -0.25, "var": 0}, {"avg": 0, "var": 0.15}],
   "velocity": [{"avg": 0, "var": 0.1}, {"avg": 0.125, "var": 0.05}, {"avg": 0, "var": 0.1}], "quantity": {"avg": 1}, "size": {"avg": 0.35, "var": 0.05},
   "lifeLength": {"avg": 0.25}, "sizeEvolve": {"transform": "linear", "factor": 0.25}, "particleModel": "Quad", "vertexFlags": 128, "windAffectednes": 0.5}
 ],
 "extinct": [
  {"hsvaColor": [{"avg": 0, "var": 0}, {"avg": 0, "var": 0}, {"avg": 70, "var": 30}, {"avg": 220, "var": 50}], "opacityEvolve": {"transform": "quadratic", "factor": -16},
   "gravityEffect": {"avg": 0, "var": 0}, "posOffset": [{"avg": 0, "var": 0.2}, {"avg": -0.3, "var": 0}, {"avg": 0, "var": 0.2}],
   "velocity": [{"avg": 0, "var": 0.05}, {"avg": 0.1, "var": 0.05}, {"avg": 0, "var": 0.05}], "quantity": {"avg": 0.02}, "size": {"avg": 0.3, "var": 0.05},
   "sizeEvolve": {"transform": "linear", "factor": 1.5}, "particleModel": "Quad", "windAffectednes": 0.7}
 ]
}"""


def blocks():
    write("blocktypes/alchemicalfuelblock.json", O([
        ("code", "alchemicalfuelblock"),
        ("variantgroups", [O([("code", "type"), ("states", FUELS)])]),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Stone"),
        ("drawtype", "cube"),
        ("textures", cube("block/alchemicalfuelblock-{type}", "block/alchemicalfuelblock-{type}")),
        ("combustiblePropsByType", O([
            ("*-alchemicalcoal", O([("burnTemperature", 1400), ("burnDuration", 1440)])),
            ("*-mobiusfuel", O([("burnTemperature", 1500), ("burnDuration", 5760)])),
            ("*-aeternalisfuel", O([("burnTemperature", 1600), ("burnDuration", 23040)])),
        ])),
        ("lightHsvByType", O([("*-alchemicalcoal", [4, 5, 3]), ("*-mobiusfuel", [8, 6, 5]), ("*-aeternalisfuel", [10, 3, 8])])),
        ("resistance", 3),
        ("requiredMiningTier", 1),
        ("sounds", O([("place", "game:block/rock-hit-pickaxe"), ("break", "game:block/rock-break-pickaxe"), ("hit", "game:block/rock-hit-pickaxe")])),
    ]))
    write("blocktypes/alchemicalchest.json", O([
        ("code", "alchemicalchest"),
        ("class", "VintageEBlockEmcMachine"),
        ("entityClass", "VintageEAlchemicalChest"),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Wood"),
        ("drawtype", "cube"),
        ("textures", cube("block/alchemicalchest-top", "block/alchemicalchest-side", "block/alchemicalchest-bottom")),
        ("attributes", O([("quantitySlots", 104), ("interactionHelp", "vintagee:blockhelp-openchest")])),
        ("resistance", 3),
        ("sounds", O([("place", "game:block/planks"), ("break", "game:block/planks"), ("hit", "game:block/planks")])),
    ]))
    # Matter furnaces are the game's firepit (BlockFirepit + a BlockEntityFirepit subclass) built from matter.
    firepit_particles = json.loads(FIREPIT_PARTICLES)
    write("blocktypes/furnace.json", O([
        ("code", "furnace"),
        ("class", "BlockFirepit"),
        ("entityClass", "VintageEFurnace"),
        ("entityBehaviors", [O([("name", "FirepitAmbient")]), O([("name", "TemperatureSensitive")])]),
        ("behaviors", [O([("name", "Lockable")]), O([("name", "Container")]), O([("name", "Ignitable")]), O([("name", "HeatSource")])]),
        ("variantgroups", [O([("code", "type"), ("states", ["dark", "red"])]), O([("code", "burnstate"), ("states", ["cold", "lit", "extinct"])])]),
        ("creativeinventory", O([("general", ["*-cold"]), ("mechanics", ["*-cold"]), ("vintagee", ["*-cold"])])),
        ("attributesByType", O([
            ("*-dark-*", O([("heatModifier", FURNACE["dark"]["heat"]), ("burnDurationModifier", FURNACE["dark"]["burn"]), ("cookingSpeed", FURNACE["dark"]["speed"]),
                            ("emcPerSecond", FURNACE["dark"]["emc"]), ("emcTemperature", 1300)])),
            ("*-red-*", O([("heatModifier", FURNACE["red"]["heat"]), ("burnDurationModifier", FURNACE["red"]["burn"]), ("cookingSpeed", FURNACE["red"]["speed"]),
                           ("emcPerSecond", FURNACE["red"]["emc"]), ("emcTemperature", 1300)])),
        ])),
        ("shapeByType", O([("*-lit", O([("base", "block/matterfurnace-lit")])), ("*", O([("base", "block/matterfurnace-cold")]))])),
        ("textures", O([("body", O([("base", "block/matterblock-{type}")])), ("ember", O([("base", "game:block/coal/ember")])),
                        ("charred", O([("base", "game:block/wood/charred")]))])),
        ("blockmaterial", "Metal"),
        ("renderpass", "OpaqueNoCull"),
        ("faceCullMode", "NeverCull"),
        ("lightAbsorption", 0),
        ("lightHsvByType", O([("*-lit", [7, 7, 18]), ("*-extinct", [7, 7, 3])])),
        ("particlePropertiesByType", O([("*-lit", firepit_particles["lit"]), ("*-extinct", firepit_particles["extinct"])])),
        ("sidesolid", O([("all", False)])),
        ("sideopaque", O([("all", False)])),
        ("collisionbox", O([("x1", 0), ("y1", 0), ("z1", 0), ("x2", 1), ("y2", 0.1875), ("z2", 1)])),
        ("selectionbox", O([("x1", 0), ("y1", 0), ("z1", 0), ("x2", 1), ("y2", 0.25), ("z2", 1)])),
        ("resistance", 10),
        ("requiredMiningTier", 4),
        ("drops", [O([("type", "block"), ("code", "furnace-{type}-cold")])]),
        ("guiTransform", O([("origin", O([("x", 0.5), ("y", 0.1), ("z", 0.5)])), ("scale", 1.6)])),
        ("sounds", SOUNDS_METAL),
    ]))
    # 1.4.0's furnaces: kept so placed ones load, then turn themselves into the new furnace.
    write("blocktypes/matterfurnace.json", O([
        ("code", "matterfurnace"),
        ("entityClass", "VintageEMatterFurnace"),
        ("variantgroups", [O([("code", "type"), ("states", ["dark", "red"])])]),
        ("blockmaterial", "Metal"),
        ("drawtype", "cube"),
        ("textures", O([("all", O([("base", "block/matterblock-{type}")]))])),
        ("drops", [O([("type", "block"), ("code", "furnace-{type}-cold")])]),
        ("resistance", 10),
        ("requiredMiningTier", 4),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/antimatterrelay.json", O([
        ("code", "antimatterrelay"),
        ("class", "VintageEBlockEmcMachine"),
        ("entityClass", "VintageEAntimatterRelay"),
        ("variantgroups", [O([("code", "tier"), ("states", ["mk1", "mk2", "mk3"])])]),
        ("creativeinventory", O([("general", ["*"]), ("mechanics", ["*"]), ("vintagee", ["*"])])),
        ("blockmaterial", "Metal"),
        ("drawtype", "cube"),
        ("textures", cube("block/antimatterrelay-{tier}-top", "block/antimatterrelay-{tier}-side", "block/transmutationtable-bottom")),
        ("attributesByType", O([
            ("*-mk1", O([("capacity", 100000), ("bonusPerSecond", 1), ("transferPerSecond", 64), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
            ("*-mk2", O([("capacity", 1000000), ("bonusPerSecond", 3), ("transferPerSecond", 192), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
            ("*-mk3", O([("capacity", 10000000), ("bonusPerSecond", 10), ("transferPerSecond", 640), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
        ])),
        ("lightHsvByType", O([("*-mk1", [46, 3, 4]), ("*-mk2", [46, 4, 6]), ("*-mk3", [0, 5, 8])])),
        ("resistance", 6),
        ("requiredMiningTier", 3),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/novaexplosive.json", O([
        ("code", "novaexplosive"),
        ("class", "VintageEBlockEmcMachine"),
        ("entityClass", "VintageENovaExplosive"),
        ("variantgroups", [O([("code", "type"), ("states", ["catalyst", "cataclysm"])])]),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Stone"),
        ("drawtype", "cube"),
        ("textures", cube("block/novaexplosive-{type}-top", "block/novaexplosive-{type}-side")),
        ("attributesByType", O([
            ("*-catalyst", O([("radius", 6), ("fuseSeconds", 4), ("igniteInteractionHelp", "vintagee:blockhelp-nova")])),
            ("*-cataclysm", O([("radius", 11), ("fuseSeconds", 6), ("igniteInteractionHelp", "vintagee:blockhelp-nova")])),
        ])),
        ("resistance", 1),
        ("sounds", O([("place", "game:block/sand"), ("break", "game:block/sand"), ("hit", "game:block/sand")])),
    ]))
    write("blocktypes/interdictiontorch.json", O([
        ("code", "interdictiontorch"),
        ("entityClass", "VintageEInterdictionTorch"),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Metal"),
        ("drawtype", "json"),
        ("shape", O([("base", "block/interdictiontorch")])),
        ("textures", O([("rod", O([("base", "item/tool/material/darkmatter")])), ("glow", O([("base", "block/interdictiontorch-glow")]))])),
        ("attributes", O([("radius", TORCH_RADIUS)])),
        ("lightHsv", [36, 3, TORCH_LIGHT]),
        ("sidesolid", O([("all", False)])),
        ("sideopaque", O([("all", False)])),
        ("emitSideAo", O([("all", False)])),
        ("collisionbox", O([("x1", 0.375), ("y1", 0), ("z1", 0.375), ("x2", 0.625), ("y2", 0.75), ("z2", 0.625)])),
        ("selectionbox", O([("x1", 0.375), ("y1", 0), ("z1", 0.375), ("x2", 0.625), ("y2", 0.75), ("z2", 0.625)])),
        ("replaceable", 100),
        ("resistance", 2),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/pedestal.json", O([
        ("code", "pedestal"),
        ("class", "VintageEBlockEmcMachine"),
        ("entityClass", "VintageEPedestal"),
        ("variantgroups", [O([("code", "state"), ("states", ["empty", "active"])])]),
        ("creativeinventoryByType", O([("*-empty", CREATIVE)])),
        ("blockmaterial", "Metal"),
        ("drawtype", "json"),
        ("shape", O([("base", "block/pedestal")])),
        ("textures", O([("body", O([("base", "item/tool/material/darkmatter")])), ("top", O([("base", "block/matterblock-red")]))])),
        ("drops", [O([("type", "block"), ("code", "pedestal-empty")])]),
        ("attributes", O([("interactionHelp", "vintagee:blockhelp-pedestal")])),
        ("lightHsvByType", O([("*-active", [48, 4, 10])])),
        ("sidesolid", O([("all", False), ("down", True)])),
        ("sideopaque", O([("all", False)])),
        ("collisionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("selectionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("resistance", 12),
        ("requiredMiningTier", 4),
        ("sounds", SOUNDS_METAL),
    ]))
    # 1.8.0: the red matter pedestal reaches twice as far and its effects are twice as strong (BEPedestal reads these attributes).
    write("blocktypes/redmatterpedestal.json", O([
        ("code", "redmatterpedestal"),
        ("class", "VintageEBlockEmcMachine"),
        ("entityClass", "VintageEPedestal"),
        ("variantgroups", [O([("code", "state"), ("states", ["empty", "active"])])]),
        ("creativeinventoryByType", O([("*-empty", CREATIVE)])),
        ("blockmaterial", "Metal"),
        ("drawtype", "json"),
        ("shape", O([("base", "block/pedestal")])),
        ("textures", O([("body", O([("base", "item/tool/material/redmatter")])), ("top", O([("base", "block/matterblock-dark")]))])),
        ("drops", [O([("type", "block"), ("code", "redmatterpedestal-empty")])]),
        ("attributes", O([("interactionHelp", "vintagee:blockhelp-pedestal"), ("pedestalRange", 2), ("pedestalPower", 2)])),
        ("lightHsvByType", O([("*-active", [0, 6, 12])])),
        ("sidesolid", O([("all", False), ("down", True)])),
        ("sideopaque", O([("all", False)])),
        ("collisionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("selectionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("resistance", 12),
        ("requiredMiningTier", 5),
        ("sounds", SOUNDS_METAL),
    ]))
    shapes()


def box(name, frm, to, tex):
    faces = O()
    sizes = {"north": (to[0] - frm[0], to[1] - frm[1]), "south": (to[0] - frm[0], to[1] - frm[1]),
             "east": (to[2] - frm[2], to[1] - frm[1]), "west": (to[2] - frm[2], to[1] - frm[1]),
             "up": (to[0] - frm[0], to[2] - frm[2]), "down": (to[0] - frm[0], to[2] - frm[2])}
    for face, (w, h) in sizes.items():
        faces[face] = O([("texture", tex), ("uv", [0, 0, round(min(16, w), 3), round(min(16, h), 3)])])
    return O([("name", name), ("from", frm), ("to", to), ("faces", faces)])


def furnace_shape(lit):
    els = [
        box("base", [0, 0, 0], [16, 1, 16], "#body"),
        box("rim-n", [0, 1, 0], [16, 3, 2], "#body"),
        box("rim-s", [0, 1, 14], [16, 3, 16], "#body"),
        box("rim-w", [0, 1, 2], [2, 3, 14], "#body"),
        box("rim-e", [14, 1, 2], [16, 3, 14], "#body"),
        box("coals", [3, 1, 3], [13, 2, 13], "#ember" if lit else "#charred"),
    ]
    if lit:
        els[-1]["glow"] = 200
        for i, (x, z) in enumerate(((5, 5), (9, 6), (6, 9), (10, 10))):
            ember = box(f"ember{i}", [x, 2, z], [x + 1, 2.5, z + 1], "#ember")
            ember["glow"] = 255
            els.append(ember)
    return O([("textureWidth", 16), ("textureHeight", 16),
              ("textures", O([("body", "vintagee:block/matterblock-dark"), ("ember", "game:block/coal/ember"), ("charred", "game:block/wood/charred")])),
              ("elements", els)])


def face(tex, uv, enabled=True):
    f = O([("texture", tex), ("uv", uv)])
    if not enabled:
        f["enabled"] = False
    return f


def anchor(name, parent, tex, children):
    """An invisible root element hung on one of the player model's elements (stepParentName)."""
    return O([("name", name), ("stepParentName", parent), ("from", [0, 0, 0]), ("to", [0, 0, 0]), ("rotationOrigin", [0, 0, 0]),
              ("faces", O((s, face(tex, [0, 0, 0.5, 0.5], False)) for s in ("north", "east", "south", "west", "up", "down"))),
              ("children", children)])


def plain(name, frm, to, tex, uv, glow=0):
    e = O([("name", name), ("from", frm), ("to", to),
           ("faces", O((s, face(tex, uv)) for s in ("north", "east", "south", "west", "up", "down")))])
    if glow:
        e["glow"] = glow
    return e


# Worn models. Coordinates are in the seraph's element space: the emblem sits on the front of "UpperTorso" (where the
# game's own emblems hang), the ring is a band round "LowerArmL" near the wrist (where the game's bracelets sit).
EMBLEM_FRONT_X = 0.0  # the chest surface: the player model faces -x (its eyes are at x=0 of the head)
EMBLEM_CENTER = (2.9, 3.1)  # (y, z) of the badge centre: upper chest, middle (the torso is 6.2 wide)
EMBLEM_SIZE = 2.6
ARM_BOX = (0.0, 2.5, 0.0, 2.0)  # lower arm cross-section: x from/to, z from/to
RING_Y = (0.8, 1.5)  # on the hand, just below the wrist
RING_GEM_SIDE = "south"  # +z: the outside of the left arm (the left arm is on the +z side of the body)


def central_opaque_pixel(png):
    """A solid pixel near the middle of a texture, used to colour the thin edges of the worn Klein star."""
    try:
        from PIL import Image
    except ImportError:
        return [7, 7, 8, 8]
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    best = None
    for y in range(h):
        for x in range(w):
            if im.getpixel((x, y))[3] > 200:
                d = (x - w / 2 + 0.5) ** 2 + (y - h / 2 + 0.5) ** 2
                if best is None or d < best[0]:
                    best = (d, x, y)
    return [best[1], best[2], best[1] + 1, best[2] + 1] if best else [7, 7, 8, 8]


def worn_shapes():
    tex_root = os.path.join(ROOT, "textures", "item")
    cy, cz = EMBLEM_CENTER
    half = EMBLEM_SIZE / 2
    for s in STARS:
        edge = central_opaque_pixel(os.path.join(tex_root, f"kleinstar-{s}.png"))
        badge = O([("name", "badge"), ("from", [EMBLEM_FRONT_X - 0.3, cy - half, cz - half]), ("to", [EMBLEM_FRONT_X + 0.05, cy + half, cz + half]),
                   ("faces", O([("north", face("#star", edge)), ("east", face("#star", [16, 0, 0, 16])), ("south", face("#star", edge)),
                                ("west", face("#star", [0, 0, 16, 16])), ("up", face("#star", edge)), ("down", face("#star", edge))])),
                   ("glow", 30)])
        write(f"shapes/entity/worn/kleinstar-{s}.json", O([
            ("textureWidth", 16), ("textureHeight", 16),
            ("textures", O([("star", f"vintagee:item/kleinstar-{s}")])),
            ("elements", [anchor("vintageekleinstar", "UpperTorso", "#star", [badge])]),
        ]))

    x0, x1, z0, z1 = ARM_BOX
    y0, y1 = RING_Y
    t = 0.3  # band thickness outside the arm
    band_uv, gem_uv = [13, 24, 19, 27], [14, 5, 18, 9]  # bottom of the ring / its gem, in the 32x32 ring textures
    ym, zm, xm = (y0 + y1) / 2, (z0 + z1) / 2, (x0 + x1) / 2
    gem = {
        "west": ([x0 - t - 0.25, ym - 0.35, zm - 0.35], [x0 - t + 0.05, ym + 0.35, zm + 0.35]),
        "east": ([x1 + t - 0.05, ym - 0.35, zm - 0.35], [x1 + t + 0.25, ym + 0.35, zm + 0.35]),
        "north": ([xm - 0.35, ym - 0.35, z0 - t - 0.25], [xm + 0.35, ym + 0.35, z0 - t + 0.05]),
        "south": ([xm - 0.35, ym - 0.35, z1 + t - 0.05], [xm + 0.35, ym + 0.35, z1 + t + 0.25]),
    }[RING_GEM_SIDE]
    for r in RINGS:
        els = [
            plain("bandW", [x0 - t, y0, z0 - t], [x0 + 0.1, y1, z1 + t], "#ring", band_uv),
            plain("bandE", [x1 - 0.1, y0, z0 - t], [x1 + t, y1, z1 + t], "#ring", band_uv),
            plain("bandN", [x0 + 0.1, y0, z0 - t], [x1 - 0.1, y1, z0 + 0.1], "#ring", band_uv),
            plain("bandS", [x0 + 0.1, y0, z1 - 0.1], [x1 - 0.1, y1, z1 + t], "#ring", band_uv),
            plain("gem", gem[0], gem[1], "#ring", gem_uv, glow=40),
        ]
        write(f"shapes/entity/worn/ring-{r}.json", O([
            ("textureWidth", 32), ("textureHeight", 32),
            ("textures", O([("ring", f"vintagee:item/trinket/{r}")])),
            ("elements", [anchor("vintageering", "LowerArmL", "#ring", els)]),
        ]))


def shapes():
    worn_shapes()
    write("shapes/block/matterfurnace-cold.json", furnace_shape(False))
    write("shapes/block/matterfurnace-lit.json", furnace_shape(True))
    write("shapes/block/interdictiontorch.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("rod", "vintagee:item/tool/material/darkmatter"), ("glow", "vintagee:block/interdictiontorch-glow")])),
        ("elements", [
            box("base", [6, 0, 6], [10, 1, 10], "#rod"),
            box("rod", [7, 1, 7], [9, 9, 9], "#rod"),
            box("head", [6.5, 9, 6.5], [9.5, 12, 9.5], "#glow"),
        ]),
    ]))
    write("shapes/block/pedestal.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("body", "vintagee:item/tool/material/darkmatter"), ("top", "vintagee:block/matterblock-red")])),
        ("elements", [
            box("foot", [2, 0, 2], [14, 2, 14], "#body"),
            box("column", [5, 2, 5], [11, 10, 11], "#body"),
            box("plate", [3, 10, 3], [13, 12, 13], "#top"),
        ]),
    ]))


def I(code, typ="item", consume=True):
    d = O([("type", typ), ("code", code)])
    if not consume:
        d["consume"] = False
    return d


def R(name, pattern, ingredients, out, qty=1, out_type="item", shapeless=False):
    rows = pattern.split(",")
    output = O([("type", out_type), ("code", out)])
    if qty != 1:
        output["quantity"] = qty
    r = O([("ingredientPattern", pattern), ("ingredients", ingredients), ("width", max(len(x) for x in rows)), ("height", len(rows)),
           ("output", output), ("name", "vintagee:" + name)])
    if shapeless:
        r["shapeless"] = True
    return r


DM, RM = I("vintagee:matter-dark"), I("vintagee:matter-red")
PS = I("vintagee:philosophersstone", consume=False)
DIAMOND = I("game:gem-diamond-rough")
OBSIDIAN = I("game:rock-obsidian", "block")
BAND = I("vintagee:ironband")


def dust(t):
    return I(f"vintagee:covalencedust-{t}")


def recipes():
    rs = []
    rs.append(R("covalencedust-low", "SSS,SCS,SSS", O([("S", I("game:stone-*")), ("C", I("game:charcoal"))]), "vintagee:covalencedust-low", 40, shapeless=True))
    rs.append(R("covalencedust-medium", "IS", O([("I", I("game:ingot-iron")), ("S", I("game:saltpeter"))]), "vintagee:covalencedust-medium", 40, shapeless=True))
    rs.append(R("covalencedust-high", "DC", O([("D", DIAMOND), ("C", I("game:coke"))]), "vintagee:covalencedust-high", 40, shapeless=True))
    rs.append(R("ironband", "III,IAI,III", O([("I", I("game:ingot-iron")), ("A", I("vintagee:alchemicalfuel-alchemicalcoal"))]), "vintagee:ironband"))

    rs.append(R("kleinstar-ein", "_M_,MDM,_M_", O([("M", I("vintagee:alchemicalfuel-mobiusfuel")), ("D", DIAMOND)]), "vintagee:kleinstar-ein"))
    for prev, nxt in zip(STARS, STARS[1:]):
        rs.append(R(f"kleinstar-{nxt}", "SS,SS", O([("S", I(f"vintagee:kleinstar-{prev}"))]), f"vintagee:kleinstar-{nxt}", shapeless=True))

    for f in FUELS:
        rs.append(R(f"alchemicalfuelblock-{f}", "FFF,FFF,FFF", O([("F", I(f"vintagee:alchemicalfuel-{f}"))]), f"vintagee:alchemicalfuelblock-{f}", out_type="block"))
        rs.append(R(f"alchemicalfuel-{f}-from-block", "B", O([("B", I(f"vintagee:alchemicalfuelblock-{f}", "block"))]), f"vintagee:alchemicalfuel-{f}", 9))

    t = lambda k: f"vintagee:trinket-{k}"
    rs.append(R("blackholeband", "RRR,DID,RRR", O([("R", I("game:rope")), ("D", DM), ("I", BAND)]), t("blackholeband")))
    rs.append(R("swiftwolfsrendinggale", "DFD,FIF,DFD", O([("D", DM), ("F", I("game:feather")), ("I", BAND)]), t("swiftwolfsrendinggale")))
    rs.append(R("harvestgoddessband", "SFS,DID,SFS", O([("S", I("game:sapling-*", "block")), ("F", I("game:flower-*", "block")), ("D", DM), ("I", BAND)]), t("harvestgoddessband")))
    rs.append(R("ignitionring", "AAA,AIA,ADA", O([("A", I("vintagee:alchemicalfuel-alchemicalcoal")), ("I", BAND), ("D", DM)]), t("ignitionring")))
    rs.append(R("zeroring", "SSS,SIS,SDS", O([("S", I("game:snowball-snow")), ("I", BAND), ("D", DM)]), t("zeroring")))
    rs.append(R("ringofarcana", "ZRI,R_R,HRS", O([("Z", I(t("zeroring"))), ("I", I(t("ignitionring"))), ("H", I(t("harvestgoddessband"))),
                                                  ("S", I(t("swiftwolfsrendinggale"))), ("R", RM)]), t("ringofarcana")))
    rs.append(R("bodystone", "HHH,RPR,HHH", O([("H", I("game:honeycomb")), ("R", RM), ("P", PS)]), t("bodystone")))
    rs.append(R("soulstone", "QQQ,RPR,QQQ", O([("Q", I("game:clearquartz")), ("R", RM), ("P", PS)]), t("soulstone")))
    rs.append(R("lifestone", "BS", O([("B", I(t("bodystone"))), ("S", I(t("soulstone")))]), t("lifestone"), shapeless=True))
    rs.append(R("repairtalisman", "LMH,SPS,HML", O([("L", dust("low")), ("M", dust("medium")), ("H", dust("high")), ("S", I("game:flaxtwine")), ("P", PS)]), t("repairtalisman")))
    rs.append(R("gemofeternaldensity", "DOD,OMO,DOD", O([("D", DIAMOND), ("O", OBSIDIAN), ("M", DM)]), t("gemofeternaldensity")))
    # ProjectE: Black Hole Band + Gem of Eternal Density + 2 red matter.
    rs.append(R("voidring", "BG,RR", O([("B", I("vintagee:trinket-blackholeband")), ("G", I("vintagee:trinket-gemofeternaldensity")), ("R", RM)]), t("voidring"), shapeless=True))
    rs.append(R("evertideamulet", "SSS,DDD,SSS", O([("S", I("game:seashell-*", "block")), ("D", DM)]), t("evertideamulet")))
    rs.append(R("volcaniteamulet", "OOO,DDD,OOO", O([("O", OBSIDIAN), ("D", DM)]), t("volcaniteamulet")))
    rs.append(R("watchofflowingtime", "GGG,DTD,GGG", O([("G", I("game:glass-*", "block")), ("D", DM), ("T", I("game:gear-temporal"))]), t("watchofflowingtime")))

    g = lambda k: f"vintagee:gadget-{k}"
    NOVA = I("vintagee:novaexplosive-catalyst", "block")
    rs.append(R("destructioncatalyst", "NMN,MDM,NMN", O([("N", NOVA), ("M", I("vintagee:alchemicalfuel-mobiusfuel")), ("D", DM)]), g("destructioncatalyst")))
    rs.append(R("hyperkineticlens", "DDD,GNG,DDD", O([("D", DM), ("G", DIAMOND), ("N", NOVA)]), g("hyperkineticlens")))
    rs.append(R("catalyticlens", "DDD,LHC,DDD", O([("D", DM), ("L", I(g("hyperkineticlens"))), ("H", dust("high")), ("C", I(g("destructioncatalyst")))]), g("catalyticlens")))
    rs.append(R("mercurialeye", "_R_,DKD,_R_", O([("R", RM), ("D", DM), ("K", I("vintagee:kleinstar-ein"))]), g("mercurialeye")))
    rs.append(R("archangelssmite", "FFF,DBD,FFF", O([("F", I("game:feather")), ("D", DM), ("B", I("game:bow-*"))]), g("archangelssmite")))
    # The Tome of Knowledge is creative-only (1.8.0): no recipe.
    rs.append(R("diviningrodlow", "__L,_S_,S__", O([("L", dust("low")), ("S", I("game:stick"))]), g("diviningrodlow")))
    rs.append(R("diviningrodmedium", "__M,_R_,M__", O([("M", dust("medium")), ("R", I(g("diviningrodlow")))]), g("diviningrodmedium")))
    rs.append(R("diviningrodhigh", "__H,_R_,H__", O([("H", dust("high")), ("R", I(g("diviningrodmedium")))]), g("diviningrodhigh")))

    for c in BAGS:
        cloth = "white" if c == "white" else c
        rs.append(R(f"alchemicalbag-{c}", "HHH,CXC,CCC", O([("H", dust("high")), ("X", I("vintagee:alchemicalchest", "block")), ("C", I(f"game:cloth-{cloth}"))]), f"vintagee:alchemicalbag-{c}"))
        # Upgrades keep what's in the bag (its attributes are copied over).
        up = R(f"alchemicalbagdark-{c}", "_D_,DBD,_D_", O([("D", DM), ("B", I(f"vintagee:alchemicalbag-{c}"))]), f"vintagee:alchemicalbagdark-{c}")
        up["copyAttributesFrom"] = "B"
        rs.append(up)
        up = R(f"alchemicalbagred-{c}", "_R_,RBR,_R_", O([("R", RM), ("B", I(f"vintagee:alchemicalbagdark-{c}"))]), f"vintagee:alchemicalbagred-{c}")
        up["copyAttributesFrom"] = "B"
        rs.append(up)

    a = lambda m, p: f"vintagee:matterarmor-{m}-{p}"
    rs.append(R("matterarmor-dark-head", "DDD,D_D", O([("D", DM)]), a("dark", "head")))
    rs.append(R("matterarmor-dark-body", "D_D,DDD,DDD", O([("D", DM)]), a("dark", "body")))
    rs.append(R("matterarmor-dark-legs", "DDD,D_D,D_D", O([("D", DM)]), a("dark", "legs")))
    rs.append(R("matterarmor-red-head", "RRR,RAR", O([("R", RM), ("A", I(a("dark", "head")))]), a("red", "head")))
    rs.append(R("matterarmor-red-body", "RAR,RRR,RRR", O([("R", RM), ("A", I(a("dark", "body")))]), a("red", "body")))
    rs.append(R("matterarmor-red-legs", "RRR,RAR,R_R", O([("R", RM), ("A", I(a("dark", "legs")))]), a("red", "legs")))
    K = I("vintagee:kleinstar-vier")
    rs.append(R("matterarmor-gem-head", "GKG,GAG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "head")))]), a("gem", "head")))
    rs.append(R("matterarmor-gem-body", "GAG,GKG,GGG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "body")))]), a("gem", "body")))
    rs.append(R("matterarmor-gem-legs", "GGG,GAG,GKG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "legs")))]), a("gem", "legs")))

    mt = lambda m, k: f"vintagee:mattertool-{m}-{k}"
    rs.append(R("darkmatter-shears", "_D,D_", O([("D", DM)]), mt("dark", "shears")))
    rs.append(R("redmatter-shears", "_R,RT", O([("R", RM), ("T", I(mt("dark", "shears")))]), mt("red", "shears")))
    rs.append(R("redmatter-katar", "WAS,HRR", O([("W", I(mt("red", "sword"))), ("A", I(mt("red", "axe"))), ("S", I(mt("red", "shears"))),
                                               ("H", I(mt("red", "hoe"))), ("R", RM)]), "vintagee:redmattertool-katar", shapeless=True))
    rs.append(R("redmatter-morningstar", "PSH,RR_", O([("P", I(mt("red", "pickaxe"))), ("S", I(mt("red", "shovel"))), ("H", I(mt("red", "hammer"))),
                                                     ("R", RM)]), "vintagee:redmattertool-morningstar", shapeless=True))

    rs.append(R("alchemicalchest", "LMH,SDS,III", O([("L", dust("low")), ("M", dust("medium")), ("H", dust("high")), ("S", I("game:rock-*", "block")),
                                                     ("D", DIAMOND), ("I", I("game:ingot-iron"))]), "vintagee:alchemicalchest", out_type="block"))
    rs.append(R("interdictiontorch", "_D_,QTQ,_A_", O([("D", DIAMOND), ("Q", I("game:clearquartz")), ("T", I("game:torch-basic-lit-up", "block")),
                                                       ("A", I("vintagee:alchemicalfuel-alchemicalcoal"))]), "vintagee:interdictiontorch", out_type="block"))
    rs.append(R("novacatalyst", "BM", O([("B", I("game:bomb-ore", "block")), ("M", I("vintagee:alchemicalfuel-mobiusfuel"))]), "vintagee:novaexplosive-catalyst", out_type="block", shapeless=True))
    rs.append(R("novacataclysm", "NA", O([("N", NOVA), ("A", I("vintagee:alchemicalfuel-aeternalisfuel"))]), "vintagee:novaexplosive-cataclysm", out_type="block", shapeless=True))
    rs.append(R("antimatterrelay-mk1", "OOO,ODO,OGO", O([("O", OBSIDIAN), ("D", DM), ("G", I("game:glass-*", "block"))]), "vintagee:antimatterrelay-mk1", out_type="block"))
    rs.append(R("antimatterrelay-mk2", "ODO,DRD,ODO", O([("O", OBSIDIAN), ("D", DM), ("R", I("vintagee:antimatterrelay-mk1", "block"))]), "vintagee:antimatterrelay-mk2", out_type="block"))
    rs.append(R("antimatterrelay-mk3", "ORO,RXR,ORO", O([("O", OBSIDIAN), ("R", RM), ("X", I("vintagee:antimatterrelay-mk2", "block"))]), "vintagee:antimatterrelay-mk3", out_type="block"))
    rs.append(R("matterfurnace-dark", "DDD,DFD,DDD", O([("D", DM), ("F", I("game:forge", "block"))]), "vintagee:furnace-dark-cold", out_type="block"))
    rs.append(R("matterfurnace-red", "_R_,RFR,_R_", O([("R", RM), ("F", I("vintagee:furnace-dark-cold", "block"))]), "vintagee:furnace-red-cold", out_type="block"))
    rs.append(R("pedestal", "R_R,RDR,DDD", O([("R", RM), ("D", I("vintagee:matterblock-dark", "block"))]), "vintagee:pedestal-empty", out_type="block"))
    rs.append(R("redmatterpedestal", "R_R,RPR,BBB", O([("R", RM), ("P", I("vintagee:pedestal-empty", "block")), ("B", I("vintagee:matterblock-red", "block"))]), "vintagee:redmatterpedestal-empty", out_type="block"))
    write("recipes/grid/projecte.json", rs)


NAMES = O([
    ("covalencedust-low", "Low Covalence Dust"), ("covalencedust-medium", "Medium Covalence Dust"), ("covalencedust-high", "High Covalence Dust"),
    ("kleinstar-ein", "Klein Star Ein"), ("kleinstar-zwei", "Klein Star Zwei"), ("kleinstar-drei", "Klein Star Drei"),
    ("kleinstar-vier", "Klein Star Vier"), ("kleinstar-sphere", "Klein Star Sphere"), ("kleinstar-omega", "Klein Star Omega"),
    ("ironband", "Iron Band"),
    ("trinket-blackholeband", "Black Hole Band"), ("trinket-swiftwolfsrendinggale", "Swiftwolf's Rending Gale"),
    ("trinket-harvestgoddessband", "Harvest Goddess Band"), ("trinket-ignitionring", "Ignition Ring"), ("trinket-zeroring", "Zero Ring"),
    ("trinket-ringofarcana", "Ring of Arcana"), ("trinket-bodystone", "Body Stone"), ("trinket-soulstone", "Soul Stone"),
    ("trinket-lifestone", "Life Stone"), ("trinket-repairtalisman", "Repair Talisman"), ("trinket-gemofeternaldensity", "Gem of Eternal Density"),
    ("trinket-evertideamulet", "Evertide Amulet"), ("trinket-volcaniteamulet", "Volcanite Amulet"), ("trinket-watchofflowingtime", "Watch of Flowing Time"),
    ("trinket-voidring", "Void Ring"),
    ("gadget-destructioncatalyst", "Destruction Catalyst"), ("gadget-hyperkineticlens", "Hyperkinetic Lens"), ("gadget-catalyticlens", "Catalytic Lens"),
    ("gadget-mercurialeye", "Mercurial Eye"), ("gadget-archangelssmite", "Archangel's Smite"), ("gadget-tomeofknowledge", "Tome of Knowledge"),
    ("gadget-diviningrodlow", "Divining Rod (low)"), ("gadget-diviningrodmedium", "Divining Rod (medium)"), ("gadget-diviningrodhigh", "Divining Rod (high)"),
    ("redmattertool-katar", "Red Matter Katar"), ("redmattertool-morningstar", "Red Matter Morning Star"),
    ("mattertool-dark-shears", "Dark Matter Shears"), ("mattertool-red-shears", "Red Matter Shears"),
])
DESCS = O([
    ("covalencedust-low", "Crafting dust for alchemical gear. Made from stone and charcoal."),
    ("covalencedust-medium", "Crafting dust for alchemical gear. Made from iron and saltpeter."),
    ("covalencedust-high", "Crafting dust for alchemical gear. Made from diamond and coke."),
    ("kleinstar-ein", "Stores EMC. Right-click to empty it into your EMC, sneak + right-click to fill it. Rings and gadgets draw from stars you carry. Combine four into the next size (their EMC is kept)."),
    ("ironband", "The base of every ring."),
    ("trinket-blackholeband", "While on: pulls loose items around you to you. Works anywhere in your hotbar or backpack; right-click to switch on/off."),
    ("trinket-swiftwolfsrendinggale", "While on: you run 40% faster and take no fall damage. Costs a little EMC while on."),
    ("trinket-harvestgoddessband", "While on: crops around you grow faster (costs EMC per growth stage)."),
    ("trinket-ignitionring", "While on: hostile creatures near you catch fire, and fire can't hurt you. Costs EMC per burn."),
    ("trinket-zeroring", "While on: still water around and below you freezes into ice."),
    ("trinket-ringofarcana", "Zero Ring, Ignition Ring, Harvest Goddess Band and Swiftwolf's Rending Gale in one. Pick the mode with the tool-mode key."),
    ("trinket-bodystone", "While on: keeps you fed using EMC."),
    ("trinket-soulstone", "While on: heals you using EMC."),
    ("trinket-lifestone", "While on: keeps you fed and heals you using EMC."),
    ("trinket-repairtalisman", "While on: slowly repairs every damaged item and piece of armour you carry. Free."),
    ("trinket-gemofeternaldensity", "While on: turns cheap junk you carry (stone, soil, sand, gravel, sticks... anything worth 8 EMC or less) into the target material. Pick the target with the tool-mode key."),
    ("trinket-evertideamulet", "Right-click a block to place a water source next to it. Free."),
    ("trinket-volcaniteamulet", "Right-click a block to place a lava source next to it. Costs EMC."),
    ("trinket-watchofflowingtime", "While on: time runs 10x faster for the whole world - crops, cooking, curing, drying and day and night (or 3x slower, which also slows hostile creatures near you; pick with the tool-mode key). Costs EMC every second it is on."),
    ("gadget-destructioncatalyst", "Right-click a block to break a 3x3 tunnel into it (3, 6 or 9 deep with the tool-mode key). Costs EMC per block."),
    ("gadget-hyperkineticlens", "Right-click to blow up the spot you're looking at, up to 64 blocks away. Costs EMC."),
    ("gadget-catalyticlens", "A bigger explosion, or the destruction catalyst's tunnel (tool-mode key). Costs EMC."),
    ("gadget-mercurialeye", "Sneak + right-click a block to remember it, then right-click blocks to turn them into it (1x1 or 3x3). You pay the difference in EMC."),
    ("gadget-archangelssmite", "Right-click to strike the creature you're looking at, up to 48 blocks away. Costs EMC."),
    ("gadget-tomeofknowledge", "Creative mode only. Right-click to learn every block and item in the creative inventory that can be transmuted."),
    ("gadget-diviningrodlow", "Right-click a block to see how much EMC lies in the ground below it (3 blocks deep)."),
    ("gadget-diviningrodmedium", "Right-click a block to see how much EMC lies in the ground below it (16 blocks deep)."),
    ("gadget-diviningrodhigh", "Right-click a block to see how much EMC lies in the ground below it (64 blocks deep)."),
    ("redmattertool-katar", "Sword, axe and shears in one: fells trees, cuts plants 3x3 or 5x5 and hits very hard. Never wears out."),
    ("redmattertool-morningstar", "Pickaxe, shovel and sledgehammer in one: digs stone, ore and soil 3x3 or 5x5. Never wears out."),
    ("mattertool-dark-shears", "Never wear out."), ("mattertool-red-shears", "Never wear out."),
])
BLOCK_NAMES = O([
    ("alchemicalfuelblock-alchemicalcoal", "Alchemical Coal Block"), ("alchemicalfuelblock-mobiusfuel", "Mobius Fuel Block"),
    ("alchemicalfuelblock-aeternalisfuel", "Aeternalis Fuel Block"), ("alchemicalchest", "Alchemical Chest"),
    ("matterfurnace-dark", "Dark Matter Furnace"), ("matterfurnace-red", "Red Matter Furnace"),
    ("antimatterrelay-mk1", "Anti-Matter Relay MK1"), ("antimatterrelay-mk2", "Anti-Matter Relay MK2"), ("antimatterrelay-mk3", "Anti-Matter Relay MK3"),
    ("novaexplosive-catalyst", "Nova Catalyst"), ("novaexplosive-cataclysm", "Nova Cataclysm"), ("interdictiontorch", "Interdiction Torch"),
    ("pedestal-empty", "Dark Matter Pedestal"), ("pedestal-active", "Dark Matter Pedestal"),
    ("redmatterpedestal-empty", "Red Matter Pedestal"), ("redmatterpedestal-active", "Red Matter Pedestal"),
] + [(f"furnace-{m}-{st}", f"{n} Matter Furnace") for m, n in (("dark", "Dark"), ("red", "Red")) for st in ("cold", "lit", "extinct")])
BLOCK_DESCS = O([
    ("alchemicalfuelblock-alchemicalcoal", "Nine alchemical coal. Burns nine times as long."),
    ("alchemicalfuelblock-mobiusfuel", "Nine mobius fuel. Burns nine times as long."),
    ("alchemicalfuelblock-aeternalisfuel", "Nine aeternalis fuel. Burns nine times as long."),
    ("alchemicalchest", "104 slots of storage."),
    ("matterfurnace-dark", "Smelts, fires and cooks anything with smelting data - ore straight to ingots, no crucible needed. Runs on fuel or on EMC from adjacent collectors and relays."),
    ("matterfurnace-red", "Like the dark matter furnace, but four times as fast and hot enough for anything."),
    ("antimatterrelay-mk1", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("antimatterrelay-mk2", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("antimatterrelay-mk3", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("novaexplosive-catalyst", "Sneak + right-click with an empty hand to light it. Explodes after a few seconds, dropping what it breaks."),
    ("novaexplosive-cataclysm", "A much bigger nova catalyst. Sneak + right-click with an empty hand to light it."),
    ("interdictiontorch", "Keeps hostile creatures away from it."),
    ("pedestal-empty", "Right-click with a ring, stone, amulet or talisman to set it here; it then works on the area around the pedestal for free."),
    ("pedestal-active", "Right-click with an empty hand to take back what's on it."),
    ("redmatterpedestal-empty", "Like the Dark Matter Pedestal, but it reaches twice as far and its effects are twice as strong. Right-click with a ring, stone, amulet or talisman to set it here."),
    ("redmatterpedestal-active", "Right-click with an empty hand to take back what's on it."),
])
LANG = O([
    ("kleinstar-stored", "Stored EMC: {0} / {1}"),
    ("heldhelp-kleinstar-empty", "Empty into your EMC"),
    ("heldhelp-kleinstar-fill", "Fill from your EMC"),
    ("trinket-on", "On"), ("trinket-off", "Off (right-click to switch on)"), ("trinket-mode", "Mode: {0}"),
    ("watch-faster", "Speed up time"), ("watch-slower", "Slow down time"),
    ("arcana-zero", "Zero Ring"), ("arcana-ignition", "Ignition Ring"), ("arcana-harvest", "Harvest Goddess Band"), ("arcana-swiftwolf", "Swiftwolf's Rending Gale"),
    ("density-target", "Makes: {0} ({1} EMC collected)"),
    ("heldhelp-trinket-toggle", "Switch on/off"), ("heldhelp-trinket-place", "Place liquid"), ("heldhelp-trinket-mode", "Change mode"),
    ("gadget-depth", "{0} deep"), ("gadget-explode", "Explode"), ("gadget-tunnel", "Tunnel"),
    ("tome-learned", "Learned {0} new items."),
    ("eye-target", "Mercurial Eye set to {0}."), ("eye-notarget", "No block chosen - sneak + right-click a block to choose one."), ("eye-current", "Turns blocks into: {0}"),
    ("divining-result", "{0} blocks deep: {1} EMC in total, {2} per block on average. Most valuable: {3} ({4} EMC)."),
    ("heldhelp-gadget-destructioncatalyst", "Break tunnel"), ("heldhelp-gadget-hyperkineticlens", "Shoot an explosive charge"), ("heldhelp-gadget-catalyticlens", "Break tunnel"),
    ("heldhelp-gadget-mercurialeye", "Turn block into chosen block"), ("heldhelp-gadget-eyepick", "Choose block"),
    ("heldhelp-gadget-archangelssmite", "Fire a homing arrow"), ("heldhelp-gadget-tomeofknowledge", "Learn everything"),
    ("heldhelp-gadget-diviningrodlow", "Divine"), ("heldhelp-gadget-diviningrodmedium", "Divine"), ("heldhelp-gadget-diviningrodhigh", "Divine"),
    ("furnace-slots", "Slots: input, fuel, output"),
    ("furnace-burning", "Burning: {0}s of heat left, {1} EMC stored"), ("furnace-cold", "Cold - add fuel or feed it EMC ({1} EMC stored)"),
    ("furnace-progress", "Smelting: {0}%"),
    ("relay-bonus", "Bonus: {0} EMC/s, passes on up to {1} EMC/s"),
    ("nova-lit", "Exploding in {0}s!"),
    ("pedestal-empty", "Empty"), ("pedestal-holding", "Holding: {0}"),
    ("blockhelp-openchest", "Open"), ("blockhelp-furnace", "Open"), ("blockhelp-relay-withdraw", "Take stored EMC (empty hand or Klein star)"),
    ("blockhelp-nova", "Light the fuse (hold)"), ("blockhelp-pedestal", "Place or take a ring/stone/amulet"),
    ("alchemicalbagcontents", "Alchemical Bag"),
    # 1.5.0
    ("arcana-toggle-on", "{0}: on (pick to switch off)"), ("arcana-toggle-off", "{0}: off (pick to switch on)"),
    ("arcana-none", "All four effects are switched off (tool-mode key)"), ("arcana-effects", "Effects on: {0}"),
    ("heldhelp-arcana-toggle", "Switch effects on/off"),
    ("gale-both", "Flight and shield"), ("gale-flight", "Flight only"), ("gale-shield", "Shield only"),
    ("gale-outofemc", "Swiftwolf's Rending Gale ran out of EMC and switched off."),
    ("heldhelp-trinket-voidliquid", "Remove a water/lava source"), ("heldhelp-trinket-plant", "Plant your seeds (on farmland)"),
    ("heldhelp-trinket-teleport", "Teleport"), ("heldhelp-trinket-shoot-evertideamulet", "Shoot water"),
    ("heldhelp-trinket-shoot-volcaniteamulet", "Shoot lava"), ("heldhelp-trinket-shoot-ignitionring", "Shoot a fireball"),
    ("gadget-charge-catalyst", "Charge {0}: 3x3 tunnel {1} deep, up to {2} EMC"), ("gadget-charge-lens", "Charge {0}: radius {1}, {2} EMC"),
    ("gadget-charge-catalytic", "Charge {0}: 3x3 tunnel {1} deep, up to {2} EMC; shots radius {3}, {4} EMC each"),
    ("heldhelp-gadget-fire", "Shoot an explosive charge"), ("heldhelp-gadget-charge", "Choose charge"),
    ("heldhelp-gadget-volley", "Fire a volley of 10 arrows"),
    ("hotkey-fire", "Vintage-E: fire projectile (lenses, amulets, Ignition Ring)"),
    ("furnace-info", "Furnace: {0} °C. EMC for heat: {1} / {2}"),
    ("cmd-emc-added", "Added {0} EMC to {1}. They now have {2} EMC."), ("cmd-emc-removed", "Took {0} EMC from {1}. They now have {2} EMC."),
    ("cmd-emc-gotadded", "{0} gave you {1} EMC. You now have {2} EMC."), ("cmd-emc-gotremoved", "{0} took {1} EMC from you. You now have {2} EMC."),
    ("cmd-emc-needplayer", "Name an online player when running this from the console."), ("cmd-emc-positive", "The amount must be a number above 0."),
    ("cmdhelp-add", "Add EMC to a player's personal EMC (yours if no name is given)"),
    ("cmdhelp-remove", "Take EMC from a player's personal EMC (yours if no name is given; never below 0)"),
    ("cmdhelp-blacklist", "Open a window listing everything on the EMC blacklist; select one to give it a value or take it off the blacklist"),
    ("blacklist-title", "EMC blacklist"),
    ("blacklist-summary", "{0} blocks and items are blacklisted: they never get an EMC value ({1} have one). Select one to give it a value or take it off the blacklist so recipes can value it. Vintage-E {2}."),
    ("blacklist-summary-none", "Nothing is blacklisted ({1} blocks and items have a value). Vintage-E {2}."),
    ("blacklist-empty", "The blacklist is empty."),
    ("missing-blacklist", "Blacklist it"), ("missing-unblacklist", "Remove from blacklist"),
    ("missing-blacklisting", "Blacklisting {0}..."), ("missing-unblacklisting", "Taking {0} off the blacklist..."),
    ("cmd-blacklisted", "{0} ({1}) is now blacklisted and has no EMC value. Saved to ModConfig/vintagee.json."),
    ("cmd-unblacklisted", "{0} ({1}) is off the blacklist but nothing gives it a value yet; it is on /emc missing now."),
    ("cmd-unblacklisted-value", "{0} ({1}) is off the blacklist and now worth {2} EMC."),
    # 1.6.0
    ("gale-speed", "Speed {0}x (flying costs {1} EMC/s)"), ("gale-speed-current", "Speed: {0}x"),
    ("missing-selectshown", "Select shown"), ("missing-clearselection", "Clear"),
    ("missing-selectedmany", "{0} selected"), ("missing-sendingmany", "Setting {0} to {1} EMC..."),
    ("missing-blacklistingmany", "Blacklisting {0}..."), ("missing-unblacklistingmany", "Taking {0} off the blacklist..."),
    ("missing-blacklistmod", "Blacklist whole mod"), ("missing-unblacklistmod", "Unblacklist whole mod"),
    ("missing-modhint-pick", "Pick a mod in the mod filter to do the whole mod at once."), ("missing-modhint", "Whole mod: {0}"),
    ("missing-nogame", "The base game can't be blacklisted or unblacklisted as a whole; select its blocks and items instead."),
    ("missing-blacklistingmod", "Blacklisting everything from {0}..."), ("missing-unblacklistingmod", "Taking everything from {0} off the blacklist..."),
    ("cmd-setmany", "{0} blocks/items are now worth {1} EMC each. Saved to ModConfig/vintagee.json."),
    ("cmd-blacklistedmany", "{0} blocks/items are now blacklisted. Saved to ModConfig/vintagee.json."),
    ("cmd-unblacklistedmany", "{0} blocks/items are off the blacklist."),
    ("cmd-blacklistedmod", "Everything from {0} is now blacklisted (saved as \"{0}:*\" in ModConfig/vintagee.json)."),
    ("cmd-unblacklistedmod", "Everything from {0} is off the blacklist."),
    ("cmdhelp-dumphand", "Show the held item's value and how it was worked out, with its ingredients (also written to ModData/vintagee)"),
    ("dump-held", "{0} ({1}): {2} EMC"), ("dump-stack", "This stack of {0}: {1} EMC in total (its condition or contents count too)"),
    ("dump-cached", "These values came from the cache: run /emc recalc to see where they came from."),
    ("dump-noorigin", "set directly (a value file, the server config or /emc set)"), ("dump-blacklisted", "blacklisted: never has a value"),
    ("dump-novalue", "no value"), ("dump-from", "from: {0}"), ("dump-part", "{0} ({1}): {2} EMC"),
    # 1.7.0
    ("tooltip-data-full-contents", "Transmutes with its contents (counted in its EMC)"),
    ("tooltip-data-full", "Transmutes as exactly this version"),
    ("tooltip-data-itemprice-contents", "Transmutes with its contents, for the item's price only"),
    ("tooltip-data-item", "Transmutes as the plain item (what it holds or is made of isn't kept)"),
    ("error-toomanyexact", "You already know {0} exact versions (filled containers, chiseled blocks...), the most allowed. Ctrl + right-click entries you don't need to forget them."),
    ("gui-create-hint", "Left-click: take one  ·  Right-click: take a stack  ·  Shift-click: stack straight into your inventory  ·  Ctrl + right-click: forget"),
    ("contents-title", "Contents and materials"),
    ("contents-help", "{0} blocks/items hold things or keep what they're made of on the item. Item only: burns and is created as the plain item (containers that hold something won't burn). "
                      "Item + contents: worth the item plus its contents or material, and you learn and create exactly that version. "
                      "Item price only: exactly that version, but it costs and burns for the item alone; its contents are free. (Vintage-E {1})"),
    ("contents-mode-item", "Item only"), ("contents-mode-full", "Item + contents"), ("contents-mode-itemprice", "Item price only"), ("contents-mode-default", "Default"),
    ("contents-kind-contents", "Holds things"), ("contents-kind-material", "Material on item"), ("contents-kind-chiseled", "Chiseled"),
    ("contents-allkinds", "All kinds"), ("contents-allmodes", "All modes"),
    ("contents-setbyyou", "(set by you; default {0})"), ("contents-isdefault", "(default)"),
    ("contents-empty", "Nothing here holds contents or keeps a material."),
    ("contents-select", "Click names to select them, then pick a mode below."),
    ("contents-sending", "Switching {0}..."),
    ("cmd-contents-set", "{0}: now {1}. Saved to ModConfig/vintagee.json."),
    ("cmd-contents-setmany", "{0} blocks/items: now {1}. Saved to ModConfig/vintagee.json."),
    ("cmd-contents-kleinstar", "Klein stars always keep their stored EMC; there's nothing to change."),
    ("cmdhelp-contents", "Open the list of everything that holds things or keeps its material on the item, and switch how each is transmuted"),
    ("cmdhelp-contentsset", "Set the held item: item only, item + contents (full), item price only, or back to the default"),
    ("cmdhelp-configure", "Open the settings window, or show and change one setting from chat (true/false, numbers, list and table entries; /emc configure list prints them all); saved and applied straight away"),
    ("cfg-title", "Vintage-E settings (ModConfig/vintagee.json):"),
    ("cfg-howto", "/emc configure <setting> shows what it does; /emc configure <setting> <value> changes it. Lists: add <entry> / remove <entry> / clear. Tables: set <key> <value> / remove <key> / clear. /emc configure reload re-reads the file after editing it by hand."),
    ("cfg-howto-bool", "Change it: /emc configure {0} true|false"),
    ("cfg-howto-value", "Change it: /emc configure {0} <value>"),
    ("cfg-howto-list", "Change it: /emc configure {0} add <entry> | remove <entry> | clear"),
    ("cfg-howto-table", "Change it: /emc configure {0} set <key> <value> | remove <key> | clear"),
    ("cfg-unknown", "There's no setting called {0}. /emc configure lists them all."),
    ("cfg-ambiguous", "{0} could be: {1}"),
    ("cfg-reloaded", "Settings re-read from ModConfig/vintagee.json and applied."),
    ("cfg-now", "{0} = {1}"),
    ("cfg-cleared", "{0} emptied."), ("cfg-removed", "Removed {0} from {1}."), ("cfg-added", "Added {0} to {1}."),
    ("cfg-set", "{1}: {0} = {2}."), ("cfg-notthere", "{0} isn't in {1}."),
    ("cfg-badmode", "{0} isn't a contents mode: use item, full or itemprice."),
    ("cfg-badvalue", "{0} isn't valid here: it needs {1}."),
    ("cfg-changed", "{0} changed to {1}."),
    ("cfg-remapped", "Saved; EMC values were worked out again."),
    ("cfg-applied", "Saved and applied."),
    ("cfg-restart", "Saved; it takes effect the next time the server starts."),
    ("cfg-window-title", "Vintage-E settings"),
    ("cfg-search", "Search settings"),
    ("cfg-hint-bool", "Switch it on or off, then Save."),
    ("cfg-hint-number", "Type a number, then Save."),
    ("cfg-hint-text", "Type a value, then Save."),
    ("cfg-hint-list", "One entry per line (codes and wildcards like game:rock-*), then Save."),
    ("cfg-hint-table", "One \"key = value\" per line (e.g. game:rock-granite = 2), then Save."),
    ("cfg-on", "On"),
    ("cfg-save", "Save"), ("cfg-revert", "Undo"),
    ("cfg-saving", "Saving..."),
    ("cfg-saved", "{0} saved."),
    ("cfg-badline", "This line needs to be \"key = value\": {0}"),
    ("announce-configure", "Vintage-E can be configured in game: <strong>/emc configure</strong> opens a window with every setting, what it does, and a way to change it. /emc help lists every command."),
    ("missing-showblacklisted", "Show blacklisted ({0})"), ("missing-blacklistedtag", "(blacklisted)"),
    ("missing-unblacklisthint", "Blacklisted entries (red): select them, then take them off the blacklist."),
    ("missing-selectblacklisted", "Select blacklisted (red) entries first."),
    ("blacklist-hidemissing", "Hide missing ({0})"), ("blacklist-missingtag", "(missing)"),
    ("blacklist-blacklisthint", "Missing entries (amber): select them, then blacklist them."),
    ("blacklist-selectmissing", "Select missing (amber) entries first."),
    # 1.8.0
    ("chat-announce", "Vintage-E version {0}: calculated {1} EMC values, {2} missing, {4} value loops (/emc loop)."),
    ("chat-announce-cached", "Vintage-E version {0}: {1} EMC values (calculated earlier, nothing has changed), {2} missing, {4} value loops (/emc loop)."),
    ("gui-sort-valuedesc", "Most EMC first"), ("gui-sort-valueasc", "Least EMC first"), ("gui-sort-nameasc", "Name A to Z"), ("gui-sort-namedesc", "Name Z to A"),
    ("gui-forget-confirm", "Forget {0}? You'd have to burn one again to learn it back."),
    ("gui-create-hint", "Left-click: take one  ·  Right-click: take a stack  ·  Shift-click: a stack straight into your inventory  ·  Ctrl + right-click: forget (asks first)"),
    ("magnet-title", "Magnet"),
    ("magnet-hint", "Put a Black Hole Band or Void Ring here. While you carry this tablet, items on the ground within {0} blocks that pass the filter are pulled in and transmuted into your EMC, and learned."),
    ("magnet-mode-only", "Pull: only these"), ("magnet-mode-except", "Pull: all except these"),
    ("magnet-none", "No magnet in the slot: nothing is pulled in. The filter is kept."),
    ("magnet-filter-only", "Pulls in only these. Click a slot holding an item to add it; with an empty hand to clear it."),
    ("magnet-filter-except", "Pulls in everything that can be transmuted except these. Click a slot holding an item to add it; with an empty hand to clear it."),
    ("magnet-filter-empty", "Nothing chosen yet: click a slot while holding an item to pull that item in."),
    ("error-notablet", "Carry a Transmutation Tablet to use its magnet."),
    ("error-notamagnet", "{0} isn't a magnet: use a single Black Hole Band or Void Ring."),
    ("error-notransmute", "{0} can't be transmuted on this server: it keeps its EMC value, but can't be burned, learned or created."),
    ("tooltip-notransmute", "Can't be transmuted on this server"),
    ("tome-creativeonly", "The Tome of Knowledge only works in creative mode."),
    ("dump-remapping", "These values came from the cache, which doesn't keep the recipes behind them, so they were worked out again to explain them."),
    ("dump-notransmute", "On the no-transmute list: it keeps its value, but can't be burned, learned or created."),
    ("dump-free", "A free ingredient: no value of its own, and it costs nothing in recipes."),
    ("dump-loop", "Stuck in a value loop: {0}. Each is only made from the next, and nothing outside the loop gives any of them a value. Give one of them a value (/emc set or BaseValues) to break it."),
    ("dump-norecipe", "Nothing the mod can read makes it (no recipe, drop or process): loot, creative-only, or made a way the mod doesn't know. Give it a value with /emc set or BaseValues."),
    ("dump-ways", "No value. {0} way(s) to make it, and what each is missing:"),
    ("dump-moreways", "...and {0} more"), ("dump-lacks", "no value: {0} ({1})"), ("dump-inloop", "(part of the same loop)"),
    ("dump-more", "...{0} more lines in the file."), ("dump-file", "Written to {0}"), ("dump-nofile", "Could not write the file (see the server log)."),
    ("cmdhelp-dump", "Writes every value, and what it was worked out from, to ModData/vintagee/emc-values.csv (the chat says where)."),
    ("cmdhelp-dumphand", "Show the held item's value and how it was worked out, or why it has none (missing ingredients, value loops); also written to a file in ModData/vintagee (the chat says where)"),
    ("loop-none", "No value loops: nothing is waiting on itself for a value."),
    ("loop-header", "{0} value loop(s). A -> B means A is only made from B, so none of them can get a value:"),
    ("loop-settled", "{0} settled loop(s): recipes that kept making each other cheaper, so their items keep their first value:"),
    ("cmdhelp-loop", "List the value loops: unvalued items that are each only made from the next (also written to ModData/vintagee/emc-loops.txt)"),
    ("notransmute-title", "Not transmutable"),
    ("notransmute-summary", "{0} blocks/items keep their EMC value but can't be burned, learned or created ({1} values in all, Vintage-E {2})."),
    ("notransmute-summary-none", "Everything with an EMC value can be transmuted ({1} values, Vintage-E {2}). Tick \"Show valued items\" to pick some that can't."),
    ("notransmute-empty", "Nothing is on the no-transmute list."),
    ("notransmute-remove", "Allow transmuting"), ("notransmute-removemod", "Allow whole mod"), ("notransmute-add", "Stop transmuting"),
    ("notransmute-addhint", "Valued items (blue): select them, then stop them being transmuted."),
    ("notransmute-showvalued", "Show valued items ({0})"), ("notransmute-valuedtag", "(valued)"),
    ("notransmute-selectvalued", "Select valued (blue) entries first."),
    ("notransmute-adding", "Stopping {0} from being transmuted..."), ("notransmute-removing", "Letting {0} be transmuted again..."),
    ("notransmute-removingmod", "Letting everything from {0} be transmuted again..."),
    ("cmd-notransmute-added", "{0} can no longer be transmuted (they keep their EMC value)."), ("cmd-notransmute-removed", "{0} can be transmuted again."),
    ("cmd-notransmute-addedmod", "Nothing from {0} can be transmuted now."), ("cmd-notransmute-removedmod", "Everything from {0} can be transmuted again."),
    ("cmd-notransmute-stillwildcard", "({0} still matched by a wildcard in NoTransmute; change that in /emc configure.)"),
    ("cmd-notransmute-held-added", "{0} ({1}) can no longer be transmuted."), ("cmd-notransmute-held-removed", "{0} ({1}) can be transmuted again."),
    ("cmdhelp-notransmute", "Open the list of blocks/items that keep their EMC value but can't be burned, learned or created"),
    ("cmdhelp-notransmuteset", "Add the held item to that list, or take it off"),
    ("missing-summary", "{0} of {1} blocks/items in the creative inventory have no EMC value ({2} values, Vintage-E {3})."),
    ("blacklist-summary", "{0} blocks/items are blacklisted and never get an EMC value ({1} have one, Vintage-E {2})."),
    ("missing-select", "Click names to select them; Shift-click selects everything in between."),
    ("cfg-default", "Default: {0}"), ("cfg-isdefault", "At its default ({0})."),
    ("cfg-resetone", "Reset to default"), ("cfg-resetall", "Reset all"), ("cfg-resetlink", "   reset"),
    ("cfg-resetone-confirm", "Put {0} back to its default ({1})?"),
    ("cfg-resetall-confirm", "Put every setting back to its default? Your BaseValues, blacklist and every other change in ModConfig/vintagee.json will be lost."),
    ("cfg-reset-done", "{0} is back to its default: {1}."),
    ("cfg-resetall-done", "Every setting is back to its default, and the EMC values were worked out again."),
    ("cmdhelp-configure", "Open the settings window, or show and change one setting from chat (true/false, numbers, list and table entries; /emc configure <setting> default resets one, /emc configure resetall resets everything, /emc configure list prints them all); saved and applied straight away"),
    # 1.9.0
    ("gray-title", "Gray list"),
    ("gray-summary", "{0} blocks/items are on the gray list: left out of /emc missing and given no guessed value, but valued as soon as a recipe makes them ({1} values in all, Vintage-E {2})."),
    ("gray-summary-none", "Nothing is on the gray list ({1} values, Vintage-E {2}). Untick \"Hide missing\" to gray-list things nothing makes yet."),
    ("gray-empty", "Nothing is on the gray list."),
    ("gray-remove", "Take off gray list"), ("gray-removemod", "Take whole mod off"), ("gray-add", "Gray-list it"),
    ("gray-addhint", "Missing entries (amber): select them, then put them on the gray list."),
    ("gray-adding", "Gray-listing {0}..."), ("gray-removing", "Taking {0} off the gray list..."),
    ("gray-removingmod", "Taking everything from {0} off the gray list..."),
    ("gray-valued", "valued by a recipe now: {0} EMC"), ("gray-waiting", "waiting: nothing makes it yet"),
    ("cmd-gray-added", "{0} gray-listed: out of /emc missing, and valued only if a recipe makes them."),
    ("cmd-gray-removed", "{0} taken off the gray list."),
    ("cmd-gray-addedmod", "Everything from {0} is on the gray list."), ("cmd-gray-removedmod", "Everything from {0} is off the gray list."),
    ("cmd-gray-held-added", "{0} ({1}) is on the gray list."), ("cmd-gray-held-removed", "{0} ({1}) is off the gray list."),
    ("cmdhelp-graylist", "Open the gray list: blocks/items left out of /emc missing that only get a value if a recipe makes them"),
    ("cmdhelp-graylistset", "Put the held item on the gray list, or take it off"),
    ("missing-clearselection", "Clear selection"),
    ("missing-selectedhidden", "({0} hidden by the filters)"),
    ("missing-blacklistmodmissing", "Blacklist its missing"),
    ("missing-modmissinghint-pick", "Pick a mod in the mod filter to blacklist only its missing blocks/items."),
    ("missing-modmissinghint", "Only the {0} missing from {1}, by their own codes: everything of it that has a value keeps it."),
    ("hotkey-setvalue", "Vintage-E: set the EMC value of the hovered block/item (admins)"),
    ("setvalue-title", "Set EMC value"),
    ("setvalue-now", "Now: {0} EMC"), ("setvalue-none", "No EMC value now"),
    ("setvalue-label", "Value"),
    ("setvalue-hint", "Save puts it all in the server config (a value like /emc set) and works the values out again. Unpin takes a value set by hand off. 0 means no EMC; 12k and 3m work too. A blacklisted item or a free ingredient has no value of its own, whatever is typed above."),
    ("setvalue-unpin", "Unpin"), ("setvalue-cancel", "Cancel"),
    ("setvalue-bad", "That isn't a number of 0 or more."),
    ("setvalue-nothing", "Hover over a block or item (in your inventory, the handbook or any window) first."),
    ("recipe-title", "Hand-written recipes"),
    ("recipe-intro", "For things the mapper can't see being made, like a boat built in stages. Hold an item on the mouse and click a slot to put a copy there (as many as you hold; right click for one more). Click a slot with nothing held to pick it."),
    ("recipe-makes", "Makes"), ("recipe-from", "From"),
    ("recipe-amount", "Amount"), ("recipe-takeout", "Take out"),
    ("recipe-noselection", "No slot picked"), ("recipe-selected", "Picked: {0}"),
    ("recipe-line", "Recipe (you can also edit it here, e.g. game:plank-* for any plank):"),
    ("recipe-add", "Add recipe"), ("recipe-save", "Save changes"), ("recipe-clear", "Clear"),
    ("recipe-saved", "Recipes on this server: {0}."), ("recipe-fromfiles", "{0} more come from value files and aren't listed."),
    ("recipe-none", "(none yet)"),
    ("recipe-edit", "Edit"), ("recipe-remove", "Remove"),
    ("recipe-problem", "Not used: {0}"), ("recipe-outvalue", "Its result is worth {0} EMC now."), ("recipe-outnovalue", "Its result has no value yet (an ingredient has none)."),
    ("recipe-incomplete", "Put the result in the first slot and at least one ingredient in the others."),
    ("recipe-working", "Working it out..."), ("recipe-editing", "Editing: change it, then Save changes."),
    ("recipe-bad", "That recipe can't be used: {0}"),
    ("recipe-already", "That recipe is already there."),
    ("recipe-added", "Recipe saved: {0} is worth {1} EMC now."),
    ("recipe-addednovalue", "Recipe saved, but {0} still has no value: one of its ingredients has none."),
    ("recipe-removed", "Recipe removed: {0}"), ("recipe-notfound", "No such recipe: {0}"),
    ("recipe-needline", "Give the recipe: output = ingredients, e.g. item:mymod:boat = 40 game:plank-oak + 8 game:rope"),
    ("cmdhelp-recipe", "Open the window of hand-written recipes, for things the mapper can't see being made"),
    ("cmdhelp-recipeset", "Add or remove a hand-written recipe by text"),
    ("loop-more", "... ({0} more)"),
    # 1.9.1
    ("setvalue-save", "Save"),
    ("setvalue-nowpinned", "Now: {0} EMC (set by hand)"), ("setvalue-nowblacklisted", "Blacklisted: no EMC value"),
    ("setvalue-nowfree", "Free ingredient: no value, free in recipes"),
    ("setvalue-blacklist", "Blacklisted: no value, can't be burned, learned or created"),
    ("setvalue-gray", "Gray list: no guessed value and left out of /emc missing, but valued by recipes"),
    ("setvalue-free", "Free ingredient: no value of its own, free in the recipes that use it"),
    ("setvalue-fromfiles", "(set by a value file or a wildcard: unticking here can't undo it)"),
    ("setvalue-result", "{0} ({1}): {2}."), ("setvalue-novalue", "no EMC value"), ("setvalue-isblacklisted", "blacklisted"),
    ("setvalue-isfree", "free ingredient"), ("setvalue-isgray", "gray-listed"),
    ("setvalue-freefromfiles", "It's a free ingredient in a value file, so it stays one."),
    ("setvalue-grayfromfiles", "A value file or a wildcard puts it on the gray list, so it stays there."),
    ("cfg-find", "Find in this list..."), ("cfg-lines", "Lines {0}-{1} of {2}"), ("cfg-lines-found", "Found {2}: {0}-{1}"),
    ("cfg-lines-nomatch", "No line has that"), ("cfg-lines-empty", "(empty)"),
    ("firekey-line", "Fire-projectile key: {0}"), ("firekey-unbound", "Fire-projectile key: not set (Settings, Controls)"),
    ("condenser-incoming", "Receiving {0} EMC a second"),
    ("food-saturation", "Saturation only"), ("food-nutrition", "Saturation and nutrition (1 EMC per point)"),
    ("watchhud-speed", "Time {0}"), ("watchhud-on", "Watch on"),
    ("clientcfg-title", "Vintage-E (this computer)"), ("clientcfg-watch", "Watch of Flowing Time"),
    ("clientcfg-icon", "Show an icon while it's on"), ("clientcfg-size", "Icon size"), ("clientcfg-position", "Position"),
    ("clientcfg-offset", "Move it by"), ("clientcfg-glow", "Make the watch glow while it's on"), ("clientcfg-color", "Glow colour"),
    ("clientcfg-note", "Saved for this computer only (ModConfig/vintagee-client.json). Type .vintagee in chat to open this again."),
    ("clientcfg-done", "Done"),
    ("clientcfg-pos-topleft", "Top left"), ("clientcfg-pos-topcenter", "Top middle"), ("clientcfg-pos-topright", "Top right"),
    ("clientcfg-pos-middleleft", "Middle left"), ("clientcfg-pos-middleright", "Middle right"),
    ("clientcfg-pos-bottomleft", "Bottom left"), ("clientcfg-pos-bottomcenter", "Bottom middle"), ("clientcfg-pos-bottomright", "Bottom right"),
    ("clientcfg-color-sky", "Sky blue"), ("clientcfg-color-gold", "Gold"), ("clientcfg-color-green", "Green"), ("clientcfg-color-purple", "Purple"),
    ("clientcfg-color-red", "Red"), ("clientcfg-color-white", "White"), ("clientcfg-color-custom", "Your own (#RRGGBB)"),
    # 1.10.0: the /emc dump and /emc loop windows
    ("cmdhelp-dumphand", "Open a window explaining everything that makes up the held item's value (or a block/item's, by code): durability, contents, the recipe and its numbers, value files and settings; or why it has none"),
    ("cmdhelp-loop", "Open a window of the value loops: unvalued items that are each only made from the next (also written to ModData/vintagee/emc-loops.txt)"),
    ("dump-unknowncode", "No block or item is called {0}. Try a full code like game:ingot-copper, or hold it and use /emc dump hand."),
    ("dumpgui-title", "EMC: {0}"),
    ("dumpgui-worth", "{0}: {1} EMC each"), ("dumpgui-worthnone", "{0}: no EMC value"),
    ("dumpgui-code", "Code: {0} (as the server config and value files write it)"),
    ("dumpgui-nofactors", "Nothing about one in particular changes that: it has no durability, contents or stored EMC."),
    ("dumpgui-stacktotal", "The stack of {0}: {1} EMC"),
    ("dumpgui-blacklisted", "Blacklisted: it never has a value, and can't be burned, learned or created."),
    ("dumpgui-gray", "On the gray list: only recipes give it a value (no guesses), and /emc missing leaves it out."),
    ("dumpgui-known", "You know it: you can create it in the transmutation table."),
    ("dumpgui-unknown", "You don't know it yet: burn one in the transmutation table to learn it."),
    ("dumpgui-sec-held", "What this one is worth, step by step"), ("dumpgui-sec-new", "What a new one is worth, step by step"),
    ("dumpgui-newnote", "A new, unworn, empty one. A worn or filled one is worth more or less: hold it and use /emc dump hand, or press Ctrl+K over it and Why this value?"),
    ("dumpgui-base", "The plain item's value: {0} EMC (where it comes from is further down)"),
    ("dumpgui-stored", "+ {0} EMC stored in it (a Klein star is worth what it holds) = {1}"),
    ("dumpgui-data-contents", "It holds things. Mode: {0} ({1}; change it with /emc contents)."),
    ("dumpgui-data-material", "What it's made of is stored on the item. Mode: {0} ({1}; change it with /emc contents)."),
    ("dumpgui-data-chiseled", "A chiseled block, made of voxels of other blocks. Mode: {0} ({1}; change it with /emc contents)."),
    ("dumpgui-modesource-default", "built in"), ("dumpgui-modesource-file", "set by a value file"), ("dumpgui-modesource-config", "set in the server config"),
    ("dumpgui-mode-item", "Item only: what it holds or is made of doesn't count. It burns, and is created, as the plain item."),
    ("dumpgui-wouldcontents", "(What it holds would be worth {0} EMC.)"),
    ("dumpgui-protected", "Something is inside, so the table and condensers won't burn it (ProtectContainersWithContents)."),
    ("dumpgui-mode-itemprice", "Item price only: what it holds ({0} EMC) is free; it doesn't count when burning or creating it."),
    ("dumpgui-nomaterialvalue", "What it's made of has no value, so this one has none."),
    ("dumpgui-molten", "+ {0} units of molten {1} (100 units = one, at {2} EMC) = {3}"),
    ("dumpgui-empty", "Nothing inside."),
    ("dumpgui-inside", "+ {0} x{1} at {2} EMC = {3}"), ("dumpgui-inside-novalue", "{0} x{1}: no value, adds nothing"),
    ("dumpgui-servings", "x {0} servings (a meal keeps one serving's ingredients) = {1} EMC for what's inside"),
    ("dumpgui-contentstotal", "= {0} EMC for what's inside"),
    ("dumpgui-variant", "This version ({0}) has its own value: {1} EMC"),
    ("dumpgui-novariant", "Nothing the mod knows makes this exact version, so it's worth the plain item: {0} EMC"),
    ("dumpgui-variants", "{0} versions of it have their own values, from {1} to {2} EMC."),
    ("dumpgui-novoxels", "No voxels stored on it, so it has no value."),
    ("dumpgui-voxels", "+ {0}: {1} of 4096 voxels, at {2} EMC a block = {3}"),
    ("dumpgui-voxels-novalue", "{0}: {1} voxels, but it has no value, so the whole block has none"),
    ("dumpgui-wear-off", "Durability {0}/{1} doesn't change the value: DurabilityScalesEmc is off in the server config."),
    ("dumpgui-wear-full", "Durability {0}/{0}: as good as new, so nothing comes off (a worn one is worth less: DurabilityScalesEmc)."),
    ("dumpgui-wear", "Durability {0}/{1}: worth {2} of new: {3} x {2} = {4} (DurabilityScalesEmc)"),
    ("dumpgui-wear-contents", "Only the item wears: what's inside keeps its full value."),
    ("dumpgui-liquid", "A liquid: {0} portions make a litre, so a litre is worth {1} EMC."),
    ("dumpgui-fresh", "How fresh it is doesn't count: once it spoils it turns into something else (rot), with its own value."),
    ("dumpgui-result", "= {0} EMC each: what burning one gives, and what creating one costs"),
    ("dumpgui-resultnone", "= no value: it can't be burned, learned or created"),
    ("dumpgui-resultstack", "x {0} in the stack = {1} EMC"),
    ("dumpgui-sec-origin", "Where the plain item's value comes from"),
    ("dumpgui-free-config", "A free ingredient, from the server config's FreeIngredients (\"{0}\"): no value of its own, and the recipes that use it don't pay for it."),
    ("dumpgui-free-file", "A free ingredient, from a value file (\"{0}\"): no value of its own, and the recipes that use it don't pay for it."),
    ("dumpgui-bl-zero", "Set to 0 by hand (\"{0}\" in the server config), which takes its value away."),
    ("dumpgui-bl-config", "On the server config's Blacklist (\"{0}\"). Ctrl+K over it, or the /emc blacklist window, takes it off."),
    ("dumpgui-bl-attribute", "Its own mod says it has no EMC (\"emcValue\": 0 in its attributes)."),
    ("dumpgui-bl-file", "Blacklisted by a value file (\"{0}\"). The /emc blacklist window can make an exception."),
    ("dumpgui-base-fixed", "Set by hand: FixedValues \"{0}\" = {1} in the server config (from /emc set, Ctrl+K or /emc configure)."),
    ("dumpgui-base-wildcard", "That's a wildcard: Ctrl+K over this item sets just this one."),
    ("dumpgui-base-config", "The server config's BaseValues: \"{0}\" = {1}."),
    ("dumpgui-base-attribute", "Its own mod gives it this value (\"emcValue\": {0} in its attributes)."),
    ("dumpgui-base-file", "A value file ({0}): \"{1}\" = {2}."),
    ("dumpgui-base-litre", "A value file ({0}): \"{1}\" = {2} a litre; {3} portions make a litre, so one is worth {4}."),
    ("dumpgui-base-note", "A base value: the mapper starts from it, and everything made from it builds on it."),
    ("dumpgui-base-fixednote", "Recipes never change a value set by hand. Unpin it (Ctrl+K, Unpin) to let recipes decide again."),
    ("dumpgui-setting", "(Server setting: {0})"),
    ("dumpgui-guessnote", "A guess, used only because no recipe gives it a value. If it's off, give it a real one with Ctrl+K or BaseValues."),
    ("dumpgui-made", "Made by {0} (the cheapest way the mapper found):"),
    ("dumpgui-term", "{0} x{1} at {2} EMC = {3}"),
    ("dumpgui-term-free", "{0} x{1}: a free ingredient, costs nothing"),
    ("dumpgui-term-novalue", "{0} x{1}: no value now"),
    ("dumpgui-cheapestof", "(the cheapest of the {0} that fit)"),
    ("dumpgui-timebonus", "{0} for the time it takes: {1} in-game days to {2}, at {3} EMC a day (TransitionEmcPerDay)"),
    ("dumpgui-extra", "{0} extra each time it's made"),
    ("dumpgui-returned", "(given back)"),
    ("dumpgui-per", "= {0} for {1}, so {2} EMC each"),
    ("dumpgui-back", "Worked backwards ({1}): nothing the mapper knows makes it, but {0} uses it, so it's worth what that makes minus everything else that goes in:"),
    ("dumpgui-whatitmakes", "(what it makes)"),
    ("dumpgui-joint-back", "Worked backwards: {0} turns it into several things at once, so it's worth all of them together:"),
    ("dumpgui-joint", "Shared out: {0} makes several things at once; what isn't already valued gets an even share of what goes in:"),
    ("dumpgui-hasvalue", "(already has a value)"),
    ("dumpgui-share", "= {0} left, split evenly between the {1} things that had no value: {2} each; {3} of this make one share, so {4} EMC each"),
    ("dumpgui-perback", "= {0} for the {1} it takes, so {2} EMC each"),
    ("dumpgui-times", "(x{0})"),
    ("dumpgui-short-fixed", "set by hand (FixedValues)"), ("dumpgui-short-config", "a base value in the server config (BaseValues)"),
    ("dumpgui-short-attribute", "a base value its own mod gives it"), ("dumpgui-short-file", "a base value from {0}"), ("dumpgui-short-back", "worked backwards from"),
    ("dumpgui-src-grid", "the crafting grid"), ("dumpgui-src-reciperegistrysystem-smithingrecipes", "smithing"),
    ("dumpgui-src-reciperegistrysystem-barrelrecipes", "a barrel"), ("dumpgui-src-reciperegistrysystem-clayformingrecipes", "clay forming"),
    ("dumpgui-src-reciperegistrysystem-knappingrecipes", "knapping"), ("dumpgui-src-reciperegistrysystem-cookingrecipes", "cooking in a pot"),
    ("dumpgui-src-reciperegistrysystem-metalalloys", "alloying in a crucible"), ("dumpgui-src-placement-state", "placing it (the same block facing or standing another way)"),
    ("dumpgui-src-block-drops", "breaking a block (what it drops)"), ("dumpgui-src-smelting-firing-cooking", "smelting, firing or cooking"),
    ("dumpgui-frozen", "Recipe loop: its recipes kept making each other cheaper, so it kept the first value it got."),
    ("dumpgui-differs", "(It keeps {0} EMC: the values above changed after it was worked out.)"),
    ("dumpgui-sec-other", "Other ways to make it ({0})"),
    ("dumpgui-other-note", "The cheapest way sets the value, so nothing can be made for less than it burns for."),
    ("dumpgui-other-set", "Its value is set, so these don't change it."),
    ("dumpgui-way", "{0}: {1} EMC each"), ("dumpgui-way-lacks", "{0}: can't be priced, no value for {1}"), ("dumpgui-way-shared", "{0} (shared out with other things)"),
    ("dumpgui-loop", "Stuck in a value loop: {0}. Each is only made from the next, and nothing outside the loop gives any of them a value."),
    ("dumpgui-lacks", "{0} has no value"),
    ("dumpgui-fixhint", "To give it a value: Ctrl+K over it (or Set value below), a recipe in /emc recipe, or a value for a missing ingredient."),
    ("loopgui-title", "EMC value loops"), ("loopgui-remapped", "The values came from the cache, which doesn't keep recipes, so they were worked out again to find the loops."),
    ("loopgui-header", "{0} value loop(s): in each, every item is only made from the next one, so none of them can get a value."),
    ("loopgui-help", "Give any one item in a loop a value (Ctrl+K over it, or click it and Set value) and the rest follow. Click an item to see why it has none."),
    ("loopgui-loop", "Loop {0}: {1} items"), ("loopgui-step", "{0} is made from {1} ({2})"),
    ("loopgui-settledheader", "{0} settled loop(s)"),
    ("loopgui-settledhelp", "Recipes that kept making each other cheaper; their items kept the first value they got. If one looks too cheap, set it with Ctrl+K."),
    ("loopgui-settled", "Settled loop {0}: {1} items"),
    ("reportgui-back", "Back"), ("reportgui-refresh", "Refresh"), ("reportgui-copy", "Copy"), ("reportgui-copied", "Copied to the clipboard."),
    ("reportgui-setvalue", "Set value"), ("reportgui-handbook", "Handbook"),
    ("reportgui-search", "Find in this..."), ("reportgui-searchloops", "Find an item in the loops..."),
    ("reportgui-page", "Page {0} of {1}"), ("reportgui-asking", "Asking the server..."),
    ("setvalue-explain", "Why this value?"),
    # 1.11.0
    ("gui-learn-label", "Learn"),
    ("gui-burn-hint", "Drop an item on Burn to turn it into EMC and learn it (left-click: the whole stack, right-click: one). Drop it on Learn to learn it and keep it."),
    ("error-alreadyknown", "You already know {0}."),
    ("cmdhelp-learn", "Learns the held item without burning it (anything the transmutation table could burn; admins: anything)"),
    ("table-noowner", "Nobody owns this table yet: the next person to open it does. A hopper or chute pointing into it feeds it items to burn into its owner's EMC."),
    ("table-owner", "A hopper or chute pointing into it burns items into {0}'s EMC."),
    ("table-owner-you", "A hopper or chute pointing into it burns items into your EMC."),
    ("table-waiting", "Waiting to burn: {0} x {1}"),
    ("dumpgui-cond-off", "Condition {0} doesn't change the value: DurabilityScalesEmc is off in the server config."),
    ("dumpgui-cond-full", "Condition 100%: as good as new, so nothing comes off (worn clothes are worth less: DurabilityScalesEmc)."),
    ("dumpgui-cond", "Condition {0}: worth {0} of new: {1} x {0} = {2} (DurabilityScalesEmc)"),
    ("dumpgui-burnwith", "Burning this one gives {0} EMC: ProtectContainersWithContents is off, so what's inside counts too."),
])


def lang():
    path = os.path.join(ROOT, "lang", "en.json")
    d = json.load(open(path, encoding="utf-8"), object_pairs_hook=O)
    for k, v in NAMES.items():
        d["item-" + k] = v
    for k, v in DESCS.items():
        d["itemdesc-" + k] = v
    for s in STARS[1:]:
        d["itemdesc-kleinstar-" + s] = DESCS["kleinstar-ein"]
    for c in BAGS:
        for code, slots, _ in BAG_TIERS:
            name = {"alchemicalbag": "Alchemical Bag", "alchemicalbagdark": "Dark Matter Alchemical Bag", "alchemicalbagred": "Red Matter Alchemical Bag"}[code]
            d[f"item-{code}-{c}"] = f"{name} ({c})" if c not in ("white", "plain") else (name if c == "white" else f"{name} (plain)")
            d[f"itemdesc-{code}-{c}"] = f"A {slots}-slot bag. Wear it in a backpack slot." + (
                {"alchemicalbag": " Upgrade it with 4 dark matter for 48 slots.", "alchemicalbagdark": " Upgrade it with 4 red matter for 64 slots; upgrading keeps what's inside.",
                 "alchemicalbagred": ""}[code])
    for m, mn in (("dark", "Dark Matter"), ("red", "Red Matter"), ("gem", "Gem")):
        for p, pn in (("head", "Helmet"), ("body", "Chestplate"), ("legs", "Leggings")):
            d[f"item-matterarmor-{m}-{p}"] = f"{mn} {pn}"
            d[f"itemdesc-matterarmor-{m}-{p}"] = {
                "dark": "Never wears out.",
                "red": "Never wears out. A little lighter on your feet.",
                "gem": "Never wears out. A full set makes you immune to fall and fire damage."}[m]
    for k, v in BLOCK_NAMES.items():
        d["block-" + k] = v
    for k, v in BLOCK_DESCS.items():
        d["blockdesc-" + k] = v
    for k, v in LANG.items():
        d[k] = v
    # Descriptions with the real numbers win over anything older. Long ones get a short tooltip line, and the full
    # text goes into the handbook page (the game reads "<domain>:<item|block>-handbooktext-<domain:code>").
    import descriptions
    for k in [k for k in d if k.startswith("vintagee:item-handbook") or k.startswith("vintagee:block-handbook")]:
        del d[k]
    for kind, texts, short in (("item", descriptions.ITEM, descriptions.SHORT_ITEM), ("block", descriptions.BLOCK, descriptions.SHORT_BLOCK)):
        for k, v in texts.items():
            d[kind + "desc-" + k] = short.get(k, v)
            if k in short:
                d[f"vintagee:{kind}-handbooktitle-vintagee:{k}"] = "How it works"
                d[f"vintagee:{kind}-handbooktext-vintagee:{k}"] = v
    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2, ensure_ascii=False)
        f.write("\n")


PROJECTILE_KINDS = ["lens", "water", "lava", "fire", "arrow"]


def entities():
    """ProjectE's projectiles (lens charges, amulet water/lava, ignition fire, smite arrows): see EntityVintageEProjectile."""
    write("entities/projectile.json", O([
        ("code", "projectile"),
        ("class", "VintageEProjectile"),
        ("variantgroups", [O([("code", "kind"), ("states", PROJECTILE_KINDS)])]),
        ("hitboxSize", O([("x", 0.25), ("y", 0.25)])),
        ("client", O([
            ("renderer", "Shape"),
            # The smite's arrows are arrows (turned to face where they fly, see EntityVintageEProjectile); the rest are orbs.
            ("shapeByType", O([("*-arrow", O([("base", "vintagee:entity/projectile-arrow")])), ("*", O([("base", "vintagee:entity/projectile")]))])),
            ("texturesByType", O([("*-" + k, O([("orb", O([("base", "vintagee:entity/projectile-" + k)]))])) for k in PROJECTILE_KINDS if k != "arrow"]
                                 + [("*-arrow", O([("arrow", O([("base", "vintagee:entity/smitearrow")]))]))])),
            ("size", 1),
            ("behaviors", [O([("code", "interpolateposition")])]),
        ])),
        ("server", O([("behaviors", [])])),
        ("sounds", O()),
    ]))
    orb = box("orb", [6, 0, 6], [10, 4, 10], "#orb")
    orb["glow"] = 255
    for f in orb["faces"].values():
        f["uv"] = [0, 0, 16, 16]  # the whole orb picture on every side (not just its corner)
    write("shapes/entity/projectile.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("orb", "vintagee:entity/projectile-lens")])),
        ("elements", [orb]),
    ]))
    # Archangel's Smite arrow: points along +x (the renderer turns +x towards the flight), centred on the hitbox
    # middle (y = 2 voxels). Flat colours from the 16x16 smitearrow texture: shaft, head, fletching.
    shaft_uv, head_uv, fletch_uv = [1, 1, 3, 3], [5, 1, 7, 3], [9, 1, 11, 3]

    def solid(name, frm, to, uv, glow):
        e = O([("name", name), ("from", frm), ("to", to), ("faces", O((f, O([("texture", "#arrow"), ("uv", uv)])) for f in ("north", "east", "south", "west", "up", "down")))])
        e["glow"] = glow
        return e
    write("shapes/entity/projectile-arrow.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("arrow", "vintagee:entity/smitearrow")])),
        ("elements", [
            solid("shaft", [1.5, 1.7, 7.7], [11.5, 2.3, 8.3], shaft_uv, 120),
            solid("head", [11.5, 1.4, 7.4], [13.5, 2.6, 8.6], head_uv, 200),
            solid("tip", [13.5, 1.75, 7.75], [14.5, 2.25, 8.25], head_uv, 200),
            solid("fletchV", [1.5, 0.9, 7.95], [4.5, 3.1, 8.05], fletch_uv, 160),
            solid("fletchH", [1.5, 1.95, 6.9], [4.5, 2.05, 9.1], fletch_uv, 160),
        ]),
    ]))
    # With ProjectETextures on, projectiles use ProjectE's 16×16 sprites, drawn whole on every face.
    sprite = box("orb", [6, 0, 6], [10, 4, 10], "#orb")
    sprite["glow"] = 255
    for face in sprite["faces"].values():
        face["uv"] = [0, 0, 16, 16]
    write("shapes/entity/projectile-sprite.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("orb", "vintagee:projecte/entity/fireball")])),
        ("elements", [sprite]),
    ]))


if __name__ == "__main__":
    items()
    blocks()
    entities()
    recipes()
    lang()
    print("ProjectE assets written")
