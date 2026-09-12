namespace AirPodsLink.Core;

/// <summary>
/// Single source of truth for choosing which Windows audio endpoint represents
/// the AirPods stereo output.
/// </summary>
/// <remarks>
/// Re-pairing leaves duplicates behind: this machine carries both
/// "Auriculares (AirPods Pro de Juan )" and "Auriculares (2- AirPods Pro de Juan )".
/// PKEY_Device_ContainerId cannot tell them apart — every duplicate, including
/// the Hands-Free nodes, reports the same container, because they are all the
/// same physical headset. The device state is what separates them: the live
/// endpoint is ACTIVE or UNPLUGGED, the leftovers are NOTPRESENT or DISABLED.
/// </remarks>
public static class AirPodsAudioEndpoints
{
    public const uint StateActive = 0x1;
    public const uint StateUnplugged = 0x8;

    /// <summary>States worth considering; anything else is a leftover.</summary>
    public const uint UsableStates = StateActive | StateUnplugged;

    public static bool IsStereoName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var isAirPods = name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase);
        var isHandsFree = name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("micrófono", StringComparison.OrdinalIgnoreCase);
        return isAirPods && !isHandsFree;
    }

    /// <summary>Higher ranks are better candidates. Zero means unusable.</summary>
    public static int Rank(uint state) => state switch
    {
        StateActive => 2,
        StateUnplugged => 1,
        _ => 0
    };
}
