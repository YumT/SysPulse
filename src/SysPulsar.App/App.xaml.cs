namespace SysPulsar.App;

public partial class App : System.Windows.Application
{
    // 多重起動禁止。Mutex が取れなければ既存インスタンスへ復帰信号を送って終了する
    // (トレイ退避中は MainWindowHandle が取れないため、名前付きイベントで通知する)。
    public const string MutexName = @"Local\SysPulsar.SingleInstance";
    public const string ShowEventName = @"Local\SysPulsar.ShowWindow";

    private Mutex? _instanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // 受け側がまだ準備できていなければ諦める
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
