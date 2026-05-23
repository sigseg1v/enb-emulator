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
#include "Stats.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include <stdio.h>
#include <stdlib.h>
#include <cstdlib>
#include <string>

Stats::Stats()
{
	m_Owner = 0 ;
    m_Player = 0;
	m_MOB = 0;
	m_ValueID = 0;
}

Stats::~Stats()
{
	ResetStats();
}

void Stats::Init(Object * Owner)
{
	m_Mutex.Lock();
	if(Owner->ObjectType() == OT_PLAYER)
		m_Player = (Player *)Owner;		

	if(Owner->ObjectType() == OT_PLAYER)
		m_MOB = (MOB *)Owner;		
	m_Mutex.Unlock();
	// Zero out all data
	//m_ValueID = 0;
	ResetStats();

}

/*
bool Stats::StatCallBack(StatCallBack func, char *Stat)
{
}
*/

void Stats::ResetStats()
{
	m_Mutex.Lock();
	// Zero out all data
	for (mapStatValues::iterator iter = m_StatsValues.begin(); iter != m_StatsValues.end(); ++iter)
	{
		for(int x=0;x<5;x++)
		{
			m_StatsValues[iter->first].Types[x].Buff.clear();
			m_StatsValues[iter->first].Types[x].Total = 0;
		}
	}
	m_StatsValues.clear();
	m_ValueLookup.clear();
	m_Mutex.Unlock();
}

void Stats::ResetStat(string Stat)
{
	m_Mutex.Lock();
	// Erase all data
	for(int x=0;x<5;x++)
	{
		m_StatsValues[Stat].Types[x].Buff.clear();
		m_StatsValues[Stat].Types[x].Total = 0;
	}
	m_Mutex.Unlock();
}

bool Stats::ChangeStat( int StatID, float NewValue )
{
	bool retval =false;
	m_Mutex.Lock();
	retval = _ChangeStat(StatID,NewValue);
	m_Mutex.Unlock();
	return retval;
}

bool Stats::_ChangeStat( int StatID, float NewValue )
{
	// Make sure this Stat ID is valid
	if (m_ValueLookup.find(StatID) == m_ValueLookup.end())
	{
		LogMessage("Warning! When trying to change StatID: %d Couldn't Find Stat in Lookup!\n", StatID);
		return false;
	}

	// Get the Stat Data
	string Buff = m_ValueLookup[StatID].BuffName;
	string Stat = m_ValueLookup[StatID].StatName;
	int Type = m_ValueLookup[StatID].Type;

	// Make sure we can find stat ID
	if (m_StatsValues[Stat].Types[Type].Buff[Buff].Values.find(StatID) == m_StatsValues[Stat].Types[Type].Buff[Buff].Values.end())
	{
		LogMessage("Warning! When trying to change StatID: %d Couldn't Find Stat!\n", StatID);
		return false;
	}

	// Change this value
	m_StatsValues[Stat].Types[Type].Buff[Buff].Values[StatID] = NewValue;

	// recalculate Stat
	CalculateValue(Stat, Type, Buff);
	return true;
}


// Trim Whitespace out of a string
char *str_trim( const char *s)
{
	char *news; /* result string */
    int n;     /* number of chars in new string, excluding trailing '\0' */
   
    while ( *s == ' ' )
   		++s;
		n = 0;
	while ( s[n] != '\0' )
		++n;
	while ( n > 0 && s[n-1] == ' ' )
   		--n;

	news = (char *) calloc( n+1, sizeof(char) );
	memcpy( news, s, n );
	return news;
}

int Stats::SetStat(int BaseType, string Stat, float Data, string BuffName)
{
	int retval = 0;
	m_Mutex.Lock();
	retval = _SetStat(BaseType,Stat,Data,BuffName);
	m_Mutex.Unlock();
	return retval;
}

int Stats::_SetStat(int BaseType, string Stat, float Data, string BuffName)
{
	/*
	char * Stat = g_StringMgr->GetStr((char *) Stats);
	char * BuffName = g_StringMgr->GetStr((char *) BuffNames);
*/
	if (BaseType != STAT_BASE_VALUE && !BuffName.compare("BASE_VALUE"))
	{
		LogMessage("Warning! Stats: BuffName = BASE_VALUE & BaseType != BASE\n");
	}

	// Keep a unique ID for each value
	m_ValueID++;

	//Yet another undebuggable STL crashbug
	if (m_StatsValues.find(Stat) == m_StatsValues.end())
	{
		for(int x=0;x<5;x++)
		{
			m_StatsValues[Stat].Types[x].Buff.clear();
			m_StatsValues[Stat].Types[x].Total = 0;
		}
	}

	// Save Data
	m_StatsValues[Stat].Types[BaseType].Buff[BuffName].Values[m_ValueID] = Data;

	// Save this to look up later for deleation
	m_ValueLookup[m_ValueID].BuffName = BuffName;
	m_ValueLookup[m_ValueID].StatName = Stat;
	m_ValueLookup[m_ValueID].Type = BaseType;

	// Calculate Stat
	CalculateValue(Stat, BaseType, BuffName);
	return m_ValueID;
}

float Stats::CalculateStat(string Stat)
{
	float Value = 0;

	//For warp recovery, the buff makes the number smaller.
	//TO-DO: Implement all other buffs that make values smaller instead of larger
	float Multiplyer = (1.0f + (m_StatsValues[Stat].Types[STAT_BUFF_MULT].Total)) - (m_StatsValues[Stat].Types[STAT_DEBUFF_MULT].Total);
	Value = (m_StatsValues[Stat].Types[STAT_BASE_VALUE].Total * Multiplyer) + (m_StatsValues[Stat].Types[STAT_BUFF_VALUE].Total - m_StatsValues[Stat].Types[STAT_DEBUFF_VALUE].Total);
	return Value;
}




float Stats::CalculateStatType( string Stat, int Type)
{
	float Total = 0;

	// Total all the values
	for(mapBuffStats::const_iterator SValue = m_StatsValues[Stat].Types[Type].Buff.begin(); SValue != m_StatsValues[Stat].Types[Type].Buff.end(); ++SValue)
	{
		Total += SValue->second.MaxValue;
	}

	return Total;
}

float Stats::FindMaxBuff( string Stat, string Buff, int Type)
{
	float Max = 0;

	for(mapStatValue::const_iterator SValue = m_StatsValues[Stat].Types[Type].Buff[Buff].Values.begin(); SValue != m_StatsValues[Stat].Types[Type].Buff[Buff].Values.end(); ++SValue)
	{
		if (Max < (float) SValue->second)
		{
			Max = (float) SValue->second;
		}
	}

	return Max;
}

void Stats::CalculateValue(string Stat, int Type, string Buff)
{
	// Find Max for Buff
	m_StatsValues[Stat].Types[Type].Buff[Buff].MaxValue = FindMaxBuff(Stat, Buff, Type);
	m_StatsValues[Stat].Types[Type].Total = CalculateStatType(Stat, Type);
	m_StatsValues[Stat].Total = CalculateStat(Stat);
}

bool Stats::DelStat(int StatID)
{
	bool retval = false;
	m_Mutex.Lock();
	retval = _DelStat(StatID);
	m_Mutex.Unlock();
	return retval;
}

bool Stats::_DelStat(int StatID)
{
	//STL BUG! yet another undebuggable crash with STL using dynamic data REWRITE this properly, without STL
	//Note from mozu: Crashed here on 9/15/09. I am now eating my words about STL not causing problems.
	string Buff = "";
	string Stat = "";
	int Type = 0;
	try {
		// Make sure this Stat ID is valid
		if (m_ValueLookup.find(StatID) == m_ValueLookup.end())
		{
			LogMessage("Warning! When trying to delete StatID: %d Couldn't Find Stat in Lookup!\n", StatID);
			return false;
		}	

		// Clear the Data
		Buff = m_ValueLookup[StatID].BuffName;
		Stat = m_ValueLookup[StatID].StatName;
		Type = m_ValueLookup[StatID].Type;

		// Make sure we can find stat ID
		if (m_StatsValues.find(Stat) == m_StatsValues.end() ||
			m_StatsValues[Stat].Types[Type].Buff.find(Buff) == m_StatsValues[Stat].Types[Type].Buff.end() ||
			m_StatsValues[Stat].Types[Type].Buff[Buff].Values.find(StatID) == m_StatsValues[Stat].Types[Type].Buff[Buff].Values.end())
		{
			LogMessage("Warning! Unable to find Stat: %d,%s,%s\n", StatID, Stat.c_str(), Buff.c_str());
			return false;
		}

		// Erase this value

		m_StatsValues[Stat].Types[Type].Buff[Buff].Values.erase(m_StatsValues[Stat].Types[Type].Buff[Buff].Values.find(StatID));
		m_ValueLookup.erase(StatID);

		// recalculate Stat
		CalculateValue(Stat, Type, Buff);

		// Update aux
		_UpdateAux(Stat);
	}
	catch (...)
	{
		LogMessage("Failed to remove stat value: %s [%d], type: %d, buff: %s", Stat.c_str(), StatID, Type, Buff.c_str());
		return false;
	}
	return true;
}

float Stats::GetStat(string Stat)
{
	float val = 0.0f;
	m_Mutex.Lock();
	val = _GetStat(Stat);
	m_Mutex.Unlock();
	return val;
}

// Get the stat from memory
float Stats::_GetStat(string Stat)
{
	// See if we have it in memory
	//char * Stat = g_StringMgr->GetStr((char *) Stats);
	
	if (m_StatsValues.find(Stat) != m_StatsValues.end())
	{
		float Total = m_StatsValues[Stat].Total;
		return Total;
	}
	else
	{
		return 0;
	}
}

float Stats::GetStatType(string Stat, int Type)
{
	float retval = 0.0f;
	m_Mutex.Lock();
	retval = _GetStatType(Stat,Type);
	m_Mutex.Unlock();
	return retval;
}

// Return a specific type
float Stats::_GetStatType(string Stat, int Type)
{
	if (m_StatsValues.find(Stat) != m_StatsValues.end())
	{
		return m_StatsValues[Stat].Types[Type].Total;
	}
	else
	{
		return 0;
	}
}

// use the supplied value as a base stat and recompute
float Stats::ModifyValueWithStat(string Stat, float Base_Value)
{
	m_Mutex.Lock();
	if (m_StatsValues.find(Stat) != m_StatsValues.end())
	{
		float Multiplyer = (1.0f + (m_StatsValues[Stat].Types[STAT_BUFF_MULT].Total)) - (m_StatsValues[Stat].Types[STAT_DEBUFF_MULT].Total);
		Base_Value = (Base_Value * Multiplyer) + (m_StatsValues[Stat].Types[STAT_BUFF_VALUE].Total - m_StatsValues[Stat].Types[STAT_DEBUFF_VALUE].Total);
		m_Mutex.Unlock();
		return Base_Value;
	}
	else
	{
		m_Mutex.Unlock();
		return Base_Value;
	}
}

void Stats::UpdateAux(string Stat)
{
	m_Mutex.Lock();
	_UpdateAux(Stat);
	m_Mutex.Unlock();
}

// Only used with Players?
void Stats::_UpdateAux(string Stat)
{
	if(!m_Player)
	{
		return;
	}
	if (!Stat.compare( STAT_WARP))
	{
		s32 WarpSpeed = (s32)this->_GetStat(Stat);
		m_Player->ShipIndex()->CurrentStats.SetWarpSpeed(WarpSpeed);
	}
	else if (!Stat.compare( STAT_IMPULSE) || !Stat.compare( STAT_ENGINE_TOP_SPEED) || !Stat.compare( STAT_TURN_RATE) || !Stat.compare( STAT_SHIP_MASS))
	{
		m_Player->AdjustAndSetSpeeds(false,false);
	}
	else if (!Stat.compare( STAT_SCAN_RANGE))
	{
		m_Player->ShipIndex()->CurrentStats.SetScanRange((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_CHEM_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistChemical((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_PSIONIC_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistPsionic((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_EMP_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistEMP((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_ENERGY_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistEnergy((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_EXPLOSIVE_DEFLECT)) 
	{
		m_Player->ShipIndex()->CurrentStats.SetResistExplosion((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_IMPACT_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistImpact((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_PLASMA_DEFLECT))
	{
		m_Player->ShipIndex()->CurrentStats.SetResistPlasma((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_SHIELD))
	{
		m_Player->ShipIndex()->SetMaxShield((float)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_SIGNATURE))
	{
		m_Player->ShipIndex()->CurrentStats.SetVisibility((s32)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_ENERGY))
	{
		m_Player->ShipIndex()->SetMaxEnergy((float)_GetStat(Stat));
	}
	else if (!Stat.compare( STAT_BEAM_RANGE) || !Stat.compare( STAT_MISSILE_RANGE) || !Stat.compare( STAT_PROJECTILES_RANGE))
	{
		for (int i=3; i<9; i++) //only iterate over weapon slots (3,4,5,6,7,8)
		{
			// Update all ranges for weapons
			m_Player->m_Equip[i].UpdateRange(_GetStatType(STAT_BEAM_RANGE, STAT_BUFF_MULT) , 
												_GetStatType(STAT_BEAM_RANGE,STAT_DEBUFF_MULT),

										     _GetStatType(STAT_BEAM_RANGE, STAT_BUFF_VALUE) , 
												_GetStatType(STAT_BEAM_RANGE,STAT_DEBUFF_VALUE),

											 _GetStatType(STAT_PROJECTILES_RANGE, STAT_BUFF_MULT) , 
												_GetStatType(STAT_PROJECTILES_RANGE, STAT_DEBUFF_MULT),

											 _GetStatType(STAT_PROJECTILES_RANGE, STAT_BUFF_VALUE) , 
												_GetStatType(STAT_PROJECTILES_RANGE, STAT_DEBUFF_VALUE),

											 _GetStatType(STAT_MISSILE_RANGE,STAT_BUFF_MULT) , 
												_GetStatType(STAT_MISSILE_RANGE, STAT_DEBUFF_MULT),

											 _GetStatType(STAT_MISSILE_RANGE,STAT_BUFF_VALUE) , 
												_GetStatType(STAT_MISSILE_RANGE, STAT_DEBUFF_VALUE));
		}
	}
	else if (Stat.compare(STAT_ENERGY_RECHARGE)==0)
	{
		//if our reactor is being drained, don't update
		if(m_Player->ShipIndex()->Energy.GetChangePerTick() > 0)
		{
			m_Player->RecalculateEnergyRegen(this->_GetStat(STAT_ENERGY),this->_GetStat(STAT_ENERGY_RECHARGE),false);
		}
	}
	else if (Stat.compare(STAT_SHIELD_RECHARGE)==0)
	{
		//if our shield is being drained, don't update
		if(m_Player->ShipIndex()->Shield.GetChangePerTick() > 0)
		{
			m_Player->RecalculateShieldRegen(this->_GetStat(STAT_SHIELD),this->_GetStat(STAT_SHIELD_RECHARGE),false);
		}
	}
	
}