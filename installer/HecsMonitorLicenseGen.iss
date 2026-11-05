; ECO / HECS License Studio(HecsMonitorLicenseGen) 설치 스크립트 (Inno Setup 6.4 이상)
; 직접 컴파일하지 말고 저장소 루트의 build-licensegen-installer.ps1을 실행하세요.
;
; 이 패키지에는 라이선스 서명 개인키(keys\hecs-license-signing.pem)가 들어 있습니다.
; 설치 비밀번호를 모르면 설치할 수 없고, 패키지 안의 파일도 이 비밀번호로 암호화되어
; 압축 해제 도구로도 꺼낼 수 없습니다.

; ===== 설치 비밀번호 =====
; 여기서 바꾸거나, build-licensegen-installer.ps1 -Password '새비밀번호' 로 빌드할 때 지정합니다.
#ifndef InstallPassword
  #define InstallPassword "admin1357!!"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\licensegen\app"
#endif
#ifndef KeyFile
  #define KeyFile "..\keys\hecs-license-signing.pem"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

; 제품 이름 (ECO | HECS): 설치 이름·폴더·바로 가기·패키지 파일 이름에 쓰인다
#ifndef Brand
  #define Brand "ECO"
#endif
#if Brand == "HECS"
  #define FilePrefix "Hecs"
  #define OtherAppName "ECO License Studio"
#else
  #define FilePrefix "Eco"
  #define OtherAppName "HECS License Studio"
#endif

#define AppName Brand + " License Studio"
#define AppExe "HecsMonitorLicenseGen.exe"

[Setup]
; AppId는 업그레이드·제거를 식별하므로 바꾸지 마세요. (HecsMonitor와 다른 값)
AppId={{B3E1D7A2-5C49-4F8E-A6D3-2F9B71C08E55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=SOLDIA
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
; 이미 설치된 PC에서는 기존 설치 폴더를 그대로 사용한다.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#FilePrefix}MonitorLicenseGen-Setup-{#AppVersion}
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
CloseApplications=yes
; 비밀번호를 맞혀야 설치 진행 + 패키지 안 파일을 비밀번호로 암호화
Password={#InstallPassword}
Encryption=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 서명 개인키: 프로그램이 {app}\keys\hecs-license-signing.pem 을 자동으로 찾는다
Source: "{#KeyFile}"; DestDir: "{app}\keys"; DestName: "hecs-license-signing.pem"; Flags: ignoreversion

[Dirs]
; 개인키 폴더는 관리자와 시스템만 수정, 사용자는 읽기만 (Program Files 기본 권한)
Name: "{app}\keys"

[InstallDelete]
; 다른 제품 이름으로 설치되어 있던 바로 가기는 지운다
Type: files; Name: "{autoprograms}\{#OtherAppName}.lnk"
Type: files; Name: "{autodesktop}\{#OtherAppName}.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 발급 이력(%APPDATA%\HecsMonitorLicenseGen)과 발급한 키(문서\HECS Monitor Licenses)는 제거할 때 남겨 둔다.
