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
#ifndef _AUXBASE_H_INCLUDED_
#define _AUXBASE_H_INCLUDED_

#include "../Net7.h"
#include "../Mutex.h"

class AuxBase
{
protected:
	AuxBase();
	~AuxBase();

	void Construct(unsigned char *, unsigned char *, unsigned int, class AuxBase *, unsigned int);

	template <typename T>
	void AddData(unsigned char *buffer, T data, long &index)
	{
		if (sizeof(T) + index < m_Max_Buffer)
		{
			*((T *) &buffer[index]) = data;
			index += sizeof(T);
		}
		else
		{
			printf("Error: Bufferoverflow in Aux!");
		}
	}

	void AddString(unsigned char *, char *, long &);
	void AddFlags(unsigned char *, unsigned int, unsigned char *, long &);

	template <typename T>
	void ReplaceData(T &orig, T src, unsigned int flagNum)
	{
        m_Mutex->Lock();

		if (orig != src)
		{
			/* The data is different, set the flags */
			SetAuxBit(m_Flags, flagNum);

            /* This eliminates useless recursion */
            if ((m_Flags[0] & 0x02) == 0)
            {
			    m_Flags[0] |= 0x02;
			    SetParentFlag();
				SetParentExtendedFlag(1);
            }

			/* Change the extended flags for this bit if needed */
			if (!orig && src)
			{
				UnsetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
				SetAuxBit(m_ExtendedFlags, flagNum);
			}
			else if (!src && orig)
			{
				SetAuxBit(m_ExtendedFlags, flagNum + m_FlagCount);
				UnsetAuxBit(m_ExtendedFlags, flagNum);
			}

			/* Copy the data */
			orig = src;
		}

        m_Mutex->Unlock();
	}

	void ReplaceString(char *, char *, unsigned int, unsigned int);
	void ReplaceAvail(u32 *, u32 *, unsigned int);
	void ReplaceColor(float *, float *, unsigned int);

	unsigned int CheckFlagBit(unsigned int);
	unsigned int CheckExtendedFlagBit1(unsigned int);
	unsigned int CheckExtendedFlagBit2(unsigned int);

	void SetParentFlag();
	void SetParentExtendedFlag(unsigned int);

    virtual void CheckData()    {} /* Checks to see if a  parent extended flag needs to be set */

private:
	unsigned int CheckAuxBit(unsigned char *, unsigned int);

	void SetAuxBit(unsigned char *, unsigned int);
	void UnsetAuxBit(unsigned char *, unsigned int);

	void ChildFlagChanged(unsigned int);
	void ChildExtendedFlagChanged(unsigned int, unsigned int);

protected:
	unsigned int	m_MemberIndex;
	unsigned int	m_FlagCount;
	unsigned char * m_Flags;
	unsigned char * m_ExtendedFlags;
	unsigned long	m_Max_Buffer;			// Holds the MAX the buffer size is

    class AuxBase * m_Parent;
    class Mutex   * m_Mutex;
};

#endif