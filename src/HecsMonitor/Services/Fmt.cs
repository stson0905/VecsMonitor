namespace HecsMonitor.Services;

public static class Fmt
{
    public static (string Value, string Unit) Rate(double bps)
    {
        if (double.IsNaN(bps) || bps <= 0) return ("—", "Mbps");
        if (bps >= 1e6)
        {
            double m = bps / 1e6;
            return (m.ToString(m >= 100 ? "0" : m >= 10 ? "0.0" : "0.00"), "Mbps");
        }
        double k = bps / 1e3;
        return (k.ToString(k >= 100 ? "0" : "0.0"), "kbps");
    }

    public static string RateText(double bps)
    {
        var (v, u) = Rate(bps);
        return v == "—" ? "—" : $"{v} {u}";
    }

    public static string Bytes(double b)
    {
        if (double.IsNaN(b) || b <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return $"{b.ToString(b >= 100 ? "0" : b >= 10 ? "0.0" : "0.00")} {units[i]}";
    }

    public static string Ratio(double r) =>
        double.IsNaN(r) || double.IsInfinity(r) || r <= 0 ? "—" : r >= 100 ? r.ToString("0") : r.ToString("0.0");

    public static string Percent(double fraction) =>
        double.IsNaN(fraction) ? "—" : (fraction * 100).ToString("0.0");

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}일 {t:hh\\:mm\\:ss}" : t.ToString(@"hh\:mm\:ss");
}
