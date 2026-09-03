@echo off
setlocal
cd /d "%~dp0"
if errorlevel 1 exit /b 1
set TRACE_OUTPUT=ACRPacenoteTrace.dll
if not "%~1"=="" set TRACE_OUTPUT=%~1
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
if /I "%~1"=="fmod" (
    cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRFmodNativeTrace.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:ACRFmodNativeTrace.dll
    if errorlevel 1 exit /b 1
    exit /b 0
)
if /I "%~1"=="fmod-parameters" (
    cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRFmodPacenoteParameterTrace.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:ACRFmodPacenoteParameterTrace.dll Advapi32.lib
    if errorlevel 1 exit /b 1
    exit /b 0
)
if /I "%~1"=="scan-fmod" (
    cl /nologo /std:c++20 /EHsc /O2 FindFmodStartCallers.cpp /link /OUT:FindFmodStartCallers.exe
    if errorlevel 1 exit /b 1
    exit /b 0
)
if /I "%~1"=="fmod-sound-info" (
    cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRFmodSoundInfoTrace.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:ACRFmodSoundInfoTrace.dll
    if errorlevel 1 exit /b 1
    exit /b 0
)
if /I "%~1"=="live-bridge" (
    cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRFmodLivePacenoteBridge.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:ACRFmodLivePacenoteBridge.dll Advapi32.lib
    if errorlevel 1 exit /b 1
    exit /b 0
)
if /I "%~1"=="internal-bridge" (
    cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRFmodLivePacenoteBridge.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:ACRFmodLivePacenoteBridge.dll Advapi32.lib
    if errorlevel 1 exit /b 1
    cl /nologo /std:c++20 /EHsc /O2 InjectTrace.cpp /link /OUT:ACRPacenoteTraceInjector.exe
    if errorlevel 1 exit /b 1
    cl /nologo /std:c++20 /EHsc /O2 /MT ACRProtonRelay.cpp /link /OUT:ACRProtonRelay.exe Ws2_32.lib
    if errorlevel 1 exit /b 1
    copy /Y ACRPacenoteTraceInjector.exe ..\..\src\ZTMZ.PacenoteTool.AC\ExperimentalBridge\ >nul
    if errorlevel 1 exit /b 1
    copy /Y ACRFmodLivePacenoteBridge.dll ..\..\src\ZTMZ.PacenoteTool.AC\ExperimentalBridge\ >nul
    if errorlevel 1 exit /b 1
    copy /Y ACRProtonRelay.exe ..\..\src\ZTMZ.PacenoteTool.AC\ExperimentalBridge\ >nul
    if errorlevel 1 exit /b 1
    exit /b 0
)
cl /nologo /std:c++20 /EHsc /LD /O2 /I third_party\minhook\include ACRPacenoteTrace.cpp third_party\minhook\src\buffer.c third_party\minhook\src\hook.c third_party\minhook\src\trampoline.c third_party\minhook\src\hde\hde64.c /link /OUT:%TRACE_OUTPUT%
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /O2 InjectTrace.cpp /link /OUT:ACRPacenoteTraceInjector.exe
if errorlevel 1 exit /b 1
