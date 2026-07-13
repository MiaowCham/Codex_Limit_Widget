param(
    [string]$Version = "1.0.0",
    [string]$InformationalVersion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versioning.ps1')
$resolved = Resolve-CodexVersion -Version $Version -InformationalVersion $InformationalVersion
$Version = $resolved.Version
$ProductVersion = $resolved.ProductVersion
$InformationalVersion = $resolved.InformationalVersion
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root 'publish\win-x64\app'
$dist = Join-Path $root 'dist'

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publish, $dist
& (Join-Path $PSScriptRoot 'download-inno-languages.ps1')
dotnet publish (Join-Path $root 'CodexLimitWidget.App\CodexLimitWidget.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version -p:InformationalVersion=$InformationalVersion `
    -p:AssemblyVersion=$ProductVersion -p:FileVersion=$ProductVersion -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 is required. Install it or add iscc.exe to PATH.' }
& $iscc "/DMyAppVersion=$Version" "/DMyAppProductVersion=$ProductVersion" (Join-Path $PSScriptRoot 'CodexLimitWidget.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
