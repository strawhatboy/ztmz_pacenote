@echo off

cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet build -c Release
cd ..

rem copy built games to target folder

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\games\ /Y /V /S

echo copying i18n files to target folder
xcopy ZTMZ.PacenoteTool.I18N\lang\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\lang\" /Y /V /S

.\launchWpfUIRelease.bat

echo done.
