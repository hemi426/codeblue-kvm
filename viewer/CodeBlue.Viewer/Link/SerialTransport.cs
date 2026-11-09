using System.Collections.Concurrent;
using System.IO.Ports;

namespace CodeBlue.Viewer.Link;

/// <summary>Receives a chunk of bytes read from the port.</summary>
internal delegate void SerialDataHandler(ReadOnlySpan<byte> data);

/// <summary>
/// Serial port with a dedicated writer thread, so input handlers (including the
/// keyboard hook, which Windows times out if it blocks) only ever enqueue, and
/// a reader thread for what the board sends back.
///
/// DTR and RTS are held low. On ESP32 DevKitC boards those lines drive the
/// auto-reset circuit, and toggling them on open can reset the board or drop it
/// into the bootloader.
/// </summary>
internal sealed class SerialTransport : IDisposable
{
    private readonly SerialPort _port;
    private readonly BlockingCollection<byte[]> _queue = new();
    private readonly Thread _writer;
    private Thread? _reader;
    private volatile bool _disposing;

    public event Action<Exception>? Faulted;

    public SerialTransport(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            WriteTimeout = 1000,
            ReadTimeout = 100,
        };
        _port.Open();

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "CodeBlue serial writer" };
        _writer.Start();
    }

    public void Send(byte[] data)
    {
        try { _queue.TryAdd(data); }
        catch (InvalidOperationException) { /* shutting down */ }
    }

    /// <summary>Starts a reader thread that hands every received chunk to <paramref name="onData"/>.</summary>
    public void StartReading(SerialDataHandler onData)
    {
        _reader = new Thread(() => ReadLoop(onData)) { IsBackground = true, Name = "CodeBlue serial reader" };
        _reader.Start();
    }

    private void ReadLoop(SerialDataHandler onData)
    {
        var buf = new byte[256];
        try
        {
            while (!_disposing)
            {
                int n;
                try { n = _port.Read(buf, 0, buf.Length); }
                catch (TimeoutException) { continue; }
                if (n > 0) onData(buf.AsSpan(0, n));
            }
        }
        catch (Exception ex) when (!_disposing)
        {
            Faulted?.Invoke(ex);
        }
        catch
        {
            // Closing the port ends a pending read; expected during Dispose.
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var data in _queue.GetConsumingEnumerable())
                _port.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    /// <summary>Drains anything still queued (final release/LED commands), then closes.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(1));
        _disposing = true;
        try { _port.Close(); } catch { /* port may already be gone (unplugged) */ }
        _reader?.Join(TimeSpan.FromSeconds(1));
        _port.Dispose();
        _queue.Dispose();
    }
}
