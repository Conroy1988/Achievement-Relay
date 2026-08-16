#ifndef AppVersion
  #define AppVersion "0.4.0"
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
WizardImageBackColor=#07090A
WizardImageBackColorDynamicDark=#07090A
WizardSmallImageFile={#RepositoryRoot}\src\AchievementRelay.App\Assets\AchievementRelay.png
WizardSmallImageFileDynamicDark={#RepositoryRoot}\src\AchievementRelay.App\Assets\AchievementRelay.png
WizardSmallImageBackColor=#0D1012
WizardSmallImageBackColorDynamicDark=#0D1012
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
Source: "{#RepositoryRoot}\installer\assets\CRNY - Relay Online.mp3"; Flags: dontcopy noencryption
Source: "{#X64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#Arm64Package}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#RepositoryRoot}\scripts\Install.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#RepositoryRoot}\scripts\Protect-InstallerSetup.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#ifexist CertificatePath
Source: "{#CertificatePath}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
#endif

[Code]
const
  MusicAlias = 'AchievementRelayMusic';
  MusicFileName = 'CRNY - Relay Online.mp3';
  SoundCloudUrl = 'https://soundcloud.com/daniel-conroy-224318319/crny-relay-online';
  MusicBackendNone = 0;
  MusicBackendWindowsMediaPlayer = 1;
  MusicBackendMci = 2;

var
  SetupChoicePage: TInputOptionWizardPage;
  CredentialsPage: TInputQueryWizardPage;
  OpenXblButton: TNewButton;
  DiscordGuideButton: TNewButton;
  MusicButton: TNewButton;
  SoundCloudButton: TNewButton;
  MusicPath: String;
  MusicPlayer: Variant;
  MusicBackend: Integer;
  MusicPlaying: Boolean;
  UpdateMode: Boolean;

function SetEnvironmentVariable(lpName, lpValue: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

function MciSendString(Command, ReturnBuffer: String;
  ReturnLength: Cardinal; CallbackWindow: HWND): DWORD;
  external 'mciSendStringW@winmm.dll stdcall';

procedure SetMusicUnavailable();
begin
  MusicButton.Caption := 'Music unavailable';
  MusicButton.Enabled := False;
end;

procedure StopInstallerMusic();
begin
  if MusicBackend = MusicBackendWindowsMediaPlayer then
  begin
    try
      MusicPlayer.controls.stop;
      MusicPlayer.close;
    except
      Log('Windows Media Player soundtrack shutdown failed safely.');
    end;
    MusicPlayer := Unassigned;
  end
  else if MusicBackend = MusicBackendMci then
  begin
    MciSendString('stop ' + MusicAlias, '', 0, 0);
    MciSendString('close ' + MusicAlias, '', 0, 0);
  end;

  MusicBackend := MusicBackendNone;
  MusicPlaying := False;
end;

function TryStartWindowsMediaPlayer(): Boolean;
var
  Controls: Variant;
  Settings: Variant;
begin
  Result := False;
  try
    MusicPlayer := CreateOleObject('WMPlayer.OCX');
    Settings := MusicPlayer.settings;
    Controls := MusicPlayer.controls;
    Settings.autoStart := False;
    Settings.enableErrorDialogs := False;
    Settings.volume := 10;
    Settings.setMode('loop', True);

    if Settings.volume <> 10 then
      RaiseException('Windows Media Player did not retain the safe soundtrack volume.');

    MusicPlayer.URL := MusicPath;
    Controls.play;

    MusicBackend := MusicBackendWindowsMediaPlayer;
    MusicPlaying := True;
    Result := True;
    Log('Installer soundtrack started through Windows Media Player at 10% volume.');
  except
    Log('Windows Media Player soundtrack initialization was unavailable.');
    try
      MusicPlayer.controls.stop;
      MusicPlayer.close;
    except
    end;
    MusicPlayer := Unassigned;
    MusicBackend := MusicBackendNone;
    MusicPlaying := False;
  end;
end;

function TryStartMci(): Boolean;
var
  CommandResult: DWORD;
begin
  Result := False;
  CommandResult := MciSendString('open "' + MusicPath +
    '" type MPEGVideo alias ' + MusicAlias, '', 0, 0);
  if CommandResult <> 0 then
  begin
    Log('MCI soundtrack open failed with code ' + IntToStr(CommandResult) + '.');
    Exit;
  end;

  CommandResult := MciSendString(
    'setaudio ' + MusicAlias + ' volume to 100', '', 0, 0);
  if CommandResult <> 0 then
  begin
    Log('MCI soundtrack safe-volume command failed with code ' +
      IntToStr(CommandResult) + '.');
    MciSendString('close ' + MusicAlias, '', 0, 0);
    Exit;
  end;

  CommandResult := MciSendString('play ' + MusicAlias + ' repeat', '', 0, 0);
  if CommandResult <> 0 then
  begin
    Log('MCI soundtrack playback failed with code ' + IntToStr(CommandResult) + '.');
    MciSendString('close ' + MusicAlias, '', 0, 0);
    Exit;
  end;

  MusicBackend := MusicBackendMci;
  MusicPlaying := True;
  Result := True;
  Log('Installer soundtrack started through the MCI fallback at 10% volume.');
end;

procedure MarkMusicPlaying();
begin
  MusicButton.Caption := 'Pause music';
  MusicButton.Enabled := True;
end;

function ResumeInstallerMusic(): Boolean;
var
  CommandResult: DWORD;
begin
  Result := False;
  try
    if MusicBackend = MusicBackendWindowsMediaPlayer then
    begin
      MusicPlayer.controls.play;
      Result := True;
    end
    else if MusicBackend = MusicBackendMci then
    begin
      CommandResult := MciSendString('resume ' + MusicAlias, '', 0, 0);
      Result := CommandResult = 0;
      if not Result then
        Log('MCI soundtrack resume failed with code ' + IntToStr(CommandResult) + '.');
    end;
  except
    Log('Installer soundtrack resume failed safely.');
    Result := False;
  end;

  if Result then
  begin
    MusicPlaying := True;
    MarkMusicPlaying();
  end;
end;

function PauseInstallerMusic(): Boolean;
var
  CommandResult: DWORD;
begin
  Result := False;
  try
    if MusicBackend = MusicBackendWindowsMediaPlayer then
    begin
      MusicPlayer.controls.pause;
      Result := True;
    end
    else if MusicBackend = MusicBackendMci then
    begin
      CommandResult := MciSendString('pause ' + MusicAlias, '', 0, 0);
      Result := CommandResult = 0;
      if not Result then
        Log('MCI soundtrack pause failed with code ' + IntToStr(CommandResult) + '.');
    end;
  except
    Log('Installer soundtrack pause failed safely.');
    Result := False;
  end;

  if Result then
  begin
    MusicPlaying := False;
    MusicButton.Caption := 'Play music';
  end;
end;

procedure StartInstallerMusic();
begin
  SetMusicUnavailable();
  MusicBackend := MusicBackendNone;
  MusicPlaying := False;
  MusicPlayer := Unassigned;
  try
    ExtractTemporaryFile(MusicFileName);
    MusicPath := ExpandConstant('{tmp}\') + MusicFileName;
    if not FileExists(MusicPath) then
    begin
      Log('Installer soundtrack extraction did not produce the expected file.');
      Exit;
    end;

    if TryStartWindowsMediaPlayer() then
      MarkMusicPlaying()
    else if TryStartMci() then
      MarkMusicPlaying()
    else
      SetMusicUnavailable();
  except
    Log('Installer soundtrack initialization failed safely.');
    StopInstallerMusic();
    SetMusicUnavailable();
  end;
end;

procedure ToggleInstallerMusic(Sender: TObject);
var
  ControlSucceeded: Boolean;
begin
  if MusicBackend = MusicBackendNone then
    Exit;

  if MusicPlaying then
    ControlSucceeded := PauseInstallerMusic()
  else
    ControlSucceeded := ResumeInstallerMusic();

  if not ControlSucceeded then
  begin
    StopInstallerMusic();
    SetMusicUnavailable();
  end;
end;

procedure OpenSoundCloud(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', SoundCloudUrl, '', '', SW_SHOWNORMAL,
    ewNoWait, ResultCode);
end;

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
#ifdef ForceUpdateMode
  UpdateMode := True;
#else
  UpdateMode := CompareText(Trim(ExpandConstant('{param:UPDATE|0}')), '1') = 0;
#endif
  if UpdateMode then
  begin
    WizardForm.Caption := 'Update - Achievement Relay';
    WizardForm.WelcomeLabel1.Caption := 'UPGRADE THE ACHIEVEMENT RELAY';
#ifdef ForceUpdateMode
    WizardForm.WelcomeLabel2.Caption :=
      'This signed one-time recovery installer replaces the updater affected by the Windows version-resource padding issue.' + #13#10 + #13#10 +
      'It keeps your encrypted connections, settings, achievement history, startup preference and desktop shortcut. After this bridge opens Achievement Relay, the verified successor will download and open automatically.';
#else
    WizardForm.WelcomeLabel2.Caption :=
      'Achievement Relay {#AppVersion} has been downloaded from the official GitHub release and verified by the app.' + #13#10 + #13#10 +
      'Update keeps your encrypted connections, settings, achievement history, startup preference and desktop shortcut. Select Next to review, then Update to apply it.';
#endif
  end
  else
  begin
    WizardForm.WelcomeLabel1.Caption := 'ENTER THE ACHIEVEMENT RELAY';
    WizardForm.WelcomeLabel2.Caption :=
      'Relay new Xbox and Steam achievements to Discord from one focused Windows gaming companion.' + #13#10 + #13#10 +
      'Setup selects the correct x64 or Arm64 package. Steam works locally without an API key. You can add Discord and optional OpenXBL now, or use the app''s step-by-step Setup later.';
  end;

  SetupChoicePage := CreateInputOptionPage(wpWelcome,
    'CONNECT YOUR RELAY',
    'Configure Discord and your achievement sources now or later.',
    'Choose one option, then select Next.', True, False);
  SetupChoicePage.Add('&Connect Discord now; add OpenXBL optionally (recommended)');
  SetupChoicePage.Add('&Skip — I will do this later in the app');
  SetupChoicePage.Values[0] := True;

  CredentialsPage := CreateInputQueryPage(SetupChoicePage.ID,
    'PLAYER CONNECTIONS',
    'Add Discord and, if wanted, Xbox through OpenXBL.',
    'The Discord webhook is required here. The OpenXBL key is optional because Steam monitoring is local and keyless. Setup encrypts supplied values for this Windows user before the app receives them.');
  CredentialsPage.Add('&OpenXBL API key (optional — leave blank for Steam only):', True);
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

  MusicButton := TNewButton.Create(WizardForm);
  MusicButton.Parent := WizardForm;
  MusicButton.Caption := 'Music unavailable';
  MusicButton.Left := ScaleX(16);
  MusicButton.Top := WizardForm.NextButton.Top;
  MusicButton.Width := ScaleX(100);
  MusicButton.Height := WizardForm.NextButton.Height;
  MusicButton.Anchors := [akLeft, akBottom];
  MusicButton.Enabled := False;
  MusicButton.OnClick := @ToggleInstallerMusic;

  SoundCloudButton := TNewButton.Create(WizardForm);
  SoundCloudButton.Parent := WizardForm;
  SoundCloudButton.Caption := 'CRNY on SoundCloud';
  SoundCloudButton.Left := MusicButton.Left + MusicButton.Width + ScaleX(8);
  SoundCloudButton.Top := MusicButton.Top;
  SoundCloudButton.Width := ScaleX(145);
  SoundCloudButton.Height := MusicButton.Height;
  SoundCloudButton.Anchors := [akLeft, akBottom];
  SoundCloudButton.OnClick := @OpenSoundCloud;

  StartInstallerMusic();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if UpdateMode then
    Result := (PageID = SetupChoicePage.ID) or
      (PageID = CredentialsPage.ID) or (PageID = wpSelectTasks)
  else
    Result := (PageID = CredentialsPage.ID) and SetupChoicePage.Values[1];
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if not UpdateMode then
    Exit;

  if CurPageID = wpReady then
    WizardForm.NextButton.Caption := '&Update'
  else if CurPageID <> wpFinished then
    WizardForm.NextButton.Caption := '&Next';
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

  if (Length(ApiKey) > 0) and
     ((Length(ApiKey) > 512) or ContainsWhitespaceOrControl(ApiKey)) then
  begin
    MsgBox('The optional OpenXBL key is not valid. Correct it, leave it blank for Steam only, or go back and choose Skip.',
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
    if not UpdateMode then
    begin
      DeletePendingSetup();
      if SetupChoicePage.Values[0] and not CreateProtectedPendingSetup() then
        RaiseException('Setup could not prepare the optional account settings. No credentials were installed.');
    end;

    if UpdateMode then
      WizardForm.StatusLabel.Caption := 'Applying the verified Achievement Relay update...'
    else
      WizardForm.StatusLabel.Caption := 'Deploying the Xbox and Steam achievement relay...';
    PowerShellPath := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
    ErrorFile := ExpandConstant('{tmp}\AchievementRelay-InstallError.txt');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{tmp}\Install.ps1') + '" -ErrorFile "' + ErrorFile + '"';
    if UpdateMode then
      Parameters := Parameters + ' -Update -PreserveDesktopShortcut'
    else if WizardIsTaskSelected('desktopicon') then
      Parameters := Parameters + ' -CreateDesktopShortcut';

    if not Exec(PowerShellPath, Parameters, ExpandConstant('{tmp}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
    begin
      if not UpdateMode then
        DeletePendingSetup();
      RaiseException('Windows could not start the package installer: ' +
        SysErrorMessage(ResultCode));
    end;

    if ResultCode <> 0 then
    begin
      if not UpdateMode then
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

procedure DeinitializeSetup();
begin
  StopInstallerMusic();
end;
