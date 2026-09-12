namespace AirPodsLink.Core;

public static class AppleProximityParser
{
    public const ushort AppleCompanyId = 0x004C;
    public const byte ProximityPairingType = 0x07;

    private static readonly IReadOnlyDictionary<ushort, string> Models = new Dictionary<ushort, string>
    {
        [0x0220] = "AirPods (1.ª gen.)",
        [0x0F20] = "AirPods (2.ª gen.)",
        [0x1320] = "AirPods (3.ª gen.)",
        [0x1920] = "AirPods (4.ª gen.)",
        [0x1B20] = "AirPods 4 con ANC",
        [0x0A20] = "AirPods Max",
        [0x1F20] = "AirPods Max USB-C",
        [0x0E20] = "AirPods Pro",
        [0x1420] = "AirPods Pro (2.ª gen.)",
        [0x2420] = "AirPods Pro 2 USB-C",
        [0x2520] = "AirPods Pro (3.ª gen.)",
    };

    public static bool TryParse(ReadOnlySpan<byte> data, out AirPodsAdvertisement? result)
    {
        result = null;
        // Some Windows Bluetooth drivers include the little-endian company ID
        // in Data even though WinRT normally exposes it separately.
        if (data.Length >= 2 && data[0] == 0x4C && data[1] == 0x00)
        {
            data = data[2..];
        }
        if (data.Length < 11 || data[0] != ProximityPairingType)
        {
            return false;
        }

        var declaredPayloadLength = data[1];
        if (declaredPayloadLength > 0 && data.Length < Math.Min(declaredPayloadLength + 2, 11))
        {
            return false;
        }

        var modelId = (ushort)((data[3] << 8) | data[4]);
        var status = data[5];
        var primaryPodIsLeft = (status & 0x20) != 0;
        var firstBattery = DecodeBattery(data[6] >> 4);
        var secondBattery = DecodeBattery(data[6] & 0x0F);

        // Apple changes the physical primary pod. Bit 5 indicates whether the
        // first battery nibble belongs to the left or right component.
        var leftBattery = primaryPodIsLeft ? firstBattery : secondBattery;
        var rightBattery = primaryPodIsLeft ? secondBattery : firstBattery;

        var chargeFlags = data[7] >> 4;
        var caseBattery = DecodeBattery(data[7] & 0x0F);
        var leftCharging = primaryPodIsLeft ? (chargeFlags & 0b0010) != 0 : (chargeFlags & 0b0001) != 0;
        var rightCharging = primaryPodIsLeft ? (chargeFlags & 0b0001) != 0 : (chargeFlags & 0b0010) != 0;

        // These fields are intentionally exposed conservatively. Firmware can
        // reuse reserved combinations, but these masks are stable across the
        // public Continuity protocol implementations used as references.
        var leftInEar = (status & 0b0000_1000) != 0;
        var rightInEar = (status & 0b0000_0011) != 0;
        var bothInCase = (status & 0b0000_0100) != 0;
        var oneInCase = (status & 0b0001_0000) != 0;

        result = new AirPodsAdvertisement(
            modelId,
            Models.GetValueOrDefault(modelId, $"AirPods/Beats 0x{modelId:X4}"),
            data[2] == 0x01,
            primaryPodIsLeft,
            leftBattery,
            rightBattery,
            caseBattery,
            leftCharging,
            rightCharging,
            (chargeFlags & 0b0100) != 0,
            leftInEar,
            rightInEar,
            bothInCase,
            oneInCase,
            (byte)(data[8] & 0x0F),
            data[10],
            data.ToArray());
        return true;
    }

    private static int? DecodeBattery(int nibble) => nibble is >= 0 and <= 10 ? nibble * 10 : null;
}
