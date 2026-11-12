# Cutover step 9 only, on cutover-lead's word (the owner approves step 9 by name): the desktop and Start-menu shortcut
# "Krytical Nova Server" (the NGO server's app) is renamed "Krytical Nova Server (NGO, rollback)" and a new "Krytical Nova Server"
# points at the RELEASE server's app in the DOTS clone. Nothing else is touched: no app, folder, setting or database. Run again = no change.
# Undo: delete the new "Krytical Nova Server.lnk", rename the "(NGO, rollback)" one back.
$app = 'C:\Users\dlaco\Krytical Nova II DOTS\Builds\Server\Windows\Krytical Nova Server.exe'
if (-not (Test-Path $app)) { Write-Host "REFUSED: the release server's app is not built yet: $app"; exit 2 }
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs')) {
  $link = Join-Path $dir 'Krytical Nova Server.lnk'
  $kept = Join-Path $dir 'Krytical Nova Server (NGO, rollback).lnk'
  if ((Test-Path $link) -and -not (Test-Path $kept)) {
    $old = $shell.CreateShortcut($link).TargetPath
    if ($old -ieq $app) { Write-Host "$link already points at the release server"; continue }
    Move-Item $link $kept; Write-Host "renamed $link -> $kept (was $old)"
  }
  $s = $shell.CreateShortcut($link)
  $s.TargetPath = $app; $s.WorkingDirectory = Split-Path $app; $s.IconLocation = "$app,0"
  $s.Description = 'Runs the Krytical Nova server (DOTS) and its save service'; $s.Save()
  Write-Host "wrote $link -> $app"
}
