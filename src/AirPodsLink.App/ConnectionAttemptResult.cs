namespace AirPodsLink.App;

internal sealed record ConnectionAttemptResult(
    bool KsEndpointFound,
    bool KsReconnectRequested,
    bool BluetoothDeviceFound,
    bool BluetoothConnected,
    bool StereoEndpointFound,
    bool StereoEndpointActivated,
    string? DeviceName,
    string? EndpointName,
    TimeSpan Elapsed,
    TimeSpan KsRequestElapsed,
    string? Error)
{
    public bool AudioReady => StereoEndpointActivated;
    public bool BluetoothLinkReady => BluetoothConnected || AudioReady;

    public string Summary => AudioReady
        ? $"Audio estéreo listo en {Elapsed.TotalMilliseconds:0} ms (orden A2DP: {KsRequestElapsed.TotalMilliseconds:0} ms)"
        : BluetoothLinkReady
            ? $"Bluetooth enlazado; audio estéreo pendiente ({Elapsed.TotalMilliseconds:0} ms)"
        : BluetoothDeviceFound
            ? $"Windows aún no conectó ({Elapsed.TotalMilliseconds:0} ms)"
            : "No se encontró un AirPod emparejado";
}
