// NavTypeClass.h
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

#ifndef _PLANETTYPE_CLASS_H_INCLUDED_
#define _PLANETTYPE_CLASS_H_INCLUDED_

#include "ObjectClass.h"

class Planet : public Object
{
public:
    Planet(long object_id);
    virtual ~Planet();

    void SetDestination(long destination_sector)    { m_Destination = destination_sector; };
    void SetAppearsInRadar()                        { m_AppearsInRadar = true; };
    void SetHuge()                                  { m_IsHuge = 1; };
    void SetNavType(long nav_type)                  { m_NavType = nav_type; };

    long NavType()                                  { return (m_NavType); };
    char IsHuge()                                   { return (m_IsHuge); };
    long Destination()                              { return (m_Destination); };
    long GetBroadcastID();

    char IsNav()                                    { return (m_NavInfo && m_NavType ? 1 : 0); };
    bool AppearsInRadar()                           { return (m_AppearsInRadar); };
    void SetEIndex(long *index_array);
    bool GetEIndex(long *index_array);
    void SendToVisibilityList(bool include_player);
	void OutOfRangeTrigger(Player *p, float range);
	void InRangeTrigger(Player *p, float range);

//create methods
    void SendObjectEffects(Player *player);
    void SendPosition(Player *player);
    void SendAuxDataPacket(Player *player);
    void SendNavigation(Player *player);
    void OnCreate(Player *player);
    void OnTargeted(Player *player);

    void SendObjectEffectsTCP(Player *player);
    void SendPositionTCP(Player *player);
    void SendAuxDataPacketTCP(Player *player);
    void SendNavigationTCP(Player *player);


private:
	char	m_IsHuge;
	bool	m_AppearsInRadar;
    long    m_NavType;
    bool    m_HasNavInfo;

	long    m_Destination;
    long    m_SignalType;
};

#endif // _PLANETTYPE_CLASS_H_INCLUDED_