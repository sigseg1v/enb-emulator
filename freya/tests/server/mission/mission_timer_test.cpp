// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// mission_timer_test.cpp
//
// PB-62: pins the spoilage-timer decision for non-forfeitable timed missions.
// The reported bug was mission 130 "Learn Build Shield Skill" (forfeitable="0",
// time="1200") pinning its mission slot forever -- MissionDismiss refuses to
// forfeit it, and without a server-side clock nothing ever frees it, so the
// player "cannot abandon+reacquire". Player::ExpireStaleTimedMissions frees the
// slot on the reacquire path once the clock runs out; the actual arithmetic and
// guard conditions live in MissionTimedOut() so they can be tested here without
// standing up a Player / ServerManager / mission DB.
//
// This drives the SAME MissionTimedOut() the server runtime calls from
// Player::IsTimedMissionExpired (server/src/PlayerMissions.cpp), so a pass here
// is a pass for the real decision, not a copy of it.

#include <gtest/gtest.h>
#include <cstdint>

#include "MissionTimer.h"

namespace {

// mission 130's real limit: time="1200" seconds == 20 minutes.
constexpr int kMission130LimitS = 1200;
constexpr uint32_t kBase = 1'000'000u; // an arbitrary "accept" tick (ms)

// --- guard conditions: these must NEVER force-expire ---------------------

TEST(MissionTimer, UntimedMissionNeverExpires) {
    // time_limit <= 0 -> untimed; even a huge elapsed span does not expire.
    EXPECT_FALSE(MissionTimedOut(kBase + 999'999'999u, kBase, 0, /*forfeitable=*/false));
    EXPECT_FALSE(MissionTimedOut(kBase + 999'999'999u, kBase, -5, /*forfeitable=*/false));
}

TEST(MissionTimer, ForfeitableTimedMissionNeverForceExpires) {
    // A forfeitable mission already has an escape (the forfeit button); we do not
    // yank it out from under the player, no matter how long it has run.
    EXPECT_FALSE(MissionTimedOut(kBase + kMission130LimitS * 1000u + 1, kBase, kMission130LimitS,
                                 /*forfeitable=*/true));
}

TEST(MissionTimer, UnarmedClockNeverExpires) {
    // start_tick == 0 means no clock was armed for the slot (untracked mission,
    // e.g. one that was never timed). Must not expire even with a live now-tick.
    EXPECT_FALSE(MissionTimedOut(kBase, 0, kMission130LimitS, /*forfeitable=*/false));
}

// --- the real decision: timed + non-forfeitable ---------------------------

TEST(MissionTimer, NotYetElapsed) {
    // one second before the 20-minute limit -> still running.
    uint32_t now = kBase + (kMission130LimitS * 1000u) - 1000u;
    EXPECT_FALSE(MissionTimedOut(now, kBase, kMission130LimitS, false));
}

TEST(MissionTimer, ExactlyAtLimitExpires) {
    // at exactly the limit the mission has spoiled (>= comparison).
    uint32_t now = kBase + (kMission130LimitS * 1000u);
    EXPECT_TRUE(MissionTimedOut(now, kBase, kMission130LimitS, false));
}

TEST(MissionTimer, PastLimitExpires) {
    uint32_t now = kBase + (kMission130LimitS * 1000u) + 60'000u; // a minute over
    EXPECT_TRUE(MissionTimedOut(now, kBase, kMission130LimitS, false));
}

TEST(MissionTimer, Mission130Boundary) {
    // 19:59 -> not spoiled; 20:00 -> spoiled.
    EXPECT_FALSE(MissionTimedOut(kBase + 1'199'000u, kBase, kMission130LimitS, false));
    EXPECT_TRUE(MissionTimedOut(kBase + 1'200'000u, kBase, kMission130LimitS, false));
}

// --- GetNet7TickCount() wrap: unsigned subtraction must stay correct ------

TEST(MissionTimer, TickWrapNotYetElapsed) {
    // armed just before the 32-bit tick counter wraps; "now" has wrapped past 0
    // but only 10s of real time has passed -> not expired.
    uint32_t start = 0xFFFFFFFFu - 5000u; // 5s before wrap
    uint32_t now = 5000u;                 // 5s after wrap  => 10s elapsed
    EXPECT_FALSE(MissionTimedOut(now, start, kMission130LimitS, false));
}

TEST(MissionTimer, TickWrapElapsed) {
    // same wrap, but a full 20 minutes of real time has elapsed across it.
    uint32_t start = 0xFFFFFFFFu - 5000u;               // 5s before wrap
    uint32_t now = (kMission130LimitS * 1000u) - 5000u; // 20min after the start
    EXPECT_TRUE(MissionTimedOut(now, start, kMission130LimitS, false));
}

} // namespace
