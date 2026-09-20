[CmdletBinding()]
param(
    [string]$ExePath = ""
)

# Acceptance: the Release build must not contain the region-adjust diagnostic
# code. The diagnostic log messages live in the #US string heap as UTF-16LE
# and must be absent from the Release exe. (Some strings such as SetWindowLong
# verdicts share production identifiers, so only diagnostic-only wording is
# probed.) Per-frame movement probes and their diagnostic-only Win32 imports
# were removed; the one remaining diagnostic import, GetWindowLong, is also a
# production import and cannot be probed.

$ErrorActionPreference = "Stop"

$exe = $ExePath
if ([string]::IsNullOrEmpty($exe)) {
    $exe = Join-Path $PSScriptRoot "..\GI-Subtitles\bin\Release\GI-Subtitles.exe"
}
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Release build not found at $exe. Build GI-Subtitles (Release) first."
}

$utf16Probes = @(
    'AdjustTrace',
    'hit-mode interactive',
    'arm accepted',
    'arm refused',
    'arm dismissed',
    'verdict=',
    'no-element-input',
    'element mouse-down',
    'element mouse-up',
    'store write:',
    'display set:',
    'capture set:',
    'outline:',
    'TRANSPARENT-BIT-STILL-SET',
    'disabled-bit',
    'DISABLED-BIT-STILL-SET'
)

function Find-Bytes {
    param([byte[]]$Haystack, [byte[]]$Needle)

    for ($i = 0; $i -le $Haystack.Length - $Needle.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $Needle.Length; $j++) {
            if ($Haystack[$i + $j] -ne $Needle[$j]) {
                $ok = $false
                break
            }
        }
        if ($ok) {
            return $true
        }
    }
    return $false
}

$bytes = [IO.File]::ReadAllBytes($exe)
$found = @()
foreach ($probe in $utf16Probes) {
    if (Find-Bytes $bytes ([Text.Encoding]::Unicode.GetBytes($probe))) {
        $found += "UTF16 string: '$probe'"
    }
}

if ($found.Count -gt 0) {
    $found
    throw "Release build contains region-adjust diagnostic code: $($exe)"
}

"OK: no region-adjust diagnostic code in $($exe)"
