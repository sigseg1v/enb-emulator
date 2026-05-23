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
#include "PlayerManufacturing.h"
#include "ItemBaseManager.h"
#include "ServerManager.h"

#include <algorithm>

void Player::HandleManufactureTerminal(unsigned char *data)
{
    ManufactureData * Packet = (ManufactureData *) data;
    long Terminal = ntohl(Packet->Data);
	LogDebug("ManufactureTerminal: %d\n", Terminal);
    switch (Terminal)
    {
        case 0:
            switch (ManuIndex()->GetMode())
            {
                case MODE_MANUFACTURE: // Manufacturing (check for override)
                    break;
                case MODE_ANALIZE: // Analize (check for target and override)
                    break;
                case MODE_DISMANTLE: // Dismantle (check for target) override???
                    break;
                case MODE_REFINE: // Refining (nothing to check)
                    break;
                default:
                    LogMessage("ManufactureTerminal 0 - Unknown Previous: %d\n",ManuIndex()->GetMode());
                    break;
            }
			// resetting the terminal on exit doesnt clear the known list off the client and prevents the next set to "" from flagging a change and being omitted from the packet
            break;
        case 1:
            ManuIndex()->TerminalReset(0);
            ManuIndex()->SetMode(MODE_MANUFACTURE);
            break;
        case 2:
            ManuIndex()->TerminalReset(0);
            ManuIndex()->SetMode(MODE_ANALIZE);
            break;
        case 4:
            ManuIndex()->TerminalReset(0);
            ManuIndex()->SetMode(MODE_REFINE);
            BuildRefineList();
            break;
        default:
            LogMessage("ManufactureTerminal - Unknown Terminal: %d\n",Terminal);
            break;
    }

    ManuIndex()->SetDifficulty(DIFFICULTY_AUTOMATIC);
    ResetManuItems();
    SendAuxManu();
}

void Player::HandleManufactureCategorySelection(unsigned char *data)
{
    ManufactureData * Packet = (ManufactureData *) data;
    long Category = ntohl(Packet->Data);
	LogDebug("ManufactureCategorySelection: %d\n", Category);
    ManuIndex()->SetCurrentItemCat(Category);

	BuildManufactureList();
}

float Player::GetAnalyzeChance()
{
	float buffBonus;
	int skill = GetManufactureSkill(m_CurManuItem,&buffBonus);
	float maxTechLevel = (skill>5)?((skill==6)?7.0f:9.0f):skill;
	const float chanceMax = 100.0f;
	const float chanceMin = 35.0f;
	float m = (maxTechLevel+1.0f+buffBonus)/(chanceMin-chanceMax);
	float c = (m * -100.0f);
	float chanceActual = (((float)(m_CurManuItem->TechLevel()))-c)/m;
	
	float ana_mod = (float)m_CurManuItem->AnalyseDifficulty();

	if(ana_mod < 50.0f)
	{
		chanceActual -= (chanceActual - chanceMin)*(ana_mod/50.0f);
	}
	else if(ana_mod > 50.0f)
	{
		chanceActual += (chanceMax - chanceActual)*((ana_mod-50.0f)/50.0f);
	}
	
	//jenquai racial analyze bonus

	if(Race() == RACE_JENQUAI)
	{
		chanceActual += skill;
	}

	return min(chanceActual,100.0f);
}

float Player::GetDismantleChance()
{
	float buffBonus;
	int skill = GetManufactureSkill(m_CurManuItem,&buffBonus);
	float maxTechLevel = (skill>5)?((skill==6)?7.0f:9.0f):skill;
	const float chanceMax = 100.0f;
	const float chanceMin = 35.0f;
	float m = (maxTechLevel+1.0f+buffBonus)/(chanceMin-chanceMax);
	float c = (m * -100.0f);
	float chanceActual = (((float)(m_CurManuItem->TechLevel()))-c)/m;

	float dis_mod = (float)m_CurManuItem->AnalyseDifficulty();
	
	if(dis_mod < 50.0f)
	{
		chanceActual -= (chanceActual - chanceMin)*(dis_mod/50.0f);
	}
	else if(dis_mod > 50.0f)
	{
		chanceActual += (chanceMax - chanceActual)*((dis_mod-50.0f)/50.0f);
	}

	//progen racial analyze bonus

	if(Race() == RACE_PROGEN)
	{
		chanceActual *= (1.5f+(skill/80.0f));
	}
	else
	{
		chanceActual *= (1.5f+(skill/100.0f));
	}

	return min(chanceActual,100.0f);
}

bool Player::AnalyseDismantleSetItem(_Item *Source)
{
    if (m_Manufacturing)
    {
        return false;
    }

	m_CurManuItem = g_ItemBaseMgr->GetItem(Source->ItemTemplateID);

	// Make sure we can find this item
	if (!m_CurManuItem)
	{
        SendVaMessage("Invalid Item!");
		return false;
	}

	// fetch waiting items from previous dismantle
	if (m_ItemsWaiting)
	{
		bool fail = false;
		for(int item=0;item<6;item++)
		{
			if (ManuIndex()->Components.Item[item].GetItemTemplateID() != -1)
			{
				if (CargoAddItem(ManuIndex()->Components.Item[item].GetItemTemplateID(), 1) == 0)
					ManuIndex()->Components.Item[item].SetItemTemplateID(-1);
				else
					fail = true;
			}
		}
		SendAuxManu();
		SendAuxShip();
		if (fail)
		{
			SendPriorityMessageString("Please make room to receive items","MessageLine",5000,4);
			return false;
		}
		m_ItemsWaiting = false;
	}

	// See if we can manufacture this first
	if (AllowManufacture(m_CurManuItem))
	{
		// Set the manufacting costs here
		ManuIndex()->SetBaseCost((long) (m_CurManuItem->Cost())); //TODO: Dynamically create me

		//terminal discount
		if (m_CurManuItem->ManufactureCost() <= 0)
		{
			int tempPrice = (int) ceil((Negotiate(m_CurManuItem->Cost(),true,false)) * 0.1f);
			ManuIndex()->SetNegotiatedCost((u64) max(1,tempPrice));
		}
		else
		{
			int tempPrice = (int) ceil((Negotiate(m_CurManuItem->Cost(),true,false)) * 0.1f);
			ManuIndex()->SetNegotiatedCost((u64) max(1,tempPrice));
		}

		/*Modify dismantle price if this is not a component*/
		if((m_ManuRecipes.find(Source->ItemTemplateID) != m_ManuRecipes.end()) &&
		   ((m_CurManuItem->Category() < IB_CATEGORY_ELECTRONIC_ITEM) ||
		   (m_CurManuItem->Category() > IB_CATEGORY_AMMO_COMPONENT)))
		{
			ManuIndex()->SetNegotiatedCost(ManuIndex()->GetNegotiatedCost()/10);
		}

		// Make sure they have enough credits
		if (ManuIndex()->GetNegotiatedCost() > PlayerIndex()->GetCredits())
		{
			ManuIndex()->SetValidity(VALIDITY_INSUFFICIENT_CREDITS);
			ManuIndex()->SetFailureMessage("Insufficient Credits");
			SendAuxManu();
			return false;
		}

		// Allow devs to analyse player made stuff
		if (m_ManuRecipes.find(Source->ItemTemplateID) == m_ManuRecipes.end() && 
			Source->BuilderName[0] != 0 && AdminLevel() < GM)
		{
			ManuIndex()->SetValidity(VALIDITY_PLAYERMADE);
			ManuIndex()->SetFailureMessage("This item is player made");
			SendAuxManu();
			return false;
		}

		// If we have recipe we could dismantle
		if (m_ManuRecipes.find(Source->ItemTemplateID) != m_ManuRecipes.end())
		{

			// If we already analyzed the ammo do not do this again
			if (m_CurManuItem->ItemType() == IB_ITEMTYPE_AMMO)
			{
				// We can not dismantle this so lets exit
				ManuIndex()->SetValidity(VALIDITY_NOT_DISMANTABLE);
				ManuIndex()->SetFailureMessage("You can not dismantle ammo");
				SendAuxManu();
				return false;
			}

			short count = 0;
			// count the components and check space
			for(int cID = 0; cID < 6; cID++)
			{
				long cItemID = m_CurManuItem->Component(cID);
				if (cItemID != -1 && !IsPartialStackOf(cItemID,1.0f))
					count++;
			}
			if (count > CargoFreeSpace()+1) // + the dismantled item
			{
				ManuIndex()->SetValidity(VALIDITY_INSUFFICIENT_CARGO);
				SendPriorityMessageString("Not enough free space to attempt dismantle","MessageLine",5000,4);
				return false;
			}

			if(Source->Quality < 0.6f)
			{
				ManuIndex()->SetValidity(VALIDITY_LOW_QUALITY);
				ManuIndex()->SetFailureMessage("This item is of too low quality.");
				SendAuxManu();
				return false;
			}

			ManuIndex()->SetMode(MODE_DISMANTLE);
			count = 0;
			// Display what you can get if it is broken down
			for(int cID = 0; cID < 6; cID++)
			{
				long cItemID = m_CurManuItem->Component(cID);
				SendItemBase(cItemID);
				ManuIndex()->Components.Item[cID].SetItemTemplateID(cItemID);
				if (cItemID != -1)
					count++;
			}

			/*Dismantle difficulty*/

			float probability = (GetDismantleChance()) + (Source->Quality - 1.0f)*20.0f;
			probability = min(probability,100.0f);
			probability = max(probability,1.0f);
			ManuIndex()->SetSuccessProbability(probability*0.01f);
			ManuIndex()->SetCriticalSuccessProbability(probability  *0.001f);
		}
		else
		{
			if(Source->Quality < 0.6f)
			{
				ManuIndex()->SetValidity(VALIDITY_LOW_QUALITY);
				ManuIndex()->SetFailureMessage("This item is of too low quality.");
				SendAuxManu();
				return false;
			}

			// clear any left over comps from a potential dismantle
			for(int cID = 0; cID < 6; cID++)
			{
				ManuIndex()->Components.Item[cID].SetItemTemplateID(-1);
			}
			// We are going to be analyzing this item
			ManuIndex()->SetMode(MODE_ANALIZE);
			float probability = GetAnalyzeChance() + (Source->Quality - 1.0f)*20.0f;
			probability = min(probability,100.0f);
			probability = max(probability,1.0f);
			ManuIndex()->SetSuccessProbability(probability*0.01f);
			ManuIndex()->SetCriticalSuccessProbability(probability * 0.001f);
		}

		ManuIndex()->Target.Item[0].SetData(Source);
		ManuIndex()->Target.Item[0].SetStackCount(1);

		// We are able to do something to this item
		ManuIndex()->SetValidity(VALIDITY_VALID);
		ManuIndex()->SetFailureMessage("");
		SendAuxManu();
	}
	else
	{
		// Update Aux and send the error message
		SendAuxManu();
		return false;
	}
	return true;
}

void Player::HandleManufactureSetItem(unsigned char *data)
{
    ManufactureData * Packet = (ManufactureData *) data;
    long Item = ntohl(Packet->Data);

	if (Item > 0xFFFF)
	{
		Item = ntohl(Packet->Data);  //lets hedge our bets till we know one way or another
	}

    if (m_Manufacturing)
    {
        return;
    }

    SendItemBase(Item);

    m_CurManuItem = g_ItemBaseMgr->GetItem(Item);

    if (!m_CurManuItem)
    {
        SendVaMessage("Invalid Item!");
        return;
    }

	// make sure the player has the build skill corresponding to this item!
	// players are getting recipes for skills they dont have from somewhere
    if (!AllowManufacture(m_CurManuItem))
    {
        return;
    }

    ResetManuItems();

    ManuIndex()->Target.Item[0].SetItemTemplateID(m_CurManuItem->ItemTemplateID());

	SendVaMessage("%s requires:",m_CurManuItem->Name());
    for (int i=0;i<6;i++)
    {
		SendItemBase(m_CurManuItem->Component(i));
		ManuIndex()->Components.Item[i].SetItemTemplateID(m_CurManuItem->Component(i));
		ItemBase *ib = g_ItemBaseMgr->GetItem(m_CurManuItem->Component(i));
		if (ib)
		{
			bool missing = CargoItemCount(m_CurManuItem->Component(i)) == 0;
			SendVaMessage("[TL%d] %s %s",ib->TechLevel(),ib->Name(),missing ? "(missing)" : "(in hold)");
		}
    }

    ManuIndex()->SetValidity(0);
    ManuIndex()->SetAdditionalIterations(0);
	ManuIndex()->SetBaseCost((u64) (m_CurManuItem->Cost()));


	/*Mozu note: Change this when the database tables get fixed for man_cost
		even then, this prevents a divide by zero error*/
	if(m_CurManuItem->ManufactureCost() <= 0)
	{
		//if the manufacture cost is <=0, use 1/10th of the base cost
		int tempPrice = m_CurManuItem->Cost();
		if (m_CurManuItem->ItemType() != 13)
		{
			//not a component, so multiply by the stack size
			tempPrice *= m_CurManuItem->MaxStack();
		}
		tempPrice = (int)ceil(0.10f * (float)Negotiate(tempPrice,true,false));
		ManuIndex()->SetNegotiatedCost((u64)max(1,tempPrice));
	}
	else
	{
		//else use the manufacture cost
		int tempPrice = m_CurManuItem->ManufactureCost();
		if (m_CurManuItem->ItemType() != 13)
		{
			//not a component, so multiply by the stack size
			tempPrice *= m_CurManuItem->MaxStack();
		}
		ManuIndex()->SetNegotiatedCost((u64)Negotiate(tempPrice,true,false));
	}

    CheckItemRequirements();

	// add the expected quality range to the terminal
	if (ManuIndex()->GetValidity() == VALIDITY_VALID)
	{
		float minq,maxq,expq;
		m_base_quality = CalculateAverageComponentQuality();
		m_tradeStackXP = GetComponentTradeStackXP();
		CalculateBuiltItemQuality(&minq,&maxq);
		expq = (minq+maxq)*0.5f;
		if (expq > 2.0f)
			expq = 2.0f;
		if (minq > 2.0f)
			minq = 2.0f;
		if (maxq > 2.0f)
			maxq = 2.0f;
		ManuIndex()->SetMinimumQuality(0);  // these functions detect changes to decide whether to send the info in the next packet
		ManuIndex()->SetMaximumQuality(0);  // so this forces the info to be sent otherwise the fields go blank on the client
		ManuIndex()->SetExpectedQuality(0); // I dont know if this is the right thing to do or not
		ManuIndex()->SetMinimumQuality(minq);
		ManuIndex()->SetMaximumQuality(maxq);
		ManuIndex()->SetExpectedQuality(expq);
	}

    SendAuxManu();

    LogDebug("ManufactureSetItem: %d\n", Item);
}

void Player::HandleRefineSetItem(unsigned char *data)
{
    ManufactureData * Packet = (ManufactureData *) data;
    //long Item = ntohl(Packet->Data); //this was done with no bit reverse, may need to test
	long Item = Packet->Data;

	if (Item > 0xFFFF)
	{
		Item = ntohl(Packet->Data);  //lets hedge our bets till we know one way or another
	}

    if (m_Manufacturing)
    {
        return;
    }

    SendItemBase(Item);

    m_CurManuItem = g_ItemBaseMgr->GetItem(Item);

    if (!m_CurManuItem)
    {
        SendVaMessage("Invalid Item!");
        return;
    }

    ResetManuItems();

    ManuIndex()->Target.Item[0].SetItemTemplateID(m_CurManuItem->ItemTemplateID());

	//TODO: Unroll me later on...this loop is 'useless' and wastes time.
    for (int i=0;i<6;i++)
    {
        ManuIndex()->Components.Item[i].SetItemTemplateID(m_CurManuItem->Component(i));
    }

	ManuIndex()->SetValidity(0);
    ManuIndex()->SetAdditionalIterations(0);
    ManuIndex()->SetBaseCost(m_CurManuItem->Cost()); //TODO: Dynamically create me

	/*Mozu note: Change this when the database tables get fixed for man_cost
		even then, this prevents a divide by zero error*/
	if(m_CurManuItem->ManufactureCost() <= 0)
	{
		int tCost = (int)ceil((2.0f / 3.0f) * (float) Negotiate(m_CurManuItem->Cost(),true,false));
		ManuIndex()->SetNegotiatedCost((u64)max(1,tCost)); 
	}
	else
	{
		ManuIndex()->SetNegotiatedCost((u64)Negotiate(m_CurManuItem->ManufactureCost(),true,false)); 
	}

    CheckItemRequirements();

    SendAuxManu();
}

void Player::HandleManufactureAction(unsigned char *data)
{
    ManufactureData * Packet = (ManufactureData *) data;
    long Action = ntohl(Packet->Data);
	LogDebug("ManufactureAction: %d\n", Action);

	int NextFormula = 0;
	SectorManager *sm = GetSectorManager();

	switch(ManuIndex()->GetMode())
	{
		case MODE_MANUFACTURE:
			switch(Action)
			{
				case ACTION_LEAVE_TERMINAL:
					// Setup Categorys
					for(int p=0;p<2;p++)
					{
						for(int c=0;c<5;c++)
						{
							for(int s=0;s<5;s++)
							{
								ManuIndex()->PrimaryCategories.PrimaryCategory[p].Categories.Category[c].SubCategories.SubCategory[s].SetIsVisible(true);
							}
						}
					}

					ManuIndex()->SetTechFilterBitField(0x03FF);
					SendAuxManu();
					break;

				case ACTION_REFINE:
					// Check for components again
					if (!HasComponents())
					{
						ManuIndex()->SetValidity(VALIDITY_MISSING_COMPONENTS);
						ManuIndex()->SetFailureMessage("Missing Component(s)");
						SendAuxManu();
						return;
					}
					// check the component qualities before they get removed!
					m_base_quality = CalculateAverageComponentQuality();
					// Get the trade stack xp from the components
					m_tradeStackXP = GetComponentTradeStackXP();

					// Remove components and credits and set a time to give item
					RemoveComponentsFromInventory();
					PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - ManuIndex()->GetNegotiatedCost());
					SaveCreditLevel();
					SendAuxPlayer();
					SendAuxShip();
					if (sm) sm->AddTimedCall(this, B_MANUFACTURE_ACTION, 3000, 0, MODE_MANUFACTURE);
					m_Manufacturing = true;
					break;

				default:
					LogMessage("ManufactureAction - Unknown Action: %d\n",Action);
					break;
			}
			break;

		case MODE_DISMANTLE:
		case MODE_ANALIZE:
			long ItemID;
			switch (Action)
			{
				case ACTION_LEAVE_TERMINAL:
					if (ManuIndex()->GetValidity() != VALIDITY_ATTEMPT_NEAR_SUCCESS)
					{
						// Move items to inventory, getting here without enough space in inventory is very bad!
						if ((ManuIndex()->GetValidity() >= VALIDITY_ATTEMPT_NORMAL_SUCCESS && ManuIndex()->GetMode() == MODE_DISMANTLE) ||
							(ManuIndex()->GetValidity() == VALIDITY_ATTEMPT_CRITICAL_SUCCESS && ManuIndex()->GetMode() == MODE_ANALIZE))
						{
							// Send the items to your inventory
							for(int item=0;item<6;item++)
							{
								if (ManuIndex()->Components.Item[item].GetItemTemplateID() != -1)
								{
									CargoAddItem(ManuIndex()->Components.Item[item].GetItemTemplateID(), 1);
									ManuIndex()->Components.Item[item].SetItemTemplateID(-1);
								}
							}
						}
						else
						{
							// Move the actual item to your cargo
							if (ManuIndex()->Target.Item[0].GetItemTemplateID() != -1)
							{
								CargoAddItem(ManuIndex()->Target.Item[0].GetData());
								ManuIndex()->Target.Item[0].Empty();
							}
						}
						m_ItemsWaiting = false;
						ManuIndex()->SetFailureMessage("");
						ManuIndex()->SetValidity(VALIDITY_NO_TARGET);
						SendAuxShip();
						SendAuxManu();
					}
					break;

				case ACTION_REFINE:
					// Locate all the components and show them up
					ItemID = ManuIndex()->Target.Item[0].GetItemTemplateID();
					if (ItemID != -1)
					{
						// Take their credits and save them
						PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - ManuIndex()->GetNegotiatedCost());
						SaveCreditLevel();

						if (sm) sm->AddTimedCall(this, B_MANUFACTURE_ACTION, 2000, 0, ManuIndex()->GetMode());
						m_Manufacturing = true;
					}
					break;

				case ACTION_RETRY:
					SendClientSound("analyze_1.wav");
					if (sm) sm->AddTimedCall(this, B_MANUFACTURE_ACTION, 2000, 0, ManuIndex()->GetMode());
					break;

				default:
					LogMessage("ManufactureAction - Unknown Action: %d\n",Action);
					break;
			}
			break;

		case MODE_REFINE:
			switch (Action)
			{
				case ACTION_LEAVE_TERMINAL:
					break;

				case ACTION_REFINE:
					// Check for components again
					if (!HasComponents())
					{
						ManuIndex()->SetValidity(VALIDITY_MISSING_COMPONENTS);
						ManuIndex()->SetFailureMessage("Missing Component(s)");
						SendAuxManu();
						return;
					}

					// Remove components and credits and set a time to give item
					RemoveComponentsFromInventory();
					PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - ManuIndex()->GetNegotiatedCost());
					SaveCreditLevel();
					SendAuxPlayer();
					SendAuxShip();
					if (sm) sm->AddTimedCall(this, B_MANUFACTURE_ACTION, 3000, 0, MODE_REFINE);
					m_Manufacturing = true;
					break;

				case ACTION_REFINE_STACK:
					// Check for components again
					if (!HasComponents(ManuIndex()->GetAdditionalIterations() + 1))
					{
						ManuIndex()->SetValidity(VALIDITY_MISSING_COMPONENTS);
						ManuIndex()->SetFailureMessage("Missing Component(s)");
						SendAuxManu();
						return;
					}

					if (PlayerIndex()->GetCredits() < ManuIndex()->GetNegotiatedCost() * (ManuIndex()->GetAdditionalIterations() + 1))
					{
						ManuIndex()->SetValidity(VALIDITY_INSUFFICIENT_CREDITS);
						ManuIndex()->SetFailureMessage("Insufficient Credits");
						SendAuxManu();
						return;
					}

					// Remove components and credits and set a time to give item
					RemoveComponentsFromInventory(ManuIndex()->GetAdditionalIterations() + 1);    
					PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - ManuIndex()->GetNegotiatedCost() * (ManuIndex()->GetAdditionalIterations() + 1));
					SaveCreditLevel();
					SendAuxPlayer();
					SendAuxShip();
					if (sm) sm->AddTimedCall(this, B_MANUFACTURE_ACTION, 3000, 0, MODE_REFINE_STACK);
					m_Manufacturing = true;
					break;
				default:
					LogMessage("ManufactureAction - Unknown Action: %d\n",Action);
					break;
			}
			break;
		default:
			LogMessage("ManufactureAction - Unknown Action: %d\n", ManuIndex()->GetMode());
			break;
	}
}

void Player::HandleManufactureLevelFilter(unsigned char *data)
{
    ManufactureTechLevelFilter * Filter = (ManufactureTechLevelFilter *) data;
    char Enable   = Filter->Enable;
    long BitField = ntohl(Filter->BitField);

    LogDebug("ManufactureLevelFilter - Enable: %d BitField %d\n",Enable,BitField);
    if (Enable)
    {
        ManuIndex()->SetTechFilterBitField(ManuIndex()->GetTechFilterBitField() | BitField);
    }
    else
    {
        ManuIndex()->SetTechFilterBitField(ManuIndex()->GetTechFilterBitField() & ~BitField);
    }
    LogDebug("Current Filter: %d\n",ManuIndex()->GetTechFilterBitField());
    BuildManufactureList();
}

void Player::BuildManufactureList()
{
    long Category = ManuIndex()->GetCurrentItemCat();
	u32 TechFilter = ManuIndex()->GetTechFilterBitField();

	// clear previous formulas (refine perhaps?)
	ManuIndex()->KnownFormulas.ResetKnownFormulas(false);
	m_NumFormulas = 0;

	for(mapManu::const_iterator KnownItem = m_ManuRecipes.begin(); KnownItem != m_ManuRecipes.end(); ++KnownItem)
	{
		ItemBase * iBase = g_ItemBaseMgr->GetItem(KnownItem->first);	// Look at item
		if (iBase && iBase->SubCategory() == Category && (TechFilter & (1<<(iBase->TechLevel()-1))) && m_NumFormulas < MAX_KNOWN_FORMULAS)
		{
			int ItemID = KnownItem->first;
			ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetItemName(iBase->Name());
			ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetTechLevel(iBase->TechLevel());
			ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetItemID(ItemID);
			SendItemBase(ItemID);
			m_NumFormulas++;
		}
	}

	// if the list was bigger, force a clear now, before the count is reset to the lower value
	if (ManuIndex()->KnownFormulas.GetKnownFormulas() > m_NumFormulas)
	{
		SendAuxManu();
		ManuIndex()->KnownFormulas.SetKnownFormulas(m_NumFormulas);
	}
	else
	{
		ManuIndex()->KnownFormulas.SetKnownFormulas(m_NumFormulas);
		SendAuxManu();
	}
}

void Player::BuildRefineList()
{
    unsigned int i,j,k;
    bool DontAdd;
    ItemBase * ItemData = 0;
    ItemBase * RefineItemData = 0;
	int Component;

	// clear the known formulas list (manufacture items in the refine terminal)
	ManuIndex()->KnownFormulas.ResetKnownFormulas(false);
    m_NumFormulas = 0;

	//Ensure that only those with the prospect skill can use the refining station.
	int RefineSkill = PlayerIndex()->RPGInfo.Skills.Skill[SKILL_PROSPECT].GetLevel();
	if(RefineSkill <= 0)
	{
		return;
	}

    for (i=0;i<40;i++)
    {
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() > 0)
        {
            ItemData = g_ItemBaseMgr->GetItem(ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID());
            if (ItemData)
            {
				for (k=0;k<MAX_REFINE_TO;k++)
				{
					int into = ItemData->RefinesInto(k);
					if (into)
					{
						if (RefineItemData = g_ItemBaseMgr->GetItem(into))
						{
							if( (RefineSkill < 6 && RefineSkill < ItemData->TechLevel()) ||
								(RefineSkill == 6 && ItemData->TechLevel() > 7) )
							{
								continue;
							}

							DontAdd = false;

							// check that this formula is not already added
							for (j=0;j<m_NumFormulas;j++)
							{
								if (ManuIndex()->KnownFormulas.Formula[j].GetItemID() == into)
								{
									DontAdd = true;
									break;
								}
							}

							// check all components exist (complex formulae)
							if (!DontAdd)
							{
								for (j=0;j<6;j++)
								{
									Component = RefineItemData->Component(j);
									if (Component > 0 && Component != ItemData->ItemTemplateID() && CargoItemCount(Component) == 0)
									{
										DontAdd = true;
										break;
									}
								}
							}

							if (!DontAdd && m_NumFormulas < MAX_KNOWN_FORMULAS)
							{
								ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetItemName(RefineItemData->Name());
								ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetItemID(RefineItemData->ItemTemplateID());
								ManuIndex()->KnownFormulas.Formula[m_NumFormulas].SetTechLevel(RefineItemData->TechLevel());
								m_NumFormulas++;
							}
						}
					}
					else
					{
						break;
					}
				}
			}
        }
    }
	// if the list was bigger, force a clear now, before the count is reset to the lower value
	if (ManuIndex()->KnownFormulas.GetKnownFormulas() > m_NumFormulas)
	{
		SendAuxManu();
		ManuIndex()->KnownFormulas.SetKnownFormulas(m_NumFormulas);
	}
	else
	{
		ManuIndex()->KnownFormulas.SetKnownFormulas(m_NumFormulas);
		SendAuxManu();
	}
}

//used to sort the dismantled items by price
class dismantleComp
{
public:
	dismantleComp(int i,u64 p):index(i),price(p){}
	int index;
	u64 price;
	bool operator<(const dismantleComp& rhs)
	{
		return(price < rhs.price);
	}
};

void Player::ManufactureTimedReturn(long Action)
{
    // We just came back from a manufacturing action
    LogDebug("ManufactureTimedReturn - Action: %d\n",Action);
    char msg_buffer[128];
    m_Manufacturing = false;
	short xp = 0;
	float ItemDamage = 0.0f;

    switch (Action)
    {
	case MODE_ANALIZE:
	case MODE_DISMANTLE:
		if (m_CurManuItem)
		{
			s32 ItemID = m_CurManuItem->ItemTemplateID();
			// See if this Item is known
			
			float roll = ((rand()%10000)+1)/100.0f;
			float successChance = ManuIndex()->GetSuccessProbability() * 100.0f;
			float critSuccessChance;
			float nearSuccess;
			ItemDamage = (((rand()%16)+10.0f)/100.0f);

			if (m_ManuRecipes.find(ItemID) == m_ManuRecipes.end())
			{
				critSuccessChance = ManuIndex()->GetCriticalSuccessProbability() * 100.0f;
				nearSuccess = successChance+((100.0f-successChance) * 0.5f);
				if(roll <= successChance)
				{
					// Analyze
					for(int cID = 0; cID < 6; cID++)
					{
						// show what this item is made from in the window
						long cItemID = m_CurManuItem->Component(cID);
						ManuIndex()->Components.Item[cID].SetItemTemplateID(cItemID);
						SendItemBase(cItemID);
					}
					sprintf_s(msg_buffer, sizeof(msg_buffer), "%s analyzed %s", Name(), m_CurManuItem->Name());
					SendMessageStringToGroup(msg_buffer);

					short xp2 = CalcAnalyzingXP(m_CurManuItem->TechLevel());
					AwardExploreXP(msg_buffer, xp2);
					m_ManuRecipes.insert(pair<int,int>(ItemID,1));
					SaveNewRecipe(ItemID);	// Save this into the database
					CheckMissions(0, ItemID, 0, OBTAIN_BLUEPRINT);
					
					ManuIndex()->Target.Item[0].Empty(); // Delete the item you analyzed

					if(roll <= critSuccessChance)
					{
						//critical success, return components
						m_ItemsWaiting = true;
						ManuIndex()->SetValidity(VALIDITY_ATTEMPT_CRITICAL_SUCCESS);
						ManuIndex()->SetFailureMessage("Analyze critically successful!");
					}
					else
					{
						ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NORMAL_SUCCESS);
						ManuIndex()->SetFailureMessage("Analyze successful");
					}
				}
				else if(roll <= nearSuccess && ManuIndex()->Target.Item[0].GetQuality()>ItemDamage)
				{
					ManuIndex()->Target.Item[0].SetQuality(ManuIndex()->Target.Item[0].GetQuality() - ItemDamage);
					ManuIndex()->SetSuccessProbability(ManuIndex()->GetSuccessProbability()-ItemDamage*0.2f);
					ManuIndex()->SetValidity(VALIDITY_VALID); // force changed state
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NEAR_SUCCESS);
					ManuIndex()->SetFailureMessage("Analyze nearly successful.");
				}
				else
				{
					ManuIndex()->Target.Item[0].Empty(); // Delete the item you analyzed
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_TOTAL_FAILURE);
					ManuIndex()->SetFailureMessage("Analyze failed");
				}
			}
			else
			{
				// Dismantling item (changed so that items are deleted from the component window if they fail
				// leave terminal (or inv button) to receive items)
				critSuccessChance = ManuIndex()->GetCriticalSuccessProbability() * 100.0f;
				nearSuccess = 100.0f - ((100.0f-successChance) * 0.1f);
				if (roll <= critSuccessChance)
				{
					m_ItemsWaiting = true;
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_CRITICAL_SUCCESS);
					ManuIndex()->SetFailureMessage("Dismantle critical success!");
					ManuIndex()->Target.Item[0].Empty(); 

					sprintf_s(msg_buffer, sizeof(msg_buffer), "%s dismantled %s", Name(), m_CurManuItem->Name());
					SendMessageStringToGroup(msg_buffer);
				}
				else if(roll <= successChance)
				{
					int item;
					long cItemID;
					std::vector<dismantleComp> compList;
					for(item=0;item<6;item++)
					{
						cItemID = ManuIndex()->Components.Item[item].GetItemTemplateID();
						if(cItemID != -1 && g_ItemBaseMgr)
						{
							ItemBase * iBase = g_ItemBaseMgr->GetItem(cItemID);
							if(iBase)
							{
								dismantleComp comp(item,iBase->Cost());
								compList.push_back(comp);
							}
						}
					}
					if (compList.size() == 0) return;

					//oh god, this is going to crash ... and has
					std::sort(compList.rbegin(),compList.rend());
					std::vector<dismantleComp>::iterator shuffleStart = compList.begin();
					shuffleStart++;
					std::random_shuffle(shuffleStart,compList.end());
					float chance = 100.0f;
					float step = 20.0f;;
					float roll2;
					for(std::vector<dismantleComp>::iterator iter = compList.begin();
						iter != compList.end();
						iter++)

					{
						roll2 = ((rand()%10000)+1)/100.0f;
						if(roll2 > chance)
						{
							ManuIndex()->Components.Item[iter->index].SetItemTemplateID(-1);
						}
						chance -= step;
						step *= 0.5f;
					}
					ManuIndex()->Target.Item[0].Empty(); 
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NORMAL_SUCCESS);
					ManuIndex()->SetFailureMessage("Dismantle successful.");
					m_ItemsWaiting = true;

					sprintf_s(msg_buffer, sizeof(msg_buffer), "%s dismantled %s", Name(), m_CurManuItem->Name());
					SendMessageStringToGroup(msg_buffer);
				}
				else if(roll <= nearSuccess && ManuIndex()->Target.Item[0].GetQuality()>ItemDamage)
				{
					ManuIndex()->Target.Item[0].SetQuality(ManuIndex()->Target.Item[0].GetQuality()-ItemDamage);
					ManuIndex()->SetSuccessProbability(ManuIndex()->GetSuccessProbability()-ItemDamage*0.2f);
					ManuIndex()->SetValidity(VALIDITY_VALID); // force changed state
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NEAR_SUCCESS);
					ManuIndex()->SetFailureMessage("Dismantle nearly successful.");
				}
				else
				{
					ManuIndex()->SetValidity(VALIDITY_ATTEMPT_TOTAL_FAILURE);
					ManuIndex()->SetFailureMessage("Dismantle total failure");
					for(int item=0;item<6;item++)
					{
						long cItemID = ManuIndex()->Components.Item[item].GetItemTemplateID();
						if( cItemID != -1 )
						{
							ManuIndex()->Components.Item[item].SetItemTemplateID(-1);
						}
					}
					ManuIndex()->Target.Item[0].Empty(); 
					m_ItemsWaiting = false;
				}
			}
			
			SendAuxPlayer();
			SendAuxShip();
			SendAuxManu();
		}
		break;

	case MODE_MANUFACTURE:
	case MODE_REFINE:
		if (m_CurManuItem)
		{
			if (!m_CurManuItem->Data() || !m_CurManuItem->Name())
			{
				LogMessage("ERROR: Manufacturing inaccessible item.");
				ManuIndex()->SetValidity(VALIDITY_NO_TARGET);
				ManuIndex()->SetFailureMessage("ERROR: Please report.");
				SendAuxManu();
				break;
			}

			// See if we are manufacturing
			if (Action == MODE_MANUFACTURE)
			{
				_Item TempData;
				memset(&TempData, 0, sizeof(_Item));
				TempData.ItemTemplateID = ManuIndex()->Target.Item[0].GetItemTemplateID();

				// See if we have components
				// If so we do not stack this item
				if (m_CurManuItem->ItemType() == 13)		// Components
				{
					TempData.StackCount = 1;
				}
				else
				{
					TempData.StackCount = m_CurManuItem->MaxStack();
				}

				// moved the ammo check into the function
				float minq,maxq;
				TempData.Quality = CalculateBuiltItemQuality(&minq,&maxq);	
				TempData.Structure = 1.0f;
				sprintf_s(TempData.BuilderName, sizeof(TempData.BuilderName), "%s", this->Name()); // Copy Builder name
				TempData.TradeStack = 0;
				CargoAddItem(&TempData);

				sprintf_s(msg_buffer, sizeof(msg_buffer), "%s manufactured a %d%% %s", Name(), (int)(TempData.Quality * 100.0f), m_CurManuItem->Name());
				SendMessageStringToGroup(msg_buffer);
				SaveManufactureAttempt(TempData.ItemTemplateID , TempData.Quality);
			}
			else
			{
				CargoAddItem(ManuIndex()->Target.Item[0].GetItemTemplateID(), 1, 1);

				sprintf_s(msg_buffer, sizeof(msg_buffer), "%s refined %s", Name(), m_CurManuItem->Name());
				SendMessageStringToGroup(msg_buffer);
			}

			xp = CalcRefineXP(m_CurManuItem->TechLevel());
			AwardTradeXP(msg_buffer, xp);

			ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NORMAL_SUCCESS);
			SendAuxPlayer();
			SendAuxShip();
			SendAuxManu();
		}
		break;

	case MODE_REFINE_STACK:
		if (m_CurManuItem)
		{
			if (!m_CurManuItem->Data() || !m_CurManuItem->Name())
			{
				LogMessage("ERROR: Manufacturing inaccessible item.");
				ManuIndex()->SetValidity(VALIDITY_NO_TARGET);
				ManuIndex()->SetFailureMessage("ERROR: Please report.");
				SendAuxManu();
				break;
			}

			int count = ManuIndex()->GetAdditionalIterations() + 1;
			sprintf_s(msg_buffer, sizeof(msg_buffer), "%s refined %d %s", Name(), count, m_CurManuItem->Name());
			SendMessageStringToGroup(msg_buffer);

			xp = CalcRefineXP(m_CurManuItem->TechLevel());
			AwardTradeXP(msg_buffer,xp * count);
			CargoAddItem(ManuIndex()->Target.Item[0].GetItemTemplateID(), count, count);
			ManuIndex()->SetValidity(VALIDITY_ATTEMPT_NORMAL_SUCCESS);
			SendAuxPlayer();
			SendAuxShip();
			SendAuxManu();
		}
		break;

	default:
		LogMessage("ManufactureTimedReturn - Unknown Action: %d\n",Action);
		break;
    }
}

void Player::CheckItemRequirements()
{
    int i, j, ItemCount = 0;
    bool HaveComponents = true;

    for (i=0;i<6;i++)
    {
        if (ManuIndex()->Components.Item[i].GetItemTemplateID() > 0)
        {
            ItemCount = CargoItemCount(ManuIndex()->Components.Item[i].GetItemTemplateID());

            for (j=0;j<i;j++)
            {
                if (ManuIndex()->Components.Item[j].GetItemTemplateID() == ManuIndex()->Components.Item[i].GetItemTemplateID())
                {
                    ItemCount--;
                }
            }
            
            if (ItemCount > 0)
            {
                ManuIndex()->Components.Item[i].SetStackCount(1);            
            }
            else
            {
                HaveComponents = false;
                ManuIndex()->Components.Item[i].SetStackCount(0);            
            }
        }
        else
        {
            ManuIndex()->Components.Item[i].SetStackCount(0);
        }
    }

    ManuIndex()->Target.Item[0].SetStackCount(HaveComponents ? 1 : 0);

    if (!HaveComponents)
    {
        ManuIndex()->SetValidity(VALIDITY_MISSING_COMPONENTS);
        ManuIndex()->SetFailureMessage("Missing Component(s)");
    }
    else if (ManuIndex()->GetNegotiatedCost() > PlayerIndex()->GetCredits())
    {
        ManuIndex()->SetValidity(VALIDITY_INSUFFICIENT_CREDITS);
        ManuIndex()->SetFailureMessage("Insufficient Credits");
    }
    else if (CanCargoAddItem(ManuIndex()->Target.Item[0].GetItemTemplateID(), 1) != 1)
    {
        ManuIndex()->SetValidity(VALIDITY_INSUFFICIENT_CARGO);
        ManuIndex()->SetFailureMessage("Inventory Full");
    }
	else if (!CanHaveAnotherOf(ManuIndex()->Target.Item[0].GetItemTemplateID()))
	{
        ManuIndex()->SetValidity(VALIDITY_DUPLICATE_UNIQUE_ITEM);
        ManuIndex()->SetFailureMessage("Duplicate Unique Item");
	}
    else
    {
        ManuIndex()->SetValidity(VALIDITY_VALID);
        ManuIndex()->SetFailureMessage("");
    }

    if (ManuIndex()->GetMode() == MODE_REFINE)
    {
        SetRefineIterations();
    }
}

void Player::SetRefineIterations()
{
    // This is assuming that refining always takes the SAME item
    // Example 2 Iron Ore -> 1 Iron
    // Never 2 Iron Ore + 1 Catalyst

    long QuantityNeededToRefine = 0;
	long MaxOreRefineRuns = 0, MaxRefineRunsByCash = 0, MaxRefineRunsByCargo = 0;

    for (int i=0;i<6;i++)
    {
        if (ManuIndex()->Components.Item[i].GetItemTemplateID() > 0)
        {
            QuantityNeededToRefine++;
        }
    }
    
    if (QuantityNeededToRefine == 0)
    {
        LogMessage("SetRefineIterations - QuantityNeededToRefine = 0\n");
        return;
    }

    // Get the maximum iterations
    MaxOreRefineRuns = (CargoItemCount(ManuIndex()->Components.Item[0].GetItemTemplateID()) / QuantityNeededToRefine) - 1;
    
	// Now check for room
    MaxRefineRunsByCargo = CargoAddItemCount(ManuIndex()->Target.Item[0].GetItemTemplateID()) - 1;

	// Now check how many runs the player can afford

	//do a sanity check.  The cost should never be zero.

	MaxRefineRunsByCash = long(PlayerIndex()->GetCredits() / ManuIndex()->GetNegotiatedCost() - 1);
	

	//If we can't afford / don't have room for all the refines we could do...
	if(MaxRefineRunsByCargo < MaxOreRefineRuns || MaxRefineRunsByCash < MaxOreRefineRuns)
	{
		//pick the lowest number of runs, since that is the greatest common divisor.
		if(MaxRefineRunsByCargo > MaxRefineRunsByCash)
			ManuIndex()->SetAdditionalIterations(MaxRefineRunsByCash);
		else
			ManuIndex()->SetAdditionalIterations(MaxRefineRunsByCargo);
	}
	else //otherwise, do all runs.
		ManuIndex()->SetAdditionalIterations(MaxOreRefineRuns);
}

void Player::ResetManuItems()
{
	ManuIndex()->ResetManuItems();
}

void Player::RemoveComponentsFromInventory(long Multiplier)
{
    for (int i=0;i<6;i++)
    {
        if (ManuIndex()->Components.Item[i].GetItemTemplateID() > 0)
        {
            CargoRemoveItem(ManuIndex()->Components.Item[i].GetItemTemplateID(), Multiplier);
        }
    }
}

bool Player::HasComponents(long Multiplier)
{
    int i, j, ItemCount;
    for (i=0;i<6;i++)
    {
        if (ManuIndex()->Components.Item[i].GetItemTemplateID() > 0)
        {
            ItemCount = CargoItemCount(ManuIndex()->Components.Item[i].GetItemTemplateID());

            for (j=0;j<i;j++)
            {
                if (ManuIndex()->Components.Item[j].GetItemTemplateID() == ManuIndex()->Components.Item[i].GetItemTemplateID())
                {
                    ItemCount -= Multiplier;
                }
            }

            if (ItemCount <= 0)
            {
                return false;
            }
        }
    }

    return true;
}




bool Player::AllowManufacture(ItemBase * iBase)
{
	if (!iBase)
		return false;

	AuxSkill * Skills = &PlayerIndex()->RPGInfo.Skills.Skill[0];

	// force an aux send if consecutive items have the same error
	ManuIndex()->SetValidity(VALIDITY_NO_TARGET);

	// Non-manufacturable
	if ((iBase->Flags() & 128))
	{
		ManuIndex()->SetValidity(VALIDITY_NOT_MANUFACTURABLE);
		ManuIndex()->SetFailureMessage("This item is non-manufacturable");
		return false;
	}

	ItemRequirements Req = iBase->GetItemRequirements();

    // Now check for race restrictions, terrans can build everything, everyone can build terran items
    if ((Req.RaceRestriction & (0x01 << Race())) && Race() != RACE_TERRAN && Req.RaceRestriction != 6)
    {
		ManuIndex()->SetValidity(VALIDITY_NOT_QUALIFIED);
		ManuIndex()->SetFailureMessage("Your Race can not analyze this item");
        return false;
    }

	int SkillLevel = 0;
	// See if we can dismantle
	switch(iBase->SubCategory())
	{
		// This is a weapon type
		case 100:
		case 101:
		case 102:
		case 103:
			SkillLevel = Skills[SKILL_BUILD_WEAPONS].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		// This is a engine type
		case 121:
			SkillLevel = Skills[SKILL_BUILD_ENGINES].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		// This is a reactor type
		case 120:
			SkillLevel = Skills[SKILL_BUILD_REACTORS].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		// This is a shield type
		case 122:
			SkillLevel = Skills[SKILL_BUILD_SHIELDS].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		// This is a device type
		case 110:
			SkillLevel = Skills[SKILL_BUILD_DEVICES].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		// This is a component	
		case 140:
		case 141:
		case 142:
		case 150:
		case 151:
		case 152:
		case 153:
		case 160:
		case 161:
		case 162:
		case 163:
		case 170:
		case 171:
		case 172:
		case 173:
		case 180:
		case 181:
		case 182:
		case 183:
			SkillLevel = Skills[SKILL_BUILD_COMPONENTS].GetLevel();
			if (iBase->TechLevel() > ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel)))
			{
				ManuIndex()->SetValidity(VALIDITY_TOO_DIFFICULT);
				return false;
			}
			break;

		default:
			ManuIndex()->SetValidity(VALIDITY_NOT_DISMANTABLE);
			return false;
	}
	// check for having the skill for this item
	if (SkillLevel == 0)
	{
		ManuIndex()->SetValidity(VALIDITY_NOT_MANUFACTURABLE);
		ManuIndex()->SetFailureMessage("You dont have the build skill for this item!");
		return false;
	}
	return true;
}

int Player::GetManufactureSkill(ItemBase * iBase, float *buff_bonus)
{
	AuxSkill * Skills = &PlayerIndex()->RPGInfo.Skills.Skill[0];

	int SkillLevel = 0;
	*buff_bonus = 0.0f;

	switch(iBase->SubCategory())
	{
		// This is a weapon type
		case 100:
		case 101:
		case 102:
		case 103:
			SkillLevel = Skills[SKILL_BUILD_WEAPONS].GetLevel();
			*buff_bonus = m_Stats.GetStat(STAT_SKILL_BUILD_WEAPONS);
			break;

		// This is a engine type
		case 121:
			SkillLevel = Skills[SKILL_BUILD_ENGINES].GetLevel();
			*buff_bonus = m_Stats.GetStat(STAT_SKILL_BUILD_ENGINES);
			break;

		// This is a reactor type
		case 120:
			SkillLevel = Skills[SKILL_BUILD_REACTORS].GetLevel();
			*buff_bonus = m_Stats.GetStat(STAT_SKILL_BUILD_REACTORS);
			break;

		// This is a shield type
		case 122:
			SkillLevel = Skills[SKILL_BUILD_SHIELDS].GetLevel();
			*buff_bonus = m_Stats.GetStat(STAT_SKILL_BUILD_SHIELDS);
			break;

		// This is a device type
		case 110:
			SkillLevel = Skills[SKILL_BUILD_DEVICES].GetLevel();
			*buff_bonus = m_Stats.GetStat(STAT_SKILL_BUILD_DEVICES);
			break;

		// This is a component
		case 140:
		case 141:
		case 142:
		case 150:
		case 151:
		case 152:
		case 153:
		case 160:
		case 161:
		case 162:
		case 163:
		case 170:
		case 171:
		case 172:
		case 173:
		case 180:
		case 181:
		case 182:
		case 183:
			SkillLevel = Skills[SKILL_BUILD_COMPONENTS].GetLevel();
			break;

		default:
			break;
	}
	*buff_bonus += m_Stats.GetStat(STAT_SKILL_BUILD_ALL);
	return ((SkillLevel == 6) ? 7 : ((SkillLevel == 7) ? 9 : SkillLevel));
}

int	Player::CountKnownRecipesOfSameType()
{
	int count = 0;
	bool weapon = m_CurManuItem->Category() == IB_CATEGORY_WEAPON;

	for(mapManu::const_iterator KnownItem = m_ManuRecipes.begin(); KnownItem != m_ManuRecipes.end(); ++KnownItem)
	{
		ItemBase * iBase = g_ItemBaseMgr->GetItem(KnownItem->first);
		if (iBase)
		{
			if (iBase->ItemType() == m_CurManuItem->ItemType())
				count++;
			else if (weapon && iBase->Category() == IB_CATEGORY_WEAPON)
				count++;
		}
	}
	return count;
}

// calculate the quality, quality of input components affects base quality
// eg. level 3 skill build of tech 3 item (low q input)  = 1.0  + 0   + 0.57 + ? + ? + 0..0.22 = 1.57 to 1.79
// eg. level 5 skill build of tech 3 item (low q input)  = 1.0  + 0.4 + 0.39 + ? + ? + 0..0.37 = 1.79 to 2.16
// eg. level 5 skill build of tech 5 item (high q input) = 1.41 + 0   + 0.39 + ? + ? + 0..0.37 = 1.80 to 2.17  
// eg. level 9 skill build of tech 8 item (high q input) = 1.41 + 0.2 + 0.19 + ? + ? + 0..0.75 = 1.80 to 2.65   87%
// eg. level 9 skill build of tech 9 item (low q input)  = 1.0  + 0   + 0.19 + ? + ? + 0..0.75 = 1.19 to 1.94
// eg. level 9 skill build of tech 9 item (med q input)  = 1.15 + 0   + 0.19 + ? + ? + 0..0.75 = 1.34 to 2.09
// eg. level 9 skill build of tech 9 item (high q input) = 1.41 + 0   + 0.19 + ? + ? + 0..0.75 = 1.60 to 2.35,  47% chance of 200%
float Player::CalculateBuiltItemQuality(float *min_quality,float *max_quality)
{
	// ammo and components are always 200% (or 100 difficulty items specified in the database)
	if (m_CurManuItem->ItemType() == IB_ITEMTYPE_AMMO || 
		m_CurManuItem->ItemType() == IB_ITEMTYPE_OREANDCOMPS ||
		m_CurManuItem->ManufactureDifficulty() == 100)
	{
		*min_quality = *max_quality = 2.0f;
		return 2.0f;
	}
	float buff_bonus = 0.0f;
		
	int skill = GetManufactureSkill(m_CurManuItem,&buff_bonus);
	
	if(Race() == RACE_TERRAN)
	{
		buff_bonus += (0.025f*skill);
	}
	//terran racial build bonus. 0.1 skill per rank.

	// calculate a random quality and the range
	return CalculateBuiltItemQuality(skill,m_CurManuItem->TechLevel(),m_CurManuItem->ManufactureDifficulty(),TradeLevel(),CountKnownRecipesOfSameType(),false,min_quality,max_quality,buff_bonus);
}

float Player::CalculateBuiltItemQuality(int skill,int tech_level,int difficulty,int trade_level,int recipes,bool show_calcs,float *min_quality,float *max_quality,float buff_bonus)
{
	float overlevel_bonus = (float)(skill + buff_bonus - tech_level) * 0.2f; // 0.2 to 1.6
	float simplicity_bonus = pow(0.83f,(float)skill); // 0.83 down to 0.19
	float building_bonus = (float)recipes * 0.001f * (float)(5 - skill/2); // 10% per 100 patterns @ 9, more skill requires more knowledge
	float random_bonus = (float)(rand()%(trade_level+1)) * 0.015f; // 0 to 0.75 @ max trade
	simplicity_bonus += (difficulty - 50) * 0.01f; // based on database, note: negative for hard items!

	float final_quality = m_base_quality + overlevel_bonus + simplicity_bonus + building_bonus + random_bonus;
	*min_quality = final_quality - random_bonus;
	*max_quality = *min_quality + trade_level * 0.015f;

// debug code
	if (show_calcs)
		printf("b%.2f o%.2f s%.2f b%.2f r%.2f = %.2f, min %.2f, max %.2f\n",m_base_quality,overlevel_bonus,simplicity_bonus,building_bonus,random_bonus,final_quality,*min_quality,*max_quality);

	return final_quality > 2.0f ? 2.0f : final_quality;
}

float Player::CalculateAverageComponentQuality()
{
	float item_qual,total_qual = 0;
	int count = 0;

    for (int c=0;c<6;c++)
    {
        if (ManuIndex()->Components.Item[c].GetItemTemplateID() > 0)
        {
			for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
		    {
				if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ManuIndex()->Components.Item[c].GetItemTemplateID())
		        {
					item_qual = ShipIndex()->Inventory.CargoInv.Item[i].GetQuality();
					total_qual += item_qual > 1.0f ? sqrt(item_qual) : item_qual * 1.0f;
					count++;
					break;
		        }
		    }
        }
    }
	return count ? total_qual / (float)count : 1.0f;
}

long Player::GetComponentTradeStackXP()
{
	long tradeStackXP = 0;

	for (int c=0;c<6;c++)
    {
        if (ManuIndex()->Components.Item[c].GetItemTemplateID() > 0)
        {
			for (u32 i=0; i<ShipIndex()->Inventory.GetCargoSpace(); i++)
		    {
				if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ManuIndex()->Components.Item[c].GetItemTemplateID())
		        {
					tradeStackXP += CalcItemStackTradeXP(ShipIndex()->Inventory.CargoInv.Item[i].GetData(), 1);
					break;
		        }
		    }
        }
    }
	return tradeStackXP;
}

void Player::TestBuildQualities()
{
	float minq,maxq;

	for (int s=1;s < 10;s++)
	{
		if (s == 6 || s == 8)
			continue;
		for (int r=0;r < 2;r++)
			for (int t=s-2;t <= s;t++)
			{
				if (t < 1)
					continue;
				for (int q=0;q < 2;q++)
				{
					if (t == 1 && q)
						continue;
					m_base_quality = q ? 1.41f : 1.0f;
					int recipes = r ? 1000 * s / 9 : 10;
					printf("skill %d, tech %d, %d recipes, %.2f quality input\n",s,t,recipes,m_base_quality);
					CalculateBuiltItemQuality(s,t,0,s*5+5,recipes,true,&minq,&maxq,0.0f);
				}
			}
		printf("\n");
	}
}
/* TEST RESULTS
skill 1, tech 1, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.83 b0.07 r0.15 = 2.05, min 1.90, max 2.05
skill 1, tech 1, 111 recipes, 1.00 quality input
b1.00 o0.00 s0.83 b0.80 r0.06 = 2.69, min 2.63, max 2.78

skill 2, tech 1, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.69 b0.06 r0.22 = 2.18, min 1.95, max 2.18
skill 2, tech 2, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.69 b0.06 r0.12 = 1.87, min 1.75, max 1.98
skill 2, tech 2, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.69 b0.06 r0.19 = 2.36, min 2.16, max 2.39
skill 2, tech 1, 222 recipes, 1.00 quality input
b1.00 o0.20 s0.69 b1.42 r0.06 = 3.37, min 3.31, max 3.53
skill 2, tech 2, 222 recipes, 1.00 quality input
b1.00 o0.00 s0.69 b1.42 r0.19 = 3.30, min 3.11, max 3.33
skill 2, tech 2, 222 recipes, 1.41 quality input
b1.41 o0.00 s0.69 b1.42 r0.06 = 3.58, min 3.52, max 3.74

skill 3, tech 1, 10 recipes, 1.00 quality input
b1.00 o0.40 s0.57 b0.06 r0.12 = 2.15, min 2.03, max 2.33
skill 3, tech 2, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.57 b0.06 r0.28 = 2.11, min 1.83, max 2.13
skill 3, tech 2, 10 recipes, 1.41 quality input
b1.41 o0.20 s0.57 b0.06 r0.28 = 2.52, min 2.24, max 2.54
skill 3, tech 3, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.57 b0.06 r0.03 = 1.66, min 1.63, max 1.93
skill 3, tech 3, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.57 b0.06 r0.18 = 2.22, min 2.04, max 2.34
skill 3, tech 1, 333 recipes, 1.00 quality input
b1.00 o0.40 s0.57 b1.86 r0.00 = 3.84, min 3.84, max 4.14
skill 3, tech 2, 333 recipes, 1.00 quality input
b1.00 o0.20 s0.57 b1.86 r0.22 = 3.86, min 3.64, max 3.94
skill 3, tech 2, 333 recipes, 1.41 quality input
b1.41 o0.20 s0.57 b1.86 r0.00 = 4.05, min 4.05, max 4.35
skill 3, tech 3, 333 recipes, 1.00 quality input
b1.00 o0.00 s0.57 b1.86 r0.01 = 3.45, min 3.44, max 3.74
skill 3, tech 3, 333 recipes, 1.41 quality input
b1.41 o0.00 s0.57 b1.86 r0.13 = 3.98, min 3.85, max 4.15

skill 4, tech 2, 10 recipes, 1.00 quality input
b1.00 o0.40 s0.47 b0.05 r0.22 = 2.15, min 1.92, max 2.30
skill 4, tech 2, 10 recipes, 1.41 quality input
b1.41 o0.40 s0.47 b0.05 r0.00 = 2.33, min 2.33, max 2.71
skill 4, tech 3, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.47 b0.05 r0.25 = 1.98, min 1.72, max 2.10
skill 4, tech 3, 10 recipes, 1.41 quality input
b1.41 o0.20 s0.47 b0.05 r0.18 = 2.31, min 2.13, max 2.51
skill 4, tech 4, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.47 b0.05 r0.21 = 1.73, min 1.52, max 1.90
skill 4, tech 4, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.47 b0.05 r0.33 = 2.26, min 1.93, max 2.31
skill 4, tech 2, 444 recipes, 1.00 quality input
b1.00 o0.40 s0.47 b2.13 r0.38 = 4.38, min 4.01, max 4.38
skill 4, tech 2, 444 recipes, 1.41 quality input
b1.41 o0.40 s0.47 b2.13 r0.04 = 4.46, min 4.42, max 4.79
skill 4, tech 3, 444 recipes, 1.00 quality input
b1.00 o0.20 s0.47 b2.13 r0.24 = 4.05, min 3.81, max 4.18
skill 4, tech 3, 444 recipes, 1.41 quality input
b1.41 o0.20 s0.47 b2.13 r0.36 = 4.58, min 4.22, max 4.59
skill 4, tech 4, 444 recipes, 1.00 quality input
b1.00 o0.00 s0.47 b2.13 r0.21 = 3.82, min 3.61, max 3.98
skill 4, tech 4, 444 recipes, 1.41 quality input
b1.41 o0.00 s0.47 b2.13 r0.34 = 4.36, min 4.02, max 4.39

skill 5, tech 3, 10 recipes, 1.00 quality input
b1.00 o0.40 s0.39 b0.04 r0.01 = 1.85, min 1.83, max 2.28
skill 5, tech 3, 10 recipes, 1.41 quality input
b1.41 o0.40 s0.39 b0.04 r0.41 = 2.65, min 2.24, max 2.69
skill 5, tech 4, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.39 b0.04 r0.16 = 1.80, min 1.63, max 2.08
skill 5, tech 4, 10 recipes, 1.41 quality input
b1.41 o0.20 s0.39 b0.04 r0.31 = 2.36, min 2.04, max 2.49
skill 5, tech 5, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.39 b0.04 r0.45 = 1.88, min 1.43, max 1.88
skill 5, tech 5, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.39 b0.04 r0.06 = 1.90, min 1.84, max 2.29
skill 5, tech 3, 555 recipes, 1.00 quality input
b1.00 o0.40 s0.39 b2.22 r0.00 = 4.01, min 4.01, max 4.46
skill 5, tech 3, 555 recipes, 1.41 quality input
b1.41 o0.40 s0.39 b2.22 r0.03 = 4.45, min 4.42, max 4.87
skill 5, tech 4, 555 recipes, 1.00 quality input
b1.00 o0.20 s0.39 b2.22 r0.18 = 3.99, min 3.81, max 4.26
skill 5, tech 4, 555 recipes, 1.41 quality input
b1.41 o0.20 s0.39 b2.22 r0.09 = 4.31, min 4.22, max 4.67
skill 5, tech 5, 555 recipes, 1.00 quality input
b1.00 o0.00 s0.39 b2.22 r0.10 = 3.72, min 3.61, max 4.06
skill 5, tech 5, 555 recipes, 1.41 quality input
b1.41 o0.00 s0.39 b2.22 r0.04 = 4.07, min 4.02, max 4.47

skill 7, tech 5, 10 recipes, 1.00 quality input
b1.00 o0.40 s0.27 b0.02 r0.34 = 2.04, min 1.70, max 2.30
skill 7, tech 5, 10 recipes, 1.41 quality input
b1.41 o0.40 s0.27 b0.02 r0.22 = 2.33, min 2.11, max 2.71
skill 7, tech 6, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.27 b0.02 r0.42 = 1.92, min 1.50, max 2.10
skill 7, tech 6, 10 recipes, 1.41 quality input
b1.41 o0.20 s0.27 b0.02 r0.31 = 2.22, min 1.91, max 2.51
skill 7, tech 7, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.27 b0.02 r0.58 = 1.88, min 1.30, max 1.90
skill 7, tech 7, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.27 b0.02 r0.21 = 1.92, min 1.71, max 2.31
skill 7, tech 5, 777 recipes, 1.00 quality input
b1.00 o0.40 s0.27 b1.86 r0.60 = 4.14, min 3.54, max 4.14
skill 7, tech 5, 777 recipes, 1.41 quality input
b1.41 o0.40 s0.27 b1.86 r0.52 = 4.47, min 3.95, max 4.55
skill 7, tech 6, 777 recipes, 1.00 quality input
b1.00 o0.20 s0.27 b1.86 r0.49 = 3.83, min 3.34, max 3.94
skill 7, tech 6, 777 recipes, 1.41 quality input
b1.41 o0.20 s0.27 b1.86 r0.10 = 3.85, min 3.75, max 4.35
skill 7, tech 7, 777 recipes, 1.00 quality input
b1.00 o0.00 s0.27 b1.86 r0.60 = 3.74, min 3.14, max 3.74
skill 7, tech 7, 777 recipes, 1.41 quality input
b1.41 o0.00 s0.27 b1.86 r0.44 = 3.98, min 3.55, max 4.15

skill 9, tech 7, 10 recipes, 1.00 quality input
b1.00 o0.40 s0.19 b0.01 r0.21 = 1.80, min 1.59, max 2.34
skill 9, tech 7, 10 recipes, 1.41 quality input
b1.41 o0.40 s0.19 b0.01 r0.01 = 2.02, min 2.00, max 2.75
skill 9, tech 8, 10 recipes, 1.00 quality input
b1.00 o0.20 s0.19 b0.01 r0.36 = 1.75, min 1.39, max 2.14
skill 9, tech 8, 10 recipes, 1.41 quality input
b1.41 o0.20 s0.19 b0.01 r0.60 = 2.40, min 1.80, max 2.55
skill 9, tech 9, 10 recipes, 1.00 quality input
b1.00 o0.00 s0.19 b0.01 r0.56 = 1.75, min 1.19, max 1.94
skill 9, tech 9, 10 recipes, 1.41 quality input
b1.41 o0.00 s0.19 b0.01 r0.57 = 2.17, min 1.60, max 2.35
skill 9, tech 7, 1000 recipes, 1.00 quality input
b1.00 o0.40 s0.19 b0.80 r0.62 = 3.00, min 2.39, max 3.14
skill 9, tech 7, 1000 recipes, 1.41 quality input
b1.41 o0.40 s0.19 b0.80 r0.07 = 2.87, min 2.80, max 3.55
skill 9, tech 8, 1000 recipes, 1.00 quality input
b1.00 o0.20 s0.19 b0.80 r0.09 = 2.28, min 2.19, max 2.94
skill 9, tech 8, 1000 recipes, 1.41 quality input
b1.41 o0.20 s0.19 b0.80 r0.52 = 3.12, min 2.60, max 3.35
skill 9, tech 9, 1000 recipes, 1.00 quality input
b1.00 o0.00 s0.19 b0.80 r0.66 = 2.65, min 1.99, max 2.74
skill 9, tech 9, 1000 recipes, 1.41 quality input
b1.41 o0.00 s0.19 b0.80 r0.10 = 2.50, min 2.40, max 3.15
*/
