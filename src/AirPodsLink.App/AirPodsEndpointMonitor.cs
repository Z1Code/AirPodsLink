using AirPodsLink.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AirPodsLink.App;

/// <summary>
/// Reports when Windows brings the AirPods stereo endpoint to ACTIVE, whoever
/// caused it.
/// </summary>
/// <remarks>
/// Windows reconnects the AirPods on its own when they leave the case, so an
/// application that only reacts to reconnections it requested itself never
/// learns they arrived. Core Audio notifications cover every path: our own
/// reconnection order, Windows' automatic one, and the app starting while the
/// AirPods are already connected.
/// </remarks>
internal sealed class AirPodsEndpointMonitor : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised with the endpoint ID once it is usable for playback.</summary>
    public event EventHandler<string>? StereoEndpointActivated;

    public event EventHandler<string>? Diagnostic;

    public void Start()
    {
        if (_registered || _disposed) return;
        _enumerator.RegisterEndpointNotificationCallback(this);
        _registered = true;
    }

    /// <summary>
    /// The endpoint that is active right now, if any. Used at startup, where no
    /// state change will ever arrive because the transition already happened.
    /// </summary>
    public string? FindActiveStereoEndpointId()
    {
        try
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    string name;
                    try { name = device.FriendlyName; } catch { continue; }
                    if (AirPodsAudioEndpoints.IsStereoName(name)) return device.ID;
                }
            }
        }
        catch (Exception error)
        {
            Diagnostic?.Invoke(this, $"No se pudo revisar los endpoints activos: {error.Message}");
        }
        return null;
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        Diagnostic?.Invoke(this, $"Core Audio: endpoint {deviceId} pasó a {newState}.");
        if (newState != DeviceState.Active) return;
        RaiseIfAirPods(deviceId);
    }

    public void OnDeviceAdded(string pwstrDeviceId) => RaiseIfAirPods(pwstrDeviceId);

    public void OnDeviceRemoved(string deviceId) { }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Deliberately ignored. FxSound restores itself as the default right
        // after we hand it the AirPods; reacting here would start a fight over
        // the default device.
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    private void RaiseIfAirPods(string deviceId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(deviceId)) return;
        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active) return;
            string name;
            try { name = device.FriendlyName; } catch { return; }
            if (!AirPodsAudioEndpoints.IsStereoName(name)) return;
        }
        catch
        {
            // The endpoint can vanish between the notification and this lookup.
            return;
        }

        // Core Audio must not be blocked inside its own callback.
        var handler = StereoEndpointActivated;
        if (handler is not null) Task.Run(() => handler(this, deviceId));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_registered) _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
            // Shutting down; a failed unregister changes nothing.
        }
        _enumerator.Dispose();
    }
}
