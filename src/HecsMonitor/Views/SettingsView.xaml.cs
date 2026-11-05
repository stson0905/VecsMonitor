using System.Windows.Controls;
using System.Windows.Threading;
using HecsMonitor.ViewModels;

namespace HecsMonitor.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // 일괄 등록 입력값이 바뀌면 바인딩 반영 후 미리보기를 갱신
    void OnVersionClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        (DataContext as SettingsEditorViewModel)?.OnVersionClicked();

    void OnBulkChanged(object sender, TextChangedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => (DataContext as SettingsEditorViewModel)?.RaiseBulkPreview());
}
