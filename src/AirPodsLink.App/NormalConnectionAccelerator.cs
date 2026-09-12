using System.Diagnostics;

namespace AirPodsLink.App;

internal sealed class NormalConnectionAccelerator : IDisposable
{
    private readonly BluetoothWarmupService _bluetooth = new();
    private readonly WindowsAudioEndpointService _audio = new();
    private readonly BluetoothAudioKsConnector _ks = new();
    private readonly LocalEventLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public NormalConnectionAccelerator(LocalEventLog log) => _log = log;

    public async Task<ConnectionAttemptResult?> TryConnectAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if ((!force && DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(6)) ||
            !await _gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        var timer = Stopwatch.StartNew();
        try
        {
            _lastAttempt = DateTimeOffset.UtcNow;
            var ks = _ks.TryReconnectAirPods();
            var ksElapsed = timer.Elapsed;
            var defaultEndpointError = ks.RequestSent
                ? DefaultAudioEndpointService.TrySetForMedia(ks.EndpointId)
                : null;
            var bluetooth = ks.RequestSent
                ? (Found: true, Connected: false, DeviceName: ks.EndpointName, Error: (string?)null)
                : await _bluetooth.TryWarmUpPairedAirPodsAsync(force, cancellationToken);
            var audio = await _audio.TryActivateStereoEndpointAsync(
                bluetooth.DeviceName ?? ks.EndpointName,
                bluetooth.Connected || ks.RequestSent,
                cancellationToken);
            var result = new ConnectionAttemptResult(
                ks.EndpointFound,
                ks.RequestSent,
                bluetooth.Found,
                bluetooth.Connected,
                audio.Found,
                audio.Activated,
                bluetooth.DeviceName,
                audio.Name,
                timer.Elapsed,
                ksElapsed,
                audio.Error ?? defaultEndpointError ?? (!ks.RequestSent ? ks.Error ?? bluetooth.Error : null));
            await _log.WriteAsync("normal-connection-attempt", result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _bluetooth.Dispose();
        _gate.Dispose();
    }
}
