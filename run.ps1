<#
  Build and launch StartDX.

    .\run.ps1                 # build (Debug) and start the dock
    .\run.ps1 -Settings       # also open the settings app
    .\run.ps1 -Release        # Release build
    .\run.ps1 -Stop           # ask a running dock to exit gracefully (restores the native taskbar)
    .\run.ps1 -RestoreTaskbar # emergency: kill any dock and bring Explorer's taskbar back (use if StartDX was killed)

  If `dotnet` is not on PATH, a per-user SDK in %LOCALAPPDATA%\Microsoft\dotnet is used automatically
  (and DOTNET_ROOT is set so the apphost exes can find the .NET 10 runtime).
#>
param([switch]$Release, [switch]$Settings, [switch]$Stop, [switch]$RestoreTaskbar)

$root = $PSScriptRoot
$userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path $userDotnet)) {
    $env:PATH = "$userDotnet;$env:PATH"
}
if (Test-Path (Join-Path $userDotnet "dotnet.exe")) { $env:DOTNET_ROOT = $userDotnet }

if ($Stop) { & "$root\tools\send-ipc.ps1" -Type command -Payload '{"name":"exit"}'; return }

$cfg = if ($Release) { "Release" } else { "Debug" }
if ($RestoreTaskbar) {
    $exe = Join-Path $root "artifacts\$cfg\StartDX.Dock.exe"
    if (-not (Test-Path $exe)) { $exe = Join-Path $root "artifacts\Debug\StartDX.Dock.exe" }
    & $exe --restore-taskbar
    return
}
dotnet build "$root\StartDX.slnx" -c $cfg -nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; return }

$out = Join-Path $root "artifacts\$cfg"
Start-Process (Join-Path $out "StartDX.Dock.exe")
if ($Settings) { Start-Process (Join-Path $out "StartDX.Settings.exe") }
