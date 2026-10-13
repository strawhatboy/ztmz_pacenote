using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

internal static class SettingsSearchCatalog
{
    private static readonly (string Page, string Title)[] Pages =
    {
        ("GeneralPage", "tabs.general"), ("PlayPage", "tabs.playback"),
        ("VoiceSettingsPage", "settings.subsection.voicePackage"), ("HudPage", "tabs.hud"),
        ("VrPage", "tabs.vr"), ("ReplaySettingsPage", "settings.subsection.replay"), ("SettingsPage", "tabs.about")
    };

    public static IReadOnlyList<SettingSearchEntry> Load()
    {
        var assembly = typeof(SettingsSearchCatalog).Assembly;
        var entries = new List<SettingSearchEntry>();
        foreach (var (page, title) in Pages)
        {
            using var stream = assembly.GetManifestResourceStream($"SettingsSearch.{page}.xaml")
                ?? throw new System.InvalidOperationException($"Missing settings search metadata: {page}");
            var document = XDocument.Load(stream);
            foreach (var element in document.Descendants())
            {
                var anchor = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "SettingsSearch.Anchor")?.Value;
                if (anchor == null) continue;
                var keys = element.DescendantsAndSelf().Attributes().Select(a => a.Value)
                    .SelectMany(value => Regex.Matches(value, @"\{DynamicResource\s+(?:ResourceKey=)?([^}\s]+)\}")
                        .Select(match => match.Groups[1].Value))
                    .Where(key => key.Contains('.') && !key.EndsWith("Brush")).Distinct().ToArray();
                entries.Add(new(page, title, anchor, keys));
            }
        }
        return entries;
    }
}
