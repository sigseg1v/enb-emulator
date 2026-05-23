-- 00-create-databases.sql
--
-- Runs first inside the `mysql` container at first-boot, courtesy of
-- the official mysql image's /docker-entrypoint-initdb.d/ contract.
--
-- The original 2010 dumps (net7.sql, net7_user.sql) lack CREATE DATABASE /
-- USE statements, so we create the databases here and grant the `net7`
-- user access; the .sh scripts that follow load the actual schemas.

CREATE DATABASE IF NOT EXISTS `net7`
  CHARACTER SET latin1
  COLLATE latin1_swedish_ci;

CREATE DATABASE IF NOT EXISTS `net7_user`
  CHARACTER SET latin1
  COLLATE latin1_swedish_ci;

GRANT ALL PRIVILEGES ON `net7`.* TO 'net7'@'%';
GRANT ALL PRIVILEGES ON `net7_user`.* TO 'net7'@'%';
FLUSH PRIVILEGES;
