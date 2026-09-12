namespace AirPodsLink.Core;

public sealed record AirPodsAdvertisement(
    ushort ModelId,
    string ModelName,
    bool IsPaired,
    bool PrimaryPodIsLeft,
    int? LeftBattery,
    int? RightBattery,
    int? CaseBattery,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    bool LeftInEar,
    bool RightInEar,
    bool BothInCase,
    bool OneInCase,
    byte LidCounter,
    byte ConnectionState,
    byte[] RawData)
{
    public int? LowestAvailableBattery
    {
        get
        {
            var values = new[] { LeftBattery, RightBattery, CaseBattery }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            return values.Length == 0 ? null : values.Min();
        }
    }
}
