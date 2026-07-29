using System.Runtime.InteropServices;
using Microsoft.Win32;
using SysPulsar.Core.Models;
using SysPulsar.Core.Pdh;

namespace SysPulsar.Core.Metrics;

/// <summary>
/// CPU 負荷は GetSystemTimes の差分(カーネル時間にはアイドルが含まれる点に注意)。
/// クロックは PDH の "% Processor Performance" x 定格 MHz(レジストリ)で算出。
/// </summary>
public sealed class CpuMonitor : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    private readonly PdhQuery _pdh = new();
    private readonly IntPtr? _perfCounter;
    private readonly int _baseMhz;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrev;

    public CpuMonitor()
    {
        _baseMhz = ReadBaseMhz();
        if (_baseMhz > 0)
            _perfCounter = _pdh.AddCounter(@"\Processor Information(_Total)\% Processor Performance");
    }

    public static string ReadName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int ReadBaseMhz()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }
        catch
        {
            return 0;
        }
    }

    public CpuSample Sample()
    {
        double? load = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong i = idle.ToUInt64(), k = kernel.ToUInt64(), u = user.ToUInt64();
            if (_hasPrev)
            {
                ulong di = i - _prevIdle;
                ulong dk = k - _prevKernel;
                ulong du = u - _prevUser;
                ulong total = dk + du;
                if (total > 0)
                    load = (1.0 - (double)di / total) * 100.0;
            }
            _prevIdle = i; _prevKernel = k; _prevUser = u;
            _hasPrev = true;
        }

        _pdh.Collect();
        double? ghz = null;
        double? perf = _pdh.GetValue(_perfCounter);
        if (perf is > 0)
            ghz = _baseMhz * perf.Value / 100.0 / 1000.0;

        return new CpuSample { Load = load, Ghz = ghz };
    }

    public void Dispose() => _pdh.Dispose();
}
