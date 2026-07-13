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
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if (-not $iscc) { $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path $iscc)) { throw '未找到 ISCC.exe。请先安装 Inno Setup 6。' }
    & (Join-Path $PSScriptRoot 'download-inno-languages.ps1')
    & $iscc "/DMyAppVersion=$($resolved.Version)" "/DMyAppProductVersion=$($resolved.ProductVersion)" (Join-Path $PSScriptRoot 'CodexLimitWidget.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC 构建失败，退出码 $LASTEXITCODE。" }
} else {
    & $buildScript -Choice 4 -Version $resolved.Version -InformationalVersion $resolved.InformationalVersion
}
