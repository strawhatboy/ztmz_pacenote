; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#include "setup.iss"

[Setup]
OutputBaseFilename=ZTMZClub_PacenoteTool_Dev_Installer_{#MyAppVersion}

[Files]
; More files for Dev version
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\Python38\*"; DestDir: "{app}\Python38"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ZTMZ.PacenoteTool\bin\Release\net6.0-windows\speech_model\*"; DestDir: "{app}\speech_model"; Flags: ignoreversion recursesubdirs createallsubdirs
