// WestwoodRC4.h
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

#ifndef _WESTWOOD_RC4_H_INCLUDED_
#define _WESTWOOD_RC4_H_INCLUDED_

class WestwoodRC4
{
public:
    WestwoodRC4();
    virtual ~WestwoodRC4();

public:
   void PrepareKey(unsigned char *key_data_ptr, int key_data_len);
   void RC4(unsigned char *buffer_ptr, long buffer_len);

private:
	static void SwapByte(unsigned char *a, unsigned char *b);

private:
	unsigned char m_State[256];
	unsigned char m_x;
	unsigned char m_y;
};

#endif // _WESTWOOD_RC4_H_INCLUDED_

