-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- Test fixture: a persisted calibration file as autocalib.save() would write
-- it. Consumed by init_spec.lua. (Real calib_data.lua files are gitignored;
-- this one lives under tests/fixtures and only feeds the spec.)
return {
    player_ptr_addr = 0x00B6C0A0,
    hull = 0x124,
    hull_max = 0x128,
}
