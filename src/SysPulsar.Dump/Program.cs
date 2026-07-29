using System.Text;
using System.Text.Json;
using SysPulsar.Core;

// SysPulsar.Dump — 計測コアの検証用 CLI(Python 版の `python syspulse.py --dump` 相当)。
// 全メトリクスを 1 回 JSON で出力する。初回サンプルはレートが出ないので捨てる。

Console.OutputEncoding = Encoding.UTF8;

int intervalMs = 1000;
if (args.Length > 0 && int.TryParse(args[0], out int ms) && ms >= 100)
    intervalMs = ms;

using var monitor = new SystemMonitor();

monitor.Sample(); // ウォームアップ(捨てる)
var deviceTask = Task.Run(() => monitor.GetDeviceInfo()); // WMI は遅いので裏で取得

Thread.Sleep(intervalMs);

var snap = monitor.Sample();
deviceTask.Wait();
snap.Devices = deviceTask.Result;

var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
});
Console.WriteLine(json);
