using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HecsMonitor.Services;

/// <summary>
/// 디코더 스레드가 넣은 최신 프레임 1장을 보관하고, UI 스레드가 렌더 타이밍에 비트맵으로 옮긴다.
/// 프레임마다 할당하지 않는다.
/// </summary>
public sealed class FrameSink
{
    readonly object _lock = new();
    readonly byte[] _buf;
    bool _dirty;

    public int Width { get; }
    public int Height { get; }
    public WriteableBitmap Bitmap { get; }

    public FrameSink(int width, int height)
    {
        Width = width;
        Height = height;
        _buf = new byte[width * height * 3];
        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
    }

    public void Submit(byte[] src)
    {
        lock (_lock)
        {
            Buffer.BlockCopy(src, 0, _buf, 0, Math.Min(src.Length, _buf.Length));
            _dirty = true;
        }
    }

    /// <summary>UI 스레드에서 호출. 새 프레임이 있었으면 true.</summary>
    public bool Present()
    {
        lock (_lock)
        {
            if (!_dirty) return false;
            Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _buf, Width * 3, 0);
            _dirty = false;
            return true;
        }
    }
}
