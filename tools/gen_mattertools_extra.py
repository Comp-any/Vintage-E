#!/usr/bin/env python3
"""Writes the dark/red matter versions of the Vintage Story tools ProjectE never had (knife, saw, chisel, scythe,
prospecting pick, smithing hammer, spear, bow) as assets/vintagee/itemtypes/matter<tool>.json, plus the thrown
spear entity.

Each reuses the game's own model, class and settings (so it works like the steel tool: the knife butchers and
harvests, the hammer smiths, the chisel chisels, the spear throws, the bow shoots) with matter textures, and
the VintageEMatterTool behaviour so it never wears out. Transforms and attributes come from the game's own
item files, summarised by the server test (analysis/vanilla-tools.json on a ci-analysis branch).

usage: gen_mattertools_extra.py <vanilla-tools.json>
"""
import json
import os
import re
import sys
from collections import OrderedDict as O

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee")
HEAD = {"dark": "vintagee:item/tool/material/darkmatter", "red": "vintagee:item/tool/material/redmatter"}
HANDLE = {"dark": "game:block/wood/debarked/ebony", "red": "vintagee:item/tool/material/darkmatter"}
BAND = {"dark": "vintagee:item/tool/material/redmatter", "red": "vintagee:item/tool/material/darkmatter"}
STRING = "game:item/resource/string"
MATS = ("dark", "red")

# Our code -> the vanilla item (and one of its codes) it copies.
TOOLS = O([
    ("knife", ("knife", "knife-generic-steel")),
    ("saw", ("saw", "saw-steel")),
    ("chisel", ("chisel", "chisel-steel")),
    ("scythe", ("scythe", "scythe-steel")),
    ("propick", ("prospectingpick", "prospectingpick-steel")),
    ("smithinghammer", ("hammer", "hammer-steel")),
    ("spear", ("spear", "spear-generic-steel")),
    ("bow", ("bow", "bow-recurve")),
])


def matches(key, code):
    if key.startswith("@"):
        return re.fullmatch(key[1:], code) is not None
    return re.fullmatch(re.escape(key).replace(r"\*", ".*"), code) is not None


def by_type(d, code):
    """The value a *ByType dict gives for this code (first matching key, as the game does)."""
    for k, v in d.items():
        if matches(k, code):
            return v
    return None


def pick(item, name, code):
    """item[name] or item[nameByType][code] (case-insensitive key names)."""
    for k, v in item.items():
        if k.lower() == name.lower():
            return v
        if k.lower() == (name + "bytype").lower() and isinstance(v, dict):
            return by_type(v, code)
    return None


def textures(tool, mat):
    head, handle, band = HEAD[mat], HANDLE[mat], BAND[mat]
    return {
        "knife": O([("material", head), ("handle", handle)]),
        "saw": O([("metal", head), ("wood", handle)]),
        "chisel": O([("metal", head), ("string", STRING)]),
        "scythe": O([("metal", head), ("handle", handle), ("string", STRING)]),
        "propick": O([("metal", head), ("handle", handle), ("string", STRING)]),
        "smithinghammer": O([("metal", head), ("wood", handle)]),
        "spear": O([("iron", head), ("handle", handle), ("string", STRING)]),
        "bow": O([("aged", head), ("bone", band), ("iron", band), ("leather", "game:block/leather/plain"), ("string", STRING)]),
    }[tool]


SHAPE = {
    "knife": O([("base", "game:item/tool/knife/copper")]),
    "saw": O([("base", "game:item/tool/saw")]),
    "chisel": O([("base", "game:item/tool/chisel")]),
    "scythe": O([("base", "game:item/tool/scythe")]),
    "propick": O([("base", "game:item/tool/prospectingpick")]),
    "smithinghammer": O([("base", "game:item/tool/hammer")]),
    "spear": O([("base", "game:item/tool/spear/metal-iron")]),
    "bow": O([("base", "game:item/tool/bow/recurve"), ("alternates", [O([("base", f"game:item/tool/bow/recurve-charge{i}")]) for i in (1, 2, 3)])]),
}
CLASS = {"knife": "ItemKnife", "chisel": "ItemChisel", "scythe": "ItemScythe", "propick": "ItemProspectingPick",
         "smithinghammer": "ItemHammer", "spear": "ItemSpear", "bow": "ItemBow"}
TOOLTYPE = {"knife": "knife", "saw": "saw", "chisel": "chisel", "scythe": "scythe", "propick": "pickaxe",
            "smithinghammer": "hammer", "spear": "spear", "bow": "bow"}
TAGS = {
    "knife": ["weapon", "weapon-melee", "tool-knife", "tool-trimming", "tool"],
    "saw": ["tool", "tool-saw"],
    "chisel": ["tool", "tool-chisel"],
    "scythe": ["tool", "tool-scythe"],
    "propick": ["tool", "tool-pickaxe", "tool-prospectingpick"],
    "smithinghammer": ["tool", "tool-hammer"],
    "spear": ["weapon", "weapon-melee", "weapon-spear"],
    "bow": ["weapon", "weapon-ranged", "weapon-bow"],
}
ANIMS = ["heldTpHitAnimation", "heldTpUseAnimation", "heldTpIdleAnimation", "heldRightReadyAnimation"]
MINING = {
    "knife": {"dark": dict(plant=6, leaves=4), "red": dict(plant=9, leaves=6)},
    "saw": {"dark": dict(wood=9, leaves=6), "red": dict(wood=14, leaves=9)},
    "propick": {"dark": dict(stone=9, ore=9, metal=9), "red": dict(stone=14, ore=14, metal=14)},
}
ATTACK = {"knife": (5, 6.5), "saw": (3.5, 4.5), "chisel": (3, 4), "scythe": (4, 5), "propick": (3.5, 4.5),
          "smithinghammer": (4, 5), "spear": (8, 10.5), "bow": (2, 2.5)}
# Extra attributes, beyond the transforms and handbook grouping.
EXTRA_ATTRS = {
    "knife": O([("heldItemPitchFollow", 1), ("knifeHitBlockAnimation", "knifecut"), ("knifeHitEntityAnimation", "knifestab")]),
    "chisel": O([("heldItemPitchFollow", 1), ("alwaysPlayHeldReady", True), ("rememberToolModeWhenBroken", True)]),
    "scythe": O([("heldItemPitchFollow", 0.5),
                 ("codePrefixes", ["crop", "tallgrass", "frostedtallgrass", "tallplant-coopersreed-land-normal", "tallplant-papyrus-land-normal",
                                   "tallplant-tule-land-normal", "tallplant-coopersreed-water-normal", "tallplant-papyrus-water-normal",
                                   "tallplant-coopersreed-ice-normal", "flower-horsetail"]),
                 ("disallowedSuffixes", ["snow2", "snow3"]), ("rememberToolModeWhenBroken", True)]),
    "propick": O([("heldItemPitchFollow", 0.9), ("slotRefillIdentifier", "propick"), ("rememberToolModeWhenBroken", True)]),
    "smithinghammer": O([("heldItemPitchFollow", 0.9), ("rememberToolModeWhenBroken", True)]),
    "spear": O([("heldItemPitchFollow", 1), ("rangedWeaponsSpeedMul", 0.5)]),
    "bow": O([("heldItemPitchFollow", 1), ("aimAnimation", "bowaimrecurve")]),
}
EXTRA_ATTRS_BY_MAT = {
    "spear": {"dark": O([("damage", 9), ("damageTier", 3), ("spearEntityCode", "vintagee:matterspear-dark")]),
              "red": O([("damage", 12), ("damageTier", 4), ("spearEntityCode", "vintagee:matterspear-red")])},
    "bow": {"dark": O([("damage", 5.5), ("damageTier", 1), ("statModifier", O([("rangedWeaponsAcc", 0.35)]))]),
            "red": O([("damage", 7), ("damageTier", 2), ("statModifier", O([("rangedWeaponsAcc", 0.45)]))])},
}
DAMAGEDBY = {"knife": ["blockbreaking", "attacking"], "saw": ["blockbreaking", "attacking"], "scythe": ["blockbreaking"],
             "propick": ["blockbreaking", "attacking"], "smithinghammer": ["blockbreaking", "attacking"], "spear": ["attacking"]}
VANILLA_BEHAVIORS_KEEP = {"AnimationAuthoritative", "EntityDeconstructTool"}


def ground_storable(vanilla, code):
    for b in vanilla.get("behaviors") or by_type(vanilla.get("behaviorsByType") or {}, code) or []:
        if b.get("name") == "GroundStorable":
            props = O(b.get("properties") or {})
            if "wallOffYByType" in props:
                props["wallOffY"] = by_type(props.pop("wallOffYByType"), code)
            return O([("name", "GroundStorable"), ("properties", props)])
    return None


def build(tool, vanilla, vcode):
    d = O()
    d["code"] = "matter" + tool
    d["variantgroups"] = [O([("code", "material"), ("states", list(MATS))])]
    if tool in CLASS:
        d["class"] = CLASS[tool]
    d["tags"] = TAGS[tool]
    if tool in DAMAGEDBY:
        d["damagedby"] = DAMAGEDBY[tool]
    d["tool"] = TOOLTYPE[tool]
    beh = []
    gs = ground_storable(vanilla, vcode)
    if gs:
        beh.append(gs)
    for b in vanilla.get("behaviors") or by_type(vanilla.get("behaviorsByType") or {}, vcode) or []:
        if b.get("name") in VANILLA_BEHAVIORS_KEEP:
            beh.append(b)
    beh.append(O([("name", "VintageEMatterTool"), ("properties", O([("maxAreaRadius", 0)]))]))
    d["behaviors"] = beh
    d["maxstacksize"] = 1
    d["creativeinventory"] = O([("general", ["*"]), ("tools", ["*"]), ("vintagee", ["*"])])
    d["shape"] = SHAPE[tool]
    d["texturesByType"] = O((f"*-{m}", O((k, O([("base", v)])) for k, v in textures(tool, m).items())) for m in MATS)
    for a in ANIMS:
        v = pick(vanilla, a, vcode)
        if v is not None:
            d[a] = v
    if tool == "spear":
        d["attackRange"] = 3.5
    for t in ("guiTransform", "tpHandTransform", "groundTransform", "fpHandTransform"):
        v = pick(vanilla, t, vcode)
        if v is not None:
            d[t] = v
    attrs = O()
    sel = vanilla.get("attributes(selected)") or {}
    for k, v in sel.items():
        name = k[:-6] if k.endswith("ByType") else k
        val = by_type(v, vcode) if k.endswith("ByType") and isinstance(v, dict) else v
        if val is None or name in ("onAntlerMountTransform", "onTongTransform", "onMetalTongTransform", "inForgeTransform", "aimAnimation"):
            continue
        if name == "handbook":
            val = O([("groupBy", ["matter" + tool + "-*"])])
        attrs[name] = val
    attrs.setdefault("handbook", O([("groupBy", ["matter" + tool + "-*"])]))
    attrs.setdefault("attachableToEntity", O([("categoryCode", "toolholding")])) if tool not in ("bow", "spear") else None
    for k, v in EXTRA_ATTRS.get(tool, {}).items():
        attrs[k] = v
    if tool in EXTRA_ATTRS_BY_MAT:
        d["attributesByType"] = O((f"*-{m}", O(list(attrs.items()) + list(EXTRA_ATTRS_BY_MAT[tool][m].items()))) for m in MATS)
    else:
        d["attributes"] = attrs
    d["tooltierByType"] = O([("*-dark", 5), ("*-red", 6)])
    if tool in MINING:
        d["miningspeedByType"] = O((f"*-{m}", MINING[tool][m]) for m in MATS)
    d["attackpowerByType"] = O([("*-dark", ATTACK[tool][0]), ("*-red", ATTACK[tool][1])])
    d["materialDensity"] = 9000
    return d


def spear_entity():
    d = O()
    d["code"] = "matterspear"
    d["class"] = "EntityProjectile"
    d["tags"] = ["inanimate", "projectile"]
    d["variantgroups"] = [O([("code", "material"), ("states", list(MATS))])]
    d["hitboxSize"] = O([("x", 0.125), ("y", 0.125)])
    d["attributes"] = O([("isProjectile", True)])
    phys = O([("code", "passivephysics"), ("groundDragFactor", 1), ("airDragFactor", 0.25), ("gravityFactor", 0.75)])
    d["client"] = O([
        ("renderer", "Shape"),
        ("shape", O([("base", "game:item/tool/spear/metal-iron"), ("offsetX", -0.8125)])),
        ("texturesByType", O((f"*-{m}", O((k, O([("base", v)])) for k, v in textures("spear", m).items())) for m in MATS)),
        ("behaviors", [phys, O([("code", "interpolateposition")])]),
    ])
    d["server"] = O([("behaviors", [phys, O([("code", "despawn"), ("minSeconds", 600)]), O([("code", "entitystatetags")])])])
    d["sounds"] = O()
    return d


def main():
    vanilla = {it.get("code"): it for it in json.load(open(sys.argv[1]))}
    for tool, (vname, vcode) in TOOLS.items():
        d = build(tool, vanilla[vname], vcode)
        out = os.path.join(ROOT, "itemtypes", "matter" + tool + ".json")
        with open(out, "w") as f:
            json.dump(d, f, indent=2)
            f.write("\n")
        print("wrote", out)
    out = os.path.join(ROOT, "entities", "matterspear.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w") as f:
        json.dump(spear_entity(), f, indent=2)
        f.write("\n")
    print("wrote", out)


if __name__ == "__main__":
    main()
