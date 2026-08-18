#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed record GamePrerequisiteStatus(
    IGame Game,
    PrerequisitesCheckResult? Result,
    string? Error)
{
    public bool IsAvailable => Result != null;
}

public sealed class PrerequisiteService
{
    public PrerequisitesCheckResult Check(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.GamePrerequisiteChecker.CheckPrerequisites(game);
    }

    public void ForceFix(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        game.GamePrerequisiteChecker.ForceFix(game);
    }

    public IReadOnlyList<GamePrerequisiteStatus> CheckAll(IEnumerable<IGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        return games.Select(game =>
        {
            try
            {
                return new GamePrerequisiteStatus(game, Check(game), null);
            }
            catch (Exception exception)
            {
                return new GamePrerequisiteStatus(game, null, exception.Message);
            }
        }).ToList();
    }
}
