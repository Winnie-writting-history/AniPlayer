param(
    [string]$Version = "v1.0.0",
    [switch]$NoPause = $false
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Starting release packaging for AniPlayer ($Version)..." -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$distDir = ".\dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$excludedSkins = @("blue_marble", "sapphire", "teal_gold")
$mediaExtensions = @("*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.ts", "*.m2ts", "*.mp3", "*.flac", "*.wav", "*.aac", "*.ogg", "*.opus", "*.m4a")

# =========================================================================
# 1. Build and pack Edition 1: Win11 Framework-Dependent (Demo\AniPlayer.zip)
#    Internal layout: AniPlayer/...
# =========================================================================
Write-Host ""
Write-Host "Packaging [1/2]: Win11 Framework-Dependent (Demo\AniPlayer.zip)..." -ForegroundColor Yellow

& .\build-demo.ps1

$demoDir = ".\Demo"
$demoPackRoot = "$distDir\demo_pack_temp"
$demoPackSubdir = "$demoPackRoot\AniPlayer"

if (Test-Path $demoPackRoot) { Remove-Item -Path $demoPackRoot -Recurse -Force }
New-Item -ItemType Directory -Path $demoPackSubdir -Force | Out-Null

# Copy essential executable & runtime binaries
Copy-Item -Path "$demoDir\AniPlayer.exe" -Destination $demoPackSubdir -Force
Copy-Item -Path "$demoDir\AniPlayer.dll" -Destination $demoPackSubdir -Force
Copy-Item -Path "$demoDir\AniPlayer.deps.json" -Destination $demoPackSubdir -Force
Copy-Item -Path "$demoDir\AniPlayer.runtimeconfig.json" -Destination $demoPackSubdir -Force
Copy-Item -Path "$demoDir\libmpv-2.dll" -Destination $demoPackSubdir -Force
if (Test-Path "$demoDir\autocrop.lua") {
    Copy-Item -Path "$demoDir\autocrop.lua" -Destination $demoPackSubdir -Force
}
if (Test-Path "$demoDir\locales") {
    Copy-Item -Path "$demoDir\locales" -Destination "$demoPackSubdir\locales" -Recurse -Force
}
if (Test-Path "$demoDir\shaders") {
    Copy-Item -Path "$demoDir\shaders" -Destination "$demoPackSubdir\shaders" -Recurse -Force
}
if (Test-Path "$demoDir\skin") {
    Copy-Item -Path "$demoDir\skin" -Destination "$demoPackSubdir\skin" -Recurse -Force
    foreach ($sk in $excludedSkins) {
        $skPath = "$demoPackSubdir\skin\$sk"
        if (Test-Path $skPath) {
            Remove-Item -Path $skPath -Recurse -Force
            Write-Host "  [Excluded skin]: $sk" -ForegroundColor DarkGray
        }
    }
}

# Clean any test media or debug files
foreach ($ext in $mediaExtensions) {
    Get-ChildItem -Path $demoPackSubdir -Filter $ext -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
}
Get-ChildItem -Path $demoPackSubdir -Filter "*.pdb" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $demoPackSubdir -Filter "*.log" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $demoPackSubdir -Filter "*.zip" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force

# Clean legacy / redundant alias zip files
if (Test-Path "$demoDir\AniPlayer.zip") { Remove-Item -Path "$demoDir\AniPlayer.zip" -Force -ErrorAction SilentlyContinue }
if (Test-Path ".\Release_self_Self-Contained.zip") { Remove-Item -Path ".\Release_self_Self-Contained.zip" -Force -ErrorAction SilentlyContinue }
if (Test-Path "$distDir\AniPlayer.zip") { Remove-Item -Path "$distDir\AniPlayer.zip" -Force -ErrorAction SilentlyContinue }
if (Test-Path "$distDir\Release_self_Self-Contained.zip") { Remove-Item -Path "$distDir\Release_self_Self-Contained.zip" -Force -ErrorAction SilentlyContinue }

$demoZipTarget = "$distDir\AniPlayer-$Version-Win11-FrameworkDependent.zip"

if (Test-Path $demoZipTarget) { Remove-Item -Path $demoZipTarget -Force }

# Compress with AniPlayer as root folder inside the zip
Compress-Archive -Path "$demoPackSubdir" -DestinationPath $demoZipTarget -CompressionLevel Optimal
Remove-Item -Path $demoPackRoot -Recurse -Force

$hashDemo = (Get-FileHash -Path $demoZipTarget -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Win11 Framework-Dependent Package Created: $demoZipTarget" -ForegroundColor Green
Write-Host "SHA-256: $hashDemo" -ForegroundColor Gray


# =========================================================================
# 2. Build and pack Edition 2: Self-Contained Standalone (dist\AniPlayer-*-Self-Contained.zip)
#    Internal layout: AniPlayer/...
# =========================================================================
Write-Host ""
Write-Host "Packaging [2/2]: Self-Contained Standalone (dist\AniPlayer-$Version-Windows-x64-Self-Contained.zip)..." -ForegroundColor Yellow

& .\publish-release.ps1

$publishDir = ".\Release_Publish"
$selfPackRoot = "$distDir\self_pack_temp"
$selfPackSubdir = "$selfPackRoot\AniPlayer"
if (Test-Path $selfPackRoot) { Remove-Item -Path $selfPackRoot -Recurse -Force }
New-Item -ItemType Directory -Path $selfPackSubdir -Force | Out-Null

Copy-Item -Path "$publishDir\*" -Destination $selfPackSubdir -Recurse -Force

# Exclude skins in self-contained package
foreach ($sk in $excludedSkins) {
    $skPath = "$selfPackSubdir\skin\$sk"
    if (Test-Path $skPath) {
        Remove-Item -Path $skPath -Recurse -Force
        Write-Host "  [Excluded skin]: $sk" -ForegroundColor DarkGray
    }
}

# Clean any test media or debug files
foreach ($ext in $mediaExtensions) {
    Get-ChildItem -Path $selfPackSubdir -Filter $ext -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
}
Get-ChildItem -Path $selfPackSubdir -Filter "*.pdb" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $selfPackSubdir -Filter "*.log" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $selfPackSubdir -Filter "*.zip" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force

$selfZipTarget = "$distDir\AniPlayer-$Version-Windows-x64-Self-Contained.zip"

if (Test-Path $selfZipTarget) { Remove-Item -Path $selfZipTarget -Force }

# Compress with AniPlayer as root folder inside the zip
Compress-Archive -Path "$selfPackSubdir" -DestinationPath $selfZipTarget -CompressionLevel Optimal
Remove-Item -Path $selfPackRoot -Recurse -Force

$hashSelf = (Get-FileHash -Path $selfZipTarget -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Self-Contained Standalone Package Created: $selfZipTarget" -ForegroundColor Green
Write-Host "SHA-256: $hashSelf" -ForegroundColor Gray


# =========================================================================
# 3. Write SHA256 Checksums
# =========================================================================
$hashLines = @(
    "$hashDemo  AniPlayer-$Version-Win11-FrameworkDependent.zip (Win11 Framework-Dependent)",
    "$hashSelf  AniPlayer-$Version-Windows-x64-Self-Contained.zip (Self-Contained Standalone)"
)
Set-Content -Path "$distDir\SHA256SUMS.txt" -Value $hashLines -Encoding UTF8

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "All Release Packages Ready in .\dist\ !" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "1. Win11 Framework-Dependent Edition:" -ForegroundColor Cyan
Write-Host "   Path   : $demoZipTarget" -ForegroundColor White
Write-Host "   SHA-256: $hashDemo" -ForegroundColor Yellow
Write-Host ""
Write-Host "2. Self-Contained Standalone Edition:" -ForegroundColor Cyan
Write-Host "   Path   : $selfZipTarget" -ForegroundColor White
Write-Host "   SHA-256: $hashSelf" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Green

if (-not $NoPause) {
    Write-Host ""
    if ($Host.Name -eq "ConsoleHost") {
        Write-Host "✨ 打包完成，请按任意键退出..." -ForegroundColor Gray
        try {
            $null = [Console]::ReadKey($true)
        } catch {
            Read-Host "✨ 打包完成，请按回车键退出..."
        }
    } else {
        Read-Host "✨ 打包完成，请按回车键退出..."
    }
}
