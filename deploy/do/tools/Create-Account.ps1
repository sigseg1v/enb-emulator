#!/usr/bin/env pwsh
# Create a player account on the live droplet over SSH.
#
# Mirrors the repo-root `just seed-account` (an Argon2id PHC stored in
# net7_user.accounts), but targets the DEPLOYED postgres container instead of
# the local dev stack -- driven from deploy/do via `just create-account`.
#
# Two deliberate differences from seed-account, because this hits a LIVE server:
#   1. It REFUSES if the username already exists. seed-account does a
#      DELETE-by-username + INSERT (fine for a throwaway test row); on a live
#      server that would orphan a real player's characters
#      (avatar_info.account_id -> a now-deleted account id) AND silently reset
#      their password. Resetting an existing account needs its own deliberate
#      flow, not this.
#   2. The username is charset-validated (letters/digits/_.-, <=40) so it is
#      safe both as a psql \set literal and in the remote command.
#
# The password is hashed LOCALLY with libsodium Argon2id (python3 + PyNaCl,
# exactly as seed-account does) and only the resulting PHC string crosses the
# wire. The plaintext never touches SQL, never appears in argv, and never
# reaches the droplet. Requires python3-nacl on THIS machine
# (`sudo apt install python3-nacl`).
param(
    [Parameter(Mandatory)][string]$Username,
    [Parameter(Mandatory)][string]$Password
)
. "$PSScriptRoot/../scripts/_Common.ps1"
Import-DeployEnv

if ($Username -notmatch '^[A-Za-z0-9_.-]{1,40}$') {
    throw "Invalid username '$Username'. Allowed: letters, digits, '_', '.', '-' (1-40 chars)."
}

# ---- hash the password locally with Argon2id (libsodium) ----
# Write the plaintext to python's STDIN with NO trailing newline, so the hashed
# bytes are exactly the password the client will send (a stray '\n' would make
# every login fail). The password is never an argv element.
$py = 'import nacl.pwhash,sys; sys.stdout.write(nacl.pwhash.argon2id.str(sys.stdin.buffer.read()).decode())'
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = 'python3'
$psi.ArgumentList.Add('-c')
$psi.ArgumentList.Add($py)
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute        = $false
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.StandardInput.Write($Password)   # no WriteLine -- exact bytes, no newline
$proc.StandardInput.Close()
$phc    = $proc.StandardOutput.ReadToEnd().Trim()
$errTxt = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($phc)) {
    throw "Argon2id hashing failed. Install the libsodium binding: sudo apt install python3-nacl`n$errTxt"
}
if ($phc -notmatch '^\$argon2id\$') { throw "Unexpected PHC format from PyNaCl: $phc" }

$ip = Get-TfOutput 'reserved_ip'
Write-Host "Droplet : root@$ip"
Write-Host "Account : $Username (status=100)"

# ---- refuse if the username already exists ----
$existsSql = "\set username '$Username'`nSELECT count(*) FROM accounts WHERE username = :'username';"
$exists = (Invoke-RemotePsql -ReservedIp $ip -Database 'net7_user' -PsqlFlags @('-tA') -Sql $existsSql | Out-String).Trim()
if ($exists -ne '0') {
    throw "Account '$Username' already exists (count=$exists). Refusing to overwrite a live account."
}

# ---- insert ----
# $Username (validated) and $phc (starts with $argon2id$, base64 body -- no
# single quote) are embedded as psql \set literals; :'var' re-quotes each as a
# safe SQL string literal. The whole script travels over ssh stdin, so the
# PHC's '$' signs are never seen by a shell.
$insertSql = @"
\set username '$Username'
\set phc '$phc'
SELECT setval('accounts_id_seq', GREATEST((SELECT COALESCE(MAX(id),0) FROM accounts), 1));
INSERT INTO accounts (username, password_phc, status, formname, email)
VALUES (:'username', :'phc', 100, :'username' || '_form', :'username' || '@local');
"@
Invoke-RemotePsql -ReservedIp $ip -Database 'net7_user' -Sql $insertSql | Out-Null

Write-Host ">>> created account '$Username' on $ip (log in from the client to create characters)"
