"""Normalise a static Meshy model (weapon or prop) for Unity and render a top-down check image.

Run: blender --background --factory-startup --python blender/prep_static.py -- \
       <in.glb> <out.fbx> <weapon|bow|prop> <size_m> <rotations> <preview.png>

rotations: degrees about global axes applied in order, e.g. "z90" or "y45,x90" ("-" for none).
weapon: after rotating, the barrel/blade must run along -Y (muzzle/tip at -Y); scaled so its
        largest dimension is size_m; origin at the grip (30% from the rear, vertical centre).
bow:    held upright, shooting along -Y; scaled by its largest dimension; origin at the centre.
prop:   scaled so its largest dimension is size_m; origin at the bottom centre.
With the FBX settings below Blender (x, y, z) lands in Unity as (-x, z, -y) (measured with an axis
probe), so Blender -Y is Unity forward (+Z) and Blender +Z is Unity up.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, kind, size = argv[0], argv[1], argv[2], float(argv[3])
rot = Matrix.Identity(4)
for step in ([] if argv[4] == "-" else argv[4].split(",")):
    rot = Matrix.Rotation(math.radians(float(step[1:])), 4, step[0].upper()) @ rot
preview = argv[5]

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in meshes:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in list(bpy.context.scene.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = "model"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

obj.data.transform(rot)
xs, ys, zs = zip(*[v.co for v in obj.data.vertices])
lo, hi = Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))
dims = hi - lo
if kind == "weapon":
    scale = size / max(dims)
    pivot = Vector(((lo.x + hi.x) / 2, hi.y - 0.3 * dims.y, (lo.z + hi.z) / 2))
elif kind == "bow":
    scale = size / max(dims)
    pivot = (lo + hi) / 2
else:
    scale = size / max(dims)
    pivot = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
obj.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-pivot))
obj.data.update()
bpy.context.view_layer.update()

dims = obj.dimensions
print(f"PREP {kind} faces={len(obj.data.polygons)} dims=({dims.x:.2f},{dims.y:.2f},{dims.z:.2f})")

# Textures go out as separate PNGs beside the FBX (Unity ignores FBX-embedded ones), named per model
# so several models in one folder never overwrite each other's "Image_0.png".
stem = os.path.splitext(os.path.basename(dst))[0]
for i, img in enumerate(bpy.data.images):
    if img.source == "FILE" or img.packed_file:
        img.name = f"{stem}_tex{i}"
        img.filepath_raw = os.path.join(os.path.dirname(dst), f"{stem}_tex{i}.png")
        img.file_format = "PNG"
        img.save()
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={"MESH"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="OFF", add_leaf_bones=False,
                         path_mode="RELATIVE")  # keep Meshy's own normals

# Top-down check render: +Y points up in the image, so a weapon's muzzle must be at the bottom.
scene = bpy.context.scene
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = max(dims.x, dims.y) * 1.3
cam.location = (0, -dims.y * 0.2 if kind == "weapon" else 0, 10)
cam.data.ortho_scale = max(dims) * 1.3
cam.rotation_euler = (0, 0, 0)
scene.camera = cam
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.color_type = "TEXTURE"
scene.display.shading.light = "FLAT"
scene.render.resolution_x = scene.render.resolution_y = 400
scene.render.filepath = preview
bpy.ops.render.render(write_still=True)
print("PREVIEW", preview)
