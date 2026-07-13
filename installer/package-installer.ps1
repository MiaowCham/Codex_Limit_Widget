param(
    [switch]$SkipBuild,
    [string]$Version,
    [string]$InformationalVersion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')
$buildScript = Join-Path $PSScriptRoot 'build.ps1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $Version) {
    $stored = Get-CodexStoredVersion -RepositoryRoot $repoRoot
    $Version = $stored.Version
    if (-not $InformationalVersion) { $InformationalVersion = $stored.InformationalVersion }
}
$resolved = Resolve-CodexVersion -Version $Version -InformationalVersion $InformationalVersion

if ($SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-windows.ps1') -Version $resolved.Version -InformationalVersion $resolved.InformationalVersion -SkipPublish
    if ($LASTEXITCODE -ne 0) { throw "双安装包构建失败，退出码 $LASTEXITCODE。" }
} else {
    & $buildScript -Choice 4 -Version $resolved.Version -InformationalVersion $resolved.InformationalVersion
}
