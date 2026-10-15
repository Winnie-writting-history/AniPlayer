param(
    [switch]$SkipProgramFilesSync,
    [switch]$NoPause
)

Stop-Process -Name "AniPlayer" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "AnniPlayer" -Force -ErrorAction SilentlyContinue

$dotnetCmd = "dotnet"
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
    $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
    $dotnetCmd = "$env:USERPROFILE\.dotnet\dotnet.exe"
}

& $dotnetCmd build .\AnniPlayer\AnniPlayer.csproj -c Release -o .\Demo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# Auto Sync to C:\Program Files\AniPlayer
if (-not $SkipProgramFilesSync) {
    $targetDir = "C:\Program Files\AniPlayer"
    try {
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force -ErrorAction SilentlyContinue | Out-Null
        }

        # Test write permission
        $testFile = Join-Path $targetDir ".perm_check"
        Set-Content -Path $testFile -Value "ok" -Force -ErrorAction Stop
        Remove-Item -Path $testFile -Force -ErrorAction SilentlyContinue

        # Copy executable and library files, excluding skins and test audio/video
        $demoFiles = Get-ChildItem -Path ".\Demo" -File | Where-Object {
            $ext = $_.Extension.ToLower()
            $ext -notin @(".mp4", ".mkv", ".avi", ".mov", ".mp3", ".wav", ".flac", ".pdb", ".log", ".tmp", ".zip")
        }

        foreach ($file in $demoFiles) {
            Copy-Item -Path $file.FullName -Destination (Join-Path $targetDir $file.Name) -Force
        }

        # Sync locales directory if present in Demo
        if (Test-Path ".\Demo\locales") {
            $destLocales = Join-Path $targetDir "locales"
            if (-not (Test-Path $destLocales)) { New-Item -ItemType Directory -Path $destLocales -Force | Out-Null }
            Get-ChildItem -Path ".\Demo\locales" -File | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination (Join-Path $destLocales $_.Name) -Force
            }
        }

        # Sync themes directory if present in Demo
        if (Test-Path ".\Demo\themes") {
            $destThemes = Join-Path $targetDir "themes"
            if (-not (Test-Path $destThemes)) { New-Item -ItemType Directory -Path $destThemes -Force | Out-Null }
            Get-ChildItem -Path ".\Demo\themes" -File | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination (Join-Path $destThemes $_.Name) -Force
            }
        }

        # Sync shaders directory if present in Demo
        if (Test-Path ".\Demo\shaders") {
            $destShaders = Join-Path $targetDir "shaders"
            if (-not (Test-Path $destShaders)) { New-Item -ItemType Directory -Path $destShaders -Force | Out-Null }
            Get-ChildItem -Path ".\Demo\shaders" -File | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination (Join-Path $destShaders $_.Name) -Force
            }
        }

        # Sync skin directory if present in Demo (Demo\skin is base source)
        if (Test-Path ".\Demo\skin") {
            $destSkin = Join-Path $targetDir "skin"
            if (-not (Test-Path $destSkin)) { New-Item -ItemType Directory -Path $destSkin -Force | Out-Null }
            Copy-Item -Path ".\Demo\skin\*" -Destination $destSkin -Recurse -Force
        }

        Write-Host "Sync success: copied runtime, themes, shaders, and skins to $targetDir." -ForegroundColor Green

        # Verify SHA-256 hashes between Demo and Program Files
        Write-Host ""
        Write-Host "🔍 [Hash Check] Verifying file consistency between Demo and Program Files:" -ForegroundColor Cyan
        $criticalFiles = @("AniPlayer.exe", "AniPlayer.dll", "autocrop.lua", "libmpv-2.dll")
        $allMatched = $true
        foreach ($cf in $criticalFiles) {
            $demoPath = Join-Path ".\Demo" $cf
            $progPath = Join-Path $targetDir $cf
            if ((Test-Path $demoPath) -and (Test-Path $progPath)) {
                $h1 = (Get-FileHash $demoPath -Algorithm SHA256).Hash
                $h2 = (Get-FileHash $progPath -Algorithm SHA256).Hash
                if ($h1 -eq $h2) {
                    Write-Host "  ✅ [MATCH] $cf (SHA-256: $($h1.Substring(0, 16))...)" -ForegroundColor Green
                } else {
                    $allMatched = $false
                    Write-Host "  ❌ [MISMATCH] $cf!" -ForegroundColor Red
                    Write-Host "      Demo:        $h1" -ForegroundColor Gray
                    Write-Host "      ProgramFiles:$h2" -ForegroundColor Gray
                }
            } elseif (Test-Path $demoPath) {
                Write-Host "  ⚠️ [MISSING IN TARGET] $cf" -ForegroundColor Yellow
            }
        }
        if ($allMatched) {
            Write-Host "✨ All critical binary files match 100%!" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "⚠️ [WARNING] Skipped sync to $targetDir (Write access denied due to Windows UAC)." -ForegroundColor Yellow
        Write-Host "👉 One-Time Fix: Double-click 'grant-programfiles-sync.bat' in the project root, OR run Administrator terminal:" -ForegroundColor Cyan
        Write-Host "   icacls `"$targetDir`" /grant `"Users:(OI)(CI)F`" /T" -ForegroundColor Gray
    }
}

# Pause on manual interactive execution
if (-not $NoPause -and [System.Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host "  全部执行完成！按任意键退出 (Press any key to exit)..." -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Cyan
    try {
        $null = [System.Console]::ReadKey($true)
    }
    catch {
        Read-Host "按回车退出"
    }
}
