namespace ZTMZ.PacenoteTool.KT;

internal sealed record Wrc7NativeResult(bool Ended, bool Failed, bool Ready, bool ProfileFailed)
{
    public bool Pending => !Ended && !Failed && !Ready && !ProfileFailed;
    public bool Retired => Ended && Failed && Ready && ProfileFailed;

    internal static Wrc7NativeResult? Decode(ReadOnlySpan<byte> finish, ReadOnlySpan<byte> profile)
    {
        if (finish.Length < 3 || profile.Length < 14 || finish[0] > 1 || finish[2] > 1 ||
            profile[12] > 1 || profile[13] > 1) return null;
        return new(finish[0] == 1, finish[2] == 1, profile[12] == 1, profile[13] == 1);
    }
}
