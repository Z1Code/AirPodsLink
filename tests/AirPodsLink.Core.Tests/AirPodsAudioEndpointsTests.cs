using AirPodsLink.Core;
using Xunit;

namespace AirPodsLink.Core.Tests;

public sealed class AirPodsAudioEndpointsTests
{
    [Theory]
    [InlineData("Auriculares (AirPods Pro de Juan )")]
    [InlineData("Auriculares (2- AirPods Pro de Juan )")]
    [InlineData("Auriculares (AirPods Pro de Juan  - Find My)")]
    [InlineData("Headphones (Powerbeats Pro)")]
    public void AcceptsStereoRenderNames(string name) =>
        Assert.True(AirPodsAudioEndpoints.IsStereoName(name));

    [Theory]
    [InlineData("Auriculares con micrófono (AirPods Pro de Juan  - Find My Hands-Free)")]
    [InlineData("AirPods Pro Hands Free")]
    [InlineData("Headset (AirPods Pro)")]
    [InlineData("Altavoces (HyperX Cloud II Wireless)")]
    [InlineData("FxSound Speakers (FxSound Audio Enhancer)")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsEverythingElse(string? name) =>
        Assert.False(AirPodsAudioEndpoints.IsStereoName(name));

    [Fact]
    public void PrefersActiveOverUnplugged() =>
        Assert.True(AirPodsAudioEndpoints.Rank(AirPodsAudioEndpoints.StateActive) >
                    AirPodsAudioEndpoints.Rank(AirPodsAudioEndpoints.StateUnplugged));

    [Fact]
    public void DiscardsLeftoverStates()
    {
        const uint notPresent = 0x4;
        const uint disabled = 0x2;
        Assert.Equal(0, AirPodsAudioEndpoints.Rank(notPresent));
        Assert.Equal(0, AirPodsAudioEndpoints.Rank(disabled));
        Assert.True(AirPodsAudioEndpoints.Rank(AirPodsAudioEndpoints.StateUnplugged) > 0);
    }

    [Fact]
    public void UsableStatesCoversOnlyActiveAndUnplugged() =>
        Assert.Equal(AirPodsAudioEndpoints.StateActive | AirPodsAudioEndpoints.StateUnplugged,
                     AirPodsAudioEndpoints.UsableStates);
}
