-- SPDX-License-Identifier: CC-BY-NC-SA-3.0
-- Deterministic test-account pool for the Phase T integration suite.
--
-- ServerFixture pipes this into the mysql container with
--   docker compose exec -T mysql mysql -unet7 -pnet7 net7_user
-- after `docker compose up -d --wait` has returned (mysql healthcheck
-- has fired), before any test gets the fixture handed in.
--
-- Each test run starts from scratch because ServerFixture tears down
-- with `down -v` (wipes mysqldata). The seed is therefore safe to
-- TRUNCATE+INSERT every run — no migration headaches.
--
-- Password hash matches login-server/Net7SSL/LinuxAuth.cpp:227 which
-- validates with `password = UPPER(MD5(?))`. So the stored value is
-- UPPER(MD5('<plaintext>')). The plaintext for every account in this
-- pool is the literal string "testpw" — same for all five so tests
-- don't have to track per-account secrets.
--
-- The hash is computed server-side at INSERT time via UPPER(MD5(...));
-- don't hardcode the digest here. (For reference, MySQL evaluates
-- UPPER(MD5('testpw')) to '8EEE3EFDDE1EB6CF6639A58848362BF4', but
-- whatever MySQL produces is what gets stored — don't trust this comment
-- over the function call.)
--
-- The accounts here use IDs 9000001..9000005 to stay well clear of any
-- real account IDs the dumps might carry (AUTO_INCREMENT=15965 on the
-- accounts table per net7_user.sql), so seed and dump never collide.

USE `net7_user`;

DELETE FROM `accounts` WHERE `id` BETWEEN 9000001 AND 9000099;

INSERT INTO `accounts` (`id`, `username`, `password`, `status`, `formname`, `email`, `warn_level`)
VALUES
  (9000001, 'cli_test01', UPPER(MD5('testpw')), 0, 'cli_test01_form', 'cli_test01@net-7.test', 0),
  (9000002, 'cli_test02', UPPER(MD5('testpw')), 0, 'cli_test02_form', 'cli_test02@net-7.test', 0),
  (9000003, 'cli_test03', UPPER(MD5('testpw')), 0, 'cli_test03_form', 'cli_test03@net-7.test', 0),
  (9000004, 'cli_test04', UPPER(MD5('testpw')), 0, 'cli_test04_form', 'cli_test04@net-7.test', 0),
  (9000005, 'cli_test05', UPPER(MD5('testpw')), 0, 'cli_test05_form', 'cli_test05@net-7.test', 0);
