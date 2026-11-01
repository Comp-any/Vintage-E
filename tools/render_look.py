#!/usr/bin/env python3
"""Renders Vintage Story shape files to a contact sheet, to look at the "New" look's models without the game.

usage: render_look.py <look map json> <output png> [--game-assets DIR] [--texture-list FILE] [--size N]

Every override in the look map that names a shape (or a cube's six faces) is drawn the way the game's inventory
shows a block: turned 45 degrees and tilted, lit from above. Textures are looked up in this mod's assets, then in
the game's assets (the server test passes its own install), and otherwise drawn in the average colour from
texture-list.txt (written by tools/ci_textures.py), so a preview can be made in a sandbox without the game.
"""
import json
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
MOD_ASSETS = os.path.join(HERE, "..", "assets")


def load_json(path):
    text = open(path, encoding="utf-8-sig").read()
    text = re.sub(r"^\s*//.*$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return json.loads(text)


class Textures:
    def __init__(self, game_assets=None, texture_list=None):
        self.game = game_assets
        self.avg = {}
        self.cache = {}
        self.missing = set()
        if texture_list and os.path.exists(texture_list):
            for line in open(texture_list):
                parts = line.split()
                if len(parts) >= 3 and parts[2].startswith("#"):
                    self.avg[parts[0]] = parts[2]

    def get(self, loc):
        if loc in self.cache:
            return self.cache[loc]
        domain, path = loc.split(":", 1) if ":" in loc else ("game", loc)
        cands = [os.path.join(MOD_ASSETS, domain, "textures", path + ".png")]
        if self.game:
            for d in ([domain] if domain != "game" else ["survival", "game", "creative"]):
                cands.append(os.path.join(self.game, d, "textures", path + ".png"))
        img = None
        for c in cands:
            if os.path.exists(c):
                img = np.asarray(Image.open(c).convert("RGBA")).astype(np.float32) / 255
                break
        if img is None:
            key = "game:" + path if domain == "game" else loc
            col = self.avg.get(key)
            if col is None:
                self.missing.add(loc)
                col = "#ff00ff"
            rgb = [int(col[i:i + 2], 16) / 255 for i in (1, 3, 5)]
            img = np.ones((16, 16, 4), np.float32)
            img[..., :3] = rgb
            # A faint grain, so flat colours still read as surfaces.
            rng = np.random.default_rng(abs(hash(loc)) % 2**32)
            img[..., :3] *= 0.9 + 0.2 * rng.random((16, 16, 1))
        self.cache[loc] = img
        return img


def rot_matrix(rx, ry, rz):
    rx, ry, rz = map(math.radians, (rx, ry, rz))
    X = np.array([[1, 0, 0], [0, math.cos(rx), -math.sin(rx)], [0, math.sin(rx), math.cos(rx)]])
    Y = np.array([[math.cos(ry), 0, math.sin(ry)], [0, 1, 0], [-math.sin(ry), 0, math.cos(ry)]])
    Z = np.array([[math.cos(rz), -math.sin(rz), 0], [math.sin(rz), math.cos(rz), 0], [0, 0, 1]])
    return X @ Y @ Z


FACE_LIGHT = {"up": 1.0, "north": 0.8, "south": 0.8, "east": 0.65, "west": 0.65, "down": 0.5}


def face_corners(f, t, face):
    """Corners (top-left, top-right, bottom-right, bottom-left as seen from outside) of one face of a box."""
    x0, y0, z0 = f
    x1, y1, z1 = t
    return {
        "north": [(x1, y1, z0), (x0, y1, z0), (x0, y0, z0), (x1, y0, z0)],
        "south": [(x0, y1, z1), (x1, y1, z1), (x1, y0, z1), (x0, y0, z1)],
        "east": [(x1, y1, z1), (x1, y1, z0), (x1, y0, z0), (x1, y0, z1)],
        "west": [(x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)],
        "up": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        "down": [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)],
    }[face]


def collect_quads(shape, texmap, tw, th):
    """Every textured face of a shape in world coordinates (0-16), with its texture, UVs and light."""
    quads = []

    def walk(el, offset, parent_m):
        f = np.add(el.get("from", [0, 0, 0]), offset)
        t = np.add(el.get("to", [0, 0, 0]), offset)
        origin = np.add(el.get("rotationOrigin", [0, 0, 0]), offset)
        m_local = rot_matrix(el.get("rotationX", 0), el.get("rotationY", 0), el.get("rotationZ", 0))

        def xf(p, m=m_local, o=origin):
            return parent_m(m @ (np.asarray(p, float) - o) + o)

        for face, fd in (el.get("faces") or {}).items():
            if not fd or fd.get("enabled") is False or "texture" not in fd:
                continue
            code = fd["texture"].lstrip("#")
            loc = texmap.get(code)
            if loc is None:
                continue
            corners = [xf(p) for p in face_corners(f, t, face)]
            uv = fd.get("uv")
            if uv is None:
                uv = [0, 0, 16, 16]
            u0, v0, u1, v1 = uv
            uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            r = int(fd.get("rotation", 0)) // 90 % 4
            uvs = uvs[-r:] + uvs[:-r] if r else uvs
            uvs = [(u / tw, v / th) for u, v in uvs]
            quads.append((corners, loc, uvs, FACE_LIGHT[face]))
        for child in el.get("children") or []:
            walk(child, f, lambda p, m=m_local, o=origin: parent_m(m @ (np.asarray(p, float) - o) + o))

    for el in shape.get("elements", []):
        walk(el, np.zeros(3), lambda p: p)
    return quads


def cube_shape(textures):
    faces = {k: {"texture": "#" + k, "uv": [0, 0, 16, 16]} for k in ("north", "south", "east", "west", "up", "down")}
    return {"textureWidth": 16, "textureHeight": 16, "elements": [{"from": [0, 0, 0], "to": [16, 16, 16], "faces": faces}]}


def render(quads, tex, size, view):
    """Draws quads with a z-buffer. view: (yaw, pitch, scale, (cx, cy, cz)) in shape units."""
    yaw, pitch, scale, centre = view
    m = rot_matrix(pitch, 0, 0) @ rot_matrix(0, yaw, 0)
    ss = 2
    W = size * ss
    img = np.zeros((W, W, 4), np.float32)
    zbuf = np.full((W, W), -1e9, np.float32)
    k = W / 16 * scale
    for corners, loc, uvs, light in quads:
        pts = []
        for p in corners:
            q = m @ (np.asarray(p, float) - centre)
            pts.append((W / 2 + q[0] * k, W / 2 - q[1] * k, q[2]))
        texture = tex.get(loc)
        h, w = texture.shape[:2]
        for tri in ((0, 1, 2), (0, 2, 3)):
            P = np.array([pts[i] for i in tri])
            UV = np.array([uvs[i] for i in tri])
            xmin, xmax = int(max(0, math.floor(P[:, 0].min()))), int(min(W - 1, math.ceil(P[:, 0].max())))
            ymin, ymax = int(max(0, math.floor(P[:, 1].min()))), int(min(W - 1, math.ceil(P[:, 1].max())))
            if xmin > xmax or ymin > ymax:
                continue
            (x0, y0, _), (x1, y1, _), (x2, y2, _) = P
            den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
            if abs(den) < 1e-9:
                continue
            ys, xs = np.mgrid[ymin:ymax + 1, xmin:xmax + 1] + 0.5
            a = ((y1 - y2) * (xs - x2) + (x2 - x1) * (ys - y2)) / den
            b = ((y2 - y0) * (xs - x2) + (x0 - x2) * (ys - y2)) / den
            c = 1 - a - b
            inside = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
            if not inside.any():
                continue
            z = a * P[0, 2] + b * P[1, 2] + c * P[2, 2]
            u = a * UV[0, 0] + b * UV[1, 0] + c * UV[2, 0]
            v = a * UV[0, 1] + b * UV[1, 1] + c * UV[2, 1]
            tx = np.clip((np.mod(u, 1.0000001) * w).astype(int), 0, w - 1)
            ty = np.clip((np.mod(v, 1.0000001) * h).astype(int), 0, h - 1)
            # UVs of exactly 1 should sample the last texel, not wrap to the first.
            tx = np.where(u >= 0.9999, np.clip((u * w).astype(int) - (u * w % 1 == 0), 0, w - 1) % w, tx)
            ty = np.where(v >= 0.9999, np.clip((v * h).astype(int) - (v * h % 1 == 0), 0, h - 1) % h, ty)
            col = texture[ty, tx]
            sub_z = zbuf[ymin:ymax + 1, xmin:xmax + 1]
            draw = inside & (col[..., 3] > 0.5) & (z > sub_z)
            sub_img = img[ymin:ymax + 1, xmin:xmax + 1]
            sub_img[draw, :3] = col[draw, :3] * light
            sub_img[draw, 3] = 1
            sub_z[draw] = z[draw]
    out = Image.fromarray((img * 255).astype(np.uint8), "RGBA")
    return out.resize((size, size), Image.LANCZOS)


def bounds(quads):
    pts = np.array([p for q in quads for p in q[0]])
    return pts.min(0), pts.max(0)


def jobs_from_map(path):
    root = load_json(path)
    for o in root.get("overrides", []):
        if not o.get("preview", True):
            continue
        label = o.get("label") or o["code"].split(":")[-1]
        tex = {k: v for k, v in (o.get("textures") or {}).items()}
        if o.get("shape"):
            yield label, o["shape"], tex, o.get("kind", "block")
        elif o.get("cube"):
            yield label, None, tex, "block"


def main():
    args = sys.argv[1:]
    opts = {"--game-assets": None, "--texture-list": None, "--size": "160", "--only": None, "--missing": None}
    pos = []
    i = 0
    while i < len(args):
        if args[i] in opts:
            opts[args[i]] = args[i + 1]
            i += 2
        else:
            pos.append(args[i])
            i += 1
    map_path, out_path = pos
    size = int(opts["--size"])
    tex = Textures(opts["--game-assets"], opts["--texture-list"])
    tiles = []
    for label, shape_loc, texmap, kind in jobs_from_map(map_path):
        if opts["--only"] and not re.search(opts["--only"], label):
            continue
        if shape_loc:
            domain, p = shape_loc.split(":", 1)
            shape = load_json(os.path.join(MOD_ASSETS, domain, "shapes", p + ".json"))
        else:
            shape = cube_shape(texmap)
        full = dict(shape.get("textures") or {})
        full.update(texmap)
        quads = collect_quads(shape, full, shape.get("textureWidth", 16), shape.get("textureHeight", 16))
        if not quads:
            continue
        lo, hi = bounds(quads)
        if kind == "block":
            view = (45, 30, 0.62, np.array([8, 8, 8]))
        else:
            ext = float(max(hi - lo)) or 1
            view = (45, 30, 0.95 * 16 / ext / 1.6, (lo + hi) / 2)
        tiles.append((label, render(quads, tex, size, view)))
    cols = 6
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * size, rows * (size + 16)), (58, 52, 44, 255))
    d = ImageDraw.Draw(sheet)
    for n, (label, im) in enumerate(tiles):
        x, y = n % cols * size, n // cols * (size + 16)
        sheet.alpha_composite(im, (x, y))
        d.text((x + 4, y + size + 2), label[:26], fill=(230, 220, 200, 255))
    sheet.save(out_path)
    if tex.missing:
        print("textures not found:", ", ".join(sorted(tex.missing)))
    if opts["--missing"]:
        with open(opts["--missing"], "w") as f:
            f.write("".join(m + "\n" for m in sorted(tex.missing)) or "none\n")
    print(len(tiles), "previews ->", out_path)


if __name__ == "__main__":
    main()
