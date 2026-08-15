#ifndef AppVersion
  #define AppVersion "0.2.1"
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
WizardImageFile={#RepositoryRoot}\installer\assets\wizard-large.png
WizardImageFileDynamicDark={#RepositoryRoot}\installer\assets\wizard-large.png
WizardImageBackColor=#090D14
WizardImageBackColorDynamicDark=#090D14
WizardSmallImageFile={#RepositoryRoot}\src\AchievementRelay.App\Assets\AchievementRelay.png
WizardSmallImageFileDynamicDark={#RepositoryRoot}\src\AchievementRelay.App\Assets\AchievementRelay.png
WizardSmallImageBackColor=#101722
WizardSmallImageBackColorDynamicDark=#101722
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
Compression=lzma2/max
SolidCompression=yes
AllowCancelDuringInstall=no
RestartIfNeededByRun=no
VersionInfoVersion={#MsixVersion}
VersionInfoCompany=Achievement Relay
VersionInfoDescription=Achievement Relay gaming companion installer
VersionInfoProductName=Achievement Relay
VersionInfoProductVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Player options:"; Flags: checkedonce

[Files]
Source: "{#X64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#Arm64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#RepositoryRoot}\scripts\Install.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#RepositoryRoot}\scripts\Protect-InstallerSetup.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#ifexist CertificatePath
Source: "{#CertificatePath}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#endif

[Code]
var
  SetupChoicePage: TInputOptionWizardPage;
  CredentialsPage: TInputQueryWizardPage;
  OpenXblButton: TNewButton;
  DiscordGuideButton: TNewButton;

function SetEnvironmentVariable(lpName, lpValue: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

procedure OpenOpenXbl(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', 'https://xbl.io/profile', '', '', SW_SHOWNORMAL,
    ewNoWait, ResultCode);
end;

procedure OpenDiscordGuide(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open',
    'https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks',
    '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure InitializeWizard();
var
  ButtonTop: Integer;
begin
  WizardForm.WelcomeLabel1.Caption := 'ENTER THE ACHIEVEMENT RELAY';
  WizardForm.WelcomeLabel2.Caption :=
    'Sync Xbox achievements to Discord from a focused Windows gaming companion.' + #13#10 + #13#10 +
    'Setup selects the correct x64 or Arm64 package. You can connect OpenXBL and Discord now, or skip that step and use Guided setup later.';

  SetupChoicePage := CreateInputOptionPage(wpWelcome,
    'CONNECT YOUR RELAY',
    'Configure the Xbox-to-Discord link now or later.',
    'Choose one option, then select Next.', True, False);
  SetupChoicePage.Add('&Connect OpenXBL and Discord now (recommended)');
  SetupChoicePage.Add('&Skip — I will do this later in Guided setup');
  SetupChoicePage.Values[0] := True;

  CredentialsPage := CreateInputQueryPage(SetupChoicePage.ID,
    'PLAYER CONNECTIONS',
    'Add the two private values used by Achievement Relay.',
    'Setup encrypts both values for this Windows user before the app receives them. On first launch, the app saves fresh encrypted settings before deleting the one-time handoff and running connection tests.');
  CredentialsPage.Add('&OpenXBL API key:', True);
  CredentialsPage.Add('&Discord webhook URL:', True);
  CredentialsPage.Edits[0].MaxLength := 512;
  CredentialsPage.Edits[1].MaxLength := 2048;

  ButtonTop := CredentialsPage.Edits[1].Top + CredentialsPage.Edits[1].Height + ScaleY(12);

  OpenXblButton := TNewButton.Create(CredentialsPage);
  OpenXblButton.Parent := CredentialsPage.Surface;
  OpenXblButton.Caption := 'Get OpenXBL key';
  OpenXblButton.Left := 0;
  OpenXblButton.Top := ButtonTop;
  OpenXblButton.Width := ScaleX(120);
  OpenXblButton.OnClick := @OpenOpenXbl;

  DiscordGuideButton := TNewButton.Create(CredentialsPage);
  DiscordGuideButton.Parent := CredentialsPage.Surface;
  DiscordGuideButton.Caption := 'Discord webhook guide';
  DiscordGuideButton.Left := OpenXblButton.Left + OpenXblButton.Width + ScaleX(8);
  DiscordGuideButton.Top := ButtonTop;
  DiscordGuideButton.Width := ScaleX(145);
  DiscordGuideButton.OnClick := @OpenDiscordGuide;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = CredentialsPage.ID) and SetupChoicePage.Values[1];
end;

function ContainsWhitespaceOrControl(const Value: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    if Ord(Value[Index]) <= 32 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsLikelyDiscordWebhook(const Value: String): Boolean;
var
  Normalized: String;
  IsDiscordHost: Boolean;
begin
  Normalized := Lowercase(Trim(Value));
  IsDiscordHost :=
    (Pos('https://discord.com/api/', Normalized) = 1) or
    (Pos('https://ptb.discord.com/api/', Normalized) = 1) or
    (Pos('https://canary.discord.com/api/', Normalized) = 1) or
    (Pos('https://discordapp.com/api/', Normalized) = 1);
  Result := IsDiscordHost and (Pos('/webhooks/', Normalized) > 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ApiKey: String;
  WebhookUrl: String;
begin
  Result := True;
  if CurPageID <> CredentialsPage.ID then
    Exit;

  ApiKey := Trim(CredentialsPage.Values[0]);
  WebhookUrl := Trim(CredentialsPage.Values[1]);

  if (Length(ApiKey) = 0) or (Length(ApiKey) > 512) or
     ContainsWhitespaceOrControl(ApiKey) then
  begin
    MsgBox('Paste a valid OpenXBL API key, or go back and choose Skip.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if not IsLikelyDiscordWebhook(WebhookUrl) then
  begin
    MsgBox('Paste a complete HTTPS Discord channel webhook URL, or go back and choose Skip.',
      mbError, MB_OK);
    Result := False;
  end;
end;

function GetLegacyPendingSetupPath(): String;
begin
  Result := ExpandConstant(
    '{localappdata}\AchievementRelay\pending-installer-setup.json');
end;

function GetPendingSetupPath(): String;
var
  UserProfile: String;
begin
  UserProfile := Trim(GetEnv('USERPROFILE'));
  if UserProfile = '' then
  begin
    Result := GetLegacyPendingSetupPath();
    Exit;
  end;

  Result := AddBackslash(UserProfile) +
    '.achievement-relay\pending-installer-setup.json';
end;

function CreateProtectedPendingSetup(): Boolean;
var
  PendingPath: String;
  Parameters: String;
  PowerShellPath: String;
  ResultCode: Integer;
begin
  Result := False;
  PendingPath := GetPendingSetupPath();
  DeleteFile(PendingPath);
  DeleteFile(GetLegacyPendingSetupPath());

  try
    if not SetEnvironmentVariable('ACHIEVEMENT_RELAY_OPENXBL_KEY',
      Trim(CredentialsPage.Values[0])) then
      Exit;
    if not SetEnvironmentVariable('ACHIEVEMENT_RELAY_DISCORD_WEBHOOK',
      Trim(CredentialsPage.Values[1])) then
      Exit;

    PowerShellPath := ExpandConstant(
      '{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{tmp}\Protect-InstallerSetup.ps1') +
      '" -OutputFile "' + PendingPath + '"';
    Result := Exec(PowerShellPath, Parameters, ExpandConstant('{tmp}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and
      FileExists(PendingPath);
  finally
    SetEnvironmentVariable('ACHIEVEMENT_RELAY_OPENXBL_KEY', '');
    SetEnvironmentVariable('ACHIEVEMENT_RELAY_DISCORD_WEBHOOK', '');
    CredentialsPage.Values[0] := '';
    CredentialsPage.Values[1] := '';
  end;
end;

procedure DeletePendingSetup();
begin
  DeleteFile(GetPendingSetupPath());
  DeleteFile(GetLegacyPendingSetupPath());
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
    DeletePendingSetup();
    if SetupChoicePage.Values[0] and not CreateProtectedPendingSetup() then
      RaiseException('Setup could not prepare the optional account settings. No credentials were installed.');

    WizardForm.StatusLabel.Caption := 'Deploying the Xbox achievement relay...';
    PowerShellPath := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
    ErrorFile := ExpandConstant('{tmp}\AchievementRelay-InstallError.txt');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{tmp}\Install.ps1') + '" -ErrorFile "' + ErrorFile + '"';
    if WizardIsTaskSelected('desktopicon') then
      Parameters := Parameters + ' -CreateDesktopShortcut';

    if not Exec(PowerShellPath, Parameters, ExpandConstant('{tmp}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
    begin
      DeletePendingSetup();
      RaiseException('Windows could not start the package installer: ' +
        SysErrorMessage(ResultCode));
    end;

    if ResultCode <> 0 then
    begin
      DeletePendingSetup();
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
