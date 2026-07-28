using System.Diagnostics;
using System.Windows.Controls;

namespace SysPulse.App;

/// <summary>
/// 右クリックメニューから開く Windows の設定・管理画面。
/// すべて管理者権限不要。URI スキーム(ms-settings:)は UseShellExecute が必須。
/// 開けなかった場合もアプリを落とさないよう例外は握りつぶす。
/// </summary>
public static class ExternalTools
{
    /// <summary>タスクマネージャー(既に起動中なら前面化される)。</summary>
    public static void OpenTaskManager() => Launch("taskmgr.exe");

    /// <summary>イベントビューアーを Windows ログ > システムを選択した状態で開く。</summary>
    public static void OpenEventViewer() => Launch("eventvwr.exe", "/c:System");

    /// <summary>コントロール パネル > システムとセキュリティ > 記憶域。</summary>
    public static void OpenStorageSpaces() => Launch("control.exe", "/name Microsoft.StorageSpaces");

    /// <summary>設定 > システム > サウンド > 音量ミキサー。</summary>
    public static void OpenVolumeMixer() => Launch("ms-settings:apps-volume");

    /// <summary>設定 > アプリ > インストールされているアプリ。</summary>
    public static void OpenInstalledApps() => Launch("ms-settings:appsfeatures");

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
        Add(menu, "イベントビューアー (システムログ)", OpenEventViewer);
        Add(menu, "記憶域 (コントロール パネル)", OpenStorageSpaces);
        menu.Items.Add(new Separator());
        Add(menu, "音量ミキサー", OpenVolumeMixer);
        Add(menu, "インストールされているアプリ", OpenInstalledApps);
        return menu;
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
