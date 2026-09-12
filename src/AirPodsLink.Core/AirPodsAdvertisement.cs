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
    /// <summary>
    /// Lowest charge between both earbuds. The case is excluded on purpose: a
    /// depleted case says nothing about how long playback will last.
    /// </summary>
    public int? LowestPodBattery => (LeftBattery, RightBattery) switch
    {
        (null, null) => null,
        (null, var right) => right,
        (var left, null) => left,
        var (left, right) => Math.Min(left!.Value, right!.Value)
    };

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
