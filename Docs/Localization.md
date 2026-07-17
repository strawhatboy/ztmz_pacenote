# Localization

The application currently ships with `en-us` and `zh-cn`. The localization loader discovers every JSON resource file in a directory named `lang`, so a locale such as `de-de.json` is included automatically once it is copied to the application output.
When an exact regional culture is unavailable, the loader uses an installed regional variant of the same language, for example `de-AT` uses `de-DE`. `zh-Hans-CN` maps to `zh-CN`.

## Shipped Beta Locales

- `de-DE` German
- `fr-FR` French
- `it-IT` Italian
- `ru-RU` Russian
- `es-ES` Spanish (Spain)
- `pt-BR` Portuguese (Brazil)
- `ja-JP` Japanese
- `ko-KR` Korean

`es-419` can be added later for Latin American Spanish when it has a separately reviewed translation.

These packs translate the current WPF interface, safety dialogs, game labels, and dashboard controls. Less-used legacy descriptions deliberately use the per-key English fallback until they receive native-speaker review.

## Adding A Locale

1. Add a locale JSON file whose culture prefix is lowercase, for example `de-de.json`. A locale can provide resources in one file or alongside the feature that owns them.
2. Translate values only. Keep every JSON key, placeholder such as `{0}`, newline, unit, and escape sequence unchanged.
3. Missing keys intentionally fall back to English. This permits a useful UI translation while detailed legacy text is reviewed by a native speaker.
4. Use a native speaker to review rally terminology, game setup instructions, warnings, and accessibility text.
5. Run `dotnet test src\ZTMZ.PacenoteTool.Tests\ZTMZ.PacenoteTool.Tests.csproj --no-restore`.

The test suite verifies that every translated key exists in the English resources. The runtime falls back to English per key, which keeps built-in, extension, and community language packs usable while their translations are updated.

## Scope

Interface text and dashboard labels are localized through these JSON resources. Co-driver audio packages are independent assets and should be supplied or reviewed by native speakers; translating the interface does not translate spoken pacenotes.
