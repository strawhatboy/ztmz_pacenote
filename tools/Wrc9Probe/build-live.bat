@echo off
setlocal
cd /d "%~dp0"
if not exist bin mkdir bin
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
pushd bin
cl /nologo /std:c++20 /EHsc /MT /O2 ..\FixedEntryHookTest.cpp /link /OUT:Wrc9FixedEntryHookTest.exe
if errorlevel 1 exit /b 1
Wrc9FixedEntryHookTest.exe
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /LD /O2 /I ..\..\ACRPacenoteTrace\third_party\minhook\include /I ..\..\ACRPacenoteTrace\third_party\minhook\src ..\Bridge.cpp ..\..\ACRPacenoteTrace\third_party\minhook\src\buffer.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hook.c ..\..\ACRPacenoteTrace\third_party\minhook\src\trampoline.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hde\hde64.c /link /OUT:Wrc9Bridge.dll
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DKT_TARGET_EXE=L\"WRC9.exe\" /DWRCG_DLL_NAME=L\"Wrc9Bridge.dll\" ..\..\WrcGenerationsProbe\Inject.cpp /link /OUT:Wrc9BridgeInject.exe
if errorlevel 1 exit /b 1
popd
