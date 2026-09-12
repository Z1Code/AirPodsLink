using AirPodsLink.Core;
using Xunit;

namespace AirPodsLink.Core.Tests;

public sealed class AppleProximityParserTests
{
    [Fact]
    public void ParsesKnownAirPodsProAdvertisement()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0xA7, 0x4A, 0x09, 0x00, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var parsed = AppleProximityParser.TryParse(data, out var result);

        Assert.True(parsed);
        Assert.NotNull(result);
        Assert.Equal("AirPods Pro (2.ª gen.)", result.ModelName);
        Assert.Equal(100, result.LeftBattery);
        Assert.Equal(70, result.RightBattery);
        Assert.Equal(100, result.CaseBattery);
        Assert.True(result.CaseCharging);
        Assert.Equal(9, result.LidCounter);
    }

    [Fact]
    public void SwapsPodNibblesWhenRightIsPrimary()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x00, 0xA7, 0x00, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Equal(70, result!.LeftBattery);
        Assert.Equal(100, result.RightBattery);
    }

    [Fact]
    public void TreatsUnavailableBatteryNibbleAsUnknown()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x0E, 0x20, 0x20, 0xAF, 0x0F, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Equal(100, result!.LeftBattery);
        Assert.Null(result.RightBattery);
        Assert.Null(result.CaseBattery);
    }

    [Fact]
    public void AcceptsDriversThatPrefixAppleCompanyId()
    {
        byte[] data = [0x4C, 0x00, 0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0xA7, 0x4A, 0x09, 0x00, 0x04];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Equal("AirPods Pro (2.ª gen.)", result!.ModelName);
    }

    [Fact]
    public void RejectsInvalidPackets()
    {
        byte[][] invalidPackets = [[], [0x06, 0x19, 0x01, 0x14], [0x07, 0x19, 0x01, 0x14]];
        Assert.All(invalidPackets, data => Assert.False(AppleProximityParser.TryParse(data, out _)));
    }
}
