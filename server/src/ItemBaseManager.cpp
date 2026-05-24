// ItemBaseManager.cpp
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

#include "ItemBaseManager.h"
#include "ItemBaseParser.h"
#include "ItemList.h"
#include "Opcodes.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ResourceClass.h"
#include "FieldClass.h"

ItemBaseManager::ItemBaseManager()
{
	memset(&EmptyItem, 0, sizeof(_Item));
	memset(&InvisibleItem, 0, sizeof(_Item));
    EmptyItem.ItemTemplateID = -1;
	InvisibleItem.ItemTemplateID = -2;

    m_ItemCount = 0;

    m_ItemDB = 0;
    m_ItemList = 0;
    m_HashTable.Table = 0;
	m_MaxItemID = 0;

    if ((m_ItemDB = new ItemBase * [MAX_ITEMBASE_ITEMS]) == NULL)
    {
        printf("ItemBaseManager - Unable to allocate `m_ItemDB`\n");
        exit(EXIT_FAILURE);
    }

    memset(m_ItemDB, 0, sizeof(ItemBase *) * MAX_ITEMBASE_ITEMS);
}

ItemBaseManager::~ItemBaseManager()
{
    if (m_ItemList)
    {
        delete [] m_ItemList;
    }

    if (m_HashTable.Table)
    {
        delete [] m_HashTable.Table;
    }

    if (m_ItemDB)
    {
        for (int i=0; i<MAX_ITEMBASE_ITEMS; i++)
        {
            if (m_ItemDB[i])
            {
                delete m_ItemDB[i];
            }
        }

        delete [] m_ItemDB;
    }
}

void ItemBaseManager::Initialize()
{
    ItemBaseParser m_Parser;
	m_Parser.LoadItemBase(m_ItemDB);

    InitializeHash();
    InitializeList();
	SetRefineInfo();
}

bool ItemBaseManager::SendItem(Player * p, ItemList * List, long ItemID)
{
    if (List->IsItemOnList(ItemID))
    {
        return true;
    }
    else
    {
        if (SendItem(p, ItemID))
        {
            List->AddItemToList(ItemID);
            return true;
        }
        else
        {
            return false;
        }
    }
}

bool ItemBaseManager::SendItem(Player * p, long ItemID)
{
    ItemBase * m_Item = GetItem(ItemID);

    if (m_Item && m_Item->BuildPacket())
    {
        p->SendOpcode(ENB_OPCODE_0025_ITEM_BASE, m_Item->Packet(), m_Item->PacketLength());
        return true;
    }

    // If the packet did not build, then we did not send
    return false;
}

ItemBase * ItemBaseManager::GetItem(char * ItemName)
{
    return m_HashTable.Table[HashItemBase(ItemName)].Item;
}

ItemBase * ItemBaseManager::GetItem(long ItemID)
{
    if (ItemID < 0 || ItemID > m_MaxItemID)
    {
        return 0;
    }

    /* This assumes that the ItemID is less than MAX_ITEMBASE_ITEMS */
    return m_ItemDB[ItemID];
}


ItemBase * ItemBaseManager::GetHulkTemplate(short level, short obj_type)
{
    ItemBase * Template[100];
    int TemplateCount = 0;
    int ItemIndex = 0;
    int Category = 10 + rand() % 3;

    while (ItemIndex < m_ItemCount && TemplateCount < 100)
    {
	    if (m_ItemList[ItemIndex]->Category() == Category && m_ItemList[ItemIndex]->TechLevel() == level)
		{
		    Template[TemplateCount++] = m_ItemList[ItemIndex];
		}

		ItemIndex++;
    }

    if (TemplateCount > 0)
    {
        return Template[rand() % TemplateCount];
    }
    else
    {
        //We found no items, choose first ore of same level
        ItemIndex = 0;

        while (ItemIndex < m_ItemCount)
        {
            if (m_ItemList[ItemIndex]->Category() == -1 && m_ItemList[ItemIndex]->TechLevel() == level)
            {
                return m_ItemList[ItemIndex];
            }

            ItemIndex++;
        }

        //If we STILL have not found anything, just return a random item
        return m_ItemList[rand() % m_ItemCount];
    }
}

ItemBase * ItemBaseManager::GetOreTemplate(short level, short obj_type, long sector_id, Field *f)
{
    ItemBase * Template[60];
    int TemplateCount = 0;
    int ItemIndex = 0;
	SectorData *sector_data = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	
	AsteroidSubcatVec *subcat_vec = g_ServerMgr->m_SectorContent.GetAsteroidContentSelection(obj_type);

	//scan through to see how many ores there are for this level and type
	if (subcat_vec)
	{
		//find out what we've got to choose from
		//base sector ores
		for (int i = 0; i < sector_data->ore_list_size; i++)
		{
			//now get item
			ItemBase * myItem = GetItem(sector_data->OreList[i]->item_id);
			if (!myItem) continue;
			char *name = myItem->Name();

			//now check to see if it's in the categories, if so, add as choice
			for (AsteroidSubcatVec::iterator subcatItr = subcat_vec->begin(); subcatItr < subcat_vec->end(); ++subcatItr)
			{
				if (myItem->SubCategory() == (*subcatItr) && myItem->TechLevel() == level)
				{
					if (obj_type < 9 && myItem->SubCategory() == 110)
					{
						if (name[0] == 'a' && name[1] == 'a' && TemplateCount < 60) Template[TemplateCount++] = myItem; //add ancient device to ore
					}
					else
					{
						Template[TemplateCount++] = myItem; //add to ore choice
						if (TemplateCount == 60) break;
					}
				}
			}
		}

		if (f)
		{
			//Additional field ores
			ItemIDList *orelist = f->GetAdditionalItemIDs();

			for (ItemIDList::iterator itrI = orelist->begin(); itrI < orelist->end(); ++itrI)
			{
				//now get item
				ItemBase * myItem = GetItem((*itrI)->item_id);

				//now check to see if it's in the categories, if so, add as choice
				for (AsteroidSubcatVec::iterator subcatItr = subcat_vec->begin(); subcatItr < subcat_vec->end(); ++subcatItr)
				{
					if (myItem->SubCategory() == (*subcatItr) && myItem->TechLevel() == level)
					{
						Template[TemplateCount++] = myItem; //add to ore choice
						if (TemplateCount == 60) break;
					}
				}
			}
		}
	}

	if (TemplateCount > 0)
	{
		return Template[rand() % TemplateCount]; //randomly choose from ore choice - TODO: add frequency weightings
	}
	else
	{
		//didn't find anything to go here - issue error from resource class
		return 0;// m_ItemList[rand() % m_ItemCount];
	}
}

void ItemBaseManager::SetRefineInfo()
{
    ItemBase * item = 0;
    ItemBase * refines_into = 0;

    for (int i=0; i<MAX_ITEMBASE_ITEMS; i++)
    {
        if ((refines_into = GetItem(i)))
        {
			if (refines_into->Category() == IB_CATEGORY_REFINED_RESOURCE)
			{
				for (int j=0;j < 6;j++)
				{
					if ((item = GetItem(refines_into->Component(j))))
					{
                    	item->SetRefinesInto(refines_into->ItemTemplateID());
					}
				}
			}
		}
	}
}

//--------------------------- HASH FUNCTIONS

void ItemBaseManager::InitializeList()
{
    int Loc = 0;
	int new_item_count = 0;

    /* Count the amount of items in our database */
    for (int i=0; i<MAX_ITEMBASE_ITEMS; i++)
    {
        if (m_ItemDB[i])
        {
            new_item_count++;
        }
    }

	if (m_ItemCount != new_item_count)
	{
		if (m_ItemList) delete[] m_ItemList;
		if ((m_ItemList = new ItemBase * [new_item_count]) == NULL)
		{
			printf("ItemBaseManager - Unable to allocate `m_ItemList`\n");
			exit(EXIT_FAILURE);
		}
		m_ItemCount = new_item_count;
	}

    memset(m_ItemList, 0, sizeof(ItemBase *) * m_ItemCount);

    /* Fill the item list */
    for (int j=0; j<MAX_ITEMBASE_ITEMS; j++)
    {
        if (m_ItemDB[j])
        {
			m_MaxItemID = j;
            m_ItemList[Loc++] = m_ItemDB[j];
        }
    }

    //TODO: Sort the list by Level AND Subcategory
}

void ItemBaseManager::InitializeHash()
{
    m_HashTable.Size = MAX_ITEMBASE_ITEMS * 4;  // Hash table is most efficient at 4x the size

    if ((m_HashTable.Table = new ItemBaseHashElement[m_HashTable.Size]) == NULL)
    {
        printf("ItemBaseManager - Unable to allocate `m_HashTable.Table`\n");
        exit(EXIT_FAILURE);
    }

    memset(m_HashTable.Table, 0, sizeof(ItemBaseHashElement) * m_HashTable.Size);

    for (int i=0; i<MAX_ITEMBASE_ITEMS; i++)
    {
        if (m_ItemDB[i])
        {
            AddItemBase(m_ItemDB[i]);
        }
    }
}

void ItemBaseManager::AddItemBase(ItemBase * Item)
{
    unsigned long Hash = HashItemBase(Item);

    if (m_HashTable.Table[Hash].Busy == ITEM_BASE_EMPTY)
    {
        m_HashTable.Table[Hash].Item = Item;
        m_HashTable.Table[Hash].Busy = ITEM_BASE_BUSY;
    }
    else
    {
        printf("AddItemBase - Duplicate Item Found! ID: %d ID: %d\n", 
            Item->ItemTemplateID(), m_HashTable.Table[Hash].Item->ItemTemplateID());
        return;
    }
}

unsigned long ItemBaseManager::HashItemBase(ItemBase * Item)
{
    return HashItemBase(Item->Name());
}

unsigned long ItemBaseManager::HashItemBase(char * Name)
{
    unsigned long Hash = HashItemBaseVal(Name);

    while (m_HashTable.Table[Hash].Busy == ITEM_BASE_BUSY)
    {
        if (!strcmp(m_HashTable.Table[Hash].Item->Name(), Name))
        {
            return Hash;
        }
        else
        {
            Hash = (Hash + 1) % m_HashTable.Size;
        }
    }

    return Hash;
}

unsigned long ItemBaseManager::HashItemBaseVal(char * Name)
{
    // This is the djb2 hash function
    unsigned long Char, Hash = 5381;

    while ((Char = *Name++))
        Hash = ((Hash << 5) + Hash) + Char;

    return Hash % m_HashTable.Size;
}
