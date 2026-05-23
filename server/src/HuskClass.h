// HuskClass.h
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

#ifndef _HUSK_CLASS_H_INCLUDED_
#define _HUSK_CLASS_H_INCLUDED_

#include "ObjectClass.h"
#include "Timenode.h"

class ItemBase;

class Husk : public Object
{
public:
    Husk(long object_id);
    Husk();
    virtual ~Husk();

    unsigned long RespawnTick()                      { return m_Respawn_tick; };
    void SetLevel(short level);
    void SetType(short type);
    void SetBasset(short basset);
    void SetRespawnTick(unsigned long respawn)       { m_Respawn_tick = respawn; };
    float ResourceRemains()                 { return (m_Resource_remains); };

    void		 AddItem(ItemBase *item, long stack);
    float		 RemoveItem(long slot_id, long stack);
	void		 PopulateHusk(long mob_type);
	void		 DropTrashLoot(int mob_level, int mob_type = -1);
	int			 GetRandomOre(int level);
	int			 GetRandomCrystal(int level);
	int			 GetRandomComponent(int level);
	int			 GetRandomTech(int level, int mob_type = -1);
	int			 GetRandomBio(int level);
	int			 GetMaxPricePerTechLevel(int tech_level);
    ContentSlot *GetContents(long index)    { return (&m_Resource_contents[index]); };
    ItemBase    *GetItem(long index);
    short        GetStack(long index);
    void         SendObjectReset();
    void         SendObjectDrain(long slot);
    void         SendToVisibilityList(bool include_player);
	void		 SetDestroyTimer(long time_delay, long respawn_delay = -1);
	void	     DestroyHusk();
	void		 RemoveDestroyTimer();
	long		 GetCreditLoot()			{ return m_HuskCredits; }
	void		 SetCreditLoot(long creds)	{ m_HuskCredits = creds; }
	void		 SetLootTimer(long time_delay);
	u32			 GetLootTime()				{ return m_LootTime; }
	void		 ResetObjectContents();

    void         SetHuskName(char *name);

//create methods 
    void         SendPosition(Player *player);
    void         SendAuxDataPacket(Player *player);
    void         OnCreate(Player *player);
    void         OnTargeted(Player *player);

private:
	ContentSlot		m_Resource_contents[MAX_ITEMS_PER_RESOURCE]; //giving us 8 slots by default is cheaper than handling a list vector for each resource
    char            m_Content_count;
    char            m_Resource_type;
	unsigned long	m_Respawn_tick;
    short	        m_Resource_start_value;       //this value serves as an indicator to work out how much is left of the asteroid.
    short           m_Resource_value;
    float           m_Resource_remains;
	long			m_HuskCredits;
	TimeNode		m_ObjectTimeSlot;
	u32				m_LootTime;
};

#endif // _HUSK_CLASS_H_INCLUDED_
