@echo off
setlocal
set "currentDir=%cd%"
set "configuration=Debug"
if not "%~1"=="" set "configuration=%~1"
for %%I in ("%~dp0..\src\ZTMZ.PacenoteTool.Avalonia") do set "projectDir=%%~fI"
set "projectFile=%projectDir%\ZTMZ.PacenoteTool.Avalonia.csproj"
set "outputDir=%projectDir%\bin\%configuration%\net10.0-windows"
set "executable=%outputDir%\com.ztmz.PacenoteTool.exe"
set "dataRoot=%ZTMZ_DATA_HOME%"
if "%dataRoot%"=="" (
    for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\ZTMZ Next Generation Pacenote Tool" /v ZTMZHome 2^>nul ^| findstr /R /C:"ZTMZHome"') do set "dataRoot=%%B"
)
if "%dataRoot%"=="" set "dataRoot=%userprofile%\Documents\My Games\ZTMZClub_nextgen"
set "dataLanguageDir=%dataRoot%\lang"

cd /D "%projectDir%"

echo Building ZTMZ.PacenoteTool.Avalonia (%configuration%)...
echo.

dotnet build "%projectFile%" -c "%configuration%" -f net10.0-windows ^
    --disable-build-servers --maxcpucount:1 -p:UseAppHost=true
if errorlevel 1 (
    echo.
    echo Build failed!
    cd /D "%currentDir%"
    exit /b 1
)

if not exist "%executable%" (
    echo.
    echo Build completed but the Windows executable was not generated:
    echo   %executable%
    cd /D "%currentDir%"
    exit /b 1
)

echo Refreshing localization files in the ZTMZ data folder...
if not exist "%dataLanguageDir%" mkdir "%dataLanguageDir%"
if not exist "%dataLanguageDir%" (
    echo.
    echo Could not create the localization folder:
    echo   %dataLanguageDir%
    cd /D "%currentDir%"
    exit /b 1
)

xcopy "%projectDir%\..\ZTMZ.PacenoteTool.I18N\lang\*" "%dataLanguageDir%\" /Y /V /S /Q
if errorlevel 1 (
    echo.
    echo Failed to refresh localization files in:
    echo   %dataLanguageDir%
    cd /D "%currentDir%"
    exit /b 1
)

echo.
echo Build succeeded. Executable:
echo   %executable%
cd /D "%currentDir%"
exit /b 0
