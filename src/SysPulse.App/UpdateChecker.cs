using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SysPulse.App;

/// <summary>
/// GitHub Releases ベースの自動更新。
/// 起動時に最新リリースを確認し、新しければバックグラウンドでダウンロード・
/// 展開まで済ませる。適用(差し替え+再起動)はユーザーがメニューから指示したときだけ。
/// 通信・DL・展開の失敗はすべて null 返しで黙殺する(モニター本体に影響させない)。
/// </summary>
public static class UpdateChecker
{
    private const string ApiUrl = "https://api.github.com/repos/YumT/SysPulse/releases/latest";

    public sealed record UpdateInfo(Version Version, string StagingDir);

    /// <summary>現在のバージョン(csproj の Version。リリースのタグ vX.Y.Z と対応)。
    /// SDK 8+ は InformationalVersion にコミットハッシュを "+..." で付記するため除去する。</summary>
    public static Version CurrentVersion { get; } =
        Version.TryParse(
            (typeof(UpdateChecker).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
                .Split('+')[0],
            out Version? v)
            ? v
            : new Version(0, 0, 0);

    /// <summary>更新があればダウンロード・展開して返す。なければ null。
    /// 展開物から config.json は除く(適用時にユーザー設定を上書きしないため)。</summary>
    public static async Task<UpdateInfo?> CheckAndDownloadAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SysPulse-UpdateChecker");
            http.Timeout = TimeSpan.FromSeconds(60);

            var json = await http.GetStringAsync(ApiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out Version? latest) || latest <= CurrentVersion)
                return null;

            string? assetUrl = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (assetUrl == null)
                return null;

            // %TEMP%\syspulse-update\stage に展開(前回の残りがあれば捨てる)
            string dir = Path.Combine(Path.GetTempPath(), "syspulse-update");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            string stage = Path.Combine(dir, "stage");
            Directory.CreateDirectory(stage);

            string zipPath = Path.Combine(dir, "update.zip");
            await File.WriteAllBytesAsync(zipPath, await http.GetByteArrayAsync(assetUrl, ct), ct);
            ZipFile.ExtractToDirectory(zipPath, stage);

            // ユーザー設定は絶対に上書きしない(exe と bat だけ適用対象)
            string stagedConfig = Path.Combine(stage, "config.json");
            if (File.Exists(stagedConfig))
                File.Delete(stagedConfig);
            if (!File.Exists(Path.Combine(stage, "SysPulse.exe")))
                return null;
            return new UpdateInfo(latest, stage);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>差し替え bat を起動する。呼んだ側は直後にアプリを完全終了すること
    /// (bat はこのプロセスの終了を待ってから exe/bat を上書きコピーし、起動し直す)。
    /// config.json / window-state.json は stage に含めていないので残る。</summary>
    public static void LaunchUpdater(UpdateInfo info)
    {
        string installDir = AppContext.BaseDirectory;
        int pid = Environment.ProcessId;
        string bat = Path.Combine(Path.GetTempPath(), "syspulse-update", "apply.bat");
        // timeout は非対話では使えない環境があるので ping で待つ
        File.WriteAllText(bat, $"""
            @echo off
            rem SysPulse auto-updater: wait for exit, replace files, restart.
            :wait
            tasklist /fi "PID eq {pid}" | findstr /c:" {pid} " > nul
            if not errorlevel 1 (
              ping 127.0.0.1 -n 2 > nul
              goto wait
            )
            copy /y "{info.StagingDir}\*" "{installDir}" > nul
            start "" "{installDir}SysPulse.exe"
            rd /s /q "{info.StagingDir}" > nul 2>&1
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
