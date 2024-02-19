@echo off
set currentDir=%cd%

cd /D "%~dp0"
cd ../src

cd ZTMZ.PacenoteTool.WpfGUI/bin/Release/net8.0-windows
start ZTMZ.PacenoteTool.WpfGUI.exe

cd /D "%currentDir%"

