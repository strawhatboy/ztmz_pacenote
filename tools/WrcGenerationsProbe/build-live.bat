@echo off
setlocal
cd /d "%~dp0"
if not exist bin mkdir bin
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
pushd bin
cl /nologo /std:c++20 /EHsc /MT /LD /O2 /I ..\..\ACRPacenoteTrace\third_party\minhook\include ..\Bridge.cpp ..\..\ACRPacenoteTrace\third_party\minhook\src\buffer.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hook.c ..\..\ACRPacenoteTrace\third_party\minhook\src\trampoline.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hde\hde64.c /link /OUT:WrcGenerationsBridge.dll
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DWRCG_DLL_NAME=L\"WrcGenerationsBridge.dll\" ..\Inject.cpp /link /OUT:WrcGenerationsBridgeInject.exe
if errorlevel 1 exit /b 1
popd
