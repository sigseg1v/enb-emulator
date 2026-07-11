-- capture import for sector 4030 (Arduinne) -- GENERATED.
-- Accurate captured data; takes priority over current data.
BEGIN;

-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000);
DELETE FROM sector_objects WHERE sector_id = 4030 AND sector_object_id >= 1000000;

-- 3. inserts (parents first, then child rows)
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1002385, 0, 0.0, 0.0, 0.0, 0, 1.0, 175041.72, 97773.63, -1030.2416, 0.0, 0.0, 0.0, 0.0, 'Irate Hippocampus', 0, 5000.0, 4030, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1002386, 0, 0.0, 0.0, 0.0, 0, 1.0, 171720.66, 97370.83, 371.25226, 0.0, 0.0, 0.0, 0.0, 'Irate Hippocampus', 0, 5000.0, 4030, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1002387, 0, 0.0, 0.0, 0.0, 0, 1.0, 169084.47, 94325.41, -290.00156, 0.0, 0.0, 0.0, 0.0, 'Irate Hippocampus', 0, 5000.0, 4030, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1002388, 0, 0.0, 0.0, 0.0, 0, 1.0, 63909.86, -153562.84, -17.766338, 0.0, 0.0, 0.0, 0.0, 'Radiated Smuggler', 0, 5000.0, 4030, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1002389, 0, 0.0, 0.0, 0.0, 0, 1.0, 69330.0, -154770.0, 1400.0, 0.0, 0.0, 0.0, 0.0, 'Gate Guardian Turret', 0, 5000.0, 4030, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1002385, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1002385, 1002385, 704, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1002385, 0, 7000.0, 0, 4030, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1002386, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1002386, 1002386, 704, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1002386, 0, 7000.0, 0, 4030, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1002387, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1002387, 1002387, 704, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1002387, 0, 7000.0, 0, 4030, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1002388, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1002388, 1002388, 1103, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1002388, 0, 7000.0, 0, 4030, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1002389, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1002389, 1002389, 573, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1002389, 0, 7000.0, 0, 4030, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
