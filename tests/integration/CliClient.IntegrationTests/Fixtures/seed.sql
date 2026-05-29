-- SPDX-License-Identifier: CC-BY-NC-SA-3.0
-- Deterministic test-account pool for the Phase T integration suite.
--
-- ServerFixture pipes this into the postgres container with
--   docker compose exec -T postgres psql -U net7 -d net7_user
-- after `docker compose up -d --wait` has returned (postgres
-- healthcheck has fired + the one-shot `schema-init` service has
-- successfully created both the `net7` and `net7_user` databases),
-- before any test gets the fixture handed in.
--
-- Each test run starts from scratch because ServerFixture tears down
-- with `down -v` (wipes pgdata). The seed is therefore safe to
-- DELETE+INSERT every run.
--
-- Password hash matches login-server/Net7SSL/LinuxAuth.cpp which
-- validates by comparing UPPER(MD5(plaintext)) against the stored
-- column. The plaintext for every account in this pool is the literal
-- string "testpw" — same for all eight so tests don't have to track
-- per-account secrets.
--
-- The hash is computed server-side at INSERT time via UPPER(MD5(...));
-- don't hardcode the digest here. (For reference, MD5('testpw')
-- evaluates to '8eee3efdde1eb6cf6639a58848362bf4', upper-cased before
-- insert — but trust the function call over this comment.)
--
-- The accounts here use IDs 9000001..9000008 to stay well clear of any
-- real account IDs the dumps might carry (AUTO_INCREMENT=15965 on the
-- accounts table per net7_user.sql), so seed and dump never collide.
--
-- Phase N portability note: was MySQL-flavoured (USE / backticks /
-- UPPER(MD5(...))) before the libpqxx migration. The Postgres rewrite
-- drops USE (the connection already targets net7_user), strips
-- backticks (double-quoted identifiers are reserved word safe but the
-- column names here aren't reserved so we can leave them bare), and
-- swaps MySQL's MD5() (returns lowercase hex) for Postgres
-- `encode(digest(..., 'md5'), 'hex')`. pgcrypto's `digest()` returns
-- bytea; encode to hex then UPPER for parity with the MySQL hash.

-- Account status convention (server/src/UDP_Global.cpp:ProcessTicketInfo):
--   0    STRESS_TEST_CLOSED — rejected at the global UDP plane (G_ERROR 12)
--   -1   INACTIVE          — Net7SSL refuses login
--   -2   BANNED             — Net7SSL refuses login
--   100  ACTIVE/admin       — what real accounts use (cf. net7_user.sql admin row)
-- We seed status=100 so the proxy↔server global UDP handshake (Phase K)
-- succeeds and the test gets a real GlobalAvatarList back. status=0 was
-- the original placeholder and only worked for the AuthLogin-only tests
-- that never reach ProcessTicketInfo.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DELETE FROM accounts WHERE id BETWEEN 9000001 AND 9000129;

INSERT INTO accounts (id, username, password, status, formname, email, warn_level)
VALUES
  (9000001, 'cli_test01',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test01_form',     'cli_test01@net-7.test',         0),
  (9000002, 'cli_test02',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test02_form',     'cli_test02@net-7.test',         0),
  (9000003, 'cli_test03',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test03_form',     'cli_test03@net-7.test',         0),
  (9000004, 'cli_test04',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test04_form',     'cli_test04@net-7.test',         0),
  (9000005, 'cli_test05',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test05_form',     'cli_test05@net-7.test',         0),
  (9000006, 'cli_test06',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test06_form',     'cli_test06@net-7.test',         0),
  (9000007, 'cli_test07',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test07_form',     'cli_test07@net-7.test',         0),
  (9000008, 'cli_test08',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test08_form',     'cli_test08@net-7.test',         0),
  (9000009, 'cli_test09',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test09_form',     'cli_test09@net-7.test',         0),
  (9000011, 'cli_test11',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test11_form',     'cli_test11@net-7.test',         0),
  (9000012, 'cli_test12',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test12_form',     'cli_test12@net-7.test',         0),
  (9000013, 'cli_test13',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test13_form',     'cli_test13@net-7.test',         0),
  (9000014, 'cli_test14',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test14_form',     'cli_test14@net-7.test',         0),
  (9000015, 'cli_test15',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test15_form',     'cli_test15@net-7.test',         0),
  (9000016, 'cli_test16',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test16_form',     'cli_test16@net-7.test',         0),
  (9000017, 'cli_test17',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test17_form',     'cli_test17@net-7.test',         0),
  (9000018, 'cli_test18',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test18_form',     'cli_test18@net-7.test',         0),
  (9000019, 'cli_test19',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test19_form',     'cli_test19@net-7.test',         0),
  (9000020, 'cli_test20',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test20_form',     'cli_test20@net-7.test',         0),
  (9000021, 'cli_test21',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test21_form',     'cli_test21@net-7.test',         0),
  (9000022, 'cli_test22',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test22_form',     'cli_test22@net-7.test',         0),
  (9000023, 'cli_test23',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test23_form',     'cli_test23@net-7.test',         0),
  (9000024, 'cli_test24',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test24_form',     'cli_test24@net-7.test',         0),
  (9000025, 'cli_test25',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test25_form',     'cli_test25@net-7.test',         0),
  (9000026, 'cli_test26',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test26_form',     'cli_test26@net-7.test',         0),
  (9000027, 'cli_test27',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test27_form',     'cli_test27@net-7.test',         0),
  (9000028, 'cli_test28',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test28_form',     'cli_test28@net-7.test',         0),
  (9000029, 'cli_test29',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test29_form',     'cli_test29@net-7.test',         0),
  (9000030, 'cli_test30',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test30_form',     'cli_test30@net-7.test',         0),
  (9000031, 'cli_test31',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test31_form',     'cli_test31@net-7.test',         0),
  (9000032, 'cli_test32',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test32_form',     'cli_test32@net-7.test',         0),
  (9000033, 'cli_test33',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test33_form',     'cli_test33@net-7.test',         0),
  (9000034, 'cli_test34',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test34_form',     'cli_test34@net-7.test',         0),
  (9000035, 'cli_test35',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test35_form',     'cli_test35@net-7.test',         0),
  (9000036, 'cli_test36',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test36_form',     'cli_test36@net-7.test',         0),
  (9000037, 'cli_test37',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test37_form',     'cli_test37@net-7.test',         0),
  (9000038, 'cli_test38',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test38_form',     'cli_test38@net-7.test',         0),
  (9000039, 'cli_test39',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test39_form',     'cli_test39@net-7.test',         0),
  (9000040, 'cli_test40',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test40_form',     'cli_test40@net-7.test',         0),
  (9000041, 'cli_test41',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test41_form',     'cli_test41@net-7.test',         0),
  (9000042, 'cli_test42',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test42_form',     'cli_test42@net-7.test',         0),
  (9000043, 'cli_test43',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test43_form',     'cli_test43@net-7.test',         0),
  (9000044, 'cli_test44',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test44_form',     'cli_test44@net-7.test',         0),
  (9000045, 'cli_test45',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test45_form',     'cli_test45@net-7.test',         0),
  (9000046, 'cli_test46',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test46_form',     'cli_test46@net-7.test',         0),
  (9000047, 'cli_test47',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test47_form',     'cli_test47@net-7.test',         0),
  (9000048, 'cli_test48',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test48_form',     'cli_test48@net-7.test',         0),
  (9000049, 'cli_test49',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test49_form',     'cli_test49@net-7.test',         0),
  (9000050, 'cli_test50',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test50_form',     'cli_test50@net-7.test',         0),
  (9000051, 'cli_test51',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test51_form',     'cli_test51@net-7.test',         0),
  (9000052, 'cli_test52',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test52_form',     'cli_test52@net-7.test',         0),
  (9000053, 'cli_test53',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test53_form',     'cli_test53@net-7.test',         0),
  (9000054, 'cli_test54',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test54_form',     'cli_test54@net-7.test',         0),
  (9000055, 'cli_test55',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test55_form',     'cli_test55@net-7.test',         0),
  (9000056, 'cli_test56',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test56_form',     'cli_test56@net-7.test',         0),
  (9000057, 'cli_test57',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test57_form',     'cli_test57@net-7.test',         0),
  (9000058, 'cli_test58',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test58_form',     'cli_test58@net-7.test',         0),
  (9000059, 'cli_test59',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test59_form',     'cli_test59@net-7.test',         0),
  (9000060, 'cli_test60',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test60_form',     'cli_test60@net-7.test',         0),
  (9000061, 'cli_test61',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test61_form',     'cli_test61@net-7.test',         0),
  (9000062, 'cli_test62',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test62_form',     'cli_test62@net-7.test',         0),
  (9000063, 'cli_test63',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test63_form',     'cli_test63@net-7.test',         0),
  (9000064, 'cli_test64',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test64_form',     'cli_test64@net-7.test',         0),
  (9000065, 'cli_test65',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test65_form',     'cli_test65@net-7.test',         0),
  (9000066, 'cli_test66',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test66_form',     'cli_test66@net-7.test',         0),
  (9000067, 'cli_test67',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test67_form',     'cli_test67@net-7.test',         0),
  (9000068, 'cli_test68',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test68_form',     'cli_test68@net-7.test',         0),
  (9000069, 'cli_test69',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test69_form',     'cli_test69@net-7.test',         0),
  (9000070, 'cli_test70',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test70_form',     'cli_test70@net-7.test',         0),
  (9000071, 'cli_test71',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test71_form',     'cli_test71@net-7.test',         0),
  (9000072, 'cli_test72',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test72_form',     'cli_test72@net-7.test',         0),
  (9000073, 'cli_test73',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test73_form',     'cli_test73@net-7.test',         0),
  (9000074, 'cli_test74',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test74_form',     'cli_test74@net-7.test',         0),
  (9000075, 'cli_test75',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test75_form',     'cli_test75@net-7.test',         0),
  (9000076, 'cli_test76',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test76_form',     'cli_test76@net-7.test',         0),
  (9000077, 'cli_test77',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test77_form',     'cli_test77@net-7.test',         0),
  (9000078, 'cli_test78',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test78_form',     'cli_test78@net-7.test',         0),
  (9000079, 'cli_test79',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test79_form',     'cli_test79@net-7.test',         0),
  (9000080, 'cli_test80',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test80_form',     'cli_test80@net-7.test',         0),
  (9000081, 'cli_test81',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test81_form',     'cli_test81@net-7.test',         0),
  (9000082, 'cli_test82',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test82_form',     'cli_test82@net-7.test',         0),
  (9000083, 'cli_test83',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test83_form',     'cli_test83@net-7.test',         0),
  (9000084, 'cli_test84',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test84_form',     'cli_test84@net-7.test',         0),
  (9000085, 'cli_test85',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test85_form',     'cli_test85@net-7.test',         0),
  (9000086, 'cli_test86',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test86_form',     'cli_test86@net-7.test',         0),
  (9000087, 'cli_test87',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test87_form',     'cli_test87@net-7.test',         0),
  (9000088, 'cli_test88',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test88_form',     'cli_test88@net-7.test',         0),
  (9000089, 'cli_test89',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test89_form',     'cli_test89@net-7.test',         0),
  (9000090, 'cli_test90',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test90_form',     'cli_test90@net-7.test',         0),
  (9000091, 'cli_test91',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test91_form',     'cli_test91@net-7.test',         0),
  (9000092, 'cli_test92',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test92_form',     'cli_test92@net-7.test',         0),
  (9000093, 'cli_test93',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test93_form',     'cli_test93@net-7.test',         0),
  (9000094, 'cli_test94',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test94_form',     'cli_test94@net-7.test',         0),
  (9000095, 'cli_test95',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test95_form',     'cli_test95@net-7.test',         0),
  (9000096, 'cli_test96',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test96_form',     'cli_test96@net-7.test',         0),
  (9000097, 'cli_test97',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test97_form',     'cli_test97@net-7.test',         0),
  (9000098, 'cli_test98',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test98_form',     'cli_test98@net-7.test',         0),
  (9000099, 'cli_test99',         UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test99_form',     'cli_test99@net-7.test',         0),
  (9000100, 'cli_test100',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test100_form',    'cli_test100@net-7.test',        0),
  (9000101, 'cli_test101',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test101_form',    'cli_test101@net-7.test',        0),
  (9000102, 'cli_test102',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test102_form',    'cli_test102@net-7.test',        0),
  (9000103, 'cli_test103',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test103_form',    'cli_test103@net-7.test',        0),
  (9000104, 'cli_test104',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test104_form',    'cli_test104@net-7.test',        0),
  (9000105, 'cli_test105',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test105_form',    'cli_test105@net-7.test',        0),
  (9000106, 'cli_test106',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test106_form',    'cli_test106@net-7.test',        0),
  (9000107, 'cli_test107',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test107_form',    'cli_test107@net-7.test',        0),
  (9000108, 'cli_test108',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test108_form',    'cli_test108@net-7.test',        0),
  (9000109, 'cli_test109',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test109_form',    'cli_test109@net-7.test',        0),
  (9000110, 'cli_test110',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test110_form',    'cli_test110@net-7.test',        0),
  (9000111, 'cli_test111',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test111_form',    'cli_test111@net-7.test',        0),
  (9000112, 'cli_test112',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test112_form',    'cli_test112@net-7.test',        0),
  (9000113, 'cli_test113',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test113_form',    'cli_test113@net-7.test',        0),
  (9000114, 'cli_test114',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test114_form',    'cli_test114@net-7.test',        0),
  (9000115, 'cli_test115',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test115_form',    'cli_test115@net-7.test',        0),
  (9000116, 'cli_test116',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test116_form',    'cli_test116@net-7.test',        0),
  (9000117, 'cli_test117',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test117_form',    'cli_test117@net-7.test',        0),
  (9000118, 'cli_test118',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test118_form',    'cli_test118@net-7.test',        0),
  (9000119, 'cli_test119',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test119_form',    'cli_test119@net-7.test',        0),
  (9000120, 'cli_test120',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test120_form',    'cli_test120@net-7.test',        0),
  (9000121, 'cli_test121',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test121_form',    'cli_test121@net-7.test',        0),
  (9000122, 'cli_test122',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test122_form',    'cli_test122@net-7.test',        0),
  (9000123, 'cli_test123',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test123_form',    'cli_test123@net-7.test',        0),
  (9000124, 'cli_test124',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test124_form',    'cli_test124@net-7.test',        0),
  (9000125, 'cli_test125',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test125_form',    'cli_test125@net-7.test',        0),
  (9000126, 'cli_test126',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test126_form',    'cli_test126@net-7.test',        0),
  (9000127, 'cli_test127',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test127_form',    'cli_test127@net-7.test',        0),
  (9000128, 'cli_test128',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test128_form',    'cli_test128@net-7.test',        0),
  (9000129, 'cli_test129',        UPPER(encode(digest('testpw', 'md5'), 'hex')), 100, 'cli_test129_form',    'cli_test129@net-7.test',        0),
  -- Status=0 fixture used by GlobalConnectTests.StressTestClosedAccount_*.
  -- LinuxAuth doesn't check status so login succeeds and the ticket is
  -- issued normally; ProcessTicketInfo on the server side rejects with
  -- G_ERROR_STRESS_TEST_CLOSED (12) which the proxy then forwards as a
  -- 0x0075 GLOBAL_ERROR frame. Validates the full UDP error path:
  -- client -> proxy -> server -> proxy -> client.
  (9000010, 'cli_test_status0',   UPPER(encode(digest('testpw', 'md5'), 'hex')),   0, 'cli_test_status0_f',  'cli_test_status0@net-7.test',   0);
