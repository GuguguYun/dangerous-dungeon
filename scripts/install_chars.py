#!/usr/bin/env python3
"""Copy rigged Meshy characters into Unity Resources and write clips.txt (clip names in file order).

Usage: scripts/install_chars.py [names...]
"""
import json
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
from meshy_assets import ANIMS, ASSETS  # noqa: E402

state = json.loads((ROOT / "assets/meshy/state.json").read_text())
names = sys.argv[1:] or [n for n, a in ASSETS.items() if a["kind"] == "char"]
for n in names:
    s = state.get(n, {})
    if not s.get("rig_fbx"):
        print(f"{n}: not rigged yet")
        continue
    dst = ROOT / "unity/Assets/Resources/Models/Chars" / n
    dst.mkdir(parents=True, exist_ok=True)
    shutil.copy(ROOT / s["rig_fbx"], dst / "rigged.fbx")
    for clip in ("walking", "running"):
        if s.get(f"rig_{clip}"):
            shutil.copy(ROOT / s[f"rig_{clip}"], dst / f"{clip}.fbx")
    if s.get("anim_file", "").endswith(".fbx"):
        shutil.copy(ROOT / s["anim_file"], dst / "actions.fbx")
        (dst / "clips.txt").write_text("\n".join(ANIMS[n]) + "\n")
    print(f"{n}: installed {sorted(p.name for p in dst.iterdir())}")
