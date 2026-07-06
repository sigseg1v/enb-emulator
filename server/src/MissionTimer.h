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

#ifndef _MISSIONTIMER_H_INCLUDED_
#define _MISSIONTIMER_H_INCLUDED_

#include <cstdint>

// PB-62: the pure decision for whether a timed, non-forfeitable mission has
// spoiled and its slot should be freed. Split out of Player::IsTimedMissionExpired
// so the arithmetic (elapsed-vs-limit, GetNet7TickCount() wrap, and the guard
// conditions) is unit-testable without constructing a Player / ServerManager.
//
//   now_tick_ms   -- GetNet7TickCount() sampled now (ms).
//   start_tick_ms -- GetNet7TickCount() captured when the mission was accepted or
//                    loaded at login; 0 means "no clock armed for this slot".
//   time_limit_s  -- <Mission time="N"> in seconds; <= 0 means untimed.
//   forfeitable   -- a forfeitable mission already has an escape (the forfeit
//                    button), so it is never force-expired here.
//
// Returns true only for an armed clock on a timed, non-forfeitable mission whose
// window has elapsed. Unsigned subtraction makes the comparison correct across a
// single GetNet7TickCount() wrap (~49 days).
static inline bool MissionTimedOut(uint32_t now_tick_ms,
                                   uint32_t start_tick_ms,
                                   int time_limit_s,
                                   bool forfeitable)
{
	if (start_tick_ms == 0)   return false; // clock not armed
	if (time_limit_s <= 0)    return false; // untimed mission
	if (forfeitable)          return false; // already escapable via forfeit

	uint32_t elapsed_ms = now_tick_ms - start_tick_ms;
	return elapsed_ms >= (uint32_t)time_limit_s * 1000u;
}

#endif
