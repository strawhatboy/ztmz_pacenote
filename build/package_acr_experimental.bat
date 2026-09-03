@echo off
setlocal

set "CURRENT_DIR=%cd%"
set "ROOT=%~dp0.."
set "SOURCE_ROOT=%ROOT%\src"
set "ARTIFACT_ROOT=%ROOT%\build\target_acr_experimental"
set "RELEASE_ROOT=%ARTIFACT_ROOT%\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
set "GAME_TARGET=%RELEASE_ROOT%\games"
set "BRIDGE_SOURCE=%ROOT%\tools\ACRPacenoteTrace"
set "BRIDGE_TARGET=%GAME_TARGET%\ACRExperimentalBridge"

if exist "%ARTIFACT_ROOT%" rmdir /S /Q "%ARTIFACT_ROOT%"

call :publish ZTMZ.PacenoteTool.Codemasters
if errorlevel 1 goto :failed
call :publish ZTMZ.PacenoteTool.RBR
if errorlevel 1 goto :failed
call :publish ZTMZ.PacenoteTool.BeamNG
if errorlevel 1 goto :failed
call :publish ZTMZ.PacenoteTool.AC
if errorlevel 1 goto :failed
call :publish ZTMZ.PacenoteTool.WpfGUI
if errorlevel 1 goto :failed

mkdir "%GAME_TARGET%"
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\" "%GAME_TARGET%\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\" "%GAME_TARGET%\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\" "%GAME_TARGET%\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.AC\release_win-x64\" "%GAME_TARGET%\" /Y /V /S /Q

mkdir "%GAME_TARGET%\lang"
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.Codemasters\release_win-x64\lang\" "%GAME_TARGET%\lang\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.RBR\release_win-x64\lang\" "%GAME_TARGET%\lang\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.BeamNG\release_win-x64\lang\" "%GAME_TARGET%\lang\" /Y /V /S /Q
xcopy "%ARTIFACT_ROOT%\bin\ZTMZ.PacenoteTool.AC\release_win-x64\lang\" "%GAME_TARGET%\lang\" /Y /V /S /Q

pushd "%BRIDGE_SOURCE%"
call build.bat internal-bridge
if errorlevel 1 (
    popd
    goto :failed
)
popd

mkdir "%BRIDGE_TARGET%"
copy /Y "%BRIDGE_SOURCE%\ACRPacenoteTraceInjector.exe" "%BRIDGE_TARGET%\" >nul
copy /Y "%BRIDGE_SOURCE%\ACRFmodLivePacenoteBridge.dll" "%BRIDGE_TARGET%\" >nul
copy /Y "%BRIDGE_SOURCE%\ACRProtonRelay.exe" "%BRIDGE_TARGET%\" >nul
copy /Y "%SOURCE_ROOT%\ZTMZ.PacenoteTool.AC\ExperimentalBridge\acr-live-bridge.manifest.json" "%BRIDGE_TARGET%\" >nul

if not defined ZTMZ_PACKAGE_DATA_HOME (
    for /f "usebackq delims=" %%H in (`powershell -NoProfile -Command "$config = Get-ItemProperty -Path 'HKCU:\Software\ZTMZ Next Generation Pacenote Tool' -ErrorAction SilentlyContinue; if ($config -and $config.ZTMZHome) { [Console]::Write($config.ZTMZHome) }"`) do set "ZTMZ_PACKAGE_DATA_HOME=%%H"
)
if not defined ZTMZ_PACKAGE_DATA_HOME set "ZTMZ_PACKAGE_DATA_HOME=%USERPROFILE%\Documents\My Games\ZTMZClub_nextgen"

set "DEFAULT_CODRIVER_SOURCE=%ZTMZ_PACKAGE_DATA_HOME%\codrivers\default"
if not exist "%DEFAULT_CODRIVER_SOURCE%\info.json" (
    echo Default co-driver package was not found: "%DEFAULT_CODRIVER_SOURCE%"
    goto :failed
)

mkdir "%RELEASE_ROOT%\codrivers\default"
xcopy "%DEFAULT_CODRIVER_SOURCE%\*" "%RELEASE_ROOT%\codrivers\default\" /E /I /Y /V /Q >nul
if errorlevel 1 goto :failed

if exist "%ZTMZ_PACKAGE_DATA_HOME%\profiles\default\" (
    mkdir "%RELEASE_ROOT%\profiles\default"
    xcopy "%ZTMZ_PACKAGE_DATA_HOME%\profiles\default\*" "%RELEASE_ROOT%\profiles\default\" /E /I /Y /V /Q >nul
    if errorlevel 1 goto :failed
)

ISCC /V:1 /O"%ROOT%\Output" setup_acr_experimental.iss
if errorlevel 1 goto :failed

echo Internal ACR experimental Release package is ready:
echo %RELEASE_ROOT%
echo Installer: %ROOT%\Output\ZTMZClub_PacenoteTool_ACR_Experimental_Installer_*.exe
echo This package is for private validation only. The bridge remains opt-in: enable its controls, then choose injection from the ACR-ready warning or settings pane.
cd /D "%CURRENT_DIR%"
exit /b 0

:publish
pushd "%SOURCE_ROOT%\%~1"
dotnet publish -c Release -f net10.0-windows -r win-x64 --artifacts-path "%ARTIFACT_ROOT%" --property WarningLevel=0 --property ArtifactsPivots=release_win-x64
set "PUBLISH_RESULT=%ERRORLEVEL%"
popd
exit /b %PUBLISH_RESULT%

:failed
echo Internal ACR experimental Release package failed.
cd /D "%CURRENT_DIR%"
exit /b 1
