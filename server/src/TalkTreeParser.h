// TalkTreeParser.h
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

#ifndef _TALK_TREE_PARSER_H_INCLUDED_
#define _TALK_TREE_PARSER_H_INCLUDED_

#include "TalkTree.h"
#include "StringManager.h"

struct XMLNode;

class TalkTreeParser
{
private:
	TalkTree * m_TalkTrees;

public:
    TalkTreeParser()    {}
    ~TalkTreeParser()   {}

public:
	TalkTree * GetTalkTree();
    bool ParseTalkTree(TalkTree *tree, char *data);
	bool ParseMissions(MissionTree *tree, char *data);

private:
	void InnerTalkTreeParser(TalkTree *tree, XMLNode *TalkNode);
};


#endif
