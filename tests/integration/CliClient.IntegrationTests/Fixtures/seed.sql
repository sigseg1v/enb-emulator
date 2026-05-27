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

DELETE FROM accounts WHERE id BETWEEN 9000001 AND 9000099;

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
  -- Status=0 fixture used by GlobalConnectTests.StressTestClosedAccount_*.
  -- LinuxAuth doesn't check status so login succeeds and the ticket is
  -- issued normally; ProcessTicketInfo on the server side rejects with
  -- G_ERROR_STRESS_TEST_CLOSED (12) which the proxy then forwards as a
  -- 0x0075 GLOBAL_ERROR frame. Validates the full UDP error path:
  -- client -> proxy -> server -> proxy -> client.
  (9000010, 'cli_test_status0',   UPPER(encode(digest('testpw', 'md5'), 'hex')),   0, 'cli_test_status0_f',  'cli_test_status0@net-7.test',   0);
