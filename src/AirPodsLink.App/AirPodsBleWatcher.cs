using AirPodsLink.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;

namespace AirPodsLink.App;

internal sealed class AirPodsBleWatcher : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly NearbyAirPodsTracker _tracker = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private int _restartPending;
    private DateTimeOffset _lastRawDiagnostic = DateTimeOffset.MinValue;

    public event EventHandler<AirPodsSeenEventArgs>? AirPodsSeen;
    public event EventHandler? AppleProximityObserved;
    public event EventHandler<string>? Diagnostic;

    /// <summary>How long a reading stays valid after the last accepted packet.</summary>
    public TimeSpan StaleAfter => _tracker.StaleAfter;

    public AirPodsBleWatcher()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        // Do not apply a payload filter here. Several vendor drivers interpret
        // an empty manufacturer buffer as "zero-length payload only" and drop
        // valid AirPods advertisements before the application sees them.
        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Created or BluetoothLEAdvertisementWatcherStatus.Stopped or BluetoothLEAdvertisementWatcherStatus.Aborted)
        {
            _watcher.Start();
            Diagnostic?.Invoke(this, $"BLE scanner started ({_watcher.ScanningMode}).");
        }
    }

    public void Stop()
    {
        if (!_disposed && _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var section in args.Advertisement.ManufacturerData.Where(item => item.CompanyId == AppleProximityParser.AppleCompanyId))
        {
            var bytes = section.Data.ToArray();
            var offset = bytes.Length >= 3 && bytes[0] == 0x4C && bytes[1] == 0x00 ? 2 : 0;
            if (bytes.Length <= offset || bytes[offset] != AppleProximityParser.ProximityPairingType)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            // Strangers walking by broadcast the same packet. Acting on theirs
            // would show their battery and fire connection attempts.
            if (!_tracker.TryAccept(args.BluetoothAddress, args.RawSignalStrengthInDBm, now))
            {
                continue;
            }

            AppleProximityObserved?.Invoke(this, EventArgs.Empty);
            if (now - _lastRawDiagnostic > TimeSpan.FromSeconds(5))
            {
                _lastRawDiagnostic = now;
                Diagnostic?.Invoke(this, $"Apple proximity packet: {bytes.Length} bytes, RSSI {args.RawSignalStrengthInDBm} dBm.");
            }

            if (AppleProximityParser.TryParse(bytes, out var advertisement) && advertisement is not null)
            {
                AirPodsSeen?.Invoke(this, new AirPodsSeenEventArgs(args.BluetoothAddress, args.RawSignalStrengthInDBm, advertisement));
            }
        }
    }

    private async void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        Diagnostic?.Invoke(this, $"BLE scanner stopped: {args.Error}; waiting for Bluetooth to return.");
        if (_disposed || Interlocked.Exchange(ref _restartPending, 1) != 0) return;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                try
                {
                    Start();
                    if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) return;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    Diagnostic?.Invoke(this, $"BLE todavía no está disponible: {error.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        finally
        {
            Interlocked.Exchange(ref _restartPending, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) _watcher.Stop();
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        _lifetime.Dispose();
    }
}

internal sealed class AirPodsSeenEventArgs(ulong bluetoothAddress, short rssi, AirPodsAdvertisement advertisement) : EventArgs
{
    public ulong BluetoothAddress { get; } = bluetoothAddress;
    public short Rssi { get; } = rssi;
    public AirPodsAdvertisement Advertisement { get; } = advertisement;
}
