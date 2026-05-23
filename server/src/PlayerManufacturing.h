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
#ifndef _PLAYER_MANUFACTURING_H_INCLUDED_
#define _PLAYER_MANUFACTURING_H_INCLUDED_

typedef enum
{
    ACTION_LEAVE_TERMINAL,
    ACTION_RETRY,
    ACTION_REFINE,
    ACTION_REFINE_STACK
} Manufacture_Action;

typedef enum
{
    MODE_NONE,
    MODE_MANUFACTURE,
    MODE_ANALIZE,
    MODE_DISMANTLE,
    MODE_REFINE,
	MODE_REFINE_STACK // not real
} Manufacture_Mode;

typedef enum
{
    VALIDITY_NO_TARGET,
    VALIDITY_NOT_MANUFACTURABLE,
    VALIDITY_NOT_DISMANTABLE,
    VALIDITY_RECIPE_NOT_KNOWN,
    VALIDITY_INSUFFICIENT_CREDITS,
    VALIDITY_MISSING_COMPONENTS,
    VALIDITY_INSUFFICIENT_CARGO,
    VALIDITY_NOT_QUALIFIED,
    VALIDITY_TOO_DIFFICULT,
    VALIDITY_LOW_QUALITY,
    VALIDITY_BACK_STACKSIZE,
    VALIDITY_PLAYERMADE,
    VALIDITY_DUPLICATE_UNIQUE_ITEM,
    VALIDITY_VALID,
    VALIDITY_ATTEMPT_TOTAL_FAILURE,
    VALIDITY_ATTEMPT_NEAR_SUCCESS,
    VALIDITY_ATTEMPT_NORMAL_SUCCESS,
    VALIDITY_ATTEMPT_CRITICAL_SUCCESS
} Manufacture_Validity;

typedef enum
{
    DIFFICULTY_INVALID,
    DIFFICULTY_AUTOMATIC,
    DIFFICULTY_EASY,
    DIFFICULTY_MODERATE,
    DIFFICULTY_HARD,
    DIFFICULTY_VERY_HARD,
    DIFFICULTY_IMPOSSIBLE
} Manufacture_Difficulty;

#endif
