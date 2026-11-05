using System.Text.Json;
using HecsMonitor.Licensing;

namespace HecsMonitorLicenseGen;

/// <summary>발급 이력 한 건. 같은 조건으로 다시 저장할 수 있도록 발급한 파일 내용도 보관한다.</summary>
public sealed class LedgerEntry
{
    public string LicenseId { get; set; }
    public string Licensee { get; set; }
    public int Channels { get; set; }
    public DateOnly IssuedDate { get; set; }
    public string MachineId { get; set; }
    public string Memo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string FilePath { get; set; }
    public string LicenseText { get; set; }

    public string MachineText => SoftwareLicenseCore.FormatMachineId(MachineId);
    public string DateText => IssuedDate.ToString("yyyy-MM-dd");
    public string Title => string.IsNullOrWhiteSpace(Licensee) ? "(사용자 미지정)" : Licensee;
}

/// <summary>발급 이력 저장소: %APPDATA%\HecsMonitorLicenseGen\ledger.json</summary>
public static class Ledger
{
    static string PathName => Path.Combine(GenSettings.DataFolder, "ledger.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<LedgerEntry> Load()
    {
        try
        {
            if (File.Exists(PathName))
                return JsonSerializer.Deserialize<List<LedgerEntry>>(File.ReadAllText(PathName), Json) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(IEnumerable<LedgerEntry> entries)
    {
        Directory.CreateDirectory(GenSettings.DataFolder);
        File.WriteAllText(PathName, JsonSerializer.Serialize(entries.ToList(), Json));
    }
}
