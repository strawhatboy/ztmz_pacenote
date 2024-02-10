@echo off
cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Debug
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Debug
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet build -c Debug
cd ..

rem copy built games to target folder

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Debug\net8.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Debug\net8.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Debug\net8.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Debug\net8.0-windows\games\ /Y /V /S

echo copying i18n files to target folder
xcopy ZTMZ.PacenoteTool.I18N\lang\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\lang\" /Y /V /S

echo copying dashboards files to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\dashboards\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\dashboards\" /Y /S

echo copying custom fonts to target folder
xcopy ZTMZ.PacenoteTool.Base.UI\fonts\ "%userprofile%\Documents\My Games\ZTMZClub_nextgen\fonts\" /Y /V /S

echo copying pacenote definitions to target folder
xcopy ZTMZ.PacenoteTool.ScriptEditor\*.csv "%userprofile%\Documents\My Games\ZTMZClub_nextgen\" /Y /V 
xcopy ZTMZ.PacenoteTool.ScriptEditor\*.csv ZTMZ.PacenoteTool.WpfGUI\bin\Release\net8.0-windows\ /Y /V 
echo done.
