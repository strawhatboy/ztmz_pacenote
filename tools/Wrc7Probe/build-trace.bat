@echo off
setlocal
cd /d "%~dp0"
if not exist bin mkdir bin
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
pushd bin
cl /nologo /std:c++20 /EHsc /MT /LD /O2 /DKT_WRC7 /I ..\..\ACRPacenoteTrace\third_party\minhook\include ..\..\WrcGenerationsProbe\Trace.cpp ..\..\ACRPacenoteTrace\third_party\minhook\src\buffer.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hook.c ..\..\ACRPacenoteTrace\third_party\minhook\src\trampoline.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hde\hde64.c /link /OUT:Wrc7SoundTrace.dll
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DKT_TARGET_EXE=L\"WRC7.exe\" /DWRCG_DLL_NAME=L\"Wrc7SoundTrace.dll\" ..\..\WrcGenerationsProbe\Inject.cpp /link /OUT:Wrc7TraceInject.exe
if errorlevel 1 exit /b 1
popd
