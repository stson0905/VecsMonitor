<#
.SYNOPSIS
    ECO Monitor 설치 패키지(dist\EcoMonitor-Setup-<버전>.exe)를 만든다.
    -Brand HECS로 만들면 HECS Monitor(dist\HecsMonitor-Setup-<버전>.exe).

.DESCRIPTION
    0. -Brand(ECO 기본 | HECS)는 설치 이름·폴더·바로 가기·패키지 파일 이름과 프로그램의 기본 표시 이름이 된다.
       설치한 PC에서 개발자 모드로 제품 이름을 바꾸면 그 패키지에서만 우선하고, 새 패키지를 설치하면 -Brand 값으로 돌아간다.
    1. -Version을 주면 HecsMonitor.csproj의 <Version>을 그 값으로 바꾼다 (생략하면 csproj 값 사용).
    2. .NET 런타임을 포함(self-contained, win-x64)해 dist\app에 게시한다.
    3. ffmpeg.exe를 함께 넣는다 (-FfmpegPath 또는 시스템 PATH에서 찾음, -NoFfmpeg로 제외).
    4. Inno Setup 6(ISCC.exe)으로 설치 파일을 만든다.

.EXAMPLE
    .\build-installer.ps1 -Version 1.1.0
.EXAMPLE
    .\build-installer.ps1 -NoFfmpeg
.EXAMPLE
    .\build-installer.ps1 -Brand HECS
#>
param(
    [string]$Version,
    [ValidateSet('ECO', 'HECS')]
    [string]$Brand = 'ECO',
    [string]$FfmpegPath,
    [switch]$NoFfmpeg
)

$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$csproj = Join-Path $root 'src\HecsMonitor\HecsMonitor.csproj'
$iss    = Join-Path $root 'installer\HecsMonitor.iss'
$dist   = Join-Path $root 'dist'
$appDir = Join-Path $dist 'app'

# ── 1. 버전 ──────────────────────────────────────────────
$text = [IO.File]::ReadAllText($csproj)
$current = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $Version) { $Version = $current }
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "버전 형식이 올바르지 않습니다: '$Version' (예: 1.2.0 또는 1.2.0.5)"
}
if ($Version -ne $current) {
    $text = $text -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    [IO.File]::WriteAllText($csproj, $text)
    Write-Host "버전 변경: $current → $Version (HecsMonitor.csproj)"
}
$Brand = $Brand.ToUpperInvariant()
$prefix = if ($Brand -eq 'HECS') { 'Hecs' } else { 'Eco' }
# 패키지마다 다른 값. 설치 PC에서 개발자 모드로 고른 제품 이름은 같은 패키지에서만 유지되고,
# 새 패키지를 설치하면 이 패키지의 -Brand 값으로 시작한다.
$buildStamp = Get-Date -Format 'yyyyMMddHHmmss'
Write-Host "== $Brand Monitor $Version 패키지 만들기 =="

# ── 2. Inno Setup 컴파일러 찾기 ──────────────────────────
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

# ── 3. 게시 (.NET 런타임 포함) ───────────────────────────
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:Brand=$Brand -p:BuildStamp=$buildStamp -p:DebugType=None -p:SatelliteResourceLanguages=ko `
    -o $appDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (종료 코드 $LASTEXITCODE)" }
# 라이선스 키는 PC마다 다르므로 절대 패키지에 넣지 않는다 (설치 후 {app}\lic\ 에 따로 넣음)
$devKey = Join-Path $appDir 'lic\license.key'
if (Test-Path $devKey) { Remove-Item $devKey -Force }

# ── 4. FFmpeg 포함 ───────────────────────────────────────
if ($NoFfmpeg) {
    Write-Host 'FFmpeg 제외: 설치한 PC의 PATH에 ffmpeg가 있어야 합니다.'
} else {
    if (-not $FfmpegPath) { $FfmpegPath = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source }
    if (-not $FfmpegPath -or -not (Test-Path $FfmpegPath)) {
        throw 'ffmpeg.exe를 찾을 수 없습니다. -FfmpegPath로 지정하거나 -NoFfmpeg로 제외하세요.'
    }
    # winget 등의 심볼릭 링크는 실제 파일로 따라간다
    $item = Get-Item $FfmpegPath
    if ($item.LinkType) { $item = Get-Item $item.Target }
    Copy-Item $item.FullName (Join-Path $appDir 'ffmpeg.exe')
    # FFmpeg 배포판의 라이선스 문서(bin 폴더의 상위)가 있으면 함께 넣는다
    $license = Join-Path (Split-Path (Split-Path $item.FullName)) 'LICENSE'
    if (Test-Path $license) { Copy-Item $license (Join-Path $appDir 'FFmpeg-LICENSE.txt') }
    Write-Host "FFmpeg 포함: $($item.FullName)"
}

# ── 5. 설치 파일 만들기 ──────────────────────────────────
& $iscc /Q "/DBrand=$Brand" "/DAppVersion=$Version" "/DSourceDir=$appDir" "/DOutputDir=$dist" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 컴파일 실패 (종료 코드 $LASTEXITCODE)" }

$setup = Join-Path $dist "${prefix}Monitor-Setup-$Version.exe"
Write-Host ("완료: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
