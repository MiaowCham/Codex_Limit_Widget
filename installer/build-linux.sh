#!/usr/bin/env bash
# Interactive Linux builder. Run on a Linux host: bash installer/build-linux.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/CodexLimitWidget.App/CodexLimitWidget.App.csproj"
source "$root/installer/versioning.sh"

choose() {
  local prompt="$1" default="$2" answer
  read -r -p "$prompt [$default]: " answer
  printf '%s' "${answer:-$default}"
}

echo 'Codex Limit Widget — Linux 构建器'
echo '选择目标框架：'
echo '  1) net10.0（默认，正式版本）'
echo '  2) net8.0（兼容构建）'
framework_choice="$(choose '输入 1/2' 1)"
case "$framework_choice" in
  1) framework=net10.0 ;;
  2) framework=net8.0 ;;
  *) echo '无效的框架选择。' >&2; exit 2 ;;
esac

echo '选择 CPU 架构：'
echo '  1) x64（Intel / AMD）'
echo '  2) arm64（ARM64）'
architecture_choice="$(choose '输入 1/2' 1)"
case "$architecture_choice" in
  1) rid=linux-x64; deb_arch=amd64 ;;
  2) rid=linux-arm64; deb_arch=arm64 ;;
  *) echo '无效的架构选择。' >&2; exit 2 ;;
esac

echo '选择输出：'
echo '  1) 自包含单文件二进制'
echo '  2) DEB 安装包'
echo '  3) 两者'
output_choice="$(choose '输入 1/2/3' 3)"
case "$output_choice" in 1|2|3) ;; *) echo '无效的输出选择。' >&2; exit 2 ;; esac

version="$(choose '版本号' "$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n1)")"
resolve_codex_version "$version" "${INFORMATIONAL_VERSION:-}"
version="$CODEX_VERSION"
product_version="$CODEX_PRODUCT_VERSION"
informational_version="$CODEX_INFORMATIONAL_VERSION"
command -v dotnet >/dev/null || { echo '未找到 dotnet。' >&2; exit 1; }
if [[ "$output_choice" != 1 ]]; then command -v dpkg-deb >/dev/null || { echo '构建 DEB 需要 dpkg-deb。' >&2; exit 1; }; fi

stage="$root/publish/$rid/app"
package="$root/dist/codex-limit-widget_${version}_${deb_arch}"
rm -rf "$stage" "$package" "$package.deb"

dotnet publish "$project" -c Release -f "$framework" -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version="$version" -p:InformationalVersion="$informational_version" \
  -p:AssemblyVersion="$product_version" -p:FileVersion="$product_version" -o "$stage"
echo "二进制已生成：$stage/CodexLimitWidget.App"

if [[ "$output_choice" != 1 ]]; then
  install -d "$package/DEBIAN" "$package/usr/bin" "$package/usr/share/applications" "$package/usr/share/icons/hicolor/256x256/apps"
  install -m 0755 "$stage/CodexLimitWidget.App" "$package/usr/bin/codex-limit-widget"
  install -m 0644 "$root/icon.png" "$package/usr/share/icons/hicolor/256x256/apps/codex-limit-widget.png"
  printf 'Package: codex-limit-widget\nVersion: %s\nSection: utils\nPriority: optional\nArchitecture: %s\nMaintainer: CodexLimitWidget Contributors\nDescription: Compact Codex rate-limit widget\n' "$version" "$deb_arch" > "$package/DEBIAN/control"
  printf '[Desktop Entry]\nType=Application\nName=Codex Limit Widget\nExec=codex-limit-widget\nIcon=codex-limit-widget\nCategories=Utility;\nTerminal=false\n' > "$package/usr/share/applications/codex-limit-widget.desktop"
  dpkg-deb --build --root-owner-group "$package" "$package.deb"
  echo "DEB 已生成：$package.deb"
fi
