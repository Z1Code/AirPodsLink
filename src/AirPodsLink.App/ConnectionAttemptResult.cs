namespace AirPodsLink.App;

/// <summary>
/// One connection attempt, broken into the milestones the latency research
/// requires: BLE advertisement, KS request, endpoint ACTIVE, default output
/// change and first audio actually streamed.
/// </summary>
internal sealed record ConnectionAttemptResult(
    bool KsEndpointFound,
    bool KsReconnectRequested,
    bool BluetoothDeviceFound,
    bool? BluetoothConnected,
    bool StereoEndpointFound,
    bool StereoEndpointActivated,
    string? DeviceName,
    string? EndpointName,
    TimeSpan Elapsed,
    TimeSpan KsRequestElapsed,
    string? Error)
{
    /// <summary>Time from the BLE advertisement that triggered this attempt.</summary>
    public TimeSpan? BleToAttempt { get; init; }

    /// <summary>Time spent selecting the AirPods as the default media output.</summary>
    public TimeSpan? DefaultEndpointElapsed { get; init; }

    /// <summary>Time from the start of the attempt until the endpoint went ACTIVE.</summary>
    public TimeSpan? EndpointActiveElapsed { get; init; }

    /// <summary>Time from the start of the attempt until WASAPI requested its first buffer.</summary>
    public TimeSpan? FirstAudioElapsed { get; init; }

    /// <summary>Deliberate wait added after playback started; not connection cost.</summary>
    public TimeSpan? StreamHold { get; init; }

    /// <summary>Endpoint that received KSPROPERTY_ONESHOT_RECONNECT.</summary>
    public string? KsEndpointId { get; init; }

    /// <summary>Endpoint that WASAPI actually opened.</summary>
    public string? AudioEndpointId { get; init; }

    public bool AudioReady => StereoEndpointActivated;
    public bool BluetoothLinkReady => BluetoothConnected == true || AudioReady;

    /// <summary>
    /// What the user actually waits for: the advertisement through to the first
    /// audio streamed, excluding the hold that only keeps the stream open.
    /// </summary>
    public TimeSpan? TimeToAudio => BleToAttempt is null || FirstAudioElapsed is null
        ? null
        : BleToAttempt + FirstAudioElapsed;

    public string Summary => AudioReady
        ? TimeToAudio is { } timeToAudio
            ? $"WASAPI recibió su primer búfer en {timeToAudio.TotalMilliseconds:0} ms desde el anuncio (orden A2DP: {KsRequestElapsed.TotalMilliseconds:0} ms)"
            : $"WASAPI recibió su primer búfer en {(FirstAudioElapsed ?? Elapsed).TotalMilliseconds:0} ms desde el inicio del intento (orden A2DP: {KsRequestElapsed.TotalMilliseconds:0} ms)"
        : BluetoothLinkReady
            ? $"Bluetooth enlazado; audio estéreo pendiente ({Elapsed.TotalMilliseconds:0} ms)"
        : BluetoothDeviceFound
            ? $"Windows aún no conectó ({Elapsed.TotalMilliseconds:0} ms)"
            : "No se encontró un AirPod emparejado";
}
