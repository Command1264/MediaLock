#ifndef AppVersion
  #error AppVersion must be supplied by the release script.
#endif
#ifndef BinaryVersion
  #error BinaryVersion must be supplied by the release script.
#endif
#ifndef PayloadPath
  #error PayloadPath must be supplied by the release script.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must be supplied by the release script.
#endif
#ifndef OutputBaseName
  #error OutputBaseName must be supplied by the release script.
#endif

#define MediaLockAppId "f52fcb56-6161-4695-89e7-00ee59069827"

[Setup]
AppId={{{#MediaLockAppId}}
AppName=Media Lock
AppVersion={#AppVersion}
AppVerName=Media Lock {#AppVersion}
AppPublisher=Command1264
AppPublisherURL=https://github.com/Command1264/MediaLock
AppSupportURL=https://github.com/Command1264/MediaLock/issues
AppUpdatesURL=https://github.com/Command1264/MediaLock/releases
AppCopyright=Copyright (C) 2026 Command1264
DefaultDirName={localappdata}\Programs\MediaLock
DefaultGroupName=Media Lock
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.22000
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
CloseApplicationsFilter=MediaLock.exe
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=Media Lock
UninstallDisplayIcon={app}\MediaLock.exe
VersionInfoVersion={#BinaryVersion}
VersionInfoProductVersion={#BinaryVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=Command1264
VersionInfoDescription=Media Lock installer
VersionInfoProductName=Media Lock

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PayloadPath}"; DestDir: "{app}"; DestName: "MediaLock.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Media Lock"; Filename: "{app}\MediaLock.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\MediaLock.exe"; Description: "{cm:LaunchProgram,Media Lock}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\MediaLock.exe"; Parameters: "--uninstall-cleanup"; \
    Flags: runhidden skipifdoesntexist; RunOnceId: "MediaLockStartupCleanup"

[Code]
function TryParseReleaseVersion(
  const VersionText: String;
  var BasePackedVersion: Int64;
  var IsStable: Boolean;
  var PrereleaseNumber: Int64): Boolean;
var
  BaseVersion: String;
  PrereleaseText: String;
  PrereleasePosition: Integer;
begin
  PrereleasePosition := Pos('-rc.', VersionText);
  if PrereleasePosition = 0 then
  begin
    BaseVersion := VersionText;
    IsStable := True;
    PrereleaseNumber := 0;
  end
  else
  begin
    BaseVersion := Copy(VersionText, 1, PrereleasePosition - 1);
    IsStable := False;
    PrereleaseText := Copy(
      VersionText,
      PrereleasePosition + Length('-rc.'),
      MaxInt);
    PrereleaseNumber := StrToInt64Def(PrereleaseText, -1);
  end;

  Result :=
    (IsStable or (PrereleaseNumber >= 0)) and
    StrToVersion(BaseVersion + '.0', BasePackedVersion);
end;

function CompareReleaseVersions(
  const LeftBasePackedVersion: Int64;
  const LeftIsStable: Boolean;
  const LeftPrereleaseNumber: Int64;
  const RightBasePackedVersion: Int64;
  const RightIsStable: Boolean;
  const RightPrereleaseNumber: Int64): Integer;
begin
  Result := ComparePackedVersion(
    LeftBasePackedVersion,
    RightBasePackedVersion);
  if Result <> 0 then
  begin
    exit;
  end;

  if LeftIsStable and not RightIsStable then
  begin
    Result := 1;
    exit;
  end;

  if not LeftIsStable and RightIsStable then
  begin
    Result := -1;
    exit;
  end;

  if LeftPrereleaseNumber < RightPrereleaseNumber then
    Result := -1
  else if LeftPrereleaseNumber > RightPrereleaseNumber then
    Result := 1
  else
    Result := 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  CandidateBasePackedVersion: Int64;
  CandidateIsStable: Boolean;
  CandidatePrereleaseNumber: Int64;
  InstalledBasePackedVersion: Int64;
  InstalledIsStable: Boolean;
  InstalledPrereleaseNumber: Int64;
  InstalledVersion: String;
  UninstallKey: String;
begin
  Result := '';
  UninstallKey :=
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' +
    '{#MediaLockAppId}' + '}_is1';

  if not RegQueryStringValue(
    HKEY_CURRENT_USER,
    UninstallKey,
    'DisplayVersion',
    InstalledVersion) then
  begin
    exit;
  end;

  if not TryParseReleaseVersion(
    InstalledVersion,
    InstalledBasePackedVersion,
    InstalledIsStable,
    InstalledPrereleaseNumber) then
  begin
    Result :=
      'Media Lock cannot verify the installed version (' + InstalledVersion +
      '). Repair or uninstall the existing installation before continuing.';
    exit;
  end;

  if not TryParseReleaseVersion(
    '{#AppVersion}',
    CandidateBasePackedVersion,
    CandidateIsStable,
    CandidatePrereleaseNumber) then
  begin
    Result :=
      'Media Lock cannot verify this installer version. Download a new installer ' +
      'from the official release page.';
    exit;
  end;

  if CompareReleaseVersions(
    InstalledBasePackedVersion,
    InstalledIsStable,
    InstalledPrereleaseNumber,
    CandidateBasePackedVersion,
    CandidateIsStable,
    CandidatePrereleaseNumber) > 0 then
  begin
    Result :=
      'Media Lock ' + InstalledVersion + ' is already installed. This installer ' +
      'contains the older version {#AppVersion}. Install version ' +
      InstalledVersion + ' or a newer release instead; Media Lock will not ' +
      'overwrite a newer installation because doing so could damage its settings.';
  end;
end;
