// Timenode.h
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Net-7 Emulator Project, an Earth & Beyond emulator by Net7 Entertainment is licensed under a Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
**
** Based on a work at http://www.earthandbeyond.com
**
** Permissions beyond the scope of this license may be available at http://www.dreamersofdawn.org/docs/More_Information.htm
**
** The license can be modified at our discretion within the bounds of Creative Commons at any time.
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
*/

#ifndef _TIMENODE_H_INCLUDED_
#define _TIMENODE_H_INCLUDED_

#include "PlayerManager.h"

class Object;

typedef enum
{
	B_WARP_BROADCAST, 
    B_DESTROY_RESOURCE, B_WARP_RESET, B_WARP_TERMINATE, B_MINE_RESOURCE, B_COLLECT_RESOURCE,
    B_MOB_DAMAGE, B_RECHARGE_REACTOR, B_FORCE_LOGOUT, 
    B_BUFF_TIMEOUT, B_CAMERA_CONTROL, B_MANUFACTURE_ACTION,
    B_SHIP_DAMAGE, B_ITEM_INSTALL, B_RECHARGE_SHIELD, B_ITEM_COOLDOWN, B_TEST_MESSAGE, B_ABILITY_REMOVE, B_REMOVE_BUFF,
	B_SHOOT_AMMO, B_PLAYER_BUFFS, B_SERVER_SHUTDOWN, B_DESTROY_HUSK, B_MOB_BUFFS, B_REMOVE_EFFECT, B_REMOVE_OBJECT_EFFECT,
	B_LOCAL_GATE, B_ARRIVE_AT_LOCAL_GATE, B_DESTROY_OBJECT
} broadcast_function;

#define SECTOR_SERVER_CONNECTIONLESS_NODE -1
#define NODE_NO_LONGER_NEEDED -2

struct TimeNode
{
	//function type
	broadcast_function func;
	
	unsigned long event_time;
	//params
	Object *obj;
	long i1;
	long i2;
	long i3;
	long i4;
	float a;
	char *ch;
	bool IsPnode;
	
    long player_id;
    short EventIndex;
} ATTRIB_PACKED;

#endif