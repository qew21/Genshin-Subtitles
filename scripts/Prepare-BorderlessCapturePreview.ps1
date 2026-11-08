param([switch]$Launch)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\GI-Subtitles'
$preview = Join-Path $project 'bin\BorderlessCapturePreview'
$manifest = Join-Path $preview 'AppxManifest.xml'
if (!(Test-Path -LiteralPath (Join-Path $preview 'GI-Subtitles.exe'))) {
    throw 'Build GI-Subtitles.csproj with BorderlessCapturePreview=true and OutputPath=bin/BorderlessCapturePreview first.'
}

$models = Join-Path $project 'bin\Debug\inference'
$previewModels = Join-Path $preview 'inference'
if (!(Test-Path -LiteralPath (Join-Path $previewModels 'models.json'))) {
    if (!(Test-Path -LiteralPath (Join-Path $models 'models.json'))) {
        throw 'OCR models are missing. Run scripts/Restore-OcrModels.ps1 and build Debug first.'
    }
    Copy-Item -LiteralPath $models -Destination $preview -Recurse -Force
}

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $preview 'PreviewAssets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
foreach ($size in 44, 50, 150) {
    $name = if ($size -eq 50) { 'StoreLogo.png' } else { "Square${size}x${size}Logo.png" }
    $icon = [System.Drawing.Icon]::new((Join-Path $project 'Resources\mask.ico'), $size, $size)
    try {
        $bitmap = $icon.ToBitmap()
        try {
            $logo = [System.Drawing.Bitmap]::new($bitmap, $size, $size)
            try {
                $logo.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
            } finally {
                $logo.Dispose()
            }
        } finally {
            $bitmap.Dispose()
        }
    } finally {
        $icon.Dispose()
    }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BorderlessCapturePreview.appxmanifest') -Destination $manifest -Force
$launcher = Join-Path $preview 'Launch-BorderlessCapturePreview.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch-BorderlessCapturePreview.ps1') -Destination $launcher -Force
if ($Launch) {
    & $launcher
}
