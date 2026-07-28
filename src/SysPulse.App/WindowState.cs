using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SysPulse.App;

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

    private static string StatePath =>
        Path.Combine(AppContext.BaseDirectory, "window-state.json");

    public static WindowState? Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            var state = JsonSerializer.Deserialize<WindowState>(File.ReadAllText(StatePath));
            if (state is null)
                return null;
            // どのモニタにも十分に重なっていなければ無効とみなす
            bool visible =
                state.Left + state.Width > SystemParameters.VirtualScreenLeft + 40 &&
                state.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
                state.Top + state.Height > SystemParameters.VirtualScreenTop + 40 &&
                state.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
            return visible ? state : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(double left, double top, double width, double height)
    {
        try
        {
            var state = new WindowState { Left = left, Top = top, Width = width, Height = height };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 保存できなくても動作に影響させない
        }
    }
}
