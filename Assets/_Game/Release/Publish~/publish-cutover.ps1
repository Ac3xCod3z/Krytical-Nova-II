# CUTOVER publish (12 Nov 2026): the DOTS game and the RELEASE entity server, one build number (the real store's last + 1), uploaded to the
# owner's TEST channel only. Three fresh batch Unity processes (client > server > upload), each only if the one before passed.
# Start through explorer.exe (via a .bat) or a normal PowerShell of the owner's profile (needs the signing key and the UGS login).
# Run only on cutover-lead's "build go": it builds for about 45-60 minutes and uploads. Never touches the NGO project's Builds.
# -From Server|Upload resumes after a step that PASSED but was read as failed (12 Nov: Start-Process -PassThru gave an empty
# ExitCode because the process handle was never taken; the client had built and its audit had passed).
param([ValidateSet('Client','Server','Upload')][string]$From = 'Client')
$ErrorActionPreference = 'Continue'
$proj = 'C:\Users\dlaco\Krytical Nova II DOTS'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$status = "$proj\Builds\Release-Cutover\publish-status.txt"
$cleanMark = "$proj\Builds\Release-Cutover\last-step-clean.txt"
New-Item -ItemType Directory -Force "$proj\Builds\Release-Cutover" | Out-Null
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
if ($From -eq 'Client' -and (-not (Test-Path $cleanMark) -or (Get-Content $cleanMark -Raw).Trim() -ne 'clean')) {
  foreach ($stale in 'Library\Bee\artifacts\WinPlayerBuildProgram','Library\Bee\artifacts\WinServerBuildProgram') {
    $p = Join-Path $proj $stale
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; S "cleared $stale (the last step did not end cleanly)" }
  }
}

$steps = 'Client','Server','Upload'
$steps = $steps[[array]::IndexOf($steps, $From)..2]
if ($From -ne 'Client') { S "resumed from $From" }
foreach ($step in $steps) {
  $log = "$proj\Builds\cutover-$($step.ToLower()).log"
  $a = "-batchmode -projectPath `"$proj`" -buildTarget Win64 -job-worker-count 3 -executeMethod KryticalNova.EditorTools.CutoverPublisher.$($step)Batch -logFile `"$log`" -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForConnection 10000 -cacheServerEnableImportResultCaching"
  $t = Get-Date; S "$step : started"
  Set-Content -Path $cleanMark -Value 'running'
  # Wait for Unity itself only: Start-Process -Wait also waits for every descendant, and an orphaned compiler server
  # (VBCSCompiler) held the runner after the server step had ended (build 3, 8 Nov 2026).
  $p = Start-Process -FilePath $unity -ArgumentList $a -WorkingDirectory $proj -NoNewWindow -PassThru
  $null = $p.Handle   # without the handle taken now, ExitCode reads empty after the process ends (Windows PowerShell 5.1)
  $p.WaitForExit()
  S ("$step : exit {0} after {1:N1} min" -f $p.ExitCode, ((Get-Date)-$t).TotalMinutes)
  if ($null -eq $p.ExitCode) { S "STOPPED at $step : no exit code could be read (check the step's log before resuming with -From)"; exit 1 }
  if ($p.ExitCode -ne 0) { S "STOPPED at $step"; exit 1 }
  Set-Content -Path $cleanMark -Value 'clean'
}
S "DONE"
