using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsLink.App;

internal sealed class BluetoothWarmupService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BluetoothDevice? _heldDevice;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public async Task<(bool Found, bool Connected, string? DeviceName, string? Error)> TryWarmUpPairedAirPodsAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if ((!force && DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(4)) || !await _gate.WaitAsync(0, cancellationToken))
        {
            return (_heldDevice is not null, _heldDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected, _heldDevice?.Name, null);
        }

        try
        {
            _lastAttempt = DateTimeOffset.UtcNow;
            var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            var candidate = devices.FirstOrDefault(device =>
                device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                device.Name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase));
            if (candidate is null) return (false, false, null, null);

            var next = await BluetoothDevice.FromIdAsync(candidate.Id);
            if (next is null) return (true, false, candidate.Name, "Windows no pudo abrir el dispositivo Bluetooth emparejado.");

            _heldDevice?.Dispose();
            _heldDevice = next;

            // Retaining the normal Windows BluetoothDevice handle gives the
            // stack an early opportunity to restore remembered profiles. Do not
            // query uncached RFCOMM services here: on some adapters that blocks
            // for 10+ seconds and A2DP does not depend on the result.
            return (true, next.ConnectionStatus == BluetoothConnectionStatus.Connected, next.Name, null);
        }
        catch (Exception error)
        {
            return (_heldDevice is not null, false, _heldDevice?.Name, $"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _heldDevice?.Dispose();
        _gate.Dispose();
    }
}
