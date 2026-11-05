using System.Text.Json;

namespace HecsMonitorLicenseGen;

/// <summary>발급 프로그램 설정: %APPDATA%\HecsMonitorLicenseGen\config.json</summary>
public sealed class GenSettings
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HecsMonitorLicenseGen");
    static string FilePath => Path.Combine(DataFolder, "config.json");

    public static string DefaultOutputRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HECS Monitor Licenses");

    /// <summary>마지막으로 불러온 서명 개인키 경로</summary>
    public string PrivateKeyPath { get; set; }
    /// <summary>license.key 저장 폴더 (비어 있으면 기본값)</summary>
    public string OutputRoot { get; set; }
    // 개발자 모드(설정 → 버전 5회 클릭)는 저장하지 않는다. 실행할 때마다 꺼진 상태로 시작한다.
    /// <summary>개발자 모드에서 고른 제품 이름 (HECS | ECO). BrandBuild가 지금 빌드와 같을 때만 쓰고, 아니면 빌드 기본값</summary>
    public string Brand { get; set; }
    /// <summary>Brand를 고른 빌드의 식별값 (HecsMonitor.Services.Brand.BuildId)</summary>
    public string BrandBuild { get; set; }

    public static GenSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<GenSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
