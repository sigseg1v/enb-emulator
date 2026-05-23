// SkillsDatabaseSQL.h
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

#ifndef _SKILLSDATABASE_SQL_H_INCLUDED_
#define _SKILLSDATABASE_SQL_H_INCLUDED_

#include <map>


// forward references

struct SkillConversion;

typedef std::map<unsigned long, SkillConversion*> SkillConversionList;

class SkillsContent
{
// Constructor/Destructor
public:
    SkillsContent();
	virtual ~SkillsContent();

// Public Methods
public:
    bool        LoadSkillsContent();
    long		GetSkillLevel(long skill_id);
    char	*	GetSkillDescription(long skill_id);
    long        GetBaseSkillID(long skill_id);

// Private Methods
private:

// Private Member Attributes
private:
    SkillConversionList		m_SkillConvList;
    long					m_highest_id;
	bool					m_updating;

};



//This is incomplete, just want to get some data read in for now
struct SkillConversion
{
    char   *m_Description;
	u8		m_Level;
	u8		m_BaseSkillID;
};


#endif // _SKILLSDATABASE_SQL_H_INCLUDED_

