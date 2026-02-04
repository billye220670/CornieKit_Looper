# CornieKit Looper - Windows File Association Unregistration Script
# Run as Administrator: Right-click -> "Run with PowerShell"

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Error: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click this file and select 'Run with PowerShell' as Administrator" -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "CornieKit Looper File Association Removal" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Define registry entries
$progId = "CornieKit.Looper.VideoFile"
$videoExtensions = @('.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.mpg', '.mpeg')

try {
    Write-Host "Removing file associations..." -ForegroundColor Yellow

    # Remove ProgID
    $progIdKey = "Registry::HKEY_CLASSES_ROOT\$progId"
    if (Test-Path $progIdKey) {
        Remove-Item -Path $progIdKey -Recurse -Force
        Write-Host "  ✓ ProgID removed" -ForegroundColor Green
    }

    # Remove application registration
    $appKey = "Registry::HKEY_CURRENT_USER\Software\Classes\Applications\CornieKit.Looper.exe"
    if (Test-Path $appKey) {
        Remove-Item -Path $appKey -Recurse -Force
        Write-Host "  ✓ Application registration removed" -ForegroundColor Green
    }

    # Remove associations from extensions
    Write-Host "Removing extension associations..." -ForegroundColor Yellow
    foreach ($ext in $videoExtensions) {
        $extKey = "Registry::HKEY_CURRENT_USER\Software\Classes\$ext\OpenWithProgids"
        if (Test-Path $extKey) {
            $value = Get-ItemProperty -Path $extKey -Name $progId -ErrorAction SilentlyContinue
            if ($value) {
                Remove-ItemProperty -Path $extKey -Name $progId -ErrorAction SilentlyContinue
                Write-Host "  ✓ Removed $ext association" -ForegroundColor Green
            }
        }
    }

    Write-Host ""
    Write-Host "File associations removed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Note: You may need to log out and back in for changes to take full effect" -ForegroundColor Yellow

} catch {
    Write-Host ""
    Write-Host "Error during removal: $_" -ForegroundColor Red
    pause
    exit 1
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
