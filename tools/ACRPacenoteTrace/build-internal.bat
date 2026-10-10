@echo off
setlocal
if "%~1"=="" exit /b 1
set "BRIDGE_OUTPUT=%~f1"
set "BRIDGE_SOURCE=%~dp0"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Visual Studio C++ build tools are required for the ACR bridge.
    exit /b 1
)
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "BRIDGE_VS=%%i"
if not defined BRIDGE_VS exit /b 1
call "%BRIDGE_VS%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
if not exist "%BRIDGE_OUTPUT%" mkdir "%BRIDGE_OUTPUT%"
if errorlevel 1 exit /b 1
pushd "%BRIDGE_OUTPUT%"
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /LD /O2 /I "%BRIDGE_SOURCE%third_party\minhook\include" "%BRIDGE_SOURCE%ACRFmodLivePacenoteBridge.cpp" "%BRIDGE_SOURCE%third_party\minhook\src\buffer.c" "%BRIDGE_SOURCE%third_party\minhook\src\hook.c" "%BRIDGE_SOURCE%third_party\minhook\src\trampoline.c" "%BRIDGE_SOURCE%third_party\minhook\src\hde\hde64.c" /link /OUT:ACRFmodLivePacenoteBridge.dll Advapi32.lib
if errorlevel 1 goto :failed
cl /nologo /std:c++20 /EHsc /O2 "%BRIDGE_SOURCE%InjectTrace.cpp" /link /OUT:ACRPacenoteTraceInjector.exe
if errorlevel 1 goto :failed
cl /nologo /std:c++20 /EHsc /O2 /MT "%BRIDGE_SOURCE%ACRProtonRelay.cpp" /link /OUT:ACRProtonRelay.exe Ws2_32.lib
if errorlevel 1 goto :failed
popd
exit /b 0
:failed
popd
exit /b 1
