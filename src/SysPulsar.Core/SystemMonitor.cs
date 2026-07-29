using SysPulsar.Core.Devices;
using SysPulsar.Core.Metrics;
using SysPulsar.Core.Models;

namespace SysPulsar.Core;

/// <summary>計測の窓口。UI 層(WPF)からもこのクラスだけを触る。</summary>
public sealed class SystemMonitor : IDisposable
{
    private readonly CpuMonitor _cpu = new();
    private readonly MemoryMonitor _mem = new();
    private readonly DiskMonitor _disks = new();
    private readonly NetworkMonitor _net = new();
    private readonly ProcessMonitor _procs = new();
    private readonly GpuMonitor _gpu = new();

    /// <summary>1 回計測。初回はレート系(CPU/ネット/ディスク)が null になるので捨てる。</summary>
    public Snapshot Sample(int processLimit = 8)
    {
        return new Snapshot
        {
            Cpu = _cpu.Sample(),
            Mem = _mem.Sample(),
            Gpu = _gpu.Sample(),
            Nic = _net.NicName,
            Net = _net.Sample(),
            Disks = _disks.Sample(),
            Processes = _procs.Sample(processLimit),
        };
    }

    /// <summary>遅いデバイス名群。必ずバックグラウンドスレッドから呼ぶこと。</summary>
    public DeviceInfo GetDeviceInfo()
    {
        var diskLetters = DeviceInfoProvider.GetDiskLetters();
        return new DeviceInfo
        {
            Cpu = CpuMonitor.ReadName(),
            Mem = DeviceInfoProvider.GetMemoryInfo(),
            Gpu = _gpu.Name.Length > 0 ? _gpu.Name : DeviceInfoProvider.GetGpuName(),
            Net = _net.NicName ?? "",
            Disks = DeviceInfoProvider.GetDiskModels(),
            DiskLetters = diskLetters,
            DiskSpaces = DeviceInfoProvider.GetDiskSpaces(diskLetters),
        };
    }

    public void Dispose()
    {
        _cpu.Dispose();
        _disks.Dispose();
        _gpu.Dispose();
        _procs.Dispose();
    }
}
