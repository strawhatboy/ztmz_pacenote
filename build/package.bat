@echo off

cd /D "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify_release_payload.ps1" -DestinationRoot "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stage_release_resources.ps1" -VerifyOnly -DestinationRoot "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
if errorlevel 1 exit /b 1
SET PATH=%PATH%;%programfiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64;

echo %PATH%

ISCC /V:1 /O..\Output setup_normal.iss
