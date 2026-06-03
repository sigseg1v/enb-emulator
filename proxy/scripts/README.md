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

### Sectors and the port filter (important)

The proxy's ports are read **once** at launch and frozen into the capture
filter for the whole run. When you warp/jump to another sector the proxy opens a
**new** socket to a different sector server, and that new port is **not** in the
filter -- its traffic is silently dropped. So if you plan to move between
sectors, capture **one file per sector**: stop with Ctrl+C and re-launch with a
distinct `-Prefix` for each (`-Prefix Luna`, then `-Prefix Aganju`, ...), which
re-resolves the filter against the proxy's then-current sockets. Alternatives:
list every server port you expect via `-ExtraPorts`, or run unfiltered (don't
pass ports) and scope the `.pcapng` afterward in Wireshark.

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
