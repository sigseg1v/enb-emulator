#ifndef _PLAYER_BUFFS_H_INCLUDED_
#define _PLAYER_BUFFS_H_INCLUDED_

#include "AuxClasses/AuxShipIndex.h"
#include "mutex.h"

#include <map>

#define DAMAGE_ABSORB			-999
#define MAX_STATS_PER_BUFF		20
#define MAX_EFFECTS_PER_BUFF	5

using namespace std;

class Object;
class CMob;
class Player;
class MOB;

struct StatList
{
	char StatName[128];
	int	StatType;
	float Value;
} ATTRIB_PACKED;

struct Buff
{
	char BuffType[128];
	int	 EffectID[MAX_EFFECTS_PER_BUFF];
	int NumEffects;
	u32 ExpireTime;
	bool IsPermanent;
	int AbsorbID;
	struct StatList Stats[MAX_STATS_PER_BUFF];
	_Elements Elements;
} ATTRIB_PACKED;

struct MapBuffs
{
	_Buff AuxBuffData;	// player only
	short AuxSlot;		// player only
// common
	Buff BuffData;
	int RemoveEffectID[MAX_EFFECTS_PER_BUFF];
	int StatID[MAX_STATS_PER_BUFF];	// Stat ID's to remove effects
} ATTRIB_PACKED;

typedef map<int, MapBuffs> mapBuffs;

class CMobBuffs
{
public:
    CMobBuffs();
    ~CMobBuffs();

    void	Init(CMob *Owner);
	bool	FindBuff(char * BuffName, bool non_permanent_only=false);
	int		AddBuff(struct Buff *AddBuff, int colour= -1, char *StatSource=NULL);
	int		RefreshOrAddBuff(struct Buff *RefreshBuff);
	bool	RemoveBuff(char *BuffName);
	void	ChangeSector();
	void	Update(int BuffID, bool force_remove=false, bool skip_dmg_shield=false);	// Update expire buffs
	void	Clear();
	void	CheckBuffExpire(long now);
private:
	Player *GetPlayer();	// NULL for Mobs
	MOB *GetMOB();			// NULL for Players
//non-locking buff functions
	int		_AddBuff(struct Buff *AddBuff, int colour= -1, char *StatSource=NULL);
	void	_Update(int BuffID, bool force_remove=false, bool skip_dmg_shield=false);	// Update expire buffs
private:
    CMob	 *m_Owner;
	mapBuffs m_BuffList;
	int		 m_BuffID;		// Incremented to never repeat
	Mutex    m_Mutex;
	multimap<long,int> m_expireTimes;
};

#endif