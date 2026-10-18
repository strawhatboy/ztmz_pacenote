@echo off
setlocal

if "%~1"=="" (
    echo Signing target was not provided.
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release_signing.ps1" -Action Sign -Path "%~1"
exit /b %ERRORLEVEL%
