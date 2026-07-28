using System.Runtime.InteropServices;
using System.Text;
using SysPulse.Core.Models;
using SysPulse.Core.Pdh;

namespace SysPulse.Core.Metrics;

/// <summary>
/// GPU 全体の負荷・温度。主経路は NVML(nvml.dll)の P/Invoke(NVIDIA。管理者権限不要)。
/// NVIDIA 以外 / ドライバ無しの環境では PDH "\GPU Engine(*)\Utilization Percentage" の
/// 全インスタンス合算(100% クランプ)にフォールバックする(AMD/Intel でも取れる。
/// ただし温度は NVML でしか取れないので null)。
/// どちらも駄目なら Sample() は null → 「—」表示に回す。
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private const int NVML_SUCCESS = 0;
    private const uint NVML_TEMPERATURE_GPU = 0;

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int NvmlShutdownNative();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlGetHandle(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
    private static extern int NvmlGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int NvmlGetUtilization(IntPtr device, out NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlGetTemperature(IntPtr device, uint sensorType, out uint temp);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    private IntPtr _device;
    private readonly string _name = "";
    private PdhQuery? _pdh;
    private readonly IntPtr? _pdhGpuUtil;

    public bool IsAvailable { get; }
    public string Name => _name;

    public GpuMonitor()
    {
        try
        {
            if (NvmlInit() != NVML_SUCCESS)
                return;
            if (NvmlGetHandle(0, out _device) != NVML_SUCCESS)
            {
                NvmlShutdownNative();
                return;
            }
            var sb = new StringBuilder(96);
            if (NvmlGetName(_device, sb, (uint)sb.Capacity) == NVML_SUCCESS)
                _name = sb.ToString();
            IsAvailable = true;
        }
        catch (DllNotFoundException)
        {
            // nvml.dll が無い環境(NVIDIA ドライバ無し)
        }
        catch (EntryPointNotFoundException)
        {
        }

        // NVML が使えなければ PDH GPU Engine にフォールバック(AMD/Intel 用)
        if (!IsAvailable)
        {
            try
            {
                _pdh = new PdhQuery();
                _pdhGpuUtil = _pdh.AddCounter(@"\GPU Engine(*)\Utilization Percentage");
                if (_pdhGpuUtil is null)
                {
                    _pdh.Dispose();
                    _pdh = null;
                }
            }
            catch
            {
                _pdh = null;
            }
        }
    }

    public GpuSample? Sample()
    {
        if (IsAvailable)
        {
            double? load = null, temp = null;
            if (NvmlGetUtilization(_device, out var u) == NVML_SUCCESS)
                load = u.Gpu;
            if (NvmlGetTemperature(_device, NVML_TEMPERATURE_GPU, out uint t) == NVML_SUCCESS)
                temp = t;
            return new GpuSample { Load = load, Temp = temp };
        }

        // PDH フォールバック: 全インスタンス合算(複数エンジンで 100 を
        // 超えることがあるのでタスクマネージャー同様に丸める)。温度は取れない
        if (_pdh != null)
        {
            _pdh.Collect();
            double sum = 0;
            var any = false;
            foreach (var (_, value) in _pdh.GetWildcardValues(_pdhGpuUtil))
            {
                sum += value;
                any = true;
            }
            if (any)
                return new GpuSample { Load = Math.Min(sum, 100.0), Temp = null };
        }
        return null;
    }

    public void Dispose()
    {
        if (IsAvailable)
        {
            try { NvmlShutdownNative(); } catch { }
        }
        _pdh?.Dispose();
    }
}
