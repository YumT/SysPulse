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
    /// <summary>表示エリア(11=1x1 左上のみ / 12=1x2 上半分 / 21=2x1 左半分 / 22=2x2 すべて)。</summary>
    [JsonPropertyName("layout")] public int Layout { get; set; } = 22;

    private static string StatePath =>
        Path.Combine(AppContext.BaseDirectory, "window-state.json");

    /// <summary>画面外チェックを挟まずにデシリアライズだけ行う。
    /// 位置が画面外で Load が null を返す場合でも Topmost / Layout は
    /// 復元したいので、それらはこちらから読む。</summary>
    private static WindowState? LoadRaw()
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

    /// <summary>「常に手前に表示」の保存値だけを読む。</summary>
    public static bool LoadTopmost() => LoadRaw()?.Topmost ?? false;

    /// <summary>表示エリアの保存値だけを読む(無効値は 22=2x2 にフォールバック)。</summary>
    public static int LoadLayout() => LoadRaw() is { } s && s.Layout is 11 or 12 or 21 or 22 ? s.Layout : 22;

    public static void Save(double left, double top, double width, double height, bool topmost, int layout)
    {
        try
        {
            var state = new WindowState { Left = left, Top = top, Width = width, Height = height, Topmost = topmost, Layout = layout };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 保存できなくても動作に影響させない
        }
    }
}
