// ResourceClass.h
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

#ifndef _RESOURCE_CLASS_H_INCLUDED_
#define _RESOURCE_CLASS_H_INCLUDED_

#include "ObjectClass.h"
#include "Timenode.h"

class ItemBase;

class Resource : public Object
{
public:
    Resource(long object_id);
    Resource();
    virtual ~Resource();

    unsigned long RespawnTick()                      { return m_Respawn_tick; };
    void SetLevel(short level);
    void SetType(short type);
    void SetLevelFromHSV(float h1);
    void SetBasset(short basset);
    void SetTypeAndName(short type);
    void SetContainerField(Object *obj)     { m_Field_Container = obj; };
    void SetRespawnTick(unsigned long respawn)       { m_Respawn_tick = respawn; };
    float ResourceRemains()                 { return (m_Resource_remains); };

    void		 AddItem(ItemBase *item, long stack);
    float		 RemoveItem(long slot_id, long stack);
    void		 ResetResource();
	void		 PopulateHusk(long mob_type);
    ContentSlot *GetContents(long index)    { return (&m_Resource_contents[index]); };
    ItemBase    *GetItem(long index);
    short        GetStack(long index);
    Object      *ContainerField()           { return (m_Field_Container); };
    void         SendObjectReset();
    void         SendObjectDrain(long slot);
    void         SendToVisibilityList(bool include_player);
	void		 SetDestroyTimer(long time_delay, long respawn_delay = -1);
	void		 RemoveDestroyTimer();
	bool		 HasValidContent()			{ return m_Content_count == 0 ? false : true; };
	void		 ResetObjectContents();
	void		 AddToClearList(Player *player);

//create methods 
    void         SendPosition(Player *player);
    void         SendAuxDataPacket(Player *player);
    void         OnCreate(Player *player);
    void         OnTargeted(Player *player);
	void		 SendObject(Player *player);

private:
    void         DestroyResource();
	void		 FormStaticPacket();

private:
	ContentSlot		m_Resource_contents[MAX_ITEMS_PER_RESOURCE]; //giving us 8 slots by default is cheaper than handling a list vector for each resource
    char            m_Content_count;
    char            m_Resource_type;
	unsigned long	m_Respawn_tick;
    short	        m_Resource_start_value;       //this value serves as an indicator to work out how much is left of the asteroid.
    short           m_Resource_value;
    Object        * m_Field_Container;
    float           m_Resource_remains;
	TimeNode		m_ObjectTimeSlot;
	u8				m_StatPacket[256];
	long			m_StatPacketLength;
};

typedef enum 
{
	REACTIVE_ASTEROID = 1,		// 204
	ROCKY_ASTEROID = 2,			// 201, 202, 203, 208
	HYDROCARBON = 3,			// 206, 207, 211
	CRYSTALLINE_ASTEROID = 4,	// 200, 205, 209
	CARBONACEOUS_ASTEROID = 5,	//
	DIRTY_ICE_ASTEROID = 6,		//
	GAS_CLOUD = 7,				// 210
	NICKEL_IRON = 8,			// 201, 208
	ORGANIC_HULK = 9,			// 
	INORGANIC_HULK = 10			//
} ResourceType;

#define RESOURCE_TYPE_SIZE 10

#endif // _RESOURCE_CLASS_H_INCLUDED_
