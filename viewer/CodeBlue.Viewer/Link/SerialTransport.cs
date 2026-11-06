using System.Collections.Concurrent;
using System.IO.Ports;

namespace CodeBlue.Viewer.Link;

/// <summary>
/// Serial port with a dedicated writer thread, so input handlers (including the
/// keyboard hook, which Windows times out if it blocks) only ever enqueue.
///
/// DTR and RTS are held low. On ESP32 DevKitC boards those lines drive the
/// auto-reset circuit, and toggling them on open can reset the board or drop it
/// into the bootloader.
/// </summary>
internal sealed class SerialTransport : IDisposable
{
    private readonly SerialPort _port;
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _writer;

    public event Action<Exception>? Faulted;

    public SerialTransport(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            WriteTimeout = 1000,
        };
        _port.Open();

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "CodeBlue serial writer" };
        _writer.Start();
    }

    public void Send(string data)
    {
        try { _queue.TryAdd(data); }
        catch (InvalidOperationException) { /* shutting down */ }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var data in _queue.GetConsumingEnumerable())
                _port.Write(data);
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
        try { _port.Close(); } catch { /* port may already be gone (unplugged) */ }
        _port.Dispose();
        _queue.Dispose();
    }
}
