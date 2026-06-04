# pcap-inventory

Decode a proxy&lt;-&gt;server sector capture (`.pcapng`) into a human-readable
inventory of every nav / station / gate, mob and resource the captured client
came within range of -- with location, name, type, combat level (mobs), faction
(mobs) and disposition (hostile / friendly, from `0x0089 RELATIONSHIP`).

```
dotnet run --project tools/pcap-inventory -- <input.pcapng> [output.txt]
```

Default output is `<input>.inventory.txt` next to the input.

## Drag-and-drop on Windows (no .NET install needed)

For a non-technical user, build a self-contained single-file `.exe`:

```
just package-pcap-inventory      # -> bin/pcap-inventory.exe
```

Then **drag a `.pcapng` (or `.pcap`) file onto `pcap-inventory.exe`**. It writes
`<name>.inventory.txt` in the same folder and holds the console window open so
you can read the summary (or any error) before it closes. The build is
self-contained `win-x64`, so it runs on a machine with no .NET runtime
installed. (Run from a terminal -- e.g. `pcap-inventory.exe foo.pcapng` -- and
it does NOT pause, so it scripts cleanly too.)

## What it reads

It reassembles the downstream sector UDP stream (`0x2016 PACKET_SEQUENCE` /
`0x201A PACKET_C_SEQUENCE`) and decodes the per-object frames, reusing the real
`CliClient.Core` decoders so the byte semantics stay in lock-step with the CLI
client and its tests:

| Opcode | Field surfaced |
|---|---|
| `0x0004` CREATE | game id, base asset, create-type (mob / planet / ...) |
| `0x2018` STATIC_OBJECT_CREATE | nav / station / gate name, position, signature, nav class (sig_flags) |
| `0x2019` RESOURCE_OBJECT_CREATE | resource name, position, resource template |
| `0x001B` AUX_DATA | mob name, combat level, faction, ship max speed |
| `0x0089` RELATIONSHIP | reaction (0 attack / 1 shun / 2 friendly / 3 adoration) + is-attacking |
| `0x0099` NAVIGATION, `0x0008` / `0x0040` / `0x003E` / `0x003F` | nav flags, positions |

## Caveats (read before trusting the output)

- **It is an inventory, not a live model.** It deliberately ignores `0x0007
  REMOVE` so flying out of range never deletes an object already catalogued.
- **Nav vs deco is read from the `0x2018` sig_flags byte, not `0x0099`.** A
  *hidden nav* (clickable but off-minimap: `IS_NAV`/`NavType>0` with `HAS_NAV`
  clear -- e.g. "Traders Run", "Strange Ship") emits **no** `0x0099 NAVIGATION`
  frame, because the server gates that on `AppearsInRadar`. Labelling nav-ness
  off `0x0099` alone mislabels every hidden nav as a deco, so the tool decodes
  the create packet's nav class directly: `nav` / `nav (major)` /
  `nav (hidden)` / `nav (hidden, major)` vs a true `deco` (NavType 0).
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
