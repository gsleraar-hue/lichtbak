@echo off
rem Runs outside the Claude sandbox via a scheduled task: installs and dumps what landed in the registry.
set OUT=%~dp0probe.txt
"%~dp0..\bin\Lichtbak.exe" --install > "%OUT%" 2>&1
echo exit=%errorlevel% >> "%OUT%"
reg query "HKCU\Software\Classes\SystemFileAssociations\image\shell\Lichtbak" /s >> "%OUT%" 2>&1
reg query "HKCU\Software\Classes\.jpg\OpenWithProgids" >> "%OUT%" 2>&1
reg query "HKCU\Software\RegisteredApplications" /v Lichtbak >> "%OUT%" 2>&1
dir "%LOCALAPPDATA%\Programs\Lichtbak" >> "%OUT%" 2>&1
dir "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Lichtbak.lnk" >> "%OUT%" 2>&1
