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
cl /nologo /std:c++20 /EHsc /MT /LD /O2 ..\DriverTrace.cpp /link /OUT:Wrc9DriverTrace.dll
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DKT_TARGET_EXE=L\"WRC9.exe\" /DWRCG_DLL_NAME=L\"Wrc9DriverTrace.dll\" ..\..\WrcGenerationsProbe\Inject.cpp /link /OUT:Wrc9DriverTraceInject.exe
exit /b %errorlevel%
