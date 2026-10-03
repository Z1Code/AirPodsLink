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
        Assert.Equal(70, result.LeftBattery);
        Assert.Equal(100, result.RightBattery);
        Assert.Equal(100, result.CaseBattery);
        Assert.True(result.CaseCharging);
        Assert.Equal(9, result.LidCounter);
    }

    [Fact]
    public void SwapsPodNibblesWhenStatusBit5IsClear()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x00, 0xA7, 0x00, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Equal(100, result!.LeftBattery);
        Assert.Equal(70, result.RightBattery);
    }

    [Fact]
    public void TreatsUnavailableBatteryNibbleAsUnknown()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x0E, 0x20, 0x20, 0xAF, 0x0F, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Null(result!.LeftBattery);
        Assert.Equal(100, result.RightBattery);
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
    public void ReportsTheLowestEarbudChargeIgnoringTheCase()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0xA7, 0x01, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Equal(10, result!.CaseBattery);
        Assert.Equal(70, result.LowestPodBattery);
    }

    [Fact]
    public void FallsBackToTheOnlyKnownEarbudCharge()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0xAF, 0x00, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.Null(result!.LeftBattery);
        Assert.Equal(100, result.LowestPodBattery);
    }

    // Headers captured from the user's real AirPods Pro 2. The trailing
    // 16-byte encrypted payload is irrelevant to every field decoded here.
    private static byte[] Captured(string header) => Convert.FromHexString(header);

    [Theory]
    [InlineData("07190114203555FA520004", "both pods in the case, lid open")]
    [InlineData("07190114205555FA520004", "both pods in the case, lid open, other pod advertising")]
    public void ReportsNoPodInEarWhileBothRestInTheCase(string header, string situation)
    {
        Assert.True(AppleProximityParser.TryParse(Captured(header), out var result), situation);
        Assert.True(result!.BothInCase, situation);
        Assert.False(result.LeftInEar, situation);
        Assert.False(result.RightInEar, situation);
        Assert.Equal(50, result.LeftBattery);
        Assert.Equal(50, result.RightBattery);
        Assert.Equal(100, result.CaseBattery);
    }

    [Fact]
    public void ReportsBothPodsInEarWhileWorn()
    {
        Assert.True(AppleProximityParser.TryParse(Captured("07190114202B008F110005"), out var result));
        Assert.True(result!.LeftInEar);
        Assert.True(result.RightInEar);
        Assert.False(result.BothInCase);
        Assert.False(result.OneInCase);
    }

    [Fact]
    public void ReportsOnePodOutOfTheEar()
    {
        // Captured seconds after the user took one pod out (status 0x29).
        Assert.True(AppleProximityParser.TryParse(Captured("071901142029008F110005"), out var result));
        Assert.NotEqual(result!.LeftInEar, result.RightInEar);
    }

    [Fact]
    public void DecodesTheUserConfirmedLeftDeadRightPlayingPacket()
    {
        // Sent for minutes while the user wore both pods with the left one
        // discharged and the right one still playing. The mirrored decode put
        // the dead pod in the ear and the playing pod out of it.
        Assert.True(AppleProximityParser.TryParse(Captured("071901142002F08F110005"), out var result));
        Assert.Null(result!.LeftBattery);
        Assert.False(result.LeftInEar);
        Assert.Equal(0, result.RightBattery);
        Assert.True(result.RightInEar);
    }

    [Fact]
    public void AssignsChargingBitsWithTheSamePodAsTheNibbles()
    {
        // Charge bit 0 belongs to the low-nibble pod: the left one when status
        // bit 0x20 is set.
        byte[] data = [0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0xA7, 0x1F, 0x00, 0x00, 0x00];

        Assert.True(AppleProximityParser.TryParse(data, out var result));
        Assert.True(result!.LeftCharging);
        Assert.False(result.RightCharging);
    }

    [Fact]
    public void ParsesATruncatedPacketWhenEveryDecodedByteIsPresent()
    {
        // Only the first eleven bytes are decoded; the encrypted tail may be
        // missing without affecting the result.
        Assert.True(AppleProximityParser.TryParse(Captured("07190114203555FA520004"), out _));
    }

    [Fact]
    public void RejectsInvalidPackets()
    {
        byte[][] invalidPackets = [[], [0x06, 0x19, 0x01, 0x14], [0x07, 0x19, 0x01, 0x14]];
        Assert.All(invalidPackets, data => Assert.False(AppleProximityParser.TryParse(data, out _)));
    }
}
