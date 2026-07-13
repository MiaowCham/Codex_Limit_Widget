param([string]$Version = "0.2.0")

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root 'publish\win-x64\app'
$dist = Join-Path $root 'dist'

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publish, $dist
& (Join-Path $PSScriptRoot 'download-inno-languages.ps1')
dotnet publish (Join-Path $root 'CodexLimitWidget.App\CodexLimitWidget.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 is required. Install it or add iscc.exe to PATH.' }
& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'CodexLimitWidget.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
