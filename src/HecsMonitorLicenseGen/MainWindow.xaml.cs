using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace HecsMonitorLicenseGen;

public partial class MainWindow : Window
{
    readonly StudioViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new StudioViewModel();
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };
        _vm.PropertyChanged += OnVmChanged;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm.IsSettingsOpen && !_vm.IsPasswordOpen && !_vm.HasNewPublicKey)
            {
                _vm.CloseSettingsCommand.Execute(null);
                e.Handled = true;
            }
        };

        // license.key를 창 어디에 끌어 놓아도 검사
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) _vm.Inspect(files[0]);
        };

        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--snapshot");
        if (i > 0 && i + 1 < args.Length) Loaded += async (_, _) => await SnapshotAsync(args[i + 1]);
    }

    /// <summary>개발·문서화용: 주요 화면을 PNG로 저장하고 종료한다. (HecsMonitorLicenseGen.exe --snapshot 폴더)</summary>
    async Task SnapshotAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        void Save(string name)
        {
            UpdateLayout();
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(folder, name));
            enc.Save(fs);
        }
        async Task ToggleDev() { for (int n = 0; n < 5; n++) { _vm.OnVersionClicked(); await Task.Delay(100); } }

        bool dev0 = _vm.DeveloperMode;
        if (dev0) await ToggleDev();               // 일반 모드부터
        await Task.Delay(1500);
        Save("1_main.png");
        _vm.OpenSettingsCommand.Execute(null);
        await Task.Delay(600);
        Save("2_settings.png");
        await ToggleDev();                         // 버전 5회 클릭 → 개발자 모드
        await Task.Delay(800);
        Save("3_settings_dev.png");
        _vm.CloseSettingsCommand.Execute(null);
        await Task.Delay(600);
        Save("4_main_dev.png");
        if (_vm.DeveloperMode != dev0) await ToggleDev();   // 원래 상태로
        Close();
    }

    void OnVmChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioViewModel.IsIssued)) AnimateStamp(_vm.IsIssued);
        else if (e.PropertyName == nameof(StudioViewModel.IsPasswordOpen) && _vm.IsPasswordOpen)
        {
            Pw1.Clear(); Pw2.Clear();
            Dispatcher.BeginInvoke(() => Pw1.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>발급되면 도장이 크게 찍히듯 내려앉는다.</summary>
    void AnimateStamp(bool show)
    {
        if (!show) { Stamp.BeginAnimation(OpacityProperty, null); Stamp.Opacity = 0; return; }
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var t = TimeSpan.FromMilliseconds(380);
        Stamp.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.95, TimeSpan.FromMilliseconds(200)));
        StampScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(2.2, 1, t) { EasingFunction = ease });
        StampScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(2.2, 1, t) { EasingFunction = ease });
    }

    void OnVersionClick(object sender, MouseButtonEventArgs e) => _vm.OnVersionClicked();
    void OnSettingsBackdrop(object sender, MouseButtonEventArgs e) => _vm.CloseSettingsCommand.Execute(null);

    void OnPasswordOk(object sender, RoutedEventArgs e) => _vm.SubmitPassword(Pw1.Password, Pw2.Password);

    void OnPasswordKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.SubmitPassword(Pw1.Password, Pw2.Password);
        else if (e.Key == Key.Escape) _vm.CancelPasswordCommand.Execute(null);
    }

    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void OnClose(object sender, RoutedEventArgs e) => Close();
}
