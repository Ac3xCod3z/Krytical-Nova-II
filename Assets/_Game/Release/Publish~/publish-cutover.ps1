# CUTOVER publish (12 Nov 2026): the DOTS game and the RELEASE entity server, one build number (the real store's last + 1), uploaded to the
# owner's TEST channel only. Three fresh batch Unity processes (client > server > upload), each only if the one before passed.
# Start through explorer.exe (via a .bat) or a normal PowerShell of the owner's profile (needs the signing key and the UGS login).
# Run only on cutover-lead's "build go": it builds for about 45-60 minutes and uploads. Never touches the NGO project's Builds.
param()
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
if (-not (Test-Path $cleanMark) -or (Get-Content $cleanMark -Raw).Trim() -ne 'clean') {
  foreach ($stale in 'Library\Bee\artifacts\WinPlayerBuildProgram','Library\Bee\artifacts\WinServerBuildProgram') {
    $p = Join-Path $proj $stale
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; S "cleared $stale (the last step did not end cleanly)" }
  }
}

$steps = 'Client','Server','Upload'
foreach ($step in $steps) {
  $log = "$proj\Builds\cutover-$($step.ToLower()).log"
  $a = "-batchmode -projectPath `"$proj`" -buildTarget Win64 -job-worker-count 3 -executeMethod KryticalNova.EditorTools.CutoverPublisher.$($step)Batch -logFile `"$log`" -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForConnection 10000 -cacheServerEnableImportResultCaching"
  $t = Get-Date; S "$step : started"
  Set-Content -Path $cleanMark -Value 'running'
  # Wait for Unity itself only: Start-Process -Wait also waits for every descendant, and an orphaned compiler server
  # (VBCSCompiler) held the runner after the server step had ended (build 3, 8 Nov 2026).
  $p = Start-Process -FilePath $unity -ArgumentList $a -WorkingDirectory $proj -NoNewWindow -PassThru
  $p.WaitForExit()
  S ("$step : exit {0} after {1:N1} min" -f $p.ExitCode, ((Get-Date)-$t).TotalMinutes)
  if ($p.ExitCode -ne 0) { S "STOPPED at $step"; exit 1 }
  Set-Content -Path $cleanMark -Value 'clean'
}
S "DONE"
