using System.Diagnostics;
using System.Globalization;

namespace HecsMonitor.Services;

/// <summary>
/// 카메라 없이 UI·측정 로직을 확인하기 위한 가상 스트림.
/// URL 형식: demo://이름?mbps=4&amp;ratio=8&amp;codec=hevc&amp;fps=25&amp;gop=50
/// 같은 이름의 원본/HECS 데모는 같은 장면과 같은 활동량 곡선을 공유한다.
/// </summary>
public sealed class DemoSource : IStreamSource
{
    readonly SourceOptions _o;
    readonly double _mbps, _ratio;
    readonly int _fps, _gop;
    readonly string _codec;
    readonly uint _seed;
    volatile bool _running;
    Thread _thread;

    public event Action<int, bool> Packet;
    public event Action<StreamInfo> Info;
    public event Action<byte[]> Frame;
    public event Action<SourceState, string> StateChanged;

    public DemoSource(SourceOptions o)
    {
        _o = o;
        var uri = o.Url["demo://".Length..];
        int q = uri.IndexOf('?');
        string name = q >= 0 ? uri[..q] : uri;
        var args = (q >= 0 ? uri[(q + 1)..] : "")
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('=', 2))
            .ToDictionary(kv => kv[0].ToLowerInvariant(), kv => kv.Length > 1 ? kv[1] : "");

        double Num(string k, double d) =>
            args.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : d;

        _mbps = Num("mbps", 4);
        _ratio = Math.Max(1, Num("ratio", 1));
        _fps = (int)Num("fps", 25);
        _gop = Math.Max(1, (int)Num("gop", 50));
        _codec = args.TryGetValue("codec", out var c) ? c : "h264";
        _seed = Fnv(name);
    }

    public void Start()
    {
        if (_thread != null) return;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "demo-source" };
        _thread.Start();
    }

    public void Dispose() => _running = false;

    static double Now() => DateTime.UtcNow.Ticks % (TimeSpan.TicksPerDay) / 1e7;

    void Run()
    {
        StateChanged?.Invoke(SourceState.Connecting, "연결 중");
        Thread.Sleep(400 + (int)(_seed % 900));
        if (!_running) { StateChanged?.Invoke(SourceState.Stopped, "중지됨"); return; }

        Info?.Invoke(new StreamInfo(_codec, 1920, 1080));
        StateChanged?.Invoke(SourceState.Live, "수신 중 (데모)");

        var rnd = new Random((int)_seed ^ (int)(_ratio * 1000));
        var renderer = _o.Decode ? new DemoRenderer(_o.Width, _o.Height, _seed, posterize: _ratio > 1) : null;
        double phase = (_seed % 1000) / 100.0;
        var sw = Stopwatch.StartNew();
        long frame = 0;
        double nextThumb = 0;

        while (_running)
        {
            double t = Now();
            double activity = 0.75 + 0.22 * Math.Sin(t * 0.21 + phase) + 0.08 * Math.Sin(t * 1.3 + phase * 2);
            double ratioNow = _ratio <= 1 ? 1 : _ratio * (1 + 0.16 * Math.Sin(t * 0.045 + phase));
            double bps = _mbps * 1e6 * activity / ratioNow;
            double avgFrame = bps / 8 / _fps;
            bool key = frame % _gop == 0;
            double pFrame = avgFrame * _gop / (_gop - 1 + 6.0);
            double size = (key ? pFrame * 6 : pFrame) * (0.85 + rnd.NextDouble() * 0.3);

            if (_o.Measure) Packet?.Invoke((int)size, key);

            double elapsed = sw.Elapsed.TotalSeconds;
            if (renderer != null && elapsed >= nextThumb)
            {
                nextThumb = elapsed + 1.0 / Math.Max(1, _o.Fps);
                Frame?.Invoke(renderer.Render(t));
            }

            frame++;
            double due = frame / (double)_fps;
            double wait = due - sw.Elapsed.TotalSeconds;
            if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
        }
        StateChanged?.Invoke(SourceState.Stopped, "중지됨");
    }

    static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }

    /// <summary>간단한 가상 CCTV 장면 (도로 + 움직이는 차량).</summary>
    sealed class DemoRenderer
    {
        readonly int _w, _h;
        readonly byte[] _bg, _frame;
        readonly bool _posterize;
        readonly (double speed, double lane, double len, byte r, byte g, byte b, double offset)[] _cars;

        static readonly (byte r, byte g, byte b, byte r2, byte g2, byte b2)[] Palettes =
        {
            (38, 58, 84, 64, 72, 70),
            (70, 52, 40, 52, 58, 46),
            (30, 44, 52, 74, 80, 88),
            (52, 40, 70, 46, 54, 50),
            (24, 60, 66, 60, 62, 58),
            (64, 64, 76, 40, 46, 40),
        };

        public DemoRenderer(int w, int h, uint seed, bool posterize)
        {
            _w = w; _h = h; _posterize = posterize;
            _bg = new byte[w * h * 3];
            _frame = new byte[w * h * 3];
            var rnd = new Random((int)seed);
            var p = Palettes[seed % Palettes.Length];
            int horizon = h * 2 / 5;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 3;
                    double r, g, b;
                    if (y < horizon)
                    {
                        double k = y / (double)horizon;
                        r = p.r + 60 * k; g = p.g + 60 * k; b = p.b + 50 * k;
                    }
                    else
                    {
                        double k = (y - horizon) / (double)(h - horizon);
                        r = p.r2 * (1 - 0.4 * k); g = p.g2 * (1 - 0.4 * k); b = p.b2 * (1 - 0.4 * k);
                        // 차선
                        bool lane = (y == horizon + (h - horizon) / 3 || y == horizon + 2 * (h - horizon) / 3) && (x / 18) % 2 == 0;
                        if (lane) { r = g = 210; b = 170; }
                    }
                    double n = rnd.NextDouble() * 10 - 5;
                    _bg[i] = Clamp(b + n); _bg[i + 1] = Clamp(g + n); _bg[i + 2] = Clamp(r + n);
                }
            }
            // 건물 실루엣
            for (int k = 0; k < 7; k++)
            {
                int bx = rnd.Next(w), bw = rnd.Next(w / 14, w / 6), bh = rnd.Next(h / 10, horizon - 4);
                byte shade = (byte)rnd.Next(18, 40);
                for (int y = horizon - bh; y < horizon; y++)
                    for (int x = bx; x < Math.Min(w, bx + bw); x++)
                    {
                        int i = (y * w + x) * 3;
                        bool window = (x - bx) % 8 < 3 && (y - horizon + bh) % 9 < 3 && rnd.NextDouble() < 0.5;
                        _bg[i] = _bg[i + 1] = _bg[i + 2] = window ? (byte)150 : shade;
                    }
            }
            _cars = Enumerable.Range(0, 5).Select(_ => (
                speed: (rnd.NextDouble() * 0.12 + 0.05) * (rnd.Next(2) == 0 ? 1 : -1),
                lane: (double)rnd.Next(3),
                len: rnd.NextDouble() * 0.08 + 0.07,
                r: (byte)rnd.Next(80, 255), g: (byte)rnd.Next(40, 220), b: (byte)rnd.Next(40, 220),
                offset: rnd.NextDouble())).ToArray();
        }

        public byte[] Render(double t)
        {
            Buffer.BlockCopy(_bg, 0, _frame, 0, _bg.Length);
            int horizon = _h * 2 / 5;
            int laneH = (_h - horizon) / 3;
            foreach (var c in _cars)
            {
                double pos = (c.offset + c.speed * t) % 1.0;
                if (pos < 0) pos += 1;
                int cw = (int)(c.len * _w), ch = (int)(laneH * 0.55);
                int x0 = (int)(pos * (_w + cw)) - cw;
                int y0 = horizon + (int)(c.lane * laneH + laneH * 0.25);
                FillRect(x0, y0, cw, ch, c.r, c.g, c.b);
                FillRect(x0 + cw / 5, y0 - ch / 3, cw * 3 / 5, ch / 3 + 1, (byte)(c.r * 0.7), (byte)(c.g * 0.7), (byte)(c.b * 0.7));
                FillRect(x0 + (c.speed > 0 ? cw - 3 : 0), y0 + 2, 3, 3, 255, 250, 200);
            }
            // 타임스탬프 막대 (1초 주기)
            int bar = (int)((t % 1.0) * (_w / 5.0));
            FillRect(_w - _w / 5 - 8, 8, bar, 3, 230, 230, 230);

            if (_posterize)
                for (int i = 0; i < _frame.Length; i++) _frame[i] = (byte)(_frame[i] & 0xF0 | 0x08);
            return _frame;
        }

        void FillRect(int x0, int y0, int w, int h, byte r, byte g, byte b)
        {
            int xs = Math.Max(0, x0), xe = Math.Min(_w, x0 + w);
            int ys = Math.Max(0, y0), ye = Math.Min(_h, y0 + h);
            for (int y = ys; y < ye; y++)
                for (int x = xs; x < xe; x++)
                {
                    int i = (y * _w + x) * 3;
                    _frame[i] = b; _frame[i + 1] = g; _frame[i + 2] = r;
                }
        }

        static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
    }
}
