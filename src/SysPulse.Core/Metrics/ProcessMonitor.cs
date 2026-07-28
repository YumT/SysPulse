using System.Diagnostics;
using System.Runtime.InteropServices;
using SysPulse.Core.Models;

namespace SysPulse.Core.Metrics;

/// <summary>
/// プロセス別 CPU / メモリ / ディスク I/O。
/// CPU は TotalProcessorTime 差分 ÷ 経過時間 ÷ 論理コア数(タスクマネージャーと同じ 0-100%)。
/// ディスク I/O は GetProcessIoCounters(psutil の io_counters と同じソース)。
/// System Idle Process (PID 0) は除外。
/// </summary>
public sealed class ProcessMonitor
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private sealed class Prev
    {
        public DateTime Time;
        public TimeSpan Cpu;
        public ulong IoBytes;
    }

    private readonly int _coreCount = Environment.ProcessorCount;
    private readonly Dictionary<int, Prev> _prev = new();
    private readonly double _totalPhys = MemoryMonitor.TotalGb;

    public List<ProcSample> Sample(int limit = 8)
    {
        var now = DateTime.UtcNow;
        var rows = new List<ProcSample>();
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            int pid = p.Id;
            string name;
            TimeSpan cpuTime;
            long workingSet;
            try
            {
                name = p.ProcessName;
                cpuTime = p.TotalProcessorTime;
                workingSet = p.WorkingSet64;
            }
            catch
            {
                continue; // 終了済み・アクセス不可
            }
            finally
            {
                p.Dispose();
            }
            if (pid == 0)
                continue; // System Idle Process

            ulong io = 0;
            bool ioOk = false;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                if (GetProcessIoCounters(h, out var c))
                {
                    io = c.ReadTransferCount + c.WriteTransferCount;
                    ioOk = true;
                }
                CloseHandle(h);
            }

            double cpuPct = 0;
            double? diskMbps = null;
            if (_prev.TryGetValue(pid, out var prev))
            {
                double elapsed = (now - prev.Time).TotalSeconds;
                if (elapsed > 0)
                {
                    cpuPct = (cpuTime - prev.Cpu).TotalSeconds / elapsed / _coreCount * 100.0;
                    if (ioOk && io >= prev.IoBytes)
                        diskMbps = (io - prev.IoBytes) / elapsed / (1024.0 * 1024.0);
                }
            }
            _prev[pid] = new Prev { Time = now, Cpu = cpuTime, IoBytes = ioOk ? io : (_prev.TryGetValue(pid, out var old) ? old.IoBytes : 0) };
            seen.Add(pid);

            rows.Add(new ProcSample
            {
                Name = name,
                Pid = pid,
                Cpu = Math.Max(0, cpuPct),
                Mem = _totalPhys > 0 ? workingSet / 1e9 / _totalPhys * 100.0 : null,
                Disk = diskMbps,
            });
        }

        // 終了したプロセスのキャッシュを掃除
        foreach (int pid in _prev.Keys.ToList())
            if (!seen.Contains(pid))
                _prev.Remove(pid);

        return rows.OrderByDescending(r => r.Cpu).Take(limit).ToList();
    }
}
