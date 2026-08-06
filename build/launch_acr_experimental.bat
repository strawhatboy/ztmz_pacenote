@echo off
setlocal

set "ROOT=%~dp0.."
set "APP_ROOT=%ROOT%\build\target_acr_experimental\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
set "APP_EXE=%APP_ROOT%\ZTMZ.PacenoteTool.WpfGUI.exe"

if not exist "%APP_EXE%" (
    echo Internal ACR experimental package was not found.
    echo Run package_acr_experimental.bat first.
    exit /b 1
)

pushd "%APP_ROOT%"
start "ZTMZ ACR Experimental" "%APP_EXE%"
popd
