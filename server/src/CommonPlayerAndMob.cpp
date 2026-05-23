
#include "net7.h"
#include "ItemBaseManager.h"
#include "CommonPlayerAndMob.h"

CommonPlayerAndMob::CommonPlayerAndMob(long object_id) : Object (object_id)
{
	ResetCommon();
}

CommonPlayerAndMob::CommonPlayerAndMob(void) : Object (-1)
{
	ResetCommon();
}

CommonPlayerAndMob::~CommonPlayerAndMob(void)
{
}

void CommonPlayerAndMob::ResetCommon()
{
    m_ShieldRecharge = 0;
	m_LastShieldChange = 0;
}

float CommonPlayerAndMob::GetShield()
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

float CommonPlayerAndMob::GetShieldValue()
{
	return GetShield() * GetMaxShield();
}

//DB Shield Recharge stops
void CommonPlayerAndMob::RemoveShield(float ShieldRemoved)
{
    unsigned long CurTick = GetNet7TickCount();
	float myShield = GetShield() - (ShieldRemoved / GetMaxShield());

    if (myShield < 0.0f)
    {
        myShield = 0;
    }

	if (myShield > GetMaxShield())
	{
		myShield = GetMaxShield();
	}

    if (ShieldAux()->GetChangePerTick() != 0)
    {
        ShieldAux()->SetEndTime(0);
        ShieldAux()->SetChangePerTick(0);
    }

	ShieldAux()->SetStartValue(myShield);

    m_LastShieldChange = GetNet7TickCount();
    SendAuxShip();

    //set a recharge delay
    m_ShieldRecharge = CurTick + SHIELD_RECHARGE_RESUME_DELAY;
}

void CommonPlayerAndMob::ShieldUpdate(unsigned long EndTime, float ChangePerTick, float StartValue)
{
	if (GetIsIncapacitated()) ChangePerTick = 0.0f;

    m_LastShieldChange = GetNet7TickCount();
    ShieldAux()->SetEndTime(EndTime);
    ShieldAux()->SetChangePerTick(ChangePerTick);
    ShieldAux()->SetStartValue(StartValue);

    SendAuxShip();
}

void CommonPlayerAndMob::RechargeShield()
{
    float Shield = GetShield();
	float Recharge = BaseShieldRecharge();
	unsigned long RequiredTime = (unsigned long)((1.0f - Shield) / Recharge);

	// Dont regen if Incapacited
	if (!GetIsIncapacitated())
	{
		m_ShieldRecharge = 0;
		ShieldUpdate(GetNet7TickCount() + RequiredTime, Recharge, Shield);
	}
}

void CommonPlayerAndMob::RecalculateShieldRegen()
{
	float MaxShield = m_Stats.GetStat(STAT_SHIELD);
	float RechargeShield = m_Stats.GetStat(STAT_SHIELD_RECHARGE);
	float StartValue = GetShield();
	float ChargeRate = (RechargeShield / MaxShield) / 1000.0f;
	unsigned long EndTime = GetNet7TickCount() + unsigned long((1.0f - StartValue) / ChargeRate);
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

bool CommonPlayerAndMob::QualityCalculator(struct _Item * myItem)
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

			sprintf_s(IInstanceInfo, sizeof(IInstanceInfo), "%d:%4.2f^", FieldID,NewFieldValue);
			strcat_s(InstanceInfo, sizeof(InstanceInfo), IInstanceInfo);
		}
	}

	if (InstanceInfo[0] != 0)
	{
		memcpy(myItem->InstanceInfo, InstanceInfo, sizeof(InstanceInfo));
		return true;
	}

	return false;
}
