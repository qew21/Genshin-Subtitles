param([int]$BootstrapProcessId)

$ErrorActionPreference = 'Stop'
try {
    if ($BootstrapProcessId) {
        $bootstrap = Get-Process -Id $BootstrapProcessId -ErrorAction SilentlyContinue
        if ($bootstrap -and !$bootstrap.WaitForExit(5000)) {
            throw 'The preview launcher did not exit. Please try again.'
        }
    }
    if (Get-Process -Name 'GI-Subtitles' -ErrorAction SilentlyContinue) {
        throw 'Close the running GI-Subtitles instance before launching the preview.'
    }

    $packageName = 'GISubtitles.BorderlessCapturePreview'
    $package = Get-AppxPackage -Name $packageName
    if (!$package -or $package.InstallLocation -ne $PSScriptRoot) {
        Add-AppxPackage -Register (Join-Path $PSScriptRoot 'AppxManifest.xml')
        $package = Get-AppxPackage -Name $packageName
        if (!$package -or $package.InstallLocation -ne $PSScriptRoot) {
            throw 'Windows did not register the preview from this folder.'
        }
    }
    & explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!Preview"
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        "$($_.Exception.Message)`n`nThis preview requires Windows 11 and Developer Mode for its first launch.",
        'GI-Subtitles Borderless Capture Preview') | Out-Null
    exit 1
}
