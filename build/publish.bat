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
