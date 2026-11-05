using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HecsMonitor.Services;

/// <summary>
/// FFmpeg 프로세스 하나로 RTSP 스트림 하나를 처리한다.
///  - 출력 1: 비디오 패킷을 재인코딩 없이 framecrc 포맷으로 stderr(pipe:2)에 기록 → 패킷 크기로 비트레이트 측정
///  - 출력 2: (선택) 디코딩·축소한 BGR24 raw 프레임을 stdout(pipe:1)으로 → 썸네일
/// 카메라/HECS 서버에 대한 연결은 하나만 사용한다.
/// </summary>
public sealed class FfmpegSource : IStreamSource
{
    readonly SourceOptions _o;
    readonly object _procLock = new();
    CancellationTokenSource _cts;
    Process _proc;
    long _lastActivity;

    // 연결마다 초기화되는 헤더 파싱 상태
    string _codec;
    int _width, _height;
    bool _infoSent;

    public event Action<int, bool> Packet;
    public event Action<StreamInfo> Info;
    public event Action<byte[]> Frame;
    public event Action<SourceState, string> StateChanged;

    public FfmpegSource(SourceOptions options) => _o = options;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() => LoopAsync(ct));
    }

    public void Dispose()
    {
        _cts?.Cancel();
        lock (_procLock) Kill(_proc);
    }

    async Task LoopAsync(CancellationToken ct)
    {
        bool first = true;
        while (!ct.IsCancellationRequested)
        {
            StateChanged?.Invoke(first ? SourceState.Connecting : SourceState.Reconnecting,
                                 first ? "연결 중" : "재연결 중");
            first = false;

            string err;
            try { err = await RunOnceAsync(ct); }
            catch (Exception ex) { err = ex.Message; }

            if (ct.IsCancellationRequested) break;
            StateChanged?.Invoke(SourceState.Error, string.IsNullOrWhiteSpace(err) ? "스트림이 종료되었습니다" : err);

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _o.ReconnectSec)), ct); }
            catch (OperationCanceledException) { break; }
        }
        StateChanged?.Invoke(SourceState.Stopped, "중지됨");
    }

    async Task<string> RunOnceAsync(CancellationToken ct)
    {
        _codec = null; _width = _height = 0; _infoSent = false;

        using var p = new Process { StartInfo = BuildStartInfo() };
        try { p.Start(); }
        catch (Exception ex) { return $"FFmpeg 실행 실패 ({_o.FfmpegPath}): {ex.Message}"; }
        ChildProcessTracker.Add(p);
        lock (_procLock) _proc = p;

        string lastErr = null;
        bool live = false;
        Touch();

        void MarkLive()
        {
            Touch();
            if (live) return;
            live = true;
            StateChanged?.Invoke(SourceState.Live, "수신 중");
        }

        // 파이프 읽기는 프로세스가 끝날 때까지 블로킹되므로 스레드 풀이 아닌 전용 스레드에서 한다.
        // (채널이 많으면 풀 스레드가 모자라 읽기가 늦게 시작되고, 그동안 파이프가 차서 FFmpeg가 멈춘다)
        var errTask = Task.Factory.StartNew(() =>
        {
            try
            {
                string line;
                var reader = p.StandardError;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!ParseLine(line, MarkLive) && line.Trim().Length > 0)
                        lastErr = line.Trim();
                }
            }
            catch { }
        }, TaskCreationOptions.LongRunning);

        var outTask = _o.Decode
            ? Task.Factory.StartNew(() => ReadFrames(p.StandardOutput.BaseStream, MarkLive), TaskCreationOptions.LongRunning)
            : Task.CompletedTask;

        while (!p.HasExited)
        {
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { break; }

            double idle = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastActivity)) / (double)Stopwatch.Frequency;
            double limit = live ? _o.StallSec : _o.StallSec + 10;
            if (idle > limit)
            {
                lastErr = live ? $"{_o.StallSec}초 이상 데이터가 수신되지 않음" : (lastErr ?? "연결 시간 초과");
                break;
            }
        }

        Kill(p);
        await Task.WhenAny(Task.WhenAll(errTask, outTask), Task.Delay(2000));
        lock (_procLock) _proc = null;
        return Explain(lastErr);
    }

    /// <summary>FFmpeg 오류를 이해하기 쉬운 문장으로 바꾸고 비밀번호를 가린다.</summary>
    public static string Explain(string err)
    {
        if (string.IsNullOrWhiteSpace(err)) return err;
        err = StreamUrl.MaskSecrets(err);
        if (err.Contains("401")) return "인증 실패(401) · ID/비밀번호를 확인하세요. 계정은 채널 설정의 ID·비밀번호 칸에 입력합니다.";
        if (err.Contains("403")) return "접근 거부(403) · 계정 권한 또는 카메라의 RTSP 접근 허용 설정을 확인하세요.";
        if (err.Contains("404")) return "스트림 경로 없음(404) · RTSP 주소의 경로를 확인하세요. ONVIF 검색의 ‘주소 조회’로 정확한 경로를 받을 수 있습니다.";
        if (err.Contains("Connection refused")) return "연결 거부 · IP/포트와 장비의 RTSP 서비스를 확인하세요.";
        if (err.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "응답 시간 초과 · 네트워크 경로와 방화벽을 확인하세요.";
        return err;
    }

    void Touch() => Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());

    ProcessStartInfo BuildStartInfo()
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveFfmpeg(_o.FfmpegPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var a = psi.ArgumentList;
        string url = _o.Url.Trim();
        bool isRtsp = url.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase);
        bool isNetwork = url.Contains("://");

        a.Add("-hide_banner"); a.Add("-nostdin"); a.Add("-nostats");
        a.Add("-loglevel"); a.Add("error");
        if (isRtsp)
        {
            a.Add("-rtsp_transport"); a.Add(_o.RtspTransport == "udp" ? "udp" : "tcp");
            a.Add("-timeout"); a.Add("5000000");
        }
        if (!isNetwork)
        {
            // 로컬 파일은 실시간 속도로 무한 반복 → 테스트용
            a.Add("-re"); a.Add("-stream_loop"); a.Add("-1");
        }
        a.Add("-fflags"); a.Add("nobuffer");
        if (_o.Decode && _o.HwAccel) { a.Add("-hwaccel"); a.Add("auto"); }
        a.Add("-i"); a.Add(url);

        if (_o.Measure)
        {
            a.Add("-map"); a.Add("0:v:0");
            a.Add("-c"); a.Add("copy");
            a.Add("-f"); a.Add("framecrc");
            a.Add("-flush_packets"); a.Add("1");
            a.Add("pipe:2");
        }
        if (_o.Decode)
        {
            int w = _o.Width, h = _o.Height;
            a.Add("-map"); a.Add("0:v:0");
            a.Add("-an"); a.Add("-sn");
            a.Add("-vf");
            a.Add($"fps={_o.Fps},scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black");
            a.Add("-pix_fmt"); a.Add("bgr24");
            a.Add("-f"); a.Add("rawvideo");
            a.Add("pipe:1");
        }
        return psi;
    }

    public static string ResolveFfmpeg(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || configured.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            return File.Exists(local) ? local : "ffmpeg";
        }
        return configured;
    }

    /// <summary>framecrc 줄이면 true. 그 외(오류 메시지 등)는 false.</summary>
    bool ParseLine(string line, Action markLive)
    {
        if (line.Length == 0) return true;

        if (line[0] == '#')
        {
            // #codec_id 0: h264 / #dimensions 0: 1920x1080
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            string value = colon > 0 ? line[(colon + 2)..].Trim() : "";
            if (line.StartsWith("#codec_id", StringComparison.Ordinal)) _codec = value;
            else if (line.StartsWith("#dimensions", StringComparison.Ordinal))
            {
                var wh = value.Split('x');
                if (wh.Length == 2) { int.TryParse(wh[0], out _width); int.TryParse(wh[1], out _height); }
            }
            if (!_infoSent && _codec != null && _width > 0) SendInfo();
            return true;
        }

        // stream, dts, pts, duration, size, crc[, F=0x..]
        if (!char.IsDigit(line[0])) return false;
        var parts = line.Split(',');
        if (parts.Length < 6 || !int.TryParse(parts[4].Trim(), out int size)) return false;

        bool key = true;
        if (parts.Length >= 7)
        {
            var f = parts[6].Trim();
            if (f.StartsWith("F=0x", StringComparison.Ordinal) &&
                int.TryParse(f.AsSpan(4), NumberStyles.HexNumber, null, out int flags))
                key = (flags & 1) != 0;
        }
        if (!_infoSent) SendInfo();
        markLive();
        Packet?.Invoke(size, key);
        return true;
    }

    void SendInfo()
    {
        _infoSent = true;
        Info?.Invoke(new StreamInfo(_codec, _width, _height));
    }

    void ReadFrames(Stream s, Action markLive)
    {
        int size = _o.Width * _o.Height * 3;
        var buf = new byte[size];
        try
        {
            while (true)
            {
                int off = 0;
                while (off < size)
                {
                    int n = s.Read(buf, off, size - off);
                    if (n <= 0) return;
                    off += n;
                }
                markLive();
                Frame?.Invoke(buf);
            }
        }
        catch { }
    }

    static void Kill(Process p)
    {
        try { if (p != null && !p.HasExited) p.Kill(true); } catch { }
    }
}
