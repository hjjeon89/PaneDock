#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'build\Installer'
$stage = Join-Path $output ('.setup-' + [guid]::NewGuid().ToString('N'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not [IO.File]::Exists($compiler)) { throw "Windows C# compiler is missing: $compiler" }

try {
    $payload = Join-Path $stage 'payload'
    & (Join-Path $repo 'install.ps1') -BuildOnly -InstallDirectory $payload
    $appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'PaneDock.dll'))
    $version = $appVersion.ProductVersion
    $setupName = "PaneDock-Setup-V$version.exe"
    @"
using System.Reflection;
[assembly: AssemblyVersion("$($appVersion.FileVersion)")]
[assembly: AssemblyFileVersion("$($appVersion.FileVersion)")]
[assembly: AssemblyInformationalVersion("$version")]
"@ | Set-Content -LiteralPath (Join-Path $stage 'SetupVersion.cs') -Encoding ASCII
    foreach ($notice in @('LICENSE', 'THIRD-PARTY-NOTICES.txt', 'Everything.LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $notice) -PathType Leaf)) {
            throw "Publish is missing the distribution notice: $notice"
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $stage 'payload.zip'))
    $scripts = @('install.ps1', 'install-files.ps1', 'uninstall.ps1')
    foreach ($script in $scripts) {
        Copy-Item -LiteralPath (Join-Path $repo $script) -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup.ps1') -Destination $stage

    $resources = @('payload.zip', 'setup.ps1') + $scripts | ForEach-Object { "/resource:$_" }
    $icon = Join-Path $repo 'src\PaneDock.App\Assets\PaneDock.ico'
    Push-Location -LiteralPath $stage
    try {
        & $compiler /nologo /platform:x64 /target:exe "/out:$setupName" "/win32icon:$icon" @resources (Join-Path $PSScriptRoot 'Setup.cs') SetupVersion.cs
        if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed (exit code $LASTEXITCODE)." }
    }
    finally { Pop-Location }

    $setup = Join-Path $output $setupName
    $compiled = Join-Path $stage $setupName
    if ([IO.File]::Exists($setup)) { [IO.File]::Replace($compiled, $setup, (Join-Path $stage 'previous.exe')) }
    else { [IO.File]::Move($compiled, $setup) }
    Write-Host "Setup: $setup"
}
finally {
    if (Test-Path -LiteralPath $stage -PathType Container) {
        $resolved = (Resolve-Path -LiteralPath $stage).ProviderPath
        if ($resolved -ne $stage -or (Split-Path -Parent $resolved) -ne $output) {
            throw "Unexpected installer staging path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
