@echo off

cd ZTMZ.PacenoteTool.Base
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Release
cd ..

cd ZTMZ.PacenoteTool.Console
dotnet build -c Release
cd ..

echo copying built games to target folder

xcopy ZTMZ.PacenoteTool.Codemasters\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.Console\bin\Release\net8.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Release\net8.0-windows\ ZTMZ.PacenoteTool.Console\bin\Release\net8.0-windows\games\ /Y /V /S
echo done.
