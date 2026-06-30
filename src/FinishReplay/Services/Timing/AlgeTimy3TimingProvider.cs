using System.IO.Ports;
using FinishReplay.Models;

namespace FinishReplay.Services.Timing;

/// <summary>
/// Real ALGE TimY3 timing provider over the device's serial / USB-serial (CDC) port. Reads ASCII
/// protocol lines and maps them to <see cref="TimingTrigger"/>s via <see cref="AlgeTimyProtocolParser"/>.
///
/// USB-only note: ALGE also ships a native USB SDK (<c>Alge.TimyUsb</c>, see <c>docs/</c>), but that
/// assembly is a .NET-Framework mixed-mode DLL and cannot be loaded by .NET 9. For USB-only devices
/// that don't expose a COM port, host that DLL in a small .NET Framework "bridge" process and feed its
/// <c>LineReceived</c> text into <see cref="AlgeTimyProtocolParser"/> (which is transport-agnostic).
/// See [[alge-timy-usb]].
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

    /// <summary>Serial ports the device might be on (for the UI port picker).</summary>
    public static IReadOnlyList<string> GetAvailablePorts() => SerialPort.GetPortNames();

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return Task.CompletedTask;

        var port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\r",      // Timy terminates lines with CR
            ReadTimeout = 500,
            Encoding = System.Text.Encoding.ASCII,
        };
        port.DataReceived += OnDataReceived;
        port.Open();
        _port = port;

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
        var port = _port;
        if (port is null) return;

        try
        {
            // Drain all complete lines currently buffered.
            while (true)
            {
                string line;
                try { line = port.ReadLine(); }
                catch (TimeoutException) { break; }

                var trigger = AlgeTimyProtocolParser.Parse(line, DateTimeOffset.Now);
                if (trigger is not null)
                    TriggerReceived?.Invoke(this, trigger);
            }
        }
        catch (Exception) when (!IsConnected)
        {
            // Port closed underneath us during shutdown — ignore.
        }
    }

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}
