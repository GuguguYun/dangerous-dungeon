#!/usr/bin/env python3
"""Run blender/prep_static.py on every remeshed static Meshy asset and drop the FBX into Unity.

Rotations (degrees) turn each model so the muzzle/blade tip points along Blender -Y (Unity forward);
they come from looking at the raw front renders in assets/meshy/previews/raw.
Usage: scripts/prep_assets.py [names...]
"""
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
from meshy_assets import ASSETS  # noqa: E402

ROTATION = {
    "pistol": "z90", "smg": "z90", "shotgun": "z90", "rifle": "z90", "launcher": "z90",
    "sword": "x90", "hammer": "z-90",
    "laser": "z90", "flamer": "z90", "grenade": "z90", "missile": "z90", "boomerang": "z90",
    "thunder": "x90", "frost": "x90", "daggers": "x90", "spear": "y45,x90", "bow": "z90",
    "raven_blade": "z90", "oath_sword": "x90", "star_staff": "x90", "dawn_scepter": "x90",
    "raven_darts": "y-45,x-90",  # made lying diagonally, point down-left: stand it up, then point first
    "proj_ice_shard": "z90",  # point first (Unity forward); the skulls already face forward
}
state = json.loads((ROOT / "assets/meshy/state.json").read_text())
names = sys.argv[1:] or [n for n, a in ASSETS.items() if a["kind"] in ("weapon", "prop")]
for n in names:
    a, s = ASSETS[n], state.get(n, {})
    if not s.get("remesh_glb"):
        print(f"{n}: not remeshed yet")
        continue
    folder = "Weapons" if a["kind"] == "weapon" else "Props"
    dst = ROOT / "unity/Assets/Resources/Models" / folder / f"{n}.fbx"
    dst.parent.mkdir(parents=True, exist_ok=True)
    preview = ROOT / "assets/meshy/previews/prep" / f"{n}.png"
    preview.parent.mkdir(parents=True, exist_ok=True)
    rot = ROTATION.get(n, "-")
    size = a.get("length") or a.get("size")
    mode = "bow" if n == "bow" else a["kind"] if a["kind"] == "weapon" else "prop"
    out = subprocess.run(["blender", "--background", "--factory-startup", "--python",
                          str(ROOT / "blender/prep_static.py"), "--", str(ROOT / s["remesh_glb"]), str(dst),
                          mode, str(size), rot, str(preview)], capture_output=True, text=True).stdout
    line = next((l for l in out.splitlines() if l.startswith("PREP")), "PREP failed\n" + out[-800:])
    print(f"{n}: {line}")
