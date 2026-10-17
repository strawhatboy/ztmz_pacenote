param([string]$Compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $repo ('tmp\installer-path-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch

# Compile and run the actual installer Code section, substituting only persisted
# registry/previous-installation inputs. Exit from InitializeWizard before any
# installation, downloads, registry writes or uninstall registration can occur.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup.iss') -Raw
$code = [regex]::Match($source, '(?s)\[Code\](.*?)\[Setup\]').Groups[1].Value
$code = $code.Replace('RegQueryStringValue(', 'TestReadRegistry(').Replace('GetPreviousData(', 'TestPreviousData(')
$testFunctions = @'
procedure TestExitProcess(Code: Integer);
  external 'ExitProcess@kernel32.dll stdcall';

function TestReadRegistry(RootKey: Integer; const Subkey, ValueName: String;
  var Value: String): Boolean;
begin
  Value := '';
  if (RootKey = HKEY_CURRENT_USER) and
     (Subkey = 'Software\{#MyAppName}') then
  begin
    if ValueName = 'InstallDir' then
      Value := ExpandConstant('{param:Registered|}')
    else if ValueName = 'ACRExperimentalInstallDir' then
      Value := ExpandConstant('{param:ExperimentalRegistered|}')
    else if ValueName = 'ZTMZHome' then
      Value := 'C:\ZTMZ test data';
  end;
  Result := Value <> '';
end;

function TestPreviousData(const ValueName, DefaultValue: String): String;
begin
  Result := DefaultValue;
  if ValueName = 'InstallDir' then
    Result := ExpandConstant('{param:Previous|}');
end;

procedure TestEqual(const Name, Actual, Expected: String);
begin
  if Actual <> Expected then
  begin
    SaveStringToFile(ExpandConstant('{param:Report}'),
      Name + ': expected ' + Expected + ', got ' + Actual, False);
    TestExitProcess(1);
  end;
end;

'@
$code = $code.Replace('#include "installer_paths.iss"', $testFunctions + "`r`n#include `"installer_paths.iss`"")
$code = $code.Replace('procedure InitializeWizard;', 'procedure InitializeProductionWizard;')
$code += @'

procedure InitializeWizard;
begin
  InitializeProductionWizard;
  TestEqual('Program destination', GetInstallDir(''), ExpandConstant('{param:Expected}'));
  TestEqual('Internal destination', WizardDirValue, GetInstallDir(''));
  TestEqual('Data destination', GetZTMZHome(''), 'C:\ZTMZ test data');
  { Simulate editing the custom page, then continuing to the Ready page. }
  DirPage.Values[0] := 'C:\ZTMZ manually selected';
  NextButtonClick(DirPage.ID);
  TestEqual('Edited internal destination', WizardDirValue, GetInstallDir(''));
  SaveStringToFile(ExpandConstant('{param:Report}'), 'PASS', False);
  TestExitProcess(0);
end;
'@

$cases = @(
    @{ Name = 'registry overrides stale previous data'; Registered = 'D:\ZTMZ current'; Previous = 'C:\ZTMZ old'; Expected = 'D:\ZTMZ current' },
    @{ Name = 'registry without previous data'; Registered = 'D:\ZTMZ current'; Expected = 'D:\ZTMZ current' },
    @{ Name = 'explicit DIR overrides registry'; Registered = 'D:\ZTMZ current'; Previous = 'C:\ZTMZ old'; DIR = 'E:\ZTMZ override'; Expected = 'E:\ZTMZ override' },
    @{ Name = 'previous data fallback'; Previous = 'D:\ZTMZ previous'; Expected = 'D:\ZTMZ previous' },
    @{ Name = 'blank registry fallback'; Registered = '   '; Previous = 'D:\ZTMZ previous'; Expected = 'D:\ZTMZ previous' },
    @{ Name = 'fresh install fallback'; Expected = 'C:\ZTMZ test default' },
    @{ Name = 'Unicode and spaces'; Registered = 'D:\工具目录\ZTMZ Test'; Expected = 'D:\工具目录\ZTMZ Test' },
    @{ Name = 'experimental ignores regular install'; Variant = 'experimental'; Registered = 'D:\ZTMZ regular'; Expected = 'C:\ZTMZ test default' },
    @{ Name = 'experimental previous install fallback'; Variant = 'experimental'; Registered = 'D:\ZTMZ regular'; Previous = 'E:\ZTMZ experimental'; Expected = 'E:\ZTMZ experimental' },
    @{ Name = 'experimental own registry'; Variant = 'experimental'; Registered = 'D:\ZTMZ regular'; ExperimentalRegistered = 'E:\ZTMZ experimental'; Expected = 'E:\ZTMZ experimental' }
)
foreach ($variant in @('normal', 'experimental')) {
    $variantSource = if ($variant -eq 'experimental') {
        Get-Content -LiteralPath (Join-Path $PSScriptRoot 'setup_acr_experimental.iss') -Raw
    } else { $source }
    $valueName = [regex]::Match($variantSource, '#define MyAppInstallDirValueName "([^"]+)"').Groups[1].Value
    if (-not $valueName) { throw "Missing install-directory registry value definition: $variant" }
    $header = @"
#define MyAppName "ZTMZ Next Generation Pacenote Tool"
#define MyAppInstallDirValueName "$valueName"
#define MyAppInstallFolder "ztmz-path-tests"
#define FolderName "ztmz-path-tests"
[Setup]
AppName=ZTMZ installer path tests
AppVersion=1
DefaultDirName=C:\ZTMZ test default
UsePreviousAppDir=no
PrivilegesRequired=lowest
Uninstallable=no
DisableDirPage=yes
OutputDir=$scratch
OutputBaseFilename=$variant
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl,$PSScriptRoot\customMessage.en.isl"
[Code]
"@
    $script = Join-Path $scratch "$variant.iss"
    [IO.File]::WriteAllText($script, $header + "`r`n" + $code, [Text.UTF8Encoding]::new($true))
    & $Compiler /Q "/I$PSScriptRoot" $script
    if ($LASTEXITCODE -ne 0) { throw "Inno compilation failed: $variant" }
}

$index = 0
foreach ($case in $cases) {
    $index++
    $variant = if ($case.Variant) { $case.Variant } else { 'normal' }
    $report = Join-Path $scratch "$index.txt"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/Report="{0}"' -f $report))
    foreach ($name in @('Registered', 'ExperimentalRegistered', 'Previous', 'DIR', 'Expected')) {
        if ($case.ContainsKey($name)) { $arguments += '/{0}="{1}"' -f $name, $case[$name] }
    }
    $process = Start-Process -FilePath (Join-Path $scratch "$variant.exe") -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) {
        $process.Kill()
        throw "Timed out: $($case.Name)"
    }
    if (-not (Test-Path -LiteralPath $report)) { throw "No test result: $($case.Name)" }
    $result = Get-Content -LiteralPath $report -Raw
    if ($result -ne 'PASS') { throw "$($case.Name): $result" }
    Write-Host "PASS: $($case.Name)"
}
Write-Host "$index installer path scenarios passed. Test artifacts: $scratch"
