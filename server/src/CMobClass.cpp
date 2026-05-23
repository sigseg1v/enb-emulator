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
#include "ItemBaseManager.h"
#include "PlayerSkills.h"
#include "PlayerClass.h"
#include "CMobClass.h"

// Load in all Abilities!
#include "Abilities.h"

CMob::CMob(long object_id) : Object (object_id)
{
	ResetCommon();
	SetupAbilities();
}

CMob::CMob(void) : Object (-1)
{
	ResetCommon();
	SetupAbilities();
}

CMob::~CMob(void)
{
	DeleteAbilities();
	ClearDamageShields(true);
}

void CMob::ResetCommon()
{
    m_ShieldRecharge = 0;
	m_LastShieldChange = 0;
	m_CurrentSkill = NULL;
	m_ShieldListHead = NULL;
	m_StealthLevel = 0;
	memset (&m_ClickedList, 0, sizeof(m_ClickedList));
}

float CMob::GetShield()
{
	u32 myTime = GetNet7TickCount();

	if (m_LastShieldChange == 0)
	{
		m_LastShieldChange = myTime;
	}

	// dont do recharge calc if we are in combat
	u32 timeElapsed = 0;
	if (m_ShieldRecharge < myTime)
	{
		timeElapsed = myTime - m_LastShieldChange;
		if (myTime > ShieldAux()->GetEndTime())
		{
			timeElapsed = ShieldAux()->GetEndTime() - m_LastShieldChange;
		}
	}

	float myShield = ShieldAux()->GetStartValue() + (float)(timeElapsed) * ShieldAux()->GetChangePerTick();

	if (myShield > 1.0f)
	{
		myShield = 1.0f;
	}
	else if (myShield < 0.0f)
	{
		myShield = 0.0f;
	}

	return (myShield);
}

float CMob::GetShieldValue()
{
	return GetShield() * GetMaxShield();
}

//DB Shield Recharge stops
void CMob::RemoveShield(float ShieldRemoved)
{
    unsigned long CurTick = GetNet7TickCount();
	float maxShield = GetMaxShield();
	if (maxShield == 0.0f)
		return;
	float myShieldOld = GetShield();
	float myShield = myShieldOld - (ShieldRemoved / maxShield);

	if (myShieldOld > 0.10f && myShield <= 0.10f)
	{
		SendClientSound("1512_00_078Se.MP3",0,0,1);
	}
	else if (myShieldOld > 0.25f && myShield <= 0.25f)
	{
		SendClientSound("1512_00_077Se.MP3",0,0,2);
	}
	else if (myShieldOld > 0.50f && myShield <= 0.50f)
	{
		SendClientSound("1512_00_076Se.MP3",0,0,3);
	}
	else if (myShieldOld > 0.75f && myShield <= 0.75f)
	{
		SendClientSound("1512_00_075Se.MP3",0,0,4);
	}
	
    if (myShield < 0.0f)
    {
        myShield = 0.0f;
    }
	else if (myShield > maxShield)
	{
		myShield = maxShield;
	}

    if (ShieldAux()->GetChangePerTick() != 0)
    {
        ShieldAux()->SetEndTime(0);
        ShieldAux()->SetChangePerTick(0);
    }

	ShieldAux()->SetStartValue(myShield);

    m_LastShieldChange = GetNet7TickCount();

	if (ObjectType() != OT_MOB) //TB: sorry about this, was trying to avoid this sort of thing, may be able to remove once player mods are finished
								//We can't allow any Aux to be sent/changed during the sector threads
								//MOB shield Aux is updated once every MOB update cycle. That's when Aux is sent if any levels change
	{
		SendAuxShip();
	}

    m_ShieldRecharge = CurTick + SHIELD_RECHARGE_RESUME_DELAY;
}

void CMob::ShieldUpdate(unsigned long EndTime, float ChangePerTick, float StartValue)
{
	if (GetIsIncapacitated()) ChangePerTick = 0.0f;

    m_LastShieldChange = GetNet7TickCount();
    ShieldAux()->SetEndTime(EndTime);
    ShieldAux()->SetChangePerTick(ChangePerTick);
    ShieldAux()->SetStartValue(StartValue);

    SendAuxShip();
}

void CMob::RechargeShield()
{
    float Shield = GetShield();
	float Recharge = BaseShieldRecharge();
	unsigned long RequiredTime = GetNet7TickCount();
	if(Recharge != 0.0f)
	{
		RequiredTime += (unsigned long)((1.0f - Shield) / Recharge);
	}

	// Dont regen if Incapacited or powered down (rank 1)

	if (!GetIsIncapacitated())
	{
		m_ShieldRecharge = 0;
		ShieldUpdate(RequiredTime, Recharge, Shield);
	}
}

void CMob::RecalculateShieldRegen(float MaxShield, float RechargeShield, bool pullData)
{
	if(pullData)
	{
		MaxShield = m_Stats.GetStat(STAT_SHIELD);
		RechargeShield = m_Stats.GetStat(STAT_SHIELD_RECHARGE);
	}
	float StartValue = GetShield();
	float ChargeRate = (RechargeShield / MaxShield) / 1000.0f;
	unsigned long EndTime = GetNet7TickCount();
	if(ChargeRate != 0.0f)
	{
		EndTime += (unsigned long)((1.0f - StartValue) / ChargeRate);
	}
	ShieldUpdate(EndTime, ChargeRate, StartValue);
}

struct QualityArray 
{
	int ItemType;
	int ItemField;
	int Direction; // 1 reduces the stat
	float MaxQuality;
	float MinQuality;
};

bool CMob::QualityCalculatorEffects(struct _Item * myItem)
{
	char InstanceCache[6*3*8+1];
	char *ptr_cache = InstanceCache;

	ItemBase * myItemBase = g_ItemBaseMgr->GetItem(myItem->ItemTemplateID);

	if (!myItemBase) return false; // early return if item invalid

	// Do not process ammo
	if (myItemBase->ItemType() == IB_ITEMTYPE_AMMO)
	{
		return false;
	}

	// Calculate Real Quality Percent
	float RealPercent;
	float ChangeRate,Ratio;
	float ItemQuality = myItem->Quality;
	float NewFieldValue = 0;

	// factor the structure into the calc (except for ammo!)
	ItemQuality *= (myItem->Structure > 1.0f) ? 1.0f : myItem->Structure;

	InstanceCache[0] = 0;
	InstanceCache[2] = 0;
	// Activated Info
	for(int x=0;x<myItemBase->ActivatableCount();x++)
	{
		ItemEffect *iEffect = myItemBase->GetActiveEffect(x);
		ptr_cache += sprintf(ptr_cache, "^");
		for(int stat=0;stat<iEffect->DescVarCount;stat++)
		{
			float Mod = iEffect->VarMod[stat];

			// Calculate real percent from numbers
			if (ItemQuality < 1.0f)
			{
				if (Mod < 1)
				{
					Ratio = 1.0f;
					if (ItemQuality > 0.01f)						
						Ratio = 1.0f / (ItemQuality * 100.0f);		// based on the curve f(x) = 1/x where 0 < x < 1
					RealPercent = Mod * Ratio;		// 0.0+(100.0)*0.01->1 = 1->100		
				}
				else // 0.01+(1.0-0.01)*0->1 = 0.01->1
				{
					RealPercent = 0.01f + (1.0f - 0.01f) * ItemQuality;
				}
			}
			else
			{
				ChangeRate = Mod - 1.0f;										// 0.65 - 1.0 = -0.35					1.5 - 1.0 = 0.5
				RealPercent = (ItemQuality - 1.0f) * ChangeRate + 1.0f;			// (2.0 - 1.0)*-0.35 + 1.0 = 0.65		(2.0 - 1.0)*0.5 + 1.0 = 1.5
			}

			// Calculate the field data (just multiply for both directions)
			NewFieldValue = RealPercent * iEffect->DescVar[stat];

			// Client does not read below 1 ? (what about Executioners Fist, reload 0.75@100 ?)
			if (NewFieldValue < 0.1f)
				NewFieldValue = 0.1f;

			ptr_cache += sprintf(ptr_cache, "%4.2f,", NewFieldValue);
		}
		ptr_cache += sprintf(ptr_cache, "#");
	}
	if (InstanceCache[2] == 0)		// See if we have more than 2 chars in the instance info
	{
		InstanceCache[0] = 0;
	}
	// Copy our instance info
	strncpy(myItem->ActivatedEffectInstanceInfo, InstanceCache, sizeof(myItem->ActivatedEffectInstanceInfo));

	InstanceCache[0] = 0;
	InstanceCache[2] = 0;
	// Equip Info
	for(int x=0;x<myItemBase->EquipableCount();x++)
	{
		ItemEffect *iEffect = myItemBase->GetEquipEffect(x);
		ptr_cache += sprintf(ptr_cache, "^");
		for(int stat=0;stat<iEffect->DescVarCount;stat++)
		{
			float Mod = iEffect->VarMod[stat];

			// Calculate real percent from numbers
			if (ItemQuality < 1.0f)
			{
				if (Mod < 1)
				{
					Ratio = 1.0f;
					if (ItemQuality > 0.01f)						
						Ratio = 1.0f / (ItemQuality * 100.0f);		// based on the curve f(x) = 1/x where 0 < x < 1
					RealPercent = Mod * Ratio;		// 0.0+(100.0)*0.01->1 = 1->100		
				}
				else // 0.01+(1.0-0.01)*0->1 = 0.01->1
				{
					RealPercent = 0.01f + (1.0f - 0.01f) * ItemQuality;
				}
			}
			else
			{
				ChangeRate = Mod - 1.0f;										// 0.65 - 1.0 = -0.35					1.5 - 1.0 = 0.5
				RealPercent = (ItemQuality - 1.0f) * ChangeRate + 1.0f;			// (2.0 - 1.0)*-0.35 + 1.0 = 0.65		(2.0 - 1.0)*0.5 + 1.0 = 1.5
			}

			// Calculate the field data (just multiply for both directions)
			NewFieldValue = RealPercent * iEffect->DescVar[stat];

			// Do not update if their is no value
			if (NewFieldValue < 0.1f)
				break;

			ptr_cache += sprintf(ptr_cache, "%4.2f,", NewFieldValue);
		}
		ptr_cache += sprintf(ptr_cache, "#");
	}
	if (InstanceCache[2] == 0)		// See if we have more than 2 chars in the instance info
	{
		InstanceCache[0] = 0;
	}
	// Copy our instance info
	strncpy(myItem->EquipEffectInstanceInfo, InstanceCache, sizeof(myItem->EquipEffectInstanceInfo));

	return true;
}

bool CMob::QualityCalculator(struct _Item * myItem)
{
	char InstanceInfo[64];
	char IInstanceInfo[64];
	int QArraySize = 8;
	QualityArray QArray[] = {{IB_ITEMTYPE_BEAMS			,ITEM_FIELD_WEAPON_DAMAGE_I			, 0, 1.50f,   0.01f  },		// Beams
							 {IB_ITEMTYPE_MISSILES		,ITEM_FIELD_WEAPON_RELOAD_F			, 1, 0.65f,   100.0f },		// ML
							 {IB_ITEMTYPE_PROJECTILES	,ITEM_FIELD_WEAPON_RELOAD_F			, 1, 0.63f,   100.0f },		// Projectiles
							 {IB_ITEMTYPE_SHIELDS		,ITEM_FIELD_SHIELD_RECHARGE_F		, 0, 1.35f,   0.01f  },		// Shields
							 {IB_ITEMTYPE_ENGINES		,ITEM_FIELD_ENGINE_SPEED_I			, 0, 1.3475f, 0.01f  },		// Engines Thrust
							 {IB_ITEMTYPE_ENGINES		,ITEM_FIELD_ENGINE_FREEWARP_DRAIN_F	, 1, 0.65f,   100.0f },		// Engines Warp Drain
							 {IB_ITEMTYPE_REACTORS		,ITEM_FIELD_REACTOR_RECHARGE_F		, 0, 1.35f,   0.01f  },		// Reactors
							 {IB_ITEMTYPE_AMMO			,ITEM_FIELD_WEAPON_DAMAGE_I			, 0, 1.30f,   0.01f  }		// Ammo
							};

	ItemBase * myItemBase = g_ItemBaseMgr->GetItem(myItem->ItemTemplateID);

	memset(InstanceInfo, 0, sizeof(InstanceInfo));

	if (!myItemBase) return false; // early return if item invalid

	for(int x=0;x<QArraySize;x++)
	{
		if (QArray[x].ItemType == myItemBase->ItemType())
		{
			int FieldID = QArray[x].ItemField;
			// Get Field Type
			int FieldType = myItemBase->FieldType(FieldID);
			float FieldData = 0;

			// Read in Data
			switch(FieldType)
			{
				// Float
				case 1:
					FieldData = myItemBase->Fields(FieldID)->fData;
					break;
				// Int
				case 2:
					FieldData = (float) myItemBase->Fields(FieldID)->iData;
					break;
			}

			// Calculate Real Quality Percent
			float RealPercent;
			float ChangeRate,Ratio;
			float ItemQuality = myItem->Quality;
			float NewFieldValue = 0;

			// factor the structure into the calc (except for ammo!)
			if (QArray[x].ItemType != IB_ITEMTYPE_AMMO)
				ItemQuality *= myItem->Structure;

			// Calculate real percent from numbers
			if (ItemQuality < 1.0f)
			{
				if (QArray[x].Direction == 1)
				{
					Ratio = 1.0f;
					if (ItemQuality > 0.01f)						
						Ratio = 1.0f / (ItemQuality * 100.0f);		// based on the curve f(x) = 1/x where 0 < x < 1
					RealPercent = QArray[x].MinQuality * Ratio;		// 0.0+(100.0)*0.01->1 = 1->100		
				}
				else // 0.01+(1.0-0.01)*0->1 = 0.01->1
				{
					RealPercent = QArray[x].MinQuality + (1.0f - QArray[x].MinQuality) * ItemQuality;
				}
			}
			else
			{
				ChangeRate = QArray[x].MaxQuality - 1.0f;						// 0.65 - 1.0 = -0.35					1.5 - 1.0 = 0.5
				RealPercent = (ItemQuality - 1.0f) * ChangeRate + 1.0f;			// (2.0 - 1.0)*-0.35 + 1.0 = 0.65		(2.0 - 1.0)*0.5 + 1.0 = 1.5
			}

			// Calculate the field data (just multiply for both directions)
			NewFieldValue = RealPercent * FieldData;

			// Client does not read below 1 ? (what about Executioners Fist, reload 0.75@100 ?)
			if (NewFieldValue < 0.1f)
				NewFieldValue = 0.1f;

			sprintf(IInstanceInfo, "%d:%4.2f^", FieldID,NewFieldValue);
			strcat(InstanceInfo, IInstanceInfo);
		}
	}

	// Calculate the Effect Info
	QualityCalculatorEffects(myItem);

	if (InstanceInfo[0] != 0)
	{
		memcpy(myItem->InstanceInfo, InstanceInfo, sizeof(InstanceInfo));
		return true;
	}

	return false;
}

float CMob::CaughtInEnergyBlast(float blast_range)
{
    float blast_damage = 0.001f;
    if (blast_range < 750.0f)
    {
        blast_damage = 0.005f;
    }
    if (blast_range < 500.0f)
    {
        blast_damage = 0.01f;
    }
    if (blast_range < 400.0f)
    {
        blast_damage = 0.015f;
    }
    if (blast_range < 300.0f)
    {
        blast_damage = 0.02f;
    }
    if (blast_range < 200.0f)
    {
        blast_damage = 0.05f;
    }
    if (blast_range < 100.0f)
    {
        blast_damage = 0.25f;
    }
    if (blast_range < 50.0f)
    {
        blast_damage = 0.5f;
    }
	if (blast_range < 25.0f)
	{
		blast_damage = 1.0f;
	}

    return (blast_damage);
}

void CMob::RemoveDamageShield(DamageShield *damage_shield)
{
	if (damage_shield->remove_buff_id != -1)
		m_Buffs.Update(damage_shield->remove_buff_id,true,true); // recalling RemoveDamageShield from the buff class in here is baaaaaad
	delete damage_shield; // now works
}

void CMob::RemoveDamageShield(long shield_id, bool doUpdate)
{
	DamageShield *current = NULL, *prev = NULL;
	
	m_Mutex.Lock();
	for(current = m_ShieldListHead; current != NULL; current = current->next)
	{
		//removing from end of list
		if(current->shield_id == shield_id && current->next == NULL && current != m_ShieldListHead)
		{
			prev->next = NULL;
			RemoveDamageShield(current);
			break;
		}
		//remove only node in list
		else if(current->shield_id == shield_id && current->next == NULL && current == m_ShieldListHead)
		{
			RemoveDamageShield(current);
			m_ShieldListHead = NULL;
			break;
		}
		//remove node at head of list or anywhere in list that is not last.
		else if(current->shield_id == shield_id && current->next != NULL)
		{
			//see if we are removing the head
			if(prev == current || prev == NULL)
				m_ShieldListHead = current->next;
			else
				prev->next = current->next;
			RemoveDamageShield(current);
			break;
		}
		prev = current;
	}
	m_Mutex.Unlock();
}

/*
* New nodes are added to the head of the list.
*/
void CMob::AddDamageShield(long shield_id, short shield_level, int remove_buff_id, float capacitance, float reflect_percentage, 
							 float reflect_max, float to_reactor_percentage, float to_reactor_max, bool reactor_to_group)
{
	DamageShield *NewNode = NULL;

	//prevent stacking of same shields, remove old, replace with new.
	if(FindDamageShield(shield_id) != NULL)
		RemoveDamageShield(shield_id);

	m_Mutex.Lock();
	NewNode = new DamageShield; // this will lead to gradual heap corruption over time. Can we use a circular buffer of 'DamageShield'?
	NewNode->next = m_ShieldListHead;
	
	NewNode->shield_id = shield_id;
	NewNode->shield_level = shield_level;
	NewNode->remove_buff_id = remove_buff_id;
	NewNode->capacitance = capacitance;
	NewNode->reflect_percentage = reflect_percentage;
	NewNode->reflect_max = reflect_max;
	NewNode->to_reactor_percentage = to_reactor_percentage;
	NewNode->to_reactor_max = to_reactor_max;
	NewNode->reactor_to_group = reactor_to_group;

	if(capacitance > 0)
		NewNode->has_capacitance = true;
	else
		NewNode->has_capacitance = false;

	m_ShieldListHead = NewNode;
	m_Mutex.Unlock();
}

/*
* Returns the Node identified by shield_id, or NULL if it cannot be located.
*/
DamageShield* CMob::FindDamageShield(long shield_id)
{
	DamageShield *found = NULL;

	m_Mutex.Lock();
	for(DamageShield *current = m_ShieldListHead; current != NULL; current = current->next)
	{
		if(current->shield_id == shield_id)
		{
			found = current;
			break;
		}	
	}
	m_Mutex.Unlock();
	return found;
}

void CMob::ClearDamageShields(bool delete_only)
{
	m_Mutex.Lock();

	DamageShield *current = m_ShieldListHead, *next;
	while(current != NULL)
	{
		next = current->next;
		if (delete_only)
			delete current;
		else
			RemoveDamageShield(current);
		current = next;
	}
	m_ShieldListHead = NULL;

	m_Mutex.Unlock();
}

bool CMob::IsEnemyOf(CMob *target)
{
	return  (ObjectType() == OT_PLAYER && target->ObjectType() == OT_MOB) ||
			(ObjectType() == OT_MOB && target->ObjectType() == OT_PLAYER) ||
			(ObjectType() == OT_MOB && target->IsTurret());
	// || 2 players PVP enabled
}

bool CMob::IsFriendOf(CMob *target)
{
	return ObjectType() == target->ObjectType();
}

float CMob::CommonDamageHandling(Object *source, long damage_type, float damage)
{
	float redux_percent;
	switch(damage_type)
	{
	case DAMAGE_IMPACT:
		//is this needed?
		redux_percent = m_Stats.GetStat(STAT_IMPACT_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	case DAMAGE_EXPLOSIVE:
		redux_percent = m_Stats.GetStat(STAT_EXPLOSIVE_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	case DAMAGE_PLASMA:
		redux_percent = m_Stats.GetStat(STAT_PLASMA_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	case DAMAGE_ENERGY:
		redux_percent = m_Stats.GetStat(STAT_ENERGY_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	case DAMAGE_EMP:
		redux_percent = m_Stats.GetStat(STAT_EMP_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	case DAMAGE_CHEMICAL:
		redux_percent = m_Stats.GetStat(STAT_CHEM_DEFLECT)/100.0f;
		damage *= (1.0f - (redux_percent > 0.5f ? 0.5f : redux_percent));
		break;
	}

	//see what damage shields the player has
	if(m_ShieldListHead != NULL)
	{
		for(DamageShield *current = m_ShieldListHead; current != NULL; /* this loop is manually incremented */)
		{
			/*
			* Reflect and To-Reactor come first by design. They are above damage absorb so we know
			*    how much damage we actually took.
			*/

			//handle damage reflect component (energy damage doesnt reflect)  (Repulsor field does, though)
			if(source && current->reflect_percentage > 0.0f && !(damage_type == DAMAGE_ENERGY && current->shield_id == ENVIRONMENTAL_BARRIER))
			{
				if(damage * current->reflect_percentage > current->reflect_max)
				{
					source->DamageObject(-1, DAMAGE_ENERGY, current->reflect_max, 0);
				}
				else
				{
					source->DamageObject(-1, DAMAGE_ENERGY, ceil(damage * current->reflect_percentage), 0);
				}
			}

			//handle damage to reactor component
			if(ObjectType() == OT_PLAYER && current->to_reactor_percentage > 0.0f)
			{
				float energy = damage * current->to_reactor_percentage;
				if (energy > current->to_reactor_max)
				{
					energy = current->to_reactor_max;
				}
				RemoveEnergy(-energy);
				// add to group also
				if (current->reactor_to_group && GroupID() != -1)
				{
					for(int x=0;x<6;x++)
					{
						int PlayerID = g_PlayerMgr->GetMemberID(GroupID(), x);
						if (PlayerID && PlayerID != GameID())
						{
							Player* groupMember = g_PlayerMgr->GetPlayer(PlayerID);

							if (groupMember && IsInSameSector(groupMember) && groupMember->RangeFrom(this) < 4500.0f) // lvl 9 enviro range
							{
								// ADD the energy drained
								groupMember->RemoveEnergy(-energy);

								// Send Energy update
								groupMember->SendAuxShip();
							}
						}
					}
				}
			}

			//handle damage shield component
			if(current->has_capacitance && current->capacitance != 0)
			{
				if(current->capacitance > damage)
				{
					current->capacitance -= damage;
					damage = 0;
				}
				else
				{
					damage -= current->capacitance;
					current->capacitance = 0;
					DamageShield *remove = current;
					current = current->next;
					RemoveDamageShield(remove->shield_id);
					//TODO: Notify client of loss of buff.
					continue;

				}
			}
			else if(current->has_capacitance && current->capacitance <= 0)
			{
				DamageShield *remove = current;
				current = current->next;
				RemoveDamageShield(remove->shield_id);
				//TODO: Notify client of loss of buff.
				continue;
			}
			current = current->next;
		} 
	}

	//first take away the points
	float shield_level = GetShieldValue();
	if (damage > shield_level)
	{
		float hull_dmg = damage - shield_level;
		if (shield_level > 0.0f)
		{
			RemoveShield(shield_level);
		}

		//if dmg type is chemical, do bonus dmg to hull
		if(damage_type == DAMAGE_CHEMICAL)
		{
			damage += hull_dmg * 0.2f;
			hull_dmg = ceil(hull_dmg * 1.2f); //20% bonus damage
		}

		//calculate in damage control, THIS IS FOR ALL CLASSES NOW VIA THE GROUP RACIAL BONUSES
		//Kingdud's Note: This is normal %, if you want the "effective percent" (like Turbo uses) you need to change this.
		//			if ( (Profession() == PROFESSION_WARRIOR && Race() == RACE_PROGEN) || 
		//				(Profession() == PROFESSION_WARRIOR && Race() == RACE_TERRAN) )
		{
			float dmg_reduce_percent = 1.0f - m_Stats.GetStat(STAT_HULL_DAMAGE_CONTROL);
			if(dmg_reduce_percent < 0)
			{
				dmg_reduce_percent = 0;
			}
			damage -= hull_dmg * (1.0f - dmg_reduce_percent);
			hull_dmg = hull_dmg * dmg_reduce_percent;
		}

		//if dmg type is EMP, take no damage to hull (EMP can't hurt the hull)
		if(damage_type == DAMAGE_EMP)
		{
			if (ObjectType() == OT_PLAYER)
			{
				hull_dmg = hull_dmg * 0.5f; //fix for exploit with EMP only damage from MOBs
			}
			else
			{
				damage -= hull_dmg;
				hull_dmg = 0;
			}
		}
		RemoveHull(hull_dmg, (CMob*)source);
	}
	else
	{
		//bonus damage for plasma
		if(damage_type == DAMAGE_PLASMA)
		{
			damage = ceil(damage * 1.2f); //20% bonus damage
		}
		RemoveShield(damage);
	}

	bool Ignored = false, OnDamage = false;
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&Ignored, &OnDamage, &Ignored))
	{
		if(OnDamage)
		{
			m_CurrentSkill->InterruptSkillOnDamage(damage);
		}
	}

	return damage;
}

void CMob::ResetAbilities()
{
	 if(m_AbilityList[CLOAK])
		 m_AbilityList[CLOAK]->Init(this);
	 if(m_AbilityList[PATCH_HULL])
		 m_AbilityList[PATCH_HULL]->Init(this);
	 if(m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA])
		 m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA]->Init(this);
	 if(m_AbilityList[JUMPSTART])
		 m_AbilityList[JUMPSTART]->Init(this);
	 if(m_AbilityList[REGENERATE_SHIELDS])
		 m_AbilityList[REGENERATE_SHIELDS]->Init(this);
	 if(m_AbilityList[SHIELD_SAP])
		 m_AbilityList[SHIELD_SAP]->Init(this);
	 if(m_AbilityList[POWER_DOWN])
		 m_AbilityList[POWER_DOWN]->Init(this);
	 if(m_AbilityList[REGENERATE_EQUIPMENT])
		 m_AbilityList[REGENERATE_EQUIPMENT]->Init(this);
	 if(m_AbilityList[SUPERCHARGE_SHIELDS])
		 m_AbilityList[SUPERCHARGE_SHIELDS]->Init(this);
	 if(m_AbilityList[BEFRIEND])
		 m_AbilityList[BEFRIEND]->Init(this);
	 if(m_AbilityList[ANGER])
		 m_AbilityList[ANGER]->Init(this);
	 if(m_AbilityList[MASS_FIELD])
		 m_AbilityList[MASS_FIELD]->Init(this);
	 if(m_AbilityList[SELF_DESTRUCT_1])
		 m_AbilityList[SELF_DESTRUCT_1]->Init(this);
	 if(m_AbilityList[SHIELD_RAM])
		 m_AbilityList[SHIELD_RAM]->Init(this);
	 if(m_AbilityList[HACK_SYSTEMS])
		 m_AbilityList[HACK_SYSTEMS]->Init(this);
	 if(m_AbilityList[BIOREPRESS])
		 m_AbilityList[BIOREPRESS]->Init(this);
	 if(m_AbilityList[DAMAGE_TACTICS])
		 m_AbilityList[DAMAGE_TACTICS]->Init(this);
  /* 
	 if(m_AbilityList[COMPULSORY_CONTEMPLATION])
		 m_AbilityList[COMPULSORY_CONTEMPLATION]->Init(this);  //this skill isn't implemented yet
  */
	 if(m_AbilityList[ENVIRONMENTAL_BARRIER])
		 m_AbilityList[ENVIRONMENTAL_BARRIER]->Init(this);
	 if(m_AbilityList[TELEPORT_SELF])
		 m_AbilityList[TELEPORT_SELF]->Init(this);
	 if(m_AbilityList[SHIELD_DRAIN])
		 m_AbilityList[SHIELD_DRAIN]->Init(this);
	 if(m_AbilityList[MINOR_REPULSOR_FIELD])
		 m_AbilityList[MINOR_REPULSOR_FIELD]->Init(this);
	 if(m_AbilityList[INTIMIDATE])
		 m_AbilityList[INTIMIDATE]->Init(this);
	 if(m_AbilityList[ENERGY_DRAIN])
		 m_AbilityList[ENERGY_DRAIN]->Init(this);
	 if(m_AbilityList[PSIONIC_BARRIER])
		 m_AbilityList[PSIONIC_BARRIER]->Init(this);
	 if(m_AbilityList[SUMMON_ENEMY])
		 m_AbilityList[SUMMON_ENEMY]->Init(this);
	 if(m_AbilityList[AFTERBURN])
		 m_AbilityList[AFTERBURN]->Init(this);
	 if(m_AbilityList[REACTOR_BOOST])
		 m_AbilityList[REACTOR_BOOST]->Init(this);
	 if(m_CombatTrance)
		 m_CombatTrance->Init(this);
}

void CMob::DeleteAbilities()
{
	delete m_AbilityList[CLOAK];
	delete m_AbilityList[PATCH_HULL];
	delete m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	delete m_AbilityList[JUMPSTART];
	delete m_AbilityList[REGENERATE_SHIELDS];
	delete m_AbilityList[SHIELD_SAP];
	delete m_AbilityList[POWER_DOWN];
	delete m_AbilityList[REGENERATE_EQUIPMENT];
	delete m_AbilityList[SUPERCHARGE_SHIELDS];
	delete m_AbilityList[BEFRIEND];
	delete m_AbilityList[ANGER];
	delete m_AbilityList[MASS_FIELD];
	delete m_AbilityList[SELF_DESTRUCT_1];
	delete m_AbilityList[SHIELD_RAM];
	delete m_AbilityList[HACK_SYSTEMS];
	delete m_AbilityList[BIOREPRESS];
	delete m_AbilityList[DAMAGE_TACTICS];
	//delete m_AbilityList[COMPULSORY_CONTEMPLATION];  //this skill isn't implemented yet
	delete m_AbilityList[ENVIRONMENTAL_BARRIER];
	delete m_AbilityList[TELEPORT_SELF];
	delete m_AbilityList[SHIELD_DRAIN];
	delete m_AbilityList[MINOR_REPULSOR_FIELD];
	delete m_AbilityList[INTIMIDATE];
	delete m_AbilityList[ENERGY_DRAIN];
	delete m_AbilityList[PSIONIC_BARRIER];
	delete m_AbilityList[SUMMON_ENEMY];
	delete m_AbilityList[AFTERBURN];  //this wasn't being deleted. Memory leak!
	delete m_CombatTrance;
}

void CMob::SetupAbilities()
{
	memset(m_AbilityList, 0, sizeof(m_AbilityList));	

	// Ability List
	/*
	* This may not seem intuative, but what it does is link all SkillRanks into
	*  the array so that they all point to a single copy of the class that handles
	*  them. The reason for doing this is so that we can have the access time of an array
	*  for calling a skill class without having to search through each class to determine
	*  if the class contains the SkillRank we need.
	*
	* For sanity's sake, the class is declared with the name of the lowest-rank skill
	*   for a given ability (EX: For Recharge Shields, the 1st rank of that ability is
	*   called RegerenateShields, so that class is created, and all further ranks of the
	*   skill point to that class.
	*
	* Also, abilities are organized Alphabetically for easy finding.
	*/

	//Cloak Ability
	m_AbilityList[CLOAK] = new ACloak(this); //rank 1
	m_AbilityList[ADVANCED_CLOAK] = m_AbilityList[CLOAK]; //rank 3
	m_AbilityList[COMBAT_CLOAK] = m_AbilityList[CLOAK]; //rank 5
	m_AbilityList[GROUP_STEALTH] = m_AbilityList[CLOAK]; //rank 6
	m_AbilityList[GROUP_CLOAK] = m_AbilityList[CLOAK]; //rank 7

	//Hull Patch ability
	m_AbilityList[PATCH_HULL] = new AHullPatch(this); //rank 1
	m_AbilityList[REPAIR_HULL] = m_AbilityList[PATCH_HULL]; //rank 3
	m_AbilityList[COMBAT_HULL_REPAIR] = m_AbilityList[PATCH_HULL]; //rank 5
	m_AbilityList[AREA_HULL_REPAIR] = m_AbilityList[PATCH_HULL]; //rank 6
	m_AbilityList[IMPROVED_AREA_HULL_REPAIR] = m_AbilityList[PATCH_HULL]; //rank 7

	// Worm Hole Ability
	m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA] = new AWormHole(this);
	m_AbilityList[WORMHOLE_CARPENTER] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	m_AbilityList[WORMHOLE_ENDRIAGO] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	m_AbilityList[WORMHOLE_JUPITER] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	m_AbilityList[WORMHOLE_KAILAASA] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	m_AbilityList[WORMHOLE_SWOOPING_EAGLE] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];
	m_AbilityList[WORMHOLE_VALKYRIE_TWINS] = m_AbilityList[WORMHOLE_ASTEROID_BELT_BETA];

	//Jump Start ability
	m_AbilityList[JUMPSTART] = new AJumpStart(this);

	//Recharge Sheilds ability
	m_AbilityList[REGENERATE_SHIELDS] = new ARechargeShields(this); //rank 1
	m_AbilityList[RECHARGE_SHIELDS] = m_AbilityList[REGENERATE_SHIELDS]; //rank 3
	m_AbilityList[COMBAT_RECHARGE_SHIELDS] = m_AbilityList[REGENERATE_SHIELDS]; //rank 5
	m_AbilityList[AREA_SHIELD_RECHARGE] = m_AbilityList[REGENERATE_SHIELDS]; //rank 6
	m_AbilityList[IMPROVED_AREA_RECHARGE] = m_AbilityList[REGENERATE_SHIELDS]; //rank 7

	//Shield Sap ability, EffectID: 146
	m_AbilityList[SHIELD_SAP] = new AShieldSap(this); //rank 1
	m_AbilityList[SHIELD_TRANSFER] = m_AbilityList[SHIELD_SAP]; //rank 3
	m_AbilityList[GROUP_SAP] = m_AbilityList[SHIELD_SAP]; //rank 5
	m_AbilityList[SAPPING_SPHERE] = m_AbilityList[SHIELD_SAP]; //rank 6
	m_AbilityList[GROUP_SAPPING_SPHERE] = m_AbilityList[SHIELD_SAP]; //rank 7

	//Power Down skill EffectID: 340 
	m_AbilityList[POWER_DOWN] = new APowerDown(this); //rank 1
	m_AbilityList[ADVANCED_POWER_DOWN] = m_AbilityList[POWER_DOWN]; //rank 3
	m_AbilityList[ADVANCED_POWER_DOWN_2] = m_AbilityList[POWER_DOWN]; //rank 5
	m_AbilityList[ADVANCED_POWER_DOWN_3] = m_AbilityList[POWER_DOWN]; //rank 6
	m_AbilityList[ADVANCED_POWER_DOWN_4] = m_AbilityList[POWER_DOWN]; //rank 7

	//Repair Equipement ability, EffectID: 178
	m_AbilityList[REGENERATE_EQUIPMENT] = new ARepairEquipment(this); //rank 1
	m_AbilityList[REPAIR_EQUIPMENT] = m_AbilityList[REGENERATE_EQUIPMENT]; //rank 3
	m_AbilityList[COMBAT_EQUIPMENT_REPAIR] = m_AbilityList[REGENERATE_EQUIPMENT]; //rank 5
	m_AbilityList[AREA_EQUIPMENT_REPAIR] = m_AbilityList[REGENERATE_EQUIPMENT]; //rank 6
	m_AbilityList[IMPROVED_AREA_REPAIR] = m_AbilityList[REGENERATE_EQUIPMENT]; //rank 7
	
	//Shield Charging ability
	m_AbilityList[SUPERCHARGE_SHIELDS] = new AShieldCharging(this); //rank 1
	m_AbilityList[ULTRACHARGE_SHIELDS] = m_AbilityList[SUPERCHARGE_SHIELDS]; //rank 3
	m_AbilityList[SUPERCHARGE_TARGET] = m_AbilityList[SUPERCHARGE_SHIELDS]; //rank 5
	m_AbilityList[ULTRACHARGE_TARGET] = m_AbilityList[SUPERCHARGE_SHIELDS]; //rank 6
	m_AbilityList[MEGACHARGE_SHIELDS] = m_AbilityList[SUPERCHARGE_SHIELDS]; //rank 7

	//Befriend ability, EffectID: 221
	m_AbilityList[BEFRIEND] = new ABefriend(this); //rank 1
	m_AbilityList[IMPROVED_BEFRIEND] = m_AbilityList[BEFRIEND]; //rank 3
	m_AbilityList[ENTRANCE] = m_AbilityList[BEFRIEND]; //rank 5
	m_AbilityList[SOOTHE] = m_AbilityList[BEFRIEND]; //rank 6
	m_AbilityList[AREA_SOOTHE] = m_AbilityList[BEFRIEND]; //rank 7
	
	//Enrage skill, EffectID: 212
	m_AbilityList[ANGER] = new AEnrage(this); //rank 1
	m_AbilityList[CAUSE_AGGRESSION] = m_AbilityList[ANGER]; //rank 3
	m_AbilityList[ENRAGE] = m_AbilityList[ANGER]; //rank 5
	m_AbilityList[ANGER_GROUP] = m_AbilityList[ANGER]; //rank 6
	m_AbilityList[ENRAGE_GROUP] = m_AbilityList[ANGER]; //rank 7	

	//Gravity Link skill, EffectID: 219
	m_AbilityList[MASS_FIELD] = new AGravityLink(this); //rank 1
	m_AbilityList[GRAVITY_FIELD] = m_AbilityList[MASS_FIELD]; //rank 3
	m_AbilityList[IMMOBILIZATION_FIELD] = m_AbilityList[MASS_FIELD]; //rank 5
	m_AbilityList[AREA_MASS_FIELD] = m_AbilityList[MASS_FIELD]; //rank 6
	m_AbilityList[AREA_IMMOBILIZATION_FIELD] = m_AbilityList[MASS_FIELD]; //rank 7
	
	//Self-Destruct Skill, EffectID: 206
	m_AbilityList[SELF_DESTRUCT_1] = new ASelfDestruct(this); //rank 1
	m_AbilityList[SELF_DESTRUCT_2] = m_AbilityList[SELF_DESTRUCT_1]; //rank 3
	m_AbilityList[SELF_DESTRUCT_3] = m_AbilityList[SELF_DESTRUCT_1]; //rank 5
	m_AbilityList[SELF_DESTRUCT_4] = m_AbilityList[SELF_DESTRUCT_1]; //rank 6
	m_AbilityList[SELF_DESTRUCT_5] = m_AbilityList[SELF_DESTRUCT_1]; //rank 7

	//Shield Nova skill, EffectID: 98
	m_AbilityList[SHIELD_RAM] = new AShieldInversion(this); //rank 1 
	m_AbilityList[SHIELD_SPIKE] = m_AbilityList[SHIELD_RAM]; //rank 3
	m_AbilityList[SHIELD_BURN] = m_AbilityList[SHIELD_RAM]; //rank 5
	m_AbilityList[SHIELD_FLARE] = m_AbilityList[SHIELD_RAM]; //rank 6
	m_AbilityList[SHIELD_NOVA] = m_AbilityList[SHIELD_RAM]; //rank 7

	//Hacking skill, , EffectID (see effect.ini for all effect-ids): 193
	m_AbilityList[HACK_SYSTEMS] = new AHacking(this); //rank 1
	m_AbilityList[HACK_WEAPONS] = m_AbilityList[HACK_SYSTEMS]; //rank 3
	m_AbilityList[MULTI_HACK] = m_AbilityList[HACK_SYSTEMS]; //rank 5
	m_AbilityList[AREA_SYSTEM_HACK] = m_AbilityList[HACK_SYSTEMS]; //rank 6
	m_AbilityList[AREA_MULTI_HACK] = m_AbilityList[HACK_SYSTEMS]; //rank 7

	//Biorepression skill
	m_AbilityList[BIOREPRESS] = new ABioRepression(this); //rank 1
	m_AbilityList[BIOSUPPRESS] = m_AbilityList[BIOREPRESS]; //rank 3
	m_AbilityList[BIOREPRESSION_SPHERE] = m_AbilityList[BIOREPRESS]; //rank 5
	m_AbilityList[BIOSUPPRESSION_SPHERE] = m_AbilityList[BIOREPRESS]; //rank 6
	m_AbilityList[BIOCESSATION] = m_AbilityList[BIOREPRESS]; //rank 7
	
	//Rally skill
	m_AbilityList[DAMAGE_TACTICS] = new ARally(this); //rank 1
	m_AbilityList[DEFENSE_TACTICS] = m_AbilityList[DAMAGE_TACTICS]; //rank 3
	m_AbilityList[FIRING_TACTICS] = m_AbilityList[DAMAGE_TACTICS]; //rank 5
	m_AbilityList[STEALTH_TACTICS] = m_AbilityList[DAMAGE_TACTICS]; //rank 7

	//Compulsory Contemplation skill
//	m_AbilityList[COMPULSORY_CONTEMPLATION] = new ACompulsoryContemplation(this); //rank 1

	//Environmental shield skill, EffectID: 216
	m_AbilityList[ENVIRONMENTAL_BARRIER] = new AEnvironmentShield(this); //rank 1
	m_AbilityList[LESSER_ENVIRONMENTAL_SHIELD] = m_AbilityList[ENVIRONMENTAL_BARRIER]; //rank 3
	m_AbilityList[ENVIRONMENTAL_SHIELD] = m_AbilityList[ENVIRONMENTAL_BARRIER]; //rank 5
	m_AbilityList[GREATER_ENVIRONMENTAL_SHIELD] = m_AbilityList[ENVIRONMENTAL_BARRIER]; //rank 6
	m_AbilityList[ULTRA_ENVIRONMENTAL_SHIELD] = m_AbilityList[ENVIRONMENTAL_BARRIER]; //rank 7
	
	//Fold Space skill, EffectID: 202
	m_AbilityList[TELEPORT_SELF] = new AFoldSpace(this); //rank 1
	m_AbilityList[TELEPORT_ENEMY] = m_AbilityList[TELEPORT_SELF]; //rank 3
	m_AbilityList[TELEPORT_FRIEND] = m_AbilityList[TELEPORT_SELF]; //rank 5
	m_AbilityList[DIRECTIONAL_TELEPORT] = m_AbilityList[TELEPORT_SELF]; //rank 5
	m_AbilityList[AREA_TELEPORT] = m_AbilityList[TELEPORT_SELF]; //rank 7

	//Sheild Leech skill
	m_AbilityList[SHIELD_DRAIN] = new AShieldLeech(this); //rank 1
	m_AbilityList[SHIELD_LEECH] = m_AbilityList[SHIELD_DRAIN]; //rank 3
	m_AbilityList[GROUP_LEECH] = m_AbilityList[SHIELD_DRAIN]; //rank 5
	m_AbilityList[SHIELD_LEECHING_SPHERE] = m_AbilityList[SHIELD_DRAIN]; //rank 6
	m_AbilityList[GROUP_LEECHING_SPHERE] = m_AbilityList[SHIELD_DRAIN]; //rank 7

	//Repulsor Field Skill
	m_AbilityList[MINOR_REPULSOR_FIELD] = new ARepulsorField(this); //rank 1
	m_AbilityList[LESSER_REPULSOR_FIELD] = m_AbilityList[MINOR_REPULSOR_FIELD]; //rank 3
	m_AbilityList[REPULSOR_FIELD] = m_AbilityList[MINOR_REPULSOR_FIELD]; //rank 5
	m_AbilityList[GREATER_REPULSOR_FIELD] = m_AbilityList[MINOR_REPULSOR_FIELD]; //rank 6
	m_AbilityList[MAJOR_REPULSOR_FIELD] = m_AbilityList[MINOR_REPULSOR_FIELD]; //rank 7

	//Menace skill, EffectID (scare): 199, , EffectID (intimidate): 198
	m_AbilityList[INTIMIDATE] = new AMenace(this); //rank 1
	m_AbilityList[SCARE] = m_AbilityList[INTIMIDATE]; //rank 3
	m_AbilityList[TERRIFY] = m_AbilityList[INTIMIDATE]; //rank 5
	m_AbilityList[AREA_INTIMIDATE] = m_AbilityList[INTIMIDATE]; //rank 6
	m_AbilityList[AREA_TERRIFY] = m_AbilityList[INTIMIDATE]; //rank 7

	//Energy Leech skill
	m_AbilityList[ENERGY_DRAIN] = new AEnergyLeech(this); //rank 1
	m_AbilityList[ENERGY_LEECH] = m_AbilityList[ENERGY_DRAIN]; //rank 3
	m_AbilityList[RENDER_ENERGY] = m_AbilityList[ENERGY_DRAIN]; //rank 5
	m_AbilityList[ENERGY_LEECHING_SPHERE] = m_AbilityList[ENERGY_DRAIN]; //rank 6
	m_AbilityList[RENDER_ENERGY_SPHERE] = m_AbilityList[ENERGY_DRAIN]; //rank 7

	//Psionic Shield skill, EffectID: 214
	m_AbilityList[PSIONIC_BARRIER] = new APsionicShield(this); //rank 1
	m_AbilityList[LESSER_PSIONIC_SHIELD] = m_AbilityList[PSIONIC_BARRIER]; //rank 3
	m_AbilityList[PSIONIC_SHIELD] = m_AbilityList[PSIONIC_BARRIER]; //rank 5
	m_AbilityList[GREATER_PSIONIC_SHIELD] = m_AbilityList[PSIONIC_BARRIER]; //rank 6
	m_AbilityList[PSIONIC_INVULNERABILITY] = m_AbilityList[PSIONIC_BARRIER]; //rank 7

	//Summon skill
	m_AbilityList[SUMMON_ENEMY] = new ASummon(this); //rank 1
	m_AbilityList[SUMMON_FRIEND] = m_AbilityList[SUMMON_ENEMY]; //rank 3
	m_AbilityList[SUMMON_GROUP] = m_AbilityList[SUMMON_ENEMY]; //rank 5
	m_AbilityList[SUMMON_ENEMY_GROUP] = m_AbilityList[SUMMON_ENEMY]; //rank 6
	m_AbilityList[RETURN_FRIEND] = m_AbilityList[SUMMON_ENEMY]; //rank 7

	//Afterburn
	m_AbilityList[AFTERBURN] = new AAfterburn(this);

	//JT's new Reactor ability
	m_AbilityList[REACTOR_BOOST] = new AReactorOptimisation(this); //rank 1
	m_AbilityList[REACTOR_SURGE] = m_AbilityList[REACTOR_BOOST]; //rank 3
	m_AbilityList[REACTOR_EXTENSION] = m_AbilityList[REACTOR_BOOST]; //rank 5
	m_AbilityList[REACTOR_AUGMENTATION] = m_AbilityList[REACTOR_BOOST]; //rank 6
	m_AbilityList[REACTOR_OPTIMISATION] = m_AbilityList[REACTOR_BOOST]; //rank 7

	m_CombatTrance = new ACombatTrance(this);
}

// This is called on a timer
void CMob::AbilityRemove(int AbilityID, long activation_ID)
{
	__try
	{
		// See if we are in range ID for the ability
		// And we have a ability handeler
		if (AbilityID < MAX_ABILITY_IDS && AbilityID >= 0 && m_AbilityList[AbilityID])
		{
			// If so lets update the ability!
			m_AbilityList[AbilityID]->Update(activation_ID);
		}
	}
	__except(EXCEPTION_EXECUTE_HANDLER)
	{
		LogMessage("skill update %d crashed\n", AbilityID);
	}
}

//this returns true if Player 'player' has clicked on 'this' player
bool CMob::IsClickedBy(Player *player)
{
	return g_PlayerMgr->GetIndex(player, m_ClickedList);
}

void CMob::SetClickedBy(Player *player)
{
	g_PlayerMgr->SetIndex(player, m_ClickedList);
}

void CMob::UnSetClickedBy(Player *player)
{
	g_PlayerMgr->UnSetIndex(player, m_ClickedList);
}