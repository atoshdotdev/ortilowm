#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef ReleaseTag
  #define ReleaseTag "v" + AppVersion
#endif

[Setup]
AppId=atoshdotdev.Ortilo
AppName=Ortilo
AppVersion={#AppVersion}
AppVerName=Ortilo {#ReleaseTag}
AppPublisher=Andrej Tosheski
AppPublisherURL=https://github.com/atoshdotdev/ortilo
AppSupportURL=https://github.com/atoshdotdev/ortilo/issues
DefaultDirName={autopf}\Ortilo
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
AppMutex=Local\Ortilo.WindowManager
CloseApplications=no
UninstallDisplayIcon={app}\Ortilo.exe
SetupIconFile=..\assets\logo.ico
OutputDir=..\artifacts
OutputBaseFilename=Ortilo-{#ReleaseTag}-win-x64-setup
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#ReleaseTag}
WizardStyle=modern

[Files]
Source: "..\src\Ortilo.App\bin\Release\net10.0-windows\win-x64\publish\Ortilo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\Ortilo"; Filename: "{app}\Ortilo.exe"

[Run]
Filename: "{app}\Ortilo.exe"; Description: "Launch Ortilo"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;

  // Use Ortilo's config creation and migration logic on both fresh installs and upgrades.
  if (not ExecAsOriginalUser(ExpandConstant('{app}\Ortilo.exe'), 'init-config',
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
    (ResultCode <> 0) then
  begin
    Log(Format('Ortilo init-config failed (code %d).', [ResultCode]));
    SuppressibleMsgBox('Ortilo was installed, but setup could not initialize or validate the configuration. ' +
      'Check the configuration with Ortilo.exe check-config.', mbError, MB_OK, IDOK);
  end;
end;
