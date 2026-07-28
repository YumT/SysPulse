using System.Text.Json.Serialization;

namespace SysPulse.Core.Models;

/// <summary>1 サンプル分の全メトリクス。Python 版 --dump の JSON 構造に合わせる。</summary>
public sealed class Snapshot
{
    [JsonPropertyName("cpu")] public CpuSample Cpu { get; set; } = new();
    [JsonPropertyName("mem")] public MemSample Mem { get; set; } = new();
    [JsonPropertyName("gpu")] public GpuSample? Gpu { get; set; }
    [JsonPropertyName("nic")] public string? Nic { get; set; }
    [JsonPropertyName("net")] public NetSample Net { get; set; } = new();
    [JsonPropertyName("disks")] public Dictionary<int, DiskSample> Disks { get; set; } = new();
    [JsonPropertyName("processes")] public List<ProcSample> Processes { get; set; } = new();
    [JsonPropertyName("devices")] public DeviceInfo? Devices { get; set; }
}

public sealed class CpuSample
{
    [JsonPropertyName("load")] public double? Load { get; set; }
    [JsonPropertyName("ghz")] public double? Ghz { get; set; }
}

public sealed class MemSample
{
    [JsonPropertyName("percent")] public double Percent { get; set; }
    [JsonPropertyName("used_gb")] public double UsedGb { get; set; }
    [JsonPropertyName("total_gb")] public double TotalGb { get; set; }
}

public sealed class GpuSample
{
    [JsonPropertyName("load")] public double? Load { get; set; }
    [JsonPropertyName("temp")] public double? Temp { get; set; }
}

public sealed class NetSample
{
    [JsonPropertyName("down_mbps")] public double? DownMbps { get; set; }
    [JsonPropertyName("up_mbps")] public double? UpMbps { get; set; }
}

public sealed class DiskSample
{
    [JsonPropertyName("busy")] public double? Busy { get; set; }
    [JsonPropertyName("mbps")] public double? Mbps { get; set; }
}

public sealed class ProcSample
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("cpu")] public double Cpu { get; set; }
    [JsonPropertyName("mem")] public double? Mem { get; set; }
    [JsonPropertyName("disk")] public double? Disk { get; set; }
    [JsonPropertyName("gpu")] public double? Gpu { get; set; }
}

/// <summary>バックグラウンドで取得する遅いデバイス名群。</summary>
public sealed class DeviceInfo
{
    [JsonPropertyName("cpu")] public string Cpu { get; set; } = "";
    [JsonPropertyName("mem")] public string Mem { get; set; } = "";
    [JsonPropertyName("gpu")] public string Gpu { get; set; } = "";
    [JsonPropertyName("net")] public string Net { get; set; } = "";
    [JsonPropertyName("disks")] public Dictionary<int, string> Disks { get; set; } = new();
}
