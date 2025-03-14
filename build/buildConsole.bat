@echo off

set currentDir=%cd%

cd /D "%~dp0"
cd ../src

cd ZTMZ.PacenoteTool.Base
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.Console
dotnet build -c Release
cd ..

echo copying built games to target folder

xcopy ZTMZ.PacenoteTool.Codemasters\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.Console\bin\Release\net8.0-windows\games\ /Y /V /S /Q
xcopy ZTMZ.PacenoteTool.RBR\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.Console\bin\Release\net8.0-windows\games\ /Y /V /S /Q

echo copying i18n files to target folder
xcopy ZTMZ.PacenoteTool.I18N\lang\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\lang\" /Y /V /S /Q

echo copying dashboards files to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\dashboards\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\dashboards\" /Y /S /V /Q

echo copying custom fonts to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\fonts\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\fonts\" /Y /V /S /Q

echo copying pacenote definitions to target folder
xcopy ZTMZ.PacenoteTool.ScriptEditor\*.csv "%userprofile%\Documents\My Games\ZTMZClub_nextgen\" /Y /V /Q
xcopy ZTMZ.PacenoteTool.ScriptEditor\*.csv ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\ /Y /V /Q

cd /D "%currentDir%"

echo done.
