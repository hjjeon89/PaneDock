#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
[xml] $project = Get-Content -LiteralPath (Join-Path $repo 'src\PaneDock.App\PaneDock.App.csproj') -Raw
$version = $project.Project.PropertyGroup.Version
$setup = Join-Path $repo "build\Installer\PaneDock-Setup-V$version.exe"
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).ProductVersion -ne $version) {
    throw 'Setup product version does not match the PaneDock project.'
}
$menu = @('invalid', '0') | & $setup
if ($LASTEXITCODE -ne 0 -or ($menu -join [Environment]::NewLine) -notmatch '(?s)1\. Install.*2\. Modify.*3\. Uninstall.*Enter 0, 1, 2 or 3') {
    throw 'Setup did not show all actions or recover from an invalid selection.'
}

Add-Type -AssemblyName System.IO.Compression
$assembly = [Reflection.Assembly]::LoadFile($setup)
$archive = [IO.Compression.ZipArchive]::new($assembly.GetManifestResourceStream('payload.zip'))
try {
    $notices = @{
        'LICENSE' = 'LICENSE'
        'THIRD-PARTY-NOTICES.txt' = 'THIRD-PARTY-NOTICES.txt'
        'Everything.LICENSE.txt' = 'src\PaneDock.App\Native\Everything.LICENSE.txt'
    }
    foreach ($name in $notices.Keys) {
        $entry = $archive.GetEntry($name)
        if (-not $entry) { throw "Missing setup notice: $name" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            if ($reader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $repo $notices[$name]))) {
                throw "Setup contains an outdated notice: $name"
            }
        }
        finally { $reader.Dispose() }
    }
    foreach ($name in @('PaneDock.exe', 'Everything64.dll')) {
        if (-not $archive.GetEntry($name)) { throw "Missing setup binary: $name" }
    }
}
finally { $archive.Dispose() }

Write-Host 'Setup version, menu and distribution notices passed.'
