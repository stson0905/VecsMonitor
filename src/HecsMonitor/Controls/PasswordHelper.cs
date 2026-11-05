using System.Windows;
using System.Windows.Controls;

namespace HecsMonitor.Controls;

/// <summary>PasswordBox.Password는 바인딩할 수 없으므로 첨부 속성으로 양방향 연결한다.</summary>
public static class PasswordHelper
{
    // 기본값을 null로 둬야 바인딩 값이 ""(빈 비밀번호)일 때도 변경 콜백이 호출되어
    // PasswordChanged 구독이 걸린다. 기본값이 ""이면 콜백이 불리지 않아 입력이 소스로 전달되지 않는다.
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordHelper),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(PasswordHelper));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnPasswordChanged;
        string value = e.NewValue as string ?? "";
        if (!(bool)box.GetValue(UpdatingProperty) && box.Password != value) box.Password = value;
        box.PasswordChanged += OnPasswordChanged;
    }

    static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(UpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(UpdatingProperty, false);
    }
}
