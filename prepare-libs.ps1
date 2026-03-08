param(
    [Parameter(Mandatory = $true)]
    [string]$GamePath
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$libDir = Join-Path $projectRoot "lib"

New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$bepinCore = Join-Path $GamePath "BepInEx\core"
$dataDir = Get-ChildItem -Path $GamePath -Directory | Where-Object { $_.Name -like "*_Data" } | Select-Object -First 1
if (-not $dataDir) {
    throw "Could not find *_Data folder under game path: $GamePath"
}

$managedDir = Join-Path $dataDir.FullName "Managed"

$required = @(
    @{ Src = Join-Path $bepinCore "BepInEx.dll"; Dst = "BepInEx.dll" },
    @{ Src = Join-Path $bepinCore "0Harmony.dll"; Dst = "0Harmony.dll" },
    @{ Src = Join-Path $managedDir "UnityEngine.dll"; Dst = "UnityEngine.dll" },
    @{ Src = Join-Path $managedDir "UnityEngine.CoreModule.dll"; Dst = "UnityEngine.CoreModule.dll" },
    @{ Src = Join-Path $managedDir "UnityEngine.UI.dll"; Dst = "UnityEngine.UI.dll" },
    @{ Src = Join-Path $managedDir "UnityEngine.TextRenderingModule.dll"; Dst = "UnityEngine.TextRenderingModule.dll" },
    @{ Src = Join-Path $managedDir "UnityEngine.TextCoreFontEngineModule.dll"; Dst = "UnityEngine.TextCoreFontEngineModule.dll" },
    @{ Src = Join-Path $managedDir "netstandard.dll"; Dst = "netstandard.dll" }
)

$optional = @(
    @{ Src = Join-Path $managedDir "Unity.TextMeshPro.dll"; Dst = "Unity.TextMeshPro.dll" }
)

foreach ($f in $required) {
    if (-not (Test-Path $f.Src)) {
        throw ("Missing required file: {0}" -f $f.Src)
    }
    Copy-Item -Path $f.Src -Destination (Join-Path $libDir $f.Dst) -Force
}

foreach ($f in $optional) {
    if (Test-Path $f.Src) {
        Copy-Item -Path $f.Src -Destination (Join-Path $libDir $f.Dst) -Force
    }
}

Write-Host ("Done. DLLs copied to {0}" -f $libDir)
