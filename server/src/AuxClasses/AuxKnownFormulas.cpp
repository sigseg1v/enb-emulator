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
#include "AuxKnownFormulas.h"

void AuxKnownFormulas::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
{
    if (TwoBitFlags)
    {
        return;
    }

	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (unsigned int i=0;i<m_NumFormulas;i++)
	{
		if (CheckFlagBit(i))
		{
			Formula[i].BuildPacket(buffer, index, TwoBitFlags);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_KnownFormulas * AuxKnownFormulas::GetData()    {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxKnownFormulas::SetData(_KnownFormulas *NewData)
{
	for (int i=0;i<MAX_KNOWN_FORMULAS;i++)
	{
		Formula[i].SetData(&NewData->Formula[i]);
	}
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxKnownFormulas::ResetKnownFormulas(bool change)
{
	for (unsigned int i=0;i<m_NumFormulas;i++)
	{
		Formula[i].SetItemName(change ? "x" : "");
		Formula[i].SetItemID(change ? -1 : 0);
		Formula[i].SetTechLevel(change ? -1 : 0);
	}
}

void AuxKnownFormulas::Clear()
{
	for (int i=0;i<MAX_KNOWN_FORMULAS;i++)
	{
		Formula[i].SetItemName("");
        Formula[i].SetItemID(0);
        Formula[i].SetTechLevel(0);
        Formula[i].ClearFlags();
	}

    memset(Flags, 0, sizeof(Flags));
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxKnownFormulas::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<MAX_KNOWN_FORMULAS;i++)
	{
        Formula[i].ClearFlags();
	}
}