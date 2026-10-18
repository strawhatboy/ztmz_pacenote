using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using ZTMZ.PacenoteTool.WebDashboard;

public class GlassPanelCommandTests
{
    [Fact]
    public void SerializesNativeWireContractAndPreservesArgb()
    {
        DrawingCommand command = DrawGlassPanelCommand.Create(0x8c123456, 10, 20, 210, 120, 16, 12, 6)!;
        var json = JObject.Parse(JsonConvert.SerializeObject(command));
        Assert.Equal(new[] { "blur", "color", "height", "radius", "refraction", "type", "width", "x", "y" },
            json.Properties().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("drawGlassPanel", (string?)json["type"]);
        Assert.Equal(0x8c123456u, (uint)json["color"]!);
        Assert.Equal(10, (float)json["x"]!);
        Assert.Equal(20, (float)json["y"]!);
        Assert.Equal(200, (float)json["width"]!);
        Assert.Equal(100, (float)json["height"]!);
        Assert.Equal(16, (float)json["radius"]!);
        Assert.Equal(12, (float)json["refraction"]!);
        Assert.Equal(6, (float)json["blur"]!);
    }

    [Fact]
    public void BoundsGpuWorkAndReplacesNonFiniteEffectValues()
    {
        var c = DrawGlassPanelCommand.Create(0, -float.MaxValue, -float.MaxValue,
            float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue)!;
        Assert.Equal(-32768, c.X); Assert.Equal(-32768, c.Y);
        Assert.Equal(32768, c.Width); Assert.Equal(32768, c.Height);
        Assert.Equal(16384, c.Radius); Assert.Equal(64, c.Refraction); Assert.Equal(32, c.Blur);
        c = DrawGlassPanelCommand.Create(0, 0, 0, 100, 20, float.NaN, float.PositiveInfinity, float.NaN)!;
        Assert.Equal(0, c.Radius); Assert.Equal(8, c.Refraction); Assert.Equal(4, c.Blur);
        c = DrawGlassPanelCommand.Create(0, 0, 0, 100, 20, 99, -2, -4)!;
        Assert.Equal(10, c.Radius); Assert.Equal(0, c.Refraction); Assert.Equal(0, c.Blur);
    }

    [Theory]
    [InlineData(5, 5, 7.5f)]
    [InlineData(999, 8, 6)]
    [InlineData(-1, 0, 10)]
    [InlineData(float.NaN, 0, 10)]
    [InlineData(float.PositiveInfinity, 0, 10)]
    public void SlantIsBoundedAndOnlyChangesWireShapeWhenPresent(float input, float expected, float radius)
    {
        var command = DrawGlassPanelCommand.Create(0, 0, 0, 20, 100, 99, 0, 0, input)!;
        Assert.Equal(expected, command.Slant);
        Assert.Equal(radius, command.Radius);
        var json = JObject.Parse(JsonConvert.SerializeObject(command));
        if (expected == 0) Assert.Null(json["slant"]);
        else Assert.Equal(expected, (float)json["slant"]!);
    }

    [Theory]
    [InlineData(float.NaN, 0, 100, 100)]
    [InlineData(0, float.PositiveInfinity, 100, 100)]
    [InlineData(0, 0, float.NegativeInfinity, 100)]
    [InlineData(0, 0, 100, float.NaN)]
    [InlineData(100, 0, 0, 100)]
    [InlineData(0, 100, 100, 0)]
    [InlineData(0, 0, 0, 100)]
    public void RejectsInvalidGeometry(float left, float top, float right, float bottom)
        => Assert.Null(DrawGlassPanelCommand.Create(0, left, top, right, bottom, 10));
}
