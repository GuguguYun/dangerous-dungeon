"""Turn the raw Meshy UI paintings into game textures.

Run: blender --background --factory-startup --python blender/ui_process.py -- <raw dir> <out dir>

- card_* and medal: the flat magenta window is keyed out (only the region connected to the window's
  centre, so pink gems elsewhere survive), its fringe de-spilled, and its box recorded.
- every image with a removed background is cropped to its visible pixels.
- skill_* pictures get a round alpha mask (they sit inside the medallion).
- everything is scaled to the size the HUD draws it at (times two for sharpness).
Writes <out dir>/<name>.png and <out dir>/ui_layout.txt ("name x y w h" of each window, 0..1 from top-left).
"""
import os
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
raw, out = argv[0], argv[1]
os.makedirs(out, exist_ok=True)

WIDTH = {"card": 512, "medal": 256, "skill": 256, "buff": 384, "panel": 512, "button": 384, "banner": 768, "glyph": 96}


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1].copy()  # top row first


def save(arr, path, width):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("tmp", w, h, alpha=True)
    img.pixels.foreach_set(arr[::-1].ravel())
    nh = max(1, round(h * width / w))
    img.scale(width, nh)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def magenta(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return np.minimum(r, b) - g


def flood(mask, seed):
    """The part of mask connected to seed (a pixel, or a mask of seed pixels)."""
    if isinstance(seed, tuple):
        region = np.zeros_like(mask)
        region[seed] = True
    else:
        region = seed & mask
    while True:
        grown = region.copy()
        grown[1:] |= region[:-1]
        grown[:-1] |= region[1:]
        grown[:, 1:] |= region[:, :-1]
        grown[:, :-1] |= region[:, 1:]
        grown &= mask
        if grown.sum() == region.sum():
            return region
        # Several steps per check: the region spreads 1 px per step.
        for _ in range(15):
            g2 = grown.copy()
            g2[1:] |= grown[:-1]
            g2[:-1] |= grown[1:]
            g2[:, 1:] |= grown[:, :-1]
            g2[:, :-1] |= grown[:, 1:]
            grown = g2 & mask
        region = grown


def dilate(mask, n):
    m = mask.copy()
    for _ in range(n):
        g = m.copy()
        g[1:] |= m[:-1]
        g[:-1] |= m[1:]
        g[:, 1:] |= m[:, :-1]
        g[:, :-1] |= m[:, 1:]
        m = g
    return m


def key_window(a, seed_uv):
    h, w = a.shape[:2]
    m = magenta(a)
    strong = m > 0.4
    sy, sx = int(seed_uv[1] * h), int(seed_uv[0] * w)
    ys, xs = np.nonzero(strong)
    k = np.argmin((ys - sy) ** 2 + (xs - sx) ** 2)
    region = flood(strong, (ys[k], xs[k]))
    ring = dilate(region, 6) & ~region
    # Inside: gone. Fringe: fade by how magenta it still is, and pull the magenta cast out.
    a[region, 3] = 0.0
    fade = np.clip((m - 0.12) / 0.28, 0.0, 1.0)
    a[ring, 3] *= 1.0 - fade[ring]
    excess = np.clip(m, 0.0, None)
    a[ring, 0] -= excess[ring]
    a[ring, 2] -= excess[ring]
    ys, xs = np.nonzero(region)
    return xs.min() / w, ys.min() / h, (xs.max() - xs.min() + 1) / w, (ys.max() - ys.min() + 1) / h


def key_outside(a):
    """Cards are painted edge to edge on flat green: clear the green connected to the image border."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gm = g - np.maximum(r, b)
    strong = gm > 0.35
    edge = np.zeros_like(strong)
    edge[0, :] = edge[-1, :] = True
    edge[:, 0] = edge[:, -1] = True
    region = flood(strong, edge)
    ring = dilate(region, 4) & ~region
    a[region, 3] = 0.0
    fade = np.clip((gm - 0.1) / 0.25, 0.0, 1.0)
    a[ring, 3] *= 1.0 - fade[ring]
    a[ring, 1] -= np.clip(gm, 0.0, None)[ring]


def parchment(a):
    """The light parchment panel under the ribbon (where the card's rules text goes)."""
    h, w = a.shape[:2]
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    beige = (r > 0.55) & (g > 0.45) & (b > 0.3) & (r - b > 0.06) & (r - b < 0.4) & (a[..., 3] > 0.9)
    ys, xs = np.nonzero(beige)
    k = np.argmin((ys - 0.78 * h) ** 2 + (xs - 0.5 * w) ** 2)
    region = flood(beige, (ys[k], xs[k]))
    ys, xs = np.nonzero(region)
    return xs.min() / w, ys.min() / h, (xs.max() - xs.min() + 1) / w, (ys.max() - ys.min() + 1) / h


def crop(a, box=None):
    vis = a[..., 3] > 0.02
    ys, xs = np.nonzero(vis)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    if box:
        h, w = a.shape[:2]
        bx, by, bw, bh = box
        box = ((bx * w - x0) / (x1 - x0), (by * h - y0) / (y1 - y0), bw * w / (x1 - x0), bh * h / (y1 - y0))
    return a[y0:y1, x0:x1], box


def round_mask(a):
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt(((xx + 0.5) / w - 0.5) ** 2 + ((yy + 0.5) / h - 0.5) ** 2) * 2.0
    a[..., 3] *= np.clip((1.0 - d) * w * 0.5, 0.0, 1.0)


layout = []
for f in sorted(os.listdir(raw)):
    if not f.endswith(".png") or "pinkgems" in f:
        continue
    name = f[:-4]
    kind = name.split("_")[0]
    if kind not in WIDTH:
        continue
    a = load(os.path.join(raw, f))
    box = None
    if kind == "card":
        key_outside(a)
        box = key_window(a, (0.5, 0.33))
    elif kind == "medal":
        box = key_window(a, (0.5, 0.5))
    text = None
    if kind == "card":
        text = parchment(a)
    if kind in ("card", "medal", "panel", "button", "banner", "glyph"):
        a2, box = crop(a, box)
        if text:
            _, text = crop(a, text)
        a = a2
    if kind == "skill":
        round_mask(a)
    if box:
        layout.append(f"{name} {box[0]:.4f} {box[1]:.4f} {box[2]:.4f} {box[3]:.4f}")
    if text:
        layout.append(f"{name}_text {text[0]:.4f} {text[1]:.4f} {text[2]:.4f} {text[3]:.4f}")
    save(a, os.path.join(out, f), WIDTH[kind])
    print("UI", name, a.shape[1], a.shape[0], box)

with open(os.path.join(out, "ui_layout.txt"), "w") as fh:
    fh.write("\n".join(layout) + "\n")
print("UI done", len(layout))
