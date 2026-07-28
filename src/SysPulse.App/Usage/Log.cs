using System.IO;
namespace SysPulse.App.Usage;

/// <summary>
/// %LOCALAPPDATA%\UsageWatcher\log.txt への簡易ログ。
/// 認証トークンを絶対に出力しないこと。
/// </summary>
static class Log
{
    public static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWatcher");

    public static readonly string LogPath = Path.Combine(AppDir, "log.txt");

    public static readonly string DebugDir = Path.Combine(AppDir, "debug");

    static readonly object Gate = new();

    static Log() => Directory.CreateDirectory(AppDir);

    public static void Info(string msg) => Write("INFO", msg);

    public static void Error(string msg) => Write("ERROR", msg);

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* ログ失敗で本体を落とさない */ }
    }

    /// <summary>デバッグ用に生JSONを保存する（トークンは含まれないレスポンスのみ渡すこと）。</summary>
    public static void DumpDebugJson(string providerId, string rawJson)
    {
        if (!AppOptions.Debug) return;
        try
        {
            Directory.CreateDirectory(DebugDir);
            var path = Path.Combine(DebugDir, $"{providerId}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, rawJson);
            Info($"{providerId}: raw JSON saved to {path}");
        }
        catch { }
    }
}
