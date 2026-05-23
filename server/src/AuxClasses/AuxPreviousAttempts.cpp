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
#include "AuxPreviousAttempts.h"

void AuxPreviousAttempts::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
{
    if (TwoBitFlags)
    {
        return;
    }

	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (int i=0;i<16;i++)
	{
		if (CheckFlagBit(i))
		{
			Attempt[i].BuildPacket(buffer, index, TwoBitFlags);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_PreviousAttempts * AuxPreviousAttempts::GetData()  {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxPreviousAttempts::SetData(_PreviousAttempts *NewData)
{
	for (int i=0;i<16;i++)
	{
		Attempt[i].SetData(&NewData->Attempt[i]);
	}
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxPreviousAttempts::ResetAttempts(unsigned int EndIndex)
{
	for (unsigned int i=0;i<EndIndex;i++)
	{
		Attempt[i].SetItemName("");
        Attempt[i].SetItemID(0);
        Attempt[i].SetTechLevel(0);
	}
}

void AuxPreviousAttempts::Clear()
{
	for (int i=0;i<16;i++)
	{
		Attempt[i].SetItemName("");
        Attempt[i].SetItemID(0);
        Attempt[i].SetTechLevel(0);
        Attempt[i].ClearFlags();
	}

    memset(Flags, 0, sizeof(Flags));
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxPreviousAttempts::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<16;i++)
	{
        Attempt[i].ClearFlags();
	}
}