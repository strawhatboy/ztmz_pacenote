@echo off

set currentDir=%cd%
set "ZTMZ_RELEASE_SCRIPT_DIR=%~dp0"

cd /D "%~dp0"
rem clean publish folder
rmdir /S /Q target
cd ../src

cd ZTMZ.PacenoteTool.Codemasters
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..

cd ZTMZ.PacenoteTool.BeamNG
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..

cd ZTMZ.PacenoteTool.AC
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..

cd ZTMZ.PacenoteTool.KT
dotnet publish -c Release -f net10.0 -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path ../../build/target --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
if errorlevel 1 goto :publish_failed
cd ..
cd /D "%~dp0"  
rem we're in build folder now

rem copy built games to target folder

echo copying built games to target folder
rem all the dlls
if exist "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games" rmdir /S /Q "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games"
mkdir target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games
xcopy target\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ /Y /V /Q  
xcopy target\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ /Y /V /Q
copy /Y "target\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\ZTMZ.PacenoteTool.BeamNG.dll" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\" >nul
xcopy "target\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\BeamNGMod\*" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\BeamNGMod\" /E /I /Y /V /Q
xcopy "target\bin\ZTMZ.PacenoteTool.AC\release_win-x64\" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\" /Y /V /S /Q
copy /Y "target\bin\ZTMZ.PacenoteTool.KT\release_win-x64\ZTMZ.PacenoteTool.KT.dll" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\" >nul
if errorlevel 1 goto :publish_failed
xcopy "target\bin\ZTMZ.PacenoteTool.KT\release_win-x64\WRCGBridge\*" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\WRCGBridge\" /E /I /Y /Q
if errorlevel 1 goto :publish_failed
xcopy "target\bin\ZTMZ.PacenoteTool.KT\release_win-x64\WRC7Bridge\*" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\WRC7Bridge\" /E /I /Y /Q
if errorlevel 1 goto :publish_failed
xcopy "target\bin\ZTMZ.PacenoteTool.KT\release_win-x64\WRC9Bridge\*" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\WRC9Bridge\" /E /I /Y /Q
if errorlevel 1 goto :publish_failed
xcopy "..\src\ZTMZ.PacenoteTool.KT\lang\*.json" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\" /I /Y /Q
if errorlevel 1 goto :publish_failed

rem the i18n
if not exist "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang" mkdir "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang"
xcopy target\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.AC\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q


rem Stage only pinned online release resources; never read personal application data.
set "RELEASE_DATA_ROOT=target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ZTMZ_RELEASE_SCRIPT_DIR%stage_release_resources.ps1" -DestinationRoot "%RELEASE_DATA_ROOT%"
if errorlevel 1 goto :publish_failed
powershell -NoProfile -ExecutionPolicy Bypass -File "%ZTMZ_RELEASE_SCRIPT_DIR%verify_release_payload.ps1" -DestinationRoot "%RELEASE_DATA_ROOT%"
if errorlevel 1 goto :publish_failed

cd /D "%currentDir%"

echo done.
exit /b 0

:publish_failed
echo Release publish failed. No installer should be created from this output.
cd /D "%currentDir%"
exit /b 1
