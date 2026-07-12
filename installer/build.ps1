param(
    [ValidateSet('1', '2', '3', '4')]
    [string]$Choice,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'CodexLimitWidget.csproj'

if (-not $Version) {
    $xml = [xml](Get-Content -Raw -LiteralPath $projectPath)
    $Version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
if (-not $Version) { throw '无法从项目文件读取版本号。' }
$Version = $Version -replace '^v', ''
Write-Host "使用版本号: $Version" -ForegroundColor DarkCyan

if (-not $Choice) {
    Write-Host '请选择构建类型:' -ForegroundColor Cyan
    Write-Host '  1) Debug 构建'
    Write-Host '  2) Release 发布（框架依赖，安装包输入）'
    Write-Host '  3) Release 单文件发布（框架依赖）'
    Write-Host '  4) 构建安装包（自动执行 Release 发布）'
    $Choice = Read-Host '输入数字 1/2/3/4'
}

Set-Location $repoRoot

function Publish-App([string]$OutputDirectory, [bool]$SingleFile) {
    if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force -LiteralPath $OutputDirectory }
    dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=$($SingleFile.ToString().ToLowerInvariant()) `
        -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version -p:InformationalVersion=$Version `
        -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
        -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE。" }
    $pdbFiles = @(Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue)
    if ($pdbFiles.Count -gt 0) { throw "发布目录中发现 PDB: $($pdbFiles.FullName -join ', ')" }
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
    throw '未找到 ISCC.exe。请先安装 Inno Setup 6。'
}

switch ($Choice) {
    '1' { dotnet build $projectPath -c Debug }
    '2' { Publish-App (Join-Path $repoRoot 'publish\release') $false }
    '3' { Publish-App (Join-Path $repoRoot 'publish\single-file') $true }
    '4' {
        Publish-App (Join-Path $repoRoot 'publish\release') $false
        $iscc = Resolve-Iscc
        $languageFile = Join-Path (Split-Path $iscc) 'Languages\ChineseSimplified.isl'
        $issFile = if (Test-Path $languageFile) {
            Join-Path $PSScriptRoot 'CodexLimitWidget.iss'
        } else {
            Write-Host '未找到中文语言包，使用 CI 英文安装脚本。' -ForegroundColor Yellow
            Join-Path $PSScriptRoot 'CodexLimitWidget.CI.iss'
        }
        & $iscc "/DMyAppVersion=$Version" $issFile
        if ($LASTEXITCODE -ne 0) { throw "ISCC 构建失败，退出码 $LASTEXITCODE。" }
        Write-Host "安装包已生成到: $(Join-Path $repoRoot 'dist')" -ForegroundColor Green
    }
}
