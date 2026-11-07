"""Render each GLB from the front and at three-quarter view (textured, flat light) for a quick check.

Run: blender --background --factory-startup --python blender/preview_models.py -- <out_dir> <a.glb> [b.glb ...]
Writes <out_dir>/<parent-folder-name>_front.png and _34.png per model.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out_dir, paths = argv[0], argv[1:]
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.color_type = "TEXTURE"
scene.display.shading.light = "STUDIO"
scene.render.resolution_x = scene.render.resolution_y = 360
scene.world.color = (0.75, 0.75, 0.78)

for path in paths:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in scene.objects if o.type == "MESH"]
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center, radius = (lo + hi) / 2, (hi - lo).length / 2
    faces = sum(len(o.data.polygons) for o in meshes)
    name = os.path.basename(os.path.dirname(path))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = radius * 2.2
    for tag, yaw in (("front", 0.0), ("34", 40.0)):
        a = math.radians(yaw)
        # Blender front view looks along +Y, so the camera sits at -Y.
        cam.location = center + Vector((math.sin(a), -math.cos(a), 0.15)) * radius * 4
        direction = center - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"{name}_{tag}.png")
        bpy.ops.render.render(write_still=True)
    print(f"PREVIEW {name} faces={faces} size=({hi.x - lo.x:.2f},{hi.y - lo.y:.2f},{hi.z - lo.z:.2f})")
