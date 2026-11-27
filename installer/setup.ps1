#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Install', 'Modify', 'Uninstall')]
    [string] $Action
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-files.ps1')

$lock = Enter-PaneDockMaintenance
try {
    $defaultRoot = Join-Path $env:LOCALAPPDATA 'Programs\PaneDock'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'PaneDock.lnk'
    $legacyRoot = $null
    $legacyWorkingDirectory = $null
    if (Test-Path -LiteralPath $shortcut -PathType Leaf) {
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
        if ([IO.Path]::GetFileName($link.TargetPath) -eq 'PaneDock.exe') {
            $legacyRoot = Split-Path -Parent $link.TargetPath
            if ($link.WorkingDirectory -and (Test-PaneDockAppShortcut $link $link.WorkingDirectory)) {
                $legacyWorkingDirectory = $link.WorkingDirectory
            }
        }
    }
    $installedRoot = @((Get-PaneDockRegisteredRoot), $legacyRoot, $legacyWorkingDirectory, $defaultRoot) |
        Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ $script:PaneDockManifestName) -PathType Leaf) } |
        Select-Object -First 1

    if ($Action -eq 'Install' -and $installedRoot) {
        throw 'PaneDock is already installed. Select Modify to update or repair it.'
    }
    if ($Action -ne 'Install' -and -not $installedRoot) {
        throw 'PaneDock is not installed. Select Install first.'
    }

    if ($Action -eq 'Uninstall') {
        & (Join-Path $PSScriptRoot 'uninstall.ps1') -InstallDirectory $installedRoot -WhatIf:$WhatIfPreference
    }
    else {
        $root = if ($installedRoot) { $installedRoot } else { $defaultRoot }
        & (Join-Path $PSScriptRoot 'install.ps1') -InstallDirectory $root -Package (Join-Path $PSScriptRoot 'payload.zip') -WhatIf:$WhatIfPreference
    }
}
finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}
