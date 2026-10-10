param(
    [switch]$SkipProgramFilesSync
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
    }
    catch {
        Write-Host "Notice: Skipped sync to $targetDir (Requires Administrator permission)." -ForegroundColor Yellow
        Write-Host "Tip: Run Administrator terminal and execute:" -ForegroundColor Cyan
        Write-Host "  icacls `"$targetDir`" /grant `"Users:(OI)(CI)F`" /T" -ForegroundColor Gray
    }
}
