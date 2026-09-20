# Installs LuminaIDE for the current user on Windows.
#   .\install.ps1              build and install to %LOCALAPPDATA%\Programs\LuminaIDE + a Start Menu shortcut
#   .\install.ps1 -Uninstall   remove them again (settings in %APPDATA%\luminaide are kept)
# Run from a "Developer PowerShell for VS" so the MSVC compiler is available.
param([switch]$Uninstall)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$dest = Join-Path $env:LOCALAPPDATA "Programs\LuminaIDE"
$lnk  = Join-Path ([Environment]::GetFolderPath("Programs")) "LuminaIDE.lnk"

if ($Uninstall) {
    Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
    Remove-Item -Force $lnk -ErrorAction SilentlyContinue
    Write-Host "Removed LuminaIDE (your settings were kept)."
    return
}

Write-Host "==> Building"
& "$PSScriptRoot\build.ps1"

Write-Host "==> Installing to $dest"
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
dotnet publish app\LuminaIDE.csproj -c Release -o $dest --no-self-contained --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $dest "LuminaIDE.exe"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.Save()

# A `luminaide` command that opens detached, like `code`.
Set-Content -Path (Join-Path $dest "luminaide.cmd") -Value "@echo off`r`nstart `"`" `"%~dp0LuminaIDE.exe`" %*"

Write-Host ""
Write-Host "Installed. Find LuminaIDE in the Start menu, or run: $dest\luminaide.cmd ."
Write-Host "To type `luminaide` anywhere, add this folder to your PATH:  $dest"
