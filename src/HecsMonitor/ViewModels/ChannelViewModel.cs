using System.Windows;
using System.Windows.Media.Imaging;
using HecsMonitor.Models;
using HecsMonitor.Services;

namespace HecsMonitor.ViewModels;

/// <summary>
/// 채널 1개 = 원본 카메라 스트림(측정만) + HECS 출력 스트림(측정만) + 썸네일 스트림(디코딩만, 화면에 보일 때만).
/// 화면 표시용 속성은 1초마다 Tick()에서 한 번에 갱신한다.
/// </summary>
public sealed class ChannelViewModel : ObservableObject
{
    /// <summary>화면에서 사라진 뒤 썸네일 디코딩을 멈추기까지의 유예(초). 페이지를 오갈 때 매번 다시 연결하지 않도록.</summary>
    const int VideoLingerSec = 10;

    readonly MainViewModel _main;
    IStreamSource _orig, _hecs, _video;
    SourceOptions _videoOpt;
    bool _videoWanted;
    DateTime? _videoIdleSince;
    FrameSink _sink;

    SourceState _origState, _hecsState;
    string _origMsg = "중지됨", _hecsMsg = "중지됨";
    bool _origErrLogged, _hecsErrLogged;
    StreamInfo _origInfo, _hecsInfo;

    MeterReading _o, _h;
    double _ratio = double.NaN;
    double _sumOrigBytes, _sumHecsBytes;
    int _pairedSeconds;
    double _minRatio = double.NaN, _maxRatio = double.NaN;
    int _lowSeconds;
    bool _alarm;
    bool _hasVideo;

    double _csvO, _csvH; int _csvN;

    public ChannelViewModel(MainViewModel main, ChannelConfig config)
    {
        _main = main;
        Config = config;
    }

    public ChannelConfig Config { get; private set; }
    public string EngineSignature { get; private set; }
    public BitrateMeter OrigMeter { get; } = new();
    public BitrateMeter HecsMeter { get; } = new();
    public List<double> OrigHistory { get; } = new();
    public List<double> HecsHistory { get; } = new();
    public int HistoryVersion { get; private set; }

    // ---------- 표시 속성 ----------
    public string Name => Config.Name;
    public string Group => string.IsNullOrWhiteSpace(Config.Group) ? "그룹 없음" : Config.Group;
    public WriteableBitmap Thumbnail => _sink?.Bitmap;
    public bool HasVideo => _hasVideo;
    /// <summary>썸네일 자리 안내 문구. 측정은 정상인데 영상을 아직 받지 못했으면 불러오는 중으로 표시.</summary>
    public string VideoPlaceholderText => StatusKind is "Live" or "Alarm" ? "영상 불러오는 중…" : StatusText;

    public SourceState OrigState => _origState;
    public SourceState HecsState => _hecsState;
    public string OrigMessage => _origMsg;
    public string HecsMessage => _hecsMsg;

    public double OrigBps => _o.Bps;
    public double HecsBps => _h.Bps;
    public double Ratio => _ratio;
    public bool HasRatio => !double.IsNaN(_ratio);
    public double Saving => HasRatio ? 1 - 1 / _ratio : double.NaN;
    public double SavedBytes => Math.Max(0, _sumOrigBytes - _sumHecsBytes);
    public double AvgRatio => _sumHecsBytes > 0 ? _sumOrigBytes / _sumHecsBytes : double.NaN;
    public bool AlarmActive => _alarm;

    public string OrigRateText => Fmt.RateText(_o.Bps);
    public string HecsRateText => Fmt.RateText(_h.Bps);
    public string OrigRateValue => Fmt.Rate(_o.Bps).Value;
    public string OrigRateUnit => Fmt.Rate(_o.Bps).Unit;
    public string HecsRateValue => Fmt.Rate(_h.Bps).Value;
    public string HecsRateUnit => Fmt.Rate(_h.Bps).Unit;
    public string RatioText => Fmt.Ratio(_ratio);
    public string AvgRatioText => Fmt.Ratio(AvgRatio);
    public string MinRatioText => Fmt.Ratio(_minRatio);
    public string MaxRatioText => Fmt.Ratio(_maxRatio);
    public string SavingText => HasRatio ? (Saving * 100).ToString("0") : "—";
    public string SavingPreciseText => Fmt.Percent(Saving);
    public double SavingFraction => HasRatio ? Math.Clamp(Saving, 0, 1) : 0;
    public double OrigFraction => _o.Bps > 0 ? 1 : 0;
    public double HecsFraction => _o.Bps > 0 ? Math.Clamp(_h.Bps / _o.Bps, 0, 1) : (_h.Bps > 0 ? 1 : 0);
    public string SavedText => Fmt.Bytes(SavedBytes);
    // 평균 비트레이트: 두 스트림이 모두 수신 중이던 구간 기준
    public double OrigAvgBps => _pairedSeconds > 0 ? _sumOrigBytes * 8 / _pairedSeconds : double.NaN;
    public double HecsAvgBps => _pairedSeconds > 0 ? _sumHecsBytes * 8 / _pairedSeconds : double.NaN;
    public string OrigAvgText => Fmt.RateText(OrigAvgBps);
    public string HecsAvgText => Fmt.RateText(HecsAvgBps);
    public string OrigAvgValue => Fmt.Rate(OrigAvgBps).Value;
    public string OrigAvgUnit => Fmt.Rate(OrigAvgBps).Unit;
    public string HecsAvgValue => Fmt.Rate(HecsAvgBps).Value;
    public string HecsAvgUnit => Fmt.Rate(HecsAvgBps).Unit;
    public double HecsAvgFraction => OrigAvgBps > 0 ? Math.Clamp(HecsAvgBps / OrigAvgBps, 0, 1) : 0;
    public string AvgSavingText => double.IsNaN(AvgRatio) ? "—" : ((1 - 1 / AvgRatio) * 100).ToString("0.0");
    public string MeasuredDurationText => Fmt.Duration(TimeSpan.FromSeconds(_pairedSeconds));
    public string OrigTotalText => Fmt.Bytes(OrigMeter.TotalBytes);
    public string HecsTotalText => Fmt.Bytes(HecsMeter.TotalBytes);

    public string OrigCodecText => _origInfo?.CodecLabel ?? "—";
    public string HecsCodecText => _hecsInfo?.CodecLabel ?? "—";
    public string OrigResText => _origInfo?.Resolution ?? "—";
    public string HecsResText => _hecsInfo?.Resolution ?? "—";
    public string OrigFpsText => _o.Fps > 0 ? _o.Fps.ToString("0.0") : "—";
    public string HecsFpsText => _h.Fps > 0 ? _h.Fps.ToString("0.0") : "—";
    public string OrigGopText => _o.Gop > 0 ? _o.Gop.ToString() : "—";
    public string HecsGopText => _h.Gop > 0 ? _h.Gop.ToString() : "—";
    public string HecsVideoInfo =>
        _hecsInfo == null ? "" : $"{_hecsInfo.CodecLabel} · {_hecsInfo.Resolution}" + (_h.Fps > 0 ? $" · {_h.Fps:0} fps" : "");

    // HECS 장비 화면과 같은 형식: "1920 × 1080 @ 30 fps · 931 kbps"
    public string InputLine => IoLine(_origInfo, _o);
    public string OutputLine => IoLine(_hecsInfo, _h);
    public string InputResFps => ResFps(_origInfo, _o);
    public string OutputResFps => ResFps(_hecsInfo, _h);
    /// <summary>HECS 출력 FPS가 원본보다 7% 이상 낮으면 프레임 손실로 표시.</summary>
    public bool FpsDrop => _o.Fps > 1 && _h.Fps > 0 && _h.Fps < _o.Fps * 0.93;
    public string OutputFpsKind => FpsDrop ? "Warn" : "Normal";
    public string FpsDropText => FpsDrop ? $"프레임 손실 {(1 - _h.Fps / _o.Fps) * 100:0}%" : "";
    public string HecsEndpoint => Endpoint(Config.HecsUrl);
    public string OrigEndpoint => Endpoint(Config.OriginalUrl);

    static string ResFps(StreamInfo i, MeterReading r) =>
        (i?.Width > 0 ? $"{i.Width} × {i.Height}" : "—") + (r.Fps > 0 ? $" @ {Math.Round(r.Fps):0} fps" : "");

    static string IoLine(StreamInfo i, MeterReading r) => $"{ResFps(i, r)} · {Fmt.RateText(r.Bps)}";

    static string Endpoint(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "URL 미설정";
        if (url.StartsWith("demo://", StringComparison.OrdinalIgnoreCase)) return "데모 스트림";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && !u.IsFile && u.Host.Length > 0)
            return u.IsDefaultPort || u.Port < 0 ? u.Host : $"{u.Host}:{u.Port}";
        return Path.GetFileName(url);
    }

    public string OrigUrlMasked => StreamSourceFactory.MaskUrl(Config.OriginalUrl);
    public string HecsUrlMasked => StreamSourceFactory.MaskUrl(Config.HecsUrl);

    /// <summary>Live / Alarm / Connecting / Offline / Disabled</summary>
    public string StatusKind
    {
        get
        {
            if (!Config.Enabled) return "Disabled";
            if (_origState == SourceState.Live && _hecsState == SourceState.Live) return _alarm ? "Alarm" : "Live";
            if (_origState == SourceState.Error || _hecsState == SourceState.Error || _origState == SourceState.Stopped || _hecsState == SourceState.Stopped)
                return "Offline";
            return "Connecting";
        }
    }

    public string StatusText => StatusKind switch
    {
        "Disabled" => "비활성",
        "Live" => "정상",
        "Alarm" => "압축률 저하",
        "Connecting" => "연결 중…",
        _ => string.IsNullOrWhiteSpace(Config.OriginalUrl) || string.IsNullOrWhiteSpace(Config.HecsUrl) ? "URL 미설정"
            : _origState != SourceState.Live && _hecsState != SourceState.Live ? $"원본·{Brand.Name} 끊김"
            : _origState != SourceState.Live ? "원본 스트림 끊김" : $"{Brand.Name} 스트림 끊김",
    };

    // ---------- 수명 ----------
    public static string Signature(ChannelConfig c, AppSettings s) =>
        string.Join("|", c.OriginalStreamUrl, c.HecsStreamUrl, c.Enabled, s.FfmpegPath, s.RtspTransport, s.HwAccel,
                    s.ThumbWidth, s.ThumbFps, s.ReconnectSec, s.StallTimeoutSec);

    public void UpdateConfig(ChannelConfig c)
    {
        Config = c;
        OnPropertyChanged(string.Empty);
    }

    public void Start(AppSettings s)
    {
        Stop();
        EngineSignature = Signature(Config, s);
        ResetStats();

        if (Config.Enabled)
        {
            int w = s.ThumbWidth & ~1, h = (w * 9 / 16) & ~1;
            _sink = new FrameSink(w, h);
            var baseOpt = new SourceOptions
            {
                FfmpegPath = s.FfmpegPath,
                RtspTransport = s.RtspTransport,
                HwAccel = s.HwAccel,
                ReconnectSec = s.ReconnectSec,
                StallSec = s.StallTimeoutSec,
            };
            _orig = Open(baseOpt.With(o => { o.Url = Config.OriginalStreamUrl; o.Measure = true; o.Decode = false; }), true);
            _hecs = Open(baseOpt.With(o => { o.Url = Config.HecsStreamUrl; o.Measure = true; o.Decode = false; }), false);
            if (!string.IsNullOrWhiteSpace(Config.HecsStreamUrl))
                _videoOpt = baseOpt.With(o =>
                {
                    o.Url = Config.HecsStreamUrl; o.Measure = false; o.Decode = true;
                    o.Width = w; o.Height = h; o.Fps = s.ThumbFps;
                });
            if (_videoWanted) StartVideo();
        }
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// 화면(현재 카드 페이지·목록 스크롤 영역)에 보이는지 알린다. 보이면 썸네일 디코딩을 시작하고,
    /// 안 보이게 되면 <see cref="VideoLingerSec"/>초 뒤 Tick()에서 멈춘다(<paramref name="linger"/>가 false면 바로). 측정 연결은 영향을 받지 않는다.
    /// </summary>
    public void SetVideoWanted(bool want, bool linger = true)
    {
        _videoWanted = want;
        if (want) { _videoIdleSince = null; StartVideo(); }
        else if (!linger) StopVideo();
        else _videoIdleSince ??= DateTime.Now;
    }

    void StartVideo()
    {
        if (_video != null || _videoOpt == null) return;
        var src = StreamSourceFactory.Create(_videoOpt);
        var sink = _sink;
        src.Frame += buf => sink.Submit(buf);
        src.StateChanged += (st, _) => Ui(() =>
        {
            if (!ReferenceEquals(src, _video) || st is SourceState.Live or SourceState.Connecting) return;
            if (_hasVideo) { _hasVideo = false; OnPropertyChanged(nameof(HasVideo)); }
        });
        _video = src;
        src.Start();
    }

    void StopVideo()
    {
        var v = _video;
        _video = null;
        v?.Dispose();
        if (_hasVideo) { _hasVideo = false; OnPropertyChanged(nameof(HasVideo)); }
    }

    IStreamSource Open(SourceOptions o, bool isOrig)
    {
        if (string.IsNullOrWhiteSpace(o.Url))
        {
            SetState(isOrig, SourceState.Stopped, "URL 미설정");
            return null;
        }
        var src = StreamSourceFactory.Create(o);
        var meter = isOrig ? OrigMeter : HecsMeter;
        src.Packet += meter.Add;
        src.Info += info => Ui(() =>
        {
            if (!IsCurrent(src)) return;
            if (isOrig) _origInfo = info; else _hecsInfo = info;
        });
        src.StateChanged += (st, msg) => Ui(() => { if (IsCurrent(src)) OnSourceState(isOrig, st, msg); });
        src.Start();
        return src;
    }

    public void Stop()
    {
        var o = _orig; var h = _hecs; var v = _video;
        _orig = _hecs = _video = null;
        _videoOpt = null;
        o?.Dispose(); h?.Dispose(); v?.Dispose();
        _origState = _hecsState = SourceState.Stopped;
        _origMsg = _hecsMsg = "중지됨";
        _hasVideo = false;
    }

    public void Reconnect()
    {
        Start(_main.Settings);
        _main.AddEvent("Info", Name, "수동 재연결", "Stream");
    }

    bool IsCurrent(IStreamSource s) => s != null && (ReferenceEquals(s, _orig) || ReferenceEquals(s, _hecs));

    static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    void SetState(bool isOrig, SourceState st, string msg)
    {
        if (isOrig) { _origState = st; _origMsg = msg; }
        else { _hecsState = st; _hecsMsg = msg; }
    }

    void OnSourceState(bool isOrig, SourceState st, string msg)
    {
        var prev = isOrig ? _origState : _hecsState;
        SetState(isOrig, st, msg);
        string who = isOrig ? "원본" : Brand.Name;

        if (st == SourceState.Live && prev != SourceState.Live)
        {
            _main.AddEvent("Ok", Name, $"{who} 스트림 수신 시작", "Stream");
            if (isOrig) _origErrLogged = false; else _hecsErrLogged = false;
        }
        else if (st == SourceState.Error)
        {
            ref bool logged = ref isOrig ? ref _origErrLogged : ref _hecsErrLogged;
            if (!logged)
            {
                logged = true;
                if (_main.Settings.AlarmOnOffline)
                    _main.AddEvent(prev == SourceState.Live ? "Alarm" : "Warn", Name, $"{who} 스트림 끊김 · {msg}", "Stream");
            }
            else
            {
                // 재연결 시도마다의 상세 오류는 DEBUG 수준
                _main.AddEvent("Debug", Name, $"{who} 재연결 실패 · {msg}", "FFmpeg");
            }
        }
        OnPropertyChanged(string.Empty);
    }

    void ResetStats()
    {
        OrigMeter.Reset(); HecsMeter.Reset();
        OrigHistory.Clear(); HecsHistory.Clear();
        _o = _h = default;
        _ratio = _minRatio = _maxRatio = double.NaN;
        _sumOrigBytes = _sumHecsBytes = 0;
        _pairedSeconds = 0;
        _lowSeconds = 0;
        _alarm = false;
        _origInfo = _hecsInfo = null;
        _origErrLogged = _hecsErrLogged = false;
        _csvO = _csvH = 0; _csvN = 0;
        _sink = null;
        HistoryVersion++;
    }

    // ---------- 주기 처리 ----------
    public void Tick(AppSettings s)
    {
        _o = OrigMeter.Read(s.MeasureWindowSec);
        _h = HecsMeter.Read(s.MeasureWindowSec);
        var o1 = OrigMeter.Read(1);
        var h1 = HecsMeter.Read(1);

        bool oLive = _origState == SourceState.Live && _o.Bps > 0;
        bool hLive = _hecsState == SourceState.Live && _h.Bps > 0;
        _ratio = oLive && hLive ? _o.Bps / _h.Bps : double.NaN;

        Push(OrigHistory, _origState == SourceState.Live ? o1.Bps : 0, s.HistorySeconds);
        Push(HecsHistory, _hecsState == SourceState.Live ? h1.Bps : 0, s.HistorySeconds);
        HistoryVersion++;

        if (oLive && hLive)
        {
            _sumOrigBytes += o1.Bps / 8;
            _sumHecsBytes += h1.Bps / 8;
            _pairedSeconds++;
            if (_pairedSeconds > s.MeasureWindowSec)
            {
                _minRatio = double.IsNaN(_minRatio) ? _ratio : Math.Min(_minRatio, _ratio);
                _maxRatio = double.IsNaN(_maxRatio) ? _ratio : Math.Max(_maxRatio, _ratio);
            }
        }

        // 압축률 하한 경보
        if (HasRatio)
        {
            if (_ratio < s.AlarmRatioMin)
            {
                _lowSeconds++;
                if (!_alarm && _lowSeconds >= s.AlarmHoldSec)
                {
                    _alarm = true;
                    _main.AddEvent("Alarm", Name, $"압축률 {Fmt.Ratio(_ratio)}:1 — 기준 {s.AlarmRatioMin:0.#}:1 미만이 {s.AlarmHoldSec}초 지속", "Alarm");
                }
            }
            else
            {
                if (_alarm) _main.AddEvent("Ok", Name, $"압축률 정상 복귀 ({Fmt.Ratio(_ratio)}:1)", "Alarm");
                _alarm = false;
                _lowSeconds = 0;
            }
        }

        if (oLive || hLive) { _csvO += _o.Bps; _csvH += _h.Bps; _csvN++; }

        if (!_videoWanted && _video != null && _videoIdleSince is { } idle && (DateTime.Now - idle).TotalSeconds >= VideoLingerSec)
            StopVideo();
        OnPropertyChanged(string.Empty);
    }

    public bool RenderTick()
    {
        if (_video == null || _sink == null || !_sink.Present()) return false;
        if (!_hasVideo)
        {
            _hasVideo = true;
            OnPropertyChanged(nameof(HasVideo));
        }
        return true;
    }

    /// <summary>CSV 기록용 구간 평균. 호출 시 누적값을 비운다.</summary>
    public string[] TakeCsvRow()
    {
        double o = _csvN > 0 ? _csvO / _csvN : double.NaN;
        double h = _csvN > 0 ? _csvH / _csvN : double.NaN;
        _csvO = _csvH = 0; _csvN = 0;
        double ratio = h > 0 ? o / h : double.NaN;
        return new[]
        {
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Name,
            CsvLogger.N(o / 1000), CsvLogger.N(h / 1000), CsvLogger.N(ratio, "0.00"),
            CsvLogger.N(double.IsNaN(ratio) ? double.NaN : (1 - 1 / ratio) * 100),
            CsvLogger.N(_o.Fps), CsvLogger.N(_h.Fps), StatusKind,
        };
    }

    static void Push(List<double> list, double v, int cap)
    {
        list.Add(v);
        if (list.Count > cap) list.RemoveRange(0, list.Count - cap);
    }
}
