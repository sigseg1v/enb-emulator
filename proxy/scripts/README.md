# proxy/scripts

Operational helpers for the proxy. Not part of the build.

## `build-openssl-mingw.sh`

Builds static OpenSSL 3 for the MinGW-w64 (x86_64) cross-build. Produces the
`proxy/third_party/openssl-mingw64/` prefix that the proxy's Win32 target links
against. See the script header.

## `Start-WindowsEnbProxyCapture.ps1`

Windows packet capture of the Net7Proxy, by process, to a timestamped
pcap/pcapng under `.\captures\`. The Windows counterpart to the Linux capture
flow:

```sh
# Linux (server side / WINE host): enter the proxy's net namespace and capture.
ps aux | grep -i Net7Proxy
sudo nsenter -t <PID> -n tcpdump -i any -nn -s0 -w network-capture.pcap
```

Windows has no per-process network namespace, so there is no exact `nsenter -n`
equivalent. The script instead finds the proxy PID, looks up the TCP/UDP ports
that PID owns (`Get-NetTCPConnection` / `Get-NetUDPEndpoint -OwningProcess`), and
captures the machine filtered to just those ports -- the same conversation,
minus unrelated host noise.

Run from an **elevated** PowerShell:

```powershell
# Until Ctrl+C:
.\Start-WindowsEnbProxyCapture.ps1

# A specific PID for 120s, also including the sector/MVAS server ports:
.\Start-WindowsEnbProxyCapture.ps1 -ProcessId 4812 -DurationSeconds 120 -ExtraPorts 3636,3806

# Label the file for organizing -> captures\Luna-enbproxy-...pcapng:
.\Start-WindowsEnbProxyCapture.ps1 -Prefix Luna
```

`-Prefix <label>` prepends `<label>-` to the file name. Stopping with **Ctrl+C
is safe**: dumpcap finalizes the `.pcapng` (pcapng is flushed block-by-block as
packets arrive), so the file is complete and not corrupt -- it contains every
packet captured up to the interrupt.

### Ports captured

The filter is the union of three sources:

1. **The EnB port baseline (always on)** -- the known control ports
   `3500, 3601, 3636, 3805, 3801, 3806, 3807, 3808, 3809, 3810` plus the entire
   sector-server band **3501-3800** (base `SECTOR_SERVER_PORT` 3501 + up to 300
   sectors, emitted as a single BPF `portrange`). This is what makes sector
   warps stay captured: a new sector server's port still falls inside the band,
   so you do **not** need to re-launch per sector. Pass `-NoBaselinePorts` to
   drop it and capture only the resolved/extra ports.
2. **The proxy PID's owned ports** at launch (its current TCP local+remote and
   UDP local sockets).
3. **`-ExtraPorts`** -- anything extra you name, on top of the above.

Even so, `-Prefix` per sector is still handy for *organizing* files when you
want one capture per sector; it is no longer *required* for coverage.

### Required software (no WSL or MinGW)

Capture uses **dumpcap**, which ships with Wireshark and uses the Npcap driver.
It produces a real `.pcapng` with BPF filtering and loopback support -- the
closest match to the Linux `tcpdump` output. dumpcap is **required**; if it is
not found the script fails (no fallback).

- Npcap -- <https://npcap.com> (tick "Support loopback traffic")
- Wireshark -- <https://www.wireshark.org> (provides `dumpcap.exe`; its
  installer also bundles a compatible Npcap)

Pass `-DumpcapPath 'C:\path\to\dumpcap.exe'` if it is installed somewhere
non-standard.

Run `Get-Help .\Start-WindowsEnbProxyCapture.ps1 -Full` for all parameters.

> Captures may contain real credentials on the login leg -- treat output files
> like the Linux ones (do not commit; `proxy/local-debug/` is gitignored).
