using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysPulse.App;

/// <summary>
/// config.json(実行ファイルと同じフォルダ)。PC 固有の設定はここに出す
/// (Python 版の DISK_ORDER / DISK_LABELS のハードコード解消)。
/// disks が空なら Online ディスクを番号順に最大 8 台自動検出。
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("intervalMs")] public int IntervalMs { get; set; } = 1000;
    [JsonPropertyName("namesRefreshSec")] public int NamesRefreshSec { get; set; } = 30;
    [JsonPropertyName("disks")] public List<DiskEntry> Disks { get; set; } = new();

    public sealed class DiskEntry
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
    }

    public static AppConfig Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                    return config;
            }
        }
        catch
        {
            // 設定が壊れていても既定値で起動する
        }
        return new AppConfig();
    }
}
