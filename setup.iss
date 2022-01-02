#define MyAppName "ZTMZ Pacenote Tool"
#define MyAppVersion "2.5.1"
#define MyAppPublisher "ZTMZ Club"
#define MyAppURL "https://gitee.com/ztmz/ztmz_pacenote"
#define MyAppExeName "ZTMZ.PacenoteTool.exe"

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedToDownload: Boolean;
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
      if StartsText('Microsoft.WindowsDesktop.App 6', Lines[i]) then
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

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
  begin
    Log(Format('Successfully downloaded file to {tmp}: %s', [FileName]));
  end;
  Result := True;
end;

procedure InitializeWizard;
begin
  NeedToDownload:= (not CheckDotNetInstalled);
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  fullFileName: String;
  ResultCode: Integer;
begin
  if (CurPageID = wpReady) and NeedToDownload then begin
    DownloadPage.Clear;
    DownloadPage.Add('https://download.visualstudio.microsoft.com/download/pr/bf058765-6f71-4971-aee1-15229d8bfb3e/c3366e6b74bec066487cd643f915274d/windowsdesktop-runtime-6.0.1-win-x64.exe', 'dotnet6.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        { install it ! }
        fullFileName:= ExpandConstant('{tmp}\dotnet6.exe');
        Result := Exec(fullFileName, '/install /passive', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end else
    Result := True;
end;

[Setup]
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
DefaultDirName={autopf}\ztmz_pacenotetool
DisableProgramGroupPage=yes
; Uncomment the following line to run in non administrative install mode (install for current user only.)
;PrivilegesRequired=lowest
Compression=lzma
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion=2.5.1.0
;PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\*"; Excludes: "*.csv"; DestDir: "{app}"; Flags: ignoreversion
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\*.json"; DestDir: "{commondocs}\ZTMZClub"; Flags: ignoreversion uninsneveruninstall
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\*.csv"; DestDir: "{commondocs}\ZTMZClub"; Flags: ignoreversion uninsneveruninstall
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\codrivers\*"; DestDir: "{commondocs}\ZTMZClub\codrivers"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\profiles\*"; DestDir: "{commondocs}\ZTMZClub\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\lang\*"; DestDir: "{commondocs}\ZTMZClub\lang"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent