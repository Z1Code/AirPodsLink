using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AirPodsLink.App;

internal sealed class WindowsAudioEndpointService
{
    public async Task<(bool Found, bool Activated, string? Name, string? Error)> TryActivateStereoEndpointAsync(
        string? preferredDeviceName,
        bool bluetoothAlreadyConnected,
        CancellationToken cancellationToken = default)
    {
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
                    endpoint = FindBestEndpoint(enumerator, preferredDeviceName);
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
                await Task.Delay(125, cancellationToken);
            }
            while (DateTimeOffset.UtcNow < deadline);

            if (endpoint is null)
            {
                try
                {
                    var stale = FindBestEndpoint(enumerator, preferredDeviceName, activeOnly: false);
                    var staleName = stale?.FriendlyName;
                    stale?.Dispose();
                    return (stale is not null, false, staleName, "El endpoint A2DP todavía no está activo en Windows.");
                }
                catch
                {
                    return (true, false, preferredDeviceName,
                        $"Windows no estabilizó el endpoint A2DP. Último estado: {lastTransientError?.Message ?? "desconocido"}");
                }
            }

            var endpointName = endpoint.FriendlyName;
            using (endpoint)
            using (var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, false, 80))
            {
                var silence = new BufferedWaveProvider(endpoint.AudioClient.MixFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                silence.AddSamples(new byte[Math.Max(silence.WaveFormat.AverageBytesPerSecond, 4096)], 0,
                    Math.Max(silence.WaveFormat.AverageBytesPerSecond, 4096));
                output.Init(silence);
                output.Play();
                await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
                output.Stop();
            }

            return (true, true, endpointName, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return (true, false, null, $"{error.GetType().Name}: {error.Message}");
        }
    }

    private static MMDevice? FindBestEndpoint(MMDeviceEnumerator enumerator, string? preferredDeviceName, bool activeOnly = true) =>
        enumerator.EnumerateAudioEndPoints(DataFlow.Render, activeOnly ? DeviceState.Active : DeviceState.All)
            .Where(IsAirPodsStereoEndpoint)
            .OrderByDescending(device => NameMatches(device.FriendlyName, preferredDeviceName))
            .ThenByDescending(device => device.State == DeviceState.Active)
            .FirstOrDefault();

    private static bool IsAirPodsStereoEndpoint(MMDevice device)
    {
        var name = device.FriendlyName;
        var isAirPods = name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase);
        var isHandsFree = name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("micrófono", StringComparison.OrdinalIgnoreCase);
        return isAirPods && !isHandsFree;
    }

    private static bool NameMatches(string endpointName, string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) &&
        endpointName.Contains(deviceName.Replace(" - Find My", string.Empty), StringComparison.OrdinalIgnoreCase);
}
