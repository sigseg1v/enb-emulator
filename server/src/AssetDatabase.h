// AssetDatabase.h
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

#ifndef _ASSETDATABASE_H_INCLUDED_
#define _ASSETDATABASE_H_INCLUDED_

#include <map>
#include <vector>

// forward references
struct AssetData;
class sql_connection_c;

typedef std::map<unsigned long, AssetData*> AssetList;

class AssetContent
{
// Constructor/Destructor
public:
    AssetContent();
	virtual ~AssetContent();

// Public Methods
public:
    bool        LoadAssetContent();
    AssetData * GetAssetData(long basset);
    void        ReloadAssetData();
    long        GetAssetCount();

// Private Member Attributes
private:
    AssetList    m_Asset;
    long         m_highest_id;

};

struct AssetData
{
    char   *m_Name;
    char   *m_CatName;
	char   *m_SubCatName;
};


#endif // _ASSETDATABASE_H_INCLUDED_

