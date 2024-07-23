@echo off

set currentDir=%cd%

cd /D "%~dp0"
rem clean publish folder
rmdir /S /Q target
cd ../src

cd ZTMZ.PacenoteTool.Codemasters
dotnet publish -c Release -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet publish -c Release -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet publish -c Release -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0
cd ..
cd /D "%~dp0"  
rem we're in build folder now

rem copy built games to target folder

echo copying built games to target folder
rem all the dlls
mkdir target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games
xcopy target\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ /Y /V /Q  
xcopy target\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ /Y /V /Q

rem the i18n
mkdir target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang
xcopy target\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q

rem put default codriver package in place
xcopy ..\src\ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\codrivers\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\codrivers\ /Y /S /V /Q
rem put profiles
xcopy ..\src\ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\profiles\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\profiles\ /Y /S /V /Q

cd /D "%currentDir%"

echo done.
