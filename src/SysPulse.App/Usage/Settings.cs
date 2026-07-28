using System.IO;
using System.Text.Json;

namespace SysPulse.App.Usage;

/// <summary>
/// %LOCALAPPDATA%\UsageWatcher\settings.json に保存するユーザー設定。
/// しきい値・取得間隔・不透明度・位置・プロバイダ有効/無効をコード外だしする。
/// </summary>
public sealed class Settings
{
    public static readonly string SettingsPath = Path.Combine(Log.AppDir, "settings.json");

    public int PollIntervalSec { get; set; } = 120;
    public double ThresholdOrange { get; set; } = 50;
    public double ThresholdRed { get; set; } = 80;
    public double Opacity { get; set; } = 0.92;
    public bool Collapsed { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool NotifyOnThreshold { get; set; } = true;
    public bool EnableClaude { get; set; } = true;
    public bool EnableKimi { get; set; } = true;

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(SettingsPath)) return s;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var r = doc.RootElement;
            s.PollIntervalSec = GetInt(r, "pollIntervalSec", s.PollIntervalSec);
            s.ThresholdOrange = GetDouble(r, "thresholdOrange", s.ThresholdOrange);
            s.ThresholdRed = GetDouble(r, "thresholdRed", s.ThresholdRed);
            s.Opacity = Math.Clamp(GetDouble(r, "opacity", s.Opacity), 0.3, 1.0);
            s.Collapsed = GetBool(r, "collapsed", s.Collapsed);
            s.NotifyOnThreshold = GetBool(r, "notifyOnThreshold", s.NotifyOnThreshold);
            s.EnableClaude = GetBool(r, "enableClaude", s.EnableClaude);
            s.EnableKimi = GetBool(r, "enableKimi", s.EnableKimi);
            if (r.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number) s.X = x.GetInt32();
            if (r.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number) s.Y = y.GetInt32();
        }
        catch (Exception ex)
        {
            Log.Error("settings.json の読み込みに失敗（既定値で継続）: " + ex.Message);
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.AppDir);
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pollIntervalSec"] = PollIntervalSec,
                ["thresholdOrange"] = ThresholdOrange,
                ["thresholdRed"] = ThresholdRed,
                ["opacity"] = Opacity,
                ["collapsed"] = Collapsed,
                ["notifyOnThreshold"] = NotifyOnThreshold,
                ["enableClaude"] = EnableClaude,
                ["enableKimi"] = EnableKimi,
                ["x"] = X,
                ["y"] = Y,
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("settings.json の保存に失敗: " + ex.Message);
        }
    }

    static int GetInt(JsonElement r, string name, int fallback) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

    static double GetDouble(JsonElement r, string name, double fallback) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    static bool GetBool(JsonElement r, string name, bool fallback) =>
        r.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
}
