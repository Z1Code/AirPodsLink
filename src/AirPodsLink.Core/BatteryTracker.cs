namespace AirPodsLink.Core;

/// <summary>One component's last reported charge.</summary>
public readonly record struct BatteryReading(int Percent, bool Charging, DateTimeOffset ObservedAt)
{
    public TimeSpan Age(DateTimeOffset now) => now - ObservedAt;
}

/// <summary>
/// Accumulates advertisements into the best current knowledge of each
/// component, instead of letting every packet overwrite what is shown.
/// </summary>
/// <remarks>
/// A single advertisement is an incomplete view: the case only reports its
/// charge while the lid is open, and a pod that is off or out of range reports
/// "unknown" (nibble 0xF). Each of those used to wipe a good reading.
/// </remarks>
public sealed class BatteryTracker
{
    /// <summary>Readings older than this are shown as remembered, not live.</summary>
    public static readonly TimeSpan LiveFor = TimeSpan.FromMinutes(2);

    public BatteryReading? Left { get; private set; }

    public BatteryReading? Right { get; private set; }

    public BatteryReading? Case { get; private set; }

    /// <returns>True when anything shown to the user changed.</returns>
    public bool Observe(AirPodsAdvertisement advertisement, DateTimeOffset now)
    {
        var before = (Left, Right, Case);

        if (advertisement.LeftBattery is int left)
            Left = new BatteryReading(left, advertisement.LeftCharging, now);
        if (advertisement.RightBattery is int right)
            Right = new BatteryReading(right, advertisement.RightCharging, now);
        if (advertisement.CaseBattery is int caseCharge)
            Case = new BatteryReading(caseCharge, advertisement.CaseCharging, now);

        return !Equivalent(before.Left, Left) ||
               !Equivalent(before.Right, Right) ||
               !Equivalent(before.Case, Case);
    }

    /// <summary>
    /// The pod with the least charge: what bounds the listening time left. On a
    /// tie the fresher reading wins, so a live value is not shown as remembered.
    /// </summary>
    public BatteryReading? LowestPodReading => (Left, Right) switch
    {
        (null, null) => null,
        (null, { } right) => right,
        ({ } left, null) => left,
        ({ } left, { } right) when left.Percent != right.Percent =>
            left.Percent < right.Percent ? left : right,
        ({ } left, { } right) => left.ObservedAt >= right.ObservedAt ? left : right
    };

    public int? LowestPod => LowestPodReading?.Percent;

    public bool PodCharging => Left?.Charging == true || Right?.Charging == true;

    public bool IsPodReadingLive(DateTimeOffset now) =>
        LowestPodReading is { } reading && reading.Age(now) < LiveFor;

    /// <summary>
    /// Text for a charge. A 0x0 nibble means "under 10 %": pods reporting it can
    /// still play, so showing a bare 0 reads as empty when it is not.
    /// </summary>
    public static string FormatPercent(int percent) => percent == 0 ? "<10" : $"{percent}";

    // Timestamps advance on every packet; only what the user sees counts.
    private static bool Equivalent(BatteryReading? a, BatteryReading? b) =>
        a?.Percent == b?.Percent && a?.Charging == b?.Charging;
}
