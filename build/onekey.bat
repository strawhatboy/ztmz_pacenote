@echo off
setlocal

set "currentDir=%cd%"

cd /D "%~dp0" || goto :failed

call .\buildWithoutLaunch.bat
if errorlevel 1 goto :failed

call .\publish.bat
if errorlevel 1 goto :failed

call .\package_signed.bat
if errorlevel 1 goto :failed

cd /D "%currentDir%"

echo done.
exit /b 0

:failed
set "exitCode=%ERRORLEVEL%"
if "%exitCode%"=="0" set "exitCode=1"
cd /D "%currentDir%"
echo Release build failed with exit code %exitCode%.
exit /b %exitCode%
