@echo off
dotnet build -c Release

rem copy built games to target folder

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Release\net6.0-windows\ ZTMZ.PacenoteTool\bin\Release\net6.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Release\net6.0-windows\ ZTMZ.PacenoteTool\bin\Release\net6.0-windows\games\ /Y /V /S
echo done.
