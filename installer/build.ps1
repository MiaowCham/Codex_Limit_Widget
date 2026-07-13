param(
    [ValidateSet('1', '2', '3', '4')]
    [string]$Choice,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'CodexLimitWidget.App\CodexLimitWidget.App.csproj'
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'

if (-not $Version) {
    $xml = [xml](Get-Content -Raw -LiteralPath $projectPath)
    $Version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if (-not $Version) {
        $versionProps = [xml](Get-Content -Raw -LiteralPath $versionPropsPath)
        $Version = @($versionProps.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    }
}
if (-not $Version) { throw '无法从项目文件读取版本号。' }
$Version = $Version -replace '^v', ''
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版本号必须使用 x.y.z 格式: $Version" }
$BinaryVersion = "$Version.0"
Write-Host "使用版本号: $Version（产品版本 $BinaryVersion）" -ForegroundColor DarkCyan

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
    # dotnet publish 默认会先写入 bin，再复制到 -o 指定目录。
    # 使用 obj 下的隔离目录作为临时构建输出，避免覆盖手动构建结果或留下重复产物。
    $isolatedBuildOutput = Join-Path $repoRoot 'obj\build-script-output'

    if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force -LiteralPath $OutputDirectory }
    if (Test-Path $isolatedBuildOutput) { Remove-Item -Recurse -Force -LiteralPath $isolatedBuildOutput }

    try {
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
            -p:BaseOutputPath="$isolatedBuildOutput\" `
            -p:PublishSingleFile=$($SingleFile.ToString().ToLowerInvariant()) `
            -p:DebugType=None -p:DebugSymbols=false `
            -p:Version=$Version -p:InformationalVersion=$BinaryVersion `
            -p:AssemblyVersion=$BinaryVersion -p:FileVersion=$BinaryVersion `
            -o $OutputDirectory
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE。" }

        # Native dependencies may include their own PDBs even when the application
        # is published with DebugType=None; they are not distributable artifacts.
        Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
        $pdbFiles = @(Get-ChildItem -Recurse -File $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue)
        if ($pdbFiles.Count -gt 0) { throw "发布目录中发现 PDB: $($pdbFiles.FullName -join ', ')" }

        $app = Get-Item (Join-Path $OutputDirectory 'CodexLimitWidget.App.exe')
        if ($app.VersionInfo.ProductVersion -ne $BinaryVersion) {
            throw "软件产品版本不正确: $($app.VersionInfo.ProductVersion)，预期 $BinaryVersion。"
        }
        if ($app.VersionInfo.FileVersion -ne $BinaryVersion) {
            throw "软件文件版本不正确: $($app.VersionInfo.FileVersion)，预期 $BinaryVersion。"
        }

        $assemblyPath = Join-Path $OutputDirectory 'CodexLimitWidget.App.dll'
        if (Test-Path $assemblyPath) {
            $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
            if ($assemblyVersion -ne $BinaryVersion) {
                throw "程序集版本不正确: $assemblyVersion，预期 $BinaryVersion。"
            }
        }
    }
    finally {
        if (Test-Path $isolatedBuildOutput) {
            Remove-Item -Recurse -Force -LiteralPath $isolatedBuildOutput
        }
    }
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
        # The Inno Setup scripts consume this directory and recursively include
        # culture-specific satellite assemblies when present.
        Publish-App (Join-Path $repoRoot 'publish\win-x64\app') $false
        & (Join-Path $PSScriptRoot 'download-inno-languages.ps1')
        $iscc = Resolve-Iscc
        & $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'CodexLimitWidget.iss')
        if ($LASTEXITCODE -ne 0) { throw "ISCC 构建失败，退出码 $LASTEXITCODE。" }
        Write-Host "安装包已生成到: $(Join-Path $repoRoot 'dist')" -ForegroundColor Green
    }
}
