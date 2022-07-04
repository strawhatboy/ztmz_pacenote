@echo off
dotnet build -c Debug

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Debug\net6.0-windows\ ZTMZ.PacenoteTool\bin\Debug\net6.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Debug\net6.0-windows\ ZTMZ.PacenoteTool\bin\Debug\net6.0-windows\games\ /Y /V /S
echo done.
