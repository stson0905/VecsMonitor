namespace HecsMonitor.ViewModels;

/// <summary>
/// 카드 보기의 페이지 배치. 카드 크기마다 한 화면(페이지)에 들어가는 개수는 고정이다.
///   S = 12개 (6×2) → 64채널 6쪽 · M = 8개 (4×2) → 8쪽 · L = 6개 (3×2) → 11쪽
/// 카드 한 장의 구성은 그대로 두고, 페이지 칸에 맞춰 카드 전체를 같은 비율로 확대·축소한다.
/// </summary>
public static class CardLayout
{
    public const double Gap = 14;            // 카드 사이 간격 (칸 오른쪽·아래 여백)

    // 카드에서 썸네일을 뺀 높이: 헤더 + 비트레이트 막대·절감률 링 + 60초 그래프 + 하단 요약 (MainWindow.xaml 카드 템플릿)
    const double Chrome = 224;

    public static int PageSize(string size) => size switch { "S" => 12, "L" => 6, _ => 8 };

    public static string Describe(string size) => size switch
    {
        "S" => "작게 · 한 화면 12채널",
        "L" => "크게 · 한 화면 6채널",
        _ => "보통 · 한 화면 8채널",
    };

    /// <summary>카드 기본 폭 (이보다 좁게 그리지 않고, 칸이 좁으면 카드 전체를 축소한다)</summary>
    static double BaseWidth(string size) => size switch { "S" => 286, "L" => 460, _ => 356 };

    /// <summary>같은 개수를 담는 배치 후보. 첫 번째가 기본 배치.</summary>
    static (int Cols, int Rows)[] Shapes(string size) => size switch
    {
        "S" => new[] { (6, 2), (4, 3) },
        "L" => new[] { (3, 2), (2, 3) },
        _ => new[] { (4, 2), (2, 4) },
    };

    public static double ThumbFor(double cardWidth) => Math.Round((cardWidth - 20) * 9 / 16);

    /// <summary>영역 크기에서 카드가 가장 크게 보이는 배치와 카드 폭을 고른다.</summary>
    public static Result Measure(string size, double width, double height)
    {
        Result best = default;
        foreach (var (cols, rows) in Shapes(size))
        {
            var r = Fit(size, cols, rows, width, height);
            if (best.Cols == 0 || r.Shown > best.Shown * 1.08) best = r;   // 기본 배치를 약간 우선
        }
        return best;
    }

    static Result Fit(string size, int cols, int rows, double width, double height)
    {
        double cw = Math.Max(80, width / cols - Gap);
        double ch = Math.Max(80, height / rows - Gap);
        double baseW = BaseWidth(size);

        // 카드 폭을 기본 폭 ~ 칸 폭 사이에서 바꿔 보며, 칸에 맞춰 축소했을 때 화면에 가장 크게 보이는 폭을 고른다.
        // 칸 높이가 모자라면 카드를 줄이기 전에 썸네일 높이를 16:9의 75%까지 먼저 줄인다 (위아래가 조금 잘림).
        double bestW = baseW, bestThumb = ThumbFor(baseW), bestShown = 0;
        for (double w = baseW; w <= Math.Max(baseW, cw); w += 4)
        {
            double natural = ThumbFor(w);
            double thumb = Math.Round(Math.Clamp(ch - Chrome, natural * 0.75, natural));
            double shown = w * Math.Min(cw / w, ch / (Chrome + thumb));
            if (shown > bestShown) { bestShown = shown; bestW = w; bestThumb = thumb; }
        }
        return new Result(cols, rows, bestW, bestThumb, Chrome + bestThumb, bestShown);
    }

    public readonly record struct Result(int Cols, int Rows, double CardWidth, double ThumbHeight, double CardHeight, double Shown);
}

/// <summary>마지막 페이지의 빈 칸. 첫 칸은 '채널 추가' 타일로 쓴다.</summary>
public sealed class EmptySlot
{
    public bool IsAdd { get; init; }
    public string AddText { get; init; }
    /// <summary>페이지 독 미니맵에서 빈 칸 표시용</summary>
    public string StatusKind => "Empty";
}

/// <summary>페이지 독의 버튼 하나. 페이지 배치 모양 그대로 채널 상태를 점으로 보여 준다.</summary>
public sealed class PageInfo : ObservableObject
{
    bool _isCurrent, _hasAlarm;
    string _toolTip;

    public PageInfo(int index, IReadOnlyList<object> slots, int first, int last)
    {
        Index = index;
        Slots = slots;
        RangeText = first == last ? $"{first}" : $"{first}–{last}";
        Refresh();
    }

    public int Index { get; }
    public string Label => (Index + 1).ToString();
    public string RangeText { get; }
    public IReadOnlyList<object> Slots { get; }

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    /// <summary>이 페이지에 압축률 경보·끊김 채널이 있음</summary>
    public bool HasAlarm { get => _hasAlarm; private set => Set(ref _hasAlarm, value); }
    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

    public void Refresh()
    {
        var chans = Slots.OfType<ChannelViewModel>().ToList();
        int alarms = chans.Count(c => c.StatusKind is "Alarm" or "Offline");
        HasAlarm = alarms > 0;
        ToolTip = $"{Label}페이지 · {RangeText}번 채널" + (alarms > 0 ? $" · 경보·끊김 {alarms}개" : " · 이상 없음");
    }
}
