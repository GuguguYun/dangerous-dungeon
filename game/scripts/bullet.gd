extends Node3D
## A projectile that ray-tests the segment it travels each frame, so fast shots never tunnel.

var velocity := Vector3.ZERO
var damage := 1
var target_group := "enemy"
var mask := 1
var color := Color(0.4, 0.9, 1.0)
var radius := 0.08
var life := 3.0


func _ready() -> void:
	var mi := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = radius
	sm.height = radius * 2.0
	mi.mesh = sm
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.emission_enabled = true
	mat.emission = color
	mat.emission_energy_multiplier = 3.0
	mi.material_override = mat
	add_child(mi)


func _physics_process(delta: float) -> void:
	life -= delta
	if life <= 0.0:
		queue_free()
		return
	var from := global_position
	var to := from + velocity * delta
	var q := PhysicsRayQueryParameters3D.create(from, to, mask)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if hit:
		var c = hit.collider
		if c and c.is_in_group(target_group) and c.has_method("take_damage"):
			c.take_damage(damage, velocity.normalized())
		_impact(hit.position)
		queue_free()
		return
	global_position = to


func _impact(pos: Vector3) -> void:
	var fx := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = radius * 3.0
	sm.height = radius * 6.0
	fx.mesh = sm
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.emission_enabled = true
	mat.emission = color
	mat.emission_energy_multiplier = 4.0
	fx.material_override = mat
	get_parent().add_child(fx)
	fx.global_position = pos
	var tw := fx.create_tween()
	tw.tween_property(fx, "scale", Vector3.ONE * 0.05, 0.15)
	tw.tween_callback(fx.queue_free)
