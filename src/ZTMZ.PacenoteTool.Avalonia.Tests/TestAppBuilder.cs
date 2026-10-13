using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(
    typeof(ZTMZ.PacenoteTool.Avalonia.Tests.TestAppBuilder))]

namespace ZTMZ.PacenoteTool.Avalonia.Tests;

public static class TestAppBuilder
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void IsolateUserData() => Environment.SetEnvironmentVariable("ZTMZ_DATA_HOME",
        Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
