using System.Diagnostics;

namespace HecsMonitor.Services;

public readonly record struct MeterReading(double Bps, double Fps, int Gop);

/// <summary>수신 패킷 크기를 도착 시각 기준 슬라이딩 윈도로 집계한다. 스레드 안전.</summary>
public sealed class BitrateMeter
{
    const double MaxWindowSec = 12;

    readonly object _lock = new();
    readonly Queue<(long t, int size)> _q = new();
    long _firstTicks = -1;
    long _lastTicks;
    long _packets;
    long _lastKeyPacket = -1;
    int _gop;

    public long TotalBytes { get; private set; }
    public long TotalPackets { get { lock (_lock) return _packets; } }

    public void Add(int size, bool key)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_firstTicks < 0) _firstTicks = now;
            _q.Enqueue((now, size));
            TotalBytes += size;
            _packets++;
            if (key)
            {
                if (_lastKeyPacket >= 0) _gop = (int)(_packets - _lastKeyPacket);
                _lastKeyPacket = _packets;
            }
            _lastTicks = now;
            Trim(now);
        }
    }

    public MeterReading Read(double windowSec)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            Trim(now);
            if (_firstTicks < 0) return default;

            // 시작 직후에는 실제 경과 시간만큼만 나눈다 (과소 측정 방지)
            double since = (now - _firstTicks) / (double)Stopwatch.Frequency;
            double win = Math.Min(windowSec, since);
            if (win < 0.5) return new MeterReading(0, 0, _gop);

            long from = now - (long)(win * Stopwatch.Frequency);
            long bytes = 0; int count = 0;
            foreach (var (t, size) in _q)
            {
                if (t < from) continue;
                bytes += size; count++;
            }
            return new MeterReading(bytes * 8 / win, count / win, _gop);
        }
    }

    public double SecondsSinceLastPacket
    {
        get
        {
            lock (_lock)
                return _firstTicks < 0 ? double.PositiveInfinity
                    : (Stopwatch.GetTimestamp() - _lastTicks) / (double)Stopwatch.Frequency;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _q.Clear();
            _firstTicks = -1;
            _packets = 0;
            _lastKeyPacket = -1;
            _gop = 0;
            TotalBytes = 0;
        }
    }

    void Trim(long now)
    {
        long limit = now - (long)(MaxWindowSec * Stopwatch.Frequency);
        while (_q.Count > 0 && _q.Peek().t < limit) _q.Dequeue();
    }
}
