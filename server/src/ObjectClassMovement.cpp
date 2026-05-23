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
#include "Net7.h"
#include "ObjectClass.h"
#include "PlayerClass.h"
#include "ObjectManager.h"
#include <float.h>

void Object::ExtrapolatePosition(float av_speed, float tdiff)
{
    float pos[3];
    float *_heading = Heading();

    pos[0] = av_speed * tdiff * _heading[0];
    pos[1] = av_speed * tdiff * _heading[1];
    pos[2] = av_speed * tdiff * _heading[2];

    MovePosition(pos[0], pos[1], pos[2]);
}

//TODO: read in and use the sector boundaries
bool Object::CheckBoundaries()
{
    float zboundary = 7500.0f;
    float xmin = -1000000.0f;
    float ymin = -1000000.0f;
    float xmax = 1000000.0f;
    float ymax = 1000000.0f;
    bool interrupt_warp = false;
	ServerParameters  *params = (0);

	SectorManager *sm = GetSectorManager();

	if (sm) 
	{
		params = sm->GetSectorParams();

		if (params->XMax > 0 && params->YMax > 0)
		{
			xmin = params->XMin;
			xmax = params->XMax;
			ymin = params->YMin;
			ymax = params->YMax;
		}
	} 

	m_Mutex.Lock();

    if (PosZ() > zboundary)
    {
        m_Position_info.Position[2] = zboundary;
    }
    else if (PosZ() < -zboundary)
    {
        m_Position_info.Position[2] = -zboundary;
    }
    
    if (PosX() > xmax)
    {
        m_Position_info.Position[0] = xmax;
        interrupt_warp = true;
    }
    else if (PosX() < xmin)
    {
        m_Position_info.Position[0] = xmin;
        interrupt_warp = true;
    }
    
    if (PosY() > ymax)
    {
        m_Position_info.Position[1] = ymax;
        interrupt_warp = true;
    }
    else if (PosY() < ymin)
    {
        m_Position_info.Position[1] = ymin;
        interrupt_warp = true;
    }

    m_Mutex.Unlock();

    return (interrupt_warp);
}

void Object::Turn(float Intensity)
{
    long current_time = GetNet7TickCount();
    CalcNewPosition(current_time, true);
    m_Mutex.Lock();
    m_ZInput = Intensity * m_GravityHandle;
    m_Position_info.RotZ = (-Intensity /883.0f) * m_GravityHandle ;
    SetLastAccessTime(current_time);
    m_ReceivedMovement = true;
    m_Mutex.Unlock();
}

void Object::Tilt(float Intensity)
{
    long current_time = GetNet7TickCount();
    CalcNewPosition(current_time, true);
    m_Mutex.Lock();
    m_YInput = Intensity * m_GravityHandle;
    m_Position_info.RotY = (-Intensity/883.0f) * m_GravityHandle;
    SetLastAccessTime(current_time);
    m_ReceivedMovement = true;
    m_Mutex.Unlock();	    
}

void Object::CalcNewPosition(unsigned long current_tick, bool turn)
{
	float tdiff;
    float av_speed = 0.0f;
  
    tdiff = (float)(current_tick - m_LastUpdate)/1000.0f;

    if (tdiff < 5.0f) //if > 5.0s the system was hibernating
    {
        CalcNewHeading(tdiff);
        
        //Now perform the acceleration calculations to update player velocity and get average speed of tdiff period
        //av_speed = CalcVelocity(tdiff);
        av_speed = Velocity();
        
        if (av_speed != 0.0f)
        {
            ExtrapolatePosition(av_speed, tdiff); 
        }
    }

    m_LastUpdate = current_tick;
}

void Object::CalcNewHeading(float tdiff)
{
    float rot_Z[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    float rot_Y[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    float result[4];

    if (m_ZInput != 0.0f)
    {
        rot_Z[2] = -m_ZInput*tdiff*0.42f/883.0f*1000.0f;
    }

    if (m_YInput != 0.0f)
    {
        rot_Y[1] = -m_YInput*tdiff*0.42f/883.0f*1000.0f;
    }

    Quat4fMul(rot_Z, rot_Y, result);
    Quat4fMul(Orientation(), result, Orientation());
    Quat4fNormalize(Orientation());
    SetHeading();
}

///////////////////////////////////////////////////
// Range List Handling
//
// These methods handle adding to and removing from
// the object range lists.

//Generic method we can use to see if the input object can see 'this' object
//This virtual method is overriden where we can do something more efficient
bool Object::ObjectInRangeList(Object *obj)
{
    bool in_range = false;

	if (obj->ObjectType() == OT_PLAYER)
    {
        Player *p = (Player*)obj;
        switch (this->ObjectType())
        {
        case OT_HULK:
        case OT_RESOURCE:
        case OT_FIELD:
        case OT_HUSK:
            in_range = GetIndex(p->ObjectRangeList());
            break;

        case OT_NAV:
        case OT_DECO:
        case OT_STATION:
        case OT_STARGATE:
            if (RangeFrom(p->Position()) < (Signature() + p->ShipIndex()->CurrentStats.GetScanRange()))
            {
                in_range = true;
            }
            break;

        case OT_PLAYER:
            LogMessage("*** Error - player/player range scan using baseclass Range method.\n");
            //drop through to generic method
        case OT_MOB:
            //to be deprecated, should have method in MOB class
            if (RangeFrom(p->Position()) < (5000.0f + p->ShipIndex()->CurrentStats.GetScanRange())) //Mob has 5000 sig
            {
                in_range = true;
            }
            break;
        }
    }
    else // just use a generic ranging method
    {
        if (RangeFrom(obj->Position()) < (Signature() + 3000.0f))
        {    
            in_range = true;
        }
    }

    return in_range;
}

//End object rangelist stuff.


bool Object::ObjectIsMoving()
{
	bool moving = true;

	if (ObjectType() ==  OT_PLAYER) // If we are a grouped player check to see if leader moves, return false if he isn't
	{
		Player *me = (Player*)this;

		if (me->GetInGroupFormation())
		{
			Player *leader = me->GetGroupLeader();

			if (leader && leader->Velocity() < 1.0f && leader->Velocity() > -1.0f)
			{
				moving = false;
			}
		} 
		else if (Velocity() < 1.0f && Velocity() > -1.0f) 
		{
			moving = false;
		}
	}
	else if (Velocity() < 1.0f && Velocity() > -1.0f)
	{
		moving = false;
	}

	return moving;
}

void Object::SendLocationAndSpeed(bool include_player, bool zeroupdate) // this will be used for MOBs and hijacked Objects.
{
    if (Active())
    {
		// Check if a gravity field is present.
		CheckAndRemoveGravity();
		if (m_Velocity == 0.0f)
		{
			m_Position_info.Bitmask = 0x00;
			m_Position_info.RotY = 0.0f;
			m_Position_info.RotZ = 0.0f;
		}
		else
		{
			m_Position_info.Bitmask = 0x07;
		}

		m_Position_info.Bitmask |= 0x28;

		long update = m_Position_info.UpdatePeriod;
		if (zeroupdate)
		{
			m_Position_info.UpdatePeriod = 100;
		}

        m_Position_info.CurrentSpeed = Velocity() * 0.001f;
		m_Position_info.MovementID = m_MovementID;
        m_Position_info.SetSpeed = Velocity() * 0.001f;

        SendToVisibilityList(include_player); //send to all players in range
		m_Position_info.UpdatePeriod = update;

    }
}

void Object::SendLocationAndSpeed(Player *player) // this will be used for MOBs, Players and hijacked Objects.
{
    if (Active())
    {
		// Check if a gravity field is present.
		CheckAndRemoveGravity();
        if (Velocity() == 0.0f) //only transmit speed if required
        {
            m_Position_info.Bitmask = 0x00;  
        }
        else
        {
            m_Position_info.Bitmask = 0x27;  
        }
        
        if (ObjectType() == OT_MOB)
        {
            //m_Position_info.Bitmask |= 0x0100;
            //m_Position_info.UpdatePeriod = 5000;
        }
        m_Position_info.CurrentSpeed = Velocity() * 0.001f;
		m_Position_info.MovementID = m_MovementID;
        m_Position_info.SetSpeed = Velocity() * 0.001f;

        player->SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
    }
}

//Send object to all players who can see us
void Object::SendToVisibilityList(bool include_player)
{
    LogMessage("*** error, generic object baseclass send used!\n");
}


/*
	This function sets the velocity of the object. It takes the
	wanted velocity and multiplies it with the GravityField modificator.
*/
void Object::SetVelocity(float velocity)
{
    m_Mutex.Lock();
	// See if we are affected by a gravity field. if so slow us down
	// Maybe set maxspeed if object refuses this speed
    m_Velocity = velocity * m_GravityField;
    m_Position_info.CurrentSpeed = Velocity() * 0.001f;
    m_Mutex.Unlock();
}

/*
	This function sets the gravity field.
	It sets an expire time, modifier and steering and saves the current objects
	velocity and acceleration.
	It then updates the object with the applied gravity field modifier and steering interferance.
	If an gravity field is already present nothing will happen.
*/

void Object::SetGravityField(float modifier, float steering, long expire)
{
	// Only apply if no current gravity field apply
	if ( m_GravityField == 1.0f )
	{
		m_GravityField = modifier;
		m_GravityHandle = steering;
		m_GravityTimeExpire  = GetNet7TickCount() + expire*1000;
		m_GravityVelocity = Velocity();                                          // Save old velocity
		m_GravityAcceleration = m_Position_info.Acceleration;                    // Save old acceleration
		m_Position_info.Acceleration = m_GravityAcceleration * m_GravityField;   // Update acceleration
		SetVelocity( Velocity() );                                               // Update to new velocity
		SendLocationAndSpeed(true);
		AdjustAndSetSpeeds(false,false);
	}
}

/*
	This function checks if an gravity field is present. 
	If the tick count is greater than expire time the gravity field is removed from
	object.
	The saved velocity and acceleration of the object is then restored.
*/
void Object::CheckAndRemoveGravity()
{
	if ( m_GravityField != 1.0f && m_GravityTimeExpire < GetNet7TickCount() )
	{
		m_GravityField = 1.0f;
		m_GravityHandle = 1.0f;									// Restore steering if modified
		m_Position_info.Acceleration = m_GravityAcceleration;	// Restore Acceleration of object
		SetVelocity( m_GravityVelocity );
		AdjustAndSetSpeeds(false,false);
	}
}

void Object::SetHeading()
{
    float _canDir[4] = { 1, 0, 0, 0 };
    float _heading[4]= { 1, 0, 0, 0 };

	float *ori = Orientation();

    Quat4fMulInv(_canDir, Orientation(), _heading);
    Quat4fMul(Orientation(), _heading, _heading);

    m_Position_info.Velocity[0] = _heading[0];
    m_Position_info.Velocity[1] = _heading[1];
    m_Position_info.Velocity[2] = _heading[2];
}

//Vector Math members
void Object::CalcOrientation(float ZHeading, float YHeading)
{
	float ZHdr = -(ZHeading + 3.141592654f) / 2.0f;
	float YHdr = (YHeading)  / 2.0f;

	float rotation0[] = { 0.0f, 0.0f, 0.0f, 1.0f };
	float rotation1[] = { 0.0f, sinf(YHdr), 0.0f, cosf(YHdr) };
	float rotation2[] = { 0.0f, 0.0f, sinf(ZHdr), cosf(ZHdr) };
	
	Quat4fMul1(rotation0, rotation1, Orientation());
	Quat4fMul1(Orientation(), rotation2, Orientation());
}

//Object
void Object::Quat4fMul1(float vector1[], float vector2[], float *vector3)
{
    float x1,y1,z1,w1,x2,y2,z2,w2;

	w1 = vector1[0];
	x1 = vector1[1];
    y1 = vector1[2];
    z1 = vector1[3];

	w2 = vector2[0];
    x2 = vector2[1];
    y2 = vector2[2];
    z2 = vector2[3];

    vector3[0] = (w1*w2 - x1*x2 - y1*y2 - z1*z2);
    vector3[1] = (w1*x2 + x1*w2 + y1*z2 - z1*y2);
    vector3[2] = (w1*y2 - x1*z2 + y1*w2 + z1*x2);
    vector3[3] = (w1*z2 + x1*y2 - y1*x2 + z1*w2);
}

void Object::Quat4fNormalize(float *vector1)
{
    float x,y,z,w,magnitude;

    x = vector1[0];
    y = vector1[1];
    z = vector1[2];
    w = vector1[3];

    magnitude = sqrtf(w*w + x*x + y*y + z*z);
    vector1[3] /= magnitude;
    vector1[0] /= magnitude;
    vector1[1] /= magnitude;
    vector1[2] /= magnitude;
}

//Object
void Object::Quat4fMul(float vector1[], float vector2[], float *vector3)
{
    float x1,y1,z1,w1,x2,y2,z2,w2;

	w1 = vector1[3];
	x1 = vector1[0];
    y1 = vector1[1];
    z1 = vector1[2];

	w2 = vector2[3];
    x2 = vector2[0];
    y2 = vector2[1];
    z2 = vector2[2];

    vector3[3] = (w1*w2 - x1*x2 - y1*y2 - z1*z2);
    vector3[0] = (w1*x2 + x1*w2 + y1*z2 - z1*y2);
    vector3[1] = (w1*y2 - x1*z2 + y1*w2 + z1*x2);
    vector3[2] = (w1*z2 + x1*y2 - y1*x2 + z1*w2);
}

//Object
void Object::Quat4fMulInv(float vector1[], float vector2[], float *vector3)
{
    float x1,y1,z1,w1,x2,y2,z2,w2;

	w1 = vector1[3];
    x1 = vector1[0];
    y1 = vector1[1];
    z1 = vector1[2];
    
	w2 = vector2[3];
    x2 = - vector2[0];
    y2 = - vector2[1];
    z2 = - vector2[2];

    vector3[3] = (w1*w2 - x1*x2 - y1*y2 - z1*z2);
    vector3[0] = (w1*x2 + x1*w2 + y1*z2 - z1*y2);
    vector3[1] = (w1*y2 - x1*z2 + y1*w2 + z1*x2);
    vector3[2] = (w1*z2 + x1*y2 - y1*x2 + z1*w2);
}

void Object::TransformCoords(float *pos1, float *pos2, float ori[])
{
	float posr[3];
	float w = ori[3];
    float x = ori[0];
    float y = ori[1];
    float z = ori[2];

	float px = pos2[0] - pos1[0];
	float py = pos2[1] - pos1[1];
	float pz = pos2[2] - pos1[2];

	posr[0] = w*w*px + 2*y*w*pz - 2*z*w*py + x*x*px + 2*y*x*py + 2*z*x*pz - z*z*px - y*y*px;
	posr[1] = 2*x*y*px + y*y*py + 2*z*y*pz + 2*w*z*px - z*z*py + w*w*py - 2*x*w*pz - x*x*py;
	posr[2] = 2*x*z*px + 2*y*z*py + z*z*pz - 2*w*y*px - y*y*pz + 2*w*x*py - x*x*pz + w*w*pz;

	pos1[0] = posr[0] + pos2[0];
	pos1[1] = posr[1] + pos2[1];
	pos1[2] = posr[2] + pos2[2];
}

//leave the object facing pos1 from pos2
void Object::CalcOrientation(float *pos1, float *pos2, bool set_heading)
{
	float Distance;
    float xdiff = fabsf(pos1[0]-pos2[0]);
    float ydiff = fabsf(pos1[1]-pos2[1]);
    float ZHeading, YHeading;
    
    if (xdiff == 0) xdiff = 0.00001f;
    if (ydiff == 0) ydiff = 0.00001f;
    
    //first work out the total distance
    Distance = sqrtf(powf(pos1[0]-pos2[0],2) 
        + powf(pos1[1]-pos2[1],2) 
        + powf(pos1[2]-pos2[2],2));

    m_Mutex.Lock();
    
    //now see what quadrant we're in and calculate the Z plane angle appropriately
    if (pos1[0]-pos2[0] > 0)
    {
        if (pos1[1]-pos2[1] > 0)
        {
            //Quadrant 2
            ZHeading = (PI/2.0f) + atanf(xdiff/ydiff);
        }
        else
        {
            //Quadrant 3
            ZHeading = (PI) + atanf(ydiff/xdiff);
        }
    }
    else
    {
        if (pos1[1]-pos2[1] > 0)
        {
            //Quadrant 1
            ZHeading = atanf(ydiff/xdiff);
        }
        else
        {
            //Quadrant 4
            ZHeading = (3.0f/2.0f*PI) + atanf(xdiff/ydiff);
        }
    }
    
    if (Distance > 0.0f)
    {
        YHeading = asinf((pos1[2] - pos2[2])/Distance); 
    }
    else
    {
        YHeading = 0.0f;
    }
    
    if (_isnan(YHeading))
    {
        YHeading = 0.0f;
    }

    m_Mutex.Unlock();
    
    CalcOrientation(ZHeading, YHeading);

    if (set_heading)
    {
        SetHeading();
    }
}

void Object::FaceObject(Object *obj)
{  
    if (obj /*&& m_GravityField != 0.0f*/)
    {
        CalcOrientation(obj->Position(), Position());
        m_ReceivedMovement = true;
    }
}

void Object::FacePosition(float *position)
{  
    if (position /*&& m_GravityField != 0.0f*/)
    {
        CalcOrientation(position, Position());
        m_ReceivedMovement = true;
    }
}

void Object::FaceAwayFromObject(Object *obj)
{  
    if (obj /*&& m_GravityField != 0.0f*/)
    {
        CalcOrientation(Position(),obj->Position());
        m_ReceivedMovement = true;
    }
}

bool Object::IsFacingObject(Object *obj)
{
	float *pos1 = obj->Position();
	float *pos2 = Position();
	
	if (pos1[0]-pos2[0] > 0)
	{
		return true;
	}
	return false;
}

float Object::GetAngleTo(float *pos)
{
    float theta;
    float distance = RangeFrom(pos, true);
    float distance2;
    //now work out a position in front of the object at 'Distance' range.

    float epos[3];
    float *_heading = Heading();

    epos[0] = PosX() + ( distance * _heading[0] );
    epos[1] = PosY() + ( distance * _heading[1] );
    epos[2] = PosZ() + ( distance * _heading[2] );

    //now calc distance between two points

    distance2 = sqrtf(powf(pos[0]-epos[0],2) 
        + powf(pos[1]-epos[1],2) 
        + powf(pos[2]-epos[2],2));

    //now do some trig to work out the theta
    theta = 2* asinf(distance2/(2*distance));

    return theta;
}

void Object::SetEulerOrientation(float roll, float pitch, float yaw)
{
    float XHdr = (roll) / 2.0f;
	float ZHdr = -(yaw + 3.141592654f) / 2.0f;
	float YHdr = (pitch) / 2.0f;
	
	float candirection[] = { 0.0f, 1.0f, 0.0f, 0.0f };
	float rotation0[] = { sinf(XHdr), 0.0f, 0.0f, cosf(XHdr) };
	float rotation1[] = { 0.0f, sinf(YHdr), 0.0f, cosf(YHdr) };
	float rotation2[] = { 0.0f, 0.0f, sinf(ZHdr), cosf(ZHdr) };
	
    m_Mutex.Lock();
	Quat4fMul(rotation0, rotation1, Orientation());
	Quat4fMul(Orientation(), rotation2, Orientation());
    m_Mutex.Unlock();
}

void Object::LevelOrientation()
{
    //work out a position in front of the object's nose at a fair distance.
    float epos[3];
    float *_heading = Heading();

    epos[0] = PosX() + ( 2000.0f * _heading[0] );
    epos[1] = PosY() + ( 2000.0f * _heading[1] );
    epos[2] = PosZ() + ( 2000.0f * _heading[2] );

    //now leave object facing that point directly
    CalcOrientation(epos, Position(), false);
}

void Object::Rotate(float x, float y, float z)
{
	float sinx = sin(x/2);
	float siny = sin(y/2);
	float sinz = sin(z/2);

	float candirection[] = { 0.0f, 1.0f, 0.0f, 0.0f };
	float rot_X[4] = { sinx, 0.0f, 0.0f, cosf(x/2) };
    float rot_Y[4] = { 0.0f, siny, 0.0f, cosf(y/2) };
    float rot_Z[4] = { 0.0f, 0.0f, sinz, cosf(z/2) };

    float result[4];    

	Quat4fMul(rot_X, rot_Y, result);
    Quat4fMul(result, rot_Z, result);
    Quat4fMul(Orientation(), result, Orientation());
	Quat4fMulInv(candirection, result, Heading());
    Quat4fNormalize(Orientation());
    SetHeading();
}

void Object::LevelOut()
{
	float *_heading = Heading();
	_heading[2] = 0.0f;
}