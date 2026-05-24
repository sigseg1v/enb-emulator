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
#include "CMobBuffs.h"
#include "PlayerClass.h"
#include "MOBClass.h"

CMobBuffs::CMobBuffs()
{
    m_Owner = 0;
	m_BuffID = 0;
}

CMobBuffs::~CMobBuffs()
{
	m_BuffList.clear();
	m_expireTimes.clear();
}

void CMobBuffs::Init(CMob * Owner)
{
	m_Mutex.Lock();
    m_Owner = Owner;
	m_BuffList.clear();
	m_expireTimes.clear();
	m_Mutex.Unlock();
}

Player *CMobBuffs::GetPlayer()
{
	return dynamic_cast<Player *>(m_Owner);
}

MOB *CMobBuffs::GetMOB()
{
	return dynamic_cast<MOB *>(m_Owner);
}

bool CMobBuffs::FindBuff(char * BuffName, bool non_permanent_only)
{
	bool Found = false;
	m_Mutex.Lock();
	for(mapBuffs::iterator iter = m_BuffList.begin();
		iter != m_BuffList.end();
		iter++)
	{
		if(strcmp(iter->second.BuffData.BuffType,BuffName) == 0)
		{
			if (non_permanent_only && iter->second.BuffData.IsPermanent)
				continue;
			// If so break
			Found = true;
			break;
		}
	}
	m_Mutex.Unlock();
	return Found;
}

int CMobBuffs::AddBuff(struct Buff *AddBuff, int colour, char *StatSource)
{
	m_Mutex.Lock();
	int retval = _AddBuff(AddBuff, colour, StatSource);
	m_Mutex.Unlock();
	return retval;
}

int CMobBuffs::_AddBuff(struct Buff *AddBuff, int colour, char *StatSource)
{
	Player *player = GetPlayer();
	bool FreeFound = false;
	int FreeSpot = -1;

	// check the client display for free spots
	if (player)
	{
		for(FreeSpot = 0;FreeSpot<16;FreeSpot++)
		{
			// See if it expired
			if (!player->ShipIndex()->Buffs.Buff[FreeSpot].GetIsPermanent() && player->ShipIndex()->Buffs.Buff[FreeSpot].GetBuffRemovalTime() < GetNet7TickCount())
			{
				// If so break
				FreeFound = true;
				break;
			}
		}
	}
	// copy in the passed in structure
	MapBuffs buffData;
	memcpy(&buffData.BuffData,AddBuff,sizeof(Buff));
	m_BuffList[m_BuffID] = buffData;
	//memcpy(&m_BuffList[m_BuffID].BuffData, AddBuff, sizeof(Buff));

	// do effects
	for(int i = 0; i < MAX_EFFECTS_PER_BUFF; i++)
	{
		if (AddBuff->EffectID[i] != -1)
		{
			// Effect Information
			ObjectEffect obj_effect;

			// Send Effect
			obj_effect.Bitmask = 3;
			obj_effect.GameID = m_Owner->GameID();
			obj_effect.TimeStamp = GetNet7TickCount();
			obj_effect.Duration = (AddBuff->IsPermanent)? 0 : AddBuff->ExpireTime - (int) GetNet7TickCount();
			obj_effect.EffectDescID = AddBuff->EffectID[i];
			// recolour the effect
			if (colour != -1)
			{
				obj_effect.Bitmask |= 0x10;
				obj_effect.HSVShift[0] = (float)colour;
			}

			m_BuffList[m_BuffID].RemoveEffectID[i] = m_Owner->m_Effects.AddEffect(&obj_effect);
			//mob->DebuffDisplay(&obj_effect); // should be covered by the above now
		}
		else
		{
			m_BuffList[m_BuffID].RemoveEffectID[i] = -1;
		}
	}

	// Update Stats
	for(int x=0;x<MAX_STATS_PER_BUFF;x++)
	{
		// Add buff
		if (AddBuff->Stats[x].StatName[0]) // StatName is inlined so its address is always non zero!
		{
			m_BuffList[m_BuffID].StatID[x] = m_Owner->m_Stats.SetStat(AddBuff->Stats[x].StatType, 
				AddBuff->Stats[x].StatName,	AddBuff->Stats[x].Value, StatSource ? StatSource : AddBuff->BuffType);

			m_Owner->m_Stats.UpdateAux(AddBuff->Stats[x].StatName);
		}
		else
		{
			m_BuffList[m_BuffID].StatID[x] = 0;
		}
	}

	if (player)
	{
		// Create Buff data to be sent to client
		_Buff myBuff;
		memset(&myBuff,0,sizeof(_Buff));
		myBuff.BuffRemovalTime = AddBuff->ExpireTime;
		strncpy_s(myBuff.BuffType, sizeof(myBuff.BuffType), AddBuff->BuffType,128);
		myBuff.BuffType[127]='\0';  //force string terminate
		myBuff.IsPermanent = AddBuff->IsPermanent;
		memcpy(&myBuff.Elements, &AddBuff->Elements, sizeof(_Elements));

		// Copy data in
		memcpy(&m_BuffList[m_BuffID].AuxBuffData, &myBuff, sizeof(_Buff));
		// ---

		if (FreeFound)
		{
			// Set Data in Aux
			m_BuffList[m_BuffID].AuxSlot = FreeSpot;
			player->ShipIndex()->Buffs.Buff[FreeSpot].SetData(&myBuff);
		}
		else
		{
			//emergency! no free buff slots, so for now just take the first slot
			//note this doesn't affect the actual buff stats themselves of the replaced buff
			m_BuffList[m_BuffID].AuxSlot = 0;
			player->ShipIndex()->Buffs.Buff[0].SetData(&myBuff);
		}

		// Send Aux Data
		player->SendAuxShip();
	}

	// add removal callback if not permanent
	/*
	if(!AddBuff->IsPermanent)
	{
		SectorManager *sm = m_Owner->GetSectorManager();
		if (sm) sm->AddTimedCall(m_Owner, B_REMOVE_BUFF, AddBuff->ExpireTime - GetNet7TickCount(), NULL, m_BuffID, 0, 0);
	}
	*/
	// always add buffs (even if all cant be displayed)
	
	if(!AddBuff->IsPermanent)
	{
		long expire = AddBuff->ExpireTime; // copy out of packed struct
		m_expireTimes.insert(pair<long,int>(expire,m_BuffID));
	}

	m_BuffID++;
	return (m_BuffID-1);
}

int CMobBuffs::RefreshOrAddBuff(struct Buff *RefreshBuff)
{
	Player *player = GetPlayer();
	unsigned long TickCount = GetNet7TickCount();
	mapBuffs::iterator Buff;

	m_Mutex.Lock();
	// does it exist
	for(Buff=m_BuffList.begin();Buff!=m_BuffList.end();++Buff)
	{
		struct MapBuffs *mapbuff = &(Buff->second);



		// yes? refresh its time
		if (!strcmp(mapbuff->BuffData.BuffType, RefreshBuff->BuffType) && 
			((mapbuff->BuffData.ExpireTime > TickCount) || mapbuff->BuffData.IsPermanent))  //refresh if this hasn't expired OR its permanent (likely we're setting an expire time for it now)
		{
			// Remove the event time if this buff we're refreshing isn't permanent
			if(!mapbuff->BuffData.IsPermanent)
			{
				pair< multimap<long,int>::iterator , multimap<long,int>::iterator > range = m_expireTimes.equal_range(mapbuff->BuffData.ExpireTime);
				for(multimap<long,int>::iterator iter = range.first;
					iter != range.second;
					iter++)
				{
					if(iter->second == Buff->first)
					{
						m_expireTimes.erase(iter);
						break;
					}
				}
			}

			mapbuff->BuffData.ExpireTime = RefreshBuff->ExpireTime;
			mapbuff->BuffData.IsPermanent = RefreshBuff->IsPermanent;

			//add the new data if its not permanent

			if(!mapbuff->BuffData.IsPermanent)
			{
				long expire = mapbuff->BuffData.ExpireTime; // copy out of packed struct
				m_expireTimes.insert(pair<long,int>(expire,Buff->first));
			}

			if (player)
			{
				// Update time in Aux
				mapbuff->AuxBuffData.BuffRemovalTime = RefreshBuff->ExpireTime;
				mapbuff->AuxBuffData.IsPermanent = RefreshBuff->IsPermanent;
				player->ShipIndex()->Buffs.Buff[mapbuff->AuxSlot].SetData(&mapbuff->AuxBuffData);
				player->SendAuxShip();
			}
			/*
			if(!mapbuff->BuffData.IsPermanent)
			{
				//reschedule buff to expire
				SectorManager *sm = m_Owner->GetSectorManager();
				if (sm) sm->AddTimedCall(m_Owner, B_REMOVE_BUFF, RefreshBuff->ExpireTime - GetNet7TickCount(), NULL, Buff->first, 0, 0);
			}
			*/
			m_Mutex.Unlock();
			return Buff->first;
		}
	}
	// add a new one
	int retval = _AddBuff(RefreshBuff);
	m_Mutex.Unlock();
	return retval;
}

// remove a buff based on its name
bool CMobBuffs::RemoveBuff(char *BuffName)
{
	mapBuffs::iterator Buff;
	m_Mutex.Lock();
	for(Buff=m_BuffList.begin();Buff!=m_BuffList.end();++Buff)
	{
		struct MapBuffs *mapbuff = &(Buff->second);

		if (!strcmp(mapbuff->BuffData.BuffType, BuffName))
		{
			_Update(Buff->first,true); // ignore remaining time
			m_Mutex.Unlock();
			return true;
		}
	}
	m_Mutex.Unlock();
	return false;
}

void CMobBuffs::Clear()
{
	m_Mutex.Lock();
	while(m_BuffList.size() > 0)
	{
		_Update(m_BuffList.begin()->first,true);
	}
	m_expireTimes.clear();
	m_BuffList.clear();
	m_Mutex.Unlock();
}

void CMobBuffs::Update(int BuffID, bool force_remove, bool skip_dmg_shield)
{
	m_Mutex.Lock();
	_Update(BuffID,force_remove,skip_dmg_shield);
	m_Mutex.Unlock();
}

// Update & Remove expired buffs
void CMobBuffs::_Update(int BuffID, bool force_remove, bool skip_dmg_shield)
{
	Player *player = GetPlayer();
	unsigned long TickCount = GetNet7TickCount();
	mapBuffs::iterator Buff;
	bool RemovedStat = false;

	//BUG! need to remove STL from dynamic data - keeps crashing for no debuggable reason!!
	if (m_BuffList.find(BuffID) != m_BuffList.end())
	{
		// check if it has expired
		if (force_remove || m_BuffList[BuffID].BuffData.ExpireTime <= TickCount && (!m_BuffList[BuffID].BuffData.IsPermanent))
		{
			for(int x=0;x<MAX_STATS_PER_BUFF;x++)
			{
				if (m_BuffList[BuffID].StatID[x] != 0)
				{
					m_Owner->m_Stats.DelStat(m_BuffList[BuffID].StatID[x]);
					RemovedStat = true;
				}
			}



			// remove the graphics effect
			for(int i = 0; i < MAX_EFFECTS_PER_BUFF; i++)
			{
				if (m_BuffList[BuffID].RemoveEffectID[i] != -1)
				{
					m_Owner->m_Effects.RemoveEffect(m_BuffList[BuffID].RemoveEffectID[i]);
				}
			}

			// remove the damage absorb
			if (m_BuffList[BuffID].BuffData.AbsorbID)
			{
				if (!skip_dmg_shield)
					m_Owner->RemoveDamageShield(m_BuffList[BuffID].BuffData.AbsorbID);
			}

			if (player)
			{
				// remove from client buff panel
				if (force_remove || m_BuffList[BuffID].BuffData.IsPermanent)
				{
					long aux_slot = m_BuffList[BuffID].AuxSlot;
					if (aux_slot >= 0 && aux_slot < 16) //crash avoidance check
					{
						player->ShipIndex()->Buffs.Buff[aux_slot].Clear();
					}
					RemovedStat = true;
				}

				if (RemovedStat)
				{
					player->SendAuxShip();
				}
			}

			// Remove the event time
			if(!m_BuffList[BuffID].BuffData.IsPermanent)
			{
				pair<multimap<long,int>::iterator,multimap<long,int>::iterator> range = m_expireTimes.equal_range(m_BuffList[BuffID].BuffData.ExpireTime);
				for(multimap<long,int>::iterator iter = range.first;
					iter != range.second;
					iter++)
				{
					if(iter->second == BuffID)
					{
						m_expireTimes.erase(iter);
						break;
					}
				}
			}
			// Remove the buff
			m_BuffList.erase(m_BuffList.find(BuffID));

			
		}
		/*
		else
		{
			// ask to be called again when time is up
			SectorManager *sm = m_Owner->GetSectorManager();
			if (sm && !m_BuffList[BuffID].BuffData.IsPermanent) sm->AddTimedCall(m_Owner, B_REMOVE_BUFF, m_BuffList[BuffID].BuffData.ExpireTime - GetNet7TickCount(), NULL, BuffID, 0, 0);
		}
		*/
	}
}

void CMobBuffs::ChangeSector()
{
	Player *player = GetPlayer();
	if (player)
	{
		int FreeSpot;
		_Buff clearBuff;
		memset(&clearBuff,0,sizeof(_Buff));
		m_Mutex.Lock();
		// Display all of the buffs again to the client
		for(FreeSpot = 0;FreeSpot<16;FreeSpot++)
		{
			if (m_BuffList[FreeSpot].AuxBuffData.IsPermanent || m_BuffList[FreeSpot].AuxBuffData.BuffRemovalTime > GetNet7TickCount())
			{
				player->ShipIndex()->Buffs.Buff[m_BuffList[FreeSpot].AuxSlot].SetData(&clearBuff);
				player->ShipIndex()->Buffs.Buff[m_BuffList[FreeSpot].AuxSlot].SetData(&m_BuffList[FreeSpot].AuxBuffData);
			}
		}
		player->SendAuxShip();
		m_Mutex.Unlock();
	}
}

void CMobBuffs::CheckBuffExpire(long now)
{
	m_Mutex.Lock();
	while((m_expireTimes.size() > 0) && (m_expireTimes.begin()->first <= now))
	{
		_Update(m_expireTimes.begin()->second,true,false);
	}
	m_Mutex.Unlock();
}
