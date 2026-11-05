using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using HecsMonitor.Services;

namespace HecsMonitor.Converters;

/// <summary>
/// 문구 속 "HECS"를 현재 제품 이름(Brand.Name)으로 바꿔 표시한다. 이름이 바뀌면 바로 갱신된다.
/// 예) Text="{b:Brand 'HECS 출력 RTSP URL'}"
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class BrandExtension : MarkupExtension
{
    public BrandExtension() { }
    public BrandExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = Brand.Hecs;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Brand.BindingSource.Name))
        {
            Source = Brand.Source,
            Mode = BindingMode.OneWay,
            Converter = BrandTextConverter.Instance,
            ConverterParameter = Text,
        }.ProvideValue(serviceProvider);
}

/// <summary>value = 현재 이름, parameter = "HECS"가 들어 있는 문구.</summary>
public sealed class BrandTextConverter : IValueConverter
{
    public static readonly BrandTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (parameter as string ?? Brand.Hecs).Replace(Brand.Hecs, value as string ?? Brand.Name);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
