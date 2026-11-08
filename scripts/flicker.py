#!/usr/bin/env python3
"""Measure flicker in the frames written by the game's -flicker mode.

A pixel flickers when its brightness jumps up then down (or down then up) between consecutive frames
by more than THRESH each time; smooth camera motion only drifts one way across an edge. For each
sequence (play_*, close_*, run_*) prints the share of pixels that flicker often, and writes a heat map over
the first frame: <dir>/<tag>_heat.png (red = flickers in most frames).

Run: uv run --with numpy --with pillow python3 scripts/flicker.py <dir> [<dir> ...]
"""
import pathlib
import sys

import numpy as np
from PIL import Image

THRESH = 0.05  # brightness step (0..1) that counts as a jump


def load(path):
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    return a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32), a


for d in map(pathlib.Path, sys.argv[1:]):
    for tag in ("play", "close", "noshadow", "noao", "nopost", "run"):
        files = sorted(d.glob(f"{tag}_[0-9][0-9].png"))
        if len(files) < 4:
            continue
        frames = [load(f) for f in files]
        lum = np.stack([f[0] for f in frames])
        d1 = np.diff(lum, axis=0)
        big = np.abs(d1) > THRESH
        flip = (np.sign(d1[1:]) != np.sign(d1[:-1])) & big[1:] & big[:-1]
        rate = flip.mean(axis=0)  # share of frame triples where this pixel jumped back
        # Only pixels that are part of the picture's moving content count (the background sky is flat).
        busy = (np.abs(d1).max(axis=0) > THRESH)
        often = rate > 0.25
        print(f"FLICKER {d.name}/{tag}: {often.sum()} px flicker in >25% of frames "
              f"({often.sum() / max(1, busy.sum()) * 100:.2f}% of changing px), mean rate {rate[busy].mean():.3f}")
        base = frames[0][1] * 0.5
        heat = np.clip(rate / 0.5, 0, 1)
        out = base.copy()
        out[..., 0] = np.maximum(out[..., 0], heat)
        out[..., 1] *= 1 - heat
        out[..., 2] *= 1 - heat
        Image.fromarray((out * 255).astype(np.uint8)).save(d / f"{tag}_heat.png")
