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
    EXTRA TCP/UDP ports to include on top of the always-on baseline. The
    baseline (3500,3601,3636,3806,3808 plus the whole sector-server band
    3501-3800) is ALWAYS in the filter regardless of this parameter, so you only
    need -ExtraPorts for something outside that set.

.PARAMETER SnapLength
    Bytes captured per packet. 0 (default) = whole packet, matching `-s0`.

.PARAMETER NoBaselinePorts
    Drop the always-on EnB port baseline and capture ONLY the PID's resolved
    ports plus -ExtraPorts. Use if you want a tightly-scoped file.

.PARAMETER DumpcapPath
    Explicit path to dumpcap.exe. By default the script looks on PATH and in the
    standard Wireshark install dirs.

.EXAMPLE
    # Capture Net7Proxy until Ctrl+C (run from an elevated PowerShell):
    .\Start-WindowsEnbProxyCapture.ps1

.EXAMPLE
    # Capture a specific PID for 120s (the EnB port baseline is always included):
    .\Start-WindowsEnbProxyCapture.ps1 -ProcessId 4812 -DurationSeconds 120

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
    [switch]   $NoBaselinePorts,
    [string]   $DumpcapPath     = ''
)

$ErrorActionPreference = 'Stop'

# Always-on EnB port baseline. The proxy may not have opened every socket yet at
# capture start (and warping to a new sector opens a fresh sector-server port),
# so we ALWAYS include the known control ports and the full sector-server band
# rather than relying solely on what the PID owns at this instant.
#   3500  proxy local TCP terminator (PROXY_LOCAL_TCP_PORT)
#   3601  client<->proxy game leg
#   3636  client<->proxy aux leg
#   3806  MVAS login (MVAS_LOGIN_PORT)
#   3808  UDP master server (UDP_MASTER_SERVER_PORT)
# plus 3805 (global), 3801 (master), 3807/3809/3810 control/UDP legs, and the
# sector-server band 3501..3800 (base SECTOR_SERVER_PORT 3501 + up to 300
# sectors) expressed as a BPF portrange so we don't emit 300 OR-terms.
$BaselineDiscretePorts = @(3500, 3601, 3636, 3805, 3801, 3806, 3807, 3808, 3809, 3810)
$SectorPortLow  = 3501
$SectorPortHigh = 3800

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

# --- resolve the ports to capture ------------------------------------------
# Windows can't filter a capture by PID, so we capture machine-wide filtered to
# a set of ports. That set is: the EnB port baseline (always, unless
# -NoBaselinePorts) + the TCP local+remote and UDP local ports this PID owns
# right now + -ExtraPorts. The baseline matters because the PID's owned-port
# snapshot is read ONCE here -- a socket the proxy opens later (notably a new
# sector-server port when you warp) would otherwise be missed. The baseline
# already covers the whole 3501..3800 sector band, so warping stays captured.
$ports = New-Object System.Collections.Generic.HashSet[int]
Get-NetTCPConnection -OwningProcess $targetPid -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.LocalPort  -gt 0) { [void]$ports.Add([int]$_.LocalPort)  }
    if ($_.RemotePort -gt 0) { [void]$ports.Add([int]$_.RemotePort) }
}
Get-NetUDPEndpoint -OwningProcess $targetPid -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.LocalPort -gt 0) { [void]$ports.Add([int]$_.LocalPort) }
}
foreach ($p in $ExtraPorts) { if ($p -gt 0) { [void]$ports.Add([int]$p) } }

# Fold in the always-on baseline (discrete ports here; the sector band is added
# as a portrange in the filter below so we don't enumerate 300 ports).
$useSectorRange = $false
if (-not $NoBaselinePorts) {
    foreach ($p in $BaselineDiscretePorts) { [void]$ports.Add([int]$p) }
    $useSectorRange = $true
}

if ($ports.Count -eq 0 -and -not $useSectorRange) {
    Write-Warning "PID $targetPid has no open TCP/UDP sockets right now and -NoBaselinePorts was given. The capture will run UNFILTERED (whole-machine traffic). Pass -ExtraPorts to scope it."
}
$portList = @($ports) | Sort-Object
if ($portList.Count -gt 0) {
    Write-Host "Capturing ports: $($portList -join ', ')$(if ($useSectorRange) { " + sector band $SectorPortLow-$SectorPortHigh" })" -ForegroundColor Cyan
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
# captures both directions of each socket. The sector band goes in as a single
# "portrange LOW-HIGH" term (BPF native) instead of 300 OR-ed "port" terms.
$terms = @()
foreach ($p in $portList) { $terms += "port $p" }
if ($useSectorRange) { $terms += "portrange $SectorPortLow-$SectorPortHigh" }
$filter = $terms -join ' or '

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
