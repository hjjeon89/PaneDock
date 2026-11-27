#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([string] $InstallDirectory)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 can bind default parameters before PSScriptRoot
# is initialized when invoked with -File from the installed CMD wrapper.
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.panedock-install.json')) {
        $PSScriptRoot
    } else { Join-Path $env:LOCALAPPDATA 'Programs\PaneDock' }
}
. (Join-Path $PSScriptRoot 'install-files.ps1')
$root = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDirectory)).TrimEnd('\')
$maintenance = Enter-PaneDockMaintenance
try {
    if (-not (Test-Path -LiteralPath $root)) { Write-Host 'PaneDock is not installed there.'; return }
    # Enumerate without following links before trusting the manifest.
    $present = @(Get-PaneDockInstallFiles $root)
    $managed = @(Read-PaneDockManifest $root)
    if (-not $PSCmdlet.ShouldProcess($root, 'Remove managed PaneDock files, Start menu shortcuts, and the Windows app entry')) { return }
    if (Get-Process -Name PaneDock -ErrorAction SilentlyContinue) { throw 'Close PaneDock before uninstalling it.' }

    # Keep the removal scripts available for retry if a managed binary is locked.
    $removalScripts = @('uninstall.ps1', 'uninstall.cmd', 'install-files.ps1')
    foreach ($relative in @($managed | Where-Object { $_ -notin $removalScripts })) {
        if ($relative -in $present) {
            Remove-Item -LiteralPath (Join-Path $root $relative) -Force
        }
    }
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'PaneDock.lnk'
    $removeShortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Uninstall PaneDock.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        if (Test-PaneDockAppShortcut ($shell.CreateShortcut($shortcutPath)) $root) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }
    }
    if (Test-Path -LiteralPath $removeShortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        if ($shell.CreateShortcut($removeShortcutPath).Arguments -eq (Get-PaneDockUninstallArguments $root)) {
            Remove-Item -LiteralPath $removeShortcutPath -Force
        }
    }
    Unregister-PaneDockInstallation $root
    foreach ($relative in $removalScripts) {
        if ($relative -in $present -and $relative -in $managed) {
            Remove-Item -LiteralPath (Join-Path $root $relative) -Force
        }
    }
    Remove-Item -LiteralPath (Join-Path $root $script:PaneDockManifestName) -Force
    # Remove only empty directories, leaving all untracked content alone.
    foreach ($directory in (Get-ChildItem -LiteralPath $root -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending)) {
        if (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0) {
            Remove-Item -LiteralPath $directory.FullName -Force
        }
    }
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) {
        Remove-Item -LiteralPath $root -Force
    }
    Write-Host 'PaneDock uninstalled. User settings in AppData and unmanaged files were preserved.'
}
finally {
    $maintenance.ReleaseMutex()
    $maintenance.Dispose()
}
