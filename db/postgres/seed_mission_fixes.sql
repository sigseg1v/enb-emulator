-- Mission text-coherence fixes (net7 content DB).
--
-- Applied UNCONDITIONALLY + idempotently by schema-init AFTER the base
-- schema.sql (which is regenerated from db/mysql/net7.sql by convert.sh, so it
-- must NOT be hand-edited) and the Phase Y content seeds, and BEFORE
-- sync_sequences.sql. Each fix is a targeted replace() of a known substring in
-- an existing mission's XML: if the old substring is absent (already fixed, or a
-- future dump changed it) the replace is a no-op, so re-running is safe and this
-- also repairs pre-existing volumes.
--
-- This file is a generated-script artifact (literal SQL text, no runtime
-- parameter channel), which is the one place literal values in SQL are allowed
-- (see CLAUDE.md). It changes only player-facing MISSION TEXT -- it does not
-- alter any completion node, condition gate, reward, or wire format, so no
-- client wire-format fidelity is at stake.
--
-- ---------------------------------------------------------------------------
-- Mission 130 "Learn Build Shield Skill" (Orkael Lazarra, Aganju TT mission).
--
-- Defect: the journal text asked the player to "retreive 5 Nommos Blubber
-- samples", but the Stage-1 objective the engine actually tracks is FIGHT_MOB
-- kill-3 of Nommos Adult (mob 977), and the Stage-2 turn-in wants only ONE
-- blubber (item 5401). Three different counts (5 / 3 / 1) -- a player hoarding 5
-- blubber for a "5/5" tracker that never appears reads the quest as broken. It
-- is not: killing the Nommos drops the blubber (mob_items 977->5401 @ 25%) and
-- returning one completes it. The fix makes the objective/dialogue text describe
-- what the mission actually does, and removes the phantom "5". The kill-3
-- mechanic, the OL20/CL14 acceptance gate, and all rewards are left untouched.
-- (A leaked dev note about Manu -- long since implemented -- is also removed.)

UPDATE missions
SET "mission_XML" =
  replace(
    replace(
      replace(
        "mission_XML",
        -- Stage-0 (Node 6) accept dialogue: drop the "at least 5 pieces" count
        -- that contradicts the single-sample turn-in.
        $old$get me some blubber. If you can get me at least 5 pieces of it. It should be fairly easy$old$,
        $new$get me some blubber. If you can bring me back a fresh sample of it, it should be fairly easy$new$
      ),
      -- Stage-1 objective description: describe hunting Nommos for a blubber
      -- sample (the real kill-then-loot mechanic), not "retrieve 5 blubber".
      $old$<Description>Orkael Lazarra, station master of Kinshasa-Mbali here in Aganju needs you to get out to Nommos point with the license and retreive 5 Nommos Blubber samples and bring them back to him. The catch: 20 minutes and they spoil. </Description>$old$,
      $new$<Description>Orkael has handed you a Nommos license. Fly out to Nommos Point in Aganju and hunt the Nommos Adults there -- they carry the blubber he wants. Cut down a few of them, recover a fresh blubber sample from the kills, and race it back to Orkael before your 20 minutes run out and it spoils.</Description>$new$
    ),
    -- Stage-2 turn-in description: strip the leaked dev note about Manu (now
    -- implemented); keep the "hurry before it spoils" framing.
    $old$<Description>Hurry, time is ticking away, you need to get back to Orkael on the station and deliver this blubber, if it's too late it'll spoil and you'll have to kill more Nommos! (Should receive schematics for these three shields, this will change when Manu is implemented.)</Description>$old$,
    $new$<Description>Hurry, time is ticking away -- get back to Orkael on Kinshasa-Mbali and hand over the blubber before it congeals. If you are too late it will spoil and you will have to hunt more Nommos.</Description>$new$
  )
WHERE mission_id = 130;
