#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
work_root="${ZTMZ_APPIMAGE_WORK_DIR:-$repo_root/artifacts/appimage}"
output_dir="${ZTMZ_APPIMAGE_OUTPUT_DIR:-$work_root/output}"
appdir="$work_root/ZTMZ_PacenoteTool.AppDir"
publish_dir="$appdir/usr/lib/ztmz"
resource_dir="$appdir/usr/share/ztmz"
configuration="${ZTMZ_BUILD_CONFIGURATION:-Release}"
runtime_identifier="${ZTMZ_RUNTIME_IDENTIFIER:-linux-x64}"
appimage_arch="${ARCH:-x86_64}"
package_data_home="${ZTMZ_PACKAGE_DATA_HOME:-$HOME/.local/share/ztmz}"
default_codriver_source="${ZTMZ_DEFAULT_CODRIVER_SOURCE:-$package_data_home/codrivers/default}"
default_profile_source="${ZTMZ_DEFAULT_PROFILE_SOURCE:-$package_data_home/profiles/default}"

if [[ "$runtime_identifier" != "linux-x64" || "$appimage_arch" != "x86_64" ]]; then
    echo "Only the linux-x64 AppImage is currently supported." >&2
    exit 64
fi

if [[ "$appdir" != "$work_root/"* ]]; then
    echo "Refusing to clean an AppDir outside the configured work directory." >&2
    exit 64
fi

version="${ZTMZ_APPIMAGE_VERSION:-dev}"
wpf_project="$repo_root/src/ZTMZ.PacenoteTool.WpfGUI/ZTMZ.PacenoteTool.WpfGUI.csproj"
if [[ "$version" == "dev" && -f "$wpf_project" ]]; then
    version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' \
        "$wpf_project" | head -n 1)"
fi
version="${version:-dev}"

rm -rf -- "$appdir"
mkdir -p -- "$publish_dir/games" "$resource_dir/lang" "$output_dir"

if [[ ! -f "$default_codriver_source/info.json" ]]; then
    echo "Default co-driver package was not found: $default_codriver_source" >&2
    exit 66
fi
if [[ ! -d "$default_profile_source" ]]; then
    echo "Default profile was not found: $default_profile_source" >&2
    exit 66
fi

dotnet publish \
    "$repo_root/src/ZTMZ.PacenoteTool.Linux/ZTMZ.PacenoteTool.Linux.csproj" \
    --configuration "$configuration" \
    --runtime "$runtime_identifier" \
    --self-contained true \
    --output "$publish_dir"

dotnet build \
    "$repo_root/src/ZTMZ.PacenoteTool.Codemasters/ZTMZ.PacenoteTool.Codemasters.csproj" \
    --framework net10.0 \
    --configuration "$configuration" \
    --runtime "$runtime_identifier"

codemasters_dll="$repo_root/src/ZTMZ.PacenoteTool.Codemasters/bin/$configuration/net10.0/$runtime_identifier/ZTMZ.PacenoteTool.Codemasters.dll"
install -Dm0644 -- "$codemasters_dll" \
    "$publish_dir/games/ZTMZ.PacenoteTool.Codemasters.dll"
install -Dm0644 -- "$repo_root/src/ZTMZ.PacenoteTool.Base/data.zdb" \
    "$resource_dir/data.zdb"
install -m0644 -- "$repo_root"/src/ZTMZ.PacenoteTool.I18N/lang/*.json \
    "$resource_dir/lang/"
install -m0644 -- "$repo_root"/src/ZTMZ.PacenoteTool.Codemasters/lang/*.json \
    "$resource_dir/lang/"
mkdir -p -- "$resource_dir/codrivers/default" "$resource_dir/profiles/default"
cp -a -- "$default_codriver_source/." "$resource_dir/codrivers/default/"
cp -a -- "$default_profile_source/." "$resource_dir/profiles/default/"

install -Dm0755 -- "$script_dir/AppRun" "$appdir/AppRun"
install -Dm0644 -- "$script_dir/com.ztmz.PacenoteTool.desktop" \
    "$appdir/com.ztmz.PacenoteTool.desktop"
install -Dm0644 -- "$script_dir/com.ztmz.PacenoteTool.desktop" \
    "$appdir/usr/share/applications/com.ztmz.PacenoteTool.desktop"
install -Dm0644 -- "$script_dir/com.ztmz.PacenoteTool.appdata.xml" \
    "$appdir/usr/share/metainfo/com.ztmz.PacenoteTool.appdata.xml"
icon_source="${ZTMZ_APPIMAGE_ICON:-$script_dir/ztmz.png}"
if [[ ! -f "$icon_source" ]]; then
    echo "App icon not found. Set ZTMZ_APPIMAGE_ICON to a PNG file." >&2
    exit 66
fi
install -Dm0644 -- "$icon_source" \
    "$appdir/usr/share/icons/hicolor/512x512/apps/ztmz.png"
ln -s -- "usr/share/icons/hicolor/512x512/apps/ztmz.png" "$appdir/ztmz.png"

required_paths=(
    "$publish_dir/ztmz"
    "$publish_dir/games/ZTMZ.PacenoteTool.Codemasters.dll"
    "$publish_dir/wwwroot/index.html"
    "$publish_dir/dashboards"
    "$publish_dir/Assets/PacenoteIcons"
    "$resource_dir/data.zdb"
    "$resource_dir/codrivers/default/info.json"
    "$resource_dir/profiles/default"
    "$resource_dir/lang/en-us.json"
    "$resource_dir/lang/en-us.cm.json"
)
for required_path in "${required_paths[@]}"; do
    if [[ ! -e "$required_path" ]]; then
        echo "Required AppImage payload is missing: $required_path" >&2
        exit 1
    fi
done

if [[ "${ZTMZ_APPDIR_ONLY:-0}" == "1" ]]; then
    echo "AppDir ready: $appdir"
    exit 0
fi

appimagetool="${APPIMAGETOOL:-}"
if [[ -z "$appimagetool" ]]; then
    appimagetool="$(command -v appimagetool || true)"
fi
if [[ -z "$appimagetool" || ! -x "$appimagetool" ]]; then
    echo "appimagetool was not found. Set APPIMAGETOOL or use ZTMZ_APPDIR_ONLY=1." >&2
    exit 69
fi

output_path="$output_dir/ZTMZClub_PacenoteTool_${version}-${appimage_arch}.AppImage"
ARCH="$appimage_arch" VERSION="$version" "$appimagetool" "$appdir" "$output_path"

echo "AppImage ready: $output_path"
