namespace HecsMonitor.Services;

public enum SourceState { Stopped, Connecting, Live, Reconnecting, Error }

public sealed record StreamInfo(string Codec, int Width, int Height)
{
    public string CodecLabel => Codec switch
    {
        "h264" => "H.264",
        "hevc" or "h265" => "H.265",
        "av1" => "AV1",
        "vp9" => "VP9",
        "mjpeg" => "MJPEG",
        "mpeg4" => "MPEG-4",
        null or "" => "—",
        _ => Codec.ToUpperInvariant(),
    };

    public string Resolution => Width > 0 ? $"{Width}×{Height}" : "—";
}

public sealed class SourceOptions
{
    public string Url { get; set; }
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string RtspTransport { get; set; } = "tcp";
    public bool HwAccel { get; set; }
    /// <summary>패킷 크기 측정(비트레이트) 여부.</summary>
    public bool Measure { get; set; } = true;
    /// <summary>디코딩해서 BGR24 프레임을 내보낼지 여부.</summary>
    public bool Decode { get; set; }
    public int Width { get; set; } = 384;
    public int Height { get; set; } = 216;
    public int Fps { get; set; } = 10;
    public int ReconnectSec { get; set; } = 5;
    public int StallSec { get; set; } = 10;

    public SourceOptions With(Action<SourceOptions> edit)
    {
        var o = (SourceOptions)MemberwiseClone();
        edit(o);
        return o;
    }
}

/// <summary>
/// 하나의 비디오 스트림. 이벤트는 백그라운드 스레드에서 발생한다.
/// Frame 이벤트의 버퍼는 콜백 동안에만 유효하다(Width*Height*3, BGR24).
/// </summary>
public interface IStreamSource : IDisposable
{
    event Action<int, bool> Packet;
    event Action<StreamInfo> Info;
    event Action<byte[]> Frame;
    event Action<SourceState, string> StateChanged;
    void Start();
}

public static class StreamSourceFactory
{
    public static IStreamSource Create(SourceOptions o) =>
        o.Url.StartsWith("demo://", StringComparison.OrdinalIgnoreCase)
            ? new DemoSource(o)
            : new FfmpegSource(o);

    /// <summary>로그·화면 표시용으로 URL의 비밀번호를 가린다.</summary>
    public static string MaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        int at = url.LastIndexOf('@');
        if (scheme < 0 || at < scheme) return url;
        int colon = url.IndexOf(':', scheme + 3);
        if (colon < 0 || colon > at) return url;
        return url[..(colon + 1)] + "****" + url[at..];
    }
}
