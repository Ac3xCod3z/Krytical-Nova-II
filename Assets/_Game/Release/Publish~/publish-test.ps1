# Publish to Test from a script (Release Guide, section 1a-4): the game in the build checkout, then the matching server in
# the server checkout, each in a fresh batch Unity inside a job object this script owns. Never promotes.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File publish-test.ps1                 origin/main, game + server
#   ... -File publish-test.ps1 -Commit <sha>                                             another commit
#   ... -File publish-test.ps1 -ServerOnly                                               the server of the last release only
#
# Start it through explorer.exe or a normal PowerShell window (it needs the owner's real profile for the signing key and the
# UGS login). One heavy job at a time: it refuses to start while another Unity runs. Kill rule (own tree only),
# the project's standing rule, under 2 GB available for 15 s. The status file says each stage, its minutes and what to check.
param([string]$Commit = '', [switch]$ServerOnly, [int]$JobWorkers = 3)
$ErrorActionPreference = 'Continue'
$main = 'C:\Users\dlaco\Krytical Nova II'
$root = "$main\Builds\Release"
$co = "$env:USERPROFILE\KN-worktrees\build"
$sco = "$env:USERPROFILE\KN-worktrees\build-server"
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$status = "$root\publish-status.txt"
$relog = "$root\release-log.txt"
function S($m){ ("{0} {1}" -f (Get-Date -Format s), $m) | Tee-Object -FilePath $status -Append | Write-Host }
Remove-Item $status -ErrorAction SilentlyContinue

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KnPublishJob {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CreateJobObject(IntPtr a, string n);
 [DllImport("kernel32.dll")] public static extern bool SetInformationJobObject(IntPtr j, int c, IntPtr i, uint l);
 [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
 [DllImport("kernel32.dll")] public static extern bool TerminateJobObject(IntPtr j, uint e);
 [StructLayout(LayoutKind.Sequential)] public struct BASIC { public long a; public long b; public uint LimitFlags; public UIntPtr c; public UIntPtr d; public uint e; public long f; public uint g; public uint h; }
 [StructLayout(LayoutKind.Sequential)] public struct IO { public ulong a,b,c,d,e,f; }
 [StructLayout(LayoutKind.Sequential)] public struct EXT { public BASIC Basic; public IO Io; public UIntPtr p,q,r,s; }
 public static IntPtr Make(){ IntPtr j=CreateJobObject(IntPtr.Zero,null); EXT x=new EXT(); x.Basic.LimitFlags=0x2000;
  int n=Marshal.SizeOf(typeof(EXT)); IntPtr m=Marshal.AllocHGlobal(n); Marshal.StructureToPtr(x,m,false); SetInformationJobObject(j,9,m,(uint)n); return j; }
}
"@

$script:min = 999.0
function RunUnity($label, $folder, $extra, $log) {
  $a = '-batchmode -projectPath "{0}" -buildTarget Win64 -job-worker-count {1} {2} -logFile "{3}" -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForConnection 10000 -cacheServerEnableImportResultCaching' -f $folder, $JobWorkers, $extra, $log
  $job = [KnPublishJob]::Make()
  $start = Get-Date
  $p = Start-Process -FilePath $unity -ArgumentList $a -WorkingDirectory $folder -WindowStyle Hidden -PassThru
  [void][KnPublishJob]::AssignProcessToJobObject($job, $p.Handle)
  try { $p.PriorityClass = 'BelowNormal' } catch {}
  S "$label : Unity pid $($p.Id) in $folder"
  $lowSince = $null; $n = 0
  while (-not $p.HasExited) {
    Start-Sleep 5
    $avail = [math]::Round((Get-Counter '\Memory\Available MBytes' -SampleInterval 1 -MaxSamples 1).CounterSamples[0].CookedValue/1024,1)
    if ($avail -lt $script:min) { $script:min = $avail }
    $n++; if ($n % 12 -eq 0) { S "$label : available=${avail}GB lowest=$($script:min)GB, $([math]::Round(((Get-Date)-$start).TotalMinutes,1)) min" }
    $kill = $false
    if ($avail -lt 2.0) { if (-not $lowSince) { $lowSince = Get-Date } elseif (((Get-Date)-$lowSince).TotalSeconds -ge 15) { $kill = $true } } else { $lowSince = $null }
    if ($kill) { [void][KnPublishJob]::TerminateJobObject($job,1); S "$label : LOW RAM ($avail GB): stopped (own tree only)"; return 99 }
  }
  $p.WaitForExit()
  S ("$label : Unity exit {0} after {1:N1} min" -f $p.ExitCode, ((Get-Date)-$start).TotalMinutes)
  return $p.ExitCode
}

function Checkout($folder, $sha) {
  if (-not (Test-Path "$folder\Assets")) {
    S "making the checkout $folder (a git worktree; its first Unity imports the Library through the Accelerator)"
    git -C $main worktree add --detach $folder $sha 2>&1 | ForEach-Object { S "git: $_" }
  }
  git -C $folder checkout --force --detach $sha 2>&1 | ForEach-Object { S "git: $_" }
  if (-not (Test-Path "$folder\Builds")) { cmd /c mklink /J "$folder\Builds" "$main\Builds" | Out-Null }
  if ((git -C $folder rev-parse HEAD).Trim() -ne $sha) { throw "$folder is not on $sha" }
}

# What to read after a run: the lines that show whether the speed-ups worked.
function Report($log, $what) {
  if (-not (Test-Path $log)) { return }
  $c = @(Select-String -Path $log -SimpleMatch 'Requested script compilation because').Count
  $d = @(Select-String -Path $log -SimpleMatch 'Define symbols changed').Count
  S "$what : $c script compilation(s) requested, $d of them because the define symbols changed (should be 0)"
  Select-String -Path $log -Pattern '^\[Maps\]|^\[Build\] .*content cache|^\[Build\] Dedicated server|Scripting backend' | Select -First 6 | ForEach-Object { S "$what : $($_.Line)" }
}

try {
  $t0 = Get-Date
  S 'start'
  if ((Get-Service UnityAccelerator).Status -ne 'Running') { throw 'the Unity Accelerator service is not running' }
  if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'another Unity is running (one heavy job at a time)' }
  if ($Commit -eq '') {
    if ($ServerOnly) { $Commit = (git -C $co rev-parse HEAD).Trim() }
    else { git -C $main fetch origin main -q; $Commit = (git -C $main rev-parse origin/main).Trim() }
  }
  S "commit $Commit"

  if (-not $ServerOnly) {
    Checkout $co $Commit
    Remove-Item "$root\background-publish.result","$root\background-publish.lock","$root\background-publish.restarted" -ErrorAction SilentlyContinue
    $c = RunUnity 'game' $co '-executeMethod KryticalNova.EditorTools.BackgroundPublisher.RunInBatch -knServerStepByScript' "$root\background-publish.log"
    $result = if (Test-Path "$root\background-publish.result") { (Get-Content "$root\background-publish.result" -Raw).Trim() } else { '' }
    if ($result.StartsWith('RESTART')) {
      S 'the guard corrected the subtarget: the game once more in a fresh Unity'
      Remove-Item "$root\background-publish.result" -ErrorAction SilentlyContinue
      $c = RunUnity 'game (again)' $co '-executeMethod KryticalNova.EditorTools.BackgroundPublisher.RunInBatch -knServerStepByScript' "$root\background-publish.log"
      $result = if (Test-Path "$root\background-publish.result") { (Get-Content "$root\background-publish.result" -Raw).Trim() } else { '' }
    }
    S "game result: '$result'"
    Report "$root\background-publish.log" 'game'
    Select-String -Path $relog -Pattern "^operations: " | Select -Last 1 | ForEach-Object { S "upload: $($_.Line)" }
    $audit = Get-ChildItem "$root\bundle-audit-build-*.json" -ErrorAction SilentlyContinue | Sort LastWriteTime -Desc | Select -First 1
    $auditOk = $audit -and ((Get-Content $audit.FullName -Raw) -match '"ok": true') -and $audit.LastWriteTime -gt $t0
    S ("bundle audit: " + $(if ($audit) { "$($audit.Name) passed=$auditOk" } else { 'none written' }))
    if ($c -ne 0 -or -not $result.StartsWith('OK') -or -not $auditOk) { throw 'the game, its gate or its upload did not pass: no server build' }
    if (-not $result.Contains('uploaded to TEST')) { S 'staged only (no CCD): no server build, as Publish to Test'; return }
  }

  # The server, in its own checkout on the Server subtarget: the game's Library is never switched (Release Guide 1a-4).
  Checkout $sco $Commit
  $c3 = RunUnity 'server' $sco '-standaloneBuildSubtarget Server -executeMethod KryticalNova.EditorTools.ServerBuildBatch.BuildWindows' "$root\server-only.log"
  $sr = if (Test-Path "$root\server-only.result") { (Get-Content "$root\server-only.result" -Raw).Trim() } else { '(no result file)' }
  S "server: exit $c3, $sr"
  Report "$root\server-only.log" 'server'
  S ("done in {0:N1} min; lowest available {1} GB" -f ((Get-Date)-$t0).TotalMinutes, $script:min)
} catch { S "STOPPED: $_" }
S 'script end'
