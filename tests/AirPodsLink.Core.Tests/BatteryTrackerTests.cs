using AirPodsLink.Core;
using Xunit;

namespace AirPodsLink.Core.Tests;

public sealed class BatteryTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 11, 0, 0, TimeSpan.Zero);

    // Headers captured from the user's AirPods Pro 2.
    private const string InCaseLidOpen = "07190114203555FA520004";   // 50/50, case 100
    // Left pod discharged and off, right pod playing; confirmed by the user.
    private const string LeftDeadRightPlaying = "071901142002F08F110005";

    private static AirPodsAdvertisement Parse(string header)
    {
        Assert.True(AppleProximityParser.TryParse(Convert.FromHexString(header), out var result));
        return result!;
    }

    private static AirPodsAdvertisement Synthetic(byte status, byte battery, byte charge) =>
        Parse($"0719011420{status:X2}{battery:X2}{charge:X2}110005");

    [Fact]
    public void ShowsTheRealLowChargeOfAPodThatIsStillPlaying()
    {
        var tracker = new BatteryTracker();

        tracker.Observe(Parse(LeftDeadRightPlaying), Start);

        Assert.Null(tracker.Left);
        Assert.Equal(0, tracker.Right?.Percent);
        Assert.Equal(0, tracker.LowestPod);
    }

    [Fact]
    public void RemembersADeadPodsLastChargeInsteadOfForgettingIt()
    {
        var tracker = new BatteryTracker();
        tracker.Observe(Synthetic(0x2B, 0x00, 0x8F), Start);          // both worn, both under 10 %

        tracker.Observe(Parse(LeftDeadRightPlaying), Start.AddMinutes(5)); // left now reports "unknown"

        Assert.Equal(0, tracker.Left?.Percent);
        Assert.Equal(Start, tracker.Left?.ObservedAt);
    }

    [Fact]
    public void RemembersTheCaseAfterItsLidCloses()
    {
        var tracker = new BatteryTracker();
        tracker.Observe(Parse(InCaseLidOpen), Start);

        // Case nibble 0xF: the case stops reporting once the lid closes.
        tracker.Observe(Synthetic(0x2B, 0x88, 0x0F), Start.AddMinutes(1));

        Assert.Equal(100, tracker.Case?.Percent);
        Assert.Equal(80, tracker.LowestPod);
    }

    [Fact]
    public void KeepsAPodWhoseNibbleBrieflyReadsUnknown()
    {
        var tracker = new BatteryTracker();
        tracker.Observe(Synthetic(0x2B, 0x87, 0x0F), Start);

        tracker.Observe(Synthetic(0x2B, 0xF7, 0x0F), Start.AddSeconds(2));

        Assert.Equal(70, tracker.LowestPod);
    }

    [Fact]
    public void MarksAReadingAsRememberedOnceItAges()
    {
        var tracker = new BatteryTracker();
        tracker.Observe(Parse(InCaseLidOpen), Start);

        Assert.True(tracker.IsPodReadingLive(Start.AddSeconds(30)));
        Assert.False(tracker.IsPodReadingLive(Start + BatteryTracker.LiveFor));
    }

    [Fact]
    public void JudgesFreshnessByThePodThatSetsTheMinimum()
    {
        var tracker = new BatteryTracker();
        tracker.Observe(Synthetic(0x2B, 0x00, 0x8F), Start);           // both under 10 %
        var later = Start + BatteryTracker.LiveFor + TimeSpan.FromMinutes(1);

        tracker.Observe(Parse(LeftDeadRightPlaying), later);           // only the right pod refreshes

        // Both are at the minimum; the right pod's live reading must win the
        // tie, or a playing pod would be drawn as a stale value.
        Assert.Equal(later, tracker.LowestPodReading?.ObservedAt);
        Assert.True(tracker.IsPodReadingLive(later));
    }

    [Fact]
    public void ReportsChangesOnlyWhenWhatIsShownChanges()
    {
        var tracker = new BatteryTracker();

        Assert.True(tracker.Observe(Parse(InCaseLidOpen), Start));
        Assert.False(tracker.Observe(Parse(InCaseLidOpen), Start.AddSeconds(1)));
        Assert.True(tracker.Observe(Parse(LeftDeadRightPlaying), Start.AddSeconds(2)));
    }

    [Fact]
    public void TracksChargingFromThePods()
    {
        var tracker = new BatteryTracker();

        // Charge nibble 0x3: both pods charging, case not.
        tracker.Observe(Synthetic(0x35, 0x99, 0x3A), Start);

        Assert.True(tracker.PodCharging);
        Assert.False(tracker.Case!.Value.Charging);
    }

    [Theory]
    [InlineData(0, "<10")]
    [InlineData(10, "10")]
    [InlineData(100, "100")]
    public void ShowsAZeroNibbleAsUnderTenPercent(int percent, string expected) =>
        Assert.Equal(expected, BatteryTracker.FormatPercent(percent));
}
