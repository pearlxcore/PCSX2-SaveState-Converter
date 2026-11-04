<#
.SYNOPSIS
    Builds a release: a folder and zip with the desktop app at the top level, the command-line
    tool in cli\, the converter in native\, plus catalog, licenses and a START HERE note; and a
    single-file desktop executable (.NET included, converter bundled), checked on its own in an
    empty folder.
.PARAMETER NativeDir
    Folder with pcsx2-stateconvert.exe (Release build, see experimental/BUILD.md), zstd.dll and z.dll.
.PARAMETER SelfContained
    Publish with the .NET runtime included (no .NET 10 Desktop Runtime needed on the target PC).
#>
param(
    [Parameter(Mandatory = $true)] [string] $NativeDir,
    [string] $Output = (Join-Path $PSScriptRoot '..\dist'),
    [string] $Version = '0.1.0',
    [switch] $SelfContained
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$name = "PCSX2-SaveState-Converter-$Version-win-x64"
$stage = Join-Path ([IO.Path]::GetFullPath($Output)) $name
if (Test-Path $stage) { throw "Release folder already exists: $stage" }
$appName = 'PCSX2 SaveState Converter.exe'
$singleExe = Join-Path ([IO.Path]::GetFullPath($Output)) "$name.exe"
if (Test-Path $singleExe) { throw "Release executable already exists: $singleExe" }

$native = (Resolve-Path $NativeDir).Path
$nativeFiles = @('pcsx2-stateconvert.exe', 'zstd.dll', 'z.dll')
foreach ($file in $nativeFiles) {
    if (-not (Test-Path (Join-Path $native $file))) { throw "Missing $file in $native" }
}

$publish = @('-c', 'Release', '-r', 'win-x64', "-p:Version=$Version", '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }))
# Build in a private folder so a running copy of a local build (which locks bin\) cannot break packaging.
$build = Join-Path ([IO.Path]::GetTempPath()) "saveconvert-package-$Version-$([guid]::NewGuid().ToString('N'))"
try {
    dotnet publish (Join-Path $root 'SaveStateConverter.Gui\SaveStateConverter.Gui.csproj') @publish "-p:OutDir=$build\gui\" -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }
    $cli = Join-Path $stage 'cli'
    dotnet publish (Join-Path $root 'SaveStateConverter.Cli\SaveStateConverter.Cli.csproj') @publish "-p:OutDir=$build\cli\" -o $cli
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }

    # Single-file executable: .NET included, the converter bundled as native\* (the .NET host extracts it
    # on start, see NativeConverter.Locate), catalog files embedded in SaveStateConverter.Core.
    $single = @('-c', 'Release', '-r', 'win-x64', "-p:Version=$Version", '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true', "-p:BundleNativeDir=$native")
    dotnet publish (Join-Path $root 'SaveStateConverter.Gui\SaveStateConverter.Gui.csproj') @single "-p:OutDir=$build\gui-single\" -o "$build\single"
    if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }
    $runtimeConfig = Get-Content -Raw -LiteralPath "$build\gui-single\PCSX2 SaveState Converter.runtimeconfig.json" | ConvertFrom-Json
    if (-not $runtimeConfig.runtimeOptions.includedFrameworks) { throw 'The single-file build is not self-contained.' }
    Copy-Item -LiteralPath (Join-Path "$build\single" $appName) -Destination $singleExe
}
finally {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
}

$nativeStage = New-Item -ItemType Directory -Path (Join-Path $stage 'native')
foreach ($file in $nativeFiles) { Copy-Item (Join-Path $native $file) $nativeStage }

# Smoke checks: the app is at the top, the converter runs, and the CLI (in cli\) finds it one folder up.
if (-not (Test-Path (Join-Path $stage $appName))) { throw "$appName is missing from the release folder." }
$env:SAVECONVERT_NATIVE = $null
& (Join-Path $nativeStage 'pcsx2-stateconvert.exe') formats | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The bundled converter did not run.' }
$formats = & (Join-Path $cli 'Saveconvert.exe') formats
if ($LASTEXITCODE -ne 0) { throw 'Published CLI did not run.' }
if (-not ($formats -match [regex]::Escape((Join-Path $nativeStage 'pcsx2-stateconvert.exe')))) { throw 'The published CLI does not find the bundled converter.' }

# The single-file executable, alone in an empty folder, must load its catalog and run its bundled
# converter (App.SelfCheck). The .NET host extracts into a private folder here.
$check = Join-Path ([IO.Path]::GetTempPath()) "saveconvert-check-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $check | Out-Null
try {
    Copy-Item -LiteralPath $singleExe -Destination $check
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $check 'extract'
    $process = Start-Process -FilePath (Join-Path $check "$name.exe") -ArgumentList '--self-check' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "The single-file executable failed its self-check (exit $($process.ExitCode): 1 catalog or titles, 2 converter missing, 3 converter did not run)."
    }
}
finally {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $null
    Remove-Item -LiteralPath $check -Recurse -Force -ErrorAction SilentlyContinue
}

Set-Content -Path (Join-Path $stage 'START HERE.txt') -Encoding utf8 -Value @"
PCSX2 SaveState Converter $Version

1. Extract this zip first (right-click the zip > Extract All...). The app cannot run from inside the zip.
2. In the extracted folder, open "$appName".

Requirements: Windows 10 or 11 (64-bit) and the .NET 10 Desktop Runtime
(https://dotnet.microsoft.com/download/dotnet/10.0). If it is missing, Windows offers to download it
when you open the app.

The "cli" folder holds the command-line version, for use in a terminal (see README.md).
"@

Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md'), (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'licenses') $stage -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage 'source') | Out-Null
Copy-Item (Join-Path $root 'experimental\*.patch'), (Join-Path $root 'experimental\BUILD.md') (Join-Path $stage 'source')
Get-ChildItem $stage -Filter '*.pdb' -Recurse | Remove-Item

$zip = "$stage.zip"
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"Release: $zip"
"SHA-256: $hash"
"Single-file executable: $singleExe"
"SHA-256: $((Get-FileHash $singleExe -Algorithm SHA256).Hash)"
