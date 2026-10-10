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


powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\build\stage_release_resources.ps1" -DestinationRoot "%RELEASE_ROOT%"
if errorlevel 1 goto :failed

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
