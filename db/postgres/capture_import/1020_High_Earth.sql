-- capture import for sector 1020 (High Earth) -- GENERATED.
-- Accurate captured data; takes priority over current data.
BEGIN;

-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000);
DELETE FROM sector_objects WHERE sector_id = 1020 AND sector_object_id >= 1000000;

-- 2. capture-priority replace: remove existing mobs/resources within 5000 units of a captured object of the same specific type
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (4728, 4729, 4732);
DELETE FROM sector_objects_mob WHERE mob_id IN (4728, 4729, 4732);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (4728, 4729, 4732);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (4728, 4729, 4732);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (4728, 4729, 4732);
DELETE FROM sector_nav_points WHERE sector_object_id IN (4728, 4729, 4732);
DELETE FROM sector_objects WHERE sector_object_id IN (4728, 4729, 4732);

-- 3. inserts (parents first, then child rows)
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000240, 0, 0.0, 0.0, 0.0, 0, 1.0, -56234.754, -52521.164, 640.9108, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000241, 0, 0.0, 0.0, 0.0, 0, 1.0, -48911.273, -55937.336, 221.96225, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000242, 0, 0.0, 0.0, 0.0, 0, 1.0, -49224.43, -55187.668, -453.56195, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000243, 0, 0.0, 0.0, 0.0, 0, 1.0, -49880.254, -55183.258, 42.016937, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000244, 0, 0.0, 0.0, 0.0, 0, 1.0, -48834.2, -57155.707, -243.12474, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000245, 0, 0.0, 0.0, 0.0, 0, 1.0, -39103.4, -57164.82, 42.74997, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000246, 0, 0.0, 0.0, 0.0, 0, 1.0, -37870.77, -54412.137, -152.07538, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000247, 0, 0.0, 0.0, 0.0, 0, 1.0, -37889.797, -55762.8, -163.2637, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000248, 0, 0.0, 0.0, 0.0, 0, 1.0, -41163.09, -55488.445, -793.5172, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000249, 0, 0.0, 0.0, 0.0, 0, 1.0, -31182.486, -65138.258, 49.962452, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000250, 0, 0.0, 0.0, 0.0, 0, 1.0, -33495.74, -68269.766, 463.29492, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000251, 0, 0.0, 0.0, 0.0, 0, 1.0, -36820.844, -62581.523, 170.39113, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000252, 0, 0.0, 0.0, 0.0, 0, 1.0, -46513.324, -34076.996, -25.793358, 0.0, 0.0, 0.0, 0.0, 'InfinitiCorp Cargo Hauler', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000253, 0, 0.0, 0.0, 0.0, 0, 1.0, -68344.0, -76788.0, 1400.0, 0.0, 0.0, 0.0, 0.0, 'Gate Guardian Turret', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000240, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000240, 1000240, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000240, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000241, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000241, 1000241, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000241, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000242, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000242, 1000242, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000242, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000243, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000243, 1000243, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000243, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000244, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000244, 1000244, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000244, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000245, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000245, 1000245, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000245, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000246, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000246, 1000246, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000246, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000247, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000247, 1000247, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000247, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000248, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000248, 1000248, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000248, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000249, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000249, 1000249, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000249, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000250, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000250, 1000250, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000250, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000251, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000251, 1000251, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000251, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000252, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000252, 1000252, 679, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000252, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000253, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000253, 1000253, 573, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000253, 0, 7000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
