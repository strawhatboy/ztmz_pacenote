@echo off

set currentDir=%cd%

cd /D "%~dp0"
cd ../src

cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Release --property WarningLevel=0
if errorlevel 1 goto :build_failed
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Release --property WarningLevel=0
if errorlevel 1 goto :build_failed
cd ..

cd ZTMZ.PacenoteTool.AC
dotnet build -c Release --property WarningLevel=0
if errorlevel 1 goto :build_failed
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet build -c Release --property WarningLevel=0
if errorlevel 1 goto :build_failed
cd ..

rem copy built games to target folder

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Release\net10.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Release\net10.0-windows\games\ /Y /V /S /Q
xcopy ZTMZ.PacenoteTool.RBR\bin\Release\net10.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Release\net10.0-windows\games\ /Y /V /S /Q
xcopy ZTMZ.PacenoteTool.AC\bin\Release\net10.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Release\net10.0-windows\games\ /Y /V /S /Q

echo copying i18n files to target folder
xcopy ZTMZ.PacenoteTool.I18N\lang\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\lang\" /Y /V /S /Q

echo copying dashboards files to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\dashboards\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\dashboards\" /Y /S /V /Q

echo copying custom fonts to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\fonts\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\fonts\" /Y /V /S /Q

echo copying pacenote definitions database to target folder
xcopy ZTMZ.PacenoteTool.Base\*.zdb "%userprofile%\Documents\My Games\ZTMZClub_nextgen\" /Y /V /Q
xcopy ZTMZ.PacenoteTool.RBR\*.zdb "%userprofile%\Documents\My Games\ZTMZClub_nextgen\games\" /Y /V /Q

cd /D "%currentDir%"

echo done.
exit /b 0

:build_failed
echo Build failed. No files were copied.
cd /D "%currentDir%"
exit /b 1
