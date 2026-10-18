using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using ZTMZ.PacenoteTool.WebDashboard;

public class GlassPanelCommandTests
{
    [Theory]
    [InlineData(6,6)]
    [InlineData(100,9)]
    [InlineData(-1,0)]
    [InlineData(float.NaN,0)]
    public void PolygonCornerRadiusFitsItsShortEdges(float requested,float expected)
    {
        var c=DrawGlassPanelCommand.CreateGeometry(0,[0,0,100,0,100,20,0,20],8,4,requested)!;
        Assert.Equal(expected,c.Radius);
        Assert.Equal(expected,(float)JObject.Parse(JsonConvert.SerializeObject(c))["radius"]!);
    }

    [Fact]
    public void ConvexGlassPreservesContourAndDerivesCaptureBounds()
    {
        float[] points = [57,0,322,0,340,23,333,85,302,124,14,124,0,110,18,47];
        var command = DrawGlassPanelCommand.CreateGeometry(0xff102030, points, 99, 99)!;
        Assert.Equal(points, command.Points);
        Assert.Equal(0, command.X); Assert.Equal(0, command.Y);
        Assert.Equal(340, command.Width); Assert.Equal(124, command.Height);
        Assert.Equal(64, command.Refraction); Assert.Equal(32, command.Blur);
        points[0] = 99;
        Assert.Equal(57, command.Points![0]); // Commands own their geometry snapshot.
        var reverse = command.Points.Chunk(2).Reverse().SelectMany(p => p).ToArray();
        Assert.Equal(command.Points, DrawGlassPanelCommand.CreateGeometry(0, reverse)!.Points);
        Assert.Equal(16, ((JArray)JObject.Parse(JsonConvert.SerializeObject(command))["points"]!).Count);
    }

    [Fact]
    public void InvalidConvexGlassIsRejected()
    {
        float[][] invalid = [[], [0,0,1,1], [0,0,1,0,0],
            [0,0,100,0,50,20,100,100,0,100], // Concave.
            [0,0,100,100,0,100,100,0], // Self-intersection.
            [0,0,100,0,100,0,0,100], // Duplicate edge.
            [0,0,50,50,100,100],
            [0,0,float.NaN,0,0,100], [0,0,float.PositiveInfinity,0,0,100],
            [0,0,40000,0,0,100], Enumerable.Range(0,18).Select(i=>(float)i).ToArray()];
        foreach (var points in invalid) Assert.Null(DrawGlassPanelCommand.CreateGeometry(0, points));
    }

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
