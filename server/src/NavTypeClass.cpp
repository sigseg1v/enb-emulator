// NavTypeClass.cpp
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
#include "NavTypeClass.h"
#include "PlayerClass.h"
#include "ObjectManager.h"
#include "PacketMethods.h"

StaticMap::StaticMap(long object_id) : Object (object_id)
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
	m_StatPacketLength = 0;
	m_IsLocalStargate = false;
}

StaticMap::~StaticMap()
{
    // TODO: destroy everything
}

long StaticMap::GetBroadcastID()
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

		case 373:
			broadcastID = 20316;
			break;

		case 1216:
			broadcastID = 20126;
			break;

		case 253:
			broadcastID = 20120;
			break;

		case 495:
			broadcastID = 20121;
			break;

	};

    return broadcastID;
}

void StaticMap::InRangeTrigger(Player *p, float range)
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

void StaticMap::OutOfRangeTrigger(Player *p, float range)
{

}

void StaticMap::SetEIndex(long *index_array)
{
	long *entry = (long*) (index_array + (m_DatabaseUID/(sizeof(long)*8)));

	//now set the specific bit
	*entry |= (1 << m_DatabaseUID%32);
}

void StaticMap::UnSetEIndex(long *index_array)
{
	long *entry = (long*) (index_array + (m_DatabaseUID/(sizeof(long)*8)));

	//now unset the specific bit
	*entry &= (0xFFFFFFFF ^ (1 << m_DatabaseUID%32));
}

bool StaticMap::GetEIndex(long *index_array)
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

void StaticMap::BlipGate(long player_id)
{
    Player *p = (0);
    u32 * sector_list = GetSectorList();
	
	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p)
			{
				p->CloseStargate(GameID());
				p->OpenStargate(GameID());
			}
		}
	}
}

//Send object to all players who can see this object. Currently used only for resource hijacks
void StaticMap::SendToVisibilityList(bool include_player)
{
	Player * p = (0);
    u32 * sector_list = GetSectorList();
	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p) 
			{
				switch (ObjectType())
				{
				case OT_PLANET:
					p->SendPlanetPositionalUpdate(GameID(), PosInfo());
					break;

				case OT_GWELL:
				case OT_RADIATION:
					// Do now show these
					break;

				default:
					p->SendAdvancedPositionalUpdate(GameID(), PosInfo());
					break;
				}
			}
		}
	}
}

void StaticMap::SendObjectEffects(Player *player)
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

void StaticMap::FormStaticPacket()
{
	unsigned char *msg = m_StatPacket;
	memset(msg,0,256);
    int index = 0;

    AddData(msg, GameID(), index);
	AddData(msg, (u8)CreateType(), index);  //4
	AddData(msg, (short)BaseAsset(), index); //5
	AddData(msg, Scale(), index); //7
	AddData(msg, HSV0(), index);  //11
	AddData(msg, HSV1(), index);  //15
	AddData(msg, HSV2(), index);  //19

	AddData(msg, (u8)RELATIONSHIP_FRIENDLY, index); //23
	AddData(msg, (u8)PosType(), index); //24
	AddData(msg, PosX(), index);        //25 
	AddData(msg, PosY(), index);        //29
	AddData(msg, PosZ(), index);        //33
	float *orientation = Orientation();
	AddData(msg, orientation[0], index);//37
	AddData(msg, orientation[1], index);//41
	AddData(msg, orientation[2], index);//45
	AddData(msg, orientation[3], index);//49

	AddData(msg, Signature(), index);   //53
	u8 sig_flags = (u8)NavType();
	if (IsNav() || ObjectType() == OT_FIELD) sig_flags |= IS_NAV;
	if (HasNavInfo() && NavType() > 0 && AppearsInRadar())
    {
		sig_flags |= HAS_NAV;
	}
	if (IsHuge()) sig_flags |= IS_HUGE;
	AddData(msg, sig_flags, index);     //57
	AddDataLS(msg, Name(), index);

	m_StaticPacketFormed = true;
	m_StatPacketLength = index;
}

void StaticMap::SendObject(Player *player)
{
	if (!m_StaticPacketFormed) FormStaticPacket();

	//update packet faction standing
	if (GetFactionID() > 0)
	{
		long reaction = RELATIONSHIP_FRIENDLY;
		float player_standing = player->GetFactionStanding(this);

		if (player_standing < 2000.0f) reaction = RELATIONSHIP_SHUN;
		if (player_standing <= -2000.0f) reaction = RELATIONSHIP_ATTACK;
		m_StatPacket[23] = (u8)reaction;
	}

	m_StatPacket[57] &= 0x7F; //remove 'has visited' flag

	if (HasNavInfo() && NavType() > 0 && AppearsInRadar())
    {
		m_StatPacket[57] &= 0x7F; //remove 'has visited' flag
		if (GetEIndex(player->ExploredNavList())) 
		{
			m_StatPacket[57] |= HAS_VISITED; //sig_flags
		}
	}

	player->SendObjectFull(m_StatPacket, m_StatPacketLength, OT_NAV);

    //send Object Effects (if any)
    SendObjectEffects(player);
   
    OnCreate(player);
}

void StaticMap::SendPosition(Player *player)
{
    switch (PosType())
    {
    case POSITION_SIMPLE :
        player->SendSimplePositionalUpdate(
                            GameID(),
                            PosInfo());
        break;

    case POSITION_CONSTANT :
        player->SendConstantPositionalUpdate(
                            GameID(),
                            PosX(),
                            PosY(),
                            PosZ(),
                            Orientation());
        break;
    }
}

void StaticMap::SendAuxDataPacket(Player *player)
{
    switch (CreateType())
    {
        case 3 :        // Planet, moon, sun
        case 11 :       // Gate
        case 12 :       // Station
            player->SendSimpleAuxName(this);
            break;
        case 37 :       // Decoration/Nav
            player->SendAuxNameSignature(this);
            break;
		case 4:
            player->SendResourceName(GameID(), Name()); //what's 4?
			break;
		case 40:
		case 41:
			// do not send anything for these
			break;
		default:
			break;
    }
}

void StaticMap::SendNavigation(Player *player)
{
    //Current Nav info workings (09/01/08). Three types of nav as follows:
    //1. Clickable, on minimap: AppearsInRadar = 1, NavType = 1 or 2 [1 is warp-path nav, 2 is destination only].
    //2. Clickable, not on minimap: AppearsInRadar = 0, NavType = 1 or 2
    //3. Non Clickable, not on minimap: AppearsInRadar = 0, NavType = 0

    if (HasNavInfo() && NavType() > 0 && AppearsInRadar())
    {
        char explored = GetEIndex(player->ExploredNavList()) ? 1 : 0;      

        player->SendNavigation(GameID(), Signature() + 5000.0f, 
            explored, 
            NavType(), IsHuge());
    }
}

//On creation of StaticMap graphic object for Player. 
void StaticMap::OnCreate(Player *player)
{

}

#define DOCKING_RANGE 5000.0f //activation range from gate/station radius
//#define STATION_RANGE 3000.0f //activation range from station radius

//Called every time this StaticMap is targeted.
void StaticMap::OnTargeted(Player *player)
{
    player->BlankVerbs();

    float docking_range = DOCKING_RANGE;

    if (player->ShipIndex()->GetIsIncapacitated())
    {
        docking_range = -1.0f;
    }

    switch (ObjectType())
    {
    case OT_STATION:
		player->AddVerb(VERBID_DOCK, docking_range);
        player->AddVerb(VERBID_REGISTER, DOCKING_RANGE);
        break;

    case OT_STARGATE:
        if (Destination() > 0)
        {
			//check for faction.
			long faction_id = GetFactionID();
			bool class_specific = GetClassSpecific();

			// See if we can take this mission
			if (GetSecurityLvl() > player->AdminLevel())
			{
				break;
			}

			if (player->GetOverrideFaction()) //allow devs and testers to override faction/class restrictions
			{
				player->AddVerb(VERBID_GATE, docking_range);
			}
			else if (class_specific)
			{
				if (faction_id == player->GetFactionID() || faction_id < 1)
				{
					player->AddVerb(VERBID_GATE, docking_range);
				}
			}
			else if (faction_id > 0)
			{
				if (player->GetFactionStanding(this) >= -1999.0f) //allow gate travel if faction is greater than zero
				{
					player->AddVerb(VERBID_GATE, docking_range);
				}
			}
			else
			{
				player->AddVerb(VERBID_GATE, docking_range);
			}
        }
		break;
       
    default:
        break;
    };

	player->MissionVerbDisplay(this);
}