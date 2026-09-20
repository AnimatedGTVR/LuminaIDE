# Run from an extracted Windows download. No SDK, compiler or administrator rights required.
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\LuminaIDE'
if (!(Test-Path (Join-Path $PSScriptRoot 'LuminaIDE.exe'))) { throw 'Extract the complete download first.' }
if ([IO.Path]::GetFullPath($PSScriptRoot) -eq [IO.Path]::GetFullPath($dest)) { throw 'Run from the extracted download, not the installed folder.' }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$PSScriptRoot\*" $dest -Recurse -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'LuminaIDE.lnk'))
$shortcut.TargetPath = Join-Path $dest 'LuminaIDE.exe'
$shortcut.WorkingDirectory = $dest
$shortcut.Save()
Write-Host "Installed to $dest. Open LuminaIDE from the Start menu."
