using System.Text.Json;
using HecsMonitor.Models;

namespace HecsMonitor.Services;

public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HECSMonitor");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch
        {
            // 손상된 설정 파일은 백업 후 기본값으로 시작
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
        }
        return CreateDefault();
    }

    public static void Save(AppSettings s)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Json));
    }

    public static AppSettings Clone(AppSettings s)
    {
        var c = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json), Json);
        c.Normalize();   // 파일에 저장하지 않는 항목(로그 분류 설명 등)을 다시 채운다
        return c;
    }

    /// <summary>첫 실행 시 카메라 없이도 화면을 확인할 수 있도록 데모 채널을 넣어 둔다.</summary>
    static AppSettings CreateDefault()
    {
        var s = new AppSettings();
        (string name, string group, double mbps, double ratio)[] demo =
        {
            ("정문 출입구", "1층", 6, 9),
            ("주차장 A", "지하 1층", 4, 7.5),
            ("로비", "1층", 5, 11),
            ("물류 하역장", "외곽", 8, 6),
            ("서버실", "3층", 3, 14),
            ("후문 (저압축 경보 데모)", "외곽", 4, 3.2),
        };
        foreach (var d in demo)
        {
            var key = d.name.Replace("?", "").Replace("&", "");
            s.Channels.Add(new ChannelConfig
            {
                Name = d.name,
                Group = d.group,
                OriginalUrl = $"demo://{key}?mbps={d.mbps}&codec=h264&fps=30&gop=60",
                // 주차장 A는 HECS 출력 프레임 손실(30→27fps) 표시 데모
                HecsUrl = $"demo://{key}?mbps={d.mbps}&ratio={d.ratio}&codec=hevc&gop=60&fps={(d.name == "주차장 A" ? 27 : 30)}",
            });
        }
        return s;
    }
}
