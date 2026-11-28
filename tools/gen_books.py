#!/usr/bin/env python3
"""Project Expansion's books for Vintage-E (1.29.3): the Knowledge Sharing Book and the Basic, Advanced, Master and Arcane
Alchemical Books. Run by tools/gen_expansion.py after tools/gen_flowers.py (it adds to the look maps both write):

    python3 tools/gen_expansion.py

Writes (and rewrites):
  - itemtypes/knowledgebook.json, itemtypes/alchemicalbook.json;
  - recipes/grid/books.json (the Basic Alchemical Book: always there), recipes/grid/books-noexpansion.json (the Knowledge
    Sharing Book with red matter: only while Expansion is off) and recipes/grid/expansion-books.json (the Knowledge
    Sharing Book with violet matter and the Advanced, Master and Arcane books: only while it's on); see
    gen_expansion.manifest;
  - their language lines (only the lines this script owns);
  - textures for the three looks and their entries in the companion look maps.
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
# tier, Project Expansion's EMC a block, Middle cover and accent, New look leather, metal and gem.
TIERS = [
    ("basic", 1000, "3a6a32", "ffd24a", "green", "brass", "emerald"),
    ("advanced", 500, "7a2a24", "ffd24a", "red", "silver", "ruby"),
    ("master", 100, "2a3a7a", "ffd24a", "blue", "gold", "sapphire"),
    ("arcane", 0, "24282c", "ffe680", "black", "electrum", "diamond"),
]
KNOWLEDGE = ("6a3070", "40d0e0", "purple", "silver", "aquamarine")
EXPANSION_TIERS = ["advanced", "master", "arcane"]  # made from the bigger stars: only with Expansion on


def types():
    gx.dump("itemtypes/knowledgebook.json", {
        "code": "knowledgebook",
        "class": "VintageEItemKnowledgeBook",
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/knowledgebook"},
        "materialDensity": 600,
        "attributes": {"handbook": {"groupBy": ["knowledgebook"]}},
    })
    gx.dump("itemtypes/alchemicalbook.json", {
        "code": "alchemicalbook",
        "class": "VintageEItemAlchemicalBook",
        "variantgroups": [{"code": "tier", "states": [t[0] for t in TIERS]}],
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "vintagee": ["*"]},
        "texture": {"base": "item/alchemicalbook-{tier}"},
        "materialDensity": 600,
        "attributesByType": {f"*-{t}": {"emcPerBlock": cost, "canBind": t != "basic"} for t, cost, *_ in TIERS},
    })


def recipes():
    book = ing(G + "book-normal-*")
    gears = ing(G + "gear-temporal", quantity=8)  # Project Expansion's nether star, as in the Final Star Shard
    stone = {**ing(f"{MOD}:philosophersstone"), "consume": False}

    def knowledge(matter):
        return {"ingredientPattern": "VNV,VBV,VNV", "ingredients": {"V": ing(matter), "N": gears, "B": book}, "width": 3, "height": 3,
                "output": ing(f"{MOD}:knowledgebook"), "name": f"{MOD}:knowledgebook"}

    basic = {"ingredientPattern": "HRH,EBP,HRH", "ingredients": {"H": ing(f"{MOD}:covalencedust-high"), "R": ing(f"{MOD}:matter-red"),
                                                                 "E": ing(G + "gear-temporal"), "B": book, "P": stone},
             "width": 3, "height": 3, "output": ing(f"{MOD}:alchemicalbook-basic"), "name": f"{MOD}:alchemicalbook-basic"}
    more = [knowledge(f"{MOD}:matter-violet")]
    prev = "basic"
    for tier, outer, inner in (("advanced", "star-magnum-ein", "star-magnum-omega"), ("master", "star-colossal-ein", "star-colossal-omega"),
                               ("arcane", "finalstarshard", "finalstar")):
        pattern = "SFS,FBF,SFS" if tier == "arcane" else "EOE,OBO,EOE"
        keys = {"S": ing(f"{MOD}:{outer}"), "F": ing(f"{MOD}:{inner}")} if tier == "arcane" else {"E": ing(f"{MOD}:{outer}"), "O": ing(f"{MOD}:{inner}")}
        more.append({"ingredientPattern": pattern, "ingredients": {**keys, "B": ing(f"{MOD}:alchemicalbook-{prev}")}, "width": 3, "height": 3,
                     "output": ing(f"{MOD}:alchemicalbook-{tier}"), "name": f"{MOD}:alchemicalbook-{tier}", "copyAttributesFrom": "B"})
        prev = tier
    without = [knowledge(f"{MOD}:matter-red")]
    gx.dump("recipes/grid/books.json", [basic])
    gx.dump("recipes/grid/books-noexpansion.json", without)
    gx.dump("recipes/grid/expansion-books.json", more)
    return [basic] + without + more


def lang_lines():
    out = {}
    kb = ("Sneak + right-click to write everything you've learned into it, then give it to someone: when they right-click it, they learn "
          "what's written in it. It isn't used up, so it can go from one player to the next (the server can make reading it use it up, as in "
          "Project Expansion); write it again to bring it up to date. Its tooltip says how many things are in it and how many of them you'd "
          "learn. Exact versions (a filled bucket, a gold shield) count against your limit of exact things learned, as usual. Made from violet "
          "matter, 16 temporal gears (Project Expansion uses nether stars) and a book; red matter instead while Project Expansion's tiers are off.")
    out["item-knowledgebook"] = "Knowledge Sharing Book"
    out["itemdesc-knowledgebook"] = kb
    out["vintagee:item-handbooktitle-vintagee:knowledgebook"] = "How it works"
    out["vintagee:item-handbooktext-vintagee:knowledgebook"] = kb
    out["knowledgebook-blank"] = "Blank: sneak + right-click to write what you've learned in it."
    out["knowledgebook-by"] = "Written by {0}: {1} things"
    out["knowledgebook-yours"] = "Your own knowledge: give it to someone else"
    out["knowledgebook-wouldlearn"] = "You'd learn {0} of them"
    out["knowledgebook-wouldlearn-limit"] = "You'd learn {0} of them ({1} exact versions past your limit would be left out)"
    out["knowledgebook-knowall"] = "You know all of it already"
    out["knowledgebook-usedup"] = "Used up when it's read (this server's setting)"
    out["knowledgebook-written"] = "Everything you've learned ({0} things) is written in the book. Whoever reads it learns it all; write it again to bring it up to date."
    out["knowledgebook-self"] = "That's your own knowledge: give the book to someone else."
    out["knowledgebook-noowner"] = "This book is blank. Sneak + right-click to write what you've learned in it."
    out["knowledgebook-gone"] = "Nothing can be read in this book."
    out["knowledgebook-learned"] = "You learned {0} new things from {1}'s book."
    out["knowledgebook-limit-note"] = "{0} exact versions were left out: you're at the server's limit of {1}."
    out["knowledgebook-kept-note"] = "You can pass the book on."
    out["knowledgebook-usedup-note"] = "The book is used up."
    out["knowledgebook-nothing"] = "You learned nothing new from {0}'s book."

    names = {"basic": "Basic", "advanced": "Advanced", "master": "Master", "arcane": "Arcane"}
    cheaper = {"basic": "", "advanced": " (half the Basic book's price)", "master": " (a tenth of the Basic book's price)", "arcane": ""}
    made = {
        "basic": "Made from high covalence dust, red matter, a temporal gear (Project Expansion uses an ender pearl), a book and the philosopher's stone, which you keep.",
        "advanced": "Made from the Basic book between Magnum Star Eins and Omegas (they're used up with what they hold).",
        "master": "Made from the Advanced book between Colossal Star Eins and Omegas (they're used up with what they hold).",
        "arcane": "Made from the Master book between Final Stars and Final Star Shards.",
    }
    for tier, cost, *_ in TIERS:
        price = "for free" if cost == 0 else f"for {gx.n(cost)} EMC a block of distance{cheaper[tier]}"
        bind = (" Sneak + right-click to bind it to you: your places are then kept with you, not in the book, so a new book of yours has them too; "
                "sneak + right-click again to unbind it.") if tier != "basic" else ""
        text = (f"Right-click to open it: save the place you're standing at under a name, then go back to any saved place {price}, paid from your EMC "
                f"(and Klein stars). Going somewhere remembers where you were, so Back takes you there.{bind} It doesn't work during a temporal storm "
                f"(the server can allow it), nor between dimensions. {made[tier]}")
        out[f"item-alchemicalbook-{tier}"] = f"{names[tier]} Alchemical Book"
        out[f"itemdesc-alchemicalbook-{tier}"] = text
        out[f"vintagee:item-handbooktitle-vintagee:alchemicalbook-{tier}"] = "How it works"
        out[f"vintagee:item-handbooktext-vintagee:alchemicalbook-{tier}"] = text
    # The book's window and its messages (GuiDialogAlchemicalBook, AlchemicalBooks).
    out["abook-emc"] = "Your EMC: {0}"
    out["abook-bound"] = "Bound to {0}: these places are kept with them."
    out["abook-own"] = "These places are kept in the book."
    out["abook-perblock"] = "{0} EMC a block"
    out["abook-free"] = "Free"
    out["abook-name"] = "Name of this place"
    out["abook-save"] = "Save here"
    out["abook-go"] = "Go"
    out["abook-delete"] = "Delete"
    out["abook-back"] = "Back ({0} EMC)"
    out["abook-noback"] = "Back: nowhere yet"
    out["abook-none"] = "No places saved yet: type a name and press Save here."
    out["abook-row"] = "{0}: {1} blocks, {2} EMC"
    out["abook-row-otherdim"] = "{0}: in another dimension"
    out["abook-page"] = "{0} / {1}"
    out["abook-saved"] = "Saved this place as \"{0}\"."
    out["abook-deleted"] = "Deleted \"{0}\"."
    out["abook-went"] = "Teleported to \"{0}\" for {1} EMC."
    out["abook-wentback"] = "Teleported back for {0} EMC."
    out["abook-err-noname"] = "Give the place a name first."
    out["abook-err-duplicate"] = "There's already a place called \"{0}\"."
    out["abook-err-full"] = "The book is full ({0} places): delete one first."
    out["abook-err-notfound"] = "There's no place called \"{0}\" any more."
    out["abook-err-noback"] = "There's nowhere to go back to."
    out["abook-err-dimension"] = "That place is in another dimension: the book can't take you there."
    out["abook-err-storm"] = "The book doesn't work during a temporal storm."
    out["abook-err-emc"] = "That costs {0} EMC, and you have {1}."
    out["abook-err-notyours"] = "This book is bound to {0}: only they can use it."
    out["abook-err-nobook"] = "You're no longer holding the book."
    out["abook-err-claim"] = "You can't save a place here."
    out["abook-nowbound"] = "This book is now bound to you: its places are kept with you, not in the book."
    out["abook-unbound"] = "This book is no longer bound to you: it has its own places again."
    out["abook-bound-info"] = "Bound to {0}"
    out["abook-places-info"] = "{0} places saved"
    # "Burn what I mine" on the tablet (TabletMining, GuiDialogTransmutation) and the doctor's lines for it.
    out["mine-off"] = "Burn what I mine: off"
    out["mine-on"] = "Burn what I mine: on"
    out["mine-hint"] = ("While you carry this tablet with this on, what drops from the blocks you break (and what your tools break around them, "
                        "and the trees your axe fells) is burned into your EMC and learned, through the filter above: only these, or all except "
                        "these; with nothing in the filter, everything that has a value. What spills from a container you break is never burned.")
    out["diag-mach-mining-none"] = "Burn what I mine: nobody in the game has it on."
    out["diag-mach-mining-users"] = "Burn what I mine is on for: {0} ({1} items burned this way since the server started)."
    out["diag-mach-mining-nohook"] = "Burn what I mine is switched off for safety: the game's container spill couldn't be hooked ({0})."
    out["diag-mach-mining-nohook-why"] = ("Without that hook a broken chest's contents could be burned along with the chest, so the tablet burns nothing a "
                                          "player mines. Copy this and report it to Vintage-E's author; the magnet still works.")
    return out


def textures_and_looks():
    written = []
    kc, ka, kl, km, kg = KNOWLEDGE
    written.append(gt.save(gt.book(gt.rgba(kc), gt.rgba(ka)), "item/knowledgebook"))
    for tier, cost, cover, accent, *_ in TIERS:
        written.append(gt.save(gt.book(gt.rgba(cover), gt.rgba(accent)), f"item/alchemicalbook-{tier}"))
    for code in ["knowledgebook"] + [t[0] for t in TIERS]:
        written.append(gx.save_png(gx.vendor(f"book-{code}"), f"projecte/expansion/book-{code}"))

    # Original look: Project Expansion's sprites.
    path = os.path.join(gx.ROOT, "config", "vintagee-projecte-look-expansion.json")
    orig = json.load(open(path))
    orig["textures"][f"{MOD}:item/knowledgebook"] = f"{MOD}:projecte/expansion/book-knowledgebook"
    for tier, *_ in TIERS:
        orig["textures"][f"{MOD}:item/alchemicalbook-{tier}"] = f"{MOD}:projecte/expansion/book-{tier}"
    gx.dump("config/vintagee-projecte-look-expansion.json", orig)

    # New look: the Tome of Knowledge's book model in the game's leathers, metals and gems.
    book = f"{MOD}:newlook/item/book"
    leather = lambda c: G + "block/leather/" + c
    plate = lambda m: G + "block/metal/plate/" + m
    gem = lambda m: G + "block/stone/gem/" + m
    path = os.path.join(gx.ROOT, "config", "vintagee-new-look-expansion.json")
    new = json.load(open(path))
    new["overrides"] = [o for o in new["overrides"] if not o["code"].startswith((f"{MOD}:knowledgebook", f"{MOD}:alchemicalbook"))]
    new["overrides"].append(gn.item("knowledgebook", book, {"leather": leather(kl), "metal": plate(km), "gem": gem(kg)}, "knowledge sharing book"))
    for tier, cost, cover, accent, l, m, g in TIERS:
        new["overrides"].append(gn.item(f"alchemicalbook-{tier}", book, {"leather": leather(l), "metal": plate(m), "gem": gem(g)}, f"{tier} alchemical book"))
    gx.dump("config/vintagee-new-look-expansion.json", new)
    return written


def main():
    types()
    rs = recipes()
    entries = lang_lines()
    gx.write_lang(entries)
    tex = textures_and_looks()
    print(f"books: {len(rs)} recipes, {len(entries)} language lines, {len(tex)} textures")


if __name__ == "__main__":
    main()
