param(
    [ValidateSet('Slim', 'Full', 'Both')]
    [Alias('Mode')]
    [string]$Package,
    [string]$Version,
    [string]$InformationalVersion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) {
    $stored = Get-CodexStoredVersion -RepositoryRoot $repoRoot
    $Version = $stored.Version
    if (-not $InformationalVersion) { $InformationalVersion = $stored.InformationalVersion }
}
$resolved = Resolve-CodexVersion -Version $Version -InformationalVersion $InformationalVersion

if (-not $Package) {
    Write-Host ''
    Write-Host 'Codex Limit Widget - Windows 安装包构建' -ForegroundColor Cyan
    Write-Host '构建前提：.NET SDK、Inno Setup 6，以及可用的网络连接（下载安装器语言文件）。'
    Write-Host ''
    Write-Host '  1) Slim  - 精简安装包；体积较小，目标电脑需要匹配的 .NET x64 Runtime'
    Write-Host '  2) Full  - 完整安装包；包含 .NET Runtime，目标电脑无需预装 .NET'
    Write-Host '  3) Both  - 同时生成 Slim 和 Full（默认）'
    Write-Host ''
    $selection = Read-Host '请选择 1/2/3，直接回车使用默认值 3'
    if ([string]::IsNullOrWhiteSpace($selection)) { $selection = '3' }
    $Package = switch ($selection) {
        '1' { 'Slim' }
        '2' { 'Full' }
        '3' { 'Both' }
        default { throw "无效选项 '$selection'。请输入 1、2 或 3。" }
    }
}

Write-Host ''
Write-Host "构建类型：$Package" -ForegroundColor DarkCyan
Write-Host "软件版本：$($resolved.Version)"
Write-Host "信息版本：$($resolved.InformationalVersion)"
Write-Host "输出目录：$(Join-Path $repoRoot 'dist')"
Write-Host ''

& (Join-Path $PSScriptRoot 'package-windows.ps1') `
    -Package $Package `
    -Version $resolved.Version `
    -InformationalVersion $resolved.InformationalVersion

Write-Host ''
Write-Host 'Windows 安装包构建完成。' -ForegroundColor Green
