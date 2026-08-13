using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
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

        const string query = """
            SELECT
                pacenote_view.id AS Id,
                pacenote_view.primary_filename AS PrimaryFilename,
                pacenote_view.filenames AS Filenames,
                pacenote_view.description AS Description,
                pacenote_view.type AS Type,
                pacenote_view.complexity AS Complexity,
                COALESCE(pacenote_fallbacks_view.fallbacks, '') AS Fallbacks
            FROM pacenote_view
            LEFT JOIN pacenote_fallbacks_view ON pacenote_view.id = pacenote_fallbacks_view.id
            ORDER BY pacenote_view.primary_filename COLLATE NOCASE;
            """;

            var tokens = (await resource.Connection.QueryAsync<PacenoteLanguageToken>(query)).ToList();
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
