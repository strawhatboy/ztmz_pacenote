using Neo.IronLua;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NeoLuaRuntimeTest
{
    [Fact]
    public void CanCreateLuaEnvironment()
    {
        using var lua = new Lua();

        var environment = lua.CreateEnvironment();

        Assert.NotNull(environment);
    }
}
