# Installs a published tool build to %LOCALAPPDATA%\Programs\<Name> and adds it to the user PATH.
# Called by `make install` on Windows; can also be run directly:
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Name totp -Source dist\totp\win-x64
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $Source,
    [string] $Dest = (Join-Path $env:LOCALAPPDATA "Programs\$Name")
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source "$Name.exe"))) { throw "$Name.exe not found in '$Source'. Run 'make publish' first." }

if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
New-Item -ItemType Directory -Force $Dest | Out-Null
Get-ChildItem $Source -Exclude *.pdb | Copy-Item -Destination $Dest -Recurse -Force
Write-Host "==> Installed $Dest\$Name.exe"

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ((($userPath -split ';') | ForEach-Object { $_.TrimEnd('\') }) -notcontains $Dest.TrimEnd('\')) {
    $newPath = if ([string]::IsNullOrEmpty($userPath)) { $Dest } else { $userPath.TrimEnd(';') + ';' + $Dest }
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    Write-Host "==> Added $Dest to your user PATH. Open a new terminal to use '$Name'."
}
