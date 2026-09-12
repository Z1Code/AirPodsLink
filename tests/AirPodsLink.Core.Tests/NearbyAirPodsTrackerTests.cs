using AirPodsLink.Core;
using Xunit;

namespace AirPodsLink.Core.Tests;

public sealed class NearbyAirPodsTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RejectsDevicesBelowTheSignalThreshold()
    {
        var tracker = new NearbyAirPodsTracker(minimumRssi: -75);

        Assert.False(tracker.TryAccept(1, -90, Start));
        Assert.True(tracker.IsStale(Start));
    }

    [Fact]
    public void KeepsFollowingTheLockedDeviceAndIgnoresOthers()
    {
        var tracker = new NearbyAirPodsTracker();

        Assert.True(tracker.TryAccept(1, -50, Start));
        Assert.False(tracker.TryAccept(2, -52, Start.AddSeconds(1)));
        Assert.True(tracker.TryAccept(1, -55, Start.AddSeconds(2)));
    }

    [Fact]
    public void SwitchesToAClearlyStrongerDevice()
    {
        var tracker = new NearbyAirPodsTracker();
        tracker.TryAccept(1, -60, Start);

        Assert.True(tracker.TryAccept(2, -45, Start.AddSeconds(1)));
        Assert.False(tracker.TryAccept(1, -60, Start.AddSeconds(2)));
    }

    [Fact]
    public void ReleasesTheLockOnceTheDeviceStopsAdvertising()
    {
        var tracker = new NearbyAirPodsTracker(staleAfter: TimeSpan.FromSeconds(30));
        tracker.TryAccept(1, -50, Start);

        Assert.False(tracker.IsStale(Start.AddSeconds(29)));
        Assert.True(tracker.IsStale(Start.AddSeconds(30)));
        // A rotated address is a different device from the adapter's point of
        // view, so it may only take over after the old one goes quiet.
        Assert.True(tracker.TryAccept(2, -58, Start.AddSeconds(31)));
    }

    [Fact]
    public void AcceptsARotatedAddressAfterTheOldAddressGoesQuietBriefly()
    {
        var tracker = new NearbyAirPodsTracker();
        tracker.TryAccept(1, -55, Start);

        Assert.False(tracker.TryAccept(2, -58, Start.AddSeconds(1)));
        Assert.True(tracker.TryAccept(2, -58, Start.AddSeconds(3)));
    }

    [Fact]
    public void StartsStale()
    {
        Assert.True(new NearbyAirPodsTracker().IsStale(Start));
    }
}
