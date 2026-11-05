using System.Collections.ObjectModel;
using System.Windows.Input;
using HecsMonitor.Licensing;
using HecsMonitor.Models;
using HecsMonitor.Services;
using Microsoft.Win32;

namespace HecsMonitor.ViewModels;

/// <summary>설정 화면. 사본(Draft)을 편집하고 "적용" 시에만 실제 설정에 반영한다.</summary>
public sealed class SettingsEditorViewModel : ObservableObject
{
    readonly MainViewModel _main;
    ChannelConfig _selected;
    string _section = "channels";
    string _origTest, _hecsTest, _ffmpegTest;

    public SettingsEditorViewModel(MainViewModel main)
    {
        _main = main;
        Channels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ChannelCountText));
        AddCommand = new RelayCommand(Add);
        DuplicateCommand = new RelayCommand(Duplicate, _ => _selected != null);
        DeleteCommand = new RelayCommand(Delete, _ => _selected != null);
        MoveUpCommand = new RelayCommand(() => Move(-1), _ => _selected != null && Channels.IndexOf(_selected) > 0);
        MoveDownCommand = new RelayCommand(() => Move(1), _ => _selected != null && Channels.IndexOf(_selected) < Channels.Count - 1);
        TestOriginalCommand = new RelayCommand(async () => { OrigTest = "⏳ 연결 테스트 중… (최대 20초)"; OrigTest = await ProbeService.TestUrlAsync(_selected?.OriginalStreamUrl, Draft); });
        TestHecsCommand = new RelayCommand(async () => { HecsTest = "⏳ 연결 테스트 중… (최대 20초)"; HecsTest = await ProbeService.TestUrlAsync(_selected?.HecsStreamUrl, Draft); });
        ResolveOriginalCommand = new RelayCommand(async () => await ResolveOriginalAsync(), _ => _selected != null && !_resolving);
        ApplyAccountToAllCommand = new RelayCommand(ApplyAccountToAll, _ => _selected != null);
        CheckFfmpegCommand = new RelayCommand(async () => { FfmpegTest = "⏳ 확인 중…"; FfmpegTest = await ProbeService.CheckFfmpegAsync(Draft.FfmpegPath); });
        BrowseFfmpegCommand = new RelayCommand(BrowseFfmpeg);
        BrowseLogFolderCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog { Title = "기록 폴더 선택", InitialDirectory = Draft.LogFolder };
            if (dlg.ShowDialog() == true) { Draft.LogFolder = dlg.FolderName; OnPropertyChanged(nameof(Draft)); }
        });
        OpenLogFolderCommand = new RelayCommand(() => MainViewModel.OpenFolder(Draft.LogFolder));
        ApplyCommand = new RelayCommand(Apply);
        RevertCommand = new RelayCommand(() => { Load(_main.Settings); _main.ShowToast("변경 사항을 되돌렸습니다"); });

        ScanCommand = new RelayCommand(async () => await ScanAsync(), _ => !_scanning);
        AddDeviceCommand = new RelayCommand(async p => await AddDevice(p as DeviceItem), p => p is DeviceItem { IsBusy: false });
        ApplyToOriginalCommand = new RelayCommand(async p => await ApplyDevice(p as DeviceItem, hecs: false), p => _selected != null && p is DeviceItem { IsBusy: false });
        ApplyToHecsCommand = new RelayCommand(async p => await ApplyDevice(p as DeviceItem, hecs: true), p => _selected != null && p is DeviceItem { IsBusy: false });
        LookupDeviceCommand = new RelayCommand(async p => await LookupAsync(p as DeviceItem), p => p is DeviceItem { IsBusy: false });
        FillDefaultAccountCommand = new RelayCommand(() =>
        {
            foreach (var d in Devices) { d.User = Draft.OnvifUser; d.Password = Draft.OnvifPassword; }
            _main.ShowToast($"기본 계정을 검색된 카메라 {Devices.Count}대에 채웠습니다");
        });
        BulkGenerateCommand = new RelayCommand(BulkGenerate);
        OpenConsoleCommand = new RelayCommand(() => MainViewModel.OpenUrl(Draft.HecsConsoleUrl));
        RefreshStorageCommand = new RelayCommand(RefreshStorage);
    }

    // ---------- ONVIF 카메라 검색 ----------
    bool _scanning;
    string _scanStatus = "검색 버튼을 누르면 같은 네트워크의 ONVIF 카메라를 찾습니다 (약 3초).";

    public ObservableCollection<DeviceItem> Devices { get; } = new();
    public bool IsScanning { get => _scanning; private set => Set(ref _scanning, value); }
    public string ScanStatus { get => _scanStatus; private set => Set(ref _scanStatus, value); }
    public ICommand ScanCommand { get; }
    public ICommand AddDeviceCommand { get; }
    public ICommand ApplyToOriginalCommand { get; }
    public ICommand ApplyToHecsCommand { get; }
    public ICommand LookupDeviceCommand { get; }
    public ICommand FillDefaultAccountCommand { get; }

    async Task ScanAsync()
    {
        IsScanning = true;
        ScanStatus = "검색 중…";
        Devices.Clear();
        try
        {
            var list = await OnvifDiscovery.DiscoverAsync();
            foreach (var d in list)
            {
                // 이 카메라로 확인된 계정이 있으면 그것을, 없으면 기본 계정을 채운다
                var saved = Draft.AccountFor(d.Ip);
                Devices.Add(saved != null
                    ? new DeviceItem(d, saved.User, saved.Password, saved: true)
                    : new DeviceItem(d, Draft.OnvifUser, Draft.OnvifPassword, saved: false));
            }
            ScanStatus = list.Count == 0
                ? "찾은 카메라가 없습니다. 카메라의 ONVIF 기능과 방화벽(UDP 3702)을 확인하세요."
                : $"카메라 {list.Count}대를 찾았습니다. 카메라마다 ID/비밀번호를 확인한 뒤 대상 채널의 원본 또는 {Brand.Name} 출력으로 지정하세요.";
        }
        catch (Exception ex)
        {
            ScanStatus = "검색 실패 · " + ex.Message;
        }
        finally { IsScanning = false; }
    }

    /// <summary>카메라에 ONVIF로 RTSP 주소를 물어본다. 실패하면 (null, 오류 메시지).</summary>
    async Task<(string Uri, string Info)> QueryStreamAsync(string xaddr, string user, string password)
    {
        try
        {
            var streams = await OnvifClient.GetStreamsAsync(xaddr, user, password);
            if (streams.Count == 0) return (null, "카메라가 비디오 스트림 주소를 알려주지 않았습니다");
            var s = streams[0];
            var (clean, _, _) = StreamUrl.SplitCredentials(s.Uri);
            return (clean, $"{s.ProfileName} · {s.Encoding} {s.Width}×{s.Height}");
        }
        catch (Exception ex)
        {
            return (null, ex is System.Net.Http.HttpRequestException or TaskCanceledException ? "ONVIF 응답 없음 · " + ex.Message : ex.Message);
        }
    }

    /// <summary>행의 계정으로 RTSP 주소를 조회하고 결과를 행에 표시한다. 성공한 계정은 IP별로 기억한다.</summary>
    async Task<string> LookupAsync(DeviceItem d)
    {
        if (d == null) return null;
        d.IsBusy = true;
        d.Status = "RTSP 주소 조회 중…";
        d.StatusKind = "Info";
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var (uri, info) = await QueryStreamAsync(d.Device.XAddr, d.User, d.Password);
            if (uri == null)
            {
                d.StreamUri = null;
                d.Status = "✖ " + info;
                d.StatusKind = "Alarm";
                return null;
            }
            d.StreamUri = uri;
            d.Status = $"✔ {uri}  ({info})";
            d.StatusKind = "Ok";
            Draft.RememberAccount(d.Ip, d.User, d.Password);
            return uri;
        }
        finally
        {
            d.IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    async Task AddDevice(DeviceItem d)
    {
        if (d == null || !_main.CheckChannelRoom(Channels.Count)) return;
        var uri = await LookupAsync(d);
        if (uri == null) return;   // 주소를 못 받으면 채널을 만들지 않고 행에 원인을 보여 준다
        var c = new ChannelConfig
        {
            Name = d.Name,
            Group = d.Device.Model,
            OriginalUrl = uri,
            OrigUser = d.User,
            OrigPassword = d.Password,
            HecsUrl = "",
        };
        Channels.Add(c);
        SelectedChannel = c;   // 새 채널이 대상 채널이 되므로 바로 HECS 출력을 지정할 수 있다
        _main.ShowToast($"'{c.Name}' 채널을 추가했습니다. 이어서 {Brand.Name} 출력 카메라에서 ‘→ {Brand.Name}’를 누르세요.");
    }

    /// <summary>대상 채널의 원본 또는 HECS 출력으로 지정. 화면은 그대로 두어 이어서 다른 쪽도 지정할 수 있다.</summary>
    async Task ApplyDevice(DeviceItem d, bool hecs)
    {
        if (d == null || _selected == null) return;
        var target = _selected;
        var uri = await LookupAsync(d);
        if (uri == null) return;
        if (hecs)
        {
            target.HecsUrl = uri;
            target.HecsUser = d.User;
            target.HecsPassword = d.Password;
            HecsTest = $"✔ ONVIF로 주소를 받았습니다 · {d.Name}";
        }
        else
        {
            target.OriginalUrl = uri;
            target.OrigUser = d.User;
            target.OrigPassword = d.Password;
            OrigTest = $"✔ ONVIF로 주소를 받았습니다 · {d.Name}";
        }
        _main.ShowToast($"'{target.Name}'의 {(hecs ? $"{Brand.Name} 출력" : "원본 카메라")}으로 지정했습니다");
    }

    // ---------- 채널 편집: ONVIF 주소 조회 / 계정 일괄 적용 ----------
    bool _resolving;
    public ICommand ResolveOriginalCommand { get; }
    public ICommand ApplyAccountToAllCommand { get; }

    /// <summary>원본 URL의 IP로 ONVIF 장치 서비스에 접속해 정확한 RTSP 주소를 받아 온다.</summary>
    async Task ResolveOriginalAsync()
    {
        var c = _selected;
        var (clean, _, _) = StreamUrl.SplitCredentials(c.OriginalUrl);
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host))
        {
            OrigTest = "✖ 원본 URL에 카메라 IP를 먼저 입력하세요 (예: rtsp://192.168.0.10/)";
            return;
        }
        _resolving = true;
        OrigTest = $"⏳ {u.Host} 에 ONVIF로 RTSP 주소를 조회하는 중…";
        try
        {
            // 채널 계정 → 이 카메라로 확인된 계정 → 기본 계정 순
            var saved = Draft.AccountFor(u.Host);
            string user = !string.IsNullOrEmpty(c.OrigUser) ? c.OrigUser : saved?.User ?? Draft.OnvifUser;
            string pass = !string.IsNullOrEmpty(c.OrigUser) ? c.OrigPassword : saved?.Password ?? Draft.OnvifPassword;
            var (uri, info) = await QueryStreamAsync($"http://{u.Host}/onvif/device_service", user, pass);
            if (uri == null) { OrigTest = "✖ ONVIF 주소 조회 실패 · " + info; return; }
            c.OriginalUrl = uri;
            if (string.IsNullOrEmpty(c.OrigUser)) { c.OrigUser = user; c.OrigPassword = pass; }
            Draft.RememberAccount(u.Host, user, pass);
            OrigTest = $"✔ RTSP 주소를 받았습니다 · {info}";
        }
        finally { _resolving = false; }
    }

    void ApplyAccountToAll()
    {
        var src = _selected;
        int n = 0;
        foreach (var c in Channels)
        {
            if (ReferenceEquals(c, src) || c.OriginalUrl.StartsWith("demo://", StringComparison.OrdinalIgnoreCase)) continue;
            c.OrigUser = src.OrigUser;
            c.OrigPassword = src.OrigPassword;
            n++;
        }
        _main.ShowToast($"원본 카메라 계정을 채널 {n}개에 적용했습니다. ‘저장 및 적용’을 눌러 반영하세요.");
    }

    // ---------- HECS 채널 일괄 등록 ----------
    public ICommand BulkGenerateCommand { get; }

    public string BulkPreview
    {
        get
        {
            if (Draft == null) return "";
            var items = Enumerable.Range(0, Math.Clamp(Draft.BulkCount, 0, 3)).Select(BulkUrl);
            return string.Join("   ", items) + (Draft.BulkCount > 3 ? "   …" : "");
        }
    }

    string BulkUrl(int i) => (Draft.BulkUrlTemplate ?? "")
        .Replace("{ip}", Draft.BulkIp?.Trim())
        .Replace("{port}", (Draft.BulkStartPort + i * Draft.BulkPortStep).ToString())
        .Replace("{n}", (i + 1).ToString());

    string BulkName(int i) => (Draft.BulkNameTemplate ?? "{n}").Replace("{n}", (i + 1).ToString());

    void BulkGenerate()
    {
        int room = AppSettings.MaxChannels - Channels.Count;
        if (room <= 0) { _main.CheckChannelRoom(Channels.Count); return; }
        int want = Math.Clamp(Draft.BulkCount, 1, AppSettings.MaxChannels);
        int n = Math.Min(want, room);
        ChannelConfig first = null;
        for (int i = 0; i < n; i++)
        {
            var c = new ChannelConfig { Name = BulkName(i), Group = Draft.BulkIp, OriginalUrl = "rtsp://", HecsUrl = BulkUrl(i) };
            Channels.Add(c);
            first ??= c;
        }
        SelectedChannel = first;
        _main.ShowToast(n < want
            ? $"최대 {AppSettings.MaxChannels}채널까지라 {want}개 중 {n}개만 만들었습니다. 각 채널의 원본 카메라 URL을 입력한 뒤 저장하세요."
            : $"{Brand.Name} 채널 {n}개를 만들었습니다. 각 채널의 원본 카메라 URL을 입력한 뒤 저장하세요.");
    }

    public void RaiseBulkPreview() => OnPropertyChanged(nameof(BulkPreview));

    // ---------- 개발자 모드 (버전 5회 클릭) ----------
    const int DevClicks = 5;
    int _versionClicks;
    DateTime _lastVersionClick;

    /// <summary>켜져 있으면 설정 화면의 상세 설명을 표시한다.</summary>
    public bool DeveloperMode => _main.DeveloperMode;

    /// <summary>2초 안에 이어서 5번 누르면 개발자 모드를 켜거나 끈다.</summary>
    public void OnVersionClicked()
    {
        var now = DateTime.Now;
        _versionClicks = (now - _lastVersionClick).TotalSeconds <= 2 ? _versionClicks + 1 : 1;
        _lastVersionClick = now;

        int left = DevClicks - _versionClicks;
        if (left > 0)
        {
            if (_versionClicks >= 3)
                _main.ShowToast(DeveloperMode ? $"개발자 모드 해제까지 {left}번 남았습니다" : $"개발자 모드까지 {left}번 남았습니다");
            return;
        }

        _versionClicks = 0;
        bool on = !DeveloperMode;
        _main.SetDeveloperMode(on);
        OnPropertyChanged(nameof(DeveloperMode));
        _main.ShowToast(on ? "개발자 모드가 활성화되었습니다 · 설정 메뉴에 상세 설명이 표시됩니다"
                           : "개발자 모드가 해제되었습니다");
    }

    // ---------- 제품 이름 (개발자 모드에서 선택) ----------
    /// <summary>HECS / ECO. 고르는 즉시 저장·적용되며 개발자 모드를 꺼도 유지된다.</summary>
    public string BrandName
    {
        get => Brand.Name;
        set
        {
            if (Brand.Normalize(value) is not { } name || name == Brand.Name) return;
            _main.SetBrand(name);
            if (Draft != null) { Draft.Brand = name; Draft.BrandBuild = Brand.BuildId; }   // ‘저장 및 적용’ 때 되돌아가지 않도록
            OnPropertyChanged();
            _main.ShowToast($"제품 이름을 {name}(으)로 바꿨습니다 · 개발자 모드를 꺼도 유지됩니다");
        }
    }

    // ---------- 저장 공간 / 시스템 정보 ----------
    string _storageText, _diskText, _ffmpegVersion = "확인 중…";
    public string StorageText { get => _storageText; private set => Set(ref _storageText, value); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }
    public string FfmpegVersion { get => _ffmpegVersion; private set => Set(ref _ffmpegVersion, value); }
    public ICommand RefreshStorageCommand { get; }
    public ICommand OpenConsoleCommand { get; }

    public string AppVersion => typeof(SettingsEditorViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string OsText => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public string RuntimeText => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string SettingsPath => Path.Combine(SettingsStore.Folder, "settings.json");
    public string MachineText => $"{Environment.MachineName} · CPU {Environment.ProcessorCount}코어";

    // ---------- 라이선스 ----------
    static LicenseInfo License => SoftwareLicense.Current;
    public bool IsLicensed => License != null;
    public string LicenseChannelsText => License == null ? "—" : $"{License.Channels}채널";
    /// <summary>프로그램 최대(64)를 넘는 라이선스면 실제 사용 가능 수를 함께 표시</summary>
    public string LicenseChannelsNote => License == null ? ""
        : License.Channels > AppSettings.ProgramMaxChannels
            ? $"프로그램 최대 {AppSettings.ProgramMaxChannels}채널까지 사용 · 현재 {_main.Channels.Count}개 감시 중"
            : $"현재 {_main.Channels.Count}개 감시 중 · 남은 채널 {Math.Max(0, AppSettings.MaxChannels - _main.Channels.Count)}개";
    public string LicenseDateText => License == null ? "—" : License.IssuedDate.ToString("yyyy-MM-dd");
    public string LicenseMachineText => License == null ? "—" : SoftwareLicenseCore.FormatMachineId(License.MachineId);
    public string LicenseIdText => License?.LicenseId ?? "—";
    public string LicenseeText => string.IsNullOrWhiteSpace(License?.Licensee) ? "—" : License.Licensee;
    public string LicensePath => SoftwareLicense.KeyPath;
    public ICommand OpenLicenseFolderCommand { get; } = new RelayCommand(() => MainViewModel.OpenFolder(SoftwareLicense.Folder));

    void RefreshStorage()
    {
        long used = CsvLogger.FolderBytes(Draft.LogFolder);
        StorageText = Draft.LogMaxMB > 0
            ? $"{Fmt.Bytes(used)} / {Draft.LogMaxMB} MB ({used / (Draft.LogMaxMB * 1048576.0) * 100:0.0}%)"
            : $"{Fmt.Bytes(used)} (제한 없음)";
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Draft.LogFolder));
            var drive = new DriveInfo(root);
            long total = drive.TotalSize, usedDisk = total - drive.AvailableFreeSpace;
            DiskText = $"{drive.Name.TrimEnd('\\')}  {Fmt.Bytes(usedDisk)} / {Fmt.Bytes(total)} ({usedDisk * 100.0 / total:0.0}%)";
        }
        catch { DiskText = "—"; }
    }

    public AppSettings Draft { get; private set; }
    public ObservableCollection<ChannelConfig> Channels { get; } = new();

    public ChannelConfig SelectedChannel
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OrigTest = HecsTest = null;
            OnPropertyChanged(nameof(HasSelection));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool HasSelection => _selected != null;

    public string Section { get => _section; set => Set(ref _section, value); }
    public string OrigTest { get => _origTest; set => Set(ref _origTest, value); }
    public string HecsTest { get => _hecsTest; set => Set(ref _hecsTest, value); }
    public string FfmpegTest { get => _ffmpegTest; set => Set(ref _ffmpegTest, value); }

    public ICommand AddCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand TestOriginalCommand { get; }
    public ICommand TestHecsCommand { get; }
    public ICommand CheckFfmpegCommand { get; }
    public ICommand BrowseFfmpegCommand { get; }
    public ICommand BrowseLogFolderCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }

    public void Load(AppSettings s)
    {
        var keepId = _selected?.Id;
        Draft = SettingsStore.Clone(s);
        Channels.Clear();
        foreach (var c in Draft.Channels) Channels.Add(c);
        SelectedChannel = Channels.FirstOrDefault(c => c.Id == keepId) ?? Channels.FirstOrDefault();
        FfmpegTest = null;
        OnPropertyChanged(nameof(Draft));
        RefreshStorage();
        RaiseBulkPreview();
        OnPropertyChanged(nameof(LicenseChannelsNote));
        _ = LoadFfmpegVersionAsync();
    }

    async Task LoadFfmpegVersionAsync()
    {
        var r = await ProbeService.CheckFfmpegAsync(Draft.FfmpegPath);
        var v = r.TrimStart('✔', '✖', ' ').Replace("ffmpeg version ", "");
        int cut = v.IndexOf(" Copyright", StringComparison.Ordinal);
        FfmpegVersion = cut > 0 ? v[..cut] : v;
    }

    public void SelectById(string id) =>
        SelectedChannel = Channels.FirstOrDefault(c => c.Id == id) ?? SelectedChannel;

    /// <summary>채널 목록 머리글: "12 / 64"</summary>
    public string ChannelCountText => $"{Channels.Count} / {AppSettings.MaxChannels}";

    void Add()
    {
        if (!_main.CheckChannelRoom(Channels.Count)) return;
        var c = new ChannelConfig { Name = $"채널 {Channels.Count + 1}", OriginalUrl = "rtsp://", HecsUrl = "rtsp://" };
        Channels.Add(c);
        SelectedChannel = c;
    }

    void Duplicate()
    {
        if (!_main.CheckChannelRoom(Channels.Count)) return;
        var s = _selected;
        var c = new ChannelConfig
        {
            Name = s.Name + " (복사)", Group = s.Group, OriginalUrl = s.OriginalUrl, HecsUrl = s.HecsUrl, Enabled = s.Enabled,
            OrigUser = s.OrigUser, OrigPassword = s.OrigPassword, HecsUser = s.HecsUser, HecsPassword = s.HecsPassword,
        };
        Channels.Insert(Channels.IndexOf(s) + 1, c);
        SelectedChannel = c;
    }

    void Delete()
    {
        int i = Channels.IndexOf(_selected);
        Channels.RemoveAt(i);
        SelectedChannel = Channels.Count == 0 ? null : Channels[Math.Min(i, Channels.Count - 1)];
    }

    void Move(int d)
    {
        int i = Channels.IndexOf(_selected);
        Channels.Move(i, i + d);
    }

    void BrowseFfmpeg()
    {
        var dlg = new OpenFileDialog { Title = "ffmpeg.exe 선택", Filter = "ffmpeg.exe|ffmpeg.exe|실행 파일 (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;
        Draft.FfmpegPath = dlg.FileName;
        OnPropertyChanged(nameof(Draft));
    }

    void Apply()
    {
        Draft.MeasureWindowSec = Math.Clamp(Draft.MeasureWindowSec, 1, 10);
        Draft.HistorySeconds = Math.Clamp(Draft.HistorySeconds, 30, 600);
        Draft.ThumbFps = Math.Clamp(Draft.ThumbFps, 1, 30);
        Draft.ReconnectSec = Math.Clamp(Draft.ReconnectSec, 1, 300);
        Draft.StallTimeoutSec = Math.Clamp(Draft.StallTimeoutSec, 3, 120);
        Draft.AlarmHoldSec = Math.Clamp(Draft.AlarmHoldSec, 1, 3600);
        Draft.CsvIntervalSec = Math.Clamp(Draft.CsvIntervalSec, 5, 3600);
        Draft.LogMaxMB = Math.Max(0, Draft.LogMaxMB);
        // URL에 user:pass@ 를 직접 붙여 넣은 경우 계정 칸으로 옮긴다 (설정 파일에 평문 비밀번호가 남지 않도록)
        foreach (var c in Channels)
        {
            var (ou, ouser, opass) = StreamUrl.SplitCredentials(c.OriginalUrl);
            if (ouser != null) { c.OriginalUrl = ou; c.OrigUser = ouser; c.OrigPassword = opass; }
            var (hu, huser, hpass) = StreamUrl.SplitCredentials(c.HecsUrl);
            if (huser != null) { c.HecsUrl = hu; c.HecsUser = huser; c.HecsPassword = hpass; }
        }
        Draft.Channels = Channels.ToList();
        _main.ApplySettings(Draft, fromSettings: true);
        Load(_main.Settings);
    }
}
