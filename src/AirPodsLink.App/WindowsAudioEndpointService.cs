using AirPodsLink.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace AirPodsLink.App;

/// <summary>
/// Result of waking the stereo endpoint, with the milestones the latency
/// research asks to record separately.
/// </summary>
internal sealed record StereoActivationResult(
    bool Found,
    bool Activated,
    string? Name,
    string? Id,
    string? Error,
    TimeSpan UntilActive,
    TimeSpan UntilFirstBuffer,
    TimeSpan Hold,
    TimeSpan DefaultEndpointElapsed,
    string? DefaultEndpointError);

internal sealed class WindowsAudioEndpointService
{
    /// <summary>
    /// How long the silent stream is held open once it plays. The endpoint is
    /// usable as soon as playback starts; this only keeps Windows from tearing
    /// the A2DP stream down again straight away.
    /// </summary>
    public static readonly TimeSpan StreamHold = TimeSpan.FromMilliseconds(900);

    /// <summary>Cheap check used to tell whether a reconnection is needed at all.</summary>
    public bool IsStereoEndpointActive()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = FindBestEndpoint(enumerator, null, null);
            return endpoint?.State == DeviceState.Active;
        }
        catch
        {
            // Treat an unreadable endpoint list as "unknown", so the caller
            // retries instead of assuming the AirPods are still connected.
            return false;
        }
    }

    public async Task<StereoActivationResult> TryActivateStereoEndpointAsync(
        string? preferredDeviceName,
        string? preferredEndpointId,
        bool bluetoothAlreadyConnected,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var untilActive = TimeSpan.Zero;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? endpoint = null;
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(bluetoothAlreadyConnected ? 8 : 3);
            Exception? lastTransientError = null;
            do
            {
                try
                {
                    endpoint = FindBestEndpoint(enumerator, preferredDeviceName, preferredEndpointId);
                    if (endpoint?.State == DeviceState.Active) break;
                    endpoint?.Dispose();
                    endpoint = null;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Bluetooth endpoints briefly disappear while BthA2dp transitions
                    // from UNPLUGGED to ACTIVE. Treat that as state change, not failure.
                    lastTransientError = error;
                }
                await Task.Delay(50, cancellationToken);
            }
            while (DateTimeOffset.UtcNow < deadline);

            untilActive = timer.Elapsed;

            if (endpoint is null)
            {
                try
                {
                    var stale = FindBestEndpoint(enumerator, preferredDeviceName, preferredEndpointId, activeOnly: false);
                    var staleName = stale?.FriendlyName;
                    var staleId = stale?.ID;
                    var staleState = stale?.State.ToString();
                    stale?.Dispose();
                    return new(stale is not null, false, staleName, staleId,
                        stale is null
                            ? "El endpoint A2DP desapareció durante la conexión. Vuelve a emparejar los AirPods desde Configuración > Bluetooth y dispositivos."
                            : $"El endpoint A2DP sigue en estado {staleState}; Windows no terminó de conectar el perfil estéreo. Revisa Bluetooth y dispositivos y vuelve a intentarlo.",
                        untilActive, TimeSpan.Zero,
                        TimeSpan.Zero, TimeSpan.Zero, null);
                }
                catch
                {
                    return new(true, false, preferredDeviceName, null,
                        $"Windows no estabilizó el endpoint A2DP. Último estado: {lastTransientError?.Message ?? "desconocido"}",
                        untilActive, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);
                }
            }

            var endpointName = endpoint.FriendlyName;
            var endpointId = endpoint.ID;
            TimeSpan untilFirstBuffer;
            using (endpoint)
            using (var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, false, 80))
            {
                var silence = new ObservedSilenceProvider(endpoint.AudioClient.MixFormat, timer);
                output.Init(silence);
                output.Play();
                var completed = await Task.WhenAny(
                    silence.FirstRead,
                    Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (completed != silence.FirstRead)
                {
                    return new(true, false, endpointName, endpointId,
                        "WASAPI no solicitó el primer búfer de audio.", untilActive, TimeSpan.Zero,
                        TimeSpan.Zero, TimeSpan.Zero, null);
                }
                untilFirstBuffer = await silence.FirstRead;

                // FxSound reacts to the default-device change. Do it as soon
                // as WASAPI proves this exact endpoint is usable, while the
                // silent stream remains open for stability. Previously this
                // waited for StreamHold and added 900 ms to perceived latency.
                var defaultEndpointStarted = timer.Elapsed;
                var defaultEndpointError = DefaultAudioEndpointService.TrySetForMedia(endpointId);
                var defaultEndpointElapsed = timer.Elapsed - defaultEndpointStarted;
                await Task.Delay(StreamHold, cancellationToken);
                output.Stop();

                return new(true, true, endpointName, endpointId, null, untilActive, untilFirstBuffer,
                    StreamHold, defaultEndpointElapsed, defaultEndpointError);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return new(true, false, null, null, $"{error.GetType().Name}: {error.Message}",
                untilActive, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);
        }
    }

    private sealed class ObservedSilenceProvider(WaveFormat waveFormat, Stopwatch timer) : IWaveProvider
    {
        private readonly TaskCompletionSource<TimeSpan> _firstRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WaveFormat WaveFormat { get; } = waveFormat;
        public Task<TimeSpan> FirstRead => _firstRead.Task;

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            _firstRead.TrySetResult(timer.Elapsed);
            return count;
        }
    }

    /// <summary>
    /// Picks the endpoint to wake. The exact ID the reconnection order already
    /// chose wins, so both halves of the attempt act on the same endpoint
    /// instead of each resolving the duplicates on its own.
    /// </summary>
    private static MMDevice? FindBestEndpoint(
        MMDeviceEnumerator enumerator,
        string? preferredDeviceName,
        string? preferredEndpointId,
        bool activeOnly = true)
    {
        // Once the KS request selected an endpoint, never silently switch to a
        // different AirPods endpoint while Windows is bringing it online.
        if (!string.IsNullOrWhiteSpace(preferredEndpointId))
        {
            var preferred = enumerator.GetDevice(preferredEndpointId);
            if (!IsAirPodsStereoEndpoint(preferred) ||
                (activeOnly && preferred.State != DeviceState.Active) ||
                (!activeOnly && preferred.State is not (DeviceState.Active or DeviceState.Unplugged)))
            {
                preferred.Dispose();
                return null;
            }

            return preferred;
        }

        var candidates = enumerator.EnumerateAudioEndPoints(
                DataFlow.Render,
                activeOnly ? DeviceState.Active : DeviceState.Active | DeviceState.Unplugged)
            .Where(IsAirPodsStereoEndpoint)
            .OrderByDescending(device => device.State == DeviceState.Active)
            .ThenByDescending(device => NameMatches(SafeName(device), preferredDeviceName))
            .ToArray();

        var selected = candidates.FirstOrDefault();
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate, selected)) candidate.Dispose();
        }

        return selected;
    }

    private static string SafeName(MMDevice device)
    {
        // Leftover endpoints throw 0xE000020B when their name is read.
        try { return device.FriendlyName; }
        catch { return string.Empty; }
    }

    private static bool IsAirPodsStereoEndpoint(MMDevice device) =>
        AirPodsAudioEndpoints.IsStereoName(SafeName(device));

    private static bool NameMatches(string endpointName, string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) &&
        endpointName.Contains(deviceName.Replace(" - Find My", string.Empty), StringComparison.OrdinalIgnoreCase);
}
