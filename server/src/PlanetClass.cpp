// PlanetClass.cpp
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

#include "Net7.h"
#include "ObjectClass.h"
#include "PlanetClass.h"
#include "PlayerClass.h"
#include "ObjectManager.h"

Planet::Planet(long object_id) : Object (object_id)
{
    m_Type = OT_NAV;
    m_CreateInfo.Type = 37;
    m_IsHuge = 0;
	m_AppearsInRadar = false;
    m_NavType = 0;
	m_Destination = 0;
    m_SignalType = 0;
    m_BroadcastID = 0;
    m_HasNavInfo = false;
}

Planet::~Planet()
{
    // TODO: destroy everything
}

long Planet::GetBroadcastID()
{
	long broadcastID = 0;
	if (m_BroadcastID != 0) return m_BroadcastID;
	switch (BaseAsset())
	{
		//Terran, very large
		case 380:
			broadcastID = 20307;
			break;

		case 47:
		case 137:
		case 457:
			broadcastID = 20305;
			break;

		case 312:
		case 123:
		case 1525:
		case 413:
			broadcastID = 20315;
			break;

		case 375:
			broadcastID = 20308;
			break;

		case 496:
			broadcastID = 20312;
			break;

		case 1040:
			broadcastID = 20310;
			break;

		case 1220:
			broadcastID = 20317;
			break;

		case 404: //Disable Net-7 Sol for now
			broadcastID = 0;
			//broadcastID = 20320;
			break;

		case 1035:
		case 166:
			broadcastID = 20090;
			break;

		case 372:
			broadcastID = 20306;
			break;

		case 1997:
			broadcastID = 20314;
			break;
	};

    return broadcastID;
}

void Planet::InRangeTrigger(Player *p, float range)
{
	//this is activated when we enter into a certain range of a nav or deco
	//at the moment, just use it to check arrival at a nav

	long lrange = (long)range;

	switch (lrange)
	{
	case TRIGGER_RANGE_1:
		p->CheckMissionRangeTrigger(this, TRIGGER_RANGE_1);
		break;
	case TRIGGER_RANGE_2:
		p->CheckMissionRangeTrigger(this, TRIGGER_RANGE_2);
		break;
	case TRIGGER_RANGE_3:
		//player is confirmed stationary
		p->CheckMissionArrivedAt(this);
		break;
	}
}

void Planet::OutOfRangeTrigger(Player *p, float range)
{

}

void Planet::SetEIndex(long *index_array)
{
	long *entry = (long*) (index_array + (m_DatabaseUID/(sizeof(long)*8)));

	//now set the specific bit
	*entry |= (1 << m_DatabaseUID%32);
}

bool Planet::GetEIndex(long *index_array)
{
	long *entry = (long*) (index_array + (m_DatabaseUID/(sizeof(long)*8)));

	//now get the specific bit
	if (*entry & (1 << m_DatabaseUID%32))
	{
		return true;
	}
	else
	{
		return false;
	}
}

//Send object to all players who can see this object. Currently used only for resource hijacks
void Planet::SendToVisibilityList(bool include_player)
{
	Player * p = (0);
    u32 * sector_list = GetSectorList();
	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p) 
			{
				p->SendPlanetPositionalUpdate(GameID(), PosInfo());
				break;
			}
		}
	}
}

void Planet::SendObjectEffects(Player *player)
{
    /*
    EffectList::iterator itrEList;
    ObjectEffect *effect;

    //this shouldn't do anything yet
    if (m_Effects)
    { 
        for (itrEList = m_Effects->begin(); itrEList < m_Effects->end(); ++itrEList) 
        {
            effect = (*itrEList);
            effect->TimeStamp = GetNet7TickCount();
            effect->GameID = GameID();
            effect->EffectID = m_ObjectMgr->GetAvailableSectorID();
            // Ignore the effect if the DescID is zero
            if (effect->EffectDescID > 0)
            {
                LogMessage("Sending effect for %s [%d]\n",Name(), effect->EffectDescID);
                connection->SendObjectEffect(effect);
            }
        }
    }*/

    if (ObjectType() == OT_STARGATE) //initial state of stargates - closed
    {
        if (player->FromSector() == Destination())
        {
            //if we came from this stargate it should be open to start off with            
            player->SendActivateNextRenderState(GameID(), 1);
        }
        else
        {
            player->SendActivateRenderState(GameID(), 1); //gate graphics activate
            player->SendActivateNextRenderState(GameID(), 3);
        }
    }
    else
    {
        // Send ActivateRenderState packet(s)
        long rs = RenderState();
        while (rs)
        {
            if (rs & 1)
            {
                player->SendActivateRenderState(GameID(), 1);
            }
            rs >>= 1;
        }
    }
}

void Planet::SendPosition(Player *player)
{
	player->SendPlanetPositionalUpdate(
		GameID(),
		PosInfo());
}

void Planet::SendAuxDataPacket(Player *player)
{
	player->SendSimpleAuxName(this);
}

void Planet::SendNavigation(Player *player)
{
    //Current Nav info workings (09/01/08). Three types of nav as follows:
    //1. Clickable, on minimap: AppearsInRadar = 1, NavType = 1 or 2 [1 is warp-path nav, 2 is destination only].
    //2. Clickable, not on minimap: AppearsInRadar = 0, NavType = 1 or 2
    //3. Non Clickable, not on minimap: AppearsInRadar = 0, NavType = 0

    if (HasNavInfo() && NavType() > 0 && AppearsInRadar())
    {
        char explored = 1;

        player->SendNavigation(GameID(), Signature() + 5000.0f, 
            explored, 
            NavType(), IsHuge());
    }
}

//On creation of Planet graphic object for Player. 
void Planet::OnCreate(Player *player)
{

}

#define DOCKING_RANGE 5000.0f //activation range from gate/station radius
//#define STATION_RANGE 3000.0f //activation range from station radius

//Called every time this Planet is targeted.
void Planet::OnTargeted(Player *player)
{
	if (Destination() > 0)
	{
		player->BlankVerbs();

		float docking_range = DOCKING_RANGE;

		if (player->ShipIndex()->GetIsIncapacitated())
		{
			docking_range = -1.0f;
		}

		player->AddVerb(VERBID_LAND, docking_range);
	}
	player->MissionVerbDisplay(this);
}