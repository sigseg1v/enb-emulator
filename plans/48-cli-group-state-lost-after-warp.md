# Plan 48 -- CLI: group/formation state is lost after a warp (sector change)

Status: **OPEN** (recorded 2026-06-12, not started). CLI-only state-tracking bug.

## The problem (exact repro)

A non-leader member accepts an invite, forms up, warps, then `formation break`
wrongly reports they are not grouped:

```
griever2@Luna > group accept
accepted group invite from Teone
* Avatar (Teone) appeared in scanner range
* Block formation initiated.
griever2@Luna > formation join
forming up into the leader's formation
* Warp ended
griever2@Luna > formation break
not in a group -- use `group invite <player>` first
```

Between `formation join` and `formation break` a warp happened, and the CLI's
cached `SessionContext.SelfGroup.InGroup` flipped from true to false. The group
on the server is still intact (the member never left) -- only the CLI's view is
wrong, so every group/formation command gates off.

## Where the state comes from

`SelfGroup` is re-derived from each self `PlayerIndex` aux (opcode `0x001B`,
GameID == 0) by `AuxDataRecord.TryExtractGroupState`
(`freya/cli-client/src/CliClient.Core/Opcodes/Records/AuxDataRecord.cs`),
plumbed through `SessionContext.TrackSelfGroup`. The current heuristics:

- `InGroup` = **populated member count > 0** (GroupInfo.Members slots whose GameID
  is not 0 and not 0xFFFFFFFF). NOT the IsGroupLeader flag, because a non-leader
  member has IsGroupLeader == 0.
- `InFormation` = `GroupInfo.Formation != 0`.
- `TryExtractGroupState` returns **null** (caller keeps cached state) when the aux
  has no GroupInfo at all -- so a credits-only diff aux does not clobber.

`_GroupInfo` (`server/src/AuxClasses/AuxGroupInfo.h:23`) has **no GroupID field** --
the only "am I grouped" signal in the aux is the member roster. That is the crux.

## Likely root cause (confirm before fixing)

After a sector change the server re-sends the member's self PlayerIndex, and that
post-warp aux evidently carries a `GroupInfo` block (so `sawGroupInfo` is true)
but with an **empty/sentinel member roster** (populatedMembers == 0). With
GroupInfo present-but-memberless, `TryExtractGroupState` returns a non-null
`GroupState(InGroup:false, ...)` that overwrites the cached true state. So the
`return null` guard does not fire (GroupInfo *is* present), and the
populated-member heuristic produces the wrong answer.

Why the roster would be empty post-warp (to verify): a non-leader's own
PlayerIndex may only get the full member list in the initial full-create aux, with
later refreshes (sector entry) sending a partial GroupInfo; or the per-sector
GroupInfo is the leader's authoritative copy and the member's is stale. Needs a
capture.

### Diagnostic step (do this first)

The CLI already logs every packet (NDJSON sink). Reproduce, then diff the self
`0x001B` aux **before vs after** the warp:

- Does the post-warp GroupInfo carry IsGroupLeader / Formation but zero populated
  members? (Confirms the hypothesis above.)
- Is `Formation` still non-zero after the warp (member still formed up)? If so,
  `InFormation: true` + `InGroup: false` is internally contradictory and is a
  second tell that the member-count signal is the weak link.
- Capture both frames into a fixture under the unit-test Captures dir so the fix
  is byte-pinned against the real post-warp aux, not a hand-built guess.

## Fix directions (pick after the diagnostic)

1. **Do not downgrade InGroup on an ambiguous aux.** Treat "GroupInfo present but
   no populated members AND not an explicit leave" as *unknown* -> keep cached
   `InGroup`. Only clear it on a definitive signal (see option 2/3). Lowest-risk;
   matches the existing "return null to preserve cache" philosophy.
2. **Track the group lifecycle from events, not just auxes.** Set InGroup=true on
   a successful `group accept` / on the "You have been invited ... " ->
   accept flow, and clear it only on an explicit leave/disband
   (the 0x002C Action 14 response, or the server's "you have left the group"
   message). The aux then only *enriches* (leader flag, formation), never
   downgrades membership.
3. **Find a stronger in-aux signal.** If the post-warp GroupInfo reliably carries
   IsGroupLeader or a non-default FormationName even with an empty roster, key
   InGroup off "GroupInfo has any non-default field" rather than member count.
   Weakest option -- depends entirely on what the capture shows.

Option 1 + 2 together is probably the right shape: events own membership truth,
auxes own leader/formation detail, and an ambiguous aux never clears membership.

## Mandatory per CLAUDE.md

- This is CLI-only (no server/proxy wire change). Update the byte-pin tests in
  `SelfGroupStateTests` to cover the real post-warp aux fixture, and add a case
  that proves a memberless-GroupInfo aux does **not** clear a previously-grouped
  state.
- No CV entry strictly required (no server change), but verifying the fix end to
  end (accept -> form up -> warp -> `formation break` still works) is worth a line
  in `plans/29-client-verification.md` since only the real client produces the
  post-warp aux sequence that triggered it.
