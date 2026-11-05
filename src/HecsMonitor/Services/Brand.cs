using System.ComponentModel;
using System.Reflection;

namespace HecsMonitor.Services;

/// <summary>
/// 화면에 표시하는 제품 이름(HECS / ECO).
/// 기본값은 빌드할 때 정한다(csproj의 &lt;Brand&gt;, 설치 패키지의 -Brand). 개발자 모드에서 바꾸면 설정 파일에
/// 그 빌드의 식별값(BuildId)과 함께 저장되어, 같은 빌드에서는 개발자 모드를 끄거나 다시 실행해도 유지된다.
/// 다른 빌드(새 설치 패키지)로 실행하면 저장된 선택을 버리고 그 빌드의 기본값을 쓴다.
/// 실행 파일·설정 폴더·라이선스 형식 같은 내부 이름은 바꾸지 않는다.
/// </summary>
public static class Brand
{
    public const string Hecs = "HECS";
    public const string Eco = "ECO";
    public static readonly string[] All = { Hecs, Eco };

    static string Metadata(string key) =>
        typeof(Brand).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>빌드 기본값 (AssemblyMetadata "DefaultBrand")</summary>
    public static string Default { get; } = Normalize(Metadata("DefaultBrand")) ?? Eco;

    /// <summary>
    /// 이 빌드의 식별값. 설치 패키지는 빌드 시각(AssemblyMetadata "BuildStamp"),
    /// 개발 빌드는 어셈블리 MVID(소스나 -p:Brand가 바뀌면 달라짐).
    /// </summary>
    public static string BuildId { get; } =
        Metadata("BuildStamp") is { Length: > 0 } stamp ? stamp : typeof(Brand).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>저장된 선택이 이 빌드에서 고른 것일 때만 그 이름, 아니면 빌드 기본값.</summary>
    public static string Resolve(string saved, string savedBuildId) =>
        savedBuildId == BuildId ? Normalize(saved) ?? Default : Default;

    static string _name = Default;

    /// <summary>현재 표시 이름. 바뀌면 화면의 {b:Brand …} 문구가 바로 갱신된다.</summary>
    public static string Name => _name;

    /// <summary>"HECS Monitor" / "ECO Monitor"</summary>
    public static string AppName => $"{_name} Monitor";

    /// <summary>"HECS Live Compression Monitor" / "ECO Live Compression Monitor"</summary>
    public static string ProductName => $"{_name} Live Compression Monitor";

    public static event EventHandler Changed;

    /// <summary>설정 값으로 바꾼다. 비어 있거나 알 수 없는 값이면 빌드 기본값.</summary>
    public static void Set(string name)
    {
        var v = Normalize(name) ?? Default;
        if (v == _name) return;
        _name = v;
        Source.Raise();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string Normalize(string name) =>
        All.FirstOrDefault(b => string.Equals(b, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>문구 속 "HECS"를 현재 이름으로 바꾼다.</summary>
    public static string Apply(string text) => _name == Hecs ? text : text?.Replace(Hecs, _name);

    /// <summary>XAML 바인딩 소스 (BrandExtension이 사용)</summary>
    public sealed class BindingSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Name => _name;
        internal void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public static BindingSource Source { get; } = new();
}
