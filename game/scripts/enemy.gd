extends CharacterBody3D
## Placeholder ranged enemy: keeps mid distance, strafes, fires slow orbs when it can see the player.

signal died

const Bullet = preload("res://scripts/bullet.gd")

var hp := 3
var speed := 3.2
var target: Node3D
var shoot_timer := 0.0
var strafe_sign := 1.0
var strafe_timer := 0.0
var flash := 0.0
var dead := false
var mat: StandardMaterial3D
var eye: Node3D


func _ready() -> void:
	add_to_group("enemy")
	collision_layer = 4
	collision_mask = 1 | 2 | 4

	var col := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.45
	cap.height = 1.6
	col.shape = cap
	col.position.y = 0.8
	add_child(col)

	var body := MeshInstance3D.new()
	var cm := CapsuleMesh.new()
	cm.radius = 0.45
	cm.height = 1.6
	body.mesh = cm
	body.position.y = 0.8
	mat = StandardMaterial3D.new()
	mat.albedo_color = Color(0.8, 0.25, 0.2)
	body.material_override = mat
	add_child(body)

	eye = MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.13
	sm.height = 0.26
	eye.mesh = sm
	eye.position = Vector3(0, 1.25, -0.4)
	var em := StandardMaterial3D.new()
	em.albedo_color = Color(1, 0.9, 0.3)
	em.emission_enabled = true
	em.emission = Color(1, 0.8, 0.2)
	em.emission_energy_multiplier = 2.0
	eye.material_override = em
	add_child(eye)

	shoot_timer = randf_range(1.0, 2.5)
	strafe_sign = 1.0 if randf() < 0.5 else -1.0
	strafe_timer = randf_range(1.5, 3.0)


func _physics_process(delta: float) -> void:
	if dead:
		return
	if not is_on_floor():
		velocity.y -= 20.0 * delta
	if target == null or not is_instance_valid(target) or target.dead:
		velocity.x = 0
		velocity.z = 0
		move_and_slide()
		return

	var to := target.global_position - global_position
	to.y = 0
	var dist := to.length()
	var dir := Vector3.ZERO
	if dist > 0.01:
		if dist > 8.0:
			dir = to / dist
		elif dist < 4.5:
			dir = -to / dist
		else:
			dir = to.cross(Vector3.UP).normalized() * strafe_sign
	strafe_timer -= delta
	if strafe_timer <= 0.0:
		strafe_sign = -strafe_sign
		strafe_timer = randf_range(1.5, 3.0)
	velocity.x = dir.x * speed
	velocity.z = dir.z * speed
	move_and_slide()
	if dist > 0.1:
		look_at(Vector3(target.global_position.x, global_position.y, target.global_position.z), Vector3.UP)

	shoot_timer -= delta
	if shoot_timer <= 0.0:
		shoot_timer = randf_range(1.6, 2.6) if _can_see_target() else 0.5
		if shoot_timer > 0.5:
			_shoot()

	if flash > 0.0:
		flash -= delta
		mat.albedo_color = Color(1, 1, 1) if flash > 0.0 else Color(0.8, 0.25, 0.2)


func _target_point() -> Vector3:
	return target.global_position + Vector3(0, 1.0, 0)


func _can_see_target() -> bool:
	var q := PhysicsRayQueryParameters3D.create(eye.global_position, _target_point(), 1)
	return get_world_3d().direct_space_state.intersect_ray(q).is_empty()


func _shoot() -> void:
	var from := eye.global_position
	var b := Bullet.new()
	b.velocity = (_target_point() - from).normalized() * 11.0
	b.target_group = "player"
	b.mask = 1 | 2
	b.color = Color(1.0, 0.55, 0.15)
	b.radius = 0.16
	b.life = 4.0
	get_parent().add_child(b)
	b.global_position = from


func take_damage(amount: int, hit_dir: Vector3) -> void:
	if dead:
		return
	hp -= amount
	flash = 0.08
	mat.albedo_color = Color(1, 1, 1)
	velocity += Vector3(hit_dir.x, 0, hit_dir.z) * 2.0
	if hp <= 0:
		dead = true
		died.emit()
		var tw := create_tween()
		tw.tween_property(self, "scale", Vector3(1.4, 0.1, 1.4), 0.15)
		tw.tween_callback(queue_free)
