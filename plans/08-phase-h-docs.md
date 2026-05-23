# Phase H — Deepen docs

Goal: expand Phase A docs with reverse-engineered protocol details, runtime walkthroughs, and ability-system internals.

## Items

- [ ] `docs/03-network-protocol.md` deepening: open `capturedPackets/*.rar` (extract with `unrar x`), classify packet types, add a packet-type table.
- [ ] `docs/04-server-modules.md` deepening: add sequence diagrams (mermaid) for login → character select → enter sector.
- [ ] `docs/05-abilities.md` deepening: for each ability, link to its `.cpp`, summarise effect, list cooldown/range/damage formula.
- [ ] `docs/12-content-pipeline.md` — how content (sectors, mobs, missions, items) flows from C# editors → DB → server.
- [ ] `docs/13-gameplay-loop.md` — high-level: combat, exploration, missions, trading, guilds.
- [ ] `docs/14-extending.md` — how to add a new ability, mob type, sector.

## Verification

- All new docs grounded in real file:line references (spot check 5 random claims).
- Proceed to Phase I.
