extends CharacterBody3D
## Third-person shooter controller: camera behind the shoulder (Naraka-style),
## mouse look, crosshair aim, dash with brief invulnerability.

signal hp_changed(hp: int, max_hp: int)
signal died

const Bullet = preload("res://scripts/bullet.gd")

const SPEED := 6.0
const ACCEL := 60.0
const DASH_SPEED := 16.0
const DASH_TIME := 0.22
const DASH_COOLDOWN := 0.6
const JUMP_VELOCITY := 6.5
const GRAVITY := 20.0
const MOUSE_SENS := 0.0025
const FIRE_INTERVAL := 0.14

var max_hp := 6
var hp := 6
var dead := false
var yaw := 0.0
var pitch := -0.15
var control_enabled := true

var cam_yaw: Node3D
var cam_pitch: Node3D
var camera: Camera3D
var model: Node3D
var muzzle: Node3D
var body_mat: StandardMaterial3D

var fire_cd := 0.0
var dash_t := 0.0
var dash_cd := 0.0
var dash_dir := Vector3.ZERO
var invuln := 0.0


func _ready() -> void:
	add_to_group("player")
	collision_layer = 2
	collision_mask = 1 | 4

	var col := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.4
	cap.height = 1.7
	col.shape = cap
	col.position.y = 0.85
	add_child(col)

	model = Node3D.new()
	add_child(model)
	body_mat = StandardMaterial3D.new()
	body_mat.albedo_color = Color(0.25, 0.5, 0.95)
	_part(CapsuleMesh.new(), Vector3(0, 0.85, 0), body_mat, func(m): m.radius = 0.4; m.height = 1.7)
	var visor := StandardMaterial3D.new()
	visor.albedo_color = Color(0.3, 1.0, 1.0)
	visor.emission_enabled = true
	visor.emission = Color(0.3, 1.0, 1.0)
	_part(BoxMesh.new(), Vector3(0, 1.45, -0.36), visor, func(m): m.size = Vector3(0.5, 0.16, 0.1))
	var gun := StandardMaterial3D.new()
	gun.albedo_color = Color(0.15, 0.15, 0.18)
	_part(BoxMesh.new(), Vector3(0.38, 1.15, -0.35), gun, func(m): m.size = Vector3(0.12, 0.16, 0.7))
	muzzle = Node3D.new()
	muzzle.position = Vector3(0.38, 1.15, -0.75)
	model.add_child(muzzle)

	cam_yaw = Node3D.new()
	cam_yaw.position = Vector3(0, 1.6, 0)
	add_child(cam_yaw)
	cam_pitch = Node3D.new()
	cam_yaw.add_child(cam_pitch)
	var spring := SpringArm3D.new()
	spring.position = Vector3(0.6, 0.2, 0)
	spring.spring_length = 3.8
	spring.collision_mask = 1
	spring.margin = 0.2
	var ss := SphereShape3D.new()
	ss.radius = 0.2
	spring.shape = ss
	cam_pitch.add_child(spring)
	camera = Camera3D.new()
	camera.fov = 70.0
	spring.add_child(camera)
	camera.current = true
	_apply_look()


func _part(mesh: PrimitiveMesh, pos: Vector3, mat: Material, setup: Callable) -> void:
	setup.call(mesh)
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.position = pos
	mi.material_override = mat
	model.add_child(mi)


func set_look(p_yaw: float, p_pitch: float) -> void:
	yaw = p_yaw
	pitch = p_pitch
	_apply_look()


func _apply_look() -> void:
	cam_yaw.rotation.y = yaw
	cam_pitch.rotation.x = pitch


func _unhandled_input(event: InputEvent) -> void:
	if not control_enabled:
		return
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		yaw -= event.relative.x * MOUSE_SENS
		pitch = clamp(pitch - event.relative.y * MOUSE_SENS, -1.2, 0.6)
		_apply_look()
	elif event is InputEventMouseButton and event.pressed and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	elif event.is_action_pressed("ui_cancel"):
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _physics_process(delta: float) -> void:
	fire_cd -= delta
	dash_cd -= delta
	invuln -= delta

	var forward := Vector3(-sin(yaw), 0, -cos(yaw))
	var right := Vector3(cos(yaw), 0, -sin(yaw))
	var input := Vector2.ZERO
	if control_enabled and not dead:
		input = Input.get_vector("move_left", "move_right", "move_forward", "move_back")
	var dir := (right * input.x + forward * -input.y)
	if dir.length() > 1.0:
		dir = dir.normalized()

	if control_enabled and not dead and Input.is_action_just_pressed("dash") and dash_cd <= 0.0:
		dash_dir = dir.normalized() if dir.length() > 0.1 else forward
		dash_t = DASH_TIME
		dash_cd = DASH_COOLDOWN
		invuln = max(invuln, DASH_TIME + 0.05)

	var horizontal := Vector3(velocity.x, 0, velocity.z)
	if dash_t > 0.0:
		dash_t -= delta
		horizontal = dash_dir * DASH_SPEED
	else:
		horizontal = horizontal.move_toward(dir * SPEED, ACCEL * delta)
	velocity.x = horizontal.x
	velocity.z = horizontal.z

	if not is_on_floor():
		velocity.y -= GRAVITY * delta
	elif control_enabled and not dead and Input.is_action_just_pressed("jump"):
		velocity.y = JUMP_VELOCITY
	move_and_slide()

	if not dead:
		model.rotation.y = lerp_angle(model.rotation.y, yaw, clamp(15.0 * delta, 0.0, 1.0))
		if control_enabled and Input.is_action_pressed("shoot") and fire_cd <= 0.0 \
				and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
			fire()

	if invuln > 0.0 and not dead and dash_t <= 0.0:
		body_mat.albedo_color = Color(1, 0.4, 0.4) if int(invuln * 20) % 2 == 0 else Color(0.25, 0.5, 0.95)
	else:
		body_mat.albedo_color = Color(0.25, 0.5, 0.95)


func aim_point() -> Vector3:
	var center := get_viewport().get_visible_rect().size / 2.0
	var ray_dir := camera.project_ray_normal(center)
	# Start at the player's depth so walls behind the player never catch the aim ray.
	var origin := camera.project_ray_origin(center)
	origin += ray_dir * camera.global_position.distance_to(cam_yaw.global_position)
	var q := PhysicsRayQueryParameters3D.create(origin, origin + ray_dir * 200.0, 1 | 4)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	return hit.position if hit else origin + ray_dir * 200.0


func fire() -> void:
	fire_cd = FIRE_INTERVAL
	var from := muzzle.global_position
	var b := Bullet.new()
	b.velocity = (aim_point() - from).normalized() * 40.0
	b.target_group = "enemy"
	b.mask = 1 | 4
	b.color = Color(0.4, 0.9, 1.0)
	get_parent().add_child(b)
	b.global_position = from


func take_damage(amount: int, _dir: Vector3) -> void:
	if dead or invuln > 0.0:
		return
	hp = max(hp - amount, 0)
	invuln = 0.6
	hp_changed.emit(hp, max_hp)
	if hp == 0:
		dead = true
		model.rotation.x = -PI / 2
		model.position.y = 0.4
		died.emit()
