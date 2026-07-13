param(
    [string]$Version = "1.0.0",
    [string]$InformationalVersion,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')
$resolved = Resolve-CodexVersion -Version $Version -InformationalVersion $InformationalVersion
$Version = $resolved.Version
$ProductVersion = $resolved.ProductVersion
$InformationalVersion = $resolved.InformationalVersion
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'CodexLimitWidget.App\CodexLimitWidget.App.csproj'
$publishRoot = Join-Path $root 'publish\win-x64'
$frameworkDependent = Join-Path $publishRoot 'framework-dependent'
$selfContained = Join-Path $publishRoot 'self-contained'
$runtimeMarker = Join-Path $publishRoot 'framework-dependent.runtime-major.txt'
$isolatedBuildRoot = Join-Path $root 'obj\build-windows-packages'
$dist = Join-Path $root 'dist'

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
    throw 'Inno Setup 6 is required. Install it or add iscc.exe to PATH.'
}

function Publish-WindowsApp([string]$OutputDirectory, [bool]$SelfContained, [string]$BuildOutputDirectory) {
    if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force -LiteralPath $OutputDirectory }
    if (Test-Path $BuildOutputDirectory) { Remove-Item -Recurse -Force -LiteralPath $BuildOutputDirectory }

    $selfContainedOption = if ($SelfContained) { '--self-contained' } else { '--no-self-contained' }
    dotnet publish $project -c Release -r win-x64 $selfContainedOption `
        -p:BaseOutputPath="$BuildOutputDirectory\" `
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version -p:InformationalVersion=$InformationalVersion `
        -p:AssemblyVersion=$ProductVersion -p:FileVersion=$ProductVersion -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

    Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
    $pdbFiles = @(Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue)
    if ($pdbFiles.Count -gt 0) { throw "Published package contains PDB files: $($pdbFiles.FullName -join ', ')" }

    $app = Get-Item (Join-Path $OutputDirectory 'CodexLimitWidget.App.exe')
    if ($app.VersionInfo.ProductVersion -ne $InformationalVersion) {
        throw "Unexpected ProductVersion: $($app.VersionInfo.ProductVersion); expected $InformationalVersion."
    }
    if ($app.VersionInfo.FileVersion -ne $ProductVersion) {
        throw "Unexpected FileVersion: $($app.VersionInfo.FileVersion); expected $ProductVersion."
    }
}

if (-not $SkipPublish) {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishRoot, $isolatedBuildRoot, $dist
    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

    $sdkVersionText = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersionText -notmatch '^(\d+)\.') {
        throw "Unable to determine the .NET SDK major version from '$sdkVersionText'."
    }
    $runtimeMajor = if ([int]$Matches[1] -ge 10) { 10 } else { 8 }

    try {
        Publish-WindowsApp $frameworkDependent $false (Join-Path $isolatedBuildRoot 'framework-dependent')
        Publish-WindowsApp $selfContained $true (Join-Path $isolatedBuildRoot 'self-contained')
        $slimSize = (Get-Item (Join-Path $frameworkDependent 'CodexLimitWidget.App.exe')).Length
        $fullSize = (Get-Item (Join-Path $selfContained 'CodexLimitWidget.App.exe')).Length
        if ($fullSize -le ($slimSize + 10MB)) {
            throw "Self-contained output must be at least 10 MiB larger than framework-dependent output; Slim=$slimSize, Full=$fullSize."
        }
        Set-Content -LiteralPath $runtimeMarker -Value $runtimeMajor -NoNewline
    }
    finally {
        if (Test-Path $isolatedBuildRoot) { Remove-Item -Recurse -Force -LiteralPath $isolatedBuildRoot }
    }
}

foreach ($directory in $frameworkDependent, $selfContained) {
    $appPath = Join-Path $directory 'CodexLimitWidget.App.exe'
    if (-not (Test-Path $appPath)) {
        throw "Missing published application: $directory"
    }
    $app = Get-Item $appPath
    if ($app.VersionInfo.ProductVersion -ne $InformationalVersion -or $app.VersionInfo.FileVersion -ne $ProductVersion) {
        throw "Published application version mismatch in ${directory}; ProductVersion=$($app.VersionInfo.ProductVersion), FileVersion=$($app.VersionInfo.FileVersion)."
    }
}
if (-not (Test-Path $runtimeMarker)) { throw "Missing runtime marker: $runtimeMarker" }
$runtimeMajor = (Get-Content -Raw -LiteralPath $runtimeMarker).Trim()
if ($runtimeMajor -notmatch '^\d+$') { throw "Invalid runtime major in ${runtimeMarker}: $runtimeMajor" }

if (Test-Path $dist) { Remove-Item -Recurse -Force -LiteralPath $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
& (Join-Path $PSScriptRoot 'download-inno-languages.ps1')
$iscc = Resolve-Iscc
$iss = Join-Path $PSScriptRoot 'CodexLimitWidget.iss'

& $iscc "/DMyAppVersion=$Version" "/DMyAppProductVersion=$ProductVersion" `
    '/DMyAppSourceDir=..\publish\win-x64\framework-dependent' `
    '/DMyAppPackageSuffix=Windows-x64-Slim-Setup' '/DMyAppPackageLabel=Slim' `
    '/DMyAppRequiresRuntime=1' "/DMyAppRequiredRuntimeMajor=$runtimeMajor" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed for Slim package: $LASTEXITCODE" }

& $iscc "/DMyAppVersion=$Version" "/DMyAppProductVersion=$ProductVersion" `
    '/DMyAppSourceDir=..\publish\win-x64\self-contained' `
    '/DMyAppPackageSuffix=Windows-x64-Full-Setup' '/DMyAppPackageLabel=Full' `
    '/DMyAppRequiresRuntime=0' "/DMyAppRequiredRuntimeMajor=$runtimeMajor" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed for Full package: $LASTEXITCODE" }

$expected = @(
    "CodexLimitWidget-$Version-Windows-x64-Slim-Setup.exe",
    "CodexLimitWidget-$Version-Windows-x64-Full-Setup.exe"
)
$actual = @(Get-ChildItem -File $dist -Filter *.exe | Select-Object -ExpandProperty Name | Sort-Object)
$difference = @(Compare-Object ($expected | Sort-Object) $actual)
if ($difference.Count -ne 0 -or $actual.Count -ne 2) {
    throw "Expected exactly two Windows installers ($($expected -join ', ')); found $($actual -join ', ')."
}

Get-ChildItem -File $dist -Filter *.exe | Sort-Object Name | Select-Object Name, Length
