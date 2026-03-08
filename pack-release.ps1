param(
    [string]$Configuration = "Release",
    [string]$Framework = "net46"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "BurglinGnomesRuAutoTranslate.csproj"
$outDir = Join-Path $root "bin\$Configuration\$Framework"
$distDir = Join-Path $root "dist"

# Make dotnet CLI deterministic in restricted environments.
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

Write-Host "Building project..."
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$dllPath = Join-Path $outDir "BurglinGnomesRuAutoTranslate.dll"
$dictPath = Join-Path $root "MonoUniversal.dictionary.txt"
if (-not (Test-Path $dictPath)) {
    $dictPath = Join-Path $root "BurglinGnomesRU.dictionary.txt"
}
$readmePath = Join-Path $root "README.md"

if (-not (Test-Path $dllPath)) {
    throw "Build output not found: $dllPath"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$version = "dev"
$pluginFile = Join-Path $root "src\Plugin.cs"
if (Test-Path $pluginFile) {
    $m = Select-String -Path $pluginFile -Pattern 'PluginVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($m.Matches.Count -gt 0) {
        $version = $m.Matches[0].Groups[1].Value
    }
}

$staging = Join-Path $distDir "staging"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item $dllPath (Join-Path $staging "BurglinGnomesRuAutoTranslate.dll") -Force
Copy-Item $dictPath (Join-Path $staging "MonoUniversal.dictionary.txt") -Force
Copy-Item $readmePath (Join-Path $staging "README.md") -Force

$install = @"
Install:
1. Copy BurglinGnomesRuAutoTranslate.dll to:
   <GAME>\\BepInEx\\plugins\\BurglinGnomesRuAutoTranslate\\
2. Copy MonoUniversal.dictionary.txt to:
   <GAME>\\BepInEx\\config\\
3. Start the game.
"@
$install | Set-Content -Encoding UTF8 (Join-Path $staging "INSTALL.txt")

$zipPath = Join-Path $distDir ("BurglinGnomesRuAutoTranslate-{0}.zip" -f $version)
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force

Write-Host "Release package ready: $zipPath"

