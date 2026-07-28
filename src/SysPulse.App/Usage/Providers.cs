using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SysPulse.App.Usage;

public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>
    /// 使用量を取得する。認証ファイルは読み取り専用で開き、絶対に書き込まない
    /// （書き込むと本家 CLI のログインが壊れる）。
    /// HTTP 429 は RateLimitException を投げる。それ以外の失敗は例外を投げてよい
    /// （呼び出し側で直前値を保持する）。
    /// </summary>
    Task<UsageSnapshot> FetchAsync(HttpClient http, CancellationToken ct);
}

/// <summary>
/// Claude Code (Max/Pro) の使用量。
/// GET https://api.anthropic.com/api/oauth/usage
/// 認証: ~/.claude/.credentials.json → claudeAiOauth.accessToken（読むだけ）
/// 抽出: limits[] 配列のみ（トップレベルの重複キーは見ない）。
/// ※ 非公式API。スキーマ変更時は --debug の生JSONで確認して本クラスを直す。
/// </summary>
public sealed class ClaudeProvider : IUsageProvider
{
    public string Id => "claude";
    public string DisplayName => "Claude";

    static readonly string CredPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    public async Task<UsageSnapshot> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var snap = new UsageSnapshot { ProviderId = Id, DisplayName = DisplayName };

        if (!File.Exists(CredPath))
        {
            snap.AuthMessage = "Claude Code にログインしてください";
            return snap;
        }

        string? token = null;
        try
        {
            // 本家が定期更新するため共有読み取りで開く。書き込みは絶対にしない。
            using var fs = new FileStream(CredPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var cred = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
            if (cred.RootElement.TryGetProperty("claudeAiOauth", out var oa) &&
                oa.TryGetProperty("accessToken", out var at))
                token = at.GetString();
        }
        catch (Exception ex)
        {
            snap.Error = "認証ファイルの読み取りに失敗: " + ex.Message;
            return snap;
        }

        if (string.IsNullOrEmpty(token))
        {
            snap.AuthMessage = "Claude Code にログインしてください（accessToken なし）";
            return snap;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // anthropic-beta の値は変わり得る。401/400 が出たら --debug で確認して調整する。
        req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Headers.TryAddWithoutValidation("User-Agent", "usage-watcher/1.0");

        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            snap.AuthMessage = "トークン失効。Claude Code で再ログインしてください";
            return snap;
        }
        if (res.StatusCode == (HttpStatusCode)429)
            throw RateLimited(res);

        var body = await res.Content.ReadAsStringAsync(ct);
        Log.DumpDebugJson(Id, body);
        res.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var lim in limits.EnumerateArray())
            {
                var kind = lim.TryGetProperty("kind", out var k) ? k.GetString() : null;
                var pct = lim.TryGetProperty("percent", out var p) && p.ValueKind == JsonValueKind.Number
                    ? p.GetDouble() : 0;
                DateTimeOffset? reset = null;
                if (lim.TryGetProperty("resets_at", out var r) &&
                    r.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var dto))
                    reset = dto;

                // 表示対象は Current session / All models の2本（週・モデル別は出さない）
                var label = kind switch
                {
                    "session" => "Current session",
                    "weekly_all" => "All models",
                    _ => null,
                };
                if (label == null) continue;

                snap.Gauges.Add(new GaugeInfo
                {
                    Label = label,
                    Percent = pct,
                    ResetAtUtc = reset,
                });
            }
        }
        return snap;
    }

    internal static RateLimitException RateLimited(HttpResponseMessage res)
    {
        var ra = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(300);
        if (ra < TimeSpan.FromSeconds(300)) ra = TimeSpan.FromSeconds(300);
        return new RateLimitException(ra);
    }
}

/// <summary>
/// Kimi Code (Kimi メンバーシップ) の使用量。
/// GET {base_url}/usages  （実測: https://agent-gw.kimi.com/coding/v1/usages → 200）
/// 認証: kimi-code config.toml の [providers.*] type="kimi" → api_key / base_url（読むだけ）
/// レスポンス: totalQuota { limit, used, remaining, resetTime(UTC) }
/// ※ 非公式API。CLI の /usage と同じ経路。スキーマ変更時は --debug で確認。
/// </summary>
public sealed class KimiProvider : IUsageProvider
{
    public string Id => "kimi";
    public string DisplayName => "Kimi";

    static string[] CandidateConfigPaths() =>
    [
        // Kimi Code CLI ユーザ設定
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kimi-code", "config.toml"),
        // Kimi Work (kimi-desktop) 同梱ランタイム
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kimi-desktop", "daimon-share", "daimon", "runtime", "kimi-code", "config.toml"),
    ];

    public async Task<UsageSnapshot> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var snap = new UsageSnapshot { ProviderId = Id, DisplayName = DisplayName };

        var path = CandidateConfigPaths().FirstOrDefault(File.Exists);
        if (path == null)
        {
            snap.AuthMessage = "Kimi Code が見つかりません（kimi で /login してください）";
            return snap;
        }

        string? apiKey, baseUrl;
        try
        {
            (apiKey, baseUrl) = ParseKimiConfig(await File.ReadAllLinesAsync(path, ct));
        }
        catch (Exception ex)
        {
            snap.Error = "設定ファイルの読み取りに失敗: " + ex.Message;
            return snap;
        }

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(baseUrl))
        {
            snap.AuthMessage = "Kimi の API キーが見つかりません（kimi で /login してください）";
            return snap;
        }

        // 取得先は2系統（実測済み）:
        //  - {設定の base_url}/usages      … Kimi Work 同梱の agent-gw は totalQuota（Total usage）だけ返す
        //  - https://api.kimi.com/coding/v1/usages … 公式ホスト。usage（7-day）と limits[300分窓]（5-hour）が返る
        var primaryUrl = baseUrl.TrimEnd('/') + "/usages";
        const string publicUrl = "https://api.kimi.com/coding/v1/usages";
        var urls = new List<string> { primaryUrl };
        if (!primaryUrl.Equals(publicUrl, StringComparison.OrdinalIgnoreCase)) urls.Add(publicUrl);

        var gotAny = false;
        Exception? primaryError = null;
        foreach (var url in urls)
        {
            var isPrimary = url == primaryUrl;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Headers.TryAddWithoutValidation("User-Agent", "usage-watcher/1.0");
                using var res = await http.SendAsync(req, ct);

                if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (isPrimary)
                    {
                        snap.AuthMessage = "Kimi の API キーが無効です（kimi で /login し直してください）";
                        return snap;
                    }
                    continue; // 補助側の失敗は無視（取れるものだけ表示）
                }
                if (res.StatusCode == (HttpStatusCode)429)
                {
                    if (isPrimary) throw ClaudeProvider.RateLimited(res);
                    continue;
                }
                if (!res.IsSuccessStatusCode)
                {
                    if (isPrimary) res.EnsureSuccessStatusCode();
                    continue;
                }

                var body = await res.Content.ReadAsStringAsync(ct);
                Log.DumpDebugJson(Id + (isPrimary ? "" : "-public"), body);
                using var json = JsonDocument.Parse(body);
                gotAny |= ParseUsages(json.RootElement, snap);
            }
            catch (RateLimitException) { throw; }
            catch (Exception ex) when (!isPrimary)
            {
                Log.Info("kimi: 補助エンドポイント失敗（続行）: " + ex.Message);
            }
            catch (Exception ex)
            {
                primaryError = ex;
            }
        }

        if (!gotAny && snap.Gauges.Count == 0)
        {
            if (primaryError != null) throw primaryError;
            snap.Error = "使用量データを取得できませんでした";
            return snap;
        }

        // 表示順: 5-hour usage → 7-day usage → Total usage
        snap.Gauges.Sort((a, b) => KimiGaugeOrder(a.Label).CompareTo(KimiGaugeOrder(b.Label)));
        return snap;
    }

    static int KimiGaugeOrder(string label) => label switch
    {
        "5-hour usage" => 0,
        "7-day usage" => 1,
        "Total usage" => 2,
        _ => 3,
    };

    /// <summary>/usages レスポンスから 5-hour / 7-day / Total の各枠を抽出して格上げする。</summary>
    static bool ParseUsages(JsonElement root, UsageSnapshot snap)
    {
        var found = false;

        // 7-day usage: usage { limit, used, resetTime }
        if (root.TryGetProperty("usage", out var usage) && GetNum(usage, "limit") > 0)
        {
            UpsertGauge(snap, "7-day usage", GetNum(usage, "used"), GetNum(usage, "limit"), GetReset(usage));
            found = true;
        }

        // 5-hour usage: limits[] のうち window = 300分（TIME_UNIT_MINUTE x 300）
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var lim in limits.EnumerateArray())
            {
                if (!lim.TryGetProperty("window", out var w)) continue;
                var unit = w.TryGetProperty("timeUnit", out var tu) ? tu.GetString() ?? "" : "";
                if (GetNum(w, "duration") != 300 ||
                    !unit.Contains("MINUTE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!lim.TryGetProperty("detail", out var d) || GetNum(d, "limit") <= 0) continue;
                UpsertGauge(snap, "5-hour usage", GetNum(d, "used"), GetNum(d, "limit"), GetReset(d));
                found = true;
            }
        }

        // Total usage: totalQuota { limit, used, resetTime }
        if (root.TryGetProperty("totalQuota", out var q) && GetNum(q, "limit") > 0)
        {
            UpsertGauge(snap, "Total usage", GetNum(q, "used"), GetNum(q, "limit"), GetReset(q));
            found = true;
        }
        return found;
    }

    static void UpsertGauge(UsageSnapshot snap, string label, double used, double limit, DateTimeOffset? reset)
    {
        var pct = limit > 0 ? used / limit * 100.0 : 0;
        var g = snap.Gauges.FirstOrDefault(x => x.Label == label);
        if (g == null)
            snap.Gauges.Add(new GaugeInfo { Label = label, Percent = pct, ResetAtUtc = reset });
        else
        {
            g.Percent = pct;
            g.ResetAtUtc = reset;
        }
    }

    static DateTimeOffset? GetReset(JsonElement el)
    {
        if (el.TryGetProperty("resetTime", out var rt) &&
            rt.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(rt.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto))
            return dto;
        return null;
    }

    static double GetNum(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0;
    }

    /// <summary>
    /// config.toml の [providers.*] ブロックから type="kimi" の api_key / base_url を拾う。
    /// 簡易パーサ（依存追加を避ける）。見つからなければ api_key / base_url を持つ最初のブロック。
    /// </summary>
    internal static (string? apiKey, string? baseUrl) ParseKimiConfig(string[] lines)
    {
        string? fallbackKey = null, fallbackUrl = null;
        string? curKey = null, curUrl = null, curType = null;
        var inProvider = false;

        (string?, string?) Flush()
        {
            if (inProvider && curKey != null && curUrl != null)
            {
                if (string.Equals(curType, "kimi", StringComparison.OrdinalIgnoreCase))
                    return (curKey, curUrl);
                fallbackKey ??= curKey;
                fallbackUrl ??= curUrl;
            }
            return (null, null);
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('['))
            {
                var hit = Flush();
                if (hit.Item1 != null) return hit;
                inProvider = line.StartsWith("[providers.", StringComparison.Ordinal);
                curKey = curUrl = curType = null;
                continue;
            }
            if (!inProvider) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            switch (name)
            {
                case "type": curType = value; break;
                case "api_key": curKey = value; break;
                case "base_url": curUrl = value; break;
            }
        }
        var last = Flush();
        return last.Item1 != null ? last : (fallbackKey, fallbackUrl);
    }
}
