/*
MySQL Data Transfer
Source Host: 208.109.124.174
Source Database: net7_user
Target Host: 208.109.124.174
Target Database: net7_user
Date: 5/4/2010 11:51:31 PM
*/

SET FOREIGN_KEY_CHECKS=0;
-- ----------------------------
-- Table structure for account_avatar_forumname
-- ----------------------------
DROP TABLE IF EXISTS `account_avatar_forumname`;
CREATE TABLE `account_avatar_forumname` (
  `accountID` int(11) DEFAULT NULL,
  `accountName` varchar(40) DEFAULT NULL,
  `avatarName` varchar(40) DEFAULT NULL,
  `forumName` varchar(40) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for account_infractions
-- ----------------------------
DROP TABLE IF EXISTS `account_infractions`;
CREATE TABLE `account_infractions` (
  `account_ID` int(10) unsigned NOT NULL,
  `infraction_date` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
  `admin_ID` int(10) unsigned NOT NULL,
  `infraction` text NOT NULL,
  `warn_level_increment` int(10) unsigned NOT NULL,
  KEY `Index_AccountID` (`account_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COMMENT='Player infractions';

-- ----------------------------
-- Table structure for account_status_levels
-- ----------------------------
DROP TABLE IF EXISTS `account_status_levels`;
CREATE TABLE `account_status_levels` (
  `id` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `account_status` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COMMENT='Reference Table for account status levels';

-- ----------------------------
-- Table structure for accounts
-- ----------------------------
DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(40) NOT NULL,
  `password` varchar(40) NOT NULL,
  `status` int(11) NOT NULL DEFAULT '0',
  `formname` varchar(40) NOT NULL DEFAULT 'no_form_name',
  `email` varchar(40) NOT NULL DEFAULT 'noemail@net-7.org',
  `last_login` timestamp NOT NULL DEFAULT '1989-12-30 00:00:00',
  `last_logout` timestamp NOT NULL DEFAULT '1989-12-30 00:00:00',
  `warn_level` int(10) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=15965 DEFAULT CHARSET=latin1 COMMENT='NEVER delete an account';

-- ----------------------------
-- Table structure for avatar_ammo
-- ----------------------------
DROP TABLE IF EXISTS `avatar_ammo`;
CREATE TABLE `avatar_ammo` (
  `avatar_id` int(11) NOT NULL,
  `equipment_slot` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) DEFAULT NULL,
  `quality` float DEFAULT NULL,
  `ammo_stack` int(11) DEFAULT NULL,
  `builder_name` text,
  `structure` float DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`equipment_slot`),
  KEY `avatar_ammo_ik1` (`avatar_id`,`equipment_slot`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_base
-- ----------------------------
DROP TABLE IF EXISTS `avatar_base`;
CREATE TABLE `avatar_base` (
  `Race` tinyint(3) unsigned NOT NULL,
  `Profession` tinyint(3) unsigned NOT NULL,
  `base_shield` int(10) unsigned NOT NULL,
  `base_reactor` int(10) unsigned NOT NULL,
  `base_engine` int(10) unsigned NOT NULL,
  `base_weapon` int(10) unsigned NOT NULL,
  `base_hull_asset` int(10) unsigned NOT NULL,
  `base_profession_asset` int(10) unsigned NOT NULL,
  `base_wing_asset` int(10) unsigned NOT NULL,
  `base_engine_asset` int(10) unsigned NOT NULL,
  `starting_sector` int(10) unsigned NOT NULL,
  `base_faction` int(10) unsigned NOT NULL,
  `base_scan_range` int(10) unsigned NOT NULL,
  `base_signature` int(10) unsigned NOT NULL,
  `base_speed` int(10) unsigned NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_data
-- ----------------------------
DROP TABLE IF EXISTS `avatar_data`;
CREATE TABLE `avatar_data` (
  `avatar_id` int(10) unsigned NOT NULL COMMENT 'Base 1',
  `first_name` varchar(20) NOT NULL,
  `last_name` varchar(20) NOT NULL,
  `type` int(11) NOT NULL,
  `version` tinyint(4) NOT NULL,
  `race` int(11) NOT NULL,
  `prof` int(11) NOT NULL,
  `gender` int(11) NOT NULL,
  `mood` int(11) NOT NULL,
  `personality` tinyint(4) NOT NULL,
  `nlp` tinyint(4) NOT NULL COMMENT 'No idea what this is',
  `body` tinyint(4) NOT NULL,
  `pants` tinyint(4) NOT NULL,
  `head` tinyint(4) NOT NULL,
  `hair` tinyint(4) NOT NULL,
  `ear` tinyint(4) NOT NULL,
  `goggle` tinyint(4) NOT NULL,
  `beard` tinyint(4) NOT NULL,
  `weapon_hip` tinyint(4) NOT NULL,
  `weapon_unique` tinyint(4) NOT NULL,
  `weapon_back` tinyint(4) NOT NULL,
  `head_texture` tinyint(4) NOT NULL,
  `tattoo_texture` tinyint(4) NOT NULL,
  `tattoo_X` float NOT NULL,
  `tattoo_Y` float NOT NULL,
  `tattoo_Z` float NOT NULL,
  `hair_H` float NOT NULL,
  `hair_S` float NOT NULL,
  `hair_V` float NOT NULL,
  `beard_H` float NOT NULL,
  `beard_S` float NOT NULL,
  `beard_V` float NOT NULL,
  `eye_H` float NOT NULL,
  `eye_S` float NOT NULL,
  `eye_V` float NOT NULL,
  `skin_H` float NOT NULL,
  `skin_S` float NOT NULL,
  `skin_V` float NOT NULL,
  `shirt_p_H` float NOT NULL,
  `shirt_p_S` float NOT NULL,
  `shirt_p_V` float NOT NULL,
  `shirt_s_H` float NOT NULL,
  `shirt_s_S` float NOT NULL,
  `shirt_s_V` float NOT NULL,
  `pants_p_H` float NOT NULL,
  `pants_p_S` float NOT NULL,
  `pants_p_V` float NOT NULL,
  `pants_s_H` float NOT NULL,
  `pants_s_S` float NOT NULL,
  `pants_s_V` float NOT NULL,
  `shirt_p_metal` int(11) NOT NULL,
  `shirt_s_metal` int(11) NOT NULL,
  `pants_p_metal` int(11) NOT NULL,
  `pants_s_metal` int(11) NOT NULL,
  `height_weight_0` float NOT NULL,
  `height_weight_1` float NOT NULL,
  `height_weight_2` float NOT NULL,
  `height_weight_3` float NOT NULL,
  `height_weight_4` float NOT NULL,
  PRIMARY KEY (`avatar_id`),
  CONSTRAINT `FK_avatar_data_1` FOREIGN KEY (`avatar_id`) REFERENCES `avatar_info` (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_equipment
-- ----------------------------
DROP TABLE IF EXISTS `avatar_equipment`;
CREATE TABLE `avatar_equipment` (
  `avatar_id` int(11) NOT NULL,
  `equipment_slot` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) DEFAULT NULL,
  `quality` float(11,4) DEFAULT NULL,
  `builder_name` text,
  `structure` float(11,4) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`equipment_slot`),
  KEY `avatar_equipment_ik1` (`avatar_id`,`equipment_slot`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_exploration
-- ----------------------------
DROP TABLE IF EXISTS `avatar_exploration`;
CREATE TABLE `avatar_exploration` (
  `avatar_id` int(11) NOT NULL,
  `object_id` int(11) NOT NULL,
  `explore_flags` tinyint(4) NOT NULL,
  KEY `avatar_exploration_ik1` (`avatar_id`,`object_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_faction_level
-- ----------------------------
DROP TABLE IF EXISTS `avatar_faction_level`;
CREATE TABLE `avatar_faction_level` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `faction_level_list` text,
  PRIMARY KEY (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_gm_items
-- ----------------------------
DROP TABLE IF EXISTS `avatar_gm_items`;
CREATE TABLE `avatar_gm_items` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) NOT NULL,
  `stack_level` int(11) NOT NULL,
  `trade_stack` int(11) NOT NULL,
  `quality` float(11,4) NOT NULL,
  `cost` int(11) NOT NULL,
  `builder_name` text NOT NULL,
  `structure` float(11,4) NOT NULL,
  PRIMARY KEY (`avatar_id`,`item_id`),
  KEY `avatar_inventory_items_ik1` (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_info
-- ----------------------------
DROP TABLE IF EXISTS `avatar_info`;
CREATE TABLE `avatar_info` (
  `avatar_id` int(10) unsigned NOT NULL,
  `account_id` int(10) unsigned NOT NULL,
  `slot` int(10) unsigned NOT NULL,
  `sector` int(10) unsigned NOT NULL,
  `galaxy` int(10) unsigned NOT NULL,
  `count` int(10) unsigned NOT NULL COMMENT 'No idea what this is',
  `admin` int(10) NOT NULL,
  `combat` int(10) unsigned NOT NULL,
  `explore` int(10) unsigned NOT NULL,
  `trade` int(10) unsigned NOT NULL,
  `engine_trail_type` int(10) DEFAULT NULL,
  `last_login` timestamp NOT NULL DEFAULT '1989-12-30 00:00:00',
  `last_logout` timestamp NOT NULL DEFAULT '1989-12-30 00:00:00',
  `last_logout_t` bigint(20) DEFAULT NULL,
  `time_played` bigint(20) NOT NULL DEFAULT '0',
  PRIMARY KEY (`avatar_id`),
  KEY `FK_avatar_info_1` (`account_id`),
  CONSTRAINT `FK_avatar_info_1` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_inventory_items
-- ----------------------------
DROP TABLE IF EXISTS `avatar_inventory_items`;
CREATE TABLE `avatar_inventory_items` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) DEFAULT NULL,
  `stack_level` int(11) DEFAULT NULL,
  `inventory_slot` int(11) NOT NULL DEFAULT '0',
  `trade_stack` int(11) DEFAULT NULL,
  `quality` float(11,4) DEFAULT NULL,
  `cost` int(11) DEFAULT NULL,
  `builder_name` text,
  `structure` float(11,4) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`inventory_slot`),
  KEY `avatar_inventory_items_ik1` (`avatar_id`,`inventory_slot`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_level_info
-- ----------------------------
DROP TABLE IF EXISTS `avatar_level_info`;
CREATE TABLE `avatar_level_info` (
  `avatar_id` int(11) NOT NULL,
  `player_rank_name` int(11) NOT NULL,
  `hull_upgrade_level` int(11) NOT NULL,
  `hull_points` float NOT NULL,
  `max_hull_points` float NOT NULL,
  `credits` bigint(20) unsigned NOT NULL,
  `cargo_space` int(11) NOT NULL,
  `combat_bar_level` float NOT NULL,
  `explore_bar_level` float NOT NULL,
  `trade_bar_level` float NOT NULL,
  `weapon_slots` int(11) DEFAULT NULL,
  `device_slots` int(11) DEFAULT NULL,
  `skill_points` int(11) DEFAULT NULL,
  `engine_thrust_type` int(11) NOT NULL,
  `warp_power_level` int(11) NOT NULL,
  `registered_starbase` int(11) DEFAULT NULL,
  `reactor_level` float DEFAULT NULL,
  `shield_level` float DEFAULT NULL,
  `xp_debt` int(10) unsigned NOT NULL DEFAULT '0',
  `last_debt` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_mission_progress
-- ----------------------------
DROP TABLE IF EXISTS `avatar_mission_progress`;
CREATE TABLE `avatar_mission_progress` (
  `avatar_id` int(11) NOT NULL,
  `mission_slot` int(11) NOT NULL DEFAULT '0',
  `mission_id` int(11) DEFAULT NULL,
  `stage_num` int(11) DEFAULT NULL,
  `mission_flags` int(11) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`mission_slot`),
  KEY `avatar_mission_progress_ik1` (`avatar_id`,`mission_slot`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_position
-- ----------------------------
DROP TABLE IF EXISTS `avatar_position`;
CREATE TABLE `avatar_position` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `posx` double DEFAULT NULL,
  `posy` double DEFAULT NULL,
  `posz` double DEFAULT NULL,
  `ori_w` double DEFAULT NULL,
  `ori_x` double DEFAULT NULL,
  `ori_y` double DEFAULT NULL,
  `ori_z` double DEFAULT NULL,
  `sector_id` int(11) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_recipes
-- ----------------------------
DROP TABLE IF EXISTS `avatar_recipes`;
CREATE TABLE `avatar_recipes` (
  `avatar_id` int(11) DEFAULT NULL,
  `item_id` int(11) DEFAULT NULL,
  `attempts` int(10) unsigned DEFAULT '0',
  `avg_quality` float DEFAULT '0'
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_skill_levels
-- ----------------------------
DROP TABLE IF EXISTS `avatar_skill_levels`;
CREATE TABLE `avatar_skill_levels` (
  `avatar_id` int(11) NOT NULL,
  `skill_id` int(11) NOT NULL DEFAULT '0',
  `skill_level` int(11) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`skill_id`),
  KEY `avatar_skill_levels_ik1` (`avatar_id`,`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_trade_items
-- ----------------------------
DROP TABLE IF EXISTS `avatar_trade_items`;
CREATE TABLE `avatar_trade_items` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) DEFAULT NULL,
  `stack_level` int(11) DEFAULT NULL,
  `inventory_slot` int(11) NOT NULL DEFAULT '0',
  `trade_stack` int(11) DEFAULT NULL,
  `quality` float(11,4) DEFAULT NULL,
  `cost` int(11) DEFAULT NULL,
  `builder_name` text,
  `structure` float(11,4) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`inventory_slot`),
  KEY `avatar_trade_items_ik1` (`avatar_id`,`inventory_slot`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for avatar_vault_items
-- ----------------------------
DROP TABLE IF EXISTS `avatar_vault_items`;
CREATE TABLE `avatar_vault_items` (
  `avatar_id` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) DEFAULT NULL,
  `stack_level` int(11) DEFAULT NULL,
  `inventory_slot` int(11) NOT NULL DEFAULT '0',
  `trade_stack` int(11) DEFAULT NULL,
  `quality` float(11,4) DEFAULT NULL,
  `cost` int(11) DEFAULT NULL,
  `builder_name` text,
  `structure` float(11,4) DEFAULT NULL,
  PRIMARY KEY (`avatar_id`,`inventory_slot`),
  KEY `avatar_vault_items_ik1` (`avatar_id`,`inventory_slot`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for cbasset
-- ----------------------------
DROP TABLE IF EXISTS `cbasset`;
CREATE TABLE `cbasset` (
  `BASE_ID` varchar(255) DEFAULT NULL,
  `BASE_RADIUS_VALUE` varchar(255) DEFAULT NULL,
  `BASE_RADIUS_VALUE_AGG` varchar(255) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- ----------------------------
-- Table structure for faction_data
-- ----------------------------
DROP TABLE IF EXISTS `faction_data`;
CREATE TABLE `faction_data` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `avatar_id` bigint(20) NOT NULL,
  `faction_id` bigint(20) NOT NULL,
  `faction_value` float NOT NULL,
  `faction_order` tinyint(4) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=4142 DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for factions
-- ----------------------------
DROP TABLE IF EXISTS `factions`;
CREATE TABLE `factions` (
  `faction_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `name` varchar(255) DEFAULT NULL,
  `description` text,
  `player_PDA` tinyint(4) DEFAULT NULL,
  `PDA_text` text,
  PRIMARY KEY (`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for forbidden_names
-- ----------------------------
DROP TABLE IF EXISTS `forbidden_names`;
CREATE TABLE `forbidden_names` (
  `nickname` varchar(255) NOT NULL,
  PRIMARY KEY (`nickname`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for friends_lists
-- ----------------------------
DROP TABLE IF EXISTS `friends_lists`;
CREATE TABLE `friends_lists` (
  `avatar_id` int(10) unsigned NOT NULL,
  `name` varchar(20) NOT NULL,
  PRIMARY KEY (`avatar_id`,`name`),
  KEY `Index_1` (`avatar_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COMMENT='friend+ignore list';

-- ----------------------------
-- Table structure for galaxy
-- ----------------------------
DROP TABLE IF EXISTS `galaxy`;
CREATE TABLE `galaxy` (
  `id` int(11) NOT NULL DEFAULT '0',
  `galaxy` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for guild_members
-- ----------------------------
DROP TABLE IF EXISTS `guild_members`;
CREATE TABLE `guild_members` (
  `avatar_id` int(11) NOT NULL,
  `guild_id` int(11) NOT NULL,
  `rank` int(11) NOT NULL,
  `contribution` int(11) NOT NULL,
  `active` tinyint(4) NOT NULL,
  `tag` varchar(32) NOT NULL,
  PRIMARY KEY (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for guild_ranks
-- ----------------------------
DROP TABLE IF EXISTS `guild_ranks`;
CREATE TABLE `guild_ranks` (
  `id` int(10) unsigned NOT NULL COMMENT 'guild*10+0-9',
  `name` varchar(64) NOT NULL,
  `permissions` int(11) NOT NULL,
  `maxpromote` int(11) NOT NULL,
  `maxremove` int(11) NOT NULL,
  `mindemote` int(11) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for guilds
-- ----------------------------
DROP TABLE IF EXISTS `guilds`;
CREATE TABLE `guilds` (
  `guild_id` int(11) NOT NULL,
  `name` varchar(40) NOT NULL,
  `motd` varchar(128) NOT NULL,
  `points` int(11) NOT NULL,
  `level` int(11) NOT NULL,
  `public` tinyint(4) NOT NULL,
  PRIMARY KEY (`guild_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for hulls
-- ----------------------------
DROP TABLE IF EXISTS `hulls`;
CREATE TABLE `hulls` (
  `Race` tinyint(3) unsigned NOT NULL,
  `Profession` tinyint(3) unsigned NOT NULL,
  `upgrade_level` tinyint(3) unsigned NOT NULL,
  `hull_points` int(10) unsigned NOT NULL,
  `weapon_slots` tinyint(3) unsigned NOT NULL,
  `device_slots` tinyint(3) unsigned NOT NULL,
  `cargo_slots` int(10) unsigned NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for ignore_lists
-- ----------------------------
DROP TABLE IF EXISTS `ignore_lists`;
CREATE TABLE `ignore_lists` (
  `avatar_id` int(10) unsigned NOT NULL,
  `name` varchar(20) NOT NULL,
  PRIMARY KEY (`avatar_id`,`name`),
  KEY `Index_1` (`avatar_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for local_respawn_time
-- ----------------------------
DROP TABLE IF EXISTS `local_respawn_time`;
CREATE TABLE `local_respawn_time` (
  `local_respawn_time` varchar(0) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- ----------------------------
-- Table structure for missions_completed
-- ----------------------------
DROP TABLE IF EXISTS `missions_completed`;
CREATE TABLE `missions_completed` (
  `avatar_id` int(11) NOT NULL,
  `mission_id` int(11) DEFAULT NULL,
  `mission_completion_flags` int(11) DEFAULT NULL,
  KEY `missions_completed_ik1` (`avatar_id`,`mission_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for professions
-- ----------------------------
DROP TABLE IF EXISTS `professions`;
CREATE TABLE `professions` (
  `id` int(11) NOT NULL DEFAULT '0',
  `profession` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for races
-- ----------------------------
DROP TABLE IF EXISTS `races`;
CREATE TABLE `races` (
  `id` int(11) NOT NULL DEFAULT '0',
  `race` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for server_local_field_respawn_times
-- ----------------------------
DROP TABLE IF EXISTS `server_local_field_respawn_times`;
CREATE TABLE `server_local_field_respawn_times` (
  `resource_id` int(40) DEFAULT NULL,
  `local_respawn_time` int(40) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- ----------------------------
-- Table structure for ship_data
-- ----------------------------
DROP TABLE IF EXISTS `ship_data`;
CREATE TABLE `ship_data` (
  `avatar_id` int(10) unsigned NOT NULL,
  `race` int(11) NOT NULL,
  `prof` int(11) NOT NULL,
  `hull` int(11) NOT NULL,
  `wing` int(11) NOT NULL,
  `decal` int(11) NOT NULL,
  `name` varchar(26) NOT NULL COMMENT 'Shipname, not chatacter',
  `name_H` float NOT NULL,
  `name_S` float NOT NULL,
  `name_V` float NOT NULL,
  `hull_p_H` float NOT NULL,
  `hull_p_S` float NOT NULL,
  `hull_p_V` float NOT NULL,
  `hull_p_flat` tinyint(4) NOT NULL,
  `hull_p_metal` int(11) NOT NULL,
  `hull_s_H` float NOT NULL,
  `hull_s_S` float NOT NULL,
  `hull_s_V` float NOT NULL,
  `hull_s_flat` tinyint(4) NOT NULL,
  `hull_s_metal` int(11) NOT NULL,
  `prof_p_H` float NOT NULL,
  `prof_p_S` float NOT NULL,
  `prof_p_V` float NOT NULL,
  `prof_p_flat` tinyint(3) NOT NULL,
  `prof_p_metal` int(11) NOT NULL,
  `prof_s_H` float NOT NULL,
  `prof_s_S` float NOT NULL,
  `prof_s_V` float NOT NULL,
  `prof_s_flat` tinyint(3) NOT NULL,
  `prof_s_metal` int(11) NOT NULL,
  `wing_p_H` float NOT NULL,
  `wing_p_S` float NOT NULL,
  `wing_p_V` float NOT NULL,
  `wing_p_flat` tinyint(3) NOT NULL,
  `wing_p_metal` int(11) NOT NULL,
  `wing_s_H` float NOT NULL,
  `wing_s_S` float NOT NULL,
  `wing_s_V` float NOT NULL,
  `wing_s_flat` tinyint(3) NOT NULL,
  `wing_s_metal` int(11) NOT NULL,
  `engine_p_H` float NOT NULL,
  `engine_p_S` float NOT NULL,
  `engine_p_V` float NOT NULL,
  `engine_p_flat` tinyint(3) NOT NULL,
  `engine_p_metal` int(11) NOT NULL,
  `engine_s_H` float NOT NULL,
  `engine_s_S` float NOT NULL,
  `engine_s_V` float NOT NULL,
  `engine_s_flat` tinyint(3) NOT NULL,
  `engine_s_metal` int(11) NOT NULL,
  PRIMARY KEY (`avatar_id`),
  CONSTRAINT `FK_ship_data_1` FOREIGN KEY (`avatar_id`) REFERENCES `avatar_info` (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for ship_info
-- ----------------------------
DROP TABLE IF EXISTS `ship_info`;
CREATE TABLE `ship_info` (
  `avatar_id` int(10) unsigned NOT NULL,
  `hull` int(11) NOT NULL,
  `prof` int(11) NOT NULL,
  `engine` int(11) NOT NULL,
  `wing` int(11) NOT NULL,
  `pos_0` float NOT NULL,
  `pos_1` float NOT NULL,
  `pos_2` float NOT NULL,
  `ori_0` float NOT NULL,
  `ori_1` float NOT NULL,
  `ori_2` float NOT NULL,
  `ori_3` float NOT NULL,
  PRIMARY KEY (`avatar_id`),
  CONSTRAINT `FK_ship_info_1` FOREIGN KEY (`avatar_id`) REFERENCES `avatar_info` (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for skill_list
-- ----------------------------
DROP TABLE IF EXISTS `skill_list`;
CREATE TABLE `skill_list` (
  `skill_id` int(10) NOT NULL AUTO_INCREMENT COMMENT 'Skill ID',
  `skill_name` varchar(128) NOT NULL COMMENT 'Skill Name',
  PRIMARY KEY (`skill_id`)
) ENGINE=InnoDB AUTO_INCREMENT=60 DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for ssl_deny_list
-- ----------------------------
DROP TABLE IF EXISTS `ssl_deny_list`;
CREATE TABLE `ssl_deny_list` (
  `deny_addr` char(255) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for status_levels
-- ----------------------------
DROP TABLE IF EXISTS `status_levels`;
CREATE TABLE `status_levels` (
  `id` int(11) NOT NULL DEFAULT '0',
  `status` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Table structure for warning_levels
-- ----------------------------
DROP TABLE IF EXISTS `warning_levels`;
CREATE TABLE `warning_levels` (
  `sound_warning_level` int(11) DEFAULT NULL,
  `avatar_id` int(11) NOT NULL,
  PRIMARY KEY (`avatar_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- ----------------------------
-- Procedure structure for accLogin
-- ----------------------------
DROP PROCEDURE IF EXISTS `accLogin`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `accLogin`(IN accID INTEGER, IN theTime VARCHAR(40))
BEGIN
    UPDATE net7_user.accounts
      SET last_login = theTime
    WHERE id = accID;
  END;;
DELIMITER ;

-- ----------------------------
-- Procedure structure for accLogout
-- ----------------------------
DROP PROCEDURE IF EXISTS `accLogout`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `accLogout`(IN accID INTEGER, IN theTime VARCHAR(40))
BEGIN
    UPDATE net7_user.accounts
      SET last_logout = theTime
    WHERE id = accID;
  END;;
DELIMITER ;

-- ----------------------------
-- Procedure structure for avaLogin
-- ----------------------------
DROP PROCEDURE IF EXISTS `avaLogin`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `avaLogin`(IN avaID INTEGER, IN theTime VARCHAR(40))
BEGIN
    UPDATE net7_user.avatar_info
      SET last_login = theTime
    WHERE avatar_id = avaID;
  END;;
DELIMITER ;

-- ----------------------------
-- Procedure structure for avaLogout
-- ----------------------------
DROP PROCEDURE IF EXISTS `avaLogout`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `avaLogout`(IN avaID INTEGER, IN theTime VARCHAR(40))
BEGIN
    UPDATE net7_user.avatar_info
      SET last_logout = theTime, time_played = time_played + (now() - last_login)
    WHERE avatar_id = avaID;
  END;;
DELIMITER ;

-- ----------------------------
-- Procedure structure for incWarn
-- ----------------------------
DROP PROCEDURE IF EXISTS `incWarn`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `incWarn`(IN accID INTEGER, IN adminID INTEGER, IN infrac TEXT, IN incAmount INTEGER)
BEGIN
    UPDATE Net7_user.accounts
      SET warn_level = warn_level + incAmount
    WHERE id = accID;

    INSERT INTO Net7_user.account_infractions
      VALUES(accID, NOW(), adminID, infrac, incAmount);
  END;;
DELIMITER ;

-- ----------------------------
-- Procedure structure for logoutOnShutdown
-- ----------------------------
DROP PROCEDURE IF EXISTS `logoutOnShutdown`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` PROCEDURE `logoutOnShutdown`(IN theTime VARCHAR(40))
BEGIN
    UPDATE Net7_user.accounts
      SET last_logout = theTime
    WHERE last_login > last_logout;

    UPDATE Net7_user.avatar_info
      SET last_logout = theTime
    WHERE last_login > last_logout;
  END;;
DELIMITER ;

-- ----------------------------
-- Function structure for getDPS
-- ----------------------------
DROP FUNCTION IF EXISTS `getDPS`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` FUNCTION `getDPS`(avaID INT(11), cat INT(11), slot INT(11), itemID INT(11)) RETURNS float
    READS SQL DATA
BEGIN
    DECLARE DPS FLOAT;
    DECLARE qMOD FLOAT;

    SELECT quality_mod INTO qMOD FROM net7.item_base WHERE id = itemID;

    SET DPS = 0.0;

    CASE
      
      WHEN cat = 100 THEN
        BEGIN

          SELECT (beam.damage_100 * (1 + (((a.Quality * 100) - 100) * (qMOD - 1) / 100)) / beam.reload_100) INTO DPS
          FROM net7_user.avatar_equipment a
          INNER JOIN net7.item_base ib ON
            a.item_id = ib.id
          INNER JOIN net7.item_beam beam ON
            a.item_id = beam.item_id
          WHERE a.avatar_id = avaID AND
            a.equipment_slot = slot;

          RETURN DPS;
        END;
      
      WHEN cat = 101 THEN
        BEGIN

          SELECT (((ia.damage_100 * (1 + (((aa.Quality * 100) - 100) * (aib.quality_mod - 1) / 100))) * pl.ammo_per_shot) / (pl.reload_100 * (1 -(((ae.Quality * 100) - 100) * (1 - qMOD) / 100)))) INTO DPS
          FROM net7_user.avatar_ammo aa
          INNER JOIN net7_user.avatar_equipment ae ON
            ae.avatar_id = aa.avatar_id AND
            ae.equipment_slot = aa.equipment_slot
          INNER JOIN net7.item_base aib ON
            aa.item_id = aib.id
          INNER JOIN net7.item_ammo ia ON
            aa.item_id = ia.item_id
          INNER JOIN net7.item_projectile pl ON
            ae.item_id = pl.item_id
          WHERE ae.avatar_id = avaID AND
            ae.equipment_slot = slot;

          RETURN DPS;
        END;
      
      WHEN cat = 102 THEN
        BEGIN

          SELECT (((ia.damage_100 * (1 + (((aa.Quality * 100) - 100) * (aib.quality_mod - 1) / 100))) * ml.ammo_per_shot) / (ml.reload_100 * (1 -(((ae.Quality * 100) - 100) * (1 - qMOD) / 100)))) INTO DPS
          FROM net7_user.avatar_ammo aa
          INNER JOIN net7_user.avatar_equipment ae ON
            ae.avatar_id = aa.avatar_id AND
            ae.equipment_slot = aa.equipment_slot
          INNER JOIN net7.item_base aib ON
            aa.item_id = aib.id
          INNER JOIN net7.item_ammo ia ON
            aa.item_id = ia.item_id
          INNER JOIN net7.item_missile ml ON
            ae.item_id = ml.item_id
          WHERE ae.avatar_id = avaID AND
            ae.equipment_slot = slot;

          RETURN DPS;
        END;
    END CASE;

  END;;
DELIMITER ;

-- ----------------------------
-- Function structure for isAccLoggedIn
-- ----------------------------
DROP FUNCTION IF EXISTS `isAccLoggedIn`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` FUNCTION `isAccLoggedIn`(theID Int(11)) RETURNS tinyint(1)
    DETERMINISTIC
BEGIN
       DECLARE li TIMESTAMP;
       DECLARE lo TIMESTAMP;
       SELECT last_login INTO li FROM net7_user.accounts WHERE id = theID;
       SELECT last_logout INTO lo FROM net7_user.accounts WHERE id = theID;
       IF li > lo THEN
         RETURN 1;
       ELSE
         RETURN 0;
       END IF;
    END;;
DELIMITER ;

-- ----------------------------
-- Function structure for isAvaLoggedIn
-- ----------------------------
DROP FUNCTION IF EXISTS `isAvaLoggedIn`;
DELIMITER ;;
CREATE DEFINER=`root`@`%` FUNCTION `isAvaLoggedIn`(theID Int(11)) RETURNS tinyint(1)
    DETERMINISTIC
BEGIN
       DECLARE li TIMESTAMP;
       DECLARE lo TIMESTAMP;
       SELECT last_login INTO li FROM net7_user.avatar_info WHERE avatar_id = theID;
       SELECT last_logout INTO lo FROM net7_user.avatar_info WHERE avatar_id = theID;
       IF li > lo THEN
         RETURN 1;
       ELSE
         RETURN 0;
       END IF;
    END;;
DELIMITER ;

-- ----------------------------
-- Records 
-- ----------------------------
INSERT INTO `accounts` VALUES ('1', 'admin', '8E2EE90C9162D43363F389F78FF42485', '100', 'no_form_name', 'noemail@net-7.org', '1989-12-30 00:00:00', '1989-12-30 00:00:00', '0');
INSERT INTO `cbasset` VALUES ('BASE_ID', 'BASE_RADIUS_VALUE', 'BASE_RADIUS_VALUE_AGG'), ('0', '92.7889', '92.7889'), ('1', '446.804', '446.804'), ('2', '351.625', '351.625'), ('3', '95.8132', '95.8132'), ('4', '404.041', '404.041'), ('5', '659.429', '659.429'), ('6', '80.8497', '80.8497'), ('7', '417.862', '417.862'), ('8', '344.45', '344.45'), ('9', '65.228', '65.228'), ('10', '273.116', '273.116'), ('11', '372.153', '372.153'), ('12', '83.0271', '83.0271'), ('13', '116.854', '116.854'), ('14', '518.572', '518.572'), ('15', '101.96', '101.96'), ('16', '866.658', '866.658'), ('17', '739.632', '739.632'), ('18', '63.3249', '63.3249'), ('19', '963.112', '963.112'), ('20', '2036.94', '2036.94'), ('21', '85.0302', '85.0302'), ('22', '259.701', '259.701'), ('23', '32.3112', '32.3112'), ('24', '87.5018', '87.5018'), ('25', '15.7889', '15.7889'), ('26', '15.1573', '15.1573'), ('27', '75348.7', '75348.7'), ('28', '55895', '55895'), ('29', '12.3643', '12.3643'), ('30', '13.382', '13.382'), ('31', '20.3068', '20.3068'), ('32', '14.0512', '14.0512'), ('33', '18.0922', '18.0922'), ('34', '14.7768', '14.7768'), ('35', '14.2235', '14.2235'), ('36', '15.8618', '15.8618'), ('37', '16.8591', '16.8591'), ('38', '13.1926', '13.1926'), ('39', '13.4855', '13.4855'), ('40', '16.4865', '16.4865'), ('41', '15.3324', '15.3324'), ('42', '16.8224', '16.8224'), ('43', '12.3356', '12.3356'), ('44', '15.949', '15.949'), ('45', '11.9433', '11.9433'), ('46', '18.2988', '18.2988'), ('47', '3331.02', '3331.02'), ('48', '1796.75', '1796.75'), ('49', '15.5972', '15.5972'), ('50', '13.7222', '13.7222'), ('51', '1513.53', '1513.53'), ('52', '1963.38', '1963.38'), ('53', '16.0047', '16.0047'), ('54', '3317.58', '3317.58'), ('55', '2892.58', '2892.58'), ('56', '7605.99', '7605.99'), ('57', '2892.58', '2892.58'), ('58', '2892.58', '2892.58'), ('59', '2892.58', '2892.58'), ('60', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('61', '2892.58', '2892.58'), ('62', '14903.3', '14903.3'), ('63', '1666.13', '1666.13');
INSERT INTO `cbasset` VALUES ('64', '1642.13', '1642.13');
INSERT INTO `cbasset` VALUES ('65', '10672.7', '10672.7');
INSERT INTO `cbasset` VALUES ('66', '0', '0');
INSERT INTO `cbasset` VALUES ('67', '0', '0');
INSERT INTO `cbasset` VALUES ('68', '0', '0');
INSERT INTO `cbasset` VALUES ('69', '15.8867', '15.8867');
INSERT INTO `cbasset` VALUES ('70', '5.9362', '5.9362');
INSERT INTO `cbasset` VALUES ('71', '10.0988', '10.0988');
INSERT INTO `cbasset` VALUES ('72', '12.157', '12.157');
INSERT INTO `cbasset` VALUES ('73', '15.7068', '15.7068');
INSERT INTO `cbasset` VALUES ('74', '2.013', '2.013');
INSERT INTO `cbasset` VALUES ('75', '1.7694', '1.7694'), ('76', '16.0047', '16.0047'), ('77', '10672.7', '10672.7'), ('78', '10672.7', '10672.7'), ('79', '10672.7', '10672.7'), ('80', '10672.7', '10672.7');
INSERT INTO `cbasset` VALUES ('81', '0', '0');
INSERT INTO `cbasset` VALUES ('82', '0', '0');
INSERT INTO `cbasset` VALUES ('83', '0', '0');
INSERT INTO `cbasset` VALUES ('84', '0', '0');
INSERT INTO `cbasset` VALUES ('85', '0', '0');
INSERT INTO `cbasset` VALUES ('86', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('87', '0', '0');
INSERT INTO `cbasset` VALUES ('88', '0', '0');
INSERT INTO `cbasset` VALUES ('89', '0.9047', '0.9047');
INSERT INTO `cbasset` VALUES ('90', '0.1', '0.1');
INSERT INTO `cbasset` VALUES ('91', '0.9047', '0.9047');
INSERT INTO `cbasset` VALUES ('92', '16.0047', '16.0047');
INSERT INTO `cbasset` VALUES ('93', '16.0047', '16.0047');
INSERT INTO `cbasset` VALUES ('94', '16.0074', '16.0074'), ('95', '16.0314', '16.0314');
INSERT INTO `cbasset` VALUES ('96', '16.0181', '16.0181');
INSERT INTO `cbasset` VALUES ('97', '16.0047', '16.0047'), ('98', '15.4299', '15.4299'), ('99', '11.3745', '11.3745'), ('100', '13.6141', '13.6141'), ('101', '9.9757', '9.9757'), ('102', '16.653', '16.653'), ('103', '12.6812', '12.6812'), ('104', '14.997', '14.997');
INSERT INTO `cbasset` VALUES ('105', '14.2556', '14.2556');
INSERT INTO `cbasset` VALUES ('106', '13.5944', '13.5944');
INSERT INTO `cbasset` VALUES ('107', '10.2374', '10.2374');
INSERT INTO `cbasset` VALUES ('108', '10.4279', '10.4279');
INSERT INTO `cbasset` VALUES ('109', '14.8931', '14.8931');
INSERT INTO `cbasset` VALUES ('110', '20.7966', '20.7966');
INSERT INTO `cbasset` VALUES ('111', '12.1187', '12.1187');
INSERT INTO `cbasset` VALUES ('112', '16.3477', '16.3477');
INSERT INTO `cbasset` VALUES ('113', '22.1908', '22.1908');
INSERT INTO `cbasset` VALUES ('114', '12.2486', '12.2486');
INSERT INTO `cbasset` VALUES ('115', '13.402', '13.402');
INSERT INTO `cbasset` VALUES ('116', '17.7008', '17.7008');
INSERT INTO `cbasset` VALUES ('117', '11.9246', '11.9246');
INSERT INTO `cbasset` VALUES ('118', '18.8035', '18.8035');
INSERT INTO `cbasset` VALUES ('119', '15.4056', '15.4056');
INSERT INTO `cbasset` VALUES ('120', '12.2494', '12.2494');
INSERT INTO `cbasset` VALUES ('121', '9.979', '9.979');
INSERT INTO `cbasset` VALUES ('122', '12.8116', '12.8116');
INSERT INTO `cbasset` VALUES ('123', '1659.57', '1659.57');
INSERT INTO `cbasset` VALUES ('124', '4731.33', '4731.33');
INSERT INTO `cbasset` VALUES ('125', '12.3442', '12.3442');
INSERT INTO `cbasset` VALUES ('126', '10.431', '10.431');
INSERT INTO `cbasset` VALUES ('127', '85.6768', '85.6768');
INSERT INTO `cbasset` VALUES ('128', '1139.78', '1139.78');
INSERT INTO `cbasset` VALUES ('129', '4376.7', '4376.7');
INSERT INTO `cbasset` VALUES ('130', '0', '0');
INSERT INTO `cbasset` VALUES ('131', '0', '0');
INSERT INTO `cbasset` VALUES ('132', '0', '0');
INSERT INTO `cbasset` VALUES ('133', '0', '0');
INSERT INTO `cbasset` VALUES ('134', '0', '0');
INSERT INTO `cbasset` VALUES ('135', '0', '0');
INSERT INTO `cbasset` VALUES ('136', '0', '0');
INSERT INTO `cbasset` VALUES ('137', '3002.18', '3002.18');
INSERT INTO `cbasset` VALUES ('138', '2592.59', '2592.59');
INSERT INTO `cbasset` VALUES ('139', '5.7742', '5.7742');
INSERT INTO `cbasset` VALUES ('140', '6626.21', '6626.21');
INSERT INTO `cbasset` VALUES ('141', '104.754', '104.754');
INSERT INTO `cbasset` VALUES ('142', '60.313', '60.313');
INSERT INTO `cbasset` VALUES ('143', '88.5213', '88.5213');
INSERT INTO `cbasset` VALUES ('144', '1970.32', '1970.32');
INSERT INTO `cbasset` VALUES ('145', '0', '0');
INSERT INTO `cbasset` VALUES ('146', '0', '0');
INSERT INTO `cbasset` VALUES ('147', '0', '0');
INSERT INTO `cbasset` VALUES ('148', '0', '0');
INSERT INTO `cbasset` VALUES ('149', '0', '0');
INSERT INTO `cbasset` VALUES ('150', '0', '0');
INSERT INTO `cbasset` VALUES ('151', '0', '0');
INSERT INTO `cbasset` VALUES ('152', '0', '0');
INSERT INTO `cbasset` VALUES ('153', '0', '0');
INSERT INTO `cbasset` VALUES ('154', '0', '0');
INSERT INTO `cbasset` VALUES ('155', '0', '0');
INSERT INTO `cbasset` VALUES ('156', '0', '0');
INSERT INTO `cbasset` VALUES ('157', '0', '0');
INSERT INTO `cbasset` VALUES ('158', '0', '0');
INSERT INTO `cbasset` VALUES ('159', '82.8222', '82.8222');
INSERT INTO `cbasset` VALUES ('160', '85.9334', '85.9334');
INSERT INTO `cbasset` VALUES ('161', '2205.94', '2205.94');
INSERT INTO `cbasset` VALUES ('162', '1306.05', '1306.05');
INSERT INTO `cbasset` VALUES ('163', '447.509', '447.509');
INSERT INTO `cbasset` VALUES ('164', '2863.71', '2863.71');
INSERT INTO `cbasset` VALUES ('165', '2155.63', '2155.63');
INSERT INTO `cbasset` VALUES ('166', '2216.4', '2216.4');
INSERT INTO `cbasset` VALUES ('167', '36.3219', '36.3219');
INSERT INTO `cbasset` VALUES ('168', '23.4267', '23.4267');
INSERT INTO `cbasset` VALUES ('169', '55.5417', '55.5417');
INSERT INTO `cbasset` VALUES ('170', '52.2337', '52.2337');
INSERT INTO `cbasset` VALUES ('171', '60746', '60746');
INSERT INTO `cbasset` VALUES ('172', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('173', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('174', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('175', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('176', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('177', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('178', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('179', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('180', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('181', '3553.45', '3553.45');
INSERT INTO `cbasset` VALUES ('182', '82.7237', '82.7237');
INSERT INTO `cbasset` VALUES ('183', '6309.16', '6309.16');
INSERT INTO `cbasset` VALUES ('184', '7.5428', '7.5428');
INSERT INTO `cbasset` VALUES ('185', '0', '0');
INSERT INTO `cbasset` VALUES ('186', '15.5771', '15.5771');
INSERT INTO `cbasset` VALUES ('187', '0', '0');
INSERT INTO `cbasset` VALUES ('188', '19.9226', '19.9226');
INSERT INTO `cbasset` VALUES ('189', '0', '0');
INSERT INTO `cbasset` VALUES ('190', '13.0751', '13.0751');
INSERT INTO `cbasset` VALUES ('191', '0', '0'), ('192', '10.7624', '10.7624');
INSERT INTO `cbasset` VALUES ('193', '0', '0');
INSERT INTO `cbasset` VALUES ('194', '0', '0');
INSERT INTO `cbasset` VALUES ('195', '0', '0'), ('196', '0', '0'), ('197', '0', '0');
INSERT INTO `cbasset` VALUES ('198', '88.5213', '88.5213');
INSERT INTO `cbasset` VALUES ('199', '10.4466', '10.4466');
INSERT INTO `cbasset` VALUES ('200', '0', '0');
INSERT INTO `cbasset` VALUES ('201', '10.7743', '10.7743');
INSERT INTO `cbasset` VALUES ('202', '18.4606', '18.4606');
INSERT INTO `cbasset` VALUES ('203', '21.8376', '21.8376'), ('204', '24.7325', '24.7325'), ('205', '0', '0');
INSERT INTO `cbasset` VALUES ('206', '0', '0');
INSERT INTO `cbasset` VALUES ('207', '0', '0');
INSERT INTO `cbasset` VALUES ('208', '0', '0');
INSERT INTO `cbasset` VALUES ('209', '0', '0');
INSERT INTO `cbasset` VALUES ('210', '0', '0');
INSERT INTO `cbasset` VALUES ('211', '0', '0');
INSERT INTO `cbasset` VALUES ('212', '21.8376', '21.8376');
INSERT INTO `cbasset` VALUES ('213', '12.2368', '12.2368');
INSERT INTO `cbasset` VALUES ('214', '12.6993', '12.6993');
INSERT INTO `cbasset` VALUES ('215', '24.7325', '24.7325');
INSERT INTO `cbasset` VALUES ('216', '434.469', '434.469');
INSERT INTO `cbasset` VALUES ('217', '40.1152', '40.1152');
INSERT INTO `cbasset` VALUES ('218', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('219', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('220', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('221', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('222', '34.5319', '34.5319');
INSERT INTO `cbasset` VALUES ('223', '0', '0');
INSERT INTO `cbasset` VALUES ('224', '0', '0');
INSERT INTO `cbasset` VALUES ('225', '0', '0');
INSERT INTO `cbasset` VALUES ('226', '0', '0');
INSERT INTO `cbasset` VALUES ('227', '0', '0');
INSERT INTO `cbasset` VALUES ('228', '34.5319', '34.5319');
INSERT INTO `cbasset` VALUES ('229', '40.3867', '40.3867');
INSERT INTO `cbasset` VALUES ('230', '20.6072', '20.6072');
INSERT INTO `cbasset` VALUES ('231', '10.7743', '10.7743');
INSERT INTO `cbasset` VALUES ('232', '18.4606', '18.4606');
INSERT INTO `cbasset` VALUES ('233', '10.4466', '10.4466');
INSERT INTO `cbasset` VALUES ('234', '40.6438', '40.6438');
INSERT INTO `cbasset` VALUES ('235', '34.5319', '34.5319');
INSERT INTO `cbasset` VALUES ('236', '34.5319', '34.5319');
INSERT INTO `cbasset` VALUES ('237', '34.5319', '34.5319'), ('238', '23.8629', '23.8629'), ('239', '17.6462', '17.6462'), ('240', '20.8831', '20.8831'), ('241', '17.9785', '17.9785'), ('242', '12.4651', '12.4651'), ('243', '2892.58', '2892.58'), ('244', '2892.58', '2892.58'), ('245', '122.995', '122.995'), ('246', '122.995', '122.995'), ('247', '178.54', '178.54'), ('248', '0', '0'), ('249', '80721.3', '80721.3'), ('250', '10533.7', '10533.7'), ('251', '36500.9', '36500.9'), ('252', '36500.9', '36500.9'), ('253', '62909', '62909'), ('254', '46366', '46366'), ('255', '71259.6', '71259.6'), ('256', '71259.6', '71259.6'), ('257', '71259.6', '71259.6'), ('258', '50666.8', '50666.8'), ('259', '73796.7', '73796.7'), ('260', '53268.6', '53268.6'), ('261', '44658', '44658');
INSERT INTO `cbasset` VALUES ('262', '42838.6', '42838.6');
INSERT INTO `cbasset` VALUES ('263', '42618.2', '42618.2');
INSERT INTO `cbasset` VALUES ('264', '37693.3', '37693.3');
INSERT INTO `cbasset` VALUES ('265', '45420.3', '45420.3');
INSERT INTO `cbasset` VALUES ('266', '46083.8', '46083.8');
INSERT INTO `cbasset` VALUES ('267', '3495.23', '3495.23');
INSERT INTO `cbasset` VALUES ('268', '3495.23', '3495.23');
INSERT INTO `cbasset` VALUES ('269', '4424.92', '4424.92');
INSERT INTO `cbasset` VALUES ('270', '3289.43', '3289.43');
INSERT INTO `cbasset` VALUES ('271', '2399.97', '2399.97');
INSERT INTO `cbasset` VALUES ('272', '3009.03', '3009.03');
INSERT INTO `cbasset` VALUES ('273', '7163.77', '7163.77');
INSERT INTO `cbasset` VALUES ('274', '60746', '60746');
INSERT INTO `cbasset` VALUES ('275', '60746', '60746');
INSERT INTO `cbasset` VALUES ('276', '2782.63', '2782.63');
INSERT INTO `cbasset` VALUES ('277', '4594.15', '4594.15');
INSERT INTO `cbasset` VALUES ('278', '60746', '60746');
INSERT INTO `cbasset` VALUES ('279', '2357.71', '2357.71');
INSERT INTO `cbasset` VALUES ('280', '60746', '60746');
INSERT INTO `cbasset` VALUES ('281', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('282', '3736.57', '3736.57');
INSERT INTO `cbasset` VALUES ('283', '60746', '60746');
INSERT INTO `cbasset` VALUES ('284', '3721.83', '3721.83');
INSERT INTO `cbasset` VALUES ('285', '6241.46', '6241.46');
INSERT INTO `cbasset` VALUES ('286', '7869.99', '7869.99'), ('287', '3319.26', '3319.26'), ('288', '4907.27', '4907.27'), ('289', '3389.55', '3389.55'), ('290', '7512.77', '7512.77'), ('291', '4243.09', '4243.09'), ('292', '4243.09', '4243.09'), ('293', '4214.07', '4214.07'), ('294', '2980.15', '2980.15'), ('295', '60746', '60746'), ('296', '60746', '60746'), ('297', '60746', '60746');
INSERT INTO `cbasset` VALUES ('298', '60746', '60746');
INSERT INTO `cbasset` VALUES ('299', '60746', '60746');
INSERT INTO `cbasset` VALUES ('300', '4120.81', '4120.81');
INSERT INTO `cbasset` VALUES ('301', '5118.44', '5118.44');
INSERT INTO `cbasset` VALUES ('302', '3056.69', '3056.69');
INSERT INTO `cbasset` VALUES ('303', '3492.94', '3492.94');
INSERT INTO `cbasset` VALUES ('304', '3873.87', '3873.87');
INSERT INTO `cbasset` VALUES ('305', '3171.63', '3171.63');
INSERT INTO `cbasset` VALUES ('306', '23515.7', '23515.7');
INSERT INTO `cbasset` VALUES ('307', '4847.55', '4847.55');
INSERT INTO `cbasset` VALUES ('308', '5958.73', '5958.73');
INSERT INTO `cbasset` VALUES ('309', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('310', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('311', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('312', '4349.65', '4349.65'), ('313', '89.105', '89.105');
INSERT INTO `cbasset` VALUES ('314', '76.1385', '76.1385');
INSERT INTO `cbasset` VALUES ('315', '81.5877', '81.5877');
INSERT INTO `cbasset` VALUES ('316', '74.6762', '74.6762');
INSERT INTO `cbasset` VALUES ('317', '59.2175', '59.2175');
INSERT INTO `cbasset` VALUES ('318', '68.7819', '68.7819');
INSERT INTO `cbasset` VALUES ('319', '59.9774', '59.9774');
INSERT INTO `cbasset` VALUES ('320', '68.6452', '68.6452');
INSERT INTO `cbasset` VALUES ('321', '86.2646', '86.2646');
INSERT INTO `cbasset` VALUES ('322', '2412.35', '2412.35');
INSERT INTO `cbasset` VALUES ('323', '293.091', '293.091');
INSERT INTO `cbasset` VALUES ('324', '9.9048', '9.9048');
INSERT INTO `cbasset` VALUES ('325', '5.099', '5.099');
INSERT INTO `cbasset` VALUES ('326', '7.885', '7.885');
INSERT INTO `cbasset` VALUES ('327', '35.1106', '35.1106');
INSERT INTO `cbasset` VALUES ('328', '10.4273', '10.4273');
INSERT INTO `cbasset` VALUES ('329', '11.5926', '11.5926');
INSERT INTO `cbasset` VALUES ('330', '5.2908', '5.2908');
INSERT INTO `cbasset` VALUES ('331', '5.3264', '5.3264');
INSERT INTO `cbasset` VALUES ('332', '3.8977', '3.8977');
INSERT INTO `cbasset` VALUES ('333', '12.9684', '12.9684');
INSERT INTO `cbasset` VALUES ('334', '12.4587', '12.4587');
INSERT INTO `cbasset` VALUES ('335', '18.7884', '18.7884');
INSERT INTO `cbasset` VALUES ('336', '5.2472', '5.2472');
INSERT INTO `cbasset` VALUES ('337', '4.5142', '4.5142');
INSERT INTO `cbasset` VALUES ('338', '5.1337', '5.1337');
INSERT INTO `cbasset` VALUES ('339', '4.9107', '4.9107');
INSERT INTO `cbasset` VALUES ('340', '4.1918', '4.1918');
INSERT INTO `cbasset` VALUES ('341', '7.1957', '7.1957');
INSERT INTO `cbasset` VALUES ('342', '8.4587', '8.4587');
INSERT INTO `cbasset` VALUES ('343', '7.7797', '7.7797');
INSERT INTO `cbasset` VALUES ('344', '4.9856', '4.9856');
INSERT INTO `cbasset` VALUES ('345', '8.196', '8.196');
INSERT INTO `cbasset` VALUES ('346', '7.4576', '7.4576');
INSERT INTO `cbasset` VALUES ('347', '7.3176', '7.3176');
INSERT INTO `cbasset` VALUES ('348', '7.5063', '7.5063');
INSERT INTO `cbasset` VALUES ('349', '4.5413', '4.5413');
INSERT INTO `cbasset` VALUES ('350', '4.6739', '4.6739');
INSERT INTO `cbasset` VALUES ('351', '11.1979', '11.1979');
INSERT INTO `cbasset` VALUES ('352', '11.1967', '11.1967');
INSERT INTO `cbasset` VALUES ('353', '8.1737', '8.1737');
INSERT INTO `cbasset` VALUES ('354', '15.2991', '15.2991');
INSERT INTO `cbasset` VALUES ('355', '15.0619', '15.0619');
INSERT INTO `cbasset` VALUES ('356', '20.0698', '20.0698'), ('357', '20.1801', '20.1801'), ('358', '20.262', '20.262'), ('359', '20.1645', '20.1645');
INSERT INTO `cbasset` VALUES ('360', '20.2756', '20.2756');
INSERT INTO `cbasset` VALUES ('361', '20.3513', '20.3513');
INSERT INTO `cbasset` VALUES ('362', '12.034', '12.034');
INSERT INTO `cbasset` VALUES ('363', '4.589', '4.589');
INSERT INTO `cbasset` VALUES ('364', '6.6757', '6.6757');
INSERT INTO `cbasset` VALUES ('365', '11.9162', '11.9162');
INSERT INTO `cbasset` VALUES ('366', '11.4307', '11.4307');
INSERT INTO `cbasset` VALUES ('367', '14.3237', '14.3237');
INSERT INTO `cbasset` VALUES ('368', '16.4081', '16.4081');
INSERT INTO `cbasset` VALUES ('369', '241471', '241471');
INSERT INTO `cbasset` VALUES ('370', '1632.44', '1632.44');
INSERT INTO `cbasset` VALUES ('371', '4252.8', '4252.8');
INSERT INTO `cbasset` VALUES ('372', '4075.21', '4075.21');
INSERT INTO `cbasset` VALUES ('373', '2293.12', '2293.12');
INSERT INTO `cbasset` VALUES ('374', '438.517', '438.517');
INSERT INTO `cbasset` VALUES ('375', '2612.43', '2612.43');
INSERT INTO `cbasset` VALUES ('376', '9269.53', '9269.53');
INSERT INTO `cbasset` VALUES ('377', '3784.22', '3784.22');
INSERT INTO `cbasset` VALUES ('378', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('379', '3553.45', '3553.45');
INSERT INTO `cbasset` VALUES ('380', '9615.26', '9615.26');
INSERT INTO `cbasset` VALUES ('381', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('382', '85.0329', '85.0329');
INSERT INTO `cbasset` VALUES ('383', '54.8104', '54.8104');
INSERT INTO `cbasset` VALUES ('384', '82.01', '82.01');
INSERT INTO `cbasset` VALUES ('385', '609.649', '609.649');
INSERT INTO `cbasset` VALUES ('386', '137.622', '137.622');
INSERT INTO `cbasset` VALUES ('387', '123.9', '123.9');
INSERT INTO `cbasset` VALUES ('388', '1961.66', '1961.66');
INSERT INTO `cbasset` VALUES ('389', '167.912', '167.912');
INSERT INTO `cbasset` VALUES ('390', '3634.92', '3634.92');
INSERT INTO `cbasset` VALUES ('391', '777.685', '777.685');
INSERT INTO `cbasset` VALUES ('392', '435.684', '435.684');
INSERT INTO `cbasset` VALUES ('393', '207.08', '207.08');
INSERT INTO `cbasset` VALUES ('394', '193.669', '193.669');
INSERT INTO `cbasset` VALUES ('395', '34.0102', '34.0102');
INSERT INTO `cbasset` VALUES ('396', '192.981', '192.981');
INSERT INTO `cbasset` VALUES ('397', '147.983', '147.983');
INSERT INTO `cbasset` VALUES ('398', '833.966', '833.966');
INSERT INTO `cbasset` VALUES ('399', '60.5658', '60.5658');
INSERT INTO `cbasset` VALUES ('400', '99.3735', '99.3735'), ('401', '53.7051', '53.7051');
INSERT INTO `cbasset` VALUES ('402', '10936.3', '10936.3');
INSERT INTO `cbasset` VALUES ('403', '10800.4', '10800.4');
INSERT INTO `cbasset` VALUES ('404', '5459.52', '5459.52');
INSERT INTO `cbasset` VALUES ('405', '4544.4', '4544.4');
INSERT INTO `cbasset` VALUES ('406', '1906.63', '1906.63');
INSERT INTO `cbasset` VALUES ('407', '6141.37', '6141.37');
INSERT INTO `cbasset` VALUES ('408', '9069.51', '9069.51');
INSERT INTO `cbasset` VALUES ('409', '5145.22', '5145.22');
INSERT INTO `cbasset` VALUES ('410', '4540.16', '4540.16');
INSERT INTO `cbasset` VALUES ('411', '3827.41', '3827.41');
INSERT INTO `cbasset` VALUES ('412', '2212.95', '2212.95');
INSERT INTO `cbasset` VALUES ('413', '1188.2', '1188.2');
INSERT INTO `cbasset` VALUES ('414', '1663.25', '1663.25');
INSERT INTO `cbasset` VALUES ('415', '3358.02', '3358.02');
INSERT INTO `cbasset` VALUES ('416', '3277.31', '3277.31');
INSERT INTO `cbasset` VALUES ('417', '373.427', '373.427');
INSERT INTO `cbasset` VALUES ('418', '604.813', '604.813');
INSERT INTO `cbasset` VALUES ('419', '477.683', '477.683');
INSERT INTO `cbasset` VALUES ('420', '743.936', '743.936');
INSERT INTO `cbasset` VALUES ('421', '655.493', '655.493');
INSERT INTO `cbasset` VALUES ('422', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('423', '4624.18', '4624.18');
INSERT INTO `cbasset` VALUES ('424', '4489.35', '4489.35');
INSERT INTO `cbasset` VALUES ('425', '205.565', '205.565');
INSERT INTO `cbasset` VALUES ('426', '262.976', '262.976');
INSERT INTO `cbasset` VALUES ('427', '210.774', '210.774');
INSERT INTO `cbasset` VALUES ('428', '2544.61', '2544.61');
INSERT INTO `cbasset` VALUES ('429', '203.1', '203.1');
INSERT INTO `cbasset` VALUES ('430', '59.8185', '59.8185');
INSERT INTO `cbasset` VALUES ('431', '136.144', '136.144');
INSERT INTO `cbasset` VALUES ('432', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('433', '168.166', '168.166');
INSERT INTO `cbasset` VALUES ('434', '127.856', '127.856');
INSERT INTO `cbasset` VALUES ('435', '533.309', '533.309');
INSERT INTO `cbasset` VALUES ('436', '690.033', '690.033');
INSERT INTO `cbasset` VALUES ('437', '0', '0');
INSERT INTO `cbasset` VALUES ('438', '0', '0');
INSERT INTO `cbasset` VALUES ('439', '0', '0');
INSERT INTO `cbasset` VALUES ('440', '0', '0');
INSERT INTO `cbasset` VALUES ('441', '0', '0');
INSERT INTO `cbasset` VALUES ('442', '0', '0');
INSERT INTO `cbasset` VALUES ('443', '0', '0');
INSERT INTO `cbasset` VALUES ('444', '0', '0'), ('445', '0', '0'), ('446', '0', '0');
INSERT INTO `cbasset` VALUES ('447', '8.7578', '8.7578');
INSERT INTO `cbasset` VALUES ('448', '14.1143', '14.1143');
INSERT INTO `cbasset` VALUES ('449', '23.7884', '23.7884');
INSERT INTO `cbasset` VALUES ('450', '37.5776', '37.5776');
INSERT INTO `cbasset` VALUES ('451', '36.8134', '36.8134');
INSERT INTO `cbasset` VALUES ('452', '0', '0');
INSERT INTO `cbasset` VALUES ('453', '0', '0');
INSERT INTO `cbasset` VALUES ('454', '21.8376', '21.8376');
INSERT INTO `cbasset` VALUES ('455', '5421.53', '5421.53');
INSERT INTO `cbasset` VALUES ('456', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('457', '11824.8', '11824.8');
INSERT INTO `cbasset` VALUES ('458', '7389.35', '7389.35');
INSERT INTO `cbasset` VALUES ('459', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('460', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('461', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('462', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('463', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('464', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('465', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('466', '71259.6', '71259.6'), ('467', '60746', '60746'), ('468', '60746', '60746'), ('469', '60746', '60746'), ('470', '71259.6', '71259.6'), ('471', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('472', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('473', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('474', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('475', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('476', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('477', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('478', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('479', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('480', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('481', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('482', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('483', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('484', '75348.7', '75348.7');
INSERT INTO `cbasset` VALUES ('485', '358.105', '358.105');
INSERT INTO `cbasset` VALUES ('486', '809.659', '809.659');
INSERT INTO `cbasset` VALUES ('487', '590.114', '590.114');
INSERT INTO `cbasset` VALUES ('488', '582.734', '582.734');
INSERT INTO `cbasset` VALUES ('489', '358.105', '358.105');
INSERT INTO `cbasset` VALUES ('490', '809.659', '809.659');
INSERT INTO `cbasset` VALUES ('491', '586.661', '586.661');
INSERT INTO `cbasset` VALUES ('492', '582.734', '582.734');
INSERT INTO `cbasset` VALUES ('493', '358.105', '358.105');
INSERT INTO `cbasset` VALUES ('494', '356.245', '356.245');
INSERT INTO `cbasset` VALUES ('495', '358.105', '358.105');
INSERT INTO `cbasset` VALUES ('496', '11880.4', '11880.4');
INSERT INTO `cbasset` VALUES ('497', '20.7991', '20.7991');
INSERT INTO `cbasset` VALUES ('498', '13.2703', '13.2703');
INSERT INTO `cbasset` VALUES ('499', '17.046', '17.046');
INSERT INTO `cbasset` VALUES ('500', '15.1405', '15.1405');
INSERT INTO `cbasset` VALUES ('501', '13.2703', '13.2703');
INSERT INTO `cbasset` VALUES ('502', '13.2703', '13.2703');
INSERT INTO `cbasset` VALUES ('503', '20.7991', '20.7991');
INSERT INTO `cbasset` VALUES ('504', '5.6811', '5.6811');
INSERT INTO `cbasset` VALUES ('505', '5.6811', '5.6811');
INSERT INTO `cbasset` VALUES ('506', '20.7991', '20.7991');
INSERT INTO `cbasset` VALUES ('507', '20.7991', '20.7991');
INSERT INTO `cbasset` VALUES ('508', '15.7072', '15.7072');
INSERT INTO `cbasset` VALUES ('509', '0', '0');
INSERT INTO `cbasset` VALUES ('510', '0', '0');
INSERT INTO `cbasset` VALUES ('511', '1', '1');
INSERT INTO `cbasset` VALUES ('512', '0', '0'), ('513', '0', '0'), ('514', '0', '0'), ('515', '0', '0'), ('516', '20.2859', '20.2859'), ('517', '0', '0'), ('518', '20.2859', '20.2859'), ('519', '0', '0'), ('520', '0', '0'), ('521', '1', '1'), ('522', '0', '0'), ('523', '1', '1'), ('524', '0', '0'), ('525', '20.2859', '20.2859'), ('526', '20.2859', '20.2859'), ('527', '1', '1'), ('528', '20.2859', '20.2859'), ('529', '0', '0'), ('530', '2892.58', '2892.58'), ('531', '0', '0');
INSERT INTO `cbasset` VALUES ('532', '0', '0'), ('533', '0', '0');
INSERT INTO `cbasset` VALUES ('534', '26.2885', '26.2885');
INSERT INTO `cbasset` VALUES ('535', '26.2885', '26.2885');
INSERT INTO `cbasset` VALUES ('536', '1', '1');
INSERT INTO `cbasset` VALUES ('537', '26.2885', '26.2885');
INSERT INTO `cbasset` VALUES ('538', '26.2885', '26.2885');
INSERT INTO `cbasset` VALUES ('539', '88142.7', '88142.7');
INSERT INTO `cbasset` VALUES ('540', '5.6748', '5.6748');
INSERT INTO `cbasset` VALUES ('541', '5.2476', '5.2476');
INSERT INTO `cbasset` VALUES ('542', '2.9625', '2.9625');
INSERT INTO `cbasset` VALUES ('543', '10.2981', '10.2981');
INSERT INTO `cbasset` VALUES ('544', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('545', '10.8074', '10.8074');
INSERT INTO `cbasset` VALUES ('546', '12.2844', '12.2844');
INSERT INTO `cbasset` VALUES ('547', '13.9465', '13.9465');
INSERT INTO `cbasset` VALUES ('548', '6.2728', '6.2728');
INSERT INTO `cbasset` VALUES ('549', '5.418', '5.418');
INSERT INTO `cbasset` VALUES ('550', '5.6811', '5.6811');
INSERT INTO `cbasset` VALUES ('551', '17.046', '17.046');
INSERT INTO `cbasset` VALUES ('552', '13.2703', '13.2703'), ('553', '15.1405', '15.1405'), ('554', '20.7991', '20.7991');
INSERT INTO `cbasset` VALUES ('555', '15.7072', '15.7072');
INSERT INTO `cbasset` VALUES ('556', '8.5563', '8.5563'), ('557', '16.2502', '16.2502'), ('558', '9.1273', '9.1273'), ('559', '5.5923', '5.5923'), ('560', '9.122', '9.122'), ('561', '13.3168', '13.3168'), ('562', '6.2483', '6.2483'), ('563', '7.0905', '7.0905'), ('564', '6.4748', '6.4748'), ('565', '8.3368', '8.3368'), ('566', '6.9881', '6.9881'), ('567', '10.3556', '10.3556'), ('568', '14.1045', '14.1045'), ('569', '12.6722', '12.6722'), ('570', '9.3808', '9.3808');
INSERT INTO `cbasset` VALUES ('571', '13.5549', '13.5549');
INSERT INTO `cbasset` VALUES ('572', '16.1398', '16.1398');
INSERT INTO `cbasset` VALUES ('573', '12.7821', '12.7821');
INSERT INTO `cbasset` VALUES ('574', '16.6132', '16.6132');
INSERT INTO `cbasset` VALUES ('575', '14.5381', '14.5381');
INSERT INTO `cbasset` VALUES ('576', '12.9717', '12.9717');
INSERT INTO `cbasset` VALUES ('577', '12.1359', '12.1359');
INSERT INTO `cbasset` VALUES ('578', '8.6345', '8.6345');
INSERT INTO `cbasset` VALUES ('579', '11.7046', '11.7046');
INSERT INTO `cbasset` VALUES ('580', '18.9233', '18.9233');
INSERT INTO `cbasset` VALUES ('581', '13.7238', '13.7238');
INSERT INTO `cbasset` VALUES ('582', '14.0688', '14.0688');
INSERT INTO `cbasset` VALUES ('583', '10.4271', '10.4271');
INSERT INTO `cbasset` VALUES ('584', '13.4499', '13.4499');
INSERT INTO `cbasset` VALUES ('585', '8.4814', '8.4814');
INSERT INTO `cbasset` VALUES ('586', '7.1889', '7.1889');
INSERT INTO `cbasset` VALUES ('587', '6.8954', '6.8954');
INSERT INTO `cbasset` VALUES ('588', '5.0448', '5.0448');
INSERT INTO `cbasset` VALUES ('589', '10.2981', '10.2981');
INSERT INTO `cbasset` VALUES ('590', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('591', '14.4012', '14.4012');
INSERT INTO `cbasset` VALUES ('592', '16.6601', '16.6601');
INSERT INTO `cbasset` VALUES ('593', '17.7663', '17.7663');
INSERT INTO `cbasset` VALUES ('594', '12.5647', '12.5647');
INSERT INTO `cbasset` VALUES ('595', '7.8575', '7.8575');
INSERT INTO `cbasset` VALUES ('596', '6.9022', '6.9022');
INSERT INTO `cbasset` VALUES ('597', '26.1668', '26.1668');
INSERT INTO `cbasset` VALUES ('598', '17.7686', '17.7686');
INSERT INTO `cbasset` VALUES ('599', '18.6671', '18.6671');
INSERT INTO `cbasset` VALUES ('600', '26.2802', '26.2802');
INSERT INTO `cbasset` VALUES ('601', '21.9118', '21.9118'), ('602', '10.3148', '10.3148'), ('603', '18.7152', '18.7152'), ('604', '10.149', '10.149');
INSERT INTO `cbasset` VALUES ('605', '9.9913', '9.9913');
INSERT INTO `cbasset` VALUES ('606', '12.1382', '12.1382');
INSERT INTO `cbasset` VALUES ('607', '13.315', '13.315');
INSERT INTO `cbasset` VALUES ('608', '6.6728', '6.6728');
INSERT INTO `cbasset` VALUES ('609', '8.4483', '8.4483');
INSERT INTO `cbasset` VALUES ('610', '12.0863', '12.0863');
INSERT INTO `cbasset` VALUES ('611', '13.7761', '13.7761');
INSERT INTO `cbasset` VALUES ('612', '11.3366', '11.3366');
INSERT INTO `cbasset` VALUES ('613', '22.8766', '22.8766');
INSERT INTO `cbasset` VALUES ('614', '7.8054', '7.8054');
INSERT INTO `cbasset` VALUES ('615', '7.2348', '7.2348');
INSERT INTO `cbasset` VALUES ('616', '7.0094', '7.0094');
INSERT INTO `cbasset` VALUES ('617', '10.2981', '10.2981');
INSERT INTO `cbasset` VALUES ('618', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('619', '15.9479', '15.9479');
INSERT INTO `cbasset` VALUES ('620', '15.7311', '15.7311');
INSERT INTO `cbasset` VALUES ('621', '16.7452', '16.7452');
INSERT INTO `cbasset` VALUES ('622', '16.7116', '16.7116');
INSERT INTO `cbasset` VALUES ('623', '10.1493', '10.1493');
INSERT INTO `cbasset` VALUES ('624', '9.56', '9.56');
INSERT INTO `cbasset` VALUES ('625', '26.1668', '26.1668');
INSERT INTO `cbasset` VALUES ('626', '20.3115', '20.3115');
INSERT INTO `cbasset` VALUES ('627', '21.965', '21.965');
INSERT INTO `cbasset` VALUES ('628', '26.2812', '26.2812');
INSERT INTO `cbasset` VALUES ('629', '22.175', '22.175');
INSERT INTO `cbasset` VALUES ('630', '10.3148', '10.3148');
INSERT INTO `cbasset` VALUES ('631', '20.0748', '20.0748');
INSERT INTO `cbasset` VALUES ('632', '13.5186', '13.5186');
INSERT INTO `cbasset` VALUES ('633', '16.5203', '16.5203');
INSERT INTO `cbasset` VALUES ('634', '12.7929', '12.7929');
INSERT INTO `cbasset` VALUES ('635', '12.7112', '12.7112');
INSERT INTO `cbasset` VALUES ('636', '8.9586', '8.9586');
INSERT INTO `cbasset` VALUES ('637', '10.4422', '10.4422');
INSERT INTO `cbasset` VALUES ('638', '10.8388', '10.8388');
INSERT INTO `cbasset` VALUES ('639', '21.9225', '21.9225');
INSERT INTO `cbasset` VALUES ('640', '13.7229', '13.7229');
INSERT INTO `cbasset` VALUES ('641', '22.7742', '22.7742');
INSERT INTO `cbasset` VALUES ('642', '0', '0');
INSERT INTO `cbasset` VALUES ('643', '0', '0');
INSERT INTO `cbasset` VALUES ('644', '0', '0');
INSERT INTO `cbasset` VALUES ('645', '0', '0');
INSERT INTO `cbasset` VALUES ('646', '0', '0');
INSERT INTO `cbasset` VALUES ('647', '0', '0');
INSERT INTO `cbasset` VALUES ('648', '0', '0');
INSERT INTO `cbasset` VALUES ('649', '0', '0');
INSERT INTO `cbasset` VALUES ('650', '0', '0');
INSERT INTO `cbasset` VALUES ('651', '0', '0');
INSERT INTO `cbasset` VALUES ('652', '0', '0');
INSERT INTO `cbasset` VALUES ('653', '0', '0');
INSERT INTO `cbasset` VALUES ('654', '0', '0');
INSERT INTO `cbasset` VALUES ('655', '0', '0');
INSERT INTO `cbasset` VALUES ('656', '0', '0');
INSERT INTO `cbasset` VALUES ('657', '0', '0');
INSERT INTO `cbasset` VALUES ('658', '0', '0');
INSERT INTO `cbasset` VALUES ('659', '0', '0');
INSERT INTO `cbasset` VALUES ('660', '0', '0');
INSERT INTO `cbasset` VALUES ('661', '0', '0');
INSERT INTO `cbasset` VALUES ('662', '0', '0');
INSERT INTO `cbasset` VALUES ('663', '0', '0');
INSERT INTO `cbasset` VALUES ('664', '0', '0');
INSERT INTO `cbasset` VALUES ('665', '0', '0');
INSERT INTO `cbasset` VALUES ('666', '0', '0');
INSERT INTO `cbasset` VALUES ('667', '0', '0');
INSERT INTO `cbasset` VALUES ('668', '0', '0');
INSERT INTO `cbasset` VALUES ('669', '0', '0');
INSERT INTO `cbasset` VALUES ('670', '0', '0');
INSERT INTO `cbasset` VALUES ('671', '0', '0');
INSERT INTO `cbasset` VALUES ('672', '0', '0');
INSERT INTO `cbasset` VALUES ('673', '0', '0');
INSERT INTO `cbasset` VALUES ('674', '0', '0');
INSERT INTO `cbasset` VALUES ('675', '0', '0');
INSERT INTO `cbasset` VALUES ('676', '0', '0');
INSERT INTO `cbasset` VALUES ('677', '0', '0');
INSERT INTO `cbasset` VALUES ('678', '0', '0');
INSERT INTO `cbasset` VALUES ('679', '0', '0');
INSERT INTO `cbasset` VALUES ('680', '0', '0');
INSERT INTO `cbasset` VALUES ('681', '0', '0');
INSERT INTO `cbasset` VALUES ('682', '0', '0');
INSERT INTO `cbasset` VALUES ('683', '0', '0');
INSERT INTO `cbasset` VALUES ('684', '0', '0');
INSERT INTO `cbasset` VALUES ('685', '0', '0'), ('686', '0', '0'), ('687', '0', '0');
INSERT INTO `cbasset` VALUES ('688', '0', '0');
INSERT INTO `cbasset` VALUES ('689', '0', '0');
INSERT INTO `cbasset` VALUES ('690', '0', '0');
INSERT INTO `cbasset` VALUES ('691', '0', '0');
INSERT INTO `cbasset` VALUES ('692', '0', '0');
INSERT INTO `cbasset` VALUES ('693', '0', '0');
INSERT INTO `cbasset` VALUES ('694', '0', '0');
INSERT INTO `cbasset` VALUES ('695', '0', '0');
INSERT INTO `cbasset` VALUES ('696', '0', '0'), ('697', '0', '0'), ('698', '0', '0'), ('699', '0', '0'), ('700', '0', '0');
INSERT INTO `cbasset` VALUES ('701', '0', '0');
INSERT INTO `cbasset` VALUES ('702', '0', '0');
INSERT INTO `cbasset` VALUES ('703', '0', '0');
INSERT INTO `cbasset` VALUES ('704', '0', '0');
INSERT INTO `cbasset` VALUES ('705', '0', '0');
INSERT INTO `cbasset` VALUES ('706', '0', '0');
INSERT INTO `cbasset` VALUES ('707', '0', '0');
INSERT INTO `cbasset` VALUES ('708', '0', '0');
INSERT INTO `cbasset` VALUES ('709', '0', '0');
INSERT INTO `cbasset` VALUES ('710', '0', '0');
INSERT INTO `cbasset` VALUES ('711', '0', '0');
INSERT INTO `cbasset` VALUES ('712', '0', '0');
INSERT INTO `cbasset` VALUES ('713', '0', '0');
INSERT INTO `cbasset` VALUES ('714', '0', '0');
INSERT INTO `cbasset` VALUES ('715', '0', '0');
INSERT INTO `cbasset` VALUES ('716', '0', '0');
INSERT INTO `cbasset` VALUES ('717', '0', '0');
INSERT INTO `cbasset` VALUES ('718', '0', '0');
INSERT INTO `cbasset` VALUES ('719', '0', '0');
INSERT INTO `cbasset` VALUES ('720', '0', '0');
INSERT INTO `cbasset` VALUES ('721', '0', '0');
INSERT INTO `cbasset` VALUES ('722', '0', '0');
INSERT INTO `cbasset` VALUES ('723', '0', '0');
INSERT INTO `cbasset` VALUES ('724', '0', '0');
INSERT INTO `cbasset` VALUES ('725', '0', '0');
INSERT INTO `cbasset` VALUES ('726', '0', '0');
INSERT INTO `cbasset` VALUES ('727', '0', '0');
INSERT INTO `cbasset` VALUES ('728', '0', '0');
INSERT INTO `cbasset` VALUES ('729', '0', '0'), ('730', '0', '0');
INSERT INTO `cbasset` VALUES ('731', '0', '0');
INSERT INTO `cbasset` VALUES ('732', '0', '0');
INSERT INTO `cbasset` VALUES ('733', '0', '0');
INSERT INTO `cbasset` VALUES ('734', '0', '0');
INSERT INTO `cbasset` VALUES ('735', '0', '0');
INSERT INTO `cbasset` VALUES ('736', '0', '0');
INSERT INTO `cbasset` VALUES ('737', '0', '0');
INSERT INTO `cbasset` VALUES ('738', '0', '0');
INSERT INTO `cbasset` VALUES ('739', '0', '0');
INSERT INTO `cbasset` VALUES ('740', '0', '0');
INSERT INTO `cbasset` VALUES ('741', '0', '0');
INSERT INTO `cbasset` VALUES ('742', '0', '0');
INSERT INTO `cbasset` VALUES ('743', '0', '0');
INSERT INTO `cbasset` VALUES ('744', '0', '0');
INSERT INTO `cbasset` VALUES ('745', '10.2981', '10.2981');
INSERT INTO `cbasset` VALUES ('746', '10.2981', '10.2981');
INSERT INTO `cbasset` VALUES ('747', '0', '0');
INSERT INTO `cbasset` VALUES ('748', '0', '0');
INSERT INTO `cbasset` VALUES ('749', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('750', '11.7759', '11.7759');
INSERT INTO `cbasset` VALUES ('751', '0', '0');
INSERT INTO `cbasset` VALUES ('752', '0', '0');
INSERT INTO `cbasset` VALUES ('753', '5.9447', '5.9447');
INSERT INTO `cbasset` VALUES ('754', '6.8687', '6.8687');
INSERT INTO `cbasset` VALUES ('755', '6.6372', '6.6372');
INSERT INTO `cbasset` VALUES ('756', '6.1877', '6.1877');
INSERT INTO `cbasset` VALUES ('757', '6.5452', '6.5452');
INSERT INTO `cbasset` VALUES ('758', '5.9055', '5.9055');
INSERT INTO `cbasset` VALUES ('759', '7.0722', '7.0722');
INSERT INTO `cbasset` VALUES ('760', '6.9161', '6.9161');
INSERT INTO `cbasset` VALUES ('761', '7.9647', '7.9647');
INSERT INTO `cbasset` VALUES ('762', '7.3268', '7.3268');
INSERT INTO `cbasset` VALUES ('763', '7.4029', '7.4029');
INSERT INTO `cbasset` VALUES ('764', '7.5666', '7.5666');
INSERT INTO `cbasset` VALUES ('765', '0', '0');
INSERT INTO `cbasset` VALUES ('766', '0', '0');
INSERT INTO `cbasset` VALUES ('767', '0', '0');
INSERT INTO `cbasset` VALUES ('768', '0', '0');
INSERT INTO `cbasset` VALUES ('769', '0', '0');
INSERT INTO `cbasset` VALUES ('770', '0', '0');
INSERT INTO `cbasset` VALUES ('771', '0', '0');
INSERT INTO `cbasset` VALUES ('772', '0', '0');
INSERT INTO `cbasset` VALUES ('773', '0', '0');
INSERT INTO `cbasset` VALUES ('774', '0', '0'), ('775', '0', '0'), ('776', '0', '0'), ('777', '7.7847', '7.7847');
INSERT INTO `cbasset` VALUES ('778', '8.1935', '8.1935');
INSERT INTO `cbasset` VALUES ('779', '0', '0');
INSERT INTO `cbasset` VALUES ('780', '0', '0');
INSERT INTO `cbasset` VALUES ('781', '0', '0');
INSERT INTO `cbasset` VALUES ('782', '0', '0');
INSERT INTO `cbasset` VALUES ('783', '0', '0');
INSERT INTO `cbasset` VALUES ('784', '0', '0');
INSERT INTO `cbasset` VALUES ('785', '0', '0');
INSERT INTO `cbasset` VALUES ('786', '0', '0');
INSERT INTO `cbasset` VALUES ('787', '0', '0');
INSERT INTO `cbasset` VALUES ('788', '9.1484', '9.1484');
INSERT INTO `cbasset` VALUES ('789', '8.4386', '8.4386');
INSERT INTO `cbasset` VALUES ('790', '7.9618', '7.9618');
INSERT INTO `cbasset` VALUES ('791', '9.5001', '9.5001');
INSERT INTO `cbasset` VALUES ('792', '8.5215', '8.5215');
INSERT INTO `cbasset` VALUES ('793', '7.98', '7.98');
INSERT INTO `cbasset` VALUES ('794', '9.5207', '9.5207');
INSERT INTO `cbasset` VALUES ('795', '9.7304', '9.7304'), ('796', '9.447', '9.447'), ('797', '10.2823', '10.2823'), ('798', '9.7569', '9.7569'), ('799', '10.6832', '10.6832'), ('800', '10.0848', '10.0848'), ('801', '0', '0'), ('802', '0', '0'), ('803', '0', '0'), ('804', '0', '0'), ('805', '0', '0'), ('806', '0', '0'), ('807', '0', '0'), ('808', '0', '0'), ('809', '0', '0'), ('810', '0', '0'), ('811', '0', '0'), ('812', '0', '0'), ('813', '0', '0'), ('814', '0', '0'), ('815', '0', '0'), ('816', '0', '0'), ('817', '0', '0'), ('818', '0', '0'), ('819', '0', '0'), ('820', '0', '0'), ('821', '0', '0'), ('822', '8.9276', '8.9276'), ('823', '8.0179', '8.0179'), ('824', '7.1592', '7.1592'), ('825', '5.1545', '5.1545'), ('826', '5.7061', '5.7061'), ('827', '7.2055', '7.2055'), ('828', '7.2791', '7.2791'), ('829', '10.7205', '10.7205'), ('830', '8.0652', '8.0652'), ('831', '7.9192', '7.9192'), ('832', '7.056', '7.056'), ('833', '7.3023', '7.3023'), ('834', '0', '0'), ('835', '0', '0'), ('836', '0', '0'), ('837', '0', '0'), ('838', '0', '0'), ('839', '0', '0'), ('840', '0', '0'), ('841', '0', '0'), ('842', '0', '0'), ('843', '0', '0'), ('844', '0', '0'), ('845', '0', '0'), ('846', '0', '0'), ('847', '0', '0'), ('848', '0', '0'), ('849', '0', '0'), ('850', '0', '0'), ('851', '0', '0'), ('852', '11.2587', '11.2587'), ('853', '16.932', '16.932'), ('854', '15.0634', '15.0634'), ('855', '10.7791', '10.7791'), ('856', '15.7618', '15.7618'), ('857', '15.0619', '15.0619'), ('858', '16.0937', '16.0937'), ('859', '20.4606', '20.4606'), ('860', '17.3923', '17.3923'), ('861', '19.4084', '19.4084'), ('862', '26.4371', '26.4371'), ('863', '15.1558', '15.1558'), ('864', '15.5151', '15.5151'), ('865', '0', '0'), ('866', '0', '0'), ('867', '0', '0'), ('868', '0', '0'), ('869', '0', '0'), ('870', '0', '0'), ('871', '0', '0'), ('872', '0', '0'), ('873', '0', '0'), ('874', '0', '0'), ('875', '0', '0'), ('876', '0', '0'), ('877', '0', '0'), ('878', '0', '0'), ('879', '0', '0'), ('880', '0', '0'), ('881', '0', '0'), ('882', '0', '0'), ('883', '15.7068', '15.7068'), ('884', '19.5145', '19.5145'), ('885', '20.1365', '20.1365'), ('886', '20.2978', '20.2978'), ('887', '19.1286', '19.1286'), ('888', '19.5955', '19.5955'), ('889', '19.8182', '19.8182'), ('890', '15.5178', '15.5178'), ('891', '19.4165', '19.4165'), ('892', '19.5596', '19.5596'), ('893', '19.7112', '19.7112'), ('894', '20.9886', '20.9886'), ('895', '19.5538', '19.5538'), ('896', '19.9841', '19.9841'), ('897', '0', '0'), ('898', '0', '0'), ('899', '0', '0'), ('900', '0', '0'), ('901', '0', '0'), ('902', '0', '0');
INSERT INTO `cbasset` VALUES ('903', '0', '0'), ('904', '0', '0'), ('905', '0', '0');
INSERT INTO `cbasset` VALUES ('906', '0', '0');
INSERT INTO `cbasset` VALUES ('907', '0', '0');
INSERT INTO `cbasset` VALUES ('908', '0', '0');
INSERT INTO `cbasset` VALUES ('909', '0', '0');
INSERT INTO `cbasset` VALUES ('910', '0', '0');
INSERT INTO `cbasset` VALUES ('911', '0', '0');
INSERT INTO `cbasset` VALUES ('912', '0', '0');
INSERT INTO `cbasset` VALUES ('913', '0', '0');
INSERT INTO `cbasset` VALUES ('914', '0', '0');
INSERT INTO `cbasset` VALUES ('915', '0', '0');
INSERT INTO `cbasset` VALUES ('916', '0', '0');
INSERT INTO `cbasset` VALUES ('917', '0', '0');
INSERT INTO `cbasset` VALUES ('918', '13.0716', '13.0716');
INSERT INTO `cbasset` VALUES ('919', '18.8582', '18.8582');
INSERT INTO `cbasset` VALUES ('920', '0', '0');
INSERT INTO `cbasset` VALUES ('921', '0', '0');
INSERT INTO `cbasset` VALUES ('922', '0', '0');
INSERT INTO `cbasset` VALUES ('923', '13.1916', '13.1916');
INSERT INTO `cbasset` VALUES ('924', '17.9279', '17.9279');
INSERT INTO `cbasset` VALUES ('925', '0', '0');
INSERT INTO `cbasset` VALUES ('926', '0', '0');
INSERT INTO `cbasset` VALUES ('927', '0', '0');
INSERT INTO `cbasset` VALUES ('928', '12.157', '12.157');
INSERT INTO `cbasset` VALUES ('929', '10.4273', '10.4273');
INSERT INTO `cbasset` VALUES ('930', '11.5926', '11.5926');
INSERT INTO `cbasset` VALUES ('931', '11.1979', '11.1979');
INSERT INTO `cbasset` VALUES ('932', '11.1967', '11.1967');
INSERT INTO `cbasset` VALUES ('933', '12.1296', '12.1296');
INSERT INTO `cbasset` VALUES ('934', '10.4639', '10.4639');
INSERT INTO `cbasset` VALUES ('935', '11.5926', '11.5926');
INSERT INTO `cbasset` VALUES ('936', '11.1979', '11.1979');
INSERT INTO `cbasset` VALUES ('937', '11.1967', '11.1967');
INSERT INTO `cbasset` VALUES ('938', '12.1296', '12.1296');
INSERT INTO `cbasset` VALUES ('939', '10.4374', '10.4374');
INSERT INTO `cbasset` VALUES ('940', '11.5926', '11.5926');
INSERT INTO `cbasset` VALUES ('941', '11.1979', '11.1979');
INSERT INTO `cbasset` VALUES ('942', '11.1967', '11.1967');
INSERT INTO `cbasset` VALUES ('943', '12.1296', '12.1296');
INSERT INTO `cbasset` VALUES ('944', '10.5496', '10.5496');
INSERT INTO `cbasset` VALUES ('945', '11.5926', '11.5926');
INSERT INTO `cbasset` VALUES ('946', '11.1979', '11.1979');
INSERT INTO `cbasset` VALUES ('947', '11.1967', '11.1967');
INSERT INTO `cbasset` VALUES ('948', '0', '0');
INSERT INTO `cbasset` VALUES ('949', '0', '0');
INSERT INTO `cbasset` VALUES ('950', '0', '0');
INSERT INTO `cbasset` VALUES ('951', '0', '0');
INSERT INTO `cbasset` VALUES ('952', '0', '0');
INSERT INTO `cbasset` VALUES ('953', '0', '0');
INSERT INTO `cbasset` VALUES ('954', '0', '0');
INSERT INTO `cbasset` VALUES ('955', '0', '0');
INSERT INTO `cbasset` VALUES ('956', '0', '0');
INSERT INTO `cbasset` VALUES ('957', '0', '0');
INSERT INTO `cbasset` VALUES ('958', '0', '0');
INSERT INTO `cbasset` VALUES ('959', '0', '0');
INSERT INTO `cbasset` VALUES ('960', '0', '0');
INSERT INTO `cbasset` VALUES ('961', '0', '0');
INSERT INTO `cbasset` VALUES ('962', '0', '0');
INSERT INTO `cbasset` VALUES ('963', '0', '0');
INSERT INTO `cbasset` VALUES ('964', '0', '0');
INSERT INTO `cbasset` VALUES ('965', '0', '0');
INSERT INTO `cbasset` VALUES ('966', '0', '0');
INSERT INTO `cbasset` VALUES ('967', '0', '0');
INSERT INTO `cbasset` VALUES ('968', '0', '0');
INSERT INTO `cbasset` VALUES ('969', '0', '0');
INSERT INTO `cbasset` VALUES ('970', '0', '0');
INSERT INTO `cbasset` VALUES ('971', '0', '0');
INSERT INTO `cbasset` VALUES ('972', '0', '0');
INSERT INTO `cbasset` VALUES ('973', '18.1463', '18.1463');
INSERT INTO `cbasset` VALUES ('974', '18.1463', '18.1463');
INSERT INTO `cbasset` VALUES ('975', '0', '0');
INSERT INTO `cbasset` VALUES ('976', '0', '0'), ('977', '0', '0'), ('978', '8.9132', '8.9132'), ('979', '7.2233', '7.2233'), ('980', '11.6471', '11.6471'), ('981', '14.447', '14.447'), ('982', '16.6053', '16.6053'), ('983', '12.7369', '12.7369'), ('984', '7.2897', '7.2897'), ('985', '16.2308', '16.2308'), ('986', '15.4483', '15.4483'), ('987', '18.0415', '18.0415'), ('988', '0', '0'), ('989', '0', '0'), ('990', '0', '0'), ('991', '0', '0'), ('992', '0', '0'), ('993', '0', '0'), ('994', '0', '0'), ('995', '0', '0'), ('996', '0', '0'), ('997', '0', '0'), ('998', '0', '0'), ('999', '0', '0'), ('1000', '0', '0'), ('1001', '0', '0'), ('1002', '0', '0'), ('1003', '12.6816', '12.6816');
INSERT INTO `cbasset` VALUES ('1004', '7.6218', '7.6218');
INSERT INTO `cbasset` VALUES ('1005', '35.8358', '35.8358');
INSERT INTO `cbasset` VALUES ('1006', '12.9624', '12.9624');
INSERT INTO `cbasset` VALUES ('1007', '13.3396', '13.3396');
INSERT INTO `cbasset` VALUES ('1008', '34.6476', '34.6476');
INSERT INTO `cbasset` VALUES ('1009', '0', '0');
INSERT INTO `cbasset` VALUES ('1010', '0', '0');
INSERT INTO `cbasset` VALUES ('1011', '0', '0');
INSERT INTO `cbasset` VALUES ('1012', '0', '0');
INSERT INTO `cbasset` VALUES ('1013', '0', '0');
INSERT INTO `cbasset` VALUES ('1014', '0', '0');
INSERT INTO `cbasset` VALUES ('1015', '0', '0');
INSERT INTO `cbasset` VALUES ('1016', '0', '0');
INSERT INTO `cbasset` VALUES ('1017', '0', '0');
INSERT INTO `cbasset` VALUES ('1018', '177639', '177639');
INSERT INTO `cbasset` VALUES ('1019', '0', '0');
INSERT INTO `cbasset` VALUES ('1020', '10.3776', '10.3776');
INSERT INTO `cbasset` VALUES ('1021', '0', '0');
INSERT INTO `cbasset` VALUES ('1022', '48.4172', '48.4172');
INSERT INTO `cbasset` VALUES ('1023', '36.4922', '36.4922');
INSERT INTO `cbasset` VALUES ('1024', '183.834', '183.834');
INSERT INTO `cbasset` VALUES ('1025', '1412.02', '1412.02');
INSERT INTO `cbasset` VALUES ('1026', '35.4356', '35.4356');
INSERT INTO `cbasset` VALUES ('1027', '52.4187', '52.4187');
INSERT INTO `cbasset` VALUES ('1028', '1408.16', '1408.16');
INSERT INTO `cbasset` VALUES ('1029', '36.3181', '36.3181');
INSERT INTO `cbasset` VALUES ('1030', '327.142', '327.142');
INSERT INTO `cbasset` VALUES ('1031', '1838.84', '1838.84');
INSERT INTO `cbasset` VALUES ('1032', '919.135', '919.135');
INSERT INTO `cbasset` VALUES ('1033', '3792.02', '3792.02');
INSERT INTO `cbasset` VALUES ('1034', '1546.91', '1546.91');
INSERT INTO `cbasset` VALUES ('1035', '2358.92', '2358.92');
INSERT INTO `cbasset` VALUES ('1036', '533.597', '533.597');
INSERT INTO `cbasset` VALUES ('1037', '2245.77', '2245.77');
INSERT INTO `cbasset` VALUES ('1038', '2564.25', '2564.25');
INSERT INTO `cbasset` VALUES ('1039', '90737.4', '90737.4');
INSERT INTO `cbasset` VALUES ('1040', '19981.7', '19981.7');
INSERT INTO `cbasset` VALUES ('1041', '4663.86', '4663.86');
INSERT INTO `cbasset` VALUES ('1042', '363.352', '363.352');
INSERT INTO `cbasset` VALUES ('1043', '323.004', '323.004');
INSERT INTO `cbasset` VALUES ('1044', '399.747', '399.747');
INSERT INTO `cbasset` VALUES ('1045', '3800.93', '3800.93');
INSERT INTO `cbasset` VALUES ('1046', '183.722', '183.722'), ('1047', '351.877', '351.877');
INSERT INTO `cbasset` VALUES ('1048', '349.392', '349.392');
INSERT INTO `cbasset` VALUES ('1049', '121.257', '121.257');
INSERT INTO `cbasset` VALUES ('1050', '2520.17', '2520.17');
INSERT INTO `cbasset` VALUES ('1051', '3720.06', '3720.06');
INSERT INTO `cbasset` VALUES ('1052', '59.7638', '59.7638');
INSERT INTO `cbasset` VALUES ('1053', '59.7638', '59.7638');
INSERT INTO `cbasset` VALUES ('1054', '3.4393', '3.4393');
INSERT INTO `cbasset` VALUES ('1055', '3.5105', '3.5105');
INSERT INTO `cbasset` VALUES ('1056', '3.5238', '3.5238');
INSERT INTO `cbasset` VALUES ('1057', '4.3853', '4.3853');
INSERT INTO `cbasset` VALUES ('1058', '3.4393', '3.4393');
INSERT INTO `cbasset` VALUES ('1059', '0', '0');
INSERT INTO `cbasset` VALUES ('1060', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1061', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1062', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1063', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1064', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1065', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1066', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1067', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1068', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1069', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1070', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1071', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1072', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1073', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1074', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1075', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1076', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1077', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1078', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1079', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1080', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1081', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1082', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1083', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1084', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1085', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1086', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1087', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1088', '2.6507', '2.6507'), ('1089', '2.6507', '2.6507'), ('1090', '2.6507', '2.6507'), ('1091', '0', '0'), ('1092', '0', '0');
INSERT INTO `cbasset` VALUES ('1093', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1094', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1095', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1096', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1097', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1098', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1099', '2.6507', '2.6507');
INSERT INTO `cbasset` VALUES ('1100', '1.6231', '1.6231');
INSERT INTO `cbasset` VALUES ('1101', '1.6231', '1.6231');
INSERT INTO `cbasset` VALUES ('1102', '1.6231', '1.6231');
INSERT INTO `cbasset` VALUES ('1103', '1.6231', '1.6231');
INSERT INTO `cbasset` VALUES ('1104', '0', '0');
INSERT INTO `cbasset` VALUES ('1105', '746.245', '746.245');
INSERT INTO `cbasset` VALUES ('1106', '704.072', '704.072');
INSERT INTO `cbasset` VALUES ('1107', '687.694', '687.694');
INSERT INTO `cbasset` VALUES ('1108', '746.245', '746.245');
INSERT INTO `cbasset` VALUES ('1109', '704.072', '704.072');
INSERT INTO `cbasset` VALUES ('1110', '687.694', '687.694');
INSERT INTO `cbasset` VALUES ('1111', '746.245', '746.245');
INSERT INTO `cbasset` VALUES ('1112', '704.072', '704.072');
INSERT INTO `cbasset` VALUES ('1113', '687.694', '687.694');
INSERT INTO `cbasset` VALUES ('1114', '1461.61', '1461.61');
INSERT INTO `cbasset` VALUES ('1115', '447.99', '447.99');
INSERT INTO `cbasset` VALUES ('1116', '447.99', '447.99');
INSERT INTO `cbasset` VALUES ('1117', '168.116', '168.116');
INSERT INTO `cbasset` VALUES ('1118', '168.116', '168.116');
INSERT INTO `cbasset` VALUES ('1119', '168.116', '168.116');
INSERT INTO `cbasset` VALUES ('1120', '82.7237', '82.7237');
INSERT INTO `cbasset` VALUES ('1121', '63.7901', '63.7901');
INSERT INTO `cbasset` VALUES ('1122', '76.1309', '76.1309');
INSERT INTO `cbasset` VALUES ('1123', '1461.61', '1461.61');
INSERT INTO `cbasset` VALUES ('1124', '5307.02', '5307.02');
INSERT INTO `cbasset` VALUES ('1125', '0', '0');
INSERT INTO `cbasset` VALUES ('1126', '40717.6', '40717.6');
INSERT INTO `cbasset` VALUES ('1127', '0', '0');
INSERT INTO `cbasset` VALUES ('1128', '2.2245', '2.2245');
INSERT INTO `cbasset` VALUES ('1129', '1', '1');
INSERT INTO `cbasset` VALUES ('1130', '22.1241', '22.1241');
INSERT INTO `cbasset` VALUES ('1131', '40.1152', '40.1152');
INSERT INTO `cbasset` VALUES ('1132', '166.22', '166.22');
INSERT INTO `cbasset` VALUES ('1133', '259.701', '259.701');
INSERT INTO `cbasset` VALUES ('1134', '218.244', '218.244');
INSERT INTO `cbasset` VALUES ('1135', '434.171', '434.171');
INSERT INTO `cbasset` VALUES ('1136', '250.791', '250.791');
INSERT INTO `cbasset` VALUES ('1137', '462.577', '462.577');
INSERT INTO `cbasset` VALUES ('1138', '744.288', '744.288');
INSERT INTO `cbasset` VALUES ('1139', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1140', '5061.86', '5061.86');
INSERT INTO `cbasset` VALUES ('1141', '2624.37', '2624.37');
INSERT INTO `cbasset` VALUES ('1142', '176.711', '176.711');
INSERT INTO `cbasset` VALUES ('1143', '87.5429', '87.5429');
INSERT INTO `cbasset` VALUES ('1144', '147.023', '147.023');
INSERT INTO `cbasset` VALUES ('1145', '0', '0');
INSERT INTO `cbasset` VALUES ('1146', '0', '0');
INSERT INTO `cbasset` VALUES ('1147', '0', '0');
INSERT INTO `cbasset` VALUES ('1148', '0', '0');
INSERT INTO `cbasset` VALUES ('1149', '1.8457', '1.8457');
INSERT INTO `cbasset` VALUES ('1150', '1.8457', '1.8457');
INSERT INTO `cbasset` VALUES ('1151', '1', '1');
INSERT INTO `cbasset` VALUES ('1152', '0', '0');
INSERT INTO `cbasset` VALUES ('1153', '0', '0');
INSERT INTO `cbasset` VALUES ('1154', '104.676', '104.676');
INSERT INTO `cbasset` VALUES ('1155', '1.0379', '1.0379');
INSERT INTO `cbasset` VALUES ('1156', '0', '0');
INSERT INTO `cbasset` VALUES ('1157', '0', '0');
INSERT INTO `cbasset` VALUES ('1158', '0', '0');
INSERT INTO `cbasset` VALUES ('1159', '0', '0');
INSERT INTO `cbasset` VALUES ('1160', '2.6527', '2.6527');
INSERT INTO `cbasset` VALUES ('1161', '2.6527', '2.6527');
INSERT INTO `cbasset` VALUES ('1162', '1.7139', '1.7139'), ('1163', '1.7139', '1.7139');
INSERT INTO `cbasset` VALUES ('1164', '1.7139', '1.7139'), ('1165', '0', '0');
INSERT INTO `cbasset` VALUES ('1166', '0', '0');
INSERT INTO `cbasset` VALUES ('1167', '1', '1');
INSERT INTO `cbasset` VALUES ('1168', '0', '0');
INSERT INTO `cbasset` VALUES ('1169', '1', '1');
INSERT INTO `cbasset` VALUES ('1170', '737.425', '737.425');
INSERT INTO `cbasset` VALUES ('1171', '737.425', '737.425');
INSERT INTO `cbasset` VALUES ('1172', '737.425', '737.425');
INSERT INTO `cbasset` VALUES ('1173', '0', '0');
INSERT INTO `cbasset` VALUES ('1174', '0', '0');
INSERT INTO `cbasset` VALUES ('1175', '2.6527', '2.6527');
INSERT INTO `cbasset` VALUES ('1176', '5774.17', '5774.17');
INSERT INTO `cbasset` VALUES ('1177', '132.445', '132.445');
INSERT INTO `cbasset` VALUES ('1178', '284.899', '284.899');
INSERT INTO `cbasset` VALUES ('1179', '237.278', '237.278');
INSERT INTO `cbasset` VALUES ('1180', '214.833', '214.833');
INSERT INTO `cbasset` VALUES ('1181', '214.833', '214.833');
INSERT INTO `cbasset` VALUES ('1182', '214.833', '214.833');
INSERT INTO `cbasset` VALUES ('1183', '423.048', '423.048');
INSERT INTO `cbasset` VALUES ('1184', '1263.95', '1263.95');
INSERT INTO `cbasset` VALUES ('1185', '1632.44', '1632.44');
INSERT INTO `cbasset` VALUES ('1186', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1187', '230.486', '230.486');
INSERT INTO `cbasset` VALUES ('1188', '1', '1');
INSERT INTO `cbasset` VALUES ('1189', '1', '1');
INSERT INTO `cbasset` VALUES ('1190', '125.671', '125.671');
INSERT INTO `cbasset` VALUES ('1191', '0.0086', '0.0086');
INSERT INTO `cbasset` VALUES ('1192', '0', '0');
INSERT INTO `cbasset` VALUES ('1193', '0', '0');
INSERT INTO `cbasset` VALUES ('1194', '0', '0');
INSERT INTO `cbasset` VALUES ('1195', '0', '0');
INSERT INTO `cbasset` VALUES ('1196', '1', '1');
INSERT INTO `cbasset` VALUES ('1197', '1', '1');
INSERT INTO `cbasset` VALUES ('1198', '1', '1');
INSERT INTO `cbasset` VALUES ('1199', '0', '0');
INSERT INTO `cbasset` VALUES ('1200', '0', '0');
INSERT INTO `cbasset` VALUES ('1201', '0', '0');
INSERT INTO `cbasset` VALUES ('1202', '1', '1');
INSERT INTO `cbasset` VALUES ('1203', '452.78', '452.78');
INSERT INTO `cbasset` VALUES ('1204', '1', '1');
INSERT INTO `cbasset` VALUES ('1205', '5011.33', '5011.33');
INSERT INTO `cbasset` VALUES ('1206', '2091.75', '2091.75');
INSERT INTO `cbasset` VALUES ('1207', '4376.32', '4376.32');
INSERT INTO `cbasset` VALUES ('1208', '13.8022', '13.8022');
INSERT INTO `cbasset` VALUES ('1209', '1', '1');
INSERT INTO `cbasset` VALUES ('1210', '1', '1');
INSERT INTO `cbasset` VALUES ('1211', '1', '1');
INSERT INTO `cbasset` VALUES ('1212', '1', '1');
INSERT INTO `cbasset` VALUES ('1213', '737.425', '737.425');
INSERT INTO `cbasset` VALUES ('1214', '737.425', '737.425');
INSERT INTO `cbasset` VALUES ('1215', '70.8106', '70.8106');
INSERT INTO `cbasset` VALUES ('1216', '5686.31', '5686.31');
INSERT INTO `cbasset` VALUES ('1217', '2008.62', '2008.62');
INSERT INTO `cbasset` VALUES ('1218', '6273.28', '6273.28');
INSERT INTO `cbasset` VALUES ('1219', '6009.01', '6009.01');
INSERT INTO `cbasset` VALUES ('1220', '5428.32', '5428.32');
INSERT INTO `cbasset` VALUES ('1221', '1530.63', '1530.63');
INSERT INTO `cbasset` VALUES ('1222', '5046.87', '5046.87');
INSERT INTO `cbasset` VALUES ('1223', '2887.54', '2887.54');
INSERT INTO `cbasset` VALUES ('1224', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1225', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1226', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1227', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1228', '71988.4', '71988.4');
INSERT INTO `cbasset` VALUES ('1229', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1230', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1231', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1232', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1233', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1234', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1235', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1236', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1237', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1238', '3074.2', '3074.2');
INSERT INTO `cbasset` VALUES ('1239', '4093.8', '4093.8');
INSERT INTO `cbasset` VALUES ('1240', '2302', '2302');
INSERT INTO `cbasset` VALUES ('1241', '1570.28', '1570.28');
INSERT INTO `cbasset` VALUES ('1242', '1820.48', '1820.48');
INSERT INTO `cbasset` VALUES ('1243', '1159.41', '1159.41'), ('1244', '959.17', '959.17'), ('1245', '1537.86', '1537.86'), ('1246', '782.527', '782.527');
INSERT INTO `cbasset` VALUES ('1247', '2573.47', '2573.47');
INSERT INTO `cbasset` VALUES ('1248', '2089.32', '2089.32');
INSERT INTO `cbasset` VALUES ('1249', '563.78', '563.78');
INSERT INTO `cbasset` VALUES ('1250', '1120.81', '1120.81');
INSERT INTO `cbasset` VALUES ('1251', '694.765', '694.765');
INSERT INTO `cbasset` VALUES ('1252', '1205.22', '1205.22');
INSERT INTO `cbasset` VALUES ('1253', '1230.52', '1230.52');
INSERT INTO `cbasset` VALUES ('1254', '725.982', '725.982');
INSERT INTO `cbasset` VALUES ('1255', '847.035', '847.035');
INSERT INTO `cbasset` VALUES ('1256', '609.132', '609.132');
INSERT INTO `cbasset` VALUES ('1257', '2078.4', '2078.4');
INSERT INTO `cbasset` VALUES ('1258', '1372.79', '1372.79');
INSERT INTO `cbasset` VALUES ('1259', '1173.88', '1173.88');
INSERT INTO `cbasset` VALUES ('1260', '1077.03', '1077.03');
INSERT INTO `cbasset` VALUES ('1261', '858.695', '858.695');
INSERT INTO `cbasset` VALUES ('1262', '18511', '18511');
INSERT INTO `cbasset` VALUES ('1263', '1051.66', '1051.66');
INSERT INTO `cbasset` VALUES ('1264', '1272.58', '1272.58');
INSERT INTO `cbasset` VALUES ('1265', '271.475', '271.475');
INSERT INTO `cbasset` VALUES ('1266', '492.654', '492.654');
INSERT INTO `cbasset` VALUES ('1267', '404.931', '404.931');
INSERT INTO `cbasset` VALUES ('1268', '176.047', '176.047');
INSERT INTO `cbasset` VALUES ('1269', '414.329', '414.329');
INSERT INTO `cbasset` VALUES ('1270', '1387.44', '1387.44');
INSERT INTO `cbasset` VALUES ('1271', '937.231', '937.231');
INSERT INTO `cbasset` VALUES ('1272', '929.783', '929.783');
INSERT INTO `cbasset` VALUES ('1273', '812.643', '812.643');
INSERT INTO `cbasset` VALUES ('1274', '1383.38', '1383.38');
INSERT INTO `cbasset` VALUES ('1275', '994.967', '994.967');
INSERT INTO `cbasset` VALUES ('1276', '612.749', '612.749');
INSERT INTO `cbasset` VALUES ('1277', '2462.32', '2462.32');
INSERT INTO `cbasset` VALUES ('1278', '4887.21', '4887.21');
INSERT INTO `cbasset` VALUES ('1279', '2892.58', '2892.58');
INSERT INTO `cbasset` VALUES ('1280', '0', '0');
INSERT INTO `cbasset` VALUES ('1281', '0', '0');
INSERT INTO `cbasset` VALUES ('1282', '4887.21', '4887.21');
INSERT INTO `cbasset` VALUES ('1283', '0', '0');
INSERT INTO `cbasset` VALUES ('1284', '0', '0');
INSERT INTO `cbasset` VALUES ('1285', '0', '0');
INSERT INTO `cbasset` VALUES ('1286', '0', '0');
INSERT INTO `cbasset` VALUES ('1287', '0', '0');
INSERT INTO `cbasset` VALUES ('1288', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1289', '0', '0');
INSERT INTO `cbasset` VALUES ('1290', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1291', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1292', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1293', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1294', '60746', '60746');
INSERT INTO `cbasset` VALUES ('1295', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1296', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1297', '71320', '71320');
INSERT INTO `cbasset` VALUES ('1298', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1299', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1300', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1301', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1302', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1303', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1304', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1305', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1306', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1307', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1308', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1309', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1310', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1311', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1312', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1313', '0', '0');
INSERT INTO `cbasset` VALUES ('1314', '2127.63', '2127.63');
INSERT INTO `cbasset` VALUES ('1315', '71259.6', '71259.6');
INSERT INTO `cbasset` VALUES ('1316', '20343.6', '20343.6');
INSERT INTO `cbasset` VALUES ('1317', '15.0943', '15.0943');
INSERT INTO `cbasset` VALUES ('1318', '15.0943', '15.0943');
INSERT INTO `cbasset` VALUES ('1319', '15.0943', '15.0943');
INSERT INTO `cbasset` VALUES ('1320', '15.0943', '15.0943');
INSERT INTO `cbasset` VALUES ('1321', '14.0572', '14.0572'), ('1322', '10.8985', '10.8985');
INSERT INTO `cbasset` VALUES ('1323', '11.1139', '11.1139'), ('1324', '12.9123', '12.9123'), ('1325', '11.6598', '11.6598'), ('1326', '8.9006', '8.9006'), ('1327', '10.6219', '10.6219');
INSERT INTO `cbasset` VALUES ('1328', '14.9607', '14.9607');
INSERT INTO `cbasset` VALUES ('1329', '13.8683', '13.8683');
INSERT INTO `cbasset` VALUES ('1330', '11.3768', '11.3768');
INSERT INTO `cbasset` VALUES ('1331', '17.2788', '17.2788');
INSERT INTO `cbasset` VALUES ('1332', '14.0092', '14.0092');
INSERT INTO `cbasset` VALUES ('1333', '12.3987', '12.3987');
INSERT INTO `cbasset` VALUES ('1334', '10.2959', '10.2959');
INSERT INTO `cbasset` VALUES ('1335', '12.1115', '12.1115');
INSERT INTO `cbasset` VALUES ('1336', '15.8867', '15.8867');
INSERT INTO `cbasset` VALUES ('1337', '10.14', '10.14');
INSERT INTO `cbasset` VALUES ('1338', '13.0442', '13.0442');
INSERT INTO `cbasset` VALUES ('1339', '10.906', '10.906');
INSERT INTO `cbasset` VALUES ('1340', '7.5209', '7.5209');
INSERT INTO `cbasset` VALUES ('1341', '7.0572', '7.0572');
INSERT INTO `cbasset` VALUES ('1342', '9.1341', '9.1341');
INSERT INTO `cbasset` VALUES ('1343', '8.0091', '8.0091');
INSERT INTO `cbasset` VALUES ('1344', '10.5847', '10.5847');
INSERT INTO `cbasset` VALUES ('1345', '7.3622', '7.3622');
INSERT INTO `cbasset` VALUES ('1346', '9.7377', '9.7377');
INSERT INTO `cbasset` VALUES ('1347', '9.0092', '9.0092');
INSERT INTO `cbasset` VALUES ('1348', '9.3962', '9.3962');
INSERT INTO `cbasset` VALUES ('1349', '8.8003', '8.8003');
INSERT INTO `cbasset` VALUES ('1350', '10.8009', '10.8009');
INSERT INTO `cbasset` VALUES ('1351', '10.9008', '10.9008');
INSERT INTO `cbasset` VALUES ('1352', '9.7168', '9.7168');
INSERT INTO `cbasset` VALUES ('1353', '10.2628', '10.2628');
INSERT INTO `cbasset` VALUES ('1354', '5.6584', '5.6584');
INSERT INTO `cbasset` VALUES ('1355', '13.3153', '13.3153');
INSERT INTO `cbasset` VALUES ('1356', '11.8662', '11.8662');
INSERT INTO `cbasset` VALUES ('1357', '14.505', '14.505');
INSERT INTO `cbasset` VALUES ('1358', '10.7234', '10.7234');
INSERT INTO `cbasset` VALUES ('1359', '11.8698', '11.8698');
INSERT INTO `cbasset` VALUES ('1360', '10.8296', '10.8296');
INSERT INTO `cbasset` VALUES ('1361', '10.144', '10.144');
INSERT INTO `cbasset` VALUES ('1362', '10.1367', '10.1367');
INSERT INTO `cbasset` VALUES ('1363', '0', '0');
INSERT INTO `cbasset` VALUES ('1364', '0', '0'), ('1365', '0', '0'), ('1366', '0', '0'), ('1367', '0', '0'), ('1368', '0', '0'), ('1369', '0', '0'), ('1370', '0', '0');
INSERT INTO `cbasset` VALUES ('1371', '0', '0');
INSERT INTO `cbasset` VALUES ('1372', '0', '0');
INSERT INTO `cbasset` VALUES ('1373', '0', '0');
INSERT INTO `cbasset` VALUES ('1374', '0', '0');
INSERT INTO `cbasset` VALUES ('1375', '0', '0');
INSERT INTO `cbasset` VALUES ('1376', '0', '0');
INSERT INTO `cbasset` VALUES ('1377', '0', '0');
INSERT INTO `cbasset` VALUES ('1378', '0', '0');
INSERT INTO `cbasset` VALUES ('1379', '0', '0');
INSERT INTO `cbasset` VALUES ('1380', '0', '0');
INSERT INTO `cbasset` VALUES ('1381', '0', '0');
INSERT INTO `cbasset` VALUES ('1382', '0', '0');
INSERT INTO `cbasset` VALUES ('1383', '0', '0');
INSERT INTO `cbasset` VALUES ('1384', '0', '0');
INSERT INTO `cbasset` VALUES ('1385', '0', '0');
INSERT INTO `cbasset` VALUES ('1386', '0', '0');
INSERT INTO `cbasset` VALUES ('1387', '0', '0');
INSERT INTO `cbasset` VALUES ('1388', '0', '0');
INSERT INTO `cbasset` VALUES ('1389', '0', '0');
INSERT INTO `cbasset` VALUES ('1390', '0', '0');
INSERT INTO `cbasset` VALUES ('1391', '0', '0');
INSERT INTO `cbasset` VALUES ('1392', '0', '0');
INSERT INTO `cbasset` VALUES ('1393', '0', '0');
INSERT INTO `cbasset` VALUES ('1394', '0', '0');
INSERT INTO `cbasset` VALUES ('1395', '0', '0');
INSERT INTO `cbasset` VALUES ('1396', '0', '0');
INSERT INTO `cbasset` VALUES ('1397', '0', '0');
INSERT INTO `cbasset` VALUES ('1398', '0', '0');
INSERT INTO `cbasset` VALUES ('1399', '0', '0');
INSERT INTO `cbasset` VALUES ('1400', '0', '0');
INSERT INTO `cbasset` VALUES ('1401', '0', '0');
INSERT INTO `cbasset` VALUES ('1402', '0', '0');
INSERT INTO `cbasset` VALUES ('1403', '0', '0');
INSERT INTO `cbasset` VALUES ('1404', '0', '0');
INSERT INTO `cbasset` VALUES ('1405', '0', '0');
INSERT INTO `cbasset` VALUES ('1406', '0', '0');
INSERT INTO `cbasset` VALUES ('1407', '0', '0');
INSERT INTO `cbasset` VALUES ('1408', '0', '0');
INSERT INTO `cbasset` VALUES ('1409', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1410', '20046.2', '20046.2'), ('1411', '20046.2', '20046.2'), ('1412', '20046.2', '20046.2'), ('1413', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1414', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1415', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1416', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1417', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1418', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1419', '34535.3', '34535.3');
INSERT INTO `cbasset` VALUES ('1420', '73961.1', '73961.1');
INSERT INTO `cbasset` VALUES ('1421', '47613', '47613');
INSERT INTO `cbasset` VALUES ('1422', '21030.7', '21030.7');
INSERT INTO `cbasset` VALUES ('1423', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1424', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1425', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1426', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1427', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1428', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1429', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1430', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1431', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1432', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1433', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1434', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1435', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1436', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1437', '20046.2', '20046.2');
INSERT INTO `cbasset` VALUES ('1438', '14751.3', '14751.3');
INSERT INTO `cbasset` VALUES ('1439', '1', '1');
INSERT INTO `cbasset` VALUES ('1440', '0', '0');
INSERT INTO `cbasset` VALUES ('1441', '617.566', '617.566');
INSERT INTO `cbasset` VALUES ('1442', '619.486', '619.486');
INSERT INTO `cbasset` VALUES ('1443', '679.469', '679.469');
INSERT INTO `cbasset` VALUES ('1444', '210.797', '210.797');
INSERT INTO `cbasset` VALUES ('1445', '230.263', '230.263');
INSERT INTO `cbasset` VALUES ('1446', '219.854', '219.854');
INSERT INTO `cbasset` VALUES ('1447', '207.639', '207.639');
INSERT INTO `cbasset` VALUES ('1448', '221.548', '221.548');
INSERT INTO `cbasset` VALUES ('1449', '505.702', '505.702');
INSERT INTO `cbasset` VALUES ('1450', '292.603', '292.603');
INSERT INTO `cbasset` VALUES ('1451', '427.804', '427.804');
INSERT INTO `cbasset` VALUES ('1452', '283.312', '283.312');
INSERT INTO `cbasset` VALUES ('1453', '161.795', '161.795');
INSERT INTO `cbasset` VALUES ('1454', '152.47', '152.47');
INSERT INTO `cbasset` VALUES ('1455', '88.1398', '88.1398');
INSERT INTO `cbasset` VALUES ('1456', '104.986', '104.986');
INSERT INTO `cbasset` VALUES ('1457', '108.319', '108.319');
INSERT INTO `cbasset` VALUES ('1458', '118.353', '118.353');
INSERT INTO `cbasset` VALUES ('1459', '88.2314', '88.2314');
INSERT INTO `cbasset` VALUES ('1460', '104.676', '104.676');
INSERT INTO `cbasset` VALUES ('1461', '5011.33', '5011.33');
INSERT INTO `cbasset` VALUES ('1462', '2091.75', '2091.75');
INSERT INTO `cbasset` VALUES ('1463', '4376.32', '4376.32');
INSERT INTO `cbasset` VALUES ('1464', '0', '0');
INSERT INTO `cbasset` VALUES ('1465', '0', '0');
INSERT INTO `cbasset` VALUES ('1466', '0', '0');
INSERT INTO `cbasset` VALUES ('1467', '0', '0');
INSERT INTO `cbasset` VALUES ('1468', '0', '0');
INSERT INTO `cbasset` VALUES ('1469', '0', '0');
INSERT INTO `cbasset` VALUES ('1470', '0', '0');
INSERT INTO `cbasset` VALUES ('1471', '0', '0');
INSERT INTO `cbasset` VALUES ('1472', '0', '0');
INSERT INTO `cbasset` VALUES ('1473', '0', '0');
INSERT INTO `cbasset` VALUES ('1474', '0', '0');
INSERT INTO `cbasset` VALUES ('1475', '0', '0');
INSERT INTO `cbasset` VALUES ('1476', '0', '0');
INSERT INTO `cbasset` VALUES ('1477', '0', '0');
INSERT INTO `cbasset` VALUES ('1478', '0', '0');
INSERT INTO `cbasset` VALUES ('1479', '0', '0');
INSERT INTO `cbasset` VALUES ('1480', '0', '0');
INSERT INTO `cbasset` VALUES ('1481', '0', '0');
INSERT INTO `cbasset` VALUES ('1482', '0', '0');
INSERT INTO `cbasset` VALUES ('1483', '0', '0');
INSERT INTO `cbasset` VALUES ('1484', '0', '0');
INSERT INTO `cbasset` VALUES ('1485', '0', '0');
INSERT INTO `cbasset` VALUES ('1486', '0', '0');
INSERT INTO `cbasset` VALUES ('1487', '0', '0');
INSERT INTO `cbasset` VALUES ('1488', '0', '0');
INSERT INTO `cbasset` VALUES ('1489', '0', '0');
INSERT INTO `cbasset` VALUES ('1490', '0', '0');
INSERT INTO `cbasset` VALUES ('1491', '0', '0');
INSERT INTO `cbasset` VALUES ('1492', '0', '0');
INSERT INTO `cbasset` VALUES ('1493', '0', '0');
INSERT INTO `cbasset` VALUES ('1494', '0', '0');
INSERT INTO `cbasset` VALUES ('1495', '0', '0'), ('1496', '0', '0'), ('1497', '0', '0'), ('1498', '0', '0'), ('1499', '0', '0'), ('1500', '0', '0'), ('1501', '1', '1'), ('1502', '1', '1'), ('1503', '0', '0'), ('1504', '0', '0'), ('1505', '0', '0'), ('1506', '0', '0'), ('1507', '0', '0'), ('1508', '0', '0'), ('1509', '0', '0'), ('1510', '1', '1'), ('1511', '0', '0'), ('1512', '0', '0');
INSERT INTO `cbasset` VALUES ('1513', '2.4198', '2.4198');
INSERT INTO `cbasset` VALUES ('1514', '3.6731', '3.6731');
INSERT INTO `cbasset` VALUES ('1515', '4.6917', '4.6917');
INSERT INTO `cbasset` VALUES ('1516', '4009.68', '4009.68');
INSERT INTO `cbasset` VALUES ('1517', '8086.46', '8086.46');
INSERT INTO `cbasset` VALUES ('1518', '40717.6', '40717.6');
INSERT INTO `cbasset` VALUES ('1519', '1', '1');
INSERT INTO `cbasset` VALUES ('1520', '0', '0');
INSERT INTO `cbasset` VALUES ('1521', '0', '0');
INSERT INTO `cbasset` VALUES ('1522', '0', '0');
INSERT INTO `cbasset` VALUES ('1523', '16.6056', '16.6056');
INSERT INTO `cbasset` VALUES ('1524', '2894.51', '2894.51');
INSERT INTO `cbasset` VALUES ('1525', '1733.01', '1733.01');
INSERT INTO `cbasset` VALUES ('1526', '1969.94', '1969.94');
INSERT INTO `cbasset` VALUES ('1527', '1285.93', '1285.93');
INSERT INTO `cbasset` VALUES ('1528', '1073.63', '1073.63');
INSERT INTO `cbasset` VALUES ('1529', '17087', '17087');
INSERT INTO `cbasset` VALUES ('1530', '4665.16', '4665.16');
INSERT INTO `cbasset` VALUES ('1531', '2035.37', '2035.37');
INSERT INTO `cbasset` VALUES ('1532', '1181.36', '1181.36');
INSERT INTO `cbasset` VALUES ('1533', '3216.51', '3216.51');
INSERT INTO `cbasset` VALUES ('1534', '2278.21', '2278.21');
INSERT INTO `cbasset` VALUES ('1535', '1876.36', '1876.36');
INSERT INTO `cbasset` VALUES ('1536', '2315.43', '2315.43');
INSERT INTO `cbasset` VALUES ('1537', '113.395', '113.395');
INSERT INTO `cbasset` VALUES ('1538', '102.907', '102.907');
INSERT INTO `cbasset` VALUES ('1539', '100.149', '100.149');
INSERT INTO `cbasset` VALUES ('1540', '1902.45', '1902.45');
INSERT INTO `cbasset` VALUES ('1541', '1507.1', '1507.1');
INSERT INTO `cbasset` VALUES ('1542', '1103.26', '1103.26');
INSERT INTO `cbasset` VALUES ('1543', '1', '1');
INSERT INTO `cbasset` VALUES ('1544', '955.21', '955.21');
INSERT INTO `cbasset` VALUES ('1545', '955.21', '955.21');
INSERT INTO `cbasset` VALUES ('1546', '13.5549', '13.5549');
INSERT INTO `cbasset` VALUES ('1547', '0', '0');
INSERT INTO `cbasset` VALUES ('1548', '0', '0');
INSERT INTO `cbasset` VALUES ('1549', '6450.08', '6450.08');
INSERT INTO `cbasset` VALUES ('1550', '18.0922', '18.0922');
INSERT INTO `cbasset` VALUES ('1551', '5645.7', '5645.7');
INSERT INTO `cbasset` VALUES ('1552', '27950.8', '27950.8');
INSERT INTO `cbasset` VALUES ('1553', '1', '1');
INSERT INTO `cbasset` VALUES ('1554', '1', '1');
INSERT INTO `cbasset` VALUES ('1555', '1', '1'), ('1556', '256.494', '256.494'), ('1557', '55895', '55895'), ('1558', '0', '0'), ('1559', '0', '0'), ('1560', '0', '0'), ('1561', '1', '1');
INSERT INTO `cbasset` VALUES ('1562', '1', '1'), ('1563', '1', '1'), ('1564', '427.804', '427.804');
INSERT INTO `cbasset` VALUES ('1565', '0.0423', '0.0423');
INSERT INTO `cbasset` VALUES ('1566', '0', '0');
INSERT INTO `cbasset` VALUES ('1567', '0', '0');
INSERT INTO `cbasset` VALUES ('1568', '0', '0');
INSERT INTO `cbasset` VALUES ('1569', '0', '0');
INSERT INTO `cbasset` VALUES ('1570', '17.046', '17.046');
INSERT INTO `cbasset` VALUES ('1571', '1', '1');
INSERT INTO `cbasset` VALUES ('1572', '0', '0');
INSERT INTO `cbasset` VALUES ('1573', '0', '0');
INSERT INTO `cbasset` VALUES ('1574', '0', '0');
INSERT INTO `cbasset` VALUES ('1575', '152.651', '152.651');
INSERT INTO `cbasset` VALUES ('1576', '139.269', '139.269');
INSERT INTO `cbasset` VALUES ('1577', '1', '1');
INSERT INTO `cbasset` VALUES ('1578', '1', '1');
INSERT INTO `cbasset` VALUES ('1579', '1', '1');
INSERT INTO `cbasset` VALUES ('1580', '1', '1');
INSERT INTO `cbasset` VALUES ('1581', '1', '1');
INSERT INTO `cbasset` VALUES ('1582', '1', '1');
INSERT INTO `cbasset` VALUES ('1583', '1', '1');
INSERT INTO `cbasset` VALUES ('1584', '1', '1');
INSERT INTO `cbasset` VALUES ('1585', '1', '1');
INSERT INTO `cbasset` VALUES ('1586', '1', '1');
INSERT INTO `cbasset` VALUES ('1587', '1', '1');
INSERT INTO `cbasset` VALUES ('1588', '1', '1');
INSERT INTO `cbasset` VALUES ('1589', '0', '0');
INSERT INTO `cbasset` VALUES ('1590', '0', '0');
INSERT INTO `cbasset` VALUES ('1591', '0', '0');
INSERT INTO `cbasset` VALUES ('1592', '0', '0');
INSERT INTO `cbasset` VALUES ('1593', '0', '0');
INSERT INTO `cbasset` VALUES ('1594', '1', '1');
INSERT INTO `cbasset` VALUES ('1595', '2.2404', '2.2404');
INSERT INTO `cbasset` VALUES ('1596', '3.0478', '3.0478');
INSERT INTO `cbasset` VALUES ('1597', '2.2404', '2.2404');
INSERT INTO `cbasset` VALUES ('1598', '2.2404', '2.2404');
INSERT INTO `cbasset` VALUES ('1599', '0', '0');
INSERT INTO `cbasset` VALUES ('1600', '33.1141', '33.1141');
INSERT INTO `cbasset` VALUES ('1601', '38.2145', '38.2145');
INSERT INTO `cbasset` VALUES ('1602', '35.129', '35.129');
INSERT INTO `cbasset` VALUES ('1603', '37.4673', '37.4673'), ('1604', '31.0342', '31.0342');
INSERT INTO `cbasset` VALUES ('1605', '34.7244', '34.7244'), ('1606', '34.377', '34.377');
INSERT INTO `cbasset` VALUES ('1607', '39.0332', '39.0332');
INSERT INTO `cbasset` VALUES ('1608', '43.3215', '43.3215');
INSERT INTO `cbasset` VALUES ('1609', '37.2536', '37.2536');
INSERT INTO `cbasset` VALUES ('1610', '40.7359', '40.7359');
INSERT INTO `cbasset` VALUES ('1611', '40.239', '40.239');
INSERT INTO `cbasset` VALUES ('1612', '36.6137', '36.6137');
INSERT INTO `cbasset` VALUES ('1613', '38.812', '38.812');
INSERT INTO `cbasset` VALUES ('1614', '39.0837', '39.0837');
INSERT INTO `cbasset` VALUES ('1615', '39.9424', '39.9424');
INSERT INTO `cbasset` VALUES ('1616', '43.4649', '43.4649');
INSERT INTO `cbasset` VALUES ('1617', '36.8318', '36.8318');
INSERT INTO `cbasset` VALUES ('1618', '40.0801', '40.0801');
INSERT INTO `cbasset` VALUES ('1619', '41.6068', '41.6068');
INSERT INTO `cbasset` VALUES ('1620', '49.1692', '49.1692');
INSERT INTO `cbasset` VALUES ('1621', '29.5523', '29.5523');
INSERT INTO `cbasset` VALUES ('1622', '23.4214', '23.4214');
INSERT INTO `cbasset` VALUES ('1623', '30.431', '30.431');
INSERT INTO `cbasset` VALUES ('1624', '43.737', '43.737');
INSERT INTO `cbasset` VALUES ('1625', '47.2848', '47.2848');
INSERT INTO `cbasset` VALUES ('1626', '50.573', '50.573');
INSERT INTO `cbasset` VALUES ('1627', '59.28', '59.28');
INSERT INTO `cbasset` VALUES ('1628', '76.5479', '76.5479');
INSERT INTO `cbasset` VALUES ('1629', '89.1885', '89.1885'), ('1630', '31.6272', '31.6272'), ('1631', '37.5373', '37.5373'), ('1632', '36.8829', '36.8829'), ('1633', '38.4991', '38.4991'), ('1634', '50.6244', '50.6244'), ('1635', '57.6966', '57.6966'), ('1636', '70.7637', '70.7637'), ('1637', '77.0179', '77.0179'), ('1638', '89.7246', '89.7246'), ('1639', '59.6559', '59.6559'), ('1640', '61.9582', '61.9582'), ('1641', '72.6909', '72.6909'), ('1642', '86.318', '86.318'), ('1643', '89.427', '89.427'), ('1644', '87.2501', '87.2501'), ('1645', '77.4025', '77.4025'), ('1646', '100.864', '100.864'), ('1647', '118.017', '118.017'), ('1648', '79.3079', '79.3079'), ('1649', '93.1814', '93.1814'), ('1650', '122.717', '122.717'), ('1651', '77.7569', '77.7569'), ('1652', '84.4425', '84.4425'), ('1653', '97.0303', '97.0303'), ('1654', '75.0218', '75.0218'), ('1655', '78.4008', '78.4008'), ('1656', '80.3815', '80.3815'), ('1657', '80.8647', '80.8647'), ('1658', '102.429', '102.429'), ('1659', '99.1508', '99.1508'), ('1660', '58.6377', '58.6377'), ('1661', '63.4574', '63.4574'), ('1662', '65.9327', '65.9327'), ('1663', '68.8428', '68.8428'), ('1664', '67.42', '67.42'), ('1665', '72.7367', '72.7367'), ('1666', '82.0358', '82.0358'), ('1667', '86.4037', '86.4037'), ('1668', '86.2918', '86.2918'), ('1669', '80.2779', '80.2779'), ('1670', '80.2776', '80.2776'), ('1671', '75.2057', '75.2057'), ('1672', '47.9884', '47.9884'), ('1673', '57.1318', '57.1318'), ('1674', '63.7341', '63.7341'), ('1675', '49.2858', '49.2858'), ('1676', '75.3168', '75.3168'), ('1677', '104.224', '104.224'), ('1678', '51.0758', '51.0758');
INSERT INTO `cbasset` VALUES ('1679', '61.8504', '61.8504'), ('1680', '82.9295', '82.9295');
INSERT INTO `cbasset` VALUES ('1681', '26.1379', '26.1379');
INSERT INTO `cbasset` VALUES ('1682', '26.1379', '26.1379');
INSERT INTO `cbasset` VALUES ('1683', '26.1379', '26.1379'), ('1684', '12.3963', '12.3963'), ('1685', '12.3963', '12.3963'), ('1686', '12.3963', '12.3963'), ('1687', '11.9828', '11.9828'), ('1688', '11.9828', '11.9828'), ('1689', '11.9828', '11.9828'), ('1690', '0', '0'), ('1691', '0', '0'), ('1692', '0', '0'), ('1693', '1', '1'), ('1694', '1', '1'), ('1695', '206.731', '206.731'), ('1696', '1', '1');
INSERT INTO `cbasset` VALUES ('1697', '1', '1');
INSERT INTO `cbasset` VALUES ('1698', '1.4064', '1.4064');
INSERT INTO `cbasset` VALUES ('1699', '1.4064', '1.4064');
INSERT INTO `cbasset` VALUES ('1700', '1.4064', '1.4064');
INSERT INTO `cbasset` VALUES ('1701', '1.3565', '1.3565');
INSERT INTO `cbasset` VALUES ('1702', '1.2105', '1.2105');
INSERT INTO `cbasset` VALUES ('1703', '1.3565', '1.3565');
INSERT INTO `cbasset` VALUES ('1704', '0', '0');
INSERT INTO `cbasset` VALUES ('1705', '1.1269', '1.1269');
INSERT INTO `cbasset` VALUES ('1706', '2.3812', '2.3812');
INSERT INTO `cbasset` VALUES ('1707', '2.4563', '2.4563');
INSERT INTO `cbasset` VALUES ('1708', '0.9047', '0.9047');
INSERT INTO `cbasset` VALUES ('1709', '0', '0');
INSERT INTO `cbasset` VALUES ('1710', '0', '0');
INSERT INTO `cbasset` VALUES ('1711', '0.9047', '0.9047');
INSERT INTO `cbasset` VALUES ('1712', '0.8993', '0.8993');
INSERT INTO `cbasset` VALUES ('1713', '1.4268', '1.4268');
INSERT INTO `cbasset` VALUES ('1714', '1.4268', '1.4268');
INSERT INTO `cbasset` VALUES ('1715', '1.4268', '1.4268');
INSERT INTO `cbasset` VALUES ('1716', '2.2955', '2.2955');
INSERT INTO `cbasset` VALUES ('1717', '0.9047', '0.9047');
INSERT INTO `cbasset` VALUES ('1718', '1870.94', '1870.94');
INSERT INTO `cbasset` VALUES ('1719', '0.8993', '0.8993');
INSERT INTO `cbasset` VALUES ('1720', '0', '0');
INSERT INTO `cbasset` VALUES ('1721', '1', '1');
INSERT INTO `cbasset` VALUES ('1722', '2.2955', '2.2955');
INSERT INTO `cbasset` VALUES ('1723', '2.2955', '2.2955');
INSERT INTO `cbasset` VALUES ('1724', '2.3194', '2.3194');
INSERT INTO `cbasset` VALUES ('1725', '1', '1');
INSERT INTO `cbasset` VALUES ('1726', '0', '0'), ('1727', '2.2955', '2.2955'), ('1728', '0', '0'), ('1729', '0', '0'), ('1730', '0', '0'), ('1731', '1', '1'), ('1732', '0', '0'), ('1733', '0', '0'), ('1734', '1', '1'), ('1735', '0', '0'), ('1736', '0', '0'), ('1737', '1', '1'), ('1738', '0', '0'), ('1739', '0', '0'), ('1740', '1', '1'), ('1741', '0', '0'), ('1742', '0', '0'), ('1743', '1', '1'), ('1744', '0', '0'), ('1745', '0', '0'), ('1746', '1', '1'), ('1747', '0', '0'), ('1748', '1', '1'), ('1749', '1', '1'), ('1750', '1', '1'), ('1751', '1', '1'), ('1752', '1', '1'), ('1753', '1', '1'), ('1754', '0', '0'), ('1755', '0', '0'), ('1756', '0', '0'), ('1757', '0', '0'), ('1758', '0', '0'), ('1759', '0', '0'), ('1760', '0', '0'), ('1761', '0.479', '0.479'), ('1762', '0', '0'), ('1763', '0.479', '0.479'), ('1764', '1', '1'), ('1765', '1', '1'), ('1766', '1', '1'), ('1767', '1.2754', '1.2754'), ('1768', '1.2754', '1.2754'), ('1769', '1.2754', '1.2754'), ('1770', '1.6165', '1.6165'), ('1771', '1.6262', '1.6262'), ('1772', '1.6262', '1.6262'), ('1773', '1.6262', '1.6262'), ('1774', '0', '0'), ('1775', '1', '1'), ('1776', '0', '0'), ('1777', '1', '1'), ('1778', '1.9707', '1.9707'), ('1779', '1', '1'), ('1780', '1', '1'), ('1781', '0.1', '0.1'), ('1782', '1.6544', '1.6544'), ('1783', '0', '0'), ('1784', '1.6544', '1.6544'), ('1785', '2.2404', '2.2404'), ('1786', '2.2404', '2.2404'), ('1787', '2.2404', '2.2404'), ('1788', '2.6507', '2.6507'), ('1789', '1', '1'), ('1790', '157.06', '157.06'), ('1791', '0', '0'), ('1792', '0', '0'), ('1793', '1', '1'), ('1794', '0', '0'), ('1795', '0', '0'), ('1796', '1', '1'), ('1797', '1.0685', '1.0685'), ('1798', '1', '1'), ('1799', '1.0685', '1.0685'), ('1800', '1.0685', '1.0685'), ('1801', '0', '0'), ('1802', '0', '0'), ('1803', '0', '0'), ('1804', '0', '0'), ('1805', '0', '0'), ('1806', '5158.65', '5158.65'), ('1807', '3503.4', '3503.4'), ('1808', '61486.9', '61486.9'), ('1809', '3290.89', '3290.89'), ('1810', '0', '0'), ('1811', '1', '1'), ('1812', '1', '1'), ('1813', '1', '1'), ('1814', '0', '0'), ('1815', '0', '0'), ('1816', '0', '0'), ('1817', '0', '0'), ('1818', '0', '0'), ('1819', '0', '0'), ('1820', '1', '1'), ('1821', '35355.3', '35355.3'), ('1822', '746.245', '746.245'), ('1823', '704.072', '704.072'), ('1824', '687.694', '687.694'), ('1825', '409.243', '409.243'), ('1826', '514.861', '514.861'), ('1827', '326.225', '326.225'), ('1828', '390.467', '390.467'), ('1829', '428.636', '428.636'), ('1830', '495.868', '495.868'), ('1831', '622.363', '622.363'), ('1832', '595.905', '595.905'), ('1833', '544.549', '544.549'), ('1834', '0', '0'), ('1835', '1', '1'), ('1836', '4.3759', '4.3759'), ('1837', '26.1379', '26.1379'), ('1838', '26.1379', '26.1379'), ('1839', '26.1379', '26.1379'), ('1840', '26.1379', '26.1379'), ('1841', '35355.3', '35355.3'), ('1842', '12.3963', '12.3963'), ('1843', '12.3963', '12.3963'), ('1844', '12.3963', '12.3963'), ('1845', '12.3963', '12.3963'), ('1846', '11.9828', '11.9828'), ('1847', '11.9828', '11.9828'), ('1848', '11.9828', '11.9828'), ('1849', '11.9828', '11.9828'), ('1850', '476.879', '476.879'), ('1851', '0', '0'), ('1852', '0', '0'), ('1853', '0', '0'), ('1854', '0', '0'), ('1855', '0', '0'), ('1856', '0', '0'), ('1857', '0', '0'), ('1858', '0', '0'), ('1859', '0', '0'), ('1860', '0', '0'), ('1861', '0', '0'), ('1862', '0', '0'), ('1863', '0', '0'), ('1864', '0', '0'), ('1865', '0', '0'), ('1866', '0', '0'), ('1867', '0', '0'), ('1868', '0', '0'), ('1869', '0', '0'), ('1870', '0', '0'), ('1871', '0', '0'), ('1872', '0', '0'), ('1873', '0', '0'), ('1874', '0', '0'), ('1875', '0', '0'), ('1876', '1.7139', '1.7139'), ('1877', '1.7139', '1.7139'), ('1878', '192.981', '192.981'), ('1879', '35355.3', '35355.3'), ('1880', '0', '0'), ('1881', '676.467', '676.467'), ('1882', '1919.9', '1919.9'), ('1883', '62.916', '62.916'), ('1884', '9.0543', '9.0543'), ('1885', '0', '0'), ('1886', '0', '0'), ('1887', '1', '1'), ('1888', '9.0543', '9.0543'), ('1889', '2892.58', '2892.58'), ('1890', '0', '0'), ('1891', '11.706', '11.706'), ('1892', '0', '0'), ('1893', '11.706', '11.706'), ('1894', '1', '1'), ('1895', '0', '0'), ('1896', '1', '1'), ('1897', '0', '0'), ('1898', '15.6834', '15.6834'), ('1899', '1', '1'), ('1900', '41.1167', '41.1167'), ('1901', '0', '0'), ('1902', '1', '1'), ('1903', '8.0059', '8.0059'), ('1904', '1', '1'), ('1905', '6.7014', '6.7014'), ('1906', '1', '1'), ('1907', '7.6491', '7.6491'), ('1908', '0', '0'), ('1909', '3.5695', '3.5695');
INSERT INTO `cbasset` VALUES ('1910', '0', '0'), ('1911', '0', '0');
INSERT INTO `cbasset` VALUES ('1912', '0', '0');
INSERT INTO `cbasset` VALUES ('1913', '0', '0');
INSERT INTO `cbasset` VALUES ('1914', '369.628', '369.628');
INSERT INTO `cbasset` VALUES ('1915', '7002.09', '7002.09');
INSERT INTO `cbasset` VALUES ('1916', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1917', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1918', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1919', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1920', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1921', '65.6727', '65.6727');
INSERT INTO `cbasset` VALUES ('1922', '63.8595', '63.8595');
INSERT INTO `cbasset` VALUES ('1923', '63.8595', '63.8595');
INSERT INTO `cbasset` VALUES ('1924', '63.8595', '63.8595');
INSERT INTO `cbasset` VALUES ('1925', '63.8595', '63.8595');
INSERT INTO `cbasset` VALUES ('1926', '63.8595', '63.8595'), ('1927', '63.8595', '63.8595'), ('1928', '62.3704', '62.3704');
INSERT INTO `cbasset` VALUES ('1929', '62.3704', '62.3704');
INSERT INTO `cbasset` VALUES ('1930', '0', '0');
INSERT INTO `cbasset` VALUES ('1931', '0', '0');
INSERT INTO `cbasset` VALUES ('1932', '0', '0');
INSERT INTO `cbasset` VALUES ('1933', '0', '0');
INSERT INTO `cbasset` VALUES ('1934', '0', '0');
INSERT INTO `cbasset` VALUES ('1935', '0', '0');
INSERT INTO `cbasset` VALUES ('1936', '0', '0');
INSERT INTO `cbasset` VALUES ('1937', '0', '0');
INSERT INTO `cbasset` VALUES ('1938', '0', '0');
INSERT INTO `cbasset` VALUES ('1939', '0', '0');
INSERT INTO `cbasset` VALUES ('1940', '0', '0');
INSERT INTO `cbasset` VALUES ('1941', '0', '0');
INSERT INTO `cbasset` VALUES ('1942', '0', '0');
INSERT INTO `cbasset` VALUES ('1943', '0', '0');
INSERT INTO `cbasset` VALUES ('1944', '1302.33', '1302.33');
INSERT INTO `cbasset` VALUES ('1945', '15811.4', '15811.4');
INSERT INTO `cbasset` VALUES ('1946', '2307.74', '2307.74');
INSERT INTO `cbasset` VALUES ('1947', '0.6247', '0.6247'), ('1948', '0.6247', '0.6247'), ('1949', '1', '1'), ('1950', '1', '1'), ('1951', '0', '0'), ('1952', '0', '0'), ('1953', '0', '0'), ('1954', '0', '0'), ('1955', '0', '0'), ('1956', '646.215', '646.215'), ('1957', '41.5171', '41.5171'), ('1958', '11.259', '11.259'), ('1959', '4.9285', '4.9285'), ('1960', '1.8813', '1.8813'), ('1961', '1.6235', '1.6235'), ('1962', '1.3533', '1.3533'), ('1963', '1.6456', '1.6456'), ('1964', '1.6036', '1.6036'), ('1965', '1.633', '1.633'), ('1966', '1.7429', '1.7429'), ('1967', '5.3587', '5.3587'), ('1968', '0', '0'), ('1969', '0', '0'), ('1970', '0', '0'), ('1971', '0', '0'), ('1972', '0', '0'), ('1973', '0', '0'), ('1974', '302.94', '302.94'), ('1975', '0', '0'), ('1976', '0', '0'), ('1977', '0', '0'), ('1978', '0', '0'), ('1979', '0', '0'), ('1980', '0', '0'), ('1981', '0', '0'), ('1982', '0', '0'), ('1983', '37.9173', '37.9173'), ('1984', '2961.6', '2961.6'), ('1985', '7795.87', '7795.87'), ('1986', '1261.19', '1261.19'), ('1987', '9307.66', '9307.66'), ('1988', '4511.34', '4511.34'), ('1989', '325.984', '325.984'), ('1990', '334.481', '334.481'), ('1991', '172.246', '172.246');
INSERT INTO `cbasset` VALUES ('1992', '205.359', '205.359'), ('1993', '215.12', '215.12'), ('1994', '223.166', '223.166'), ('1995', '0', '0'), ('1996', '0', '0'), ('1997', '8446.42', '8446.42'), ('1998', '5846.7', '5846.7'), ('1999', '10626.4', '10626.4'), ('2000', '5795.32', '5795.32'), ('2001', '2678.28', '2678.28'), ('2002', '5729.82', '5729.82'), ('2003', '1', '1'), ('2004', '838.362', '838.362'), ('2005', '835.731', '835.731'), ('2006', '0', '0'), ('2007', '0', '0'), ('2008', '0', '0'), ('2009', '0', '0'), ('2010', '2.2245', '2.2245'), ('2011', '2.6507', '2.6507'), ('2012', '2.6562', '2.6562'), ('2013', '1', '1'), ('2014', '2.631', '2.631'), ('2015', '2.631', '2.631'), ('2016', '2.631', '2.631'), ('2017', '0', '0'), ('2018', '0', '0'), ('2019', '0', '0'), ('2020', '0', '0'), ('2021', '0', '0'), ('2022', '0', '0'), ('2023', '0', '0'), ('2024', '0', '0'), ('2025', '0', '0'), ('2026', '0', '0'), ('2027', '0', '0'), ('2028', '0', '0'), ('2029', '0', '0'), ('2030', '0', '0'), ('2031', '0', '0'), ('2032', '2.7582', '2.7582'), ('2033', '2.7582', '2.7582'), ('2034', '2.7582', '2.7582'), ('2035', '3.4489', '3.4489'), ('2036', '0', '0'), ('2037', '2.7412', '2.7412'), ('2038', '3.2772', '3.2772'), ('2039', '3.5809', '3.5809'), ('2040', '1.8555', '1.8555'), ('2041', '0', '0'), ('2042', '0', '0'), ('2043', '0', '0'), ('2044', '0', '0'), ('2045', '0', '0'), ('2046', '0', '0'), ('2047', '0', '0'), ('2048', '0', '0'), ('2049', '0', '0'), ('2050', '1437.32', '1437.32'), ('2051', '1596.9', '1596.9'), ('2052', '1845.27', '1845.27'), ('2053', '758.88', '758.88'), ('2054', '844.177', '844.177'), ('2055', '738.769', '738.769'), ('2056', '58996', '58996'), ('2057', '58.0448', '58.0448'), ('2058', '259.808', '259.808'), ('2059', '0', '0'), ('2060', '0', '0'), ('2061', '58.0448', '58.0448'), ('2062', '58.0448', '58.0448'), ('2063', '1962.69', '1962.69'), ('2064', '111.172', '111.172'), ('2065', '216.35', '216.35'), ('2066', '28.2885', '28.2885'), ('2067', '0', '0'), ('2068', '0', '0'), ('2069', '0', '0'), ('2070', '58.0448', '58.0448'), ('2071', '58.0448', '58.0448'), ('2072', '58.0448', '58.0448'), ('2073', '0', '0'), ('2074', '0', '0'), ('2075', '0', '0'), ('2076', '0', '0'), ('2077', '0', '0'), ('2078', '0', '0'), ('2079', '0', '0'), ('2080', '0', '0'), ('2081', '0', '0'), ('2082', '0', '0'), ('2083', '0', '0'), ('2084', '0', '0'), ('2085', '0', '0'), ('2086', '0', '0'), ('2087', '364.402', '364.402'), ('2088', '36.1036', '36.1036'), ('2089', '2536.62', '2536.62'), ('2090', '54.7585', '54.7585'), ('2091', '61.0995', '61.0995'), ('2092', '70.4043', '70.4043'), ('2093', '76.0904', '76.0904'), ('2094', '241.802', '241.802'), ('2095', '130.951', '130.951'), ('2096', '169.323', '169.323'), ('2097', '2790.3', '2790.3'), ('2098', '2790.3', '2790.3'), ('2099', '2790.3', '2790.3'), ('2100', '32.5628', '32.5628'), ('2101', '309.355', '309.355'), ('2102', '1.9038', '1.9038'), ('2103', '0', '0'), ('2104', '0', '0'), ('2105', '0', '0'), ('2106', '0', '0'), ('2107', '0', '0'), ('2108', '0', '0'), ('2109', '0', '0'), ('2110', '0', '0'), ('2111', '0', '0'), ('2112', '4471.59', '4471.59'), ('2113', '0.3928', '0.3928'), ('2114', '0', '0'), ('2115', '0', '0'), ('2116', '0', '0'), ('2117', '19.5534', '19.5534'), ('2118', '19.4476', '19.4476'), ('2119', '19.4476', '19.4476'), ('2120', '19.4476', '19.4476'), ('2121', '19.4476', '19.4476'), ('2122', '0', '0'), ('2123', '0', '0'), ('2124', '0', '0'), ('2125', '0', '0'), ('2126', '0', '0'), ('2127', '25.2126', '25.2126'), ('2128', '31.635', '31.635'), ('2129', '70.7323', '70.7323'), ('2130', '69.5733', '69.5733'), ('2131', '37.0314', '37.0314'), ('2132', '38.544', '38.544'), ('2133', '181.558', '181.558'), ('2134', '49.9302', '49.9302'), ('2135', '562.108', '562.108'), ('2136', '81.3791', '81.3791'), ('2137', '1172.57', '1172.57'), ('2138', '0', '0'), ('2139', '0', '0'), ('2140', '0', '0'), ('2141', '0', '0'), ('2142', '0', '0'), ('2143', '0', '0'), ('2144', '6.1433', '6.1433'), ('2145', '0', '0'), ('2146', '1', '1'), ('2147', '0', '0'), ('2148', '1', '1'), ('2149', '1', '1'), ('2150', '1', '1'), ('2151', '1', '1'), ('2152', '0', '0'), ('2153', '2.0177', '2.0177'), ('2154', '1', '1'), ('2155', '1', '1'), ('2156', '1', '1'), ('2157', '0', '0'), ('2158', '0', '0'), ('2159', '0', '0'), ('2160', '0', '0'), ('2161', '0', '0'), ('2162', '1', '1'), ('2163', '0', '0'), ('2164', '0', '0'), ('2165', '0', '0'), ('2166', '75348.7', '75348.7'), ('2167', '1', '1'), ('2168', '1', '1'), ('2169', '1', '1'), ('2170', '0', '0'), ('2171', '2.223', '2.223'), ('2172', '2.223', '2.223'), ('2173', '2.223', '2.223'), ('2174', '0', '0'), ('2175', '0', '0'), ('2176', '0', '0'), ('2177', '0', '0'), ('2178', '0', '0'), ('2179', '0', '0'), ('2180', '0', '0'), ('2181', '0', '0');
INSERT INTO `cbasset` VALUES ('2182', '41.5902', '41.5902'), ('2183', '7.7031', '7.7031'), ('2184', '15.5672', '15.5672'), ('2185', '17.0376', '17.0376');
INSERT INTO `cbasset` VALUES ('2186', '8.1155', '8.1155');
INSERT INTO `cbasset` VALUES ('2187', '5.8313', '5.8313');
INSERT INTO `cbasset` VALUES ('2188', '12.3589', '12.3589');
INSERT INTO `cbasset` VALUES ('2189', '32.6954', '32.6954');
INSERT INTO `cbasset` VALUES ('2190', '1.8977', '1.8977');
INSERT INTO `cbasset` VALUES ('2191', '1.8977', '1.8977');
INSERT INTO `cbasset` VALUES ('2192', '1.8977', '1.8977');
INSERT INTO `cbasset` VALUES ('2193', '79.304', '79.304');
INSERT INTO `cbasset` VALUES ('2194', '38.0021', '38.0021');
INSERT INTO `cbasset` VALUES ('2195', '40.16', '40.16');
INSERT INTO `cbasset` VALUES ('2196', '72.3943', '72.3943');
INSERT INTO `cbasset` VALUES ('2197', '1', '1');
INSERT INTO `cbasset` VALUES ('2198', '1', '1');
INSERT INTO `cbasset` VALUES ('2199', '1', '1');
INSERT INTO `cbasset` VALUES ('2200', '0', '0');
INSERT INTO `cbasset` VALUES ('2201', '15.7072', '15.7072');
INSERT INTO `cbasset` VALUES ('2202', '0', '0');
INSERT INTO `cbasset` VALUES ('2203', '0', '0');
INSERT INTO `cbasset` VALUES ('2204', '0', '0');
INSERT INTO `cbasset` VALUES ('2205', '0', '0');
INSERT INTO `cbasset` VALUES ('2206', '11182.8', '11182.8');
INSERT INTO `cbasset` VALUES ('2207', '5168.34', '5168.34');
INSERT INTO `cbasset` VALUES ('2208', '6627.52', '6627.52');
INSERT INTO `cbasset` VALUES ('2209', '3697.45', '3697.45');
INSERT INTO `cbasset` VALUES ('2210', '2268.2', '2268.2');
INSERT INTO `cbasset` VALUES ('2211', '2537.37', '2537.37');
INSERT INTO `cbasset` VALUES ('2212', '1482.8', '1482.8');
INSERT INTO `cbasset` VALUES ('2213', '1395.03', '1395.03');
INSERT INTO `cbasset` VALUES ('2214', '254.293', '254.293');
INSERT INTO `cbasset` VALUES ('2215', '208.102', '208.102'), ('2216', '28.1309', '28.1309');
INSERT INTO `cbasset` VALUES ('2217', '0', '0');
INSERT INTO `cbasset` VALUES ('2218', '0', '0');
INSERT INTO `cbasset` VALUES ('2219', '0', '0');
INSERT INTO `cbasset` VALUES ('2220', '0', '0');
INSERT INTO `cbasset` VALUES ('2221', '0', '0');
INSERT INTO `cbasset` VALUES ('2222', '0', '0');
INSERT INTO `cbasset` VALUES ('2223', '0', '0');
INSERT INTO `cbasset` VALUES ('2224', '7.0566', '7.0566');
INSERT INTO `cbasset` VALUES ('2225', '0', '0');
INSERT INTO `cbasset` VALUES ('2226', '7.6153', '7.6153');
INSERT INTO `cbasset` VALUES ('2227', '0', '0');
INSERT INTO `cbasset` VALUES ('2228', '8.8305', '8.8305');
INSERT INTO `cbasset` VALUES ('2229', '99.2835', '99.2835');
INSERT INTO `cbasset` VALUES ('2230', '11.0692', '11.0692');
INSERT INTO `cbasset` VALUES ('2231', '12.7206', '12.7206'), ('2232', '0', '0'), ('2233', '0', '0'), ('2234', '0', '0'), ('2235', '1', '1');
INSERT INTO `cbasset` VALUES ('2236', '0', '0');
INSERT INTO `cbasset` VALUES ('2237', '0', '0');
INSERT INTO `cbasset` VALUES ('2238', '0', '0');
INSERT INTO `cbasset` VALUES ('2239', '803.048', '803.048');
INSERT INTO `cbasset` VALUES ('2240', '176.821', '176.821');
INSERT INTO `cbasset` VALUES ('2241', '44.1095', '44.1095');
INSERT INTO `cbasset` VALUES ('2242', '39.1778', '39.1778');
INSERT INTO `cbasset` VALUES ('2243', '0', '0');
INSERT INTO `cbasset` VALUES ('2244', '0', '0');
INSERT INTO `cbasset` VALUES ('2245', '0', '0');
INSERT INTO `cbasset` VALUES ('2246', '0', '0');
INSERT INTO `cbasset` VALUES ('2247', '17.9589', '17.9589');
INSERT INTO `cbasset` VALUES ('2248', '6.7036', '6.7036');
INSERT INTO `cbasset` VALUES ('2249', '0', '0');
INSERT INTO `cbasset` VALUES ('2250', '4462.15', '4462.15');
INSERT INTO `cbasset` VALUES ('2251', '0', '0');
INSERT INTO `cbasset` VALUES ('2252', '0', '0');
INSERT INTO `cbasset` VALUES ('2253', '1', '1');
INSERT INTO `cbasset` VALUES ('2254', '0', '0');
INSERT INTO `cbasset` VALUES ('2255', '0', '0');
INSERT INTO `cbasset` VALUES ('2256', '0', '0');
INSERT INTO `cbasset` VALUES ('2257', '1', '1');
INSERT INTO `cbasset` VALUES ('2258', '1', '1');
INSERT INTO `cbasset` VALUES ('2259', '0', '0');
INSERT INTO `cbasset` VALUES ('2260', '0', '0');
INSERT INTO `cbasset` VALUES ('2261', '0', '0');
INSERT INTO `cbasset` VALUES ('2262', '0', '0');
INSERT INTO `cbasset` VALUES ('2263', '21.9118', '21.9118');
INSERT INTO `cbasset` VALUES ('2264', '1.2911', '1.2911');
INSERT INTO `cbasset` VALUES ('2265', '0', '0');
INSERT INTO `cbasset` VALUES ('2266', '0', '0');
INSERT INTO `cbasset` VALUES ('2267', '0', '0');
INSERT INTO `cbasset` VALUES ('2268', '0', '0');
INSERT INTO `cbasset` VALUES ('2269', '0', '0');
INSERT INTO `cbasset` VALUES ('2270', '0', '0');
INSERT INTO `cbasset` VALUES ('2271', '0', '0');
INSERT INTO `cbasset` VALUES ('2272', '0', '0');
INSERT INTO `cbasset` VALUES ('2273', '0', '0');
INSERT INTO `cbasset` VALUES ('2274', '0', '0');
INSERT INTO `cbasset` VALUES ('2275', '0', '0');
INSERT INTO `cbasset` VALUES ('2276', '0', '0');
INSERT INTO `cbasset` VALUES ('2277', '0', '0');
INSERT INTO `cbasset` VALUES ('2278', '0', '0');
INSERT INTO `cbasset` VALUES ('2279', '0', '0');
INSERT INTO `cbasset` VALUES ('2280', '0', '0');
INSERT INTO `cbasset` VALUES ('2281', '0', '0');
INSERT INTO `cbasset` VALUES ('2282', '0', '0');
INSERT INTO `cbasset` VALUES ('2283', '0', '0');
INSERT INTO `cbasset` VALUES ('2284', '0', '0');
INSERT INTO `cbasset` VALUES ('2285', '0', '0');
INSERT INTO `cbasset` VALUES ('2286', '0', '0');
INSERT INTO `cbasset` VALUES ('2287', '0', '0');
INSERT INTO `cbasset` VALUES ('2288', '0', '0');
INSERT INTO `cbasset` VALUES ('2289', '45.8429', '45.8429');
INSERT INTO `cbasset` VALUES ('2290', '24.6211', '24.6211');
INSERT INTO `cbasset` VALUES ('2291', '23.0744', '23.0744');
INSERT INTO `cbasset` VALUES ('2292', '15.839', '15.839');
INSERT INTO `cbasset` VALUES ('2293', '15.839', '15.839');
INSERT INTO `cbasset` VALUES ('2294', '15.839', '15.839');
INSERT INTO `cbasset` VALUES ('2295', '6.0339', '6.0339');
INSERT INTO `cbasset` VALUES ('2296', '5.9249', '5.9249');
INSERT INTO `cbasset` VALUES ('2297', '6.6121', '6.6121');
INSERT INTO `cbasset` VALUES ('2298', '39.5772', '39.5772');
INSERT INTO `cbasset` VALUES ('2299', '310.94', '310.94');
INSERT INTO `cbasset` VALUES ('2300', '194.014', '194.014');
INSERT INTO `cbasset` VALUES ('2301', '6173.87', '6173.87');
INSERT INTO `cbasset` VALUES ('2302', '58.8556', '58.8556');
INSERT INTO `cbasset` VALUES ('2303', '58.3256', '58.3256');
INSERT INTO `cbasset` VALUES ('2304', '58.3927', '58.3927');
INSERT INTO `cbasset` VALUES ('2305', '3301.79', '3301.79');
INSERT INTO `cbasset` VALUES ('2306', '2484.12', '2484.12');
INSERT INTO `cbasset` VALUES ('2307', '6.0622', '6.0622');
INSERT INTO `cbasset` VALUES ('2308', '0', '0');
INSERT INTO `cbasset` VALUES ('2309', '0', '0');
INSERT INTO `cbasset` VALUES ('2310', '21.0283', '21.0283');
INSERT INTO `cbasset` VALUES ('2311', '1', '1');
INSERT INTO `cbasset` VALUES ('2312', '0', '0');
INSERT INTO `cbasset` VALUES ('2313', '1', '1');
INSERT INTO `cbasset` VALUES ('2314', '94.2562', '94.2562');
INSERT INTO `cbasset` VALUES ('2315', '2.2562', '2.2562');
INSERT INTO `cbasset` VALUES ('2316', '2.4599', '2.4599');
INSERT INTO `cbasset` VALUES ('2317', '1.9015', '1.9015');
INSERT INTO `cbasset` VALUES ('2318', '0', '0');
INSERT INTO `cbasset` VALUES ('2319', '0', '0');
INSERT INTO `cbasset` VALUES ('2320', '0', '0');
INSERT INTO `cbasset` VALUES ('2321', '228.916', '228.916');
INSERT INTO `cbasset` VALUES ('2322', '258.157', '258.157');
INSERT INTO `cbasset` VALUES ('2323', '1026.49', '1026.49');
INSERT INTO `cbasset` VALUES ('2324', '0', '0');
INSERT INTO `cbasset` VALUES ('2325', '0', '0');
INSERT INTO `cbasset` VALUES ('2326', '0', '0');
INSERT INTO `cbasset` VALUES ('2327', '0', '0');
INSERT INTO `cbasset` VALUES ('2328', '95.3966', '95.3966');
INSERT INTO `cbasset` VALUES ('2329', '0', '0');
INSERT INTO `cbasset` VALUES ('2330', '1311.66', '1311.66');
INSERT INTO `cbasset` VALUES ('2331', '3316.05', '3316.05');
INSERT INTO `cbasset` VALUES ('2332', '2817.19', '2817.19');
INSERT INTO `cbasset` VALUES ('2333', '2294.21', '2294.21');
INSERT INTO `cbasset` VALUES ('2334', '149.696', '149.696');
INSERT INTO `cbasset` VALUES ('2335', '134.492', '134.492');
INSERT INTO `cbasset` VALUES ('2336', '0', '0');
INSERT INTO `cbasset` VALUES ('2337', '4.899', '4.899');
INSERT INTO `cbasset` VALUES ('2338', '4.899', '4.899');
INSERT INTO `cbasset` VALUES ('2339', '4.899', '4.899');
INSERT INTO `cbasset` VALUES ('2340', '1.9739', '1.9739');
INSERT INTO `cbasset` VALUES ('2341', '418.692', '418.692');
INSERT INTO `cbasset` VALUES ('2342', '0', '0');
INSERT INTO `cbasset` VALUES ('2343', '0', '0');
INSERT INTO `cbasset` VALUES ('2344', '0', '0');
INSERT INTO `cbasset` VALUES ('2345', '0', '0');
INSERT INTO `cbasset` VALUES ('2346', '0', '0');
INSERT INTO `cbasset` VALUES ('2347', '0', '0');
INSERT INTO `cbasset` VALUES ('2348', '0', '0');
INSERT INTO `cbasset` VALUES ('2349', '0', '0');
INSERT INTO `cbasset` VALUES ('2350', '0', '0');
INSERT INTO `cbasset` VALUES ('2351', '0', '0');
INSERT INTO `cbasset` VALUES ('2352', '0', '0');
INSERT INTO `cbasset` VALUES ('2353', '0', '0');
INSERT INTO `cbasset` VALUES ('2354', '0', '0');
INSERT INTO `cbasset` VALUES ('2355', '0', '0');
INSERT INTO `cbasset` VALUES ('2356', '0', '0');
INSERT INTO `cbasset` VALUES ('2357', '0', '0');
INSERT INTO `cbasset` VALUES ('2358', '0', '0');
INSERT INTO `cbasset` VALUES ('2359', '0', '0'), ('2360', '0', '0');
INSERT INTO `cbasset` VALUES ('2361', '0', '0');
INSERT INTO `cbasset` VALUES ('2362', '0', '0');
INSERT INTO `cbasset` VALUES ('2363', '0', '0');
INSERT INTO `cbasset` VALUES ('2364', '0', '0');
INSERT INTO `cbasset` VALUES ('2365', '0', '0');
INSERT INTO `cbasset` VALUES ('2366', '0', '0');
INSERT INTO `cbasset` VALUES ('2367', '0', '0');
INSERT INTO `cbasset` VALUES ('2368', '0', '0');
INSERT INTO `cbasset` VALUES ('2369', '0', '0');
INSERT INTO `cbasset` VALUES ('2370', '0', '0');
INSERT INTO `cbasset` VALUES ('2371', '0', '0');
INSERT INTO `cbasset` VALUES ('2372', '0', '0');
INSERT INTO `cbasset` VALUES ('2373', '0', '0');
INSERT INTO `cbasset` VALUES ('2374', '0', '0');
INSERT INTO `cbasset` VALUES ('2375', '0', '0');
INSERT INTO `cbasset` VALUES ('2376', '0', '0');
INSERT INTO `cbasset` VALUES ('2377', '382.681', '382.681');
INSERT INTO `cbasset` VALUES ('2378', '3431.33', '3431.33');
INSERT INTO `cbasset` VALUES ('2379', '0', '0');
INSERT INTO `cbasset` VALUES ('2380', '0', '0');
INSERT INTO `cbasset` VALUES ('2381', '0', '0');
INSERT INTO `cbasset` VALUES ('2382', '0', '0');
INSERT INTO `cbasset` VALUES ('2383', '2.6473', '2.6473');
INSERT INTO `cbasset` VALUES ('2384', '2.6473', '2.6473');
INSERT INTO `cbasset` VALUES ('2385', '2.6473', '2.6473');
INSERT INTO `cbasset` VALUES ('2386', '0', '0');
INSERT INTO `cbasset` VALUES ('2387', '0', '0');
INSERT INTO `cbasset` VALUES ('2388', '0', '0');
INSERT INTO `cbasset` VALUES ('2389', '0', '0');
INSERT INTO `cbasset` VALUES ('2390', '0', '0');
INSERT INTO `cbasset` VALUES ('2391', '0', '0');
INSERT INTO `cbasset` VALUES ('2392', '0', '0');
INSERT INTO `cbasset` VALUES ('2393', '0', '0');
INSERT INTO `cbasset` VALUES ('2394', '0', '0');
INSERT INTO `cbasset` VALUES ('2395', '0', '0');
INSERT INTO `cbasset` VALUES ('2396', '0', '0');
INSERT INTO `cbasset` VALUES ('2397', '0', '0');
INSERT INTO `cbasset` VALUES ('2398', '0', '0');
INSERT INTO `cbasset` VALUES ('2399', '0', '0');
INSERT INTO `cbasset` VALUES ('2400', '0', '0');
INSERT INTO `cbasset` VALUES ('2401', '0', '0');
INSERT INTO `cbasset` VALUES ('2402', '0', '0');
INSERT INTO `cbasset` VALUES ('2403', '0', '0');
INSERT INTO `cbasset` VALUES ('2404', '0', '0');
INSERT INTO `cbasset` VALUES ('2405', '0', '0');
INSERT INTO `cbasset` VALUES ('2406', '1425.88', '1425.88');
INSERT INTO `cbasset` VALUES ('2407', '0', '0');
INSERT INTO `cbasset` VALUES ('2408', '104.788', '104.788'), ('2409', '1', '1'), ('2410', '1', '1'), ('2411', '1', '1');
INSERT INTO `galaxy` VALUES ('1', 'DAWN');
INSERT INTO `guild_ranks` VALUES ('0', 'Recruit', '5', '0', '0', '1'), ('1', 'Apprentice', '5', '0', '0', '1'), ('2', 'Petty Officer', '5', '0', '0', '1'), ('3', 'Master Chief', '5', '0', '0', '1'), ('4', 'GM', '207', '5', '4', '1'), ('5', 'Deputy GM', '207', '6', '5', '1'), ('6', 'Head GM', '207', '7', '6', '1'), ('7', 'Content Dev', '255', '8', '7', '1'), ('8', 'Server Dev', '255', '9', '8', '1'), ('9', 'Admin', '4095', '10', '9', '1'), ('10', 'Recruit', '5', '0', '0', '1'), ('11', 'Apprentice', '5', '0', '0', '1'), ('12', 'Petty Officer', '5', '0', '0', '1'), ('13', 'Master Chief', '5', '0', '0', '1'), ('14', 'Ensign', '207', '5', '4', '1'), ('15', 'Lieutenant', '207', '6', '5', '1'), ('16', 'Commander', '207', '7', '6', '1'), ('17', 'Captain', '255', '8', '7', '1'), ('18', 'Commodore', '255', '9', '8', '1'), ('19', 'Admiral', '4095', '10', '9', '1'), ('20', 'Recruit', '5', '0', '0', '1'), ('21', 'Apprentice', '5', '0', '0', '1'), ('22', 'Petty Officer', '5', '0', '0', '1'), ('23', 'Master Chief', '5', '0', '0', '1'), ('24', 'Ensign', '207', '5', '4', '1'), ('25', 'Lieutenant', '207', '6', '5', '1'), ('26', 'Commander', '207', '7', '6', '1'), ('27', 'Captain', '255', '8', '7', '1'), ('28', 'Commodore', '255', '9', '8', '1'), ('29', 'Admiral', '4095', '10', '9', '1');
INSERT INTO `guilds` VALUES ('0', 'E & B Classic Staff', 'Welcome Everyone', '0', '0', '1');
INSERT INTO `hulls` VALUES ('0', '0', '0', '18', '2', '1', '23');
INSERT INTO `hulls` VALUES ('0', '0', '0', '18', '2', '1', '23');
INSERT INTO `hulls` VALUES ('0', '0', '1', '70', '2', '2', '25');
INSERT INTO `hulls` VALUES ('0', '0', '2', '280', '3', '2', '27');
INSERT INTO `hulls` VALUES ('0', '0', '3', '1100', '3', '3', '29');
INSERT INTO `hulls` VALUES ('0', '0', '4', '4500', '4', '3', '31');
INSERT INTO `hulls` VALUES ('0', '0', '5', '18000', '4', '4', '33');
INSERT INTO `hulls` VALUES ('0', '0', '6', '72000', '5', '4', '35');
INSERT INTO `hulls` VALUES ('0', '1', '0', '13', '1', '2', '28');
INSERT INTO `hulls` VALUES ('0', '1', '1', '50', '1', '3', '30');
INSERT INTO `hulls` VALUES ('0', '1', '2', '210', '2', '3', '32');
INSERT INTO `hulls` VALUES ('0', '1', '3', '850', '2', '4', '34');
INSERT INTO `hulls` VALUES ('0', '1', '4', '3400', '3', '4', '36');
INSERT INTO `hulls` VALUES ('0', '1', '5', '13400', '3', '5', '38');
INSERT INTO `hulls` VALUES ('0', '1', '6', '56000', '4', '5', '40');
INSERT INTO `hulls` VALUES ('0', '2', '0', '12', '1', '2', '25'), ('0', '2', '1', '48', '1', '3', '27'), ('0', '2', '2', '210', '2', '3', '29');
INSERT INTO `hulls` VALUES ('0', '2', '3', '850', '2', '4', '31');
INSERT INTO `hulls` VALUES ('0', '2', '4', '3300', '3', '4', '33');
INSERT INTO `hulls` VALUES ('0', '2', '5', '13200', '3', '5', '35');
INSERT INTO `hulls` VALUES ('0', '2', '6', '55500', '4', '5', '37');
INSERT INTO `hulls` VALUES ('1', '0', '0', '16', '2', '1', '18');
INSERT INTO `hulls` VALUES ('1', '0', '1', '65', '2', '2', '20');
INSERT INTO `hulls` VALUES ('1', '0', '2', '260', '3', '2', '22');
INSERT INTO `hulls` VALUES ('1', '0', '3', '1000', '3', '3', '24');
INSERT INTO `hulls` VALUES ('1', '0', '4', '4100', '4', '3', '26');
INSERT INTO `hulls` VALUES ('1', '0', '5', '16500', '4', '4', '28');
INSERT INTO `hulls` VALUES ('1', '0', '6', '66300', '5', '4', '30');
INSERT INTO `hulls` VALUES ('1', '1', '0', '12', '1', '2', '22');
INSERT INTO `hulls` VALUES ('1', '1', '1', '50', '1', '3', '24');
INSERT INTO `hulls` VALUES ('1', '1', '2', '200', '2', '3', '26');
INSERT INTO `hulls` VALUES ('1', '1', '3', '800', '2', '4', '28');
INSERT INTO `hulls` VALUES ('1', '1', '4', '3200', '3', '4', '30');
INSERT INTO `hulls` VALUES ('1', '1', '5', '13000', '3', '5', '32');
INSERT INTO `hulls` VALUES ('1', '1', '6', '55000', '4', '5', '34');
INSERT INTO `hulls` VALUES ('1', '2', '0', '11', '1', '2', '18');
INSERT INTO `hulls` VALUES ('1', '2', '1', '45', '1', '3', '20');
INSERT INTO `hulls` VALUES ('1', '2', '2', '170', '2', '3', '22');
INSERT INTO `hulls` VALUES ('1', '2', '3', '680', '2', '4', '24');
INSERT INTO `hulls` VALUES ('1', '2', '4', '2700', '2', '5', '26');
INSERT INTO `hulls` VALUES ('1', '2', '5', '11000', '3', '5', '28');
INSERT INTO `hulls` VALUES ('1', '2', '6', '42000', '3', '6', '30');
INSERT INTO `hulls` VALUES ('2', '0', '0', '20', '2', '1', '16');
INSERT INTO `hulls` VALUES ('2', '0', '1', '80', '3', '1', '18');
INSERT INTO `hulls` VALUES ('2', '0', '2', '320', '3', '2', '20');
INSERT INTO `hulls` VALUES ('2', '0', '3', '1300', '4', '2', '22');
INSERT INTO `hulls` VALUES ('2', '0', '4', '5100', '5', '2', '24');
INSERT INTO `hulls` VALUES ('2', '0', '5', '20500', '5', '3', '26');
INSERT INTO `hulls` VALUES ('2', '0', '6', '82000', '6', '3', '28');
INSERT INTO `hulls` VALUES ('2', '1', '0', '14', '2', '1', '20');
INSERT INTO `hulls` VALUES ('2', '1', '1', '65', '2', '2', '22');
INSERT INTO `hulls` VALUES ('2', '1', '2', '260', '3', '2', '24');
INSERT INTO `hulls` VALUES ('2', '1', '3', '1000', '3', '3', '26');
INSERT INTO `hulls` VALUES ('2', '1', '4', '4000', '4', '3', '28');
INSERT INTO `hulls` VALUES ('2', '1', '5', '17000', '5', '3', '30');
INSERT INTO `hulls` VALUES ('2', '1', '6', '66300', '5', '4', '32');
INSERT INTO `hulls` VALUES ('2', '2', '0', '13', '2', '1', '18');
INSERT INTO `hulls` VALUES ('2', '2', '1', '50', '2', '2', '20');
INSERT INTO `hulls` VALUES ('2', '2', '2', '210', '3', '2', '22');
INSERT INTO `hulls` VALUES ('2', '2', '3', '850', '3', '3', '24');
INSERT INTO `hulls` VALUES ('2', '2', '4', '3500', '3', '4', '26');
INSERT INTO `hulls` VALUES ('2', '2', '5', '13700', '4', '4', '28');
INSERT INTO `hulls` VALUES ('2', '2', '6', '55000', '4', '5', '30');
INSERT INTO `professions` VALUES ('0', 'WARRIOR');
INSERT INTO `professions` VALUES ('1', 'TRADER'), ('2', 'EXPLORER');
INSERT INTO `races` VALUES ('0', 'TERRAN');
INSERT INTO `races` VALUES ('1', 'JENQUAI'), ('2', 'PROGEN');
INSERT INTO `server_local_field_respawn_times` VALUES ('5545', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5546', '12'), ('5547', '15'), ('5548', '12'), ('5549', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5550', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5551', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5552', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5653', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('12018', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('12019', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('12020', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('12021', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('12022', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13537', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14119', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14120', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14709', '10'), ('14392', '12'), ('14396', '10'), ('14397', '10'), ('13419', '10'), ('13420', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13421', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13422', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4532', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4691', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4692', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6648', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6649', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6650', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6651', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6652', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13722', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14905', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14909', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4436', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4447', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4448', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('4701', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4751', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4752', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4753', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4754', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4775', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13514', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13602', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4404', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4429', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4430', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4490', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4499', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6655', '10'), ('6656', '12'), ('6657', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6663', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6665', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('6666', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6667', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14976', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14977', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14978', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14979', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14980', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14981', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15033', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15034', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4372', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4375', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4396', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4397', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4402', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4488', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('4705', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4698', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13515', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13842', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13844', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13845', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13847', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13849', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13854', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13855', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13884', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13939', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13940', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13941', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13943', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13944', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13946', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13957', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13958', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13961', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4199', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4200', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4201', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4202', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4203', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4204', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('4205', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4206', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4207', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4208', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4209', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4210', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4211', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4218', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4219', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4220', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4221', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13903', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13907', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13911', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13912', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13914', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13916', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13919', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13920', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13922', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13924', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13925', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13926', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13927', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13929', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13930', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13931', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13932', '15'), ('13934', '20'), ('13935', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13936', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13954', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('14403', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13445', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13446', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13448', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('3824', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4386', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4388', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4390', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4391', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4392', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4401', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4420', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4421', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4501', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4502', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4511', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4617', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4618', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4652', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4655', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4656', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4657', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4658', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4659', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4703', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5431', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5435', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5534', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4463', '10'), ('4464', '10'), ('4465', '12'), ('4466', '10'), ('4468', '10'), ('4469', '10'), ('4470', '10'), ('4471', '12'), ('4472', '12'), ('4473', '10'), ('4487', '12'), ('4503', '12'), ('4704', '30'), ('15201', '10'), ('4482', '10'), ('4483', '10'), ('4484', '10'), ('4485', '12'), ('4486', '12'), ('4509', '12'), ('4667', '12'), ('4706', '30'), ('14982', '12'), ('14983', '12'), ('14984', '12'), ('15575', '10'), ('15577', '10'), ('15578', '12'), ('15579', '20'), ('15582', '30'), ('15590', '30'), ('15597', '15'), ('15598', '15'), ('15599', '20'), ('15600', '20'), ('15601', '30'), ('15602', '30'), ('15603', '30'), ('15604', '20'), ('15605', '20'), ('15606', '20'), ('15607', '30'), ('15608', '30'), ('15609', '30'), ('15610', '12'), ('15611', '12'), ('15612', '15'), ('15613', '20'), ('15820', '15'), ('13645', '12'), ('13646', '12'), ('13647', '10'), ('13648', '10'), ('13649', '10'), ('13650', '10'), ('13662', '10'), ('13664', '12'), ('5304', '10'), ('5305', '10'), ('5306', '10'), ('5307', '10'), ('5308', '10'), ('5309', '10'), ('5310', '10'), ('12013', '10'), ('12014', '10'), ('12015', '10'), ('12016', '10'), ('14916', '10'), ('5531', '20'), ('5532', '20'), ('14598', '30'), ('14616', '15'), ('14617', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15083', '20'), ('15296', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('6851', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('6852', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('6853', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('6855', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('6856', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('6857', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('6858', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('6859', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('6860', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('6861', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('6862', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13505', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13506', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('14314', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14315', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5276', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5277', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5278', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5279', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5280', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5281', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5282', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5283', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5285', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5286', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5287', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5288', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5289', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5290', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('5291', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13360', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13361', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13362', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13363', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14340', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('14467', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('12304', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('12305', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13455', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13456', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13457', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13458', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13459', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13460', '12'), ('13461', '12'), ('13462', '12'), ('13507', '15'), ('13508', '15'), ('13510', '20'), ('13511', '15'), ('7047', '10'), ('7048', '10'), ('7049', '10'), ('7050', '10'), ('7052', '10'), ('7053', '12'), ('7054', '12'), ('7055', '12'), ('14350', '20'), ('14351', '30'), ('14353', '15'), ('6730', '10'), ('6731', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6732', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6733', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6740', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6741', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6742', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6743', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14433', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5365', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5366', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5367', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5368', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5369', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5370', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5371', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('5372', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5373', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5374', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('5375', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5376', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5377', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5378', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13396', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13399', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13403', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13406', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13410', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5059', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('5060', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5061', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5076', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5077', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5078', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5079', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5080', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4916', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4917', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4918', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4919', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4920', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4921', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4922', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4923', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4924', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4925', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4926', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4927', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4928', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4929', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4930', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4931', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4932', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13571', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13573', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13575', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13576', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13578', '10'), ('13579', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13581', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4996', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4997', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('5000', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5001', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5005', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('5006', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('5008', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5009', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('5012', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13556', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13558', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13559', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13560', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13561', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13562', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13563', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13564', '20'), ('14366', '15'), ('14370', '20'), ('14379', '20'), ('14380', '20'), ('14383', '15'), ('14384', '15'), ('14385', '30'), ('4899', '12'), ('4900', '12'), ('4907', '20'), ('4908', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4912', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4913', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13566', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13568', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13582', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13583', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13584', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4971', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4972', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4973', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('4974', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4975', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('4976', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('4977', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('13544', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13545', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13547', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13548', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('13549', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13551', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('13553', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13554', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('14355', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('14356', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4748', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('4959', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15297', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4778', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4782', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('4783', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4784', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4785', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4786', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4823', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4825', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4826', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4827', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4950', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('4983', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('14619', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14621', '10'), ('14622', '10'), ('14623', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14624', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14627', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13822', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('13824', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('13825', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('5330', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6004', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6006', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6038', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15084', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15085', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15546', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15131', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15132', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15133', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15134', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15135', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15136', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15138', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15139', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15140', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15141', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15142', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15143', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15144', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15145', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15146', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15147', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15148', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15170', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15172', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15173', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15174', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15180', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15182', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15183', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15184', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('15185', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15407', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('15408', '20');
INSERT INTO `server_local_field_respawn_times` VALUES ('15409', '15');
INSERT INTO `server_local_field_respawn_times` VALUES ('15411', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('15433', '12');
INSERT INTO `server_local_field_respawn_times` VALUES ('14661', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('14662', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('15058', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('15059', '30');
INSERT INTO `server_local_field_respawn_times` VALUES ('6804', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6805', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6806', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6807', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6808', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6809', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6810', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6811', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6812', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6813', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6814', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6815', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6816', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6817', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6818', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6819', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6820', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6821', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('6822', '10'), ('6823', '10'), ('6824', '10'), ('6825', '10'), ('6826', '10'), ('6827', '10'), ('6828', '10'), ('6829', '10'), ('6830', '12'), ('6831', '12'), ('6833', '15'), ('6834', '15'), ('12337', '12'), ('12339', '12'), ('12340', '12'), ('12341', '15'), ('12342', '15'), ('12343', '12'), ('15025', '15'), ('15026', '15'), ('15027', '12'), ('15029', '12'), ('15030', '20'), ('5105', '10'), ('5122', '10'), ('5136', '12'), ('5139', '15'), ('5199', '20'), ('5200', '20'), ('5201', '20'), ('5204', '30'), ('5205', '30'), ('5206', '30'), ('5207', '30'), ('5209', '30'), ('5211', '20'), ('14487', '30'), ('14489', '30'), ('14490', '30'), ('14491', '30'), ('16017', '30'), ('12106', '10');
INSERT INTO `server_local_field_respawn_times` VALUES ('14985', '12'), ('15300', '30'), ('15456', '20'), ('15737', '30'), ('15738', '20'), ('15686', '15'), ('15688', '20'), ('15689', '30'), ('15690', '12'), ('15691', '15'), ('15692', '20'), ('15693', '30'), ('15831', '12'), ('15682', '12'), ('15683', '12'), ('15834', '10'), ('15835', '12'), ('15365', '20'), ('15370', '20'), ('15371', '30'), ('15375', '30'), ('15376', '20'), ('15540', '30'), ('15541', '30'), ('15542', '30'), ('15543', '30'), ('14110', '10'), ('14111', '12'), ('14113', '10'), ('14114', '12'), ('4884', '20'), ('4885', '10'), ('4886', '20'), ('4887', '20'), ('4888', '10'), ('6867', '10'), ('6868', '10'), ('6869', '30'), ('6870', '10'), ('6872', '30'), ('6873', '10'), ('6874', '10'), ('6912', '20'), ('6913', '20'), ('6914', '30'), ('6917', '10'), ('6918', '10'), ('15847', '30'), ('16027', '10'), ('16028', '10'), ('16029', '10'), ('16030', '20'), ('16031', '20'), ('16032', '30'), ('16033', '30'), ('4837', '12'), ('4838', '12'), ('4839', '10'), ('4840', '10'), ('4841', '10'), ('4842', '10'), ('6875', '10'), ('6876', '10'), ('6877', '10'), ('6878', '10'), ('6879', '10'), ('6881', '12'), ('6882', '10'), ('6920', '10'), ('6921', '10'), ('6922', '10'), ('11925', '10'), ('13893', '30'), ('13894', '30'), ('13895', '30'), ('13896', '30'), ('13897', '30'), ('13898', '30'), ('13899', '30'), ('13900', '30'), ('13902', '20'), ('14024', '30'), ('14693', '10'), ('14695', '12'), ('14696', '12'), ('14697', '10'), ('14698', '15'), ('14821', '12'), ('14823', '30'), ('16056', '10'), ('16057', '10'), ('16058', '10'), ('16059', '10'), ('16060', '10'), ('16061', '10'), ('16062', '10'), ('16063', '10'), ('16064', '10'), ('16065', '10'), ('16066', '10'), ('16067', '10'), ('16072', '12'), ('16073', '12'), ('16074', '15'), ('16076', '30'), ('16077', '30'), ('16078', '12'), ('16079', '12'), ('16080', '15'), ('16081', '20'), ('16082', '30'), ('16083', '30'), ('16084', '12'), ('16086', '15'), ('16087', '20'), ('16088', '30'), ('16089', '30'), ('16091', '12'), ('16092', '15'), ('16093', '20'), ('16094', '30'), ('16095', '30'), ('14929', '30'), ('14930', '30'), ('14931', '30'), ('14932', '30'), ('14933', '30'), ('14934', '30'), ('14935', '30'), ('14936', '30'), ('15858', '30'), ('15860', '30'), ('16005', '30'), ('16006', '30'), ('15880', '30'), ('15881', '30'), ('15915', '30'), ('15918', '30'), ('15919', '10'), ('15920', '30'), ('15927', '30'), ('15928', '30'), ('15929', '30'), ('15930', '30'), ('15931', '30'), ('15932', '30'), ('15933', '30'), ('15934', '30'), ('15935', '30'), ('15892', '30'), ('15893', '30'), ('15951', '30'), ('15952', '30'), ('15953', '30'), ('15954', '30'), ('15955', '30'), ('15956', '30'), ('15957', '30'), ('5112', '12'), ('5113', '12'), ('5114', '12'), ('5115', '12'), ('5125', '12'), ('5126', '12'), ('5127', '12'), ('5128', '12'), ('5190', '12'), ('4853', '10'), ('4854', '10'), ('4855', '10'), ('4870', '20'), ('4871', '20'), ('4872', '12'), ('4874', '12'), ('15993', '10'), ('3012', '10'), ('4829', '10'), ('4830', '10'), ('4831', '10'), ('4832', '10'), ('4833', '10'), ('4834', '10'), ('4835', '10'), ('4836', '10'), ('4860', '12'), ('4861', '12'), ('15233', '12'), ('14007', '12'), ('14008', '12'), ('14009', '12'), ('14011', '12'), ('14012', '12'), ('12286', '30'), ('12287', '30'), ('12288', '20'), ('13002', '20'), ('13752', '30'), ('13753', '20'), ('13755', '20'), ('13756', '30'), ('13757', '20'), ('13759', '30'), ('13760', '20'), ('13762', '30'), ('13764', '30'), ('12181', '10'), ('12185', '10'), ('12189', '10'), ('12194', '12'), ('12199', '12'), ('12213', '12'), ('12216', '12'), ('12219', '10'), ('13836', '12'), ('13837', '12'), ('13838', '10'), ('13839', '10'), ('13841', '12'), ('3036', '15'), ('3037', '15'), ('3038', '30'), ('3039', '15'), ('3040', '15'), ('3042', '12'), ('3043', '15'), ('3044', '20'), ('3045', '20'), ('12210', '12'), ('12211', '15'), ('12227', '15'), ('12228', '15'), ('13597', '12'), ('13599', '20'), ('13600', '20'), ('13601', '30'), ('14511', '20'), ('14513', '30'), ('14514', '30'), ('14515', '30'), ('6883', '10'), ('12065', '12'), ('12066', '12'), ('12067', '12'), ('12068', '10'), ('12069', '10'), ('12070', '30'), ('12076', '20'), ('12077', '20'), ('12078', '15'), ('12133', '12'), ('12134', '12'), ('12135', '12'), ('12136', '12'), ('13610', '12'), ('13621', '30'), ('12079', '12'), ('12080', '12'), ('12081', '30'), ('12083', '20'), ('12084', '20'), ('12085', '20'), ('12086', '20'), ('12087', '30'), ('13669', '20'), ('13675', '12'), ('13676', '20'), ('13677', '30'), ('13680', '30'), ('13681', '30'), ('12090', '20'), ('12091', '30'), ('12092', '30'), ('12094', '30'), ('12095', '12'), ('12096', '15'), ('12097', '20'), ('12098', '15'), ('12125', '30'), ('12126', '20'), ('12127', '30'), ('12129', '30'), ('12130', '30'), ('12997', '20'), ('12998', '20'), ('12999', '30'), ('13000', '30'), ('13684', '20'), ('13685', '20'), ('13686', '30'), ('13687', '30'), ('13689', '30'), ('13691', '20'), ('13692', '20'), ('13700', '30'), ('13727', '20'), ('13729', '30'), ('13730', '30'), ('13732', '12'), ('15032', '10'), ('4403', '15'), ('13848', '20'), ('4500', '12'), ('14915', '20'), ('5284', '12'), ('13454', '12'), ('13463', '12'), ('13509', '15'), ('14352', '12'), ('5379', '20'), ('4914', '12'), ('12365', '10'), ('13823', '15'), ('15181', '12'), ('15410', '15'), ('15434', '12'), ('6832', '15'), ('15028', '15'), ('15687', '12'), ('6871', '20'), ('6880', '12'), ('16075', '20'), ('16085', '12'), ('4873', '12'), ('14010', '12'), ('14512', '20'), ('12064', '10'), ('12075', '15'), ('12082', '20'), ('13678', '15');
INSERT INTO `skill_list` VALUES ('1', 'Beam Weapon');
INSERT INTO `skill_list` VALUES ('2', 'Befriend'), ('3', 'Biorepression'), ('4', 'Build Components'), ('5', 'Build Devices');
INSERT INTO `skill_list` VALUES ('6', 'Build Engines');
INSERT INTO `skill_list` VALUES ('7', 'Build Items');
INSERT INTO `skill_list` VALUES ('8', 'Build Reactors');
INSERT INTO `skill_list` VALUES ('9', 'Build Shields');
INSERT INTO `skill_list` VALUES ('10', 'Build Weapons');
INSERT INTO `skill_list` VALUES ('11', 'Call Forward');
INSERT INTO `skill_list` VALUES ('12', 'Cloak');
INSERT INTO `skill_list` VALUES ('13', 'Combat Trance');
INSERT INTO `skill_list` VALUES ('14', 'Compulsory Contemplation');
INSERT INTO `skill_list` VALUES ('15', 'Create Wormhole');
INSERT INTO `skill_list` VALUES ('16', 'Critical Targeting');
INSERT INTO `skill_list` VALUES ('17', 'Damage Control');
INSERT INTO `skill_list` VALUES ('18', 'Device Tech');
INSERT INTO `skill_list` VALUES ('19', 'Energy Leech');
INSERT INTO `skill_list` VALUES ('20', 'Engine Tech');
INSERT INTO `skill_list` VALUES ('21', 'Engineering');
INSERT INTO `skill_list` VALUES ('22', 'Enrage');
INSERT INTO `skill_list` VALUES ('23', 'Environment Shield');
INSERT INTO `skill_list` VALUES ('24', 'Fold Space');
INSERT INTO `skill_list` VALUES ('25', 'Gravity Link');
INSERT INTO `skill_list` VALUES ('26', 'Hacking');
INSERT INTO `skill_list` VALUES ('27', 'Hull Patch');
INSERT INTO `skill_list` VALUES ('28', 'Item Tech');
INSERT INTO `skill_list` VALUES ('29', 'Jenquai Culture');
INSERT INTO `skill_list` VALUES ('30', 'Jenquai Lore');
INSERT INTO `skill_list` VALUES ('31', 'Jumpstart');
INSERT INTO `skill_list` VALUES ('32', 'Maelstrom Resonance');
INSERT INTO `skill_list` VALUES ('33', 'Menace');
INSERT INTO `skill_list` VALUES ('34', 'Missile Weapon');
INSERT INTO `skill_list` VALUES ('35', 'Navigate');
INSERT INTO `skill_list` VALUES ('36', 'Negotiate');
INSERT INTO `skill_list` VALUES ('37', 'Power Down');
INSERT INTO `skill_list` VALUES ('38', 'Progen Culture');
INSERT INTO `skill_list` VALUES ('39', 'Progen Lore');
INSERT INTO `skill_list` VALUES ('40', 'Projectile Weapon');
INSERT INTO `skill_list` VALUES ('41', 'Prospect'), ('42', 'Psionic Shield'), ('43', 'Quantum Flux'), ('44', 'Rally'), ('45', 'Reactor Tech'), ('46', 'Recharge Shields'), ('47', 'Repair Equipment'), ('48', 'Repulsor Field'), ('49', 'Scan'), ('50', 'Self Destruct'), ('51', 'Shield Charging'), ('52', 'Shield Inversion'), ('53', 'Shield Leech'), ('54', 'Shield Sap'), ('55', 'Shield Tech'), ('56', 'Summon'), ('57', 'Terran Culture'), ('58', 'Nullfactor Field'), ('59', 'Afterburn');
INSERT INTO `ssl_deny_list` VALUES ('');
INSERT INTO `status_levels` VALUES ('-2', 'BANNED');
INSERT INTO `status_levels` VALUES ('-1', 'DISABLED'), ('0', 'USER'), ('10', 'DONER'), ('20', 'HELPER'), ('30', 'BETA');
INSERT INTO `status_levels` VALUES ('40', 'STAFF');
INSERT INTO `status_levels` VALUES ('50', 'GM'), ('60', 'DGM');
INSERT INTO `status_levels` VALUES ('70', 'HGM');
INSERT INTO `status_levels` VALUES ('80', 'DEV');
INSERT INTO `status_levels` VALUES ('90', 'SDEV');
INSERT INTO `status_levels` VALUES ('100', 'ADMIN');
