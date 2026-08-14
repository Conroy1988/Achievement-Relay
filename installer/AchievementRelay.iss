#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif

#ifndef MsixVersion
  #define MsixVersion AppVersion + ".0"
#endif

#ifndef PackageDirectory
  #define PackageDirectory "..\artifacts"
#endif

#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts"
#endif

#ifndef RepositoryRoot
  #define RepositoryRoot ".."
#endif

#define X64Package PackageDirectory + "\AchievementRelay_" + MsixVersion + "_x64.msix"
#define Arm64Package PackageDirectory + "\AchievementRelay_" + MsixVersion + "_arm64.msix"
#define CertificatePath PackageDirectory + "\AchievementRelay.Development.cer"

[Setup]
AppId=AchievementRelay.Setup
AppName=Achievement Relay
AppVersion={#AppVersion}
AppPublisher=Achievement Relay contributors
AppPublisherURL=https://github.com/Conroy1988/Achievement-Relay
AppSupportURL=https://github.com/Conroy1988/Achievement-Relay/issues
AppUpdatesURL=https://github.com/Conroy1988/Achievement-Relay/releases/latest
DefaultDirName={tmp}\AchievementRelay
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
ArchitecturesAllowed=win64
MinVersion=10.0.19041
OutputDir={#OutputDirectory}
OutputBaseFilename=AchievementRelay_Setup
SetupIconFile={#RepositoryRoot}\src\AchievementRelay.App\Assets\AchievementRelay.ico
WizardStyle=modern dynamic
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
Compression=lzma2/max
SolidCompression=yes
AllowCancelDuringInstall=no
RestartIfNeededByRun=no
VersionInfoVersion={#MsixVersion}
VersionInfoCompany=Achievement Relay
VersionInfoDescription=Achievement Relay installer
VersionInfoProductName=Achievement Relay
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#X64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#Arm64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#RepositoryRoot}\scripts\Install.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#ifexist CertificatePath
Source: "{#CertificatePath}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#endif

[Code]
procedure InitializeWizard();
begin
  WizardForm.WelcomeLabel2.Caption :=
    'Setup selects the correct x64 or Arm64 package, installs Achievement Relay for your Windows account, and opens Guided setup.' + #13#10 + #13#10 +
    'Alpha builds may request administrator approval once to trust the package certificate.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorFile: String;
  ErrorText: AnsiString;
  Parameters: String;
  PowerShellPath: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Installing the Achievement Relay Windows package...';
    PowerShellPath := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
    ErrorFile := ExpandConstant('{tmp}\AchievementRelay-InstallError.txt');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{tmp}\Install.ps1') + '" -ErrorFile "' + ErrorFile + '"';

    if not Exec(PowerShellPath, Parameters, ExpandConstant('{tmp}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
    begin
      RaiseException('Windows could not start the package installer: ' +
        SysErrorMessage(ResultCode));
    end;

    if ResultCode <> 0 then
    begin
      ErrorText := '';
      if FileExists(ErrorFile) then
        LoadStringFromFile(ErrorFile, ErrorText);

      if ErrorText = '' then
        ErrorText := 'Windows installer exit code: ' + IntToStr(ResultCode) + '.';

      RaiseException('Achievement Relay could not be installed.' + #13#10 + #13#10 +
        ErrorText);
    end;
  end;
end;
