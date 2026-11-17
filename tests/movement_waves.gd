extends SceneTree

func _initialize():
	create_timer(15).timeout.connect(func(): quit(1))
	_run.call_deferred()

func _state(actor: Dictionary, ack: int) -> PackedFloat32Array:
	var direction: Vector3 = actor.get("dash_direction",Vector3.ZERO)
	return PackedFloat32Array([actor.p.x,actor.p.y,actor.p.z,actor.hp,actor.cd,actor.dash,actor.y,actor.v,actor.get("max_hp",100),actor.get("speed",6.5),actor.get("fire_interval",0.16),ack,actor.get("dash_left",0),direction.x,direction.y,direction.z])

func _run():
	var game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_process(false)
	game.set_physics_process(false)
	game.test_mode = false
	game._start_mission(0)
	game.local_id = 1
	var authoritative: Dictionary = game.actors[1].duplicate(true)
	var origin: Vector3 = authoritative.p
	# Simulate 12 local frames and a delayed server snapshot acknowledging frame 9.
	for seq in range(1,13):
		var packet = {"seq":seq,"m":Vector2(0,-1),"yaw":0.0}
		game.pending_movement.append({"packet":packet,"dt":1.0/60})
		game._move_actor(game.actors[1],packet,1.0/60)
		if seq <= 9: game._move_actor(authoritative,packet,1.0/60)
	assert(game.actors[1].p.distance_to(origin) > 1.0)
	var predicted: Vector3 = game.actors[1].p
	game._snapshot(PackedInt32Array([1]),_state(authoritative,9),PackedFloat32Array(),0,0,0)
	assert(game.pending_movement.size() == 3)
	assert(game.actors[1].p.distance_to(predicted) < 0.0001)
	# A speed buff from the server is used when replaying unacknowledged movement.
	authoritative.speed = 9.75
	game._snapshot(PackedInt32Array([1]),_state(authoritative,9),PackedFloat32Array(),0,0,0)
	assert(game.actors[1].p.distance_to(authoritative.p) > 0.48)
	# Jump, dash and arena limits share the exact same movement step on both sides.
	var first = authoritative.duplicate(true)
	var second = authoritative.duplicate(true)
	var action = {"m":Vector2(1,1),"yaw":0.7,"dash":true,"jump":true}
	for tick in range(90):
		game._move_actor(first,action,1.0/60)
		game._move_actor(second,action,1.0/60)
	assert(first.p.is_equal_approx(second.p) and first.p.y >= 0 and absf(first.p.x) <= 21 and absf(first.p.z) <= 21)
	game.pending_movement.clear()
	game.network = "offline"
	# Every region has 20 waves, exactly one boss on 10/20, capped ordinary speed.
	for region in range(3):
		assert(game.MISSIONS[region][4] == 20)
		game.selected = region
		var previous_hp = 0.0
		var boss10_hp = 0.0
		for current_wave in range(1,21):
			game.wave = current_wave
			game.enemies.clear()
			game._spawn_wave()
			var bosses = 0
			for enemy in game.enemies.values():
				assert(is_equal_approx(enemy.hp,enemy.max_hp))
				if enemy.boss:
					bosses += 1
					if current_wave == 10: boss10_hp = enemy.hp
					if current_wave == 20: assert(enemy.hp > boss10_hp)
				else:
					assert(enemy.hp > previous_hp)
					assert(enemy.speed <= (2.1+region*0.3)*1.25+0.0001)
			previous_hp = (55.0+region*10)*(1+0.08*(current_wave-1))
			assert(bosses == (1 if current_wave in [10,20] else 0))
			assert(game.enemies.size() <= 24)
	# Boss health reaches the client and updates/hides the top HUD correctly.
	game._start_mission(0)
	game.wave = 10
	game.enemies.clear()
	game._spawn_wave()
	var boss_id = game.enemies.keys()[0]
	var boss = game.enemies[boss_id]
	assert(boss.boss)
	game._snapshot(PackedInt32Array([1]),_state(game.actors[1],0),PackedFloat32Array([boss_id,boss.p.x,boss.p.y,boss.p.z,boss.hp/2,0,1,boss.max_hp]),10,0,0)
	game._update_hud()
	assert(game.boss_panel.visible and game.boss_health_bar.value == 1500 and game.boss_health_bar.max_value == 3000)
	game.modal_open = true
	game._update_hud()
	assert(not game.boss_panel.visible)
	game.modal_open = false
	game.enemies.clear()
	game._update_hud()
	assert(not game.boss_panel.visible)
	# Final wave still offers a buff, then ends instead of spawning wave 21.
	var damage_before = game.BASE_WEAPON_DAMAGE
	game.actors[1].damage = damage_before
	for pick in range(20): game._apply_buff(1,0)
	assert(is_equal_approx(game.actors[1].damage,damage_before+112.0))
	game.wave = 20
	game.pending_levels = 1
	game._simulate(0.01)
	assert(game.buff_pending)
	game._apply_buff(1,0)
	game._resolve_buffs()
	game._simulate(1.1)
	assert(game.mode == "result" and game.result_success and game.wave == 20)
	game.queue_free()
	await process_frame
	print("MOVEMENT_WAVES_PASS delayed_reconciliation buff_speed movement_parity 60_waves bosses snapshot_health hud final_buff")
	quit()
