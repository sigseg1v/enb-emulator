# Phase AV -- HUD render/flicker decouple

Owner ask (2026-06-20): the Freya HUD / chat flickers, and a "duplicate chat"
briefly appears at top-middle when a new message arrives. Owner explicitly said:
do NOT fix immediately -- capture it in a separate plan. This file is that plan.

## Symptoms (owner-reported)

- The HUD "hides randomly and shows up flickering, sometimes."
- A duplicate of the chat appears at top-middle of the screen temporarily,
  correlated with new chat messages arriving.

## What is already proven (this session)

`enb.diag()` (added to the DLL: `tick_count`, `present_count`) measured live in
one in-space session:

| | tick (HUD rebuild) | present (HUD draw) | ratio |
|---|---|---|---|
| idle | ~55/s | ~33/s | 1.7x |
| mouse-move flood | ~130/s | ~31/s | 4.1x |

So the display-list REBUILD rate is clocked off the message pump
(`hooks.cpp` `hk_PeekMessageA` -> `g_tick()` -> `tick()` ->
`overlay::begin_frame`/run `on_tick` handlers/`overlay::commit_frame`) and scales
with mouse input, while the DRAW rate (`overlay.cpp` `hk_Present`) is steady and
independent of input. The rebuild is wastefully pump-clocked; the draw is not.

## Root-cause candidates (NOT yet disambiguated -- do this first)

These are likely TWO different bugs the owner has lumped under "flicker". Do not
assume they share a fix.

1. **Overlay rebuild decoupled from / outpacing draw (the Freya HUD flicker).**
   `tick()` and `hk_Present` both run on the one game thread, and `commit_frame`
   only swaps a fully-built staging list, so a partial frame "shouldn't" be
   drawable. BUT if any `on_tick` handler conditionally emits NOTHING on some
   ticks (e.g. `enb.self()` transiently nil, a visibility gate flapping), that
   tick commits an EMPTY list; whichever rebuild happens to be the last one
   before a Present is what gets drawn, so an occasional empty rebuild => a blank
   HUD frame => visible flicker. Mouse-flood multiplies ticks-per-present
   (4x), widening the odds of catching an empty/partial rebuild between presents.
   - Candidate fix: clock the display-list rebuild off the Present hook (or gate
     the rebuild to run at most once per present), so rebuild rate == draw rate
     and is independent of mouse input. Keep the cmd-channel poll + event drain
     where they are (or accept they move to present rate). TRADEOFF: if the game
     stops presenting (alt-tab/minimize) the rebuild + cmd channel stall -- the
     `hooks.cpp` heartbeat comment deliberately chose `PeekMessageA` to avoid
     that. Decide explicitly.

2. **Native chat re-show / top-middle duplicate (separate hide-stickiness bug).**
   `scripts/mods/hide-ui/native_hud_hide.lua` `apply()` runs every tick and
   clears `ENABLE_BIT` on `CHAT_TEXT_VT` (0x00afbf28) leaves. `lua_api.cpp:839`
   notes "the game re-shows a gadget when its state changes" -- a new chat
   message changes the chat-text gadget's state, so the game re-asserts it and
   there is a ~1-frame window before the next `apply()` re-hides it.
   - The "top-middle" position is suspicious: the bottom-left chat leaf would
     flash in place, not at top-middle. Strong hint this is actually a SEPARATE
     transient gadget -- e.g. the stock "new message while chat closed"
     popup/toast -- with a DIFFERENT vtable class that `chat_text_leaves()` never
     matches, so it is never hidden at all (not a timing race).
   - INVESTIGATE FIRST: dump the scene leaf list at the instant a message
     arrives (hook the chat-line-append path, snapshot leaves + their VT + flags
     + screen rect) and identify the top-middle leaf's VT. Then either add its VT
     to the wholesale-hide set, or (if it is the same CHAT_TEXT_VT leaf racing)
     hide it synchronously from the append hook instead of waiting for the next
     tick.

## Work items

- [ ] AV-1 Disambiguate the two candidates with a live scene-leaf + diag capture
      at message-arrival time. Record which leaf/VT renders at top-middle.
- [ ] AV-2 Native chat duplicate: hide the offending VT (new-message popup) or
      re-hide synchronously on the append hook. Verify no top-middle flash.
- [ ] AV-3 Overlay rebuild decouple: clock `begin_frame`/`on_tick`/`commit_frame`
      off Present (or once-per-present gate). Re-measure `enb.diag()`: rebuild
      rate should track present rate and stop scaling with mouse input. Decide +
      document the alt-tab/no-present tradeoff.
- [ ] AV-4 Real-client visual confirm (owner): no HUD flicker on fast mouse
      move, no top-middle chat duplicate on new messages. Add a CV entry to
      `plans/29-client-verification.md`.

## Related incident -- pump-clocked tick is also a HANG risk (2026-06-20)

A live local client hard-froze ~1 min after entering space (pump dead: enbmod
log frozen, cmd channel unread, ALL threads parked at 0% CPU; main thread stuck
in the `PeekMessageA`/`GetMessageA` wait). Root cause: the `autocalibrate` dev
mod auto-armed a per-frame memory walk (~250 `enb.mem.*` reads/frame across the
energy/shield/hull gadgets + a churning entity pointer graph) on load. Because
`on_tick` fires off the message-pump hook, that heavy walk ran on the
message-pump thread every iteration and wedged the pump. Fixed by making
autocalibrate opt-in (`require("autocalib").arm()`), so loading it is inert
(`scripts/mods/autocalibrate/autocalib.lua`).

Lesson for this phase: clocking `on_tick` off the message pump is not just
*wasteful* (the flicker), it is *unsafe* -- any `on_tick` handler that is heavy,
blocking, or faults can stall the whole client. AV-3 (decouple rebuild/tick from
the pump) should ALSO add a per-tick budget/guard so one slow handler cannot
wedge the pump, and `on_tick` handlers must stay cheap + non-blocking by
contract. Document that contract where `enb.on_tick` is defined.

## Notes

- `enb.diag()` is the measurement tool; keep it.
- This is client-only (`freya/client-injection/enbmod/`, MIT); no server/proxy
  wire change, so no server integrity gate -- but the real-client visual check
  (AV-4) is the only proof the flicker is actually gone.
