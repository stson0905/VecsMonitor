using System.Windows;
using System.Windows.Threading;
using HecsMonitor.Licensing;
using HecsMonitor.Services;
using HecsMonitor.Views;

namespace HecsMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
        var saved = SettingsStore.Load();   // 라이선스 안내 창에도 고른 제품 이름을 쓴다
        Brand.Set(Brand.Resolve(saved.Brand, saved.BrandBuild));

        // 허가된 라이선스 키가 없으면 실행하지 않는다
        if (!SoftwareLicense.Validate())
        {
            ShowLicenseBlocked();
            Shutdown(1);
            return;
        }
        new MainWindow().Show();
    }

    static void ShowLicenseBlocked()
    {
        string id = SoftwareLicenseCore.GetMachineIdForActivation();
        bool copied = false;
        if (id != null)
        {
            try { Clipboard.SetText(SoftwareLicenseCore.FormatMachineId(id)); copied = true; } catch { }
        }

        string message =
            "라이선스 정책에 따라 프로그램을 실행할 수 없습니다.\n\n" +
            $"사유: {SoftwareLicense.Error}\n\n" +
            $"이 PC의 장치 ID: {(id != null ? SoftwareLicenseCore.FormatMachineId(id) : "확인 불가 (유선 랜카드 없음)")}\n" +
            $"키 파일 위치: {SoftwareLicense.KeyPath}\n\n" +
            "장치 ID를 공급사에 전달해 라이선스 키(license.key)를 발급받은 뒤 위 위치에 넣고 다시 실행하세요." +
            (copied ? "\n(장치 ID가 클립보드에 복사되었습니다.)" : "");

        MessageBox.Show(message, $"{Brand.AppName} 라이선스", MessageBoxButton.OK, MessageBoxImage.Stop);
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.AppendAllText(Path.Combine(SettingsStore.Folder, "crash.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n");
        }
        catch { }
        MessageBox.Show(e.Exception.Message, $"{Brand.AppName} 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
