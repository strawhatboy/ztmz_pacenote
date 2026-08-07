#define MyAppDisplayName "ZTMZ Pacenote Tool - ACR Experimental"
#define MyAppPublishRoot "target_acr_experimental\publish\ZTMZ.PacenoteTool.WpfGUI\release_win-x64"
#define MyAppReleaseSuffix "-acr-experimental"
#define MyAppId "{{F3C27DA8-3DE5-4F97-AB35-16C6BB98E8E1}"
#define MyAppInstallFolder "ztmzclub_pacenotetool_acr_experimental"

#include "setup.iss"

[Setup]
OutputBaseFilename=ZTMZClub_PacenoteTool_ACR_Experimental_Installer_{#MyAppVersion}

[Files]
Source: "{#MyAppPublishRoot}\*"; Excludes: "*.csv,XAudio*"; DestDir: "{code:GetInstallDir}"; Flags: ignoreversion
