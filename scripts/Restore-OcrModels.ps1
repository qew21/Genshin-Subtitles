[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\inference\models.json"),
    [string]$DestinationRoot = (Join-Path $PSScriptRoot ".."),
    [string]$ArchivePath,
    [string]$CacheDirectory = (Join-Path $env:LOCALAPPDATA "GI-Subtitles\ModelCache"),
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

$manifestPathResolved = (Resolve-Path -LiteralPath $ManifestPath).Path
$destinationRootResolved = [IO.Path]::GetFullPath($DestinationRoot)
$manifest = Get-Content -LiteralPath $manifestPathResolved -Raw -Encoding UTF8 |
    ConvertFrom-Json

if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported OCR model manifest schema: $($manifest.schemaVersion)"
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-ManifestFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$ThrowOnFailure
    )

    foreach ($entry in $manifest.files) {
        $relativePath = $entry.path.Replace("/", [IO.Path]::DirectorySeparatorChar)
        $path = Join-Path $Root $relativePath
        $valid = (Test-Path -LiteralPath $path -PathType Leaf) -and
            ((Get-Item -LiteralPath $path).Length -eq [long]$entry.size) -and
            ((Get-FileSha256 -Path $path) -eq $entry.sha256)

        if (-not $valid) {
            if ($ThrowOnFailure) {
                throw "OCR model validation failed: $($entry.path)"
            }
            return $false
        }
    }

    return $true
}

if (Test-ManifestFiles -Root $destinationRootResolved) {
    Write-Host "OCR models $($manifest.bundleVersion) are already valid."
    return
}

if ($VerifyOnly) {
    Test-ManifestFiles -Root $destinationRootResolved -ThrowOnFailure | Out-Null
    return
}

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
    $ArchivePath = Join-Path $CacheDirectory $manifest.archive.fileName

    $archiveIsValid = (Test-Path -LiteralPath $ArchivePath -PathType Leaf) -and
        ((Get-Item -LiteralPath $ArchivePath).Length -eq [long]$manifest.archive.size) -and
        ((Get-FileSha256 -Path $ArchivePath) -eq $manifest.archive.sha256)

    if (-not $archiveIsValid) {
        Write-Host "Downloading OCR model bundle $($manifest.bundleVersion)..."
        $downloadPath = "$ArchivePath.download"
        Invoke-WebRequest -Uri $manifest.archive.url -OutFile $downloadPath -UseBasicParsing

        if ((Get-Item -LiteralPath $downloadPath).Length -ne [long]$manifest.archive.size -or
            (Get-FileSha256 -Path $downloadPath) -ne $manifest.archive.sha256) {
            Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
            throw "Downloaded OCR model archive failed SHA-256 or size validation."
        }

        Move-Item -LiteralPath $downloadPath -Destination $ArchivePath -Force
    }
}

$archivePathResolved = (Resolve-Path -LiteralPath $ArchivePath).Path
if ((Get-Item -LiteralPath $archivePathResolved).Length -ne [long]$manifest.archive.size -or
    (Get-FileSha256 -Path $archivePathResolved) -ne $manifest.archive.sha256) {
    throw "OCR model archive failed SHA-256 or size validation: $archivePathResolved"
}

$extractRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "gi-subtitles-ocr-models-" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    Expand-Archive -LiteralPath $archivePathResolved -DestinationPath $extractRoot
    Test-ManifestFiles -Root $extractRoot -ThrowOnFailure | Out-Null

    foreach ($entry in $manifest.files) {
        $relativePath = $entry.path.Replace("/", [IO.Path]::DirectorySeparatorChar)
        $source = Join-Path $extractRoot $relativePath
        $destination = Join-Path $destinationRootResolved $relativePath
        $destinationDirectory = Split-Path $destination -Parent
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

        $temporaryDestination = "$destination.download"
        Copy-Item -LiteralPath $source -Destination $temporaryDestination -Force
        Move-Item -LiteralPath $temporaryDestination -Destination $destination -Force
    }
}
finally {
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

Test-ManifestFiles -Root $destinationRootResolved -ThrowOnFailure | Out-Null
Write-Host "OCR models $($manifest.bundleVersion) restored and verified."
