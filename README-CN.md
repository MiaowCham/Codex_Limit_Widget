<div align="center">
<img src="icon.png" width="20%" alt="Codex Limit Widget 图标" style="margin-bottom: -20px;"/>

# Codex Limit Widget

[English](README.md) / 简体中文

[![Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-orange.svg)](https://github.com/MiaowCham/Codex_Limit_Widget/blob/main/LICENSE)
[![Static Badge](https://img.shields.io/badge/Languages-C%23-blue.svg)](https://github.com/search?q=repo%3AMiaowCham%2FCodex_Limit_Widget++language%3AC%23&type=code)
[![Github Release](https://img.shields.io/github/v/release/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/releases)
[![GitHub Actions](https://img.shields.io/github/actions/workflow/status/MiaowCham/Codex_Limit_Widget/.github/workflows/build.yml)](https://github.com/MiaowCham/Codex_Limit_Widget/actions/workflows/build.yml)
[![GitHub last commit](https://img.shields.io/github/last-commit/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/commits/main)

一个用于查看 Codex 用量与重置时间的轻量跨平台桌面组件。

</div>

> [!NOTE]
> 本项目由 Codex 提供支持

## 功能

- 显示主要限额、周限额、重置时间、Credits 和当前套餐
- 支持定时刷新与手动刷新
- 支持窗口拖动和置顶切换
- 支持系统托盘菜单：显示/隐藏窗口、切换置顶、刷新、打开项目主页和退出
- 提供 `status` 与 `watch` 两种 CLI 查询模式
- 在 Windows、Linux 和 macOS 上构建并测试

## 运行要求

跨平台桌面端基于 Avalonia 12。正式构建使用 .NET 10；App、CLI、Core 和测试项目在使用较旧 SDK 时也可回退到 `net8.0`。

程序通过 `codex app-server` 读取限额，因此要求：

- 已安装 Codex CLI
- `codex` 命令可通过 `PATH` 找到
- Codex CLI 已完成登录

IDE 插件通常不会自动提供独立的 `codex` CLI，必要时请单独安装。

## 使用

### 安装构建版

前往 [Releases](https://github.com/MiaowCham/Codex_Limit_Widget/releases) 或 GitHub Actions 构建产物下载：

- Windows x64 Slim 安装包：体积较小，依赖 .NET 10 x64 Runtime
- Windows x64 Full 安装包：包含运行时，无需另行安装 .NET
- Linux x64：自包含可执行文件和 DEB 安装包
- macOS：Apple Silicon 与 Intel 的 ad-hoc 签名 `.app`

macOS 产物未进行 Apple Developer ID 签名或公证，首次运行时可能需要在系统设置中手动允许。

### 启动桌面组件

```powershell
dotnet run --project CodexLimitWidget.App -- --interval 60
```

`--interval` 的单位为秒，可设置为 `1` 至 `86400`，默认值为 `60`。

程序默认跟随系统显示语言，也可通过 `--language` 参数为单次启动指定语言：

```powershell
dotnet run --project CodexLimitWidget.App -- --language JP
```

目前支持英文、简体中文、繁体中文和日文；未提供翻译的语言会回退到英文。`JP` 是日文 `ja-JP` 的简写。

### 查询一次

```powershell
dotnet run --project CodexLimitWidget.Cli -- status --language en-US
```

### 持续查询

```powershell
dotnet run --project CodexLimitWidget.Cli -- watch --interval 60 --language zh-Hant
```

按 `Ctrl+C` 停止持续查询。

## 从源码构建

推荐安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
dotnet build CodexLimitWidget.slnx -c Release
dotnet test CodexLimitWidget.slnx -c Release --no-build
```

### Windows x64

生成自包含单文件程序：

```powershell
dotnet publish CodexLimitWidget.App/CodexLimitWidget.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish/win-x64/app
```

安装 Inno Setup 6 后，可通过交互菜单选择生成 Slim、Full 或两种 Windows 安装包：

```powershell
./installer/build.ps1
```

如需非交互地同时构建两种安装包：

```powershell
./installer/build.ps1 -Package Both -Version 1.0.0
```

`-Package` 支持 `Slim`、`Full`、`Both`。Slim 是体积较小的框架依赖版本；Full 是包含运行时的完整版本。

版本输入支持 `x.y.z`、`x.y.z.w`，以及带字母数字后缀的形式，例如 `1.2.3-preview` 或 `1.2.3.4-preview`。完整输入会写入 `InformationalVersion`；程序集、文件和安装器资源版本使用 `-` 前的数字部分，三段版本会补 `.0`。CI 构建只会在 `InformationalVersion` 末尾再追加一个 `-` 和 7 位提交短 hash。.NET 会把信息版本映射为 Windows PE 的 `ProductVersion`；严格数字版本仍保存在 Assembly/File 版本和安装器 `VersionInfoVersion` 中。

两种安装包是同一应用的替代安装介质，共用安装目录和卸载项，安装其中一种会替换另一种：

- `CodexLimitWidget-<版本>-Windows-x64-Slim-Setup.exe` 体积较小，需要匹配的 .NET x64 Runtime。
- `CodexLimitWidget-<版本>-Windows-x64-Full-Setup.exe` 已包含运行时，无需单独安装 .NET。

### Linux

交互式脚本支持 x64/ARM64、.NET 10/.NET 8、自包含程序和 DEB 安装包：

```bash
bash installer/build-linux.sh
```

构建 DEB 还需要 `dpkg-deb`。

### macOS

交互式脚本支持 Apple Silicon/Intel、.NET 10/.NET 8，并可生成 `.app` 与 ZIP：

```bash
bash installer/build-macos.sh
```

.NET 10 为默认构建目标，脚本同时提供 .NET 8 兼容构建。应用包不在 `Info.plist` 中声明最低 macOS 版本；生成应用包需要项目根目录中的 `CodexLimitWidget.icns` 和系统自带的 `codesign`。Apple 的 bundle 版本字段保持三段纯数字，完整信息版本保存在 `CFBundleGetInfoString` 和 `CodexInformationalVersion` 中。

## 日志

- Windows：`%LOCALAPPDATA%\CodexLimitWidget\Logs\widget.log`
- Linux：`$XDG_STATE_HOME/CodexLimitWidget/Logs/widget.log`
- Linux 未设置 `XDG_STATE_HOME`：`~/.local/state/CodexLimitWidget/Logs/widget.log`
- macOS：`~/Library/Logs/CodexLimitWidget/widget.log`

## CI 与发布

GitHub Actions 会在 Windows、Ubuntu 和 macOS 上执行 Release 构建与测试。推送 `v*` 标签或手动触发工作流时，还会生成：

- Windows x64 Slim 与 Full 安装程序
- Linux x64 自包含程序和 DEB
- macOS Apple Silicon 与 Intel `.app`

## 许可证

本项目采用 [Apache License 2.0](LICENSE)。
