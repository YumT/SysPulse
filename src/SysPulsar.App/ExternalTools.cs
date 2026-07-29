using System.Diagnostics;
using System.Windows.Controls;

namespace SysPulsar.App;

/// <summary>
/// 右クリックメニューから開く Windows の設定・管理画面。
/// すべて管理者権限不要。URI スキーム(ms-settings:)は UseShellExecute が必須。
/// 開けなかった場合もアプリを落とさないよう例外は握りつぶす。
/// </summary>
public static class ExternalTools
{
    /// <summary>タスクマネージャー(既に起動中なら前面化される)。</summary>
    public static void OpenTaskManager() => Launch("taskmgr.exe");

    /// <summary>リソース モニター(プロセス別ディスク/ネット/メモリの詳細)。</summary>
    public static void OpenResourceMonitor() => Launch("resmon.exe");

    /// <summary>パフォーマンス モニター(PDH カウンタの生値)。</summary>
    public static void OpenPerformanceMonitor() => Launch("perfmon.exe");

    /// <summary>イベントビューアーを Windows ログ > システムを選択した状態で開く。</summary>
    public static void OpenEventViewer() => Launch("eventvwr.exe", "/c:System");

    /// <summary>ディスクの管理(パーティション構成)。</summary>
    public static void OpenDiskManagement() => Launch("diskmgmt.msc");

    /// <summary>コントロール パネル > システムとセキュリティ > 記憶域。</summary>
    public static void OpenStorageSpaces() => Launch("control.exe", "/name Microsoft.StorageSpaces");

    /// <summary>ネットワーク接続(アダプター一覧)。</summary>
    public static void OpenNetworkConnections() => Launch("ncpa.cpl");

    /// <summary>電源オプション。</summary>
    public static void OpenPowerOptions() => Launch("powercfg.cpl");

    /// <summary>設定 > システム > サウンド > 音量ミキサー。</summary>
    public static void OpenVolumeMixer() => Launch("ms-settings:apps-volume");

    /// <summary>設定 > アプリ > インストールされているアプリ。</summary>
    public static void OpenInstalledApps() => Launch("ms-settings:appsfeatures");

    /// <summary>Kimi Code Console(API キー発行・使用量確認)。ブラウザで開く。</summary>
    public static void OpenKimiConsole() => Launch("https://www.kimi.com/code/console");

    /// <summary>PID のプロセスの exe をエクスプローラーで選択表示する。
    /// パス取得不可(システムプロセス等)のときは何もしない。</summary>
    public static void OpenProcessFileLocation(int pid)
    {
        try
        {
            string? path = System.Diagnostics.Process.GetProcessById(pid).MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                return;
            Launch("explorer.exe", $"/select,\"{path}\"");
        }
        catch
        {
            // 終了済み・アクセス不可は黙って無視
        }
    }

    private static void Launch(string fileName, string arguments = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
        }
        catch
        {
            // 起動失敗(環境差異など)でモニター本体を落とさない
        }
    }

    /// <summary>ウィンドウの右クリックメニューを構築する。</summary>
    public static ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        Add(menu, "タスクマネージャー", OpenTaskManager);
        Add(menu, "リソース モニター", OpenResourceMonitor);
        Add(menu, "パフォーマンス モニター", OpenPerformanceMonitor);
        menu.Items.Add(new Separator());
        Add(menu, "イベントビューアー (システムログ)", OpenEventViewer);
        menu.Items.Add(new Separator());
        Add(menu, "ディスクの管理", OpenDiskManagement);
        Add(menu, "記憶域 (コントロール パネル)", OpenStorageSpaces);
        Add(menu, "ネットワーク接続", OpenNetworkConnections);
        Add(menu, "電源オプション", OpenPowerOptions);
        menu.Items.Add(new Separator());
        Add(menu, "音量ミキサー", OpenVolumeMixer);
        Add(menu, "インストールされているアプリ", OpenInstalledApps);
        Add(menu, "Kimi Console", OpenKimiConsole);
        return menu;
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
