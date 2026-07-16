@echo off
setlocal

if "%~1"=="" (
    echo Signing target was not provided.
    exit /b 1
)

if not defined ZTMZ_SIGNING_PFX_PATH set "ZTMZ_SIGNING_PFX_PATH=%USERPROFILE%\ztmz_selfsigncert.pfx"

if not defined ZTMZ_SIGNING_PFX_PASSWORD (
    echo ZTMZ_SIGNING_PFX_PASSWORD is not configured for the current user.
    exit /b 1
)

for /f "usebackq delims=" %%S in (`powershell -NoProfile -Command "$root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'; Get-ChildItem -Path $root -Filter signtool.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName"`) do set "SIGNTOOL=%%S"

if not defined SIGNTOOL (
    echo Windows SDK signtool.exe was not found.
    exit /b 1
)

"%SIGNTOOL%" sign /f "%ZTMZ_SIGNING_PFX_PATH%" /p "%ZTMZ_SIGNING_PFX_PASSWORD%" /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a "%~1"
