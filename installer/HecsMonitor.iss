; ECO / HECS Live Compression Monitor 설치 스크립트 (Inno Setup 6)
; 직접 컴파일하지 말고 저장소 루트의 build-installer.ps1을 실행하세요.
; build-installer.ps1이 /DBrand, /DAppVersion, /DSourceDir, /DOutputDir를 넘겨 줍니다.

; 제품 이름 (ECO | HECS): 설치 이름·폴더·바로 가기·패키지 파일 이름에 쓰인다
#ifndef Brand
  #define Brand "ECO"
#endif
#if Brand == "HECS"
  #define FilePrefix "Hecs"
  #define OtherShortName "ECO Monitor"
#else
  #define FilePrefix "Eco"
  #define OtherShortName "HECS Monitor"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName Brand + " Live Compression Monitor"
#define AppShortName Brand + " Monitor"
#define AppExe "HecsMonitor.exe"

[Setup]
; AppId는 업그레이드·제거를 식별하므로 바꾸지 마세요. (제품 이름이 달라도 같은 프로그램으로 업그레이드)
; 이미 설치된 PC에서는 기존 설치 폴더를 그대로 사용한다.
AppId={{6F4B2C1E-8A3D-4E57-9B21-3C7D5E8F0A14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppShortName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#FilePrefix}Monitor-Setup-{#AppVersion}
SetupIconFile=..\src\HecsMonitor\Assets\HecsMonitor.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
; 실행 중인 프로그램을 설치 전에 닫도록 안내
CloseApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; 라이선스 키 폴더: 프로그램은 {app}\lic\license.key를 찾는다.
; 관리자 권한 없이도 키 파일을 넣을 수 있도록 일반 사용자에게 수정 권한을 준다.
Name: "{app}\lic"; Permissions: users-modify

[Files]
; license.key는 PC마다 다르므로 패키지에 넣지 않는다 (업그레이드해도 기존 키는 그대로 유지)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "lic\license.key"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 다른 제품 이름으로 설치되어 있던 바로 가기는 지운다 (예: HECS Monitor → ECO Monitor 업그레이드)
Type: files; Name: "{autoprograms}\{#OtherShortName}.lnk"
Type: files; Name: "{autodesktop}\{#OtherShortName}.lnk"

[Icons]
Name: "{autoprograms}\{#AppShortName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppShortName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppShortName}}"; Flags: nowait postinstall skipifsilent

; 설정(%APPDATA%\HECSMonitor)과 측정 기록(문서\<제품 이름> Monitor)은 제거할 때 남겨 둔다.
; {app}\lic\license.key도 설치 프로그램이 넣은 파일이 아니므로 제거 후에도 남는다.
