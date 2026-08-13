# Builds desk-rotate (installing the .NET 8 SDK locally first if needed), creates a
# DeskRotate.lnk shortcut next to this repo pointing at the built exe, and launches it.
# Kept ASCII-only on purpose: this file's encoding must match whatever code page the
# calling cmd.exe / powershell.exe happens to default to, and ASCII is the only text
# that reads correctly under every code page without a BOM.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnetLocal = Join-Path $root ".dotnet"
$env:PATH = "$dotnetLocal;$env:PATH"

function Test-DotnetAvailable {
    return [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
}

if (-not (Test-DotnetAvailable)) {
    Write-Host ".NET 8 SDK was not found on this machine."
    $answer = Read-Host "Download and install .NET 8 SDK into this folder only (no admin rights needed)? [y/N]"
    if ($answer -notmatch '^[Yy]$') {
        Write-Host "Skipping install. Get .NET 8 SDK from https://dotnet.microsoft.com/download and run this again."
        exit 1
    }

    Write-Host "Downloading .NET 8 SDK..."
    try {
        $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
        & $installScript -Channel 8.0 -InstallDir $dotnetLocal
    }
    catch {
        Write-Host "Automatic install failed: $_"
        Write-Host "Get .NET 8 SDK from https://dotnet.microsoft.com/download and run this again."
        exit 1
    }

    if (-not (Test-DotnetAvailable)) {
        Write-Host "Install finished but dotnet still cannot be found. Open a new window and run this again."
        exit 1
    }
}

Write-Host "Building desk-rotate..."
& dotnet build -c Release -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed."
    exit 1
}

$exeDir = Join-Path $root "src\DeskRotate\bin\Release\net8.0-windows10.0.19041.0"
$exePath = Join-Path $exeDir "DeskRotate.exe"

Write-Host "Creating DeskRotate.lnk..."
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $root "DeskRotate.lnk"))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $exeDir
$shortcut.IconLocation = $exePath
$shortcut.Save()

Write-Host "Done. From now on, just double-click DeskRotate.lnk to run it directly."
Start-Process -FilePath $exePath
