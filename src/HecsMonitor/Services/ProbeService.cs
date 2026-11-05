using System.Diagnostics;
using HecsMonitor.Models;

namespace HecsMonitor.Services;

/// <summary>설정 화면의 "연결 테스트" / "FFmpeg 확인".</summary>
public static class ProbeService
{
    public static async Task<string> TestUrlAsync(string url, AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(url)) return "✖ URL을 입력하세요";

        var meter = new BitrateMeter();
        StreamInfo info = null;
        string lastErr = null;
        using var src = StreamSourceFactory.Create(new SourceOptions
        {
            Url = url.Trim(),
            FfmpegPath = s.FfmpegPath,
            RtspTransport = s.RtspTransport,
            Measure = true,
            Decode = false,
            ReconnectSec = 60,
            StallSec = 10,
        });
        src.Packet += meter.Add;
        src.Info += i => info = i;
        src.StateChanged += (st, msg) => { if (st == SourceState.Error) lastErr = msg; };
        src.Start();

        var sw = Stopwatch.StartNew();
        while (meter.TotalPackets == 0 && lastErr == null && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(200);
        if (meter.TotalPackets == 0)
            return "✖ 연결 실패 · " + (lastErr ?? "응답 없음 (15초)");

        await Task.Delay(4000);
        var r = meter.Read(4);
        return $"✔ 연결 성공 · {info?.CodecLabel} {info?.Resolution} · {r.Fps:0.#} fps · {Fmt.RateText(r.Bps)}" +
               (r.Gop > 0 ? $" · GOP {r.Gop}" : "");
    }

    public static async Task<string> CheckFfmpegAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo(FfmpegSource.ResolveFfmpeg(path), "-hide_banner -version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            string first = await p.StandardOutput.ReadLineAsync();
            await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
            return "✔ " + (first ?? "FFmpeg 응답 확인");
        }
        catch (Exception ex)
        {
            return "✖ FFmpeg를 찾을 수 없습니다 · " + ex.Message;
        }
    }
}
