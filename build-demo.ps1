param(
    [switch]$SkipProgramFilesSync
)

Stop-Process -Name "AniPlayer" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "AnniPlayer" -Force -ErrorAction SilentlyContinue

dotnet build .\AnniPlayer\AnniPlayer.csproj -c Release -o .\Demo
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

        Write-Host "Sync success: copied runtime to $targetDir (excluded skins and media files)." -ForegroundColor Green
    }
    catch {
        Write-Host "Notice: Skipped sync to $targetDir (Requires Administrator permission)." -ForegroundColor Yellow
        Write-Host "Tip: Run Administrator terminal and execute:" -ForegroundColor Cyan
        Write-Host "  icacls `"$targetDir`" /grant `"Users:(OI)(CI)F`" /T" -ForegroundColor Gray
    }
}
