using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed class PacenoteLanguageCatalog
{
    public IReadOnlyList<PacenoteLanguageToken> Tokens { get; init; } = [];
    public IReadOnlyList<PacenoteLanguageAttribute> Attributes { get; init; } = [];
}

public sealed class PacenoteLanguageToken
{
    public int Id { get; init; }
    public string PrimaryFilename { get; init; } = string.Empty;
    public string Filenames { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Complexity { get; init; } = string.Empty;
    public string Fallbacks { get; init; } = string.Empty;
    public string IconUri { get; set; } = string.Empty;
}

public sealed class PacenoteLanguageAttribute
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public static class PacenoteLanguageCatalogProvider
{
    public static async Task<PacenoteLanguageCatalog> LoadAsync()
    {
        try
        {
            var resource = ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance;
            await resource.LoadData();
            var locale = I18NLoader.Instance.CurrentCultureName;
            var filenamesById = resource.Filenames
                .GroupBy(item => item.id)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item => item.is_primary)
                        .ThenBy(item => item.filename, System.StringComparer.OrdinalIgnoreCase)
                        .ToList());
            var primaryFilenameById = filenamesById.ToDictionary(
                item => item.Key,
                item => item.Value.First().filename);
            var fallbacksById = resource.Fallbacks
                .GroupBy(item => item.id)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(",", group
                        .OrderBy(item => item.order_id)
                        .Select(item => primaryFilenameById.GetValueOrDefault(item.fallback_id))
                        .Where(item => !string.IsNullOrWhiteSpace(item))));

            var tokens = resource.Pacenotes
                .Where(item => filenamesById.ContainsKey(item.id))
                .Select(item =>
                {
                    var primaryFilename = primaryFilenameById[item.id];
                    return new PacenoteLanguageToken
                    {
                        Id = item.id,
                        PrimaryFilename = primaryFilename,
                        Filenames = string.Join(",", filenamesById[item.id].Select(filename => filename.filename)),
                        Description = resource.ResolvePacenoteDisplayName(primaryFilename, locale),
                        Type = resource.TypeDict.GetValueOrDefault(item.type)?.name ?? string.Empty,
                        Complexity = resource.ComplexityDict.GetValueOrDefault(item.complexity)?.name ?? string.Empty,
                        Fallbacks = fallbacksById.GetValueOrDefault(item.id) ?? string.Empty
                    };
                })
                .OrderBy(item => item.PrimaryFilename, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            AttachIconUris(tokens);
            return new PacenoteLanguageCatalog
            {
                Tokens = tokens,
                Attributes = GetAttributes()
            };
        }
        catch (System.Exception exception)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(exception, "Could not load the pacenote editor catalog.");
            return new PacenoteLanguageCatalog { Attributes = GetAttributes() };
        }
    }

    internal static void AttachIconUris(IReadOnlyList<PacenoteLanguageToken> tokens)
    {
        try
        {
            var iconCatalog = PacenoteIconCatalog.Load();
            foreach (var token in tokens)
            {
                if (iconCatalog.TryResolve(token.PrimaryFilename, out var icon))
                {
                    token.IconUri = new System.Uri(icon!.FilePath).AbsoluteUri;
                }
            }
        }
        catch (System.Exception exception)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(
                exception,
                "Pacenote editor icons are unavailable; the roadbook preview will use fallback symbols.");
        }
    }

    private static IReadOnlyList<PacenoteLanguageAttribute> GetAttributes()
    {
        return new List<PacenoteLanguageAttribute>
        {
            new PacenoteLanguageAttribute
            {
                Name = $"@{ScriptFlags.AUTHOR}",
                Description = "Author of this pacenote script."
            },
            new PacenoteLanguageAttribute
            {
                Name = $"@{ScriptFlags.DYNAMIC}",
                Description = "Play this script using dynamic timing."
            }
        };
    }
}
