<#
.SYNOPSIS
    Capture the Net7Proxy's network traffic on Windows, by process, to a
    timestamped pcap/pcapng under .\captures\ -- the Windows counterpart to the
    Linux `nsenter -t <PID> -n tcpdump -i any -s0 -w ...` flow.

.DESCRIPTION
    Windows has no per-process network namespace, so there is no exact
    equivalent of `nsenter -n` that scopes a capture to one PID. The faithful
    substitute is: find the proxy process, look up the TCP/UDP ports it owns,
    then capture the whole machine filtered to just those ports. That yields the
    same conversation the Linux capture does (the proxy <-> server cleartext UDP
    legs and the client <-> proxy TCP legs), minus unrelated host noise.

    Two capture backends are supported, auto-detected in this order:

      1. dumpcap (preferred) -- ships with Wireshark and uses the Npcap driver.
         Produces a real .pcapng, supports BPF capture filters and loopback,
         and is byte-for-byte the format our tooling already reads. This is the
         closest match to the Linux tcpdump output.

      2. pktmon (fallback) -- built into Windows 10 1809+ and Windows 11, so it
         needs NOTHING installed. Captures to an .etl, which the script then
         converts to .pcapng via `pktmon pcapng`. Filters are limited to
         port/protocol (no PID), which is exactly what we feed it.

    NEITHER backend requires WSL or MinGW.

.PARAMETER ProcessName
    Process image name to find (without .exe). Default: Net7Proxy.

.PARAMETER ProcessId
    Capture this exact PID instead of searching by name. Overrides ProcessName.

.PARAMETER OutputDir
    Folder for capture files. Default: .\captures (created if missing).

.PARAMETER DurationSeconds
    Stop automatically after this many seconds. 0 (default) = run until you
    press Ctrl+C (the file is finalized cleanly on stop either way).

.PARAMETER Backend
    Auto (default), Dumpcap, or Pktmon. Auto prefers dumpcap, falls back to
    pktmon.

.PARAMETER ExtraPorts
    Additional TCP/UDP ports to include in the filter (e.g. known server ports
    the proxy talks to: 3500,3601,3636,3806,3808). Useful if the proxy hasn't
    opened a socket yet at capture start.

.PARAMETER SnapLength
    Bytes captured per packet. 0 (default) = whole packet, matching `-s0`.

.EXAMPLE
    # Capture Net7Proxy until Ctrl+C (run from an elevated PowerShell):
    .\Start-WindowsEnbProxyCapture.ps1

.EXAMPLE
    # Capture a specific PID for 120s, also include the sector/MVAS server ports:
    .\Start-WindowsEnbProxyCapture.ps1 -ProcessId 4812 -DurationSeconds 120 -ExtraPorts 3636,3806

.NOTES
    Run from an ELEVATED PowerShell (Run as administrator) -- packet capture
    needs admin on both backends.

    Software to install for the dumpcap backend:
      - Npcap         https://npcap.com  (the capture driver; tick
                      "Support loopback traffic" during install)
      - Wireshark     https://www.wireshark.org  (provides dumpcap.exe; the
                      Wireshark installer bundles a compatible Npcap)
    The pktmon backend needs nothing installed (built into Windows).
#>

[CmdletBinding()]
param(
    [string]   $ProcessName     = 'Net7Proxy',
    [int]      $ProcessId       = 0,
    [string]   $OutputDir       = '.\captures',
    [int]      $DurationSeconds = 0,
    [ValidateSet('Auto', 'Dumpcap', 'Pktmon')]
    [string]   $Backend         = 'Auto',
    [int[]]    $ExtraPorts      = @(),
    [int]      $SnapLength      = 0
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-Dumpcap {
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

# --- resolve the target process -------------------------------------------
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
}

# --- output path -----------------------------------------------------------
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$stamp    = Get-Date -Format 'yyyyMMdd-HHmmss'
$baseName = "enbproxy-$($proc.ProcessName)-pid$targetPid-$stamp"

# --- pick a backend --------------------------------------------------------
$dumpcap = Find-Dumpcap
$useDumpcap = switch ($Backend) {
    'Dumpcap' { if (-not $dumpcap) { Write-Error "dumpcap.exe not found. Install Wireshark + Npcap, or use -Backend Pktmon."; exit 1 }; $true }
    'Pktmon'  { $false }
    default   { [bool]$dumpcap }   # Auto
}

if ($useDumpcap) {
    # ---------------------- dumpcap (Npcap) backend ------------------------
    $outFile = Join-Path $OutputDir "$baseName.pcapng"

    # Build a BPF capture filter. "port N" matches src OR dst, so the union of
    # the proxy's local+remote ports captures both directions of each socket.
    $filter = ''
    if ($portList.Count -gt 0) {
        $filter = ($portList | ForEach-Object { "port $_" }) -join ' or '
    }

    # Capture on ALL interfaces (dumpcap takes repeated -i). This mirrors
    # tcpdump's `-i any`; loopback is included when Npcap was installed with
    # loopback support (proxy<->server on localhost rides loopback).
    $ifaceArgs = @()
    & $dumpcap -D 2>$null | ForEach-Object {
        if ($_ -match '^\s*(\d+)\.') { $ifaceArgs += @('-i', $Matches[1]) }
    }
    if ($ifaceArgs.Count -eq 0) { $ifaceArgs = @('-i', '1') }  # fall back to first

    $dcArgs = $ifaceArgs + @('-s', $SnapLength, '-w', $outFile)
    if ($filter)             { $dcArgs += @('-f', $filter) }
    if ($DurationSeconds -gt 0) { $dcArgs += @('-a', "duration:$DurationSeconds") }

    Write-Host "Backend: dumpcap ($dumpcap)" -ForegroundColor Green
    Write-Host "Writing: $outFile" -ForegroundColor Green
    if ($DurationSeconds -gt 0) { Write-Host "Stopping after $DurationSeconds s." }
    else { Write-Host "Press Ctrl+C to stop and finalize the file." -ForegroundColor Yellow }

    & $dumpcap @dcArgs
    Write-Host "Done. Capture saved to $outFile" -ForegroundColor Green
}
else {
    # ---------------------- pktmon (built-in) backend ----------------------
    if (-not (Get-Command pktmon.exe -ErrorAction SilentlyContinue)) {
        Write-Error "Neither dumpcap nor pktmon is available. Install Wireshark+Npcap (recommended), or use a Windows build with pktmon (10 1809+/11)."
        exit 1
    }
    if ($portList.Count -eq 0) {
        Write-Error "pktmon needs at least one port to filter on (it cannot capture by PID). Re-run once the proxy has open sockets, or pass -ExtraPorts."
        exit 1
    }

    $etlFile    = Join-Path $OutputDir "$baseName.etl"
    $pcapngFile = Join-Path $OutputDir "$baseName.pcapng"

    Write-Host "Backend: pktmon (built-in)" -ForegroundColor Green

    # Clear any leftover filters from a previous run, then add one per port.
    # Each `filter add` is OR'd, so this matches any of the proxy's ports.
    & pktmon filter remove 2>$null | Out-Null
    foreach ($p in $portList) {
        & pktmon filter add "enbproxy-$p" -p $p | Out-Null
    }

    # --pkt-size 0 = whole packet (the -s0 equivalent); --comp nics captures
    # the physical/virtual NICs.
    & pktmon start --capture --pkt-size 0 --comp nics --file-name $etlFile | Out-Null
    Write-Host "Writing: $etlFile" -ForegroundColor Green

    try {
        if ($DurationSeconds -gt 0) {
            Write-Host "Capturing for $DurationSeconds s..."
            Start-Sleep -Seconds $DurationSeconds
        } else {
            Write-Host "Press Ctrl+C to stop." -ForegroundColor Yellow
            while ($true) { Start-Sleep -Seconds 1 }
        }
    }
    finally {
        & pktmon stop | Out-Null
        & pktmon filter remove 2>$null | Out-Null
        Write-Host "Converting $etlFile -> $pcapngFile ..." -ForegroundColor Green
        & pktmon pcapng $etlFile -o $pcapngFile | Out-Null
        Write-Host "Done. Capture saved to $pcapngFile (raw ETL kept at $etlFile)" -ForegroundColor Green
    }
}
