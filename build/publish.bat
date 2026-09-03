@echo off

set currentDir=%cd%

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

rem the i18n
mkdir target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang
xcopy target\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q
xcopy target\bin\ZTMZ.PacenoteTool.AC\release_win-x64\lang\ target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\lang\ /Y /V /Q

pushd ..\tools\ACRPacenoteTrace
call build.bat internal-bridge
if errorlevel 1 (
    popd
    cd /D "%currentDir%"
    exit /b 1
)
popd

mkdir "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ACRExperimentalBridge"
copy /Y "..\tools\ACRPacenoteTrace\ACRPacenoteTraceInjector.exe" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ACRExperimentalBridge\" >nul
copy /Y "..\tools\ACRPacenoteTrace\ACRFmodLivePacenoteBridge.dll" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ACRExperimentalBridge\" >nul
copy /Y "..\tools\ACRPacenoteTrace\ACRProtonRelay.exe" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ACRExperimentalBridge\" >nul
copy /Y "..\src\ZTMZ.PacenoteTool.AC\ExperimentalBridge\acr-live-bridge.manifest.json" "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\ACRExperimentalBridge\" >nul

rem stage only release defaults from the configured application-data directory
if not defined ZTMZ_PACKAGE_DATA_HOME (
    for /f "usebackq delims=" %%H in (`powershell -NoProfile -Command "$config = Get-ItemProperty -Path 'HKCU:\Software\ZTMZ Next Generation Pacenote Tool' -ErrorAction SilentlyContinue; if ($config -and $config.ZTMZHome) { [Console]::Write($config.ZTMZHome) }"`) do set "ZTMZ_PACKAGE_DATA_HOME=%%H"
)
if not defined ZTMZ_PACKAGE_DATA_HOME set "ZTMZ_PACKAGE_DATA_HOME=%USERPROFILE%\Documents\My Games\ZTMZClub_nextgen"

set "RELEASE_DATA_ROOT=target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
set "DEFAULT_CODRIVER_SOURCE=%ZTMZ_PACKAGE_DATA_HOME%\codrivers\default"

if not exist "%DEFAULT_CODRIVER_SOURCE%\info.json" (
    echo Default co-driver package was not found: "%DEFAULT_CODRIVER_SOURCE%"
    exit /b 1
)

mkdir "%RELEASE_DATA_ROOT%\codrivers\default"
xcopy "%DEFAULT_CODRIVER_SOURCE%\*" "%RELEASE_DATA_ROOT%\codrivers\default\" /E /I /Y /V /Q >nul
if errorlevel 1 exit /b %errorlevel%

if exist "%ZTMZ_PACKAGE_DATA_HOME%\profiles\default\" (
    mkdir "%RELEASE_DATA_ROOT%\profiles\default"
    xcopy "%ZTMZ_PACKAGE_DATA_HOME%\profiles\default\*" "%RELEASE_DATA_ROOT%\profiles\default\" /E /I /Y /V /Q >nul
    if errorlevel 1 exit /b %errorlevel%
)

cd /D "%currentDir%"

echo done.
exit /b 0

:publish_failed
echo Release publish failed. No installer should be created from this output.
cd /D "%currentDir%"
exit /b 1
