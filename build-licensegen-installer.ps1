<#
.SYNOPSIS
    ECO License Studio(라이선스 키 생성 프로그램) 설치 패키지(dist\EcoMonitorLicenseGen-Setup-<버전>.exe)를 만든다.
    -Brand HECS로 만들면 HECS License Studio(dist\HecsMonitorLicenseGen-Setup-<버전>.exe).

.DESCRIPTION
    0. -Brand(ECO 기본 | HECS)는 설치 이름·폴더·바로 가기·패키지 파일 이름과 프로그램의 기본 표시 이름이 된다.
    1. -Version을 주면 HecsMonitorLicenseGen.csproj의 <Version>을 그 값으로 바꾼다 (생략하면 csproj 값 사용).
    2. .NET 런타임을 포함(self-contained, win-x64)해 dist\licensegen\app에 게시한다.
    3. 서명 개인키(keys\hecs-license-signing.pem)를 함께 넣는다.
    4. Inno Setup 6.4 이상으로 설치 비밀번호가 걸린 암호화 설치 파일을 만든다.
       비밀번호 기본값은 installer\HecsMonitorLicenseGen.iss의 InstallPassword ("admin1357!!").

    ※ 이 패키지에는 개인키가 들어 있으므로 비밀번호를 아는 사람에게만 전달하세요.

.EXAMPLE
    .\build-licensegen-installer.ps1 -Version 1.0.0
.EXAMPLE
    .\build-licensegen-installer.ps1 -Password '다른비밀번호'
#>
param(
    [string]$Version,
    [ValidateSet('ECO', 'HECS')]
    [string]$Brand = 'ECO',
    [string]$Password,
    [string]$KeyFile
)

$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$csproj = Join-Path $root 'src\HecsMonitorLicenseGen\HecsMonitorLicenseGen.csproj'
$iss    = Join-Path $root 'installer\HecsMonitorLicenseGen.iss'
$dist   = Join-Path $root 'dist'
$appDir = Join-Path $dist 'licensegen\app'
if (-not $KeyFile) { $KeyFile = Join-Path $root 'keys\hecs-license-signing.pem' }

# ── 1. 버전 ──────────────────────────────────────────────
$text = [IO.File]::ReadAllText($csproj)
$current = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $Version) { $Version = $current }
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "버전 형식이 올바르지 않습니다: '$Version' (예: 1.0.0)"
}
if ($Version -ne $current) {
    $text = $text -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    [IO.File]::WriteAllText($csproj, $text)
    Write-Host "버전 변경: $current → $Version (HecsMonitorLicenseGen.csproj)"
}
$Brand = $Brand.ToUpperInvariant()
$prefix = if ($Brand -eq 'HECS') { 'Hecs' } else { 'Eco' }
# 패키지마다 다른 값: 새 패키지를 설치하면 개발자 모드에서 고른 제품 이름 대신 -Brand 값으로 시작한다
$buildStamp = Get-Date -Format 'yyyyMMddHHmmss'
Write-Host "== $Brand License Studio $Version 패키지 만들기 =="

# ── 2. 개인키 확인 ───────────────────────────────────────
if (-not (Test-Path $KeyFile)) { throw "서명 개인키를 찾을 수 없습니다: $KeyFile" }
if ((Get-Content $KeyFile -Raw) -notmatch 'PRIVATE KEY') { throw "PEM 개인키 파일이 아닙니다: $KeyFile" }

# ── 3. Inno Setup 컴파일러 찾기 ──────────────────────────
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6을 찾을 수 없습니다. "winget install JRSoftware.InnoSetup"으로 설치하세요.'
}

# ── 4. 게시 (.NET 런타임 포함) ───────────────────────────
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:Brand=$Brand -p:BuildStamp=$buildStamp -p:DebugType=None -p:SatelliteResourceLanguages=ko `
    -o $appDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (종료 코드 $LASTEXITCODE)" }
# HecsMonitor 참조로 따라온 파일·개발 PC 키는 넣지 않는다
foreach ($f in 'HecsMonitor.exe', 'HecsMonitor.runtimeconfig.json', 'HecsMonitor.deps.json', 'lic') {
    $p = Join-Path $appDir $f
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
}

# ── 5. 설치 파일 만들기 ──────────────────────────────────
$isccArgs = @('/Q', "/DBrand=$Brand", "/DAppVersion=$Version", "/DSourceDir=$appDir", "/DKeyFile=$KeyFile", "/DOutputDir=$dist")
if ($Password) { $isccArgs += "/DInstallPassword=$Password" }
& $iscc @isccArgs $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 컴파일 실패 (종료 코드 $LASTEXITCODE). Inno Setup 6.4 이상인지 확인하세요." }

$setup = Join-Path $dist "${prefix}MonitorLicenseGen-Setup-$Version.exe"
Write-Host ("완료: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
Write-Host ("설치 비밀번호: {0}" -f ($(if ($Password) { '(-Password로 지정한 값)' } else { 'installer\HecsMonitorLicenseGen.iss의 InstallPassword' })))
