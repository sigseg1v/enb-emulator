// SectorContentParser.h
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

#ifndef _SECTOR_CONTENT_PARSER_H_INCLUDED_
#define _SECTOR_CONTENT_PARSER_H_INCLUDED_

//#include "XmlParser.h"
#include "SectorData.h"
#include <map>
#include <vector>

// forward references
struct SectorData;
class Object;
class sql_connection_c;
class sql_row_c;

typedef std::vector<long> AsteroidSubcatVec;
typedef std::map<int, AsteroidSubcatVec*> AsteroidContentList;

// Phase AI: type-aware residency. Cross-sector object access provably occurs --
// MissionManager::PickMissionSector walks REMOTE sectors' ObjectManagers through
// the gate graph (FindGate -> Destination -> GetObjectManager -> FindPlanet) to
// generate missions, and gate-sealing reads g_SectorObjects[gate->Destination()]
// for a gate in another sector. So the navigation skeleton (stargates, planets,
// stations, deco/navs, gravity wells, radiation) MUST stay resident in every
// sector galaxy-wide. Only the heavy populating objects -- MOBs and asteroid
// fields/resources -- are deferred to first sector entry.
//   SKELETON_ONLY : boot pass, every sector -- load the nav skeleton, no MOBs/fields
//   DEFERRED_ONLY : first entry into a sector -- load its MOBs/fields, skeleton already resident, no wipe
//   ALL           : GM reload (/rsectors, /rsectorall) -- wipe and reload everything
enum SectorLoadMode { SECTOR_LOAD_SKELETON_ONLY, SECTOR_LOAD_DEFERRED_ONLY, SECTOR_LOAD_ALL };

class SectorContentParser //: protected XmlParser
{
// Constructor/Destructor
public:
    SectorContentParser();
	virtual ~SectorContentParser();

// Public Methods
public:
    bool LoadSectorContent();                                   // GM reload-all: ALL types, every sector
    bool LoadSectorContent(long sector_id,
                           SectorLoadMode mode = SECTOR_LOAD_DEFERRED_ONLY);
    // Phase AI: boot pass. Load the per-sector metadata (sectors table: name,
    // system, boundaries, params) AND the navigation skeleton (gates/planets/
    // stations/navs/gwell/radiation) for ALL sectors, each with its obj_manager,
    // but SKIP the heavy MOBs + asteroid fields. Cross-sector reads (mission gen,
    // gate-seal, GetSectorData) stay correct because the skeleton is resident; the
    // deferred MOBs/fields load on demand per sector via LoadSectorContent(id)
    // when a player is first handed off there (ServerManager::EnsureSectorStarted).
    bool LoadSectorMetadata();
    SectorData * GetSectorData(long sector_id);
	SectorData * GetSectorData(char *sector_name);
	char * _GetSectorName(long sector_id);  //do not use these directly
	char * _GetSystemName(long sector_id);
	long GetNextSectorID (long sector_id);
	AsteroidSubcatVec* GetAsteroidContentSelection(long asteroid_type);
	void AddMOBTypes(Object *obj, long resource_id, sql_connection_c *connection); //public for now, we need this function to re-populate fields with changed asteroid counts

// Private Methods
private:
    bool ParseSectorContent(long sector_id, SectorLoadMode mode = SECTOR_LOAD_ALL);
	void UpdateBoundaries(SectorData *sector, float *position);
    void AddResourceTypes(Object *obj, long resource_id, sql_connection_c *connection);
	void LoadSystems(sql_connection_c *connection);
	void AddSystemInfo(SectorData *sector);
	void LoadSectorOreAvailability(SectorData *sector, sql_connection_c *connection);
	void LoadAsteroidContentSelection(sql_connection_c *connection);
	void AddFieldOreIDs(Object *obj, long object_id, sql_connection_c *connection);
	void ProcessDefaultObjectStats(Object *current_object, sql_row_c &ObjectData);

// Private Member Attributes
private:
    SectorDataMap	m_SectorList;
	SystemDataMap	m_SystemList;
	AsteroidContentList m_AsteroidContentList;
	bool			m_Success;
	// Systems + asteroid-content selection are GLOBAL (not per-sector) and only
	// need loading once. With on-demand per-sector loading (Phase AI), every cold
	// sector start would otherwise re-run those two full-table reads; this gates
	// them to the first parse pass (the boot metadata pass).
	bool			m_GlobalsLoaded = false;


};


#endif // _SECTOR_CONTENT_PARSER_H_INCLUDED_
