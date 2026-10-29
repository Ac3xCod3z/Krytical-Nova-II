<#
.SYNOPSIS
  M2c: the first real-socket two-client test of the Netcode for Entities server (DOTS clone only).

.DESCRIPTION
  Starts the probe server (Builds\KnProbeServer, UDP 7797 + page 7798), then client A (roster question, then joins) and client B
  (joins, waits to be kicked), kicks B through the loopback page, checks B exits 4, stops the server with POST /stop and checks it
  exits 0 after the fake save ("SAVE ALL (probe)") and that the save came before the disconnects. Writes probe-report.md.

  Ports: ONLY 7797 (UDP), 7798 (UDP + TCP page) and 8097 (reserved, must also be free). It never starts anything on
  7777/7778/8090/7801/7802, never talks to a save service (the settings name an address nothing listens on, only the key file
  matters) and never reads the owner's secrets. Run it from a quiet PC: no Unity needed, 11 GB free RAM (the server player takes
  about 1-2 GB, a client player about 1-2 GB each; the rest is margin).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File "Assets\_Game\Networking\Entities\Probe\Scripts~\run_probe_2client.ps1"
#>
[CmdletBinding()]
param(
    [string]$ServerDir = "",
    [string]$ClientDir = "",
    [string]$RunDir = "",
    [double]$MinFreeGB = 11,
    [double]$KillFreeGB = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

# ------------------------------------------------------------------ constants (the only ports this script may use)
$GamePort = 7797
$PagePort = 7798
$ReservedPort = 8097
$ForbiddenPorts = @(7777, 7778, 8090, 7801, 7802)
$MapName = 'KnProbeEmpty'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..\..')).Path
if (-not $ServerDir) { $ServerDir = Join-Path $Root 'Builds\KnProbeServer' }
if (-not $ClientDir) { $ClientDir = Join-Path $Root 'Builds\KnProbeClient' }
if (-not $RunDir) { $RunDir = Join-Path $Root ('Builds\KnProbeRuns\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$ServerExe = Join-Path $ServerDir 'KnProbeServer.exe'
$ClientExe = Join-Path $ClientDir 'KnProbeClient.exe'

$script:Started = New-Object System.Collections.ArrayList   # processes this script started
$script:Steps = New-Object System.Collections.ArrayList     # @{ name; ok; detail; seconds }
$script:Clock = [System.Diagnostics.Stopwatch]::StartNew()
$script:MinFreeSeen = 1e9
$script:Failed = $false

function Write-Log([string]$text) { Write-Host ('[{0,6:N1}s] {1}' -f $script:Clock.Elapsed.TotalSeconds, $text) }

function Get-FreeGB { [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2) }

function Stop-All([string]$why) {
    foreach ($p in $script:Started) {
        try { if (-not $p.HasExited) { Write-Log "killing $($p.ProcessName) pid $($p.Id) ($why)"; $p.Kill() } } catch { }
    }
}

function Add-Step([string]$name, [bool]$ok, [string]$detail) {
    [void]$script:Steps.Add(@{ name = $name; ok = $ok; detail = $detail; seconds = [math]::Round($script:Clock.Elapsed.TotalSeconds, 1) })
    if ($ok) { Write-Log "PASS $name  $detail" } else { Write-Log "FAIL $name  $detail"; $script:Failed = $true }
}

# Called in every wait loop: the RAM kill switch and the process-exit check.
function Watch-Ram {
    $free = Get-FreeGB
    if ($free -lt $script:MinFreeSeen) { $script:MinFreeSeen = $free }
    if ($free -lt $KillFreeGB) {
        Stop-All "free RAM $free GB is below the kill switch $KillFreeGB GB"
        throw "kill switch: only $free GB of RAM free"
    }
}

function Wait-Until([scriptblock]$condition, [double]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        Watch-Ram
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 300
    }
    Write-Log "timed out after $seconds s waiting for: $what"
    return $false
}

function Test-PortFree([int]$port) {
    $udp = @(Get-NetUDPEndpoint -LocalPort $port -ErrorAction SilentlyContinue)
    $tcp = @(Get-NetTCPConnection -LocalPort $port -State Listen, Bound -ErrorAction SilentlyContinue)
    return ($udp.Count -eq 0 -and $tcp.Count -eq 0)
}

# One HTTP call to the loopback page. Returns @{ Status; Body }; Status 0 = nobody answered.
function Invoke-Page([string]$method, [string]$path, [string]$key = '') {
    try {
        $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$PagePort$path")
        $req.Method = $method
        $req.Timeout = 5000
        $req.ReadWriteTimeout = 5000
        if ($key) { $req.Headers.Add('X-KN-Key', $key) }
        if ($method -eq 'POST') { $req.ContentLength = 0 }
        $resp = $req.GetResponse()
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ Status = 0; Body = '' } }
    } catch { return @{ Status = 0; Body = '' } }
    try {
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $body = $reader.ReadToEnd()
        $reader.Close()
        return @{ Status = [int]$resp.StatusCode; Body = $body }
    } finally { $resp.Close() }
}

function Start-Probe([string]$exe, [string]$argText, [string]$label) {
    Write-Log "starting $label"
    $p = Start-Process -FilePath $exe -ArgumentList $argText -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $exe)
    [void]$script:Started.Add($p)
    return $p
}

function Read-Result([string]$name) {
    $path = Join-Path $RunDir $name
    if (-not (Test-Path $path)) { return $null }
    try { return (Get-Content -Raw -Path $path | ConvertFrom-Json) } catch { return $null }
}

function Get-Players([string]$key) {
    $r = Invoke-Page 'GET' '/players' $key
    if ($r.Status -ne 200) { return @() }
    try { return @((ConvertFrom-Json $r.Body).players) } catch { return @() }
}

function Quote([string]$s) { return '"' + $s + '"' }

# ------------------------------------------------------------------ main
$report = @()
try {
    # ---- 0. safety: never the owner's server, never other ports
    if ($ForbiddenPorts -contains $GamePort -or $ForbiddenPorts -contains $PagePort -or $ForbiddenPorts -contains $ReservedPort) { throw 'port table error' }
    foreach ($exe in @($ServerExe, $ClientExe)) {
        if (-not (Test-Path $exe)) { throw "missing $exe : build the probe players first (KnProbeBuild.BuildServer and BuildClient)" }
    }
    $free = Get-FreeGB
    $script:MinFreeSeen = $free
    if ($free -lt $MinFreeGB) { throw "only $free GB of RAM free, $MinFreeGB GB needed: nothing was started" }
    Add-Step 'RAM preflight' $true "free $free GB (needs $MinFreeGB)"

    $busy = @()
    foreach ($port in @($GamePort, $PagePort, $ReservedPort)) { if (-not (Test-PortFree $port)) { $busy += $port } }
    if ($busy.Count -gt 0) { throw "port(s) $($busy -join ', ') are in use: nothing was started" }
    Add-Step 'Ports free' $true "UDP/TCP $GamePort, $PagePort, $ReservedPort free"

    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $RunDir 'saves') | Out-Null

    # ---- 1. the server's files: a made-up key (the page's /stop and /players want one) and the settings beside the exe
    $pageKey = ([guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N'))
    $keyFile = Join-Path $RunDir 'probe-key.txt'
    [System.IO.File]::WriteAllText($keyFile, $pageKey)
    $settings = [ordered]@{
        serverName         = 'KN DOTS probe'
        port               = $GamePort
        pagePort           = $PagePort
        maxPlayers         = 8
        map                = $MapName
        saveFolder         = (Join-Path $RunDir 'saves')
        backupHours        = 0
        # Names an address nothing listens on (port 9, discard): the stop endpoint only needs "a save service" to exist for its key.
        saveService        = 'http://127.0.0.1:9'
        saveServiceKeyFile = $keyFile
        plainTransport     = $true
        # These three keys keep the NGO option reader from rewriting the file (it adds them when missing and would drop plainTransport).
        callLimits         = @{}
        ledgerDays         = 90
        mapServers         = @()
    }
    $json = $settings | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $ServerDir 'server-settings.json'), $json)

    # ---- 2. the server
    $serverLog = Join-Path $RunDir 'server.log'
    $serverArgs = '-batchmode -nographics -knprobe server -port {0} -pageport {1} -plaintransport -map {2} -knout {3} -logFile {4}' -f `
        $GamePort, $PagePort, $MapName, (Quote $RunDir), (Quote $serverLog)
    $server = Start-Probe $ServerExe $serverArgs 'server'
    $t0 = $script:Clock.Elapsed.TotalSeconds
    $up = Wait-Until { (-not $server.HasExited) -and ((Invoke-Page 'GET' '/status').Status -eq 200) } 90 'the server page answering /status'
    if (-not $up) {
        $why = if ($server.HasExited) { "the server exited early with code $($server.ExitCode)" } else { 'the page never answered' }
        Add-Step 'Server up' $false $why
        throw $why
    }
    $startupSeconds = [math]::Round($script:Clock.Elapsed.TotalSeconds - $t0, 1)
    $status = (ConvertFrom-Json (Invoke-Page 'GET' '/status').Body)
    Add-Step 'Server up' $true "page answered after $startupSeconds s, build '$($status.build)', players $($status.players)"

    $denied = (Invoke-Page 'GET' '/players' 'wrong-key').Status
    Add-Step 'Page is owner-only' ($denied -eq 403) "GET /players with a wrong key -> $denied (expected 403)"

    # ---- 3. client A: roster question, then joins and stays
    $common = '-batchmode -nographics -knprobe client -knaddr 127.0.0.1 -knport {0} -knpageport {1} -knout {2}' -f $GamePort, $PagePort, (Quote $RunDir)
    $a = Start-Probe $ClientExe ("$common -knname A -knaction roster -kntimeout 40 -logFile " + (Quote (Join-Path $RunDir 'client-A.log'))) 'client A (roster, then connect)'
    $okA = Wait-Until { $r = Read-Result 'kn-probe-client-A.json'; ($null -ne $r -and $r.connected) -or $a.HasExited } 60 'client A connected'
    $ra = Read-Result 'kn-probe-client-A.json'
    $connA = ($okA -and $null -ne $ra -and $ra.connected -and -not $a.HasExited)
    $rosterDetail = if ($null -ne $ra) { "rosterOk=$($ra.rosterOk) answer=$($ra.rosterAnswer) refusal=$($ra.refusalReason) $($ra.error)" } else { 'no result file' }
    Add-Step 'Client A: roster answered' ($null -ne $ra -and [bool]$ra.rosterOk -and ([string]$ra.rosterAnswer).Contains('roster-ok')) $rosterDetail
    Add-Step 'Client A: connected with a NetworkId' ($connA -and $ra.networkId -gt 0) ("networkId=" + $(if ($null -ne $ra) { $ra.networkId } else { 'n/a' }))
    if (-not $connA) { throw 'client A did not connect' }

    $playersA = Wait-Until { @(Get-Players $pageKey).Count -ge 1 } 15 'the page listing client A'
    $listA = @(Get-Players $pageKey)
    $keyA = if ($listA.Count -ge 1) { [string]$listA[0].key } else { '' }
    Add-Step 'Client A has a PlayerKey' ($playersA -and $keyA -match '^[0-9]+$' -and $keyA -ne '0') "key=$keyA"

    # ---- 4. client B: joins, waits to be kicked
    $b = Start-Probe $ClientExe ("$common -knname B -knaction idle -kntimeout 40 -logFile " + (Quote (Join-Path $RunDir 'client-B.log'))) 'client B (connect, wait for the kick)'
    $okB = Wait-Until { $r = Read-Result 'kn-probe-client-B.json'; ($null -ne $r -and $r.connected) -or $b.HasExited } 60 'client B connected'
    $rb = Read-Result 'kn-probe-client-B.json'
    $connB = ($okB -and $null -ne $rb -and $rb.connected -and -not $b.HasExited)
    Add-Step 'Client B: connected with a NetworkId' ($connB -and $rb.networkId -gt 0) ("networkId=" + $(if ($null -ne $rb) { $rb.networkId } else { 'n/a' }))
    if (-not $connB) { throw 'client B did not connect' }
    $twoPlayers = Wait-Until { @(Get-Players $pageKey).Count -ge 2 } 15 'the page listing two players'
    $list = @(Get-Players $pageKey)
    $keyB = ''
    foreach ($row in $list) { if ([string]$row.key -ne $keyA) { $keyB = [string]$row.key } }
    Add-Step 'Two players, two PlayerKeys' ($twoPlayers -and $keyB -match '^[0-9]+$' -and $keyB -ne $keyA) "A=$keyA B=$keyB"
    if (-not ($keyB -match '^[0-9]+$')) { throw 'no key for client B' }

    # ---- 5. kick B through the page
    $kickAt = $script:Clock.Elapsed.TotalSeconds
    $kick = Invoke-Page 'POST' "/kick?player=$keyB" $pageKey
    Add-Step 'Kick accepted by the page' ($kick.Status -eq 202) "POST /kick -> $($kick.Status) $($kick.Body)"
    $bGone = Wait-Until { $b.HasExited } 30 'client B to exit after the kick'
    $kickSeconds = [math]::Round($script:Clock.Elapsed.TotalSeconds - $kickAt, 1)
    $rb = Read-Result 'kn-probe-client-B.json'
    Add-Step 'Client B exits 4 (kicked)' ($bGone -and $b.ExitCode -eq 4 -and $null -ne $rb -and [bool]$rb.kicked) ("exit=" + $(if ($bGone) { $b.ExitCode } else { 'still running' }) + ", $kickSeconds s after the kick, reason=" + $(if ($null -ne $rb) { $rb.exitReason } else { 'n/a' }))
    Add-Step 'Client A still connected after the kick' (-not $a.HasExited -and @(Get-Players $pageKey).Count -eq 1) "players now $(@(Get-Players $pageKey).Count)"

    # ---- 6. stop the server through the page
    $stopAt = $script:Clock.Elapsed.TotalSeconds
    $stop = Invoke-Page 'POST' '/stop' $pageKey
    Add-Step 'POST /stop accepted' ($stop.Status -eq 200) "-> $($stop.Status) $($stop.Body)"
    $serverGone = Wait-Until { $server.HasExited } 60 'the server to exit after /stop'
    $stopSeconds = [math]::Round($script:Clock.Elapsed.TotalSeconds - $stopAt, 1)
    Add-Step 'Server exits 0 after /stop' ($serverGone -and $server.ExitCode -eq 0) ("exit=" + $(if ($serverGone) { $server.ExitCode } else { 'still running' }) + ", $stopSeconds s after the request")
    $aGone = Wait-Until { $a.HasExited } 20 'client A to notice the server closing'
    $ra = Read-Result 'kn-probe-client-A.json'
    Add-Step 'Client A ends cleanly (server closed, not kicked)' ($aGone -and $a.ExitCode -eq 0 -and $null -ne $ra -and -not [bool]$ra.kicked) ("exit=" + $(if ($aGone) { $a.ExitCode } else { 'still running' }) + ", reason=" + $(if ($null -ne $ra) { $ra.exitReason } else { 'n/a' }))

    # ---- 7. the evidence: log lines and the order
    $lines = if (Test-Path $serverLog) { @(Get-Content $serverLog) } else { @() }
    $saveEnd = -1; $savedLine = -1; $stoppingLine = -1; $saveCount = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'SAVE ALL \(probe\) end') { $saveCount++; if ($saveEnd -lt 0) { $saveEnd = $i } }
        if ($lines[$i] -match 'Stopping: ' -and $stoppingLine -lt 0) { $stoppingLine = $i }
        if ($lines[$i] -match 'Saved\. Waiting for the players' -and $savedLine -lt 0) { $savedLine = $i }
    }
    Add-Step "Log has 'SAVE ALL (probe)'" ($saveCount -ge 1) "end lines: $saveCount, first at log line $saveEnd"
    Add-Step 'Order in the log: stopping, then save, then disconnects sent' ($stoppingLine -ge 0 -and $saveEnd -gt $stoppingLine -and $savedLine -gt $saveEnd) "stopping=$stoppingLine saveEnd=$saveEnd disconnectsSent=$savedLine"

    $rs = Read-Result 'kn-probe-server.json'
    if ($null -ne $rs -and $null -ne $ra) {
        $orderMs = ($ra.droppedUtcMs -ge $rs.saveEndUtcMs -and $rs.saveEndUtcMs -gt 0)
        Add-Step 'Order by clock: client A dropped after the save ended' $orderMs "saveEnd=$($rs.saveEndUtcMs) A dropped=$($ra.droppedUtcMs) (delta $($ra.droppedUtcMs - $rs.saveEndUtcMs) ms)"
        $keys = @($rs.playerKeys)
        Add-Step 'Server result file' (($rs.joins -eq 2) -and ($keys.Count -eq 2) -and ($keys[0] -ne $keys[1]) -and ($rs.rosterAnswers -eq 1) -and ($rs.saveCalls -ge 1) -and ($rs.exitReason -eq 'stopped') -and ($rs.exitCode -eq 0)) "joins=$($rs.joins) keys=$($keys -join ',') rosterAnswers=$($rs.rosterAnswers) saves=$($rs.saveCalls) exit=$($rs.exitReason)/$($rs.exitCode)"
    } else {
        Add-Step 'Server result file' $false 'kn-probe-server.json or the client A file is missing'
    }
}
catch {
    Write-Log "ABORT: $($_.Exception.Message)"
    Add-Step 'Run aborted' $false $_.Exception.Message
}
finally {
    $leftover = @($script:Started | Where-Object { -not $_.HasExited })
    if ($leftover.Count -gt 0) { Stop-All 'clean-up'; Start-Sleep -Milliseconds 500 }
    $portsFreeAfter = ((Test-PortFree $GamePort) -and (Test-PortFree $PagePort))
    # Nothing of ours may be left: processes and ports.
    $stillThere = @($script:Started | Where-Object { -not $_.HasExited }).Count
    $total = [math]::Round($script:Clock.Elapsed.TotalSeconds, 1)
    $lines = @()
    $verdict = if (-not $script:Failed) { 'PASS' } else { 'FAIL' }
    $lines += "# Probe report: M2c two-client test ($verdict)"
    $lines += ''
    $lines += "Run folder: $RunDir  |  total $total s  |  lowest free RAM seen $(if ($script:MinFreeSeen -gt 1e6) { "n/a" } else { $script:MinFreeSeen }) GB  |  leftover processes: $stillThere  |  ports 7797/7798 free afterwards: $portsFreeAfter"
    $lines += ''
    $lines += '| Step | Result | Detail | At (s) |'
    $lines += '|---|---|---|---|'
    foreach ($s in $script:Steps) { $lines += ('| {0} | {1} | {2} | {3} |' -f $s.name, $(if ($s.ok) { 'PASS' } else { 'FAIL' }), ($s.detail -replace '\|', '/'), $s.seconds) }
    $lines += ''
    $lines += 'Files in the run folder: server.log, client-A.log, client-B.log, kn-probe-server.json, kn-probe-client-A.json, kn-probe-client-B.json.'
    try {
        New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
        Set-Content -Path (Join-Path $RunDir 'probe-report.md') -Value $lines -Encoding UTF8
    } catch { }
    $lines | ForEach-Object { Write-Host $_ }
    # The made-up key file is not a secret, but it has no use after the run.
    if (Test-Path (Join-Path $RunDir 'probe-key.txt')) { Remove-Item (Join-Path $RunDir 'probe-key.txt') -Force -ErrorAction SilentlyContinue }
}
if ($script:Failed) { exit 1 } else { exit 0 }
