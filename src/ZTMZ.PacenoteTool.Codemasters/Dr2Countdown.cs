namespace ZTMZ.PacenoteTool.Codemasters;

/// <summary>Announce only newly observed native start lights. Missing reads and
/// pauses never advance a clock, replay a number, or queue missed numbers.</summary>
public sealed class Dr2Countdown
{
    private ulong address;
    private int lastNumber = 6;

    public int? Update(Dr2StartLightsSnapshot? snapshot)
    {
        if (snapshot is not { Phase: >= 0 and <= 6 } lights) return null;
        if (address != lights.Address || lights.Phase == 0)
        {
            address = lights.Address;
            lastNumber = 6;
        }
        var number = lights.Number;
        if (number >= lastNumber) return null;
        lastNumber = number;
        return number is >= 1 and <= 5 ? number : null;
    }

    public void Reset() { address = 0; lastNumber = 6; }
}
