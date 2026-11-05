using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HecsMonitor.Converters;

/// <summary>
/// value.ToString() == parameter 비교. bool/Visibility 대상 모두 지원하며,
/// 라디오 버튼 양방향 바인딩 시 parameter를 소스 타입으로 변환해 돌려준다.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool eq = string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture),
                                parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return targetType == typeof(Visibility) ? (eq ? Visibility.Visible : Visibility.Collapsed) : eq;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) return Binding.DoNothing;
        if (targetType == typeof(string) || targetType == typeof(object)) return parameter?.ToString();
        return System.Convert.ChangeType(parameter, targetType, CultureInfo.InvariantCulture);
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool v => v,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>상태/이벤트 레벨 문자열 → 색상.</summary>
public sealed class KindBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value?.ToString() switch
        {
            "Live" or "Ok" => "SaveBrush",
            "Connecting" or "Warn" => "WarnBrush",
            "Offline" or "Alarm" => "AlarmBrush",
            "Info" => "HecsBrush",
            "Normal" => "SubTextBrush",
            _ => "FaintBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>카드 페이지 칸: 채널이면 채널 카드, 빈 칸이면 빈 슬롯 템플릿.</summary>
public sealed class SlotTemplateSelector : DataTemplateSelector
{
    public DataTemplate ChannelTemplate { get; set; }
    public DataTemplate EmptyTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) =>
        item is ViewModels.EmptySlot ? EmptyTemplate : ChannelTemplate;
}

/// <summary>double 값 × parameter.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        double k = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 1;
        return v * k;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
