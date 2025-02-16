@echo off

set currentDir=%cd%

cd /D "%~dp0"

call .\buildWithoutLaunch.bat

call .\publish.bat

call .\package_signed.bat

cd /D "%currentDir%"

echo done.
