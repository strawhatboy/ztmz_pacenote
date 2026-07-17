using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class PacenoteDropoutPolicyTest
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(50, 49, true)]
    [InlineData(50, 50, false)]
    [InlineData(100, 99, true)]
    [InlineData(-1, 0, false)]
    [InlineData(101, 99, true)]
    public void ShouldDropUsesAClampedPercentageThreshold(
        int chancePercentage,
        int randomRoll,
        bool expected)
    {
        Assert.Equal(expected, PacenoteDropoutPolicy.ShouldDrop(chancePercentage, randomRoll));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void ShouldDropRejectsRollsOutsideTheRandomRange(int randomRoll)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PacenoteDropoutPolicy.ShouldDrop(50, randomRoll));
    }
}
