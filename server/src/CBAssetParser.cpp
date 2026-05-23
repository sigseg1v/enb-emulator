//CBAssetParser.cpp
//XML version
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

#include "CBAssetParser.h"
#include "xmlParser/xmlParser.h"

#include <sstream>
using namespace std;

CBAssetParser::CBAssetParser()
{
	numBases = 0;
	radii    = (0);
}

CBAssetParser::~CBAssetParser()
{
	if(radii != (0))
	{
		delete [] radii;
		radii = (0);
	}
}

bool CBAssetParser::ParseRadii()
{
	if(radii != (0))
	{
		delete [] radii;
		radii = (0);
	}
	
	LogMessage("Parsing \'cbasset.xml\'...\n");

	char orig_path[MAX_PATH];
    GetCurrentDirectory(sizeof(orig_path), orig_path);
    SetCurrentDirectory(SERVER_DATABASE_PATH);

    XMLNode xMainNode = XMLNode::openFileHelper("cbasset.xml");

    SetCurrentDirectory(orig_path);

	numBases = xMainNode.getChildNode("Bases").nChildNode("Base");
	
	radii = new float[numBases];
	for(unsigned int currentBase = 0; currentBase < numBases; currentBase++)
	{
		stringstream s;		//We should avoid using strings & streams but providing they are not used in the main app it's ok.
							//essentially these templates are undebuggable, but if the data is ok at the end they should be alright.
							//note for future - NEVER use strings within the main loop where memory can be allocated/deallocated.
		s << currentBase;
		radii[currentBase] = (float)atof( xMainNode.getChildNode("Bases").getChildNodeWithAttribute("Base", "ID", s.str().c_str()).getChildNode("Radius").getAttribute("Value") );
		//float adjust = float)atof( xMainNode.getChildNode("Bases").getChildNodeWithAttribute("Base", "ID", s.str().c_str()).getChildNode("Radius").getAttribute("Value") );
	}
    
    return true;
}





float CBAssetParser::GetRadius(unsigned int baseID)
{
    if(radii == NULL)
    {
        //Error, Radius data hasn't been parsed yet
        LogMessage("Error - Cannot read Radius values, data has not been parsed yet!!!\n");
        return -1.0f;
    }
    if(baseID >= numBases)
    {
        //Error, out of bounds of the 'radii' array
        LogMessage("Error - Specified BASE id does not exist!!!\n");
        return -1.0f;
    }
    
    return radii[baseID];
}



unsigned int CBAssetParser::GetNumBases()
{
	return numBases;
}
