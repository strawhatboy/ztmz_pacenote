; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!
#include "setup.iss"
[Setup]
OutputBaseFilename=ZTMZClub_PacenoteTool_Installer_{#MyAppVersion}

[Files]
Source: "target\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64\*"; Excludes: "*.csv,XAudio*"; DestDir: "{code:GetInstallDir}"; Flags: ignoreversion
