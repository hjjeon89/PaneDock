#Requires -Version 5.1
<#
.SYNOPSIS
Publishes PaneDock with the .NET and WPF runtimes into build\Release.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\build.ps1
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\build.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param()

& (Join-Path $PSScriptRoot 'install.ps1') -BuildOnly -InstallDirectory (Join-Path $PSScriptRoot 'build\Release') @PSBoundParameters
