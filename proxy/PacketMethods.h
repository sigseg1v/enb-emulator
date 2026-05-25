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
	AddData(packet,short(strlen(mydata)),index);
    memcpy(&packet[index], mydata, strlen(mydata));
    index += strlen(mydata);
}

/* Same as above but the strings null terminated charachter is added */
static void AddDataLSN(unsigned char *packet, char *mydata, int &index)
{
	AddData(packet,short(strlen(mydata) + 1),index);
    memcpy(&packet[index], mydata, strlen(mydata) + 1);
    index += strlen(mydata) + 1;
}

/* Flip the byte order of the data.
**
** Wire field is 4 bytes (Win32 long width). On Linux `long` is 8 bytes, so
** the original `*((long*) ...) = ntohl(...)` wrote 8 bytes (4 valid + 4
** of zero/garbage tail) for every flip-4. That overran the intended slot
** and stomped the next packet field on the final call of any sequence —
** see proxy/PacketMethods.h ExtractLong for the read-side mirror of this
** bug. Cast to int32_t* to write exactly 4 wire bytes regardless of host
** long width.
*/
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

/* Wire field is 4 bytes (Win32 long width). On Linux `long` is 8 bytes, so
** the original `*((long*) ...)` read pulled 8 bytes — the intended 4-byte
** value PLUS 4 bytes of whatever came next in the packet. That made every
** wire field after the first one decode as garbage on Linux. Cast to
** int32_t* to read exactly 4 wire bytes; sign-extend to long for the
** API-compatible return type. Same class of fix as the
** HandleGlobalTicketRequest char_slot read in server/src/UDP_Global.cpp.
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

static u8 ExtractU8(unsigned char *packet, int &index)
{
	index += 1;
	return (*((u8*) &packet[index-1]) );
}

static float ExtractFloat(unsigned char *packet, int &index)
{
    float result = *((float *) &packet[index]);
    index += 4;
    return result;
}

#endif