@echo off
set currentDir=%cd%

cd /D "%~dp0"
cd ../src/ZTMZ.PacenoteTool.Avalonia

echo Building ZTMZ.PacenoteTool.Avalonia...
echo.

dotnet build -c Debug --disable-build-servers --maxcpucount:1 -p:WarningLevel=0
if errorlevel 1 (
    echo.
    echo Build failed!
    cd /D "%currentDir%"
    exit /b 1
)

echo.
echo Build succeeded.
cd /D "%currentDir%"
exit /b 0
