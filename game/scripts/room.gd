extends Node3D
## A combat room: locks its gates when the player walks in, runs its enemy waves,
## then reopens the gates and reports `cleared`.

signal cleared(room: Node3D)

const Enemy = preload("res://scripts/enemy.gd")

enum State { IDLE, FIGHTING, CLEARED }

var state := State.IDLE
var rect_min := Vector3.ZERO
var rect_max := Vector3.ZERO
var gates: Array[StaticBody3D] = []
var blocked: Array[Vector3] = []
var waves := [4, 5]
var wave_i := 0
var alive := 0
var player: Node3D
var hud


func center() -> Vector3:
	return (rect_min + rect_max) / 2.0


func _ready() -> void:
	var area := Area3D.new()
	area.collision_layer = 0
	area.collision_mask = 2
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	var margin := 2.0
	box.size = Vector3(rect_max.x - rect_min.x - margin * 2.0, 3.0, rect_max.z - rect_min.z - margin * 2.0)
	cs.shape = box
	area.add_child(cs)
	area.position = center() + Vector3(0, 1.5, 0)
	add_child(area)
	area.body_entered.connect(_on_body_entered)
	_set_gates(false)


func _on_body_entered(body: Node3D) -> void:
	if state == State.IDLE and body.is_in_group("player"):
		state = State.FIGHTING
		_set_gates(true)
		hud.show_message("房间已锁定！清空所有敌人", 2.0)
		get_tree().create_timer(0.8).timeout.connect(_spawn_wave)


func gates_closed() -> bool:
	return gates.size() > 0 and gates[0].visible


func _set_gates(closed: bool) -> void:
	for g in gates:
		g.visible = closed
		for c in g.get_children():
			if c is CollisionShape3D:
				c.set_deferred("disabled", not closed)


func _spawn_wave() -> void:
	var n: int = waves[wave_i]
	for i in n:
		var e := Enemy.new()
		e.target = player
		e.died.connect(_on_enemy_died)
		add_child(e)
		e.global_position = _spawn_point()
		e.scale = Vector3.ONE * 0.05
		e.create_tween().tween_property(e, "scale", Vector3.ONE, 0.35)
	alive += n


func _spawn_point() -> Vector3:
	var lo := rect_min + Vector3(2.5, 0, 2.5)
	var hi := rect_max - Vector3(2.5, 0, 2.5)
	var best := center()
	for attempt in 40:
		var p := Vector3(randf_range(lo.x, hi.x), 0.1, randf_range(lo.z, hi.z))
		if player and p.distance_to(player.global_position) < 7.0:
			continue
		var ok := true
		for b in blocked:
			if Vector2(p.x - b.x, p.z - b.z).length() < 1.8:
				ok = false
				break
		if ok:
			return p
		best = p
	return best


func _on_enemy_died() -> void:
	alive -= 1
	if alive > 0:
		return
	wave_i += 1
	if wave_i < waves.size():
		hud.show_message("第 %d 波" % (wave_i + 1), 1.5)
		get_tree().create_timer(1.0).timeout.connect(_spawn_wave)
	else:
		state = State.CLEARED
		_set_gates(false)
		hud.show_message("房间已清空！", 2.5)
		cleared.emit(self)
