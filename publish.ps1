<#
  Build self-contained, single-file executables of StartDX - no .NET install needed on the target PC.

    .\publish.ps1                  # win-x64 -> .\publish\StartDX.Dock.exe + StartDX.Settings.exe
    .\publish.ps1 -Runtime win-arm64
    .\publish.ps1 -ReadyToRun      # pre-compiles to native code: faster startup, larger files (downloads the crossgen pack)

  Keep StartDX.Dock.exe and StartDX.Settings.exe in the same folder: the dock launches the settings app from beside itself.
  On first launch each exe unpacks its native WPF libraries to %TEMP%\.net\ (a one-time, per-version step).
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Output = (Join-Path $PSScriptRoot "publish"),
    [switch]$ReadyToRun
)

$userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path $userDotnet)) { $env:PATH = "$userDotnet;$env:PATH" }
$env:DOTNET_NOLOGO = "1"; $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$common = @(
    "-c", "Release", "-r", $Runtime, "--self-contained", "true", "-o", $Output,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",   # WPF's native DLLs cannot load from inside the bundle
    "-p:EnableCompressionInSingleFile=true",          # roughly halves the file size
    "-p:DebugType=none", "-p:DebugSymbols=false",
    "-p:PublishTrimmed=false"                         # WPF + ComImport interop are not trim-safe
)
if ($ReadyToRun) { $common += "-p:PublishReadyToRun=true" }

foreach ($proj in "StartDX.Dock", "StartDX.Settings") {
    Write-Host "== Publishing $proj ($Runtime)" -ForegroundColor Cyan
    dotnet publish (Join-Path $PSScriptRoot "src\$proj\$proj.csproj") @common
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish of $proj failed."; return }
}

Write-Host "`nDone:" -ForegroundColor Green
Get-ChildItem $Output -Filter *.exe | ForEach-Object { "  {0,-24} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
