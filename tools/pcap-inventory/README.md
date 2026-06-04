# pcap-inventory

Decode a proxy&lt;-&gt;server sector capture (`.pcapng`) into a human-readable
inventory of every nav / station / gate, mob and resource the captured client
came within range of -- with location, name, type, combat level (mobs), faction
(mobs) and disposition (hostile / friendly, from `0x0089 RELATIONSHIP`).

```
dotnet run --project tools/pcap-inventory -- <input.pcapng> [output.txt]
```

Default output is `<input>.inventory.txt` next to the input.

## What it reads

It reassembles the downstream sector UDP stream (`0x2016 PACKET_SEQUENCE` /
`0x201A PACKET_C_SEQUENCE`) and decodes the per-object frames, reusing the real
`CliClient.Core` decoders so the byte semantics stay in lock-step with the CLI
client and its tests:

| Opcode | Field surfaced |
|---|---|
| `0x0004` CREATE | game id, base asset, create-type (mob / planet / ...) |
| `0x2018` STATIC_OBJECT_CREATE | nav / station / gate name, position, signature |
| `0x2019` RESOURCE_OBJECT_CREATE | resource name, position, resource template |
| `0x001B` AUX_DATA | mob name, combat level, faction, ship max speed |
| `0x0089` RELATIONSHIP | reaction (0 attack / 1 shun / 2 friendly / 3 adoration) + is-attacking |
| `0x0099` NAVIGATION, `0x0008` / `0x0040` / `0x003E` / `0x003F` | nav flags, positions |

## Caveats (read before trusting the output)

- **It is an inventory, not a live model.** It deliberately ignores `0x0007
  REMOVE` so flying out of range never deletes an object already catalogued.
- **Completeness is path-bound.** Navs have huge signature ranges so the nav set
  is reliable, but mobs and resources are range-gated: the listing is everything
  the capture's flight path passed, which is *not* provably the whole sector.
- **The capture is the cleartext proxy&lt;-&gt;server leg.** Object creates are
  in the server's compact (`0x2018` / `0x2019`) form, not the expanded
  client-facing form. See `CLAUDE.md` ("The proxy is NOT a dumb relay").
- **Captures and outputs are not committed.** A `proxy/local-debug/` capture can
  contain real credentials / session data; this tool's `.gitignore` keeps
  `*.pcapng` and `*.inventory.txt` out of git.

Only LITTLE-ENDIAN pcapng with `LINKTYPE_ETHERNET` (IPv4/UDP) is supported --
which is what the loopback proxy-side captures use.
