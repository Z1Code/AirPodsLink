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

        var modelId = (ushort)((data[3] << 8) | data[4]);
        var status = data[5];

        // Each pod owns one battery nibble, one charging bit and one in-ear
        // bit; status bit 0x20 says which pod is which. When it is set the left
        // pod owns the low nibble, charging bit 0 and in-ear bit 0x02, matching
        // the OpenPods reference decoder. This mapping used to be mirrored:
        // with the left pod discharged and the right one playing, real AirPods
        // Pro 2 sent "07190114 2002F08F" for minutes, and the mirrored decode
        // reported the dead left pod in the ear and the playing right pod out.
        var lowIsLeft = (status & 0x20) != 0;
        var highBattery = DecodeBattery(data[6] >> 4);
        var lowBattery = DecodeBattery(data[6] & 0x0F);
        var leftBattery = lowIsLeft ? lowBattery : highBattery;
        var rightBattery = lowIsLeft ? highBattery : lowBattery;

        var chargeFlags = data[7] >> 4;
        var caseBattery = DecodeBattery(data[7] & 0x0F);
        var lowCharging = (chargeFlags & 0b0001) != 0;
        var highCharging = (chargeFlags & 0b0010) != 0;
        var leftCharging = lowIsLeft ? lowCharging : highCharging;
        var rightCharging = lowIsLeft ? highCharging : lowCharging;

        // Bit 0x01 is not an in-ear flag. Treating it as one reported a pod in
        // the ear while both sat in the case, in every in-case capture (status
        // 0x35 and 0x55).
        var lowInEar = (status & 0b0000_0010) != 0;
        var highInEar = (status & 0b0000_1000) != 0;
        var leftInEar = lowIsLeft ? lowInEar : highInEar;
        var rightInEar = lowIsLeft ? highInEar : lowInEar;
        var bothInCase = (status & 0b0000_0100) != 0;
        var oneInCase = (status & 0b0001_0000) != 0;

        result = new AirPodsAdvertisement(
            modelId,
            Models.GetValueOrDefault(modelId, $"AirPods/Beats 0x{modelId:X4}"),
            data[2] == 0x01,
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

    /// <summary>
    /// Nibbles carry tenths of charge: 0x7 means 70-79 %, and 0x0 means under
    /// 10 % — not empty, a pod reporting it can still be playing. 0xF and the
    /// other values above 0xA mean "not reported".
    /// </summary>
    private static int? DecodeBattery(int nibble) => nibble is >= 0 and <= 10 ? nibble * 10 : null;
}
