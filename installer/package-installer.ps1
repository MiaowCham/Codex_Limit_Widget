param(
    [switch]$SkipBuild,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$buildScript = Join-Path $PSScriptRoot 'build.ps1'

if ($SkipBuild) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    if (-not $Version) {
        $xml = [xml](Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'CodexLimitWidget.csproj'))
        $Version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    }
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if (-not $iscc) { $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path $iscc)) { throw '未找到 ISCC.exe。请先安装 Inno Setup 6。' }
    $languageFile = Join-Path (Split-Path $iscc) 'Languages\ChineseSimplified.isl'
    $issFile = if (Test-Path $languageFile) { 'CodexLimitWidget.iss' } else { 'CodexLimitWidget.CI.iss' }
    & $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot $issFile)
    if ($LASTEXITCODE -ne 0) { throw "ISCC 构建失败，退出码 $LASTEXITCODE。" }
} else {
    & $buildScript -Choice 4 -Version $Version
}
