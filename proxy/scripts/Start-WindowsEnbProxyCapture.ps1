<#
.SYNOPSIS
    Capture the Net7Proxy's network traffic on Windows, by process, to a
    timestamped pcapng under .\captures\ -- the Windows counterpart to the
    Linux `nsenter -t <PID> -n tcpdump -i any -s0 -w ...` flow.

.DESCRIPTION
    Windows has no per-process network namespace, so there is no exact
    equivalent of `nsenter -n` that scopes a capture to one PID. The faithful
    substitute is: find the proxy process, look up the TCP/UDP ports it owns,
    then capture the whole machine filtered to just those ports. That yields the
    same conversation the Linux capture does (the proxy <-> server cleartext UDP
    legs and the client <-> proxy TCP legs), minus unrelated host noise.

    Capture is done with dumpcap, which ships with Wireshark and uses the Npcap
    driver. It produces a real .pcapng, supports BPF capture filters and
    loopback, and is byte-for-byte the format our tooling already reads -- the
    closest match to the Linux tcpdump output. dumpcap is REQUIRED: if it is not
    found, the script fails (no fallback).

    Does NOT require WSL or MinGW.

.PARAMETER ProcessName
    Process image name to find (without .exe). Default: Net7Proxy.

.PARAMETER ProcessId
    Capture this exact PID instead of searching by name. Overrides ProcessName.

.PARAMETER Prefix
    Optional label prepended (with a dash) to the capture file name, for
    organizing files -- e.g. -Prefix Luna yields "Luna-enbproxy-...pcapng".

.PARAMETER OutputDir
    Folder for capture files. Default: .\captures (created if missing).

.PARAMETER DurationSeconds
    Stop automatically after this many seconds. 0 (default) = run until you
    press Ctrl+C (the file is finalized cleanly on stop either way).

.PARAMETER ExtraPorts
    Additional TCP/UDP ports to include in the filter (e.g. known server ports
    the proxy talks to: 3500,3601,3636,3806,3808). Useful if the proxy hasn't
    opened a socket yet at capture start.

.PARAMETER SnapLength
    Bytes captured per packet. 0 (default) = whole packet, matching `-s0`.

.PARAMETER DumpcapPath
    Explicit path to dumpcap.exe. By default the script looks on PATH and in the
    standard Wireshark install dirs.

.EXAMPLE
    # Capture Net7Proxy until Ctrl+C (run from an elevated PowerShell):
    .\Start-WindowsEnbProxyCapture.ps1

.EXAMPLE
    # Capture a specific PID for 120s, also include the sector/MVAS server ports:
    .\Start-WindowsEnbProxyCapture.ps1 -ProcessId 4812 -DurationSeconds 120 -ExtraPorts 3636,3806

.EXAMPLE
    # Label the file for organizing -- yields captures\Luna-enbproxy-...pcapng:
    .\Start-WindowsEnbProxyCapture.ps1 -Prefix Luna

.NOTES
    Run from an ELEVATED PowerShell (Run as administrator) -- packet capture
    needs admin.

    Stopping with Ctrl+C is safe: dumpcap catches the console Ctrl+C, stops the
    capture loop, and finalizes the .pcapng. pcapng is flushed block-by-block as
    packets arrive, so the file is complete and NOT corrupt -- it holds every
    packet captured up to the moment you pressed Ctrl+C.

    Required software (dumpcap backend; nothing else):
      - Npcap         https://npcap.com  (the capture driver; tick
                      "Support loopback traffic" during install)
      - Wireshark     https://www.wireshark.org  (provides dumpcap.exe; the
                      Wireshark installer bundles a compatible Npcap)
#>

[CmdletBinding()]
param(
    [string]   $ProcessName     = 'Net7Proxy',
    [int]      $ProcessId       = 0,
    [string]   $Prefix          = '',
    [string]   $OutputDir       = '.\captures',
    [int]      $DurationSeconds = 0,
    [int[]]    $ExtraPorts      = @(),
    [int]      $SnapLength      = 0,
    [string]   $DumpcapPath     = ''
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-Dumpcap {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path $Explicit) { return $Explicit }
        Write-Error "dumpcap not found at -DumpcapPath '$Explicit'."
        exit 1
    }
    $cmd = Get-Command dumpcap.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
        "$env:ProgramFiles\Wireshark\dumpcap.exe",
        "${env:ProgramFiles(x86)}\Wireshark\dumpcap.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

if (-not (Test-Admin)) {
    Write-Error "This script must run in an ELEVATED PowerShell (Run as administrator). Packet capture needs admin."
    exit 1
}

# --- dumpcap is required; fail early if it's missing -----------------------
$dumpcap = Find-Dumpcap -Explicit $DumpcapPath
if (-not $dumpcap) {
    Write-Error @"
dumpcap.exe was not found. This script requires it (no fallback).
Install:
  - Npcap      https://npcap.com  (tick "Support loopback traffic")
  - Wireshark  https://www.wireshark.org  (provides dumpcap.exe)
Then re-run, or pass -DumpcapPath 'C:\path\to\dumpcap.exe'.
"@
    exit 1
}

# --- resolve the target process --------------------------------------------
if ($ProcessId -gt 0) {
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $proc) { Write-Error "No process with PID $ProcessId is running."; exit 1 }
} else {
    $candidates = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) {
        Write-Error "No process named '$ProcessName' is running. Start the proxy first, or pass -ProcessId. (Tip: Get-Process *net7* to find it.)"
        exit 1
    }
    if ($candidates.Count -gt 1) {
        Write-Warning "Multiple '$ProcessName' processes found (PIDs: $($candidates.Id -join ', ')). Using the first; pass -ProcessId to pick one."
    }
    $proc = $candidates[0]
}
$targetPid = $proc.Id
Write-Host "Target process: $($proc.ProcessName) (PID $targetPid)" -ForegroundColor Cyan

# --- resolve the ports it owns ---------------------------------------------
# Windows can't filter a capture by PID, so we capture machine-wide filtered to
# the union of TCP local+remote and UDP local ports this PID currently holds.
#
# CAVEAT -- ports are read ONCE, here, and baked into a static BPF filter for the
# whole run. EnB sector servers listen on different ports, and the proxy opens a
# NEW socket (new remote port) when you warp/jump to another sector. That later
# port is NOT in this snapshot, so its traffic is silently dropped from the
# capture. If you are going to move between sectors, prefer ONE capture PER
# sector: stop (Ctrl+C) and re-launch with a distinct -Prefix for each sector
# (e.g. -Prefix Luna, then -Prefix Aganju) so the filter is re-resolved against
# the proxy's then-current sockets. Alternatively pass every server port you
# expect to touch up-front via -ExtraPorts, or run unfiltered (no ports) and
# scope the pcap afterward in Wireshark.
$ports = New-Object System.Collections.Generic.HashSet[int]
Get-NetTCPConnection -OwningProcess $targetPid -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.LocalPort  -gt 0) { [void]$ports.Add([int]$_.LocalPort)  }
    if ($_.RemotePort -gt 0) { [void]$ports.Add([int]$_.RemotePort) }
}
Get-NetUDPEndpoint -OwningProcess $targetPid -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.LocalPort -gt 0) { [void]$ports.Add([int]$_.LocalPort) }
}
foreach ($p in $ExtraPorts) { if ($p -gt 0) { [void]$ports.Add([int]$p) } }

if ($ports.Count -eq 0) {
    Write-Warning "PID $targetPid has no open TCP/UDP sockets right now. The capture will run UNFILTERED (whole-machine traffic). Pass -ExtraPorts to scope it, or start the capture after the proxy has connected."
}
$portList = @($ports) | Sort-Object
if ($portList.Count -gt 0) {
    Write-Host "Capturing ports: $($portList -join ', ')" -ForegroundColor Cyan
    Write-Host "Note: these ports are fixed for this run. If you warp to another sector the proxy may open a NEW port that won't be captured -- stop and re-launch per sector with a distinct -Prefix." -ForegroundColor DarkYellow
}

# --- output path -----------------------------------------------------------
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
# Optional -Prefix label, sanitized of path-illegal characters, prepended with
# a dash so e.g. -Prefix Luna -> "Luna-enbproxy-...".
$prefixPart = ''
if (-not [string]::IsNullOrWhiteSpace($Prefix)) {
    $clean = ($Prefix -replace '[\\/:*?"<>|]', '_').Trim()
    if ($clean) { $prefixPart = "$clean-" }
}
$outFile = Join-Path $OutputDir "$prefixPart`enbproxy-$($proc.ProcessName)-pid$targetPid-$stamp.pcapng"

# --- build a BPF capture filter --------------------------------------------
# "port N" matches src OR dst, so the union of the proxy's local+remote ports
# captures both directions of each socket.
$filter = ''
if ($portList.Count -gt 0) {
    $filter = ($portList | ForEach-Object { "port $_" }) -join ' or '
}

# Capture on ALL interfaces (dumpcap takes repeated -i). This mirrors tcpdump's
# `-i any`; loopback is included when Npcap was installed with loopback support
# (proxy<->server on localhost rides loopback).
$ifaceArgs = @()
& $dumpcap -D 2>$null | ForEach-Object {
    if ($_ -match '^\s*(\d+)\.') { $ifaceArgs += @('-i', $Matches[1]) }
}
if ($ifaceArgs.Count -eq 0) { $ifaceArgs = @('-i', '1') }  # fall back to first

$dcArgs = $ifaceArgs + @('-s', $SnapLength, '-w', $outFile)
if ($filter)               { $dcArgs += @('-f', $filter) }
if ($DurationSeconds -gt 0) { $dcArgs += @('-a', "duration:$DurationSeconds") }

Write-Host "Backend: dumpcap ($dumpcap)" -ForegroundColor Green
Write-Host "Writing: $outFile" -ForegroundColor Green
if ($DurationSeconds -gt 0) { Write-Host "Stopping after $DurationSeconds s." }
else { Write-Host "Press Ctrl+C to stop and finalize the file." -ForegroundColor Yellow }

# Invoke dumpcap as a native child so Ctrl+C reaches it directly: Windows
# delivers CTRL_C_EVENT to every process in the console group, and dumpcap's
# console-control handler stops the capture loop and CLOSES the pcapng cleanly.
# pcapng is written block-by-block with each block flushed as it is captured, so
# a Ctrl+C stop yields a complete, non-corrupt file containing every packet up
# to the interrupt (it is NOT a kill -9 that would truncate mid-block).
& $dumpcap @dcArgs
Write-Host "Done. Capture saved to $outFile" -ForegroundColor Green
