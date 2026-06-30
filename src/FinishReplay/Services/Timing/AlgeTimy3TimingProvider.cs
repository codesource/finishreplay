using System.IO.Ports;
using FinishReplay.Models;

namespace FinishReplay.Services.Timing;

/// <summary>
/// Reads timing events from an ALGE TimY3 over USB/serial. Structural stub: the serial
/// plumbing and connection lifecycle are in place, but the line parser is not implemented.
///
/// TODO:
///   - Confirm the TimY3 serial settings (baud rate, parity, line ending) and adjust below.
///   - Implement <see cref="TryParse"/> to map a raw line to a <see cref="TimingTrigger"/>.
///   - Map device channels to Start / Stop / Intermediate; everything else -> Unknown,
///     always preserving the raw line in <see cref="TimingTrigger.RawMessage"/>.
/// The real device is not required to build or run the app.
/// </summary>
public sealed class AlgeTimy3TimingProvider : ITimingProvider
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public AlgeTimy3TimingProvider(string portName, int baudRate = 9600)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public string Name => $"ALGE TimY3 ({_portName})";

    public bool IsConnected => _port?.IsOpen ?? false;

    public event EventHandler<TimingTrigger>? TriggerReceived;
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>List serial ports the device might be on (for the UI port picker).</summary>
    public static IReadOnlyList<string> GetAvailablePorts() => SerialPort.GetPortNames();

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // TODO: verify parity/stop-bits/handshake for the TimY3.
        _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\r\n",
            ReadTimeout = 500,
        };
        _port.DataReceived += OnDataReceived;
        _port.Open();
        ConnectionChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            if (_port.IsOpen) _port.Close();
            _port.Dispose();
            _port = null;
        }
        ConnectionChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port is null) return;

        try
        {
            var line = _port.ReadLine();
            if (TryParse(line, out var trigger))
                TriggerReceived?.Invoke(this, trigger);
        }
        catch (TimeoutException)
        {
            // No complete line yet; ignore and wait for more data.
        }
    }

    /// <summary>
    /// Parse a raw TimY3 line into a <see cref="TimingTrigger"/>.
    /// TODO: implement the real protocol. For now everything is reported as Unknown
    /// so raw data still flows into metadata during bring-up.
    /// </summary>
    private static bool TryParse(string line, out TimingTrigger trigger)
    {
        trigger = new TimingTrigger
        {
            Type = TimingTriggerType.Unknown,
            ReceivedAt = DateTimeOffset.Now,
            VideoTime = TimeSpan.Zero, // TODO: derive from recording start once wired to the engine.
            RawMessage = line,
        };
        return !string.IsNullOrWhiteSpace(line);
    }

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}
