using System.Management;
using System.Text.RegularExpressions;
using SysPulsar.Core.Models;

namespace SysPulsar.Core.Devices;

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

    /// <summary>
    /// GPU 名。NVML が使えない環境(AMD/Intel)向けのフォールバック。
    /// Win32_VideoController から仮想/基本表示アダプタを除いた最初の名前を返す。
    /// </summary>
    public static string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = (mo["Name"] as string)?.Trim() ?? "";
                if (name.Length == 0)
                    continue;
                // 基本表示/リモート表示などの仮想アダプタは飛ばす
                if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
                    continue;
                return name;
            }
        }
        catch
        {
        }
        return "";
    }

    /// <summary>
    /// 物理ディスク番号 → ドライブレター("C:"、複数パーティションは "C:D:")の対応を返す。
    /// レターが 1 つも無いディスクは含めない。
    /// 主経路は MSFT_Partition(Storage プロバイダ)、失敗時は
    /// Win32_LogicalDiskToPartition にフォールバック。
    /// </summary>
    public static Dictionary<int, string> GetDiskLetters()
    {
        try
        {
            var result = GetDiskLettersViaMsftPartition();
            if (result.Count > 0)
                return result;
        }
        catch
        {
            // フォールバックへ
        }
        return GetDiskLettersViaWin32();
    }

    private static Dictionary<int, string> GetDiskLettersViaMsftPartition()
    {
        var letters = new Dictionary<int, SortedSet<char>>();
        var scope = new ManagementScope(@"root\Microsoft\Windows\Storage");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));
        foreach (ManagementObject mo in searcher.Get())
        {
            if (mo["DiskNumber"] is not uint diskNo)
                continue;
            // DriveLetter は char。レター無しのパーティションは '\0'
            char letter = Convert.ToChar(mo["DriveLetter"] ?? '\0');
            if (letter == '\0')
                continue;
            if (!letters.TryGetValue((int)diskNo, out var set))
                letters[(int)diskNo] = set = new SortedSet<char>();
            set.Add(letter);
        }
        return JoinLetters(letters);
    }

    private static Dictionary<int, string> GetDiskLettersViaWin32()
    {
        var letters = new Dictionary<int, SortedSet<char>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            foreach (ManagementObject mo in searcher.Get())
            {
                // Antecedent: Win32_DiskPartition.DeviceID="Disk #0, Partition #1"
                // Dependent:  Win32_LogicalDisk.DeviceID="C:"
                string ant = mo["Antecedent"] as string ?? "";
                string dep = mo["Dependent"] as string ?? "";
                var mDisk = Regex.Match(ant, @"Disk #(\d+)");
                var mLetter = Regex.Match(dep, @"([A-Z]):""$");
                if (!mDisk.Success || !mLetter.Success)
                    continue;
                int diskNo = int.Parse(mDisk.Groups[1].Value);
                if (!letters.TryGetValue(diskNo, out var set))
                    letters[diskNo] = set = new SortedSet<char>();
                set.Add(mLetter.Groups[1].Value[0]);
            }
        }
        catch
        {
        }
        return JoinLetters(letters);
    }

    private static Dictionary<int, string> JoinLetters(Dictionary<int, SortedSet<char>> letters) =>
        letters.ToDictionary(kv => kv.Key, kv => string.Concat(kv.Value.Select(c => c + ":")));

    /// <summary>
    /// 物理ディスク番号 → 容量情報(そのディスクの全ボリュームを合算)を返す。
    /// letters(GetDiskLetters の結果)でレター→ディスクを逆引きして紐付ける。
    /// 主経路は MSFT_Volume、失敗時は Win32_LogicalDisk にフォールバック。
    /// </summary>
    public static Dictionary<int, DiskSpaceInfo> GetDiskSpaces(IReadOnlyDictionary<int, string> letters)
    {
        // レター → ディスク番号の逆引き表("C:F:" なら C と F の両方を登録)
        var letterToDisk = new Dictionary<char, int>();
        foreach (var (diskNo, ls) in letters)
            foreach (char c in ls)
                if (c != ':')
                    letterToDisk[c] = diskNo;

        var spaces = new Dictionary<int, (double free, double total)>();
        var volLabels = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            var scope = new ManagementScope(@"root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DriveLetter, Size, SizeRemaining, FileSystemLabel FROM MSFT_Volume"));
            foreach (ManagementObject mo in searcher.Get())
            {
                char letter = Convert.ToChar(mo["DriveLetter"] ?? '\0');
                if (letter == '\0' || !letterToDisk.TryGetValue(letter, out int diskNo))
                    continue;
                AddSpace(spaces, diskNo, Convert.ToDouble(mo["SizeRemaining"] ?? 0),
                    Convert.ToDouble(mo["Size"] ?? 0));
                AddLabel(volLabels, diskNo, letter, (mo["FileSystemLabel"] as string)?.Trim() ?? "");
            }
        }
        catch
        {
            spaces.Clear();
            volLabels.Clear();
        }
        if (spaces.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Size, FreeSpace, VolumeName FROM Win32_LogicalDisk WHERE DriveType = 3");
                foreach (ManagementObject mo in searcher.Get())
                {
                    string id = (mo["DeviceID"] as string) ?? "";
                    if (id.Length < 2 || !letterToDisk.TryGetValue(id[0], out int diskNo))
                        continue;
                    AddSpace(spaces, diskNo, Convert.ToDouble(mo["FreeSpace"] ?? 0),
                        Convert.ToDouble(mo["Size"] ?? 0));
                    AddLabel(volLabels, diskNo, id[0], (mo["VolumeName"] as string)?.Trim() ?? "");
                }
            }
            catch
            {
            }
        }

        const double Gb = 1024.0 * 1024.0 * 1024.0;
        return spaces.ToDictionary(kv => kv.Key, kv => new DiskSpaceInfo
        {
            FreeGb = kv.Value.free / Gb,
            TotalGb = kv.Value.total / Gb,
            VolumeLabels = volLabels.GetValueOrDefault(kv.Key, new Dictionary<string, string>()),
        });
    }

    private static void AddSpace(Dictionary<int, (double free, double total)> spaces,
        int diskNo, double free, double total)
    {
        spaces.TryGetValue(diskNo, out var cur);
        spaces[diskNo] = (cur.free + free, cur.total + total);
    }

    private static void AddLabel(Dictionary<int, Dictionary<string, string>> volLabels,
        int diskNo, char letter, string label)
    {
        if (!volLabels.TryGetValue(diskNo, out var d))
            volLabels[diskNo] = d = new Dictionary<string, string>();
        d[letter.ToString()] = label;
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
