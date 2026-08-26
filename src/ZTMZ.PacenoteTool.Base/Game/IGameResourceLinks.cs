#nullable enable

using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>Semantic category used by a UI to present a game resource appropriately.</summary>
public enum GameResourceKind
{
    General,
    Pacenotes,
    Localization
}

/// <summary>Describes an external resource without depending on a UI framework.</summary>
public sealed record GameResourceLink(
    string Id,
    string LabelKey,
    Uri Uri,
    GameResourceKind Kind = GameResourceKind.General,
    string? DescriptionKey = null,
    string? ToolTipKey = null);

/// <summary>
/// Optional capability for games that publish documentation, localization, or mod resources.
/// The host UI owns presentation and opening the URI.
/// </summary>
public interface IGameWithResourceLinks
{
    IReadOnlyList<GameResourceLink> ResourceLinks { get; }
}
