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
```

### Backends (auto-detected; no WSL or MinGW required)

1. **dumpcap** (preferred) -- ships with Wireshark, uses the Npcap driver.
   Produces a real `.pcapng` with BPF filtering and loopback support; the
   closest match to the Linux `tcpdump` output. Install:
   - Npcap -- <https://npcap.com> (tick "Support loopback traffic")
   - Wireshark -- <https://www.wireshark.org> (provides `dumpcap.exe`; its
     installer also bundles a compatible Npcap)
2. **pktmon** (fallback) -- built into Windows 10 1809+ and Windows 11, so it
   needs **nothing installed**. Captures to `.etl`, which the script converts to
   `.pcapng` via `pktmon pcapng`. Filters are port/protocol only (no PID), which
   is what the script feeds it.

Run `Get-Help .\Start-WindowsEnbProxyCapture.ps1 -Full` for all parameters.

> Captures may contain real credentials on the login leg -- treat output files
> like the Linux ones (do not commit; `proxy/local-debug/` is gitignored).
