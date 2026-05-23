// XmlParser.h
//
//      This class implements pretty simplistic XML parser routines.
//
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

#ifndef _XML_PARSER_H_INCLUDED_
#define _XML_PARSER_H_INCLUDED_

#include "Net7.h"
#include "XmlTags.h"


class XmlParser
{
public:
    // forward reference
    struct XmlTagLookupTable;

// Constructor/Destructur
public:
    XmlParser();
    ~XmlParser();

// Data Structures
public:
    struct XmlTagLookupTable
    {
        enum eTagId tag_id;
        char *tag;
    };
    typedef struct XmlTagLookupTable XmlTagLookupTable;

// Public Methods
public:
    bool    ParseXmlTag(char **buffer);
    bool    GetXmlAttribute(char *attrib, char *value, unsigned int length, bool required = false);
    bool    GetXmlData(char **buffer, char *data, long length);
    int     ParseInt(char **buffer, int id, int min, int max);
    double  ParseDouble(char **buffer, int id, double min, double max, bool allow_minus_one = false);
    DWORD   ParseColor(char **buffer, int id);
    bool    ParseFloatArray(char *attrib_values, int count, float *array);
    bool    CheckID(int id);

    // NOTE: The caller must delete the returned string!!
    char  * ParseString(char **buffer, int id);

    // These are a static member function so that they can be
    // called without instantiating an XmlParser object.
    // NOTE: The caller must delete the returned string!!
    static char * EncodeXmlString(char * str);
    static char * DecodeXmlString(char * str);

// Public Member Attributes
public:
    bool    m_Success;
    eTagId  m_XmlTagID;

// Protected Member Attributes
protected:
    int     m_CurrentID;
    char    m_Tag[512];
    char    m_Attributes[512];
    XmlTagLookupTable * m_XmlTagLookupTable;
};

#endif // _XML_PARSER_H_INCLUDED_
