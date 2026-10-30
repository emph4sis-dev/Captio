<#
.SYNOPSIS
  Publishes Captio and builds the per-user installer.

.DESCRIPTION
  1. dotnet publish: self-contained (no .NET install needed), win-x64, ReadyToRun for a faster start.
  2. Inno Setup compiles installer/Captio.iss into artifacts/installer/Captio-Setup-<version>.exe.

  Used both locally and by the release workflow. Requires the .NET 10 SDK and Inno Setup 6.

.PARAMETER Version
  Version to stamp on the app and the installer. Defaults to <Version> in Directory.Build.props.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 1.2.3, got '$Version'."
}

$publish = Join-Path $root 'artifacts\publish'
$output = Join-Path $root 'artifacts\installer'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publishing Captio $Version..."
dotnet publish (Join-Path $root 'src\Captio\Captio.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it with: winget install JRSoftware.InnoSetup' }

Write-Host "Building installer with $iscc..."
& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$output" (Join-Path $root 'installer\Captio.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Join-Path $output "Captio-Setup-$Version.exe"
Write-Host ("Done: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
