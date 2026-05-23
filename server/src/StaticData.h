//This contains static data for players
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

//Data is always: TW, TT, TE, JW, JT, JE, PW, PT, PE
//Or Terran, Jenquai, Progen

#ifndef BASE_STATS_H
#define BASE_STATS_H

//                               TW    TT    TE    JW    JT    JE    PW    PT    PE
static int MaxWeaponSlots[] =   {5,    4,    4,    5,    4,    3,    6,    5,    4};
static int MaxDeviceSlots[] =   {4,    5,    5,    4,    5,    6,    3,    4,    5};

static int BaseCargo[] =        {23,   28,   23,   18,   28,   18,   18,   28,   18};
static int BaseScanRange[] =    {3500, 3500, 5000, 3500, 3500, 5000, 3500, 3500, 5000};
static int BaseVisableRange[] = {3500, 2500, 2500, 500,  500,  500,  2500, 2500, 2500};
/* "Master Data Tables.xls"
static int MaxWeaponSlots[] =   {5,    4,    3,    5,    4,    3,    6,    5,    4};
static int MaxDeviceSlots[] =   {4,    5,    6,    4,    5,    6,    3,    4,    5};

static int BaseCargo[] =        {23,   28,   23,   18,   28,   18,   18,   28,   18}; // +2 per hull upgrade
static int BaseScanRange[] =    {3500, 3500, 5000, 3500, 3500, 5000, 3500, 3500, 5000};
static int BaseVisableRange[] = {2500, 2500, 2500, 500,  500,  500,  2500, 2500, 2500};*/
static int BaseSpeed[] =        {155,  177,  206,  137,  155,  177,  124,  137,  155}; // calculated from thrust and mass
static int BaseMass[] =			{40,   35,   30,   45,   40,   35,   50,   45,   40}; // +5 per hull upgrade
static int BaseManeuver[]=		{70,   60,   50,   70,   60,   50,   80,   70,   60}; // -5 per hull upgrade

#ifdef USE_MYSQL_ITEMS
   static char * BaseShield[] =    {"Repulsion Field Generator", "Reflection Field Generator", "Absorption Field Generator"};
   static char * BaseReactor[] =   {"Terran Chemical Reactor", "Jenquai Compression Reactor", "Progen Radium Reactor"};
   static char * BaseEngine[] =    {"InfinitiCorp XR-24-36-G", "Compression Thrusters", "Chemical Thrusters"};
   static char * BaseWeapon[] =    {"Gradient Laser", "Pulse Laser", "Diamond Laser"};
   static int	 nBaseEngine[]=    {2507, 2505, 2506};
#else
   static char * BaseShield[] =    {"Repulsion Field Generator", "Reflection Field Generator", "Absorption Field Generator"};
   static char * BaseReactor[] =   {"Terran Chemical Reactor", "Jenquai Compression Reactor", "Projen Chemical Reactor"};
   static char * BaseEngine[] =    {"InfinitiCorp XR-24-36-G", "Compression Thrusters", "Chemical Thrusters"};
   static char * BaseWeapon[] =    {"Gradient Laser", "Pulse Laser", "Diamond Laser"};
#endif

static long BaseHullAsset[9] =  {1600, 1603, 1606, 1609, 1612, 1615, 1618, 1621, 1624};
static long BaseProfAsset[9] =  {1630, 1627, 1633, 1639, 1636, 1642, 1648, 1645, 1651};
static long BaseWingAsset[9] =  {1654, 1657, 1660, 1663, 1666, 1669, 1672, 1675, 1678};

static long BaseEngineAsset[3] = {1681, 1684, 1687};

static long StartSector[] =
{
    10151,      // Terran Warrior   = Enforcer  (Luna, Luna Station)
    10201,      // Terran Trader    = Tradesman (High Earth, Loki Station)
    10251,      // Terran Explorer  = Scout     ()
    10551,      // Jenquai Warrior  = Defender  (Europa, Ashanti Maru)
    10401,      // Jenquai Trader   = Seeker    ()
    10521,      // Jenquai Explorer = Explorer  (Io, Nishino Research Facility)
    10361,      // Progen Warrior   = Warrior   (Mars Alpha, Arx Forgus)
    10371,      // Progen Trader    = Privateer (Mars Gama)
    10301,      // Progen Explorer  = Sentinel  (Mars Beta, Arx Prima)
};

// How many hull points awarded at each upgrade level
/*static int HullTable[] = 
{
    18, 70, 280, 1100, 4500, 18000, 72000, // TW
    13, 50, 210, 850,  3400, 13400, 56000, // TT
    12, 48, 210, 850,  3300, 13200, 55500, // TE guess
    16, 65, 260, 1000, 4100, 16500, 66300, // JW
    12, 50, 200, 800,  3200, 13000, 55000, // JT guess
    11, 45, 170, 680,  2700, 11000, 42000, // JE
    20, 80, 320, 1300, 5100, 20500, 82000, // PW
    14, 65, 260, 1000, 4000, 17000, 66300, // PT guess
    13, 50, 210, 850,  3500, 13700, 55000  // PE
};*/
// "Master Data Tables.xls"
static int HullTable[] = 
{
    18, 70, 280, 1100, 4500, 18000, 72000, // TW
    13, 55, 210, 850,  3400, 13400, 56000, // TT
    12, 50, 190, 750,  3000, 12000, 48000, // TE
    16, 65, 260, 1000, 4100, 16500, 65500, // JW
    12, 50, 190, 770,  3100, 12500, 49000, // JT
    11, 45, 170, 680,  2700, 11000, 44000, // JE
    20, 80, 320, 1300, 5100, 20500, 82000, // PW
    15, 60, 240, 960,  3900, 15500, 61000, // PT
    13, 50, 210, 850,  3400, 13700, 55000  // PE
};

// How many weapon slots awarded at each upgrade level
static int WeaponTable[] = 
{
    2,  0,  1,  0,  1,  0,  1,  // TW
    1,  0,  1,  0,  1,  0,  1,  // TT
    1,  0,  1,  0,  1,  0,  1,  // TE
    2,  0,  1,  0,  1,  0,  1,  // JW
    1,  0,  1,  0,  1,  0,  1,  // JT
    1,  0,  1,  0,  0,  1,  0,  // JE
    2,  1,  0,  1,  1,  0,  1,  // PW
    2,  0,  1,  0,  1,  1,  0,  // PT
    2,  0,  1,  0,  0,  1,  0   // PE
};
/* "Master Data Tables.xls"
static int WeaponTable[] = 
{
    2,  0,  1,  0,  1,  0,  1,  // TW
    1,  0,  1,  0,  1,  0,  1,  // TT
    1,  0,  1,  0,  0,  1,  0,  // TE
    2,  0,  1,  0,  1,  0,  1,  // JW
    1,  0,  1,  0,  1,  0,  1,  // JT
    1,  0,  1,  0,  0,  1,  0,  // JE
    2,  1,  0,  1,  1,  0,  1,  // PW
    2,  0,  1,  0,  1,  0,  1,  // PT
    2,  0,  1,  0,  0,  1,  0   // PE
};*/

// How many device slots awarded at each upgrade level
static int DeviceTable[] =
{
    1,  1,  0,  1,  0,  1,  0,  // TW
    2,  1,  0,  1,  0,  1,  0,  // TT
    2,  1,  0,  1,  0,  1,  0,  // TE
    1,  1,  0,  1,  0,  1,  0,  // JW
    2,  1,  0,  1,  0,  1,  0,  // JT
    2,  1,  0,  1,  1,  0,  1,  // JE
    1,  0,  1,  0,  0,  1,  0,  // PW
    1,  1,  0,  1,  0,  0,  1,  // PT
    1,  1,  0,  1,  1,  0,  1   // PE
};
/* "Master Data Tables.xls"
static int DeviceTable[] =
{
    1,  1,  0,  1,  0,  1,  0,  // TW
    2,  1,  0,  1,  0,  1,  0,  // TT
    2,  1,  0,  1,  1,  0,  1,  // TE
    1,  1,  0,  1,  0,  1,  0,  // JW
    2,  1,  0,  1,  0,  1,  0,  // JT
    2,  1,  0,  1,  1,  0,  1,  // JE
    1,  0,  1,  0,  0,  1,  0,  // PW
    1,  1,  0,  1,  0,  1,  0,  // PT
    1,  1,  0,  1,  1,  0,  1   // PE
};*/

static int faction_list[] =
{
	6,  // TW
	11, // TT
	10, // TE
	18, // JW
	17, // JT
	16, // JE
	3,  // PW
	5,  // PT
	15  // PE
};

static int __vrix_faction = 23;

static char * ChannelNames[] =    {"Broadcast", "General Chat", "Explorers", "Private Channel", "Out of Context", 
								   "Warriors", "Group", "Market", "Tradesman", "Guild", "New Players", "Sentinels",
								   "Local", "Terran", "Defenders", "Jenquai", "Enforcers", "Progen", "Scouts", "Seekers",
								   "Privateers", "Errors", "Staff", "GM", "Devs" , NULL};

#define PUMPKIN_CHUNKER_ID 1791
#define EYEBALL_POPPER_ID 1792

#endif // BASE_STATS_H
