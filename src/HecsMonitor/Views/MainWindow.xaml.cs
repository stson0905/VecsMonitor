using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HecsMonitor.Services;
using HecsMonitor.ViewModels;

namespace HecsMonitor.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new MainViewModel();
        StateChanged += (_, _) => UpdateMaximizedState();
        Closed += (_, _) => _vm.Shutdown();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter && _vm.IsDialogOpen) { _vm.CloseDialogCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape && _vm.IsDetailOpen) { _vm.CloseDetail(); e.Handled = true; }
            else if (CanFlipPages && Keyboard.FocusedElement is not TextBox) e.Handled = OnPageKey(e.Key);
        };
        // 모달이 열리면 확인 버튼에 포커스 (Enter/Space로 바로 닫기)
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDialogOpen) && _vm.IsDialogOpen)
                Dispatcher.BeginInvoke(() => DialogOk.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            else if (e.PropertyName == nameof(MainViewModel.CardPage) && _vm.CardPage != _shownPage)
            {
                _shownPage = _vm.CardPage;
                AnimatePageSlide(_vm.PageDirection);
            }
        };
        WipeHecs.SizeChanged += (_, _) => UpdateWipeClip();

        // 목록 보기: 스크롤이 멈춘 뒤 보이는 행만 썸네일 디코딩
        _listVisibleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _listVisibleTimer.Tick += (_, _) => { _listVisibleTimer.Stop(); UpdateListVisible(); };
        ListScroll.ScrollChanged += (_, _) => QueueListVisible();
        ListScroll.IsVisibleChanged += (_, _) => QueueListVisible();
        ListItems.ItemContainerGenerator.StatusChanged += (_, _) => QueueListVisible();

        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--snapshot");
        if (i > 0 && i + 1 < args.Length) Loaded += async (_, _) => await SnapshotAsync(args[i + 1]);
    }

    /// <summary>개발·문서화용: 주요 화면을 PNG로 저장하고 종료한다. (HecsMonitor.exe --snapshot 폴더)</summary>
    async Task SnapshotAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        void Save(string name)
        {
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var rtb = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY),
                                             dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(folder, name));
            enc.Save(fs);
        }

        await Task.Delay(12000);
        Save("1_dashboard.png");

        // 카드 크기별 페이지 (S 12 · M 8 · L 6), 요약 접기, 다음 페이지
        string size0 = _vm.CardSize;
        bool hero0 = _vm.HeroCollapsed;
        foreach (var size in new[] { "S", "M", "L" })
        {
            _vm.CardSize = size;
            await Task.Delay(1500);
            Save($"1_cards_{size}.png");
        }
        _vm.HeroCollapsed = true;
        _vm.CardSize = "S";
        await Task.Delay(1500);
        Save("1_cards_S_hero_collapsed.png");
        _vm.LastPage();
        await Task.Delay(1500);
        Save("1_cards_S_last_page.png");
        _vm.FirstPage();
        _vm.HeroCollapsed = hero0;
        _vm.CardSize = size0;
        if (_vm.Channels.Count > 0)
        {
            _vm.OpenDetailCommand.Execute(_vm.Channels[0]);
            await Task.Delay(5000);
            Save("2_compare_wipe.png");
            _vm.CompareMode = "side";
            await Task.Delay(1000);
            Save("3_compare_side.png");
            _vm.CloseDetail();
        }
        _vm.ViewMode = "list";
        await Task.Delay(1200);
        Save("4_dashboard_list.png");
        _vm.ViewMode = "card";

        _vm.Page = "settings";
        await Task.Delay(800);
        Save("5_settings_channels.png");
        _vm.Editor.Section = "discover";
        _vm.Editor.ScanCommand.Execute(null);
        await Task.Delay(4000);
        Save("6_settings_discover.png");
        _vm.Editor.Section = "alarm";
        await Task.Delay(500);
        Save("7_settings_alarm.png");
        _vm.Editor.Section = "about";
        await Task.Delay(1500);
        Save("8_settings_system.png");

        // 개발자 모드에서 제품 이름 바꾸기 → 대시보드·비교 화면 (끝나면 원래 상태로 되돌린다)
        bool dev0 = _vm.DeveloperMode;
        string brand0 = _vm.Settings.Brand, build0 = _vm.Settings.BrandBuild;
        void ToggleDev() { for (int k = 0; k < 5; k++) _vm.Editor.OnVersionClicked(); }
        if (!dev0) ToggleDev();
        string other = Brand.Name == Brand.Eco ? Brand.Hecs : Brand.Eco;
        _vm.Editor.BrandName = other;
        await Task.Delay(800);
        Save($"9_settings_system_dev_{other}.png");
        if (!dev0) ToggleDev();
        _vm.Page = "dashboard";
        await Task.Delay(1500);
        Save($"9_dashboard_{other}.png");
        if (_vm.Channels.Count > 0)
        {
            _vm.OpenDetailCommand.Execute(_vm.Channels[0]);
            await Task.Delay(4000);
            Save($"9_compare_{other}.png");
            _vm.CloseDetail();
        }
        _vm.Settings.Brand = brand0;
        _vm.Settings.BrandBuild = build0;
        SettingsStore.Save(_vm.Settings);
        Brand.Set(Brand.Resolve(brand0, build0));
        Close();
    }

    // ---------- 목록 보기 ----------
    readonly DispatcherTimer _listVisibleTimer;

    void QueueListVisible()
    {
        _listVisibleTimer.Stop();
        _listVisibleTimer.Start();
    }

    /// <summary>스크롤 영역에 조금이라도 걸친 행을 뷰모델에 알린다.</summary>
    void UpdateListVisible()
    {
        var rows = new List<ChannelViewModel>();
        if (ListScroll.IsVisible)
        {
            var view = new Rect(0, 0, ListScroll.ViewportWidth, ListScroll.ViewportHeight);
            for (int k = 0; k < ListItems.Items.Count; k++)
            {
                if (ListItems.Items[k] is not ChannelViewModel c ||
                    ListItems.ItemContainerGenerator.ContainerFromIndex(k) is not FrameworkElement el || !el.IsVisible) continue;
                var r = el.TransformToAncestor(ListScroll).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                if (r.IntersectsWith(view)) rows.Add(c);
            }
        }
        _vm.SetListVisible(rows);
    }

    // ---------- 카드 페이지 ----------
    int _shownPage;
    DateTime _lastWheel;

    bool CanFlipPages => _vm.Page == "dashboard" && _vm.ViewMode == "card" && !_vm.IsDetailOpen && !_vm.IsDialogOpen;

    bool OnPageKey(Key key)
    {
        switch (key)
        {
            case Key.PageDown or Key.Right: _vm.StepPage(1); return true;
            case Key.PageUp or Key.Left: _vm.StepPage(-1); return true;
            case Key.Home: _vm.FirstPage(); return true;
            case Key.End: _vm.LastPage(); return true;
            default: return false;
        }
    }

    void OnCardHostSizeChanged(object sender, SizeChangedEventArgs e) =>
        _vm.SetCardArea(e.NewSize.Width, e.NewSize.Height);

    /// <summary>카드 영역에서 휠 한 칸 = 한 페이지 (터치패드 연속 입력은 0.25초 간격으로 묶음)</summary>
    void OnCardHostWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if ((DateTime.Now - _lastWheel).TotalMilliseconds < 250) return;
        _lastWheel = DateTime.Now;
        _vm.StepPage(e.Delta < 0 ? 1 : -1);
    }

    void OnCardHostMouseEnter(object sender, MouseEventArgs e) => _vm.TourHover = true;
    void OnCardHostMouseLeave(object sender, MouseEventArgs e) => _vm.TourHover = false;

    /// <summary>다음 페이지는 오른쪽에서, 이전 페이지는 왼쪽에서 밀려 들어온다.</summary>
    void AnimatePageSlide(int direction)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(260);
        CardSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(48 * direction, 0, duration) { EasingFunction = ease });
        CardItems.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, duration) { EasingFunction = ease });
    }

    void UpdateMaximizedState()
    {
        // WindowChrome 최대화 시 화면 밖으로 밀리는 테두리 보정
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnWipeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateWipeClip();
    void OnCompareSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWipeClip();

    /// <summary>슬라이더 핸들 위치 기준으로 왼쪽은 원본, 오른쪽은 HECS를 보여준다.</summary>
    void UpdateWipeClip()
    {
        if (WipeHecs == null || Wipe == null) return;
        double w = WipeHecs.ActualWidth, h = WipeHecs.ActualHeight;
        if (w <= 0 || h <= 0) return;
        // Image(Uniform)는 그리드 중앙에 배치되므로 슬라이더(그리드 전체 폭) 좌표를 이미지 좌표로 변환
        double hostW = Wipe.ActualWidth;
        double thumb = 32;
        double xHost = thumb / 2 + Wipe.Value * Math.Max(0, hostW - thumb);
        double offset = (hostW - w) / 2;
        double x = Math.Clamp(xHost - offset, 0, w);
        WipeHecs.Clip = new RectangleGeometry(new Rect(x, 0, w - x, h));
    }
}
