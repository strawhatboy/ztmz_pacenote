using Neo.IronLua;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class RoadbookGroupingTest
{
    [Fact]
    public void GroupedFragmentsStayTogetherButOrdinaryGameCallsRemainSeparate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string script;
        do
        {
            script = Path.Combine(directory!.FullName, "src/ZTMZ.PacenoteTool.WebDashboard/dashboards/pacenote_roadbook/script.lua");
            directory = directory.Parent;
        } while (!File.Exists(script) && directory != null);
        using var lua = new Lua();
        var env = lua.CreateEnvironment();
        var result = env.DoChunk(File.ReadAllText(script) + "\n" + """
            local args = { Self = { AnimationTimeSeconds = 10,
                ResolvePacenoteIconToken = function(t) return t end,
                ResolvePacenoteDisplayName = function(t) return t end },
                GameData = { Time = 0 }, GameContext = {} }
            local function update(id, group, count)
                local tokens = { Count = count }
                for i = 0, count - 1 do tokens[i] = {Token = 'right', Modifiers = {Count = 0}} end
                args.GameContext.CurrentPacenote = {Tokens = tokens, GroupId = group, DispatchedAtUtc = id, Source = 'live'}
                updateCurrentRow(args, 3)
            end
            update(1, 'acr-1', 1)
            update(2, 'acr-1', 2)
            assert(#history == 0 and #currentRow.items == 2)
            update(3, 'acr-2', 1)
            assert(#history == 1 and #history[1].items == 2)
            args.Self.AnimationTimeSeconds = 10.3
            assert(getTransitionProgress(args) == 1) -- frozen game clock
            resetRoadbook()
            update(4, '', 1)
            update(5, '', 1)
            assert(#history == 1) -- other games do not merge
            update(6, nil, 1)
            assert(#history == 2) -- legacy snapshots also retain old behavior
            resetRoadbook()
            local images = {}
            local gfx = { DrawImage = function(image, x1, y1, x2, y2, opacity)
                table.insert(images, {x = (x1 + x2) / 2, opacity = opacity}) end }
            args.Self.ImageResources = { ['pacenote@right'] = {} }
            local function render()
                images = {}
                drawRow(gfx, args.Self, currentRow, 0, 0, 300, 100, 1, nil, false, 0)
            end
            args.Self.AnimationTimeSeconds = 20
            update(7, 'append', 1)
            render()
            assert(images[1].x == 150)
            update(8, 'append', 2)
            render()
            assert(images[1].x == 150 and images[2].opacity == 0)
            args.Self.AnimationTimeSeconds = 20.075
            render()
            local halfway = images[1].x
            assert(halfway < 150 and halfway > 100)
            update(9, 'append', 3)
            render()
            assert(math.abs(images[1].x - halfway) < 0.001) -- no jump on another Mid
            args.Self.AnimationTimeSeconds = 20.3
            render()
            assert(images[3].opacity == 1 and #history == 0)
            args.GameContext.CurrentPacenote.GroupId = 'next'
            args.GameContext.CurrentPacenote.DispatchedAtUtc = 10
            updateCurrentRow(args, 0)
            assert(outgoingRow ~= nil and #history == 0)
            return true
            """, "roadbook-grouping.lua");
        Assert.True((bool)result[0]);
    }
}
