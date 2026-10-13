using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZTMZ.PacenoteTool.Core;

public sealed record SettingSearchEntry(string PageId, string PageTitleKey, string Anchor, string[] ResourceKeys);
public sealed record SettingSearchResult(SettingSearchEntry Entry, string Title, string PageTitle);

public static class SettingsSearchIndex
{
    public static IReadOnlyList<SettingSearchResult> Search(IEnumerable<SettingSearchEntry> entries, string query,
        Func<string, string> translate, Func<string, string> fallbackTranslate)
    {
        var terms = Normalize(query).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return Array.Empty<SettingSearchResult>();
        return entries.Select(entry =>
        {
            var title = translate(entry.Anchor);
            var page = translate(entry.PageTitleKey);
            var text = Normalize(string.Join(" ", entry.ResourceKeys.Concat(new[] { entry.Anchor, entry.PageTitleKey })
                .SelectMany(key => new[] { key, translate(key), fallbackTranslate(key) })));
            var normalizedTitle = Normalize(title);
            var score = terms.All(text.Contains) ? terms.Sum(term => normalizedTitle == term ? 10 : normalizedTitle.Contains(term) ? 3 : 1) : 0;
            return (Result: new SettingSearchResult(entry, title, page), Score: score);
        }).Where(item => item.Score > 0).OrderByDescending(item => item.Score)
            .ThenBy(item => item.Result.PageTitle).ThenBy(item => item.Result.Title)
            .Select(item => item.Result).ToArray();
    }

    private static string Normalize(string value) => (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
}
