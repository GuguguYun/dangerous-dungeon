"""青苔地宫 textures from the Meshy pictures (scripts/meshy_assets.py images tex_floor_moss tex_wall_moss
decal_moss_hang decal_moss_patch) into unity/Assets/Resources/Kit.
Run: blender --background --factory-startup --python blender/moss_textures.py
Floor: the edit's moss only along the original's grout (its round blobs on the slabs read as slime or
poison), thicker in some places than others, made seamless again. Wall: the edit as it is (it tiles).
Cut-outs: cropped to what is opaque."""
import pathlib
import numpy as np, bpy
ROOT = pathlib.Path(__file__).resolve().parent.parent
IMG = str(ROOT / "assets/meshy/images") + "/"
KIT = str(ROOT / "unity/Assets/Resources/Kit") + "/"
def load(p):
    im = bpy.data.images.load(p); w, h = im.size
    return np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
def save(a, p):
    h, w = a.shape[:2]
    im = bpy.data.images.new("out", w, h, alpha=True)
    im.pixels[:] = np.ascontiguousarray(a, dtype=np.float32).ravel()
    im.filepath_raw = p; im.file_format = "PNG"; im.save()
def dilate(m, r):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r: out = np.maximum(out, np.roll(np.roll(m, dy, 0), dx, 1))
    return out
def blur(m, r):
    for ax in (0, 1):
        m = sum(np.roll(m, k, ax) for k in range(-r, r + 1)) / (2 * r + 1)
    return m
# Unity's kit texture vs the raw Meshy image: same picture?
A = load(IMG + "tex_floor.png"); K = load(KIT + "kit_tex_floor.png")
print("kit vs raw floor diff", float(np.abs(A[..., :3] - K[..., :3]).mean()))
B = load(IMG + "tex_floor_moss.png")
# Floor: moss only where the original has grout (dark lines), a few px onto the slab edges; the
# round blobs the edit painted on the slabs are left out (they read as slime or poison).
lum = A[..., :3].mean(2)
grout = (lum < 0.30).astype(np.float32)
m = np.clip(blur(dilate(grout, 6), 2), 0, 1)
# Thick in some places, thin in others: smooth noise made of whole waves, so it tiles too.
yy, xx = np.mgrid[0:A.shape[0], 0:A.shape[1]] / A.shape[0] * 2 * np.pi
rs = np.random.RandomState(7)
noise = sum(np.sin(fx * xx + fy * yy + rs.uniform(0, 6.3)) for fx, fy in [(1, 2), (2, -1), (3, 1), (1, -3), (2, 3)]) / 5
m = m * np.clip(0.8 + noise * 1.3, 0.3, 1.0)
C = A.copy(); C[..., :3] = A[..., :3] * (1 - m[..., None]) + B[..., :3] * m[..., None]; C[..., 3] = 1
# Seamless: near the edges use the copy shifted by half (the slab grid repeats every quarter, so it lines up).
n = C.shape[0]; e = 40
ramp = np.clip(np.minimum(np.arange(n), n - 1 - np.arange(n)) / e, 0, 1); ramp = ramp * ramp * (3 - 2 * ramp)
W = (ramp[:, None] * ramp[None, :])[..., None]
R = np.roll(np.roll(C, n // 2, 0), n // 2, 1)
C = C * W + R * (1 - W)
def seam(X): return f"lr {np.abs(X[:, 0, :3] - X[:, -1, :3]).mean():.3f} tb {np.abs(X[0, :, :3] - X[-1, :, :3]).mean():.3f}"
print("floor moss seam", seam(C), "| green share", float(((C[..., 1] > C[..., 0] * 1.25) & (C[..., 1] > C[..., 2] * 1.1)).mean()))
save(C, KIT + "kit_tex_floor_moss.png")
Wl = load(IMG + "tex_wall_moss.png"); Wl[..., 3] = 1
print("wall moss seam", seam(Wl))
save(Wl, KIT + "kit_tex_wall_moss.png")
# Cut-outs: crop to what is opaque (a little margin), alpha kept.
for src, dst in [("decal_moss_hang", "moss_hang"), ("decal_moss_patch", "moss_patch")]:
    D = load(IMG + src + ".png")
    ys, xs = np.where(D[..., 3] > 0.05)
    y0, y1, x0, x1 = max(ys.min() - 4, 0), min(ys.max() + 5, D.shape[0]), max(xs.min() - 4, 0), min(xs.max() + 5, D.shape[1])
    D = D[y0:y1, x0:x1]
    print(dst, "size", D.shape[1], "x", D.shape[0])
    save(D, KIT + dst + ".png")
