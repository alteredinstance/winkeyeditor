<#
.SYNOPSIS
    Builds WinKey Editor from source and packages it as a zip.

.DESCRIPTION
    Publishes WinKeyEditor.exe into .\publish\<runtime> and zips it into .\dist.
    By default the exe is self-contained (no .NET install needed to run it).
    Requires the .NET 8 SDK (or newer): https://dotnet.microsoft.com/download/dotnet/8.0

.EXAMPLE
    .\build.ps1                       # self-contained single exe + zip
.EXAMPLE
    .\build.ps1 -FrameworkDependent   # small exe that needs the .NET 8 Desktop Runtime
.EXAMPLE
    .\build.ps1 -Runtime win-arm64 -Run
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Produce a small exe that relies on an installed .NET 8 Desktop Runtime.
    [switch]$FrameworkDependent,

    # Skip creating the zip in .\dist.
    [switch]$NoZip,

    # Launch the built exe when done.
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'WinKeyEditor.csproj'

function Fail($message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

# --- Check for the .NET SDK ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "The .NET SDK was not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and re-run."
}
$sdkMajors = @(& dotnet --list-sdks | ForEach-Object { [int]($_ -split '\.')[0] })
if (-not ($sdkMajors | Where-Object { $_ -ge 8 })) {
    Fail "WinKey Editor needs the .NET 8 SDK or newer (found: $((& dotnet --list-sdks) -join ', ')). Get it from https://dotnet.microsoft.com/download/dotnet/8.0"
}

$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
if (-not $version) { $version = '0.0.0' }
$flavor = if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
Write-Host "Building WinKey Editor $version ($Configuration, $Runtime, $flavor)" -ForegroundColor Cyan

# --- Publish ---
$publishDir = Join-Path $root "publish\$Runtime"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $publishDir,
    '-p:PublishSingleFile=true',
    '-p:DebugType=none',
    '--nologo'
)
if ($FrameworkDependent) {
    $publishArgs += '--self-contained', 'false'
} else {
    $publishArgs += '--self-contained', 'true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true'
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Fail "Build failed. If the error says WinKeyEditor.exe is locked, exit WinKey Editor from its tray icon and try again."
}

Copy-Item (Join-Path $root 'README.md') $publishDir
$exe = Join-Path $publishDir 'WinKeyEditor.exe'
Write-Host "Built: $exe" -ForegroundColor Green

# --- Zip ---
if (-not $NoZip) {
    $distDir = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $distDir | Out-Null
    $suffix = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
    $zip = Join-Path $distDir "WinKeyEditor-$version-$Runtime$suffix.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -Force
    $sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Packaged: $zip ($sizeMb MB)" -ForegroundColor Green
}

if ($Run) {
    Start-Process $exe
}
