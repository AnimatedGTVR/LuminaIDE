# Native Windows build. Supports a normal PowerShell via Visual Studio discovery.
param(
    [Parameter(Position=0)][string]$Command = "build",
    [Parameter(ValueFromRemainingArguments=$true)][string[]]$AppArgs
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
function Step($Text) { Write-Host "`n$Text" -ForegroundColor Cyan }
function Invoke-Checked($Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed ($LASTEXITCODE). See the output above." }
}
try {
    if ($Command -in @("--help", "help", "-h")) {
        Write-Host 'Usage: .\build.ps1 [build|run [app args...]|test|package|--check|--gui]'
        exit 0
    }
    if ($Command -notin @("build", "run", "test", "package", "--check", "--gui")) { throw "Unknown command: $Command. Use --help." }
    Step 'LuminaIDE / Build studio'
    if ($Command -eq '--gui') {
        Invoke-Checked dotnet @('run', '--project', 'tools/Builder/Builder.csproj', '-c', 'Release')
        exit 0
    }
    # Find and import the compiler environment without asking users to find a special shell.
    if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path $vswhere) {
            $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if ($vs) {
                Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
                Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
            }
        }
    }
    $missing = @()
    foreach ($tool in @('dotnet', 'cargo', 'cmake', 'cl.exe')) {
        if (Get-Command $tool -ErrorAction SilentlyContinue) { Write-Host "  OK       $tool" }
        else { Write-Host "  MISSING  $tool" -ForegroundColor Yellow; $missing += $tool }
    }
    if ($missing.Count) { throw 'Install the missing tools using docs/BUILDING.md, then try again.' }
    if (-not ((& dotnet --list-sdks) -match '^8\.')) { throw 'The .NET 8 SDK is required. See docs/BUILDING.md.' }
    # Git Bash also ships a link.exe; always put the MSVC compiler directory first.
    $compilerDir = Split-Path (Get-Command cl.exe).Source
    $env:PATH = "$compilerDir;$env:PATH"
    $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER = Join-Path $compilerDir 'link.exe'
    if ($Command -eq '--check') { exit 0 }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $lib = Join-Path $PSScriptRoot 'build\lib'
    New-Item -ItemType Directory -Force $lib | Out-Null
    Step '[1/3] Rust engine'
    Invoke-Checked cargo @('build', '--release', '--locked', '--manifest-path', 'core/Cargo.toml')
    Copy-Item core\target\release\lumina_core.dll $lib -Force
    Step '[2/3] C++ terminal'
    $gen = @()
    if (!(Test-Path build/native/CMakeCache.txt) -and (Get-Command ninja -ErrorAction SilentlyContinue)) { $gen = @('-G', 'Ninja') }
    Invoke-Checked cmake (@('-S', 'native', '-B', 'build/native') + $gen + @('-DCMAKE_BUILD_TYPE=Release', "-DLUMINA_OUT=$lib"))
    Invoke-Checked cmake @('--build', 'build/native', '--config', 'Release', '--parallel')
    Step '[3/3] Desktop app'
    Invoke-Checked dotnet @('build', 'app/LuminaIDE.csproj', '-c', 'Release', '--nologo', '-v', 'minimal')
    if ($Command -eq 'test') {
        Step 'Verification'
        Invoke-Checked cargo @('test', '--locked', '--manifest-path', 'core/Cargo.toml')
        Invoke-Checked dotnet @('test', 'tests/LuminaIDE.Tests', '-c', 'Release', '--nologo', '-v', 'minimal')
        Invoke-Checked dotnet @('app/bin/Release/net8.0/LuminaIDE.dll', '--selftest')
    }
    if ($Command -eq 'package') {
        # The supported Windows package uses the x64 MSVC/Rust toolchain, including on ARM via emulation.
        $hostInfo = & rustc -vV
        if (-not ($hostInfo -match 'host: x86_64-pc-windows-msvc')) { throw 'Install the x86_64-pc-windows-msvc Rust toolchain for the Windows x64 package.' }
        $version = ([xml](Get-Content app/LuminaIDE.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
        $stage = Join-Path ([IO.Path]::GetTempPath()) ('lumina-package-' + [guid]::NewGuid())
        try {
            Step 'Packaging / win-x64'
            Invoke-Checked dotnet @('publish', 'app/LuminaIDE.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $stage, '--nologo', '-v', 'minimal')
            Invoke-Checked (Join-Path $stage 'LuminaIDE.exe') @('--selftest')
            Copy-Item packaging/windows/install-binary.ps1 (Join-Path $stage 'install.ps1')
            New-Item -ItemType Directory -Force dist | Out-Null
            $name = "LuminaIDE-v$version-win-x64.zip"
            $archive = Join-Path $PSScriptRoot "dist/$name"
            Compress-Archive -Path "$stage/*" -DestinationPath $archive -Force
            $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText("$archive.sha256", "$hash  $name`n")
            Write-Host "Package: $archive"
        } finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force } }
    }
    Step ("Finished in {0:n1}s" -f $watch.Elapsed.TotalSeconds)
    Write-Host 'App: app\bin\Release\net8.0\LuminaIDE.exe'
    if ($Command -eq 'run') { Invoke-Checked dotnet (@('app/bin/Release/net8.0/LuminaIDE.dll') + $AppArgs) }
} catch {
    Write-Host "`nBuild stopped: $_" -ForegroundColor Red
    exit 1
}
