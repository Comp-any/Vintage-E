#!/usr/bin/env python3
"""Generates the "New" look: models (shapes) and textures that fit Vintage Story's own style, and the map that
tells the mod which block/item uses which (assets/vintagee/config/vintagee-new-look.json).

usage: python3 tools/gen_newlook.py

The models are built from the game's own materials wherever there is one (planks, metal plates, polished stone,
gems, linen, leather, paper), referenced by path, so nothing from the game is copied into the mod. The few
textures the game has no equivalent for (dark and red matter, the transmutation circle, alchemical fuels, covalence
dust) are generated here in the same muted, noisy 32x32 style as the game's.

Server setting Look = "New" applies the map at start-up (see src/Assets/ProjectELook.cs).
"""
import json
import math
import os

import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee")
TEX = os.path.join(ROOT, "textures", "newlook")
SHAPES = os.path.join(ROOT, "shapes", "newlook")
MAP = os.path.join(ROOT, "config", "vintagee-new-look.json")


# ----------------------------------------------------------------------------------------------- textures

def rng(seed):
    return np.random.default_rng(seed)


def value_noise(size, cells, seed):
    """Smooth tileable noise in 0..1."""
    r = rng(seed)
    grid = r.random((cells, cells))
    xs = np.arange(size) / size * cells
    x0 = np.floor(xs).astype(int)
    f = xs - x0
    f = f * f * (3 - 2 * f)
    x1 = (x0 + 1) % cells
    a = grid[np.ix_(x0, x0)]
    b = grid[np.ix_(x0, x1)]
    c = grid[np.ix_(x1, x0)]
    d = grid[np.ix_(x1, x1)]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fractal(size, seed, octaves=((2, 0.5), (4, 0.3), (8, 0.2))):
    n = sum(value_noise(size, c, seed + i * 17) * w for i, (c, w) in enumerate(octaves))
    return (n - n.min()) / (np.ptp(n) or 1)


def ramp(t, stops):
    """Maps 0..1 onto a list of (position, (r, g, b)) colour stops."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    pos = [p for p, _ in stops]
    cols = np.array([c for _, c in stops], float)
    for ch in range(3):
        out[..., ch] = np.interp(t, pos, cols[:, ch])
    return out


def save(img, rel):
    path = os.path.join(TEX, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    arr = np.clip(img, 0, 255).astype(np.uint8)
    if arr.shape[-1] == 3:
        arr = np.concatenate([arr, np.full(arr.shape[:2] + (1,), 255, np.uint8)], -1)
    Image.fromarray(arr, "RGBA").save(path)
    return "vintagee:newlook/" + rel


def veins(size, seed, count=3):
    """Thin wandering lines (0..1), like the veins in the game's polished stone."""
    r = rng(seed)
    v = np.zeros((size, size))
    for _ in range(count):
        x, y = r.random() * size, r.random() * size
        ang = r.random() * math.tau
        for _ in range(size * 2):
            ang += r.normal(0, 0.35)
            x, y = (x + math.cos(ang) * 0.7) % size, (y + math.sin(ang) * 0.7) % size
            v[int(y), int(x)] = 1
    return v


def matter_texture(seed, dark, mid, light, vein, glint):
    n = fractal(32, seed)
    img = ramp(n, [(0, dark), (0.55, mid), (1, light)])
    v = veins(32, seed + 1, 4)
    img = img * (1 - v[..., None] * 0.8) + np.array(vein) * v[..., None] * 0.8
    r = rng(seed + 2)
    for _ in range(7):
        x, y = r.integers(0, 32, 2)
        img[y, x] = glint
    return img


def bevel(img, light=1.18, dark=0.7, width=2):
    """A chiselled border like the game's polished blocks: lit top/left edge, shaded bottom/right."""
    img = img.copy()
    s = img.shape[0]
    for i in range(width):
        img[i, i:s - i] *= light
        img[i:s - i, i] *= light
        img[s - 1 - i, i:s - i] *= dark
        img[i:s - i, s - 1 - i] *= dark
    return img


def lumps(seed, stops, size=32, cells=6, crack=None):
    """Packed chunks, like the game's coal blocks: cellular pieces with dark gaps between them."""
    r = rng(seed)
    pts = r.random((cells * cells, 2)) * size
    yy, xx = np.mgrid[0:size, 0:size]
    d = []
    for px, py in pts:
        dx = np.minimum(abs(xx - px), size - abs(xx - px))
        dy = np.minimum(abs(yy - py), size - abs(yy - py))
        d.append(np.hypot(dx, dy))
    d = np.sort(np.stack(d), 0)
    edge = d[1] - d[0]
    shade = 1 - np.clip(d[0] / 6, 0, 1)
    n = fractal(size, seed + 3)
    img = ramp(0.55 * shade + 0.45 * n, stops)
    gap = edge < 0.9
    # The gaps are a darker shade of the fuel itself, not black, like the game's coal blocks.
    img[gap] = np.array(crack or stops[0][1], float) * 0.75 + img[gap] * 0.15
    return img


def circle_top(seed):
    """Slate with a transmutation circle cut in and chalked: the table top and the tablet's face."""
    n = fractal(32, seed)
    img = ramp(n, [(0, (38, 33, 33)), (0.6, (54, 47, 45)), (1, (68, 60, 56))])
    yy, xx = np.mgrid[0:32, 0:32] + 0.5
    cx = cy = 16
    rr = np.hypot(xx - cx, yy - cy)
    ang = np.arctan2(yy - cy, xx - cx)
    chalk = np.zeros((32, 32), bool)
    chalk |= abs(rr - 13) < 0.6
    chalk |= abs(rr - 11) < 0.5
    chalk |= abs(rr - 5) < 0.55
    # A triangle and its upside-down twin inside the ring.
    for k in range(6):
        a0 = -math.pi / 2 + k * math.tau / 6
        a1 = a0 + math.tau / 3
        p0 = np.array([cx + 11 * math.cos(a0), cy + 11 * math.sin(a0)])
        p1 = np.array([cx + 11 * math.cos(a1), cy + 11 * math.sin(a1)])
        d = p1 - p0
        t = np.clip(((xx - p0[0]) * d[0] + (yy - p0[1]) * d[1]) / (d @ d), 0, 1)
        dist = np.hypot(xx - (p0[0] + t * d[0]), yy - (p0[1] + t * d[1]))
        chalk |= dist < 0.5
    # Runes between the rings.
    for k in range(12):
        a = k * math.tau / 12
        chalk |= (abs(rr - 12) < 0.45) & (abs(((ang - a + math.pi) % math.tau) - math.pi) < 0.07)
    wear = fractal(32, seed + 9) > 0.25
    img[chalk & wear] = (196, 188, 170)
    img[chalk & ~wear] = (140, 134, 122)
    img[(abs(rr - 2) < 1.2)] = (214, 160, 70)  # a brass boss in the middle
    return bevel(img, 1.1, 0.8, 1)


def powder(seed, stops):
    n = fractal(32, seed, ((4, 0.4), (8, 0.35), (16, 0.25)))
    img = ramp(n, stops)
    r = rng(seed)
    sparkle = r.random((32, 32)) > 0.97
    img[sparkle] = np.minimum(img[sparkle] * 1.35, 255)
    return img


def make_textures():
    t = {}
    # Dark and red matter: dense, faintly glowing stone with veins, in the game's muted tones.
    t["darkmatter"] = save(matter_texture(11, (22, 16, 30), (44, 32, 58), (70, 52, 92), (120, 84, 160), (190, 160, 230)), "material/darkmatter")
    t["redmatter"] = save(matter_texture(23, (40, 10, 12), (82, 20, 22), (120, 36, 30), (196, 78, 48), (250, 170, 110)), "material/redmatter")
    gem = matter_texture(37, (40, 60, 90), (70, 110, 140), (140, 190, 200), (220, 240, 240), (255, 255, 255))
    t["gemmaterial"] = save(gem, "material/gem")
    t["matterblock-dark"] = save(bevel(matter_texture(12, (22, 16, 30), (44, 32, 58), (70, 52, 92), (120, 84, 160), (190, 160, 230))), "block/matterblock-dark")
    t["matterblock-red"] = save(bevel(matter_texture(24, (40, 10, 12), (82, 20, 22), (120, 36, 30), (196, 78, 48), (250, 170, 110))), "block/matterblock-red")
    fuels = {
        "alchemicalcoal": [(0, (26, 20, 20)), (0.6, (52, 34, 30)), (0.9, (120, 44, 30)), (1, (200, 90, 40))],
        "mobiusfuel": [(0, (60, 26, 10)), (0.5, (140, 64, 20)), (0.85, (210, 120, 40)), (1, (250, 190, 90))],
        "aeternalisfuel": [(0, (110, 96, 80)), (0.5, (190, 176, 140)), (0.85, (236, 222, 180)), (1, (255, 250, 225))],
    }
    for i, (name, stops) in enumerate(fuels.items()):
        t["fuel-" + name] = save(lumps(50 + i, stops), "block/fuel-" + name)
    t["circle"] = save(circle_top(70), "block/transmutation-circle")
    dust = {
        "low": [(0, (96, 92, 84)), (0.6, (140, 136, 126)), (1, (186, 182, 170))],
        "medium": [(0, (54, 84, 52)), (0.6, (88, 128, 80)), (1, (140, 176, 120))],
        "high": [(0, (40, 70, 110)), (0.6, (70, 112, 160)), (1, (130, 170, 210))],
    }
    for i, (name, stops) in enumerate(dust.items()):
        t["dust-" + name] = save(powder(80 + i, stops), "item/covalencedust-" + name)
    return t


# ------------------------------------------------------------------------------------------------- shapes

FACES = ("north", "east", "south", "west", "up", "down")


def auto_uv(face, f, t):
    """World-aligned UVs (as the game maps a full block), so tiling materials line up across elements."""
    x0, y0, z0 = f
    x1, y1, z1 = t

    def wrap(a, b):
        # Keep UVs inside one 16-unit tile where possible.
        off = math.floor(min(a, b) / 16) * 16
        a, b = a - off, b - off
        if max(a, b) > 16:
            a, b = 0, abs(b - a)
        return a, b

    if face in ("north", "south"):
        u0, u1 = wrap(x0, x1)
        v0, v1 = wrap(16 - y1, 16 - y0)
        return [u1, v0, u0, v1] if face == "north" else [u0, v0, u1, v1]
    if face in ("east", "west"):
        u0, u1 = wrap(z0, z1)
        v0, v1 = wrap(16 - y1, 16 - y0)
        return [u1, v0, u0, v1] if face == "east" else [u0, v0, u1, v1]
    u0, u1 = wrap(x0, x1)
    v0, v1 = wrap(z0, z1)
    return [u0, v0, u1, v1]


def box(name, f, t, tex, faces=None, glow=0, rot=None, origin=None, children=None, uv=None):
    """One element. tex: a texture code, or {face: code}. rot: (x, y, z) degrees about origin."""
    el = {"name": name, "from": list(f), "to": list(t), "faces": {}}
    for face in FACES:
        if faces is not None and face not in faces:
            continue
        code = tex.get(face) if isinstance(tex, dict) else tex
        if code is None:
            continue
        fd = {"texture": "#" + code, "uv": (uv or {}).get(face) or auto_uv(face, f, t)}
        if glow:
            fd["glow"] = glow
        el["faces"][face] = fd
    if rot:
        el["rotationOrigin"] = list(origin or [(a + b) / 2 for a, b in zip(f, t)])
        for axis, deg in zip("XYZ", rot):
            if deg:
                el["rotation" + axis] = deg
    if children:
        el["children"] = children
    return el


def shape(textures, elements):
    return {"textureWidth": 16, "textureHeight": 16, "textures": textures, "elements": elements}


def save_shape(rel, data):
    path = os.path.join(SHAPES, rel + ".json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(data, f, indent=1)
    return "vintagee:newlook/" + rel


# Default materials (the map's per-item textures override them).
G = "game:"
OAK = G + "block/wood/planks/oak1"
EBONY = G + "block/wood/planks/ebony1"
EBONY_LOG = G + "block/wood/debarked/ebony"
OAK_LOG = G + "block/wood/debarked/oak"
BRASS = G + "block/metal/plate/brass"
IRON = G + "block/metal/plate/iron"
GOLD = G + "block/metal/plate/gold"
SILVER = G + "block/metal/plate/silver"
COPPER = G + "block/metal/plate/copper"
BLACKBRONZE = G + "block/metal/plate/blackbronze"
TINBRONZE = G + "block/metal/plate/tinbronze"
STEEL = G + "block/metal/plate/steel"
BASALT = G + "block/stone/polishedrock/basalt"
SLATE = G + "block/stone/polishedrock/slate"
GRANITE = G + "block/stone/polishedrock/granite"
WHITEMARBLE = G + "block/stone/polishedrock/whitemarble"
OBSIDIAN_COBBLE = G + "block/stone/cobblestone/obsidian1"
BAUXITE_BRICK = G + "block/stone/brick/bauxite1"
LEATHER = G + "block/leather/plain"
LEATHER_BLACK = G + "block/leather/black"
PAPER = G + "block/cloth/paper/blank-small"
LINEN_WHITE = G + "block/cloth/linen/white"
ROPE = G + "block/cloth/reedrope"
CANDLE = G + "block/candle"
CHARRED = G + "block/wood/charred"
EMBER = G + "block/coal/ember"


def gemtex(name):
    return G + "block/stone/gem/" + name


def make_shapes(t):
    s = {}

    # ---- Blocks -------------------------------------------------------------------------------------------

    # Transmutation table: a low slab of slate with the circle cut into it, in a dark wood frame, a brass cap and
    # a candle at each corner (the block is 4 high, like before).
    els = [
        box("base", (0.5, 0, 0.5), (15.5, 1, 15.5), "frame"),
        box("frame-n", (0, 1, 0), (16, 4, 1.5), "frame"),
        box("frame-s", (0, 1, 14.5), (16, 4, 16), "frame"),
        box("frame-w", (0, 1, 1.5), (1.5, 4, 14.5), "frame"),
        box("frame-e", (14.5, 1, 1.5), (16, 4, 14.5), "frame"),
        box("slate", (1.5, 1, 1.5), (14.5, 3.5, 14.5), {"up": "top", "north": "stone", "south": "stone", "east": "stone", "west": "stone"},
            uv={"up": [0, 0, 16, 16]}),
    ]
    for i, (x, z) in enumerate(((0, 0), (14, 0), (0, 14), (14, 14))):
        els.append(box(f"cap{i}", (x - 0.2, 4, z - 0.2), (x + 2.2, 4.5, z + 2.2), "trim"))
        els.append(box(f"candle{i}", (x + 0.5, 4.5, z + 0.5), (x + 1.5, 6.5, z + 1.5), "candle", uv={f: [7, 0, 8, 2] for f in FACES[:4]} | {"up": [7, 7, 8, 8]}))
        els.append(box(f"wick{i}", (x + 0.9, 6.5, z + 0.9), (x + 1.1, 7, z + 1.1), "wick"))
    s["table"] = save_shape("block/transmutationtable", shape(
        {"frame": EBONY, "stone": SLATE, "top": t["circle"], "trim": BRASS, "candle": CANDLE, "wick": CHARRED}, els))

    # Pedestal: a stepped stone plinth, a column of matter between brass bands, a stone dish on top.
    els = [
        box("plinth", (1.5, 0, 1.5), (14.5, 2, 14.5), "stone"),
        box("step", (3, 2, 3), (13, 3, 13), "stone"),
        box("band-low", (4.5, 3, 4.5), (11.5, 4, 11.5), "trim"),
        box("column", (5, 4, 5), (11, 9, 11), "matter", glow=20),
        box("band-high", (4.5, 9, 4.5), (11.5, 10, 11.5), "trim"),
        box("dish", (3, 10, 3), (13, 11.5, 13), "stone"),
        box("dish-rim", (2.5, 11.5, 2.5), (13.5, 12, 13.5), "stone", faces=("north", "south", "east", "west", "down")),
        box("inlay", (4, 11.5, 4), (12, 12, 12), "matter", faces=("up",), glow=40),
    ]
    for i, (x, z) in enumerate(((2.5, 2.5), (12.5, 2.5), (2.5, 12.5), (12.5, 12.5))):
        els.append(box(f"stud{i}", (x - 0.5, 12, z - 0.5), (x + 0.5, 12.5, z + 0.5), "trim"))
    s["pedestal"] = save_shape("block/pedestal", shape({"stone": BASALT, "matter": t["darkmatter"], "trim": BRASS}, els))

    # Interdiction torch: a wrought iron stand holding a caged, glowing crystal.
    els = [
        box("foot", (4.5, 0, 4.5), (11.5, 1, 11.5), "metal"),
        box("foot2", (5.5, 1, 5.5), (10.5, 1.5, 10.5), "metal"),
        box("shaft", (7.25, 1.5, 7.25), (8.75, 8, 8.75), "wood"),
        box("collar", (5.5, 8, 5.5), (10.5, 9, 10.5), "metal"),
        box("crystal", (6.5, 9, 6.5), (9.5, 12.5, 9.5), "glow", glow=255),
        box("crystal-tip", (7.25, 12.5, 7.25), (8.75, 13.25, 8.75), "glow", glow=255),
        box("cap", (5.5, 13.25, 5.5), (10.5, 13.75, 10.5), "metal"),
        box("finial", (7.5, 13.75, 7.5), (8.5, 14.75, 8.5), "metal"),
    ]
    for i, (x, z) in enumerate(((5.5, 5.5), (9.9, 5.5), (5.5, 9.9), (9.9, 9.9))):
        els.append(box(f"bar{i}", (x, 9, z), (x + 0.6, 13.25, z + 0.6), "metal"))
    s["torch"] = save_shape("block/interdictiontorch", shape({"metal": IRON, "wood": EBONY_LOG, "glow": gemtex("amethyst")}, els))

    # Furnaces (fire pits like the game's, ringed with stones, on a bed of matter coals).
    def firepit(lit):
        els = [box("bed", (2, 0, 2), (14, 0.5, 14), "ash")]
        stones = [(1, 1, 4, 4), (5, 0.5, 8, 3), (9, 0.75, 12, 3.5), (12, 1.5, 15, 4.5), (12.5, 5, 15.5, 8), (12, 9, 15, 12),
                  (11.5, 12, 14.5, 15), (8, 12.75, 11, 15.5), (4.5, 12.5, 7.5, 15), (1.5, 11.5, 4.5, 14.5), (0.5, 8, 3.5, 11), (1, 4.5, 4, 7.5)]
        for i, (x0, z0, x1, z1) in enumerate(stones):
            h = 2.5 + (i * 7 % 3) * 0.5
            els.append(box(f"stone{i}", (x0, 0, z0), (x1, h, z1), "stone", rot=(0, (i * 23) % 30 - 15, 0)))
        coals = [(4, 4, 7, 7), (7.5, 4.5, 10.5, 7.5), (5, 8, 8, 11), (8.5, 8, 11.5, 11), (6.5, 6.5, 9.5, 9.5)]
        for i, (x0, z0, x1, z1) in enumerate(coals):
            y1 = 1.5 + (i % 2) * 0.5 + (0.5 if i == 4 else 0)
            els.append(box(f"coal{i}", (x0, 0.5, z0), (x1, y1, z1), "body", rot=(0, (i * 31) % 40 - 20, 0), glow=60 if lit else 0))
        if lit:
            for i, (x, z) in enumerate(((5, 5), (9, 6), (6, 9), (10, 10), (7.5, 7.5))):
                els.append(box(f"ember{i}", (x, 2, z), (x + 1, 2.5, z + 1), "ember", glow=200))
        else:
            els.append(box("char", (3, 0.5, 6), (13, 1, 10), "charred"))
        return shape({"stone": OBSIDIAN_COBBLE, "body": t["darkmatter"], "ash": CHARRED, "ember": EMBER, "charred": CHARRED}, els)

    s["furnace-lit"] = save_shape("block/furnace-lit", firepit(True))
    s["furnace-cold"] = save_shape("block/furnace-cold", firepit(False))

    # Alchemical chest: a dark wooden chest bound in brass, a gem set into the lock on its front and back.
    els = [
        box("body", (1, 0, 1), (15, 9, 15), "wood"),
        box("lid", (1, 9, 1), (15, 13, 15), "wood"),
        box("lid-arch", (1.5, 13, 3), (14.5, 14, 13), "wood"),
        box("rim", (0.75, 8.75, 0.75), (15.25, 9.5, 15.25), "metal"),
    ]
    for i, (x, z) in enumerate(((0.75, 0.75), (14, 0.75), (0.75, 14), (14, 14))):
        els.append(box(f"corner{i}", (x, 0, z), (x + 1.25, 13.25, z + 1.25), "metal"))
    for side, (f, t_) in {"n": ((6.5, 6, 0.6), (9.5, 10.5, 1)), "s": ((6.5, 6, 15), (9.5, 10.5, 15.4))}.items():
        els.append(box("lock-" + side, f, t_, "metal"))
    els.append(box("gem-n", (7.25, 7.25, 0.35), (8.75, 8.75, 0.6), "gem", glow=120))
    els.append(box("gem-s", (7.25, 7.25, 15.4), (8.75, 8.75, 15.65), "gem", glow=120))
    els.append(box("band-top", (7, 13.9, 2.9), (9, 14.2, 13.1), "metal"))
    s["chest"] = save_shape("block/alchemicalchest", shape({"wood": EBONY, "metal": BRASS, "gem": gemtex("amethyst")}, els))

    # Energy collector: a timber-framed box with a sun-catching crystal on top, held by four metal prongs.
    els = [
        box("body", (0.5, 0, 0.5), (15.5, 10, 15.5), "wood"),
        box("top", (0, 10, 0), (16, 11, 16), "metal"),
        box("socket", (5, 11, 5), (11, 12, 11), "metal"),
        box("crystal", (6, 12, 6), (10, 15, 10), "glow", glow=180),
        box("crystal-tip", (7, 15, 7), (9, 16, 9), "glow", glow=180),
        box("window-n", (4, 3, 0.3), (12, 8, 0.5), "glass", glow=60),
        box("window-s", (4, 3, 15.5), (12, 8, 15.7), "glass", glow=60),
        box("window-w", (0.3, 3, 4), (0.5, 8, 12), "glass", glow=60),
        box("window-e", (15.5, 3, 4), (15.7, 8, 12), "glass", glow=60),
    ]
    for i, (x, z) in enumerate(((0, 0), (14, 0), (0, 14), (14, 14))):
        els.append(box(f"post{i}", (x, 0, z), (x + 2, 10, z + 2), "frame"))
    for i, (x, z, ry) in enumerate(((5.5, 5.5, 45), (9.5, 5.5, -45), (5.5, 9.5, -45), (9.5, 9.5, 45))):
        els.append(box(f"prong{i}", (x - 0.4, 11.5, z - 0.4), (x + 0.4, 14.5, z + 0.4), "metal"))
    s["collector"] = save_shape("block/energycollector", shape(
        {"wood": EBONY, "frame": EBONY_LOG, "metal": BRASS, "glow": gemtex("citrine"), "glass": gemtex("citrine")}, els))

    # Antimatter relay: a squat stone drum around a matter core, banded in metal, with a crystal on top.
    els = [
        box("base", (0, 0, 0), (16, 3, 16), "stone"),
        box("drum", (2, 3, 2), (14, 11, 14), "stone"),
        box("core-n", (5, 4, 1.6), (11, 10, 2), "matter", glow=60),
        box("core-s", (5, 4, 14), (11, 10, 14.4), "matter", glow=60),
        box("core-w", (1.6, 4, 5), (2, 10, 11), "matter", glow=60),
        box("core-e", (14, 4, 5), (14.4, 10, 11), "matter", glow=60),
        box("band", (1.5, 6.5, 1.5), (14.5, 7.5, 14.5), "metal"),
        box("top", (1, 11, 1), (15, 13, 15), "metal"),
        box("seat", (5, 13, 5), (11, 14, 11), "stone"),
        box("crystal", (6.5, 14, 6.5), (9.5, 16, 9.5), "glow", glow=180),
    ]
    s["relay"] = save_shape("block/antimatterrelay", shape(
        {"stone": GRANITE, "matter": t["darkmatter"], "metal": COPPER, "glow": gemtex("citrine")}, els))

    # Energy condenser: a stone strongbox bound in metal, its lid set with a large crystal.
    els = [
        box("body", (1, 0, 1), (15, 11, 15), "stone"),
        box("lid", (0.5, 11, 0.5), (15.5, 13, 15.5), "metal"),
        box("foot", (0.5, 0, 0.5), (15.5, 1.5, 15.5), "metal"),
        box("bezel", (4, 13, 4), (12, 13.75, 12), "metal"),
        box("crystal", (5, 13.5, 5), (11, 15, 11), "glow", glow=150),
        box("crystal-top", (6.5, 15, 6.5), (9.5, 15.75, 9.5), "glow", glow=150),
    ]
    for i, (x, z) in enumerate(((0.75, 0.75), (14, 0.75), (0.75, 14), (14, 14))):
        els.append(box(f"corner{i}", (x, 1.5, z), (x + 1.25, 11, z + 1.25), "metal"))
    for side, (f, t_) in {"n": ((5, 3, 0.6), (11, 9, 1)), "s": ((5, 3, 15), (11, 9, 15.4)), "w": ((0.6, 3, 5), (1, 9, 11)), "e": ((15, 3, 5), (15.4, 9, 11))}.items():
        els.append(box("plate-" + side, f, t_, "matter", glow=30))
    s["condenser"] = save_shape("block/energycondenser", shape(
        {"stone": BASALT, "metal": BRASS, "matter": t["darkmatter"], "glow": gemtex("quartz")}, els))

    # Nova catalyst / cataclysm: a powder keg with metal hoops, a glowing band and a rope fuse.
    els = [
        box("staves", (2, 0, 2), (14, 14, 14), "wood"),
        box("bulge-x", (1, 1.5, 3), (15, 12.5, 13), "wood"),
        box("bulge-z", (3, 1.5, 1), (13, 12.5, 15), "wood"),
        box("lid", (2.5, 14, 2.5), (13.5, 14.5, 13.5), "wood"),
        box("hoop-low", (0.75, 2, 2.75), (15.25, 3, 13.25), "metal"),
        box("hoop-low2", (2.75, 2, 0.75), (13.25, 3, 15.25), "metal"),
        box("hoop-high", (0.75, 11, 2.75), (15.25, 12, 13.25), "metal"),
        box("hoop-high2", (2.75, 11, 0.75), (13.25, 12, 15.25), "metal"),
        box("band", (0.6, 6, 2.6), (15.4, 8, 13.4), "glow", glow=140),
        box("band2", (2.6, 6, 0.6), (13.4, 8, 15.4), "glow", glow=140),
        box("plug", (7, 14.5, 7), (9, 15, 9), "metal"),
        box("fuse", (7.6, 15, 7.6), (8.4, 17, 8.4), "fuse", rot=(0, 0, 20), origin=(8, 15, 8)),
    ]
    s["nova"] = save_shape("block/novaexplosive", shape({"wood": OAK, "metal": IRON, "glow": t["darkmatter"], "fuse": ROPE}, els))

    # ---- Items --------------------------------------------------------------------------------------------
    # Items are modelled standing up, centred on x = z = 8, so one set of transforms suits them all.

    def ring_band(y0=2, y1=11, w=0.9, depth=1.6):
        x0, x1 = 4, 12
        z0, z1 = 8 - depth / 2, 8 + depth / 2
        return [
            box("band-bottom", (x0 + 1, y0, z0), (x1 - 1, y0 + w, z1), "metal"),
            box("band-top", (x0 + 1, y1 - w, z0), (x1 - 1, y1, z1), "metal"),
            box("band-left", (x0, y0 + 1, z0), (x0 + w, y1 - 1, z1), "metal"),
            box("band-right", (x1 - w, y0 + 1, z0), (x1, y1 - 1, z1), "metal"),
            box("bevel-bl", (x0 + 0.4, y0 + 0.4, z0 + 0.05), (x0 + 1.4, y0 + 1.4, z1 - 0.05), "metal", rot=(0, 0, 45)),
            box("bevel-br", (x1 - 1.4, y0 + 0.4, z0 + 0.05), (x1 - 0.4, y0 + 1.4, z1 - 0.05), "metal", rot=(0, 0, 45)),
            box("bevel-tl", (x0 + 0.4, y1 - 1.4, z0 + 0.05), (x0 + 1.4, y1 - 0.4, z1 - 0.05), "metal", rot=(0, 0, 45)),
            box("bevel-tr", (x1 - 1.4, y1 - 1.4, z0 + 0.05), (x1 - 0.4, y1 - 0.4, z1 - 0.05), "metal", rot=(0, 0, 45)),
        ]

    els = ring_band() + [
        box("setting", (6.25, 10.9, 6.9), (9.75, 11.9, 9.1), "metal"),
        box("gem", (6.6, 11.6, 7.1), (9.4, 13.4, 8.9), "gem", glow=40),
        box("gem-top", (7.1, 13.4, 7.5), (8.9, 14, 8.5), "gem", glow=40),
    ]
    for i, x in enumerate((6.1, 9.4)):
        els.append(box(f"prong{i}", (x, 11.6, 7.7), (x + 0.5, 13, 8.3), "metal"))
    s["ring"] = save_shape("item/ring", shape({"metal": GOLD, "gem": gemtex("ruby")}, els))
    s["band"] = save_shape("item/band", shape({"metal": IRON}, ring_band(2.5, 10.5, 1.2, 2.2)))

    # Amulet: a leather cord, a metal bezel and a gem.
    def amulet_parts():
        return [
            box("cord-l", (5, 9, 7.75), (5.6, 16, 8.25), "cord", rot=(0, 0, -12), origin=(5.3, 9, 8)),
            box("cord-r", (10.4, 9, 7.75), (11, 16, 8.25), "cord", rot=(0, 0, 12), origin=(10.7, 9, 8)),
            box("bail", (7.3, 8, 7.4), (8.7, 9.5, 8.6), "metal"),
            box("bezel", (5.5, 2.5, 7.1), (10.5, 8, 8.9), "metal"),
            box("bezel-x", (5, 3.5, 7.15), (11, 7, 8.85), "metal"),
            box("bezel-y", (6.5, 2, 7.15), (9.5, 8.5, 8.85), "metal"),
            box("gem", (6.2, 3.5, 6.7), (9.8, 7, 7.1), "gem", glow=50),
            box("gem-back", (6.2, 3.5, 8.9), (9.8, 7, 9.3), "gem", glow=50),
            box("gem-dome", (6.9, 4.2, 6.4), (9.1, 6.3, 6.7), "gem", glow=50),
        ]

    s["amulet"] = save_shape("item/amulet", shape({"metal": SILVER, "gem": gemtex("aquamarine"), "cord": LEATHER}, amulet_parts()))

    # Archangel's Smite: the amulet with a pair of white wings.
    wings = []
    for side, sign in (("l", -1), ("r", 1)):
        x0 = 8 + sign * 2.5
        for i, (length, y, ang) in enumerate(((6, 6, 20), (5, 4.5, 5), (4, 3, -10))):
            a, b = (x0, x0 + sign * length) if sign > 0 else (x0 + sign * length, x0)
            wings.append(box(f"wing-{side}{i}", (a, y, 7.7), (b, y + 1.6, 8.3), "feather", rot=(0, 0, sign * ang), origin=(x0, y, 8)))
    s["smite"] = save_shape("item/wingedamulet", shape({"metal": GOLD, "gem": gemtex("diamond"), "cord": LEATHER, "feather": LINEN_WHITE},
                                                       amulet_parts() + wings))

    # Stones: a smooth, rounded gemstone with a metal cap and loop, wired front and back.
    els = [
        box("core", (5, 2, 6), (11, 8, 10), "gem", glow=30),
        box("round-x", (4.5, 3, 6.5), (11.5, 7, 9.5), "gem", glow=30),
        box("round-z", (5.5, 3, 5.5), (10.5, 7, 10.5), "gem", glow=30),
        box("top", (6, 8, 6.5), (10, 8.6, 9.5), "gem", glow=30),
        box("bottom", (6, 1.5, 6.5), (10, 2, 9.5), "gem", glow=30),
        box("cap", (6.75, 8.5, 6.75), (9.25, 9.2, 9.25), "metal"),
        box("loop", (7.4, 9.2, 7.75), (8.6, 10.6, 8.25), "metal"),
        box("wire-front", (7.7, 2.6, 5.3), (8.3, 8.6, 5.5), "metal"),
        box("wire-back", (7.7, 2.6, 10.5), (8.3, 8.6, 10.7), "metal"),
    ]
    s["stone"] = save_shape("item/stone", shape({"metal": BRASS, "gem": gemtex("topaz")}, els))

    # Gem of Eternal Density: three crystals in an iron claw.
    els = [
        box("claw", (5, 1, 5), (11, 3, 11), "metal"),
        box("claw-lip", (5.5, 3, 5.5), (10.5, 3.5, 10.5), "metal"),
        box("crystal-a", (6.5, 3, 6.5), (9.5, 12, 9.5), "gem", glow=50, rot=(0, 45, 0)),
        box("crystal-a-tip", (7.25, 12, 7.25), (8.75, 13.5, 8.75), "gem", glow=50, rot=(0, 45, 0)),
        box("crystal-b", (4.5, 3, 7), (6.5, 9, 9), "gem", glow=50, rot=(0, 0, 18), origin=(6, 3, 8)),
        box("crystal-c", (9.5, 3, 7), (11.5, 8, 9), "gem", glow=50, rot=(0, 0, -18), origin=(10, 3, 8)),
    ]
    for i, (x, z) in enumerate(((5, 5), (10.4, 5), (5, 10.4), (10.4, 10.4))):
        els.append(box(f"talon{i}", (x, 3, z), (x + 0.6, 5.5, z + 0.6), "metal"))
    s["densegem"] = save_shape("item/densegem", shape({"metal": IRON, "gem": gemtex("diamond")}, els))

    # Repair talisman: a round bronze charm on a cord with a gem in the middle.
    els = [
        box("disc", (5, 2.5, 7.5), (11, 9.5, 8.5), "metal"),
        box("disc-x", (4, 3.5, 7.55), (12, 8.5, 8.45), "metal"),
        box("disc-y", (6, 1.5, 7.55), (10, 10.5, 8.45), "metal"),
        box("rim", (5.5, 3, 7.3), (10.5, 9, 7.5), "trim"),
        box("gem", (7, 5, 7.0), (9, 7, 7.3), "gem", glow=60),
        box("bail", (7.3, 10.5, 7.6), (8.7, 11.75, 8.4), "metal"),
        box("cord-l", (5.5, 11, 7.75), (6.1, 16, 8.25), "cord", rot=(0, 0, -15), origin=(5.8, 11, 8)),
        box("cord-r", (9.9, 11, 7.75), (10.5, 16, 8.25), "cord", rot=(0, 0, 15), origin=(10.2, 11, 8)),
    ]
    s["talisman"] = save_shape("item/talisman", shape({"metal": TINBRONZE, "trim": BRASS, "gem": gemtex("quartz"), "cord": LEATHER}, els))

    # Watch of Flowing Time: a brass pocket watch with a paper face and iron hands.
    els = [
        box("case", (4, 3, 7), (12, 11, 9), "metal"),
        box("case-x", (3, 4, 7.05), (13, 10, 8.95), "metal"),
        box("case-y", (5, 2, 7.05), (11, 12, 8.95), "metal"),
        box("face", (5, 4, 6.85), (11, 10, 7), "face", faces=("north",)),
        box("face-x", (4.2, 5, 6.9), (11.8, 9, 7), "face", faces=("north",)),
        box("hand-h", (7.85, 7, 6.75), (8.15, 9.25, 6.85), "hand"),
        box("hand-m", (8, 6.85, 6.75), (10.25, 7.15, 6.85), "hand"),
        box("pin", (7.75, 6.75, 6.7), (8.25, 7.25, 6.8), "metal"),
        box("crown", (7.25, 12, 7.25), (8.75, 13, 8.75), "metal"),
        box("loop", (7, 13, 7.6), (9, 14.5, 8.4), "metal"),
        box("face-back", (5, 4, 9), (11, 10, 9.15), "face", faces=("south",)),
        box("face-back-x", (4.2, 5, 9), (11.8, 9, 9.1), "face", faces=("south",)),
        box("hand-h-back", (7.85, 7, 9.15), (8.15, 9.25, 9.25), "hand"),
        box("hand-m-back", (5.75, 6.85, 9.15), (8, 7.15, 9.25), "hand"),
        box("pin-back", (7.75, 6.75, 9.2), (8.25, 7.25, 9.3), "metal"),
    ]
    s["watch"] = save_shape("item/watch", shape({"metal": BRASS, "face": PAPER, "hand": BLACKBRONZE, "gem": gemtex("citrine")}, els))

    # Lens (Hyperkinetic / Catalytic): a metal-rimmed lens on a wooden handle.
    els = [
        box("handle", (7.25, 0, 7.25), (8.75, 6, 8.75), "wood"),
        box("ferrule", (7, 5, 7), (9, 6.5, 9), "metal"),
        box("rim-bottom", (5, 6.5, 7.2), (11, 7.5, 8.8), "metal"),
        box("rim-top", (5, 14.5, 7.2), (11, 15.5, 8.8), "metal"),
        box("rim-left", (3, 8.5, 7.2), (4, 13.5, 8.8), "metal"),
        box("rim-right", (12, 8.5, 7.2), (13, 13.5, 8.8), "metal"),
        box("rim-bl", (3.6, 6.9, 7.25), (5.6, 8.9, 8.75), "metal", rot=(0, 0, 45)),
        box("rim-br", (10.4, 6.9, 7.25), (12.4, 8.9, 8.75), "metal", rot=(0, 0, 45)),
        box("rim-tl", (3.6, 13.1, 7.25), (5.6, 15.1, 8.75), "metal", rot=(0, 0, 45)),
        box("rim-tr", (10.4, 13.1, 7.25), (12.4, 15.1, 8.75), "metal", rot=(0, 0, 45)),
        box("glass", (4, 7.5, 7.6), (12, 14.5, 8.4), "glass", glow=50),
    ]
    s["lens"] = save_shape("item/lens", shape({"metal": BRASS, "wood": EBONY_LOG, "glass": gemtex("quartz")}, els))

    # Destruction Catalyst: a short rod banded in metal, tipped with a dark crystal.
    els = [
        box("rod", (7.1, 0, 7.1), (8.9, 9, 8.9), "wood"),
        box("pommel", (6.75, 0, 6.75), (9.25, 1, 9.25), "metal"),
        box("band", (6.8, 4, 6.8), (9.2, 4.75, 9.2), "metal"),
        box("cup", (6.25, 9, 6.25), (9.75, 10, 9.75), "metal"),
        box("crystal", (6.75, 10, 6.75), (9.25, 13.5, 9.25), "gem", glow=90, rot=(0, 45, 0)),
        box("crystal-tip", (7.4, 13.5, 7.4), (8.6, 15, 8.6), "gem", glow=90, rot=(0, 45, 0)),
    ]
    for i, (x, z) in enumerate(((6.25, 6.25), (9.25, 6.25), (6.25, 9.25), (9.25, 9.25))):
        els.append(box(f"claw{i}", (x, 10, z), (x + 0.5, 11.5, z + 0.5), "metal"))
    s["catalyst"] = save_shape("item/catalyst", shape({"metal": BLACKBRONZE, "wood": EBONY_LOG, "gem": gemtex("ruby")}, els))

    # Mercurial Eye: a marble eye with a gem iris, held in a brass ring on a little stand.
    els = [
        box("stand", (5.5, 0, 6), (10.5, 1, 10), "metal"),
        box("stem", (7.25, 1, 7.5), (8.75, 3, 8.5), "metal"),
        box("ball", (5, 3, 5), (11, 9, 11), "white"),
        box("ball-x", (4.5, 4, 5.5), (11.5, 8, 10.5), "white"),
        box("ball-y", (5.5, 2.5, 5.5), (10.5, 9.5, 10.5), "white"),
        box("ball-z", (5.5, 4, 4.5), (10.5, 8, 11.5), "white"),
        box("iris", (6.5, 4.5, 4.3), (9.5, 7.5, 4.5), "gem", glow=80),
        box("pupil", (7.4, 5.4, 4.1), (8.6, 6.6, 4.3), "pupil"),
        box("iris-back", (6.5, 4.5, 11.5), (9.5, 7.5, 11.7), "gem", glow=80),
        box("pupil-back", (7.4, 5.4, 11.7), (8.6, 6.6, 11.9), "pupil"),
        box("ring-l", (3.8, 4, 7.4), (4.4, 8, 8.6), "metal"),
        box("ring-r", (11.6, 4, 7.4), (12.2, 8, 8.6), "metal"),
        box("ring-t", (5.5, 9.6, 7.4), (10.5, 10.2, 8.6), "metal"),
    ]
    s["eye"] = save_shape("item/eye", shape({"metal": BRASS, "white": WHITEMARBLE, "gem": gemtex("ruby"), "pupil": G + "block/stone/gem/onyx"}, els))

    # Tome of Knowledge: a leather-bound book with brass corners and a gem on the cover.
    els = [
        box("cover-back", (3.5, 1, 7.4), (12.5, 14, 8.0), "leather"),
        box("pages", (3.9, 1.4, 8.0), (12.1, 13.6, 9.8), "paper"),
        box("cover-front", (3.5, 1, 9.8), (12.5, 14, 10.4), "leather"),
        box("spine", (3, 1, 7.4), (3.6, 14, 10.4), "leather"),
        box("gem", (7, 6.5, 10.4), (9, 8.5, 10.7), "gem", glow=60),
        box("gem-frame", (6.5, 6, 10.35), (9.5, 9, 10.5), "metal"),
    ]
    for i, (x, y) in enumerate(((11.2, 1), (11.2, 12.7))):
        els.append(box(f"corner{i}", (x, y, 7.3), (12.6, y + 1.3, 10.5), "metal"))
    els.append(box("clasp", (12.4, 6.5, 7.6), (12.8, 8.5, 10.2), "metal"))
    s["book"] = save_shape("item/book", shape({"leather": LEATHER, "paper": PAPER, "metal": BRASS, "gem": gemtex("amethyst")}, els))

    # Divining rods: a forked branch with a metal tip on each prong.
    els = [box("handle", (7.25, 0, 7.25), (8.75, 7, 8.75), "wood"),
           box("wrap", (7.1, 1, 7.1), (8.9, 4, 8.9), "cord")]
    for side, ang in (("l", 22), ("r", -22)):
        els.append(box("prong-" + side, (7.35, 6.5, 7.35), (8.65, 14, 8.65), "wood", rot=(0, 0, ang), origin=(8, 6.5, 8),
                       children=[box("tip-" + side, (-0.15, 7.5, -0.15), (1.45, 9, 1.45), "metal")]))
    s["rod"] = save_shape("item/rod", shape({"wood": OAK_LOG, "cord": LEATHER, "metal": COPPER}, els))

    # Everlasting Pitch Glue: a wooden bucket full of dark pitch glue, iron-banded, with a stirring stick leaning in it.
    els = [
        box("bottom", (4, 0, 4), (12, 1, 12), "inner"),
        box("wall-n", (4, 1, 4), (12, 9, 5), {"north": "sides", "south": "inner", "up": "sides"}),
        box("wall-s", (4, 1, 11), (12, 9, 12), {"south": "sides", "north": "inner", "up": "sides"}),
        box("wall-w", (4, 1, 5), (5, 9, 11), {"west": "sides", "east": "inner", "up": "sides"}),
        box("wall-e", (11, 1, 5), (12, 9, 11), {"east": "sides", "west": "inner", "up": "sides"}),
        box("band-low", (3.8, 1.5, 3.8), (12.2, 2.5, 12.2), "metal"),
        box("band-high", (3.8, 7, 3.8), (12.2, 8, 12.2), "metal"),
        box("glue", (5, 1, 5), (11, 7.6, 11), "glue", glow=12),
        box("drip-n", (6, 7.6, 3.9), (8, 8.4, 4.6), "glue"),
        box("drip-e", (11.4, 5.5, 7), (12.6, 8, 9), "glue"),
        box("stick", (7.25, 2, 7.25), (8.75, 15, 8.75), "wood", rot=(0, 0, -22), origin=(8, 2, 8)),
        box("stick-glue", (7.1, 2, 7.1), (8.9, 7.5, 8.9), "glue", rot=(0, 0, -22), origin=(8, 2, 8)),
    ]
    s["gluebucket"] = save_shape("item/gluebucket", shape({"sides": G + "block/wood/bucket/sides", "inner": G + "block/wood/bucket/inner side",
                                                           "metal": IRON, "glue": G + "block/liquid/glue-cold", "wood": OAK_LOG}, els))

    # Transmutation tablet: a slate tablet with the circle, in a wooden frame (standing on its edge).
    els = [
        box("frame", (2.5, 1, 7.2), (13.5, 15, 8.8), "wood"),
        box("slate", (3.5, 2, 6.9), (12.5, 14, 7.2), {"north": "top"}, uv={"north": [0, 0, 16, 16]}),
        box("slate-back", (3.5, 2, 8.8), (12.5, 14, 9.1), {"south": "top"}, uv={"south": [0, 0, 16, 16]}),
        box("foot", (2, 0, 6.5), (14, 1, 9.5), "wood"),
    ]
    s["tablet"] = save_shape("item/tablet", shape({"wood": EBONY, "top": t["circle"], "stone": SLATE}, els))

    # Klein Star: a six-pointed crystal star set in a metal band.
    els = [
        box("core", (6, 5, 6), (10, 9, 10), "gem", glow=70),
        box("spike-x", (3, 6.1, 7.1), (13, 7.9, 8.9), "gem", glow=70),
        box("spike-y", (7.1, 2, 7.1), (8.9, 12, 8.9), "gem", glow=70),
        box("spike-z", (7.1, 6.1, 3), (8.9, 7.9, 13), "gem", glow=70),
        box("band", (5.5, 6.6, 5.5), (10.5, 7.4, 10.5), "metal"),
    ]
    s["star"] = save_shape("item/star", shape({"metal": GOLD, "gem": gemtex("quartz")}, els))

    # Philosopher's Stone and matter: a cut gem and a polished orb.
    els = [
        box("pavilion", (6, 2, 6), (10, 3, 10), "gem", glow=60),
        box("girdle", (5, 3, 5), (11, 7, 11), "gem", glow=60),
        box("girdle-x", (4.5, 4, 5.75), (11.5, 6, 10.25), "gem", glow=60),
        box("girdle-z", (5.75, 4, 4.5), (10.25, 6, 11.5), "gem", glow=60),
        box("crown", (5.5, 7, 5.5), (10.5, 8, 10.5), "gem", glow=60),
        box("table", (6.5, 8, 6.5), (9.5, 8.6, 9.5), "gem", glow=60),
    ]
    for e in els:
        e["rotationOrigin"] = [8, 5, 8]
        e["rotationY"] = 45
    s["gem"] = save_shape("item/gem", shape({"gem": gemtex("ruby")}, els))
    els = [
        box("orb", (5, 3, 5), (11, 9, 11), "matter", glow=50),
        box("orb-x", (4.5, 4, 5.5), (11.5, 8, 10.5), "matter", glow=50),
        box("orb-y", (5.5, 2.5, 5.5), (10.5, 9.5, 10.5), "matter", glow=50),
        box("orb-z", (5.5, 4, 4.5), (10.5, 8, 11.5), "matter", glow=50),
    ]
    s["orb"] = save_shape("item/orb", shape({"matter": t["darkmatter"]}, els))

    # Covalence dust: a little heap of powder.
    els = [
        box("heap-1", (3.5, 0, 4), (12.5, 1, 12), "dust"),
        box("heap-2", (4.5, 1, 4.5), (11.5, 2, 11), "dust"),
        box("heap-3", (5.5, 2, 5.5), (10.5, 3, 10.5), "dust"),
        box("heap-4", (6.5, 3, 6.5), (9.5, 3.75, 9.5), "dust"),
        box("spill", (10, 0, 9), (13.5, 0.5, 12.5), "dust"),
    ]
    s["dust"] = save_shape("item/dust", shape({"dust": t["dust-low"]}, els))

    # Alchemical fuels: a few chunks, like the game's coal.
    els = [
        box("chunk-1", (4, 0, 5), (9, 4, 10), "fuel", rot=(0, 20, 0), glow=20),
        box("chunk-2", (8, 0, 6), (12, 3, 10), "fuel", rot=(0, -25, 8), glow=20),
        box("chunk-3", (6, 2.5, 6.5), (10, 6, 10.5), "fuel", rot=(12, 40, 0), glow=20),
    ]
    s["lump"] = save_shape("item/lump", shape({"fuel": t["fuel-alchemicalcoal"]}, els))
    return s


# ---------------------------------------------------------------------------------------------- the map

def shape_bounds(shape_loc):
    """The box (in 0-16 units) around every element of a model, children included."""
    path = os.path.join(SHAPES, shape_loc.split("newlook/", 1)[1] + ".json")
    els = json.load(open(path))["elements"]
    pts = []

    def walk(e, off):
        f = [a + b for a, b in zip(e["from"], off)]
        t = [a + b for a, b in zip(e["to"], off)]
        pts.extend([f, t])
        for c in e.get("children") or []:
            walk(c, f)

    for e in els:
        walk(e, [0, 0, 0])
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return lo, hi


def item_transforms(shape_loc):
    """Hand, inventory and ground placement worked out from the model's size, so small and large items look alike."""
    lo, hi = shape_bounds(shape_loc)
    ext = max(h - l for h, l in zip(hi, lo))
    c = [round((h + l) / 2 / 16, 3) for h, l in zip(hi, lo)]

    def v(x, y, z):
        return {"x": x, "y": y, "z": z}

    r2 = lambda x: round(x, 2)
    # Like the game's own small items (gems, nuggets, coal): turned over and tilted in the inventory (the GUI's
    # y axis points down), lying flat on the ground, held in the hand at a fixed small size; first person is left
    # to the game's default, as for those items.
    return {
        "guiTransform": {"translation": v(0, 0, 0), "rotation": v(150, 45, 0), "origin": v(*c), "scale": r2(min(4.0, 15 / ext))},
        "tpHandTransform": {"translation": v(-0.8, -0.1, -0.7), "rotation": v(5, 82, 16), "origin": v(*c), "scale": r2(min(0.75, 4.5 / ext))},
        "groundTransform": {"translation": v(0, 0, 0), "rotation": v(0, 0, 0), "origin": v(c[0], round(lo[1] / 16, 3), c[2]),
                            "scale": r2(min(4.0, max(1.2, 16 / ext)))},
    }


def full_textures(shape_loc, textures):
    """The shape's own textures with the override's on top: written out in full, so a texture code the block or item
    already had (a torch's "glow", say) can't win over the model's."""
    path = os.path.join(SHAPES, shape_loc.split("newlook/", 1)[1] + ".json")
    out = dict(json.load(open(path)).get("textures") or {})
    out.update(textures)
    return out


def item(code, shape_loc, textures, label=None):
    o = {"code": "vintagee:" + code, "label": label or code, "kind": "item", "shape": shape_loc, "textures": full_textures(shape_loc, textures)}
    o.update(item_transforms(shape_loc))
    return o


def block(code, shape_loc, textures, label=None, cube_before=True):
    """A block's model. Its outline (selection box) fits the model. Its collision box stays the same in every look:
    looks are picked per player, and everyone has to bump into the same thing."""
    o = {"code": "vintagee:" + code, "label": label or code, "kind": "block", "shape": shape_loc,
         "textures": full_textures(shape_loc, textures), "drawtype": "JSON"}
    lo, hi = shape_bounds(shape_loc)
    box = {k: round(min(16.0, max(0.0, v)) / 16, 4) for k, v in zip(("x1", "y1", "z1", "x2", "y2", "z2"), lo + hi)}
    o["selectionbox"] = box
    if cube_before:
        # It was a full cube: its neighbours must now draw the faces next to it, and it shouldn't darken them.
        o["sideopaque"] = False
        o["set"] = {"LightAbsorption": 0}
    return o


def make_map(t, s):
    files = {
        # Matter, as used on tools, armour, the pedestal and the furnaces.
        "vintagee:item/tool/material/darkmatter": t["darkmatter"],
        "vintagee:item/tool/material/redmatter": t["redmatter"],
        "vintagee:item/tool/material/gem": t["gemmaterial"],
        "vintagee:block/matterblock-dark": t["matterblock-dark"],
        "vintagee:block/matterblock-red": t["matterblock-red"],
    }
    for fuel in ("alchemicalcoal", "mobiusfuel", "aeternalisfuel"):
        files[f"vintagee:block/alchemicalfuelblock-{fuel}"] = t["fuel-" + fuel]
    # Alchemical bags: the game's own linen in each colour.
    for c in ("white", "plain", "gray", "black", "brown", "red", "orange", "yellow", "green", "blue", "purple", "pink"):
        files[f"vintagee:item/bag/alchemicalbag-{c}"] = f"game:block/cloth/linen/{c}"
    files["vintagee:item/bag/alchemicalbag-top"] = ROPE

    ov = []
    # ---- blocks
    ov.append(block("transmutationtable", s["table"], {}, "transmutation table", cube_before=False))
    ov.append(block("pedestal-*", s["pedestal"], {"stone": BASALT, "matter": t["darkmatter"], "trim": BRASS}, "dark matter pedestal", cube_before=False))
    ov.append(block("redmatterpedestal-*", s["pedestal"], {"stone": G + "block/stone/polishedrock/chert", "matter": t["redmatter"], "trim": GOLD},
                    "red matter pedestal", cube_before=False))
    ov.append(block("interdictiontorch*", s["torch"], {}, "interdiction torch", cube_before=False))
    for kind, stone, matter in (("dark", OBSIDIAN_COBBLE, t["darkmatter"]), ("red", BAUXITE_BRICK, t["redmatter"])):
        for state in ("lit", "extinct", "cold"):
            o = block(f"furnace-{kind}-{state}*", s["furnace-lit" if state == "lit" else "furnace-cold"],
                      {"stone": stone, "body": matter}, f"{kind} matter furnace ({state})", cube_before=False)
            o["preview"] = state != "extinct"
            ov.append(o)
    ov.append(block("alchemicalchest", s["chest"], {}, "alchemical chest"))
    tiers = {"mk1": (BRASS, "citrine"), "mk2": (SILVER, "amethyst"), "mk3": (GOLD, "ruby")}
    for tier, (metal, gem) in tiers.items():
        ov.append(block(f"energycollector-{tier}", s["collector"], {"metal": metal, "glow": gemtex(gem), "glass": gemtex(gem)}, f"collector {tier}"))
    relay_metal = {"mk1": COPPER, "mk2": SILVER, "mk3": GOLD}
    for tier, (_, gem) in tiers.items():
        ov.append(block(f"antimatterrelay-{tier}", s["relay"], {"metal": relay_metal[tier], "glow": gemtex(gem)}, f"relay {tier}"))
    ov.append(block("energycondenser-mk1", s["condenser"], {}, "condenser mk1"))
    ov.append(block("energycondenser-mk2", s["condenser"], {"stone": G + "block/stone/polishedrock/chert", "metal": GOLD, "matter": t["redmatter"],
                                                             "glow": gemtex("ruby")}, "condenser mk2"))
    ov.append(block("novaexplosive-catalyst", s["nova"], {}, "nova catalyst"))
    ov.append(block("novaexplosive-cataclysm", s["nova"], {"wood": EBONY, "metal": GOLD, "glow": t["redmatter"]}, "nova cataclysm"))
    # Cube blocks keep their shape and only get new textures (through the file list); shown here for the preview.
    for kind in ("dark", "red"):
        ov.append({"code": f"vintagee:matterblock-{kind}", "label": f"{kind} matter block", "cube": True, "preview_only": True,
                   "textures": {f: t["matterblock-" + kind] for f in FACES}})
    for fuel in ("alchemicalcoal", "mobiusfuel", "aeternalisfuel"):
        ov.append({"code": f"vintagee:alchemicalfuelblock-{fuel}", "label": fuel + " block", "cube": True, "preview_only": True,
                   "textures": {f: t["fuel-" + fuel] for f in FACES}})

    # ---- items
    rings = {
        "blackholeband": (BLACKBRONZE, "lapislazuli"),
        "swiftwolfsrendinggale": (SILVER, "diamond"),
        "harvestgoddessband": (GOLD, "emerald"),
        "ignitionring": (COPPER, "ruby"),
        "zeroring": (SILVER, "aquamarine"),
        "ringofarcana": (GOLD, "amethyst"),
        "voidring": (BLACKBRONZE, "onyx"),
    }
    for code, (metal, gem) in rings.items():
        ov.append(item("trinket-" + code, s["ring"], {"metal": metal, "gem": gemtex(gem)}, code))
    ov.append(item("ironband", s["band"], {}, "iron band"))
    stones = {"bodystone": (BRASS, "topaz"), "soulstone": (SILVER, "sapphire"), "lifestone": (GOLD, "ruby")}
    for code, (metal, gem) in stones.items():
        ov.append(item("trinket-" + code, s["stone"], {"metal": metal, "gem": gemtex(gem)}, code))
    ov.append(item("trinket-evertideamulet", s["amulet"], {"metal": SILVER, "gem": gemtex("aquamarine")}, "evertide amulet"))
    ov.append(item("trinket-volcaniteamulet", s["amulet"], {"metal": COPPER, "gem": gemtex("topaz")}, "volcanite amulet"))
    ov.append(item("trinket-repairtalisman", s["talisman"], {}, "repair talisman"))
    ov.append(item("trinket-gemofeternaldensity", s["densegem"], {}, "gem of eternal density"))
    ov.append(item("trinket-watchofflowingtime", s["watch"], {}, "watch of flowing time"))
    ov.append(item("gadget-archangelssmite", s["smite"], {}, "archangel's smite"))
    ov.append(item("gadget-hyperkineticlens", s["lens"], {}, "hyperkinetic lens"))
    ov.append(item("gadget-catalyticlens", s["lens"], {"metal": GOLD, "glass": gemtex("ruby")}, "catalytic lens"))
    ov.append(item("gadget-destructioncatalyst", s["catalyst"], {}, "destruction catalyst"))
    ov.append(item("gadget-mercurialeye", s["eye"], {}, "mercurial eye"))
    ov.append(item("gadget-tomeofknowledge", s["book"], {}, "tome of knowledge"))
    for code, metal in (("diviningrodlow", COPPER), ("diviningrodmedium", SILVER), ("diviningrodhigh", GOLD)):
        ov.append(item("gadget-" + code, s["rod"], {"metal": metal}, code))
    ov.append(item("gadget-eternalglue", s["gluebucket"], {}, "everlasting pitch glue"))
    ov.append(item("transmutationtablet", s["tablet"], {}, "transmutation tablet"))
    stars = {"ein": ("quartz", COPPER), "zwei": ("citrine", BRASS), "drei": ("topaz", SILVER), "vier": ("emerald", SILVER),
             "sphere": ("sapphire", GOLD), "omega": ("amethyst", GOLD)}
    for tier, (gem, metal) in stars.items():
        ov.append(item("kleinstar-" + tier, s["star"], {"gem": gemtex(gem), "metal": metal}, "klein star " + tier))
    ov.append(item("philosophersstone", s["gem"], {}, "philosopher's stone"))
    ov.append(item("matter-dark", s["orb"], {"matter": t["darkmatter"]}, "dark matter"))
    ov.append(item("matter-red", s["orb"], {"matter": t["redmatter"]}, "red matter"))
    for level in ("low", "medium", "high"):
        ov.append(item("covalencedust-" + level, s["dust"], {"dust": t["dust-" + level]}, "covalence dust " + level))
    for fuel in ("alchemicalcoal", "mobiusfuel", "aeternalisfuel"):
        ov.append(item("alchemicalfuel-" + fuel, s["lump"], {"fuel": t["fuel-" + fuel]}, fuel))

    root = {
        "//": "Written by tools/gen_newlook.py. Used when the server setting Look is \"New\": 'textures' swaps one texture "
              "for another everywhere; each override gives a block or item a new model (shape), adds or replaces the "
              "textures it names, and for items sets how they're held and shown. 'label', 'kind', 'preview' and "
              "'preview_only' are only for tools/render_look.py.",
        "textures": files,
        "overrides": ov,
    }
    with open(MAP, "w") as f:
        json.dump(root, f, indent=1)
    return root


def main():
    t = make_textures()
    s = make_shapes(t)
    root = make_map(t, s)
    print(f"{len(os.listdir(SHAPES))} shape folders, {len(root['overrides'])} overrides, {len(root['textures'])} texture swaps")


if __name__ == "__main__":
    main()
