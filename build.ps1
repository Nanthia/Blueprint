# Builds Shutter and the installer.  Usage:  .\build.ps1            (installer)
#                                           .\build.ps1 -SkipInstaller
param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = 'dotnet'
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }

Get-Process Shutter -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host '== Publishing self-contained single-file build' -ForegroundColor Cyan
& $dotnet publish "$root\Shutter\Shutter.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o "$root\dist-sc"
Remove-Item "$root\dist-sc\*.pdb" -ErrorAction SilentlyContinue

if (-not $SkipInstaller) {
    Write-Host '== Building installer' -ForegroundColor Cyan
    & $iscc "$root\installer\Shutter.iss"
    Write-Host "Installer: $root\installer\output" -ForegroundColor Green
}
