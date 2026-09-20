[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$Target = "GI-Subtitles",
    [switch]$SkipRestore,
    [switch]$SkipOcrModels
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "GI-Subtitles.sln"

function Get-MsBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere.exe was not found. Install Visual Studio Build Tools with the MSBuild component."
    }

    $candidates = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"
    if (-not $candidates) {
        throw "MSBuild.exe was not found. Install Visual Studio with the .NET desktop development workload."
    }

    $preferred = @($candidates | Where-Object { $_ -match '\\MSBuild\\Current\\Bin\\MSBuild\.exe$' })
    if ($preferred.Count -gt 0) {
        return $preferred[0]
    }

    return @($candidates)[0]
}

function Get-NugetPath {
    $onPath = Get-Command nuget -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $cacheDir = Join-Path $env:LOCALAPPDATA "GI-Subtitles"
    $nugetPath = Join-Path $cacheDir "nuget.exe"
    if (Test-Path -LiteralPath $nugetPath) {
        return $nugetPath
    }

    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    Write-Host "Downloading nuget.exe to $nugetPath"
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetPath -UseBasicParsing
    return $nugetPath
}

$msbuild = Get-MsBuildPath
Write-Host "MSBuild: $msbuild"

if (-not $SkipRestore) {
    $nuget = Get-NugetPath
    Write-Host "NuGet: $nuget"
    & $nuget restore $solutionPath
    if ($LASTEXITCODE -ne 0) {
        throw "nuget restore failed with exit code $LASTEXITCODE"
    }
}

if (-not $SkipOcrModels) {
    & (Join-Path $PSScriptRoot "Restore-OcrModels.ps1")
}

Write-Host "Building $Target ($Configuration|$Platform)"
& $msbuild $solutionPath "-t:${Target}:Rebuild" "-p:Configuration=$Configuration" "-p:Platform=$Platform"
if ($LASTEXITCODE -ne 0) {
    throw "msbuild failed with exit code $LASTEXITCODE"
}
