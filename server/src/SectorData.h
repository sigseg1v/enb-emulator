// SectorData.h
//
//	Container for the Sector Data for a single Sector, Planet, or Station
//
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

#ifndef _SECTOR_DATA_H_INCLUDED_
#define _SECTOR_DATA_H_INCLUDED_

#define MAX_OBJS_PER_SECTOR 16384  //if we have 1 bit per each object it's easy to work out if they are in/out of range.
#define MAX_MOBS_PER_SECTOR 2048

#define PLAYER_TAG (1<<30)
#define MANU_TAG (1<<31)
#define IS_PLAYER(x) (x & PLAYER_TAG && ((x & 0xFFFFFFF) < 0xFFFFF))

#define ASTEROID_FIELD ((char(0xFF))
 

#include <net7/PacketStructures.h>
#include <map>
#include <vector>

class ItemBase;
class ObjectManager;

enum PositionalUpdateType
{
    POSITION_SIMPLE,
    POSITION_ADVANCED,
    POSITION_PLANET,
    POSITION_CONSTANT,
    POSITION_COMPONENT
};

struct PositionInformation;
struct PositionInformation
{
    PositionalUpdateType type;
	long	GameID;
	float	Position[3];
	float	Orientation[4];
    // Simple Positional Update
	float	Velocity[3];
    // Advanced Positional Update
	short	Bitmask;				// flags for condional fields
	unsigned long MovementID;
	float	CurrentSpeed;
	float	SetSpeed;
	float	Acceleration;
	float	RotY;
	float	DesiredY;
	float	RotZ;
	float	DesiredZ;
	float	ImpartedVelocity[3];
	float	ImpartedSpin;
	float	ImpartedRoll;
	float	ImpartedPitch;
    // Planet Positional Update
    long    OrbitID;
    float   OrbitDist;
    float   OrbitAngle;
    float   OrbitRate;
    float   RotateAngle;
    float   RotateRate;
    float   TiltAngle;
    // Component Positional Update
    float   ImpartedDecay;
    float   TractorSpeed;
    long    TractorID;
    long    TractorEffectID;
	unsigned long UpdatePeriod;
} ATTRIB_PACKED;

struct OreNode
{
	long item_id;
	float frequency; //this may not be used for a bit
};

typedef std::vector<OreNode*> SectorOreList;

struct SectorData;
struct SectorData
{
    long sector_id;                     // Sector ID (i.e. 1060 = Earth Sector)
    long system_id;                     // System ID
    char *system_name;               // System Name (i.e. "Sol")
    char *name;                      // Sector Name (i.e. "Earth")
    char greetings[128];
    char repair_msg[128];
    char dock_msg[128];
    char launch_msg[128];
	long challenge_rating;	// sector challenge rating from 0 (off) to 9 (~level 50 mobs)

    ServerParameters server_params;

    long  num_objects;                  // Number of game objects
	long  sector_type;

    ObjectManager *obj_manager;

	float		 m_xmin;
	float		 m_xmax;
	float		 m_ymin;
	float		 m_ymax;

	//ore types for sector
	SectorOreList OreList;
	long		 ore_list_size;

    //SectorData *next;       // pointer to next Sector in the linked list
} ATTRIB_PACKED;

struct SystemData
{
	char *system_name;
} ATTRIB_PACKED;

typedef std::map<int, SectorData*> SectorDataMap;
typedef std::map<int, SystemData*> SystemDataMap;

#endif // _SECTOR_DATA_H_INCLUDED_
