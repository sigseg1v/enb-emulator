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

class SectorContentParser //: protected XmlParser
{
// Constructor/Destructor
public:
    SectorContentParser();
	virtual ~SectorContentParser();

// Public Methods
public:
    bool LoadSectorContent();
    bool LoadSectorContent(long sector_id);
    SectorData * GetSectorData(long sector_id);
	SectorData * GetSectorData(char *sector_name);
	char * _GetSectorName(long sector_id);  //do not use these directly
	char * _GetSystemName(long sector_id);
	long GetNextSectorID (long sector_id);
	AsteroidSubcatVec* GetAsteroidContentSelection(long asteroid_type);
	void AddMOBTypes(Object *obj, long resource_id, sql_connection_c *connection); //public for now, we need this function to re-populate fields with changed asteroid counts

// Private Methods
private:
    bool ParseSectorContent(long sector_id);
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


};


#endif // _SECTOR_CONTENT_PARSER_H_INCLUDED_
