function Resolve-CodexVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$InformationalVersion
    )

    $pattern = '^(?<numeric>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:\.(?:0|[1-9]\d*))?)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$'

    function Normalize([string]$Value, [string]$Name) {
        $normalized = $Value.Trim()
        if ($normalized.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) {
            $normalized = $normalized.Substring(1)
        }
        if ($normalized -notmatch $pattern) {
            throw "$Name 必须为 x.y.z[-suffix] 或 x.y.z.w[-suffix]：$Value"
        }
        return $normalized
    }

    $normalizedVersion = Normalize $Version '版本号'
    $versionMatch = [regex]::Match($normalizedVersion, $pattern)
    $numericVersion = $versionMatch.Groups['numeric'].Value
    $segments = @($numericVersion.Split('.'))
    foreach ($segment in $segments) {
        [uint32]$value = 0
        if (-not [uint32]::TryParse($segment, [ref]$value) -or $value -gt 65534) {
            throw "数字版本的每一段必须位于 0..65534：$numericVersion"
        }
    }
    $productVersion = if ($segments.Count -eq 3) { "$numericVersion.0" } else { $numericVersion }

    $resolvedInformational = if ($InformationalVersion) {
        Normalize $InformationalVersion 'InformationalVersion'
    } else {
        $normalizedVersion
    }
    $informationalMatch = [regex]::Match($resolvedInformational, $pattern)
    if ($informationalMatch.Groups['numeric'].Value -ne $numericVersion) {
        throw "InformationalVersion 的数字部分必须与版本号一致：$resolvedInformational / $numericVersion"
    }

    [pscustomobject]@{
        Version              = $normalizedVersion
        NumericVersion       = $numericVersion
        ProductVersion       = $productVersion
        InformationalVersion = $resolvedInformational
        MacOSVersion         = ($segments[0..2] -join '.')
    }
}

function Add-CodexCommitHash {
    param(
        [Parameter(Mandatory = $true)][string]$InformationalVersion,
        [Parameter(Mandatory = $true)][string]$CommitHash
    )

    $hash = $CommitHash.Trim()
    if ($hash -notmatch '^[0-9A-Fa-f]{7,64}$') {
        throw "提交 hash 必须至少包含 7 个十六进制字符：$CommitHash"
    }
    "$InformationalVersion-$($hash.Substring(0, 7).ToLowerInvariant())"
}

function Get-CodexStoredVersion {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $path = Join-Path $RepositoryRoot 'Directory.Build.props'
    $xml = [xml](Get-Content -Raw -LiteralPath $path)
    $version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $informational = @($xml.Project.PropertyGroup.InformationalVersion | Where-Object { $_ })[0]
    if (-not $version) { throw "无法从 $path 读取 Version。" }
    Resolve-CodexVersion -Version $version -InformationalVersion $informational
}
