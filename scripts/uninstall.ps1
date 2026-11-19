# Removes a tool installed by scripts\install.ps1 (user config files are kept).
param(
    [Parameter(Mandatory)] [string] $Name,
    [string] $Dest = (Join-Path $env:LOCALAPPDATA "Programs\$Name")
)
$ErrorActionPreference = 'Stop'

if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath) {
    $kept = ($userPath -split ';') | Where-Object { $_ -and $_.TrimEnd('\') -ne $Dest.TrimEnd('\') }
    [Environment]::SetEnvironmentVariable('Path', ($kept -join ';'), 'User')
}
Write-Host "==> Uninstalled $Name (user config files were kept)"
