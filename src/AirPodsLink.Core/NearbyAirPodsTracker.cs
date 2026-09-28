namespace AirPodsLink.Core;

/// <summary>
/// Decides which Apple proximity advertisement belongs to the user's own
/// AirPods. Nearby strangers broadcast the same packets, so without this filter
/// their battery would overwrite the local reading and trigger connection
/// attempts.
/// </summary>
/// <remarks>
/// AirPods rotate their BLE address periodically, so the tracker cannot match
/// the paired address. It follows nearby advertisers, allows a short quiet gap
/// for address rotation and lets a stronger signal take over.
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
    private readonly Dictionary<ulong, DateTimeOffset> _recentCandidates = new();

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

        // Keep a short rolling view of advertisements. A fixed lock can get
        // captured by another person's AirPods and suppress the user's device
        // for the full stale timeout. RSSI is only a proximity heuristic, so
        // allow a meaningfully stronger nearby advertiser to take over.
        foreach (var address in _recentCandidates.Where(pair => now - pair.Value >= AddressHandoverAfter)
                     .Select(pair => pair.Key).ToArray())
        {
            _recentCandidates.Remove(address);
        }

        _recentCandidates[bluetoothAddress] = now;
        var lockedRecent = _recentCandidates.TryGetValue(_address, out var lockedLastSeen) &&
                           now - lockedLastSeen < AddressHandoverAfter;
        if (!_locked || now - _lastAccepted >= StaleAfter ||
            (!lockedRecent && now - _lastAccepted >= AddressHandoverAfter))
        {
            _locked = true;
            _address = bluetoothAddress;
            _rssi = rssi;
            _lastAccepted = now;
            return true;
        }

        if (bluetoothAddress == _address)
        {
            _rssi = rssi;
            _lastAccepted = now;
            return true;
        }

        if (rssi >= _rssi + SwitchMarginDb)
        {
            _address = bluetoothAddress;
            _rssi = rssi;
            _lastAccepted = now;
            return true;
        }

        return false;
    }

    public bool IsStale(DateTimeOffset now) => !_locked || now - _lastAccepted >= StaleAfter;
}
