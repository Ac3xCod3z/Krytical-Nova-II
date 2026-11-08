@echo off
rem Krytical Nova Publisher: builds the app into <project>\Builds\Publisher and puts a shortcut on the desktop.
rem   Build Publisher.bat              build + desktop shortcut
rem   Build Publisher.bat noshortcut   build only
setlocal
set HERE=%~dp0
for %%I in ("%HERE%..\..\..\..\..") do set PROJECT=%%~fI
set OUT=%PROJECT%\Builds\Publisher
dotnet build "%HERE%Publisher.csproj" -c Release -o "%OUT%" -nologo -v q
if errorlevel 1 (echo The Publisher did not build. & exit /b 1)
echo Built: %OUT%\Krytical Nova Publisher.exe
if /i "%~1"=="noshortcut" exit /b 0
powershell -NoProfile -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $d 'Krytical Nova Publisher.lnk')); $s.TargetPath='%OUT%\Krytical Nova Publisher.exe'; $s.WorkingDirectory='%OUT%'; $s.IconLocation='%OUT%\Krytical Nova Publisher.exe,0'; $s.Description='Push Krytical Nova updates: upload, promote, roll back'; $s.Save(); Write-Host ('Shortcut: ' + $s.FullName)"
