; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!
#include "setup.iss"
[Setup]
OutputBaseFilename=ZTMZClub_PacenoteTool_Installer_{#MyAppVersion}

[Files]
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\*"; Excludes: "*.csv,XAudio*"; DestDir: "{app}"; Flags: ignoreversion
