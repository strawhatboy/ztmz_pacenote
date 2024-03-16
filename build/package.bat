@echo off

cd /D "%~dp0"
SET PATH=%PATH%;%programfiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64;

echo %PATH%

ISCC /V:1 /O..\Output setup_normal.iss
