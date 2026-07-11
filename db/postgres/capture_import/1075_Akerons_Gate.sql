-- capture import for sector 1075 (Akerons Gate) -- GENERATED.
-- Accurate captured data; takes priority over current data.
BEGIN;

-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)
DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000);
DELETE FROM sector_objects WHERE sector_id = 1075 AND sector_object_id >= 1000000;

-- 3. inserts (parents first, then child rows)
INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES (1000372, 1834, 0.0, 0.0, 0.0, 38, 1.0, 42605.836, 95573.82, 577.0, 0.0, 0.0, 0.0, 0.0, 'Gas Cloud', 0, 5000.0, 1075, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;
INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES (1000372, 4, 0, 1, 0.0, 1.0, 0.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) VALUES (1000372, 1000372, 1834) ON CONFLICT (id) DO NOTHING;
INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, base_xp, exploration_range, object_radius_patch) VALUES (1000372, 0, 7000.0, 0, 1075, 0, 3000.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;

COMMIT;
