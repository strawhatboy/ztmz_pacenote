@echo off

set currentDir=%cd%

cd /D "%~dp0"

call .\buildWithoutLaunch.bat
if errorlevel 1 (
    cd /D "%currentDir%"
    exit /b 1
)

cd /D "%~dp0"
call .\launchWpfUIRelease.bat

cd /D "%currentDir%"

echo done.
