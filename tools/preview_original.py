#!/usr/bin/env python3
"""Draws docs/preview-original.png: the Original look (ProjectE's textures), laid out like docs/preview.png.

usage: python3 tools/preview_original.py

Rings and stones are shown switched on, as they look in game in the Original look.
"""
import os

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
TEX = os.path.join(ROOT, "assets", "vintagee", "textures")

TILES = [
    # items
    "projecte/item/philosophers_stone", "projecte/item/transmutation_tablet",
    "projecte/item/fuels/alchemical_coal", "projecte/item/fuels/mobius", "projecte/item/fuels/aeternalis",
    "projecte/item/matter/dark", "projecte/item/matter/red", "projecte/item/covalence_dust/low",
    "projecte/item/covalence_dust/medium", "projecte/item/covalence_dust/high",
    "item/kleinstar-ein", "item/kleinstar-zwei", "item/kleinstar-drei", "item/kleinstar-vier",
    "item/kleinstar-sphere", "item/kleinstar-omega",
    # rings, stones and amulets (switched on)
    "projecte/item/rings/zero_on", "projecte/item/rings/ignition_on", "projecte/item/rings/harvest_god_on",
    "projecte/item/rings/black_hole_on", "projecte/item/rings/swrg_on1", "projecte/item/rings/arcana_0_on",
    "projecte/item/rings/void_on", "projecte/item/rings/iron_band",
    "projecte/item/rings/body_stone_on", "projecte/item/rings/soul_stone_on", "projecte/item/rings/life_stone_on",
    "projecte/item/rings/evertide_amulet", "projecte/item/rings/volcanite_amulet", "projecte/item/repair_talisman",
    "projecte/item/dense_gem_on", "projecte/item/rings/time_watch_on",
    # gadgets
    "projecte/item/hyperkinetic_lens", "projecte/item/catalytic_lens", "projecte/item/destruction_catalyst",
    "projecte/item/mercurial_eye", "projecte/item/rings/archangel_smite", "projecte/item/tome",
    "projecte/item/divining_rod_3", "projecte/item/alchemy_bags/red",
    # blocks
    "projecte/block/transmutation_stone/top", "projecte/block/collectors/top_3", "projecte/block/collectors/front",
    "projecte/block/relays/top_3", "projecte/derived/condenser_mk1-top", "projecte/derived/alchemical_chest-side",
    "projecte/block/dark_matter_block", "projecte/block/red_matter_block",
]

SIZE = 128   # each picture, as in docs/preview.png
GAP = 8
COLS = 8
BG = (60, 64, 72, 255)


def main():
    rows = (len(TILES) + COLS - 1) // COLS
    sheet = Image.new("RGBA", (COLS * (SIZE + GAP) + GAP, rows * (SIZE + GAP) + GAP), BG)
    for i, rel in enumerate(TILES):
        tex = Image.open(os.path.join(TEX, rel + ".png")).convert("RGBA").resize((SIZE, SIZE), Image.NEAREST)
        sheet.alpha_composite(tex, (GAP + (i % COLS) * (SIZE + GAP), GAP + (i // COLS) * (SIZE + GAP)))
    out = os.path.join(ROOT, "docs", "preview-original.png")
    sheet.convert("RGB").save(out, optimize=True)
    print(f"{len(TILES)} pictures -> {out}")


if __name__ == "__main__":
    main()
