# Codex Limit Widget

一个更轻的 Codex 限额查看工具，直接走官方 `codex app-server` 协议读取数据，当前实现改成了 `.NET + WinForms`，目标是比 `Python + tkinter` 更适合常驻挂桌面。

支持：
- CLI 单次查看
- CLI 持续刷新
- Windows 桌面悬浮窗

## 为什么改成 .NET

- 比 Python 后台常驻更规整，适合做常驻小组件
- 使用系统原生 WinForms，视觉和交互比 `tkinter` 更稳定
- 继续直连 `account/rateLimits/read`，不解析 TUI 文本

## 使用

### 终端看一次

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

安装包输出到 `dist`，发布文件输出到 `publish`。安装包需要目标电脑具有 .NET 10 Desktop Runtime。

## 当前界面说明

- 左键拖动窗口
- 双击窗口立即刷新
- 托盘双击恢复窗口
- 托盘右键可刷新或退出

## 备注

- 当前主信息来自官方 app-server 的 `account/rateLimits/read`
- “精确多久会耗尽”并不是协议直接返回的字段，所以现在显示的是使用率和重置倒计时
- 如果后续要再降占用，可以继续做 `publish` 单文件发布，对日常使用会更舒服
