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
VersionInfoProductVersion={#AppVersion}
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
