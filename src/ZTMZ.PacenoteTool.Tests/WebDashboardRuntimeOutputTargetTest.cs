using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Tests;

public class WebDashboardRuntimeOutputTargetTest
{
    [Theory]
    [InlineData(DashboardOutputTarget.Web, DashboardOutputTarget.Web)]
    [InlineData(DashboardOutputTarget.Overlay, DashboardOutputTarget.Overlay)]
    [InlineData(DashboardOutputTarget.Both, DashboardOutputTarget.Web)]
    [InlineData((DashboardOutputTarget)99, DashboardOutputTarget.Web)]
    public void RuntimeOnlyAcceptsOneConcreteClientTarget(
        DashboardOutputTarget requested,
        DashboardOutputTarget expected)
    {
        Assert.Equal(expected, WebDashboardRuntime.NormalizeOutputTarget(requested));
    }
}
