#ifndef _OBJECT_EFFECTS_H_INCLUDED_
#define _OBJECT_EFFECTS_H_INCLUDED_

#include "PacketStructures.h"
#include "mutex.h"
#include <map>

using namespace std;

class Object;
class Player;

//#define INVALID_EFFECT 0
//#define EFFECT_OBJ_EFFECT 1
//#define EFFECT_OBJ_2_OBJ_EFFECT 2

typedef enum {INVALID_EFFECT = 0, EFFECT_OBJ_EFFECT, EFFECT_OBJ_2_OBJ_EFFECT} EFFECT_TYPE;

struct MapEffect
{
	EFFECT_TYPE type;
	ObjectEffect obj_effect;
	ObjectToObjectEffect obj2obj_effect;
} ATTRIB_PACKED;

typedef map<int, MapEffect> mapEffect;

class Effects
{
public:
    Effects();
    ~Effects();

    void	Init(Object *Owner);
	int		AddEffect(ObjectEffect *obj_effect);
	int     AddEffect(ObjectToObjectEffect *obj_effect);
	bool	RemoveEffect(int EffectID);
	bool	SendEffects(Player *p);
	bool	SendRemoveEffects(Player *p);

private:
    Object *m_Object;
	mapEffect m_EffectList;
	Mutex m_Mutex;
};

#endif