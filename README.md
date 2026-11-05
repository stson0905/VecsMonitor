# HECS Live Compression Monitor

원본 카메라 RTSP 스트림과 **HECS가 압축·전송한 RTSP 스트림**을 동시에 받아 실시간 비트레이트·압축률·대역폭 절감률을 측정하는 Windows 데스크톱 프로그램입니다. 압축된 영상은 메인 화면 카드에 썸네일로 실시간 표시됩니다.

- 원본 대비 HECS의 **실시간 압축률(N:1)·절감률(%)·누적 절약 데이터** 측정
- 원본 ↔ HECS 화질을 한 화면에서 비교하는 **와이프 비교**
- **ONVIF 카메라 검색**과 카메라별 계정으로 정확한 RTSP 주소 자동 조회
- 압축률 저하·스트림 끊김 경보, CSV 측정 기록, 보고서 내보내기

---

## 1. 요구 사항

| 항목 | 내용 |
|---|---|
| 운영체제 | Windows 10 / 11 (64비트) |
| 런타임 | .NET 10 (빌드 시 .NET 10 SDK) |
| FFmpeg | `ffmpeg.exe`가 프로그램 폴더 또는 시스템 PATH에 있어야 함 (FFmpeg 8.1에서 확인) |
| 네트워크 | 카메라·HECS 장비의 RTSP(기본 554 등), ONVIF 검색용 UDP 3702 |

## 2. 빌드와 실행

```powershell
# 실행 (개발)
dotnet run --project src/HecsMonitor

# 솔루션 빌드 (HecsMonitor + 라이선스 발급 프로그램 HecsMonitorLicenseGen)
dotnet build HecsMonitor.sln -c Release

# 배포용 폴더 만들기 → publish 폴더에 ffmpeg.exe를 함께 넣으면 PATH 설정 없이 동작
dotnet publish src/HecsMonitor -c Release -r win-x64 --self-contained false -o publish
```

### 설치 패키지 만들기

다른 PC에 설치할 `Setup.exe`를 만듭니다. [Inno Setup 6.4 이상](https://jrsoftware.org/isinfo.php)이 필요합니다(`winget install JRSoftware.InnoSetup`).

| 패키지 | 명령 | 결과 | 대상 |
|---|---|---|---|
| ECO Monitor | `.\build-installer.ps1 -Version 1.3.0` | `dist\EcoMonitor-Setup-<버전>.exe` | 고객 PC (라이선스 키 필요) |
| ECO License Studio | `.\build-licensegen-installer.ps1 -Version 1.1.0` | `dist\EcoMonitorLicenseGen-Setup-<버전>.exe` | 키 발급 담당 PC (설치 비밀번호 필요, 서명 개인키 포함) |

- **제품 이름**: 두 스크립트 모두 `-Brand ECO`(기본) / `-Brand HECS`를 받습니다. 설치 이름·설치 폴더(`C:\Program Files\ECO Monitor`)·바로 가기·패키지 파일 이름(ECO → `EcoMonitor-Setup-…`, HECS → `HecsMonitor-Setup-…`)과 프로그램의 기본 표시 이름이 이 값을 따릅니다. 4.5절도 참고하세요.
  - `AppId`가 같아서 HECS로 설치된 PC에 ECO 패키지를 설치하면 업그레이드됩니다. 설치 폴더는 기존 폴더를 그대로 쓰고, 이전 이름의 바로 가기는 지웁니다.

- 두 프로젝트에 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`를 지정해 두었습니다. 그래서 VS Code나 일반 빌드가 복원을 다시 해도 패키지 빌드(NETSDK1047)가 깨지지 않습니다.

HECS Monitor 패키지 옵션:

```powershell
# 버전을 바꿔서 패키지 만들기 → csproj의 <Version>도 함께 바뀜
.\build-installer.ps1 -Version 1.1.0
# csproj에 적힌 현재 버전으로 만들기
.\build-installer.ps1

# FFmpeg를 넣지 않기 / 다른 ffmpeg.exe 지정
.\build-installer.ps1 -NoFfmpeg
.\build-installer.ps1 -FfmpegPath D:\tools\ffmpeg.exe

# 제품 이름: -Brand ECO(기본) | HECS
#   설치 이름·설치 폴더·바로 가기·패키지 파일 이름과 프로그램의 기본 표시 이름이 됩니다.
#   설치한 PC에서 개발자 모드(시스템 → 버전 5회 클릭)로 제품 이름을 바꾸면 그 패키지에서만 유지되고(개발자 모드를 끄거나 재실행해도 유지),
#   새 패키지를 설치하면 그 패키지의 -Brand 값으로 시작합니다. 개발자 모드 자체는 실행할 때마다 꺼진 상태로 시작합니다.
# ECO Monitor 설치 패키지 (고객 PC용) → dist\EcoMonitor-Setup-<버전>.exe
./build-installer.ps1 -Version 1.3.0
./build-installer.ps1 -Version 1.3.0 -Brand HECS      # HECS 이름으로 → dist\HecsMonitor-Setup-<버전>.exe
```

- 결과: `dist\HecsMonitor-Setup-<버전>.exe`. 설치 대상 PC에 .NET이나 FFmpeg를 따로 설치할 필요가 없습니다.
  - .NET 10 런타임이 함께 들어 있습니다.
  - FFmpeg는 기본으로 시스템 PATH의 `ffmpeg.exe`를 넣습니다.
- 설치 위치는 `C:\Program Files\HECS Monitor`이고, 시작 메뉴 바로 가기와 선택형 바탕 화면 바로 가기를 만듭니다. 제어판에서 제거할 수 있습니다.
- 새 버전을 설치하면 기존 설치를 그대로 덮어써서 업그레이드합니다. 설정과 측정 기록은 유지됩니다. 제거할 때도 설정과 측정 기록은 남습니다.
- 설치 스크립트: `installer\HecsMonitor.iss`. 이 파일의 `AppId`는 업그레이드를 식별하므로 바꾸지 마세요.

### 라이선스

프로그램은 실행 파일 옆 `lic\license.key`에 **이 PC용으로 발급된 키**가 있어야 실행됩니다.
- 설치 PC에서는 `C:\Program Files\HECS Monitor\lic\license.key`입니다.
- 키가 없거나 맞지 않으면 "라이선스 정책에 따라 프로그램을 실행할 수 없습니다" 안내 후 종료합니다. 이때 이 PC의 장치 ID가 표시되고 클립보드에 복사됩니다.

| 항목 | 내용 |
|---|---|
| 내용 | 허가 채널 수, 생성일, 장치 ID(MAC), 사용자(고객·현장), 라이선스 ID |
| 서명 | **ECDSA P-256 + SHA-256 공개키 서명**. 개인키는 발급 프로그램만 갖고, HecsMonitor에는 공개키만 있음 → 프로그램을 분석해도 키 위조 불가 |
| 파일 | 내용+서명을 장치 ID에서 만든 키로 **AES-256-GCM 암호화** 후 Base64. 텍스트 편집기로 열어도 채널 수·날짜·장치가 보이지 않고, 다른 PC에서는 풀리지 않음 |
| 장치 ID | 연결된 첫 유선 이더넷 MAC(없으면 첫 유선 MAC). 검증은 이 PC의 모든 유선 MAC으로 확인 |
| 채널 제한 | 감시·등록 가능 채널 = min(허가 채널 수, 64). 넘는 채널은 설정에 남지만 연결하지 않음 |
| 표시 | 설정 → 시스템 → **라이선스**: 허가 채널 수, 생성일, 장치 ID, 사용자, 라이선스 ID, 키 파일 위치 |
| 소스 | `src\HecsMonitor\lic\` (`SoftwareLicenseCore.cs` 공용 코어, `SoftwareLicense.cs` 검증) |

```
-----BEGIN HECS MONITOR LICENSE-----
SEwB+LJcQiATo8kjONx0bPzQOb8dpeW+7fAZ/2S+lWkpBQk9htYY3GwAKWDLnVG1
...
-----END HECS MONITOR LICENSE-----
```

#### 라이선스 발급 프로그램 (HECS License Studio)

`src\HecsMonitorLicenseGen\`는 솔루션 빌드에 포함됩니다. **HECS Monitor 설치 패키지에는 넣지 않고**, 별도 패키지(`build-licensegen-installer.ps1`)로 배포합니다.

| 영역 | 내용 |
|---|---|
| 발급 정보 | ① 사용자 ② 장치 ID(붙여넣기 / 이 PC, 형식 검사) ③ 채널 수(4·8·16·32·64 또는 직접 입력) ④ 생성일 ⑤ 메모(이력에만 저장) |
| 인증서 미리보기 | 입력하는 대로 바뀌는 라이선스 인증서. **서명 인장**은 7×7 무늬로, 발급 전에는 입력값, 발급 후에는 실제 서명에서 만들어 라이선스마다 고유합니다. 발급하면 ISSUED 도장이 찍힘 |
| 발급 | 서명 → 암호화 → HecsMonitor와 같은 코드로 **자체 검증** → `문서\HECS Monitor Licenses\<날짜>_<사용자>_<MAC>\license.key` 저장. 텍스트 편집기에서 보이는 원문도 표시 |
| 발급 이력 | 검색(사용자·장치 ID·라이선스 ID·메모), 같은 조건으로 재발급, 폴더 열기, 다시 저장, 장치 ID 복사, 삭제. `%APPDATA%\HecsMonitorLicenseGen\ledger.json` (설정은 같은 폴더의 `config.json`) |
| 키 검사 | license.key를 창에 끌어 놓으면 입력 중인 장치 ID·이력·이 PC로 열어 내용과 서명 확인 |
| 서명 키 | 상단에 상태(준비됨 / 공개키 불일치 / 없음) 표시 |
| 설정 (상단 ⚙) | 키 저장 폴더 바꾸기·열기·기본값, 서명 키(.pem) 불러오기(비밀번호 보호 키 지원), 프로그램 정보(버전·런타임·데이터 폴더) |
| 개발자 모드 | 설정의 **버전 숫자를 2초 안에 5번 클릭**하면 켜고 끕니다(HecsMonitor와 같은 방식). **기본값은 꺼짐**이며 상태는 config.json에 저장됩니다. 켜면 다음이 보입니다. ① 기능 상세 설명 ② 서명 키 상세(개인키 경로·보호 여부·지문, 앱 공개키·불러온 키의 공개키) ③ 새 서명 키 쌍 만들기 ④ 인증서의 서명 방식·키 지문 ⑤ 텍스트 편집기에서 보이는 키 원문 |

- **서명 개인키**: `keys\hecs-license-signing.pem`
  - 저장소 루트에 있고 `.gitignore`에 들어 있습니다. 발급 프로그램이 자동으로 찾습니다.
  - 이 파일을 잃어버리면 더 이상 키를 발급할 수 없고, 유출되면 누구나 키를 만들 수 있습니다. **안전한 곳에 백업하고 외부에 공유하지 마세요.**
- **설치 패키지**: `.\build-licensegen-installer.ps1 -Version 1.0.0` → `dist\HecsMonitorLicenseGen-Setup-<버전>.exe`
  - 서명 개인키가 함께 들어가 `{app}\keys\`에 설치되고, 프로그램이 자동으로 찾습니다. 그래서 다른 업체 PC에서도 키를 발급할 수 있습니다.
  - 설치할 때 **비밀번호**를 넣어야 합니다. 기본값은 `admin1357!!`입니다.
  - 비밀번호는 `installer\HecsMonitorLicenseGen.iss`의 `InstallPassword`에서 바꿉니다. 빌드할 때 `-Password '새비밀번호'`로 지정할 수도 있습니다.
  - 패키지 파일은 이 비밀번호로 암호화됩니다(Inno Setup `Encryption`, 6.4 이상 필요). 압축 해제 도구로도 개인키를 꺼낼 수 없습니다.
  - 설치된 PC에서는 개인키 파일이 그대로 있으므로, 그 PC의 관리자는 키를 볼 수 있습니다. 믿을 수 있는 곳에만 설치하세요.
- **키 교체**: 발급 프로그램에서 새 키 쌍을 만든 뒤 공개키를 `SoftwareLicenseCore.PublicKeyBase64`에 넣고 다시 빌드·배포합니다. 교체하면 이전에 발급한 키는 모두 무효가 됩니다.
- **설치 폴더**: 설치 프로그램이 `{app}\lic` 폴더를 만들고 일반 사용자에게 수정 권한을 줍니다. 재설치·업그레이드·제거 후에도 `license.key`는 남습니다.
- **개발 PC**: `src\HecsMonitor\lic\license.key`에 그 PC의 키를 두면 디버그 실행이 됩니다. 빌드 출력에만 복사되고 설치 패키지에는 들어가지 않습니다.

```
# 라이선스 키 생성 프로그램 설치 패키지 (ECO License Studio) → dist\EcoMonitorLicenseGen-Setup-<버전>.exe
# 서명 개인키가 들어 있으며, 설치할 때 비밀번호를 입력해야 합니다.
# 기본 비밀번호: admin1357!!  (installer\HecsMonitorLicenseGen.iss 의 InstallPassword 에서 변경)
./build-licensegen-installer.ps1 -Version 1.0.0
./build-licensegen-installer.ps1 -Password '다른비밀번호'     # 이번 빌드만 다른 비밀번호
./build-licensegen-installer.ps1 -Brand HECS                 # HECS 이름으로 → dist\HecsMonitorLicenseGen-Setup-<버전>.exe
```

## 3. 빠른 시작

1. 처음 실행하면 카메라 없이 화면을 볼 수 있도록 **데모 채널 6개**(`demo://…`)가 등록되어 있습니다.
2. **설정 → 카메라 검색 (ONVIF)** 에서 `네트워크 검색`을 누릅니다.
3. 상단의 **대상 채널**을 고르고, 카메라 줄마다 **ID/비밀번호**를 입력합니다.
4. 원본 카메라 줄에서 `→ 원본`, HECS 출력 줄에서 `→ HECS 출력`을 누릅니다. 카메라에서 정확한 RTSP 주소를 받아 와 채널에 채웁니다.
   - 새 채널로 만들려면 `새 채널`을 누릅니다. 새 채널이 대상 채널이 되어 바로 HECS 출력을 지정할 수 있습니다.
5. 필요하면 **채널 관리**에서 `연결 테스트`로 확인한 뒤 **저장 및 적용**을 누릅니다. 저장 결과가 화면 가운데 창으로 표시됩니다.
6. 대시보드에서 실시간 측정이 시작됩니다. 데모 채널은 채널 관리에서 삭제하세요.

---

## 4. 화면 구성

### 4.1 대시보드

| 영역 | 내용 |
|---|---|
| 상단 요약 | 전체 실시간 절감률(%), 전체 압축률, 원본 → HECS 합계 대역폭, 전체 대역폭 추이 그래프, 누적 절약 데이터, 가동 채널·경보 수, 가동 시간 |
| 카드 보기 | HECS 출력 실시간 썸네일, 압축률 배지(N:1), ▶ HECS 주소, `INPUT/OUTPUT 해상도 @ fps` + 비트레이트 막대, 절감률 링, 최근 60초 그래프, 평균 압축률, 절약 데이터. 카드 크기 S/M/L에 따라 페이지로 나눔(아래 표) |
| 리스트 보기 | HECS 장비의 카메라 목록과 같은 순서(썸네일 · 절감률 · 이름/주소 · INPUT/OUTPUT) + 압축률, 60초 그래프, 줄마다 비교·재연결·편집·복제·삭제 |
| 이벤트 패널 | 스트림 연결/끊김, 압축률 저하 경보·복귀, 설정 적용 등 |
| 상단 바 | 채널 검색, 카드/리스트 전환, 전체 재연결, 보고서(CSV) 내보내기, HECS 웹 콘솔 열기, **+ 채널 추가** |
| 왼쪽 메뉴 | 대시보드 / 설정, 이벤트 패널 표시·숨기기, 기록 폴더 열기 |

#### 카드 페이지 (최대 64채널)

| 카드 크기 | 한 화면 | 기본 배치 | 64채널일 때 |
|---|---|---|---|
| S (작게) | 12개 | 6×2 | 6쪽 |
| M (보통) | 8개 | 4×2 | 8쪽 |
| L (크게) | 6개 | 3×2 | 11쪽 |

- 채널은 **최대 64개**까지 등록할 수 있습니다. 채널마다 FFmpeg 2개를 쓰므로 최대 128개입니다. 채널 추가·복제·일괄 등록·카메라 검색 모두 이 한도를 지킵니다.
- 카드 한 장의 구성(썸네일, INPUT/OUTPUT 막대, 절감률 링, 60초 그래프, 평균·절약)은 크기와 관계없이 같습니다. 페이지 칸에 맞춰 카드 전체가 같은 비율로 커지거나 작아집니다.
- 창이 세로로 길면 같은 개수로 배치를 바꿉니다(예: 6×2 ↔ 4×3).
- **페이지 독**(카드 아래)
  - 각 페이지 버튼에 그 페이지의 실제 배치 모양대로 채널 상태 점(초록 정상, 노랑 연결 중, 빨강 경보·끊김)을 보여 줍니다.
  - 경보 채널이 있는 페이지 버튼에는 빨간 점이 깜빡여, 다른 페이지의 이상을 바로 알 수 있습니다.
- **페이지 넘기기**: 페이지 버튼, ‹ ›, 카드 위 마우스 휠, `PgUp`/`PgDn`/`←`/`→`, `Home`/`End`
- **자동 넘김**: 끔 / 10초 / 30초 / 60초 (관제 화면 순환)
  - 현재 페이지 버튼 아래 진행 막대가 표시됩니다.
  - 카드 위에 마우스를 올리거나 비교 화면을 열면 일시정지합니다.
- 마지막 페이지의 빈 칸 첫 번째는 **채널 추가** 타일입니다(64개 미만일 때).
- 상단 요약 오른쪽 위 `⌃` 버튼으로 요약을 **한 줄로 접어** 카드를 더 크게 볼 수 있습니다.

- **프레임 손실 표시**: HECS 출력 fps가 원본보다 7% 이상 낮으면 노란색과 "프레임 손실 N%" 배지로 표시합니다.
- **경보 표시**: 압축률 저하 경보 중이면 카드 테두리가 빨간색, 스트림이 끊기면 붉은 테두리가 됩니다.

### 4.2 비교 화면 (카드 클릭)

- **와이프 비교**: 슬라이더를 좌우로 끌어 왼쪽은 원본, 오른쪽은 HECS 영상을 한 화면에서 비교합니다. **나란히 보기**도 지원합니다.
- 오른쪽 정보
  - 실시간 압축률과 절감률 링, 평균·최소·최대 압축률
  - 원본/HECS 비교표: 코덱, 해상도, 프레임레이트, GOP, 현재·평균 비트레이트, 누적 수신량
  - **평균 비트레이트 카드**: 원본 → HECS 평균을 큰 글자로, 평균 비율 막대, 평균 압축률·절감률, 측정 시간
  - **이 채널에서 절약한 데이터**
  - 스트림 연결 상태와 주소(비밀번호는 표시하지 않음)
- 하단: 비트레이트 추이 그래프. `재연결`, `채널 설정` 버튼. Esc로 닫습니다.

### 4.3 설정

| 메뉴 | 내용 |
|---|---|
| 채널 관리 | 채널 추가·복제·삭제·순서 변경. 원본/HECS 각각 **URL·ID·비밀번호**, `연결 테스트`, `ONVIF 주소 조회`, `계정 전체 적용`. **HECS 채널 일괄 등록**(장비 IP + 시작 포트 7000, 간격 100 → `rtsp://{ip}:{port}/`) |
| 카메라 검색 (ONVIF) | 네트워크 검색, **대상 채널** 선택, 카메라마다 **ID/비밀번호** 입력, `새 채널` / `→ 원본` / `→ HECS 출력`, 줄마다 조회 결과(✔ 주소 / ✖ 원인). 기본 계정과 `모든 카메라에 채우기` |
| 측정 · 화면 | 측정 구간(1/3/5/10초), 그래프 기록 길이(1~10분), 썸네일 해상도·갱신 속도, 카드 크기, 이벤트 패널 표시 |
| 알림 · 기록 | 압축률 경보(최소 N:1, 지속 시간), 끊김 기록, CSV 측정 기록(간격·폴더), **저장 용량**(최대 MB, 기록·디스크 사용량, 초과 시 오래된 기록 삭제 / 기록 중지), **분류별 로그 수준**(DEBUG/INFO/WARN/ERROR) |
| 스트림 엔진 | FFmpeg 경로·확인, RTSP 전송 방식(TCP 권장/UDP), 재연결 간격, 무신호 판정 시간, 하드웨어 디코딩 |
| 시스템 | 버전·운영체제·하드웨어·런타임·FFmpeg 버전·설정 파일 위치, **HECS 장비 웹 콘솔** 주소/열기 |

- 변경 사항은 **저장 및 적용**을 눌러야 반영되며, 결과(채널 수, 추가·삭제·다시 연결된 채널 수)가 화면 가운데 창으로 표시됩니다. URL·계정·엔진 설정이 바뀐 채널만 다시 연결되고, 이름만 바꾼 채널은 측정 이력을 유지합니다.
- HECS 장비 자체의 라이선스·녹화·장비 로그 레벨은 장비 웹 콘솔에서 관리합니다.

### 4.4 개발자 모드

- 설정 화면의 **상세 설명**(각 항목 설명, URL 입력 팁, 로그 분류 설명, 측정 방식 안내 등)은 기본적으로 숨겨져 있습니다.
- **설정 → 시스템 → 버전 숫자를 2초 안에 5번 연속 클릭**하면 개발자 모드가 켜지고 상세 설명이 표시됩니다. 같은 방법으로 끕니다.
  - 3번째 클릭부터 남은 횟수를 안내합니다.
  - 켜져 있으면 설정 제목 옆에 "개발자 모드" 배지가 붙습니다.
  - 상태는 저장하지 않습니다. 프로그램을 다시 실행하거나 재설치하면 항상 **꺼진 상태**로 시작합니다. License Studio도 같습니다.

### 4.5 제품 이름 (HECS / ECO)

- 화면에 표시하는 제품 이름을 **HECS**와 **ECO** 중에서 고릅니다. 창 제목, 대시보드·카드·비교 화면, 설정 화면, 이벤트, 토스트, 보고서(CSV 머리글·파일 이름), 라이선스 안내 창에 적용됩니다.
- **개발자 모드 → 설정 → 시스템 → 제품 이름**에서 고릅니다. 누르는 즉시 모든 화면에 반영되고, 고른 빌드의 식별값과 함께 설정 파일(`Brand`, `BrandBuild`)에 저장됩니다.
- **같은 빌드(같은 설치 패키지)**에서는 개발자 모드를 끄거나 프로그램을 다시 실행해도 고른 이름이 유지됩니다.
- **새 설치 패키지**로 설치해 실행하면 이전 선택은 무시하고 그 패키지의 기본값으로 시작합니다. `-Brand ECO`로 만든 패키지는 ECO, `-Brand HECS`로 만든 패키지는 HECS입니다.
  - 패키지 스크립트는 빌드할 때마다 빌드 시각(`BuildStamp`)을 넣습니다.
  - 개발 빌드는 어셈블리 MVID를 쓰므로 소스나 `-p:Brand`를 바꿔 다시 빌드하면 기본값으로 돌아갑니다.
- 빌드 기본값은 ECO입니다. csproj의 `<Brand>`나 패키지 스크립트의 `-Brand`로 정합니다.
- License Studio도 같은 방식입니다. 개발자 모드 설정 패널에서 고르며 `config.json`에 저장됩니다(`Brand`, `BrandBuild`).
- **바뀌지 않는 내부 이름**: 실행 파일(`HecsMonitor.exe`), 설정 폴더(`%APPDATA%\HECSMonitor`), 라이선스 키 형식(`HECS MONITOR LICENSE`), 이미 저장된 측정 기록 폴더.
  - 기존 키와 설정을 그대로 쓰기 위해 바꾸지 않습니다.
  - 새로 설치한 PC의 기본 측정 기록 폴더는 `문서\ECO Monitor\logs`입니다.
- 소스: `Services\Brand.cs`(현재 이름·변경 알림), `Converters\BrandExtension.cs`(XAML `{b:Brand '… HECS …'}` → 문구 속 HECS를 현재 이름으로 표시)

---

## 5. RTSP / ONVIF 인증

- 채널마다 **원본과 HECS 출력의 ID/비밀번호를 따로** 입력합니다. FFmpeg로 연결할 때만 `rtsp://ID:비밀번호@…` 형태로 붙이며, 특수문자(`@ : / #` 등)는 인코딩해서 전달합니다.
- 비밀번호는 설정 파일에 **Windows DPAPI(현재 사용자)로 암호화**해 저장합니다.
- URL에 `user:pass@`를 붙여 넣어도 저장할 때 ID/비밀번호 칸으로 옮겨집니다.
- **ONVIF 주소 조회**: GetCapabilities → GetProfiles → GetStreamUri 순서로 카메라에 물어 정확한 RTSP 경로(예: `rtsp://192.168.30.69/video1`)를 받아 옵니다.
  - 해상도가 가장 큰 프로필(메인 스트림)을 사용합니다.
  - WS-Security(PasswordDigest)와 HTTP Digest 인증을 함께 사용하고, 카메라 시계 차이를 자동 보정합니다.
- 카메라 검색에서 조회에 성공한 계정은 **카메라 IP별로 기억**하여 다음 검색과 `ONVIF 주소 조회`에 자동으로 사용합니다.
- 오류 안내: 401 → ID/비밀번호, 403 → 권한, 404 → 경로, 연결 거부·시간 초과 → 네트워크. 로그와 화면의 URL 비밀번호는 `****`로 가립니다.

## 6. 측정 방식

- 측정은 스트림 하나당 FFmpeg 프로세스 하나(원본·HECS 각각)로, 디코딩 없이 합니다.
  - `-c copy -f framecrc pipe:2`: 재인코딩 없이 비디오 패킷 크기·키프레임 여부를 받아 비트레이트·FPS·GOP 계산
- 썸네일은 **화면에 보이는 채널만** HECS 스트림에 별도 연결(`-f rawvideo -pix_fmt bgr24 pipe:1`, 축소 디코딩)을 엽니다.
  - 카드 보기: 현재 페이지(자동 넘김 중이면 다음 페이지도 미리) · 목록 보기: 스크롤 영역에 보이는 행 · 설정 화면: 없음
  - 화면에서 사라지면 10초 뒤 닫습니다(자동 넘김 중에는 바로). 연결 상태·압축률·페이지 독·기록은 모든 채널이 계속 측정합니다.
  - 64채널에서 모든 썸네일을 디코딩하면 CPU가 포화되어 연결이 무작위로 끊겼기 때문입니다(2026-10-23).
- FFmpeg 출력 파이프는 채널마다 전용 스레드로 읽습니다(스레드 풀을 쓰면 채널이 많을 때 읽기가 늦게 시작되어 FFmpeg가 멈춤).
- **비트레이트** = 측정 구간(기본 3초) 안에 도착한 패킷 바이트 합 × 8 ÷ 구간 길이
- **압축률** = 원본 ÷ HECS, **절감률** = 1 − HECS ÷ 원본
- **평균 비트레이트·누적 절약 데이터**는 원본과 HECS가 **모두 수신 중인 시간**만 기준으로 계산합니다.
- 첫 번째 비디오 트랙만 측정합니다(오디오 제외).
- 비교 화면을 여는 동안에만 원본/HECS 고해상도(960×540) 미리보기 연결을 추가로 엽니다.
- 끊기면 지정 간격으로 자동 재연결하고, 앱이 비정상 종료되어도 FFmpeg 프로세스가 남지 않도록 정리합니다.

검증: 4 Mbps H.264 원본(10.10 MB)과 400 kbps HEVC(1.03 MB) 파일로 측정한 평균 압축률은 9.8:1로, 파일 크기 비율 9.79:1과 일치했습니다.

## 7. 저장 위치

| 항목 | 위치 |
|---|---|
| 설정 | `%APPDATA%\HECSMonitor\settings.json` |
| 측정 기록 | `문서\HECS Monitor\logs\measure_YYYY-MM-DD.csv` (채널별 구간 평균, 기본 60초 간격, Excel에서 바로 열림) |
| 이벤트 로그 | 같은 폴더의 `events_YYYY-MM-DD.log` |
| 오류 로그 | `%APPDATA%\HECSMonitor\crash.log` |
| 보고서 | 상단 바의 내보내기 버튼으로 원하는 위치에 CSV 저장 |
| 라이선스 키 | 프로그램 폴더의 `lic\license.key` (설치 시 `C:\Program Files\HECS Monitor\lic\`) |
| 발급 프로그램 설정·이력 | `%APPDATA%\HecsMonitorLicenseGen\config.json`, `ledger.json` |
| 발급한 키 | `문서\HECS Monitor Licenses\<날짜>_<사용자>_<장치ID>\license.key` (설정에서 변경) |
| 서명 개인키 | 개발: 저장소 `keys\hecs-license-signing.pem` · 설치: `C:\Program Files\HECS License Studio\keys\` |

## 8. 테스트 방법

- URL 칸에 **로컬 영상 파일 경로**를 넣으면 실시간 속도로 반복 재생되어 RTSP 없이 측정을 시험할 수 있습니다.
- `demo://이름?mbps=4&ratio=8&codec=hevc&fps=30`: 가상 스트림. 같은 이름의 원본/HECS 데모는 같은 장면을 공유합니다.
- `HecsMonitor.exe --snapshot <폴더>`: 주요 화면을 PNG로 저장하고 종료합니다(화면 확인용). 카드 크기별(S/M/L) 화면, 요약 접기, 마지막 페이지도 함께 저장합니다. 다른 제품 이름으로 바꾼 시스템·대시보드·비교 화면(`9_…`)도 저장한 뒤 원래 이름으로 되돌립니다.
- `HecsMonitorLicenseGen.exe --snapshot <폴더>`: 발급 화면, 설정, 개발자 모드 설정·발급 화면을 저장합니다. 개발자 모드는 끝나면 원래 상태로 되돌립니다.

## 9. 소스 구조

```
HecsMonitor.sln
build-installer.ps1 · build-licensegen-installer.ps1 · buildGuide.txt
installer/      HecsMonitor.iss, HecsMonitorLicenseGen.iss(설치 비밀번호 InstallPassword)
keys/           hecs-license-signing.pem — 서명 개인키 (git 제외, 백업 필수)
src/HecsMonitorLicenseGen   라이선스 발급 프로그램 (HECS License Studio)
├─ StudioViewModel  발급 양식·인증서 미리보기·서명 인장·발급·이력·검사·설정·개발자 모드
├─ SigningKeyStore  개인키 불러오기/만들기(비밀번호 보호), 앱 공개키와 짝 확인
├─ GenSettings · Ledger   설정(config.json) · 발급 이력(ledger.json)
└─ MainWindow       화면 (HecsMonitor의 Theme.xaml을 그대로 사용)
src/HecsMonitor
├─ lic/         SoftwareLicenseCore(ECDSA 서명 확인·AES-GCM 봉인·장치 ID), SoftwareLicense(license.key 검증), README.txt(설치 폴더 안내)
├─ ViewModels/CardPaging   카드 페이지 배치(S 12 · M 8 · L 6), 페이지 독
├─ Assets/      HecsMonitor.ico(16~256px) · HecsMonitor.png — 프로그램 아이콘
├─ Models/      AppSettings(설정·채널·계정), 로그 수준, ONVIF 계정
├─ Services/    FfmpegSource(스트림 수신·파싱·재연결), DemoSource, BitrateMeter, FrameSink,
│               OnvifDiscovery(WS-Discovery), OnvifClient(GetStreamUri), StreamUrl(계정 처리·DPAPI),
│               ProbeService(연결 테스트), CsvLogger(기록·용량 관리), SettingsStore, ChildProcessTracker
├─ ViewModels/  MainViewModel(합계·이벤트·비교 화면·모달), ChannelViewModel(채널 측정·경보),
│               SettingsEditorViewModel(설정·검색·개발자 모드), DeviceItem(검색 결과 줄)
├─ Controls/    DualSeriesChart(원본/HECS 그래프), RingGauge, PasswordHelper
├─ Converters/  화면 바인딩 변환기
├─ Views/       MainWindow(대시보드·비교 화면), SettingsView
└─ Themes/      Theme.xaml(다크 테마, 컨트롤 스타일)
```

## 10. 참고 사항

- **아이콘**: 게이지 링(절감률) 안에 긴 주황 막대(원본)와 짧은 청록 막대(HECS)를 넣어 "원본 → 압축"을 표현했습니다. exe·작업 표시줄·왼쪽 메뉴·시스템 화면에 적용됩니다.
- **폴더 이름**: 프로젝트 폴더는 2026-10-14에 `HECS_vms`에서 `HecsMonitor`로 바뀌었습니다. 폴더를 다시 옮기면 `src\HecsMonitor\bin`, `obj`를 지운 뒤 다시 빌드하세요(빌드 캐시에 이전 절대 경로가 남습니다).
- FFmpeg 프로세스 명령줄에는 RTSP 계정이 포함되므로, 같은 PC의 관리자는 작업 관리자에서 볼 수 있습니다.
- 설정 파일을 다른 PC나 다른 Windows 계정으로 복사하면 비밀번호는 다시 입력해야 합니다(DPAPI 암호화).
- HECS 스트림의 키프레임 간격이 길면 비교 화면의 HECS 영상이 몇 초 늦게 나타날 수 있습니다.

## 11. 작업 이력

| 구분 | 내용 |
|---|---|
| 기본 기능 (2026-10-14) | 원본/HECS 동시 수신, 실시간 비트레이트·압축률·절감률 측정, HECS 썸네일, 대시보드·비교 화면·설정 화면 |
| HECS 장비 화면 반영 | 리스트 보기, INPUT/OUTPUT 표기, 프레임 손실 표시, 채널 일괄 등록, 저장 용량·로그 수준, 시스템 정보·웹 콘솔 |
| 인증 | 채널별 RTSP ID/비밀번호(DPAPI 암호화), ONVIF 주소 조회, 비밀번호 입력 칸이 값을 전달하지 못하던 문제 수정 |
| 카메라 검색 개선 | 대상 채널 선택, 카메라별 계정, `→ 원본` / `→ HECS 출력` 지정, IP별 계정 기억 |
| 화면 개선 | 비교 화면 평균 비트레이트 카드, 저장 완료 모달, 개발자 모드(상세 설명 표시) |
| 기타 | 프로그램 아이콘 적용, 폴더 이름 변경 스크립트 |
| 카드 페이지 (2026-10-16) | 최대 64채널 제한, 카드 크기별 페이지(S 12 · M 8 · L 6), 원래 카드 구성 유지 + 칸에 맞춘 확대·축소, 상태 미니맵 페이지 독, 자동 넘김, 요약 접기 |
| 라이선스 (2026-10-19) | ECDSA P-256 공개키 서명 + AES-256-GCM 암호화 키 파일, 미인증 시 실행 차단, 허가 채널 수 적용, 시스템 화면 라이선스 표시, `lic\` 키 폴더 |
| 발급 프로그램 (2026-10-19) | HECS License Studio(HecsMonitorLicenseGen): 발급 양식·인증서 미리보기·서명 인장·이력·키 검사, 설정(저장 폴더·서명 키·버전), 개발자 모드(버전 5회 클릭) |
| 배포 (2026-10-19) | 발급 프로그램 설치 패키지(개인키 포함, 설치 비밀번호·패키지 암호화), 패키지 빌드 NETSDK1047 수정(`RuntimeIdentifiers`) |
| 제품 이름 (2026-10-21) | HECS / ECO 표시 이름 전환(개발자 모드에서 선택, 즉시 반영, 같은 패키지에서만 유지 · 새 패키지는 `-Brand` 기본값), 두 프로그램 모두 적용, 설치 패키지 `-Brand`(기본 ECO → `EcoMonitor-Setup-…`). 개발자 모드는 저장하지 않음(실행 시 항상 꺼짐) |
| 64채널 부하 (2026-10-23) | 랜덤 끊김 대응: 썸네일은 화면에 보이는 채널만 별도 연결로 디코딩(측정 연결은 디코딩 안 함), FFmpeg 파이프 읽기를 전용 스레드로 변경(스레드 풀 고갈로 연결이 늦게 시작·멈추던 문제) |
