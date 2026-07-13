$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')

function Assert-Equal([string]$Expected, [string]$Actual, [string]$Label) {
    if ($Expected -ne $Actual) { throw "$Label：预期 $Expected，实际 $Actual。" }
}

$cases = @(
    @{ Input = '1.2.3'; Product = '1.2.3.0'; Info = '1.2.3'; Ci = '1.2.3-abcdef1' },
    @{ Input = '1.2.3.4'; Product = '1.2.3.4'; Info = '1.2.3.4'; Ci = '1.2.3.4-abcdef1' },
    @{ Input = '1.2.3-abcd'; Product = '1.2.3.0'; Info = '1.2.3-abcd'; Ci = '1.2.3-abcd-abcdef1' },
    @{ Input = '1.2.3.4-abcd'; Product = '1.2.3.4'; Info = '1.2.3.4-abcd'; Ci = '1.2.3.4-abcd-abcdef1' },
    @{ Input = '1.2.3-rc.1'; Product = '1.2.3.0'; Info = '1.2.3-rc.1'; Ci = '1.2.3-rc.1-abcdef1' }
)

foreach ($case in $cases) {
    $resolved = Resolve-CodexVersion -Version $case.Input
    Assert-Equal $case.Input $resolved.Version "$($case.Input) Version"
    Assert-Equal $case.Product $resolved.ProductVersion "$($case.Input) ProductVersion"
    Assert-Equal $case.Info $resolved.InformationalVersion "$($case.Input) InformationalVersion"
    $ci = Add-CodexCommitHash -InformationalVersion $resolved.InformationalVersion -CommitHash 'abcdef1234567890'
    Assert-Equal $case.Ci $ci "$($case.Input) CI InformationalVersion"
}

foreach ($invalid in @('1.2', '1.2.3.4.5', '1.2.3-', '1.2.3+meta', '1.2.3/evil', '01.2.3', '1.2.65535', '1.2.99999999999999999999')) {
    try {
        [void](Resolve-CodexVersion -Version $invalid)
        throw "非法版本未被拒绝：$invalid"
    } catch {
        if ($_.Exception.Message -eq "非法版本未被拒绝：$invalid") { throw }
    }
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) "codex-version-test-$([guid]::NewGuid().ToString('N'))"
try {
    foreach ($directory in @('installer', '.github/workflows')) {
        [void](New-Item -ItemType Directory -Force -Path (Join-Path $fixture $directory))
    }
    foreach ($relative in @(
        'Directory.Build.props',
        'installer/CodexLimitWidget.iss',
        'installer/build-windows.ps1',
        'README.md',
        'README-CN.md',
        '.github/workflows/build.yml'
    )) {
        Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) $relative) -Destination (Join-Path $fixture $relative)
    }

    & (Join-Path $PSScriptRoot 'bump-version.ps1') -Version '1.2.3.4-abcd' -Root $fixture
    $props = [xml](Get-Content -Raw -LiteralPath (Join-Path $fixture 'Directory.Build.props'))
    Assert-Equal '1.2.3.4-abcd' $props.Project.PropertyGroup.Version 'bump Version'
    Assert-Equal '1.2.3.4' $props.Project.PropertyGroup.AssemblyVersion 'bump AssemblyVersion'
    Assert-Equal '1.2.3.4' $props.Project.PropertyGroup.FileVersion 'bump FileVersion'
    Assert-Equal '1.2.3.4-abcd' $props.Project.PropertyGroup.InformationalVersion 'bump InformationalVersion'
    $iss = Get-Content -Raw -LiteralPath (Join-Path $fixture 'installer/CodexLimitWidget.iss')
    if ($iss -notmatch '#define MyAppVersion "1\.2\.3\.4-abcd"' -or $iss -notmatch '#define MyAppProductVersion "1\.2\.3\.4"') {
        throw 'bump 未正确拆分 Inno Setup 显示版本和产品版本。'
    }
} finally {
    if (Test-Path $fixture) { Remove-Item -Recurse -Force -LiteralPath $fixture }
}

Write-Host 'PowerShell versioning tests passed.' -ForegroundColor Green
