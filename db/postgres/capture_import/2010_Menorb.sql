-- capture import for sector 2010 (Menorb) -- GENERATED.
-- Accurate captured data; takes priority over current data.
BEGIN;

-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000);
DELETE FROM sector_objects WHERE sector_id = 2010 AND sector_object_id >= 1000000;

-- 3. inserts (parents first, then child rows)
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001485, 0, 0.0, 0.0, 0.0, 0, 1.0, -48135.105, 10085.032, 6156.2734, 0.0, 0.0, 0.0, 0.0, 'Sharim Trader', 0, 5000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001486, 0, 0.0, 0.0, 0.0, 0, 1.0, -69210.5, 77659.7, 2000.0, 0.0, 0.0, 0.0, 0.0, 'Gate Guardian Turret', 0, 5000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001487, 1446, 0.0, 0.0, 0.0, 38, 1.0, 44488.598, -61455.19, -196.0, 0.0, 0.0, 0.0, 0.0, 'Crystal', 0, 5000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001488, 1447, 0.0, 0.0, 0.0, 38, 1.0, 49211.773, -66554.914, -617.0, 0.0, 0.0, 0.0, 0.0, 'Crystal', 0, 5000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001489, 1444, 0.0, 0.0, 0.0, 38, 1.0, 44061.887, -71586.33, 45.0, 0.0, 0.0, 0.0, 0.0, 'Crystal', 0, 5000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1001490, 0, 0.0, 0.0, 0.0, 37, 1.0, -21866.44, 52532.95, 0.0, 0.0, 0.0, 0.0, 0.0, 'Crystal Altar 5', 0, 15000.0, 2010, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001485, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001485, 1001485, 1746, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001485, 0, 7000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_mob (mob_id, mob_count, mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) VALUES (1001486, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;
INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, group_index) VALUES (1001486, 1001486, 573, 0) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001486, 0, 7000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001487, 1, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001487, 1001487, 1446) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001487, 0, 7000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001488, 1, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001488, 1001488, 1447) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001488, 0, 7000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1001489, 1, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1001489, 1001489, 1444) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001489, 0, 7000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1001490, 1, 15000.0, 0, 2010, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
