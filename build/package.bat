@echo off

cd /D "%~dp0"

ISCC /V:1 /O..\Output setup_normal.iss
