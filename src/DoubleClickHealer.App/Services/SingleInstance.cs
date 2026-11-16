using System.IO;
using System.IO.Pipes;

namespace DoubleClickHealer.App.Services;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\DoubleClickHealer.SingleInstance";
    private const string PipeName = "DoubleClickHealer.Activate";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listener;
    private bool _disposed;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public void Listen(Action onActivate)
    {
        _listener = new Thread(() =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    server.WaitForConnection();
                    if (server.IsConnected) onActivate();
                }
                catch (Exception) when (_cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                }
            }
        })
        {
            IsBackground = true,
            Name = "DCH.SingleInstance",
        };

        _listener.Start();
    }

    public static void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            client.WriteByte(1);
        }
        catch (Exception)
        {
            // 已有实例可能正好在退出，忽略。
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellation.Cancel();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 非拥有者释放是正常的。
        }

        _mutex.Dispose();
        _cancellation.Dispose();
        _listener = null;
    }
}
