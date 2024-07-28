using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Net.Leksi.Util;
public class LifetimeVisualizer
{
    public static LifetimeVisualizer Instance = new();
    private Dictionary<Type, CountHolder> _counts = [];
    private bool _running = false;
    private int _maxNameLen = 0;
    private LifetimeObserver? _lifetimeObserver = null;
    private Socket _socket = null!;
    private string _socketPath = null!;
    private LifetimeVisualizer() { }
    public void Start(LifetimeObserver lifetimeObserver)
    {
        if (lifetimeObserver != null && lifetimeObserver.IsEnabled)
        {
            _lifetimeObserver = lifetimeObserver;
            if (!_running)
            {
                _running = true;
                lock (_counts)
                {
                    foreach (Type type in _lifetimeObserver!.GetTracedTypes())
                    {
                        _counts.Add(type, new CountHolder());
                        if (_maxNameLen < type.FullName!.Length)
                        {
                            _maxNameLen = type.FullName.Length;
                        }
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
        if (_lifetimeObserver != null)
        {
            _lifetimeObserver!.LifetimeEventOccured -= LifetimeObserver_LifetimeEventOccured;
        }
        _running = false;
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
    private void LifetimeObserver_LifetimeEventOccured(object? sender, LifetimeEventArgs e)
    {
        lock (e.Type)
        {
            CountHolder ch = _counts[e.Type];
            switch (e.Kind)
            {
                case LifetimeEventKind.Created:
                    ++ch._incCount;
                    if(ch._incCount - ch._decCount > ch._maxCount)
                    {
                        ch._maxCount = ch._incCount - ch._decCount;
                    }
                    break;
                case LifetimeEventKind.Finalized:
                    ++ch._decCount;
                    break;
            };
        }
        Write();
    }
    private void S_lifetimeObserver_NextTracedCount(object? sender, EventArgs e)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        GC.WaitForPendingFinalizers();
    }
    private void Write()
    {
        string data = string.Join('\n', _counts.Select(item =>
        {
            string line = string.Format(
                $"{{0,-{_maxNameLen}}}\t+{{1}}\t-{{2}}\t={{3}}\t<{{4}}", 
                item.Key, item.Value._incCount, item.Value._decCount, 
                item.Value._incCount - item.Value._decCount, item.Value._maxCount
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
internal class CountHolder
{
    internal int _incCount = 0;
    internal int _decCount = 0;
    internal int _maxCount = 0;
}