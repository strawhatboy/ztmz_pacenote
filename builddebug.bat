@echo off
cd ZTMZ.PacenoteTool.Codemasters
dotnet build -c Debug
cd ..

cd ZTMZ.PacenoteTool.RBR
dotnet build -c Debug
cd ..

cd ZTMZ.PacenoteTool.WpfGUI
dotnet build -c Debug
cd ..

rem copy built games to target folder

echo copying built games to target folder
xcopy ZTMZ.PacenoteTool.Codemasters\bin\Debug\net6.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Debug\net6.0-windows\games\ /Y /V /S
xcopy ZTMZ.PacenoteTool.RBR\bin\Debug\net6.0-windows\ ZTMZ.PacenoteTool.WpfGUI\bin\Debug\net6.0-windows\games\ /Y /V /S
echo done.
