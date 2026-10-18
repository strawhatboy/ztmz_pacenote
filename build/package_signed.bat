@echo off
setlocal

cd /D "%~dp0" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release_signing.ps1" -Action Package
exit /b %ERRORLEVEL%
