<#
.SYNOPSIS
  Builds a self-contained release of aghouse: a publish folder, a portable zip and an installer.

.DESCRIPTION
  Output, in artifacts/:
    publish/                          the app folder (AgHouseApp.exe, aghouse.exe, runtime)
    aghouse-<ver>-win-x64.zip         portable build: unzip anywhere and run AgHouseApp.exe
    aghouse-<ver>-setup-x64.exe       installer (needs Inno Setup 6: winget install JRSoftware.InnoSetup)

  The version comes from Directory.Build.props.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File packaging/publish.ps1
  powershell -ExecutionPolicy Bypass -File packaging/publish.ps1 -NoInstaller
#>
param(
    [string]$Runtime = 'win-x64',
    [switch]$NoZip,
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
Write-Host "aghouse $version ($Runtime)" -ForegroundColor Cyan

# Start from an empty folder so files dropped in a newer version don't linger. Empty it rather
# than delete it: a shell or Explorer window sitting in the folder would block the delete.
if (Test-Path $publish) { Get-ChildItem $publish -Force | Remove-Item -Recurse -Force }

dotnet publish (Join-Path $root 'src\AgHouse.App\AgHouse.App.csproj') `
    -c Release -r $Runtime --self-contained true -o $publish `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

# NuGet packages can still carry their own symbols (e.g. Microsoft.Terminal.Control.pdb).
Get-ChildItem $publish -Recurse -Filter *.pdb | Remove-Item -Force

foreach ($exe in 'AgHouseApp.exe', 'aghouse.exe') {
    if (-not (Test-Path (Join-Path $publish $exe))) { throw "$exe is missing from the publish output." }
}
$size = (Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published to {0} ({1:N0} MB)" -f $publish, $size) -ForegroundColor Green

if (-not $NoZip) {
    $zip = Join-Path $artifacts "aghouse-$version-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("Zip:       {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
}

if (-not $NoInstaller) {
    if ($Runtime -ne 'win-x64') { throw 'The installer script targets x64 only; pass -NoInstaller for other runtimes.' }
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or pass -NoInstaller.'
    }
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'aghouse.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
    $setup = Join-Path $artifacts "aghouse-$version-setup-x64.exe"
    Write-Host ("Installer: {0} ({1:N0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
}
