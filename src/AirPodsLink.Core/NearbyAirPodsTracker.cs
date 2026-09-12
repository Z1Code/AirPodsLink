namespace AirPodsLink.Core;

/// <summary>
/// Decides which Apple proximity advertisement belongs to the user's own
/// AirPods. Nearby strangers broadcast the same packets, so without this filter
/// their battery would overwrite the local reading and trigger connection
/// attempts.
/// </summary>
/// <remarks>
/// AirPods rotate their BLE address periodically, so the tracker cannot match
/// the paired address. It locks onto the first close-enough device and keeps it
/// until the advertisements stop for <see cref="StaleAfter"/>, which also covers
/// the rotation: the old address goes quiet and the new one takes the lock.
/// </remarks>
public sealed class NearbyAirPodsTracker
{
    /// <summary>Roughly the gap between "on my desk" and "through a wall".</summary>
    private const int SwitchMarginDb = 10;
    private static readonly TimeSpan AddressHandoverAfter = TimeSpan.FromSeconds(2);

    private ulong _address;
    private short _rssi;
    private DateTimeOffset _lastAccepted = DateTimeOffset.MinValue;
    private bool _locked;

    /// <param name="minimumRssi">
    /// Entry gate only; the lock below is what keeps strangers out. Measured
    /// AirPods on the same desk swing between -55 and -65 dBm, so the floor has
    /// to sit well under that or the user's own reading drops out.
    /// </param>
    public NearbyAirPodsTracker(short minimumRssi = -75, TimeSpan? staleAfter = null)
    {
        MinimumRssi = minimumRssi;
        StaleAfter = staleAfter ?? TimeSpan.FromSeconds(30);
    }

    public short MinimumRssi { get; }

    public TimeSpan StaleAfter { get; }

    public bool TryAccept(ulong bluetoothAddress, short rssi, DateTimeOffset now)
    {
        if (rssi < MinimumRssi)
        {
            return false;
        }

        if (_locked && bluetoothAddress != _address &&
            now - _lastAccepted < AddressHandoverAfter &&
            rssi < _rssi + SwitchMarginDb)
        {
            return false;
        }

        _locked = true;
        _address = bluetoothAddress;
        _rssi = rssi;
        _lastAccepted = now;
        return true;
    }

    public bool IsStale(DateTimeOffset now) => !_locked || now - _lastAccepted >= StaleAfter;
}
