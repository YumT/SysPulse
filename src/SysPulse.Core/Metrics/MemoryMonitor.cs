using System.Runtime.InteropServices;
using SysPulse.Core.Models;

namespace SysPulse.Core.Metrics;

/// <summary>物理メモリ使用率。GlobalMemoryStatusEx 一発で取れる。</summary>
public sealed class MemoryMonitor
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public static double TotalGb
    {
        get
        {
            var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref st) ? st.ullTotalPhys / 1e9 : 0;
        }
    }

    public MemSample Sample()
    {
        var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref st))
            return new MemSample();
        double total = st.ullTotalPhys / 1e9;
        double used = (st.ullTotalPhys - st.ullAvailPhys) / 1e9;
        return new MemSample { Percent = st.dwMemoryLoad, UsedGb = used, TotalGb = total };
    }
}
