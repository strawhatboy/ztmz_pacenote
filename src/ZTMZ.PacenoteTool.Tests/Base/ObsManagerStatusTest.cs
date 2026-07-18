using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class ObsConnectionStatusTest
{
    [Theory]
    [InlineData(ObsConnectionState.Disabled, false)]
    [InlineData(ObsConnectionState.Disconnected, false)]
    [InlineData(ObsConnectionState.Connecting, false)]
    [InlineData(ObsConnectionState.Connected, true)]
    [InlineData(ObsConnectionState.Failed, false)]
    public void IsConnectedOnlyForConnectedState(ObsConnectionState state, bool expected)
    {
        var status = new ObsConnectionStatus(state, string.Empty);

        Assert.Equal(expected, status.IsConnected);
    }
}
