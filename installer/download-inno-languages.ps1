$ErrorActionPreference = 'Stop'

$languageDirectory = Join-Path $PSScriptRoot 'Languages'
New-Item -ItemType Directory -Force -Path $languageDirectory | Out-Null

$languages = @(
    @{ Name = 'ChineseSimplified.isl'; Url = 'https://raw.githubusercontent.com/jrsoftware/issrc/refs/heads/main/Files/Languages/ChineseSimplified.isl' },
    @{ Name = 'ChineseTraditional.isl'; Url = 'https://raw.githubusercontent.com/jrsoftware/issrc/refs/heads/main/Files/Languages/ChineseTraditional.isl' },
    @{ Name = 'Japanese.isl'; Url = 'https://raw.githubusercontent.com/jrsoftware/issrc/refs/heads/main/Files/Languages/Japanese.isl' }
)

foreach ($language in $languages) {
    Invoke-WebRequest -Uri $language.Url -OutFile (Join-Path $languageDirectory $language.Name)
}
