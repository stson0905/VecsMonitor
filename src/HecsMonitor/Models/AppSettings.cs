using System.Text.Json.Serialization;
using HecsMonitor.Services;
using HecsMonitor.ViewModels;

namespace HecsMonitor.Models;

/// <summary>원본 카메라 스트림과 HECS 출력 스트림 한 쌍.</summary>
public sealed class ChannelConfig : ObservableObject
{
    string _name = "새 채널";
    string _group = "";
    string _originalUrl = "";
    string _hecsUrl = "";
    bool _enabled = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Group { get => _group; set => Set(ref _group, value); }
    public string OriginalUrl { get => _originalUrl; set => Set(ref _originalUrl, value); }
    public string HecsUrl { get => _hecsUrl; set => Set(ref _hecsUrl, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    // RTSP 인증 계정. 비밀번호는 메모리에서만 평문, 파일에는 DPAPI로 암호화해 저장한다.
    string _origUser = "", _origPassword = "", _hecsUser = "", _hecsPassword = "";
    public string OrigUser { get => _origUser; set => Set(ref _origUser, value); }
    public string HecsUser { get => _hecsUser; set => Set(ref _hecsUser, value); }

    [JsonIgnore] public string OrigPassword { get => _origPassword; set => Set(ref _origPassword, value); }
    [JsonIgnore] public string HecsPassword { get => _hecsPassword; set => Set(ref _hecsPassword, value); }

    public string OrigPasswordProtected
    {
        get => SecretProtector.Protect(_origPassword);
        set => _origPassword = SecretProtector.Unprotect(value);
    }

    public string HecsPasswordProtected
    {
        get => SecretProtector.Protect(_hecsPassword);
        set => _hecsPassword = SecretProtector.Unprotect(value);
    }

    /// <summary>FFmpeg에 넘길 실제 URL (계정 포함).</summary>
    public string OriginalStreamUrl => StreamUrl.WithCredentials(OriginalUrl, OrigUser, OrigPassword);
    public string HecsStreamUrl => StreamUrl.WithCredentials(HecsUrl, HecsUser, HecsPassword);
}

public sealed class OnvifAccount
{
    public string Ip { get; set; }
    public string User { get; set; }
    [JsonIgnore] public string Password { get; set; } = "";
    public string PasswordProtected
    {
        get => SecretProtector.Protect(Password);
        set => Password = SecretProtector.Unprotect(value);
    }
}

/// <summary>이벤트 분류별 기록 수준 (HECS 장비의 모듈별 로그 레벨에 대응).</summary>
public sealed class LogLevelSetting
{
    public string Key { get; set; }
    public string Label { get; set; }
    string _description;
    /// <summary>설명 문구 ("HECS"는 현재 제품 이름으로 표시). 코드의 기본값에서 채우므로 파일에는 저장하지 않는다.</summary>
    [JsonIgnore] public string Description { get => Services.Brand.Apply(_description); set => _description = value; }
    public string Level { get; set; } = "INFO";

    public static readonly string[] Levels = { "DEBUG", "INFO", "WARN", "ERROR" };

    public static List<LogLevelSetting> Defaults() => new()
    {
        new() { Key = "Stream", Label = "스트림 연결", Description = "원본/HECS 스트림 연결·끊김·재연결" },
        new() { Key = "Alarm", Label = "압축률 경보", Description = "압축률 기준 미달 및 복귀" },
        new() { Key = "Measure", Label = "측정 기록", Description = "CSV 기록, 저장 용량 관리" },
        new() { Key = "FFmpeg", Label = "FFmpeg 엔진", Description = "재연결 시도마다의 FFmpeg 오류 메시지 (DEBUG)" },
        new() { Key = "System", Label = "시스템", Description = "시작, 설정 적용, 보고서 저장" },
    };
}

public sealed class AppSettings
{
    /// <summary>프로그램이 지원하는 최대 채널 수. 채널마다 FFmpeg 2개(원본·HECS)를 쓰므로 최대 128 프로세스.</summary>
    public const int ProgramMaxChannels = 64;

    /// <summary>등록·감시할 수 있는 채널 수 = 라이선스 허가 채널 수와 64 중 작은 값.</summary>
    public static int MaxChannels => Math.Min(ProgramMaxChannels, Licensing.SoftwareLicense.Current?.Channels ?? ProgramMaxChannels);

    public List<ChannelConfig> Channels { get; set; } = new();

    // 엔진
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string RtspTransport { get; set; } = "tcp";
    public bool HwAccel { get; set; }
    public int ReconnectSec { get; set; } = 5;
    public int StallTimeoutSec { get; set; } = 10;

    // 측정 / 표시
    public int MeasureWindowSec { get; set; } = 3;
    public int HistorySeconds { get; set; } = 120;
    public int ThumbWidth { get; set; } = 384;
    public int ThumbFps { get; set; } = 10;
    public string CardSize { get; set; } = "M";
    public string ViewMode { get; set; } = "card";   // card | list
    public bool ShowEventPanel { get; set; } = true;
    /// <summary>대시보드 상단 요약을 한 줄로 접기 (카드를 더 크게 보기)</summary>
    public bool HeroCollapsed { get; set; }
    /// <summary>카드 페이지 자동 넘김 간격(초). 0 = 끔</summary>
    public int PageTourSec { get; set; }
    // 개발자 모드(시스템 → 버전 5회 클릭)는 저장하지 않는다. 실행할 때마다 꺼진 상태로 시작한다.
    /// <summary>개발자 모드에서 고른 제품 이름 (HECS | ECO). BrandBuild가 지금 빌드와 같을 때만 쓰고, 아니면 빌드 기본값</summary>
    public string Brand { get; set; }
    /// <summary>Brand를 고른 빌드의 식별값 (Services.Brand.BuildId)</summary>
    public string BrandBuild { get; set; }

    // 알림 / 기록
    public double AlarmRatioMin { get; set; } = 3.0;
    public int AlarmHoldSec { get; set; } = 10;
    public bool AlarmOnOffline { get; set; } = true;
    public bool CsvLogging { get; set; } = true;
    public int CsvIntervalSec { get; set; } = 60;
    public string LogFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"{Services.Brand.Default} Monitor", "logs");
    public int LogMaxMB { get; set; } = 500;           // 0 = 무제한
    public string LogOverflow { get; set; } = "delete"; // delete(오래된 기록 삭제) | stop(기록 중지)
    public List<LogLevelSetting> LogLevels { get; set; } = LogLevelSetting.Defaults();

    // HECS 장비
    public string HecsConsoleUrl { get; set; } = "http://192.168.100.210:8001";
    public string BulkIp { get; set; } = "192.168.100.210";
    public int BulkStartPort { get; set; } = 7000;
    public int BulkPortStep { get; set; } = 100;
    public int BulkCount { get; set; } = 8;
    public string BulkUrlTemplate { get; set; } = "rtsp://{ip}:{port}/";
    public string BulkNameTemplate { get; set; } = "1-{n}";

    // ONVIF 카메라 검색에 사용할 공통 계정
    public string OnvifUser { get; set; } = "admin";
    [JsonIgnore] public string OnvifPassword { get; set; } = "";
    public string OnvifPasswordProtected
    {
        get => SecretProtector.Protect(OnvifPassword);
        set => OnvifPassword = SecretProtector.Unprotect(value);
    }

    /// <summary>카메라(IP)별로 확인된 ONVIF 계정. 다음 검색 때 자동으로 채운다.</summary>
    public List<OnvifAccount> OnvifAccounts { get; set; } = new();

    public OnvifAccount AccountFor(string ip) => OnvifAccounts.FirstOrDefault(a => a.Ip == ip);

    public void RememberAccount(string ip, string user, string password)
    {
        var a = AccountFor(ip);
        if (a == null) OnvifAccounts.Add(a = new OnvifAccount { Ip = ip });
        a.User = user;
        a.Password = password;
    }

    /// <summary>예전 설정 파일에 없는 항목을 기본값으로 채운다.</summary>
    public void Normalize()
    {
        var defaults = LogLevelSetting.Defaults();
        var current = (LogLevels ?? new()).Where(l => l?.Key != null).ToDictionary(l => l.Key);
        LogLevels = defaults.Select(d =>
        {
            if (current.TryGetValue(d.Key, out var c) && LogLevelSetting.Levels.Contains(c.Level)) d.Level = c.Level;
            return d;
        }).ToList();
        Channels ??= new();
        OnvifAccounts ??= new();
    }

    public string LevelOf(string category) =>
        LogLevels.FirstOrDefault(l => l.Key == category)?.Level ?? "INFO";
}
