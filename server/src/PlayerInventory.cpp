// PlayerInventory.cpp
//
// Contains all methods to modify / access player inventory
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

#include "PlayerClass.h"
#include "ItemBaseManager.h"

void Player::SendItemBase(long ItemID)
{
    g_ItemBaseMgr->SendItem(this, &m_ItemList, ItemID);
}

bool Player::CanReceiveTradeItems(Player *trader)
{
    for (u32 i=0; i<6; i++)
		if (!CanHaveAnotherOf(trader->ShipIndex()->Inventory.TradeInv.Item[i].GetItemTemplateID()))
			return false;
	return true;
}

int Player::TradeSpaceUsed()
{
	int count=0;

    for (u32 i=0; i<6; i++)
		if (ShipIndex()->Inventory.TradeInv.Item[i].GetItemTemplateID() > 0)
			count++;
	return count;
}

int Player::CargoFreeSpace()
{
	int count=0;

	for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() <= 0)
            count++;
	return count;
}

// Returns the count of items with ID in cargo inventory
long Player::CargoItemCount(long ItemID)
{
    long ItemCount = 0;

    for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ItemID)
        {
            ItemCount += ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount();
        }
    }

    return ItemCount;
}

long Player::VaultItemCount(long ItemID)
{
    long ItemCount = 0;

    for (u32 i=0; i<96; i++)
    {
        if (PlayerIndex()->SecureInv.Item[i].GetItemTemplateID() == ItemID)
        {
            ItemCount += PlayerIndex()->SecureInv.Item[i].GetStackCount();
        }
    }

    return ItemCount;
}

long Player::EquipItemCount(long ItemID)
{
    long ItemCount = 0;

    for (u32 i=0; i<20; i++)
    {
		if (ShipIndex()->Inventory.EquipInv.EquipItem[i].GetItemTemplateID() == ItemID)
        {
            ItemCount += ShipIndex()->Inventory.EquipInv.EquipItem[i].GetStackCount();
        }
    }

    return ItemCount;
}

long Player::TradeItemCount(long ItemID)
{
    long ItemCount = 0;

    for (u32 i=0; i<6; i++)
    {
		if (ShipIndex()->Inventory.TradeInv.Item[i].GetItemTemplateID() == ItemID)
        {
            ItemCount += ShipIndex()->Inventory.TradeInv.Item[i].GetStackCount();
        }
    }

    return ItemCount;
}

bool Player::CanHaveAnotherOf(long ItemID)
{
	if (ItemID > 0)
	{
		ItemBase *item = g_ItemBaseMgr->GetItem(ItemID);
		if (item && (item->Flags() & ITEM_FLAGS_UNIQUE))
			return CargoItemCount(ItemID) == 0 && VaultItemCount(ItemID) == 0 && EquipItemCount(ItemID) == 0 && TradeItemCount(ItemID) == 0;
	}
	return true;
}

bool Player::IsPartialStackOf(long ItemID, float Quality)
{
    ItemBase * ItemData = g_ItemBaseMgr->GetItem(ItemID);

	if (!ItemData)
        return false;

	for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ItemID &&
            ShipIndex()->Inventory.CargoInv.Item[i].GetQuality() == Quality &&
			ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount() < ItemData->MaxStack())
        {
			return true;
		}
	}
	return false;
}

bool Player::CanCargoAddItem(_Item * myItem)
{
    return CanCargoAddItem(myItem->ItemTemplateID, myItem->StackCount, myItem->Quality);
}

// Returns whether or not the item with a given stack and quality can be added to inventory
bool Player::CanCargoAddItem(long ItemID, u32 Stack, float Quality)
{
    return (CargoAddItemCount(ItemID, Quality) >= Stack);
}

// Returns how many items with a given quality can be added to inventory
u32 Player::CargoAddItemCount(long ItemID, float Quality)
{
    u32 Count = 0;
    ItemBase * ItemData = g_ItemBaseMgr->GetItem(ItemID);

    if (!ItemData)
    {
        return 0;
    }

	m_Mutex.Lock();
    for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ItemID &&
            ShipIndex()->Inventory.CargoInv.Item[i].GetQuality() == Quality)
        {
            // We found an item stack of the same item and quality
            Count += (ItemData->MaxStack() - ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount());
        }
        else if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == -1)
        {
            Count += ItemData->MaxStack();
        }
    }
	m_Mutex.Unlock();
    return Count;
}

int Player::CargoAddItem(long ItemID, u32 Stack, u32 TradeStack)
{
    _Item TempData;
    memset(&TempData, 0, sizeof(_Item));
    TempData.ItemTemplateID = ItemID;
    TempData.StackCount = Stack;
    TempData.Quality = 1.0f;
    TempData.Structure = 1.0f;
    TempData.TradeStack = TradeStack;

    return CargoAddItem(&TempData);
}

int Player::CargoAddItem(_Item * myItem)
{
	if (myItem->ItemTemplateID < 0)
	{
		return -1;
	}

	ItemBase * myItemBase = g_ItemBaseMgr->GetItem(myItem->ItemTemplateID);

	if (!myItemBase)
	{
		return -2;
	}

    u32 curTrade = myItem->TradeStack;
    u32 curStack = myItem->StackCount;
	u32 maxStack = myItemBase->MaxStack();
	u32 curPrice = (u32)myItem->Price;

	if (myItem->Structure == 0)
		myItem->Structure = 1;

    SendItemBase(myItem->ItemTemplateID);

	// Update the instance information
	QualityCalculator(myItem);

    // If this is a stackable item, check stacks
    if (maxStack > 1)
    {
		m_Mutex.Lock();
	    for(u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
	    {
		    if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == myItem->ItemTemplateID &&
				ShipIndex()->Inventory.CargoInv.Item[i].GetQuality() == myItem->Quality &&
				!strcmp(ShipIndex()->Inventory.CargoInv.Item[i].GetBuilderName(), myItem->BuilderName))
		    {
			    if (ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount() + curStack > maxStack)
			    {
				    // There is SOME room in this slot
					u32 oldstack = ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount();
                    u32 moved = maxStack - ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount();
				    curStack -= moved;

                    ShipIndex()->Inventory.CargoInv.Item[i].AddTradeStack(curTrade < moved ? curTrade : moved);
                    curTrade -= moved;

				    ShipIndex()->Inventory.CargoInv.Item[i].SetStackCount(maxStack);
					//set average cost
					float average = ( (ShipIndex()->Inventory.CargoInv.Item[i].GetAveCost() * oldstack) + (float)(moved * curPrice) ) / (float)maxStack;
					ShipIndex()->Inventory.CargoInv.Item[i].SetAveCost(average);
					if (myItem->Price != 0)
					{
						ShipIndex()->Inventory.CargoInv.Item[i].SetPrice(myItem->Price);
					}
					m_Mutex.Unlock();
					SaveInventoryChange(i);
					m_Mutex.Lock();
			    }
			    else
			    {
				    // Enough room here for the rest of the stack
					u32 oldstack = ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount();
					//set average cost
					float average = ( (ShipIndex()->Inventory.CargoInv.Item[i].GetAveCost() * oldstack) + (float)(curStack * curPrice) ) / (float)(curStack + oldstack);
					ShipIndex()->Inventory.CargoInv.Item[i].SetAveCost(average);
					if (myItem->Price != 0)
					{
						ShipIndex()->Inventory.CargoInv.Item[i].SetPrice(myItem->Price);
					}

				    curStack += oldstack;
                    curTrade += ShipIndex()->Inventory.CargoInv.Item[i].GetTradeStack();
				    ShipIndex()->Inventory.CargoInv.Item[i].SetStackCount(curStack);
				    ShipIndex()->Inventory.CargoInv.Item[i].SetTradeStack(curTrade);
					m_Mutex.Unlock();
					SaveInventoryChange(i);
					goto check_mission;
                    //return 0;
			    }
		    }
	    }
		m_Mutex.Unlock();
    }

    // At this point, we need to fill empty slots with the item
	m_Mutex.Lock();
	for(u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
	{
		if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == -1)
		{
			if (curStack > maxStack)
			{
				// We have more than a full stack
				curStack -= maxStack;
				ShipIndex()->Inventory.CargoInv.Item[i].SetData(myItem);
				ShipIndex()->Inventory.CargoInv.Item[i].SetStackCount(maxStack);

                // The tradable count is the smaller amount of whats left tradable and max stack
                ShipIndex()->Inventory.CargoInv.Item[i].SetTradeStack(curTrade < maxStack ? curTrade : maxStack);
				ShipIndex()->Inventory.CargoInv.Item[i].SetAveCost((float)curPrice);

                // The SetTradeStack method converts negative numbers to zero so this is allowerd
				curTrade -= maxStack;
				m_Mutex.Unlock();
				SaveInventoryChange(i);
				m_Mutex.Lock();
			}
			else
			{
				// Enough room here to finish adding this item
				ShipIndex()->Inventory.CargoInv.Item[i].SetData(myItem);
				ShipIndex()->Inventory.CargoInv.Item[i].SetStructure(myItem->Structure);
				ShipIndex()->Inventory.CargoInv.Item[i].SetStackCount(curStack);
				ShipIndex()->Inventory.CargoInv.Item[i].SetTradeStack(curTrade);
				ShipIndex()->Inventory.CargoInv.Item[i].SetAveCost((float)curPrice);
				m_Mutex.Unlock();
				SaveInventoryChange(i);
                goto check_mission;
			}
		}
	}
	m_Mutex.Unlock();
    return -3;

check_mission:
    //Check for mission stage, moved to SaveInventoryChange to catch ALL possibilities
	//CheckMissions(0, myItem->ItemTemplateID, 0, OBTAIN_ITEMS);
	return 0;
}


void Player::CargoRemoveItem(long ItemID, u32 Stack)
{
    if (ItemID < 0 || Stack <= 0)
    {
        return;
    }
	m_Mutex.Lock();
    u32 ItemStack;
    for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ItemID)
        {
            ItemStack = ShipIndex()->Inventory.CargoInv.Item[i].GetStackCount();

            if (ItemStack <= Stack)
            {
                ShipIndex()->Inventory.CargoInv.Item[i].Empty();
                Stack -= ItemStack;
            }
            else
            {
                ItemStack -= Stack;
                ShipIndex()->Inventory.CargoInv.Item[i].SetStackCount(ItemStack);
                Stack = 0;
            }
			m_Mutex.Unlock();
			SaveInventoryChange(i);
			m_Mutex.Lock();

            if (Stack == 0)
            {
                break;
            }
        }
    }
	m_Mutex.Unlock();
    if (Stack != 0)
    {
        LogMessage("RemoveTradeItem - Stack Remaining: %d\n",Stack);
    }
	
}

/* Moving an item from Source to Destination, check if they can stack */
/* Note: If modified, the source and destination stacks have to be inverted */

/* This currently does not check quality */
void Player::CheckStack(u32 MoveNum, _Item * Source, _Item * Destination)
{
    /* If the items have the same ID they can stack */
	if (Source->ItemTemplateID == Destination->ItemTemplateID &&
		Source->Quality == Destination->Quality &&
		!strcmp(Source->BuilderName, Destination->BuilderName))
	{
	    ItemBase * myItemBase = g_ItemBaseMgr->GetItem(Source->ItemTemplateID);

	    if (!myItemBase)
        {
		    return;
        }

        /* If the MoveNum has enough items to make a full stack at Destination */
		if (Destination->StackCount + MoveNum > myItemBase->MaxStack())
		{
            u32 moved = myItemBase->MaxStack() - Destination->StackCount;
			Source->StackCount -= moved;						
			Destination->StackCount = myItemBase->MaxStack();

            /* Now update the trade stacks */
            Destination->TradeStack += (Source->TradeStack < moved ? Source->TradeStack : moved);
            Source->TradeStack -= moved;

		}
        /* Otherwise the Destination can store the entire MoveNum ammount */
		else
		{
			Destination->StackCount += MoveNum;
			Source->StackCount -= MoveNum;

            /* Now update the trade stacks */
            Destination->TradeStack += (Source->TradeStack < MoveNum ? Source->TradeStack : MoveNum);
            Source->TradeStack -= MoveNum;
		}

        /* Switch the Source and Destination stack counts */
        u32 tmpStack = Destination->StackCount;
        Destination->StackCount = Source->StackCount;
        Source->StackCount = tmpStack;

        /* Now switch their trade stacks aswell */
        tmpStack = Destination->TradeStack;
        Destination->TradeStack = Source->TradeStack;
        Source->TradeStack = tmpStack;

        /* If weve moved all of the items out of the source, set it to an empty item */
		if (Destination->StackCount == 0)
		{
			memcpy(Destination, &g_ItemBaseMgr->EmptyItem, sizeof(_Item));
		}
	}
    /* If the destination is am empty item and we are moving a substack*/
	else if (Destination->ItemTemplateID == -1 && MoveNum < Source->StackCount)
	{
        /* Copy the source item to the destination */
        memcpy(Destination, Source, sizeof(_Item));

        Destination->StackCount = MoveNum;
		Source->StackCount -= MoveNum;

        /* Now update the trade stacks */
        Destination->TradeStack += (Source->TradeStack < MoveNum ? Source->TradeStack : MoveNum);
        Source->TradeStack -= MoveNum;

        /* Switch the Source and Destination stack counts */
        u32 tmpStack = Destination->StackCount;
        Destination->StackCount = Source->StackCount;
        Source->StackCount = tmpStack;

        /* Now switch their trade stacks aswell */
        tmpStack = Destination->TradeStack;
        Destination->TradeStack = Source->TradeStack;
        Source->TradeStack = tmpStack;

        /* If weve moved all of the items out of the source, set it to an empty item */
		if (Destination->StackCount == 0)
		{
			memcpy(Destination, &g_ItemBaseMgr->EmptyItem, sizeof(_Item));
		}
	}
}

void Player::SetPrices()
{
    u32 i;
    ItemBase * Item = 0;

    for (i=0; i<96; i++)
    {
		PlayerIndex()->SecureInv.Item[i].SetPrice(Negotiate(
			GetVenderBuyPrice(PlayerIndex()->SecureInv.Item[i].GetItemTemplateID()),false,true
			));
    }

	for (i=0; i<20; i++)
    {
		ShipIndex()->Inventory.EquipInv.EquipItem[i].SetPrice(Negotiate(
			GetVenderBuyPrice(ShipIndex()->Inventory.EquipInv.EquipItem[i].GetItemTemplateID()),false,true
			));
    }

	for (i=0; i<20; i++)
    {
		ShipIndex()->Inventory.AmmoInv.Item[i].SetPrice(Negotiate(
			GetVenderBuyPrice(ShipIndex()->Inventory.AmmoInv.Item[i].GetItemTemplateID()),false,true
			));
    }

    for (i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
		
		ShipIndex()->Inventory.CargoInv.Item[i].SetPrice(Negotiate(
			GetVenderBuyPrice(ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID()),false,true
			));
    }
}

void Player::ClearPrices()
{
    u32 i;

    for (i=0; i<96; i++)
    {
        PlayerIndex()->SecureInv.Item[i].SetPrice(0);
    }

    for (i=0; i<20; i++)
    {
		ShipIndex()->Inventory.AmmoInv.Item[i].SetPrice(0);
    }

    for (i=0; i<20; i++)
    {
		ShipIndex()->Inventory.EquipInv.EquipItem[i].SetPrice(0);
    }

    for (i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
        ShipIndex()->Inventory.CargoInv.Item[i].SetPrice(0);
    }
}

long Player::FindFreeVaultSpace(long item_id, u32 stack)
{
	//first check if this is stackable, and will fit
	for(u32 i=0; i<96; i++)
	{
		if (PlayerIndex()->SecureInv.Item[i].GetItemTemplateID() == item_id)
		{
			ItemBase * newItemBase = g_ItemBaseMgr->GetItem(item_id);
			if (newItemBase && (PlayerIndex()->SecureInv.Item[i].GetStackCount() + stack) <= newItemBase->MaxStack())
			{
				return i;
			}
		}
	}

	//ok now check for free slot
	for(u32 i=0; i<96; i++)
	{
		if (PlayerIndex()->SecureInv.Item[i].GetItemTemplateID() == -1)
		{
			return i;
		}
	}

	return -1;
}

// Damage the player's trade cargo by damage amount (1.0f to 0.0f percent)
// Currently uses the item's quality to track damage, needs to be changed to
// structure when structure is in the game.
// Returns the number of items changed.
int Player::DamageTradeCargo(float damage)
{
        int count = 0;

		// Prevent damage for incapacitated ships
		if (ShipIndex()->GetIsIncapacitated())
			return count;

        // Prevent code from 'repairing' items with this function
        if (damage > 1.0f)
            damage = 1.0f;

        // Damage all cargo in the ship's inventory
        for (u32 i=0; i < ShipIndex()->Inventory.GetCargoSpace(); i++)
        {
            ItemBase * baseItem = g_ItemBaseMgr->GetItem(ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID());
            if (baseItem && baseItem->Category() == 90)
            {
				ShipIndex()->Inventory.CargoInv.Item[i].SetStructure(ShipIndex()->Inventory.CargoInv.Item[i].GetStructure() * damage);
				SaveInventoryChange(i);
                count++;
            }
        }
        return count;
}

// Damage one of the players equipped items (50% chance) when hull is hit
// Damage amount based on size of hit, enemy level, number of attackers, remaining hull and max hull
// Chance to take quality damage based on remaining structure
void Player::DamageEquipment(float hull_dmg,float hull_remaining, Object *mob)
{
	u32 dmg_type = rand()%6; // 0=shi/rea/eng,1=devices,2=weapons,3-5=ship(no dmg)
	
	// 50%
	if (dmg_type < 3 && hull_dmg > 0.0f) 
	{
		u32 equip_index = 0;
		float damage_control = 1.0f - m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL);

		// what item is damaged?
		switch (dmg_type)
		{
			case 0:
				equip_index = rand()%3;
				switch (equip_index)
				{
				case 0:
					damage_control -= m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL_SHIELD);
					break;
				case 1:
					damage_control -= m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL_REACTOR);
					break;
				case 2:
					damage_control -= m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL_ENGINE);
					break;
				}
				break;
			case 1:
				if (!m_DeviceSlots)
					return;
				equip_index = 9+rand()%m_DeviceSlots;
				damage_control -= m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL_DEVICES);
				break;
			case 2:
				if (!m_WeaponSlots)
					return;
				equip_index = 3+rand()%m_WeaponSlots;
				damage_control -= m_Stats.GetStat(STAT_EQUIPMENT_DAMAGE_CONTROL_WEAPONS);
				break;
		}
		if (damage_control < 0.0f)
			damage_control = 0.0f;

		// is slot used?
		s32 itemid = ShipIndex()->Inventory.EquipInv.EquipItem[equip_index].GetItemTemplateID();
		if (itemid >= 0)
		{
			ItemBase *myItem = g_ItemBaseMgr->GetItem(itemid);
			float hull_max = ShipIndex()->GetMaxHullPoints();
			float base_dmg = (float)(rand()%10+1); // 1 to 10 (percent)
			float hit_multiplier = (float)m_AttackerCount;
			float level_multiplier = 1.0f;
			float armour_mitigation = 2.0f;
			float structure_dmg,quality_dmg,cur_structure,cur_quality;
			char *item_name = myItem ? myItem->Name() : "unknown";
			int tech = myItem ? myItem->TechLevel() : 9;

			// do the damage calcs
			if (hull_max > 0.0f)
			{
				if (hull_dmg > hull_max)
					hit_multiplier = hull_dmg / hull_max * (float)m_AttackerCount;
	
				if (hull_remaining > 0.0f)
					armour_mitigation = 1.0f - hull_remaining / hull_max;
			}
			if (mob && CombatLevel())
				level_multiplier = (float)mob->Level() / (float)CombatLevel();
			structure_dmg = base_dmg * hit_multiplier * level_multiplier * armour_mitigation * damage_control;

			// reduce the structure
			cur_structure = ShipIndex()->Inventory.EquipInv.EquipItem[equip_index].GetStructure();
			cur_structure -= structure_dmg * 0.01f;
			if (cur_structure < 0.0f)
				cur_structure = 0.0f;
			ShipIndex()->Inventory.EquipInv.EquipItem[equip_index].SetStructure(cur_structure);

			// if the structure is low possibly reduce the quality too!, chance range 5%@50% structure to 100%@0% structure
			if (cur_structure < 0.5f && (float)(rand()%55)*0.01f >= cur_structure)
			{
				base_dmg = (float)(rand()%(tech/2+1)+1); // 1 to 5
				quality_dmg = base_dmg * armour_mitigation * damage_control;
				cur_quality = ShipIndex()->Inventory.EquipInv.EquipItem[equip_index].GetQuality();
				cur_quality -= quality_dmg * 0.01f;
				if (cur_quality < 0.0f)
					cur_quality = 0.0f;
				ShipIndex()->Inventory.EquipInv.EquipItem[equip_index].SetQuality(cur_quality);
				// give the player the bad news
				char msg_buffer[128];
				sprintf_s(msg_buffer,128,"%s has been badly damaged and has lost %.0f%% of its quality!",
					item_name,quality_dmg); 
				SendPushMessage(msg_buffer,"MessageLine",5000,3);
			}

			// save the damage
			m_Equip[equip_index].Lock();
			_Item *instance = m_Equip[equip_index].GetItem();
			SaveEquipmentChange(equip_index,instance);
			QualityCalculator(instance);
			m_Equip[equip_index].Unlock();

			// dont spam messages for low hits
			if (structure_dmg >= 5.0f)
				SendVaMessage("%s has been %s.",item_name,cur_structure == 0.0f ? "destroyed" : (cur_structure < 0.5f ? "damaged" : "dented"));
		}
	}
}

void Player::RestoreEquipmentStructure()
{
	u32 i;
	_Item *instance;

	for(i=0;i<20;i++) 
	{
		m_Equip[i].Lock();
		if (ShipIndex()->Inventory.EquipInv.EquipItem[i].GetStructure() < 1.0f)
		{
			ShipIndex()->Inventory.EquipInv.EquipItem[i].SetStructure(1.0f);
			instance = m_Equip[i].GetItem();
			SaveEquipmentChange(i, instance);
			QualityCalculator(instance);
		}
		m_Equip[i].Unlock();
	}
}

/*
	Restore the equipment[index] with structure points.
*/

void Player::RestoreEquipmentStructure(int index,float structure)
{
	_Item *instance;
	m_Equip[index].Lock();
	ShipIndex()->Inventory.EquipInv.EquipItem[index].SetStructure(structure);
	instance = m_Equip[index].GetItem();
	SaveEquipmentChange(index, instance);
	QualityCalculator(instance);
	m_Equip[index].Unlock();
}

//this finishes off our installs at first login
void Player::CompleteInstalls()
{
    for (int i=0; i<20; i++)
    {
		m_Equip[i].Install(0);
	}
}