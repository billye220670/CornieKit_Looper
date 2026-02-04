# CornieKit Looper - Windows File Association Registration Script
# Run as Administrator: Right-click -> "Run with PowerShell"

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Error: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click this file and select 'Run with PowerShell' as Administrator" -ForegroundColor Yellow
    pause
    exit 1
}

# Get the executable path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir "CornieKit.Looper.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Error: Cannot find CornieKit.Looper.exe in $scriptDir" -ForegroundColor Red
    Write-Host "Please run this script from the application folder" -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "CornieKit Looper File Association Registration" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Executable: $exePath" -ForegroundColor Green
Write-Host ""

# Define supported video extensions
$videoExtensions = @('.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.mpg', '.mpeg')

# Define registry entries
$progId = "CornieKit.Looper.VideoFile"
$appName = "CornieKit Looper"

try {
    # Register ProgID
    Write-Host "Registering application..." -ForegroundColor Yellow

    # Create ProgID key
    $progIdKey = "Registry::HKEY_CLASSES_ROOT\$progId"
    New-Item -Path $progIdKey -Force | Out-Null
    Set-ItemProperty -Path $progIdKey -Name "(Default)" -Value "$appName Video File"

    # Create DefaultIcon
    $iconKey = "$progIdKey\DefaultIcon"
    New-Item -Path $iconKey -Force | Out-Null
    Set-ItemProperty -Path $iconKey -Name "(Default)" -Value "`"$exePath`",0"

    # Create shell\open\command
    $commandKey = "$progIdKey\shell\open\command"
    New-Item -Path $commandKey -Force | Out-Null
    Set-ItemProperty -Path $commandKey -Name "(Default)" -Value "`"$exePath`" `"%1`""

    Write-Host "  ✓ ProgID registered" -ForegroundColor Green

    # Register application in the "Open With" list
    Write-Host "Registering 'Open With' entries..." -ForegroundColor Yellow

    $appKey = "Registry::HKEY_CURRENT_USER\Software\Classes\Applications\CornieKit.Looper.exe"
    New-Item -Path $appKey -Force | Out-Null

    $appCommandKey = "$appKey\shell\open\command"
    New-Item -Path $appCommandKey -Force | Out-Null
    Set-ItemProperty -Path $appCommandKey -Name "(Default)" -Value "`"$exePath`" `"%1`""

    # Set FriendlyAppName
    New-Item -Path "$appKey\shell\open" -Force | Out-Null
    Set-ItemProperty -Path "$appKey\shell\open" -Name "FriendlyAppName" -Value $appName

    Write-Host "  ✓ 'Open With' entries created" -ForegroundColor Green

    # Associate with video extensions
    Write-Host "Associating video file types..." -ForegroundColor Yellow

    foreach ($ext in $videoExtensions) {
        # Add to OpenWithProgids
        $extKey = "Registry::HKEY_CURRENT_USER\Software\Classes\$ext\OpenWithProgids"
        New-Item -Path $extKey -Force | Out-Null
        Set-ItemProperty -Path $extKey -Name $progId -Value ([byte[]]@()) -Type Binary

        Write-Host "  ✓ Associated $ext" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Registration completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now:" -ForegroundColor Cyan
    Write-Host "  1. Right-click any video file" -ForegroundColor White
    Write-Host "  2. Select 'Open with' -> '$appName'" -ForegroundColor White
    Write-Host "  3. Or set as default program in Windows Settings" -ForegroundColor White
    Write-Host ""
    Write-Host "Note: You may need to log out and back in for changes to take full effect" -ForegroundColor Yellow

} catch {
    Write-Host ""
    Write-Host "Error during registration: $_" -ForegroundColor Red
    pause
    exit 1
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
