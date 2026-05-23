//Equipable.cpp

#include "Equipable.h"
#include "ServerManager.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include "ObjectManager.h"
#include "StaticData.h"

Equipable::Equipable()
{
    m_PlayerID = 0;
    m_ItemBase = 0;
    m_Slot = 0;
    m_ReadyTime = GetNet7TickCount();
    m_UsesAmmo = false;
    m_AuxEquipItem = 0;
    m_AuxAmmoItem = 0;
	m_MaxID = 0;
	m_EEffectID = 0;

    memset(&m_ItemInstance, 0, sizeof(ItemInstance));
    memset(&m_AmmoInstance, 0, sizeof(AmmoInstance));
    memset(&m_TimeNode, 0, sizeof(TimeNode));

	m_AmmoBase = 0;
}

Equipable::~Equipable()
{
}

void Equipable::Init(Player * Owner, int SlotNum)
{
	m_PlayerID = Owner->GetGameIndex();
    m_Slot = SlotNum;

    if (SlotNum == 0)
    {
        m_Type = EQUIP_SHIELD;
    }
    else if (SlotNum == 1)
    {
        m_Type = EQUIP_REACTOR;
    }
    else if (SlotNum == 2)
    {
        m_Type = EQUIP_ENGINE;
    }
    else if (SlotNum >= 3 && SlotNum <= 8)
    {
        m_Type = EQUIP_WEAPON;
    }
    else if (SlotNum >= 9 && SlotNum <= 14)
    {
        m_Type = EQUIP_DEVICE;
    }

	m_AmmoBase = 0;
    m_UsesAmmo = false;
    m_AuxEquipItem = &Owner->ShipIndex()->Inventory.EquipInv.EquipItem[SlotNum];
    m_AuxAmmoItem = &Owner->ShipIndex()->Inventory.AmmoInv.Item[SlotNum];
    m_ReadyTime = m_AuxEquipItem->GetReadyTime();
	m_first_equip = true;
	if (m_AuxAmmoItem)
	{
		m_AuxAmmoItem->m_check = AMMO_TAG;
	}

	// Make sure we are calculating quality on item
	Owner->QualityCalculator(m_AuxEquipItem->GetItemData());
}

/* We need to initialize this class with Aux data */
void Equipable::PullAuxData()
{
    m_ItemBase = g_ItemBaseMgr->GetItem(m_AuxEquipItem->GetItemTemplateID());

    if (!m_ItemBase)
    {
        return;
    }

    m_UsesAmmo = (m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER || m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER);

	m_ItemInstance = m_ItemBase->GetItemInstance(m_AuxEquipItem->GetInstanceInfo());

	if (m_UsesAmmo && VALID_AMMO(m_AuxAmmoItem) && m_AuxAmmoItem->GetItemTemplateID() > 0)
	{
		if (m_AmmoBase = g_ItemBaseMgr->GetItem(m_AuxAmmoItem->GetItemTemplateID()))
		{
			m_AmmoInstance = m_AmmoBase->GetAmmoInstance(m_AuxAmmoItem->GetInstanceInfo());
		}
	}

    //TODO: Once server is stable, this needs to check if ReadyTime < GetNet7TickCount() first
    m_AuxEquipItem->SetReadyTime(GetNet7TickCount());

    /* Only reactors, engines and shields have stats. Other items only have effects */
    //if (m_Slot < 3)
    //{
        SetStats();
    //}

    //AddEffects();
}

bool Equipable::InvalidType(long slot)
{
	if (m_Type < EQUIP_SHIELD || m_Type > EQUIP_DEVICE) 
	{
		Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
		//slot has invalid content - reset this slot
		Init(p, slot);
	}
	return false;
}

/* Checks if an item can be equiped in this slot */
bool Equipable::CanEquip(_Item * NewItem)
{
    /* If this is a weapon/device then we can unequip it */
	if (m_Slot >= 3 && m_Slot <= 14 && NewItem->ItemTemplateID == -1)
    {
		return true;
    }

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	if(p==NULL)
	{
		return false;
	}
	// Make sure we are not warping
	if (p && p->m_WarpDrive)
	{
		p->SendPriorityMessageString("Can't do this while in warp!","MessageLine",2000,4);
		return false;
	}

	ItemBase * myItemBase = g_ItemBaseMgr->GetItem(NewItem->ItemTemplateID);

    /* If we fail to find an itembase - exit */
	if (!myItemBase)
    {
		return false;
    }

    int SubCat = myItemBase->SubCategory();

    /* Now check to see if this is ammo for current item */
    if (SubCat == IB_SUBCATEGORY_AMMO && m_UsesAmmo && !CorrectAmmo(NewItem))
    {
		p->SendPriorityMessageString("The ammo does not fit here","MessageLine",2000,4);
        //printf("CanEquip - Wrong ammo\n");
        return false;
    }

    /* Cannot equip ammo without a launcher */
    if (SubCat == IB_SUBCATEGORY_AMMO && !m_UsesAmmo)
    {
		p->SendPriorityMessageString("Weapon doesn't require ammo","MessageLine",2000,4);
		//p->SendVaMessage("Weapon doesn't use ammo");
        //printf("CanEquip - Ammo with no launcher\n");
        return false;
    }

    /* Now check that the item matches the slot type */
	if ((m_Slot == 0 && SubCat != IB_SUBCATEGORY_SHIELD) ||  // Shield
        (m_Slot == 1 && SubCat != IB_SUBCATEGORY_REACTOR) ||  // Reactor
        (m_Slot == 2 && SubCat != IB_SUBCATEGORY_ENGINE) ||  // Engine
        (m_Slot >= 3 && m_Slot <= 8 && SubCat != IB_SUBCATEGORY_BEAM_WEAPON      && SubCat != IB_SUBCATEGORY_PROJECTILE_LAUNCHER 
									&& SubCat != IB_SUBCATEGORY_MISSILE_LAUNCHER && SubCat != IB_SUBCATEGORY_AMMO) || //Weapon/Ammo
        (m_Slot >= 9 && m_Slot <= 15 && SubCat != IB_SUBCATEGORY_DEVICE))  //Device
    {
		p->SendPriorityMessageString("Item does not fit here","MessageLine",2000,4);
        //printf("CanEquip - Wrong item for slot\n");
		return false;
    }


    AuxSkill * Skills = &(p->PlayerIndex()->RPGInfo.Skills.Skill[0]);

    /* Now check skill requirements */
    if ((SubCat == IB_SUBCATEGORY_BEAM_WEAPON			&& Skills[SKILL_BEAM_WEAPON].GetLevel()			< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_PROJECTILE_LAUNCHER	&& Skills[SKILL_PROJECTILE_WEAPON].GetLevel()	< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_MISSILE_LAUNCHER		&& Skills[SKILL_MISSILE_WEAPON].GetLevel()		< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_DEVICE				&& Skills[SKILL_DEVICE_TECH].GetLevel()			< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_REACTOR				&& Skills[SKILL_REACTOR_TECH].GetLevel()		< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_ENGINE				&& Skills[SKILL_ENGINE_TECH].GetLevel()			< myItemBase->TechLevel()) ||
        (SubCat == IB_SUBCATEGORY_SHIELD				&& Skills[SKILL_SHIELD_TECH].GetLevel()			< myItemBase->TechLevel()))
    {
		p->SendPriorityMessageString("You need more skill to equip this item","MessageLine",2000,4);
        //printf("CanEquip - bad skill\n");
        return false;
    }

    ItemRequirements Req = myItemBase->GetItemRequirements();

    /* Now check for race restrictions */
    if (Req.RaceRestriction & (0x01 << p->Race()))
    {
        //printf("CanEquip - Race restriction\n");
		p->SendPriorityMessageString("Your Race can not equip this item","MessageLine",2000,4);
        return false;
    }

    /* Also check for race lore restrictions */
    if ((p->Race() == 1 && Req.LoreRestriction == 0x02) ||
        (p->Race() == 2 && Req.LoreRestriction == 0x01))
    {
        //printf("CanEquip - Lore restriction\n");
		p->SendPriorityMessageString("Your Lore can not equip this item","MessageLine",2000,4);
        return false;
    }

    /* Now check for profession restrictions */
    if (Req.ProfessionRestriction & (0x01 << p->Profession()))
    {
		p->SendPriorityMessageString("Your profession can not equip this item","MessageLine",2000,4);
        //printf("CanEquip - Profession restriction\n");
        return false;
    }

    /* Now check for level requirements */
    if ((Req.CombatRequirement > p->CombatLevel()) ||
        (Req.ExploreRequirement > p->ExploreLevel()) ||
        (Req.TradeRequirement > p->TradeLevel()) ||
        (Req.OverallRequirement > p->TotalLevel()))
    {
		p->SendPriorityMessageString("You can not equip this item","MessageLine",2000,4);
        //printf("CanEquip - level restriction\n");
        //printf("Combat Req %d Act %d\n",Req.CombatRequirement,p->CombatLevel());
        //printf("Explore Req %d Act %d\n",Req.CombatRequirement,p->ExploreLevel());
        //printf("Trade Req %d Act %d\n",Req.CombatRequirement,p->TradeLevel());
        //printf("Overall Req %d Act %d\n",Req.CombatRequirement,p->TotalLevel());
        return false;
    }

	return true;
}

bool Equipable::CorrectAmmo(_Item * Ammo)
{
    /* If our current item is empty, cant equip ammo */
    if (!m_ItemBase)
    {
        return false;
    }

    /* If our current item does not use ammo, then we cannot equip any */
    if (!m_UsesAmmo)
    {
        return false;
    }

    /* If we are removing ammo and have ammo to remove, return true */
    if (Ammo->ItemTemplateID == -1 && VALID_AMMO(m_AuxAmmoItem) && m_AuxAmmoItem->GetItemTemplateID() != -1)
    {
        return true;
    }

    /* If for some reason the ammo field is null, print an error and exit */
    if (m_ItemInstance.WeaponAmmo == 0)
    {
        LogMessage("Item ID: [%d] uses ammo but has null ammo field\n", m_AuxEquipItem->GetItemTemplateID());
        return false;
    }

	ItemBase * newItemBase = g_ItemBaseMgr->GetItem(Ammo->ItemTemplateID);

    /* If we fail to find an itembase - exit */
	if (!newItemBase)
    {
		return false;
    }
    
    /* Check to see if this is ammo */
    if (newItemBase->SubCategory() != IB_SUBCATEGORY_AMMO)
    {
        return false;
    }

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    /* Now see if the ammo matches */
    if (!strstr(newItemBase->Fields(1)->sData, m_ItemInstance.WeaponAmmo))
    {
		p->SendVaMessage("Wrong ammo type. Trying to install '%s'. Weapon takes '%s'", newItemBase->Fields(1)->sData, m_ItemInstance.WeaponAmmo);
        return false;
    }

	// check the ammo level against the launcher level
	if (m_ItemBase && newItemBase->TechLevel() > m_ItemBase->TechLevel())
	{
		p->SendVaMessage("Ammo too high level for launcher");
		return false;
	}

    return true;
}

_Item *Equipable::GetItem()
{
	if (m_AuxEquipItem)
	{
		return (m_AuxEquipItem->GetItemData());
	}
	else
	{
		return (0);
	}
}

/* This returns the item thats un-equiped as its possible to unequip ammo and leave launcher */
_Item Equipable::Equip(_Item * NewItem)
{
    /* NOTE: This item has already passed equip checks */
    if (NewItem->ItemTemplateID == -2)
    {
        return *NewItem;
    }

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    /* Check to see if we are equiping ammo */
    if (CorrectAmmo(NewItem))
    {
		return EquipAmmo(NewItem);
    }

    /* Unequip item */
    if (NewItem->ItemTemplateID == -1)
    {
        RemoveEffects();
		SetStats(true);		// Remove old Stats

        _Item OldItem = *m_AuxEquipItem->GetItemData();

        m_AuxEquipItem->SetItemData(NewItem);
        m_ItemBase = 0;
        m_AmmoBase = 0;
        m_UsesAmmo = false;

        /* If this was a weapon, remove the Asset */
        if (m_Type == EQUIP_WEAPON)
        {
            p->ShipIndex()->Lego.Attachments.Attachment[m_Slot-3].Clear();
        }

		p->SaveEquipmentChange(m_Slot, NewItem);

        return OldItem;
    }

	/* If we have ammo, it gets removed with the launcher */
	if (m_UsesAmmo && VALID_AMMO(m_AuxAmmoItem) && m_AuxAmmoItem->GetItemTemplateID() > 0)
	{
        p->CargoAddItem(m_AuxAmmoItem->GetData());
        m_AuxAmmoItem->Clear();
        m_AmmoBase = 0;
		p->SaveAmmoChange(m_Slot, m_AuxAmmoItem->GetData());
	}

	// Make sure we are calculating quality on item
	p->QualityCalculator(NewItem);

    ItemBase * NewItemBase = g_ItemBaseMgr->GetItem(NewItem->ItemTemplateID);

    /* Make sure we have an itembase, if not, return the item back */
    if (!NewItemBase)
    {
        LogMessage("Could not find ItemBase for ItemID %d\n",NewItem->ItemTemplateID);
        return *NewItem;
    }

	// make sure we dont overwrite weapons with ammo (that is no longer valid for the weapon)
	if (NewItemBase->SubCategory() == IB_SUBCATEGORY_AMMO)
	{
        p->CargoAddItem(NewItem);
		LogMessage("ERROR: Overwriting ammo %d into weapon slot %d, moved to cargo\n",NewItem->ItemTemplateID,m_AuxEquipItem->GetItemTemplateID());
        return *NewItem;
    }

    /* Begin equip process */
	if (!m_first_equip)
	{
	    RemoveEffects();
		SetStats(true);		// Remove old Stats if not loading
		p->SendAuxShip();
	}

	m_ItemBase = NewItemBase;
    m_ItemInstance = m_ItemBase->GetItemInstance(NewItem->InstanceInfo);

    /* If this is a launcher set the Ammo flag */
    m_UsesAmmo = (m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER || m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER);

    /* For now, make equip time be 10 seconds for each activatable effect, plus weapon reload time, (5 second minimum) */
    float EquipTime;
	if (m_first_equip)
		EquipTime = 5000.0f; // delay to load effects, if this is 0 effects happen before player is visible
	else
	{
		EquipTime = 5000.0f + m_ItemBase->ActivatableCount() * 10000.0f + m_ItemInstance.WeaponReload * 1000.0f;
		EquipTime = p->m_Stats.ModifyValueWithStat(STAT_EQUIPMENT_ENGINEERING,EquipTime);
		if (EquipTime < 0.0f)
			EquipTime = 0.0f;
	}

    _Item OldItem = *m_AuxEquipItem->GetItemData();

    /* Install this item */
    m_AuxEquipItem->SetItemData(NewItem);

    /* If this was a weapon, set the Asset */
    if (m_Type == EQUIP_WEAPON)
    {
        p->ShipIndex()->Lego.Attachments.Attachment[m_Slot-3].Clear();
        p->ShipIndex()->Lego.Attachments.Attachment[m_Slot-3].SetAsset(m_ItemBase->GameBaseAsset());
        p->ShipIndex()->Lego.Attachments.Attachment[m_Slot-3].SetType(2);
        p->ShipIndex()->Lego.Attachments.Attachment[m_Slot-3].SetBoneName(p->ShipIndex()->Inventory.MountBones.GetMountBoneName(m_Slot));

        /* Turn on autofire for this item */
        m_AuxEquipItem->SetItemState(m_AuxEquipItem->GetItemState() | ITEM_STATE_AUTO_FIRE_ENABLE);
    }

    Install((unsigned long)EquipTime);

	p->SaveEquipmentChange(m_Slot, NewItem);

    return OldItem;
}

_Item Equipable::EquipAmmo(_Item * NewAmmo)
{
    _Item OldAmmo = *m_AuxAmmoItem->GetData();

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    if (m_AmmoBase = g_ItemBaseMgr->GetItem(NewAmmo->ItemTemplateID))
    {
        m_AmmoInstance = m_AmmoBase->GetAmmoInstance(NewAmmo->InstanceInfo);
		UpdateRange();
    }

    m_AuxAmmoItem->SetData(NewAmmo);
	m_AuxAmmoItem->m_check = AMMO_TAG;

	p->SaveAmmoChange(m_Slot, NewAmmo);

    if (m_AuxAmmoItem->GetItemTemplateID() < 0)
    {
        AddItemStateFlag(ITEM_STATE_NO_AMMO);
		AddItemStateFlag(ITEM_STATE_NO_TARGETING);
        AddItemStateFlag(ITEM_STATE_DISABLED);
    }
    else if (m_AuxEquipItem->GetItemState() & ITEM_STATE_NO_AMMO)
    {
        //TODO: Check activation requirements
        RemoveItemStateFlag(ITEM_STATE_NO_AMMO);
		RemoveItemStateFlag(ITEM_STATE_NO_TARGETING);

        /* If the item is read, remove the disabled flag aswell */
        if (m_ReadyTime < GetNet7TickCount())
        {
		    RemoveItemStateFlag(ITEM_STATE_DISABLED);
        }
    }

    return OldAmmo;
}

void Equipable::Install(unsigned long InstallDelay)
{
    m_ReadyTime = GetNet7TickCount() + InstallDelay;
    m_AuxEquipItem->SetReadyTime(m_ReadyTime);

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    /* Disable the item since its being installed */
    AddItemStateFlag(ITEM_STATE_DISABLED);

    /* If item has no equip time, install it immediately */
    if (InstallDelay == 0)
    {
        FinishInstall();
    }
    else
    {
        RemoveTimeNode();

		m_TimeNode.player_id = p->GameID();
        m_TimeNode.func = B_ITEM_INSTALL;
        m_TimeNode.i1 = m_Slot;
		SectorManager *sm = p->GetSectorManager();
		if (sm) sm->AddTimedCallPNode(&m_TimeNode, InstallDelay);

        //m_TimeNode = p->m_SectorMgr->AddTimedCall(p, B_ITEM_INSTALL, InstallDelay, 0, m_Slot);
    }
}

void Equipable::Hack(unsigned long InstallDelay)
{

}

void Equipable::FinishInstall(Player *update, int /*Slot*/)
{
	/* Remove our time node */
	RemoveTimeNode();

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

	if (update && update != p)
	{
		m_PlayerID = update->GetGameIndex();
		p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
		LogMessage("--->>> just stopped crash for player %s/%s\n", p->Name(), p->AccountUsername());
	}

	/* Set the ItemState */
	if (m_AuxEquipItem && m_AuxEquipItem->GetItemState() && !(m_AuxEquipItem->GetItemState() & ITEM_STATE_NO_AMMO))
	{
		RemoveItemStateFlag(ITEM_STATE_DISABLED);
	}
	if(m_ItemBase && m_ItemBase->Name() != NULL)
	{
		p->SendVaMessage("%s Installed",m_ItemBase->Name());
	}
	else
	{
		p->SendVaMessage("Equipment Installed");
	}

	/* Add the item's stats, if needed */
	SetStats();

	// only send effects if in space
	if (p->InSpace())
	{
		AddEffects();
	}
	p->SendAuxShip();

	m_first_equip = false;
}

void Equipable::ManualActivate()
{
    /* If we have no item, exit */
	if ((m_AuxEquipItem == NULL) || (m_AuxEquipItem->GetData() == NULL) || (m_AuxEquipItem->GetItemTemplateID() < 0))
    {
        return;
    }

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    /* If they use the item with it auto firing, it cancels autofire */
    if (m_AuxEquipItem->GetItemState() & ITEM_STATE_AUTO_FIRE)
    {
        RemoveItemStateFlag(ITEM_STATE_AUTO_FIRE);
        p->SendAuxShip();
        return;
    }

    /* Check if item is ready */
    if (!ItemReady())
    {
        return;
    }

    Activate();

	p->SendAuxShip();
}

//TODO: Add checks to activate item when enough energy present
void Equipable::AutoActivate()
{
    //LogMessage("Autofire Item: %d\n", m_Slot);
    Activate();
}

void Equipable::Activate()
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
    Object * Target = (0);
	ObjectManager *om = p->GetObjectManager();

    if (om)
    {
        Target = om->GetObjectFromID(p->ShipIndex()->GetTargetGameID());
    }
    else
    {
        return;
    }

	if(m_Type == EQUIP_WEAPON)
	{
        if (!UseWeapon(Target))
			return;
	}
	else if(m_Type == EQUIP_DEVICE || m_Type == EQUIP_REACTOR || m_Type == EQUIP_ENGINE || m_Type == EQUIP_SHIELD)
	{
        if (!UseDevice(Target))
			return;
	}
	else
		return;

    /* Set Ready time and send Aux */
    m_AuxEquipItem->SetReadyTime(m_ReadyTime);

    AddItemStateFlag(ITEM_STATE_DISABLED);

    if (m_ReadyTime < GetNet7TickCount()) //sometimes cooldown didn't happen because readytime was before current tick
    {
        LogMessage("Instant Cooldown\n");
        p->m_Equip[m_Slot].CoolDown();
    }
    else
    {
        RemoveTimeNode();

        m_TimeNode.player_id = p->GameID();
        m_TimeNode.func = B_ITEM_COOLDOWN;
        m_TimeNode.i1 = m_Slot;
        p->GetSectorManager()->AddTimedCallPNode(&m_TimeNode, m_ReadyTime - GetNet7TickCount());
    }
}

void Equipable::CancelAutofire()
{
    RemoveItemStateFlag(ITEM_STATE_AUTO_FIRE);
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
    p->SendAuxShip();
}

void Equipable::CoolDown()
{
    //LogMessage("Cooldown Item: %d\n", m_Slot);

    /* Remove disable flag incase were not autofiring or firing fails */
    RemoveItemStateFlag(ITEM_STATE_DISABLED);
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    /* Check if we are autofiring */
    if (m_AuxEquipItem->GetItemState() & ITEM_STATE_AUTO_FIRE)
    {
        RemoveItemStateFlag(ITEM_STATE_AUTO_FIRE);  //FOR NOW
        AutoActivate();
    }

    p->SendAuxShip();
}

bool Equipable::UseDevice(Object * Target)
{
	// All range, energy effect pulled from item_effect_container

	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	if(p == (NULL))
	{
		return false;
	}
	if (p && p->m_WarpDrive)
	{
		p->SendPriorityMessageString("Can not use while in warp!","MessageLine",2000,4);
		return false;
	}
	if(p && p->ShipIndex()->GetIsIncapacitated())
	{
		p->SendPriorityMessageString("Can not use while incapacitated!","MessageLine",2000,4);
		return false;
	}
	if (p->GetEnergyValue() < m_ItemBase->ActivatableEnergyUse())
	{
		p->SendPriorityMessageString("Not enough energy!","MessageLine",2000,4);
		return false;
	}

	// For Debugging
	int tempvar = m_ItemBase->SubCategory();
	int tempvar2 = m_ItemBase->Category();
	LogDebug("ItemTemplateID\t%ld\n",m_ItemBase->ItemTemplateID());
	LogDebug("ItemType\t%ld\n",m_ItemBase->ItemType());
	LogDebug("UseEffect\t%ld\n",m_ItemBase->UseEffect());
	LogDebug("ActivatableCount\t%ld\n",m_ItemBase->ActivatableCount());
	LogDebug("EquipableCount\t%ld\n",m_ItemBase->EquipableCount());

	int numActiveEffects = m_ItemBase->ActivatableCount();
	if(numActiveEffects < 1)  // Need for shields, engines, reactor with no effects
		return false;

	Player* targetPlayer;
	MOB * targetMOB;

	for (int i=0;i < numActiveEffects;i++)
	{
		ItemEffect *cur_ItemEffect = m_ItemBase->GetActiveEffect(i);
		long EffectID = GetNet7TickCount();
		int tempFlag1 = cur_ItemEffect->Flag1;
		int tempFlag2 = cur_ItemEffect->Flag2;

		targetPlayer = NULL;
		targetMOB = NULL;
		// Flag1 => Target Enemy = 32, Target Friendly 16, Target Groupmember = 64, Target self = 0
		// Flag2 => 1 requires target, 2 possibly requires no target (target OR self, not just self), 0 not set
		if(tempFlag1 == 0) // always use on self
		{
			targetPlayer = p;
		}
		else if(tempFlag1 > 0 && Target) // we have a target, try and use that
		{
			// check target type against flag1
			if ((!(tempFlag1 == 16 && Target->ObjectType() == OT_PLAYER) &&
				!(tempFlag1 == 32 && Target->ObjectType() == OT_MOB) &&
				!(tempFlag1 == 64 && Target->ObjectType() == OT_PLAYER)) ||
				!(tempFlag1 == 16 || tempFlag1 == 32 || tempFlag1 == 64))
			{
				p->SendPriorityMessageString("Invalid target type.","MessageLine",2000,4);
				return false;
			}
			if(tempFlag1 == 32 && Target->ObjectType() == OT_MOB)
			{
				targetMOB = (MOB *)Target;
				//p->SendPriorityMessageString("Mob debuffs not implemented.","MessageLine",2000,4);
				//return false;
			}
			if((tempFlag1 == 16 || tempFlag1 ==64) && Target->ObjectType() == OT_PLAYER)
			{			
				targetPlayer = (Player *)Target;
				if(tempFlag1 == 64 && targetPlayer != p && !g_ServerMgr->m_PlayerMgr.CheckGrouped(targetPlayer,p))
				{
					p->SendPriorityMessageString("Target not in your group.","MessageLine",2000,4);
					return false;
				}
			}
		}
		else if(tempFlag2 == 2) // target not required and we dont have a target, so use self
		{
			targetPlayer = p;
		}

		// have a target?
		if (!(targetPlayer || targetMOB))
		{
			p->SendPriorityMessageString("A target is required.","MessageLine",2000,4);
			return false;
		}

		// Check range
		float range = 0.0f;

		if(m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER)
		{
			range = (float) m_AmmoInstance.WeaponRange;
		}
		else if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_BEAM_WEAPON || 
			     m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER)
		{
			range = (float) m_ItemInstance.WeaponRange;
		}
		else
		{
			 range = (float)m_ItemBase->ActivatableEffectRange();
		}

		if (range < 500.0f) // not set?
			range = 3000.0f;
		if(Target && Target->RangeFrom(p->Position()) > range && tempFlag2!=2)
		{
			p->SendPriorityMessageString("Target out of effect range.","MessageLine",2000,4);		
			return false;
		}

		// Remove energy from player
		float energy = (float)m_ItemBase->ActivatableEnergyUse();
		if (energy < 10.0f)
			energy = 10.0f;
		p->RemoveEnergy(energy);

		Buff ItemBuff;
		memset(&ItemBuff, 0, sizeof(Buff));
		ItemBuff.IsPermanent = false;
		for(int j = 0; j < 5; j++)
		{
			ItemBuff.EffectID[j] = -1;
		}
		ItemBuff.EffectID[0] = m_ItemBase->UseEffect() ? m_ItemBase->UseEffect() : 533; // temp
		if(tempFlag2 == 32)
			ItemBuff.EffectID[0] = m_ItemBase->UseEffect() ? m_ItemBase->UseEffect() : 563; // temp
		strncpy_s(ItemBuff.BuffType, sizeof(ItemBuff.BuffType), cur_ItemEffect->BuffName,128);
		ItemBuff.BuffType[127]='\0';
		// Calculate ready time 
		unsigned long myTime = GetNet7TickCount();
		m_ReadyTime = myTime + (unsigned long)(m_ItemBase->ActivatableRechargeTime() * 1000.0f);

		float Duration = 0.0f;
		int statNum = 0;
		// Variables Loop
		for(int x=0;x<3;x++)
		{
			if (cur_ItemEffect->VarType[x])
			{
				LogDebug("%d\t%s\t%d\t%f\n",x,cur_ItemEffect->VarStats[x],cur_ItemEffect->VarType[x],cur_ItemEffect->DescVar[x]);
				// Get category and value
				if (cur_ItemEffect->VarType[x] != 5)
				{
					strcpy_s(ItemBuff.Stats[statNum].StatName, sizeof(ItemBuff.Stats[statNum].StatName),
						cur_ItemEffect->VarStats[x]);
					ItemBuff.Stats[statNum].StatName[sizeof(ItemBuff.Stats[statNum].StatName)-1] = '\0';
				}
				ItemBuff.Stats[statNum].Value = cur_ItemEffect->DescVar[x];

				// 1 = addition (STAT_BUFF_VALUE)
				// 2 = mult (STAT_BUFF_MULT)
				// 3 = subtraction (STAT_DEBUFF_VALUE)
				// 4 = division (STAT_DEBUFF_MULT)
				// 5 = Duration
				switch(cur_ItemEffect->VarType[x])
				{
				case 1:
					ItemBuff.Stats[statNum].StatType = STAT_BUFF_VALUE;
					statNum++;
					break;
				case 2:
					ItemBuff.Stats[statNum].StatType = STAT_BUFF_MULT;
					ItemBuff.Stats[statNum].Value /= 100;
					statNum++;
					break;
				case 3:
					ItemBuff.Stats[statNum].StatType = STAT_DEBUFF_VALUE;
					statNum++;
					break;
				case 4:
					ItemBuff.Stats[statNum].StatType = STAT_DEBUFF_MULT;
					ItemBuff.Stats[statNum].Value /= 100;
					statNum++;
					break;
				case 5:
					Duration = 1000.0f * cur_ItemEffect->DescVar[x];
					break;
				}
			}
		}

		// Constants Loop
		for(int x=0;x<2;x++)
		{
			if (cur_ItemEffect->ConstType[x])
			{
				LogDebug("%d\t%s\t%d\t%f\n",x,cur_ItemEffect->ConstStats[x],cur_ItemEffect->ConstType[x],cur_ItemEffect->ConstValue[x]);
				if (cur_ItemEffect->ConstType[x] != 5)
				{
					strcpy_s(ItemBuff.Stats[statNum].StatName, sizeof(ItemBuff.Stats[statNum].StatName), 
						cur_ItemEffect->ConstStats[x]);
					ItemBuff.Stats[statNum].StatName[sizeof(ItemBuff.Stats[statNum].StatName)-1] = '\0';
				}
				ItemBuff.Stats[statNum].Value = cur_ItemEffect->ConstValue[x];
				switch(cur_ItemEffect->ConstType[x])
				{
				case 1:
					ItemBuff.Stats[statNum].StatType = STAT_BUFF_VALUE;
					statNum++;
					break;
				case 2:
					ItemBuff.Stats[statNum].StatType = STAT_BUFF_MULT;
					ItemBuff.Stats[statNum].Value /= 100;
					statNum++;
					break;
				case 3:
					ItemBuff.Stats[statNum].StatType = STAT_DEBUFF_VALUE;
					statNum++;
					break;
				case 4:
					ItemBuff.Stats[statNum].StatType = STAT_DEBUFF_MULT;
					ItemBuff.Stats[statNum].Value /= 100;
					statNum++;
					break;
				case 5:
					Duration = 1000.0f * cur_ItemEffect->ConstValue[x];
					break;
				}
			}
		}

		// default to 10 seconds if the duration is missing (it shouldnt be!)
		if (Duration < 1000.0f)
			Duration = 10000.0f;
		ItemBuff.ExpireTime = myTime + (long)Duration;

		// create a beam to show buffing another player
		if (targetPlayer != p && targetPlayer)
		{
			ObjectToObjectEffect ItemBuffEffect;
			memset(&ItemBuffEffect, 0, sizeof(ItemBuffEffect));		// Zero out memory
			ItemBuffEffect.Bitmask = 3;
			ItemBuffEffect.TimeStamp = EffectID+1;
			ItemBuffEffect.EffectID = EffectID+1;
			ItemBuffEffect.EffectDescID = m_ItemBase->UseEffect() ? m_ItemBase->UseEffect() : 668; // default to a beam if not present
			ItemBuffEffect.GameID = p->GameID();
			ItemBuffEffect.TargetID = targetPlayer->GameID();
			p->SendObjectToObjectEffectRL(&ItemBuffEffect);
		}

		// Apply buff to target player
		// NOTE: RemoveAndAdd seems broken.. this works for now
		// Should these be checking again name not Buff_name from item_effect_base?
		// i.e. Buff_name just for client display and name used for checking stack/overwrite?
		char *buffName = ItemBuff.BuffType;
		if(targetPlayer)
		{
			// special case active effect
			if (strcmp("Shunt Shields (Instant)",cur_ItemEffect->Description)==0)
			{
				float shield_lost    = targetPlayer->m_Stats.GetStat(STAT_SHIELD_RECHARGE) * ItemBuff.Stats[0].Value;
				float reactor_gained = targetPlayer->m_Stats.GetStat(STAT_ENERGY_RECHARGE) * ItemBuff.Stats[1].Value;
				targetPlayer->RemoveShield(shield_lost);
				targetPlayer->RemoveEnergy(-reactor_gained);
			}
			else
			{
				if (targetPlayer->m_Buffs.FindBuff(buffName))
					targetPlayer->m_Buffs.RemoveBuff(buffName);
				targetPlayer->m_Buffs.AddBuff(&ItemBuff);
			}
		}
		if(targetMOB)
		{
			if(buffName[0] != '\0')
			{
				 if(targetMOB->m_Buffs.FindBuff(buffName))
					targetMOB->m_Buffs.RemoveBuff(buffName);
				 //p->SendVaMessage("Debuffing mob with %s.",ItemBuff.BuffType);
			}
			else
			{
				p->SendVaMessage("This item's debuff has no name, please report. Item #%d",this->GetItemBase()->ItemTemplateID());
			}
			targetMOB->m_Buffs.AddBuff(&ItemBuff);
			// Add hate for mob debuff
			int damage_level = 100 * m_ItemBase->TechLevel() * m_ItemBase->TechLevel();
			targetMOB->AddHate(p->GameID(), damage_level);
		}
	}

	return true;
}




bool Equipable::UseWeapon(Object * Target)
{
	bool Ignored = false, OnAction = false;
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

    if (m_ItemBase->SubCategory() == 99) // TODO: check for this weapon properly
    {
        if (p->FireEnergyCannon(&m_ItemInstance))
        {
            m_ReadyTime = GetNet7TickCount() + (unsigned long)(m_ItemInstance.WeaponReload * 1000.0f);
            return true;
        }
        else
        {
            return false;
        }
    }

	// Make sure we are not warping
	if (p && p->m_WarpDrive)
	{
		p->SendPriorityMessageString("Can not use while in warp!","MessageLine",2000,4);
		return false;
	}

    if (Target && Target->ObjectType() == OT_MOB && !p->ShipIndex()->GetIsIncapacitated())
    {	
        if (!CheckRange(Target))
        {
			p->SendPriorityMessageString("Out of weapon range","MessageLine",2000,4);
            return false;
        }

        if (!CheckOrientation(Target))
        {
			p->SendPriorityMessageString("You must face target","MessageLine",2000,4);
            return false;
        }

		// check for weapons with an activated effect (eg Hellbore Missile Launcher)
		if (m_ItemBase->ActivatableCount())
			UseDevice(Target);

		/* Use the energy (factor in energy conservation) */
		float conservation = 1.0f * p->m_Stats.ModifyValueWithStat(STAT_WEAPON_ENERGY_CONSERVATION,1.0f);
		switch (m_ItemBase->SubCategory())
		{
		case IB_SUBCATEGORY_BEAM_WEAPON:
			conservation *= p->m_Stats.ModifyValueWithStat(STAT_BEAM_ENERGY_CONSERVATION,1.0f);
			break;
		case IB_SUBCATEGORY_PROJECTILE_LAUNCHER:
			conservation *= p->m_Stats.ModifyValueWithStat(STAT_PROJECTILE_ENERGY_CONSERVATION,1.0f);
			break;
		case IB_SUBCATEGORY_MISSILE_LAUNCHER:
			conservation *= p->m_Stats.ModifyValueWithStat(STAT_MISSILE_ENERGY_CONSERVATION,1.0f);
			break;
		default:
			LogMessage("ERROR: dodgy ItemBase %s in UseWeapon\n",m_ItemBase->Name());
			return false;
		}

        if (p->GetEnergyValue() < (m_ItemInstance.EnergyUse * conservation))
        {
			p->SendPriorityMessageString("Not enough energy!","MessageLine",2000,4);
	        return false;
        }

		// Make sure we have ammo
		if ((m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER || m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER))
		{
			if (m_AuxAmmoItem->GetItemTemplateID() < 0)
			{
				AddItemStateFlag(ITEM_STATE_NO_AMMO);
				AddItemStateFlag(ITEM_STATE_NO_TARGETING);
				AddItemStateFlag(ITEM_STATE_DISABLED);
				p->SendPriorityMessageString("Out of ammo","MessageLine",2000,4);
				return false;
			}
		}

        //see if we are prospecting, if so, cancel prospect.
	    p->AbortProspecting(true, false);

		//interrupt any skills that need interrupting
		if(p->m_CurrentSkill && p->m_CurrentSkill->SkillInterruptable(&Ignored, &Ignored, &OnAction))
		{
			if(OnAction)
			{
				p->m_CurrentSkill->InterruptSkillOnAction(SHOOTING);
			}
		}
        
        p->RemoveEnergy(m_ItemInstance.EnergyUse * conservation);

        /*If autofire is on item, autofire */
        if (m_AuxEquipItem->GetItemState() & ITEM_STATE_AUTO_FIRE_ENABLE)
        {
            AddItemStateFlag(ITEM_STATE_AUTO_FIRE);
        }

		/* Calculate ready time (factor in turbo)*/
		unsigned long myTime = GetNet7TickCount();
		// Currently coding so that Delay = Base * (1-x) where x capped at 0.5
		// Should plan to have stacking of some extent here for explorer benefit and so hard to get items (shield etc are useful)
		float turboAmount = p->m_Stats.ModifyValueWithStat(STAT_WEAPON_TURBO,100.0f)-100.0f;
		switch (m_ItemBase->SubCategory())
		{
		case IB_SUBCATEGORY_BEAM_WEAPON:
			turboAmount += p->m_Stats.ModifyValueWithStat(STAT_BEAM_TURBO,100.0f)-100.0f;
			break;
		case IB_SUBCATEGORY_PROJECTILE_LAUNCHER:
			turboAmount += p->m_Stats.ModifyValueWithStat(STAT_PROJECTILE_TURBO,100.0f)-100.0f;
			break;
		case IB_SUBCATEGORY_MISSILE_LAUNCHER:
			turboAmount += p->m_Stats.ModifyValueWithStat(STAT_MISSILE_TURBO,100.0f)-100.0f;
			break;
		}
		// cap to double firing rate
		if (turboAmount > 50.0f)
			turboAmount = 50.0f;
		m_ReadyTime = myTime + (unsigned long)(m_ItemInstance.WeaponReload * 1000.0f * (1.0f-turboAmount/100));

		int AmmoShots = m_ItemBase->Fields(22)->iData;
		m_Target = p->ShipIndex()->GetTargetGameID();

		// Calculate delay
		long Delay = (int)(m_ItemInstance.WeaponReload/(float)AmmoShots/2.0f) * 1000;

		if (Delay > 400)
		{
			Delay = 400;
		}

		// Send out # of ammo used
		if (m_ItemBase->SubCategory() != IB_SUBCATEGORY_BEAM_WEAPON)
		{
			if (AmmoShots > 1)
			{
				// stop dodgy info filling up all the timeslots
				if (AmmoShots > 9)
					AmmoShots = 9;
				// Calculate delay
				ShootAmmo(Target->GameID(),AmmoShots);			// No delay for first one
				/*
				for(int x=1;x<AmmoShots;x++)
				{
					// Have it call a timer to shoot off each peice of ammo
					p->GetSectorManager()->AddTimedCall(p, B_SHOOT_AMMO,(long)(Delay * (float) x), NULL, m_Slot, Target->GameID());
				}
				*/
			}
			else
			{
				// No need for timer on single ammo
				ShootAmmo(Target->GameID(),1);
			}
		}
		else
		{
			// No need for timer on beams
			ShootAmmo(Target->GameID(),1);
		}

        return true;
    }
    else
    {
        //LogMessage("Weapon use failed\n");
        return false;
    }
}

float Equipable::DamageMult(float Damage)
{
	float CalcDamage = 0.0f;
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

	// Calculate Damage
	if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_BEAM_WEAPON)
	{
		CalcDamage = (float) (Damage * (1.0 + p->m_Stats.GetStatType(STAT_BEAM_DAMAGE, STAT_BUFF_MULT)));
		CalcDamage += p->m_Stats.GetStatType(STAT_BEAM_DAMAGE, STAT_BUFF_VALUE);
	}
	else if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER)
	{
		CalcDamage = (float) (Damage * (1.0 + p->m_Stats.GetStatType(STAT_PROJECTILES_DAMAGE, STAT_BUFF_MULT)));
		CalcDamage += p->m_Stats.GetStatType(STAT_PROJECTILES_DAMAGE, STAT_BUFF_VALUE);
	}
	else
	{
		CalcDamage = (float) (Damage * (1.0 + p->m_Stats.GetStatType(STAT_MISSILE_DAMAGE, STAT_BUFF_MULT)));
		CalcDamage += p->m_Stats.GetStatType(STAT_MISSILE_DAMAGE, STAT_BUFF_VALUE);
	}

	return CalcDamage;
}

void Equipable::ShootAmmo(int TargetID, unsigned int quantity)
{
	// Make sure all pointers are not null
	ObjectManager *om = (0);
	SectorManager *sm = (0);
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

	if (p) om = p->GetObjectManager();
	if (p) sm = p->GetSectorManager();
	if (!m_ItemBase || !p || !om)
	{
		return;
	}

	Object * Target = om->GetObjectFromID(TargetID);

	if (!Target)
	{
		return;
	}

    unsigned long myTime = GetNet7TickCount();

    /* Damage delay is 0 for beams and 1sec/k for launchers */
    u16 DamageDelay = 0;

    if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER)
    {
        DamageDelay = u16(Target->RangeFrom(p->Position()) * 1.5f); 
    }
    else if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER)
    {
        DamageDelay = u16(Target->RangeFrom(p->Position()) * 2.0f); 
    }

	// how much damage done?
	bool critical = false;
	float Damage;
	if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_BEAM_WEAPON)
		Damage = p->CalcDamage(m_ItemInstance.WeaponDamage, m_ItemBase->SubCategory(), &critical, Target->Level());
	else
		Damage = p->CalcDamage(m_AmmoInstance.WeaponDamage, m_ItemBase->SubCategory(), &critical, Target->Level());

    /* Activate effect */
    ObjectToObjectEffect OBTOBE;
	memset(&OBTOBE,0,sizeof(ObjectToObjectEffect));
    OBTOBE.Bitmask = 0x07;
    OBTOBE.GameID = p->GameID();
    OBTOBE.TargetID = m_Target;

	// Use ammo effect if it needs ammo
	if ((m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER || m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER) && m_AmmoBase)
    {
		OBTOBE.EffectDescID = m_AmmoBase->UseEffect();
    }
	else
    {
		OBTOBE.EffectDescID = m_ItemBase->UseEffect();
    }
	// fix hellbore torpedo
	if (m_ItemBase->ItemTemplateID() == 2836)
	{
		OBTOBE.Bitmask |= 0x80;
		OBTOBE.Scale = 10.0f;
		DamageDelay = DamageDelay / 3 * 4;
	}
	// show a miss visually
	if (Damage < 0.5f)
	{
		OBTOBE.Bitmask |= 0x40;
		OBTOBE.TargetOffset[0] = (100.0f + (float)(rand()%400)) * (rand()%3-1);
		OBTOBE.TargetOffset[1] = (100.0f + (float)(rand()%400)) * (rand()%3-1);
		OBTOBE.TargetOffset[2] = (100.0f + (float)(rand()%400)) * (rand()%3-1);
	}

    OBTOBE.Message = p->ShipIndex()->Inventory.MountBones.GetMountBoneName(m_Slot);
    OBTOBE.TimeStamp = myTime;
    OBTOBE.Duration = DamageDelay;
	OBTOBE.EffectID = myTime;

	p->SendObjectToObjectEffectRL(&OBTOBE, true);

	// now do the damage (now for beams, later for ammo)
	if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_BEAM_WEAPON)
    {
		if(m_ItemInstance.WeaponDamageType == DAMAGE_PLASMA || m_ItemInstance.WeaponDamageType == DAMAGE_CHEMICAL)
		{
			Target->DamageMOB(p->GameID(), m_ItemInstance.WeaponDamageType, (Damage/6.0f)*quantity, critical ? 3 : 0);
			for(int i = 1; i < 6; i++)
			{
				sm->AddTimedCall(0, B_MOB_DAMAGE, DamageDelay+(i*1000), Target, p->GameID(), m_ItemInstance.WeaponDamageType, critical ? 3 : 0, 0, 0, (Damage/6.0f)*quantity);		
			}
		}
		else
		{
			Target->DamageMOB(p->GameID(), m_ItemInstance.WeaponDamageType, Damage, critical ? 3 : 0);
		}
    }
	else if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER || m_ItemBase->SubCategory() == IB_SUBCATEGORY_MISSILE_LAUNCHER) 
	{
		if(m_AmmoInstance.WeaponDamageType == DAMAGE_PLASMA || 
			m_AmmoInstance.WeaponDamageType == DAMAGE_CHEMICAL)
		{
			for(int i = 0; i < 6; i++)
			{
				sm->AddTimedCall(0, B_MOB_DAMAGE, DamageDelay+(i*1000), Target, p->GameID(), m_AmmoInstance.WeaponDamageType, critical ? 3 : 0, 0, 0, (Damage/6.0f)*quantity);		
			}
		}
		else
		{
				sm->AddTimedCall(0, B_MOB_DAMAGE, DamageDelay, Target, p->GameID(), m_AmmoInstance.WeaponDamageType, critical ? 3 : 0, 0, 0, Damage*quantity);
		}

		u32 StackCount = m_AuxAmmoItem->GetStackCount();

		if (StackCount < quantity)
		{
			if (!Reload(quantity))
			{
				AddItemStateFlag(ITEM_STATE_NO_AMMO);
				AddItemStateFlag(ITEM_STATE_NO_TARGETING);
				AddItemStateFlag(ITEM_STATE_DISABLED);
				RemoveItemStateFlag(ITEM_STATE_AUTO_FIRE);
				m_AuxAmmoItem->Clear();
				m_AmmoBase = 0;
			}
		}
		else
		{
			m_AuxAmmoItem->SetStackCount(StackCount - quantity);
		}
	}
}

/* Checks to see if we are within range of the target */
bool Equipable::CheckRange(Object * Target)
{
	float TargetSize;
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	// Get target size
	if (Target->BaseAsset() != 0)
	{
		TargetSize = g_ServerMgr->m_CBassetList.GetRadius(Target->BaseAsset()) * Target->Scale();
	}
	else
	{
		TargetSize = 0.0f;
	}

	return (Target->RangeFrom(p->Position()) <= (m_Range + TargetSize));
}

/* Checks to see if we have the correct orientation relative to the target */
bool Equipable::CheckOrientation(Object * Target)
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
    if (m_ItemBase->SubCategory() == IB_SUBCATEGORY_BEAM_WEAPON || m_ItemBase->SubCategory() == IB_SUBCATEGORY_PROJECTILE_LAUNCHER)
    {
        return (fabsf(p->GetAngleTo(Target->Position())) < (PI/4.5f));
    }

    return true;
}

/* We are out of ammo, reload the launcher with a stack of ammo that will allow at least 1 shot*/
bool Equipable::Reload(unsigned int quantity)
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
    ItemBase * Ammo = 0;
	_Item ammoTemp;
	ammoTemp.StackCount = 0;

	for(u32 i=0; i<p->ShipIndex()->Inventory.GetCargoSpace(); i++)
	{
		_Item	 * this_item = p->ShipIndex()->Inventory.CargoInv.Item[i].GetData();
		ItemBase * newItemBase = g_ItemBaseMgr->GetItem(this_item->ItemTemplateID);
		
        /* If this is ammo that matches the launcher */
		if (newItemBase && newItemBase->SubCategory() == IB_SUBCATEGORY_AMMO && CorrectAmmo(this_item) && this_item->StackCount >= quantity)
		{
			if(m_AuxAmmoItem->GetStackCount() > 0)
			{
				memcpy(&ammoTemp,m_AuxAmmoItem->GetData(),sizeof(_Item));
			}

			m_AuxAmmoItem->SetData(this_item);
			p->ShipIndex()->Inventory.CargoInv.Item[i].SetData(&g_ItemBaseMgr->EmptyItem);

			if(ammoTemp.StackCount > 0)
			{
				p->CargoAddItem(&ammoTemp);
			}

			p->SaveInventoryChange(i);
			p->SaveAmmoChange(m_Slot, this_item);
			return true;
		}
	}

	return false;
}

//This is for the various effects associated with the item
void Equipable::AddEffects()
{
	ItemBase * myItemBase = g_ItemBaseMgr->GetItem(m_AuxEquipItem->GetItemTemplateID());

	if (myItemBase && myItemBase->EquipEffect() > 0)
	{
		// Add Equip Effect
		ObjectEffect Effect;
		Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	    
		Effect.Bitmask = 0x07;
		Effect.EffectDescID = myItemBase->EquipEffect();
		Effect.GameID = p->GameID();
		Effect.Duration = 0;
		Effect.TimeStamp = GetNet7TickCount();

		m_EEffectID = p->m_Effects.AddEffect(&Effect);
		// ----
	}

    /*
	if (m_ItemBase->m_EffectsEquip > 0)
	{
		// Display Equip Effect 
		ObjectEffect OBTOBE;
				
		OBTOBE.Bitmask = 0x07;
		OBTOBE.GameID = p->GameID();
		OBTOBE.EffectDescID = m_ItemBase->m_EffectsEquip;
		OBTOBE.EffectID = GetNet7TickCount();
		OBTOBE.TimeStamp = GetNet7TickCount();
		OBTOBE.Duration = 1750;
		
        if (p->ConnectionAvailable())
        {
            p->Connection()->SendObjectEffect(p->GameID(), &OBTOBE, TRUE);	// Sector Wide
        }
	}
    */
}

void Equipable::RemoveEffects()
{
	// Remove effect from player
	if (m_EEffectID > 0)
	{
		Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
		p->m_Effects.RemoveEffect(m_EEffectID);
		m_EEffectID = 0;
	}
}

void Equipable::UpdateRange()
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	float Range_Bonus = 1;

	if (m_Type == EQUIP_WEAPON && m_ItemBase && m_ItemBase->SubCategory())
	{
		switch(m_ItemBase->SubCategory())
		{
			case IB_SUBCATEGORY_BEAM_WEAPON:		// Beam
				Range_Bonus = 1.0f + p->m_Stats.GetStatType(STAT_BEAM_RANGE, STAT_BUFF_MULT);
				m_Range = m_ItemInstance.WeaponRange * Range_Bonus + p->m_Stats.GetStatType(STAT_BEAM_RANGE, STAT_BUFF_VALUE);
				break;

			case IB_SUBCATEGORY_PROJECTILE_LAUNCHER:		// Projectile
				Range_Bonus = 1.0f + p->m_Stats.GetStatType(STAT_PROJECTILES_RANGE, STAT_BUFF_MULT);
				m_Range = m_ItemInstance.WeaponRange * Range_Bonus + p->m_Stats.GetStatType(STAT_PROJECTILES_RANGE, STAT_BUFF_VALUE);
				break;

			case IB_SUBCATEGORY_MISSILE_LAUNCHER:		// Missiles
				Range_Bonus = 1.0f + p->m_Stats.GetStatType(STAT_MISSILE_RANGE, STAT_BUFF_MULT);
				m_Range = m_AmmoInstance.WeaponRange * Range_Bonus + p->m_Stats.GetStatType(STAT_MISSILE_RANGE, STAT_BUFF_VALUE);
				break;
		}

		if (Range_Bonus < 1.0f)
		{
			Range_Bonus = 1.0f;
		}

		//printf("WeaponRange: %f\n", m_Range);
		if (m_AuxEquipItem) m_AuxEquipItem->SetTargetRange(m_Range);
	}
}

//This is for item stats that are part of the item
void Equipable::SetStats(bool Remove)
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	int RemoveStat;

	if (Remove)
		RemoveStat = -1;
	else
		RemoveStat = 1;

	//see if player ptr is valid
	if (p && !IS_PLAYER(p->GameID()))
	{
		return; //something has gone wrong in Equipable object
	}

	if (Remove)
	{
		for(int ID=0;ID<m_MaxID;ID++)
		{
			// Loop and remove all the Stats from the ship
			p->m_Stats.DelStat(m_StatIDs[ID]);
		}

		m_MaxID = 0;

		/* If this is an empty item, disable the slot */
		if (m_AuxEquipItem->GetItemTemplateID() < 0)
		{
			AddItemStateFlag(ITEM_STATE_DISABLED);
		}
		//return;
		// perform other type specific removal tasks
	}


    switch(m_Type)
    {
    case EQUIP_WEAPON:
        /* We dont have ammot at this point anyways */
		if (!Remove)
			UpdateRange();

        /* Removes all of the itemstate flags */
        m_AuxEquipItem->SetItemState(m_AuxEquipItem->GetItemState() & 0xFFFFFF00);

        if (m_UsesAmmo && m_AuxAmmoItem->GetItemTemplateID() < 0)
        {
            AddItemStateFlag(ITEM_STATE_NO_AMMO);
			AddItemStateFlag(ITEM_STATE_NO_TARGETING);
		}

        break;

    case EQUIP_DEVICE:
		EquipDevice(!Remove);

        m_AuxEquipItem->SetTargetRange((float)m_ItemInstance.EffectRange);
        break;

    case EQUIP_SHIELD:
        if (p) {
			// Dont remove stats from the Stats YET
			if (!Remove)
			{
				// Set BaseValue Stats
				m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_SHIELD, m_ItemInstance.ShieldCap, "ITEM_VALUE");
				m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_SHIELD_RECHARGE, m_ItemInstance.ShieldRecharge, "ITEM_VALUE");
				// ----

				float MaxShield = p->m_Stats.GetStat(STAT_SHIELD);
				float RechargeShield = p->m_Stats.GetStat(STAT_SHIELD_RECHARGE);
				float StartValue = p->GetShield();
				p->ShipIndex()->SetMaxShield(MaxShield);
				
				if (!m_first_equip)
					StartValue = 0.0f; // whats this for exactly?

				if (StartValue > 1.0f)
				{
					StartValue = 1.0f;
				}

    			float ChargeRate = (RechargeShield / MaxShield) / 1000.0f;
				unsigned long EndTime = GetNet7TickCount() + (unsigned long)((1.0f - StartValue) / ChargeRate);

				p->ShieldUpdate(EndTime, ChargeRate, StartValue);

				m_AuxEquipItem->SetTargetRange((float)m_ItemInstance.EffectRange);
			}
			else
			{
				p->ShipIndex()->SetMaxShield(0);
				p->ShieldUpdate(0, 0, 0);
			}
        }
        break;

    case EQUIP_REACTOR:
        {
			// Dont remove stats from the Stats YET
			if (!Remove)
			{
				// Set BaseValue Stats
				m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_ENERGY, m_ItemInstance.ReactorCap, "ITEM_VALUE");
				m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_ENERGY_RECHARGE, m_ItemInstance.ReactorRecharge, "ITEM_VALUE");
				// ----

				float MaxEnergy = p->m_Stats.GetStat(STAT_ENERGY);
				float RechargeEnergy = p->m_Stats.GetStat(STAT_ENERGY_RECHARGE);
				float StartValue = p->GetEnergy();
				p->ShipIndex()->SetMaxEnergy(MaxEnergy);
						
				if (StartValue > 1.0f)
				{
					StartValue = 1.0f;
				}

				float ChargeRate = (RechargeEnergy / MaxEnergy) / 1000.0f;
				unsigned long EndTime = GetNet7TickCount() + (unsigned long)((1.0f - StartValue) / ChargeRate);

				p->EnergyUpdate(EndTime, ChargeRate, StartValue);

				m_AuxEquipItem->SetTargetRange((float)m_ItemInstance.EffectRange);
			}
			else
			{
				p->ShipIndex()->SetMaxEnergy(0);
				p->EnergyUpdate(0, 0, 0);
			}
        }
        break;

    case EQUIP_ENGINE:
		// Dont remove stats from the Stats YET
		if (!Remove)
		{
			// Set BaseValue Stats
			m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_IMPULSE, (float)m_ItemInstance.EngineSpeed, "BASE_SHIP_VALUE");
			m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_WARP, (float)m_ItemInstance.EngineWarpSpeed, "BASE_SHIP_VALUE");
			m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(STAT_BASE_VALUE, STAT_SIGNATURE,(float)m_ItemInstance.EngineSignature + BaseVisableRange[p->ClassIndex()], "BASE_SHIP_VALUE");

			p->ShipIndex()->CurrentStats.SetVisibility((s32)p->m_Stats.GetStat(STAT_SIGNATURE));
			p->ShipIndex()->CurrentStats.SetWarpSpeed((s32)p->m_Stats.GetStat(STAT_WARP));

			p->AdjustAndSetSpeeds(false,false);

			m_AuxEquipItem->SetTargetRange((float)m_ItemInstance.EffectRange);
		}
		else
			p->AdjustAndSetSpeeds(false,true);

        break;
    }

	EquipEffects(RemoveStat);

	/* If this is an empty item, disable the slot */
    if (m_AuxEquipItem->GetItemTemplateID() < 0)
    {
        AddItemStateFlag(ITEM_STATE_DISABLED);
    }
}

void Equipable::EquipEffects(int RemoveStat)
{
	if (!m_ItemBase)
		return;

	ItemBaseData * ItemBaseD = m_ItemBase->Data();
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);

	if(!p)
		return;

	if (!ItemBaseD)
		return;

	if (ItemBaseD->EquipableEffects.Count > 6)
		return;

	bool Remove = (RemoveStat == -1);

	if (Remove)
		return;

	for(int Effect=0;Effect<ItemBaseD->EquipableEffects.Count;Effect++)
	{
		ItemEffect * EffectData = m_ItemBase->GetEquipEffect(Effect);
		// Load all varable data
		for(int StatID=0;StatID<3;StatID++)
		{
			char *StatName	= EffectData->VarStats[StatID];
			int VarType		= EffectData->VarType[StatID];
			float VarData	= EffectData->DescVar[StatID];

			// Add value if it is used
			if (VarType != 0)
			{
				if (VarType == 5)		// See if we are using a duration in an eqipable!
				{
					// This should not happen!
					LogMessage("Warning! Buff: %s is Equipable using duration!", EffectData->BuffName);
					break;
				}
				else
				{
					// Convert to percentage
					if (VarType == STAT_BUFF_MULT || VarType == STAT_DEBUFF_MULT)
					{
						VarData = VarData / 100.0f;
					}
					m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(VarType, StatName, VarData, "ITEM_EFFECTS");
					p->m_Stats.UpdateAux(StatName);		// Update Aux data
				}
			}
		}
		// Load all Constant Data
		for(int StatID=0;StatID<2;StatID++)
		{
			char *StatName	= EffectData->ConstStats[StatID];
			int VarType		= EffectData->ConstType[StatID];
			float VarData	= EffectData->ConstValue[StatID];

			// Add value if it is used
			if (VarType != 0)
			{
				if (VarType == 5)		// See if we are using a duration in an eqipable!
				{
					// This should not happen!
					LogMessage("Warning! Buff: %s is Equipable using duration!", EffectData->BuffName);
					break;
				}
				// Convert to percentage
				if (VarType == STAT_BUFF_MULT || VarType == STAT_DEBUFF_MULT)
				{
					VarData = VarData / 100.0f;
				}
				m_StatIDs[m_MaxID++] = p->m_Stats.SetStat(VarType, StatName, VarData, "ITEM_EFFECTS");
				p->m_Stats.UpdateAux(StatName);		// Update Aux data
			}
		}
	}
}

void Equipable::RemoveTimeNode()
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	if (m_TimeNode.player_id && p && p->GetSectorManager())
    {
        p->GetSectorManager()->RemoveTimedCall(&m_TimeNode, true);
    }
}

void Equipable::AddItemStateFlag(unsigned long State)
{
    m_AuxEquipItem->SetItemState(m_AuxEquipItem->GetItemState() | State);
}

void Equipable::RemoveItemStateFlag(unsigned long State)
{
    m_AuxEquipItem->SetItemState(m_AuxEquipItem->GetItemState() & ~State);
}

bool Equipable::ItemReady()
{
    return (m_TimeNode.player_id <= 0);
}

float Equipable::GetQuality()
{
	_Item *item = m_AuxEquipItem->GetItemData();
	return item->Quality;
}

void Equipable::EquipDevice(bool equip)
{
	Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	if (m_AuxEquipItem->GetItemTemplateID() == 5081) //grail affinity device
	{
		p->SetGrailAffinity(equip);
		return;
	}

	if (!m_ItemBase) return;

	float neg = equip ? 1.0f : -1.0f;
	float zero = equip ? 1.0f : 0.0f;

	char *description = m_ItemBase->Description();

	//passive devices
	if (m_ItemBase->EquipableCount() > 0)
	{
		for (int i = 0; i < m_ItemBase->EquipableCount(); ++i)
		{
			ItemEffect *ie = m_ItemBase->GetEquipEffect(i);

			if (ie)
			{
				
			}
		}
		
	}

	//TODO: remove this mess and use the 'EquipableCount' vartype code above.
	//this is a nasty hack !!
	if (description)
	{
		//see if this is a sculptor type device
		if (CheckForItem(description, "increase to your Prospect skill")) 
		{
			p->ChangeProspectSkill(zero * (float)m_ItemBase->TechLevel() / 2.25f);

			if (strstr(description, "and scan range")) //scan range too?
			{
				p->AddScanSkill(zero * m_ItemBase->TechLevel() * 2000.0f);
			}
		}

		//see if this is a harpy's type device
		if (CheckForItem(description, "increase to your tractor beam speed"))
		{
			p->ChangeTractorBeamSpeed(zero * (float)m_ItemBase->TechLevel() * 0.18f );
		}
	}
}

//remove this too, once the proper code is being used.
bool Equipable::CheckForItem(char *description, char *search)
{
	long skill_count = 0;
	bool no_duplicate = true;
	bool retval = false;
	if (strstr(description, search))
	{
		Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
		retval = true;
		//check to see if there's already an item like this installed
		for(int i=0;i<6;i++)
		{
			_Item *item = p->ShipIndex()->Inventory.EquipInv.EquipItem[9+i].GetItemData();
			if (item->ItemTemplateID > 0)
			{
				ItemBase *itembase = g_ItemBaseMgr->GetItem(item->ItemTemplateID);

				//protect against equipping a null item, but how can that happen?
				if(itembase)
				{
					//what have we here?
					char *item_desc = itembase->Description();
					if (strstr(item_desc, search))
					{
						//already equipped this item, don't allow it to affect stats again
						skill_count++;
						if (skill_count > 1) 
						{
							no_duplicate = false;
							break;
						}
					}
				}
			}
		}
	}

	return (retval && no_duplicate);
}