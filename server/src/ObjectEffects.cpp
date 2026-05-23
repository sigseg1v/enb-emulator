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

#include "ObjectEffects.h"
#include "PlayerClass.h"

Effects::Effects()
{
    m_Object = 0;
}

Effects::~Effects()
{
	m_EffectList.clear();
}

void Effects::Init(Object * Owner)
{
	m_Mutex.Lock();
    m_Object = Owner;
	m_EffectList.clear();
	m_Mutex.Unlock();
}

int Effects::AddEffect(ObjectToObjectEffect *obj_effect)
{
	long EffectID = -1;
	m_Mutex.Lock();
	if  (m_Object->GetSectorManager())
	{
		EffectID = m_Object->GetSectorManager()->GetNextEffectID(); // crash: refreshing combat trance on a not fully initialised player (sectorid = -8billion)

		obj_effect->EffectID = EffectID;
		memcpy(&m_EffectList[EffectID].obj2obj_effect, obj_effect, sizeof(ObjectToObjectEffect));		// Copy effect in
		m_EffectList[EffectID].type = EFFECT_OBJ_2_OBJ_EFFECT;
		// Send to all player in your range
		if(m_Object->GetStartStatus())
		{
			m_Object->SendObjectToObjectEffectRL(obj_effect);
		}
	}
	m_Mutex.Unlock();
	return EffectID;
}

int Effects::AddEffect(ObjectEffect *obj_effect)
{
	long EffectID = -1;
	m_Mutex.Lock();
	if  (m_Object->GetSectorManager())
	{
		EffectID = m_Object->GetSectorManager()->GetNextEffectID(); // crash: refreshing combat trance on a not fully initialised player (sectorid = -8billion)

		obj_effect->EffectID = EffectID;
		memcpy(&m_EffectList[EffectID].obj_effect, obj_effect, sizeof(ObjectEffect));		// Copy effect in
		m_EffectList[EffectID].type = EFFECT_OBJ_EFFECT;
		// Send to all player in your range
		if(m_Object->GetStartStatus())
		{
			m_Object->SendObjectEffectRL(obj_effect);
		}
	}
	m_Mutex.Unlock();
	return EffectID;
}

bool Effects::RemoveEffect(int EffectID)
{
	m_Mutex.Lock();
	if (m_EffectList.count(EffectID) != 0)
	{
		m_EffectList.erase(m_EffectList.find(EffectID));

		// Send Remove Effect to Range List
		m_Object->RemoveEffectRL(EffectID);

		m_Mutex.Unlock();
		return true;
	}
	else
	{
		LogMessage("Warning: Removing an Effect ID that does not exsist!\n");
		m_Mutex.Unlock();
		return false;
	}
}


bool Effects::SendRemoveEffects(Player *p)
{
	m_Mutex.Lock();
	for(mapEffect::const_iterator Effect = m_EffectList.begin(); Effect != m_EffectList.end(); ++Effect)
	{
		p->SendRemoveEffect(Effect->first);
	}
	m_Mutex.Unlock();
	return true;
}


bool Effects::SendEffects(Player *p)
{
	m_Mutex.Lock();
	for(mapEffect::const_iterator Effect = m_EffectList.begin(); Effect != m_EffectList.end(); ++Effect)
	{
		if(Effect->second.type == EFFECT_OBJ_EFFECT)
		{
			p->SendObjectEffect((ObjectEffect *) &Effect->second.obj_effect);
		}
		else if(Effect->second.type == EFFECT_OBJ_2_OBJ_EFFECT)
		{
			p->SendObjectToObjectEffect((ObjectToObjectEffect *) &Effect->second.obj2obj_effect);
		}
	}
	m_Mutex.Unlock();
	return true;
}