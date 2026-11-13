# CornieKit Looper - Automated Build & Package Script
# This script builds the application and creates a release package with file association tools

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

Write-Host "CornieKit Looper Build & Package Script" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Green
Write-Host "Runtime: $Runtime" -ForegroundColor Green
Write-Host "Self-contained: $SelfContained" -ForegroundColor Green
Write-Host ""

# Define paths
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\CornieKit.Looper\CornieKit.Looper.csproj"
$publishDir = Join-Path $projectRoot "publish"
$zipFileName = "CornieKit_Looper_v${Version}_${Runtime}.zip"
$zipPath = Join-Path $projectRoot $zipFileName

# Check if project file exists
if (-not (Test-Path $projectFile)) {
    Write-Host "Error: Project file not found at $projectFile" -ForegroundColor Red
    exit 1
}

# Clean previous build
Write-Host "Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

# Build the project
Write-Host "Building project..." -ForegroundColor Yellow
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$publishArgs = @(
    "publish", $projectFile,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $publishDir,
    "/p:PublishSingleFile=false",
    "/p:PublishTrimmed=false"
)
if ($SelfContained) {
    # ReadyToRun crossgen is unnecessary for a portable zip and fails more often in CI.
    $publishArgs += "/p:PublishReadyToRun=false"
    if ($Runtime -eq "win-x64") {
        $publishArgs += "/p:VlcWindowsX86Enabled=false"
    }
}
dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "  ✓ Build completed" -ForegroundColor Green

# The player loads Effects\VideoAdjust.ps from the exe directory at startup.
$shaderSource = Join-Path $projectRoot "src\CornieKit.Looper\Effects\VideoAdjust.ps"
$shaderDestDir = Join-Path $publishDir "Effects"
if (-not (Test-Path $shaderSource)) {
    Write-Host "Error: pixel shader not found at $shaderSource" -ForegroundColor Red
    exit 1
}
New-Item -ItemType Directory -Force -Path $shaderDestDir | Out-Null
Copy-Item -Path $shaderSource -Destination (Join-Path $shaderDestDir "VideoAdjust.ps") -Force
Write-Host "  ✓ Copied VideoAdjust.ps" -ForegroundColor Green

$modelSource = Join-Path $projectRoot "src\CornieKit.Looper\Models\rife_v425lite.onnx"
if (Test-Path $modelSource) {
    $modelDestDir = Join-Path $publishDir "Models"
    New-Item -ItemType Directory -Force -Path $modelDestDir | Out-Null
    Copy-Item -Path $modelSource -Destination (Join-Path $modelDestDir "rife_v425lite.onnx") -Force
    Write-Host "  ✓ Copied rife_v425lite.onnx" -ForegroundColor Green
} else {
    Write-Host "  ! RIFE model missing. High frame rate loops will stay on the original frames." -ForegroundColor Yellow
}

# Copy file association tools
Write-Host "Copying file association tools..." -ForegroundColor Yellow

$filesToCopy = @(
    "RegisterFileAssociation.ps1",
    "UnregisterFileAssociation.ps1",
    "FILE_ASSOCIATION_GUIDE.md"
)

foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $projectRoot $file
    $destPath = Join-Path $publishDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destPath -Force
        Write-Host "  ✓ Copied $file" -ForegroundColor Green
    } else {
        Write-Host "  ! Warning: $file not found" -ForegroundColor Yellow
    }
}

# Create README.txt for the release
Write-Host "Creating README.txt..." -ForegroundColor Yellow
$readmeContent = @"
CornieKit Looper v$Version
==========================

Quick Start:
1. Run CornieKit.Looper.exe
2. Drag a video file to start playing

File Association (Optional):
1. Right-click 'RegisterFileAssociation.ps1'
2. Select 'Run with PowerShell' as Administrator
3. After registration, you can double-click video files to open them

See FILE_ASSOCIATION_GUIDE.md for detailed instructions.

Installation:
1. Download the zip
2. Extract it
3. Run CornieKit.Looper.exe

No installer.

System Requirements:
- 64-bit Windows 10/11
$(if ($SelfContained) {
"- The .NET runtime is included in this zip. You do not need to install .NET."
} else {
"- .NET 8.0 Desktop Runtime (https://dotnet.microsoft.com/download/dotnet/8.0)"
})

Support:
https://github.com/billye220670/CornieKit_Looper
"@

$readmePath = Join-Path $publishDir "README.txt"
Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8
Write-Host "  ✓ Created README.txt" -ForegroundColor Green

# Create ZIP archive. tar handles the large self-contained output more reliably
# than Compress-Archive.
Write-Host "Creating ZIP archive..." -ForegroundColor Yellow
if (Get-Command tar -ErrorAction SilentlyContinue) {
    & tar -a -c -f $zipPath -C $publishDir .
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ! tar failed to create ZIP archive" -ForegroundColor Red
        exit 1
    }
} else {
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
}

if (Test-Path $zipPath) {
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host "  ✓ Created $zipFileName ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
} else {
    Write-Host "  ! Failed to create ZIP archive" -ForegroundColor Red
    exit 1
}

# Summary
Write-Host ""
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Output directory: $publishDir" -ForegroundColor White
Write-Host "Release package: $zipPath" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test the application in: $publishDir" -ForegroundColor White
Write-Host "  2. Upload $zipFileName to GitHub releases" -ForegroundColor White
Write-Host ""
