using System.Runtime.InteropServices;

namespace SysPulsar.Core.Pdh;

/// <summary>
/// PDH (Performance Data Helper) の薄いラッパー。
/// PdhAddEnglishCounter を使うため、日本語 Windows でも英語カウンタ名
/// ("\PhysicalDisk(*)\% Disk Time") がそのまま使える
/// (System.Diagnostics.PerformanceCounter はローカライズ名しか受け付けない)。
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private const uint ERROR_SUCCESS = 0;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_FMT_DOUBLE = 0x00000200;

    private IntPtr _query;
    private readonly List<IntPtr> _counters = new();
    private bool _disposed;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwType, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr hCounter, uint dwType, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr SzName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    public PdhQuery()
    {
        uint status = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (status != ERROR_SUCCESS)
            throw new InvalidOperationException($"PdhOpenQuery failed: 0x{status:X8}");
    }

    /// <summary>カウンタを追加。失敗時は null を返す(そのカウンタは諦める)。</summary>
    public IntPtr? AddCounter(string path)
    {
        uint status = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out IntPtr counter);
        if (status != ERROR_SUCCESS)
            return null;
        _counters.Add(counter);
        return counter;
    }

    /// <summary>全カウンタのデータを収集。レート系は 2 回目以降で有効になる。</summary>
    public void Collect() => PdhCollectQueryData(_query);

    /// <summary>単一インスタンスカウンタの値。未取得/無効なら null。</summary>
    public double? GetValue(IntPtr? counter)
    {
        if (counter is null || counter == IntPtr.Zero)
            return null;
        uint status = PdhGetFormattedCounterValue(counter.Value, PDH_FMT_DOUBLE, out _, out var value);
        if (status != ERROR_SUCCESS || value.CStatus != ERROR_SUCCESS)
            return null;
        return value.DoubleValue;
    }

    /// <summary>ワイルドカードカウンタ("(*)")の値を インスタンス名→値 で返す。</summary>
    public Dictionary<string, double> GetWildcardValues(IntPtr? counter)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (counter is null || counter == IntPtr.Zero)
            return result;

        uint bufferSize = 0;
        uint status = PdhGetFormattedCounterArrayW(counter.Value, PDH_FMT_DOUBLE,
            ref bufferSize, out uint itemCount, IntPtr.Zero);
        if (status != PDH_MORE_DATA || bufferSize == 0)
            return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter.Value, PDH_FMT_DOUBLE,
                ref bufferSize, out itemCount, buffer);
            if (status != ERROR_SUCCESS)
                return result;

            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(
                    IntPtr.Add(buffer, i * itemSize));
                if (item.FmtValue.CStatus != ERROR_SUCCESS)
                    continue;
                string? name = Marshal.PtrToStringUni(item.SzName);
                if (!string.IsNullOrEmpty(name))
                    result[name] = item.FmtValue.DoubleValue;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
