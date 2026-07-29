using SysPulsar.Core.Models;
using SysPulsar.Core.Pdh;

namespace SysPulsar.Core.Metrics;

/// <summary>
/// 物理ディスクの busy% と転送速度。タスクマネージャーと同じく
/// "\PhysicalDisk(*)\% Disk Time" をソースにする(psutil の時間カウンタは
/// Windows で信頼できないため)。インスタンス名は "0 C:" や "16" の形式。
/// </summary>
public sealed class DiskMonitor : IDisposable
{
    private readonly PdhQuery _pdh = new();
    private readonly IntPtr? _busyCounter;
    private readonly IntPtr? _readCounter;
    private readonly IntPtr? _writeCounter;

    public DiskMonitor()
    {
        _busyCounter = _pdh.AddCounter(@"\PhysicalDisk(*)\% Disk Time");
        _readCounter = _pdh.AddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _writeCounter = _pdh.AddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
    }

    public Dictionary<int, DiskSample> Sample()
    {
        _pdh.Collect();
        var busy = _pdh.GetWildcardValues(_busyCounter);
        var read = _pdh.GetWildcardValues(_readCounter);
        var write = _pdh.GetWildcardValues(_writeCounter);

        var result = new Dictionary<int, DiskSample>();
        foreach (var (instance, _) in busy)
        {
            if (!TryParseDiskNumber(instance, out int num))
                continue;
            double? mbps = null;
            bool hasRead = read.TryGetValue(instance, out double r);
            bool hasWrite = write.TryGetValue(instance, out double w);
            if (hasRead || hasWrite)
                mbps = ((hasRead ? r : 0) + (hasWrite ? w : 0)) / (1024.0 * 1024.0);
            // % Disk Time は RAID 等で 100 を超えることがあるのでタスクマネージャー同様に丸める
            result[num] = new DiskSample { Busy = Math.Min(busy[instance], 100.0), Mbps = mbps };
        }
        return result;
    }

    /// <summary>"0 C:" / "0 C: D:" / "16" → 0 / 0 / 16。"_Total" 等は除外。</summary>
    private static bool TryParseDiskNumber(string instance, out int number)
    {
        number = 0;
        int i = 0;
        while (i < instance.Length && char.IsDigit(instance[i]))
            i++;
        return i > 0 && int.TryParse(instance.AsSpan(0, i), out number);
    }

    public void Dispose() => _pdh.Dispose();
}
