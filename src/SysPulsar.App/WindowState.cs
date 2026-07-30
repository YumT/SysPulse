using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SysPulsar.App;

/// <summary>
/// ウィンドウの位置・サイズの永続化。実行ファイルと同じフォルダの
/// window-state.json に終了時保存し、起動時に復元する。
/// 画面外に保存されていた場合(マルチモニタ構成変更など)は中央に戻す。
/// </summary>
public sealed class WindowState
{
    [JsonPropertyName("left")] public double Left { get; set; }
    [JsonPropertyName("top")] public double Top { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; } = 640;
    [JsonPropertyName("height")] public double Height { get; set; } = 550;
    [JsonPropertyName("topmost")] public bool Topmost { get; set; }
    /// <summary>表示エリア(11=1x1 / 12=1x2 / 21=2x1 / 22=2x2)。スロット数は 1/2/2/4。</summary>
    [JsonPropertyName("layout")] public int Layout { get; set; } = 22;
    /// <summary>表示要素: メトリクス(CPU/メモリ/GPU/ネット)を表示するか。</summary>
    [JsonPropertyName("showMetrics")] public bool ShowMetrics { get; set; } = true;
    /// <summary>表示要素: プロセス＆イベントを表示するか。</summary>
    [JsonPropertyName("showProcesses")] public bool ShowProcesses { get; set; } = true;
    /// <summary>表示要素: ディスクを表示するか。</summary>
    [JsonPropertyName("showDisks")] public bool ShowDisks { get; set; } = true;
    /// <summary>表示要素: AI Usage(Claude/Kimi)を表示するか。</summary>
    [JsonPropertyName("showUsage")] public bool ShowUsage { get; set; } = true;

    private static string StatePath =>
        Path.Combine(AppContext.BaseDirectory, "window-state.json");

    /// <summary>画面外チェックを挟まずにデシリアライズだけ行う。
    /// 位置が画面外で Load が null を返す場合でも Topmost / Layout / 表示要素は
    /// 復元したいので、それらはこちらから読む。</summary>
    public static WindowState? LoadRaw()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            return JsonSerializer.Deserialize<WindowState>(File.ReadAllText(StatePath));
        }
        catch
        {
            return null;
        }
    }

    public static WindowState? Load()
    {
        if (LoadRaw() is not { } state)
            return null;
        // どのモニタにも十分に重なっていなければ無効とみなす
        bool visible =
            state.Left + state.Width > SystemParameters.VirtualScreenLeft + 40 &&
            state.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            state.Top + state.Height > SystemParameters.VirtualScreenTop + 40 &&
            state.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        return visible ? state : null;
    }

    public static void Save(WindowState state)
    {
        try
        {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 保存できなくても動作に影響させない
        }
    }
}
