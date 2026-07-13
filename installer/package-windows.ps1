param(
    [ValidateSet('Slim', 'Full', 'Both')]
    [string]$Package = 'Both',
    [string]$Version,
    [string]$InformationalVersion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) {
    $stored = Get-CodexStoredVersion -RepositoryRoot $root
    $Version = $stored.Version
    if (-not $InformationalVersion) { $InformationalVersion = $stored.InformationalVersion }
}
$resolved = Resolve-CodexVersion -Version $Version -InformationalVersion $InformationalVersion
$Version = $resolved.Version
$ProductVersion = $resolved.ProductVersion
$InformationalVersion = $resolved.InformationalVersion

$buildSlim = $Package -in @('Slim', 'Both')
$buildFull = $Package -in @('Full', 'Both')
$project = Join-Path $root 'CodexLimitWidget.App\CodexLimitWidget.App.csproj'
$publishRoot = Join-Path $root 'publish\win-x64'
$frameworkDependent = Join-Path $publishRoot 'framework-dependent'
$selfContained = Join-Path $publishRoot 'self-contained'
$runtimeMarker = Join-Path $publishRoot 'framework-dependent.runtime-major.txt'
$isolatedBuildRoot = Join-Path $root 'obj\windows-packages'
$dist = Join-Path $root 'dist'

function Remove-WorkspaceDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $workspacePrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除工作区之外的目录：$fullPath"
    }
    if (Test-Path $fullPath) { Remove-Item -Recurse -Force -LiteralPath $fullPath }
}

function Resolve-Iscc {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw '未找到 Inno Setup 6（ISCC.exe）。请先安装 Inno Setup 6。'
}

function Publish-WindowsApp([string]$OutputDirectory, [bool]$SelfContained, [string]$BuildOutputDirectory) {
    Remove-WorkspaceDirectory $OutputDirectory
    Remove-WorkspaceDirectory $BuildOutputDirectory

    $selfContainedOption = if ($SelfContained) { '--self-contained' } else { '--no-self-contained' }
    dotnet publish $project -c Release -r win-x64 $selfContainedOption `
        -p:BaseOutputPath="$BuildOutputDirectory\" `
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version -p:InformationalVersion=$InformationalVersion `
        -p:AssemblyVersion=$ProductVersion -p:FileVersion=$ProductVersion -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE。" }

    Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
    $pdbFiles = @(Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue)
    if ($pdbFiles.Count -gt 0) { throw "发布目录中仍存在 PDB：$($pdbFiles.FullName -join ', ')" }

    $app = Get-Item (Join-Path $OutputDirectory 'CodexLimitWidget.App.exe')
    if ($app.VersionInfo.ProductVersion -ne $InformationalVersion) {
        throw "ProductVersion 不正确：$($app.VersionInfo.ProductVersion)，预期 $InformationalVersion。"
    }
    if ($app.VersionInfo.FileVersion -ne $ProductVersion) {
        throw "FileVersion 不正确：$($app.VersionInfo.FileVersion)，预期 $ProductVersion。"
    }
}

function Invoke-InnoPackage(
    [string]$SourceDirectory,
    [string]$Suffix,
    [string]$Label,
    [bool]$RequiresRuntime,
    [string]$RuntimeMajor
) {
    $requiresRuntimeValue = if ($RequiresRuntime) { '1' } else { '0' }
    & $script:Iscc "/DMyAppVersion=$Version" "/DMyAppProductVersion=$ProductVersion" `
        "/DMyAppSourceDir=$SourceDirectory" "/DMyAppPackageSuffix=$Suffix" "/DMyAppPackageLabel=$Label" `
        "/DMyAppRequiresRuntime=$requiresRuntimeValue" "/DMyAppRequiredRuntimeMajor=$RuntimeMajor" $script:Iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译 $Label 安装包失败，退出码 $LASTEXITCODE。" }
}

Remove-WorkspaceDirectory $publishRoot
Remove-WorkspaceDirectory $isolatedBuildRoot
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

$sdkVersionText = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersionText -notmatch '^(\d+)\.') {
    throw "无法从 '$sdkVersionText' 判断 .NET SDK 主版本。"
}
$runtimeMajor = if ([int]$Matches[1] -ge 10) { 10 } else { 8 }

try {
    if ($buildSlim) {
        Write-Host '正在发布 Slim（framework-dependent）程序...' -ForegroundColor Cyan
        Publish-WindowsApp $frameworkDependent $false (Join-Path $isolatedBuildRoot 'framework-dependent')
        Set-Content -LiteralPath $runtimeMarker -Value $runtimeMajor -NoNewline
    }
    if ($buildFull) {
        Write-Host '正在发布 Full（self-contained）程序...' -ForegroundColor Cyan
        Publish-WindowsApp $selfContained $true (Join-Path $isolatedBuildRoot 'self-contained')
    }
    if ($buildSlim -and $buildFull) {
        $slimSize = (Get-Item (Join-Path $frameworkDependent 'CodexLimitWidget.App.exe')).Length
        $fullSize = (Get-Item (Join-Path $selfContained 'CodexLimitWidget.App.exe')).Length
        if ($fullSize -le ($slimSize + 10MB)) {
            throw "Full 输出应至少比 Slim 大 10 MiB；Slim=$slimSize，Full=$fullSize。"
        }
    }
}
finally {
    Remove-WorkspaceDirectory $isolatedBuildRoot
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -File $dist -Filter 'CodexLimitWidget-*-Windows-x64-*-Setup.exe' -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -File $dist -Filter 'CodexLimitWidget-*-Setup.exe' -ErrorAction SilentlyContinue | Remove-Item -Force

& (Join-Path $PSScriptRoot 'fetch-inno-languages.ps1')
$script:Iscc = Resolve-Iscc
$script:Iss = Join-Path $PSScriptRoot 'CodexLimitWidget.iss'
$expected = @()

if ($buildSlim) {
    Invoke-InnoPackage '..\publish\win-x64\framework-dependent' 'Windows-x64-Slim-Setup' 'Slim' $true $runtimeMajor
    $expected += Join-Path $dist "CodexLimitWidget-$Version-Windows-x64-Slim-Setup.exe"
}
if ($buildFull) {
    Invoke-InnoPackage '..\publish\win-x64\self-contained' 'Windows-x64-Full-Setup' 'Full' $false $runtimeMajor
    $expected += Join-Path $dist "CodexLimitWidget-$Version-Windows-x64-Full-Setup.exe"
}

$missing = @($expected | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) { throw "缺少预期安装包：$($missing -join ', ')" }

Write-Host ''
Write-Host '生成的安装包：' -ForegroundColor Green
Get-Item -LiteralPath $expected | Sort-Object Name | Select-Object Name, Length, @{Name='MiB'; Expression={[math]::Round($_.Length / 1MB, 2)}}
