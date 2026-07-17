using System;

namespace ZTMZ.PacenoteTool.Base;

public static class PacenoteDropoutPolicy
{
    public static bool ShouldDrop(int chancePercentage, int randomRoll)
    {
        if (randomRoll is < 0 or >= 100)
        {
            throw new ArgumentOutOfRangeException(nameof(randomRoll));
        }

        return randomRoll < Math.Clamp(chancePercentage, 0, 100);
    }
}
