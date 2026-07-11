-- capture import for sector 1425 (Zweihander) -- GENERATED.
-- Accurate captured data; takes priority over current data.
BEGIN;

-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000);
DELETE FROM sector_objects WHERE sector_id = 1425 AND sector_object_id >= 1000000;

-- 3. inserts (parents first, then child rows)
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001079, 0, 0.0, 0.0, 0.0, 0, 1.0, 12124.857, -40073.21, 99.07926, 0.0, 0.0, 0.0, 0.0, 'V''rix Scout', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001080, 0, 0.0, 0.0, 0.0, 0, 1.0, 95347.24, 80735.59, -939.9648, 0.0, 0.0, 0.0, 0.0, 'Bio-Thief', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001081, 0, 0.0, 0.0, 0.0, 0, 1.0, 168371.0, 15068.015, 98.8444, 0.0, 0.0, 0.0, 0.0, 'Free Spacer Recruit', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001082, 0, 0.0, 0.0, 0.0, 0, 1.0, 163119.66, 8908.641, 31.234068, 0.0, 0.0, 0.0, 0.0, 'Free Spacer Recruit', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001083, 1825, 0.0, 0.0, 0.0, 38, 1.0, -72559.06, 119773.01, 928.0, 0.0, 0.0, 0.0, 0.0, 'Asteroid', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001084, 1825, 0.0, 0.0, 0.0, 38, 1.0, -80851.8, 109897.5, 530.0, 0.0, 0.0, 0.0, 0.0, 'Asteroid', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001085, 1834, 0.0, 0.0, 0.0, 38, 1.0, 42605.836, 95573.82, 577.0, 0.0, 0.0, 0.0, 0.0, 'Gas Cloud', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001086, 1827, 0.0, 0.0, 0.0, 38, 1.0, -144618.61, 130292.9, 758.0, 0.0, 0.0, 0.0, 0.0, 'Asteroid', 0, 5000.0, 1425, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001079, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001079, 1001079, 1506, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001079, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001080, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001080, 1001080, 144, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001080, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001081, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001081, 1001081, 538, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001081, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001082, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001082, 1001082, 538, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001082, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001083, 7, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001083, 1001083, 1825) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001083, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001084, 7, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001084, 1001084, 1825) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001084, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001085, 3, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001085, 1001085, 1834) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001085, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001086, 7, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001086, 1001086, 1827) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001086, 0, 7000.0, 0, 1425, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
