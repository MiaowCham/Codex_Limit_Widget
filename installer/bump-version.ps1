param(
    [string]$Version,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
if (-not $Version) { $Version = Read-Host '输入新版本号（x.y.z）' }
$Version = $Version.Trim().TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为 x.y.z，例如 0.2.0。' }
$ProductVersion = "$Version.0"
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$replacements = @(
    @{ Path = 'Directory.Build.props'; Patterns = @(
        @{ Find = '<Version>[^<]+</Version>'; Replace = "<Version>$Version</Version>" },
        @{ Find = '<AssemblyVersion>[^<]+</AssemblyVersion>'; Replace = "<AssemblyVersion>$ProductVersion</AssemblyVersion>" },
        @{ Find = '<FileVersion>[^<]+</FileVersion>'; Replace = "<FileVersion>$ProductVersion</FileVersion>" },
        @{ Find = '<InformationalVersion>[^<]+</InformationalVersion>'; Replace = "<InformationalVersion>$ProductVersion</InformationalVersion>" }) },
    @{ Path = 'CodexLimitWidget.csproj'; Patterns = @(
        @{ Find = '<Version>[^<]+</Version>'; Replace = "<Version>$Version</Version>" },
        @{ Find = '<AssemblyVersion>[^<]+</AssemblyVersion>'; Replace = "<AssemblyVersion>$ProductVersion</AssemblyVersion>" },
        @{ Find = '<FileVersion>[^<]+</FileVersion>'; Replace = "<FileVersion>$ProductVersion</FileVersion>" },
        @{ Find = '<InformationalVersion>[^<]+</InformationalVersion>'; Replace = "<InformationalVersion>$ProductVersion</InformationalVersion>" }) },
    @{ Path = 'installer/CodexLimitWidget.iss'; Patterns = @(@{ Find = '(?m)^\s*#define MyAppVersion "[^"]+"'; Replace = "  #define MyAppVersion `"$Version`"" }) },
    @{ Path = 'installer/build-windows.ps1'; Patterns = @(@{ Find = '(?m)^(param\(\[string\]\$Version = )"[^"]+"'; Replace = "`$1`"$Version`"" }) },
    @{ Path = 'README.md'; Patterns = @(@{ Find = '(?m)^(\./installer/build-windows\.ps1 -Version )\d+\.\d+\.\d+'; Replace = "`$1$Version" }) },
    @{ Path = 'README-CN.md'; Patterns = @(@{ Find = '(?m)^(\./installer/build-windows\.ps1 -Version )\d+\.\d+\.\d+'; Replace = "`$1$Version" }) },
    @{ Path = '.github/workflows/build.yml'; Patterns = @(@{ Find = '(?m)^(\s*default:) "[^"]+"'; Replace = "`$1 `"$Version`"" }) }
)

foreach ($entry in $replacements) {
    $path = Join-Path $root $entry.Path
    $content = [IO.File]::ReadAllText($path)
    foreach ($replacement in $entry.Patterns) {
        if (-not [regex]::IsMatch($content, $replacement.Find)) { throw "未能在 $($entry.Path) 中找到版本字段：$($replacement.Find)" }
        $updated = [regex]::Replace($content, $replacement.Find, $replacement.Replace)
        $content = $updated
    }
    if ($WhatIf) { Write-Host "Would update $($entry.Path)" -ForegroundColor Yellow }
    else { [IO.File]::WriteAllText($path, $content) }
}

Write-Host "版本将更新为 $Version（产品版本 $ProductVersion）。" -ForegroundColor Green
