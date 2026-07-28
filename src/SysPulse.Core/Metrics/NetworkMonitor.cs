using System.Net.NetworkInformation;
using SysPulse.Core.Models;

namespace SysPulse.Core.Metrics;

/// <summary>
/// ネット速度。稼働中 NIC のバイトカウンタ差分 x8 / 経過時間 (Mbps)。
/// NIC 選択は「イーサネット優先、loopback/bluetooth/wi-fi 以外の先頭」
/// (Python 版と同じルール)。
/// </summary>
public sealed class NetworkMonitor
{
    private NetworkInterface? _nic;
    private long _prevRx, _prevTx;
    private DateTime _prevTime;
    private bool _hasPrev;

    public string? NicName => _nic?.Description;

    public static NetworkInterface? PickNic()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToList();

        static int Score(NetworkInterface n)
        {
            string s = (n.Name + " " + n.Description).ToLowerInvariant();
            if (s.Contains("bluetooth"))
                return 3;
            if (s.Contains("ethernet") || s.Contains("イーサネット"))
                return 0;
            if (s.Contains("wi-fi") || s.Contains("wireless") || s.Contains("wlan"))
                return 2;
            return 1;
        }

        return up.OrderBy(Score).FirstOrDefault();
    }

    public NetSample Sample()
    {
        if (_nic is null || _nic.OperationalStatus != OperationalStatus.Up)
        {
            _nic = PickNic();
            _hasPrev = false;
        }
        if (_nic is null)
            return new NetSample();

        long rx, tx;
        try
        {
            var st = _nic.GetIPStatistics();
            rx = st.BytesReceived;
            tx = st.BytesSent;
        }
        catch (NetworkInformationException)
        {
            _nic = null;
            _hasPrev = false;
            return new NetSample();
        }

        var now = DateTime.UtcNow;
        double? down = null, up = null;
        if (_hasPrev)
        {
            double elapsed = (now - _prevTime).TotalSeconds;
            if (elapsed > 0)
            {
                down = (rx - _prevRx) * 8.0 / elapsed / 1e6;
                up = (tx - _prevTx) * 8.0 / elapsed / 1e6;
            }
        }
        _prevRx = rx; _prevTx = tx; _prevTime = now;
        _hasPrev = true;
        return new NetSample { DownMbps = down, UpMbps = up };
    }
}
