@echo off

set currentDir=%cd%

cd /D "%~dp0"

call .\buildWithoutLaunch.bat

cd /D "%~dp0"
call .\launchWpfUIRelease.bat

cd /D "%currentDir%"

echo done.
