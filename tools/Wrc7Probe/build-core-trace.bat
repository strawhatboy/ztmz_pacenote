@echo off
setlocal
cd /d "%~dp0"
if not exist bin mkdir bin
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
pushd bin
set "trace_name=Wrc7CoreTrace"
set "trace_define="
set "buffer_source=..\..\ACRPacenoteTrace\third_party\minhook\src\buffer.c"
if /i "%~1"=="engine" (
  set "trace_name=Wrc7DriverTraceExtended"
  rem Only the byte-verified driver prologue is hooked in this variant. Its
  rem relocated instructions have no RIP-relative operands; the wider rel32
  rem allocation range does not change other games or generic FMOD hooks.
  set "trace_define=/DKT_ENGINE_TRACE"
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-minhook.ps1"
  if errorlevel 1 exit /b 1
  set "buffer_source=wrc7-buffer.c"
)
cl /nologo /std:c++20 /EHsc /MT /LD /O2 %trace_define% /I ..\..\ACRPacenoteTrace\third_party\minhook\include /I ..\..\ACRPacenoteTrace\third_party\minhook\src ..\CoreTrace.cpp %buffer_source% ..\..\ACRPacenoteTrace\third_party\minhook\src\hook.c ..\..\ACRPacenoteTrace\third_party\minhook\src\trampoline.c ..\..\ACRPacenoteTrace\third_party\minhook\src\hde\hde64.c /link /OUT:%trace_name%.dll
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /DKT_TARGET_EXE=L\"WRC7.exe\" /DWRCG_DLL_NAME=L\"%trace_name%.dll\" ..\..\WrcGenerationsProbe\Inject.cpp /link /OUT:%trace_name%Inject.exe
if errorlevel 1 exit /b 1
popd
