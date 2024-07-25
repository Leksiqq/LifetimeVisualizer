using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Net.Leksi.Util;
public static class LifetimeVisualizer
{
    private static Dictionary<Type, CountHolder> s_counts = [];
    private static bool s_running = false;
    private static int s_maxNameLen = 0;
    private static LifetimeObserver? s_lifetimeObserver = null;
    private static Socket s_socket = null!;
    private static string s_socketPath = null!;
    public static void Start(LifetimeObserver lifetimeObserver)
    {
        if (lifetimeObserver != null)
        {
            s_lifetimeObserver = lifetimeObserver;
            if (!s_running)
            {
                s_running = true;
                lock (s_counts)
                {
                    foreach (Type type in s_lifetimeObserver!.GetTracedTypes())
                    {
                        s_counts.Add(type, new CountHolder());
                        if (s_maxNameLen < type.FullName!.Length)
                        {
                            s_maxNameLen = type.FullName.Length;
                        }
                    }
                }
                s_lifetimeObserver.NextTracedCount += S_lifetimeObserver_NextTracedCount;
#if !NO_SOCKET
                s_socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                s_socketPath = Path.GetTempFileName();
                File.Delete(s_socketPath);
                Process echo = new();
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
#if DEBUG
                echo.StartInfo.FileName = "Net.Leksi.EchoConsole.exe";
#else
                echo.StartInfo.FileName = Path.Combine("EchoConsole", "Net.Leksi.EchoConsole.exe");
#endif
                echo.StartInfo.Arguments = $"--title \"LifetimeVisualizer: {Assembly.GetEntryAssembly()!.GetName()}\" --socket-path {s_socketPath}";
                echo.StartInfo.UseShellExecute = true;
                echo.Exited += Echo_Exited;
                echo.Start();
                while(!File.Exists(s_socketPath))
                {
                    Thread.Sleep(10);
                }
                s_socket.Connect(new UnixDomainSocketEndPoint(s_socketPath));
#endif
                Write();
                s_lifetimeObserver.LifetimeEventOccured += LifetimeObserver_LifetimeEventOccured; 
            }
        }
    }
    public static void Stop()
    {
        if (s_lifetimeObserver != null)
        {
            s_lifetimeObserver!.LifetimeEventOccured -= LifetimeObserver_LifetimeEventOccured;
        }
        s_running = false;
    }
    private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
#if !NO_SOCKET
        s_socket.Close();
#endif
    }
    private static void Echo_Exited(object? sender, EventArgs e)
    {
        Stop();
    }
    private static void LifetimeObserver_LifetimeEventOccured(object? sender, LifetimeEventArgs e)
    {
        lock (e.Type)
        {
            CountHolder ch = s_counts[e.Type];
            switch (e.Kind)
            {
                case LifetimeEventKind.Created:
                    Interlocked.Increment(ref ch._incCount);
                    break;
                case LifetimeEventKind.Finalized:
                    Interlocked.Increment(ref ch._decCount);
                    break;
            };
        }
        Write();
    }
    private static void S_lifetimeObserver_NextTracedCount(object? sender, EventArgs e)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        GC.WaitForPendingFinalizers();
    }
    private static void Write()
    {
        string data = string.Join('\n', s_counts.Select(item =>
        {
            string line = string.Format($"{{0,-{s_maxNameLen}}}\t+{{1}}\t-{{2}}\t{{3}}", item.Key, item.Value._incCount, item.Value._decCount, item.Value._incCount - item.Value._decCount);
            return $"{line}";
        })) + "\n";
#if !NO_SOCKET
        string message = $"{data}\0";
        try
        {
            s_socket.Send(Encoding.UTF8.GetBytes(message));
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
}