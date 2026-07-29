using System.Net.Http;
namespace SysPulsar.App.Usage;

/// <summary>
/// 120秒に1回のポーリング + 429 バックオフ。
/// 通信失敗時は直前の値を保持して表示を継続する。
/// イベントはバックグラウンドスレッドから投げるので UI 側で BeginInvoke すること。
/// </summary>
public sealed class UsagePoller : IDisposable
{
    readonly List<IUsageProvider> _providers;
    readonly Settings _settings;
    readonly HttpClient _http = new();
    readonly Dictionary<string, UsageSnapshot> _last = new();
    readonly CancellationTokenSource _cts = new();

    public event Action<UsageSnapshot>? SnapshotUpdated;

    public UsagePoller(IEnumerable<IUsageProvider> providers, Settings settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    public void Start()
    {
        foreach (var p in _providers)
            _ = Task.Run(() => LoopAsync(p));
    }

    public async Task RefreshNowAsync()
    {
        foreach (var p in _providers)
            await FetchOneAsync(p);
    }

    async Task LoopAsync(IUsageProvider provider)
    {
        while (!_cts.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(30, _settings.PollIntervalSec));
            try
            {
                await FetchOneAsync(provider);
            }
            catch (RateLimitException rlx)
            {
                delay = rlx.RetryAfter;
                Log.Info($"{provider.Id}: 429 のため {delay.TotalSeconds:0} 秒待機");
            }
            try { await Task.Delay(delay, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task FetchOneAsync(IUsageProvider provider)
    {
        try
        {
            var snap = await provider.FetchAsync(_http, _cts.Token);
            _last[provider.Id] = snap;
            Log.Info($"{provider.Id}: 取得成功（ゲージ {snap.Gauges.Count} 件" +
                     (snap.AuthMessage != null ? "、要認証" : "") + "）");
            SnapshotUpdated?.Invoke(snap);
        }
        catch (RateLimitException) { throw; }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // 直前値保持: 古いスナップショットにエラーを添えて表示継続
            if (_last.TryGetValue(provider.Id, out var old))
            {
                old.Error = ex.Message;
                SnapshotUpdated?.Invoke(old);
            }
            else
            {
                var s = new UsageSnapshot
                {
                    ProviderId = provider.Id,
                    DisplayName = provider.DisplayName,
                    Error = ex.Message,
                };
                _last[provider.Id] = s;
                SnapshotUpdated?.Invoke(s);
            }
            Log.Error($"{provider.Id}: 取得失敗（直前値を保持）: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}
