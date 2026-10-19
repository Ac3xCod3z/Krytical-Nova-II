# One-time setup of the save database (online phase 4, milestone 1), run by the owner on the PC that holds Postgres.
#
# - Reads the Postgres admin password from Documents\KryticalNova-Secrets\postgres-admin.txt (written by the owner when
#   installing Postgres). It is used only here and never printed.
# - Makes the login the save service uses (krytical_save) with a new random password, and the databases it owns:
#   krytical_nova (the real saves) and krytical_nova_test (tests only). If the login is there already, its password is
#   renewed: restart a running save service afterwards.
# - Writes the service's connection details to Documents\KryticalNova-Secrets\save-service.json, readable only by this
#   Windows user. The password never appears on screen.
# - Has Postgres listen on this PC only (listen_addresses = localhost); takes effect when Postgres restarts.
#
# Run it with "Setup Database.bat" beside it, or: powershell -ExecutionPolicy Bypass -File setup-database.ps1

$ErrorActionPreference = 'Stop'
$secrets = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'KryticalNova-Secrets'
$adminFile = Join-Path $secrets 'postgres-admin.txt'
$outFile = Join-Path $secrets 'save-service.json'
$psql = Join-Path $env:ProgramFiles 'PostgreSQL\18\bin\psql.exe'

function Fail($text) { Write-Host ""; Write-Host "SETUP STOPPED: $text" -ForegroundColor Red; Write-Host ""; Read-Host "Press Enter to close"; exit 1 }

if (-not (Test-Path $psql)) { Fail "Postgres 18 is not installed where expected ($psql)." }
if (-not (Test-Path $adminFile)) { Fail "There is no $adminFile. Write the Postgres admin password (the one typed in the installer) into it, alone on the first line." }

$admin = (Get-Content -LiteralPath $adminFile -TotalCount 1).Trim()
if ([string]::IsNullOrEmpty($admin)) { Fail "$adminFile is empty." }

# A new random password for the service: 32 letters and digits, never shown.
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$servicePassword = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })

$env:PGPASSWORD = $admin
try {
    $sql = @"
DO `$`$
BEGIN
  IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'krytical_save') THEN
    ALTER ROLE krytical_save WITH LOGIN PASSWORD '$servicePassword';
  ELSE
    CREATE ROLE krytical_save WITH LOGIN PASSWORD '$servicePassword';
  END IF;
END
`$`$;
ALTER SYSTEM SET listen_addresses = 'localhost';
"@
    $sql | & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -q 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Postgres refused the admin login or the setup. Check the password in $adminFile." }

    # The real saves, and a database of their own for tests (the Editor's tests and the measure server), never mixed.
    foreach ($name in 'krytical_nova', 'krytical_nova_test') {
        $exists = & $psql -h 127.0.0.1 -U postgres -d postgres -tA -c "SELECT 1 FROM pg_database WHERE datname = '$name'"
        if ($exists -ne '1') {
            & $psql -h 127.0.0.1 -U postgres -d postgres -q -c "CREATE DATABASE $name OWNER krytical_save ENCODING 'UTF8'" | Out-Null
            if ($LASTEXITCODE -ne 0) { Fail "The database $name could not be made." }
        }
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

$settings = [ordered]@{
    host = '127.0.0.1'
    port = 5432
    database = 'krytical_nova'
    user = 'krytical_save'
    password = $servicePassword
    made = (Get-Date).ToString('yyyy-MM-dd HH:mm')
}
$settings | ConvertTo-Json | Set-Content -LiteralPath $outFile -Encoding UTF8

# Only this Windows user may read it.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
$me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($me, 'FullControl', 'Allow')))
Set-Acl -LiteralPath $outFile -AclObject $acl

Write-Host ""
Write-Host "DONE." -ForegroundColor Green
Write-Host "  The save databases krytical_nova (real) and krytical_nova_test (tests) and their login krytical_save are ready."
Write-Host "  If a save service window is open, close it; Start Server.bat starts it again with the new login."
Write-Host "  The service's details are in $outFile (only you can read it)."
Write-Host "  Postgres will listen on this PC only after it restarts (or the PC does)."
Write-Host ""
Read-Host "Press Enter to close"
