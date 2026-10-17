#define MyAppName "ZTMZ Next Generation Pacenote Tool"
#ifndef MyAppPublishRoot
  #define MyAppPublishRoot "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
#endif
#define MyAppPublishPath AddBackslash(SourcePath) + MyAppPublishRoot + "\ZTMZ.PacenoteTool.WpfGUI.exe"
#if !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\Assets\Games\wrc9.jpg") || !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\games\WRC9Bridge\Wrc9Bridge.dll") || !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\games\WRC9Bridge\Wrc9BridgeInject.exe")
  #error "WRC9 provider payload is missing. Run the release build and payload verification first."
#endif
#if !FileExists(MyAppPublishPath)
  #error "Published WPF executable was not found. Run build\publish.bat before packaging."
#endif
#define MyAppMonacoIndexPath AddBackslash(SourcePath) + MyAppPublishRoot + "\Assets\Monaco\index.html"
#if !FileExists(MyAppMonacoIndexPath)
  #error "Published Monaco editor assets were not found. Run build\publish.bat before packaging."
#endif
#define MyAppPacenoteIconManifestPath AddBackslash(SourcePath) + MyAppPublishRoot + "\Assets\PacenoteIcons\v1\manifest.json"
#if !FileExists(MyAppPacenoteIconManifestPath)
  #error "Published pacenote icon assets were not found. Run build\publish.bat before packaging."
#endif
#define MyAppVersion GetVersionNumbersString(MyAppPublishPath)
#if !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\Assets\Games\wrcg.jpg") || !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\games\WRCGBridge\WrcGenerationsBridge.dll") || !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\games\WRCGBridge\WrcGenerationsBridgeInject.exe")
  #error "Published WRCG image or bridge is missing. Run build\publish.bat before packaging."
#endif
#define MyAppNativeDashboardPath AddBackslash(SourcePath) + MyAppPublishRoot + "\native-dashboard\win-x64\ztmz_dashboard_overlay.dll"
#if !FileExists(MyAppNativeDashboardPath)
  #error "Published native dashboard DLL is missing. Run build\publish.bat before packaging."
#endif
#define MyAppNativeFontPath AddBackslash(SourcePath) + MyAppPublishRoot + "\fonts\"
#define MyAppNativeX86Path AddBackslash(SourcePath) + MyAppPublishRoot + "\native-dashboard\win-x86\"
#if !FileExists(MyAppNativeX86Path + "ztmz_dashboard_overlay.dll") || !FileExists(MyAppNativeX86Path + "ztmz_dashboard_injector.exe")
  #error "Published x86 native dashboard DLL/injector is missing. Run build\publish.bat."
#endif
#if !FileExists(MyAppNativeFontPath + "WRCCleanRoman.ttf") || !FileExists(MyAppNativeFontPath + "WRCCleanBold.ttf")
  #error "Published native dashboard fonts are missing. Run build\publish.bat before packaging."
#endif
#ifndef MyAppReleaseSuffix
  #define MyAppReleaseSuffix "-rc29"
#endif
#define MyAppPublisher "ZTMZ Club"
#define MyAppURL "https://gitee.com/ztmz/ztmz_pacenote"
#define MyAppExeName "ZTMZ.PacenoteTool.WpfGUI.exe"
#define MyLauncherExeName "ZTMZ.Launcher.exe"
#if !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\ZTMZ.Launcher.exe") || !FileExists(AddBackslash(SourcePath) + MyAppPublishRoot + "\ZTMZ.startup.json")
  #error "Native startup launcher/manifest is missing. Run build\publish.bat."
#endif
#define FolderName "ZTMZClub_nextgen"
#ifndef MyAppDisplayName
  #define MyAppDisplayName MyAppName
#endif
#ifndef MyAppId
  #define MyAppId "{{9EF39FA9-58C6-40E7-B957-75EAC73369E7}"
#endif
#ifndef MyAppInstallFolder
  #define MyAppInstallFolder "ztmzclub_pacenotetool_nextgen"
#endif
#ifndef MyAppInstallDirValueName
  #define MyAppInstallDirValueName "InstallDir"
#endif

[Components]
Name: "ztmz_ngptool"; Description: "ZTMZ Next Generation Pacenote Tool"; Types: full compact custom; Flags: fixed

; selectable components
Name: "webview2"; Description: "Microsoft Edge WebView2 Runtime"; Types: full custom; ExtraDiskSpaceRequired: 50000000

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedDesktopRuntime: Boolean;
  NeedAspNetCoreRuntime: Boolean;
  DirPage: TInputDirWizardPage;
  HiddenPage: TInputDirWizardPage;
  HiddenPage2: TInputDirWizardPage;
  InstallWebView2: Boolean;
  WebView2InstallerDownloaded: Boolean;

#include "installer_paths.iss"

procedure AppendDirBrowseClick(Sender: TObject);
begin
  HiddenPage.Values[0] := DirPage.Values[0];
  HiddenPage.Buttons[0].OnClick(HiddenPage.Buttons[0]);
  DirPage.Values[0] := HiddenPage.Values[0];
end;
procedure AppendDirBrowseClick2(Sender: TObject);
begin
  HiddenPage2.Values[0] := DirPage.Values[1];
  HiddenPage2.Buttons[0].OnClick(HiddenPage2.Buttons[0]);
  DirPage.Values[1] := HiddenPage2.Values[0];
end;

function SkipPage(Sender: TWizardPage): Boolean;
begin
  Result := True;
end;


{ Exec with output stored in result. }
{ ResultString will only be altered if True is returned. }
function ExecWithResult(const Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ResultCode: Integer; var ResultString: AnsiString): Boolean;
var
  TempFilename: String;
  Command: String;
begin
  TempFilename := ExpandConstant('{tmp}\~execwithresult.txt');
  { Exec via cmd and redirect output to file. Must use special string-behavior to work. }
  Command :=
    Format('"%s" /S /C ""%s" %s > "%s""', [
      ExpandConstant('{cmd}'), Filename, Params, TempFilename]);
  Result := Exec(ExpandConstant('{cmd}'), Command, WorkingDir, ShowCmd, Wait, ResultCode);
  if not Result then
    Exit;
  LoadStringFromFile(TempFilename, ResultString);  { Cannot fail }
  DeleteFile(TempFilename);
  { Remove new-line at the end }
  if (Length(ResultString) >= 2) and (ResultString[Length(ResultString) - 1] = #13) and
     (ResultString[Length(ResultString)] = #10) then
    Delete(ResultString, Length(ResultString) - 1, 2);
end;

function StartsText(const substr: String; str: String): Boolean;
var 
  index: Integer;

begin
  index:= Pos(substr, str);
  Result:= False;
  if (index = 1) then
    Result:= True;
end;

function IsChinaStandardTime: Boolean;
var
  TimeZoneKeyName: String;
begin
  Result := RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\TimeZoneInformation',
    'TimeZoneKeyName', TimeZoneKeyName) and
    (CompareText(TimeZoneKeyName, 'China Standard Time') = 0);
end;

function GetDotNetDesktopRuntimeDownloadUrl: String;
begin
  if IsChinaStandardTime then
  begin
    Log('Using the Gitee .NET Desktop Runtime mirror for China Standard Time.');
    Result := 'https://gitee.com/ztmz/dotnet-runtimes/releases/download/desktop-10.0.10/windowsdesktop-runtime-10.0.10-win-x64.exe';
  end
  else
  begin
    Log('Using the Microsoft .NET Desktop Runtime download.');
    Result := 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  end;
end;

function GetAspNetCoreRuntimeDownloadUrl: String;
begin
  if IsChinaStandardTime then
  begin
    Log('Using the Gitee ASP.NET Core Runtime mirror for China Standard Time.');
    Result := 'https://gitee.com/ztmz/dotnet-runtimes/releases/download/aspnet-core10.0.10/aspnetcore-runtime-10.0.10-win-x64.exe';
  end
  else
  begin
    Log('Using the Microsoft ASP.NET Core Runtime download.');
    Result := 'https://aka.ms/dotnet/10.0/aspnetcore-runtime-win-x64.exe';
  end;
end;

function CheckDotNetRuntimeInstalled(const RuntimeName: String): Boolean;
var 
  i: Integer;
  Success: Boolean;
  ResultCode: Integer;
  ExecStdout: AnsiString;
  Lines: TStringList;
begin
  Success :=
    ExecWithResult('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated,
      ResultCode, ExecStdout) or
    (ResultCode <> 0);
  if (Success) then
  begin
    Lines := TStringList.Create;
    try
      Lines.Text := ExecStdout;
      { Find the .NET 10 shared framework required by this release. }
      for i:= 0 to Lines.Count-1 do
      begin
        if StartsText(RuntimeName + ' 10.', Lines[i]) then
        begin
          Result:= True;
          Exit;
        end;
      end;
    finally
      Lines.Free;
    end;
  end;
  Result:= False;
end;

function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  // 检查 64 位系统的注册表项
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    if Version > '0.0.0.0' then
      Result := True;
  end
  else
  begin
    // 检查 32 位系统的注册表项
    if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    begin
      if Version > '0.0.0.0' then
        Result := True;
    end;
  end;
end;

function OnDownloadProgress(const Url, Filename: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    Log(Format('  %d of %d bytes done.', [Progress, ProgressMax]))
  else
    Log(Format('  %d bytes done.', [Progress]));
  Result := True;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'InstallDir', DirPage.Values[0]);
  SetPreviousData(PreviousDataKey, 'ZTMZHome', DirPage.Values[1]);
end;

function GetInstallDir(const Param: String): String;
begin
  Result := DirPage.Values[0];
end;

function GetZTMZHome(const Param: String): String;
begin
  Result := DirPage.Values[1];
end;

procedure ProjectLinkLabelOnClick(Sender: TObject; const Link: string; LinkType: TSysLinkType);
var
  ErrorCode: Integer;
begin
  ShellExecAsOriginalUser('open', 'http://gitee.com/ztmz', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

{ The Main Function }
procedure InitializeWizard;
var
  ProjectLinkLabel: TNewLinkLabel;
  ConfiguredInstallDir: String;
  ConfiguredDataHome: String;
begin
  { the link label at bottom left}
  ProjectLinkLabel := TNewLinkLabel.Create(WizardForm);
  ProjectLinkLabel.Parent := WizardForm;
  ProjectLinkLabel.Caption := '<a href="http://gitee.com/ztmz">ZTMZ Club</a>';
  ProjectLinkLabel.Left := ScaleX(16);
  ProjectLinkLabel.Top :=
    WizardForm.BackButton.Top +
    (WizardForm.BackButton.Height div 2) -
    (ProjectLinkLabel.Height div 2);
  ProjectLinkLabel.Anchors := [akLeft, akBottom];
  ProjectLinkLabel.OnLinkClick := @ProjectLinkLabelOnClick;

  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);

  DirPage := CreateInputDirPage(
  wpSelectDir, SetupMessage(msgWizardSelectDir), '', ExpandConstant('{cm:SelectInstallTarget}'), False, '');

  DirPage.Add(ExpandConstant('{cm:ZTMZInstallFolder}'));
  DirPage.Add(ExpandConstant('{cm:ZTMZFolder}'));

  { GetPreviousData reads Inno's uninstall data, not our application registry key. }
  ConfiguredInstallDir := '';
  RegQueryStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppName}',
    '{#MyAppInstallDirValueName}', ConfiguredInstallDir);
  DirPage.Values[0] := ResolveInstallDirectory(
    ExpandConstant('{param:DIR|}'), ConfiguredInstallDir,
    GetPreviousData('InstallDir', ''), WizardDirValue);
  { Keep Inno's app directory, Ready page and uninstaller beside the program. }
  WizardForm.DirEdit.Text := DirPage.Values[0];
  Log('Selected ZTMZ installation directory: ' + DirPage.Values[0]);
  { The application may have moved its data folder since the last installation. }
  ConfiguredDataHome := '';
  RegQueryStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppName}', 'ZTMZHome', ConfiguredDataHome);
  if Trim(ConfiguredDataHome) <> '' then
    DirPage.Values[1] := ConfiguredDataHome
  else
  begin
    try
      DirPage.Values[1] := GetPreviousData('ZTMZHome', ExpandConstant('{userdocs}\My Games\{#FolderName}'));
    except
      { Fall back if the user's Documents folder cannot be resolved. }
      DirPage.Values[1] := GetPreviousData('ZTMZHome', ExpandConstant('{autoappdata}\{#FolderName}'));
    end;
  end;

  DirPage.Buttons[0].OnClick := @AppendDirBrowseClick;
  DirPage.Buttons[1].OnClick := @AppendDirBrowseClick2;

  HiddenPage := CreateInputDirPage(
    wpSelectDir, SetupMessage(msgWizardSelectDir), '', '', True, ExpandConstant('{cm:ZTMZInstallFolder}'));
  HiddenPage.Add('');
  HiddenPage.OnShouldSkipPage := @SkipPage;

  HiddenPage2 := CreateInputDirPage(
    wpSelectDir, SetupMessage(msgWizardSelectDir), '', '', True, ExpandConstant('{cm:ZTMZFolder}'));
  HiddenPage2.Add('');
  HiddenPage2.OnShouldSkipPage := @SkipPage;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  fullFileName: String;
  ResultCode: Integer;
  WebView2Installer: String;
begin
  WizardForm.DirEdit.Text := DirPage.Values[0];
  if (CurPageID = wpReady) then
  begin
    DownloadPage.Clear;

    // The application requires both the Windows Desktop and ASP.NET Core shared frameworks.
    NeedDesktopRuntime := not CheckDotNetRuntimeInstalled('Microsoft.WindowsDesktop.App');
    NeedAspNetCoreRuntime := not CheckDotNetRuntimeInstalled('Microsoft.AspNetCore.App');
    if NeedDesktopRuntime then
    begin
      DownloadPage.Add(GetDotNetDesktopRuntimeDownloadUrl(), 'dotnet-desktop-10.exe', '');
    end;
    if NeedAspNetCoreRuntime then
    begin
      DownloadPage.Add(GetAspNetCoreRuntimeDownloadUrl(), 'dotnet-aspnetcore-10.exe', '');
    end;

    // Check for WebView2
    InstallWebView2 := WizardIsComponentSelected('webview2') and not IsWebView2Installed();
    if InstallWebView2 then
    begin
      WebView2Installer := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
    end;

    if NeedDesktopRuntime or NeedAspNetCoreRuntime or InstallWebView2 then
    begin
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          WebView2InstallerDownloaded := True; // Add this line

          // Install the Desktop Runtime before the ASP.NET Core Runtime.
          if NeedDesktopRuntime then
          begin
            fullFileName := ExpandConstant('{tmp}\dotnet-desktop-10.exe');
            Result := Exec(fullFileName, '/install /passive', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            if not Result then
            begin
              RaiseException('Failed to install the .NET Desktop Runtime.');
            end;
          end;

          if NeedAspNetCoreRuntime then
          begin
            fullFileName := ExpandConstant('{tmp}\dotnet-aspnetcore-10.exe');
            Result := Exec(fullFileName, '/install /passive', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            if not Result then
            begin
              RaiseException('Failed to install the ASP.NET Core Runtime.');
            end;
          end;

          // Install WebView2
          if InstallWebView2 then
          begin
            fullFileName := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
            Result := Exec(fullFileName, '/install /silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            if not Result then
            begin
              RaiseException('Failed to install WebView2.');
            end;
          end;
        except
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end
    else
      Result := True;
  end
  else
    Result := True;
end;

[Setup]
; Need to set the signtool in Inno Setup UI, tool name is "mssign"
; tool command is "signtool.exe sign /f "[THE PFX CERT PATH]" /p [THE PASSWORD] /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a $p"
; replace [THE PFX CERT PATH] with the path of the pfx file, and [THE PASSWORD] with the password of the pfx file

; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={#MyAppId}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
; .NET 10 does not support Windows 7 or Windows 8.1.
MinVersion=10.0
;AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppInstallFolder}
DisableProgramGroupPage=yes
; Uncomment the following line to run in non administrative install mode (install for current user only.)
PrivilegesRequired=lowest
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#MyAppVersion}
;PrivilegesRequired=lowest
UninstallDisplayIcon={code:GetInstallDir}\{#MyAppExeName}
WizardSmallImageFile=wizard_small_image.bmp

; use custom dir page
DisableDirPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl,customMessage.en.isl"; LicenseFile:"..\LICENSE"; InfoBeforeFile:"..\src\ZTMZ.PacenoteTool.WpfGUI\eula.txt";
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl,customMessage.cn.isl"; LicenseFile:"..\LICENSE"; InfoBeforeFile:"..\src\ZTMZ.PacenoteTool.WpfGUI\eula-cn.txt";

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";

[InstallDelete]
Type: files; Name: "{code:GetZTMZHome}\config.json";
; Delete previous default audio package
Type: filesandordirs; Name: "{code:GetZTMZHome}\codrivers\default";

[Files]
; No json file!
; Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\*.json"; Excludes:"config.json,userconfig.json,*.deps.json,*.runtimeconfig.json"; DestDir: "{code:GetZTMZHome}"; Flags: ignoreversion
; Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\*.csv"; DestDir: "{code:GetZTMZHome}"; Flags: ignoreversion
Source: "{#MyAppPublishRoot}\codrivers\*"; DestDir: "{code:GetZTMZHome}\codrivers"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\profiles\*"; Excludes: "default\Assetto Corsa Rally\*"; DestDir: "{code:GetZTMZHome}\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#MyAppPublishRoot}\lang\*"; DestDir: "{code:GetZTMZHome}\lang"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\games\*"; Excludes:"*.json,dashboards\*"; DestDir: "{code:GetInstallDir}\games"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\games\lang\*.json"; DestDir: "{code:GetInstallDir}\games\lang"; Flags: ignoreversion recursesubdirs createallsubdirs
; BeamNG's optional bridge is packaged from these files at runtime, so its JSON
; manifest and input-action definition must remain beside the provider DLL.
Source: "{#MyAppPublishRoot}\games\BeamNGMod\*.json"; DestDir: "{code:GetInstallDir}\games\BeamNGMod"; Flags: ignoreversion recursesubdirs createallsubdirs
; ACR's experimental bridge is explicitly enabled by the user at runtime.
Source: "{#MyAppPublishRoot}\games\ACRExperimentalBridge\*.json"; DestDir: "{code:GetInstallDir}\games\ACRExperimentalBridge"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\games\*.json"; Excludes:"*.deps.json"; DestDir: "{code:GetZTMZHome}\games"; Flags: ignoreversion recursesubdirs createallsubdirs
; Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\runtimes\*"; DestDir: "{code:GetInstallDir}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\dashboards\*"; DestDir: "{code:GetZTMZHome}\dashboards"; Flags: ignoreversion recursesubdirs createallsubdirs
; WebDashboardServer serves its static client and fonts beside the installed executable.
Source: "{#MyAppPublishRoot}\wwwroot\*"; DestDir: "{code:GetInstallDir}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs
; Third-party satellite resource DLLs are intentionally omitted (English fallback).
; WebView2Loader.dll is already included at the application root by setup_normal.iss.
Source: "{#MyAppPublishRoot}\ThirdPartyNotices\*"; DestDir: "{code:GetInstallDir}\ThirdPartyNotices"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\fonts\*"; DestDir: "{code:GetInstallDir}\fonts"; Flags: ignoreversion recursesubdirs createallsubdirs
; The in-game renderer reuses the shared fonts above.
Source: "{#MyAppPublishRoot}\native-dashboard\win-x64\ztmz_dashboard_overlay.dll"; DestDir: "{code:GetInstallDir}\native-dashboard\win-x64"; Flags: ignoreversion
Source: "{#MyAppPublishRoot}\native-dashboard\win-x64\licenses\volk.txt"; DestDir: "{code:GetInstallDir}\native-dashboard\win-x64\licenses"; Flags: ignoreversion
Source: "{#MyAppPublishRoot}\native-dashboard\win-x86\ztmz_dashboard_overlay.dll"; DestDir: "{code:GetInstallDir}\native-dashboard\win-x86"; Flags: ignoreversion
Source: "{#MyAppPublishRoot}\native-dashboard\win-x86\ztmz_dashboard_injector.exe"; DestDir: "{code:GetInstallDir}\native-dashboard\win-x86"; Flags: ignoreversion
Source: "{#MyAppPublishRoot}\native-dashboard\win-x86\licenses\volk.txt"; DestDir: "{code:GetInstallDir}\native-dashboard\win-x86\licenses"; Flags: ignoreversion
; Native overlays resolve fonts from the separately configured data directory.
Source: "{#MyAppPublishRoot}\fonts\*"; DestDir: "{code:GetZTMZHome}\fonts"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\x64\*"; DestDir: "{code:GetInstallDir}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\x86\*"; DestDir: "{code:GetInstallDir}\x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\Assets\ztmz_ngptool_3.0_help.png"; DestDir: "{code:GetInstallDir}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\Assets\Games\*"; DestDir: "{code:GetInstallDir}\Assets\Games"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\Assets\Monaco\*"; DestDir: "{code:GetInstallDir}\Assets\Monaco"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppPublishRoot}\Assets\PacenoteIcons\*"; DestDir: "{code:GetInstallDir}\Assets\PacenoteIcons"; Flags: ignoreversion recursesubdirs createallsubdirs
; db files
Source: "..\src\ZTMZ.PacenoteTool.Base\*.zdb"; DestDir: "{code:GetZTMZHome}"; Flags: ignoreversion
Source: "..\src\ZTMZ.PacenoteTool.RBR\*.zdb"; DestDir: "{code:GetZTMZHome}\games"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{code:GetInstallDir}\{#MyLauncherExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{code:GetInstallDir}\{#MyLauncherExeName}"; Tasks: desktopicon

[Run]
Filename: "{code:GetInstallDir}\{#MyLauncherExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\{#MyAppName}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "{#MyAppInstallDirValueName}"; ValueData: "{code:GetInstallDir}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "ZTMZHome"; ValueData: "{code:GetZTMZHome}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "AppVersion"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletevalue
