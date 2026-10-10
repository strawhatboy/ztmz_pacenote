@echo off
setlocal

cd /D "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify_release_payload.ps1" -DestinationRoot "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stage_release_resources.ps1" -VerifyOnly -DestinationRoot "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
if errorlevel 1 exit /b 1
set "PATH=%PATH%;%programfiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64"

if not defined ZTMZ_SIGNING_PFX_PASSWORD (
    for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "[Environment]::GetEnvironmentVariable('ZTMZ_SIGNING_PFX_PASSWORD', 'User')"`) do set "ZTMZ_SIGNING_PFX_PASSWORD=%%P"
)

if not defined ZTMZ_SIGNING_PFX_PASSWORD (
    echo ZTMZ_SIGNING_PFX_PASSWORD is not configured for the current user.
    exit /b 1
)

if not defined ZTMZ_SIGNING_PFX_PATH set "ZTMZ_SIGNING_PFX_PATH=%USERPROFILE%\ztmz_selfsigncert.pfx"

if not exist "%ZTMZ_SIGNING_PFX_PATH%" (
    echo Signing certificate was not found: "%ZTMZ_SIGNING_PFX_PATH%"
    exit /b 1
)

ISCC /V:1 /O..\Output "/Smssign=$q%~dp0sign_local.bat$q $f" setup_normal_signed.iss
