#!/usr/bin/env python3
"""Lists the game's textures and shapes for the server test's results (texture-list.txt, shape-list.txt).

usage: ci_textures.py <game assets dir> <output dir>

Each texture line is "domain:path  WxH  #rrggbb" (the average colour of its opaque pixels), so the "New" look's
models can be written against textures that really exist, and their colours checked, without copying any of them.
"""
import os
import sys

from PIL import Image

assets, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

tex_lines, shape_lines = [], []
for domain in sorted(os.listdir(assets)):
    for kind, lines in (("textures", tex_lines), ("shapes", shape_lines)):
        root = os.path.join(assets, domain, kind)
        if not os.path.isdir(root):
            continue
        for dirpath, _, files in os.walk(root):
            for name in sorted(files):
                p = os.path.join(dirpath, name)
                rel = os.path.relpath(p, root).replace(os.sep, "/")
                if kind == "textures" and name.endswith(".png"):
                    try:
                        im = Image.open(p).convert("RGBA")
                        px = [c for c in im.getdata() if c[3] > 128]
                        n = max(1, len(px))
                        avg = tuple(sum(c[i] for c in px) // n for i in range(3))
                        lines.append(f"{domain}:{rel[:-4]}  {im.width}x{im.height}  #{avg[0]:02x}{avg[1]:02x}{avg[2]:02x}")
                    except Exception as e:  # noqa: BLE001
                        lines.append(f"{domain}:{rel[:-4]}  error {e}")
                elif kind == "shapes" and name.endswith(".json"):
                    lines.append(f"{domain}:{rel[:-5]}")

with open(os.path.join(out, "texture-list.txt"), "w") as f:
    f.write("\n".join(sorted(tex_lines)) + "\n")
with open(os.path.join(out, "shape-list.txt"), "w") as f:
    f.write("\n".join(sorted(shape_lines)) + "\n")
print(len(tex_lines), "textures,", len(shape_lines), "shapes")
