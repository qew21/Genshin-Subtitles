# Local Release Build Test - Simulates GitHub Actions Workflow
$ErrorActionPreference = "Stop"
$BASE_VERSION = "1.6.3"
$RELEASE_DIR = "test-release-package"

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "  Local Release Build Test" -ForegroundColor Cyan
Write-Host "=========================================`n"

Write-Host "[1/6] Downloading $BASE_VERSION.zip..." -ForegroundColor Yellow
if (!(Test-Path "$BASE_VERSION.zip")) {
    Invoke-WebRequest -Uri "https://github.com/qew21/Genshin-Subtitles/releases/download/$BASE_VERSION/$BASE_VERSION.zip" -OutFile "$BASE_VERSION.zip" -UseBasicParsing
}

Write-Host "[2/6] Extracting..." -ForegroundColor Yellow
if (Test-Path "base-release") { Remove-Item -Recurse -Force "base-release" }
New-Item -ItemType Directory -Force -Path "base-release" | Out-Null
Expand-Archive -Path "$BASE_VERSION.zip" -DestinationPath "base-release" -Force

Write-Host "[3/6] Building (if needed)..." -ForegroundColor Yellow
if (!(Test-Path "GI-Subtitles/bin/Release/GI-Subtitles.exe")) {
    nuget restore GI-Subtitles.sln | Out-Null
    msbuild GI-Subtitles.sln /p:Configuration=Release /t:Build /verbosity:quiet | Out-Null
}

Write-Host "[4/6] Creating package..." -ForegroundColor Yellow
if (Test-Path $RELEASE_DIR) { Remove-Item -Recurse -Force $RELEASE_DIR }
New-Item -ItemType Directory -Force -Path $RELEASE_DIR | Out-Null
Copy-Item -Path "base-release\*" -Destination $RELEASE_DIR -Recurse -Force
Copy-Item "GI-Subtitles/bin/Release/GI-Subtitles.exe" "$RELEASE_DIR/" -Force
Copy-Item "GI-Subtitles/bin/Release/GI-Subtitles.pdb" "$RELEASE_DIR/" -Force -ErrorAction SilentlyContinue
Copy-Item "GI-Subtitles/bin/Release/GI-Subtitles.exe.config" "$RELEASE_DIR/" -Force -ErrorAction SilentlyContinue
$preserve = @("onnxruntime.dll","opencv_videoio_ffmpeg4110_64.dll","OpenCvSharp4.runtime.dll")
$x64only = @("Microsoft.ML.OnnxRuntime*")
Get-ChildItem "GI-Subtitles/bin/Release" -Filter "*.dll" | ForEach-Object {
    if ($preserve -contains $_.Name) { return }
    if ($_.Name -like "Microsoft.ML.OnnxRuntime*") { return }
    Copy-Item $_.FullName "$RELEASE_DIR/$($_.Name)" -Force -ErrorAction SilentlyContinue
}

Write-Host "`n[5/6] Verifying..." -ForegroundColor Yellow
Write-Host "`nCritical Files:"
@("GI-Subtitles.exe","Config.json","inference","x64","onnxruntime.dll") | ForEach-Object {
    if (Test-Path "$RELEASE_DIR/$_") { Write-Host "  [OK] $_" -ForegroundColor Green }
    else { Write-Host "  [FAIL] $_ MISSING" -ForegroundColor Red }
}
Write-Host "`nRoot: $(Get-ChildItem $RELEASE_DIR -Name -Join ', ')"
if (Test-Path "$RELEASE_DIR/x64") { Write-Host "x64: $(Get-ChildItem "$RELEASE_DIR/x64" -Name -Join ', ')" }
if (Test-Path "$RELEASE_DIR/inference") { Write-Host "inference: FOUND" }

Write-Host "`n[6/6] Creating test-release.zip..." -ForegroundColor Yellow
if (Test-Path "test-release.zip") { Remove-Item "test-release.zip" }
Compress-Archive -Path "$RELEASE_DIR\*" -DestinationPath "test-release.zip" -Force

Write-Host "`n=========================================" -ForegroundColor Green
Write-Host "  DONE! Extract test-release.zip and test GI-Subtitles.exe" -ForegroundColor Green
Write-Host "=========================================`n"
