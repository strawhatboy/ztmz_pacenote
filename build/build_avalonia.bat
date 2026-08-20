@echo off
setlocal
set "currentDir=%cd%"
set "configuration=Debug"
if not "%~1"=="" set "configuration=%~1"
for %%I in ("%~dp0..\src\ZTMZ.PacenoteTool.Avalonia") do set "projectDir=%%~fI"
set "projectFile=%projectDir%\ZTMZ.PacenoteTool.Avalonia.csproj"
set "outputDir=%projectDir%\bin\%configuration%\net10.0-windows"
set "executable=%outputDir%\com.ztmz.PacenoteTool.exe"

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

echo.
echo Build succeeded. Executable:
echo   %executable%
cd /D "%currentDir%"
exit /b 0
