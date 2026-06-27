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
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000000, 0, 0.0, 0.0, 0.0, 0, 1.0, -56234.754, -52521.164, 640.9108, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000001, 0, 0.0, 0.0, 0.0, 0, 1.0, -48911.273, -55937.336, 221.96225, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000002, 0, 0.0, 0.0, 0.0, 0, 1.0, -49224.43, -55187.668, -453.56195, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000003, 0, 0.0, 0.0, 0.0, 0, 1.0, -49880.254, -55183.258, 42.016937, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000004, 0, 0.0, 0.0, 0.0, 0, 1.0, -48834.2, -57155.707, -243.12474, 0.0, 0.0, 0.0, 0.0, 'Scuttle Larva', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000005, 0, 0.0, 0.0, 0.0, 0, 1.0, -39103.4, -57164.82, 42.74997, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000006, 0, 0.0, 0.0, 0.0, 0, 1.0, -37870.77, -54412.137, -152.07538, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000007, 0, 0.0, 0.0, 0.0, 0, 1.0, -37889.797, -55762.8, -163.2637, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000008, 0, 0.0, 0.0, 0.0, 0, 1.0, -41163.09, -55488.445, -793.5172, 0.0, 0.0, 0.0, 0.0, 'Scuttle Pupa', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000009, 0, 0.0, 0.0, 0.0, 0, 1.0, -31182.486, -65138.258, 49.962452, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000010, 0, 0.0, 0.0, 0.0, 0, 1.0, -33495.74, -68269.766, 463.29492, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000011, 0, 0.0, 0.0, 0.0, 0, 1.0, -36820.844, -62581.523, 170.39113, 0.0, 0.0, 0.0, 0.0, 'Relentless Drone', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000012, 0, 0.0, 0.0, 0.0, 0, 1.0, -46513.324, -34076.996, -25.793358, 0.0, 0.0, 0.0, 0.0, 'InfinitiCorp Cargo Hauler', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000013, 0, 0.0, 0.0, 0.0, 0, 1.0, -68344.0, -76788.0, 1400.0, 0.0, 0.0, 0.0, 0.0, 'Gate Guardian Turret', 0, 5000.0, 1020, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000000, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000000, 1000000, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000000, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000001, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000001, 1000001, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000001, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000002, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000002, 1000002, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000002, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000003, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000003, 1000003, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000003, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000004, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000004, 1000004, 1253, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000004, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000005, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000005, 1000005, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000005, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000006, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000006, 1000006, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000006, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000007, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000007, 1000007, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000007, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000008, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000008, 1000008, 1255, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000008, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000009, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000009, 1000009, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000009, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000010, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000010, 1000010, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000010, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000011, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000011, 1000011, 1126, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000011, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000012, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000012, 1000012, 679, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000012, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1000013, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1000013, 1000013, 573, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000013, 0, 1000.0, 0, 1020, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
