using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Net.Leksi.Util;
public class LifetimeVisualizer
{
    public static LifetimeVisualizer Instance = new();
    private bool _running = false;
    private int _maxNameLen = 0;
    private LifetimeObserver? _lifetimeObserver = null;
    private Socket _socket = null!;
    private string _socketPath = null!;
    private LifetimeGauge? _gauge = null;
    private LifetimeVisualizer() { }
    public void Start(LifetimeObserver lifetimeObserver)
    {
        if (lifetimeObserver != null && lifetimeObserver.IsEnabled)
        {
            _lifetimeObserver = lifetimeObserver;
            if (!_running)
            {
                _running = true;
                _gauge = new(lifetimeObserver);
                foreach (Type type in _gauge!.GetTracedTypes())
                {
                    if (_maxNameLen < type.FullName!.Length)
                    {
                        _maxNameLen = type.FullName.Length;
                    }
                }
                _lifetimeObserver.NextTracedCount += S_lifetimeObserver_NextTracedCount;
#if !NO_SOCKET
                _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                _socketPath = Path.GetTempFileName();
                File.Delete(_socketPath);
                Process echo = new();
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
#if DEBUG
                echo.StartInfo.FileName = "Net.Leksi.EchoConsole.exe";
#else
                echo.StartInfo.FileName = Path.Combine("EchoConsole", "Net.Leksi.EchoConsole.exe");
#endif
                echo.StartInfo.Arguments = $"--title \"LifetimeVisualizer: {Assembly.GetEntryAssembly()!.GetName()}\" --socket-path {_socketPath}";
                echo.StartInfo.UseShellExecute = true;
                echo.Exited += Echo_Exited;
                echo.Start();
                while(!File.Exists(_socketPath))
                {
                    Thread.Sleep(10);
                }
                _socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
#endif
                Write();
                _lifetimeObserver.LifetimeEventOccured += LifetimeObserver_LifetimeEventOccured;
            }
        }
    }
    public void Stop()
    {
        if(_gauge != null)
        {
            _gauge.Stop();
        }
        _lifetimeObserver!.LifetimeEventOccured -= LifetimeObserver_LifetimeEventOccured;
        _running = false;
    }
    private void LifetimeObserver_LifetimeEventOccured(object? sender, LifetimeEventArgs args)
    {
        Write();
    }
    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
#if !NO_SOCKET
        _socket.Close();
#endif
    }
    private void Echo_Exited(object? sender, EventArgs e)
    {
        Stop();
    }
    private void S_lifetimeObserver_NextTracedCount(object? sender, EventArgs e)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        GC.WaitForPendingFinalizers();
    }
    private void Write()
    {
        string data = string.Join('\n', _gauge!.GetTracedTypes().Select(item =>
        {
            (int created, int released, int waterMark) = _gauge.GetCounts(item);
            string line = string.Format(
                $"{{0,-{_maxNameLen}}}\t+{{1}}\t-{{2}}\t={{3}}\t<{{4}}\t%{{5:f2}}", 
                item, created, released,
                created - released, waterMark, created > 0 ? 100.0 * (created - released) / created : "-"
            );
            return $"{line}";
        })) + "\n";
#if !NO_SOCKET
        string message = $"{data}\0";
        try
        {
            _socket.Send(Encoding.UTF8.GetBytes(message));
        }
        catch (Exception)
        {
            Stop();
        }
#else
        Console.Write(data);
#endif
    }
}
