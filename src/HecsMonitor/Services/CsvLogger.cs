using System.Globalization;
using System.Text;

namespace HecsMonitor.Services;

/// <summary>측정 기록(CSV)과 이벤트 로그를 일자별 파일로 남긴다.</summary>
public static class CsvLogger
{
    static readonly Encoding Utf8Bom = new UTF8Encoding(true);
    const string Header = "timestamp,channel,original_kbps,hecs_kbps,ratio,saving_pct,original_fps,hecs_fps,status";

    public static void AppendMeasurement(string folder, IEnumerable<string[]> rows)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"measure_{DateTime.Now:yyyy-MM-dd}.csv");
            var sb = new StringBuilder();
            if (!File.Exists(path)) sb.AppendLine(Header);
            foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Escape)));
            File.AppendAllText(path, sb.ToString(), Utf8Bom);
        }
        catch { }
    }

    public static void AppendEvent(string folder, DateTime time, string level, string channel, string message)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"events_{time:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{time:yyyy-MM-dd HH:mm:ss}\t{level}\t{channel}\t{message}{Environment.NewLine}", Utf8Bom);
        }
        catch { }
    }

    public static void WriteTable(string path, string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Escape)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Escape)));
        File.WriteAllText(path, sb.ToString(), Utf8Bom);
    }

    static IEnumerable<FileInfo> LogFiles(string folder)
    {
        var dir = new DirectoryInfo(folder);
        if (!dir.Exists) return Enumerable.Empty<FileInfo>();
        return dir.EnumerateFiles("measure_*.csv").Concat(dir.EnumerateFiles("events_*.log"));
    }

    public static long FolderBytes(string folder)
    {
        try { return LogFiles(folder).Sum(f => f.Length); } catch { return 0; }
    }

    /// <summary>
    /// 기록 폴더가 maxMB를 넘으면 overflow 정책에 따라 처리한다.
    /// delete: 오늘 파일을 제외한 가장 오래된 파일부터 삭제. 반환값: (용량 초과 여부, 삭제한 파일 수)
    /// </summary>
    public static (bool Over, int Deleted) EnforceQuota(string folder, int maxMB, string overflow)
    {
        if (maxMB <= 0) return (false, 0);
        long limit = maxMB * 1024L * 1024L;
        try
        {
            var files = LogFiles(folder).OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            if (total <= limit) return (false, 0);
            if (overflow != "delete") return (true, 0);

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int deleted = 0;
            foreach (var f in files)
            {
                if (total <= limit) break;
                if (f.Name.Contains(today)) continue;
                total -= f.Length;
                f.Delete();
                deleted++;
            }
            return (total > limit, deleted);
        }
        catch { return (false, 0); }
    }

    public static string N(double v, string fmt = "0.0") =>
        double.IsNaN(v) ? "" : v.ToString(fmt, CultureInfo.InvariantCulture);

    static string Escape(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
