# Shared install/uninstall file inventory. Only a dedicated directory created
# by the installer is eligible for replacement. The user's profile is separate.
$script:PaneDockManifestName = '.panedock-install.json'

function Get-PaneDockUninstallArguments([string] $Root) {
    return '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $Root 'uninstall.ps1') + '"'
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
