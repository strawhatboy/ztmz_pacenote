using Neo.IronLua;
using System.Reflection;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class RoadbookDispatchTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentCallsWithinOneSecondAreBothDisplayed(bool serialized)
    {
        using var lua = new Lua();
        var env = Load(lua);
        var context = new GameContext();
        context.CurrentPacenote = Call("6_left", 100);
        Render(env, context, serialized);
        context.CurrentPacenote = Call("2_right", 500);
        Render(env, context, serialized);
        Assert.Equal("6_left|2_right", (string)env.CallMember("displayedTokens")[0]);
        Render(env, context, serialized);
        Assert.Equal("6_left|2_right", (string)env.CallMember("displayedTokens")[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsBetweenFramesAreRetainedAndResetClearsThem(bool serialized)
    {
        using var lua = new Lua();
        var env = Load(lua);
        var context = new GameContext();
        context.CurrentPacenote = Call("6_left", 100);
        context.CurrentPacenote = Call("2_right", 200);
        context.CurrentPacenote = Call("crest", 300);
        Render(env, context, serialized);
        Assert.Equal("6_left|2_right|crest", (string)env.CallMember("displayedTokens")[0]);
        Render(env, context, serialized);
        Assert.Equal("6_left|2_right|crest", (string)env.CallMember("displayedTokens")[0]);
        context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
        Render(env, context, serialized);
        Assert.Equal("", (string)env.CallMember("displayedTokens")[0]);
        context.CurrentPacenote = Call("jump", 400);
        Render(env, context, serialized);
        Assert.Equal("jump", (string)env.CallMember("displayedTokens")[0]);
    }

    [Fact]
    public void RetentionIsBoundedAndPreviouslyPublishedWindowsDoNotChange()
    {
        using var lua = new Lua();
        var env = Load(lua);
        var context = new GameContext { CurrentPacenote = Call("first", 0) };
        var saved = context.RecentPacenotes;
        Render(env, context, false);
        for (var i = 0; i < 100; i++) context.CurrentPacenote = Call($"call{i}", i);
        Assert.Single(saved);
        Assert.Equal("first", saved[0].Tokens[0].Token);
        Assert.Equal(32, context.RecentPacenotes.Count);
        // A renderer that fell behind still catches up to the retained window.
        Render(env, context, false);
        Assert.Equal("call96|call97|call98|call99", (string)env.CallMember("displayedTokens")[0]);
        context.CurrentPacenote = context.CurrentPacenote;
        Render(env, context, false);
        Assert.Equal("call96|call97|call98|call99", (string)env.CallMember("displayedTokens")[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupedCallsAccumulateWithoutLosingOrdinaryCalls(bool serialized)
    {
        using var lua = new Lua();
        var env = Load(lua);
        var context = new GameContext();
        var first = Call("6_left", 100) with { GroupId = "acr-1" };
        context.CurrentPacenote = first;
        Render(env, context, serialized);
        context.CurrentPacenote = Call("6_left", 100) with
        {
            GroupId = "acr-1", Tokens = [new("6_left", []), new("long", [])]
        };
        context.CurrentPacenote = Call("2_right", 100);
        Render(env, context, serialized);
        Assert.Equal("6_left,long|2_right", (string)env.CallMember("displayedTokens")[0]);
    }

    private static void Render(LuaGlobal env, GameContext context, bool serialized)
    {
        if (!serialized)
        {
            env.CallMember("renderContext", context);
            return;
        }
        // Exercise both full-scene and delta context payloads used by the native
        // in-game host and browser. Recreate Lua tables as after a JSON decode.
        var serialize = typeof(WebDashboardServer).GetMethod("CreateBrowserLuaTelemetryContext",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var fullScene in new[] { true, false })
        {
            var payload = serialize.Invoke(null, [new GameData(), context, fullScene]);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            env.CallMember("renderContext", ToLua(json.RootElement));
        }
    }

    private static object? ToLua(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var table = new LuaTable();
            foreach (var member in value.EnumerateObject()) table[member.Name] = ToLua(member.Value);
            return table;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var table = new LuaTable();
            table["Count"] = value.GetArrayLength();
            var index = 0;
            foreach (var item in value.EnumerateArray()) table[index++] = ToLua(item);
            return table;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static PacenotePlaybackSnapshot Call(string token, int milliseconds) => new(
        100, [new(token, Array.Empty<string>())],
        new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(milliseconds),
        "script", "rbr", "");

    private static LuaGlobal Load(Lua lua)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string script;
        do
        {
            script = Path.Combine(directory!.FullName, "src/ZTMZ.PacenoteTool.WebDashboard/dashboards/pacenote_roadbook/script.lua");
            directory = directory.Parent;
        } while (!File.Exists(script) && directory != null);
        var env = lua.CreateEnvironment();
        env.DoChunk(File.ReadAllText(script) + "\n" + """
            local args = { Self = { AnimationTimeSeconds = 10,
                ResolvePacenoteIconToken = function(t) return t end,
                ResolvePacenoteDisplayName = function(t) return t end } }
            function renderContext(context)
                args.GameContext = context
                updateCurrentRow(args, 3)
            end
            function displayedTokens()
                local tokens = {}
                local function rowTokens(row)
                    local items = {}
                    for _, item in ipairs(row.items) do table.insert(items, item.token) end
                    return table.concat(items, ',')
                end
                for i = #history, 1, -1 do table.insert(tokens, rowTokens(history[i])) end
                if currentRow ~= nil then table.insert(tokens, rowTokens(currentRow)) end
                return table.concat(tokens, '|')
            end
            """, "roadbook-dispatch.lua");
        return env;
    }
}
