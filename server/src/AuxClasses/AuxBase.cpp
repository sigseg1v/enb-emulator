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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/
#include "AuxBase.h"

AuxBase::AuxBase()
{
	m_Flags = 0;
	m_ExtendedFlags = 0;
	m_FlagCount = 0;
	m_Parent = 0;
	m_MemberIndex = 0;
    m_Mutex = 0;

	// Phase K Wave 9: only 4 of 57 AuxBase subclasses (AuxHulkIndex/AuxShipIndex/
	// AuxMobIndex/AuxHarvestable) explicitly set m_Max_Buffer. The other 53 leave
	// it uninitialised. On Win32 the heap garbage happened to be a large value, so
	// AddData()'s `sizeof(T)+index < m_Max_Buffer` guard passed through; on Linux
	// the garbage is often small and every AddData() call spuriously hits the
	// "Error: Bufferoverflow in Aux!" printf branch — when a sector has 3+ players
	// the per-player serialisation flood saturates stdout and blocks the sector
	// tick from servicing UDP packets, making opcode replies time out.
	//
	// Caller-side buffer sizing is the actual safety mechanism: every BuildPacket
	// is passed a fixed-size buffer it must fit into. The m_Max_Buffer check is
	// an opportunistic secondary guard, never the load-bearing one. Initialising
	// to ULONG_MAX here restores Win32 de-facto behaviour without weakening any
	// real bound — subclasses that *know* a tighter cap (AuxHulkIndex=1000,
	// AuxShipIndex=10000, AuxMobIndex=2000, AuxHarvestable=2000) keep their
	// explicit assignments and override this default.
	m_Max_Buffer = (unsigned long)-1;
}

AuxBase::~AuxBase()
{
}

void AuxBase::Construct(unsigned char *Flags, unsigned char *ExtendedFlags, unsigned int FlagCount,
                class AuxBase *Parent, unsigned int MemberIndex)
{
	m_Flags = Flags;
	m_ExtendedFlags = ExtendedFlags;
	m_FlagCount = FlagCount;
	m_Parent = Parent;
	m_MemberIndex = MemberIndex;

    if (m_Parent)
    {
        m_Mutex = m_Parent->m_Mutex;
    }
}

/******************************
*         ADD METHODS         *
******************************/

void AuxBase::AddString(unsigned char *buffer, char *str, long &index)
{
	if (index + strlen(str) < m_Max_Buffer)
	{
		AddData(buffer,short(strlen(str)),index);
		memcpy(&buffer[index], str, strlen(str));
		index += strlen(str);
	}
	else
	{
		printf("Error: Bufferoverflow in Aux!");
	}
}

void AuxBase::AddFlags(unsigned char *flags, unsigned int size, unsigned char *buffer, long &index)
{
	for (unsigned int i=0;i<size;i++)
	{
		AddData(buffer,flags[i],index);
	}
}

/******************************
*       REPLACE METHODS       *
******************************/

void AuxBase::ReplaceString(char *orig, char *src, unsigned int flagNum, unsigned int len)
{
    m_Mutex->Lock();

	if (strcmp(orig,src))
	{
		/* The string is different, set the flags */
		SetAuxBit(m_Flags, flagNum);

        /* This eliminates useless recursion */
        if ((m_Flags[0] & 0x02) == 0)
        {
			m_Flags[0] |= 0x02;
			SetParentFlag();
			SetParentExtendedFlag(1);
        }

		/* Change the extended flags for this bit if needed */
		if (!strlen(orig) && strlen(src))
		{
			UnsetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
			SetAuxBit(m_ExtendedFlags, flagNum);
		}
		else if (!strlen(src) && strlen(orig))
		{
			SetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
			UnsetAuxBit(m_ExtendedFlags, flagNum);
		}

		/* Copy the string */
		
		strncpy(orig,src,len);
		orig[len-1]='\0'; //force terminate
	}

    m_Mutex->Unlock();
}

void AuxBase::ReplaceAvail(u32 *orig, u32 *src, unsigned int flagNum)
{
    m_Mutex->Lock();

	if (memcmp(orig,src,sizeof(u32)*4))
	{
		/* The ints are differnt */
		SetAuxBit(m_Flags, flagNum);

        /* This eliminates useless recursion */
        if ((m_Flags[0] & 0x02) == 0)
        {
			m_Flags[0] |= 0x02;
			SetParentFlag();
        }

		/* Since the default is non-empty, availability cannot be empty */

		/* Copy the ints */
		memcpy(orig,src,sizeof(u32)*4);
	}

    m_Mutex->Unlock();
}

void AuxBase::ReplaceColor(float *orig, float *src, unsigned int flagNum)
{
	if (memcmp(orig,src,sizeof(float)*3))
	{
		/* The floats are differnt */
		SetAuxBit(m_Flags, flagNum);

        /* This eliminates useless recursion */
        if ((m_Flags[0] & 0x02) == 0)
        {
			m_Flags[0] |= 0x02;
			SetParentFlag();
        }

		/* Change the extended flags for this bit if needed */
		if (src[0] || src[1] || src[2])
		{
			UnsetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
			SetAuxBit(m_ExtendedFlags, flagNum);
		}
		else
		{
			SetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
			UnsetAuxBit(m_ExtendedFlags, flagNum);
		}

		/* Copy the floats */
		memcpy(orig,src,sizeof(float)*3);
	}
}

/******************************
*      FLAG CHECK METHODS     *
******************************/

unsigned int AuxBase::CheckFlagBit(unsigned int flagNum)
{
	return (CheckAuxBit(m_Flags, flagNum));
}

unsigned int AuxBase::CheckExtendedFlagBit1(unsigned int flagNum)
{
	return (CheckAuxBit(m_ExtendedFlags, flagNum));
}

unsigned int AuxBase::CheckExtendedFlagBit2(unsigned int flagNum)
{
	return (CheckAuxBit(m_ExtendedFlags, flagNum + m_FlagCount));
}

unsigned int AuxBase::CheckAuxBit(unsigned char *flagBuffer, unsigned int flagNum)
{
	return (flagBuffer[(flagNum + 4) / 8] & (1 << ((flagNum + 4) % 8)));
}

/******************************
*  FLAG MANIPULATION METHODS  *
******************************/

void AuxBase::SetAuxBit(unsigned char *flagBuffer, unsigned int flagNum)
{
	flagBuffer[(flagNum + 4) / 8] |= (1 << ((flagNum + 4) % 8));
}

void AuxBase::UnsetAuxBit(unsigned char *flagBuffer, unsigned int flagNum)
{
	flagBuffer[(flagNum + 4) / 8] &= ~(1 << ((flagNum + 4) % 8));
}

/******************************
*   RECURSIVE START METHODS   *
******************************/

void AuxBase::SetParentFlag()
{
	if (m_Parent)
	{
		m_Parent->ChildFlagChanged(m_MemberIndex);
	}
}

void AuxBase::SetParentExtendedFlag(unsigned int Set)
{
	if (m_Parent)
	{
		m_Parent->ChildExtendedFlagChanged(m_MemberIndex, Set);
	}
}

/******************************
*      RECURSIVE METHODS      *
******************************/

void AuxBase::ChildFlagChanged(unsigned int ChildIndex)
{
	unsigned char test;
	try
	{
		test = *m_Flags;
	}
	catch (...)
	{
		LogMessage("ERROR: No m_Flags in ChildFlagChanged() call.\n");
		return;
	}

	SetAuxBit(m_Flags, ChildIndex); /* Set the flag associated with the index */
	if ((m_Flags[0] & 0x02) == 0)
	{
		m_Flags[0] |= 0x02;         /* Set the main flag */
		SetParentFlag();	        /* Continue upward */
	}
}

void AuxBase::ChildExtendedFlagChanged(unsigned int ChildIndex, unsigned int Set)
{
	if (!m_ExtendedFlags)
		return;

	if (Set)
	{
		SetAuxBit(m_ExtendedFlags, ChildIndex);
	}
	else
	{
		UnsetAuxBit(m_ExtendedFlags, ChildIndex);
	}

    CheckData();    /* Continue recursion on a case-by-case basis */
}
