using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HecsMonitor.Models;
using HecsMonitor.Services;
using Microsoft.Win32;

namespace HecsMonitor.ViewModels;

public sealed class EventEntry
{
    public DateTime Time { get; init; }
    public string Level { get; init; }   // Debug / Info / Ok / Warn / Alarm
    public string Category { get; init; }
    public string Channel { get; init; }
    public string Message { get; init; }
    public string TimeText => Time.ToString("HH:mm:ss");
}

public sealed class MainViewModel : ObservableObject
{
    readonly DispatcherTimer _tick;
    readonly DispatcherTimer _render;
    readonly DispatcherTimer _toastTimer;
    readonly DateTime _startTime = DateTime.Now;
    int _csvCounter;

    string _page = "dashboard";
    string _search = "";
    ChannelViewModel _selected;
    bool _detailOpen;
    string _compareMode = "wipe";
    string _toast;

    // 상세 화면 전용 고해상도 미리보기
    IStreamSource _detailOrig, _detailHecs;
    FrameSink _detailOrigSink, _detailHecsSink;
    string _detailOrigState = "", _detailHecsState = "";

    // 전체 합계
    double _totalOrig, _totalHecs;
    public List<double> TotalOrigHistory { get; } = new();
    public List<double> TotalHecsHistory { get; } = new();
    public int TotalHistoryVersion { get; private set; }

    public MainViewModel()
    {
        Settings = SettingsStore.Load();
        Brand.Set(Brand.Resolve(Settings.Brand, Settings.BrandBuild));
        ChannelsView = CollectionViewSource.GetDefaultView(Channels);
        ChannelsView.Filter = o => o is ChannelViewModel c && Matches(c);
        Editor = new SettingsEditorViewModel(this);

        OpenDetailCommand = new RelayCommand(p => OpenDetail(p as ChannelViewModel));
        CloseDetailCommand = new RelayCommand(CloseDetail);
        ReconnectCommand = new RelayCommand(p => (p as ChannelViewModel ?? _selected)?.Reconnect());
        ReconnectAllCommand = new RelayCommand(ReconnectAll);
        ToggleEventsCommand = new RelayCommand(() => { Settings.ShowEventPanel = !Settings.ShowEventPanel; SaveQuiet(); OnPropertyChanged(nameof(ShowEventPanel)); });
        ClearEventsCommand = new RelayCommand(() => Events.Clear());
        ExportReportCommand = new RelayCommand(ExportReport);
        GoSettingsCommand = new RelayCommand(() => Page = "settings");
        EditChannelCommand = new RelayCommand(p =>
        {
            var ch = p as ChannelViewModel ?? _selected;
            CloseDetail();
            Page = "settings";
            Editor.Section = "channels";
            if (ch != null) Editor.SelectById(ch.Config.Id);
        });
        OpenLogFolderCommand = new RelayCommand(() => OpenFolder(Settings.LogFolder));
        AddChannelCommand = new RelayCommand(() =>
        {
            if (!CheckChannelRoom(Settings.Channels.Count)) return;
            CloseDetail();
            Page = "settings";
            Editor.Section = "channels";
            Editor.AddCommand.Execute(null);
        });
        DuplicateChannelCommand = new RelayCommand(p => DuplicateChannel(p as ChannelViewModel));
        DeleteChannelCommand = new RelayCommand(p => DeleteChannel(p as ChannelViewModel));
        OpenConsoleCommand = new RelayCommand(() => OpenUrl(Settings.HecsConsoleUrl));
        PrevPageCommand = new RelayCommand(() => GoToPage(_cardPage - 1), _ => _cardPage > 0);
        NextPageCommand = new RelayCommand(() => GoToPage(_cardPage + 1), _ => _cardPage < Pages.Count - 1);
        GoPageCommand = new RelayCommand(p => { if (p is PageInfo pi) GoToPage(pi.Index); });
        ToggleHeroCommand = new RelayCommand(() => HeroCollapsed = !HeroCollapsed);

        foreach (var cfg in LicensedChannels())
        {
            var vm = new ChannelViewModel(this, cfg);
            Channels.Add(vm);
            vm.Start(Settings);
        }
        RebuildPages();
        WarnOverLicense();
        AddEvent("Info", "시스템", $"모니터링 시작 · 채널 {Channels.Count}개", "System");

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();

        _render = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _render.Tick += (_, _) => RenderTick();
        _render.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };
    }

    public AppSettings Settings { get; private set; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ICollectionView ChannelsView { get; }
    public ObservableCollection<EventEntry> Events { get; } = new();
    public SettingsEditorViewModel Editor { get; }

    public ICommand OpenDetailCommand { get; }
    public ICommand CloseDetailCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand ReconnectAllCommand { get; }
    public ICommand ToggleEventsCommand { get; }
    public ICommand ClearEventsCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand GoSettingsCommand { get; }
    public ICommand EditChannelCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand AddChannelCommand { get; }
    public ICommand DuplicateChannelCommand { get; }
    public ICommand DeleteChannelCommand { get; }
    public ICommand OpenConsoleCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand GoPageCommand { get; }
    public ICommand ToggleHeroCommand { get; }

    bool Matches(ChannelViewModel c) =>
        string.IsNullOrWhiteSpace(_search) ||
        c.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        c.Group.Contains(_search, StringComparison.OrdinalIgnoreCase);

    /// <summary>card | list</summary>
    public string ViewMode
    {
        get => Settings.ViewMode;
        set
        {
            if (Settings.ViewMode == value) return;
            Settings.ViewMode = value;
            SaveQuiet();
            OnPropertyChanged();
            UpdateVideoDemand();
        }
    }

    // ---------- 화면 상태 ----------
    public string Page
    {
        get => _page;
        set
        {
            if (_page == value) return;
            if (value == "settings") Editor.Load(Settings);
            _page = value;
            OnPropertyChanged();
            UpdateVideoDemand();
        }
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            ChannelsView.Refresh();
            _cardPage = 0;
            RebuildPages();
        }
    }

    public string CardSize
    {
        get => Settings.CardSize;
        set
        {
            if (Settings.CardSize == value) return;
            // 크기를 바꿔도 지금 보던 첫 채널이 들어 있는 페이지로 이동
            int first = _cardPage * PageSize;
            Settings.CardSize = value;
            SaveQuiet();
            _cardPage = first / PageSize;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CardSizeText));
            RebuildPages();
            UpdateCardLayout();
        }
    }

    public string CardSizeText => CardLayout.Describe(Settings.CardSize);
    public bool ShowEventPanel => Settings.ShowEventPanel;
    public bool HasNoChannels => Channels.Count == 0;
    public bool HasNoSearchResult => Channels.Count > 0 && PageItems.Count == 0;

    // ---------- 카드 페이지 ----------
    int _cardPage, _gridCols = 4, _gridRows = 2, _pageDirection = 1;
    double _areaW, _areaH, _tourElapsed, _tourProgress;
    double _cardWidth = 356, _thumbHeight = 189, _cardHeight = 413;
    bool _tourHover;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastRender;

    /// <summary>현재 페이지에 표시할 칸 (ChannelViewModel 또는 EmptySlot)</summary>
    public ObservableCollection<object> PageItems { get; } = new();
    public ObservableCollection<PageInfo> Pages { get; } = new();

    public int PageSize => CardLayout.PageSize(Settings.CardSize);
    public int CardPage => _cardPage;
    public int PageCount => Pages.Count;
    public bool HasPages => Pages.Count > 1;
    /// <summary>마지막 페이지 이동 방향 (+1 다음 / -1 이전) — 전환 애니메이션용</summary>
    public int PageDirection => _pageDirection;
    public int GridColumns => _gridCols;
    public int GridRows => _gridRows;
    /// <summary>카드를 그리는 기본 크기. 화면에서는 페이지 칸에 맞춰 같은 비율로 확대·축소된다.</summary>
    public double CardWidth => _cardWidth;
    public double ThumbHeight => _thumbHeight;
    public double CardHeight => _cardHeight;
    public string PageText => $"{_cardPage + 1} / {Math.Max(1, Pages.Count)}";
    public string PageRangeText => Pages.Count > 0 && _cardPage < Pages.Count ? Pages[_cardPage].RangeText : "—";
    public string ChannelCountText => $"{Channels.Count} / {AppSettings.MaxChannels}";
    public bool CanAddChannel => Channels.Count < AppSettings.MaxChannels;

    public bool HeroCollapsed
    {
        get => Settings.HeroCollapsed;
        set
        {
            if (Settings.HeroCollapsed == value) return;
            Settings.HeroCollapsed = value;
            SaveQuiet();
            OnPropertyChanged();
        }
    }

    /// <summary>자동 넘김 간격(초), 0 = 끔</summary>
    public int PageTourSec
    {
        get => Settings.PageTourSec;
        set
        {
            if (Settings.PageTourSec == value) return;
            Settings.PageTourSec = value;
            SaveQuiet();
            _tourElapsed = 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTourOn));
            UpdateVideoDemand();
        }
    }

    public bool IsTourOn => Settings.PageTourSec > 0;
    public double TourProgress => _tourProgress;
    /// <summary>카드 영역에 마우스가 있으면 자동 넘김을 멈춘다.</summary>
    public bool TourHover
    {
        get => _tourHover;
        set { if (Set(ref _tourHover, value)) OnPropertyChanged(nameof(IsTourPaused)); }
    }
    public bool IsTourPaused => IsTourOn && !TourRunning;
    bool TourRunning => IsTourOn && !_tourHover && !_detailOpen && _page == "dashboard" && ViewMode == "card" && Pages.Count > 1;

    /// <summary>카드 영역 크기가 바뀌면 화면에서 호출. 배치 모양과 카드 크기를 다시 정한다.</summary>
    public void SetCardArea(double width, double height)
    {
        if (Math.Abs(width - _areaW) < 1 && Math.Abs(height - _areaH) < 1) return;
        _areaW = width;
        _areaH = height;
        UpdateCardLayout();
    }

    void UpdateCardLayout()
    {
        if (_areaW <= 0 || _areaH <= 0) return;
        var r = CardLayout.Measure(Settings.CardSize, _areaW, _areaH);
        bool shape = r.Cols != _gridCols || r.Rows != _gridRows;
        _gridCols = r.Cols; _gridRows = r.Rows;
        _cardWidth = r.CardWidth;
        _thumbHeight = r.ThumbHeight;
        _cardHeight = r.CardHeight;
        if (shape) { OnPropertyChanged(nameof(GridColumns)); OnPropertyChanged(nameof(GridRows)); }
        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(ThumbHeight));
        OnPropertyChanged(nameof(CardHeight));
    }

    /// <summary>검색 결과를 페이지 크기로 나눠 페이지 독과 현재 페이지 칸을 다시 만든다.</summary>
    void RebuildPages()
    {
        var list = Channels.Where(Matches).ToList();
        int size = PageSize;
        int count = (list.Count + size - 1) / size;
        bool canAdd = string.IsNullOrWhiteSpace(_search) && Channels.Count < AppSettings.MaxChannels;

        Pages.Clear();
        for (int p = 0; p < count; p++)
        {
            var slots = new List<object>(list.Skip(p * size).Take(size));
            int filled = slots.Count;
            while (slots.Count < size)
                slots.Add(new EmptySlot
                {
                    IsAdd = canAdd && slots.Count == filled,   // 첫 빈 칸만 '채널 추가'
                    AddText = $"{Channels.Count} / {AppSettings.MaxChannels}",
                });
            Pages.Add(new PageInfo(p, slots, p * size + 1, p * size + filled));
        }
        _cardPage = Math.Clamp(_cardPage, 0, Math.Max(0, count - 1));
        ShowPage();
    }

    void GoToPage(int index)
    {
        if (Pages.Count == 0) return;
        index = Math.Clamp(index, 0, Pages.Count - 1);
        _tourElapsed = 0;
        if (index == _cardPage) return;
        _pageDirection = index > _cardPage ? 1 : -1;
        _cardPage = index;
        ShowPage();
        OnPropertyChanged(nameof(PageDirection));
    }

    void ShowPage()
    {
        PageItems.Clear();
        if (Pages.Count > 0)
            foreach (var s in Pages[_cardPage].Slots) PageItems.Add(s);
        foreach (var p in Pages) p.IsCurrent = p.Index == _cardPage;
        OnPropertyChanged(nameof(CardPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(PageRangeText));
        OnPropertyChanged(nameof(ChannelCountText));
        OnPropertyChanged(nameof(CanAddChannel));
        OnPropertyChanged(nameof(HasNoSearchResult));
        CommandManager.InvalidateRequerySuggested();
        UpdateVideoDemand();
    }

    // ---------- 썸네일 디코딩 대상 ----------
    HashSet<ChannelViewModel> _listVisible = new();

    /// <summary>목록 보기에서 스크롤 영역에 보이는 행. 화면(코드 비하인드)에서 스크롤·크기 변경 때 호출.</summary>
    public void SetListVisible(IEnumerable<ChannelViewModel> rows)
    {
        _listVisible = rows.ToHashSet();
        UpdateVideoDemand();
    }

    /// <summary>
    /// 썸네일은 화면에 보이는 채널만 디코딩한다 (카드 보기: 현재 페이지 + 자동 넘김 중이면 다음 페이지, 목록 보기: 보이는 행).
    /// 연결 상태·압축률 측정은 모든 채널이 계속한다. 자동 넘김 중에는 지나간 페이지가 곧 다시 오지 않으므로 바로 멈춘다.
    /// </summary>
    void UpdateVideoDemand()
    {
        var want = new HashSet<ChannelViewModel>();
        bool touring = false;
        if (_page == "dashboard")
        {
            if (ViewMode == "list") want.UnionWith(_listVisible);
            else if (Pages.Count > 0)
            {
                want.UnionWith(Pages[_cardPage].Slots.OfType<ChannelViewModel>());
                if (IsTourOn && Pages.Count > 1)
                {
                    touring = true;
                    want.UnionWith(Pages[(_cardPage + 1) % Pages.Count].Slots.OfType<ChannelViewModel>());
                }
            }
        }
        foreach (var c in Channels) c.SetVideoWanted(want.Contains(c), linger: !touring);
    }

    /// <summary>키보드·마우스 휠 페이지 이동 (+1 / -1). 처음·끝은 Home/End.</summary>
    public void StepPage(int delta) => GoToPage(_cardPage + delta);
    public void FirstPage() => GoToPage(0);
    public void LastPage() => GoToPage(Pages.Count - 1);

    void TourTick(double dt)
    {
        if (!IsTourOn) return;
        if (TourRunning)
        {
            _tourElapsed += dt;
            if (_tourElapsed >= Settings.PageTourSec)
            {
                int next = (_cardPage + 1) % Pages.Count;   // 마지막 페이지 다음은 처음으로
                _pageDirection = 1;
                _cardPage = next;
                _tourElapsed = 0;
                ShowPage();
                OnPropertyChanged(nameof(PageDirection));
            }
        }
        double progress = Math.Clamp(_tourElapsed / Settings.PageTourSec, 0, 1);
        if (Math.Abs(progress - _tourProgress) > 0.002 || progress == 0 && _tourProgress != 0)
        {
            _tourProgress = progress;
            OnPropertyChanged(nameof(TourProgress));
        }
        if (_tourPausedShown != IsTourPaused)
        {
            _tourPausedShown = IsTourPaused;
            OnPropertyChanged(nameof(IsTourPaused));
        }
    }

    bool _tourPausedShown;

    public string Toast
    {
        get => _toast;
        private set { Set(ref _toast, value); OnPropertyChanged(nameof(HasToast)); }
    }
    public bool HasToast => !string.IsNullOrEmpty(_toast);

    public void ShowToast(string text)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---------- 합계(히어로) ----------
    public string SavingPctText => _totalHecs > 0 ? ((1 - _totalHecs / _totalOrig) * 100).ToString("0.0") : "—";
    public string TotalRatioText => Fmt.Ratio(_totalHecs > 0 ? _totalOrig / _totalHecs : double.NaN);
    public string TotalOrigValue => Fmt.Rate(_totalOrig).Value;
    public string TotalOrigUnit => Fmt.Rate(_totalOrig).Unit;
    public string TotalHecsValue => Fmt.Rate(_totalHecs).Value;
    public string TotalHecsUnit => Fmt.Rate(_totalHecs).Unit;
    public string SavedBandwidthText => Fmt.RateText(_totalOrig - _totalHecs);
    public string TotalSavedText => Fmt.Bytes(Channels.Sum(c => c.SavedBytes));
    public string OnlineText => $"{Channels.Count(c => c.StatusKind is "Live" or "Alarm")} / {Channels.Count(c => c.Config.Enabled)}";
    public int AlarmCount => Channels.Count(c => c.AlarmActive || (c.Config.Enabled && c.StatusKind == "Offline"));
    public bool HasAlarm => AlarmCount > 0;
    public string UptimeText => Fmt.Duration(DateTime.Now - _startTime);
    public string ClockText => DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
    public int HistoryCapacity => Settings.HistorySeconds;
    public string HistoryLabel => Settings.HistorySeconds >= 120 ? $"최근 {Settings.HistorySeconds / 60}분" : $"최근 {Settings.HistorySeconds}초";

    static readonly string[] TickProps =
    {
        nameof(SavingPctText), nameof(TotalRatioText), nameof(TotalOrigValue), nameof(TotalOrigUnit),
        nameof(TotalHecsValue), nameof(TotalHecsUnit), nameof(SavedBandwidthText), nameof(TotalSavedText),
        nameof(OnlineText), nameof(AlarmCount), nameof(HasAlarm), nameof(UptimeText), nameof(ClockText),
        nameof(TotalHistoryVersion), nameof(DetailOrigState), nameof(DetailHecsState),
    };

    void Tick()
    {
        foreach (var c in Channels) c.Tick(Settings);

        var paired = Channels.Where(c => c.HasRatio).ToList();
        _totalOrig = paired.Sum(c => c.OrigBps);
        _totalHecs = paired.Sum(c => c.HecsBps);

        Push(TotalOrigHistory, Channels.Sum(c => c.OrigHistory.Count > 0 ? c.OrigHistory[^1] : 0));
        Push(TotalHecsHistory, Channels.Sum(c => c.HecsHistory.Count > 0 ? c.HecsHistory[^1] : 0));
        TotalHistoryVersion++;

        if (Settings.CsvLogging && ++_csvCounter >= Math.Max(5, Settings.CsvIntervalSec))
        {
            _csvCounter = 0;
            var rows = Channels.Where(c => c.Config.Enabled).Select(c => c.TakeCsvRow()).ToList();
            var s = Settings;
            Task.Run(() =>
            {
                var (over, deleted) = CsvLogger.EnforceQuota(s.LogFolder, s.LogMaxMB, s.LogOverflow);
                Application.Current?.Dispatcher.BeginInvoke(() => OnQuotaChecked(over, deleted));
                if (!over || s.LogOverflow == "delete") CsvLogger.AppendMeasurement(s.LogFolder, rows);
            });
        }

        foreach (var p in Pages) p.Refresh();
        foreach (var p in TickProps) OnPropertyChanged(p);
    }

    void OnQuotaChecked(bool over, int deleted)
    {
        if (deleted > 0)
            AddEvent("Info", "기록", $"저장 용량 {Settings.LogMaxMB} MB 초과 · 오래된 기록 파일 {deleted}개 삭제", "Measure");
        if (over && Settings.LogOverflow != "delete")
        {
            if (!_logStopped) AddEvent("Warn", "기록", $"저장 용량 {Settings.LogMaxMB} MB 초과 · 측정 기록을 중지했습니다", "Measure");
            _logStopped = true;
        }
        else _logStopped = false;
    }

    bool _logStopped;

    void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > Settings.HistorySeconds) list.RemoveRange(0, list.Count - Settings.HistorySeconds);
    }

    void RenderTick()
    {
        foreach (var c in Channels) c.RenderTick();
        _detailOrigSink?.Present();
        _detailHecsSink?.Present();

        double now = _clock.Elapsed.TotalSeconds;
        TourTick(Math.Min(now - _lastRender, 0.5));
        _lastRender = now;
    }

    // ---------- 상세(비교) 화면 ----------
    public ChannelViewModel SelectedChannel { get => _selected; private set => Set(ref _selected, value); }
    public bool IsDetailOpen { get => _detailOpen; private set => Set(ref _detailOpen, value); }
    public WriteableBitmap DetailOrigBitmap => _detailOrigSink?.Bitmap;
    public WriteableBitmap DetailHecsBitmap => _detailHecsSink?.Bitmap;
    public string DetailOrigState => _detailOrigState;
    public string DetailHecsState => _detailHecsState;

    public string CompareMode
    {
        get => _compareMode;
        set => Set(ref _compareMode, value);
    }

    void OpenDetail(ChannelViewModel ch)
    {
        if (ch == null) return;
        StopDetailSources();
        SelectedChannel = ch;
        IsDetailOpen = true;
        if (!ch.Config.Enabled) return;

        const int w = 960, h = 540;
        _detailOrigSink = new FrameSink(w, h);
        _detailHecsSink = new FrameSink(w, h);
        var opt = new SourceOptions
        {
            FfmpegPath = Settings.FfmpegPath,
            RtspTransport = Settings.RtspTransport,
            HwAccel = Settings.HwAccel,
            Measure = false,
            Decode = true,
            Width = w, Height = h, Fps = 15,
            ReconnectSec = Settings.ReconnectSec,
            StallSec = Settings.StallTimeoutSec,
        };
        _detailOrig = OpenPreview(opt.With(o => o.Url = ch.Config.OriginalStreamUrl), _detailOrigSink, true);
        _detailHecs = OpenPreview(opt.With(o => o.Url = ch.Config.HecsStreamUrl), _detailHecsSink, false);
        OnPropertyChanged(nameof(DetailOrigBitmap));
        OnPropertyChanged(nameof(DetailHecsBitmap));
    }

    IStreamSource OpenPreview(SourceOptions o, FrameSink sink, bool isOrig)
    {
        if (string.IsNullOrWhiteSpace(o.Url)) return null;
        var src = StreamSourceFactory.Create(o);
        src.Frame += sink.Submit;
        src.StateChanged += (st, msg) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(src, isOrig ? _detailOrig : _detailHecs)) return;
            var text = st == SourceState.Live ? "" : msg;
            if (isOrig) _detailOrigState = text; else _detailHecsState = text;
        });
        if (isOrig) _detailOrigState = "연결 중"; else _detailHecsState = "연결 중";
        src.Start();
        return src;
    }

    void StopDetailSources()
    {
        _detailOrig?.Dispose(); _detailHecs?.Dispose();
        _detailOrig = _detailHecs = null;
        _detailOrigSink = _detailHecsSink = null;
        _detailOrigState = _detailHecsState = "";
    }

    public void CloseDetail()
    {
        StopDetailSources();
        IsDetailOpen = false;
        SelectedChannel = null;
        OnPropertyChanged(nameof(DetailOrigBitmap));
        OnPropertyChanged(nameof(DetailHecsBitmap));
    }

    // ---------- 설정 적용 ----------
    /// <param name="fromSettings">설정 화면의 ‘저장 및 적용’이면 결과를 모달 창으로 보여 준다.</param>
    public void ApplySettings(AppSettings draft, bool fromSettings = false)
    {
        Settings = SettingsStore.Clone(draft);
        Brand.Set(Brand.Resolve(Settings.Brand, Settings.BrandBuild));
        string saveError = null;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex)
        {
            saveError = ex.Message;
            AddEvent("Warn", "시스템", "설정 저장 실패 · " + ex.Message, "System");
        }

        var existing = Channels.ToDictionary(c => c.Config.Id);
        var next = new List<ChannelViewModel>();
        int added = 0, restarted = 0;
        foreach (var cfg in LicensedChannels())
        {
            if (existing.Remove(cfg.Id, out var vm) && vm.EngineSignature == ChannelViewModel.Signature(cfg, Settings))
            {
                vm.UpdateConfig(cfg);   // 이름/그룹만 바뀐 경우: 측정 이력 유지
                next.Add(vm);
                continue;
            }
            if (vm == null) added++; else restarted++;
            vm?.Stop();
            vm = new ChannelViewModel(this, cfg);
            vm.Start(Settings);
            next.Add(vm);
        }
        int removed = existing.Count;
        foreach (var gone in existing.Values) gone.Stop();
        if (_selected != null && !next.Contains(_selected)) CloseDetail();

        Channels.Clear();
        foreach (var vm in next) Channels.Add(vm);
        RebuildPages();
        UpdateCardLayout();

        OnPropertyChanged(nameof(CardSize));
        OnPropertyChanged(nameof(CardSizeText));
        OnPropertyChanged(nameof(HeroCollapsed));
        OnPropertyChanged(nameof(PageTourSec));
        OnPropertyChanged(nameof(IsTourOn));
        OnPropertyChanged(nameof(ShowEventPanel));
        OnPropertyChanged(nameof(HasNoChannels));
        OnPropertyChanged(nameof(HistoryCapacity));
        OnPropertyChanged(nameof(HistoryLabel));
        OnPropertyChanged(nameof(ViewMode));
        _logStopped = false;
        AddEvent("Info", "시스템", $"설정 적용 · 채널 {Channels.Count}개", "System");
        WarnOverLicense();

        if (!fromSettings)
        {
            ShowToast("설정이 저장되고 적용되었습니다");
            return;
        }
        var changes = new List<string>();
        if (added > 0) changes.Add($"추가 {added}개");
        if (removed > 0) changes.Add($"삭제 {removed}개");
        if (restarted > 0) changes.Add($"다시 연결 {restarted}개");
        ShowDialog(
            saveError == null ? "설정이 저장되었습니다" : "설정 파일 저장에 실패했습니다",
            saveError == null
                ? $"변경 사항이 바로 적용되었습니다.\n채널 {Channels.Count}개" + (changes.Count > 0 ? " · " + string.Join(" · ", changes) : " · 연결 변경 없음")
                : $"변경 사항은 이번 실행에만 적용됩니다.\n{saveError}",
            saveError == null ? "Ok" : "Alarm");
    }

    // ---------- 중앙 모달 ----------
    bool _dialogOpen;
    string _dialogTitle, _dialogMessage, _dialogKind = "Ok";

    public bool IsDialogOpen { get => _dialogOpen; private set => Set(ref _dialogOpen, value); }
    public string DialogTitle { get => _dialogTitle; private set => Set(ref _dialogTitle, value); }
    public string DialogMessage { get => _dialogMessage; private set => Set(ref _dialogMessage, value); }
    /// <summary>Ok(초록 체크) / Alarm(빨강 경고)</summary>
    public string DialogKind { get => _dialogKind; private set => Set(ref _dialogKind, value); }
    public ICommand CloseDialogCommand => _closeDialog ??= new RelayCommand(() => IsDialogOpen = false);
    ICommand _closeDialog;

    public void ShowDialog(string title, string message, string kind = "Ok")
    {
        DialogTitle = title;
        DialogMessage = message;
        DialogKind = kind;
        IsDialogOpen = true;
    }

    // ---------- 개발자 모드 ----------
    // 저장하지 않는다: 프로그램을 다시 실행하거나 재설치하면 항상 꺼진 상태로 시작
    bool _developerMode;
    public bool DeveloperMode => _developerMode;

    public void SetDeveloperMode(bool on)
    {
        _developerMode = on;
        OnPropertyChanged(nameof(DeveloperMode));
        AddEvent("Info", "시스템", on ? "개발자 모드 활성화" : "개발자 모드 해제", "System");
    }

    /// <summary>
    /// 제품 이름(HECS / ECO)을 바꾸고 바로 저장한다. 같은 빌드에서는 개발자 모드를 끄거나 다시 실행해도 유지되고,
    /// 새 설치 패키지로 실행하면 그 패키지의 기본값으로 돌아간다.
    /// </summary>
    public void SetBrand(string name)
    {
        name = Brand.Normalize(name);
        if (name == null || name == Brand.Name) return;
        Settings.Brand = name;
        Settings.BrandBuild = Brand.BuildId;
        SaveQuiet();
        Brand.Set(name);
        AddEvent("Info", "시스템", $"제품 이름 변경 · {name}", "System");
    }

    void ReconnectAll()
    {
        foreach (var c in Channels) c.Start(Settings);
        AddEvent("Info", "시스템", "전체 채널 재연결", "Stream");
        ShowToast("전체 채널을 다시 연결합니다");
    }

    // ---------- 메인 화면의 채널 작업 (복제 / 삭제) ----------
    /// <summary>채널이 최대 개수면 안내 창을 띄우고 false.</summary>
    public bool CheckChannelRoom(int current, int adding = 1)
    {
        if (current + adding <= AppSettings.MaxChannels) return true;
        ShowDialog("채널을 더 추가할 수 없습니다",
            $"라이선스에 따라 채널은 최대 {AppSettings.MaxChannels}개까지 등록할 수 있습니다 (현재 {current}개).\n사용하지 않는 채널을 삭제하거나 라이선스를 확장하세요.",
            "Alarm");
        return false;
    }

    /// <summary>라이선스 채널 수까지만 감시한다. 넘는 채널은 설정에 남겨 두되 연결하지 않는다.</summary>
    IEnumerable<ChannelConfig> LicensedChannels() => Settings.Channels.Take(AppSettings.MaxChannels);

    void WarnOverLicense()
    {
        int over = Settings.Channels.Count - AppSettings.MaxChannels;
        if (over > 0)
            AddEvent("Alarm", "라이선스", $"허가 채널 수({AppSettings.MaxChannels}개)를 넘는 채널 {over}개는 감시하지 않습니다", "System");
    }

    void DuplicateChannel(ChannelViewModel ch)
    {
        if (ch == null || !CheckChannelRoom(Settings.Channels.Count)) return;
        var s = SettingsStore.Clone(Settings);
        int i = s.Channels.FindIndex(c => c.Id == ch.Config.Id);
        var src = s.Channels[i];
        s.Channels.Insert(i + 1, new ChannelConfig
        {
            Name = src.Name + " (복사)", Group = src.Group, OriginalUrl = src.OriginalUrl, HecsUrl = src.HecsUrl, Enabled = src.Enabled,
            OrigUser = src.OrigUser, OrigPassword = src.OrigPassword, HecsUser = src.HecsUser, HecsPassword = src.HecsPassword,
        });
        ApplySettings(s);
    }

    void DeleteChannel(ChannelViewModel ch)
    {
        if (ch == null) return;
        var answer = MessageBox.Show($"'{ch.Name}' 채널을 삭제할까요?\n측정 기록 파일은 삭제되지 않습니다.", "채널 삭제",
                                     MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        var s = SettingsStore.Clone(Settings);
        s.Channels.RemoveAll(c => c.Id == ch.Config.Id);
        ApplySettings(s);
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public void Shutdown()
    {
        _tick.Stop();
        _render.Stop();
        StopDetailSources();
        foreach (var c in Channels) c.Stop();
    }

    void SaveQuiet()
    {
        try { SettingsStore.Save(Settings); } catch { }
    }

    // ---------- 이벤트 / 보고서 ----------
    static int Rank(string level) => level switch
    {
        "Debug" or "DEBUG" => 0,
        "Info" or "Ok" or "INFO" => 1,
        "Warn" or "WARN" => 2,
        _ => 3,   // Alarm / ERROR
    };

    /// <summary>분류별 기록 수준(설정 → 알림·기록 → 로그 레벨) 미만의 이벤트는 버린다.</summary>
    public void AddEvent(string level, string channel, string message, string category = "System")
    {
        if (Rank(level) < Rank(Settings.LevelOf(category))) return;
        var e = new EventEntry { Time = DateTime.Now, Level = level, Category = category, Channel = channel, Message = message };
        Events.Insert(0, e);
        while (Events.Count > 500) Events.RemoveAt(Events.Count - 1);
        if (Settings.CsvLogging && !_logStopped)
        {
            var folder = Settings.LogFolder;
            Task.Run(() => CsvLogger.AppendEvent(folder, e.Time, level.ToUpperInvariant(), $"{category}/{channel}", message));
        }
    }

    void ExportReport()
    {
        var dlg = new SaveFileDialog
        {
            Title = "압축 효율 보고서 저장",
            Filter = "CSV 파일 (*.csv)|*.csv",
            FileName = $"{Brand.Name}_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        string b = Brand.Name;
        string[] header =
        {
            "채널", "그룹", "상태", "원본 코덱", "원본 해상도", $"{b} 코덱", $"{b} 해상도",
            "현재 원본(kbps)", $"현재 {b}(kbps)", "현재 압축률", "평균 압축률", "최소 압축률", "최대 압축률",
            "현재 절감률(%)", "절약 데이터", "원본 수신 누적", $"{b} 수신 누적",
        };
        var rows = Channels.Select(c => new[]
        {
            c.Name, c.Group, c.StatusText, c.OrigCodecText, c.OrigResText, c.HecsCodecText, c.HecsResText,
            CsvLogger.N(c.OrigBps / 1000), CsvLogger.N(c.HecsBps / 1000), c.RatioText, c.AvgRatioText,
            c.MinRatioText, c.MaxRatioText, c.SavingPreciseText, c.SavedText, c.OrigTotalText, c.HecsTotalText,
        });
        try
        {
            CsvLogger.WriteTable(dlg.FileName, header, rows);
            ShowToast("보고서를 저장했습니다");
        }
        catch (Exception ex)
        {
            ShowToast("보고서 저장 실패 · " + ex.Message);
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
