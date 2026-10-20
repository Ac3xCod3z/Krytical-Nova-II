# One-time: the practice databases of the restore drill (online phase 4, milestone 6), run by the owner on the PC that holds
# Postgres.
#
# - Makes krytical_nova_drill (the drill puts a copy of the real saves in it to prove the copy brings them back) and
#   krytical_nova_test_drill (the same for the tests' copies), both owned by the save service's login (krytical_save), so
#   the service can empty and fill them. Nothing else changes: no password is renewed, the saves are not touched.
# - Reads the Postgres admin password from Documents\KryticalNova-Secrets\postgres-admin.txt. It is used only here and
#   never printed.
#
# Run it with "Make Drill Database.bat" beside it. Safe to run again: a database that is there already is left as it is.

$ErrorActionPreference = 'Stop'
$secrets = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'KryticalNova-Secrets'
$adminFile = Join-Path $secrets 'postgres-admin.txt'
$psql = Join-Path $env:ProgramFiles 'PostgreSQL\18\bin\psql.exe'

function Fail($text) { Write-Host ""; Write-Host "STOPPED: $text" -ForegroundColor Red; Write-Host ""; Read-Host "Press Enter to close"; exit 1 }

if (-not (Test-Path $psql)) { Fail "Postgres 18 is not installed where expected ($psql)." }
if (-not (Test-Path $adminFile)) { Fail "There is no $adminFile (the Postgres admin password, written when Postgres was installed)." }
$admin = (Get-Content -LiteralPath $adminFile -TotalCount 1).Trim()
if ([string]::IsNullOrEmpty($admin)) { Fail "$adminFile is empty." }

$env:PGPASSWORD = $admin
try {
    $role = & $psql -h 127.0.0.1 -U postgres -d postgres -tA -c "SELECT 1 FROM pg_roles WHERE rolname = 'krytical_save'"
    if ($LASTEXITCODE -ne 0) { Fail "Postgres refused the admin login. Check the password in $adminFile." }
    if ($role -ne '1') { Fail "The save service's login is not there yet: run ""Setup Database.bat"" first." }

    foreach ($name in 'krytical_nova_drill', 'krytical_nova_test_drill') {
        $exists = & $psql -h 127.0.0.1 -U postgres -d postgres -tA -c "SELECT 1 FROM pg_database WHERE datname = '$name'"
        if ($exists -eq '1') { Write-Host "$name is there already." ; continue }
        & $psql -h 127.0.0.1 -U postgres -d postgres -q -c "CREATE DATABASE $name OWNER krytical_save ENCODING 'UTF8'" | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "The database $name could not be made." }
        Write-Host "Made $name."
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Done. The restore drill can run now: ""Restore Drill.bat"" beside the save service." -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to close"
