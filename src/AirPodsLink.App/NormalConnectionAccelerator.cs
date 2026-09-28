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

    /// <summary>
    /// Waits between repeated selections, measured against FxSound: it ignored
    /// the endpoint immediately after reconnection but adopted it seconds later.
    /// </summary>
    private static readonly TimeSpan[] RouteRetryDelays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(8)
    ];

    public NormalConnectionAccelerator(LocalEventLog log) => _log = log;

    /// <summary>Whether Windows still exposes an active AirPods stereo endpoint.</summary>
    public bool IsStereoEndpointActive() => _audio.IsStereoEndpointActive();

    public async Task<ConnectionAttemptResult?> TryConnectAsync(
        bool force = false,
        DateTimeOffset? advertisementObservedAt = null,
        CancellationToken cancellationToken = default)
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

            // The KS path does not open a Bluetooth handle, so the classic link
            // state is genuinely unknown here rather than disconnected.
            var bluetooth = ks.RequestSent
                ? (Found: true, Connected: (bool?)null, DeviceName: ks.EndpointName, Error: (string?)null)
                : await WarmUpAsync(force, cancellationToken);

            var audioPhaseStarted = timer.Elapsed;
            var audio = await _audio.TryActivateStereoEndpointAsync(
                bluetooth.DeviceName ?? ks.EndpointName,
                ks.EndpointId,
                bluetooth.Connected == true || ks.RequestSent,
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
                audio.Error ?? audio.DefaultEndpointError ?? (!ks.RequestSent ? ks.Error ?? bluetooth.Error : null))
            {
                BleToAttempt = advertisementObservedAt is null
                    ? null
                    : _lastAttempt - advertisementObservedAt.Value,
                DefaultEndpointElapsed = audio.DefaultEndpointElapsed,
                EndpointActiveElapsed = audioPhaseStarted + audio.UntilActive,
                FirstAudioElapsed = audio.UntilFirstBuffer == TimeSpan.Zero ? null : audioPhaseStarted + audio.UntilFirstBuffer,
                StreamHold = audio.Hold == TimeSpan.Zero ? null : audio.Hold,
                KsEndpointId = ks.EndpointId,
                AudioEndpointId = audio.Id
            };
            await _log.WriteAsync("normal-connection-attempt", result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Makes an endpoint that Windows just reported as ACTIVE the default media
    /// output, but only once it can really be opened.
    /// </summary>
    /// <remarks>
    /// A freshly reported A2DP endpoint is not playable yet. Handing it over
    /// immediately makes FxSound fail to open it and silently fall back to its
    /// previous device, which was measured: the tone still came out of the old
    /// speakers. Opening a short stream first proves the endpoint works and
    /// leaves it warm for whoever adopts it next.
    /// </remarks>
    public async Task<string?> RouteToStereoEndpointAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken))
        {
            return "Otra operación de audio está en curso.";
        }

        try
        {
            var audio = await _audio.TryActivateStereoEndpointAsync(
                null, endpointId, bluetoothAlreadyConnected: true, cancellationToken);
            if (!audio.Activated)
            {
                var failure = audio.Error ?? "El endpoint no llegó a estar reproducible.";
                await _log.WriteAsync("stereo-endpoint-routed", new { endpointId, playable = false, error = failure });
                return failure;
            }

            var target = audio.Id ?? endpointId;
            var error = DefaultAudioEndpointService.TrySetForMedia(target);

            // Selecting it once is not enough. A downstream processor such as
            // FxSound refreshes its own device list only after Windows settles
            // the new Bluetooth endpoint; a single early selection was measured
            // to leave the audio on the previous speakers. Repeating the
            // selection is idempotent and cheap.
            foreach (var delay in RouteRetryDelays)
            {
                await Task.Delay(delay, cancellationToken);
                if (!_audio.IsStereoEndpointActive()) break;
                error = DefaultAudioEndpointService.TrySetForMedia(target) ?? error;
            }

            await _log.WriteAsync("stereo-endpoint-routed", new
            {
                endpointId = target,
                name = audio.Name,
                playable = true,
                untilActiveMs = audio.UntilActive.TotalMilliseconds,
                retries = RouteRetryDelays.Length,
                error
            });
            return error;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(bool Found, bool? Connected, string? DeviceName, string? Error)> WarmUpAsync(
        bool force, CancellationToken cancellationToken)
    {
        var warm = await _bluetooth.TryWarmUpPairedAirPodsAsync(force, cancellationToken);
        return (warm.Found, warm.Connected, warm.DeviceName, warm.Error);
    }

    public void Dispose()
    {
        _bluetooth.Dispose();
        _gate.Dispose();
    }
}
