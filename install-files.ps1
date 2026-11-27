# Shared install/uninstall file inventory. Only a dedicated directory created
# by the installer is eligible for replacement. The user's profile is separate.
$script:PaneDockManifestName = '.panedock-install.json'
$script:PaneDockUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PaneDock'

function Enter-PaneDockMaintenance {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex = New-Object Threading.Mutex($false, "Global\PaneDock-Maintenance-$sid")
    try {
        if (-not $mutex.WaitOne(0)) { throw 'Another PaneDock setup or uninstall is running. Try again after it finishes.' }
    }
    catch [Threading.AbandonedMutexException] {
        # Windows transfers ownership to the next caller when setup exits unexpectedly.
    }
    catch {
        $mutex.Dispose()
        throw
    }
    return $mutex
}

function Get-PaneDockRegisteredRoot {
    (Get-ItemProperty -LiteralPath $script:PaneDockUninstallKey -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
}

function Register-PaneDockInstallation([string] $Root) {
    $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $exe = Join-Path $Root 'PaneDock.exe'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Root 'PaneDock.dll')).ProductVersion
    $values = @{
        DisplayVersion = $version
        DisplayIcon = '"' + $exe + '",0'
        InstallLocation = $Root
        UninstallString = '"' + $powerShell + '" ' + (Get-PaneDockUninstallArguments $Root) + ' -InstallDirectory "' + $Root + '"'
    }
    if (-not (Test-Path -LiteralPath $script:PaneDockUninstallKey)) {
        New-Item -Path $script:PaneDockUninstallKey -Force | Out-Null
    }
    foreach ($name in $values.Keys) {
        New-ItemProperty -LiteralPath $script:PaneDockUninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null
    }
    foreach ($name in @('NoModify', 'NoRepair')) {
        New-ItemProperty -LiteralPath $script:PaneDockUninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $script:PaneDockUninstallKey -Name DisplayName -Value PaneDock -PropertyType String -Force | Out-Null
}

function Unregister-PaneDockInstallation([string] $Root) {
    if ((Get-PaneDockRegisteredRoot) -eq $Root) {
        Remove-Item -LiteralPath $script:PaneDockUninstallKey
    }
}

function Get-PaneDockUninstallArguments([string] $Root) {
    return '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $Root 'uninstall.ps1') + '"'
}

function Test-PaneDockAppShortcut($Shortcut, [string] $Root) {
    $exe = Join-Path $Root 'PaneDock.exe'
    if ($Shortcut.TargetPath -eq $exe) { return $true }
    # Windows can resolve a missing shortcut target to a different profile path.
    return ($Shortcut.WorkingDirectory -eq $Root -and
        [IO.Path]::GetFileName($Shortcut.TargetPath) -eq 'PaneDock.exe' -and
        (Test-Path -LiteralPath (Join-Path $Root $script:PaneDockManifestName) -PathType Leaf) -and
        -not (Test-Path -LiteralPath $Shortcut.TargetPath -PathType Leaf))
}

function Get-PaneDockInstallFiles([string] $Root) {
    # Windows PowerShell 5.1 uses .NET Framework, not modern System.IO.Path.
    if ($Root -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\)' -or
        $Root.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\')) {
        throw "A dedicated subdirectory is required: $Root"
    }
    if (-not (Test-Path -LiteralPath $Root)) { return }
    $base = Get-Item -LiteralPath $Root -Force
    if (-not $base.PSIsContainer -or ($base.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Not a normal installation directory: $Root"
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in (Get-ChildItem -LiteralPath ($pending.Pop()) -Force)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Installation contains a link/reparse point: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { $entry.FullName.Substring($Root.Length + 1) }
        }
    }
}

function Read-PaneDockManifest([string] $Root) {
    $path = Join-Path $Root $script:PaneDockManifestName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No PaneDock installation manifest: $Root. Choose an empty directory or remove the old installation manually."
    }
    $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($data.Product -ne 'PaneDock' -or $data.Version -ne 1) {
        throw "Invalid PaneDock installation manifest: $path"
    }
    $files = @($data.Files)
    foreach ($file in $files) {
        if ($file -isnot [string] -or [string]::IsNullOrWhiteSpace($file) -or
            [IO.Path]::IsPathRooted($file) -or $file -match '(^|[\\/])\.\.?([\\/]|$)|:' -or
            $file -eq $script:PaneDockManifestName) {
            throw "Unsafe path in installation manifest: $file"
        }
    }
    if ('PaneDock.exe' -notin $files -or 'uninstall.ps1' -notin $files) {
        throw 'Incomplete PaneDock installation manifest.'
    }
    return $files
}

function Write-PaneDockManifest([string] $Root) {
    $files = @(Get-PaneDockInstallFiles $Root)
    @{ Product = 'PaneDock'; Version = 1; Files = $files } |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $Root $script:PaneDockManifestName) -Encoding UTF8
}

function Assert-PaneDockInstallTarget([string] $Root) {
    $files = @(Get-PaneDockInstallFiles $Root)
    if (-not (Test-Path -LiteralPath $Root)) { return }
    if ($files.Count -eq 0 -and @(Get-ChildItem -LiteralPath $Root -Force).Count -eq 0) { return }
    $managed = @(Read-PaneDockManifest $Root)
    $unexpected = @($files | Where-Object { $_ -ne $script:PaneDockManifestName -and $_ -notin $managed })
    if ($unexpected.Count -gt 0) {
        throw "Installation directory contains unmanaged files: $($unexpected[0]). Move them elsewhere before updating."
    }
}
