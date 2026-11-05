using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HecsMonitor.Licensing;
using HecsMonitor.Services;
using HecsMonitor.ViewModels;
using Microsoft.Win32;

namespace HecsMonitorLicenseGen;

/// <summary>license.key 검사 결과</summary>
public sealed class InspectResult
{
    public bool Ok { get; init; }
    public string FileName { get; init; }
    public string Status { get; init; }
    public string Licensee { get; init; }
    public string ChannelsText { get; init; }
    public string DateText { get; init; }
    public string MachineText { get; init; }
    public string LicenseId { get; init; }
    /// <summary>텍스트 편집기로 열었을 때 보이는 내용 (앞부분)</summary>
    public string RawText { get; init; }
}

public sealed class StudioViewModel : ObservableObject
{
    readonly SigningKeyStore _store = new();
    readonly GenSettings _settings = GenSettings.Load();
    readonly DispatcherTimer _toastTimer;

    string _licensee = "", _machineInput = "", _dateText, _memo = "", _search = "", _toast;
    int _channels = 16;
    bool _issued;
    LedgerEntry _lastIssued;
    IReadOnlyList<Brush> _seal;
    InspectResult _inspect;
    string _tab = "ledger";

    public StudioViewModel()
    {
        _dateText = DateTime.Today.ToString("yyyy-MM-dd");
        Brand.Set(Brand.Resolve(_settings.Brand, _settings.BrandBuild));

        foreach (var e in Ledger.Load().OrderByDescending(e => e.CreatedAt)) Entries.Add(e);
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = o => o is LedgerEntry e && (string.IsNullOrWhiteSpace(_search) ||
            (e.Licensee ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            (Normalize(_search).Length >= 2 && e.MachineId.Contains(Normalize(_search), StringComparison.OrdinalIgnoreCase)) ||
            (e.LicenseId ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            (e.Memo ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase));

        UseThisPcCommand = new RelayCommand(() =>
        {
            var id = SoftwareLicenseCore.GetMachineIdForActivation();
            if (id == null) ShowToast("이 PC에서 유선 랜카드를 찾지 못했습니다");
            else MachineInput = SoftwareLicenseCore.FormatMachineId(id);
        });
        PasteIdCommand = new RelayCommand(() =>
        {
            try { MachineInput = Clipboard.GetText().Trim(); } catch { }
        });
        SetChannelsCommand = new RelayCommand(p => { if (int.TryParse(p?.ToString(), out int n)) Channels = n; });
        TodayCommand = new RelayCommand(() => DateText = DateTime.Today.ToString("yyyy-MM-dd"));
        IssueCommand = new RelayCommand(Issue, _ => CanIssue);
        NewFormCommand = new RelayCommand(() =>
        {
            Licensee = ""; MachineInput = ""; Memo = ""; Channels = 16;
            TodayCommand.Execute(null);
        });
        ChangeOutputCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog { Title = "license.key 저장 폴더", InitialDirectory = Directory.Exists(OutputRoot) ? OutputRoot : null };
            if (dlg.ShowDialog() == true) SetOutputRoot(dlg.FolderName);
        });
        ResetOutputCommand = new RelayCommand(() => SetOutputRoot(null), _ => !string.IsNullOrEmpty(_settings.OutputRoot));
        OpenOutputCommand = new RelayCommand(() =>
        {
            try
            {
                Directory.CreateDirectory(OutputRoot);
                Process.Start("explorer.exe", $"\"{OutputRoot}\"");
            }
            catch (Exception ex) { ShowToast("폴더를 열 수 없습니다 · " + ex.Message); }
        });
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            try { Directory.CreateDirectory(GenSettings.DataFolder); Process.Start("explorer.exe", $"\"{GenSettings.DataFolder}\""); } catch { }
        });
        OpenFolderCommand = new RelayCommand(p =>
        {
            var path = (p as LedgerEntry)?.FilePath ?? _lastIssued?.FilePath;
            if (path != null) OpenInExplorer(path);
        });
        SaveAsCommand = new RelayCommand(p => SaveAs(p as LedgerEntry ?? _lastIssued));
        CopyTextCommand = new RelayCommand(p => { try { Clipboard.SetText(p?.ToString() ?? ""); ShowToast("복사했습니다"); } catch { } });
        ReuseCommand = new RelayCommand(p =>
        {
            if (p is not LedgerEntry e) return;
            Licensee = e.Licensee; MachineInput = e.MachineText; Channels = e.Channels; Memo = e.Memo;
            TodayCommand.Execute(null);
            ShowToast("같은 조건을 불러왔습니다 · 채널 수를 바꿔 재발급할 수 있습니다");
        });
        DeleteEntryCommand = new RelayCommand(p =>
        {
            if (p is not LedgerEntry e) return;
            if (MessageBox.Show($"'{e.Title}' 발급 이력을 목록에서 지울까요?\n저장된 license.key 파일은 지우지 않습니다.", "발급 이력",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Entries.Remove(e);
            SaveLedger();
        });
        InspectBrowseCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog { Title = "검사할 license.key", Filter = "라이선스 키 (*.key)|*.key|모든 파일|*.*" };
            if (dlg.ShowDialog() == true) Inspect(dlg.FileName);
        });
        LoadKeyCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog { Title = "서명 개인키 불러오기", Filter = "PEM 개인키 (*.pem)|*.pem|모든 파일|*.*" };
            if (dlg.ShowDialog() == true) BeginLoadKey(dlg.FileName);
        });
        CreateKeyCommand = new RelayCommand(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "새 서명 개인키 저장 위치", Filter = "PEM 개인키 (*.pem)|*.pem",
                FileName = $"hecs-license-signing-{DateTime.Today:yyyyMMdd}.pem",
            };
            if (dlg.ShowDialog() != true) return;
            _pendingKeyPath = dlg.FileName;
            OpenPassword("create");
        }, _ => DeveloperMode);   // 키 교체는 이전 라이선스를 모두 무효로 만드므로 개발자 모드에서만
        CancelPasswordCommand = new RelayCommand(() => PasswordMode = null);
        CloseNewKeyCommand = new RelayCommand(() => NewPublicKey = null);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };

        UpdateSeal();
        var keyPath = SigningKeyStore.FindDefaultKeyPath(_settings.PrivateKeyPath);
        if (keyPath != null) Application.Current.Dispatcher.BeginInvoke(() => BeginLoadKey(keyPath), DispatcherPriority.Loaded);
    }

    // ---------- 명령 ----------
    public ICommand UseThisPcCommand { get; }
    public ICommand PasteIdCommand { get; }
    public ICommand SetChannelsCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand IssueCommand { get; }
    public ICommand NewFormCommand { get; }
    public ICommand ChangeOutputCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand CopyTextCommand { get; }
    public ICommand ReuseCommand { get; }
    public ICommand DeleteEntryCommand { get; }
    public ICommand InspectBrowseCommand { get; }
    public ICommand LoadKeyCommand { get; }
    public ICommand CreateKeyCommand { get; }
    public ICommand CancelPasswordCommand { get; }
    public ICommand CloseNewKeyCommand { get; }
    public ICommand ResetOutputCommand { get; }
    public ICommand OpenOutputCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    // ---------- 설정 ----------
    bool _settingsOpen;
    public bool IsSettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }

    void SetOutputRoot(string folder)
    {
        _settings.OutputRoot = folder;
        _settings.Save();
        OnPropertyChanged(nameof(OutputRoot));
        OnPropertyChanged(nameof(IsDefaultOutput));
        CommandManager.InvalidateRequerySuggested();
        ShowToast(folder == null ? "저장 폴더를 기본값으로 되돌렸습니다" : "저장 폴더를 바꿨습니다");
    }
    public bool IsDefaultOutput => string.IsNullOrEmpty(_settings.OutputRoot);

    public string AppVersion => typeof(StudioViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string RuntimeText => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string DataFolder => GenSettings.DataFolder;
    public string AppPublicKey => SoftwareLicenseCore.PublicKeyBase64;
    public string LoadedPublicKey => _store.PublicKeyBase64 ?? "—";
    public string KeyPathText => _store.KeyPath ?? "—";
    public string KeyFingerprintText => _store.Fingerprint ?? "—";
    public string KeyProtectionText => _store.Key == null ? "—" : _store.IsEncryptedFile ? "비밀번호로 암호화됨" : "암호화 안 됨 (평문 PEM)";

    // ---------- 개발자 모드 (설정 → 버전 5회 클릭) ----------
    const int DevClicks = 5;
    int _versionClicks;
    DateTime _lastVersionClick;

    /// <summary>켜져 있으면 기능 상세 설명과 서명 키 정보를 표시한다. 저장하지 않으므로 실행할 때마다 꺼진 상태로 시작.</summary>
    bool _developerMode;
    public bool DeveloperMode => _developerMode;

    /// <summary>2초 안에 이어서 5번 누르면 개발자 모드를 켜거나 끈다 (HecsMonitor와 같은 방식).</summary>
    public void OnVersionClicked()
    {
        var now = DateTime.Now;
        _versionClicks = (now - _lastVersionClick).TotalSeconds <= 2 ? _versionClicks + 1 : 1;
        _lastVersionClick = now;

        int left = DevClicks - _versionClicks;
        if (left > 0)
        {
            if (_versionClicks >= 3)
                ShowToast(DeveloperMode ? $"개발자 모드 해제까지 {left}번 남았습니다" : $"개발자 모드까지 {left}번 남았습니다");
            return;
        }
        _versionClicks = 0;
        _developerMode = !_developerMode;
        OnPropertyChanged(nameof(DeveloperMode));
        OnPropertyChanged(nameof(IssueHint));
        OnPropertyChanged(nameof(KeyDetail));
        CommandManager.InvalidateRequerySuggested();   // '새 서명 키 쌍 만들기' 버튼 활성 상태 갱신
        ShowToast(DeveloperMode ? "개발자 모드가 활성화되었습니다 · 상세 설명과 서명 키 정보가 표시됩니다" : "개발자 모드가 해제되었습니다");
    }

    /// <summary>
    /// 제품 이름 HECS / ECO (개발자 모드에서 선택). 고르는 즉시 저장되며, 같은 빌드에서는 개발자 모드를 끄거나
    /// 다시 실행해도 유지되고 새 설치 패키지로 실행하면 그 패키지의 기본값으로 돌아간다.
    /// </summary>
    public string BrandName
    {
        get => Brand.Name;
        set
        {
            if (Brand.Normalize(value) is not { } name || name == Brand.Name) return;
            _settings.Brand = name;
            _settings.BrandBuild = Brand.BuildId;
            _settings.Save();
            Brand.Set(name);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IssueHint));
            OnPropertyChanged(nameof(KeyDetail));
            OnPropertyChanged(nameof(ChannelsNote));
            ShowToast($"제품 이름을 {name}(으)로 바꿨습니다 · 개발자 모드를 꺼도 유지됩니다");
        }
    }

    // ---------- 발급 양식 ----------
    public string Licensee { get => _licensee; set { if (Set(ref _licensee, value)) FormChanged(); } }
    public string Memo { get => _memo; set => Set(ref _memo, value); }

    public string MachineInput
    {
        get => _machineInput;
        set { if (Set(ref _machineInput, value)) { OnPropertyChanged(nameof(MachineState)); OnPropertyChanged(nameof(MachinePreview)); FormChanged(); } }
    }
    string MachineId => SoftwareLicenseCore.NormalizeMachineId(_machineInput, out var m) ? m : null;
    /// <summary>empty | ok | bad</summary>
    public string MachineState => string.IsNullOrWhiteSpace(_machineInput) ? "empty" : MachineId != null ? "ok" : "bad";
    public string MachinePreview => MachineId != null ? SoftwareLicenseCore.FormatMachineId(MachineId) : "__-__-__-__-__-__";

    public int Channels
    {
        get => _channels;
        set
        {
            if (!Set(ref _channels, Math.Clamp(value, 0, SoftwareLicenseCore.MaxKeyChannels))) return;
            OnPropertyChanged(nameof(ChannelsNote));
            FormChanged();
        }
    }
    public string ChannelsNote => _channels > 64 ? $"{Brand.AppName}는 최대 64채널까지 사용합니다" : _channels <= 0 ? "1 이상 입력하세요" : "";

    public string DateText { get => _dateText; set { if (Set(ref _dateText, value)) { OnPropertyChanged(nameof(DateOk)); FormChanged(); } } }
    bool TryDate(out DateOnly d) => DateOnly.TryParseExact(_dateText?.Trim(), new[] { "yyyy-MM-dd", "yyyyMMdd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    public bool DateOk => TryDate(out _);

    public string OutputRoot => string.IsNullOrEmpty(_settings.OutputRoot) ? GenSettings.DefaultOutputRoot : _settings.OutputRoot;

    bool CanIssue => _store.Key != null && _store.MatchesApp && MachineId != null && _channels > 0 && DateOk;
    public string IssueHint =>
        _store.Key == null ? "서명 개인키를 먼저 불러오세요"
        : !_store.MatchesApp ? $"이 개인키는 {Brand.AppName}의 공개키와 짝이 아닙니다"
        : MachineId == null ? "장치 ID(MAC 12자리)를 입력하세요"
        : _channels <= 0 ? "채널 수를 입력하세요"
        : !DateOk ? "생성일 형식: yyyy-MM-dd"
        : DeveloperMode ? "ECDSA P-256 서명 → 장치 ID로 암호화 → license.key 저장"
        : "발급할 준비가 되었습니다";

    void FormChanged()
    {
        if (_issued) { _issued = false; OnPropertyChanged(nameof(IsIssued)); }
        OnPropertyChanged(nameof(IssueHint));
        OnPropertyChanged(nameof(PreviewLicensee));
        OnPropertyChanged(nameof(PreviewDate));
        OnPropertyChanged(nameof(PreviewLicenseId));
        UpdateSeal();
    }

    // ---------- 인증서 미리보기 ----------
    public bool IsIssued => _issued;
    public string PreviewLicensee => string.IsNullOrWhiteSpace(_licensee) ? "사용자 (고객 · 현장)" : _licensee.Trim();
    public string PreviewDate => TryDate(out var d) ? d.ToString("yyyy.MM.dd") : "----.--.--";
    public string PreviewLicenseId => _issued && _lastIssued != null ? _lastIssued.LicenseId : "HL-····-····-····-····";
    public LedgerEntry LastIssued { get => _lastIssued; private set => Set(ref _lastIssued, value); }

    /// <summary>인증서의 7×7 서명 인장. 발급 전에는 입력값, 발급 후에는 실제 서명에서 만든다.</summary>
    public IReadOnlyList<Brush> Seal { get => _seal; private set => Set(ref _seal, value); }

    static readonly Brush[] SealPalette =
    {
        Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24))),
    };
    static readonly Brush SealEmpty = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    void UpdateSeal(byte[] seed = null)
    {
        seed ??= Encoding.UTF8.GetBytes($"{_licensee}|{_channels}|{_dateText}|{MachineId}");
        byte[] h = SHA256.HashData(seed);
        var a = SealPalette[h[30] % SealPalette.Length];
        var b = SealPalette[(h[30] + 1 + h[31] % 3) % SealPalette.Length];
        var cells = new Brush[49];
        for (int r = 0; r < 7; r++)
            for (int c = 0; c < 4; c++)
            {
                int bit = r * 4 + c;
                bool on = (h[bit / 8] >> (bit % 8) & 1) == 1;
                var brush = on ? ((h[8 + bit / 8] >> (bit % 8) & 1) == 1 ? a : b) : SealEmpty;
                cells[r * 7 + c] = brush;
                cells[r * 7 + 6 - c] = brush;   // 좌우 대칭
            }
        Seal = cells;
    }

    // ---------- 발급 ----------
    void Issue()
    {
        if (!TryDate(out var date) || MachineId == null) return;
        try
        {
            var payload = new LicensePayload(_channels, date, MachineId, (_licensee ?? "").Trim(), SoftwareLicenseCore.NewLicenseId());
            byte[] data = SoftwareLicenseCore.SerializePayload(payload);
            byte[] sig = _store.Sign(data);
            string text = SoftwareLicenseCore.Seal(data, sig, MachineId);

            // HecsMonitor와 같은 코드로 다시 열어 확인한 뒤 저장
            if (SoftwareLicenseCore.Read(text, new[] { MachineId }, out var err) == null)
                throw new CryptographicException("발급한 키의 자체 검증에 실패했습니다. " + err);

            string folder = Path.Combine(OutputRoot, $"{date:yyyyMMdd}_{SafeName(payload.Licensee)}_{MachineId}");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "license.key");
            File.WriteAllText(path, text);

            var entry = new LedgerEntry
            {
                LicenseId = payload.LicenseId, Licensee = payload.Licensee, Channels = payload.Channels, IssuedDate = date,
                MachineId = MachineId, Memo = (_memo ?? "").Trim(), CreatedAt = DateTime.Now, FilePath = path, LicenseText = text,
            };
            Entries.Insert(0, entry);
            SaveLedger();

            LastIssued = entry;
            _issued = true;
            UpdateSeal(sig);
            OnPropertyChanged(nameof(IsIssued));
            OnPropertyChanged(nameof(PreviewLicenseId));
            Tab = "ledger";
            ShowToast("라이선스를 발급했습니다");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "발급 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    static string SafeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(s.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return name.Length > 40 ? name[..40] : name;
    }

    void SaveAs(LedgerEntry e)
    {
        if (e == null) return;
        var dlg = new SaveFileDialog { Title = "license.key 저장", FileName = "license.key", Filter = "라이선스 키 (*.key)|*.key" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, e.LicenseText);
        ShowToast("저장했습니다");
    }

    static void OpenInExplorer(string file)
    {
        try
        {
            if (File.Exists(file)) Process.Start("explorer.exe", $"/select,\"{file}\"");
            else if (Directory.Exists(Path.GetDirectoryName(file))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(file)}\"");
        }
        catch { }
    }

    // ---------- 발급 이력 ----------
    public ObservableCollection<LedgerEntry> Entries { get; } = new();
    public ICollectionView EntriesView { get; }
    public string Search { get => _search; set { if (Set(ref _search, value)) EntriesView.Refresh(); } }
    public string LedgerSummary => $"{Entries.Count}건 · {Entries.Select(e => e.MachineId).Distinct().Count()}대 · {Entries.Sum(e => e.Channels)}채널";
    void SaveLedger()
    {
        try { Ledger.Save(Entries); } catch (Exception ex) { ShowToast("이력 저장 실패 · " + ex.Message); }
        OnPropertyChanged(nameof(LedgerSummary));
    }

    /// <summary>ledger | inspect</summary>
    public string Tab { get => _tab; set => Set(ref _tab, value); }

    // ---------- 키 검사 ----------
    public InspectResult InspectResult { get => _inspect; private set => Set(ref _inspect, value); }

    public void Inspect(string path)
    {
        Tab = "inspect";
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { InspectResult = new InspectResult { Ok = false, FileName = Path.GetFileName(path), Status = ex.Message }; return; }

        string raw = string.Join("\n", text.Split('\n').Take(4).Select(l => l.TrimEnd())) + (text.Split('\n').Length > 4 ? "\n…" : "");
        // 장치 ID 후보: 입력 중인 ID, 발급 이력, 이 PC
        var ids = new List<string>();
        if (MachineId != null) ids.Add(MachineId);
        ids.AddRange(Entries.Select(e => e.MachineId));
        ids.AddRange(SoftwareLicenseCore.GetWiredMacs());
        var p = SoftwareLicenseCore.Read(text, ids.Distinct(), out var err);

        InspectResult = p == null
            ? new InspectResult { Ok = false, FileName = Path.GetFileName(path), RawText = raw,
                Status = err + " (장치 ID를 입력란에 넣고 다시 검사하면 그 장치로도 확인합니다)" }
            : new InspectResult
            {
                Ok = true, FileName = Path.GetFileName(path), RawText = raw, Status = "유효한 라이선스 · 서명 확인됨",
                Licensee = string.IsNullOrWhiteSpace(p.Licensee) ? "—" : p.Licensee, ChannelsText = $"{p.Channels}채널",
                DateText = p.IssuedDate.ToString("yyyy-MM-dd"), MachineText = SoftwareLicenseCore.FormatMachineId(p.MachineId), LicenseId = p.LicenseId,
            };
    }

    // ---------- 서명 키 ----------
    string _pendingKeyPath, _passwordMode, _passwordError, _newPublicKey;

    /// <summary>none | ok | mismatch</summary>
    public string KeyState => _store.Key == null ? "none" : _store.MatchesApp ? "ok" : "mismatch";
    public string KeyStatusText => KeyState switch
    {
        "ok" => "서명 키 준비됨",
        "mismatch" => "공개키 불일치",
        _ => "서명 키 없음",
    };
    public string KeyDetail => _store.Key == null
        ? "설정에서 서명 키를 불러오세요"
        : !_store.MatchesApp ? $"{Brand.AppName}와 짝이 맞지 않는 키입니다"
        : DeveloperMode ? $"{Path.GetFileName(_store.KeyPath)} · 지문 {_store.Fingerprint}"
        : "라이선스를 발급할 수 있습니다";
    public string AppFingerprint => SoftwareLicenseCore.PublicKeyFingerprint;

    void BeginLoadKey(string path)
    {
        try
        {
            if (SigningKeyStore.IsEncrypted(path)) { _pendingKeyPath = path; OpenPassword("load"); return; }
            _store.Load(path);
            KeyLoaded();
        }
        catch (Exception ex) { ShowToast("개인키를 불러오지 못했습니다 · " + ex.Message); }
    }

    void KeyLoaded()
    {
        _settings.PrivateKeyPath = _store.KeyPath;
        _settings.Save();
        OnPropertyChanged(nameof(LoadedPublicKey));
        OnPropertyChanged(nameof(KeyPathText));
        OnPropertyChanged(nameof(KeyFingerprintText));
        OnPropertyChanged(nameof(KeyProtectionText));
        OnPropertyChanged(nameof(KeyState));
        OnPropertyChanged(nameof(KeyStatusText));
        OnPropertyChanged(nameof(KeyDetail));
        OnPropertyChanged(nameof(IssueHint));
        CommandManager.InvalidateRequerySuggested();
        ShowToast(_store.MatchesApp ? "서명 키를 불러왔습니다" : $"불러온 키가 {Brand.AppName} 공개키와 짝이 아닙니다");
    }

    /// <summary>null | load | create</summary>
    public string PasswordMode { get => _passwordMode; private set { Set(ref _passwordMode, value); OnPropertyChanged(nameof(IsPasswordOpen)); } }
    public bool IsPasswordOpen => _passwordMode != null;
    public string PasswordError { get => _passwordError; private set => Set(ref _passwordError, value); }
    public string PasswordTitle => _passwordMode == "create" ? "새 서명 키 보호" : "서명 키 비밀번호";
    public string PasswordDesc => _passwordMode == "create"
        ? "개인키 파일을 비밀번호로 암호화합니다 (권장). 비워 두면 암호화하지 않습니다. 비밀번호를 잃어버리면 키를 쓸 수 없습니다."
        : $"{Path.GetFileName(_pendingKeyPath)} 은(는) 비밀번호로 보호되어 있습니다.";

    void OpenPassword(string mode)
    {
        PasswordError = null;
        PasswordMode = mode;
        OnPropertyChanged(nameof(PasswordTitle));
        OnPropertyChanged(nameof(PasswordDesc));
    }

    /// <summary>비밀번호 창의 확인 (PasswordBox는 바인딩하지 않고 화면에서 넘겨 준다)</summary>
    public void SubmitPassword(string password, string confirm)
    {
        try
        {
            if (_passwordMode == "load")
            {
                _store.Load(_pendingKeyPath, password);
                PasswordMode = null;
                KeyLoaded();
            }
            else if (_passwordMode == "create")
            {
                if (password != confirm) { PasswordError = "비밀번호가 서로 다릅니다"; return; }
                NewPublicKey = SigningKeyStore.CreateNew(_pendingKeyPath, password);
                PasswordMode = null;
                _store.Load(_pendingKeyPath, password);
                KeyLoaded();
            }
        }
        catch (CryptographicException) { PasswordError = "비밀번호가 맞지 않습니다"; }
        catch (Exception ex) { PasswordError = ex.Message; }
    }

    /// <summary>새로 만든 키 쌍의 공개키 (HecsMonitor 소스에 넣어야 함)</summary>
    public string NewPublicKey { get => _newPublicKey; private set { Set(ref _newPublicKey, value); OnPropertyChanged(nameof(HasNewPublicKey)); } }
    public bool HasNewPublicKey => _newPublicKey != null;

    // ---------- 알림 ----------
    public string Toast { get => _toast; private set { Set(ref _toast, value); OnPropertyChanged(nameof(HasToast)); } }
    public bool HasToast => _toast != null;
    public void ShowToast(string text) { Toast = text; _toastTimer.Stop(); _toastTimer.Start(); }

    static string Normalize(string s) => new string((s ?? "").Where(Uri.IsHexDigit).ToArray());
}
