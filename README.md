<div align="center">
<img src="icon.png" width="20%" alt="icon" style="margin-bottom: -20px;"/>

# Codex Limit Widget macOS12 Dev
[![MIT](https://img.shields.io/badge/License-Apache%202.0-orange.svg)](https://github.com/MiaowCham/Codex_Limit_Widget/blob/main/LICENSE)
[![Static Badge](https://img.shields.io/badge/Languages-C%23-blue.svg)](https://github.com/search?q=repo%3AMiaowCham%2FCodex_Limit_Widget++language%3AC%23&type=code)
[![Github Release](https://img.shields.io/github/v/release/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/releases)
[![GitHub Actions](https://img.shields.io/github/actions/workflow/status/MiaowCham/Codex_Limit_Widget/.github/workflows/build.yml)](https://github.com/MiaowCham/Codex_Limit_Widget/actions/workflows/build.yml)
[![GitHub last commit](https://img.shields.io/github/last-commit/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/commits/main)

一个轻量的 Codex 限额查看小组件，**macOS12 测试版**

</div>

>[!note]
>本项目使用AI生成。  
>This project uses AI generation.

## TODO

- [x] 确认各系统构建版本运行情况
  - [x] Windows
  - [x] Linux (WSL2)
  - [x] macOS
- [ ] 构建 Linux、macOS 软件包
- [ ] 适配 Inno Setup 打包
- [ ] 适配各系统托盘图标
- [ ] 增加全平台 CI 构建流程

## 依赖与当前限制

跨平台版本使用 .NET 8 + Avalonia 12，并要求 `codex` CLI 可通过 `PATH` 找到。  
IDE 插件不会自动提供 `codex` CLI；Linux/macOS 用户需要单独安装 CLI。

当前迁移状态：Windows、Linux、macOS 均可生成 RID 产物，但 Linux/macOS 尚未组装原生安装包或 `.app`，需要手动运行二进制文件。  
现有 Inno Setup 脚本只适用于旧版 Windows/WinForms 项目，尚未适配 Avalonia 跨平台版本。

### 安装 .NET 8

官方安装入口：[Install .NET](https://dotnet.microsoft.com/download/dotnet/8.0)。

Windows（PowerShell）：

```powershell
winget install Microsoft.DotNet.SDK.8
```

Ubuntu/Debian 等 Linux，也可以使用官方脚本进行用户级安装：

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 8.0
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

macOS：

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh

./dotnet-install.sh \
  --channel 8.0 \
  --install-dir "$HOME/.dotnet"

echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> ~/.zshrc
echo 'export PATH="$DOTNET_ROOT:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

也可以从官方页面下载对应的 macOS Arm64 或 x64 SDK。当前项目的 .NET 10 官方支持范围最低为 macOS 14；macOS 12 Monterey 不属于本项目的正式支持目标。

## 使用

### 使用构建版

~~前往 Release 页或 CI 构建中获取安装包，安装后使用。~~  
跨平台版暂未打包和提供软件包，请直接直接 dotnet 运行，或自行构建二进制文件。

### 终端查看

```powershell
 dotnet run --project CodexLimitWidget.Cli -- status
```

### 终端持续刷新

```powershell
 dotnet run --project CodexLimitWidget.Cli -- watch --interval 60
```

### 启动悬浮窗

```powershell
dotnet run --project CodexLimitWidget.App -- --interval 60
```

## 各平台构建与启动

### Windows x64

```powershell
dotnet publish CodexLimitWidget.App/CodexLimitWidget.App.csproj `
  -c Release -f net8.0 -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish/windows-x64
.\publish\windows-x64\CodexLimitWidget.App.exe --interval 60
```

日志：`%LOCALAPPDATA%\CodexLimitWidget\Logs\widget.log`。

### Linux x64

```bash
dotnet publish CodexLimitWidget.App/CodexLimitWidget.App.csproj \
  -c Release -f net8.0 -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish/linux-x64
chmod +x publish/linux-x64/CodexLimitWidget.App
./publish/linux-x64/CodexLimitWidget.App --interval 60
```

Linux 当前没有 AppImage、DEB 或 RPM 安装包，需要手动运行二进制文件。日志默认位于：

```text
$XDG_STATE_HOME/CodexLimitWidget/Logs/widget.log
```

未设置 `XDG_STATE_HOME` 时使用 `~/.local/state/CodexLimitWidget/Logs/widget.log`。

### macOS Apple Silicon / Intel

```bash
# Apple Silicon
dotnet publish CodexLimitWidget.App/CodexLimitWidget.App.csproj \
  -c Release -f net8.0 -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o publish/osx-arm64

chmod +x publish/osx-arm64/CodexLimitWidget.App
./publish/osx-arm64/CodexLimitWidget.App --interval 60

# Intel
dotnet publish CodexLimitWidget.App/CodexLimitWidget.App.csproj \
  -c Release -f net8.0 -r osx-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish/osx-x64

chmod +x publish/osx-arm64/CodexLimitWidget.App
./publish/osx-x64/CodexLimitWidget.App --interval 60
```

macOS 当前没有 `.app`、DMG、签名或 notarization 产物，需要手动运行发布目录中的二进制文件。日志位于：`~/Library/Logs/CodexLimitWidget/widget.log`。

macOS 正式分发前还需要组装 `Contents/MacOS`、`Contents/Resources` 和 `Info.plist`，然后进行签名与公证。

## Release 与安装包

当前稳定版使用 Inno Setup 6 生成 Windows 安装包；跨平台迁移中的 App 与 CLI 可独立构建：

```powershell
dotnet build CodexLimitWidget.slnx -c Release
dotnet test CodexLimitWidget.slnx -c Release
dotnet publish CodexLimitWidget.App -c Release -r win-x64 --self-contained true
```

GitHub Actions 会在 Windows、Ubuntu 与 macOS 上执行 restore、build 和测试；标签构建另会生成对应 RID 的 App 与 CLI 自包含产物。Linux/macOS 安装包尚未完成，Inno Setup 脚本也未适配此跨平台版本。

旧版打包命令：

```powershell
# Release 发布（框架依赖、不含 PDB）
.\installer\build.ps1 -Choice 2

# Release 单文件发布（框架依赖、不含 PDB）
.\installer\build.ps1 -Choice 3

# 发布并生成安装包
.\installer\build.ps1 -Choice 4
```

安装包输出到 `dist`，发布文件输出到 `publish`。

## GUI 说明

- 左键拖动窗口
- 使用“刷新”按钮手动刷新，或通过 `--interval` 设置定时刷新
- 使用“置顶”按钮切换窗口置顶
- 首期不提供系统托盘、双击刷新和自动启动
