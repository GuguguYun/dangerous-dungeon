"""Tile images into one contact sheet (row-major, top-left first).

Run: blender --background --factory-startup --python scripts/contact_sheet.py -- <out.png> <cols> <tile> <img>...
"""
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
out, cols, tile, paths = argv[0], int(argv[1]), int(argv[2]), argv[3:]
rows = (len(paths) + cols - 1) // cols
sheet = np.ones((rows * tile, cols * tile, 4), dtype=np.float32)
for i, p in enumerate(paths):
    img = bpy.data.images.load(p)
    img.scale(tile, tile)
    px = np.empty(tile * tile * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(tile, tile, 4)  # Blender rows run bottom-up
    r, c = i // cols, i % cols
    y0 = (rows - 1 - r) * tile
    sheet[y0:y0 + tile, c * tile:(c + 1) * tile] = px
out_img = bpy.data.images.new("sheet", cols * tile, rows * tile, alpha=True)
out_img.pixels.foreach_set(sheet.ravel())
out_img.filepath_raw = out
out_img.file_format = "PNG"
out_img.save()
print("SHEET", out)
