#!/usr/bin/env bash
# Interactive macOS builder. Run on macOS: bash installer/build-macos.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/CodexLimitWidget.App/CodexLimitWidget.App.csproj"
icon="$root/CodexLimitWidget.icns"

choose() {
  local prompt="$1" default="$2" answer
  read -r -p "$prompt [$default]: " answer
  printf '%s' "${answer:-$default}"
}

build_one() {
  local framework="$1" rid="$2" version="$3" stage="$root/publish/$rid-$framework/app"
  rm -rf "$stage"
  dotnet publish "$project" -c Release -f "$framework" -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:Version="$version" -o "$stage" >&2
  printf '%s' "$stage"
}

create_app() {
  local source="$1"
  local name="$2"
  local version="$3"
  local minimum_system="$4"
  local app="$root/dist/$name.app"
  rm -rf "$app"
  install -d "$app/Contents/MacOS" "$app/Contents/Resources"
  # Avalonia self-contained output includes native libraries next to the apphost.
  # Copy the complete publish directory; copying only the executable makes the
  # bundle silently fail at launch when dyld cannot find those dependencies.
  ditto "$source" "$app/Contents/MacOS"
  mv "$app/Contents/MacOS/CodexLimitWidget.App" "$app/Contents/MacOS/CodexLimitWidget"
  chmod 0755 "$app/Contents/MacOS/CodexLimitWidget"
  install -m 0644 "$icon" "$app/Contents/Resources/CodexLimitWidget.icns"
  cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>CodexLimitWidget</string>
  <key>CFBundleIconFile</key><string>CodexLimitWidget</string>
  <key>CFBundleIdentifier</key><string>io.github.miaowcham.codexlimitwidget</string>
  <key>CFBundleName</key><string>Codex Limit Widget</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSUIElement</key><true/>
  <key>LSMinimumSystemVersion</key><string>$minimum_system</string>
</dict></plist>
EOF
  codesign --force --deep --sign - "$app"
  echo "App 已生成并完成 ad-hoc 签名：$app"
}

echo 'Codex Limit Widget — macOS 构建器'
echo '选择目标 .NET：'
echo '  1) net10.0（默认；正式版本，macOS 14+）'
echo '  2) net8.0（macOS 12 实验性兼容版本）'
framework_choice="$(choose '输入 1/2' 1)"
case "$framework_choice" in
  1) framework=net10.0; minimum_system=14.0 ;;
  2) framework=net8.0; minimum_system=12.0 ;;
  *) echo '无效的框架选择。' >&2; exit 2 ;;
esac

echo '选择 CPU 架构：'
echo '  1) Apple Silicon（osx-arm64）'
echo '  2) Intel（osx-x64）'
architecture_choice="$(choose '输入 1/2' 1)"
case "$architecture_choice" in 1|2) ;; *) echo '无效的架构选择。' >&2; exit 2 ;; esac

echo '选择输出：'
echo '  1) .app'
echo '  2) .app + ZIP'
output_choice="$(choose '输入 1/2' 2)"
case "$output_choice" in 1|2) ;; *) echo '无效的输出选择。' >&2; exit 2 ;; esac

version="$(choose '版本号' "$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n1)")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo '版本号必须为 x.y.z。' >&2; exit 2; }
command -v dotnet >/dev/null || { echo '未找到 dotnet。' >&2; exit 1; }
command -v codesign >/dev/null || { echo '本脚本必须在 macOS 上运行。' >&2; exit 1; }
[[ -f "$icon" ]] || { echo "未找到 $icon。请先生成项目根目录的 .icns 图标。" >&2; exit 1; }
mkdir -p "$root/dist"

build_and_package() {
  local rid="$1" label="$2" stage
  stage="$(build_one "$framework" "$rid" "$version")"
  create_app "$stage" "CodexLimitWidget-${version}-${label}" "$version" "$minimum_system"
  if [[ "$output_choice" == 2 ]]; then
    ditto -c -k --sequesterRsrc --keepParent "$root/dist/CodexLimitWidget-${version}-${label}.app" "$root/dist/CodexLimitWidget-${version}-${label}.zip"
    echo "ZIP 已生成：$root/dist/CodexLimitWidget-${version}-${label}.zip"
  fi
}

case "$architecture_choice" in
  1) build_and_package osx-arm64 osx-arm64 ;;
  2) build_and_package osx-x64 osx-x64 ;;
esac
