extends RefCounted
## Loads the Blender-made dungeon kit (one GLB, one object per piece) at runtime.
## GLTFDocument also works for GLBs downloaded later, e.g. Meshy assets.

var meshes := {}


func _init(path: String) -> void:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_file(path, state)
	if err != OK:
		push_error("kit load failed (%s): %s" % [err, path])
		return
	var root := doc.generate_scene(state)
	_collect(root)
	root.free()


func _collect(n: Node) -> void:
	if n is MeshInstance3D:
		meshes[String(n.name)] = n.mesh
	elif n is ImporterMeshInstance3D:
		meshes[String(n.name)] = n.mesh.get_mesh()
	for c in n.get_children():
		_collect(c)


func mesh(piece: String) -> Mesh:
	return meshes.get(piece)
