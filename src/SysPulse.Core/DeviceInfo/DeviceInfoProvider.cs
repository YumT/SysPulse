using System.Management;
using SysPulse.Core.Models;

namespace SysPulse.Core.Devices;

/// <summary>
/// 遅いデバイス名取得(WMI 系)。Python 版で起動フリーズの原因になった経緯があるため、
/// 必ずバックグラウンドスレッドから呼び、結果はキャッシュして使うこと。
/// </summary>
public static class DeviceInfoProvider
{
    // SMBIOSMemoryType → DDR 種別(PORTING.md の確定マップ)
    private static readonly Dictionary<int, string> DdrTypes = new()
    {
        [20] = "DDR", [21] = "DDR2", [24] = "DDR3", [26] = "DDR4", [34] = "DDR5",
    };

    /// <summary>"DDR4-3200 16GBx2" 形式のメモリモジュール情報。</summary>
    public static string GetMemoryInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            var groups = new Dictionary<(string type, uint speed, ulong cap), int>();
            foreach (ManagementObject mo in searcher.Get())
            {
                ulong cap = mo["Capacity"] as ulong? ?? 0;
                uint speed = mo["Speed"] as uint? ?? 0;
                int typeCode = mo["SMBIOSMemoryType"] as uint? is uint t ? (int)t : 0;
                string type = DdrTypes.TryGetValue(typeCode, out string? d) ? d : "";
                var key = (type, speed, cap);
                groups[key] = groups.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            if (groups.Count == 0)
                return "";
            return string.Join(" + ", groups.Select(g =>
            {
                double gb = g.Key.cap / (1024.0 * 1024.0 * 1024.0);
                string prefix = g.Key.type.Length > 0 ? $"{g.Key.type}-{g.Key.speed} " : "";
                return $"{prefix}{gb:0}GBx{g.Value}";
            }));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Online の物理ディスクのみ 番号→モデル名 で返す。
    /// 切断済みが列挙に残る問題への対策(PORTING.md 参照)。
    /// 主経路は MSFT_Disk(Storage プロバイダ)、失敗時は Win32_DiskDrive にフォールバック。
    /// </summary>
    public static Dictionary<int, string> GetDiskModels()
    {
        try
        {
            var result = GetDiskModelsViaMsftDisk();
            if (result.Count > 0)
                return result;
        }
        catch
        {
            // フォールバックへ
        }
        return GetDiskModelsViaWin32DiskDrive();
    }

    private static Dictionary<int, string> GetDiskModelsViaMsftDisk()
    {
        var result = new Dictionary<int, string>();
        var scope = new ManagementScope(@"root\Microsoft\Windows\Storage");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT Number, FriendlyName, OperationalStatus FROM MSFT_Disk"));
        foreach (ManagementObject mo in searcher.Get())
        {
            // OperationalStatus: 0=Unknown, 1=Online, 2=Not Ready, 3=No Media, 4=Offline, 5=Failed
            ushort status = mo["OperationalStatus"] as ushort? ?? 0;
            if (status != 1)
                continue;
            if (mo["Number"] is not uint number)
                continue;
            string name = (mo["FriendlyName"] as string)?.Trim() ?? "";
            result[(int)number] = name;
        }
        return result;
    }

    private static Dictionary<int, string> GetDiskModelsViaWin32DiskDrive()
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["Index"] is not uint index)
                    continue;
                string name = (mo["Model"] as string)?.Trim() ?? "";
                result[(int)index] = name;
            }
        }
        catch
        {
        }
        return result;
    }
}
