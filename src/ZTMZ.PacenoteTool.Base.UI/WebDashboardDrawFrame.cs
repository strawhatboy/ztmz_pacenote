using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Identifies whether a captured command layer is retained between WebDashboard frames.
/// </summary>
public enum WebDashboardDrawLayerKind
{
    Static,
    Dynamic
}

/// <summary>
/// A sequence of commands that must remain at its original z-order relative to the other layers.
/// </summary>
public sealed class WebDashboardDrawLayer
{
    public WebDashboardDrawLayer(
        string id,
        WebDashboardDrawLayerKind kind,
        IReadOnlyList<DrawingCommand> commands,
        bool staticContentProvided = false)
    {
        Id = id;
        Kind = kind;
        Commands = commands ?? new List<DrawingCommand>();
        StaticContentProvided = staticContentProvided;
    }

    public string Id { get; }

    public WebDashboardDrawLayerKind Kind { get; }

    public IReadOnlyList<DrawingCommand> Commands { get; }

    /// <summary>
    /// True when a static layer was intentionally redrawn for this frame. Empty static
    /// layers with this flag clear a previously cached layer.
    /// </summary>
    public bool StaticContentProvided { get; }
}

/// <summary>
/// The command output captured for one WebDashboard render tick.
/// </summary>
public sealed class WebDashboardDrawFrame
{
    public WebDashboardDrawFrame(
        IReadOnlyList<WebDashboardDrawLayer> layers,
        IReadOnlyList<DrawingCommand> legacyCommands,
        bool hasExplicitStaticLayers)
    {
        Layers = layers ?? new List<WebDashboardDrawLayer>();
        LegacyCommands = legacyCommands ?? new List<DrawingCommand>();
        HasExplicitStaticLayers = hasExplicitStaticLayers;
    }

    public IReadOnlyList<WebDashboardDrawLayer> Layers { get; }

    /// <summary>
    /// Flattened commands for clients or dashboards that do not opt into layered rendering.
    /// </summary>
    public IReadOnlyList<DrawingCommand> LegacyCommands { get; }

    public bool HasExplicitStaticLayers { get; }

    public bool CanUseLayeredRendering => HasExplicitStaticLayers
        && Layers.Any(layer => layer.Kind == WebDashboardDrawLayerKind.Static);
}
