@echo off
setlocal
cd /d "%~dp0"

:: Check for Administrator permissions
net session >nul 2>&1
if errorlevel 1 (
    echo ============================================================
    echo   AniPlayer - Program Files One-Time Permission Setup
    echo ============================================================
    echo.
    echo [INFO] Requesting Administrator permission...
    echo [INFO] Please click YES / Allow on the Windows UAC prompt.
    echo.
    powershell -NoProfile -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Elevated section: Run as Administrator
title AniPlayer - Program Files Sync Tool
echo ============================================================
echo   AniPlayer - Program Files One-Time Permission Setup
echo ============================================================
echo.
echo [1/2] Granting Users Full Control permissions to C:\Program Files\AniPlayer...

if not exist "C:\Program Files\AniPlayer" (
    mkdir "C:\Program Files\AniPlayer"
)

icacls "C:\Program Files\AniPlayer" /grant Users:(OI)(CI)F /T

echo.
echo [2/2] Running build-demo.ps1 to sync all files...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-demo.ps1"

echo.
echo ============================================================
echo   [SUCCESS] All files synced to C:\Program Files\AniPlayer!
echo   Future runs of build-demo.ps1 will sync automatically.
echo ============================================================
echo.
pause
