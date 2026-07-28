using System.Runtime.InteropServices;
using System.Text;
using SysPulse.Core.Models;

namespace SysPulse.Core.Metrics;

/// <summary>
/// NVIDIA GPU の負荷・温度。NVML(nvml.dll)の P/Invoke。管理者権限不要。
/// NVIDIA 以外 / ドライバ無しの環境では IsAvailable=false になり「—」表示に回す。
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
    }

    public GpuSample? Sample()
    {
        if (!IsAvailable)
            return null;
        double? load = null, temp = null;
        if (NvmlGetUtilization(_device, out var u) == NVML_SUCCESS)
            load = u.Gpu;
        if (NvmlGetTemperature(_device, NVML_TEMPERATURE_GPU, out uint t) == NVML_SUCCESS)
            temp = t;
        return new GpuSample { Load = load, Temp = temp };
    }

    public void Dispose()
    {
        if (IsAvailable)
        {
            try { NvmlShutdownNative(); } catch { }
        }
    }
}
