#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef ReleaseTag
  #define ReleaseTag "v" + AppVersion
#endif

[Setup]
AppId=atoshdotdev.Tessel
AppName=Tessel
AppVersion={#AppVersion}
AppVerName=Tessel {#ReleaseTag}
AppPublisher=Andrej Tosheski
AppPublisherURL=https://github.com/atoshdotdev/tessel
AppSupportURL=https://github.com/atoshdotdev/tessel/issues
DefaultDirName={autopf}\Tessel
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
AppMutex=Local\Tessel.WindowManager
CloseApplications=no
UninstallDisplayIcon={app}\Tessel.exe
SetupIconFile=..\assets\tessel-logo.ico
OutputDir=..\artifacts
OutputBaseFilename=Tessel-{#ReleaseTag}-win-x64-setup
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#ReleaseTag}
WizardStyle=modern

[Files]
Source: "..\src\Tessel.App\bin\Release\net10.0-windows\win-x64\publish\Tessel.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\Tessel"; Filename: "{app}\Tessel.exe"

[Run]
Filename: "{app}\Tessel.exe"; Description: "Launch Tessel"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;

  // Use Tessel's config creation and migration logic on both fresh installs and upgrades.
  if (not ExecAsOriginalUser(ExpandConstant('{app}\Tessel.exe'), 'init-config',
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
    (ResultCode <> 0) then
  begin
    Log(Format('Tessel init-config failed (code %d).', [ResultCode]));
    SuppressibleMsgBox('Tessel was installed, but setup could not initialize or validate the configuration. ' +
      'Check the configuration with Tessel.exe check-config.', mbError, MB_OK, IDOK);
  end;
end;
