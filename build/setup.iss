#define MyAppName "ZTMZ Next Generation Pacenote Tool"
#define MyAppVersion "2.99.99.33"
#define MyAppPublisher "ZTMZ Club"
#define MyAppURL "https://gitee.com/ztmz/ztmz_pacenote"
#define MyAppExeName "ZTMZ.PacenoteTool.WpfGUI.exe"
#define FolderName "ZTMZClub_nextgen"

[Components]
Name: "ztmz_ngptool"; Description: "ZTMZ Next Generation Pacenote Tool"; Types: full compact custom; Flags: fixed

; selectable components
Name: "webview2"; Description: "Microsoft Edge WebView2 Runtime"; Types: full custom; ExtraDiskSpaceRequired: 50000000

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedToDownload: Boolean;
  DirPage: TInputDirWizardPage;
  HiddenPage: TInputDirWizardPage;
  HiddenPage2: TInputDirWizardPage;
  InstallWebView2: Boolean;
  WebView2InstallerDownloaded: Boolean;

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

function CheckDotNetInstalled: Boolean;
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
    Lines.Text := ExecStdout;
    { find dotnet6 desktop }
    for i:= 0 to Lines.Count-1 do
    begin
{ Force version 8.* }
      if StartsText('Microsoft.WindowsDesktop.App 8', Lines[i]) then
      begin
        { found }
        Result:= True;
        Exit;
      end;
    end;
    Lines.Free;
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

  { assign default directories for the items from the previously stored data; if }
  { there are no data stored from the previous installation, use default folders }
  { of your choice }
  DirPage.Values[0] := GetPreviousData('InstallDir', ExpandConstant('{autopf}\ztmzclub_pacenotetool_nextgen'));
  try
    DirPage.Values[1] := GetPreviousData('ZTMZHome', ExpandConstant('{userdocs}\My Games\{#FolderName}'));
  except
    { damn, some user has their documents folder interrputed, try appdata }
    DirPage.Values[1] := GetPreviousData('ZTMZHome', ExpandConstant('{autoappdata}\{#FolderName}'));
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
  if (CurPageID = wpReady) then
  begin
    DownloadPage.Clear;

    // Check for .NET runtime
    NeedToDownload := (not CheckDotNetInstalled);
    if NeedToDownload then
    begin
      DownloadPage.Add('https://gitee.com/ztmz/dotnet-runtimes/releases/download/desktop-8.0.10/windowsdesktop-runtime-8.0.10-win-x64.exe', 'dotnet8.exe', '');
    end;

    // Check for WebView2
    InstallWebView2 := WizardIsComponentSelected('webview2') and not IsWebView2Installed();
    if InstallWebView2 then
    begin
      WebView2Installer := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
    end;

    if NeedToDownload or InstallWebView2 then
    begin
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          WebView2InstallerDownloaded := True; // Add this line

          // Install .NET runtime
          if NeedToDownload then
          begin
            fullFileName := ExpandConstant('{tmp}\dotnet8.exe');
            Result := Exec(fullFileName, '/install /passive', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            if not Result then
            begin
              RaiseException('Failed to install .NET runtime.');
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
AppId={{9EF39FA9-58C6-40E7-B957-75EAC73369E7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
;AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\ztmzclub_pacenotetool_nextgen
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
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\codrivers\*"; DestDir: "{code:GetZTMZHome}\codrivers"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\profiles\*"; DestDir: "{code:GetZTMZHome}\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\lang\*"; DestDir: "{code:GetZTMZHome}\lang"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\*"; Excludes:"*.json"; DestDir: "{code:GetInstallDir}\games"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\games\*.json"; Excludes:"*.deps.json"; DestDir: "{code:GetZTMZHome}\games"; Flags: ignoreversion recursesubdirs createallsubdirs
; Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\runtimes\*"; DestDir: "{code:GetInstallDir}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\dashboards\*"; DestDir: "{code:GetZTMZHome}\dashboards"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\fonts\*"; DestDir: "{code:GetZTMZHome}\fonts"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\x64\*"; DestDir: "{code:GetInstallDir}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\x86\*"; DestDir: "{code:GetInstallDir}\x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\Assets\ztmz_ngptool_3.0_help.png"; DestDir: "{code:GetInstallDir}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
; db files
Source: "..\src\ZTMZ.PacenoteTool.Base\*.zdb"; DestDir: "{code:GetZTMZHome}"; Flags: ignoreversion
Source: "..\src\ZTMZ.PacenoteTool.RBR\*.zdb"; DestDir: "{code:GetZTMZHome}\games"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{code:GetInstallDir}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{code:GetInstallDir}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{code:GetInstallDir}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\{#MyAppName}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{code:GetInstallDir}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "ZTMZHome"; ValueData: "{code:GetZTMZHome}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "AppVersion"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletevalue
