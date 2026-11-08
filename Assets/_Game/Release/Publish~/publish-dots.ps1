# DOTS channel publish: three fresh batch Unity processes (client > server > upload), each only if the one before passed.
# Start through explorer.exe or a normal PowerShell (needs the owner's profile for the signing key and UGS login).
$ErrorActionPreference = 'Continue'
$proj = 'C:\Users\dlaco\Krytical Nova II DOTS'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$status = "$proj\Builds\Release-DOTS\publish-status.txt"
New-Item -ItemType Directory -Force "$proj\Builds\Release-DOTS" | Out-Null
function S($m){ ("{0} {1}" -f (Get-Date -Format s), $m) | Tee-Object -FilePath $status -Append | Write-Host }
foreach ($step in 'Client','Server','Upload') {
  $log = "$proj\Builds\dots-$($step.ToLower()).log"
  $a = "-batchmode -projectPath `"$proj`" -buildTarget Win64 -job-worker-count 3 -executeMethod KryticalNova.EditorTools.DotsChannelPublisher.$($step)Batch -logFile `"$log`" -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForConnection 10000 -cacheServerEnableImportResultCaching"
  $t = Get-Date; S "$step : started"
  $p = Start-Process -FilePath $unity -ArgumentList $a -WorkingDirectory $proj -NoNewWindow -PassThru -Wait
  S ("$step : exit {0} after {1:N1} min" -f $p.ExitCode, ((Get-Date)-$t).TotalMinutes)
  if ($p.ExitCode -ne 0) { S "STOPPED at $step"; exit 1 }
}
S "DONE"
