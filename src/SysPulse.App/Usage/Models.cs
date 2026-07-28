namespace SysPulse.App.Usage;

/// <summary>ゲージ1本ぶんのデータ。</summary>
public sealed class GaugeInfo
{
    public string Label { get; set; } = "";
    public double Percent { get; set; }
    public DateTimeOffset? ResetAtUtc { get; set; }
}

/// <summary>プロバイダ1社ぶんの取得結果スナップショット。</summary>
public sealed class UsageSnapshot
{
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public List<GaugeInfo> Gauges { get; } = new();

    /// <summary>通信失敗など。直前値を保持したまま表示する場合にもセットされる。</summary>
    public string? Error { get; set; }

    /// <summary>未ログイン・トークン失効など。認証の「読むだけ」原則により警告表示のみ。</summary>
    public string? AuthMessage { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.Now;

    public double WorstPercent => Gauges.Count == 0 ? 0 : Gauges.Max(g => g.Percent);
}

/// <summary>HTTP 429。Retry-After 優先・無ければ300秒のバックオフを運ぶ。</summary>
public sealed class RateLimitException : Exception
{
    public TimeSpan RetryAfter { get; }

    public RateLimitException(TimeSpan retryAfter)
        : base($"rate limited; retry after {retryAfter.TotalSeconds:0}s")
    {
        RetryAfter = retryAfter;
    }
}

static class AppOptions
{
    /// <summary>--debug: 生JSONを %LOCALAPPDATA%\UsageWatcher\debug に保存する。</summary>
    public static bool Debug;
}
