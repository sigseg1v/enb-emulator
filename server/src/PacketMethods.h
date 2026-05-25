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
#ifndef _PACKET_METHODS_H_INCLUDED_
#define _PACKET_METHODS_H_INCLUDED_

#include <stdint.h>

template <typename T>
static void AddData(unsigned char *packet, T mydata, int &index)
{
	*((T *) &packet[index]) = mydata;
	index += sizeof(T);
}

/* Adds the string only */
static void AddDataS(unsigned char *packet, char *mydata, int &index)
{
	memcpy(&packet[index], mydata, strlen(mydata));
	index += strlen(mydata);
}

/* Adds the string with a null terminating charachter */
static void AddDataSN(unsigned char *packet, char *mydata, int &index)
{
	memcpy(&packet[index], mydata, strlen(mydata) + 1);
	index += strlen(mydata) + 1;
}

/* Adds the length (short) of the string followed by the string itself */
static void AddDataLS(unsigned char *packet, char *mydata, int &index)
{
	if (mydata)
	{
		AddData(packet,short(strlen(mydata)),index);
		memcpy(&packet[index], mydata, strlen(mydata));
		index += strlen(mydata);
	}
}

/* Same as above but the strings null terminated charachter is added */
static void AddDataLSN(unsigned char *packet, char *mydata, int &index)
{
	AddData(packet,short(strlen(mydata) + 1),index);
    memcpy(&packet[index], mydata, strlen(mydata) + 1);
    index += strlen(mydata) + 1;
}

/* Flip the byte order of the data. See proxy/PacketMethods.h for the
** rationale on the int32_t cast — Win32 `long` is 4 bytes, Linux is 8,
** and the wire format is 4. */
static void AddDataFlip4(unsigned char *packet, long mydata, int &index)
{
	*((int32_t *) &packet[index]) = ntohl((uint32_t)mydata);
	index += 4;
}

/* Flip the byte order of the data */
static void AddDataFlip2(unsigned char *packet, short mydata, int &index)
{
	*((short *) &packet[index]) = ntohs(mydata);
	index += 2;
}

/* Add another buffer */
static void AddBuffer(unsigned char *packet, unsigned char *buffer, int length, int &index)
{
	memcpy(&packet[index], buffer, length);
	index += length;
}

/* Extract a string from an 'AddDataLS' encoded string */
static void ExtractDataLS(unsigned char *packet, char *buffer, int &index)
{
    short string_length = *((short *) &packet[index]);
    index += 2;
    memcpy(buffer, &packet[index], string_length);
    buffer[string_length] = 0;
    index += string_length;
}

/* Win32 long is 4B, Linux is 8B, wire is 4B. The original `long*` cast
** read 8 bytes on Linux — 4 valid + 4 of the next wire field as garbage.
** See proxy/PacketMethods.h for the read-side rationale; same fix here.
*/
static long ExtractLong(unsigned char *packet, int &index)
{
    index += 4;
    return (long) *((int32_t*) &packet[index-4]);
}

static short ExtractShort(unsigned char *packet, int &index)
{
    index += 2;
    return (*((short*) &packet[index-2]) );
}

static float ExtractFloat(unsigned char *packet, int &index)
{
    float result = *((float *) &packet[index]);
    index += 4;
    return result;
}

#endif