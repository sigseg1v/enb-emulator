// BuffDatabaseSQL.h
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

#ifndef _BUFF_DATABASE_SQL_H_INCLUDED_
#define _BUFF_DATABASE_SQL_H_INCLUDED_

#include <map>


struct BuffData
{
	int		EffectID;
	int		EffectLength;
};

typedef std::map<char *, BuffData> BuffList;

class BuffContent
{
// Constructor/Destructor
public:
    BuffContent();
	virtual ~BuffContent();

// Public Methods
public:
    bool        LoadBuffContent();
	long		GetBuffEffectTime(char *buff);
	long		GetBuffEffect(char *buff);


// Private Member Attributes
private:
    BuffList				m_BuffData;
	bool					m_updating;

};


#endif // _BUFF_DATABASE_SQL_H_INCLUDED_

