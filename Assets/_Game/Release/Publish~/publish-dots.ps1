# DOTS channel publish: three fresh batch Unity processes (client > server > upload), each only if the one before passed.
# Start through explorer.exe or a normal PowerShell (needs the owner's profile for the signing key and UGS login).
#   -ClientOnly : client + upload only, as the last build number (refused by the release tool when that number is already staged).
param([switch]$ClientOnly)
$ErrorActionPreference = 'Continue'
$proj = 'C:\Users\dlaco\Krytical Nova II DOTS'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$status = "$proj\Builds\Release-DOTS\publish-status.txt"
$cleanMark = "$proj\Builds\Release-DOTS\last-step-clean.txt"
New-Item -ItemType Directory -Force "$proj\Builds\Release-DOTS" | Out-Null
function S($m){ ("{0} {1}" -f (Get-Date -Format s), $m) | Tee-Object -FilePath $status -Append | Write-Host }

# ---- guards (pipeline audit, 8 Nov 2026) ----
$unityOnClone = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match [regex]::Escape($proj) })
if ($unityOnClone.Count -gt 0) { S "REFUSED: a Unity already runs on the clone (pid $($unityOnClone[0].ProcessId))"; exit 2 }
Push-Location $proj
$merging = Test-Path (Join-Path (git rev-parse --git-dir) 'MERGE_HEAD')
$dirty = @(git status --porcelain -- ProjectSettings 'ProjectSettings/BurstAotSettings*' | Where-Object { $_ -notmatch '^\?\?' -or $_ -match 'BurstAotSettings' })
Pop-Location
if ($merging) { S "REFUSED: a git merge is in progress in the clone"; exit 2 }
if ($dirty.Count -gt 0) { S "REFUSED: ProjectSettings has changes a build would bake in: $($dirty -join '; ')"; exit 2 }
# A step that did not end with a clean exit 0 (killed, crashed) can leave the player build's linker/IL2CPP output half-written:
# the next incremental build trusts it (build 3 attempt, 8 Nov: il2cpp FileNotFoundException in ManagedStripped). Clear it then.
if (-not (Test-Path $cleanMark) -or (Get-Content $cleanMark -Raw).Trim() -ne 'clean') {
  foreach ($stale in 'Library\Bee\artifacts\WinPlayerBuildProgram','Library\Bee\artifacts\WinServerBuildProgram') {
    $p = Join-Path $proj $stale
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; S "cleared $stale (the last step did not end cleanly)" }
  }
}

$steps = if ($ClientOnly) { 'Client','Upload' } else { 'Client','Server','Upload' }
foreach ($step in $steps) {
  $log = "$proj\Builds\dots-$($step.ToLower()).log"
  $a = "-batchmode -projectPath `"$proj`" -buildTarget Win64 -job-worker-count 3 -executeMethod KryticalNova.EditorTools.DotsChannelPublisher.$($step)Batch $(if ($ClientOnly) { '-dotsreuse' }) -logFile `"$log`" -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForConnection 10000 -cacheServerEnableImportResultCaching"
  $t = Get-Date; S "$step : started"
  Set-Content -Path $cleanMark -Value 'running'
  $p = Start-Process -FilePath $unity -ArgumentList $a -WorkingDirectory $proj -NoNewWindow -PassThru -Wait
  S ("$step : exit {0} after {1:N1} min" -f $p.ExitCode, ((Get-Date)-$t).TotalMinutes)
  if ($p.ExitCode -ne 0) { S "STOPPED at $step"; exit 1 }
  Set-Content -Path $cleanMark -Value 'clean'
}
S "DONE"
