param([switch]$NoOpen)
$ErrorActionPreference='Stop'
$previewRoot=$PSScriptRoot
$python='C:\Users\dlaco\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if(-not (Get-NetTCPConnection -LocalPort 8769 -State Listen -ErrorAction SilentlyContinue)) {
 Start-Process -FilePath $python -ArgumentList ('"'+(Join-Path $previewRoot 'private_preview_server.py')+'"') -WorkingDirectory $previewRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $previewRoot 'preview-server.log') -RedirectStandardError (Join-Path $previewRoot 'preview-server-error.log')
 for($i=0;$i -lt 30;$i++){Start-Sleep -Milliseconds 250;if(Get-NetTCPConnection -LocalPort 8769 -State Listen -ErrorAction SilentlyContinue){break}}
}
& (Join-Path (Split-Path $previewRoot -Parent) 'Concord\Open-Concord-Preview.ps1') -NoOpen
if(-not $NoOpen){Start-Process 'http://127.0.0.1:8769/crucible/review-3d.html'}

