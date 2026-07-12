<div align="center">
<img src="icon.png" width="20%" alt="icon" style="margin-bottom: -20px;"/>

# Codex Limit Widget
[![MIT](https://img.shields.io/badge/License-Apache%202.0-orange.svg)](https://github.com/MiaowCham/Codex_Limit_Widget/blob/main/LICENSE)
[![Static Badge](https://img.shields.io/badge/Languages-C%23-blue.svg)](https://github.com/search?q=repo%3AMiaowCham%2FCodex_Limit_Widget++language%3AC%23&type=code)
[![Github Release](https://img.shields.io/github/v/release/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/releases)
[![GitHub Actions](https://img.shields.io/github/actions/workflow/status/MiaowCham/Codex_Limit_Widget/.github/workflows/build.yml)](https://github.com/MiaowCham/Codex_Limit_Widget/actions/workflows/build.yml)
[![GitHub last commit](https://img.shields.io/github/last-commit/MiaowCham/Codex_Limit_Widget)](https://github.com/MiaowCham/Codex_Limit_Widget/commits/main)

一个轻量的 Codex 限额查看小组件

</div>

>[!note]
>本项目使用AI生成。  
>This project uses AI generation.

## 依赖

安装包需要目标电脑具有 .NET 10 Desktop Runtime。

## 使用

### 使用构建版

前往 Release 页或 CI 构建中获取安装包，安装后使用。

### 终端查看

```powershell
 dotnet run -- status
```

### 终端持续刷新

```powershell
 dotnet run -- watch --interval 60
```

### 启动悬浮窗

```powershell
 dotnet run -- gui --interval 60
```

## Release 与安装包

项目使用 Inno Setup 6 生成 Windows 安装包：

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
- 双击窗口立即刷新
- 托盘双击恢复窗口
- 托盘右键可刷新或退出
