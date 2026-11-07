"""Build the dungeon kit (modular pieces) and export it as one GLB for Godot.

Run: blender --background --factory-startup --python blender/build_kit.py -- <out_dir> [texture_dir]
Writes dungeon_kit.blend, dungeon_kit.glb and dungeon_kit.fbx (the one Unity uses).
With texture_dir (Meshy tileable images tex_floor/tex_wall/tex_trim/tex_wood.png), stone and wood
materials get those images, mapped by box projection in metres so the pattern runs on across pieces.

Every piece sits at the world origin, named exactly as Godot looks it up.
Blender is Z-up; the glTF exporter turns it into Godot's Y-up.
Walls run along X and are symmetric in Y, so the axis flip does not matter.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix

TILE = 4.0

COLORS = {
    "grout": ((0.08, 0.08, 0.10), 0.0),
    "stone_a": ((0.32, 0.34, 0.40), 0.0),
    "stone_b": ((0.27, 0.29, 0.35), 0.0),
    "wall": ((0.36, 0.33, 0.42), 0.0),
    "trim": ((0.22, 0.20, 0.28), 0.0),
    "frame": ((0.45, 0.30, 0.18), 0.0),
    "gate": ((0.90, 0.25, 0.15), 2.0),
    "wood": ((0.55, 0.36, 0.20), 0.0),
    "wood_dark": ((0.35, 0.22, 0.12), 0.0),
    "portal_ring": ((0.60, 0.40, 1.00), 3.0),
    "portal_core": ((0.45, 0.25, 1.00), 1.5),
    "iron": ((0.06, 0.06, 0.08), 0.0),
    "embers": ((1.00, 0.35, 0.08), 2.5),
    "steel": ((0.25, 0.27, 0.32), 0.0),
}


# material -> (Meshy texture, metres per texture repeat)
TEXTURED = {
    "stone_a": ("tex_floor", 4.0), "stone_b": ("tex_floor", 4.0), "grout": ("tex_floor", 4.0),
    "wall": ("tex_wall", 2.0), "trim": ("tex_trim", 1.5),
    "frame": ("tex_wood", 1.0), "wood": ("tex_wood", 1.0), "wood_dark": ("tex_wood", 1.0),
}
TEX_DIR = None


def make_material(name):
    color, emission = COLORS[name]
    if TEX_DIR and name in TEXTURED:
        tex, _ = TEXTURED[name]
        mat = bpy.data.materials.new(name)
        try:
            mat.use_nodes = True
        except AttributeError:
            pass
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        img = bpy.data.images.load(os.path.join(TEX_DIR, tex + ".png"), check_existing=True)
        img.name = "kit_" + tex
        node.image = img
        mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.9
        return mat
    mat = bpy.data.materials.new(name)
    try:
        mat.use_nodes = True
    except AttributeError:
        pass
    mat.diffuse_color = (*color, 1.0)
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def add_box(bm, mn, mx, mi):
    x0, y0, z0 = mn
    x1, y1, z1 = mx
    v = [bm.verts.new(p) for p in [
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
    ]]
    for f in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        face = bm.faces.new([v[i] for i in f])
        face.material_index = mi


def box_uvs(bm, mats):
    """Box projection in metres: each face takes the two axes it faces across."""
    uv = bm.loops.layers.uv.verify()
    for face in bm.faces:
        tex = TEXTURED.get(mats[face.material_index])
        size = tex[1] if tex else 1.0
        n = face.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        u_axis, v_axis = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[ax]
        for loop in face.loops:
            co = loop.vert.co
            loop[uv].uv = (co[u_axis] / size, co[v_axis] / size)


def piece(name, mats, boxes):
    """boxes: list of (min, max, material name)."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for mn, mx, mat in boxes:
        add_box(bm, mn, mx, mats.index(mat))
    bm.normal_update()
    box_uvs(bm, mats)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(MATS[m])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def floor_tile():
    h = TILE / 2
    if TEX_DIR:  # the texture already draws the slabs and grout
        piece("floor_tile", ["stone_a"], [((-h, -h, -0.30), (h, h, 0.0), "stone_a")])
        return
    boxes = [((-h, -h, -0.30), (h, h, -0.03), "grout")]
    for i in range(2):
        for j in range(2):
            x0 = -h + i * h + 0.05
            y0 = -h + j * h + 0.05
            mat = "stone_a" if (i + j) % 2 == 0 else "stone_b"
            boxes.append(((x0, y0, -0.10), (x0 + h - 0.10, y0 + h - 0.10, 0.0), mat))
    piece("floor_tile", ["grout", "stone_a", "stone_b"], boxes)


def wall():
    h = TILE / 2
    piece("wall", ["wall", "trim"], [
        ((-h, -0.30, 0.0), (h, 0.30, 4.0), "wall"),
        ((-h, -0.38, 0.0), (h, 0.38, 0.35), "trim"),
        ((-h, -0.40, 4.0), (h, 0.40, 4.3), "trim"),
    ])


def wall_door():
    h = TILE / 2
    d = 1.2  # half opening width
    e = 0.02  # the frame stands 2 cm proud of the wall so no two visible faces share a plane (z-fighting flicker)
    piece("wall_door", ["wall", "trim", "frame"], [
        ((-h, -0.30, 0.0), (-d, 0.30, 4.0), "wall"),
        ((d, -0.30, 0.0), (h, 0.30, 4.0), "wall"),
        ((-d, -0.30, 3.0), (d, 0.30, 4.0), "wall"),
        ((-h, -0.38, 0.0), (-d - 0.2, 0.38, 0.35), "trim"),
        ((d + 0.2, -0.38, 0.0), (h, 0.38, 0.35), "trim"),
        ((-h, -0.40, 4.0), (h, 0.40, 4.3), "trim"),
        ((-d - 0.2, -0.42, 0.0), (-d + e, 0.42, 3.0 - e), "frame"),
        ((d - e, -0.42, 0.0), (d + 0.2, 0.42, 3.0 - e), "frame"),
        ((-d - 0.2, -0.42, 3.0 - e), (d + 0.2, 0.42, 3.2), "frame"),
    ])


def pillar():
    piece("pillar", ["trim"], [
        ((-0.45, -0.45, 0.0), (0.45, 0.45, 4.6), "trim"),
        ((-0.55, -0.55, 0.0), (0.55, 0.55, 0.4), "trim"),
        ((-0.55, -0.55, 4.6), (0.55, 0.55, 4.85), "trim"),
    ])


def gate():
    boxes = []
    for k in range(6):
        x = -1.0 + k * 0.4
        boxes.append(((x - 0.06, -0.06, 0.0), (x + 0.06, 0.06, 3.0), "gate"))
    for z in (1.0, 2.0):
        boxes.append(((-1.2, -0.08, z), (1.2, 0.08, z + 0.12), "gate"))
    piece("gate", ["gate"], boxes)


def crate():
    s = 0.6
    piece("crate", ["wood", "wood_dark"], [
        ((-s, -s, 0.0), (s, s, 2 * s), "wood"),
        ((-s - 0.02, -s - 0.02, 0.0), (s + 0.02, s + 0.02, 0.12), "wood_dark"),
        ((-s - 0.02, -s - 0.02, 2 * s - 0.12), (s + 0.02, s + 0.02, 2 * s + 0.01), "wood_dark"),
    ])


def trap_grate():
    """2 x 2 m fire-pit grate: an iron frame and bars over a glowing ember bed (all above the floor)."""
    boxes = [((-0.9, -0.9, 0.01), (0.9, 0.9, 0.02), "embers")]  # inset: its edges hide inside the frame
    boxes += [((-1.0, -1.0, 0.0), (1.0, -0.88, 0.07), "iron"), ((-1.0, 0.88, 0.0), (1.0, 1.0, 0.07), "iron"),
              ((-1.0, -0.88, 0.0), (-0.88, 0.88, 0.07), "iron"), ((0.88, -0.88, 0.0), (1.0, 0.88, 0.07), "iron")]
    for k in range(6):
        x = -0.75 + k * 0.3
        boxes.append(((x - 0.05, -0.88, 0.025), (x + 0.05, 0.88, 0.065), "iron"))
    piece("trap_grate", ["iron", "embers"], boxes)


def spike_plate():
    """1.8 m steel plate with a 3 x 3 grid of dark holes the spikes come out of."""
    boxes = [((-0.9, -0.9, 0.0), (0.9, 0.9, 0.05), "steel")]
    for i in range(3):
        for j in range(3):
            x, y = -0.55 + i * 0.55, -0.55 + j * 0.55
            boxes.append(((x - 0.12, y - 0.12, 0.05), (x + 0.12, y + 0.12, 0.055), "iron"))
    piece("spike_plate", ["steel", "iron"], boxes)


def spikes():
    """Nine steel cones; Unity raises and lowers this object."""
    mesh = bpy.data.meshes.new("spikes")
    bm = bmesh.new()
    for i in range(3):
        for j in range(3):
            x, y = -0.55 + i * 0.55, -0.55 + j * 0.55
            m = Matrix.Translation((x, y, 0.3))
            bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.1, radius2=0.0, depth=0.6, matrix=m)
    for f in bm.faces:
        f.material_index = 0
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(MATS["steel"])
    obj = bpy.data.objects.new("spikes", mesh)
    bpy.context.scene.collection.objects.link(obj)


def portal():
    bpy.ops.mesh.primitive_torus_add(major_radius=1.2, minor_radius=0.12,
                                     location=(0, 0, 1.6), rotation=(math.pi / 2, 0, 0))
    ring = bpy.context.active_object
    ring.name = "portal_ring"
    ring.data.materials.append(MATS["portal_ring"])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=1.1, fill_type="NGON",
                                      location=(0, 0, 1.6), rotation=(math.pi / 2, 0, 0))
    core = bpy.context.active_object
    core.name = "portal_core"
    core.data.materials.append(MATS["portal_core"])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def report_coplanar():
    """Faces facing the same way in the same plane with overlapping area flicker (z-fighting)."""
    found = 0
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        rects = []
        for f in obj.data.polygons:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            if abs(abs(n[ax]) - 1) > 1e-4:
                continue  # only the axis-aligned box faces can be checked this way
            plane = obj.data.vertices[f.vertices[0]].co[ax]
            if ax == 2 and n[ax] < 0 and abs(plane) < 1e-4:
                continue  # bottom faces sit on the floor: never seen
            if ax == 0 and obj.name.startswith("wall") and abs(abs(plane) - TILE / 2) < 1e-4:
                continue  # wall ends always end inside a corner pillar
            us = [v for i in range(3) if i != ax for v in [i]]
            cos = [obj.data.vertices[i].co for i in f.vertices]
            lo = [min(c[u] for c in cos) for u in us]
            hi = [max(c[u] for c in cos) for u in us]
            rects.append((ax, n[ax] > 0, round(cos[0][ax], 4), lo, hi, f.index))
        for i, a in enumerate(rects):
            for b in rects[i + 1:]:
                if a[:3] != b[:3]:
                    continue
                overlap = all(min(a[4][k], b[4][k]) - max(a[3][k], b[3][k]) > 1e-4 for k in range(2))
                if overlap:
                    found += 1
                    print(f"ZFIGHT {obj.name}: faces {a[5]} and {b[5]} share plane axis={a[0]} at {a[2]}")
    print(f"ZFIGHT check: {found} coplanar overlapping face pairs")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else "assets/kit")
    os.makedirs(out_dir, exist_ok=True)
    global TEX_DIR
    TEX_DIR = os.path.abspath(argv[1]) if len(argv) > 1 else None

    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    global MATS
    MATS = {name: make_material(name) for name in COLORS}

    floor_tile()
    wall()
    wall_door()
    pillar()
    gate()
    crate()
    portal()
    trap_grate()
    spike_plate()
    spikes()

    report_coplanar()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "dungeon_kit.blend"))
    bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, "dungeon_kit.glb"),
                              export_format="GLB", export_apply=True)
    # Textures travel as separate PNGs beside the FBX (Unity ignores FBX-embedded ones).
    for img in bpy.data.images:
        if img.name.startswith("kit_"):
            img.filepath_raw = os.path.join(out_dir, img.name + ".png")
            img.file_format = "PNG"
            img.save()
    # Unity reads FBX natively; bake the axis conversion so every piece imports unrotated.
    bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, "dungeon_kit.fbx"),
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", add_leaf_bones=False, path_mode="RELATIVE")
    for obj in bpy.data.objects:
        print("KIT", obj.name, len(obj.data.polygons), "faces",
              [round(v, 2) for v in obj.dimensions])


MATS = {}
main()
